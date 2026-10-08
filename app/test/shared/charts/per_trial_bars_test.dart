import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/charts/per_trial_bars.dart';

void main() {
  testWidgets('renders one bar per trial without throwing, empty state has no chart', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: PerTrialBars(data: []))),
    );
    expect(find.text('No trials yet.'), findsOneWidget);

    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: PerTrialBars(
            data: [
              PerTrialBarDatum(trial: 0, value: 65.19, outcome: 'timeout'),
              PerTrialBarDatum(trial: 1, value: 788.79, outcome: 'success'),
              PerTrialBarDatum(trial: 2, value: 512.0, outcome: 'success'),
            ],
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(PerTrialBars), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
