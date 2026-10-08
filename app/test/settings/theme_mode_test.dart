// B1 (R3 D11): the app starts in dark mode, and the Settings toggle still
// switches between light / auto / dark.
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/providers/settings_providers.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/features/settings/settings_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';
import 'package:opus_app/main.dart';

void main() {
  test('the theme mode provider starts dark, and set() still overrides it', () {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    expect(container.read(appThemeModeProvider), ThemeMode.dark);
    container.read(appThemeModeProvider.notifier).set(ThemeMode.light);
    expect(container.read(appThemeModeProvider), ThemeMode.light);
  });

  testWidgets('the real app boots in the dark theme', (tester) async {
    await tester.pumpWidget(const ProviderScope(child: OpusApp()));
    await tester.pumpAndSettle();
    expect(tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode, ThemeMode.dark);
    expect(Theme.of(tester.element(find.byType(Scaffold).first)).brightness, Brightness.dark);
  });

  testWidgets('the Settings toggle switches light, auto and dark', (tester) async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: Consumer(
          builder: (context, ref, _) => MaterialApp(
            theme: AppTheme.light(),
            darkTheme: AppTheme.dark(),
            themeMode: ref.watch(appThemeModeProvider),
            localizationsDelegates: const [
              ...AppLocalizations.localizationsDelegates,
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            supportedLocales: AppLocalizations.supportedLocales,
            home: const SettingsScreen(),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    Brightness shown() => Theme.of(tester.element(find.byType(SettingsScreen))).brightness;
    expect(shown(), Brightness.dark);

    await tester.tap(find.byIcon(Icons.light_mode));
    await tester.pumpAndSettle();
    expect(container.read(appThemeModeProvider), ThemeMode.light);
    expect(shown(), Brightness.light);

    await tester.tap(find.byIcon(Icons.brightness_auto));
    await tester.pumpAndSettle();
    expect(container.read(appThemeModeProvider), ThemeMode.system);

    await tester.tap(find.byIcon(Icons.dark_mode));
    await tester.pumpAndSettle();
    expect(container.read(appThemeModeProvider), ThemeMode.dark);
    expect(shown(), Brightness.dark);
  });
}
