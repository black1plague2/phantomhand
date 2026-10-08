import 'dart:math' as math;

import 'package:opus_app/data/models/phantom_witness.dart';

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

/// How much of each trace the card keeps and draws: long enough that a flinch
/// is still on screen while the operator looks back at the phone.
const phantomTraceWindowMs = 20000.0;

/// Fixed y range of the |accel| trace. It never rescales to the data, so a
/// small jolt looks small and a big one big, and the labelled min / max tell the
/// reader the size. |accel| in m/s²: rest is gravity (9.8), a jolt peaks at 14
/// to 24 there.
const ({double min, double max}) phantomAccelScale = (min: 0, max: 25);

/// y range of the EMG trace until its first sample arrives; after that the plot
/// follows the data ([traceRange]). Raw ADC counts: the L3 fixture
/// (`tools/demo/tests/fixtures/ph_l3_main`) rests near 420 and flinches to about
/// 2500, but a real person rested near 230 and a contraction rose by only a few
/// tens to a few hundred, which is a flat line on 0 to 3000.
const ({double min, double max}) phantomEmgScale = (min: 0, max: 3000);

/// Resting level of a trace, estimated in the app (no protocol field): the
/// median of the samples in view, or null until [minSamples] have arrived (2 s
/// at 20 Hz). The median ignores a flinch, which is a short spike, so
/// "× resting" is measured against where the signal sits when quiet.
double? restingLevel(List<TracePoint> points, {int minSamples = 40}) {
  if (points.length < minSamples) return null;
  final v = [for (final p in points) p.value]..sort();
  final mid = v.length ~/ 2;
  return v.length.isOdd ? v[mid] : (v[mid - 1] + v[mid]) / 2;
}

/// The ESP32 ADC counts 0 to 4095, so the EMG axis stays inside that.
const _adcMax = 4095.0;

/// Narrowest EMG axis, in counts. A person at rest wanders by about 20 counts, so
/// a quiet trace fills only part of the height and does not look like activity,
/// while a rise of a few tens still reads as a rise.
const traceMinSpan = 60.0;

/// Room above and below the data, as a share of its span, so a peak never
/// touches the edge of the plot.
const _rangePad = 0.1;

/// Axis numbers are multiples of this: round labels that change in steps, not
/// digit by digit.
const _rangeLabelStep = 10.0;

/// An edge that sits less than this share of the span beyond the data is left
/// alone, so the noise of a person at rest does not make the axis creep.
const _rangeSlack = 0.15;

/// A too-wide edge closes half of its gap to the data in this long.
const _rangeHalfLifeMs = 2000.0;

/// The "newest peak" is the highest sample of this long.
const _newestPeakMs = 5000.0;

/// The EMG plot's y range, held between status messages, with the two numbers
/// read off the same samples. Made by [traceRange].
class TraceRange {
  const new({required this.low, required this.high, required this.atMs, required this.peak, this.rest});

  /// The held edges, exact. The axis the plot draws and labels is [min]..[max].
  final double low;
  final double high;

  /// Session time of the newest sample these were worked out for.
  final double atMs;

  /// Highest sample of the newest [_newestPeakMs] ms.
  final double peak;

  /// [restingLevel] of the samples in view, or null until there are enough.
  final double? rest;

  /// Axis bottom and top as labelled: round numbers just outside [low]..[high]
  /// and inside the ADC's 0 to 4095, so the labels are the real range drawn.
  double get min => math.max(0, (low / _rangeLabelStep).floorToDouble() * _rangeLabelStep);
  double get max => math.min(_adcMax, (high / _rangeLabelStep).ceilToDouble() * _rangeLabelStep);

  /// How big the newest peak is against rest, or null while rest is unknown or zero.
  double? get peakOverRest {
    final r = rest;
    return r != null && r > 0 ? peak / r : null;
  }
}

/// The y range for the EMG samples in view, or null when there are none.
///
/// The range runs from a little below the lowest sample to a little above the
/// highest, never narrower than [traceMinSpan] and always inside 0 to 4095. It is
/// held between calls ([previous] is the last result): it widens at once, so a
/// spike is never clipped, and it narrows only when an edge is clearly too far
/// out, and then slowly, so the plot does not twitch with every sample and a
/// spike that leaves the window lets the scale back down smoothly.
TraceRange? traceRange(List<TracePoint> points, {TraceRange? previous}) {
  if (points.isEmpty) return null;
  final nowMs = points.last.tMs;
  var lowest = double.infinity;
  var highest = double.negativeInfinity;
  var peak = double.negativeInfinity;
  for (final p in points) {
    lowest = math.min(lowest, p.value);
    highest = math.max(highest, p.value);
    if (p.tMs >= nowMs - _newestPeakMs) peak = math.max(peak, p.value);
  }

  // Where the edges would sit if drawn fresh: the data and its margin, centred on
  // the data, then moved (not squeezed) to stay inside the ADC's counts.
  final span = math.max(traceMinSpan, (highest - lowest) * (1 + 2 * _rangePad));
  final mid = (lowest + highest) / 2;
  var fitLow = mid - span / 2;
  var fitHigh = mid + span / 2;
  if (fitLow < 0) {
    fitHigh -= fitLow;
    fitLow = 0;
  }
  if (fitHigh > _adcMax) {
    fitLow = math.max(0, fitLow - (fitHigh - _adcMax));
    fitHigh = _adcMax;
  }

  var low = fitLow;
  var high = fitHigh;
  if (previous != null) {
    final k = 1 - math.pow(0.5, math.max(0, nowMs - previous.atMs) / _rangeHalfLifeMs);
    final slack = (fitHigh - fitLow) * _rangeSlack;
    low = _heldEdge(previous.low, fitLow, wider: fitLow <= previous.low, slack: slack, k: k);
    high = _heldEdge(previous.high, fitHigh, wider: fitHigh >= previous.high, slack: slack, k: k);
  }
  return TraceRange(low: low, high: high, atMs: nowMs, peak: peak, rest: restingLevel(points));
}

/// One edge of the held range: out to [fit] at once when it is [wider] than the
/// held one, otherwise nowhere unless it is more than [slack] away, and then a
/// share [k] of the way in.
double _heldEdge(double held, double fit, {required bool wider, required double slack, required num k}) {
  if (wider) return fit;
  final gap = fit - held;
  return gap.abs() > slack ? held + gap * k : held;
}

/// Rolling window over successive [TraceChunk]s ("The app concatenates
/// successive traces"). Keeps only the last [windowMs] (default
/// [phantomTraceWindowMs]) before the newest sample; markers older than the
/// window are dropped with them.
class TraceBuffer {
  TraceBuffer({this.windowMs = phantomTraceWindowMs});

  final double windowMs;
  final List<TracePoint> _emg = [];
  final List<TracePoint> _accel = [];
  final List<TraceMarker> _markers = [];
  double? _latestMs;
  TraceRange? _emgRange;

  List<TracePoint> get emg => List.unmodifiable(_emg);
  List<TracePoint> get accel => List.unmodifiable(_accel);
  List<TraceMarker> get markers => List.unmodifiable(_markers);

  /// The EMG plot's y range and the numbers read off the same samples
  /// ([traceRange]), updated with every chunk; null while there is no EMG sample.
  TraceRange? get emgRange => _emgRange;

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
    _emgRange = traceRange(_emg, previous: _emgRange);
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
    _emgRange = null;
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
    this.witness,
    this.conditionOrder,
    this.rttMs,
    this.receivedAt,
  });

  final PhantomRunState runState;

  /// The hub connection (or the mock) is up.
  final bool connected;
  final PhantomGameState? game;

  /// New trace samples carried by this status (null: none).
  final TraceChunk? chunk;

  /// Markers that arrived with this snapshot.
  final List<TraceMarker> markers;

  /// The `witness_summary` event that arrived with this snapshot (null: none).
  /// [PhantomLiveModel] keeps the latest one for the audience mirror.
  final PhantomWitness? witness;

  /// The order the headset reports for the next run, if known.
  final String? conditionOrder;

  /// Round trip to the headset (ping / pong) in ms, if measured.
  final int? rttMs;

  /// When the latest `status` arrived; null when unknown (the scripted demo).
  /// Events do not move it: it is the age of the numbers on the card.
  final DateTime? receivedAt;
}

/// Is the link behind the card alive, quiet, or gone.
enum PhantomLinkState { live, stale, offline }

/// How far the card's numbers can be trusted right now. The headset sends a
/// status every 0.5 s, and the hub only drops the link after about three
/// missed 1 s pongs, so for a few seconds a frozen card looks live: this makes
/// that visible.
class PhantomLinkHealth {
  const new(this.state, {this.rttMs, this.ageS});

  factory of(PhantomLiveSnapshot? s, DateTime now) {
    if (s == null || !s.connected) return const PhantomLinkHealth(PhantomLinkState.offline);
    final at = s.receivedAt;
    final age = at == null ? Duration.zero : now.difference(at);
    if (age >= staleAfter) return PhantomLinkHealth(PhantomLinkState.stale, ageS: age.inSeconds);
    return PhantomLinkHealth(PhantomLinkState.live, rttMs: s.rttMs);
  }

  /// No status for this long counts as stale (four missed statuses).
  static const staleAfter = Duration(seconds: 2);

  final PhantomLinkState state;

  /// Live: the round trip in ms, if known.
  final int? rttMs;

  /// Stale: whole seconds since the last status.
  final int? ageS;

  /// The numbers on the card are not current.
  bool get stale => state != PhantomLinkState.live;
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
  PhantomLiveModel({double windowMs = phantomTraceWindowMs}) : buffer = TraceBuffer(windowMs: windowMs);

  final TraceBuffer buffer;
  PhantomLiveSnapshot? latest;
  bool inductionSeen = false;

  /// The latest `witness_summary` of this run: snapshots carry it once, the
  /// audience mirror needs it for as long as the witness phase lasts.
  PhantomWitness? witness;

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
      // The next person must not see the last person's numbers.
      witness = null;
    }
    latest = s;
    if (s.witness != null) witness = s.witness;
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
