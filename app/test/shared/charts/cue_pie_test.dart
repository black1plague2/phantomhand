import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/metrics/haptic_analysis.dart';
import 'package:opus_app/shared/widgets/charts/cue_pie.dart';

void main() {
  testWidgets('empty summary shows an empty state', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: CuePie(summary: HapticCueSummary.empty))),
    );
    expect(find.text('No cues this session.'), findsOneWidget);
  });

  testWidgets('renders cue types with plain-language labels and delivered %', (tester) async {
    const summary = HapticCueSummary(
      occurrences: [
        HapticCueOccurrence(tMs: 0, trial: 0, cue: 'trunk_lean', delivered: true),
        HapticCueOccurrence(tMs: 100, trial: 1, cue: 'trunk_lean', delivered: true),
        HapticCueOccurrence(tMs: 200, trial: 2, cue: 'low_confidence', delivered: false),
        HapticCueOccurrence(tMs: 300, trial: 3, cue: 'success', delivered: true),
      ],
      countsByType: {'trunk_lean': 2, 'low_confidence': 1, 'success': 1},
      cuesPerMinute: 4,
      deliveredCount: 3,
      loggedOnlyCount: 1,
    );
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: CuePie(summary: summary))),
    );
    await tester.pumpAndSettle();

    expect(find.text('Trunk lean'), findsOneWidget);
    expect(find.text('Hand out of view'), findsOneWidget);
    expect(find.text('Success'), findsOneWidget);
    expect(find.text('75% delivered'), findsOneWidget); // 3/4
    expect(tester.takeException(), isNull);
  });
}
