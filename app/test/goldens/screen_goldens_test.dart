@Tags(['golden'])
library;

// Run 8 fix: this file's own comment below ("Tagged `golden` so a normal
// `flutter test` run can exclude this file's ~4-5 minute runtime") was never
// actually true -- there was no `@Tags` annotation anywhere in this file, so
// `flutter test --exclude-tags golden` silently ran all 32 of these tests
// too, on top of whatever run6/7's session logs believed "the non-golden
// suite" meant. Adding the tag for real is what the comment already claimed.
//
// Item 5 (docs/agent-briefs/A-next-run.md): goldens for profile, live
// monitor, program builder and session report at phone (390x844), tablet
// (1280x800) and desktop (1600x1000), light and dark, with a phone text
// scale of 2.0 (the brief's "at least on phone" floor). Real screens with
// real mock-fixture data (`synthetic-healthy-42` / the `healthy` session),
// through the actual provider graph -- not a hand-built stand-in widget.
//
// Reduced from the brief's full 3-viewport x 2-theme x 2-textscale (12
// cells/screen = 48 total) matrix to 8 cells/screen (32 total): every
// viewport x theme combination at the default text scale, plus both themes
// at 2.0x on phone specifically (where cramped layouts are most likely to
// overflow, and where APP_DESIGN.md's "must still pass at 200% system text
// scale" requirement is explicit). Tablet/desktop at 2.0x is not covered
// this run -- flagged as remaining scope in the session log, not attempted
// partially. Tagged `golden` so a normal `flutter test` run can exclude
// this file's ~4-5 minute runtime.
import 'dart:io';
import 'dart:typed_data';

import 'package:collection/collection.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show FontLoader;
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/data/models/game_manifest.dart';
import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/outcome_measure.dart';
import 'package:opus_app/data/models/patient.dart';
import 'package:opus_app/data/models/program.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/manifests_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_manifests_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_outcomes_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_patients_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_programs_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_sessions_repository.dart';
import 'package:opus_app/data/repositories/outcomes_repository.dart';
import 'package:opus_app/data/repositories/patients_repository.dart';
import 'package:opus_app/data/repositories/programs_repository.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/features/live/live_monitor_screen.dart';
import 'package:opus_app/features/patients/patient_list_screen.dart';
import 'package:opus_app/features/patients/patient_profile_screen.dart';
import 'package:opus_app/features/programs/program_builder_screen.dart';
import 'package:opus_app/features/sessions/reports_list_screen.dart';
import 'package:opus_app/features/sessions/session_report_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

/// `flutter test` renders text with a synthetic placeholder glyph (solid
/// boxes) unless the real font is explicitly registered -- goldens
/// generated without this are useless for the visual QA this item asks for
/// (every letterform is a black rectangle). Loads the actual bundled
/// Atkinson Hyperlegible Next file the app ships
/// (`docs/APP_DESIGN.md`: "Bundle the font files ... so clinics work
/// offline") so these goldens show real, reviewable typography.
Future<void> _loadRealFonts() async {
  final data = await File('assets/fonts/AtkinsonHyperlegibleNext-Variable.ttf').readAsBytes();
  final loader = FontLoader('AtkinsonHyperlegibleNext')..addFont(Future.value(ByteData.view(data.buffer)));
  await loader.load();
}

/// Visual QA finding: "icons render as boxes in goldens" -- `Icon` widgets
/// (pause/stop/chevron/etc, used all over these screens) draw glyphs from
/// the `MaterialIcons` font, which -- like the app's own bundled font above
/// -- isn't registered by default under `flutter test`. Loads it straight
/// from the Flutter SDK's own cached copy (shipped with every checkout at
/// `<flutter>/bin/cache/artifacts/material_fonts/`), the same file the real
/// app embeds at build time, so goldens show the real glyphs instead of
/// tofu boxes.
Future<void> _loadIconFont() async {
  final file = _findMaterialIconsFont();
  if (file == null) return;
  final data = await file.readAsBytes();
  final loader = FontLoader('MaterialIcons')..addFont(Future.value(ByteData.view(data.buffer)));
  await loader.load();
}

/// `flutter test` runs inside `flutter_tester.exe`, which -- unlike plain
/// `dart` -- lives at `<flutter>/bin/cache/artifacts/engine/<platform>/`,
/// several levels deeper than the dart-sdk `dart` executable (confirmed
/// directly: `Platform.resolvedExecutable` prints
/// `.../flutter/bin/cache/artifacts/engine/windows-x64/flutter_tester.exe`
/// under this exact command). Rather than hardcode that engine-platform
/// path (which varies by OS/arch), walk up from the executable looking for
/// the `flutter` root by its own on-disk shape: the directory that has
/// `bin/cache/artifacts/material_fonts/MaterialIcons-Regular.otf` under it.
File? _findMaterialIconsFont() {
  try {
    var dir = File(Platform.resolvedExecutable).absolute.parent;
    for (var i = 0; i < 8; i++) {
      final candidate = File('${dir.path}/bin/cache/artifacts/material_fonts/MaterialIcons-Regular.otf');
      if (candidate.existsSync()) return candidate;
      if (dir.parent.path == dir.path) break; // reached filesystem root
      dir = dir.parent;
    }
    return null;
  } catch (_) {
    return null;
  }
}

const _patientId = 'synthetic-healthy-42';
// Run 7 fix: this used to be a UUID that matched no fixture at all (every
// real session id in `assets/fixtures/sessions/*.json` is generated per
// fixture, none of them this literal string), so `getSessionEnvelope`/
// `getSessionMetrics`/`getReachTraces` below silently preloaded `null`/empty
// data and `session_report`'s golden only ever rendered its "No metrics for
// this session yet." empty state -- never the trial table, SPARC/RT trends,
// workspace heatmap, or (this run's addition) the haptics section. This is
// the `healthy` fixture's real `session_id` (`assets/fixtures/sessions/
// healthy__session.json`), whose `patient_ref` is `_patientId` above, so the
// preload below now actually resolves real data.
const _sessionId = '0de01a20-3a71-4377-8078-f88cdd07b438';

// Run 8 fix: the `patient_profile` golden used `_patientId` (`healthy`, one
// session) for its patient too, so the Recovery section's `TrendLineChart`s
// always hit the "not enough sessions for a trend yet" honest-empty-state
// branch (`patient_profile_screen.dart`'s `_RecoveryLine`, `values.length < 2`)
// -- correct behavior for that single-session patient, but it meant the one
// golden meant to visually QA "a recovery line chart with the shaded MDC
// band" (`docs/APP_DESIGN.md`) never actually exercised that code path, and
// its "Outcome measures" section was always empty too (`MockOutcomesRepository`
// only seeds outcome-measure entries for this patient below, never for
// `healthy` -- see that file's `_seed()`). `synthetic-longitudinal-9000` is
// the fixture built for exactly this (7 weekly sessions,
// `docs/agent-briefs/A-next-run.md` item 1's "longitudinal fixtures"), so the
// `patient_profile` screen now renders against it instead, while the other
// three screens (which need the `healthy` fixture's richer events/haptics
// data) keep using `_patientId`/`_sessionId`.
const _longitudinalPatientId = 'synthetic-longitudinal-9000';

/// Visual QA finding: "the patient_profile goldens only show a loading
/// spinner" -- root cause is real, accumulated `rootBundle`/`Future.delayed`
/// latency from the mock repositories' fixture loading piling up across this
/// file's 32-test shared process (each mock repo call carries `Env.
/// mockLatency` = 220 ms; harmless in a single test, but see run4's session
/// log for the full diagnosis of the flakiness once dozens of widget trees
/// share one process). Rather than betting on a bigger pump budget, load
/// every fixture the four screens need exactly once here (paying that real
/// latency a single time, before any test's own timing-sensitive pump loop
/// starts) and override the repository providers with tiny synchronous
/// wrappers around the already-resolved data -- "loaded fixture data" per
/// `docs/agent-briefs/A-next-run.md`'s literal instruction.
class _PreloadedFixtures {
  const new({
    required this.patient,
    required this.programs,
    required this.manifests,
    required this.sessions,
    required this.envelope,
    required this.metrics,
    required this.traces,
    required this.outcomes,
    required this.events,
    required this.longitudinalPatient,
    required this.longitudinalSessions,
    required this.longitudinalMetricsById,
    required this.longitudinalTracesById,
    required this.longitudinalOutcomes,
  });

  final Patient? patient;
  final List<Program> programs;
  final List<GameManifest> manifests;
  final List<SessionEnvelope> sessions;
  final SessionEnvelope? envelope;
  final SessionMetrics? metrics;
  final ReachTraceSet traces;
  final List<OutcomeMeasureEntry> outcomes;
  // Run 7 addition: `session_report`'s new haptics section
  // (`shared/metrics/haptic_analysis.dart`) reads `getSessionEvents` --
  // `_InstantSessionsRepository.getSessionEvents` used to hardcode `const
  // []` regardless of the fixture, so preload the real events here the same
  // way every other repository call in this class already is.
  final List<SessionEvent> events;

  // Run 8 additions: the `synthetic-longitudinal-9000` patient used only by
  // the `patient_profile` golden (see `_longitudinalPatientId`'s doc above).
  // Kept as separate fields/maps rather than folding into the fields above,
  // so the other three screens' `healthy`-fixture behavior is byte-for-byte
  // unchanged.
  final Patient? longitudinalPatient;
  final List<SessionEnvelope> longitudinalSessions;
  final Map<String, SessionMetrics?> longitudinalMetricsById;
  final Map<String, ReachTraceSet> longitudinalTracesById;
  final List<OutcomeMeasureEntry> longitudinalOutcomes;

  static Future<_PreloadedFixtures> load() async {
    final patients = MockPatientsRepository();
    final programs = MockProgramsRepository();
    final manifests = MockManifestsRepository();
    final sessions = MockSessionsRepository();
    // The patient overview's outcome-measures summary
    // (`features/patients/patient_profile_screen.dart`) reads
    // `outcomesRepositoryProvider` directly -- preload it too, same as every
    // other repository this file's screens touch, so it never carries the
    // mock's real `Env.mockLatency` into the golden pump loop below.
    final outcomes = MockOutcomesRepository();

    final longitudinalSessions = await sessions.listSessionsForPatient(_longitudinalPatientId);
    final longitudinalMetricsById = <String, SessionMetrics?>{};
    final longitudinalTracesById = <String, ReachTraceSet>{};
    for (final s in longitudinalSessions) {
      longitudinalMetricsById[s.sessionId] = await sessions.getSessionMetrics(s.sessionId);
      longitudinalTracesById[s.sessionId] = await sessions.getReachTraces(s.sessionId);
    }

    return _PreloadedFixtures(
      patient: await patients.getPatient(_patientId),
      programs: await programs.listProgramsForPatient(_patientId),
      manifests: await manifests.listManifests(),
      sessions: await sessions.listSessionsForPatient(_patientId),
      envelope: await sessions.getSessionEnvelope(_sessionId),
      metrics: await sessions.getSessionMetrics(_sessionId),
      traces: await sessions.getReachTraces(_sessionId),
      outcomes: await outcomes.listEntries(_patientId),
      events: await sessions.getSessionEvents(_sessionId),
      longitudinalPatient: await patients.getPatient(_longitudinalPatientId),
      longitudinalSessions: longitudinalSessions,
      longitudinalMetricsById: longitudinalMetricsById,
      longitudinalTracesById: longitudinalTracesById,
      longitudinalOutcomes: await outcomes.listEntries(_longitudinalPatientId),
    );
  }
}

class _InstantPatientsRepository implements PatientsRepository {
  const new(this._data);
  final _PreloadedFixtures _data;
  @override
  Future<Patient?> getPatient(String id) {
    if (id == _longitudinalPatientId) return Future.value(_data.longitudinalPatient);
    return Future.value(_data.patient);
  }

  @override
  Future<List<Patient>> listPatients([PatientFilter filter = const PatientFilter()]) => Future.value([
        if (_data.patient != null) _data.patient!,
        if (_data.longitudinalPatient != null) _data.longitudinalPatient!,
      ]);
}

class _InstantProgramsRepository implements ProgramsRepository {
  const new(this._data);
  final _PreloadedFixtures _data;
  @override
  Future<List<Program>> listProgramsForPatient(String patientId) => Future.value(_data.programs);
  @override
  Future<Program?> getProgram(String programId) =>
      Future.value(_data.programs.where((p) => p.programId == programId).firstOrNull);
  @override
  Future<Program> saveProgram(Program program) => Future.value(program);
}

class _InstantManifestsRepository implements ManifestsRepository {
  const new(this._data);
  final _PreloadedFixtures _data;
  @override
  Future<List<GameManifest>> listManifests() => Future.value(_data.manifests);
  @override
  Future<GameManifest?> getManifest(String gameId) =>
      Future.value(_data.manifests.where((m) => m.id == gameId).firstOrNull);
}

class _InstantSessionsRepository implements SessionsRepository {
  const new(this._data);
  final _PreloadedFixtures _data;

  @override
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId) {
    if (patientId == _longitudinalPatientId) return Future.value(_data.longitudinalSessions);
    return Future.value(_data.sessions);
  }

  @override
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId) {
    if (sessionId == _sessionId) return Future.value(_data.envelope);
    return Future.value(_data.longitudinalSessions.where((s) => s.sessionId == sessionId).firstOrNull);
  }

  @override
  Future<SessionMetrics?> getSessionMetrics(String sessionId) {
    if (sessionId == _sessionId) return Future.value(_data.metrics);
    return Future.value(_data.longitudinalMetricsById[sessionId]);
  }

  @override
  Future<List<SessionEvent>> getSessionEvents(String sessionId) {
    if (sessionId == _sessionId) return Future.value(_data.events);
    return Future.value(const []);
  }

  @override
  Future<ReachTraceSet> getReachTraces(String sessionId) {
    if (sessionId == _sessionId) return Future.value(_data.traces);
    return Future.value(_data.longitudinalTracesById[sessionId] ?? ReachTraceSet.empty);
  }
}

class _InstantOutcomesRepository implements OutcomesRepository {
  const new(this._data);
  final _PreloadedFixtures _data;
  @override
  Future<List<OutcomeMeasureEntry>> listEntries(String patientId, {OutcomeMeasureType? type}) {
    final all = patientId == _longitudinalPatientId ? _data.longitudinalOutcomes : _data.outcomes;
    return Future.value(type == null ? all : all.where((e) => e.type == type).toList());
  }

  @override
  Future<OutcomeMeasureEntry> addEntry(OutcomeMeasureEntry entry) => Future.value(entry);
}

// A3 run1 added `phone360`. The pilot phone (CPH2381, 1080x2412 at density
// 480) renders **360 dp** wide at system font_scale 1.0, not the 390 dp this
// matrix was authored against -- 8 % narrower, which is where list rows went
// to two lines and section titles wrapped on device
// (`docs/design/OPUS_DESIGN_V2.md` §2: "Goldens must include a 360x800 phone
// breakpoint, not only 390x844").
const _viewports = {
  'phone360': Size(360, 800),
  'phone': Size(390, 844),
  'tablet': Size(1280, 800),
  'desktop': Size(1600, 1000),
};

Widget _wrap(Widget screen, Brightness brightness, double textScale, _PreloadedFixtures fixtures) => ProviderScope(
      overrides: [
        patientsRepositoryProvider.overrideWithValue(_InstantPatientsRepository(fixtures)),
        programsRepositoryProvider.overrideWithValue(_InstantProgramsRepository(fixtures)),
        manifestsRepositoryProvider.overrideWithValue(_InstantManifestsRepository(fixtures)),
        sessionsRepositoryProvider.overrideWithValue(_InstantSessionsRepository(fixtures)),
        outcomesRepositoryProvider.overrideWithValue(_InstantOutcomesRepository(fixtures)),
      ],
      child: MaterialApp(
        debugShowCheckedModeBanner: false,
        theme: brightness == Brightness.light ? AppTheme.light() : AppTheme.dark(),
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
        home: screen,
      ),
    );

Future<void> _goldenFor(
  WidgetTester tester, {
  required String name,
  required Widget screen,
  required String viewport,
  required Brightness brightness,
  required _PreloadedFixtures fixtures,
  double textScale = 1,
}) async {
  final size = _viewports[viewport]!;
  // Only drive the logical size via the test binding's view -- also
  // wrapping in a second, independently-sized `MediaQuery` (as an earlier
  // version of this helper did) left the widget tree in a state where async
  // FutureProviders' data never reached the screen even after many pumps
  // (reproduced in isolation; root-caused to the double MediaQuery /
  // setSurfaceSize combination, not to the providers themselves -- the same
  // screens render correctly at the default test size). Text scale is
  // applied once, via MaterialApp's own `builder`, instead.
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  await tester.pumpWidget(_wrap(screen, brightness, textScale, fixtures));
  // NOT pumpAndSettle: the live monitor/devices-style screens hold open
  // streams (hub RTT ticks, status polling) that never go idle, so
  // pumpAndSettle's "wait until nothing is scheduled" times out. The
  // repository providers are overridden above with already-resolved data
  // (see `_PreloadedFixtures`), so every `FutureProvider` in these screens
  // settles within a couple of microtask turns -- this loop is now just a
  // small, bounded safety net (not the primary mechanism, as it used to be
  // when it had to wait out real 220 ms mock latency accumulating across the
  // whole 32-test process; that was the actual cause of the spinner-only
  // goldens this fixes).
  for (var i = 0; i < 10; i++) {
    await tester.pump(const Duration(milliseconds: 50));
    // Root cause (run8): this used to check only `CircularProgressIndicator`
    // -- the top-level per-screen loading spinner (e.g.
    // `PatientProfileScreen`'s outer `patientAsync.when(loading: ...)`). But
    // `patient_profile_screen.dart`'s overview tab has its *own* nested
    // per-widget `FutureProvider`s (`progressDataProvider`,
    // `_sessionsProvider`, `_overviewOutcomesProvider` inside `_RecoveryLine`
    // / `_OverviewSessionsStrip` / `_OverviewOutcomesSummary`) that only start
    // resolving *after* the outer patient future resolves and the tab first
    // builds -- one extra async hop the outer spinner check doesn't see. Those
    // widgets show `LinearProgressIndicator`, not `CircularProgressIndicator`,
    // while loading, so the loop broke on the very first pump (as soon as the
    // outer spinner vanished) while the four solid `LinearProgressIndicator`
    // bars under Recovery/Sessions/Outcome measures were still on screen --
    // that's what run7's rejected `patient_profile` goldens actually captured
    // (see `docs/APP_DESIGN.md`/session log run8 CHECKPOINT 1 for the full
    // diagnosis). Waiting out both indicator types fixes it without betting on
    // a bigger fixed pump budget.
    // `i >= 3` (a minimum of 4 pumps): some of these screens' loading states
    // use no visible indicator at all -- e.g. `_SessionTimelineStrip`'s
    // per-session `ReachTraceGlyph` shows a plain, empty `SizedBox` while its
    // own `_reachTracesProvider(sessionId)` resolves, one more async hop
    // below the tab's own providers. Breaking purely on "no indicator found"
    // could still fire on pump 1, before that fourth-level future has had a
    // turn to resolve and repaint -- reproduced with `patient_profile`'s
    // session strip staying blank even after the Circular/Linear fix above.
    // A small unconditional minimum plus the indicator check is a bounded,
    // deterministic way to give deep provider chains room to settle without
    // just guessing at a bigger fixed budget.
    if (i >= 3 &&
        find.byType(CircularProgressIndicator).evaluate().isEmpty &&
        find.byType(LinearProgressIndicator).evaluate().isEmpty) {
      break;
    }
  }
  await expectLater(
    find.byType(MaterialApp),
    matchesGoldenFile('goldens/${name}_${viewport}_${brightness.name}_${textScale}x.png'),
  );
}

void main() {
  late _PreloadedFixtures fixtures;
  setUpAll(() async {
    await _loadRealFonts();
    await _loadIconFont();
    fixtures = await _PreloadedFixtures.load();
  });

  final screens = <String, Widget Function()>{
    // A3 run1: the Patients list had no golden at all, and it is the screen
    // the user's "full clinical description on every row" complaint was
    // about -- covered now at every viewport/theme/text-scale cell.
    'patient_list': () => const PatientListScreen(),
    'patient_profile': () => const PatientProfileScreen(patientId: _longitudinalPatientId),
    'program_builder': () => const ProgramBuilderScreen(patientId: _patientId),
    'live_monitor': () => const LiveMonitorScreen(patientId: _patientId, sessionId: _sessionId),
    'session_report': () => const SessionReportScreen(patientId: _patientId, sessionId: _sessionId),
    // A2 run13: the Reports tab (`docs/APP_DESIGN.md` bottom nav decision,
    // `reports_list_screen.dart`) -- every session across every patient. No
    // constructor params (it builds its own flat list from the same
    // preloaded repository fixtures every other screen here uses).
    'reports_list': () => const ReportsListScreen(),
  };

  for (final entry in screens.entries) {
    for (final viewport in _viewports.keys) {
      for (final brightness in Brightness.values) {
        testWidgets(
          '${entry.key} $viewport ${brightness.name} matches golden',
          tags: ['golden'],
          (tester) => _goldenFor(
            tester,
            name: entry.key,
            screen: entry.value(),
            viewport: viewport,
            brightness: brightness,
            fixtures: fixtures,
          ),
        );
      }
    }
    // 2.0 text scale on BOTH phone widths -- §7's "text scales to 2.0
    // without clipping" has to hold on the narrower real device too, which
    // is exactly where it is most likely to fail.
    for (final phone in ['phone360', 'phone']) {
      for (final brightness in Brightness.values) {
        testWidgets(
          '${entry.key} $phone ${brightness.name} @2x text scale matches golden',
          tags: ['golden'],
          (tester) => _goldenFor(
            tester,
            name: entry.key,
            screen: entry.value(),
            viewport: phone,
            brightness: brightness,
            fixtures: fixtures,
            textScale: 2,
          ),
        );
      }
    }
  }
}
