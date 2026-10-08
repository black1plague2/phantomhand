import 'dart:async';
import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/core/hub/live_message.dart';
import 'package:opus_app/core/hub/udp_beacon.dart';
import 'package:opus_app/shared/widgets/dynamic_form/field_spec.dart';
import 'package:path_provider/path_provider.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'hub_providers.g.dart';

/// `true` on Windows/Android desktop-capable builds (`dart:io` available),
/// `false` on web -- the web build is a viewer only (Phase 3 cloud relay),
/// never a hub (`docs/agent-briefs/A-flutter-app.md` M3).
bool get hubCapable => !kIsWeb;

/// Read-only snapshot of one headset (connected or recently-disconnected)
/// for the Devices screen. `connected: false` rows render the
/// `docs/APP_DESIGN.md` disconnected copy instead of vanishing, so a
/// clinician mid-session can see a headset dropped and that it will
/// resync rather than assuming data was lost.
class HeadsetInfo {
  const new({
    required this.deviceId,
    required this.rttMs,
    required this.status,
    this.connected = true,
    this.disconnectedAt,
  });
  final String deviceId;
  final int? rttMs;
  final Map<String, dynamic>? status;
  final bool connected;
  final DateTime? disconnectedAt;
}

/// Owns the (optional) `HubServer` + `UdpBeacon` lifecycle behind a settings
/// toggle (brief M3: "Repository switch: Mock ↔ Hub data sources (settings
/// toggle)"). Nothing starts a server automatically -- the clinician turns
/// hub mode on from Settings/Devices.
@Riverpod(keepAlive: true)
class HubController extends _$HubController {
  HubServer? _server;
  UdpBeacon? _beacon;
  StreamSubscription<List<HeadsetSnapshot>>? _sub;
  String? _sessionsDir;

  @override
  HubControllerState build() {
    ref.onDispose(() {
      unawaited(_server?.stop());
      unawaited(_beacon?.stop());
      _sub?.cancel();
    });
    return const HubControllerState.stopped();
  }

  /// The directory the hub writes uploaded session files to (`session.json`,
  /// `events.ndjson`, `kin_###.json`, `metrics.json` per session-id
  /// subfolder), once the hub has been started. `null` while stopped.
  String? get sessionsDir => _sessionsDir;

  Future<void> start({int port = 8787, int beaconPort = 8788}) async {
    if (!hubCapable || state.running) return;
    final dir = await getApplicationSupportDirectory();
    _sessionsDir = '${dir.path}/sessions';
    final server = HubServer(sessionsDir: _sessionsDir!, port: port);
    await server.start();
    final beacon = UdpBeacon(hubId: server.hubId, port: port, name: 'OPUS Hub', beaconPort: beaconPort);
    await beacon.start();
    _server = server;
    _beacon = beacon;
    _sub = server.knownHeadsetsStream.listen((_) => _emit(server));
    state = HubControllerState.running(hubId: server.hubId, port: port, headsets: const []);
  }

  Future<void> stop() async {
    await _sub?.cancel();
    await _server?.stop();
    await _beacon?.stop();
    _server = null;
    _beacon = null;
    state = const HubControllerState.stopped();
  }

  void _emit(HubServer server) {
    final headsets = server.knownHeadsets
        .map((h) => HeadsetInfo(
              deviceId: h.deviceId,
              rttMs: h.rtt?.inMilliseconds,
              status: h.status,
              connected: h.connected,
              disconnectedAt: h.disconnectedAt,
            ))
        .toList();
    state = HubControllerState.running(hubId: server.hubId, port: server.port, headsets: headsets);
  }

  /// The live [HubConnection] for [deviceId], or `null` if not connected.
  HubConnection? connectionFor(String deviceId) =>
      _server?.connections.where((c) => c.deviceId == deviceId).firstOrNull;

  /// The live [HubConnection] currently running [sessionId] (per its own
  /// `session_started`/`session_ended` tracking), or `null` if no connected
  /// headset is running that session -- used by the live monitor to bind to
  /// the real stream instead of the mock one (brief B3).
  HubConnection? connectionForSession(String sessionId) =>
      _server?.connections.where((c) => c.activeSessionId == sessionId).firstOrNull;

  /// Sends a `command` to one connected headset (start/pause/resume/stop/
  /// recenter/skip_block/adjust_params/show_message per LIVE_PROTOCOL.md) and
  /// resolves once the headset acks (`true`), it explicitly rejects (`false`),
  /// or the ack times out / all 3 retries are exhausted (`false`) -- so the UI
  /// can show "Pausing..." then "Paused" / "Headset didn't confirm. Try
  /// again." per `docs/APP_DESIGN.md`. Resolves `false` immediately if that
  /// headset isn't currently connected.
  Future<bool> sendCommand(String deviceId, String command, {Map<String, dynamic>? params, String? text}) async {
    final conn = connectionFor(deviceId);
    if (conn == null) return false;
    return conn.sendAndAwaitAck(
      LiveMessage.command(
        conn.nextSeq,
        sessionId: conn.activeSessionId ?? '',
        command: command,
        params: params,
        text: text,
      ),
    );
  }

  Future<bool> startSession(String deviceId) => sendCommand(deviceId, 'start');
  Future<bool> pause(String deviceId) => sendCommand(deviceId, 'pause');
  Future<bool> resume(String deviceId) => sendCommand(deviceId, 'resume');
  Future<bool> stopSession(String deviceId) => sendCommand(deviceId, 'stop');
  Future<bool> recenter(String deviceId) => sendCommand(deviceId, 'recenter');
  Future<bool> skipBlock(String deviceId) => sendCommand(deviceId, 'skip_block');
  Future<bool> showMessage(String deviceId, String text) => sendCommand(deviceId, 'show_message', text: text);

  /// `adjust_params` per LIVE_PROTOCOL.md: "validated against the game
  /// manifest on the headset and rejected with `ack.ok=false` if invalid" --
  /// the headset is the source of truth, but the app validates first against
  /// the same `paramSchema` (reusing the dynamic-form field validation) so
  /// the clinician gets an immediate, local error instead of a round trip for
  /// an obviously-bad value.
  Future<HubCommandResult> adjustParams(
    String deviceId,
    Map<String, dynamic> params, {
    Map<String, dynamic>? paramSchema,
  }) async {
    if (paramSchema != null) {
      final errors = validateParamsAgainstSchema(paramSchema, params);
      if (errors.isNotEmpty) return HubCommandResult.rejected(errors.values.first);
    }
    final ok = await sendCommand(deviceId, 'adjust_params', params: params);
    return ok ? const HubCommandResult.acked() : const HubCommandResult.rejected("Headset didn't confirm. Try again.");
  }

  /// Sends `assign_program` (the full block sequence + manifests) to one
  /// connected headset. Resolves the same way as [sendCommand].
  ///
  /// Also remembers `deviceId -> patientRef` for this run (in-memory only --
  /// there's no persistence layer for it, and it's re-established every time
  /// a program is sent, which is the only place the app currently learns
  /// which patient a headset is being used for). This backs
  /// [runningConnectionForPatient], which the patient profile's "Live now"
  /// pane (`docs/APP_DESIGN.md`) uses to find a connected headset actively
  /// running a session for *this* patient, since the wire protocol itself
  /// carries no patient identity outside `assign_program`/`session.json`.
  Future<bool> assignProgram(
    String deviceId, {
    required Map<String, dynamic> program,
    required String patientRef,
    List<Map<String, dynamic>>? manifests,
  }) async {
    final conn = connectionFor(deviceId);
    if (conn == null) return false;
    final ok = await conn.sendAndAwaitAck(
      LiveMessage.assignProgram(conn.nextSeq, program: program, patientRef: patientRef, manifests: manifests),
    );
    if (ok) _devicePatientRef[deviceId] = patientRef;
    return ok;
  }

  final Map<String, String> _devicePatientRef = {};

  /// A connected headset currently running a session for [patientId], or
  /// `null` if none is (no headset was assigned a program for this patient
  /// this run, or none currently has an active session). Used to show/hide
  /// the tablet/desktop "Live now" pane.
  HubConnection? runningConnectionForPatient(String patientId) {
    final server = _server;
    if (server == null) return null;
    for (final c in server.connections) {
      if (_devicePatientRef[c.deviceId] == patientId && c.activeSessionId != null) return c;
    }
    return null;
  }

  /// The `patientRef` a currently-live [sessionId] is running for, if this
  /// app instance's own `HubController` assigned the program that started it
  /// (`assignProgram` is the only place `deviceId -> patientRef` is learned --
  /// see [_devicePatientRef]'s doc comment). `null` if no connected headset is
  /// running that session, or the patient is unknown (e.g. the session was
  /// started by a different app instance/CLI tool). Used by the top-level
  /// Monitor tab to open the real [LiveMonitorScreen] for whichever session
  /// is live without the clinician having drilled in from a patient profile.
  String? patientRefForSession(String sessionId) {
    final server = _server;
    if (server == null) return null;
    for (final c in server.connections) {
      if (c.activeSessionId == sessionId) return _devicePatientRef[c.deviceId];
    }
    return null;
  }

  /// Every session id any currently-connected headset is actively running,
  /// for the Reports tab's "Live" badge (`reports_list_screen.dart`) -- it
  /// scans every patient's session list, so it needs a set it can check
  /// membership against rather than one patient at a time like
  /// [runningConnectionForPatient].
  Set<String> allRunningSessionIds() {
    final server = _server;
    if (server == null) return const {};
    return {
      for (final c in server.connections)
        if (c.activeSessionId != null) c.activeSessionId!,
    };
  }

  /// Every `session-*` directory currently stored under [sessionsDir]
  /// (uploaded by a headset via `PUT /opus/v1/sessions/{id}/files/{name}`),
  /// newest first -- backs the Devices screen's "Run analysis" action until
  /// a full hub-backed `SessionsRepository` exists (`docs/MANUAL_TODO.md`).
  /// The on-disk directory for [sessionId] if the hub has one (i.e. this is
  /// a real headset-uploaded session, not a bundled mock fixture) -- lets
  /// the session report screen offer "Run analysis" directly for a hub
  /// session it's viewing, without going via the Devices screen.
  String? sessionDirFor(String sessionId) {
    final dir = _sessionsDir;
    if (dir == null) return null;
    final path = '$dir/$sessionId';
    return Directory(path).existsSync() ? path : null;
  }

  List<Directory> storedSessionDirs() {
    final dir = _sessionsDir;
    if (dir == null || !Directory(dir).existsSync()) return const [];
    final dirs = Directory(dir).listSync().whereType<Directory>().toList()
      ..sort((a, b) => b.statSync().modified.compareTo(a.statSync().modified));
    return dirs;
  }

  /// Runs `python -m opus_analytics <sessionDir>` using the repo's analytics
  /// venv (`analytics\.venv\Scripts\python.exe` on Windows, resolved relative
  /// to the app's own directory tree so it works regardless of the launching
  /// shell's cwd) against one hub-stored session directory, per
  /// `docs/agent-briefs/A-next-run.md` B3 ("Run analysis" on desktop). Returns
  /// the process result; the caller is responsible for surfacing stdout/
  /// stderr/exit code to the clinician (a `metrics.json` file appears next to
  /// the session's other files on success).
  Future<ProcessResult> runAnalysis(String sessionDir) async {
    final python = _resolveAnalyticsPython();
    return Process.run(python, ['-m', 'opus_analytics', sessionDir]);
  }

  String _resolveAnalyticsPython() {
    final exeName = Platform.isWindows ? 'python.exe' : 'python';
    final binDir = Platform.isWindows ? 'Scripts' : 'bin';
    // The app runs from `<repo>/app` in dev (`flutter run`) and from
    // `<repo>/app/build/...` in a release build -- walk up from the running
    // executable's directory looking for `<repo>/analytics/.venv`.
    var dir = Directory.current;
    for (var i = 0; i < 6; i++) {
      final candidate = File('${dir.path}/analytics/.venv/$binDir/$exeName');
      if (candidate.existsSync()) return candidate.path;
      final parent = dir.parent;
      if (parent.path == dir.path) break;
      dir = parent;
    }
    // Fall back to the documented relative path from the app's own cwd.
    return 'analytics/.venv/$binDir/$exeName';
  }
}

/// Best-effort local IPv4 address for the Devices screen's "Hub address"
/// display (`ws://<ip>:<port>/opus/v1/live>`) -- prefers a non-loopback
/// adapter so the QR/manual-entry path in `contracts/LIVE_PROTOCOL.md` shows
/// something a headset on the same LAN can actually dial. Returns `null` if
/// none is found (loopback-only environment); the UI falls back to a
/// placeholder in that case.
Future<String?> localIpv4Address() async {
  final all = await localIpv4Addresses();
  return all.isEmpty ? null : all.first;
}

/// Every non-loopback local IPv4 address (one per active adapter), for the
/// Devices/Monitor empty state -- a clinic phone routinely has more than one
/// (e.g. `wlan0` on the clinic Wi-Fi *and* `wlan1` its own hotspot for the
/// haptic sleeve, run12's on-device network: `172.18.226.79` +
/// `10.179.145.149`), and a headset must be told the one on the SAME network
/// it's on, not just "the" IP. Empty if none is found (loopback-only
/// environment); the UI falls back to a placeholder in that case.
Future<List<String>> localIpv4Addresses() async {
  final labeled = await localIpv4AddressesLabeled();
  return [for (final l in labeled) l.address];
}

/// One non-loopback IPv4 address plus a clinician-facing label for it, e.g.
/// `("Wi-Fi", "172.18.226.79")` / `("Hotspot", "10.179.145.149")` --
/// [localIpv4Addresses] alone can't tell a clinician which address a headset
/// on the clinic Wi-Fi should actually dial vs. this device's own hotspot
/// (2026-09-19 known fact: the test phone's `wlan0` = `172.18.226.79/20` on
/// the shared clinic Wi-Fi, `wlan1` = `10.179.145.149` is its own hotspot for
/// the haptic sleeve -- a headset must be told the Wi-Fi one, and the old
/// Devices card showing the hotspot address was the reported bug).
///
/// The label comes from the OS-reported interface name where that's
/// meaningful (Windows already names adapters `Wi-Fi`/`Ethernet`), and from
/// a `wlan0`/`wlan1` convention on Android (`wlan0` is conventionally the
/// station/infra Wi-Fi radio, `wlan1` the SoftAP/hotspot radio on the
/// Android devices this app targets) -- falling back to the raw interface
/// name when neither convention matches. Sorted Wi-Fi-labelled entries first
/// so the Devices/Monitor screens can show "the" address as `.first` and
/// still label every candidate underneath.
Future<List<({String label, String address})>> localIpv4AddressesLabeled() async {
  try {
    final interfaces = await NetworkInterface.list(type: InternetAddressType.IPv4);
    final out = <({String label, String address})>[];
    for (final iface in interfaces) {
      final label = _labelForInterface(iface.name);
      for (final addr in iface.addresses) {
        if (!addr.isLoopback) out.add((label: label, address: addr.address));
      }
    }
    out.sort((a, b) {
      final aWifi = a.label == 'Wi-Fi' ? 0 : 1;
      final bWifi = b.label == 'Wi-Fi' ? 0 : 1;
      return aWifi.compareTo(bWifi);
    });
    return out;
  } catch (_) {
    // Best-effort only -- some sandboxes/CI environments deny interface enumeration.
    return const [];
  }
}

String _labelForInterface(String name) {
  final n = name.toLowerCase();
  if (n == 'wlan0') return 'Wi-Fi';
  if (n == 'wlan1') return 'Hotspot';
  if (n.contains('wi-fi') || n.contains('wifi')) return 'Wi-Fi';
  if (n.contains('hotspot') || n.contains('ap0') || n.contains('softap')) return 'Hotspot';
  if (n.contains('ethernet')) return 'Ethernet';
  return name;
}

/// Result of an [HubController.adjustParams] call -- distinguishes a local
/// validation rejection (never sent to the headset) from a headset
/// acknowledgement, so the UI can show the right inline message either way.
sealed class HubCommandResult {
  const new();
  const factory acked() = HubCommandAcked;
  const factory rejected(String reason) = HubCommandRejected;
  bool get ok => this is HubCommandAcked;
}

class HubCommandAcked extends HubCommandResult {
  const new();
}

class HubCommandRejected extends HubCommandResult {
  const new(this.reason);
  final String reason;
}

/// Immutable snapshot of the hub's running state, for the Devices screen.
/// Public variants (not private) so UI code can pattern-match/type-check
/// against [HubRunning] directly instead of reaching into a sealed subtype
/// via `dynamic`.
sealed class HubControllerState {
  const new();
  const factory stopped() = HubStopped;
  const factory running({
    required String hubId,
    required int port,
    required List<HeadsetInfo> headsets,
  }) = HubRunning;

  bool get running => this is HubRunning;
}

class HubStopped extends HubControllerState {
  const new();
}

class HubRunning extends HubControllerState {
  const new({required this.hubId, required this.port, required this.headsets});
  final String hubId;
  final int port;
  final List<HeadsetInfo> headsets;
}
