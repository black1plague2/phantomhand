// B12: what `HubPhantomLiveRepository` stamps on its snapshots so the card can
// show link health: the time the latest status arrived, and the measured round
// trip. A real `HubConnection` over a fake socket, real (short) delays.
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/live_message.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/hub/hub_phantom_live_repository.dart';

import '../hub/fake_live_socket.dart';

const Map<String, dynamic> _status = {
  'state': 'running',
  'game_id': 'phantom_hand',
  'game_state': {
    'phase': 'induction',
    'condition': 'sync',
    'remaining_s': 41.5,
    'nodes': {
      'haptic': {'connected': true},
      'bio': {'connected': true, 'emg_level': 0.1},
    },
  },
};

Future<void> _settle() => Future<void>.delayed(const Duration(milliseconds: 25));

void main() {
  late FakeLiveSocket socket;
  late HubConnection conn;
  late HubPhantomLiveRepository repo;

  setUp(() {
    socket = FakeLiveSocket();
    conn = HubConnection(
      deviceId: 'quest-1',
      socket: socket,
      pairToken: '123456',
      incoming: socket.incoming,
      pingInterval: const Duration(hours: 1),
    );
    repo = HubPhantomLiveRepository(conn);
  });

  tearDown(() async {
    await repo.dispose();
    await conn.close();
  });

  test('a snapshot carries when its status arrived and the measured round trip; an event does not move the time', () async {
    final seen = <PhantomLiveSnapshot>[];
    final sub = repo.watch().listen(seen.add);

    // One ping / pong exchange gives the connection a round trip.
    conn.send(LiveMessage.ping(conn.nextSeq));
    final echo = (socket.sent.last['payload'] as Map)['echo_ts_ms'];
    await _settle();
    socket.receive(headsetMessage(type: 'pong', id: 'pong-1', seq: 0, payload: {'echo_ts_ms': echo}));
    await _settle();
    expect(conn.lastRtt, isNotNull);

    final before = DateTime.now();
    socket.receive(headsetMessage(type: 'status', id: 'status-1', seq: 1, payload: _status));
    await _settle();
    final status = seen.lastWhere((s) => s.game != null);
    expect(status.rttMs, conn.lastRtt!.inMilliseconds);
    expect(status.receivedAt, isNotNull);
    expect(status.receivedAt!.isBefore(before), isFalse);

    // Later, a trial event: the numbers on the card are not any fresher.
    await Future<void>.delayed(const Duration(milliseconds: 80));
    socket.receive(headsetMessage(type: 'trial_event', id: 'event-1', seq: 2, payload: {'type': 'threat_impact', 't_ms': 400.0}));
    await _settle();
    final event = seen.last;
    expect(event.markers, isNotEmpty);
    expect(event.receivedAt, status.receivedAt);

    // The next status moves it.
    socket.receive(headsetMessage(type: 'status', id: 'status-2', seq: 3, payload: _status));
    await _settle();
    expect(seen.last.receivedAt!.isAfter(status.receivedAt!), isTrue);
    await sub.cancel();
  });

  test('a card opened onto a silent link gets the replayed last status stamped as just received, so it turns stale within seconds', () async {
    socket.receive(headsetMessage(type: 'status', id: 'status-1', seq: 0, payload: _status));
    await _settle();
    expect(conn.lastStatus, isNotNull);

    final before = DateTime.now();
    final seen = <PhantomLiveSnapshot>[];
    final sub = repo.watch().listen(seen.add);
    await _settle();
    expect(seen.first.game?.phase, 'induction');
    expect(seen.first.receivedAt!.isBefore(before), isFalse);
    await sub.cancel();
  });

  test('before any round trip the snapshot has no rtt; a dropped link is reported as not connected', () async {
    final seen = <PhantomLiveSnapshot>[];
    final sub = repo.watch().listen(seen.add);
    socket.receive(headsetMessage(type: 'status', id: 'status-1', seq: 0, payload: _status));
    await _settle();
    expect(seen.last.rttMs, isNull);
    expect(PhantomLinkHealth.of(seen.last, DateTime.now()).state, PhantomLinkState.live);

    await conn.close();
    await _settle();
    expect(seen.last.connected, isFalse);
    expect(PhantomLinkHealth.of(seen.last, DateTime.now()).state, PhantomLinkState.offline);
    await sub.cancel();
  });
}
