// A1: the real hub stores a Phantom Hand session (status.game_state +
// status.trace accepted, sens_###.json uploaded) and carries the new operator
// commands. Port 8797 (8787 is reserved for Unity live tests).
import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/hub/hub_phantom_live_repository.dart';
import 'package:uuid/uuid.dart';

const _uuid = Uuid();
const _port = 8797;

Map<String, dynamic> _msg(String type, int seq, Map<String, dynamic> payload) => {
      'v': 1,
      'type': type,
      'id': _uuid.v4(),
      'seq': seq,
      'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
      'from': 'headset',
      'payload': payload,
    };

Map<String, dynamic> _status({Map<String, dynamic>? gameState, Map<String, dynamic>? trace}) => {
      'state': 'running',
      'game_state': gameState ??
          {
            'phase': 'induction',
            'condition': 'sync',
            'remaining_s': 41.5,
            'nodes': {
              'haptic': {'connected': true},
              'bio': {'connected': true, 'emg_level': 0.12},
            },
          },
      'trace': trace ??
          {
            'emg_env': List<double>.filled(20, 400),
            'accel_mag': List<double>.filled(20, 9.8),
            't0_ms': 0,
            'fs_hz': 20,
          },
    };

void main() {
  late Directory dataDir;
  late HubServer server;
  late WebSocket socket;
  late StreamIterator<dynamic> incoming;

  Future<Map<String, dynamic>> next() async {
    expect(await incoming.moveNext().timeout(const Duration(seconds: 5)), isTrue);
    return jsonDecode(incoming.current as String) as Map<String, dynamic>;
  }

  /// Next frame that is not a ping.
  Future<Map<String, dynamic>> nextReal() async {
    while (true) {
      final m = await next();
      if (m['type'] != 'ping') return m;
    }
  }

  setUp(() async {
    dataDir = Directory.systemTemp.createTempSync('opus_ph_hub_');
    server = HubServer(sessionsDir: dataDir.path, port: _port);
    await server.start();
    socket = await WebSocket.connect('ws://127.0.0.1:$_port/opus/v1/live');
    incoming = StreamIterator(socket);
    socket.add(
      jsonEncode(
        _msg('hello', 0, {
          'device_id': 'ph-headset',
          'role': 'headset',
          'versions': {'shell': '0.0.0-test'},
        }),
      ),
    );
    expect((await nextReal())['type'], 'hello_ack');
  });

  tearDown(() async {
    await incoming.cancel();
    await socket.close();
    await server.stop();
    dataDir.deleteSync(recursive: true);
  });

  test('status with game_state + trace reaches the repository as a snapshot; markers follow trial events', () async {
    final conn = server.connections.firstWhere((c) => c.deviceId == 'ph-headset');
    final repo = HubPhantomLiveRepository(conn);
    final snaps = <PhantomLiveSnapshot>[];
    final sub = repo.watch().listen(snaps.add);

    socket
      ..add(jsonEncode(_msg('status', 1, _status())))
      ..add(jsonEncode(_msg('trial_event', 2, {'type': 'threat_impact', 'trial': 0, 'block': 0, 't_ms': 400.0, 'seq': 0})));
    await Future<void>.delayed(const Duration(milliseconds: 200));

    final withGame = snaps.where((s) => s.game != null && s.chunk != null).toList();
    expect(withGame, isNotEmpty);
    final s = withGame.last;
    expect(s.game!.phase, 'induction');
    expect(s.game!.condition, PhantomCondition.sync);
    expect(s.game!.hapticConnected, isTrue);
    expect(s.game!.bioConnected, isTrue);
    expect(s.chunk!.emgEnv, hasLength(20));
    expect(snaps.any((x) => x.markers.any((m) => m.kind == TraceMarkerKind.threatImpact)), isTrue);
    await sub.cancel();
    await repo.dispose();
  });

  test('GET /opus/v1/live/last_status relays the latest status + recent events (empty before any)', () async {
    final client = HttpClient();
    Future<Map<String, dynamic>> get() async {
      final req = await client.getUrl(Uri.parse('http://127.0.0.1:$_port/opus/v1/live/last_status'));
      final resp = await req.close();
      expect(resp.statusCode, HttpStatus.ok);
      return jsonDecode(await utf8.decodeStream(resp)) as Map<String, dynamic>;
    }

    final empty = await get();
    expect(empty['status'], isNull);
    expect(empty['events'], isEmpty);

    socket
      ..add(jsonEncode(_msg('status', 1, _status())))
      ..add(jsonEncode(_msg('trial_event', 2, {'type': 'threat_impact', 'trial': 0, 'block': 0, 't_ms': 400.0, 'seq': 0})));
    await Future<void>.delayed(const Duration(milliseconds: 200));

    final doc = await get();
    final st = doc['status'] as Map<String, dynamic>;
    expect((st['game_state'] as Map)['condition'], 'sync');
    expect((st['trace'] as Map)['emg_env'], hasLength(20));
    expect(st['_recv_ms'], isA<num>());
    final evs = doc['events'] as List;
    expect(evs, hasLength(1));
    expect((evs.first as Map)['type'], 'threat_impact');
    expect((evs.first as Map)['seq'], 0);
    client.close(force: true);
  });

  test('a status with a malformed trace or game_state is rejected with invalid_message', () async {
    socket.add(
      jsonEncode(
        _msg('status', 1, _status(trace: {'emg_env': <double>[], 'accel_mag': <double>[], 't0_ms': 0, 'fs_hz': 50})),
      ),
    );
    final err = await nextReal();
    expect(err['type'], 'error');
    expect((err['payload'] as Map)['code'], 'invalid_message');

    socket.add(
      jsonEncode(
        _msg('status', 2, _status(gameState: {'phase': 'x', 'condition': 'both', 'remaining_s': null, 'nodes': <String, dynamic>{}})),
      ),
    );
    final err2 = await nextReal();
    expect((err2['payload'] as Map)['code'], 'invalid_message');
  });

  test('operator commands phase_next / abort_phase / next_person / set_condition_order go out and are acked', () async {
    final conn = server.connections.firstWhere((c) => c.deviceId == 'ph-headset');
    final repo = HubPhantomLiveRepository(conn);
    final sub = repo.watch().listen((_) {});
    final expected = {
      PhantomCommand.phaseNext: 'phase_next',
      PhantomCommand.abortPhase: 'abort_phase',
      PhantomCommand.nextPerson: 'next_person',
      PhantomCommand.conditionOrder: 'set_condition_order',
    };
    var seq = 10;
    for (final e in expected.entries) {
      final isOrder = e.key == PhantomCommand.conditionOrder;
      final result = repo.send(e.key, params: isOrder ? {'condition_order': 'sync_first'} : null);
      final m = await nextReal();
      expect(m['type'], 'command');
      expect((m['payload'] as Map)['command'], e.value);
      if (isOrder) {
        expect(((m['payload'] as Map)['params'] as Map)['condition_order'], 'sync_first');
      }
      socket.add(
        jsonEncode(_msg('ack', seq++, {'ack_id': m['id'], 'ok': true, 'recv_ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble()})),
      );
      expect(await result.timeout(const Duration(seconds: 5)), isTrue, reason: e.value);
    }
    await sub.cancel();
    await repo.dispose();
  });

  test('a phantom_hand session with sens chunks is stored on disk (201 new, 200 identical, bad name 400)', () async {
    const sessionId = 'ph-session-1';
    final client = HttpClient();
    Future<int> put(String name, List<int> bytes) async {
      final req = await client.putUrl(Uri.parse('http://127.0.0.1:$_port/opus/v1/sessions/$sessionId/files/$name'));
      final resp = await (req..add(bytes)).close();
      await resp.drain<void>();
      return resp.statusCode;
    }

    final session = utf8.encode(jsonEncode({'session_id': sessionId, 'game_id': 'phantom_hand'}));
    final sens0 = utf8.encode(jsonEncode({'node': 'bio', 'chunk': 0, 't0_ms': 0, 'samples': <dynamic>[]}));
    final sens1 = utf8.encode(jsonEncode({'node': 'bio', 'chunk': 1, 't0_ms': 1000, 'samples': <dynamic>[]}));
    expect(await put('session.json', session), HttpStatus.created);
    expect(await put('sens_000.json', sens0), HttpStatus.created);
    expect(await put('sens_001.json', sens1), HttpStatus.created);
    expect(await put('sens_000.json', sens0), HttpStatus.ok, reason: 'idempotent by sha256');
    expect(await put('sens_1.json', sens0), HttpStatus.badRequest, reason: 'three digits required');
    expect(await put('sens_000.csv', sens0), HttpStatus.badRequest);

    final dir = Directory('${dataDir.path}/$sessionId');
    expect(File('${dir.path}/sens_000.json').existsSync(), isTrue);
    expect(File('${dir.path}/sens_001.json').existsSync(), isTrue);
    expect(File('${dir.path}/sens_1.json').existsSync(), isFalse);
    client.close(force: true);
  });

  test('file_available for a sens chunk is accepted; a malformed name is rejected', () async {
    final ok = {'session_id': 's', 'name': 'sens_002.json', 'bytes': 12, 'sha256': 'ab' * 32};
    socket
      ..add(jsonEncode(_msg('file_available', 1, ok)))
      ..add(jsonEncode(_msg('file_available', 2, {...ok, 'name': 'sens_2.json'})));
    final err = await nextReal();
    expect(err['type'], 'error', reason: 'only the malformed name is rejected');
    expect((err['payload'] as Map)['code'], 'invalid_message');
  });
}
