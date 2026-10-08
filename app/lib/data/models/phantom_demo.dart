/// The Phantom Hand in-app demo (no headset, sleeve or hub): one recorded run
/// of the game against the sleeve simulator, replayed in compressed time on the
/// operator card. SIMULATED data (a scripted participant), labelled as such
/// wherever it is shown.
///
/// Pure Dart (no Flutter, no I/O): [PhantomDemoScript.tryBuild] turns the
/// recording's `events.ndjson` lines and its 20 Hz live trace into the demo
/// timeline the mock engine plays, so every rule is unit-testable. Nothing is
/// invented: phases, conditions, flinch timing and the witness numbers are the
/// recording's own; only the clock is compressed (a 60 s induction plays in
/// [phantomDemoSeconds]['induction'] s).
library;

import 'dart:math' as math;

import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/models/phantom_witness.dart';

/// The demo participant (`assets/fixtures/patients.json`) and the one session
/// the demo patient has. The session id is the recording's own.
const demoPhantomPatientId = 'demo-phantom-hand';
const demoPhantomSessionId = 'a7c5d8c6-2e5a-40cd-a3f4-cda09ef85e92';

/// The route of the demo screen (`liveRoutes`) and of the demo session's report.
const demoPhantomRoute = '/demo/phantom-hand';
const demoPhantomReportPath = '/patients/$demoPhantomPatientId/sessions/$demoPhantomSessionId';

/// Wall-clock seconds each phase plays for (the recording's phases last 1 to
/// 60 s). Long phases are compressed, short ones stretched so a caption can be
/// read; the stone drop plays at the speed of the recording (it lasts 5 s
/// there), so its flinch is the recorded one, sample for sample. The 14 phases
/// add up to 73 s.
const phantomDemoSeconds = <String, double>{
  'calibrate': 4,
  'probe_pre': 4,
  'induction': 10,
  'threat': 5,
  'probe_post': 4,
  'questionnaire': 4,
  'dissolve': 5,
  'reveal': 5,
  'witness': 5,
};

/// How long the audience results mirror stays up before the report opens.
const phantomDemoMirrorSeconds = 12;

double _seconds(String phase) => phantomDemoSeconds[phase] ?? 4;

/// What the live card plots, from a recording: the EMG envelope (raw ADC counts)
/// and |accel| (m/s²) at [fsHz] on the recording's session clock, bin `k`
/// covering `[k, k+1) / fsHz` from [t0Ms]. The recording's bins are the means
/// the headset itself sends in `status.payload.trace`.
class PhantomRecordedTrace {
  const new({required this.fsHz, required this.emgEnv, required this.accelMag, this.t0Ms = 0});

  final int fsHz;
  final List<double> emgEnv;
  final List<double> accelMag;
  final double t0Ms;

  /// `live_trace.json`; null when it is not a map or lacks the two series.
  static PhantomRecordedTrace? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final fs = raw['fs_hz'];
    final emg = raw['emg_env'];
    final acc = raw['accel_mag'];
    final t0 = raw['t0_ms'];
    if (fs is! int || fs <= 0 || emg is! List || acc is! List || emg.isEmpty || acc.isEmpty) return null;
    return PhantomRecordedTrace(
      fsHz: fs,
      emgEnv: [for (final v in emg) if (v is num) v.toDouble()],
      accelMag: [for (final v in acc) if (v is num) v.toDouble()],
      t0Ms: t0 is num ? t0.toDouble() : 0,
    );
  }

  /// Largest value of [series] over `[fromMs, toMs)` of the session clock (the
  /// bin of [fromMs] at least), clamped to the recording. A demo sample that
  /// stands for several recorded bins keeps the biggest, so a flinch spike
  /// survives the compression.
  double peak(List<double> series, double fromMs, double toMs) {
    final binMs = 1000 / fsHz;
    final first = ((fromMs - t0Ms) / binMs).floor().clamp(0, series.length - 1);
    final last = ((toMs - t0Ms - 1e-6) / binMs).floor().clamp(first, series.length - 1);
    var best = series[first];
    for (var i = first + 1; i <= last; i++) {
      best = math.max(best, series[i]);
    }
    return best;
  }
}

/// One phase of the demo timeline: [seconds] of wall clock playing
/// `[realFromMs, realToMs)` of the recording.
class PhantomDemoStep {
  const new({
    required this.phase,
    required this.condition,
    required this.seconds,
    required this.realFromMs,
    required this.realToMs,
  });

  final String phase;
  final PhantomCondition? condition;
  final double seconds;
  final double realFromMs;
  final double realToMs;
}

/// The demo timeline: the recording's phases in order (then `done`), the traces
/// and the stone / muscle-burst markers on the demo clock, and the recording's
/// `witness_summary`.
class PhantomDemoScript {
  const new({
    required this.steps,
    required this.emg,
    required this.accel,
    required this.markers,
    required this.witness,
  });

  /// The phases, ending with `done` (0 s).
  final List<PhantomDemoStep> steps;

  /// 20 Hz samples on the demo clock, from the start of the first phase.
  final List<double> emg;
  final List<double> accel;

  /// Stone-lands and muscle-burst markers, `tMs` on the demo clock (ms from the
  /// start of the first phase).
  final List<TraceMarker> markers;
  final PhantomWitness witness;

  /// Demo trace rate (the card's own: `TraceChunk.fsHz` defaults to 20).
  static const fsHz = 20;

  /// Seconds the live part plays for.
  double get totalSeconds => steps.fold(0, (sum, s) => sum + s.seconds);

  double emgAt(int i) => emg[i.clamp(0, emg.length - 1)];
  double accelAt(int i) => accel[i.clamp(0, accel.length - 1)];

  /// Markers on the demo clock inside `[fromMs, toMs)`.
  List<TraceMarker> markersIn(double fromMs, double toMs) => [
        for (final m in markers)
          if (m.tMs >= fromMs && m.tMs < toMs) m,
      ];

  /// Builds the timeline from the recording's event lines ([events], in file
  /// order) and [trace]. Null when the recording has no phases, no
  /// `witness_summary` or no trace: the demo then cannot play.
  static PhantomDemoScript? tryBuild({required List<Map<String, dynamic>> events, required PhantomRecordedTrace? trace}) {
    if (trace == null) return null;
    final starts = [for (final e in events) if (e['type'] == 'phase_start' && e['t_ms'] is num) e];
    if (starts.isEmpty) return null;
    final witness = PhantomWitness.tryParseEvent(events.where((e) => e['type'] == 'witness_summary').firstOrNull);
    if (witness == null) return null;
    final endMs = [
          for (final e in events)
            if (e['type'] == 'block_end' && e['t_ms'] is num) (e['t_ms'] as num).toDouble(),
        ].firstOrNull ??
        trace.t0Ms + trace.emgEnv.length * 1000 / trace.fsHz;

    final steps = <PhantomDemoStep>[];
    for (var i = 0; i < starts.length; i++) {
      final data = starts[i]['data'];
      final phase = data is Map ? data['phase'] : null;
      if (phase is! String || phase.isEmpty) return null;
      final from = (starts[i]['t_ms'] as num).toDouble();
      final to = i + 1 < starts.length ? (starts[i + 1]['t_ms'] as num).toDouble() : endMs;
      if (to <= from) return null;
      steps.add(
        PhantomDemoStep(
          phase: phase,
          condition: PhantomCondition.parse((data as Map)['condition']),
          seconds: _seconds(phase),
          realFromMs: from,
          realToMs: to,
        ),
      );
    }

    // The trace on the demo clock: each demo sample plays the real stretch it
    // stands for, keeping the largest value of it.
    final emg = <double>[];
    final accel = <double>[];
    final stepStartMs = <double>[];
    var clockMs = 0.0;
    for (final s in steps) {
      stepStartMs.add(clockMs);
      final n = (s.seconds * fsHz).round();
      for (var j = 0; j < n; j++) {
        final a = s.realFromMs + (s.realToMs - s.realFromMs) * j / n;
        final b = s.realFromMs + (s.realToMs - s.realFromMs) * (j + 1) / n;
        emg.add(trace.peak(trace.emgEnv, a, b));
        accel.add(trace.peak(trace.accelMag, a, b));
      }
      clockMs += s.seconds * 1000;
    }
    if (emg.isEmpty) return null;

    // The stone and the muscle burst: the recording's own times, moved onto the
    // demo clock inside the threat phase they happened in.
    double? demoMs(double realMs) {
      for (var i = 0; i < steps.length; i++) {
        final s = steps[i];
        if (s.phase == 'threat' && realMs >= s.realFromMs && realMs < s.realToMs) {
          return stepStartMs[i] + (realMs - s.realFromMs) / (s.realToMs - s.realFromMs) * s.seconds * 1000;
        }
      }
      return null;
    }

    final markers = <TraceMarker>[];
    for (var i = 0; i < events.length; i++) {
      final e = events[i];
      if (e['type'] != 'threat_impact' || e['data'] is! Map) continue;
      final impact = (e['data'] as Map)['impact_ms'];
      if (impact is! num) continue;
      final impactDemo = demoMs(impact.toDouble());
      if (impactDemo == null) continue;
      markers.add(TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: impactDemo));
      // The flinch answering that stone: the next threat_response's EMG latency.
      final response = events.skip(i + 1).firstWhere((x) => x['type'] == 'threat_response', orElse: () => const {});
      final latency = response['data'] is Map ? (response['data'] as Map)['emg_latency_ms'] : null;
      final burstDemo = latency is num ? demoMs(impact.toDouble() + latency.toDouble()) : null;
      if (burstDemo != null) markers.add(TraceMarker(kind: TraceMarkerKind.emgBurst, tMs: burstDemo));
    }
    markers.sort((a, b) => a.tMs.compareTo(b.tMs));

    return PhantomDemoScript(
      steps: [
        ...steps,
        const PhantomDemoStep(phase: 'done', condition: null, seconds: 0, realFromMs: 0, realToMs: 0),
      ],
      emg: emg,
      accel: accel,
      markers: markers,
      witness: witness,
    );
  }
}
