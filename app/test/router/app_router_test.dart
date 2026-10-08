// `docs/IMPROVEMENT_BRIEF.md` §3 / `OPUS/CLAUDE.md`: "GoRouter lives inside a
// Riverpod provider (refreshListenable on auth/role; one redirect function)
// ... The router must not be rebuilt on every state change." This test
// exercises the real `appRouterProvider` end to end -- sign in, sign out --
// and asserts both halves of that requirement: (1) the redirect behavior
// works (signed-out lands on /login, signed-in lands on /patients) and (2)
// `container.read(appRouterProvider)` returns the *same* `GoRouter` instance
// throughout, proving `_AuthRefreshListenable` (which notifies the existing
// router rather than the provider rebuilding a new one) plus `keepAlive:
// true` actually holds.
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/core/router/app_router.dart';
import 'package:opus_app/data/models/user.dart';
import 'package:opus_app/features/auth/auth_controller.dart';
import 'package:opus_app/features/auth/login_screen.dart';
import 'package:opus_app/features/patients/patient_list_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';

/// `flutter test` runs each test body inside a fake-clock zone: a real
/// `Timer`/`Future.delayed` (which is exactly what every mock repository
/// call goes through, `Env.mockLatency`) only ever fires when something
/// pumps that fake clock forward (`tester.pump`/`pumpAndSettle`) -- nothing
/// advances it just by sitting at a plain `await` in the test body. So
/// `await notifier.signIn(...)` directly, as an earlier version of this
/// test did, deadlocks forever: `signIn()` suspends on its internal
/// `Future.delayed`, and nothing pumps the clock while the test is stuck
/// waiting for `signIn()` itself to return. Confirmed directly, not
/// theorized: that version hit the harness's 10-minute test timeout both
/// standalone and combined with other files (see the session log). The fix
/// is to never await the sign-in/out call directly -- start it (`unawaited`)
/// so it suspends and returns control to the test body immediately, then
/// pump; `pumpAndSettle()` (unlike a small fixed number of `pump(duration)`
/// calls) keeps advancing the clock for as long as any frame is still
/// scheduled, which is exactly what a still-pending mock-repository call
/// does, so it reliably runs the clock past the 220ms delay and lets the
/// suspended future resume.
Future<void> _signInAndSettle(WidgetTester tester, ProviderContainer container, AppRole role) async {
  unawaited(container.read(authControllerProvider.notifier).signIn(role));
  // Jump the fake clock past the mock repository's simulated latency
  // directly (`Env.mockLatency` = 220ms) before the general settle pass --
  // `pumpAndSettle()` alone occasionally stopped one frame short of the
  // redirect actually landing, confirmed by running this test repeatedly.
  await tester.pump(const Duration(milliseconds: 300));
  await tester.pumpAndSettle();
}

Future<void> _signOutAndSettle(WidgetTester tester, ProviderContainer container) async {
  unawaited(container.read(authControllerProvider.notifier).signOut());
  await tester.pump(const Duration(milliseconds: 300));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('auth state change redirects without recreating the GoRouter instance', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);

    final router = container.read(appRouterProvider);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          routerConfig: router,
          localizationsDelegates: const [
            ...AppLocalizations.localizationsDelegates,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          supportedLocales: AppLocalizations.supportedLocales,
        ),
      ),
    );
    await tester.pumpAndSettle();

    // Signed out on first build -> the single top-level redirect function
    // sends every non-/login location to /login.
    expect(find.byType(LoginScreen), findsOneWidget);
    expect(find.byType(PatientListScreen), findsNothing);
    expect(GoRouter.of(tester.element(find.byType(LoginScreen))), same(router));

    await _signInAndSettle(tester, container, AppRole.clinician);

    // Signed in -> redirected off /login to the initial /patients location.
    expect(find.byType(LoginScreen), findsNothing);
    expect(find.byType(PatientListScreen), findsOneWidget);
    // The critical assertion: still the exact same GoRouter object identity
    // -- the auth change re-ran `redirect` via `refreshListenable`, it did
    // NOT cause the `appRouter` provider to rebuild a new GoRouter.
    expect(container.read(appRouterProvider), same(router));
    expect(GoRouter.of(tester.element(find.byType(PatientListScreen))), same(router));

    await _signOutAndSettle(tester, container);

    expect(find.byType(LoginScreen), findsOneWidget);
    expect(container.read(appRouterProvider), same(router));
  });
}
