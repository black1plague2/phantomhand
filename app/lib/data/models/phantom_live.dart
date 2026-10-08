import 'dart:math' as math;

/// Live-session models for the generic "game-state" operator card
/// (FR-AP-01, `docs/agent-briefs/ph/03-SPEC.md` §4/§9, `contracts/LIVE_PROTOCOL.md`
/// v0.2): `status.payload.game_state`, `status.payload.trace`, the operator
/// commands and the rules that enable them. Pure Dart (no Flutter, no I/O) so
/// every rule is unit-testable. Nothing here is specific to one game's
/// *parameters*; the phase ids are the ones the card knows how to label.

/// `status.payload.state` of the live-message schema.
enum PhantomRunState {
  idle,
  paired,
  calibrating,
  ready,
  running,
  paused,
  rest,
  finished,
  error;

  static PhantomRunState parse(Object? raw) {
    for (final v in PhantomRunState.values) {
      if (v.name == raw) return v;
    }
    return PhantomRunState.idle;
  }

  /// A run is in progress on the headset.
  bool get active => this == running || this == paused || this == rest;
}

/// The two Phantom Hand conditions (`status.game_state.condition`).
enum PhantomCondition {
  sync,
  async;

  static PhantomCondition? parse(Object? raw) {
    if (raw == 'sync') return PhantomCondition.sync;
    if (raw == 'async') return PhantomCondition.async;
    return null;
  }
}

/// Phase ids used by the headset (spec v3.1). Anything else is shown as given.
const phantomPhaseIds = [
  'calibrate',
  'probe_pre',
  'induction',
  'self_touch',
  'agency',
  'threat',
  'probe_post',
  'questionnaire',
  'dissolve',
  'reveal',
  'witness',
  'done',
];

/// Phases at or after which the first induction has started. The condition
/// order can no longer be changed from the first induction on.
const _preInductionPhases = {'calibrate', 'probe_pre'};

/// `status.payload.game_state` (all four keys are required by the schema).
class PhantomGameState {
  const new({
    required this.phase,
    required this.condition,
    required this.remainingS,
    required this.hapticConnected,
    required this.bioConnected,
    required this.emgLevel,
  });

  final String phase;
  final PhantomCondition? condition;
  final double? remainingS;
  final bool hapticConnected;
  final bool bioConnected;

  /// Normalised EMG (0 = rest, 1 = MVC), or null.
  final double? emgLevel;

  /// Parses a `game_state` map; `null` when it is absent or malformed.
  static PhantomGameState? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final phase = raw['phase'];
    final nodes = raw['nodes'];
    if (phase is! String || phase.isEmpty || nodes is! Map) return null;
    final haptic = nodes['haptic'];
    final bio = nodes['bio'];
    if (haptic is! Map || bio is! Map) return null;
    final remaining = raw['remaining_s'];
    final emg = bio['emg_level'];
    return PhantomGameState(
      phase: phase,
      condition: PhantomCondition.parse(raw['condition']),
      remainingS: remaining is num ? remaining.toDouble() : null,
      hapticConnected: haptic['connected'] == true,
      bioConnected: bio['connected'] == true,
      emgLevel: emg is num ? emg.toDouble() : null,
    );
  }
}

/// `status.payload.trace`: newest samples since the previous status.
class TraceChunk {
  const new({required this.emgEnv, required this.accelMag, required this.t0Ms, this.fsHz = 20});

  final List<double> emgEnv;
  final List<double> accelMag;
  final double t0Ms;
  final int fsHz;

  static TraceChunk? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final t0 = raw['t0_ms'];
    final emg = raw['emg_env'];
    final acc = raw['accel_mag'];
    if (t0 is! num || emg is! List || acc is! List) return null;
    final fs = raw['fs_hz'];
    return TraceChunk(
      emgEnv: [for (final v in emg) if (v is num) v.toDouble()],
      accelMag: [for (final v in acc) if (v is num) v.toDouble()],
      t0Ms: t0.toDouble(),
      fsHz: fs is num && fs > 0 ? fs.toInt() : 20,
    );
  }
}

/// Events drawn as vertical markers on the traces.
enum TraceMarkerKind { threatImpact, emgBurst }

class TraceMarker {
  const new({required this.kind, required this.tMs});
  final TraceMarkerKind kind;
  final double tMs;

  /// `trial_event` payload (event schema: `t_ms`, `type`) -> marker, or null for
  /// any other event type.
  static TraceMarker? fromEvent(Map<String, dynamic> payload) {
    final type = payload['type'];
    final t = payload['t_ms'];
    if (t is! num) return null;
    return switch (type) {
      'threat_impact' => TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: t.toDouble()),
      'emg_burst' => TraceMarker(kind: TraceMarkerKind.emgBurst, tMs: t.toDouble()),
      _ => null,
    };
  }
}

/// One drawn sample.
class TracePoint {
  const new(this.tMs, this.value);
  final double tMs;
  final double value;
}

/// Rolling window over successive [TraceChunk]s ("The app concatenates
/// successive traces"). Keeps only the last [windowMs] (default 10 s) before
/// the newest sample; markers older than the window are dropped with them.
class TraceBuffer {
  TraceBuffer({this.windowMs = 10000});

  final double windowMs;
  final List<TracePoint> _emg = [];
  final List<TracePoint> _accel = [];
  final List<TraceMarker> _markers = [];
  double? _latestMs;

  List<TracePoint> get emg => List.unmodifiable(_emg);
  List<TracePoint> get accel => List.unmodifiable(_accel);
  List<TraceMarker> get markers => List.unmodifiable(_markers);

  /// Session time of the newest sample, or null when empty.
  double? get latestMs => _latestMs;

  bool get isEmpty => _emg.isEmpty && _accel.isEmpty;

  /// Window start (ms): [windowMs] before the newest sample.
  double get windowStartMs => (_latestMs ?? 0) - windowMs;

  void addChunk(TraceChunk chunk) {
    final dt = 1000.0 / chunk.fsHz;
    // A chunk that starts before the newest sample we already have is a
    // restart (new session / `next_person` resets the session clock).
    final latest = _latestMs;
    if (latest != null && chunk.t0Ms + dt < latest - windowMs) clear();
    for (var i = 0; i < chunk.emgEnv.length; i++) {
      _emg.add(TracePoint(chunk.t0Ms + i * dt, chunk.emgEnv[i]));
    }
    for (var i = 0; i < chunk.accelMag.length; i++) {
      _accel.add(TracePoint(chunk.t0Ms + i * dt, chunk.accelMag[i]));
    }
    final newest = math.max(
      chunk.emgEnv.isEmpty ? -1.0 : chunk.t0Ms + (chunk.emgEnv.length - 1) * dt,
      chunk.accelMag.isEmpty ? -1.0 : chunk.t0Ms + (chunk.accelMag.length - 1) * dt,
    );
    if (newest >= 0) _latestMs = _latestMs == null ? newest : math.max(_latestMs!, newest);
    _trim();
  }

  void addMarker(TraceMarker marker) {
    // The same event can arrive twice after a reconnect replay; de-dup.
    if (_markers.any((m) => m.kind == marker.kind && (m.tMs - marker.tMs).abs() < 1)) return;
    _markers.add(marker);
    _latestMs ??= marker.tMs;
    _trim();
  }

  void clear() {
    _emg.clear();
    _accel.clear();
    _markers.clear();
    _latestMs = null;
  }

  void _trim() {
    final start = windowStartMs;
    _emg.removeWhere((p) => p.tMs < start);
    _accel.removeWhere((p) => p.tMs < start);
    _markers.removeWhere((m) => m.tMs < start);
  }
}

/// What the live card renders: the latest headset state plus connection facts.
class PhantomLiveSnapshot {
  const new({
    required this.runState,
    required this.connected,
    this.game,
    this.chunk,
    this.markers = const [],
    this.conditionOrder,
  });

  final PhantomRunState runState;

  /// The hub connection (or the mock) is up.
  final bool connected;
  final PhantomGameState? game;

  /// New trace samples carried by this status (null: none).
  final TraceChunk? chunk;

  /// Markers that arrived with this snapshot.
  final List<TraceMarker> markers;

  /// The order the headset reports for the next run, if known.
  final String? conditionOrder;
}

/// Operator buttons of the live card.
enum PhantomCommand {
  start('start'),
  phaseNext('phase_next'),
  abortPhase('abort_phase'),
  pause('pause'),
  resume('resume'),
  end('stop'),
  nextPerson('next_person'),
  conditionOrder('set_condition_order');

  const new(this.wire);

  /// The `command.payload.command` value on the wire.
  final String wire;
}

/// Which operator buttons are enabled right now (FR-AP-01 item 4).
///
/// [inductionSeen] is true once any phase after calibrate/probe_pre has been
/// reported in this run: from then on the condition order is locked
/// (`set_condition_order` is rejected by the headset once a run has started).
class PhantomCommandRules {
  const new({
    required this.connected,
    required this.runState,
    required this.phase,
    required this.inductionSeen,
  });

  final bool connected;
  final PhantomRunState runState;
  final String? phase;
  final bool inductionSeen;

  bool get _phaseDone => phase == 'done';

  bool isEnabled(PhantomCommand c) {
    if (!connected) return false;
    switch (c) {
      case PhantomCommand.start:
        return runState == PhantomRunState.idle || runState == PhantomRunState.paired || runState == PhantomRunState.ready;
      case PhantomCommand.phaseNext:
      case PhantomCommand.abortPhase:
        return runState == PhantomRunState.running && !_phaseDone;
      case PhantomCommand.pause:
        return runState == PhantomRunState.running && !_phaseDone;
      case PhantomCommand.resume:
        return runState == PhantomRunState.paused;
      case PhantomCommand.end:
        return runState.active;
      case PhantomCommand.nextPerson:
        return runState.active || runState == PhantomRunState.finished || runState == PhantomRunState.error || _phaseDone;
      case PhantomCommand.conditionOrder:
        return !inductionSeen && !runState.active && runState != PhantomRunState.finished;
    }
  }

  /// Does [phase] mean the first induction has started?
  static bool phaseLocksOrder(String? phase) => phase != null && !_preInductionPhases.contains(phase);
}

/// sent -> acked | failed, per command.
enum CommandStatus { sent, acked, failed }

/// Tracks the last send of each command and exposes it as a plain map so the
/// card can show "Sent / Confirmed / Failed" next to each button.
class CommandTracker {
  final Map<PhantomCommand, CommandStatus> _status = {};

  Map<PhantomCommand, CommandStatus> get status => Map.unmodifiable(_status);

  /// Marks [c] as sent, awaits [send] and records the outcome. A throwing
  /// [send] counts as failed.
  Future<CommandStatus> run(PhantomCommand c, Future<bool> Function() send, {void Function()? onChange}) async {
    _status[c] = CommandStatus.sent;
    onChange?.call();
    var ok = false;
    try {
      ok = await send();
    } catch (_) {
      ok = false;
    }
    final result = ok ? CommandStatus.acked : CommandStatus.failed;
    _status[c] = result;
    onChange?.call();
    return result;
  }

  void clear() => _status.clear();
}

/// Accumulates snapshots into what the card draws, and tracks whether the
/// first induction has been seen (for the condition-order lock).
class PhantomLiveModel {
  PhantomLiveModel({double windowMs = 10000}) : buffer = TraceBuffer(windowMs: windowMs);

  final TraceBuffer buffer;
  PhantomLiveSnapshot? latest;
  bool inductionSeen = false;

  /// Order the operator last chose or the headset last reported (default
  /// `async_first`, spec D9).
  String conditionOrder = 'async_first';

  void apply(PhantomLiveSnapshot s) {
    final previous = latest;
    // A new participant (`next_person`, or the run ended and the headset is
    // waiting again): the traces start over and the order is unlocked.
    final wasRunOrOver = previous != null &&
        (previous.runState.active || previous.runState == PhantomRunState.finished || previous.game?.phase == 'done');
    final waitingAgain =
        s.runState == PhantomRunState.paired || s.runState == PhantomRunState.ready || s.runState == PhantomRunState.idle;
    if (wasRunOrOver && waitingAgain) {
      buffer.clear();
      inductionSeen = false;
    }
    latest = s;
    if (PhantomCommandRules.phaseLocksOrder(s.game?.phase) && s.runState != PhantomRunState.idle && s.runState != PhantomRunState.paired) {
      inductionSeen = true;
    }
    if (s.conditionOrder != null) conditionOrder = s.conditionOrder!;
    final chunk = s.chunk;
    if (chunk != null) buffer.addChunk(chunk);
    for (final m in s.markers) {
      buffer.addMarker(m);
    }
  }

  PhantomCommandRules get rules => PhantomCommandRules(
        connected: latest?.connected ?? false,
        runState: latest?.runState ?? PhantomRunState.idle,
        phase: latest?.game?.phase,
        inductionSeen: inductionSeen,
      );
}
