import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/data/models/user.dart';
import 'package:opus_app/features/auth/auth_controller.dart';
import 'package:opus_app/features/live/phantom_demo_button.dart';
import 'package:opus_app/l10n/app_localizations.dart';

class LoginScreen extends ConsumerWidget {
  const new({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final roleLabels = {
      AppRole.admin: l10n.loginRoleAdmin,
      AppRole.clinician: l10n.loginRoleClinician,
      AppRole.therapist: l10n.loginRoleTherapist,
      AppRole.nurse: l10n.loginRoleNurse,
      AppRole.patient: l10n.loginRolePatient,
    };

    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  // v3 amendment (BINDING, docs/design/OPUS_DESIGN_V2.md top
                  // box): "Zero subtitles. A title, then content." -- dropped
                  // `l10n.loginSubtitle` ("Mock authentication -- pick a role
                  // to continue"); the role buttons below are self-explanatory.
                  Text(l10n.loginTitle, style: Theme.of(context).textTheme.headlineMedium, textAlign: TextAlign.center),
                  const SizedBox(height: 24),
                  // A first-time user sees the whole Phantom Hand run with one tap: it signs in as
                  // the Clinician (mock sign-in) and opens the demo.
                  PhantomDemoButton(onPressed: () => unawaited(_runDemo(context, ref))),
                  const SizedBox(height: 24),
                  for (final role in AppRole.values)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: FilledButton(
                        style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(48)),
                        onPressed: () => ref.read(authControllerProvider.notifier).signIn(role),
                        child: Text(roleLabels[role]!),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// Signs in as the Clinician, then opens the demo (the router would otherwise
  /// send a signed-in user off the login screen to the patient list).
  Future<void> _runDemo(BuildContext context, WidgetRef ref) async {
    final router = GoRouter.of(context);
    await ref.read(authControllerProvider.notifier).signIn(AppRole.clinician);
    router.go(demoPhantomRoute);
  }
}
