// B3/B4: exercises HubLiveRepository against a REAL HubServer + a real
// WebSocket client standing in for a headset (same pattern as
// hub_integration_test.dart), verifying the live monitor's translation of
// wire-level status/trial_event messages into the app's LiveMessage shape --
// not just that the hub protocol works (hub_integration_test.dart already
// covers that), but that the UI-facing repository built on top of it reports
// the right trial index, status and outcome as a real session plays out.
import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/core/hub/live_message.dart';
import 'package:opus_app/data/models/live_message.dart' as app;
import 'package:opus_app/data/repositories/hub/hub_live_repository.dart';

void main() {
  late Directory dataDir;
  late HubServer server;
  late int port;

  setUp(() async {
    dataDir = Directory.systemTemp.createTempSync('opus_hub_live_repo_test_');
    port = 20787 + Random().nextInt(2000);
    server = HubServer(sessionsDir: dataDir.path, port: port);
    await server.start();
  });

  tearDown(() async {
    await server.stop();
    dataDir.deleteSync(recursive: true);
  });

  test('HubLiveRepository reflects real status/trial_event traffic from a connected headset', () async {
    final socket = await WebSocket.connect('ws://127.0.0.1:$port/opus/v1/live');
    final incoming = StreamIterator(socket);

    void send(LiveMessage m) => socket.add(jsonEncode(m.toJson()));
    Future<Map<String, dynamic>> nextMessage() async {
      expect(await incoming.moveNext(), isTrue);
      return jsonDecode(incoming.current as String) as Map<String, dynamic>;
    }

    // hello -> hello_ack (mirrors a real headset's handshake).
    send(
      LiveMessage(
        type: 'hello',
        from: 'headset',
        seq: 0,
        payload: {
          'device_id': 'headset-test',
          'role': 'headset',
          'versions': {'shell': '0.0.0-test'},
        },
      ),
    );
    final helloAck = await nextMessage();
    expect(helloAck['type'], 'hello_ack');

    // Wait for the server to register the connection (async after hello).
    await Future<void>.delayed(const Duration(milliseconds: 100));
    expect(server.connections, hasLength(1));
    final connection = server.connections.single;
    connection.activeSessionId = 'session-under-test';

    final repo = HubLiveRepository(connection);
    final messages = <app.LiveMessage>[];
    final sub = repo.watch('session-under-test', totalTrials: 5).listen(messages.add);
    addTearDown(sub.cancel);
    addTearDown(repo.dispose);

    // Real trial_event traffic, as a headset would send it.
    send(LiveMessage(type: 'status', from: 'headset', seq: 1, payload: {'state': 'running'}));
    send(
      LiveMessage(
        type: 'trial_event',
        from: 'headset',
        seq: 2,
        payload: {'type': 'trial_start', 'trial': 0, 'block': 0},
      ),
    );
    send(
      LiveMessage(
        type: 'trial_event',
        from: 'headset',
        seq: 3,
        payload: {'type': 'trial_end', 'trial': 0, 'block': 0, 'outcome': 'success'},
      ),
    );
    send(
      LiveMessage(
        type: 'metrics_tick',
        from: 'headset',
        seq: 4,
        payload: {
          'window_trials': 1,
          'metrics': {'reaction_time_ms': 412.0},
        },
      ),
    );

    await Future<void>.delayed(const Duration(milliseconds: 150));

    expect(messages, isNotEmpty);
    final last = messages.last;
    expect(last.status, app.LiveSessionStatus.running);
    expect(last.trialIndex, 1); // 0-indexed wire trial -> 1-indexed UI
    expect(last.totalTrials, 5);
    expect(last.lastOutcome, 'success');
    expect(last.rollingMetrics['reaction_time_ms'], 412.0);

    // A "finished" status (the schema's terminal state) flips the app-level
    // status to stopped, exactly what drives the live monitor's End-session
    // button to disable.
    send(LiveMessage(type: 'status', from: 'headset', seq: 5, payload: {'state': 'finished'}));
    await Future<void>.delayed(const Duration(milliseconds: 100));
    expect(messages.last.status, app.LiveSessionStatus.stopped);

    await incoming.cancel();
    await socket.close();
  });

  test('HubConnection.sendAndAwaitAck resolves true on a real ack and false on timeout', () async {
    final socket = await WebSocket.connect('ws://127.0.0.1:$port/opus/v1/live');
    final incoming = StreamIterator(socket);
    void send(LiveMessage m) => socket.add(jsonEncode(m.toJson()));
    Future<Map<String, dynamic>> nextMessage() async {
      expect(await incoming.moveNext(), isTrue);
      return jsonDecode(incoming.current as String) as Map<String, dynamic>;
    }

    send(
      LiveMessage(
        type: 'hello',
        from: 'headset',
        seq: 0,
        payload: {
          'device_id': 'headset-ack-test',
          'role': 'headset',
          'versions': {'shell': '0.0.0-test'},
        },
      ),
    );
    await nextMessage(); // hello_ack
    await Future<void>.delayed(const Duration(milliseconds: 100));
    final connection = server.connections.singleWhere((c) => c.deviceId == 'headset-ack-test');

    // Fire the command via the real HubConnection API, then reply with a
    // real ack from the "headset" socket -- this is HubController.pause()'s
    // underlying call.
    final ackFuture = connection.sendAndAwaitAck(
      LiveMessage.command(connection.nextSeq, sessionId: 'session-under-test', command: 'pause'),
    );
    final commandMsg = await nextMessage();
    expect(commandMsg['type'], 'command');
    send(LiveMessage.ack(0, ackId: commandMsg['id'] as String, ok: true));
    expect(await ackFuture, isTrue);

    await incoming.cancel();
    await socket.close();
  });
}
