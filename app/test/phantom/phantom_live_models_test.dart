import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/phantom_live.dart';

Map<String, dynamic> _gameStateJson({
  String phase = 'induction',
  String? condition = 'sync',
  num? remaining = 41.5,
  bool haptic = true,
  bool bio = true,
  num? emg = 0.07,
}) =>
    {
      'phase': phase,
      'condition': condition,
      'remaining_s': remaining,
      'nodes': {
        'haptic': {'connected': haptic},
        'bio': {'connected': bio, 'emg_level': emg},
      },
    };

TraceChunk _chunk(double t0, int n, {double emg = 410, double acc = 9.8}) =>
    TraceChunk(emgEnv: List.filled(n, emg), accelMag: List.filled(n, acc), t0Ms: t0);

void main() {
  group('game_state parsing', () {
    test('parses the contract example', () {
      final g = PhantomGameState.tryParse(_gameStateJson())!;
      expect(g.phase, 'induction');
      expect(g.condition, PhantomCondition.sync);
      expect(g.remainingS, 41.5);
      expect(g.hapticConnected, isTrue);
      expect(g.bioConnected, isTrue);
      expect(g.emgLevel, 0.07);
    });

    test('null condition / remaining_s / emg_level are allowed', () {
      final g = PhantomGameState.tryParse(_gameStateJson(condition: null, remaining: null, emg: null))!;
      expect(g.condition, isNull);
      expect(g.remainingS, isNull);
      expect(g.emgLevel, isNull);
    });

    test('async condition and offline nodes', () {
      final g = PhantomGameState.tryParse(_gameStateJson(condition: 'async', haptic: false, bio: false))!;
      expect(g.condition, PhantomCondition.async);
      expect(g.hapticConnected, isFalse);
      expect(g.bioConnected, isFalse);
    });

    test('malformed input yields null instead of throwing', () {
      expect(PhantomGameState.tryParse(null), isNull);
      expect(PhantomGameState.tryParse('x'), isNull);
      expect(PhantomGameState.tryParse({'phase': 'induction'}), isNull);
      expect(PhantomGameState.tryParse({'phase': '', 'nodes': {'haptic': <String, dynamic>{}, 'bio': <String, dynamic>{}}}), isNull);
      expect(PhantomGameState.tryParse({'phase': 'x', 'nodes': {'haptic': <String, dynamic>{}}}), isNull);
    });
  });

  group('trace parsing', () {
    test('parses the contract example', () {
      final c = TraceChunk.tryParse({
        'emg_env': [412.5, 415.1, 418.9],
        'accel_mag': [9.8, 9.79, 9.81],
        't0_ms': 96400,
        'fs_hz': 20,
      })!;
      expect(c.emgEnv, [412.5, 415.1, 418.9]);
      expect(c.accelMag, [9.8, 9.79, 9.81]);
      expect(c.t0Ms, 96400);
      expect(c.fsHz, 20);
    });

    test('ints are accepted, non-numbers dropped, bad shapes rejected', () {
      final c = TraceChunk.tryParse({
        'emg_env': [400, 'x', 410],
        'accel_mag': <num>[],
        't0_ms': 0,
        'fs_hz': 20,
      })!;
      expect(c.emgEnv, [400.0, 410.0]);
      expect(c.accelMag, isEmpty);
      expect(TraceChunk.tryParse(null), isNull);
      expect(TraceChunk.tryParse({'emg_env': <double>[], 'accel_mag': <double>[]}), isNull);
    });
  });

  group('marker events', () {
    test('threat_impact and emg_burst become markers at t_ms, others do not', () {
      final impact = TraceMarker.fromEvent({'type': 'threat_impact', 't_ms': 1500.0});
      final burst = TraceMarker.fromEvent({'type': 'emg_burst', 't_ms': 1620});
      expect(impact!.kind, TraceMarkerKind.threatImpact);
      expect(impact.tMs, 1500);
      expect(burst!.kind, TraceMarkerKind.emgBurst);
      expect(TraceMarker.fromEvent({'type': 'stroke', 't_ms': 5}), isNull);
      expect(TraceMarker.fromEvent({'type': 'emg_burst'}), isNull);
    });
  });

  group('TraceBuffer windowing', () {
    test('concatenates successive chunks at 20 Hz', () {
      final b = TraceBuffer()
        ..addChunk(_chunk(0, 4))
        ..addChunk(_chunk(200, 4));
      expect(b.emg.length, 8);
      expect(b.emg[4].tMs, 200);
      expect(b.emg.last.tMs, 350);
      expect(b.latestMs, 350);
    });

    test('keeps only the last 10 s (201 samples, both edges inclusive)', () {
      final b = TraceBuffer();
      // 30 s of data in 100-sample (5 s) chunks
      for (var i = 0; i < 6; i++) {
        b.addChunk(_chunk(i * 5000.0, 100));
      }
      expect(b.latestMs, closeTo(29950, 1e-9));
      expect(b.emg.first.tMs, greaterThanOrEqualTo(b.latestMs! - 10000));
      expect(b.emg.length, 201);
      expect(b.accel.length, 201);
    });

    test('drops markers once they scroll out of the window', () {
      final b = TraceBuffer()
        ..addChunk(_chunk(0, 20))
        ..addMarker(const TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 500))
        ..addMarker(const TraceMarker(kind: TraceMarkerKind.emgBurst, tMs: 620));
      expect(b.markers.length, 2);
      b.addChunk(_chunk(20000, 20));
      expect(b.markers, isEmpty);
    });

    test('a replayed marker is not added twice', () {
      final b = TraceBuffer()
        ..addChunk(_chunk(0, 20))
        ..addMarker(const TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 500))
        ..addMarker(const TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 500.2));
      expect(b.markers.length, 1);
    });

    test('a chunk far in the past (new session clock) restarts the buffer', () {
      final b = TraceBuffer()..addChunk(_chunk(60000, 20));
      b.addChunk(_chunk(0, 20));
      expect(b.latestMs, 950);
      expect(b.emg.length, 20);
    });

    test('custom window', () {
      final b = TraceBuffer(windowMs: 2000);
      for (var i = 0; i < 10; i++) {
        b.addChunk(_chunk(i * 1000.0, 20));
      }
      expect(b.emg.length, 41);
    });
  });

  group('command enabling rules', () {
    PhantomCommandRules rules(
      PhantomRunState s, {
      String? phase,
      bool induction = false,
      bool connected = true,
    }) =>
        PhantomCommandRules(connected: connected, runState: s, phase: phase, inductionSeen: induction);

    Set<PhantomCommand> enabled(PhantomCommandRules r) => {
          for (final c in PhantomCommand.values)
            if (r.isEnabled(c)) c,
        };

    test('before the run: start and condition order only', () {
      expect(enabled(rules(PhantomRunState.paired, phase: 'calibrate')), {PhantomCommand.start, PhantomCommand.conditionOrder});
      expect(enabled(rules(PhantomRunState.ready)), {PhantomCommand.start, PhantomCommand.conditionOrder});
    });

    test('while running: next/abort/pause/end/next person, order locked after induction', () {
      expect(
        enabled(rules(PhantomRunState.running, phase: 'probe_pre')),
        {
          PhantomCommand.phaseNext,
          PhantomCommand.abortPhase,
          PhantomCommand.pause,
          PhantomCommand.end,
          PhantomCommand.nextPerson,
        },
      );
      expect(rules(PhantomRunState.running, phase: 'induction', induction: true).isEnabled(PhantomCommand.conditionOrder), isFalse);
    });

    test('paused: resume and end, no phase buttons', () {
      expect(
        enabled(rules(PhantomRunState.paused, phase: 'induction', induction: true)),
        {PhantomCommand.resume, PhantomCommand.end, PhantomCommand.nextPerson},
      );
    });

    test('finished or done: next person only', () {
      expect(enabled(rules(PhantomRunState.finished, phase: 'done', induction: true)), {PhantomCommand.nextPerson});
      expect(rules(PhantomRunState.running, phase: 'done', induction: true).isEnabled(PhantomCommand.phaseNext), isFalse);
    });

    test('idle is quiet except start and order', () {
      expect(enabled(rules(PhantomRunState.idle)), {PhantomCommand.start, PhantomCommand.conditionOrder});
    });

    test('nothing is enabled while disconnected', () {
      for (final s in PhantomRunState.values) {
        expect(enabled(rules(s, phase: 'induction', connected: false)), isEmpty, reason: s.name);
      }
    });

    test('the order stays locked after the first induction even once paired again', () {
      expect(rules(PhantomRunState.paired, induction: true).isEnabled(PhantomCommand.conditionOrder), isFalse);
    });

    test('wire names match the contract', () {
      expect({for (final c in PhantomCommand.values) c.wire}, {
        'start',
        'phase_next',
        'abort_phase',
        'pause',
        'resume',
        'stop',
        'next_person',
        'set_condition_order',
      });
    });
  });

  group('CommandTracker', () {
    test('sent then acked', () async {
      final tracker = CommandTracker();
      final seen = <CommandStatus?>[];
      final f = tracker.run(PhantomCommand.phaseNext, () async => true, onChange: () => seen.add(tracker.status[PhantomCommand.phaseNext]));
      expect(tracker.status[PhantomCommand.phaseNext], CommandStatus.sent);
      expect(await f, CommandStatus.acked);
      expect(seen, [CommandStatus.sent, CommandStatus.acked]);
    });

    test('a rejected ack and a thrown error both end as failed', () async {
      final tracker = CommandTracker();
      expect(await tracker.run(PhantomCommand.abortPhase, () async => false), CommandStatus.failed);
      expect(await tracker.run(PhantomCommand.pause, () async => throw StateError('socket closed')), CommandStatus.failed);
      expect(tracker.status[PhantomCommand.pause], CommandStatus.failed);
    });

    test('each command keeps its own status', () async {
      final tracker = CommandTracker();
      await tracker.run(PhantomCommand.start, () async => true);
      await tracker.run(PhantomCommand.end, () async => false);
      expect(tracker.status[PhantomCommand.start], CommandStatus.acked);
      expect(tracker.status[PhantomCommand.end], CommandStatus.failed);
    });
  });

  group('PhantomLiveModel', () {
    PhantomLiveSnapshot snap(PhantomRunState s, String phase, {TraceChunk? chunk, List<TraceMarker> markers = const []}) => PhantomLiveSnapshot(
          runState: s,
          connected: true,
          game: PhantomGameState(
            phase: phase,
            condition: null,
            remainingS: 10,
            hapticConnected: true,
            bioConnected: true,
            emgLevel: 0.1,
          ),
          chunk: chunk,
          markers: markers,
        );

    test('locks the order from the first induction and unlocks on the next person', () {
      final m = PhantomLiveModel()..apply(snap(PhantomRunState.paired, 'calibrate'));
      expect(m.rules.isEnabled(PhantomCommand.conditionOrder), isTrue);
      m.apply(snap(PhantomRunState.running, 'probe_pre'));
      expect(m.inductionSeen, isFalse);
      m.apply(snap(PhantomRunState.running, 'induction'));
      expect(m.inductionSeen, isTrue);
      m.apply(snap(PhantomRunState.finished, 'done'));
      expect(m.inductionSeen, isTrue);
      m.apply(snap(PhantomRunState.paired, 'calibrate'));
      expect(m.inductionSeen, isFalse);
      expect(m.rules.isEnabled(PhantomCommand.conditionOrder), isTrue);
    });

    test('next person during a run also clears the traces and the lock', () {
      final m = PhantomLiveModel()
        ..apply(snap(PhantomRunState.running, 'threat', chunk: _chunk(0, 20)))
        ..apply(snap(PhantomRunState.paired, 'calibrate'));
      expect(m.buffer.isEmpty, isTrue);
      expect(m.inductionSeen, isFalse);
    });

    test('feeds chunks and markers into the buffer', () {
      final m = PhantomLiveModel()
        ..apply(
          snap(
            PhantomRunState.running,
            'threat',
            chunk: _chunk(0, 20),
            markers: const [TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 400)],
          ),
        );
      expect(m.buffer.emg.length, 20);
      expect(m.buffer.markers.single.tMs, 400);
    });

    test('with no snapshot yet every command is disabled', () {
      for (final c in PhantomCommand.values) {
        expect(PhantomLiveModel().rules.isEnabled(c), isFalse);
      }
    });

    test('the reported condition order is remembered', () {
      final m = PhantomLiveModel();
      expect(m.conditionOrder, 'async_first');
      m.apply(const PhantomLiveSnapshot(runState: PhantomRunState.paired, connected: true, conditionOrder: 'sync_first'));
      expect(m.conditionOrder, 'sync_first');
    });
  });
}
