// "Run Phantom Hand demo", end to end in the real app (router, shell, mock
// repositories, the bundled recording): one tap on the button of the login
// screen plays the operator card through the 14 phases, then the audience
// results mirror, then opens the demo participant's report. Also with a hub
// running and nothing connected, in Hindi, with Stop and Skip, and twice in a row.
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/providers/settings_providers.dart';
import 'package:opus_app/core/router/app_router.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/data/repositories/hub/hub_sessions_repository.dart';
import 'package:opus_app/features/auth/login_screen.dart';
import 'package:opus_app/features/live/phantom_demo_screen.dart';
import 'package:opus_app/features/live/phantom_witness_mirror.dart';
import 'package:opus_app/features/patients/patient_list_screen.dart';
import 'package:opus_app/features/sessions/embodiment_report.dart';

import 'phantom_demo_test_support.dart';
import 'witness_test_support.dart';

const _runDemo = ValueKey('ph-run-demo');

/// What the operator card showed, phase by phase, while the demo played.
class _Live {
  final labels = <String>[];
  final chips = <String?>[];
  final captions = <String>[];
  final stoneInPhase = <int>{};
  final emgRatioInPhase = <int, double>{};
  var badgeAndControlsAlways = true;
  var sensorsShown = true;
}

/// All the text under the widget with [key], joined.
String _allText(WidgetTester tester, String key) => [
      for (final r in tester.widgetList<RichText>(find.descendant(of: find.byKey(ValueKey(key)), matching: find.byType(RichText))))
        r.text.toPlainText(),
    ].join(' | ');

/// Watches the demo from its first phase to the audience mirror, 200 ms at a time.
Future<_Live> _watchLive(WidgetTester tester) async {
  final seen = _Live();
  final phase = find.byKey(const ValueKey('ph-phase'));
  void look() {
    if (phase.evaluate().isEmpty) return;
    seen.badgeAndControlsAlways &= find.byKey(const ValueKey('ph-demo-badge')).evaluate().isNotEmpty &&
        find.byKey(const ValueKey('ph-demo-skip')).evaluate().isNotEmpty &&
        find.byKey(const ValueKey('ph-demo-stop')).evaluate().isNotEmpty;
    seen.sensorsShown &= find.byKey(const ValueKey('ph-node-haptic')).evaluate().isNotEmpty &&
        find.byKey(const ValueKey('ph-node-bio')).evaluate().isNotEmpty;
    final label = tester.widget<Text>(phase).data!;
    if (seen.labels.isEmpty || seen.labels.last != label) {
      seen.labels.add(label);
      final chip = find.descendant(of: find.byKey(const ValueKey('ph-condition')), matching: find.byType(Text));
      seen.chips.add(chip.evaluate().isEmpty ? null : tester.widget<Text>(chip).data);
      seen.captions.add(tester.widget<Text>(find.byKey(const ValueKey('ph-demo-caption'))).data!);
    }
    final index = seen.labels.length - 1;
    if (find.text('Stone lands').evaluate().isNotEmpty) seen.stoneInPhase.add(index);
    final ratio = find.byKey(const ValueKey('ph-resting-emg'));
    if (ratio.evaluate().isNotEmpty) {
      final x = double.parse(tester.widget<Text>(ratio).data!.split('×').first);
      seen.emgRatioInPhase[index] = math.max(seen.emgRatioInPhase[index] ?? 0, x);
    }
  }

  // Signing in (a mock latency) and reading the recording take a few steps; then the demo plays by itself.
  await pumpUntil(tester, phase, max: 40, real: true, step: const Duration(milliseconds: 250));
  look();
  await pumpUntil(tester, find.byType(PhantomWitnessMirror), each: look);
  return seen;
}

const _phaseLabels = [
  'Calibrating',
  'Pointing check, before',
  'Brush and touch',
  'Stone drop',
  'Pointing check, after',
  'Questions',
  'Pointing check, before',
  'Brush and touch',
  'Stone drop',
  'Pointing check, after',
  'Questions',
  'Fading out',
  'Reveal',
  'Results',
];

void _expectLive(_Live live) {
  expect(live.labels, _phaseLabels, reason: 'the 14 phases, in the recording\'s order');
  expect(live.chips, [
    null,
    for (var i = 0; i < 5; i++) 'ASYNC',
    for (var i = 0; i < 5; i++) 'SYNC',
    null,
    null,
    null,
  ], reason: 'delayed first, in sync second, no chip around them');
  expect(live.captions, everyElement(isNotEmpty));
  expect(live.captions[2], contains('late'));
  expect(live.captions[7], contains('together'));
  expect(live.captions[3], contains('stone'));
  expect(live.captions[1], contains('Point to where your hand feels'));
  expect(live.captions[5], contains('rating'));
  expect(live.captions[11], contains('fades'));
  expect(live.captions[12], contains('reveal'));
  expect(live.captions[13], contains('What changed'));
  expect(live.badgeAndControlsAlways, isTrue, reason: '"Demo, simulated data" and Skip / Stop on screen in every phase');
  expect(live.sensorsShown, isTrue, reason: 'the Sleeve and Muscle sensor chips');
  expect(live.stoneInPhase, containsAll([3, 8]), reason: 'the stone is marked on the traces in both stone drops');
  expect(live.emgRatioInPhase[3], inInclusiveRange(5.0, 7.0), reason: 'the delayed flinch, about 6x resting');
  expect(live.emgRatioInPhase[8], inInclusiveRange(5.0, 7.0), reason: 'the in-sync flinch, about 6x resting');
}

/// The audience mirror with the recording\'s own witness_summary (1..7 on the wire, -3..+3 on screen).
Future<void> _expectMirror(WidgetTester tester) async {
  Finder row(String card, String row, String text) =>
      find.descendant(of: find.byKey(ValueKey('ph-witness-$card-$row')), matching: find.text(text));
  expect(find.byKey(const ValueKey('ph-demo-badge')), findsOneWidget);
  expect(find.byKey(const ValueKey('ph-demo-skip')), findsOneWidget);
  expect(row('sync', 'drift', '+3.0 cm'), findsOneWidget);
  expect(row('async', 'drift', '+1.0 cm'), findsOneWidget);
  expect(row('sync', 'flinch', '122 ms'), findsOneWidget, reason: 'the game\'s witness_summary: 121.9 ms');
  expect(row('async', 'flinch', '125 ms'), findsOneWidget, reason: 'the game\'s witness_summary: 124.6 ms');
  expect(row('sync', 'flinch', 'Strong'), findsOneWidget);
  expect(row('async', 'flinch', 'Strong'), findsOneWidget);
  expect(row('sync', 'ownership', '+2.0'), findsOneWidget, reason: 'wire 6 of 1..7');
  expect(row('async', 'ownership', '-1.0'), findsOneWidget, reason: 'wire 3 of 1..7');
  expect(row('sync', 'awareness', '0.0'), findsOneWidget, reason: 'q4 = 4, asked once, after the last condition');
  expect(row('async', 'awareness', 'No data'), findsOneWidget, reason: 'q4 was not asked after the first condition');
  expect(find.text('A pointer, not proof'), findsNWidgets(2));
  expect(find.text('What changed?'), findsOneWidget);
  // The closing lines are the event\'s own, faded in 3 s after the mirror appears.
  await tester.pump(const Duration(seconds: 4));
  expect(find.text('The body changed. The touch changed. The feeling of "mine" changed.'), findsOneWidget);
  expect(find.text('You noticed every change.'), findsOneWidget);
  expect(find.text('Tattva 5: consciousness is beyond the body and mind.'), findsOneWidget);
  expect(find.text('Preliminary'), findsOneWidget);
}

/// The demo participant's report, from the recording's metrics.json.
Future<void> _expectReport(WidgetTester tester) async {
  await pumpUntil(tester, find.byType(EmbodimentReport), real: true, step: const Duration(milliseconds: 250), max: 120);
  await settle(tester, rounds: 3);
  expect(find.byType(PhantomDemoScreen), findsNothing, reason: 'the report replaced the demo');
  expect(find.byKey(const ValueKey('ph-simulated-run')), findsOneWidget);
  expect(find.text('Simulated run'), findsOneWidget);
  expect(find.text('Success %'), findsNothing, reason: 'not the trial layout');
  final verdict = tester.widget<Text>(find.byKey(const ValueKey('ph-embodiment-verdict'))).data!;
  expect(verdict, startsWith('In sync, the hand felt more like yours (6.0 vs 3.0) and your sense of where your hand was moved 2.0 cm'));
  expect(_allText(tester, 'ph-embodiment-drift'), allOf(contains('In sync  +3.0 cm'), contains('Delayed  +1.0 cm')));
  expect(_allText(tester, 'ph-embodiment-ownership'), allOf(contains('In sync  6.0'), contains('Delayed  3.0')));
  expect(_allText(tester, 'ph-embodiment-flinchSize'), allOf(contains('In sync  6.0×'), contains('Delayed  6.0×')));
  expect(_allText(tester, 'ph-embodiment-flinchSpeed'), allOf(contains('In sync  112 ms'), contains('Delayed  115 ms')),
      reason: 'the analytics\' latencies, not the game\'s 122 / 125 ms on the mirror');
  expect(_allText(tester, 'ph-embodiment-awareness'), allOf(contains('In sync  4.0'), contains('Delayed  No data')));
  expect(find.textContaining('consciousness'), findsNothing);
}

/// Taps [button], then plays the demo to the report and checks all three stages.
Future<void> _runAndCheck(WidgetTester tester, Finder button) async {
  final clock = tester.binding.clock;
  final tapped = clock.now();
  await tester.tap(button);
  await tester.pump();
  final live = await _watchLive(tester);
  final mirrorAt = clock.now();
  expect(mirrorAt.difference(tapped).inMilliseconds / 1000, inInclusiveRange(73, 76), reason: 'the card plays the run in about a minute and a quarter');
  _expectLive(live);
  await _expectMirror(tester);
  await _expectReport(tester);
  expect(clock.now().difference(mirrorAt).inMilliseconds / 1000, inInclusiveRange(12, 15), reason: 'the mirror stays up 12 s, then the report opens');
  expect(tester.takeException(), isNull);
}

void main() {
  setUpAll(() async {
    await loadWitnessTestFonts();
    await warmDemoAssets();
  });

  testWidgets('cold start: the login screen has the button, one tap plays the run, the mirror and opens the report', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    await pumpDemoApp(tester, container);
    expect(find.byType(LoginScreen), findsOneWidget);
    expect(find.byKey(_runDemo), findsOneWidget);
    expect(find.text('Run Phantom Hand demo'), findsOneWidget);
    expect(find.text('Administrator'), findsOneWidget, reason: 'the role buttons are still there');

    await _runAndCheck(tester, find.byKey(_runDemo));
  }, timeout: const Timeout(Duration(minutes: 4)));

  testWidgets('twice in a row: the demo participant\'s profile has the button too, and the second run is the same', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    await pumpDemoApp(tester, container);
    await _runAndCheck(tester, find.byKey(_runDemo));

    // Second run, from the demo participant's profile: it lists the session as a simulated run.
    container.read(appRouterProvider).go('/patients/$demoPhantomPatientId');
    await settle(tester);
    expect(find.text('Demo participant (simulated)'), findsOneWidget);
    expect(find.text('Simulated run'), findsOneWidget, reason: 'the profile lists the session');
    expect(find.byKey(_runDemo), findsOneWidget);
    expect(find.text('Success %'), findsNothing, reason: 'no Orchard tiles for a Phantom Hand participant');
    await _runAndCheck(tester, find.byKey(_runDemo));

    // Back from the report returns to the profile it was started from.
    container.read(appRouterProvider).pop();
    await settle(tester, rounds: 3);
    expect(find.text('Demo participant (simulated)'), findsOneWidget);
    expect(find.byKey(_runDemo), findsOneWidget);
  }, timeout: const Timeout(Duration(minutes: 6)));

  testWidgets('hub mode, nothing connected: the same demo, same report, from the same button', (tester) async {
    final empty = Directory.systemTemp.createTempSync('ph_demo_idle_hub_');
    addTearDown(() => empty.deleteSync(recursive: true));
    final container = ProviderContainer(overrides: [hubControllerProvider.overrideWith(() => IdleHub(empty.path))]);
    addTearDown(container.dispose);
    expect(container.read(hubControllerProvider), isA<HubRunning>());
    expect((container.read(hubControllerProvider) as HubRunning).headsets, isEmpty);
    expect((container.read(sessionsRepositoryProvider) as CompositeSessionsRepository).hub, isA<HubSessionsRepository>(),
        reason: 'the hub\'s repository is in the composite');

    await pumpDemoApp(tester, container);
    await _runAndCheck(tester, find.byKey(_runDemo));
  }, timeout: const Timeout(Duration(minutes: 4)));

  testWidgets('Skip goes from the card to the mirror to the report; the report is the same one', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    await pumpDemoApp(tester, container);
    await tester.tap(find.byKey(_runDemo));
    await settle(tester, rounds: 3);
    await tester.pump(const Duration(seconds: 6));
    expect(find.byKey(const ValueKey('ph-phase')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('ph-demo-skip')));
    await tester.pump();
    expect(find.byType(PhantomWitnessMirror), findsOneWidget);
    await tester.tap(find.byKey(const ValueKey('ph-demo-skip')));
    await settle(tester);
    await _expectReport(tester);
  });

  testWidgets('Stop leaves the demo at once: to the patient list when it was the first screen, to where it started otherwise', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    await pumpDemoApp(tester, container);
    await tester.tap(find.byKey(_runDemo));
    await settle(tester, rounds: 3);
    await tester.pump(const Duration(seconds: 8));
    expect(find.byKey(const ValueKey('ph-phase')), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('ph-demo-stop')));
    await settle(tester);
    expect(find.byType(PhantomDemoScreen), findsNothing);
    expect(find.byType(PatientListScreen), findsOneWidget);

    // From the demo participant's profile: Stop returns to the profile.
    container.read(appRouterProvider).go('/patients/$demoPhantomPatientId');
    await settle(tester);
    await tester.tap(find.byKey(_runDemo));
    await settle(tester, rounds: 3);
    await tester.pump(const Duration(seconds: 3));
    await tester.tap(find.byKey(const ValueKey('ph-demo-stop')));
    await settle(tester);
    expect(find.byType(PhantomDemoScreen), findsNothing);
    expect(find.text('Demo participant (simulated)'), findsOneWidget);
    // Nothing keeps running after Stop (a pending timer would fail this test).
  });

  testWidgets('a small phone with large text: login, card, mirror and report lay out with no overflow', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    container.read(textScaleOverrideProvider.notifier).set(2);
    await pumpDemoApp(tester, container, size: const Size(360, 640));
    expect(tester.takeException(), isNull, reason: 'login');
    await tester.tap(find.byKey(_runDemo));
    await settle(tester, rounds: 3);
    await tester.pump(const Duration(seconds: 30));
    expect(find.byKey(const ValueKey('ph-phase')), findsOneWidget);
    expect(tester.takeException(), isNull, reason: 'live card');
    await tester.tap(find.byKey(const ValueKey('ph-demo-skip')));
    await tester.pump(const Duration(seconds: 5));
    expect(find.byType(PhantomWitnessMirror), findsOneWidget);
    expect(tester.takeException(), isNull, reason: 'results mirror');
    await tester.tap(find.byKey(const ValueKey('ph-demo-skip')));
    await settle(tester);
    await pumpUntil(tester, find.byType(EmbodimentReport), real: true, max: 120, step: const Duration(milliseconds: 250));
    expect(tester.takeException(), isNull, reason: 'report');
  });

  testWidgets('Hindi: the button, the badge and the captions are in Hindi', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    container.read(appLocaleProvider.notifier).set(const Locale('hi'));
    await pumpDemoApp(tester, container);
    expect(find.text('फैंटम हैंड डेमो चलाएँ'), findsOneWidget);
    await tester.tap(find.byKey(_runDemo));
    await settle(tester, rounds: 3);
    await tester.pump(const Duration(seconds: 6));
    expect(find.byKey(const ValueKey('ph-demo-badge')), findsOneWidget);
    expect(find.text('डेमो, सिमुलेटेड डेटा'), findsOneWidget);
    expect(find.text('आगे बढ़ें'), findsOneWidget);
    expect(find.text('डेमो रोकें'), findsOneWidget);
    expect(find.textContaining('बताइए'), findsOneWidget, reason: 'the pointing-check caption, in Hindi');

    await tester.tap(find.byKey(const ValueKey('ph-demo-skip')));
    await tester.pump();
    await tester.tap(find.byKey(const ValueKey('ph-demo-skip')));
    await settle(tester);
    await pumpUntil(tester, find.byType(EmbodimentReport), real: true, max: 120, step: const Duration(milliseconds: 250));
    expect(find.text('सिमुलेटेड रन'), findsOneWidget);
    expect(find.byKey(const ValueKey('ph-embodiment-verdict')), findsOneWidget);
  });
}
