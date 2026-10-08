import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/udp_beacon.dart';

typedef _Sent = ({InternetAddress to, int port, List<int> data});

/// A socket that records what is sent and can fail the way a dead one does.
/// Whatever the beacon does not use goes to [noSuchMethod], so a stray call
/// fails the test. It never touches the network.
class _FakeSocket implements RawDatagramSocket {
  /// What `send` answers for one datagram: its length when sent, 0 when not
  /// sent, or it throws.
  int Function(InternetAddress to, int length) onSend = (_, length) => length;
  final sent = <_Sent>[];
  bool closed = false;
  bool broadcast = false;

  @override
  int send(List<int> buffer, InternetAddress address, int port) {
    sent.add((to: address, port: port, data: buffer));
    return onSend(address, buffer.length);
  }

  @override
  void close() => closed = true;

  @override
  set broadcastEnabled(bool value) => broadcast = value;

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

/// Hands out a new [_FakeSocket] per bind, so a test sees every socket the
/// beacon ever used.
class _FakeBinder {
  final sockets = <_FakeSocket>[];
  int attempts = 0;

  /// The next this-many binds throw.
  int failNext = 0;

  /// While set, a bind waits for it before it answers.
  Completer<void>? gate;

  Future<RawDatagramSocket> bind() async {
    attempts++;
    await gate?.future;
    if (failNext > 0) {
      failNext--;
      throw const SocketException('bind failed');
    }
    final socket = _FakeSocket();
    sockets.add(socket);
    return socket;
  }
}

void main() {
  group('directedBroadcastsForPrefixRange', () {
    test('172.18.226.79 across /16..24 includes the real /20 broadcast and the wrong /24 guess', () {
      // Real on-device network (Opus, 2026-09-19): wlan0 172.18.226.79/20,
      // real broadcast 172.18.239.255. A naive /24 guess would compute
      // 172.18.226.255, which never reached the PC on the same /20 network.
      final result = directedBroadcastsForPrefixRange([172, 18, 226, 79], 16, 24).map((a) => a.address).toSet();

      expect(result, contains('172.18.239.255')); // real /20 broadcast
      expect(result, contains('172.18.226.255')); // old /24 guess, still included
      expect(result, contains('172.18.255.255')); // /16 broadcast
    });

    test('dedupes addresses shared across adjacent prefix lengths', () {
      // /24 and /25 that share the same fixed high bits pattern can collapse
      // to the same broadcast for some host-bit values; verify no duplicates
      // are emitted regardless.
      final result = directedBroadcastsForPrefixRange([10, 0, 0, 1], 16, 24).toList();
      final addresses = result.map((a) => a.address).toList();
      expect(addresses.toSet().length, addresses.length);
    });

    test('single prefix length matches the classic /24 computation', () {
      final result = directedBroadcastsForPrefixRange([192, 168, 1, 42], 24, 24).map((a) => a.address);
      expect(result, ['192.168.1.255']);
    });
  });

  group('UdpBeacon heals its socket', () {
    // Loopback addresses, and the sockets are fakes: nothing is ever sent.
    final a = InternetAddress('127.0.0.1');
    final b = InternetAddress('127.0.0.2');
    late _FakeBinder binder;
    late UdpBeacon beacon;

    setUp(() {
      binder = _FakeBinder();
      beacon = UdpBeacon(
        hubId: 'hub-test',
        port: 8797,
        name: 'Test Hub',
        beaconPort: 8798,
        bindSocket: binder.bind,
        targets: [a, b],
      );
      addTearDown(beacon.stop);
    });

    // start() also sends one beacon at once, which counts as tick 1. Let it
    // finish so the ticks a test drives by hand are the only ones running.
    Future<_FakeSocket> startBeacon() async {
      await beacon.start();
      await Future<void>.delayed(Duration.zero);
      return binder.sockets.single;
    }

    test('a healthy beacon sends the discovery datagram to every target on a broadcast socket', () async {
      final socket = await startBeacon();

      expect(socket.broadcast, isTrue);
      expect(socket.sent.map((s) => s.to), [a, b]);
      expect(socket.sent.map((s) => s.port).toSet(), {8798});
      expect(jsonDecode(utf8.decode(socket.sent.first.data)), {
        'opus_hub': 1,
        'hub_id': 'hub-test',
        'port': 8797,
        'name': 'Test Hub',
      });
    });

    for (final (how, onSend) in <(String, int Function(InternetAddress, int))>[
      ('returns 0', (_, _) => 0),
      ('throws', (_, _) => throw const SocketException('network is unreachable')),
    ]) {
      test('a socket whose send $how is replaced after one tick and the next tick uses the new one', () async {
        final dead = await startBeacon();
        dead.onSend = onSend; // the socket dies, as after the phone dozed

        await beacon.sendBeacon();
        expect(dead.closed, isTrue);
        expect(binder.sockets, hasLength(2));
        final fresh = binder.sockets.last;
        expect(fresh.broadcast, isTrue);

        final sentOnDead = dead.sent.length;
        await beacon.sendBeacon();
        expect(fresh.sent.map((s) => s.to), containsAll([a, b]));
        expect(dead.sent, hasLength(sentOnDead));
      });
    }

    test('a healthy socket is still replaced after beaconRebindEveryTicks ticks', () async {
      final first = await startBeacon(); // tick 1
      for (var tick = 2; tick < beaconRebindEveryTicks; tick++) {
        await beacon.sendBeacon();
      }
      expect(binder.sockets, hasLength(1), reason: 'one tick short of the limit');
      expect(first.closed, isFalse);

      await beacon.sendBeacon(); // the last tick on the first socket
      expect(binder.sockets, hasLength(2));
      expect(first.closed, isTrue);
      expect(first.sent, hasLength(beaconRebindEveryTicks * 2)); // two targets per tick

      await beacon.sendBeacon();
      expect(binder.sockets.last.sent.map((s) => s.to), containsAll([a, b]));
    });

    test('a socket that reaches at least one target is kept', () async {
      final socket = await startBeacon();
      socket.onSend = (to, length) => to == a ? length : 0; // b is a wrong directed broadcast

      for (var tick = 0; tick < 5; tick++) {
        await beacon.sendBeacon();
      }
      expect(binder.sockets, hasLength(1));
      expect(socket.closed, isFalse);
    });

    test('a bind that throws once is retried on the next tick', () async {
      final dead = await startBeacon();
      dead.onSend = (_, _) => 0;
      binder.failNext = 1;

      await beacon.sendBeacon(); // the dead socket goes, the new bind throws
      expect(dead.closed, isTrue);
      expect(binder.sockets, hasLength(1));

      await beacon.sendBeacon(); // binds again and, once it has a socket, sends at once
      expect(binder.attempts, 3);
      expect(binder.sockets, hasLength(2));
      expect(binder.sockets.last.sent.map((s) => s.to), [a, b]);
    });

    test('after stop() a tick binds no socket and sends nothing', () async {
      final socket = await startBeacon();
      socket.onSend = (_, _) => 0; // a tick would replace it
      final sentBefore = socket.sent.length;

      await beacon.stop();
      expect(socket.closed, isTrue);

      await beacon.sendBeacon();
      expect(binder.attempts, 1);
      expect(socket.sent, hasLength(sentBefore));
    });

    test('a tick that is running when stop() is called binds no socket and sends nothing afterwards', () async {
      final socket = await startBeacon();
      socket.onSend = (_, _) => 0; // this tick would replace it
      final sentBefore = socket.sent.length;

      final running = beacon.sendBeacon(); // runs up to its first await
      await beacon.stop();
      await running;

      expect(binder.attempts, 1);
      expect(socket.sent, hasLength(sentBefore));
    });

    test('a socket still binding when stop() is called is closed and never used', () async {
      final socket = await startBeacon();
      socket.onSend = (_, _) => 0;
      final gate = Completer<void>();
      binder.gate = gate;

      final running = beacon.sendBeacon();
      await Future<void>.delayed(Duration.zero); // the tick now waits for its new socket
      expect(binder.attempts, 2);

      await beacon.stop();
      gate.complete();
      await running;

      expect(binder.sockets, hasLength(2));
      expect(binder.sockets.last.closed, isTrue);
      expect(binder.sockets.last.sent, isEmpty);
    });
  });
}
