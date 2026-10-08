import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:opus_app/data/models/user.dart';
import 'package:opus_app/features/auth/auth_controller.dart';
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
    );
  }
}
