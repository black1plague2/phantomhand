import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/shared/metrics/events_derived_metrics.dart';

/// A2 run13 brief item 3: real hub sessions have no `metrics.json` until
/// `opus_analytics` runs, but `events.ndjson` already carries enough
/// timestamps to derive reaction time (`target_shown` -> `movement_onset`)
/// and movement time (`movement_onset` -> `contact`) per trial. Uses the
/// committed fixture copied from a real headset session
/// (`app/.hub_data/c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7/`, which also has a
/// real `metrics.json` -- see `directory_session_repository_test.dart` for
/// the metrics.json-present path) rather than a hand-built synthetic one, so
/// this exercises the real event shapes the headset actually emits.
void main() {
  late List<SessionEvent> events;

  setUpAll(() {
    final path = '${Directory.current.path}/test/fixtures/sessions/'
        'c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7/events.ndjson';
    final lines = File(path).readAsLinesSync().where((l) => l.trim().isNotEmpty);
    events = [for (final l in lines) SessionEvent(jsonDecode(l) as Map<String, dynamic>)];
  });

  test('derives reaction time and movement time from real target_shown/movement_onset/contact events', () {
    final trials = buildEventDerivedTrials(events);
    expect(trials, isNotEmpty);

    // Trial 0: timed out before contact (real fixture data) -- reaction
    // time is derivable, movement time is not.
    final trial0 = trials.firstWhere((t) => t.trial == 0);
    expect(trial0.outcome, 'timeout');
    expect(trial0.metrics['reaction_time_ms']!.value, closeTo(65.19, 0.1));
    expect(trial0.metrics['reaction_time_ms']!.quality, 'degraded');
    expect(trial0.metrics['reaction_time_ms']!.qualityReasons, contains(eventsDerivedPendingAnalysisReason));
    expect(trial0.metrics['movement_time_ms']!.value, isNull);
    expect(trial0.metrics['movement_time_ms']!.quality, 'invalid');
    expect(trial0.metrics['movement_time_ms']!.qualityReasons, contains('no_contact_event'));

    // Trial 1: a full success -- both are derivable.
    final trial1 = trials.firstWhere((t) => t.trial == 1);
    expect(trial1.outcome, 'success');
    expect(trial1.hand, 'right');
    expect(trial1.metrics['reaction_time_ms']!.value, closeTo(788.79, 0.1));
    expect(trial1.metrics['movement_time_ms']!.value, closeTo(1375.61, 0.1));
    expect(trial1.metrics['reaction_time_ms']!.quality, 'degraded');
    expect(trial1.metrics['movement_time_ms']!.quality, 'degraded');

    // Never guesses at metrics that genuinely need the real analytics
    // worker -- these keys must be absent, not a fabricated 0/null entry.
    expect(trial1.metrics.containsKey('sparc'), isFalse);
    expect(trial1.metrics.containsKey('peak_speed_mps'), isFalse);
  });

  test('trials come out sorted by trial number', () {
    final trials = buildEventDerivedTrials(events);
    final numbers = trials.map((t) => t.trial).toList();
    expect(numbers, [for (var i = 0; i < numbers.length; i++) i]);
  });

  test('events with no trial number (session_start, block_start) are ignored', () {
    final trials = buildEventDerivedTrials(events);
    expect(trials.every((t) => t.trial >= 0), isTrue);
  });
}
