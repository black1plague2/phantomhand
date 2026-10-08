import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';

PhantomLiveModel _runUntil(PhantomMockEngine e, bool Function(PhantomLiveModel) done, {int maxSteps = 2000}) {
  final model = PhantomLiveModel()..apply(e.snapshot());
  for (var i = 0; i < maxSteps && !done(model); i++) {
    model.apply(e.step(const Duration(milliseconds: 200)));
  }
  return model;
}

void main() {
  test('is deterministic for a seed', () {
    PhantomLiveSnapshot run(int seed) {
      final e = PhantomMockEngine(seed: seed)..apply(PhantomCommand.start);
      late PhantomLiveSnapshot last;
      for (var i = 0; i < 40; i++) {
        last = e.step(const Duration(milliseconds: 200));
      }
      return last;
    }

    expect(run(3).chunk!.emgEnv, run(3).chunk!.emgEnv);
    expect(run(3).chunk!.emgEnv, isNot(run(4).chunk!.emgEnv));
  });

  test('before start: paired, no countdown, traces flow at 20 Hz', () {
    final e = PhantomMockEngine();
    final s = e.step(const Duration(milliseconds: 200));
    expect(s.runState, PhantomRunState.paired);
    expect(s.chunk!.emgEnv.length, 4);
    expect(s.chunk!.accelMag.length, 4);
    expect(s.game!.remainingS, isNull);
  });

  test('a full async_first run visits the phases in order and ends done', () {
    final e = PhantomMockEngine()..apply(PhantomCommand.start);
    final phases = <String>[];
    final conditions = <PhantomCondition?>[];
    final model = _runUntil(e, (m) {
      final g = m.latest?.game;
      if (g != null && (phases.isEmpty || phases.last != g.phase)) {
        phases.add(g.phase);
        conditions.add(g.condition);
      }
      return m.latest?.runState == PhantomRunState.finished;
    });
    expect(phases, [
      'calibrate',
      'probe_pre',
      'induction',
      'threat',
      'probe_post',
      'questionnaire',
      'induction',
      'threat',
      'probe_post',
      'questionnaire',
      'witness',
      'done',
    ]);
    expect(conditions[2], PhantomCondition.async);
    expect(conditions[6], PhantomCondition.sync);
    expect(model.latest!.runState, PhantomRunState.finished);
  });

  test('sync_first flips the order', () {
    final e = PhantomMockEngine(conditionOrder: 'sync_first')..apply(PhantomCommand.start);
    final model = _runUntil(e, (m) => m.latest?.game?.phase == 'induction');
    expect(model.latest!.game!.condition, PhantomCondition.sync);
  });

  test('the stone drop produces an impact marker then an EMG burst and a flinch in both traces', () {
    final e = PhantomMockEngine(conditionOrder: 'sync_first')..apply(PhantomCommand.start);
    final model = PhantomLiveModel(windowMs: 1e9);
    final markers = <TraceMarker>[];
    for (var i = 0; i < 400 && markers.length < 2; i++) {
      final s = e.step(const Duration(milliseconds: 200));
      markers.addAll(s.markers);
      model.apply(s);
    }
    expect(markers.map((m) => m.kind), [TraceMarkerKind.threatImpact, TraceMarkerKind.emgBurst]);
    expect(markers[1].tMs - markers[0].tMs, closeTo(120, 50));
    // keep stepping so the flinch decays into the buffer
    for (var i = 0; i < 6; i++) {
      model.apply(e.step(const Duration(milliseconds: 200)));
    }
    final baseline = model.buffer.emg.where((p) => p.tMs < markers[0].tMs).map((p) => p.value);
    final peak = model.buffer.emg.where((p) => p.tMs >= markers[0].tMs).map((p) => p.value).reduce((a, b) => a > b ? a : b);
    expect(peak, greaterThan(baseline.reduce((a, b) => a > b ? a : b) + 80));
    final accPeak = model.buffer.accel.where((p) => p.tMs >= markers[0].tMs).map((p) => p.value).reduce((a, b) => a > b ? a : b);
    expect(accPeak, greaterThan(10.5));
  });

  test('SYNC flinch is larger than ASYNC (script direction)', () {
    double peakFor(String order) {
      final e = PhantomMockEngine(conditionOrder: order)..apply(PhantomCommand.start);
      double peak = 0;
      var impacts = 0;
      for (var i = 0; i < 600 && impacts < 1; i++) {
        final s = e.step(const Duration(milliseconds: 200));
        if (s.markers.any((m) => m.kind == TraceMarkerKind.threatImpact)) impacts++;
      }
      for (var i = 0; i < 10; i++) {
        final s = e.step(const Duration(milliseconds: 200));
        for (final v in s.chunk!.emgEnv) {
          if (v > peak) peak = v;
        }
      }
      return peak;
    }

    expect(peakFor('sync_first'), greaterThan(peakFor('async_first')));
  });

  group('commands', () {
    test('start only from paired; pause and resume; end finishes', () {
      final e = PhantomMockEngine();
      expect(e.apply(PhantomCommand.pause), isFalse);
      expect(e.apply(PhantomCommand.start), isTrue);
      expect(e.apply(PhantomCommand.start), isFalse);
      expect(e.apply(PhantomCommand.pause), isTrue);
      expect(e.runState, PhantomRunState.paused);
      final before = e.step(const Duration(milliseconds: 200)).game!.remainingS;
      final after = e.step(const Duration(milliseconds: 200)).game!.remainingS;
      expect(after, before, reason: 'the countdown holds while paused');
      expect(e.apply(PhantomCommand.resume), isTrue);
      expect(e.apply(PhantomCommand.end), isTrue);
      expect(e.runState, PhantomRunState.finished);
      expect(e.snapshot().game!.phase, 'done');
    });

    test('phase_next and abort_phase jump to the next phase; both need a running run', () {
      final e = PhantomMockEngine();
      expect(e.apply(PhantomCommand.phaseNext), isFalse);
      e.apply(PhantomCommand.start);
      expect(e.snapshot().game!.phase, 'calibrate');
      expect(e.apply(PhantomCommand.phaseNext), isTrue);
      expect(e.snapshot().game!.phase, 'probe_pre');
      expect(e.apply(PhantomCommand.abortPhase), isTrue);
      expect(e.snapshot().game!.phase, 'induction');
    });

    test('set_condition_order works only before the run and validates the value', () {
      final e = PhantomMockEngine();
      expect(e.apply(PhantomCommand.conditionOrder, params: {'condition_order': 'bogus'}), isFalse);
      expect(e.apply(PhantomCommand.conditionOrder, params: {'condition_order': 'sync_first'}), isTrue);
      expect(e.snapshot().conditionOrder, 'sync_first');
      e.apply(PhantomCommand.start);
      expect(e.apply(PhantomCommand.conditionOrder, params: {'condition_order': 'async_first'}), isFalse);
    });

    test('next_person resets the run', () {
      final e = PhantomMockEngine()..apply(PhantomCommand.start);
      e.step(const Duration(seconds: 2));
      expect(e.apply(PhantomCommand.nextPerson), isTrue);
      expect(e.runState, PhantomRunState.paired);
      expect(e.started, isFalse);
      expect(e.apply(PhantomCommand.conditionOrder, params: {'condition_order': 'sync_first'}), isTrue);
    });
  });

  group('MockPhantomLiveRepository', () {
    test('streams while listened to and releases its timer on cancel', () async {
      final repo = MockPhantomLiveRepository(ackDelay: Duration.zero, tick: const Duration(milliseconds: 20));
      final seen = <PhantomLiveSnapshot>[];
      final sub = repo.watch().listen(seen.add);
      await Future<void>.delayed(const Duration(milliseconds: 120));
      await sub.cancel();
      final n = seen.length;
      expect(n, greaterThan(2));
      await Future<void>.delayed(const Duration(milliseconds: 80));
      expect(seen.length, n, reason: 'no events after cancel');
    });

    test('send applies the command and reports the engine verdict; failing commands are rejected', () async {
      final repo = MockPhantomLiveRepository(ackDelay: Duration.zero, failing: {PhantomCommand.abortPhase});
      expect(await repo.send(PhantomCommand.start), isTrue);
      expect(await repo.send(PhantomCommand.start), isFalse);
      expect(await repo.send(PhantomCommand.abortPhase), isFalse);
      expect(repo.engine.runState, PhantomRunState.running);
    });
  });
}
