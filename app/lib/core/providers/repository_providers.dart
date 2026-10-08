import 'package:opus_app/core/env/env.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/data/repositories/auth_repository.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/hub/directory_session_repository.dart';
import 'package:opus_app/data/repositories/hub/hub_sessions_repository.dart';
import 'package:opus_app/data/repositories/live_repository.dart';
import 'package:opus_app/data/repositories/manifests_repository.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';
import 'package:opus_app/data/repositories/mock/mock_auth_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_live_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_manifests_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_outcomes_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_patients_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_programs_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_sessions_repository.dart';
import 'package:opus_app/data/repositories/outcomes_repository.dart';
import 'package:opus_app/data/repositories/patients_repository.dart';
import 'package:opus_app/data/repositories/programs_repository.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'repository_providers.g.dart';

/// Flip via the Settings screen in-app (`errorInjectorProvider`) to exercise
/// error/retry UI on any screen without a real backend.
@Riverpod(keepAlive: true)
class ErrorInjector extends _$ErrorInjector {
  final _injector = MockErrorInjector();

  @override
  MockErrorInjector build() => _injector;

  void setEnabled(bool enabled) {
    _injector.enabled = enabled;
    state = _injector;
  }
}

/// Every provider below resolves to a mock implementation while
/// `Env.mockMode` is true (Track A). Phase 3 flips the branch to a real,
/// generated-OpenAPI-client-backed implementation -- screens depend only on
/// the abstract repository types, never on `mock/*` directly.
@Riverpod(keepAlive: true)
AuthRepository authRepository(Ref ref) {
  if (Env.mockMode) return MockAuthRepository();
  throw UnimplementedError('Phase 3 backend-backed AuthRepository not wired yet.');
}

@Riverpod(keepAlive: true)
PatientsRepository patientsRepository(Ref ref) {
  final injector = ref.watch(errorInjectorProvider);
  if (Env.mockMode) return MockPatientsRepository(errorInjector: injector);
  throw UnimplementedError('Phase 3 backend-backed PatientsRepository not wired yet.');
}

@Riverpod(keepAlive: true)
ManifestsRepository manifestsRepository(Ref ref) {
  final injector = ref.watch(errorInjectorProvider);
  if (Env.mockMode) return MockManifestsRepository(errorInjector: injector);
  throw UnimplementedError('Phase 3 backend-backed ManifestsRepository not wired yet.');
}

@Riverpod(keepAlive: true)
ProgramsRepository programsRepository(Ref ref) {
  final injector = ref.watch(errorInjectorProvider);
  if (Env.mockMode) return MockProgramsRepository(errorInjector: injector);
  throw UnimplementedError('Phase 3 backend-backed ProgramsRepository not wired yet.');
}

@Riverpod(keepAlive: true)
SessionsRepository sessionsRepository(Ref ref) {
  final injector = ref.watch(errorInjectorProvider);
  final mock = Env.mockMode
      ? MockSessionsRepository(errorInjector: injector)
      : throw UnimplementedError('Phase 3 backend-backed SessionsRepository not wired yet.');
  // Watching hubControllerProvider (not just reading the notifier) makes
  // this provider rebuild when the hub starts/stops or `sessionsDir`
  // becomes available, so a session uploaded by a headset shows up in the
  // sessions list/report without the clinician needing to navigate away and
  // back (`docs/agent-briefs/A-next-run.md` item 2).
  final hubState = ref.watch(hubControllerProvider);
  final sessionsDir = hubState is HubRunning ? ref.read(hubControllerProvider.notifier).sessionsDir : null;
  final hub = sessionsDir == null ? null : HubSessionsRepository(sessionsDir);
  // Sessions a clinician explicitly opened via "Open a session folder..."
  // (Devices screen) -- e.g. from `app/.hub_data/<id>/`, which a session
  // produced by `tool/hub_cli.dart` or a Unity pipeline run lands in, but
  // which this app instance's own hub never wrote and so `hub` above never
  // sees. Watched so opening a new folder refreshes any screen already open.
  final opened = ref.watch(openedSessionDirectoriesProvider);
  return CompositeSessionsRepository(mock: mock, hub: hub, opened: opened);
}

/// Session directories opened one-off via "Open a session folder..."
/// (Devices screen), for the lifetime of the app run -- see
/// [DirectorySessionsRepository]'s doc comment for why this exists
/// alongside the hub-lifecycle-bound [HubSessionsRepository].
@Riverpod(keepAlive: true)
class OpenedSessionDirectories extends _$OpenedSessionDirectories {
  @override
  List<DirectorySessionsRepository> build() => [];

  /// Opens [path] as a session directory and adds it to the list (if not
  /// already open), returning the session's envelope -- which carries the
  /// `patientRef`/`sessionId` a caller needs to navigate straight to its
  /// report -- or `null` if [path] isn't a valid session directory (no
  /// `session.json`, or it fails to parse).
  Future<SessionEnvelope?> open(String path) async {
    final repo = await DirectorySessionsRepository.open(path);
    if (repo == null) return null;
    if (!state.any((r) => r.envelope.sessionId == repo.envelope.sessionId)) {
      state = [...state, repo];
    }
    return repo.envelope;
  }
}

@Riverpod(keepAlive: true)
OutcomesRepository outcomesRepository(Ref ref) {
  final injector = ref.watch(errorInjectorProvider);
  if (Env.mockMode) return MockOutcomesRepository(errorInjector: injector);
  throw UnimplementedError('Phase 3 backend-backed OutcomesRepository not wired yet.');
}

@Riverpod(keepAlive: true)
LiveRepository liveRepository(Ref ref) {
  if (Env.mockMode) return MockLiveRepository();
  throw UnimplementedError('Phase 3 backend-backed LiveRepository not wired yet.');
}
