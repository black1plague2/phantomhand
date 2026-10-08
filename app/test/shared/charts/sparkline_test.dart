import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/charts/sparkline.dart';

void main() {
  testWidgets('renders a minimal trend line, empty state has no chart', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: Sparkline(values: []))),
    );
    expect(find.text('No trials yet.'), findsOneWidget);

    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: Sparkline(values: [120, 140, 90, 200, 65.19]))),
    );
    await tester.pumpAndSettle();
    expect(find.byType(Sparkline), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('single repeated value does not throw (degenerate range)', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: Sparkline(values: [100, 100, 100]))),
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
}
