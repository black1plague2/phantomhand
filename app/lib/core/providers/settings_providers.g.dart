// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'settings_providers.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(AppLocale)
final appLocaleProvider = AppLocaleProvider._();

final class AppLocaleProvider extends $NotifierProvider<AppLocale, Locale> {
  AppLocaleProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'appLocaleProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$appLocaleHash();

  @$internal
  @override
  AppLocale create() => AppLocale();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(Locale value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<Locale>(value),
    );
  }
}

String _$appLocaleHash() => r'c5862a83ce916de2373bac277a08dd24ffd78a69';

abstract class _$AppLocale extends $Notifier<Locale> {
  Locale build();
  @$mustCallSuper
  @override
  WhenComplete runBuild() {
    final ref = this.ref as $Ref<Locale, Locale>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<Locale, Locale>,
              Locale,
              Object?,
              Object?
            >;
    return element.handleCreate(ref, build);
  }
}

@ProviderFor(AppThemeMode)
final appThemeModeProvider = AppThemeModeProvider._();

final class AppThemeModeProvider
    extends $NotifierProvider<AppThemeMode, ThemeMode> {
  AppThemeModeProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'appThemeModeProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$appThemeModeHash();

  @$internal
  @override
  AppThemeMode create() => AppThemeMode();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ThemeMode value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ThemeMode>(value),
    );
  }
}

String _$appThemeModeHash() => r'ed99836113f51a409116c719be4a54080ec2bc9c';

abstract class _$AppThemeMode extends $Notifier<ThemeMode> {
  ThemeMode build();
  @$mustCallSuper
  @override
  WhenComplete runBuild() {
    final ref = this.ref as $Ref<ThemeMode, ThemeMode>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<ThemeMode, ThemeMode>,
              ThemeMode,
              Object?,
              Object?
            >;
    return element.handleCreate(ref, build);
  }
}

/// Large-text accessibility toggle -- multiplies `MediaQuery.textScaler` app
/// wide (brief: "respects text scaling"). The OS/browser text-scale setting
/// already flows through automatically; this is an in-app override for
/// demoing 200% text without changing device settings.

@ProviderFor(TextScaleOverride)
final textScaleOverrideProvider = TextScaleOverrideProvider._();

/// Large-text accessibility toggle -- multiplies `MediaQuery.textScaler` app
/// wide (brief: "respects text scaling"). The OS/browser text-scale setting
/// already flows through automatically; this is an in-app override for
/// demoing 200% text without changing device settings.
final class TextScaleOverrideProvider
    extends $NotifierProvider<TextScaleOverride, double?> {
  /// Large-text accessibility toggle -- multiplies `MediaQuery.textScaler` app
  /// wide (brief: "respects text scaling"). The OS/browser text-scale setting
  /// already flows through automatically; this is an in-app override for
  /// demoing 200% text without changing device settings.
  TextScaleOverrideProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'textScaleOverrideProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$textScaleOverrideHash();

  @$internal
  @override
  TextScaleOverride create() => TextScaleOverride();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(double? value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<double?>(value),
    );
  }
}

String _$textScaleOverrideHash() => r'6f6d47682e8ec14b840bd1fec954d56f31aba91b';

/// Large-text accessibility toggle -- multiplies `MediaQuery.textScaler` app
/// wide (brief: "respects text scaling"). The OS/browser text-scale setting
/// already flows through automatically; this is an in-app override for
/// demoing 200% text without changing device settings.

abstract class _$TextScaleOverride extends $Notifier<double?> {
  double? build();
  @$mustCallSuper
  @override
  WhenComplete runBuild() {
    final ref = this.ref as $Ref<double?, double?>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<double?, double?>,
              double?,
              Object?,
              Object?
            >;
    return element.handleCreate(ref, build);
  }
}
