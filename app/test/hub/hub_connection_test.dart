import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/live_message.dart';

import 'fake_live_socket.dart';

void main() {
  group('HubConnection protocol engine (in-process fake socket, no real I/O)', () {
    late FakeLiveSocket fake;
    late HubConnection conn;

    setUp(() {
      fake = FakeLiveSocket();
      conn = HubConnection(
        deviceId: 'headset-1',
        socket: fake,
        pairToken: '123456',
        incoming: fake.incoming,
        ackTimeout: const Duration(milliseconds: 50),
        pingInterval: const Duration(hours: 1), // disabled for these tests
      );
    });

    tearDown(() => conn.close());

    test('requires_ack message is retried after ackTimeout if unacked', () async {
      conn.send(LiveMessage.command(conn.nextSeq, sessionId: 's1', command: 'start'));
      expect(fake.sent, hasLength(1));
      // Poll on wall-clock time rather than a single fixed delay: under a
      // loaded test-runner (full `flutter test` in parallel), a bare
      // `Future.delayed(70ms)` can fire before the 50ms ackTimeout Timer gets
      // scheduled, making this flaky (seen in run12's baseline). Poll up to a
      // generous ceiling and return as soon as the retry lands, so the test
      // is only as slow as it needs to be and isn't racing a fixed margin.
      final deadline = DateTime.now().add(const Duration(seconds: 2));
      while (fake.sent.length < 2 && DateTime.now().isBefore(deadline)) {
        await Future<void>.delayed(const Duration(milliseconds: 10));
      }
      expect(fake.sent, hasLength(2)); // retried once
      expect(fake.sent[0]['id'], fake.sent[1]['id']); // same message id
    });

    test('ack stops the retry', () async {
      final msg = LiveMessage.command(conn.nextSeq, sessionId: 's1', command: 'start');
      conn.send(msg);
      fake.receive(headsetMessage(type: 'ack', id: 'ack-1', seq: 0, payload: {'ack_id': msg.id, 'ok': true}));
      await Future<void>.delayed(const Duration(milliseconds: 70));
      expect(fake.sent, hasLength(1)); // no retry once acked
    });

    test('retries stop after 3 attempts', () async {
      conn.send(LiveMessage.command(conn.nextSeq, sessionId: 's1', command: 'start'));
      await Future<void>.delayed(const Duration(milliseconds: 250));
      // Initial send + up to 3 retries = 4 total.
      expect(fake.sent.length, lessThanOrEqualTo(4));
      expect(fake.sent.length, greaterThanOrEqualTo(4));
    });

    test('ping/pong tracks RTT', () async {
      expect(conn.lastRtt, isNull);
      // Manually trigger the ping/pong exchange the periodic timer would do.
      conn.send(LiveMessage.ping(conn.nextSeq));
      final echoTs = fake.sent.last['payload']['echo_ts_ms'] as double;
      await Future<void>.delayed(const Duration(milliseconds: 5));
      fake.receive(headsetMessage(type: 'pong', id: 'pong-1', seq: 0, payload: {'echo_ts_ms': echoTs}));
      await Future<void>.delayed(Duration.zero);
      expect(conn.lastRtt, isNotNull);
      expect(conn.lastRtt!.inMilliseconds, greaterThanOrEqualTo(0));
    });

    test('inbound duplicate id is processed only once (de-dup)', () async {
      var statusCount = 0;
      conn.statusStream.listen((_) => statusCount++);
      final msg = headsetMessage(type: 'status', id: 'dup-1', seq: 0, payload: {'state': 'running'});
      fake.receive(msg);
      fake.receive(msg); // exact same id again
      await Future<void>.delayed(Duration.zero);
      expect(statusCount, 1);
    });

    test('invalid inbound message gets an error reply, not a crash', () async {
      fake.receive({'v': 1, 'type': 'status', 'id': 'bad-1', 'seq': 0, 'ts_ms': 0, 'from': 'headset', 'payload': <String, dynamic>{}});
      await Future<void>.delayed(Duration.zero);
      expect(fake.sent, hasLength(1));
      expect(fake.sent.first['type'], 'error');
      expect(fake.sent.first['payload']['code'], 'invalid_message');
    });

    test('malformed (non-JSON) frame is ignored, not a crash', () async {
      fake.receiveRaw('not json{{{');
      await Future<void>.delayed(Duration.zero);
      expect(fake.sent, isEmpty);
    });

    test('trial_event and metrics_tick payloads are exposed on their streams', () async {
      final events = <Map<String, dynamic>>[];
      final metrics = <Map<String, dynamic>>[];
      conn.trialEventStream.listen(events.add);
      conn.metricsStream.listen(metrics.add);

      fake.receive(headsetMessage(
        type: 'trial_event',
        id: 'e1',
        seq: 0,
        payload: {'type': 'trial_start', 'trial': 0, 'block': 0, 't_ms': 0.0, 'seq': 0},
      ));
      fake.receive(headsetMessage(
        type: 'metrics_tick',
        id: 'm1',
        seq: 1,
        payload: {'window_trials': 1, 'metrics': <String, dynamic>{}},
      ));
      await Future<void>.delayed(Duration.zero);
      expect(events, hasLength(1));
      expect(metrics, hasLength(1));
    });

    test('messageLog emits one entry per inbound message with type/seq/a non-negative latency', () async {
      final log = <InboundMessageLog>[];
      conn.messageLog.listen(log.add);

      fake.receive(headsetMessage(type: 'status', id: 's1', seq: 5, payload: {'state': 'running'}));
      fake.receive(headsetMessage(
        type: 'trial_event',
        id: 'e1',
        seq: 6,
        payload: {'type': 'trial_start', 'trial': 0, 'block': 0, 't_ms': 0.0, 'seq': 0},
      ));
      await Future<void>.delayed(Duration.zero);

      expect(log, hasLength(2));
      expect(log[0].type, 'status');
      expect(log[0].seq, 5);
      expect(log[0].latencyMs, greaterThanOrEqualTo(0));
      expect(log[1].type, 'trial_event');
      expect(log[1].seq, 6);
    });

    test('resume: replaySince replays only messages after the given seq', () {
      conn
        ..send(LiveMessage.command(conn.nextSeq, sessionId: 's1', command: 'start')) // seq 0
        ..send(LiveMessage.command(conn.nextSeq, sessionId: 's1', command: 'pause')) // seq 1
        ..send(LiveMessage.command(conn.nextSeq, sessionId: 's1', command: 'resume')); // seq 2
      fake.sent.clear();
      conn.replaySince(0);
      expect(fake.sent, hasLength(2)); // seq 1 and 2 only
      expect(fake.sent.map((m) => m['payload']['command']), ['pause', 'resume']);
    });

    test("seedHistory lets a fresh connection object replay a previous connection's history", () {
      final old = HubConnection(
        deviceId: 'headset-1',
        socket: FakeLiveSocket(),
        pairToken: '123456',
        incoming: const Stream.empty(),
        pingInterval: const Duration(hours: 1),
      )..send(LiveMessage.command(0, sessionId: 's1', command: 'start'));
      conn.seedHistory(old.sentHistory);
      fake.sent.clear();
      conn.replaySince(-1);
      expect(fake.sent, hasLength(1));
      expect(fake.sent.first['payload']['command'], 'start');
    });
  });

  group('validateLiveMessage', () {
    test('rejects a message missing required top-level fields', () {
      expect(validateLiveMessage({}), isNotNull);
      expect(validateLiveMessage({'v': 1, 'type': 'ping'}), isNotNull);
    });

    test('accepts a minimal valid hello', () {
      expect(
        validateLiveMessage({
          'v': 1,
          'type': 'hello',
          'id': 'x',
          'seq': 0,
          'ts_ms': 0,
          'from': 'headset',
          'payload': {'device_id': 'd1', 'role': 'headset', 'versions': <String, dynamic>{}},
        }),
        isNull,
      );
    });

    test('rejects hello missing payload.device_id', () {
      expect(
        validateLiveMessage({
          'v': 1,
          'type': 'hello',
          'id': 'x',
          'seq': 0,
          'ts_ms': 0,
          'from': 'headset',
          'payload': {'role': 'headset', 'versions': <String, dynamic>{}},
        }),
        isNotNull,
      );
    });

    test('rejects an unknown message type', () {
      expect(
        validateLiveMessage({'v': 1, 'type': 'bogus', 'id': 'x', 'seq': 0, 'ts_ms': 0, 'from': 'headset'}),
        isNotNull,
      );
    });

    test('rejects file_available with a disallowed file name', () {
      expect(
        validateLiveMessage({
          'v': 1,
          'type': 'file_available',
          'id': 'x',
          'seq': 0,
          'ts_ms': 0,
          'from': 'headset',
          'payload': {'name': 'evil.exe', 'bytes': 1, 'sha256': 'a'},
        }),
        isNotNull,
      );
    });
  });
}
