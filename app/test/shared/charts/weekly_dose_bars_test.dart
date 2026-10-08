import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/charts/weekly_dose_bars.dart';

void main() {
  testWidgets('renders bars against the target line without throwing', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: WeeklyDoseBars(weeks: [], target: 3))),
    );
    expect(find.text('No sessions yet.'), findsOneWidget);

    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: WeeklyDoseBars(
            weeks: [
              WeeklyDoseDatum(label: 'W1', sessions: 2),
              WeeklyDoseDatum(label: 'W2', sessions: 4),
              WeeklyDoseDatum(label: 'W3', sessions: 3),
            ],
            target: 3,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('W1'), findsOneWidget);
    expect(find.text('W2'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
