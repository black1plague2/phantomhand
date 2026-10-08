import 'dart:math' as math;

import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:riverpod/src/providers/future_provider.dart';

/// One session's worth of metric values (+ per-metric SD and, when
/// `opus_analytics` publishes one, a real `mdc95` -- see [mdcFor] and
/// [progressDataProvider]'s doc), sorted chronologically by the provider
/// below.
///
/// Shared between the progress screen's full trend picker and the patient
/// overview's compact "recovery line" (`docs/APP_DESIGN.md`'s workstation
/// layout) so both read the exact same session history instead of two
/// providers silently drifting apart.
class SessionMetricPoint {
  new(this.values, this.sds, [this.mdc95s = const {}]);
  final Map<String, double> values;
  final Map<String, double?> sds;
  final Map<String, double?> mdc95s;
}

/// Longitudinal per-session metric values for one patient, oldest first.
///
/// KNOWN SIMPLIFICATION: a true, cited MDC requires test-retest reliability
/// data from real patients, which doesn't exist yet (`docs/ARCHITECTURE.md`
/// §5 calls this out as planned, not delivered).
/// `opus_analytics` v0.2.0 (2026-09-17) started publishing a real `mdc95`
/// per metric for `trunk_lean_cm`/`neglect_index`, computed from *synthetic*
/// test-retest sessions -- Opus's own note: "far tighter than real
/// patients." [mdcFor] prefers that value when a metric has one; every other
/// metric still falls back to the older stand-in, `1.96 * sqrt(2) * SD` of
/// the first session's per-trial values (the standard MDC95 formula, applied
/// to a same-session SD as a stand-in for test-retest SD). **Both sources are
/// synthetic, not a clinically validated MDC** -- every caller must render
/// the "(synthetic estimate)" caveat next to any band built from [mdcFor],
/// per `docs/IMPROVEMENT_BRIEF.md`'s citation rule (flagged again as a
/// "Contract change request": real, cited per-metric MDC constants should
/// replace this once available).
final FutureProviderFamily<List<SessionMetricPoint>, String> progressDataProvider =
    FutureProvider.family<List<SessionMetricPoint>, String>((ref, patientId) async {
  final sessionsRepo = ref.watch(sessionsRepositoryProvider);
  final sessions = await sessionsRepo.listSessionsForPatient(patientId);
  final sorted = [...sessions]..sort((a, b) => a.startedAt.compareTo(b.startedAt));
  final points = <SessionMetricPoint>[];
  for (final s in sorted) {
    final metrics = await sessionsRepo.getSessionMetrics(s.sessionId);
    if (metrics == null) continue;
    final values = <String, double>{};
    final sds = <String, double?>{};
    final mdc95s = <String, double?>{};
    metrics.session.metrics.forEach((key, mv) {
      if (mv.value != null) values[key] = mv.value!;
      sds[key] = mv.sd;
      mdc95s[key] = mv.mdc95;
    });
    // Run 8 fix: the patient overview's "Smoothness (SPARC)" and "Reaction
    // time" recovery lines always said "No sessions recorded yet." even for
    // a patient (`synthetic-longitudinal-9000`) with 7 real sessions, while
    // "Reach area"/"Trunk lean" plotted fine from the same fixtures. Read one
    // real `metrics.json` (`assets/fixtures/sessions/
    // longitudinal__week_00__session_0__metrics.json`) to confirm: analytics
    // v0.2 only publishes `sparc`/`reaction_time_ms` (and
    // `movement_time_ms`/`peak_speed_mps`/etc.) **per trial**
    // (`metrics.trials[i].metrics`) -- `metrics.session.metrics` (the session
    // aggregate the loop above reads) only has
    // `rate_hz`/`success_rate`/`neglect_index`/`trunk_lean_cm`/
    // `fatigue_slope`/`reach_envelope_area_m2`/`tracking_loss_pct`. There is
    // no session-level `sparc`/`reaction_time_ms` to read, by design of the
    // schema, not a producer bug -- so for any metric the session aggregate
    // doesn't have, fall back to the mean of that session's per-trial values
    // (and the trial-to-trial sample SD, used by `mdcFor`'s stand-in below
    // exactly the way a same-session SD already is for aggregate metrics).
    final trialValuesByMetric = <String, List<double>>{};
    for (final trial in metrics.trials) {
      trial.metrics.forEach((key, mv) {
        if (mv.value != null) trialValuesByMetric.putIfAbsent(key, () => []).add(mv.value!);
      });
    }
    trialValuesByMetric.forEach((key, trialValues) {
      if (values.containsKey(key) || trialValues.isEmpty) return;
      values[key] = trialValues.reduce((a, b) => a + b) / trialValues.length;
      sds[key] = _sampleStdDev(trialValues);
      // No `mdc95s[key]` here: analytics doesn't publish a per-trial-mean
      // mdc95, and `mdcFor` already falls back to the SD95 stand-in when the
      // real one is absent.
    });
    points.add(SessionMetricPoint(values, sds, mdc95s));
  }
  return points;
});

/// The MDC half-width to use for [metricId] given its baseline (first-session)
/// point: the real, analytics-published `mdc95` when present, otherwise the
/// SD95 stand-in -- see [progressDataProvider]'s doc for why both are
/// synthetic-data estimates, never a validated clinical constant.
double? mdcFor(String metricId, SessionMetricPoint baseline) {
  final real = baseline.mdc95s[metricId];
  if (real != null) return real;
  final sd = baseline.sds[metricId];
  if (sd == null) return null;
  return 1.96 * 1.4142135623730951 * sd; // 1.96 * sqrt(2)
}

/// Sample standard deviation of per-trial values within one session, used as
/// the `sd` for a metric [progressDataProvider] had to derive from
/// `metrics.trials` instead of reading directly from `metrics.session.metrics`
/// (see that function's doc). Returns null for fewer than 2 values -- an SD
/// from a single trial isn't meaningful and would otherwise silently read as
/// "0 variance".
double? _sampleStdDev(List<double> values) {
  if (values.length < 2) return null;
  final mean = values.reduce((a, b) => a + b) / values.length;
  final sumSquaredDiffs = values.map((v) => (v - mean) * (v - mean)).reduce((a, b) => a + b);
  final variance = sumSquaredDiffs / (values.length - 1);
  return math.sqrt(variance);
}
