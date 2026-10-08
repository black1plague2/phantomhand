// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'repository_providers.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning
/// Flip via the Settings screen in-app (`errorInjectorProvider`) to exercise
/// error/retry UI on any screen without a real backend.

@ProviderFor(ErrorInjector)
final errorInjectorProvider = ErrorInjectorProvider._();

/// Flip via the Settings screen in-app (`errorInjectorProvider`) to exercise
/// error/retry UI on any screen without a real backend.
final class ErrorInjectorProvider
    extends $NotifierProvider<ErrorInjector, MockErrorInjector> {
  /// Flip via the Settings screen in-app (`errorInjectorProvider`) to exercise
  /// error/retry UI on any screen without a real backend.
  ErrorInjectorProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'errorInjectorProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$errorInjectorHash();

  @$internal
  @override
  ErrorInjector create() => ErrorInjector();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(MockErrorInjector value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<MockErrorInjector>(value),
    );
  }
}

String _$errorInjectorHash() => r'678e28393bea3fac131b9453a1ccef2df303aa26';

/// Flip via the Settings screen in-app (`errorInjectorProvider`) to exercise
/// error/retry UI on any screen without a real backend.

abstract class _$ErrorInjector extends $Notifier<MockErrorInjector> {
  MockErrorInjector build();
  @$mustCallSuper
  @override
  WhenComplete runBuild() {
    final ref = this.ref as $Ref<MockErrorInjector, MockErrorInjector>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<MockErrorInjector, MockErrorInjector>,
              MockErrorInjector,
              Object?,
              Object?
            >;
    return element.handleCreate(ref, build);
  }
}

/// Every provider below resolves to a mock implementation while
/// `Env.mockMode` is true (Track A). Phase 3 flips the branch to a real,
/// generated-OpenAPI-client-backed implementation -- screens depend only on
/// the abstract repository types, never on `mock/*` directly.

@ProviderFor(authRepository)
final authRepositoryProvider = AuthRepositoryProvider._();

/// Every provider below resolves to a mock implementation while
/// `Env.mockMode` is true (Track A). Phase 3 flips the branch to a real,
/// generated-OpenAPI-client-backed implementation -- screens depend only on
/// the abstract repository types, never on `mock/*` directly.

final class AuthRepositoryProvider
    extends $FunctionalProvider<AuthRepository, AuthRepository, AuthRepository>
    with $Provider<AuthRepository> {
  /// Every provider below resolves to a mock implementation while
  /// `Env.mockMode` is true (Track A). Phase 3 flips the branch to a real,
  /// generated-OpenAPI-client-backed implementation -- screens depend only on
  /// the abstract repository types, never on `mock/*` directly.
  AuthRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'authRepositoryProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$authRepositoryHash();

  @$internal
  @override
  $ProviderElement<AuthRepository> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  AuthRepository create(Ref ref) {
    return authRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AuthRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AuthRepository>(value),
    );
  }
}

String _$authRepositoryHash() => r'97e5de9b78bc6bd812e81f29dc9073ce9b1db6fd';

@ProviderFor(patientsRepository)
final patientsRepositoryProvider = PatientsRepositoryProvider._();

final class PatientsRepositoryProvider
    extends
        $FunctionalProvider<
          PatientsRepository,
          PatientsRepository,
          PatientsRepository
        >
    with $Provider<PatientsRepository> {
  PatientsRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'patientsRepositoryProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$patientsRepositoryHash();

  @$internal
  @override
  $ProviderElement<PatientsRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  PatientsRepository create(Ref ref) {
    return patientsRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PatientsRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PatientsRepository>(value),
    );
  }
}

String _$patientsRepositoryHash() =>
    r'eac2bb612d70cbb38c96abe54897a5889b5d0358';

@ProviderFor(manifestsRepository)
final manifestsRepositoryProvider = ManifestsRepositoryProvider._();

final class ManifestsRepositoryProvider
    extends
        $FunctionalProvider<
          ManifestsRepository,
          ManifestsRepository,
          ManifestsRepository
        >
    with $Provider<ManifestsRepository> {
  ManifestsRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'manifestsRepositoryProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$manifestsRepositoryHash();

  @$internal
  @override
  $ProviderElement<ManifestsRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ManifestsRepository create(Ref ref) {
    return manifestsRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ManifestsRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ManifestsRepository>(value),
    );
  }
}

String _$manifestsRepositoryHash() =>
    r'f8b6d519c98cb5a31a19149c591732144ccbeca1';

@ProviderFor(programsRepository)
final programsRepositoryProvider = ProgramsRepositoryProvider._();

final class ProgramsRepositoryProvider
    extends
        $FunctionalProvider<
          ProgramsRepository,
          ProgramsRepository,
          ProgramsRepository
        >
    with $Provider<ProgramsRepository> {
  ProgramsRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'programsRepositoryProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$programsRepositoryHash();

  @$internal
  @override
  $ProviderElement<ProgramsRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ProgramsRepository create(Ref ref) {
    return programsRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ProgramsRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ProgramsRepository>(value),
    );
  }
}

String _$programsRepositoryHash() =>
    r'7b229a51cadbbed08d6ce61d878ab72bf4021a48';

@ProviderFor(sessionsRepository)
final sessionsRepositoryProvider = SessionsRepositoryProvider._();

final class SessionsRepositoryProvider
    extends
        $FunctionalProvider<
          SessionsRepository,
          SessionsRepository,
          SessionsRepository
        >
    with $Provider<SessionsRepository> {
  SessionsRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'sessionsRepositoryProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$sessionsRepositoryHash();

  @$internal
  @override
  $ProviderElement<SessionsRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  SessionsRepository create(Ref ref) {
    return sessionsRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SessionsRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SessionsRepository>(value),
    );
  }
}

String _$sessionsRepositoryHash() =>
    r'b0d22245ca0d506638aa473b54804a892255c6c1';

/// Session directories opened one-off via "Open a session folder..."
/// (Devices screen), for the lifetime of the app run -- see
/// [DirectorySessionsRepository]'s doc comment for why this exists
/// alongside the hub-lifecycle-bound [HubSessionsRepository].

@ProviderFor(OpenedSessionDirectories)
final openedSessionDirectoriesProvider = OpenedSessionDirectoriesProvider._();

/// Session directories opened one-off via "Open a session folder..."
/// (Devices screen), for the lifetime of the app run -- see
/// [DirectorySessionsRepository]'s doc comment for why this exists
/// alongside the hub-lifecycle-bound [HubSessionsRepository].
final class OpenedSessionDirectoriesProvider
    extends
        $NotifierProvider<
          OpenedSessionDirectories,
          List<DirectorySessionsRepository>
        > {
  /// Session directories opened one-off via "Open a session folder..."
  /// (Devices screen), for the lifetime of the app run -- see
  /// [DirectorySessionsRepository]'s doc comment for why this exists
  /// alongside the hub-lifecycle-bound [HubSessionsRepository].
  OpenedSessionDirectoriesProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'openedSessionDirectoriesProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$openedSessionDirectoriesHash();

  @$internal
  @override
  OpenedSessionDirectories create() => OpenedSessionDirectories();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(List<DirectorySessionsRepository> value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<List<DirectorySessionsRepository>>(
        value,
      ),
    );
  }
}

String _$openedSessionDirectoriesHash() =>
    r'e4c5f18c968f6eba54877e819697ae588ad5befe';

/// Session directories opened one-off via "Open a session folder..."
/// (Devices screen), for the lifetime of the app run -- see
/// [DirectorySessionsRepository]'s doc comment for why this exists
/// alongside the hub-lifecycle-bound [HubSessionsRepository].

abstract class _$OpenedSessionDirectories
    extends $Notifier<List<DirectorySessionsRepository>> {
  List<DirectorySessionsRepository> build();
  @$mustCallSuper
  @override
  WhenComplete runBuild() {
    final ref =
        this.ref
            as $Ref<
              List<DirectorySessionsRepository>,
              List<DirectorySessionsRepository>
            >;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<
                List<DirectorySessionsRepository>,
                List<DirectorySessionsRepository>
              >,
              List<DirectorySessionsRepository>,
              Object?,
              Object?
            >;
    return element.handleCreate(ref, build);
  }
}

@ProviderFor(outcomesRepository)
final outcomesRepositoryProvider = OutcomesRepositoryProvider._();

final class OutcomesRepositoryProvider
    extends
        $FunctionalProvider<
          OutcomesRepository,
          OutcomesRepository,
          OutcomesRepository
        >
    with $Provider<OutcomesRepository> {
  OutcomesRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'outcomesRepositoryProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$outcomesRepositoryHash();

  @$internal
  @override
  $ProviderElement<OutcomesRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  OutcomesRepository create(Ref ref) {
    return outcomesRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(OutcomesRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<OutcomesRepository>(value),
    );
  }
}

String _$outcomesRepositoryHash() =>
    r'e7e7d398d96bba0b007a8d6e345cf847f7016955';

@ProviderFor(liveRepository)
final liveRepositoryProvider = LiveRepositoryProvider._();

final class LiveRepositoryProvider
    extends $FunctionalProvider<LiveRepository, LiveRepository, LiveRepository>
    with $Provider<LiveRepository> {
  LiveRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'liveRepositoryProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$liveRepositoryHash();

  @$internal
  @override
  $ProviderElement<LiveRepository> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  LiveRepository create(Ref ref) {
    return liveRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(LiveRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<LiveRepository>(value),
    );
  }
}

String _$liveRepositoryHash() => r'e06e327ac5f72c4e97b76690450a6b6da906bcb9';
