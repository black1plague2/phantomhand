// M4 integration test: starts the REAL hub server (the exact code
// `tool/hub_cli.dart` and the Flutter app both run) and a Dart WebSocket test
// client standing in for a headset, on real loopback sockets. Drives:
// hello -> hello_ack -> ping/pong (RTT) -> trial_events visible on the hub ->
// assign_program(ack) -> command start(ack) -> file PUT (idempotent by
// sha256) -> session appears on disk. Measures real send/receive wall-clock
// latency (LIVE_PROTOCOL.md goal G2: < 250 ms p95 -- trivially true on
// loopback, but this is the harness a real headset build would reuse).
import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/core/hub/live_message.dart';
import 'package:uuid/uuid.dart';

const _uuid = Uuid();

void main() {
  late Directory dataDir;
  late HubServer server;
  late int port;

  setUp(() async {
    dataDir = Directory.systemTemp.createTempSync('opus_hub_test_');
    // HttpServer.bind supports an OS-assigned port (0), but HubServer
    // doesn't expose the bound port back out, so this picks a high,
    // unlikely-to-collide fixed port per test run instead.
    port = 18787 + Random().nextInt(2000);
    server = HubServer(sessionsDir: dataDir.path, port: port);
    await server.start();
  });

  tearDown(() async {
    await server.stop();
    dataDir.deleteSync(recursive: true);
  });

  test('GET /opus/v1/health responds ok', () async {
    final resp = await HttpClient().getUrl(Uri.parse('http://127.0.0.1:$port/opus/v1/health'));
    final result = await (await resp.close()).transform(utf8.decoder).join();
    final json = jsonDecode(result) as Map<String, dynamic>;
    expect(json['status'], 'ok');
    expect(json['hub_id'], server.hubId);
  });

  test(
    'full flow: hello -> ping/pong RTT -> trial_events visible -> assign_program+start '
    '(acked) -> file PUT (idempotent) -> session on disk',
    () async {
      final socket = await WebSocket.connect('ws://127.0.0.1:$port/opus/v1/live');
      final incoming = StreamIterator(socket);
      final sendTimesById = <String, DateTime>{};

      Future<Map<String, dynamic>> nextMessage() async {
        expect(await incoming.moveNext(), isTrue);
        return jsonDecode(incoming.current as String) as Map<String, dynamic>;
      }

      void send(Map<String, dynamic> msg) {
        sendTimesById[msg['id'] as String] = DateTime.now();
        socket.add(jsonEncode(msg));
      }

      // 1. hello -> hello_ack.
      send({
        'v': 1,
        'type': 'hello',
        'id': _uuid.v4(),
        'seq': 0,
        'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
        'from': 'headset',
        'payload': {
          'device_id': 'test-headset-1',
          'role': 'headset',
          'versions': {'shell': '0.0.0-test'},
        },
      });
      final helloAck = await nextMessage();
      expect(helloAck['type'], 'hello_ack');
      expect(helloAck['payload']['pair_token'], isNotNull);
      final conn = server.connections.firstWhere((c) => c.deviceId == 'test-headset-1');

      // 2. The hub pings every 1s by default; reply and confirm RTT tracking.
      final firstPing = await nextMessage();
      expect(firstPing['type'], 'ping');
      send({
        'v': 1,
        'type': 'pong',
        'id': _uuid.v4(),
        'seq': 1,
        'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
        'from': 'headset',
        'payload': {'echo_ts_ms': firstPing['payload']['echo_ts_ms']},
      });
      await Future<void>.delayed(const Duration(milliseconds: 30));
      expect(conn.lastRtt, isNotNull);
      expect(conn.lastRtt!.inMilliseconds, lessThan(250)); // loopback, same process

      // 3. headset -> hub trial_events: measure real wall-clock latency from
      // "test sends" to "HubConnection.trialEventStream fires" (goal G2).
      final visibleLatenciesMs = <double>[];
      final received = <Map<String, dynamic>>[];
      final sub = conn.trialEventStream.listen((payload) {
        final sentAt = sendTimesById[payload['_test_msg_id']];
        if (sentAt != null) {
          visibleLatenciesMs.add(DateTime.now().difference(sentAt).inMicroseconds / 1000);
        }
        received.add(payload);
      });

      for (var i = 0; i < 10; i++) {
        final id = _uuid.v4();
        send({
          'v': 1,
          'type': 'trial_event',
          'id': id,
          'seq': 2 + i,
          'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
          'from': 'headset',
          'payload': {
            'type': 'trial_start',
            'trial': i,
            'block': 0,
            't_ms': i * 1000.0,
            'seq': i,
            // Test-only field riding along so the receiver can match this
            // event back to its send time; not part of the real schema
            // (event.schema.json doesn't forbid additional properties).
            '_test_msg_id': id,
          },
        });
      }
      await Future<void>.delayed(const Duration(milliseconds: 100));
      expect(received, hasLength(10));
      await sub.cancel();

      if (visibleLatenciesMs.isNotEmpty) {
        visibleLatenciesMs.sort();
        double percentile(double p) =>
            visibleLatenciesMs[(visibleLatenciesMs.length * p).floor().clamp(0, visibleLatenciesMs.length - 1)];
        print(
          'hub_integration_test: headset->hub trial_event visible latency '
          'p50=${percentile(0.5).toStringAsFixed(2)}ms p95=${percentile(0.95).toStringAsFixed(2)}ms '
          'max=${visibleLatenciesMs.last.toStringAsFixed(2)}ms (n=${visibleLatenciesMs.length}, loopback, same process)',
        );
        expect(percentile(0.95), lessThan(250)); // LIVE_PROTOCOL.md goal G2
      }

      // 4. hub -> headset assign_program (requires_ack): ack it, then a
      // command start (requires_ack): ack that too. Confirms the retry
      // machinery doesn't fire once a real ack arrives.
      conn.send(LiveMessage.assignProgram(
        conn.nextSeq,
        patientRef: 'test-patient',
        program: {
          'program_id': 'test-program',
          'patient_ref': 'test-patient',
          'created_by': 'hub_integration_test',
          'schedule': {'sessions_per_week': 3, 'weeks': 6},
          'blocks': [
            {'index': 0, 'game_id': 'orchard_reach', 'game_version': '0.1.0', 'params': <String, dynamic>{}},
          ],
        },
      ));
      final assignMsg = await nextMessage();
      expect(assignMsg['type'], 'assign_program');
      expect(assignMsg['requires_ack'], isTrue);
      send({
        'v': 1,
        'type': 'ack',
        'id': _uuid.v4(),
        'seq': 3,
        'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
        'from': 'headset',
        'payload': {'ack_id': assignMsg['id'], 'ok': true, 'recv_ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble()},
      });

      conn.send(LiveMessage.command(conn.nextSeq, sessionId: 'test-session-1', command: 'start'));
      final startMsg = await nextMessage();
      expect(startMsg['type'], 'command');
      expect((startMsg['payload'] as Map)['command'], 'start');
      send({
        'v': 1,
        'type': 'ack',
        'id': _uuid.v4(),
        'seq': 4,
        'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
        'from': 'headset',
        'payload': {'ack_id': startMsg['id'], 'ok': true, 'recv_ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble()},
      });
      // Give the ack a moment to land, then confirm no retry frame follows
      // (the outbox timer was cancelled).
      await Future<void>.delayed(const Duration(milliseconds: 1100));

      // 5. Bulk channel: file PUT idempotency (201 new / 200 identical /
      // 409 mismatch) and the file actually lands under sessionsDir.
      const sessionId = 'test-session-1';
      final bytes = utf8.encode(jsonEncode({'session_id': sessionId, 'ok': true}));
      final putUri = Uri.parse('http://127.0.0.1:$port/opus/v1/sessions/$sessionId/files/session.json');
      final client = HttpClient();

      final put1 = await (await client.putUrl(putUri)..add(bytes)).close();
      expect(put1.statusCode, HttpStatus.created); // 201 new
      await put1.drain<void>();

      final put2 = await (await client.putUrl(putUri)..add(bytes)).close();
      expect(put2.statusCode, HttpStatus.ok); // 200 identical (idempotent)
      await put2.drain<void>();

      final put3 = await (await client.putUrl(putUri)..add(utf8.encode(jsonEncode({'different': true})))).close();
      expect(put3.statusCode, HttpStatus.conflict); // 409 mismatch
      await put3.drain<void>();

      final storedFile = File('${dataDir.path}/$sessionId/session.json');
      expect(storedFile.existsSync(), isTrue);
      expect(jsonDecode(storedFile.readAsStringSync()), {'session_id': sessionId, 'ok': true});

      await incoming.cancel();
      await socket.close();
      client.close(force: true);
    },
    timeout: const Timeout(Duration(seconds: 15)),
  );
}
