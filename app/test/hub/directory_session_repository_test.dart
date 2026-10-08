// Task (A run11): "Make the app able to open a session directory the hub
// received and render its existing session report from it." This is the
// real, non-mocked proof: `DirectorySessionsRepository` opened directly
// against `app/.hub_data/933e98f3-.../`, a session this repo's own history
// committed as evidence of the 2026-09-19 full three-way pipeline run
// (`logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md`) -- a real headset
// session, complete with a real `metrics.json` from `opus_analytics`, not a
// synthetic fixture built for this test. Then `SessionReportScreen` itself,
// wired to that repository the exact way "Open a session folder..."
// (Devices screen) wires it, rendering real trial data on screen.
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/repositories/hub/directory_session_repository.dart';
import 'package:opus_app/data/repositories/hub/hub_sessions_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_sessions_repository.dart';
import 'package:opus_app/features/sessions/session_report_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';

const _realSessionId = '933e98f3-7754-4e47-b6ff-389f0e9e3694';
const _realPatientId = 'run12-pipeline-patient';

String get _realSessionDir => '${Directory.current.path}/.hub_data/$_realSessionId';

void main() {
  test('DirectorySessionsRepository.open reads a real hub-received session directory', () async {
    expect(
      Directory(_realSessionDir).existsSync(),
      isTrue,
      reason: 'app/.hub_data/$_realSessionId is committed evidence from the full pipeline run '
          '(logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md) -- if this fails, that directory moved.',
    );

    final repo = await DirectorySessionsRepository.open(_realSessionDir);
    expect(repo, isNotNull);
    expect(repo!.envelope.sessionId, _realSessionId);
    expect(repo.envelope.patientRef, _realPatientId);
    expect(repo.sessionDirPath, _realSessionDir);

    // Real metrics.json (opus_analytics already ran during the pipeline
    // run) -- this session has some invalid-quality trials by design (the
    // demo's deliberate tracking-loss window), so this also exercises that
    // getSessionMetrics doesn't choke on `value: null` entries.
    final metrics = await repo.getSessionMetrics(_realSessionId);
    expect(metrics, isNotNull);
    expect(metrics!.trials, isNotEmpty);

    final events = await repo.getSessionEvents(_realSessionId);
    expect(events, isNotEmpty);

    final traces = await repo.getReachTraces(_realSessionId);
    expect(traces.trials, isNotEmpty);

    // A directory-opened repository knows about exactly the one session it
    // was opened against -- an unrelated id/patient must not leak into it.
    expect(await repo.getSessionEnvelope('not-this-session'), isNull);
    expect(await repo.listSessionsForPatient('not-this-patient'), isEmpty);
  });

  test('DirectorySessionsRepository.open returns null for a folder with no session.json', () async {
    final scratch = Directory.systemTemp.createTempSync('opus_directory_repo_test_');
    addTearDown(() => scratch.deleteSync(recursive: true));
    expect(await DirectorySessionsRepository.open(scratch.path), isNull);
    expect(await DirectorySessionsRepository.open('${scratch.path}/does-not-exist'), isNull);
  });

  // A2 run12 note (kept for context, see run13 log for the actual fix):
  // this test previously hung to the suite's 10-minute default timeout.
  // Bounded to `timeout: Timeout(Duration(seconds: 60))` below so a retry
  // can only ever cost 60s.
  testWidgets(
    'SessionReportScreen renders a real hub-received session opened via DirectorySessionsRepository',
    (tester) async {
      // Root cause found here (A2 run13): `testWidgets` runs its whole body
      // inside flutter_test's FakeAsync zone (not just the pump calls), so
      // even this bare `dart:io` read -- called before any pumpWidget --
      // never completes unless it's wrapped in `tester.runAsync`, which is
      // the one thing that unpauses the real event loop long enough for the
      // real IO helper isolate's port callback (`_RawReceivePort._handleMessage`,
      // exactly what the old TimeoutException's stack trace showed) to
      // actually fire. The two plain `test()`s above never hit this because
      // they aren't wrapped by `TestWidgetsFlutterBinding.runTest`.
      final directoryRepo = await tester.runAsync(() => DirectorySessionsRepository.open(_realSessionDir));
      expect(directoryRepo, isNotNull); // same directory as above -- see reason there.

      // Wires the report screen the same way "Open a session folder..."
      // does: the opened directory repository merged into the composite
      // via CompositeSessionsRepository's `opened` list, with no live hub
      // and no matching mock fixture -- proving the real path, not a
      // coincidence of some other fallback.
      final container = ProviderContainer(
        overrides: [
          sessionsRepositoryProvider.overrideWithValue(
            CompositeSessionsRepository(
              mock: MockSessionsRepository(),
              hub: null,
              opened: [directoryRepo!],
            ),
          ),
        ],
      );
      addTearDown(container.dispose);

      // Root cause of the hang (see CONTEXT.md's warning about a hanging
      // test once stalling the whole suite): DirectorySessionsRepository
      // does real `dart:io` file reads, and FutureProvider awaits those
      // futures. run12 wrapped `pumpWidget` itself inside `tester.runAsync`,
      // which is wrong: `pumpWidget`/`pump` must run in flutter_test's own
      // fake-async zone (they drive `TestWidgetsFlutterBinding`'s frame
      // scheduling), while `runAsync` suspends that fake zone and runs its
      // body on the *real* zone -- nesting a pump call inside it deadlocks
      // because the pump is waiting on fake-zone machinery that `runAsync`
      // has paused. The correct pattern (per flutter_test's own docs for
      // "a widget that starts a real Future during build"): pump normally
      // to kick off the build, use `runAsync` only to await a real-clock
      // delay so the actual dart:io read finishes, then pump again (still
      // in the normal fake zone) to let that completed future flow through.
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const MaterialApp(
            localizationsDelegates: [
              ...AppLocalizations.localizationsDelegates,
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            supportedLocales: AppLocalizations.supportedLocales,
            home: SessionReportScreen(patientId: _realPatientId, sessionId: _realSessionId),
          ),
        ),
      );
      // Let the real file-read futures resolve on the real event loop, then
      // pump -- repeated, since the screen watches several chained
      // FutureProviders (metrics -> events/traces/envelope), each one only
      // starts its own real IO once the widget that watches it actually
      // builds, which can take more than one round trip. Polls for the
      // expected content instead of a fixed count, same rationale as
      // `hub_connection_test.dart`'s ack-retry poll -- only as slow as it
      // needs to be, bounded by the outer 60s test timeout either way.
      for (var round = 0; round < 15; round++) {
        await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 100)));
        await tester.pump(const Duration(milliseconds: 100));
        // "Outcomes" is the first section BELOW the stat tiles, so it is the
        // earliest proof that the metrics branch has real data. A3 run1
        // changed this from polling for 'Trial table': the §5 section order
        // (tiles, Outcomes, Reaction time per trial, Speed profile, Haptic
        // cues, Trials) puts the trial table last, well outside the 800x600
        // test viewport's lazily-built extent, so it is never in the tree
        // until the list is scrolled -- see the scroll below.
        if (find.text('Outcomes').evaluate().isNotEmpty) break;
      }

      // The real metrics.json means the report renders trial content
      // directly, not the "Run analysis" prompt (that path is exercised
      // separately against a session with no metrics.json yet).
      expect(find.text('This session has no metrics yet.'), findsNothing);
      await tester.scrollUntilVisible(
        find.text('Trial table'),
        300,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('Trial table'), findsOneWidget);
    },
    timeout: const Timeout(Duration(seconds: 60)),
  );
}
