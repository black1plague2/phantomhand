import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'package:opus_app/core/providers/settings_providers.dart';
import 'package:opus_app/core/router/app_router.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/l10n/app_localizations.dart';

void main() {
  runApp(const ProviderScope(child: OpusApp()));
}

class OpusApp extends ConsumerWidget {
  const new({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(appRouterProvider);
    final locale = ref.watch(appLocaleProvider);
    final themeMode = ref.watch(appThemeModeProvider);
    final textScaleOverride = ref.watch(textScaleOverrideProvider);

    return MaterialApp.router(
      title: 'OPUS Clinician',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: themeMode,
      locale: locale,
      supportedLocales: AppLocalizations.supportedLocales,
      localizationsDelegates: const [
        ...AppLocalizations.localizationsDelegates,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      routerConfig: router,
      builder: (context, child) {
        final mediaQuery = MediaQuery.of(context);
        return MediaQuery(
          data: mediaQuery.copyWith(
            textScaler: textScaleOverride == null
                ? mediaQuery.textScaler
                : TextScaler.linear(textScaleOverride),
          ),
          child: child!,
        );
      },
    );
  }
}
