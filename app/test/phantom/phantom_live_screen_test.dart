import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:opus_app/features/live/live_monitor_screen.dart';
import 'package:opus_app/features/live/phantom_live_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';

/// Test repository: snapshots are pushed by hand, commands are recorded and
/// answered by completers the test controls (so "sent" can be observed).
class _FakePhantomRepo implements PhantomLiveRepository {
  final controller = StreamController<PhantomLiveSnapshot>.broadcast();
  final sent = <(PhantomCommand, Map<String, dynamic>?)>[];
  final _pending = <Completer<bool>>[];

  @override
  Stream<PhantomLiveSnapshot> watch() => controller.stream;

  @override
  Future<bool> send(PhantomCommand command, {Map<String, dynamic>? params}) {
    sent.add((command, params));
    final c = Completer<bool>();
    _pending.add(c);
    return c.future;
  }

  void answer(bool ok) => _pending.removeAt(0).complete(ok);

  void push(PhantomLiveSnapshot s) => controller.add(s);
}

PhantomLiveSnapshot _snap(
  PhantomRunState state, {
  String phase = 'induction',
  PhantomCondition? condition = PhantomCondition.sync,
  double? remaining = 41.5,
  bool haptic = true,
  bool bio = true,
  double? emg = 0.07,
  bool connected = true,
  TraceChunk? chunk,
  List<TraceMarker> markers = const [],
}) =>
    PhantomLiveSnapshot(
      runState: state,
      connected: connected,
      game: PhantomGameState(
        phase: phase,
        condition: condition,
        remainingS: remaining,
        hapticConnected: haptic,
        bioConnected: bio,
        emgLevel: emg,
      ),
      chunk: chunk,
      markers: markers,
    );

Widget _app(Widget home, {Locale locale = const Locale('en'), double textScale = 1}) => MaterialApp(
      theme: AppTheme.dark(),
      locale: locale,
      localizationsDelegates: const [
        ...AppLocalizations.localizationsDelegates,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      supportedLocales: AppLocalizations.supportedLocales,
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(textScale)),
        child: child!,
      ),
      home: home,
    );

Future<void> _size(WidgetTester tester, Size size) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

Future<_FakePhantomRepo> _pump(
  WidgetTester tester, {
  Size size = const Size(360, 800),
  double textScale = 1,
  Locale locale = const Locale('en'),
  PhantomLiveSnapshot? first,
  bool observer = false,
}) async {
  await _size(tester, size);
  final repo = _FakePhantomRepo();
  addTearDown(repo.controller.close);
  await tester.pumpWidget(_app(PhantomLiveScreen(repository: repo, initialObserver: observer), locale: locale, textScale: textScale));
  repo.push(first ?? _snap(PhantomRunState.paired, phase: 'calibrate', condition: null, remaining: null));
  await tester.pump();
  return repo;
}

bool _enabled(WidgetTester tester, PhantomCommand c) {
  final finder = find.byKey(ValueKey('ph-cmd-${c.wire}'));
  final w = tester.widget(finder);
  if (w is ButtonStyleButton) return w.onPressed != null;
  throw StateError('not a button: $w');
}

Future<void> _tap(WidgetTester tester, PhantomCommand c) async {
  final f = find.byKey(ValueKey('ph-cmd-${c.wire}'));
  await tester.ensureVisible(f);
  await tester.tap(f);
  await tester.pump();
}

void main() {
  testWidgets('before the run only Start and the order selector are live', (tester) async {
    await _pump(tester);
    expect(_enabled(tester, PhantomCommand.start), isTrue);
    for (final c in [
      PhantomCommand.phaseNext,
      PhantomCommand.abortPhase,
      PhantomCommand.pause,
      PhantomCommand.resume,
      PhantomCommand.end,
      PhantomCommand.nextPerson,
    ]) {
      expect(_enabled(tester, c), isFalse, reason: c.wire);
    }
    final order = tester.widget<SegmentedButton<String>>(find.byKey(const ValueKey('ph-order')));
    expect(order.onSelectionChanged, isNotNull);
    expect(order.selected, {'async_first'}, reason: 'spec D9 default');
  });

  testWidgets('shows phase, SYNC chip, time left and node chips from game_state', (tester) async {
    await _pump(tester, first: _snap(PhantomRunState.running));
    expect(find.text('Brush and touch'), findsOneWidget);
    expect(find.text('SYNC'), findsOneWidget);
    expect(find.text('0:42'), findsOneWidget, reason: '41.5 s rounds up like a countdown');
    expect(find.text('Sleeve'), findsOneWidget);
    expect(find.text('Muscle sensor'), findsOneWidget);
    expect(find.text('Connected'), findsNWidgets(2));
    expect(find.text('7%'), findsOneWidget);
  });

  testWidgets('ASYNC chip, offline nodes and a missing countdown', (tester) async {
    await _pump(
      tester,
      first: _snap(PhantomRunState.running, phase: 'questionnaire', condition: PhantomCondition.async, remaining: null, haptic: false, bio: false, emg: null),
    );
    expect(find.text('ASYNC'), findsOneWidget);
    expect(find.text('Questions'), findsOneWidget);
    expect(find.text('—'), findsOneWidget);
    expect(find.text('Offline'), findsNWidgets(2));
    expect(find.text('Muscle activity'), findsNothing);
  });

  testWidgets('while running: phase buttons live, order locked once the induction has begun', (tester) async {
    await _pump(tester, first: _snap(PhantomRunState.running));
    expect(_enabled(tester, PhantomCommand.start), isFalse);
    expect(_enabled(tester, PhantomCommand.phaseNext), isTrue);
    expect(_enabled(tester, PhantomCommand.abortPhase), isTrue);
    expect(_enabled(tester, PhantomCommand.pause), isTrue);
    expect(_enabled(tester, PhantomCommand.resume), isFalse);
    expect(_enabled(tester, PhantomCommand.end), isTrue);
    final order = tester.widget<SegmentedButton<String>>(find.byKey(const ValueKey('ph-order')));
    expect(order.onSelectionChanged, isNull);
  });

  testWidgets('paused swaps Pause for Resume', (tester) async {
    await _pump(tester, first: _snap(PhantomRunState.paused));
    expect(_enabled(tester, PhantomCommand.pause), isFalse);
    expect(_enabled(tester, PhantomCommand.resume), isTrue);
  });

  testWidgets('command shows Sent, then Confirmed on ack, and sends the right command', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.running));
    await _tap(tester, PhantomCommand.phaseNext);
    expect(repo.sent.single.$1, PhantomCommand.phaseNext);
    expect(find.text('Sent'), findsOneWidget);
    expect(_enabled(tester, PhantomCommand.phaseNext), isFalse, reason: 'no double send while in flight');
    repo.answer(true);
    await tester.pump();
    expect(find.text('Confirmed'), findsOneWidget);
    expect(find.text('Sent'), findsNothing);
    expect(_enabled(tester, PhantomCommand.phaseNext), isTrue);
  });

  testWidgets('a rejected or lost command shows Failed', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.running));
    await _tap(tester, PhantomCommand.abortPhase);
    repo.answer(false);
    await tester.pump();
    expect(find.text('Failed'), findsOneWidget);
    expect(repo.sent.single.$1, PhantomCommand.abortPhase);
  });

  testWidgets('each button keeps its own status', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.running));
    await _tap(tester, PhantomCommand.pause);
    repo.answer(true);
    await tester.pump();
    await _tap(tester, PhantomCommand.abortPhase);
    repo.answer(false);
    await tester.pump();
    expect(find.text('Confirmed'), findsOneWidget);
    expect(find.text('Failed'), findsOneWidget);
  });

  testWidgets('End asks first; cancelling sends nothing, confirming sends stop', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.running));
    await _tap(tester, PhantomCommand.end);
    expect(find.text('End this session?'), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(repo.sent, isEmpty);

    await _tap(tester, PhantomCommand.end);
    await tester.tap(find.text('Confirm'));
    await tester.pumpAndSettle();
    expect(repo.sent.single.$1, PhantomCommand.end);
    expect(PhantomCommand.end.wire, 'stop');
  });

  testWidgets('Next person: no dialog once finished, a dialog mid-run', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.finished, phase: 'done', remaining: null));
    await _tap(tester, PhantomCommand.nextPerson);
    expect(find.text('Start the next person?'), findsNothing);
    expect(repo.sent.single.$1, PhantomCommand.nextPerson);
    repo
      ..answer(true)
      ..push(_snap(PhantomRunState.running));
    await tester.pump();
    await _tap(tester, PhantomCommand.nextPerson);
    expect(find.text('Start the next person?'), findsOneWidget);
  });

  testWidgets('condition order sends set_condition_order with the chosen value and remembers it', (tester) async {
    final repo = await _pump(tester);
    final seg = find.text('Sync first');
    await tester.ensureVisible(seg);
    await tester.tap(seg);
    await tester.pump();
    expect(repo.sent.single.$1, PhantomCommand.conditionOrder);
    expect(repo.sent.single.$2, {'condition_order': 'sync_first'});
    repo.answer(true);
    await tester.pump();
    final order = tester.widget<SegmentedButton<String>>(find.byKey(const ValueKey('ph-order')));
    expect(order.selected, {'sync_first'});
    expect(find.text('Confirmed'), findsOneWidget);
  });

  testWidgets('everything is disabled when the headset is offline, with a tag in the app bar', (tester) async {
    await _pump(tester, first: _snap(PhantomRunState.running, connected: false));
    for (final c in PhantomCommand.values.where((c) => c != PhantomCommand.conditionOrder)) {
      expect(_enabled(tester, c), isFalse, reason: c.wire);
    }
    expect(find.text('Headset offline'), findsOneWidget);
  });

  testWidgets('traces: empty state, then values with markers once a chunk arrives', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.running));
    expect(find.text('Waiting for signal'), findsNWidgets(2));
    repo.push(
      _snap(
        PhantomRunState.running,
        chunk: TraceChunk(emgEnv: List.filled(10, 431), accelMag: List.filled(10, 9.8), t0Ms: 0),
        markers: const [TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 200)],
      ),
    );
    await tester.pump();
    expect(find.text('Waiting for signal'), findsNothing);
    expect(find.text('431'), findsOneWidget);
    expect(find.text('9.8 m/s²'), findsOneWidget);
    expect(find.byKey(const ValueKey('ph-plot-emg')), findsOneWidget);
    expect(find.text('Stone lands'), findsOneWidget);
    expect(find.text('Muscle burst'), findsOneWidget);
  });

  testWidgets('observer mode hides the controls and the section titles, keeps SYNC, and can be left', (tester) async {
    await _pump(tester, first: _snap(PhantomRunState.running));
    expect(find.text('Controls'), findsOneWidget);
    await tester.tap(find.byTooltip('Observer view'));
    await tester.pump();
    expect(find.text('Controls'), findsNothing);
    expect(find.byKey(const ValueKey('ph-cmd-start')), findsNothing);
    expect(find.text('SYNC'), findsOneWidget);
    expect(find.byKey(const ValueKey('ph-time-left')), findsOneWidget);
    expect(find.byKey(const ValueKey('ph-plot-emg')), findsOneWidget);
    await tester.tap(find.byTooltip('Exit observer view'));
    await tester.pump();
    expect(find.text('Controls'), findsOneWidget);
  });

  testWidgets('observer mode renders on a projector-sized screen without overflow', (tester) async {
    await _pump(tester, size: const Size(1600, 1000), observer: true, first: _snap(PhantomRunState.running));
    expect(tester.takeException(), isNull);
    expect(find.text('SYNC'), findsOneWidget);
    final chip = tester.getSize(find.byKey(const ValueKey('ph-condition')));
    expect(chip.height, greaterThan(60), reason: 'condition chip is large enough for 2 m');
  });

  for (final size in const [Size(360, 800), Size(390, 844), Size(1280, 800), Size(1600, 1000)]) {
    for (final scale in const [1.0, 2.0]) {
      testWidgets('no overflow at ${size.width.toInt()}x${size.height.toInt()} text scale $scale', (tester) async {
        await _pump(
          tester,
          size: size,
          textScale: scale,
          first: _snap(
            PhantomRunState.running,
            chunk: TraceChunk(emgEnv: List.generate(40, (i) => 410.0 + i), accelMag: List.filled(40, 9.8), t0Ms: 0),
          ),
        );
        expect(tester.takeException(), isNull);
      });
    }
  }

  testWidgets('Hindi labels', (tester) async {
    await _pump(tester, locale: const Locale('hi'), first: _snap(PhantomRunState.running));
    expect(find.text('ब्रश और स्पर्श'), findsOneWidget);
    expect(find.text('अगला चरण'), findsOneWidget);
    expect(find.text('जुड़ा है'), findsNWidgets(2));
    expect(find.text('SYNC'), findsOneWidget);
  });

  testWidgets('LiveMonitorScreen shows the operator card for the scripted demo session and stops its timer on dispose', (tester) async {
    await _size(tester, const Size(390, 844));
    await tester.pumpWidget(
      ProviderScope(
        child: _app(const LiveMonitorScreen(patientId: 'demo', sessionId: mockPhantomSessionId)),
      ),
    );
    await tester.pump(const Duration(milliseconds: 500));
    expect(find.text('Phantom Hand'), findsOneWidget);
    expect(find.text('Demo, no headset'), findsOneWidget);
    expect(_enabled(tester, PhantomCommand.start), isTrue);

    // Drive the demo: Start, then the clock moves.
    await _tap(tester, PhantomCommand.start);
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pump(const Duration(milliseconds: 400));
    expect(find.text('Calibrating'), findsOneWidget);
    expect(_enabled(tester, PhantomCommand.phaseNext), isTrue);

    await tester.pumpWidget(const SizedBox());
  });
}
