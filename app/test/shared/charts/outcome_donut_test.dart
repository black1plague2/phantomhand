import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/charts/outcome_donut.dart';

/// Real numbers from the committed hub fixture
/// `test/fixtures/sessions/c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7/metrics.json`
/// (brief A3 item 3: "incl. the c93ae4fa fixture: 5 success / 1 timeout ->
/// 83% in the donut centre") rather than a hand-picked round number.
const _fixtureDir = 'test/fixtures/sessions/c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7';

Future<List<String?>> _fixtureOutcomes() async {
  final raw = await File('$_fixtureDir/metrics.json').readAsString();
  final json = jsonDecode(raw) as Map<String, dynamic>;
  final trials = (json['trials'] as List).cast<Map<String, dynamic>>();
  return [for (final t in trials) t['outcome'] as String?];
}

void main() {
  testWidgets('c93ae4fa fixture: 5 success / 1 timeout renders 83% in the centre', (tester) async {
    // A bare `dart:io` file read inside `testWidgets` never completes under
    // flutter_test's FakeAsync zone (its completion arrives via a real event
    // loop callback FakeAsync doesn't process) -- see
    // `test/hub/directory_session_repository_test.dart`'s doc comment for the
    // same bug, root-caused in run13. `tester.runAsync` is the fix: it
    // unpauses the real event loop just for this read.
    final outcomes = await tester.runAsync(_fixtureOutcomes);
    final counts = OutcomeCounts.fromOutcomes(outcomes!);
    expect(counts.success, 5);
    expect(counts.timeout, 1);
    expect(counts.dropped, 0);
    expect(counts.wrongBasket, 0);
    expect(counts.total, 6);
    expect(counts.successPct, 83); // round(500/6) = 83

    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: OutcomeDonut(counts: counts))),
    );
    await tester.pumpAndSettle();

    expect(find.text('83%'), findsOneWidget);
    // v3 amendment (BINDING, docs/design/OPUS_DESIGN_V2.md top box): layman
    // outcome words -- "In basket / Too slow / Dropped / Wrong basket" --
    // replaced the old "success"/"Success"/"Timeout" wording.
    expect(find.text('in basket'), findsOneWidget);
    expect(find.text('In basket'), findsOneWidget);
    expect(find.text('Too slow'), findsOneWidget);
    // Legend counts.
    expect(find.text('5'), findsOneWidget);
    expect(find.text('1'), findsOneWidget);
    // Zero-count outcomes still get a legend row (per spec: "4-row legend
    // with counts") -- Dropped/Wrong basket at 0.
    expect(find.text('Dropped'), findsOneWidget);
    expect(find.text('Wrong basket'), findsOneWidget);
  });

  testWidgets('empty session shows an empty state, not a NaN/0% donut', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: OutcomeDonut(counts: OutcomeCounts()))),
    );
    expect(find.text('No trials yet.'), findsOneWidget);
    expect(find.text('0%'), findsNothing);
  });

  test('OutcomeCounts.fromOutcomes ignores null/unknown outcomes', () {
    final counts = OutcomeCounts.fromOutcomes([null, 'success', 'not_a_real_outcome', 'wrong_basket']);
    expect(counts.success, 1);
    expect(counts.wrongBasket, 1);
    expect(counts.total, 2);
  });
}
