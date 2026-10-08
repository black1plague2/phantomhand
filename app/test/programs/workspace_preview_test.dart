// `docs/IMPROVEMENT_BRIEF.md` §3 item 10: "confirm the live workspace
// preview updates from the paramSchema ranges (reachPercent, azimuthRangeDeg,
// elevationRangeDeg, neglectBias)". Checking this surfaced a real bug: the
// preview read `params['azimuth_range_deg']`/`params['reach_percent_range']`
// -- snake_case keys that appear nowhere in the real manifest's paramSchema
// (`contracts/fixtures/orchard_reach.manifest.json` uses camelCase
// `azimuthRangeDeg`/`reachPercent`) -- so it was permanently stuck on the
// "no workspace geometry" placeholder. This test pins the fix: the real
// Orchard Reach param keys must produce the wedge, not the placeholder, and
// changing the params must change the rendered painter's geometry.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/features/programs/program_builder_screen.dart';

void main() {
  Future<void> pumpPreview(WidgetTester tester, Map<String, dynamic> params) => tester.pumpWidget(
        MaterialApp(home: Scaffold(body: WorkspacePreview(params: params))),
      );

  const orchardReachDefaults = {
    // Real defaults from contracts/fixtures/orchard_reach.manifest.json.
    'reachPercent': [50, 85],
    'azimuthRangeDeg': [-45, 45],
    'elevationRangeDeg': [0, 40],
    'neglectBias': 0,
  };

  testWidgets('real Orchard Reach param keys render the wedge, not the placeholder', (tester) async {
    await pumpPreview(tester, orchardReachDefaults);

    expect(find.text("No workspace geometry in this game's parameters."), findsNothing);
    final painter = tester.widget<CustomPaint>(find.byType(CustomPaint).last).painter! as WorkspaceWedgePainter;
    expect(painter.azimuthRange, (-45.0, 45.0));
    expect(painter.reachRange.$1, closeTo(50 / 110, 1e-9));
    expect(painter.reachRange.$2, closeTo(85 / 110, 1e-9));
  });

  testWidgets('the OLD snake_case keys (pre-fix) correctly fall back to the placeholder', (tester) async {
    // Regression guard the other direction: unknown/legacy keys must never
    // be silently misread as geometry.
    await pumpPreview(tester, const {
      'azimuth_range_deg': [-45, 45],
      'reach_percent_range': [50, 85],
    });
    expect(find.text("No workspace geometry in this game's parameters."), findsOneWidget);
  });

  testWidgets('changing reachPercent/azimuthRangeDeg updates the rendered wedge geometry', (tester) async {
    await pumpPreview(tester, orchardReachDefaults);
    final before =
        tester.widget<CustomPaint>(find.byType(CustomPaint).last).painter! as WorkspaceWedgePainter;

    await pumpPreview(tester, const {
      'reachPercent': [20, 60],
      'azimuthRangeDeg': [-90, 0],
      'elevationRangeDeg': [0, 40],
      'neglectBias': 0,
    });
    final after =
        tester.widget<CustomPaint>(find.byType(CustomPaint).last).painter! as WorkspaceWedgePainter;

    expect(after.azimuthRange, isNot(before.azimuthRange));
    expect(after.reachRange, isNot(before.reachRange));
    expect(after.azimuthRange, (-90.0, 0.0));
    expect(after.reachRange.$1, closeTo(20 / 110, 1e-9));
    expect(after.reachRange.$2, closeTo(60 / 110, 1e-9));
  });

  testWidgets('elevationRangeDeg and neglectBias render as text annotations', (tester) async {
    await pumpPreview(tester, const {
      'reachPercent': [50, 85],
      'azimuthRangeDeg': [-45, 45],
      'elevationRangeDeg': [0, 40],
      'neglectBias': 0.4,
    });

    expect(find.textContaining('Elevation'), findsOneWidget);
    expect(find.textContaining('Elevation'), findsOneWidget);
    expect(find.textContaining('Neglect bias: 0.40'), findsOneWidget);
    expect(find.textContaining('right'), findsOneWidget);
  });

  testWidgets('zero neglectBias states there is no bias in plain language', (tester) async {
    await pumpPreview(tester, const {
      'reachPercent': [50, 85],
      'azimuthRangeDeg': [-45, 45],
      'neglectBias': 0,
    });
    expect(find.textContaining('Neglect bias: none'), findsOneWidget);
  });
}
