@Tags(['golden'])
library;

// B11 goldens: the audience witness mirror at 390x844 (phone) and 1280x800
// (laptop), light and dark, for three data cases:
//   fixture - the real witness_summary line of contracts/fixtures/sessions/phantom_hand_min
//             (it predates flinch latency / strength and q5, so those read "No data")
//   full    - the same numbers completed with latency, strength and a q5 (the format the headset sends)
//   nodata  - no summary at all
// Taken 5 s after the mirror appears, so the closing lines have faded in.
// Images land in test/phantom/goldens/. Regenerate with:
//   flutter test --update-goldens test/phantom/phantom_witness_mirror_goldens_test.dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:opus_app/features/live/phantom_witness_mirror.dart';

import 'witness_test_support.dart';

const _viewports = {
  'phone': Size(390, 844),
  'tablet': Size(1280, 800),
};

Future<void> _golden(
  WidgetTester tester, {
  required String variant,
  required PhantomWitness? summary,
  required String viewport,
  required Brightness brightness,
}) async {
  tester.view.physicalSize = _viewports[viewport]!;
  tester.view.devicePixelRatio = 1;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(witnessApp(PhantomWitnessMirror(summary: summary, lang: 'en'), brightness: brightness));
  await tester.pump();
  await tester.pump(const Duration(seconds: 5));
  expect(tester.takeException(), isNull);
  await expectLater(
    find.byType(MaterialApp),
    matchesGoldenFile('goldens/phantom_witness_${variant}_${viewport}_${brightness.name}_1.0x.png'),
  );
}

void main() {
  setUpAll(loadWitnessTestFonts);

  final variants = <String, PhantomWitness? Function()>{
    'fixture': fixtureWitness,
    'full': fullWitness,
    'nodata': () => null,
  };

  for (final variant in variants.keys) {
    for (final viewport in _viewports.keys) {
      for (final brightness in Brightness.values) {
        testWidgets('witness mirror $variant $viewport ${brightness.name} 1.0x', tags: ['golden'], (tester) async {
          await _golden(
            tester,
            variant: variant,
            summary: variants[variant]!(),
            viewport: viewport,
            brightness: brightness,
          );
        });
      }
    }
  }
}
