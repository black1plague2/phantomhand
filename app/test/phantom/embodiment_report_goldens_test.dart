@Tags(['golden'])
library;

// B19 goldens: the embodiment report on the report screen's black ground
// (dark only: the report screens force black), for
//   min   - contracts/fixtures/sessions/phantom_hand_min/metrics.json (all four rows favour In sync)
//   main  - tools/demo/tests/fixtures/ph_l3_main/metrics.json (real pipeline output: flinch equal)
//   nodea - tools/demo/tests/fixtures/ph_l3_fault_node_a_off/metrics.json (sleeve missed touches: Partial)
// at 390x1250 ("phonetall": the whole page, it scrolls on a phone) and 1280x800.
// Images land in test/phantom/goldens/. Regenerate with:
//   flutter test --update-goldens test/phantom/embodiment_report_goldens_test.dart
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/embodiment.dart';
import 'package:opus_app/features/sessions/embodiment_report.dart';

import 'witness_test_support.dart';

const _fixtures = {
  'min': 'contracts/fixtures/sessions/phantom_hand_min/metrics.json',
  'main': 'tools/demo/tests/fixtures/ph_l3_main/metrics.json',
  'nodea': 'tools/demo/tests/fixtures/ph_l3_fault_node_a_off/metrics.json',
};

const _viewports = {
  'phonetall': Size(390, 1250),
  'tablet': Size(1280, 800),
};

void main() {
  setUpAll(loadWitnessTestFonts);

  for (final fixture in _fixtures.keys) {
    for (final viewport in _viewports.keys) {
      testWidgets('embodiment report $fixture $viewport dark 1.0x', tags: ['golden'], (tester) async {
        tester.view.physicalSize = _viewports[viewport]!;
        tester.view.devicePixelRatio = 1;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });
        final e = Embodiment.tryParseMetrics(jsonDecode(File(repoPath(_fixtures[fixture]!)).readAsStringSync()))!;
        await tester.pumpWidget(
          witnessApp(ListView(padding: const EdgeInsets.all(16), children: [EmbodimentReport(embodiment: e)])),
        );
        await tester.pump();
        expect(tester.takeException(), isNull);
        await expectLater(
          find.byType(MaterialApp),
          matchesGoldenFile('goldens/phantom_embodiment_${fixture}_${viewport}_dark_1.0x.png'),
        );
      });
    }
  }
}
