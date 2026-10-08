import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:opus_app/features/live/live_monitor_screen.dart';
import 'package:opus_app/features/live/phantom_live_screen.dart';
import 'package:opus_app/features/live/phantom_trace_plot.dart';
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
  int? rtt,
  DateTime? at,
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
      rttMs: rtt,
      receivedAt: at,
    );

/// [n] samples at 20 Hz from [t0]: EMG rests at 420 and |accel| at 9.8; the last
/// [flinchLast] samples are a flinch (2100 and 14.0).
TraceChunk _rest(int n, {double t0 = 0, int flinchLast = 0}) => TraceChunk(
      emgEnv: [for (var i = 0; i < n; i++) if (i >= n - flinchLast) 2100.0 else 420.0],
      accelMag: [for (var i = 0; i < n; i++) if (i >= n - flinchLast) 14.0 else 9.8],
      t0Ms: t0,
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
  DateTime Function()? clock,
}) async {
  await _size(tester, size);
  final repo = _FakePhantomRepo();
  addTearDown(repo.controller.close);
  await tester.pumpWidget(
    _app(PhantomLiveScreen(repository: repo, initialObserver: observer, clock: clock), locale: locale, textScale: textScale),
  );
  repo.push(first ?? _snap(PhantomRunState.paired, phase: 'calibrate', condition: null, remaining: null));
  await tester.pump();
  return repo;
}

/// Hands [s] to the screen and renders it. The stream delivers on a microtask,
/// which a lone `pump()` right after a timed pump does not draw.
Future<void> _push(WidgetTester tester, _FakePhantomRepo repo, PhantomLiveSnapshot s) async {
  repo.push(s);
  await tester.pump();
  await tester.pump();
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

  // B3 (R3 D7): before the first Start the headset reports phase `idle` and no condition.
  testWidgets('B3: idle reads "Waiting to start", not the raw id, and there is no "No condition" slab', (tester) async {
    await _pump(tester, first: _snap(PhantomRunState.ready, phase: 'idle', condition: null, remaining: null));
    expect(find.text('Waiting to start'), findsOneWidget);
    expect(find.text('idle'), findsNothing);
    expect(find.byKey(const ValueKey('ph-condition')), findsNothing);
    expect(find.text('No condition'), findsNothing);
    expect(find.text('—'), findsOneWidget, reason: 'the countdown slot stays');
  });

  testWidgets('B3: the condition chip comes back with the first induction', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.ready, phase: 'idle', condition: null, remaining: null));
    expect(find.byKey(const ValueKey('ph-condition')), findsNothing);
    repo.push(_snap(PhantomRunState.running, condition: PhantomCondition.async));
    await tester.pump();
    expect(find.byKey(const ValueKey('ph-condition')), findsOneWidget);
    expect(find.text('ASYNC'), findsOneWidget);
  });

  testWidgets('B3: the audience view shows no "No condition" slab before the induction either', (tester) async {
    await _pump(
      tester,
      size: const Size(1600, 1000),
      observer: true,
      first: _snap(PhantomRunState.ready, phase: 'idle', condition: null, remaining: null),
    );
    expect(tester.takeException(), isNull);
    expect(find.text('Waiting to start'), findsOneWidget);
    expect(find.byKey(const ValueKey('ph-condition')), findsNothing);
  });

  testWidgets('B3: Hindi reads the waiting label too', (tester) async {
    await _pump(tester, locale: const Locale('hi'), first: _snap(PhantomRunState.ready, phase: 'idle', condition: null, remaining: null));
    expect(find.text('शुरू होने की प्रतीक्षा'), findsOneWidget);
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

  testWidgets('everything is disabled when the headset is offline, with the Offline pill in the app bar', (tester) async {
    await _pump(tester, first: _snap(PhantomRunState.running, connected: false));
    for (final c in PhantomCommand.values.where((c) => c != PhantomCommand.conditionOrder)) {
      expect(_enabled(tester, c), isFalse, reason: c.wire);
    }
    expect(find.descendant(of: find.byKey(const ValueKey('ph-link')), matching: find.text('Offline')), findsOneWidget);
  });

  // B12 (R3 D5, D17): the operator can tell a live card from a frozen one, and an offline node says what to do.
  group('B12 link health', () {
    final t0 = DateTime(2026, 10, 8, 14);
    Finder dotOf(String id) => find.descendant(
          of: find.byKey(ValueKey('ph-plot-$id')),
          matching: find.byWidgetPredicate((w) => w is Container && w.decoration is BoxDecoration && (w.decoration! as BoxDecoration).shape == BoxShape.circle),
        );
    Color traceTint(WidgetTester tester, String id) => (tester.widget<Container>(dotOf(id).first).decoration! as BoxDecoration).color!;
    final grey = OpusTokens.dark.slate;

    testWidgets('live: "Quest 18 ms" with a round trip, plain "Quest" without one', (tester) async {
      final now = t0.add(const Duration(milliseconds: 500));
      final repo = await _pump(tester, clock: () => now, first: _snap(PhantomRunState.running, rtt: 18, at: t0));
      expect(find.descendant(of: find.byKey(const ValueKey('ph-link')), matching: find.text('Quest 18 ms')), findsOneWidget);
      repo.push(_snap(PhantomRunState.running, at: t0));
      await tester.pump();
      expect(find.text('Quest'), findsOneWidget);
      expect(find.textContaining('ms'), findsNothing);
    });

    testWidgets('a quiet link turns the pill amber on its own, the traces grey, and the next status restores them', (tester) async {
      var now = t0;
      final repo = await _pump(tester, clock: () => now, first: _snap(PhantomRunState.running, rtt: 18, at: t0, chunk: _rest(400)));
      expect(find.text('Quest 18 ms'), findsOneWidget);
      expect(traceTint(tester, 'emg'), OpusTokens.metricColorV3('emg'));
      expect(traceTint(tester, 'accel'), OpusTokens.metricColorV3('accel'));

      // Nothing arrives for 4 s: only the 1 s tick re-reads the clock.
      now = t0.add(const Duration(seconds: 4, milliseconds: 300));
      await tester.pump(const Duration(seconds: 1));
      expect(find.text('No update 4 s'), findsOneWidget);
      expect(find.text('Quest 18 ms'), findsNothing);
      expect(traceTint(tester, 'emg'), grey);
      expect(traceTint(tester, 'accel'), grey);

      await _push(tester, repo, _snap(PhantomRunState.running, rtt: 12, at: now));
      expect(find.text('Quest 12 ms'), findsOneWidget);
      expect(find.textContaining('No update'), findsNothing);
      expect(traceTint(tester, 'emg'), OpusTokens.metricColorV3('emg'));
    });

    testWidgets('under 2 s of silence is still live (statuses come every 0.5 s)', (tester) async {
      var now = t0;
      await _pump(tester, clock: () => now, first: _snap(PhantomRunState.running, rtt: 18, at: t0));
      now = t0.add(const Duration(milliseconds: 1900));
      await tester.pump(const Duration(seconds: 1));
      expect(find.text('Quest 18 ms'), findsOneWidget);
      now = t0.add(const Duration(seconds: 2));
      await tester.pump(const Duration(seconds: 1));
      expect(find.text('No update 2 s'), findsOneWidget);
    });

    testWidgets('a lost link: red "Offline" pill and grey traces (the node chips are untouched)', (tester) async {
      await _pump(tester, first: _snap(PhantomRunState.running, connected: false, chunk: _rest(400)));
      expect(find.text('Offline'), findsOneWidget, reason: 'the pill only: both nodes were reported connected');
      expect(traceTint(tester, 'emg'), grey);
    });

    testWidgets('the audience view has no pill but its traces still turn grey when stale', (tester) async {
      var now = t0;
      await _pump(
        tester,
        size: const Size(1600, 1000),
        observer: true,
        clock: () => now,
        first: _snap(PhantomRunState.running, rtt: 18, at: t0, chunk: _rest(400)),
      );
      expect(find.byKey(const ValueKey('ph-link')), findsNothing);
      expect(traceTint(tester, 'emg'), OpusTokens.metricColorV3('emg'));
      now = t0.add(const Duration(seconds: 5));
      await tester.pump(const Duration(seconds: 1));
      expect(traceTint(tester, 'emg'), grey);
    });

    testWidgets('Hindi pill words', (tester) async {
      var now = t0;
      await _pump(tester, locale: const Locale('hi'), clock: () => now, first: _snap(PhantomRunState.running, rtt: 18, at: t0));
      expect(find.text('क्वेस्ट 18 ms'), findsOneWidget);
      now = t0.add(const Duration(seconds: 4));
      await tester.pump(const Duration(seconds: 1));
      expect(find.text('4 सेकंड से कोई अपडेट नहीं'), findsOneWidget);
    });

    const fix = 'Not reachable. Check its power cable and the hotspot.';

    testWidgets('an offline node says what to do next to "Offline"; a connected one does not', (tester) async {
      await _pump(tester, first: _snap(PhantomRunState.running, haptic: false));
      expect(find.descendant(of: find.byKey(const ValueKey('ph-node-haptic')), matching: find.text('Offline')), findsOneWidget);
      expect(find.descendant(of: find.byKey(const ValueKey('ph-node-haptic')), matching: find.text(fix)), findsOneWidget);
      expect(find.descendant(of: find.byKey(const ValueKey('ph-node-bio')), matching: find.text(fix)), findsNothing);
    });

    testWidgets('the audience view says Offline for a node but gives no instructions', (tester) async {
      await _pump(tester, size: const Size(1600, 1000), observer: true, first: _snap(PhantomRunState.running, haptic: false, bio: false));
      expect(find.text('Offline'), findsNWidgets(2));
      expect(find.text(fix), findsNothing);
    });

    testWidgets('Hindi node fix', (tester) async {
      await _pump(tester, locale: const Locale('hi'), first: _snap(PhantomRunState.running, bio: false));
      expect(find.text('पहुँच से बाहर। इसकी पावर केबल और हॉटस्पॉट जाँचें।'), findsOneWidget);
    });
  });

  testWidgets('traces: empty state, then values with markers once a chunk arrives', (tester) async {
    final repo = await _pump(tester, first: _snap(PhantomRunState.running));
    expect(find.text('Waiting for signal'), findsNWidgets(2));
    repo.push(
      _snap(
        PhantomRunState.running,
        chunk: TraceChunk(emgEnv: List.filled(10, 431), accelMag: List.filled(10, 9.8), t0Ms: 0),
        markers: const [
          TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 200),
          TraceMarker(kind: TraceMarkerKind.emgBurst, tMs: 320),
        ],
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

  group('B8 traces that prove the flinch', () {
    Finder inPlot(String id, Finder f) => find.descendant(of: find.byKey(ValueKey('ph-plot-$id')), matching: f);
    const impact = TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 17000);
    const burst = TraceMarker(kind: TraceMarkerKind.emgBurst, tMs: 17120);

    PhantomTracePlot plotOf(WidgetTester tester, String id) => tester.widget<PhantomTracePlot>(inPlot(id, find.byType(PhantomTracePlot)));

    testWidgets("EMG axis: the plain default until a sample, then the data's own range with its real numbers; |accel| keeps its fixed scale",
        (tester) async {
      final repo = await _pump(tester, first: _snap(PhantomRunState.running));
      expect(inPlot('emg', find.text('3000')), findsOneWidget);
      expect(inPlot('emg', find.text('0')), findsOneWidget);
      expect(inPlot('accel', find.text('25')), findsOneWidget);

      // Rest at 420: the axis closes in on it (60 counts at least) and says so.
      repo.push(_snap(PhantomRunState.running, chunk: _rest(400)));
      await tester.pump();
      expect(inPlot('emg', find.text('450')), findsOneWidget);
      expect(inPlot('emg', find.text('390')), findsOneWidget);
      expect(inPlot('emg', find.text('3000')), findsNothing);
      expect(inPlot('accel', find.text('25')), findsOneWidget);
      expect(inPlot('accel', find.text('0')), findsOneWidget);

      // A flinch to 2100: the axis widens in the same message, so the spike is not clipped. |accel| stays put.
      repo.push(_snap(PhantomRunState.running, chunk: _rest(20, t0: 20000, flinchLast: 20)));
      await tester.pump();
      expect(inPlot('emg', find.text('2270')), findsOneWidget);
      expect(inPlot('emg', find.text('250')), findsOneWidget);
      expect(inPlot('accel', find.text('25')), findsOneWidget);
      expect(inPlot('accel', find.text('0')), findsOneWidget);

      // The labels are the numbers the plot draws with.
      expect((plotOf(tester, 'emg').yMin, plotOf(tester, 'emg').yMax), (250, 2270));
      expect((plotOf(tester, 'accel').yMin, plotOf(tester, 'accel').yMax), (0, 25));
    });

    testWidgets('a contraction of +260 on a rest of 230 is drawn tall, with the rest line low on the same scale', (tester) async {
      final emg = [
        for (var i = 0; i < 400; i++) 230 + 10 * math.sin(i * 1.7) + (i >= 330 && i < 350 ? 260 * math.sin(math.pi * (i - 330) / 20) : 0),
      ];
      await _pump(tester, first: _snap(PhantomRunState.running, chunk: TraceChunk(emgEnv: emg, accelMag: List.filled(400, 9.8), t0Ms: 0)));

      // Paint the EMG plot into a 300 x 100 test canvas and read back what it drew.
      final painter = tester.widget<CustomPaint>(inPlot('emg', find.byType(CustomPaint))).painter!;
      final canvas = TestRecordingCanvas();
      painter.paint(canvas, const Size(300, 100));
      final calls = [for (final c in canvas.invocations) c.invocation];
      Paint paintOf(Invocation c) => c.positionalArguments.last as Paint;

      final trace = calls
          .where((c) => c.memberName == #drawPath && paintOf(c).style == PaintingStyle.stroke)
          .map((c) => (c.positionalArguments[0] as Path).getBounds())
          .single;
      expect(trace.height, greaterThan(70), reason: 'on 0 to 3000 this contraction was 9 of the 100');
      expect(trace.top, lessThan(15), reason: 'the peak is near the top edge');

      // The dashed rest line is at the rest level on that same scale (the 8-bit colour: a Paint rounds it).
      final plot = plotOf(tester, 'emg');
      expect(plot.baseline, closeTo(230, 3));
      final slate = OpusTokens.dark.slate.toARGB32();
      final restLines = calls.where((c) => c.memberName == #drawLine && paintOf(c).color.toARGB32() == slate);
      expect(restLines, isNotEmpty);
      final expectedY = 99 - (plot.baseline! - plot.yMin) / (plot.yMax - plot.yMin) * 98;
      for (final line in restLines) {
        expect((line.positionalArguments[0] as Offset).dy, closeTo(expectedY, 1e-6));
      }
      expect(expectedY, greaterThan(trace.top + 60), reason: 'rest sits low, the spike stands above it');
    });

    testWidgets('no samples: the plot looks as it did; one sample: a sane axis, no caption, no exception', (tester) async {
      final repo = await _pump(tester, first: _snap(PhantomRunState.running));
      expect(inPlot('emg', find.text('Waiting for signal')), findsOneWidget);
      expect(inPlot('emg', find.text('3000')), findsOneWidget);
      expect(inPlot('emg', find.text('0')), findsOneWidget);
      expect(find.byKey(const ValueKey('ph-resting-emg')), findsNothing);
      expect(plotOf(tester, 'emg').baseline, isNull);

      repo.push(_snap(PhantomRunState.running, chunk: const TraceChunk(emgEnv: [231], accelMag: [9.8], t0Ms: 0)));
      await tester.pump();
      expect(tester.takeException(), isNull);
      expect(inPlot('emg', find.text('Waiting for signal')), findsNothing);
      expect(inPlot('emg', find.text('231')), findsOneWidget);
      expect(inPlot('emg', find.text('270')), findsOneWidget);
      expect(inPlot('emg', find.text('200')), findsOneWidget);
      expect(find.byKey(const ValueKey('ph-resting-emg')), findsNothing);
      expect(plotOf(tester, 'emg').baseline, isNull);
    });

    testWidgets('"x resting" is read off the trace: none under 2 s of samples, then the latest value over the median', (tester) async {
      final repo = await _pump(tester, first: _snap(PhantomRunState.running, chunk: _rest(30)));
      expect(find.byKey(const ValueKey('ph-resting-emg')), findsNothing);

      repo.push(_snap(PhantomRunState.running, chunk: _rest(370, t0: 1500, flinchLast: 20)));
      await tester.pump();
      expect(find.text('5.0× resting'), findsOneWidget, reason: '2100 over a resting level of 420');
      expect(inPlot('emg', find.text('5.0× resting')), findsOneWidget);
      expect(inPlot('accel', find.text('1.4× resting')), findsOneWidget, reason: '14.0 over 9.8');
    });

    testWidgets('on the EMG plot "x resting" is the newest peak, so it outlives the contraction by 5 s; |accel| still shows its latest value',
        (tester) async {
      // 19 s at rest (420), then a flinch to 2100 for the last second.
      final repo = await _pump(tester, first: _snap(PhantomRunState.running, chunk: _rest(400, flinchLast: 20)));
      expect(inPlot('emg', find.text('5.0× resting')), findsOneWidget);
      expect(inPlot('accel', find.text('1.4× resting')), findsOneWidget);

      // 3 s on the signal is back at rest, the flinch is within the last 5 s: still reported.
      repo.push(_snap(PhantomRunState.running, chunk: _rest(60, t0: 20000)));
      await tester.pump();
      expect(inPlot('emg', find.text('420')), findsOneWidget, reason: 'the latest sample is at rest');
      expect(inPlot('emg', find.text('5.0× resting')), findsOneWidget);
      expect(inPlot('accel', find.text('1.0× resting')), findsOneWidget, reason: 'its latest value is back at rest');

      // 4 s later still: older than 5 s, so back to 1.0. The axis keeps the flinch in view while it is in the window.
      repo.push(_snap(PhantomRunState.running, chunk: _rest(80, t0: 23000)));
      await tester.pump();
      expect(inPlot('emg', find.text('1.0× resting')), findsOneWidget);
      expect(inPlot('emg', find.text('2270')), findsOneWidget);
    });

    testWidgets('the latest stone and burst are named once, on the EMG plot, just left of their line', (tester) async {
      await _pump(tester, first: _snap(PhantomRunState.running, chunk: _rest(400), markers: const [impact, burst]));
      expect(find.text('Stone lands'), findsOneWidget);
      expect(find.text('Muscle burst'), findsOneWidget);
      expect(inPlot('emg', find.text('Stone lands')), findsOneWidget);
      expect(inPlot('accel', find.text('Stone lands')), findsNothing);

      final canvas = tester.getRect(inPlot('emg', find.byType(CustomPaint)));
      final markerX = canvas.left + (17000 - (19950 - 20000)) / 20000 * canvas.width;
      expect(tester.getTopRight(find.text('Stone lands')).dx, lessThan(markerX));
      expect(tester.getTopRight(find.text('Muscle burst')).dx, lessThan(canvas.left + (17120 + 50) / 20000 * canvas.width));
      expect(tester.getTopLeft(find.text('Muscle burst')).dy, greaterThan(tester.getTopLeft(find.text('Stone lands')).dy), reason: 'two rows, no overlap');
    });

    testWidgets('a marker at the left edge is named on its right instead, and stays inside the plot', (tester) async {
      await _pump(
        tester,
        first: _snap(PhantomRunState.running, chunk: _rest(400), markers: const [TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 300)]),
      );
      final canvas = tester.getRect(inPlot('emg', find.byType(CustomPaint)));
      final markerX = canvas.left + (300 + 50) / 20000 * canvas.width;
      expect(tester.getTopLeft(find.text('Stone lands')).dx, greaterThan(markerX));
      expect(tester.getTopRight(find.text('Stone lands')).dx, lessThanOrEqualTo(canvas.right));
    });

    testWidgets('no marker names before a marker exists, and none once it has scrolled out of the window', (tester) async {
      final repo = await _pump(tester, first: _snap(PhantomRunState.running, chunk: _rest(400)));
      expect(find.text('Stone lands'), findsNothing);
      expect(find.text('Muscle burst'), findsNothing);

      repo.push(_snap(PhantomRunState.running, chunk: _rest(40, t0: 20000), markers: const [TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 20500)]));
      await tester.pump();
      expect(find.text('Stone lands'), findsOneWidget);

      repo.push(_snap(PhantomRunState.running, chunk: _rest(400, t0: 22000)));
      await tester.pump();
      expect(find.text('Stone lands'), findsNothing, reason: 'older than the 20 s window');
    });

    testWidgets('the 20 s window and the axis words and units come from l10n, in both languages', (tester) async {
      final semantics = tester.ensureSemantics();
      await _pump(tester, first: _snap(PhantomRunState.running, chunk: _rest(400)));
      expect(find.text('20 s ago'), findsNWidgets(2));
      expect(find.text('now'), findsNWidgets(2));
      expect(find.text('9.8 m/s²'), findsOneWidget);
      expect(find.bySemanticsLabel(RegExp('Muscle signal, Last 20 s')), findsWidgets);
      semantics.dispose();

      await _pump(tester, locale: const Locale('hi'), first: _snap(PhantomRunState.running, chunk: _rest(400)));
      expect(find.text('20 सेकंड पहले'), findsNWidgets(2));
      expect(find.text('अभी'), findsNWidgets(2));
    });

    for (final observer in const [false, true]) {
      testWidgets('wide and tall: the two plots share the pane height${observer ? ' (audience view)' : ''}', (tester) async {
        await _pump(tester, size: const Size(1600, 1000), observer: observer, first: _snap(PhantomRunState.running, chunk: _rest(400)));
        expect(tester.takeException(), isNull);
        final emg = tester.getRect(find.byKey(const ValueKey('ph-plot-emg')));
        final accel = tester.getRect(find.byKey(const ValueKey('ph-plot-accel')));
        expect(emg.height, greaterThan(250));
        expect(accel.height, greaterThan(250));
        expect((emg.height - accel.height).abs(), lessThan(1), reason: 'an equal share each');
        expect(accel.bottom, greaterThan(1000 - 80), reason: 'down to the bottom of the pane, no empty half');
      });
    }

    testWidgets('narrow, or wide but short: fixed-height plots in a scroll view, no overflow', (tester) async {
      await _pump(tester, size: const Size(390, 844), first: _snap(PhantomRunState.running, chunk: _rest(400)));
      expect(tester.takeException(), isNull);
      final phone = tester.getSize(find.byKey(const ValueKey('ph-plot-emg'))).height;
      expect(phone, lessThan(200), reason: '128 dp of plot plus header and axis');

      await _pump(tester, size: const Size(1280, 500), first: _snap(PhantomRunState.running, chunk: _rest(400)));
      expect(tester.takeException(), isNull);
      expect(tester.getSize(find.byKey(const ValueKey('ph-plot-emg'))).height, closeTo(phone, 1));
    });
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
    expect(find.byKey(const ValueKey('ph-link')), findsNothing, reason: 'the demo has no link to report on');
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
