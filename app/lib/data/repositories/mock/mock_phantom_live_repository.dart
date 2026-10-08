import 'dart:async';
import 'dart:math' as math;

import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';

/// Session id the app uses for the scripted demo (no hub needed).
const mockPhantomSessionId = 'mock-phantom-hand';

/// What the demo's audience mirror shows in the witness phase: the numbers of
/// the fixture session `contracts/fixtures/sessions/phantom_hand_min`, plus
/// flinch latency and strength. One shared object, so the mirror does not
/// restart its closing-line fade on every snapshot. The demo run has no agency
/// phase, hence no `agency_q5`.
const mockPhantomWitness = PhantomWitness(
  conditionOrder: ['async', 'sync'],
  sync: PhantomWitnessCondition(
    driftChangeCm: 2,
    flinchLatencyMs: 96,
    flinchStrength: PhantomFlinchStrength.strong,
    flinchEmgPeakX: 6.4,
    ownership: 5.5,
    control: 3,
    witnessQ4: 6,
  ),
  async: PhantomWitnessCondition(
    driftChangeCm: 0,
    flinchLatencyMs: 140,
    flinchStrength: PhantomFlinchStrength.weak,
    flinchEmgPeakX: 2.1,
    ownership: 2.5,
    control: 3,
    witnessQ4: 6,
  ),
);

/// One scripted step of the demo run.
class _Step {
  const _Step(this.phase, this.condition, this.seconds);
  final String phase;
  final PhantomCondition? condition;
  final double seconds;
}

/// Deterministic, steppable simulation of a Phantom Hand run: the phases and
/// conditions of spec §5 (short durations), `game_state`-style fields and a
/// 20 Hz synthetic EMG envelope + |accel| trace with a flinch after the stone
/// lands (bigger in SYNC than ASYNC). No timers here: call [step].
///
/// With a [demo] script it plays a recorded run instead: the recording's 14
/// phases in its order and conditions, its EMG / |accel| trace (the two flinch
/// spikes included), its stone and muscle-burst markers and its
/// `witness_summary`, in the script's compressed time.
class PhantomMockEngine {
  PhantomMockEngine({int seed = 7, this.conditionOrder = 'async_first', this.demo}) : _rand = math.Random(seed);

  final math.Random _rand;
  String conditionOrder;

  /// The recorded run to play, or null for the synthetic one.
  final PhantomDemoScript? demo;

  PhantomRunState runState = PhantomRunState.paired;
  bool hapticConnected = true;
  bool bioConnected = true;

  int _sample = 0;
  List<_Step> _plan = const [];
  int _stepIdx = -1;
  double _stepElapsed = 0;
  double _impactMs = -1;

  /// Demo only: samples played in this phase / since the start, so a phase
  /// ends on exactly the sample the script allotted it.
  int _stepSamples = 0;
  int _demoIdx = 0;

  static const _threatImpactAfterS = 1.5;
  static const _burstAfterImpactMs = 120.0;

  bool get started => _stepIdx >= 0;

  /// Session clock (ms), advancing with the 20 Hz sample counter.
  double get clockMs => _sample * 50.0;

  _Step? get _step => (_stepIdx >= 0 && _stepIdx < _plan.length) ? _plan[_stepIdx] : null;

  List<_Step> _buildPlan() {
    final script = demo;
    if (script != null) return [for (final s in script.steps) _Step(s.phase, s.condition, s.seconds)];
    final first = conditionOrder == 'sync_first' ? PhantomCondition.sync : PhantomCondition.async;
    final second = first == PhantomCondition.sync ? PhantomCondition.async : PhantomCondition.sync;
    return [
      const _Step('calibrate', null, 5),
      const _Step('probe_pre', null, 4),
      for (final c in [first, second]) ...[
        _Step('induction', c, 12),
        _Step('threat', c, 5),
        _Step('probe_post', c, 4),
        _Step('questionnaire', c, 4),
      ],
      const _Step('witness', null, 6),
      const _Step('done', null, 0),
    ];
  }

  // -- commands (mirror the headset's acceptance rules) ---------------------

  bool apply(PhantomCommand c, {Map<String, dynamic>? params}) {
    switch (c) {
      case PhantomCommand.start:
        final canStart = runState == PhantomRunState.paired || runState == PhantomRunState.ready || runState == PhantomRunState.idle;
        if (!canStart) return false;
        _plan = _buildPlan();
        _stepIdx = 0;
        _stepElapsed = 0;
        _stepSamples = 0;
        _demoIdx = 0;
        _impactMs = -1;
        runState = PhantomRunState.running;
        return true;
      case PhantomCommand.phaseNext:
      case PhantomCommand.abortPhase:
        if (runState != PhantomRunState.running) return false;
        _advanceStep();
        return true;
      case PhantomCommand.pause:
        if (runState != PhantomRunState.running) return false;
        runState = PhantomRunState.paused;
        return true;
      case PhantomCommand.resume:
        if (runState != PhantomRunState.paused) return false;
        runState = PhantomRunState.running;
        return true;
      case PhantomCommand.end:
        if (!runState.active) return false;
        runState = PhantomRunState.finished;
        _stepIdx = _plan.length - 1; // 'done'
        return true;
      case PhantomCommand.nextPerson:
        _plan = const [];
        _stepIdx = -1;
        _stepElapsed = 0;
        _stepSamples = 0;
        _demoIdx = 0;
        _impactMs = -1;
        runState = PhantomRunState.paired;
        return true;
      case PhantomCommand.conditionOrder:
        final v = params?['condition_order'];
        if (started || (v != 'sync_first' && v != 'async_first')) return false;
        conditionOrder = v as String;
        return true;
    }
  }

  void _advanceStep() {
    if (_stepIdx < _plan.length - 1) {
      _stepIdx++;
      _stepElapsed = 0;
      _stepSamples = 0;
      _impactMs = -1;
      if (_plan[_stepIdx].phase == 'done') runState = PhantomRunState.finished;
    }
  }

  // -- time -----------------------------------------------------------------

  /// Advances the simulation by [dt] and returns the snapshot, with the new
  /// trace samples (20 Hz) and the markers crossed in that interval.
  PhantomLiveSnapshot step(Duration dt) {
    final n = math.max(1, (dt.inMilliseconds / 50).round());
    final t0 = clockMs;
    final emg = <double>[];
    final acc = <double>[];
    final markers = <TraceMarker>[];
    final script = demo;
    for (var i = 0; i < n; i++) {
      if (runState == PhantomRunState.running) {
        _stepElapsed += 0.05;
        _stepSamples++;
        final s = _step;
        if (script != null) {
          // The recording's own stone and muscle burst, placed on this sample.
          final from = _demoIdx * 1000.0 / PhantomDemoScript.fsHz;
          for (final m in script.markersIn(from, from + 1000.0 / PhantomDemoScript.fsHz)) {
            markers.add(TraceMarker(kind: m.kind, tMs: clockMs + (m.tMs - from)));
          }
        } else {
          if (s != null && s.phase == 'threat' && _impactMs < 0 && _stepElapsed >= _threatImpactAfterS) {
            _impactMs = clockMs;
            markers.add(TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: _impactMs));
          }
          if (s != null && s.phase == 'threat' && _impactMs >= 0) {
            final burstAt = _impactMs + _burstAfterImpactMs;
            if (clockMs <= burstAt && burstAt < clockMs + 50) {
              markers.add(TraceMarker(kind: TraceMarkerKind.emgBurst, tMs: burstAt));
            }
          }
        }
        if (s != null && s.seconds > 0) {
          final over = script == null ? _stepElapsed >= s.seconds : _stepSamples >= (s.seconds * PhantomDemoScript.fsHz).round();
          if (over) _advanceStep();
        }
      }
      final t = clockMs;
      emg.add(script == null ? _emgAt(t) : script.emgAt(_demoIdx));
      acc.add(script == null ? _accelAt(t) : script.accelAt(_demoIdx));
      if (script != null && runState == PhantomRunState.running) _demoIdx++;
      _sample++;
    }
    return snapshot(chunk: TraceChunk(emgEnv: emg, accelMag: acc, t0Ms: t0), markers: markers);
  }

  double _flinchGain() => _step?.condition == PhantomCondition.async ? 0.55 : 1.0;

  double _emgAt(double t) {
    var v = 410 + 8 * math.sin(t / 700) + (_rand.nextDouble() - 0.5) * 6;
    if (_impactMs >= 0) {
      final b = _impactMs + _burstAfterImpactMs;
      // Sized like the real thing (rest ~420, SYNC flinch ~5x) so it shows on the fixed trace scale.
      if (t >= b) v += 1900 * _flinchGain() * math.exp(-(t - b) / 260);
    }
    return double.parse(v.toStringAsFixed(1));
  }

  double _accelAt(double t) {
    var v = 9.8 + (_rand.nextDouble() - 0.5) * 0.08;
    if (_impactMs >= 0) {
      final a = _impactMs + 60;
      if (t >= a) v += 4.5 * _flinchGain() * math.exp(-(t - a) / 140) * math.cos((t - a) / 45);
    }
    return double.parse(v.toStringAsFixed(2));
  }

  /// Snapshot of the current state (no new samples unless given).
  PhantomLiveSnapshot snapshot({TraceChunk? chunk, List<TraceMarker> markers = const []}) {
    final s = _step;
    final emgLevel = chunk == null || chunk.emgEnv.isEmpty ? 0.0 : ((chunk.emgEnv.last - 410) / 2000).clamp(0.0, 1.0);
    final level = bioConnected ? double.parse(emgLevel.toStringAsFixed(2)) : null;
    return PhantomLiveSnapshot(
      runState: runState,
      connected: true,
      game: started && s != null
          ? PhantomGameState(
              phase: s.phase,
              condition: s.condition,
              remainingS: s.seconds > 0 ? math.max(0, s.seconds - _stepElapsed) : null,
              hapticConnected: hapticConnected,
              bioConnected: bioConnected,
              emgLevel: level,
            )
          : PhantomGameState(
              phase: 'calibrate',
              condition: null,
              remainingS: null,
              hapticConnected: hapticConnected,
              bioConnected: bioConnected,
              emgLevel: level,
            ),
      chunk: chunk,
      markers: markers,
      // The headset sends its witness_summary as the witness phase begins.
      witness: s?.phase == 'witness' ? (demo?.witness ?? mockPhantomWitness) : null,
      conditionOrder: conditionOrder,
    );
  }
}

/// [PhantomLiveRepository] driving a [PhantomMockEngine] at 5 Hz while it is
/// listened to (the timer is released with the last listener, like
/// `MockLiveRepository`).
class MockPhantomLiveRepository implements PhantomLiveRepository {
  MockPhantomLiveRepository({
    PhantomMockEngine? engine,
    this.ackDelay = const Duration(milliseconds: 250),
    this.tick = const Duration(milliseconds: 200),
    this.failing = const {},
  }) : engine = engine ?? PhantomMockEngine() {
    _controller = StreamController<PhantomLiveSnapshot>.broadcast(
      onListen: () {
        _controller.add(this.engine.snapshot());
        _timer = Timer.periodic(tick, (_) => _controller.add(this.engine.step(tick)));
      },
      onCancel: () {
        _timer?.cancel();
        _timer = null;
      },
    );
  }

  final PhantomMockEngine engine;
  final Duration ackDelay;
  final Duration tick;

  /// Commands that the mock "headset" rejects (fault injection for UI tests).
  final Set<PhantomCommand> failing;

  late final StreamController<PhantomLiveSnapshot> _controller;
  Timer? _timer;

  @override
  Stream<PhantomLiveSnapshot> watch() => _controller.stream;

  @override
  Future<bool> send(PhantomCommand command, {Map<String, dynamic>? params}) async {
    if (ackDelay > Duration.zero) await Future<void>.delayed(ackDelay);
    if (failing.contains(command)) return false;
    final ok = engine.apply(command, params: params);
    if (!_controller.isClosed && _controller.hasListener) _controller.add(engine.snapshot());
    return ok;
  }
}
