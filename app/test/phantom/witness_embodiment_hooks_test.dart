// Integration of the witness mirror (B11) and the embodiment report (B19) into
// the app: one test per hook, each of which fails when its hook is taken out.
//   (a) PhantomLiveSnapshot / PhantomLiveModel carry the witness summary
//   (b) HubPhantomLiveRepository parses a real `witness_summary` event
//   (c) the audience view shows the mirror in the witness phase
//   (d) the session report shows the embodiment report for a Phantom Hand session
// (e) is not a hook: the in-app demo reaches the witness phase with numbers.
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:opus_app/data/repositories/hub/hub_phantom_live_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:opus_app/features/live/phantom_live_screen.dart';
import 'package:opus_app/features/live/phantom_witness_mirror.dart';
import 'package:opus_app/features/sessions/embodiment_report.dart';
import 'package:opus_app/features/sessions/session_report_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';

import '../hub/fake_live_socket.dart';
import 'witness_test_support.dart';

class _Repo implements PhantomLiveRepository {
  final controller = StreamController<PhantomLiveSnapshot>.broadcast();

  @override
  Stream<PhantomLiveSnapshot> watch() => controller.stream;

  @override
  Future<bool> send(PhantomCommand command, {Map<String, dynamic>? params}) async => true;
}

PhantomLiveSnapshot _snap(PhantomRunState state, String phase, {PhantomWitness? witness}) => PhantomLiveSnapshot(
      runState: state,
      connected: true,
      game: PhantomGameState(
        phase: phase,
        condition: null,
        remainingS: null,
        hapticConnected: true,
        bioConnected: true,
        emgLevel: 0.1,
      ),
      witness: witness,
    );

Widget _app(Widget home) => MaterialApp(
      theme: AppTheme.dark(),
      localizationsDelegates: const [
        ...AppLocalizations.localizationsDelegates,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      supportedLocales: AppLocalizations.supportedLocales,
      home: home,
    );

void _laptop(WidgetTester tester) {
  tester.view.physicalSize = const Size(1280, 800);
  tester.view.devicePixelRatio = 1;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

/// [text] inside the mirror row [row] of card [card] (`sync` / `async`).
Finder _mirrorText(String card, String row, String text) =>
    find.descendant(of: find.byKey(ValueKey('ph-witness-$card-$row')), matching: find.text(text));

void main() {
  setUpAll(loadWitnessTestFonts);

  test('(a) the model keeps the witness summary a snapshot carried, and drops it for the next person', () {
    final summary = fixtureWitness();
    final model = PhantomLiveModel();
    expect(model.witness, isNull);

    model.apply(_snap(PhantomRunState.running, 'witness', witness: summary));
    expect(model.witness, same(summary));

    // The event comes once; later statuses of the same phase must not lose it.
    model.apply(_snap(PhantomRunState.running, 'witness'));
    expect(model.witness, same(summary));
    model.apply(_snap(PhantomRunState.finished, 'done'));
    expect(model.witness, same(summary));

    // The headset waits for the next person: the last person's numbers go.
    model.apply(_snap(PhantomRunState.paired, 'calibrate'));
    expect(model.witness, isNull);
  });

  test('(b) a real witness_summary trial event becomes snapshot.witness; a marker event does not', () async {
    final socket = FakeLiveSocket();
    final conn = HubConnection(
      deviceId: 'quest-1',
      socket: socket,
      pairToken: '123456',
      incoming: socket.incoming,
      pingInterval: const Duration(hours: 1),
    );
    final repo = HubPhantomLiveRepository(conn);
    addTearDown(() async {
      await repo.dispose();
      await conn.close();
    });
    final seen = <PhantomLiveSnapshot>[];
    final sub = repo.watch().listen(seen.add);
    addTearDown(sub.cancel);

    // The event line of contracts/fixtures/sessions/phantom_hand_min/events.ndjson, as the headset sends it.
    socket.receive(headsetMessage(type: 'trial_event', id: 'event-1', seq: 1, payload: fixtureWitnessEvent()));
    await Future<void>.delayed(const Duration(milliseconds: 25));

    final carrying = seen.where((s) => s.witness != null).toList();
    expect(carrying, hasLength(1));
    final w = carrying.single.witness!;
    expect(w.conditionOrder, ['async', 'sync']);
    expect(w.sync.driftChangeCm, 2.0);
    expect(w.async.driftChangeCm, 0.0);
    expect(w.sync.ownership, 5.5);
    expect(w.async.ownership, 2.5);
    expect(w.sync.flinchEmgPeakX, 6.4);
    expect(w.async.flinchEmgPeakX, 2.1);
    expect(w.sync.witnessQ4, 6.0);
    expect(carrying.single.markers, isEmpty, reason: 'a summary is not a trace marker');

    // The other event type still reaches the traces, and carries no summary.
    socket.receive(
      headsetMessage(type: 'trial_event', id: 'event-2', seq: 2, payload: {'type': 'threat_impact', 't_ms': 400.0}),
    );
    await Future<void>.delayed(const Duration(milliseconds: 25));
    expect(seen.last.markers, hasLength(1));
    expect(seen.last.witness, isNull);
  });

  testWidgets('(c) the audience view shows the mirror with the event numbers in the witness phase only', (tester) async {
    _laptop(tester);
    final repo = _Repo();
    addTearDown(repo.controller.close);
    await tester.pumpWidget(_app(PhantomLiveScreen(repository: repo, initialObserver: true)));
    Future<void> push(PhantomLiveSnapshot s) async {
      repo.controller.add(s);
      await tester.pump();
      await tester.pump();
    }

    await push(_snap(PhantomRunState.running, 'threat'));
    expect(find.byType(PhantomWitnessMirror), findsNothing, reason: 'traces before the witness phase');

    await push(_snap(PhantomRunState.running, 'witness', witness: fixtureWitness()));
    expect(find.byType(PhantomWitnessMirror), findsOneWidget);
    expect(_mirrorText('sync', 'drift', '+2.0 cm'), findsOneWidget);
    expect(_mirrorText('sync', 'ownership', '+1.5'), findsOneWidget);
    expect(_mirrorText('async', 'ownership', '-1.5'), findsOneWidget);

    // The exit button of the audience view sits top right: it must not cover the
    // header (at 1280 x 800 the "Preliminary" chip reached 6 px under it).
    final exit = tester.getRect(find.byType(IconButton));
    for (final key in ['ph-witness-title', 'ph-witness-preliminary']) {
      final header = tester.getRect(find.byKey(ValueKey(key)));
      expect(exit.overlaps(header), isFalse, reason: '$key $header under the exit button $exit');
    }

    // The next status of the same phase carries no event; the numbers stay.
    await push(_snap(PhantomRunState.running, 'witness'));
    expect(_mirrorText('sync', 'drift', '+2.0 cm'), findsOneWidget);

    await push(_snap(PhantomRunState.finished, 'done'));
    expect(find.byType(PhantomWitnessMirror), findsNothing, reason: 'traces again after the witness phase');
  });

  testWidgets('(c) the operator card never shows the mirror, also not in the witness phase', (tester) async {
    _laptop(tester);
    final repo = _Repo();
    addTearDown(repo.controller.close);
    await tester.pumpWidget(_app(PhantomLiveScreen(repository: repo)));
    repo.controller.add(_snap(PhantomRunState.running, 'witness', witness: fixtureWitness()));
    await tester.pump();
    await tester.pump();
    expect(find.text('Results'), findsWidgets, reason: 'the card did get the witness phase');
    expect(find.byType(PhantomWitnessMirror), findsNothing);
  });

  testWidgets('(d) the session report shows the embodiment report when metrics.json has an embodiment block', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    // "Open a session folder...": the PH fixture session of the contracts.
    final envelope = await tester.runAsync(
      () => container.read(openedSessionDirectoriesProvider.notifier).open(contractsPath('fixtures/sessions/phantom_hand_min')),
    );
    expect(envelope, isNotNull, reason: 'the fixture folder must open as a session');

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: _app(SessionReportScreen(patientId: envelope!.patientRef, sessionId: envelope.sessionId)),
      ),
    );
    // Real file reads: let them finish on the real clock, then pump.
    for (var round = 0; round < 30; round++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 100)));
      await tester.pump(const Duration(milliseconds: 100));
      if (find.byType(EmbodimentReport).evaluate().isNotEmpty) break;
    }

    expect(find.byType(EmbodimentReport), findsOneWidget);
    final verdict = tester.widget<Text>(find.byKey(const ValueKey('ph-embodiment-verdict'))).data;
    expect(verdict, contains('the hand felt more like yours (5.5 vs 2.5)'));
    // Not the trial layout of a session with trials.
    expect(find.text('Success %'), findsNothing);
    expect(find.text('Full biomarkers after analysis.'), findsNothing);
  });

  testWidgets('(e) the in-app demo reaches the witness phase and the mirror shows numbers, not "No data"', (tester) async {
    _laptop(tester);
    final repo = MockPhantomLiveRepository(ackDelay: Duration.zero);
    await tester.pumpWidget(_app(PhantomLiveScreen(repository: repo, demo: true, initialObserver: true)));
    await tester.pump();
    expect(find.byType(PhantomWitnessMirror), findsNothing);

    // calibrate, probe_pre, 2 x (induction, threat, probe_post, questionnaire), witness.
    expect(await repo.send(PhantomCommand.start), isTrue);
    for (var i = 0; i < 10; i++) {
      expect(await repo.send(PhantomCommand.phaseNext), isTrue);
    }
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));

    expect(find.byType(PhantomWitnessMirror), findsOneWidget);
    expect(_mirrorText('sync', 'drift', '+2.0 cm'), findsOneWidget);
    expect(_mirrorText('async', 'ownership', '-1.5'), findsOneWidget);
    expect(find.text('No data'), findsNothing);
    // Several 200 ms ticks later it is still the same object: the closing-line fade is not restarted.
    final fade = tester.widget<FadeTransition>(find.byKey(const ValueKey('ph-witness-closing')));
    await tester.pump(const Duration(seconds: 4));
    expect(fade.opacity.value, 1.0);
  });
}
