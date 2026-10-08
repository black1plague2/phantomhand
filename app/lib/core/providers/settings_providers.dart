import 'package:flutter/material.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'settings_providers.g.dart';

@Riverpod(keepAlive: true)
class AppLocale extends _$AppLocale {
  @override
  Locale build() => const Locale('en');

  void set(Locale locale) => state = locale;
}

@Riverpod(keepAlive: true)
class AppThemeMode extends _$AppThemeMode {
  // Dark by default (R3 D11): the live card's status colours only reach 3:1 on
  // the dark panel, and the laptop/projector then matches the phone. The
  // Settings toggle (light / auto / dark) still overrides it.
  @override
  ThemeMode build() => ThemeMode.dark;

  void set(ThemeMode mode) => state = mode;
}

/// Large-text accessibility toggle -- multiplies `MediaQuery.textScaler` app
/// wide (brief: "respects text scaling"). The OS/browser text-scale setting
/// already flows through automatically; this is an in-app override for
/// demoing 200% text without changing device settings.
@Riverpod(keepAlive: true)
class TextScaleOverride extends _$TextScaleOverride {
  @override
  double? build() => null;

  void set(double? scale) => state = scale;
}
