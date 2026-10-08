import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';

/// `docs/agent-briefs` A2 run13 brief item 3: a real hub session has no
/// `metrics.json` until `opus_analytics` runs, but the raw `events.ndjson`
/// the headset already uploaded carries enough timestamps to show something
/// better than an empty report while the clinician waits -- reaction time
/// (`target_shown` -> `movement_onset`) and movement time (`movement_onset`
/// -> `contact`), per trial. Everything else (SPARC, peak speed, trunk
/// lean...) genuinely needs the real kinematic analysis and is left absent
/// rather than guessed at.
const String eventsDerivedMethodVersion = 'events_derived_v0';

/// Marks every value this builder produces so the trial table's quality
/// badge (and anything else reading [MetricValue.qualityReasons]) can tell a
/// "provisional, from raw events" number apart from a real analytics-worker
/// one -- callers show "Full biomarkers after analysis" alongside it.
const String eventsDerivedPendingAnalysisReason = 'events_derived_pending_full_analysis';

/// Builds one [TrialMetrics] per trial number seen in [events], with only
/// `reaction_time_ms`/`movement_time_ms` populated (quality `degraded`, both
/// tagged [eventsDerivedPendingAnalysisReason]) when the needed event pair
/// exists, or `invalid` with a reason naming the missing event when it
/// doesn't (e.g. the trial timed out before the patient moved). Trials with
/// no events at all (shouldn't happen, but a session mid-upload might be
/// partial) are skipped rather than shown as all-invalid placeholder rows.
List<TrialMetrics> buildEventDerivedTrials(List<SessionEvent> events) {
  final byTrial = <int, List<SessionEvent>>{};
  for (final e in events) {
    final trial = e.trial;
    if (trial == null) continue;
    byTrial.putIfAbsent(trial, () => []).add(e);
  }

  final trialNumbers = byTrial.keys.toList()..sort();
  final result = <TrialMetrics>[];
  for (final trialNum in trialNumbers) {
    final trialEvents = byTrial[trialNum]!;
    SessionEvent? firstOfType(String type) {
      for (final e in trialEvents) {
        if (e.type == type) return e;
      }
      return null;
    }

    final targetShown = firstOfType('target_shown');
    final movementOnset = firstOfType('movement_onset');
    final contact = firstOfType('contact');
    final trialEnd = firstOfType('trial_end');
    final trialStart = firstOfType('trial_start');

    final rt = (targetShown != null && movementOnset != null) ? movementOnset.tMs - targetShown.tMs : null;
    final mt = (movementOnset != null && contact != null) ? contact.tMs - movementOnset.tMs : null;

    MetricValue metricFor(double? value, String missingReason) => MetricValue(
          value: value,
          unit: 'unitless',
          methodVersion: eventsDerivedMethodVersion,
          quality: value == null ? 'invalid' : 'degraded',
          qualityReasons: value == null ? [missingReason] : const [eventsDerivedPendingAnalysisReason],
        );

    result.add(
      TrialMetrics(
        block: trialEvents.first.block,
        trial: trialNum,
        hand: movementOnset?.hand ?? targetShown?.hand ?? trialStart?.hand,
        outcome: trialEnd?.outcome,
        target: targetShown?.target,
        metrics: {
          'reaction_time_ms': metricFor(rt, 'no_movement_onset_event'),
          'movement_time_ms': metricFor(mt, 'no_contact_event'),
        },
      ),
    );
  }
  return result;
}
