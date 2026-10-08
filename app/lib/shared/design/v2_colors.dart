import 'package:flutter/material.dart';

/// Design v2 palette (`docs/design/OPUS_DESIGN_V2.md` §1) -- the literal hex
/// values, exactly as written there. This is a **stopgap**: A1 owns
/// `core/theme/**` and is landing these as the app's real [ThemeExtension]
/// (`OpusTokens`) in parallel; that file still has the old "Kinetic Clinical
/// VR" palette as of this run (2026-09-19-A3), so screens/charts in this
/// track's scope reach these constants directly instead of
/// `Theme.of(context).extension<OpusTokens>()`.
///
/// TODO(A1): fold these into `core/theme/opus_tokens.dart`'s dark palette (v2
/// is dark-only -- there is no light variant in the design doc) and delete
/// this file once every screen can read `OpusTokens` instead. Noted in
/// `logs/sessions/2026-09-19-A3-flutter-run14.md`.
class V2Colors {
  const V2Colors._();

  /// App background. True black, not tinted.
  static const black = Color(0xFF000000);

  /// Section containers.
  static const panel = Color(0xFF120708);

  /// Tiles inside a section, table header, selected row.
  static const panelRaised = Color(0xFF1C0A0C);

  /// 1 px borders and dividers -- the only way containers are separated.
  static const line = Color(0xFF33161A);

  /// Primary: filled primary button, selected nav item, the main data series.
  static const oxblood = Color(0xFF7A1A20);

  /// Emphasis only: live indicator dot, the current value on a chart,
  /// destructive "Stop". Never large fills.
  static const crimson = Color(0xFFA8242C);

  /// Titles and values.
  static const text = Color(0xFFECE4E3);

  /// Axis labels, units, table secondary columns.
  static const textDim = Color(0xFF8F8384);

  /// Chart series palette, in order -- all muted, no saturated/glowing
  /// colours (§1). Use for generic multi-series charts (weekly dose, speed
  /// profile) that aren't one of the fixed outcome colours below.
  static const chartSeries = [crimson, bone, stone, maroon, clay];

  static const bone = Color(0xFFD8CCC4);
  static const stone = Color(0xFF7E706C);
  static const maroon = Color(0xFF5A1419);
  static const clay = Color(0xFFB9837E);

  /// Outcome colours, fixed everywhere a trial outcome is drawn (§1).
  static const outcomeSuccess = bone;
  static const outcomeTimeout = stone;
  static const outcomeDropped = clay;
  static const outcomeWrongBasket = crimson;

  static Color forOutcome(String outcome) => switch (outcome) {
        'success' => outcomeSuccess,
        'timeout' => outcomeTimeout,
        'dropped' => outcomeDropped,
        'wrong_basket' => outcomeWrongBasket,
        _ => textDim,
      };

  static String outcomeLabel(String outcome) => switch (outcome) {
        'success' => 'Success',
        'timeout' => 'Timeout',
        'dropped' => 'Dropped',
        'wrong_basket' => 'Wrong basket',
        _ => outcome,
      };

  // -- v3 amendment (BINDING, docs/design/OPUS_DESIGN_V2.md top box) --
  // mirrors `core/theme/opus_tokens.dart`'s v3 additions so screens still
  // reaching `V2Colors` directly (the A3-run1 stopgap noted above) get the
  // same semantic palette. Keep these two files' v3 values identical.
  static const good = Color(0xFF4FBF8B);
  static const warn = Color(0xFFE0A43B);
  static const info = Color(0xFF5B9BE6);
  static const accent2 = Color(0xFFA98BE0);
  static const bad = Color(0xFFE0605A);
  static const outcomeDroppedV3 = Color(0xFFC98B5B);
  static const metricMovementTime = Color(0xFF56C2C9);
  static const metricSmoothness = Color(0xFFD07BB5);

  static Color forOutcomeV3(String outcome) => switch (outcome) {
        'success' => good,
        'timeout' => warn,
        'dropped' => outcomeDroppedV3,
        'wrong_basket' => bad,
        _ => textDim,
      };

  /// Layman outcome labels: "In basket / Too slow / Dropped / Wrong basket".
  static String outcomeLabelV3(String outcome) => switch (outcome) {
        'success' => 'In basket',
        'timeout' => 'Too slow',
        'dropped' => 'Dropped',
        'wrong_basket' => 'Wrong basket',
        _ => outcome,
      };

  /// Fixed per-metric colour, same everywhere.
  static Color metricColorV3(String metricId) => switch (metricId) {
        'reactionTime' => info,
        'movementTime' => metricMovementTime,
        'peakSpeed' => accent2,
        'smoothness' => metricSmoothness,
        'trunkLean' => warn,
        'success' => good,
        'accuracy' => metricMovementTime,
        _ => info,
      };

  /// Layman metric labels: "Smoothness" not SPARC, "Accuracy" not endpoint
  /// error.
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
}
