// Headless entrypoint for `HubServer` + `UdpBeacon` -- the exact same hub
// code the Flutter app runs (`lib/core/hub/`), started as a plain `dart run`
// process so M4's integration test (and manual testing against
// `sim/live/fake_headset.py`) doesn't need a full Flutter Windows build.
//
// Usage (from `app/`):
//   dart run tool/hub_cli.dart [--port 8787] [--beacon-port 8788] [--data-dir <path>] [--no-beacon]
//
// Then, from `sim/live/` (see that folder's README.md), against this process:
//   .venv/Scripts/python.exe fake_headset.py --session ../../contracts/fixtures/sessions/healthy \
//     --host 127.0.0.1 --port 8787 --no-scenario
import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/core/hub/live_message.dart';
import 'package:opus_app/core/hub/udp_beacon.dart';

Future<void> main(List<String> args) async {
  var port = 8787;
  var beaconPort = 8788;
  var dataDir = '${Directory.current.path}/.hub_data';
  var beacon = true;
  var autoDrive = false;

  for (var i = 0; i < args.length; i++) {
    switch (args[i]) {
      case '--port':
        port = int.parse(args[++i]);
      case '--beacon-port':
        beaconPort = int.parse(args[++i]);
      case '--data-dir':
        dataDir = args[++i];
      case '--no-beacon':
        beacon = false;
      case '--auto-drive':
        // Manual/M4 test convenience: as soon as a headset connects, send a
        // minimal assign_program then a start command automatically, so this
        // CLI can drive `sim/live/fake_headset.py --no-scenario` end to end
        // without a second script. Off by default (the real app decides
        // when to start a session from clinician UI, not automatically).
        autoDrive = true;
    }
  }

  final server = HubServer(sessionsDir: dataDir, port: port);
  await server.start();
  stdout.writeln('hub_cli: HubServer listening on 0.0.0.0:$port (data: $dataDir, hub_id: ${server.hubId})');

  UdpBeacon? udpBeacon;
  if (beacon) {
    udpBeacon = UdpBeacon(hubId: server.hubId, port: port, name: 'OPUS Hub CLI', beaconPort: beaconPort);
    await udpBeacon.start();
    stdout.writeln('hub_cli: UDP beacon broadcasting on port $beaconPort every 1s');
  }

  // Real headset<->hub latency p50/p95 (LIVE_PROTOCOL.md goal G2) is measured
  // by comparing real send/receive wall-clock timestamps in
  // `test/hub/hub_integration_test.dart`, which owns both ends of the
  // connection and so can time them precisely. This CLI only logs message
  // traffic for manual runs against `sim/live/fake_headset.py`.
  //
  // `driven` is keyed by *connection instance* (`HubConnection` has no
  // custom `==`, so this is object identity), not `deviceId`. A second
  // headset connecting with the same `deviceId` -- the normal case for a
  // repeated demo run, or a real reconnect -- gets a brand-new
  // `HubConnection` object from `HubServer` (`hub_server.dart`'s
  // `_connections[deviceId] = conn`, a fresh instance each time), so keying
  // on the string used to silently swallow every connection after the
  // first: `driven.add(c.deviceId)` returned `false` forever once that
  // deviceId had ever been driven once, and no later headset ever received
  // `start` (`hubSaidStart=False` in the 2026-09-19 full-pipeline run's
  // later Unity runs -- see logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md's
  // "known-wrong and still open"). Pruning `driven` to whatever is actually
  // still connected on every update, before re-adding, is what makes this
  // re-arm per connection rather than leaking forever.
  final driven = <HubConnection>{};
  server.connectionsStream.listen((conns) {
    driven.removeWhere((c) => !conns.contains(c));
    stdout.writeln('hub_cli: ${conns.length} connected headset(s): ${conns.map((c) => c.deviceId).join(', ')}');
    for (final c in conns) {
      c.statusStream.listen((s) => stdout.writeln('hub_cli: [${c.deviceId}] status: ${jsonEncode(s)}'));
      c.trialEventStream.listen((e) => stdout.writeln('hub_cli: [${c.deviceId}] trial_event: ${jsonEncode(e)}'));
      c.metricsStream.listen((m) => stdout.writeln('hub_cli: [${c.deviceId}] metrics_tick: ${jsonEncode(m)}'));
      // Per-message latency (envelope ts_ms -> hub receipt), for the
      // integration demo (docs/agent-briefs/A-next-run.md task 3): a coarse,
      // one-way figure per InboundMessageLog's own doc -- ping/pong RTT
      // (logged separately below) is the more trustworthy number when the
      // two clocks may not be in sync.
      c.messageLog.listen(
        (m) => stdout.writeln('hub_cli: [${c.deviceId}] ${m.type} (seq ${m.seq}) latency ${m.latencyMs} ms'),
      );

      if (autoDrive && driven.add(c)) {
        unawaited(_autoDrive(c));
      }
    }
  });

  // Keep the process alive; Ctrl+C stops it (SIGINT isn't reliably supported
  // on Windows via ProcessSignal, so this simply runs until externally killed
  // -- fine for a manual/test-harness CLI).
  await Completer<void>().future;
}

/// `--auto-drive`: sends a minimal `assign_program` then a `command: start`
/// to a newly-connected headset, so `sim/live/fake_headset.py --no-scenario`
/// (which waits up to 30 s for a real `start`) can be exercised manually
/// without a second script.
Future<void> _autoDrive(HubConnection c) async {
  await Future<void>.delayed(const Duration(milliseconds: 500));
  stdout.writeln('hub_cli: [${c.deviceId}] auto-drive: assign_program');
  c.send(LiveMessage.assignProgram(
    c.nextSeq,
    patientRef: 'synthetic-healthy-42',
    program: {
      'program_id': 'hub-cli-auto',
      'patient_ref': 'synthetic-healthy-42',
      'created_by': 'hub_cli',
      'schedule': {'sessions_per_week': 3, 'weeks': 6},
      'blocks': [
        {
          'index': 0,
          'game_id': 'orchard_reach',
          'game_version': '0.1.0',
          'params': {'trialCount': 5, 'side': 'alternate'},
        },
      ],
    },
  ));
  await Future<void>.delayed(const Duration(milliseconds: 500));
  stdout.writeln('hub_cli: [${c.deviceId}] auto-drive: command start');
  c.send(LiveMessage.command(c.nextSeq, sessionId: c.activeSessionId ?? 'hub-cli-session', command: 'start'));
}
