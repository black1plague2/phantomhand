/// Human-readable metric names + units, per `docs/APP_DESIGN.md`'s copy
/// rule: "Metrics use a human name first with the method in brackets:
/// 'Smoothness (SPARC)', 'Reaction time', 'Trunk lean', 'Reach area'." and
/// the live-monitor wireframe's literal examples ("Reaction 412 ms",
/// "Smoothness −1.9", "Trunk lean 3 cm").
///
/// Fixes the visual-QA finding that the live monitor showed raw metric ids
/// (`reaction_time_ms`) straight from `contracts/schemas/metrics.schema.json`
/// / `LIVE_PROTOCOL.md`'s `rolling_metrics` map -- those ids are a wire
/// format, not clinician-facing copy. Every screen that renders a metric by
/// its schema key (live monitor's rolling metrics, the session report table,
/// the progress screen's trend picker) should go through this file instead
/// of printing the key directly.
///
/// Also carries the shared "change beyond MDC" vocabulary
/// (`docs/APP_DESIGN.md` §Language: "Change is stated honestly") so the
/// patient overview's recovery line and the progress screen's trend chips
/// agree on both which direction counts as improvement per metric and the
/// exact wording/color.
library;

import 'package:flutter/material.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/outcome_measure.dart';

/// One metric's display metadata: the human label (with method name in
/// brackets where the design calls for it), the unit suffix to show after
/// the value (empty for unitless metrics), how many decimal places to round
/// to, and an optional transform applied to the raw value before display
/// (e.g. `success_rate`'s 0..1 fraction -> a percentage).
class MetricDisplay {
  const MetricDisplay(this.label, {this.unit = '', this.decimals = 1, this.transform});

  final String label;
  final String unit;
  final int decimals;
  final double Function(double raw)? transform;

  String format(double rawValue) {
    final value = transform == null ? rawValue : transform!(rawValue);
    final valueStr = value.toStringAsFixed(decimals);
    return unit.isEmpty ? valueStr : '$valueStr $unit';
  }

  /// "Reaction time 412 ms" / "Smoothness (SPARC) -1.9" -- the label
  /// followed by the formatted value, exactly the wireframe's convention.
  String display(double rawValue) => '$label ${format(rawValue)}';
}

/// Keyed by the metric id as it appears in `metrics.schema.json` /
/// `rolling_metrics` (`contracts/LIVE_PROTOCOL.md`) -- i.e. exactly the keys
/// `SessionMetrics`/`LiveMessage.rollingMetrics` use.
const Map<String, MetricDisplay> _metricDisplays = {
  'reaction_time_ms': MetricDisplay('Reaction time', unit: 'ms', decimals: 0),
  'movement_time_ms': MetricDisplay('Movement time', unit: 'ms', decimals: 0),
  'peak_speed_mps': MetricDisplay('Peak speed', unit: 'm/s', decimals: 2),
  'time_to_peak_speed_pct': MetricDisplay('Time to peak speed', unit: '%', decimals: 0),
  'tracking_loss_pct': MetricDisplay('Tracking loss', unit: '%', decimals: 0),
  // v3 amendment (docs/design/OPUS_DESIGN_V2.md top box, BINDING): "SPARC"
  // and "LDLJ" are never shown by those names -- layman word "Smoothness"
  // only, no method name in brackets.
  'sparc': MetricDisplay('Smoothness'),
  'ldlj': MetricDisplay('Smoothness'),
  'n_submovements': MetricDisplay('Submovements', decimals: 0),
  'path_length_ratio': MetricDisplay('Path length ratio', decimals: 2),
  // v3: "endpoint error" never shown by that name -- "Accuracy (cm)" instead.
  'endpoint_error_cm': MetricDisplay('Accuracy', unit: 'cm'),
  'trunk_displacement_cm': MetricDisplay('Trunk lean', unit: 'cm'),
  // The actual key `opus_analytics` writes into `metrics.json` (verified
  // against `contracts/fixtures/sessions/*/metrics.json`) -- kept alongside
  // `trunk_displacement_cm` above rather than replacing it, in case a
  // producer somewhere still emits the other name.
  'trunk_lean_cm': MetricDisplay('Trunk lean', unit: 'cm'),
  'success_rate': MetricDisplay('Success rate', unit: '%', decimals: 0, transform: _toPercent),
  'neglect_index': MetricDisplay('Neglect index', decimals: 2),
  'fatigue_slope': MetricDisplay('Fatigue slope', unit: 'ms/trial'),
  'reach_envelope_area_m2': MetricDisplay('Reach area', unit: 'm²', decimals: 3),
};

double _toPercent(double fraction) => fraction * 100;

/// Falls back to a title-cased, unit-stripped rendering of an unrecognized
/// key (e.g. a future metric not yet in the table above) rather than ever
/// showing a raw `snake_case_ms` id -- so an unmapped metric degrades
/// gracefully instead of regressing this fix.
MetricDisplay _displayFor(String metricKey) =>
    _metricDisplays[metricKey] ?? MetricDisplay(_titleCaseFallback(metricKey));

String _titleCaseFallback(String key) {
  final withoutKnownUnitSuffix = key.replaceAll(RegExp(r'_(ms|pct|mps|cm|m2)$'), '');
  final words = withoutKnownUnitSuffix.split('_').where((w) => w.isNotEmpty);
  return words.map((w) => w[0].toUpperCase() + w.substring(1)).join(' ');
}

/// The human label alone, e.g. `"Smoothness (SPARC)"` -- for column headers
/// and axis labels where the value is shown separately.
String metricLabel(String metricKey) => _displayFor(metricKey).label;

/// The formatted value alone (with unit), e.g. `"412 ms"`.
String metricValueText(String metricKey, double value) => _displayFor(metricKey).format(value);

/// Label + value together, e.g. `"Reaction time 412 ms"` -- the live
/// monitor's rolling-metrics cards and any other single-line readout.
String metricDisplayText(String metricKey, double value) => _displayFor(metricKey).display(value);

/// Run 8 fix: the patient overview's "Trunk lean" line said "Worse beyond
/// normal variation" while the plotted value *fell* from ~1.2 to ~0.4 -- a
/// clinically backwards label, because the old `_lowerIsBetterMetrics` set
/// only listed `reaction_time_ms`/`movement_time_ms` and silently treated
/// every other metric (including trunk lean, a distance/compensation measure
/// where less is better) as "higher is better" by default. Every metric
/// [_metricDisplays] knows about now has an explicit entry here instead of
/// relying on a bool + an implicit fallback, so a new metric can't silently
/// inherit the wrong direction.
///
/// [MetricDirection.lowerMagnitudeIsBetter] exists for signed metrics where
/// the clinically meaningful quantity is the *size* of the deviation, not its
/// sign -- `neglect_index` can be positive or negative depending on which
/// side is under-attended, so a plain "lower raw value is better" rule would
/// call a patient's neglect getting *more* negative (i.e. worse, but a lower
/// number) an improvement. Comparisons for this direction use `.abs()` on
/// both endpoints before applying the MDC threshold (see
/// [mdcChangeDirection]).
enum MetricDirection { higherIsBetter, lowerIsBetter, lowerMagnitudeIsBetter }

/// Per-metric clinical direction, keyed exactly like [_metricDisplays].
/// Sources: `docs/APP_DESIGN.md`'s metric vocabulary + standard kinematic
/// literature conventions (SPARC/LDLJ are dimensionless jerk-based smoothness
/// scores expressed as negative numbers -- a value *closer to zero*, i.e.
/// algebraically higher, means smoother movement).
const Map<String, MetricDirection> _metricDirections = {
  'reaction_time_ms': MetricDirection.lowerIsBetter,
  'movement_time_ms': MetricDirection.lowerIsBetter,
  'peak_speed_mps': MetricDirection.higherIsBetter,
  // A peak that occurs later in the movement (a higher % of the way through)
  // indicates more corrective/compensatory sub-movement rather than one
  // clean ballistic reach -- earlier (lower %) is the smoother, healthier
  // pattern.
  'time_to_peak_speed_pct': MetricDirection.lowerIsBetter,
  'tracking_loss_pct': MetricDirection.lowerIsBetter,
  'sparc': MetricDirection.higherIsBetter,
  'ldlj': MetricDirection.higherIsBetter,
  'n_submovements': MetricDirection.lowerIsBetter,
  'path_length_ratio': MetricDirection.lowerIsBetter,
  'endpoint_error_cm': MetricDirection.lowerIsBetter,
  'trunk_displacement_cm': MetricDirection.lowerIsBetter,
  'trunk_lean_cm': MetricDirection.lowerIsBetter,
  'success_rate': MetricDirection.higherIsBetter,
  'neglect_index': MetricDirection.lowerMagnitudeIsBetter,
  // The rate at which a metric degrades across trials within a session --
  // regardless of which raw direction is "better" for the underlying metric,
  // a *smaller-magnitude* slope means less within-session fatigue effect. A
  // slope near zero is the good outcome; a large slope of either sign (rapid
  // worsening, or an unrealistically rapid "improvement" that usually signals
  // a measurement artifact) is not.
  'fatigue_slope': MetricDirection.lowerMagnitudeIsBetter,
  'reach_envelope_area_m2': MetricDirection.higherIsBetter,
};

/// Falls back to [MetricDirection.higherIsBetter] only for a metric id this
/// table has genuinely never seen (mirrors [_displayFor]'s fallback
/// behavior) -- every metric [_metricDisplays] knows about is listed above
/// explicitly, so this fallback should not be reachable in practice.
MetricDirection metricDirectionFor(String metricId) => _metricDirections[metricId] ?? MetricDirection.higherIsBetter;

/// Whether [last] is an improvement over [first] for [metricId], honoring
/// [MetricDirection.lowerMagnitudeIsBetter] by comparing absolute values.
/// Used by the progress screen's simple "Improving"/"Declining" chip (no MDC
/// threshold there, just a raw comparison).
bool metricIsImprovement(String metricId, double first, double last) {
  final direction = metricDirectionFor(metricId);
  final a = direction == MetricDirection.lowerMagnitudeIsBetter ? first.abs() : first;
  final b = direction == MetricDirection.lowerMagnitudeIsBetter ? last.abs() : last;
  return direction == MetricDirection.higherIsBetter ? b > a : b < a;
}

/// `docs/APP_DESIGN.md` §Language: "Change is stated honestly: 'Improved
/// beyond normal variation' (leaf) · 'Within normal variation' (slate) ·
/// 'Worse beyond normal variation' (alert). This is the MDC band made
/// readable."
enum ChangeDirection { improved, within, worse }

/// Classifies the change from [first] (baseline) to [last] (latest) against
/// the minimal-detectable-change half-width [mdc], given which direction
/// counts as improvement. A null or zero [mdc] (no reliability estimate yet)
/// is treated as "within" -- never claim a change is real without an MDC to
/// measure it against. The metric-id-keyed [mdcChangeDirection] below is a
/// thin wrapper for VR metrics; [outcomeMeasureChangeDirection] is the
/// standardized-outcome-measure equivalent (`docs/IMPROVEMENT_BRIEF.md` §3
/// item 9: "using the same MDC language").
ChangeDirection changeDirectionFor({
  required bool lowerIsBetter,
  required double first,
  required double last,
  required double? mdc,
}) {
  if (mdc == null || mdc <= 0) return ChangeDirection.within;
  final diff = last - first;
  if (diff.abs() <= mdc) return ChangeDirection.within;
  final better = lowerIsBetter ? diff < 0 : diff > 0;
  return better ? ChangeDirection.improved : ChangeDirection.worse;
}

/// VR-metric version of [changeDirectionFor]: looks up the metric's
/// [MetricDirection] from [metricDirectionFor] and, for
/// [MetricDirection.lowerMagnitudeIsBetter] metrics, compares `.abs()` of
/// both endpoints instead of their signed raw values (see that enum value's
/// doc on [MetricDirection]).
ChangeDirection mdcChangeDirection({
  required String metricId,
  required double first,
  required double last,
  required double? mdc,
}) {
  final direction = metricDirectionFor(metricId);
  final firstCompare = direction == MetricDirection.lowerMagnitudeIsBetter ? first.abs() : first;
  final lastCompare = direction == MetricDirection.lowerMagnitudeIsBetter ? last.abs() : last;
  return changeDirectionFor(
    lowerIsBetter: direction != MetricDirection.higherIsBetter,
    first: firstCompare,
    last: lastCompare,
    mdc: mdc,
  );
}

/// The exact wording `docs/APP_DESIGN.md` specifies for each [ChangeDirection].
String changeLabel(ChangeDirection direction) => switch (direction) {
      ChangeDirection.improved => 'Improved beyond normal variation',
      ChangeDirection.within => 'Within normal variation',
      ChangeDirection.worse => 'Worse beyond normal variation',
    };

/// The token color paired with [changeLabel] -- color is never the only
/// carrier (the label itself already says the direction), but it reinforces
/// it for sighted users per the design's leaf/slate/alert roles.
Color changeColor(ChangeDirection direction, OpusTokens t) => switch (direction) {
      ChangeDirection.improved => t.leaf,
      ChangeDirection.within => t.slate,
      ChangeDirection.worse => t.alert,
    };

/// Standardized outcome measures where a *lower* score is the better
/// outcome. The Modified Ashworth Scale grades spasticity 0 (no increased
/// tone) to 4 (rigid) -- less spasticity is the improvement. FMA-UE, ARAT and
/// Box-and-Block are all "more/higher is better" (more points, more blocks
/// moved).
bool outcomeLowerIsBetter(OutcomeMeasureType type) => type == OutcomeMeasureType.mas;

/// `docs/IMPROVEMENT_BRIEF.md` §3 item 9 asks for the outcome measures to be
/// "plotted alongside the VR metrics ... using the same 'Improved/Within/
/// Worse ... normal variation' MDC language" as the VR recovery line.
///
/// KNOWN SIMPLIFICATION, stated as plainly as the VR-metric MDC95 stand-in
/// above: there is no per-measure published MDC wired into this app yet, and
/// `docs/IMPROVEMENT_BRIEF.md`'s own header is explicit that "Clinical
/// figures ... must cite primary sources before putting any number into
/// user-facing copy" -- this codebase has no verified, cited MDC constant for
/// FMA-UE/ARAT/Box-and-Block/MAS to put here, and fabricating one would be
/// exactly what that rule forbids. Rather than either (a) inventing an
/// uncited number or (b) silently applying the *VR* MDC wording to outcome
/// data with no band at all (which would always read "Within normal
/// variation", falsely flattening a real change like FMA-UE 28->41), this
/// returns a clearly-flagged **placeholder** band -- 10% of the measure's
/// total score range (`OutcomeMeasureRange`) -- and every caller must render
/// the "(placeholder threshold, not a published MDC)" caveat next to it. This
/// mirrors the existing SPARC/RT MDC95 stand-in's own honesty pattern.
/// **Contract change request** (flagged again in the session log): Opus/the
/// user should supply cited, per-measure MDC constants to replace this.
double outcomeMdcPlaceholder(OutcomeMeasureType type) {
  final (min, max) = type.range;
  return 0.10 * (max - min);
}

/// Outcome-measure version of [changeDirectionFor]/[mdcChangeDirection],
/// using [outcomeMdcPlaceholder] -- see that function's doc for why this is
/// explicitly a placeholder, not a cited clinical constant.
ChangeDirection outcomeMeasureChangeDirection({
  required OutcomeMeasureType type,
  required double first,
  required double last,
}) =>
    changeDirectionFor(
      lowerIsBetter: outcomeLowerIsBetter(type),
      first: first,
      last: last,
      mdc: outcomeMdcPlaceholder(type),
    );

/// The full clinical name for a standardized outcome measure -- used
/// wherever a measure is named in the UI (patient overview summary, progress
/// timeline) instead of the bare enum name (`fmaUe`).
String outcomeMeasureLabel(OutcomeMeasureType type) => switch (type) {
      OutcomeMeasureType.fmaUe => 'Fugl-Meyer (upper limb)',
      OutcomeMeasureType.arat => 'Action Research Arm Test',
      OutcomeMeasureType.boxAndBlock => 'Box and block test',
      OutcomeMeasureType.mas => 'Modified Ashworth Scale',
    };
