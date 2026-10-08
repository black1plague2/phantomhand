/// Haptics analysis for the session report (`docs/IMPROVEMENT_BRIEF.md`
/// haptics addendum, item 3: "the user explicitly asked for this"):
/// summarizes `haptic_cue` events (`contracts/schemas/event.schema.json`,
/// `contracts/HAPTIC_PROTOCOL.md`) from a session's `events.ndjson` into the
/// counts/rate/delivery numbers the report shows, plus which trials had a
/// cue so the trial table can flag them.
///
/// Until `opus_analytics`/the synthetic-patient generator emit real
/// `haptic_cue` events into the bundled fixtures, this reads whatever cues
/// happen to be present -- for the `healthy` fixture that's a small,
/// hand-written set added directly to
/// `assets/fixtures/sessions/healthy__events.ndjson` (see that file's
/// trailing `haptic_cue` lines) so the report has real data to render
/// against. A session with no `haptic_cue` events at all (every other
/// fixture, and any real session recorded before the sleeve existed) reports
/// an honest "no cues" summary, not an error.
library;

import 'package:opus_app/data/repositories/sessions_repository.dart';

/// One `haptic_cue` event, pulled out of a [SessionEvent]'s `data` map
/// (`{cue, intensity, delivered}` per `contracts/HAPTIC_PROTOCOL.md`).
class HapticCueOccurrence {
  const HapticCueOccurrence({
    required this.tMs,
    required this.trial,
    required this.cue,
    required this.delivered,
    this.intensity,
  });

  final double tMs;
  final int? trial;
  final String cue;
  final bool delivered;
  final double? intensity;
}

/// Aggregated haptics numbers for one session's report.
class HapticCueSummary {
  const HapticCueSummary({
    required this.occurrences,
    required this.countsByType,
    required this.cuesPerMinute,
    required this.deliveredCount,
    required this.loggedOnlyCount,
  });

  static const empty = HapticCueSummary(
    occurrences: [],
    countsByType: {},
    cuesPerMinute: 0,
    deliveredCount: 0,
    loggedOnlyCount: 0,
  );

  final List<HapticCueOccurrence> occurrences;

  /// cue id (`trunk_lean`/`low_confidence`/`success`) -> how many times it fired.
  final Map<String, int> countsByType;

  final double cuesPerMinute;
  final int deliveredCount;
  final int loggedOnlyCount;

  bool get isEmpty => occurrences.isEmpty;
  int get totalCount => occurrences.length;

  /// Trial indices that had at least one `trunk_lean` cue -- used to flag
  /// the trial table's rows so the cue timeline reads aligned with the
  /// reach-trace/trial data, per the brief's "trunk-lean episodes ... aligned
  /// with the reach-trace/trial table".
  Set<int> get trunkLeanTrials => {
        for (final o in occurrences)
          if (o.cue == 'trunk_lean' && o.trial != null) o.trial!,
      };

  /// Every cue id -> the set of trial indices it fired during, for any
  /// caller (e.g. the trial table) that wants to badge a specific trial row.
  Map<int, List<HapticCueOccurrence>> get byTrial {
    final map = <int, List<HapticCueOccurrence>>{};
    for (final o in occurrences) {
      if (o.trial == null) continue;
      map.putIfAbsent(o.trial!, () => []).add(o);
    }
    return map;
  }

  /// Whether any cue this session went undelivered (sleeve not connected --
  /// "cues were only logged").
  bool get hadUndeliveredCues => loggedOnlyCount > 0;

  /// Whether any cue this session actually reached the sleeve.
  bool get hadDeliveredCues => deliveredCount > 0;
}

/// Human label for a cue id -- plain language, no jargon
/// (`docs/APP_DESIGN.md` copy rules), matching the clinical names
/// `contracts/HAPTIC_PROTOCOL.md` gives each cue.
String hapticCueLabel(String cue) => switch (cue) {
      'trunk_lean' => 'Trunk lean',
      'low_confidence' => 'Hand out of view',
      'success' => 'Success',
      _ => cue.replaceAll('_', ' '),
    };

/// Builds a [HapticCueSummary] from a session's full event list. Session
/// duration for the cues-per-minute rate is taken from the first and last
/// event's `t_ms` (the events span the whole session, `session_start` to
/// `session_end`) rather than requiring a separate duration input.
HapticCueSummary summarizeHapticCues(List<SessionEvent> events) {
  if (events.isEmpty) return HapticCueSummary.empty;

  final cueEvents = events.where((e) => e.type == 'haptic_cue').toList();
  if (cueEvents.isEmpty) return HapticCueSummary.empty;

  final occurrences = <HapticCueOccurrence>[];
  final counts = <String, int>{};
  var delivered = 0;
  var loggedOnly = 0;

  for (final e in cueEvents) {
    final data = (e.raw['data'] as Map?)?.cast<String, dynamic>() ?? const {};
    final cue = data['cue'] as String? ?? 'unknown';
    final isDelivered = data['delivered'] as bool? ?? false;
    final intensity = (data['intensity'] as num?)?.toDouble();
    occurrences.add(HapticCueOccurrence(
      tMs: e.tMs,
      trial: e.trial,
      cue: cue,
      delivered: isDelivered,
      intensity: intensity,
    ));
    counts[cue] = (counts[cue] ?? 0) + 1;
    if (isDelivered) {
      delivered++;
    } else {
      loggedOnly++;
    }
  }

  final firstTMs = events.first.tMs;
  final lastTMs = events.last.tMs;
  final durationMinutes = (lastTMs - firstTMs) / 60000.0;
  final cuesPerMinute = durationMinutes > 0 ? occurrences.length / durationMinutes : 0.0;

  return HapticCueSummary(
    occurrences: occurrences,
    countsByType: counts,
    cuesPerMinute: cuesPerMinute,
    deliveredCount: delivered,
    loggedOnlyCount: loggedOnly,
  );
}
