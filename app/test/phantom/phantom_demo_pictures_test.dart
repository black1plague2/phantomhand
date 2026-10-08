// Pictures of the Phantom Hand demo flow at 390x844 (a phone), written to
// logs/sessions/screens/ph/app-demo/ so a reviewer can look at them:
//   01 the button, on the first screen (login) and on the demo participant's profile
//   02 the live card in the in-sync induction, badge and caption on screen, the first flinch on the traces
//   03 the audience results mirror (the second card, scrolled to)
//   04 the demo participant's report
// The test also checks that each picture shows what it is named for. It writes
// the files every time it runs; nothing is compared against them.
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/router/app_router.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/features/live/phantom_witness_mirror.dart';
import 'package:opus_app/features/sessions/embodiment_report.dart';

import 'phantom_demo_test_support.dart';
import 'witness_test_support.dart';

final _dir = repoPath('logs/sessions/screens/ph/app-demo');

Future<void> _shoot(WidgetTester tester, GlobalKey boundary, String name) async {
  await tester.runAsync(() async {
    final render = boundary.currentContext!.findRenderObject()! as RenderRepaintBoundary;
    final image = await render.toImage();
    expect(image.width, 390);
    expect(image.height, 844);
    final bytes = (await image.toByteData(format: ui.ImageByteFormat.png))!;
    final file = File('$_dir/$name');
    await file.parent.create(recursive: true);
    await file.writeAsBytes(bytes.buffer.asUint8List());
  });
}

void main() {
  setUpAll(() async {
    await loadWitnessTestFonts();
    await warmDemoAssets();
  });

  testWidgets('the four pictures of the flow', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    final boundary = GlobalKey();
    await pumpDemoApp(tester, container, boundary: boundary);

    // 1. The button on the first screen a cold start lands on.
    expect(find.byKey(const ValueKey('ph-run-demo')), findsOneWidget);
    expect(find.text('Run Phantom Hand demo'), findsOneWidget);
    await _shoot(tester, boundary, '01-button-login.png');

    // 2. The live card, 2.5 s into the in-sync induction.
    await tester.tap(find.byKey(const ValueKey('ph-run-demo')));
    await tester.pump();
    final chip = find.descendant(of: find.byKey(const ValueKey('ph-condition')), matching: find.text('SYNC'));
    await pumpUntil(tester, find.byKey(const ValueKey('ph-phase')), real: true, step: const Duration(milliseconds: 250), max: 40);
    // The in-sync induction (the pointing check before it has the SYNC chip already).
    await pumpUntil(tester, find.text('Touch seen and felt together. The brush touches the virtual hand while the sleeve touches your real arm at the same moment.'), max: 600);
    expect(chip, findsOneWidget);
    await tester.pump(const Duration(milliseconds: 2500));
    expect(find.text('Brush and touch'), findsOneWidget);
    expect(find.byKey(const ValueKey('ph-demo-badge')), findsOneWidget);
    expect(find.text('Demo, simulated data'), findsOneWidget);
    expect(find.textContaining('Touch seen and felt together'), findsOneWidget);
    expect(find.text('Stone lands'), findsOneWidget, reason: 'the first flinch is on the traces');
    await _shoot(tester, boundary, '02-live-card-induction.png');

    // 3. The audience results mirror, closing lines faded in; then the second card.
    await pumpUntil(tester, find.byType(PhantomWitnessMirror));
    await tester.pump(const Duration(seconds: 4, milliseconds: 500));
    expect(find.text('What changed?'), findsOneWidget);
    expect(find.text('+3.0 cm'), findsOneWidget);
    await _shoot(tester, boundary, '03-results-mirror-1.png');
    await tester.drag(find.byType(SingleChildScrollView).last, const Offset(0, -520));
    await tester.pump(const Duration(milliseconds: 500));
    expect(find.text('Delayed'), findsOneWidget);
    expect(find.text('You noticed every change.'), findsOneWidget);
    await _shoot(tester, boundary, '03-results-mirror-2.png');

    // 4. The demo participant's report.
    await pumpUntil(tester, find.byType(EmbodimentReport), real: true, step: const Duration(milliseconds: 250), max: 120);
    await settle(tester, rounds: 4);
    expect(find.text('Simulated run'), findsOneWidget);
    expect(container.read(appRouterProvider).routeInformationProvider.value.uri.toString(), demoPhantomReportPath);
    await _shoot(tester, boundary, '04-report-1.png');
    await tester.drag(find.byType(ListView).last, const Offset(0, -600));
    await tester.pump(const Duration(milliseconds: 500));
    await _shoot(tester, boundary, '04-report-2.png');

    // The button on the demo participant's profile.
    container.read(appRouterProvider).go('/patients/$demoPhantomPatientId');
    await settle(tester);
    expect(find.byKey(const ValueKey('ph-run-demo')), findsOneWidget);
    expect(find.text('Demo participant (simulated)'), findsOneWidget);
    await _shoot(tester, boundary, '01-button-profile.png');
    expect(tester.takeException(), isNull);
  }, timeout: const Timeout(Duration(minutes: 4)));
}
