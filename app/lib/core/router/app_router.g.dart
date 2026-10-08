// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'app_router.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning
/// The single [GoRouter] instance, built inside a Riverpod provider per
/// `docs/IMPROVEMENT_BRIEF.md` §3 / `OPUS/CLAUDE.md`: refreshListenable on
/// auth state, one top-level redirect function, and each feature folder
/// (`features/<name>/<name>_routes.dart`) exports its own `List<RouteBase>`
/// that this function composes -- no feature screen is imported directly
/// here.
///
/// `keepAlive: true`: this provider's *body* never reads (`ref.watch`) auth
/// state -- only `_AuthRefreshListenable.new` below does, via `ref.listen`,
/// specifically so an auth change notifies the existing `GoRouter` (which
/// re-runs `redirect`) instead of rebuilding this provider and constructing
/// a brand new `GoRouter` instance. `keepAlive` closes the remaining gap: an
/// auto-dispose provider that transiently loses its last watcher (e.g. a
/// widget tree rebuild that unmounts and remounts `MaterialApp.router` in
/// the same frame, or a test `ProviderContainer` churn) would otherwise be
/// disposed and recreated -- verified against exactly that regression by
/// `test/router/app_router_test.dart`'s "auth change does not recreate the
/// GoRouter instance" case.

@ProviderFor(appRouter)
final appRouterProvider = AppRouterProvider._();

/// The single [GoRouter] instance, built inside a Riverpod provider per
/// `docs/IMPROVEMENT_BRIEF.md` §3 / `OPUS/CLAUDE.md`: refreshListenable on
/// auth state, one top-level redirect function, and each feature folder
/// (`features/<name>/<name>_routes.dart`) exports its own `List<RouteBase>`
/// that this function composes -- no feature screen is imported directly
/// here.
///
/// `keepAlive: true`: this provider's *body* never reads (`ref.watch`) auth
/// state -- only `_AuthRefreshListenable.new` below does, via `ref.listen`,
/// specifically so an auth change notifies the existing `GoRouter` (which
/// re-runs `redirect`) instead of rebuilding this provider and constructing
/// a brand new `GoRouter` instance. `keepAlive` closes the remaining gap: an
/// auto-dispose provider that transiently loses its last watcher (e.g. a
/// widget tree rebuild that unmounts and remounts `MaterialApp.router` in
/// the same frame, or a test `ProviderContainer` churn) would otherwise be
/// disposed and recreated -- verified against exactly that regression by
/// `test/router/app_router_test.dart`'s "auth change does not recreate the
/// GoRouter instance" case.

final class AppRouterProvider
    extends $FunctionalProvider<GoRouter, GoRouter, GoRouter>
    with $Provider<GoRouter> {
  /// The single [GoRouter] instance, built inside a Riverpod provider per
  /// `docs/IMPROVEMENT_BRIEF.md` §3 / `OPUS/CLAUDE.md`: refreshListenable on
  /// auth state, one top-level redirect function, and each feature folder
  /// (`features/<name>/<name>_routes.dart`) exports its own `List<RouteBase>`
  /// that this function composes -- no feature screen is imported directly
  /// here.
  ///
  /// `keepAlive: true`: this provider's *body* never reads (`ref.watch`) auth
  /// state -- only `_AuthRefreshListenable.new` below does, via `ref.listen`,
  /// specifically so an auth change notifies the existing `GoRouter` (which
  /// re-runs `redirect`) instead of rebuilding this provider and constructing
  /// a brand new `GoRouter` instance. `keepAlive` closes the remaining gap: an
  /// auto-dispose provider that transiently loses its last watcher (e.g. a
  /// widget tree rebuild that unmounts and remounts `MaterialApp.router` in
  /// the same frame, or a test `ProviderContainer` churn) would otherwise be
  /// disposed and recreated -- verified against exactly that regression by
  /// `test/router/app_router_test.dart`'s "auth change does not recreate the
  /// GoRouter instance" case.
  AppRouterProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'appRouterProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$appRouterHash();

  @$internal
  @override
  $ProviderElement<GoRouter> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GoRouter create(Ref ref) {
    return appRouter(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GoRouter value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GoRouter>(value),
    );
  }
}

String _$appRouterHash() => r'ea17e57a930d05062826200295b6d0bb94cd5adb';
