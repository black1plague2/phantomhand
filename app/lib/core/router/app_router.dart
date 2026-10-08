import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/core/a11y/adaptive_shell.dart';
import 'package:opus_app/core/router/programs_home_screen.dart';
import 'package:opus_app/features/auth/auth_controller.dart';
import 'package:opus_app/features/auth/auth_routes.dart';
import 'package:opus_app/features/devices/device_routes.dart';
import 'package:opus_app/features/live/live_routes.dart';
import 'package:opus_app/features/outcomes/outcome_routes.dart';
import 'package:opus_app/features/patients/patient_routes.dart';
import 'package:opus_app/features/programs/program_routes.dart';
import 'package:opus_app/features/sessions/session_routes.dart';
import 'package:opus_app/features/settings/settings_routes.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'app_router.g.dart';

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
@Riverpod(keepAlive: true)
GoRouter appRouter(Ref ref) {
  return GoRouter(
    initialLocation: '/patients',
    refreshListenable: _AuthRefreshListenable(ref),
    redirect: (context, state) {
      final signedIn = ref.read(authControllerProvider) != null;
      final onLogin = state.matchedLocation == '/login';
      if (!signedIn && !onLogin) return '/login';
      if (signedIn && onLogin) return '/patients';
      return null;
    },
    routes: [
      ...authRoutes,
      ShellRoute(
        builder: (context, state, child) => AdaptiveShell(child: child),
        routes: [
          ...patientRoutes,
          GoRoute(path: '/programs', builder: (context, state) => const ProgramsHomeScreen()),
          ...programRoutes,
          ...sessionRoutes,
          ...liveRoutes,
          ...outcomeRoutes,
          ...settingsRoutes,
          ...deviceRoutes,
        ],
      ),
    ],
  );
}

/// Bridges Riverpod's `AuthController` state to go_router's `Listenable`
/// refresh hook so signing in/out re-runs `redirect` immediately.
class _AuthRefreshListenable extends ChangeNotifier {
  new(this.ref) {
    ref.listen(authControllerProvider, (prev, next) => notifyListeners());
  }
  final Ref ref;
}
