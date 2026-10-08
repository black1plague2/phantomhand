import 'package:flutter/material.dart';

import 'package:opus_app/core/theme/opus_tokens.dart';

/// Builds [ThemeData] from [OpusTokens] (`docs/APP_DESIGN.md`): `mist` ground,
/// `paper` surfaces, `ink` text/primary actions, `rule` as the only border
/// color, no drop shadows (elevation 0 everywhere; depth comes from the
/// mist/paper contrast), radius hierarchy 16 (panes) / 10 (inputs, buttons) /
/// fully round (chips) / 0 (charts).
class AppTheme {
  const new _();

  static ThemeData light() => _base(OpusTokens.light, Brightness.light);
  static ThemeData dark() => _base(OpusTokens.dark, Brightness.dark);

  static ThemeData _base(OpusTokens t, Brightness brightness) {
    // v2 (`docs/design/OPUS_DESIGN_V2.md`) is a dark-only direction ("true
    // black... darker colours") -- it doesn't redesign the light palette, so
    // only dark gets v2's primary=oxblood/emphasis=crimson split (a
    // background-fill role, paired with `ink` text on top of it, verified by
    // `test/theme/opus_tokens_contrast_test.dart`). Light keeps its original
    // primary=`ink`/onPrimary=`paper` pairing (dark text as the button fill,
    // light text on top) -- also contrast-tested, and swapping it to the v2
    // pairing would need light-theme-specific oxblood/crimson tints v2 never
    // specifies.
    final isDark = brightness == Brightness.dark;
    final scheme = ColorScheme(
      brightness: brightness,
      primary: isDark ? t.lake : t.ink,
      onPrimary: isDark ? t.ink : t.paper,
      secondary: isDark ? t.alert : t.lake,
      onSecondary: isDark ? t.ink : t.paper,
      error: t.alert,
      onError: isDark ? t.ink : t.paper,
      surface: t.paper,
      onSurface: t.ink,
      surfaceContainerLowest: t.mist,
      surfaceContainerLow: t.paper,
      surfaceContainer: t.paper,
      surfaceContainerHigh: t.panelRaised,
      surfaceContainerHighest: t.panelRaised,
      outline: t.rule,
      outlineVariant: t.rule,
      onSurfaceVariant: t.slate,
      inversePrimary: t.paper,
      inverseSurface: t.ink,
      onInverseSurface: t.paper,
      // v2 §1: crimson is "emphasis only... Never large fills." Without these
      // two, Flutter derives `secondaryContainer` from `secondary` -- which in
      // dark is `alert` (crimson) -- and every Material widget that fills with
      // secondaryContainer (SegmentedButton's selected segment, ChoiceChip,
      // NavigationDrawer's indicator) renders a large saturated crimson block.
      // Observed on device 2026-09-19: the Settings "Theme" segmented control's
      // selected segment was a bright red slab. Pinned to the raised panel
      // surface so selection reads as a raised tile, the same vocabulary v2 §4
      // uses everywhere else.
      secondaryContainer: isDark ? t.panelRaised : t.mist,
      onSecondaryContainer: t.ink,
    );
    final textTheme = opusTextTheme(t.ink, dimColor: t.slate);
    return ThemeData(
      useMaterial3: true,
      brightness: brightness,
      colorScheme: scheme,
      extensions: [t],
      scaffoldBackgroundColor: t.mist,
      canvasColor: t.mist,
      textTheme: textTheme,
      fontFamily: 'AtkinsonHyperlegibleNext',
      visualDensity: VisualDensity.standard,
      splashFactory: NoSplash.splashFactory,
      // Touch targets >= 48dp everywhere per brief accessibility requirement.
      materialTapTargetSize: MaterialTapTargetSize.padded,
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: t.paper,
        indicatorColor: t.mist,
        minWidth: 72,
        selectedLabelTextStyle: textTheme.labelMedium,
        unselectedLabelTextStyle: textTheme.labelMedium?.copyWith(color: t.slate),
      ),
      navigationBarTheme: NavigationBarThemeData(
        height: 64,
        elevation: 0,
        backgroundColor: t.paper,
        indicatorColor: t.mist,
        surfaceTintColor: Colors.transparent,
      ),
      cardTheme: CardThemeData(
        elevation: 0,
        color: t.paper,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(OpusTokens.radiusPane),
          side: BorderSide(color: t.rule),
        ),
        margin: EdgeInsets.zero,
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: t.paper,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(OpusTokens.radiusPane),
          side: BorderSide(color: t.rule),
        ),
        titleTextStyle: textTheme.titleMedium,
        contentTextStyle: textTheme.bodyMedium,
      ),
      popupMenuTheme: PopupMenuThemeData(
        color: t.paper,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
          side: BorderSide(color: t.rule),
        ),
        textStyle: textTheme.bodyMedium,
      ),
      chipTheme: ChipThemeData(
        backgroundColor: t.panelRaised,
        side: BorderSide(color: t.rule),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(OpusTokens.radiusChip)),
        labelStyle: textTheme.labelMedium,
      ),
      appBarTheme: AppBarTheme(
        backgroundColor: t.mist,
        foregroundColor: t.ink,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        titleTextStyle: textTheme.titleMedium,
      ),
      dividerTheme: DividerThemeData(color: t.rule, thickness: 1, space: 1),
      inputDecorationTheme: InputDecorationTheme(
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
          borderSide: BorderSide(color: t.rule),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
          borderSide: BorderSide(color: t.rule),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
          borderSide: BorderSide(color: t.ink, width: 2),
        ),
        filled: true,
        fillColor: t.paper,
        labelStyle: textTheme.bodyMedium?.copyWith(color: t.slate),
      ),
      // v2 §4: "One primary action per screen (filled oxblood, full width)"
      // -- dark only (see the ColorScheme comment above); light keeps its
      // original ink-filled button.
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: scheme.primary,
          foregroundColor: scheme.onPrimary,
          disabledBackgroundColor: t.panelRaised,
          disabledForegroundColor: t.slate,
          elevation: 0,
          minimumSize: const Size(64, OpusTokens.touchTargetMin),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(OpusTokens.radiusControl)),
          textStyle: textTheme.labelLarge,
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: scheme.primary,
          foregroundColor: scheme.onPrimary,
          disabledBackgroundColor: t.panelRaised,
          disabledForegroundColor: t.slate,
          elevation: 0,
          minimumSize: const Size(64, OpusTokens.touchTargetMin),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(OpusTokens.radiusControl)),
          textStyle: textTheme.labelLarge,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: t.ink,
          side: BorderSide(color: t.rule),
          minimumSize: const Size(64, OpusTokens.touchTargetMin),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(OpusTokens.radiusControl)),
          textStyle: textTheme.labelLarge,
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: t.ink,
          minimumSize: const Size(48, OpusTokens.touchTargetMin),
          textStyle: textTheme.labelLarge,
        ),
      ),
      sliderTheme: SliderThemeData(
        showValueIndicator: ShowValueIndicator.onDrag,
        activeTrackColor: t.ink,
        thumbColor: t.ink,
        inactiveTrackColor: t.rule,
      ),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? t.ink : t.slate,
        ),
        trackColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? t.lake : t.panelRaised,
        ),
        trackOutlineColor: WidgetStatePropertyAll(t.rule),
      ),
      segmentedButtonTheme: SegmentedButtonThemeData(
        style: ButtonStyle(
          shape: WidgetStatePropertyAll(
            RoundedRectangleBorder(borderRadius: BorderRadius.circular(OpusTokens.radiusChip)),
          ),
          side: WidgetStatePropertyAll(BorderSide(color: t.rule)),
          // Belt and braces with `secondaryContainer` above: state the selected
          // fill explicitly so a future ColorScheme edit can't reintroduce a
          // large crimson slab here (v2 §1).
          backgroundColor: WidgetStateProperty.resolveWith(
            (states) => states.contains(WidgetState.selected) ? t.panelRaised : Colors.transparent,
          ),
          foregroundColor: WidgetStatePropertyAll(t.ink),
        ),
      ),
      tooltipTheme: TooltipThemeData(
        decoration: BoxDecoration(color: t.ink, borderRadius: BorderRadius.circular(8)),
        textStyle: textTheme.bodySmall?.copyWith(color: t.paper),
      ),
    );
  }
}

/// Semantic colors for the analytics `quality` flag (`ok`/`degraded`/`invalid`
/// per `contracts/schemas/metrics.schema.json`). Per `docs/APP_DESIGN.md`,
/// quality is never conveyed by color alone: `QualityBadge` always pairs this
/// with a word ("Reliable" / "Partial tracking" / "Not usable") and a glyph
/// (plain / hatched / struck-through). `ok` and `degraded` both stay `slate`
/// (quiet, not celebratory) -- `lake`/`ochre` are reserved for side, `leaf`
/// only for "improved beyond MDC". Only `invalid` ("Not usable", excluded
/// from trends) gets `alert`.
Color qualityColor(String quality, OpusTokens t) => switch (quality) {
      'invalid' => t.alert,
      _ => t.slate,
    };
