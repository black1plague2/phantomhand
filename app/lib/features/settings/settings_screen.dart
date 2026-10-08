import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/providers/settings_providers.dart';
import 'package:opus_app/features/auth/auth_controller.dart';
import 'package:opus_app/l10n/app_localizations.dart';

class SettingsScreen extends ConsumerWidget {
  const new({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final user = ref.watch(authControllerProvider);
    final locale = ref.watch(appLocaleProvider);
    final themeMode = ref.watch(appThemeModeProvider);
    final textScale = ref.watch(textScaleOverrideProvider);
    final errorInjector = ref.watch(errorInjectorProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.navSettings)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (user != null) ListTile(title: Text(l10n.signedInAs(user.displayName, user.role.name))),
          const Divider(),
          ListTile(
            title: const Text('Language'),
            trailing: DropdownButton<Locale>(
              value: locale,
              items: const [
                DropdownMenuItem(value: Locale('en'), child: Text('English')),
                DropdownMenuItem(value: Locale('hi'), child: Text('हिंदी')),
              ],
              onChanged: (l) => l == null ? null : ref.read(appLocaleProvider.notifier).set(l),
            ),
          ),
          ListTile(
            title: const Text('Theme'),
            trailing: SegmentedButton<ThemeMode>(
              segments: const [
                ButtonSegment(value: ThemeMode.light, icon: Icon(Icons.light_mode)),
                ButtonSegment(value: ThemeMode.system, icon: Icon(Icons.brightness_auto)),
                ButtonSegment(value: ThemeMode.dark, icon: Icon(Icons.dark_mode)),
              ],
              selected: {themeMode},
              onSelectionChanged: (s) => ref.read(appThemeModeProvider.notifier).set(s.first),
            ),
          ),
          SwitchListTile(
            title: const Text('Large text (200%)'),
            value: textScale == 2.0,
            onChanged: (v) => ref.read(textScaleOverrideProvider.notifier).set(v ? 2.0 : null),
          ),
          const Divider(),
          SwitchListTile(
            title: const Text('Simulate network errors'),
            value: errorInjector.enabled,
            onChanged: (v) => ref.read(errorInjectorProvider.notifier).setEnabled(v),
          ),
          const Divider(),
          if (user != null)
            ListTile(
              leading: const Icon(Icons.logout),
              title: Text(l10n.signOut),
              onTap: () => ref.read(authControllerProvider.notifier).signOut(),
            ),
        ],
      ),
    );
  }
}
