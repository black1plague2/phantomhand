import 'dart:math' as math;

import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import 'package:opus_app/shared/design/v2_colors.dart';

/// A generic x/y trend line with an optional dashed MDC ("minimal detectable
/// change") band around the most recent value, used by both the session
/// report (SPARC/RT trend across trials) and the progress screen
/// (longitudinal trend across sessions with MDC bands) -- brief A5.
///
/// Design v2 (`docs/design/OPUS_DESIGN_V2.md` §1/§5, "TrendLine"): oxblood
/// series line, latest point crimson, `#33161A` gridlines only (no chart
/// border), `textDim` tabular axis labels, MDC shown as a dashed band rather
/// than a soft fill.
class TrendLineChart extends StatelessWidget {
  const new({
    required this.points,
    this.yLabel = '',
    this.xAxisLabel = '',
    this.mdcBand,
    this.color,
    super.key,
  });

  /// (x, y) pairs already in chart order.
  final List<(double x, double y)> points;
  final String yLabel;

  /// Run 8 fix: the x-axis had no unit at all (just bare numbers like
  /// "0 1 2 3 4 5 6"), which reads as meaningless without knowing what a step
  /// of 1 represents. Callers pass what their x actually is -- e.g. "Week"
  /// for the patient overview's longitudinal recovery line, "Session" for the
  /// progress screen's picker, "Trial" for the session report's per-trial
  /// trend -- rendered as `bottomTitles`' `axisNameWidget`. Left blank (the
  /// default) renders no axis name, same as before this fix.
  final String xAxisLabel;

  /// Half-width of a shaded band around the *first* point's y value,
  /// representing "no reliably different from baseline" -- points inside the
  /// band should be read as noise, not real change (ARCHITECTURE.md §5).
  final double? mdcBand;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    if (points.isEmpty) {
      return SizedBox(
        height: 160,
        child: Center(
          child: Text('No data yet.', style: TextStyle(color: V2Colors.textDim)),
        ),
      );
    }
    final lineColor = color ?? V2Colors.oxblood;
    final minX = points.map((p) => p.$1).reduce((a, b) => a < b ? a : b);
    final maxX = points.map((p) => p.$1).reduce((a, b) => a > b ? a : b);
    // Run 8 fix: every caller (`patient_profile_screen.dart`,
    // `progress_screen.dart`, `session_report_screen.dart`) passes whole
    // session/trial indices as x. Without an explicit `interval`, fl_chart
    // picks its own tick spacing from the pixel width and the x range, which
    // for a small integer range (e.g. 7 sessions, 0..6) came out as a
    // fraction like 0.2 -- and `getTitlesWidget` below renders
    // `v.round().toString()`, so five fractional ticks in a row all round
    // to the same integer ("0 0 0 0 0 1 1 1 1 ..."), the duplicate-label
    // rendering visible in the run7/run8 `patient_profile` "Reach area" and
    // "Trunk lean" goldens once those lines finally had real multi-point
    // data to plot. A whole-number interval (at least 1, growing for wider
    // ranges so a long trial-count session report chart doesn't cram in a
    // label per trial) keeps ticks distinct.
    final xInterval = math.max(1, ((maxX - minX) / 6).round()).toDouble();
    // Run 8 fix: the "Reach area" chart (unit m², values ~0.02-0.15) showed
    // every y tick as "0.1" or "0.0" -- `toStringAsFixed(1)` was hardcoded
    // regardless of the metric's scale, so a range that only spans a tenth of
    // a unit rounds every tick to the same 1-decimal label. `_yPrecision`
    // below picks enough decimals from the actual y-range (including the MDC
    // band, so its shaded edges get distinct labels too) to keep roughly 2-3
    // significant figures per tick, whatever unit the caller's data is in --
    // "Trunk lean" (range ~1 cm) still gets 1-2 decimals as before, "Reach
    // area" (range ~0.1 m²) now gets 3.
    final yValues = [
      ...points.map((p) => p.$2),
      if (mdcBand != null) points.first.$2 - mdcBand!,
      if (mdcBand != null) points.first.$2 + mdcBand!,
    ];
    final minY = yValues.reduce((a, b) => a < b ? a : b);
    final maxY = yValues.reduce((a, b) => a > b ? a : b);
    final yInterval = (maxY - minY) / 4;
    final yDecimals = _decimalsFor(yInterval);
    // Design v2 §2: tabular figures for every number, `textDim` axis labels.
    final axisStyle = Theme.of(context)
        .textTheme
        .bodySmall
        ?.copyWith(color: V2Colors.textDim, fontFeatures: const [FontFeature.tabularFigures()]);
    final lastX = points.last.$1;

    return SizedBox(
      height: 200,
      child: LineChart(
        LineChartData(
          minX: minX,
          maxX: maxX,
          minY: minY,
          maxY: maxY,
          gridData: FlGridData(
            drawVerticalLine: false,
            horizontalInterval: yInterval == 0 ? null : yInterval,
            getDrawingHorizontalLine: (_) => const FlLine(color: V2Colors.line, strokeWidth: 1),
          ),
          titlesData: FlTitlesData(
            topTitles: const AxisTitles(),
            rightTitles: const AxisTitles(),
            leftTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 44,
                interval: yInterval == 0 ? null : yInterval,
                getTitlesWidget: (v, _) => Text(v.toStringAsFixed(yDecimals), style: axisStyle),
              ),
            ),
            bottomTitles: AxisTitles(
              axisNameWidget: xAxisLabel.isEmpty ? null : Text(xAxisLabel, style: axisStyle),
              axisNameSize: xAxisLabel.isEmpty ? 0 : 18,
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 24,
                interval: xInterval,
                getTitlesWidget: (v, _) => Text(v.round().toString(), style: axisStyle),
              ),
            ),
          ),
          borderData: FlBorderData(show: false),
          // MDC shown as a dashed band boundary (§5: "MDC as a dashed band"),
          // not the old soft-alpha fill -- two dashed horizontal lines at
          // baseline +/- mdcBand, drawn as extra lines so they read as a
          // *band edge*, distinct from the gridlines.
          extraLinesData: mdcBand == null
              ? const ExtraLinesData()
              : ExtraLinesData(
                  horizontalLines: [
                    HorizontalLine(
                      y: points.first.$2 - mdcBand!,
                      color: V2Colors.textDim,
                      strokeWidth: 1,
                      dashArray: const [4, 4],
                    ),
                    HorizontalLine(
                      y: points.first.$2 + mdcBand!,
                      color: V2Colors.textDim,
                      strokeWidth: 1,
                      dashArray: const [4, 4],
                    ),
                  ],
                ),
          lineTouchData: LineTouchData(
            touchTooltipData: LineTouchTooltipData(
              getTooltipColor: (_) => V2Colors.panelRaised,
              getTooltipItems: (spots) => [
                for (final s in spots)
                  LineTooltipItem(
                    '${s.y.toStringAsFixed(2)}${yLabel.isEmpty ? '' : ' $yLabel'}',
                    axisStyle ?? const TextStyle(color: V2Colors.text),
                  ),
              ],
            ),
          ),
          lineBarsData: [
            LineChartBarData(
              spots: [for (final p in points) FlSpot(p.$1, p.$2)],
              color: lineColor,
              barWidth: 2,
              dotData: FlDotData(
                show: true,
                getDotPainter: (spot, percent, bar, index) => FlDotCirclePainter(
                  radius: spot.x == lastX ? 4 : 2.5,
                  color: spot.x == lastX ? V2Colors.crimson : lineColor,
                  strokeWidth: 0,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// How many decimal places to show a y-axis tick with, given the spacing
/// between consecutive ticks ([interval]): enough that two adjacent ticks
/// never render identically (the "0.1 / 0.1 / 0.1" bug), aiming for roughly
/// 2-3 significant figures of the interval itself rather than a fixed decimal
/// count that only happens to work for metrics around 1 unit. `interval <= 0`
/// (a single repeated value, or an empty/degenerate range) falls back to 1
/// decimal, matching this chart's pre-run8 behavior for that edge case.
int _decimalsFor(double interval) {
  if (interval <= 0 || interval.isNaN || interval.isInfinite) return 1;
  var decimals = 0;
  var scaled = interval;
  while (scaled < 1 && decimals < 5) {
    scaled *= 10;
    decimals++;
  }
  return (decimals + 1).clamp(1, 5);
}
