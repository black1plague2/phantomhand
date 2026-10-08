import 'package:flutter/cupertino.dart' show MediaQuery;
import 'package:flutter/material.dart';
import 'package:flutter/widgets.dart' show MediaQuery;

/// OPUS design tokens (`docs/APP_DESIGN.md`) as a [ThemeExtension] so every
/// widget can reach the exact palette/shape/spacing vocabulary via
/// `Theme.of(context).extension<OpusTokens>()!` instead of hardcoding colors.
///
/// Roles (never reassigned outside this meaning):
/// - `mist`: app ground. `paper`: working surfaces (forms, report body, charts).
/// - `ink`: text, primary filled buttons, chart baselines. `slate`: secondary
///   text, inactive icons, axis labels. `rule`: the only separator/outline color.
/// - `lake`: the LEFT side (paths, L/R charts, side chips) -- never anything else.
/// - `ochre`: the RIGHT side -- never anything else.
/// - `leaf`: ONLY "improved beyond MDC" -- never decorative.
/// - `alert`: errors, Stop/End session, invalid data -- never decorative.
@immutable
class OpusTokens extends ThemeExtension<OpusTokens> {
  const new({
    required this.mist,
    required this.paper,
    required this.ink,
    required this.slate,
    required this.rule,
    required this.lake,
    required this.ochre,
    required this.leaf,
    required this.alert,
    Color? panelRaised,
  }) : panelRaised = panelRaised ?? paper;

  final Color mist;
  final Color paper;
  final Color ink;
  final Color slate;
  final Color rule;
  final Color lake;
  final Color ochre;
  final Color leaf;
  final Color alert;

  /// `docs/design/OPUS_DESIGN_V2.md` §1 `panelRaised` -- tiles inside a
  /// section, table header, selected row. One level up from [paper] (a
  /// section's own background); [paper] itself now plays the "panel" role.
  /// Defaults to [paper] for any caller still constructing [OpusTokens]
  /// without it.
  final Color panelRaised;

  static const light = OpusTokens(
    mist: Color(0xFFEDF1F4),
    paper: Color(0xFFFFFFFF),
    ink: Color(0xFF1C2833),
    slate: Color(0xFF5E6B77),
    rule: Color(0xFFD3DBE2),
    lake: Color(0xFF2F6FA8),
    ochre: Color(0xFFB8741F),
    // Was 0xFF2E7D5B: that shade meets 4.5:1 on `paper` but only 4.40:1 on
    // `mist` (the "improved" change label can render directly on the mist
    // scaffold background, not just on a paper card -- e.g. the patient
    // overview's recovery-line change labels). Darkened green channel by 3
    // (0x7D -> 0x7A), a change too small to read as a different color, to
    // clear WCAG AA 4.5:1 against both surfaces (see
    // `test/theme/opus_tokens_contrast_test.dart`).
    leaf: Color(0xFF2E7A5B),
    alert: Color(0xFFB3261E),
  );

  // Dark palette = Design v2 (`docs/design/OPUS_DESIGN_V2.md`, 2026-09-19),
  // binding -- supersedes the Kinetic Clinical VR direction (the user
  // rejected it: "too neon, too much text, too many buttons, weak
  // analytics"). v2's palette is literally the only colors allowed
  // (§1) -- mapped onto OPUS's existing semantic roles rather than adding new
  // ones, since `mist`/`paper`/`ink`/`slate`/`rule`/`lake`/`ochre`/`leaf`/
  // `alert` are load-bearing across every screen in the app, in flight across
  // three agents concurrently:
  // - `mist` (app ground) = v2 `black` #000000 -- true black, not tinted.
  // - `paper` (section containers) = v2 `panel` #120708.
  // - `panelRaised` (tiles inside a section) = v2 `panelRaised` #1C0A0C (new
  //   field, see above -- v2 needs a THIRD surface level the old two-surface
  //   mist/paper model didn't have).
  // - `ink` (titles/values/body text) = v2 `text` #ECE4E3.
  // - `slate` (secondary text, axis/table meta) = v2 `textDim` #8F8384.
  // - `rule` (the only border/divider color) = v2 `line` #33161A.
  // - `lake` = v2 `oxblood` #7A1A20 -- repurposed from "left side" accent to
  //   v2's PRIMARY role (filled primary button, selected nav indicator, main
  //   chart data series) since v2 has no left/right side concept at all and
  //   explicitly forbids the old ice-blue. `side()` below still returns a
  //   distinct color per side (oxblood vs. the chart-series `stone`), just no
  //   longer named for it.
  // - `ochre` = v2 chart-series `stone` #7E706C -- the other side / secondary
  //   series color, a muted neutral (v2 forbids saturated/neon secondary
  //   accents).
  // - `leaf` ("improved beyond MDC") = v2 chart-series `bone` #D8CCC4 (also
  //   v2's fixed "success" outcome color) -- v2 explicitly moves improvement
  //   vs. decline to an arrow glyph + sign instead of a green/red color (§1
  //   "Improvement vs. decline is shown with an arrow glyph + sign, not
  //   green/red"), so `leaf` no longer needs to read as "green," just as a
  //   calm, legible highlight distinct from body text.
  // - `alert` (errors, Stop, destructive, live indicator dot) = v2 `crimson`
  //   #A8242C -- v2 §1's own "emphasis only... destructive Stop" role, an
  //   exact match for OPUS's existing `alert` role.
  static const dark = OpusTokens(
    mist: Color(0xFF000000), // black
    paper: Color(0xFF120708), // panel
    panelRaised: Color(0xFF1C0A0C), // panelRaised
    ink: Color(0xFFECE4E3), // text
    slate: Color(0xFF8F8384), // textDim
    rule: Color(0xFF33161A), // line
    lake: Color(0xFF7A1A20), // oxblood (primary)
    ochre: Color(0xFF7E706C), // stone (secondary chart series)
    leaf: Color(0xFFD8CCC4), // bone (success outcome / "improved" highlight)
    alert: Color(0xFFA8242C), // crimson (emphasis/destructive)
  );

  /// v2 §1's fixed chart-series order (muted, no saturated/glowing colors):
  /// crimson, bone, stone, maroon, clay. Used by any chart needing more than
  /// the two `lake`/`ochre` series colors (e.g. a 5-outcome breakdown).
  static const chartSeries = [
    Color(0xFFA8242C), // crimson
    Color(0xFFD8CCC4), // bone
    Color(0xFF7E706C), // stone
    Color(0xFF5A1419), // maroon
    Color(0xFFB9837E), // clay
  ];

  /// v2 §1's fixed trial-outcome colors (same everywhere in the app):
  /// success = bone, timeout = stone, dropped = clay, wrong basket = crimson.
  ///
  /// SUPERSEDED for new UI by [outcomeColorV3]/[outcomeLabelV3] below (the v3
  /// amendment, 2026-09-19 17:20, binding). Kept only so any not-yet-migrated
  /// caller doesn't break; new code must use the v3 variants.
  static Color outcomeColor(String outcome) => switch (outcome) {
        'success' => const Color(0xFFD8CCC4),
        'timeout' => const Color(0xFF7E706C),
        'dropped' => const Color(0xFFB9837E),
        'wrong_basket' => const Color(0xFFA8242C),
        _ => const Color(0xFF7E706C),
      };

  // -- v3 amendment (docs/design/OPUS_DESIGN_V2.md top box, 2026-09-19 17:20,
  // BINDING, wins over the v2 body above): "very clustered and very dull" ->
  // colour is now used for meaning, generously but not neon. A readable
  // semantic palette, each tested >= 4.5:1 on `paper`/`panel`. These live
  // alongside (not instead of) the v2 dark palette above: `lake`/`ochre`/
  // `alert` remain the structural primary/secondary/destructive roles;
  // `good`/`warn`/`info`/`accent2`/`bad` are the new *data-meaning* colours a
  // KPI tile's left bar and value, an outcome donut slice, or a trend series
  // pick from, by what the number MEANS, not by which side of the screen
  // it's on.
  static const good = Color(0xFF4FBF8B); // success, improving, in the basket, connected
  static const warn = Color(0xFFE0A43B); // timeout, needs attention, trunk lean
  static const info = Color(0xFF5B9BE6); // reaction time series, neutral info, links
  static const accent2 = Color(0xFFA98BE0); // smoothness series, second data series
  static const bad = Color(0xFFE0605A); // dropped / wrong basket / decline / disconnected

  /// v3 outcome colours (everywhere a trial outcome renders): success=good,
  /// timeout=warn, dropped=`#C98B5B`, wrong basket=bad.
  static const outcomeDroppedV3 = Color(0xFFC98B5B);
  static Color outcomeColorV3(String outcome) => switch (outcome) {
        'success' => good,
        'timeout' => warn,
        'dropped' => outcomeDroppedV3,
        'wrong_basket' => bad,
        _ => const Color(0xFF8F8384),
      };

  /// v3 layman outcome labels ("In basket / Too slow / Dropped / Wrong
  /// basket") -- SPARC/LDLJ/endpoint-error-style jargon is never shown by
  /// name anywhere in the UI; see [metricLabelV3]/[metricColorV3] for the
  /// same rule applied to metric names.
  static String outcomeLabelV3(String outcome) => switch (outcome) {
        'success' => 'In basket',
        'timeout' => 'Too slow',
        'dropped' => 'Dropped',
        'wrong_basket' => 'Wrong basket',
        _ => outcome,
      };

  /// v3 fixed per-metric colours "so the doctor learns them" -- every screen
  /// that shows one of these metrics uses the SAME colour for it, always.
  /// Reaction time=info, Movement time=`#56C2C9`, Peak speed=accent2,
  /// Smoothness=`#D07BB5`, Trunk lean=warn, Success=good.
  static const metricMovementTime = Color(0xFF56C2C9);
  static const metricSmoothness = Color(0xFFD07BB5);
  static Color metricColorV3(String metricId) => switch (metricId) {
        'reactionTime' => info,
        'movementTime' => metricMovementTime,
        'peakSpeed' => accent2,
        'smoothness' => metricSmoothness,
        'trunkLean' => warn,
        'success' => good,
        'accuracy' => metricMovementTime,
        // Phantom Hand live card: muscle (EMG) envelope = accent2, arm movement (|accel|) = teal.
        'emg' => accent2,
        'accel' => metricMovementTime,
        _ => info,
      };

  /// v3 layman metric labels -- "Smoothness" not SPARC, "Accuracy" not
  /// endpoint error, "Movement time" / "Reaction time" / "Peak speed" /
  /// "Trunk lean" / "Success" plainly.
  static String metricLabelV3(String metricId) => switch (metricId) {
        'reactionTime' => 'Reaction time',
        'movementTime' => 'Movement time',
        'peakSpeed' => 'Peak speed',
        'smoothness' => 'Smoothness',
        'trunkLean' => 'Trunk lean',
        'success' => 'Success',
        'accuracy' => 'Accuracy',
        _ => metricId,
      };

  /// v2 §4: exactly two radii app-wide -- 12 (sections/outer containers), 8
  /// (tiles/inputs/buttons/chips -- everything "inner"). `radiusChip` and
  /// `radiusChart` point at the same two values rather than having their own
  /// (v2: "Two radii only"); chart plot areas are unrounded rectangles (0)
  /// same as before.
  static const radiusPane = 12.0;
  static const radiusControl = 8.0;
  static const radiusChip = 8.0;
  static const radiusChart = 0.0;

  /// Spacing on a 4 pt grid.
  static const space8 = 8.0;
  static const space12 = 12.0;
  static const space16 = 16.0;
  static const space24 = 24.0;
  static const space32 = 32.0;
  static const space48 = 48.0;

  /// Minimum touch target; live-session controls use [touchTargetLive].
  static const touchTargetMin = 48.0;
  static const touchTargetLive = 64.0;

  /// Color for a patient's affected/tracked side. Side is never carried by
  /// color alone elsewhere (line style + an L/R letter also encode it).
  Color side(String side) => side.toLowerCase().startsWith('l') ? lake : ochre;

  @override
  OpusTokens copyWith({
    Color? mist,
    Color? paper,
    Color? ink,
    Color? slate,
    Color? rule,
    Color? lake,
    Color? ochre,
    Color? leaf,
    Color? alert,
    Color? panelRaised,
  }) =>
      OpusTokens(
        mist: mist ?? this.mist,
        paper: paper ?? this.paper,
        ink: ink ?? this.ink,
        slate: slate ?? this.slate,
        rule: rule ?? this.rule,
        lake: lake ?? this.lake,
        ochre: ochre ?? this.ochre,
        leaf: leaf ?? this.leaf,
        alert: alert ?? this.alert,
        panelRaised: panelRaised ?? this.panelRaised,
      );

  @override
  OpusTokens lerp(ThemeExtension<OpusTokens>? other, double t) {
    if (other is! OpusTokens) return this;
    return OpusTokens(
      mist: Color.lerp(mist, other.mist, t)!,
      paper: Color.lerp(paper, other.paper, t)!,
      ink: Color.lerp(ink, other.ink, t)!,
      slate: Color.lerp(slate, other.slate, t)!,
      rule: Color.lerp(rule, other.rule, t)!,
      lake: Color.lerp(lake, other.lake, t)!,
      ochre: Color.lerp(ochre, other.ochre, t)!,
      leaf: Color.lerp(leaf, other.leaf, t)!,
      alert: Color.lerp(alert, other.alert, t)!,
      panelRaised: Color.lerp(panelRaised, other.panelRaised, t)!,
    );
  }
}

/// One entry of `docs/design/OPUS_DESIGN_V2.md` §2's type scale: screen
/// title 22/700, section title 14/600, big value 26/700 (unit sits beside it
/// at 12/400 `textDim`, not part of this scale), body/row 13/400, axis/table
/// meta 11/400 `textDim`.
///
/// Sizes reduced 2026-09-19 (Opus, amends v2 §2). The original scale
/// (28/17/34/15/12) was authored against the 390 dp golden width, but the
/// pilot phone (CPH2381: 1080x2412 at density 480) renders **360 dp** wide
/// with system `font_scale` 1.0 -- so every screen was ~8 % over-wide on top
/// of an already-large scale. On device the result was two-line list rows,
/// wrapped section titles and a cramped bottom nav. Verified against the
/// baseline screenshot `logs/sessions/screens/app/device/2026-09-19-baseline/`.
enum OpusTextStep { axis, body, sectionTitle, screenTitle, bigValue }

const _opusFontFamily = 'AtkinsonHyperlegibleNext';

double opusFontSize(OpusTextStep step, {double scaleFactor = 1}) {
  final base = switch (step) {
    OpusTextStep.axis => 11.0,
    OpusTextStep.body => 13.0,
    OpusTextStep.sectionTitle => 14.0,
    OpusTextStep.screenTitle => 22.0,
    OpusTextStep.bigValue => 26.0,
  };
  return base * scaleFactor;
}

/// Builds the app [TextTheme] from the v2 scale, every number carrying
/// tabular figures by default (v2 §2: "Tabular figures for every number") --
/// call sites that render prose rather than a number can still opt out via
/// `.copyWith(fontFeatures: [])`, but the default now matches v2's rule
/// rather than requiring every metric/table call site to opt in via the
/// [TabularFigures] extension below. [scaleFactor] is the extra "patient
/// mode" multiplier (1.25) on top of the system text-scale setting, which
/// Flutter applies separately via [MediaQuery.textScaler].
TextTheme opusTextTheme(Color color, {double scaleFactor = 1, Color? dimColor}) {
  TextStyle style(OpusTextStep step, FontWeight weight, double height, {Color? c}) => TextStyle(
        fontFamily: _opusFontFamily,
        fontSize: opusFontSize(step, scaleFactor: scaleFactor),
        fontWeight: weight,
        height: height,
        color: c ?? color,
        fontFeatures: const [FontFeature.tabularFigures()],
      );
  final dim = dimColor ?? color;
  return TextTheme(
    bodySmall: style(OpusTextStep.axis, FontWeight.w400, 1.4, c: dim),
    bodyMedium: style(OpusTextStep.body, FontWeight.w400, 1.4),
    bodyLarge: style(OpusTextStep.body, FontWeight.w400, 1.4),
    labelSmall: style(OpusTextStep.axis, FontWeight.w500, 1.2, c: dim),
    labelMedium: style(OpusTextStep.body, FontWeight.w600, 1.2),
    labelLarge: style(OpusTextStep.body, FontWeight.w600, 1.2),
    titleSmall: style(OpusTextStep.sectionTitle, FontWeight.w600, 1.2),
    titleMedium: style(OpusTextStep.sectionTitle, FontWeight.w600, 1.2),
    titleLarge: style(OpusTextStep.screenTitle, FontWeight.w700, 1.15),
    headlineSmall: style(OpusTextStep.bigValue, FontWeight.w700, 1.1),
    headlineMedium: style(OpusTextStep.bigValue, FontWeight.w700, 1.1),
    headlineLarge: style(OpusTextStep.screenTitle, FontWeight.w700, 1.15),
    displaySmall: style(OpusTextStep.bigValue, FontWeight.w700, 1.1),
    displayMedium: style(OpusTextStep.bigValue, FontWeight.w700, 1.1),
    displayLarge: style(OpusTextStep.screenTitle, FontWeight.w700, 1.15),
  );
}

/// Tabular-figure variant of any [TextStyle], for every number in tables,
/// live readouts and charts (`docs/APP_DESIGN.md` §Type).
extension TabularFigures on TextStyle {
  TextStyle get tabular => copyWith(
        fontFeatures: const [FontFeature.tabularFigures()],
      );
}
