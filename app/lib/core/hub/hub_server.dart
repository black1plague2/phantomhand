import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:async/async.dart';
import 'package:crypto/crypto.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/live_message.dart';
import 'package:uuid/uuid.dart';

const _uuid = Uuid();
final _fileNamePattern = RegExp(r'^(session\.json|events\.ndjson|(?:kin|sens)_\d{3}\.json|metrics\.json)$');

/// The OPUS hub server (`contracts/LIVE_PROTOCOL.md`): an `HttpServer` on
/// `0.0.0.0:8787` serving the WebSocket upgrade at `/opus/v1/live`, the file
/// upload endpoint `PUT /opus/v1/sessions/{id}/files/{name}`, and
/// `GET /opus/v1/health`. Runs on Windows desktop and Android (`dart:io`);
/// the web build has no hub (`kIsWeb` callers should show "hub not available
/// on web" instead of constructing this).
///
/// [sessionsDir] is where uploaded files land (`sessions/{id}/...`); the
/// Flutter app passes its app-support directory, `tool/hub_cli.dart` passes a
/// plain path so the exact same server code runs headless for testing.
class HubServer {
  new({required this.sessionsDir, this.port = 8787, String? hubId, this.hubName = 'OPUS Hub'})
      : hubId = hubId ?? _uuid.v4();

  final int port;
  final String sessionsDir;
  final String hubId;
  final String hubName;

  /// One `pair_token` per device, issued on first `hello` and reused after
  /// (LIVE_PROTOCOL.md: "the hub issues payload.pair_token in hello_ack on
  /// first pairing, and the headset stores it and sends it in later hellos").
  final Map<String, String> _pairTokens = {};
  final Map<String, HubConnection> _connections = {}; // deviceId -> connection
  final _connectionsController = StreamController<List<HubConnection>>.broadcast();

  /// Last-known snapshot of a headset that has since disconnected, kept so
  /// the Devices screen can show a clear "disconnected, reconnecting" row
  /// instead of the device silently vanishing from the list
  /// (`docs/APP_DESIGN.md`: "Headset disconnected. The session keeps running
  /// on the headset and will sync when it reconnects."). Cleared once that
  /// device reconnects.
  final Map<String, DisconnectedHeadset> _disconnected = {};
  final _knownHeadsetsController = StreamController<List<HeadsetSnapshot>>.broadcast();

  HttpServer? _server;
  Stream<List<HubConnection>> get connectionsStream => _connectionsController.stream;
  List<HubConnection> get connections => List.unmodifiable(_connections.values);

  /// Every headset the hub has ever seen this run, connected or not --
  /// active connections first, then disconnected ones (newest-disconnected
  /// first). Use this for the Devices screen; use [connections] for command
  /// dispatch, where only currently-reachable sockets matter.
  Stream<List<HeadsetSnapshot>> get knownHeadsetsStream => _knownHeadsetsController.stream;
  List<HeadsetSnapshot> get knownHeadsets => [
        for (final c in connections) HeadsetSnapshot.connected(c),
        for (final d in _disconnected.values.toList()..sort((a, b) => b.disconnectedAt.compareTo(a.disconnectedAt)))
          HeadsetSnapshot.disconnected(d),
      ];

  Future<void> start() async {
    _server = await HttpServer.bind(InternetAddress.anyIPv4, port);
    _server!.listen(_handleRequest);
  }

  Future<void> stop() async {
    // Snapshot first: `c.close()` synchronously fires `onDisconnected`, whose
    // listener (`HubServer._onConnectionClosed`) removes the entry from
    // `_connections` -- iterating the live map's `.values` directly throws
    // "Concurrent modification during iteration" (found by a real test run,
    // not just review).
    for (final c in connections) {
      await c.close();
    }
    await _server?.close(force: true);
    await _connectionsController.close();
    await _knownHeadsetsController.close();
  }

  Future<void> _handleRequest(HttpRequest request) async {
    final path = request.uri.path;
    try {
      if (path == '/opus/v1/health') {
        _handleHealth(request);
      } else if (path == '/opus/v1/live/last_status' && request.method == 'GET') {
        _handleLastStatus(request);
      } else if (path == '/opus/v1/live' && WebSocketTransformer.isUpgradeRequest(request)) {
        final socket = await WebSocketTransformer.upgrade(request);
        _handleSocket(socket);
      } else if (request.method == 'PUT' && _fileUploadPattern.hasMatch(path)) {
        await _handleFileUpload(request);
      } else {
        request.response.statusCode = HttpStatus.notFound;
        await request.response.close();
      }
    } catch (e) {
      request.response.statusCode = HttpStatus.internalServerError;
      request.response.write(jsonEncode({'error': e.toString()}));
      await request.response.close();
    }
  }

  static final _fileUploadPattern = RegExp(r'^/opus/v1/sessions/([^/]+)/files/([^/]+)$');

  static const _maxRecentEvents = 60;
  Map<String, dynamic>? _lastStatus;
  final List<Map<String, dynamic>> _recentEvents = [];

  /// `GET /opus/v1/live/last_status` (read-only relay for third viewers such as
  /// `tools/demo/live_plot.py --hub`): `{"status": <latest status payload incl.
  /// game_state + trace, plus _recv_ms> | null, "events": [last <=60 trial_event
  /// payloads]}`. Always 200; `status` is null until a headset has sent one.
  void _handleLastStatus(HttpRequest request) {
    request.response
      ..statusCode = HttpStatus.ok
      ..headers.contentType = ContentType.json
      ..write(jsonEncode({'status': _lastStatus, 'events': _recentEvents}));
    request.response.close();
  }

  void _handleHealth(HttpRequest request) {
    request.response
      ..statusCode = HttpStatus.ok
      ..headers.contentType = ContentType.json
      ..write(jsonEncode({
        'status': 'ok',
        'hub_id': hubId,
        'name': hubName,
        'connected_headsets': _connections.length,
      }));
    request.response.close();
  }

  /// `PUT /opus/v1/sessions/{id}/files/{name}`, idempotent by
  /// `(session_id, name)` + sha256: 201 new, 200 identical content already
  /// stored, 409 a *different* file already exists at that name.
  Future<void> _handleFileUpload(HttpRequest request) async {
    final match = _fileUploadPattern.firstMatch(request.uri.path)!;
    final sessionId = Uri.decodeComponent(match.group(1)!);
    final name = Uri.decodeComponent(match.group(2)!);
    if (!_fileNamePattern.hasMatch(name)) {
      request.response.statusCode = HttpStatus.badRequest;
      await request.response.close();
      return;
    }
    final bytes = await request.fold<List<int>>([], (acc, chunk) => acc..addAll(chunk));
    final sha = sha256.convert(bytes).toString();

    final dir = Directory('$sessionsDir/$sessionId');
    await dir.create(recursive: true);
    final file = File('${dir.path}/$name');

    if (file.existsSync()) {
      final existingSha = sha256.convert(await file.readAsBytes()).toString();
      if (existingSha == sha) {
        request.response.statusCode = HttpStatus.ok; // 200: identical, idempotent no-op
      } else {
        request.response.statusCode = HttpStatus.conflict; // 409: mismatch
      }
      await request.response.close();
      return;
    }

    await file.writeAsBytes(bytes);
    request.response.statusCode = HttpStatus.created; // 201: new
    request.response.headers.set('X-Sha256', sha);
    await request.response.close();
  }

  /// A raw `WebSocket` can only be listened to once (dart:io), but the hub
  /// needs to read the first message (must be `hello`, LIVE_PROTOCOL.md)
  /// before it knows which `HubConnection` to hand the rest of the stream to.
  /// `StreamQueue` solves this: it attaches the one-and-only listener here,
  /// and `.rest` gives a fresh, listenable stream of everything after.
  Future<void> _handleSocket(WebSocket socket) async {
    final queue = StreamQueue<dynamic>(socket);
    if (!await queue.hasNext) return;
    final data = await queue.next;
    if (data is! String) {
      await socket.close();
      return;
    }
    Map<String, dynamic> json;
    try {
      json = jsonDecode(data) as Map<String, dynamic>;
    } catch (_) {
      await socket.close();
      return;
    }
    if (json['type'] != 'hello') {
      // First message must be hello (LIVE_PROTOCOL.md).
      socket.add(jsonEncode(LiveMessage.error(0, code: 'expected_hello', message: 'first message must be hello').toJson()));
      await socket.close();
      return;
    }
    _onHello(socket, queue.rest, json);
  }

  void _onHello(WebSocket socket, Stream<dynamic> incoming, Map<String, dynamic> helloJson) {
    final error = validateLiveMessage(helloJson);
    if (error != null) {
      socket.add(jsonEncode(LiveMessage.error(0, code: 'invalid_hello', message: error).toJson()));
      socket.close();
      return;
    }
    final payload = (helloJson['payload'] as Map).cast<String, dynamic>();
    final deviceId = payload['device_id'] as String;
    final resumeFromSeq = payload['resume_from_seq'] as int?;

    final pairToken = _pairTokens.putIfAbsent(deviceId, _generatePairToken);
    final old = _connections[deviceId];

    final conn = HubConnection(
      deviceId: deviceId,
      socket: WebSocketLiveSocket(socket),
      pairToken: pairToken,
      incoming: incoming,
    );
    if (old != null) {
      // Reconnect: carry over what the old connection had sent so
      // `replaySince` below has something to work with, then retire the old
      // socket (it's already gone from the peer's side, but close it to be
      // safe -- e.g. free timers, drop it from being double-counted).
      conn.seedHistory(old.sentHistory);
      unawaited(old.close());
    }
    _disconnected.remove(deviceId); // it's back -- clear any stale "disconnected" row
    _connections[deviceId] = conn;
    conn.onDisconnected.listen((_) => _onConnectionClosed(deviceId, conn));
    // Re-publish the snapshot whenever the headset reports something. Previously snapshots went out only on
    // connect/disconnect, so every screen built from them (Monitor, Devices) froze at the moment of connection:
    // no session id yet -> Monitor showed "No headset connected" forever while the hub was happily receiving
    // status + trial events (found on the pilot phone 2026-09-19 with the mock headset). Throttled so a burst
    // of trial events costs at most ~4 rebuilds/s.
    void touch(_) => _publishSnapshotThrottled();
    conn.statusStream.listen((s) {
      _lastStatus = {...s, '_recv_ms': DateTime.now().millisecondsSinceEpoch.toDouble()};
      touch(s);
    });
    conn.trialEventStream.listen((e) {
      _recentEvents.add(e);
      if (_recentEvents.length > _maxRecentEvents) _recentEvents.removeAt(0);
      touch(e);
    });
    conn.metricsStream.listen(touch);
    conn.send(LiveMessage.helloAck(conn.nextSeq, pairToken: pairToken, resumeFromSeq: old != null ? resumeFromSeq : null));
    if (old != null && resumeFromSeq != null) conn.replaySince(resumeFromSeq);
    _connectionsController.add(connections);
    _knownHeadsetsController.add(knownHeadsets);
  }

  void _onConnectionClosed(String deviceId, HubConnection conn) {
    if (_connections[deviceId] == conn) {
      // Keep the last-known state around as a "disconnected" row for the
      // Devices screen, but drop from the "active" set so a reconnect
      // creates a fresh HubConnection.
      _connections.remove(deviceId);
      _disconnected[deviceId] = DisconnectedHeadset(
        deviceId: deviceId,
        lastStatus: conn.lastStatus,
        lastRtt: conn.lastRtt,
        disconnectedAt: DateTime.now(),
      );
      _connectionsController.add(connections);
      _knownHeadsetsController.add(knownHeadsets);
    }
  }

  Timer? _snapshotTimer;

  void _publishSnapshotThrottled() {
    if (_snapshotTimer?.isActive ?? false) return;
    _snapshotTimer = Timer(const Duration(milliseconds: 250), () {
      if (!_knownHeadsetsController.isClosed) _knownHeadsetsController.add(knownHeadsets);
    });
  }

  String _generatePairToken() {
    final rand = Random.secure();
    return List.generate(6, (_) => rand.nextInt(10)).join();
  }
}

/// Last-known snapshot of a headset that disconnected, kept so the Devices
/// screen can distinguish "never seen" from "was here, dropped" instead of
/// the row just disappearing.
class DisconnectedHeadset {
  new({
    required this.deviceId,
    required this.lastStatus,
    required this.lastRtt,
    required this.disconnectedAt,
  });
  final String deviceId;
  final Map<String, dynamic>? lastStatus;
  final Duration? lastRtt;
  final DateTime disconnectedAt;
}

/// One row of the Devices screen's headset list: either a live [HubConnection]
/// or a [DisconnectedHeadset] snapshot, normalized to the same shape so the
/// UI doesn't need to branch on which it got.
class HeadsetSnapshot {
  new _({
    required this.deviceId,
    required this.connected,
    required this.status,
    required this.rtt,
    this.disconnectedAt,
  });

  factory connected(HubConnection c) => HeadsetSnapshot._(
        deviceId: c.deviceId,
        connected: true,
        status: c.lastStatus,
        rtt: c.lastRtt,
      );

  factory disconnected(DisconnectedHeadset d) => HeadsetSnapshot._(
        deviceId: d.deviceId,
        connected: false,
        status: d.lastStatus,
        rtt: d.lastRtt,
        disconnectedAt: d.disconnectedAt,
      );

  final String deviceId;
  final bool connected;
  final Map<String, dynamic>? status;
  final Duration? rtt;
  final DateTime? disconnectedAt;
}
