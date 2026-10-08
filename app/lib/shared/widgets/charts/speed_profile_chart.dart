import 'dart:math' as math;

import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import 'package:opus_app/core/theme/opus_tokens.dart';

/// Draws an indicative bell-shaped speed-vs-time curve for one trial from its
/// *summary* metrics (`movement_time_ms`, `peak_speed_mps`,
/// `time_to_peak_speed_pct`).
///
/// KNOWN SIMPLIFICATION (see session log): the real per-frame speed curve
/// lives in the raw kinematics chunks (`kin_*.json`, 60-90 Hz wrist poses)
/// which `tool/sync_fixtures.dart` deliberately does NOT copy into the app
/// bundle (see that file's header) -- the app renders computed metrics, not
/// raw telemetry, and 6.8 MB of pose frames buys nothing here. This widget
/// instead synthesizes a smooth, physiologically-plausible curve (a skewed
/// bump peaking at `time_to_peak_speed_pct`) from the three real summary
/// numbers, so clinicians see an indicative shape and the real peak speed /
/// timing at a glance. If Phase 3 analytics later exposes a per-trial speed
/// series, swap this for a direct plot of it.
class SpeedProfileChart extends StatelessWidget {
  const new({
    required this.movementTimeMs,
    required this.peakSpeedMps,
    required this.timeToPeakPct,
    super.key,
  });

  final double movementTimeMs;
  final double peakSpeedMps;
  final double timeToPeakPct;

  @override
  Widget build(BuildContext context) {
    final tPeak = (timeToPeakPct.clamp(1, 99) / 100) * movementTimeMs;
    const steps = 40;
    final spots = <FlSpot>[];
    for (var i = 0; i <= steps; i++) {
      final t = movementTimeMs * i / steps;
      // Two-sided Gaussian-ish bump around tPeak, independently scaled on
      // each side so it reaches ~0 at t=0 and t=movementTimeMs.
      final sigma = t <= tPeak ? math.max(tPeak, 1) / 2.2 : math.max(movementTimeMs - tPeak, 1) / 2.2;
      final v = peakSpeedMps * math.exp(-math.pow((t - tPeak) / sigma, 2));
      spots.add(FlSpot(t, v));
    }
    final color = Theme.of(context).colorScheme.primary;
    // Visual-QA finding: at fl_chart's default tick density, x-axis labels
    // ("0ms", "20ms", "40ms" ...) overlapped into an unreadable smear on the
    // session-report goldens. Tabular figures (docs/APP_DESIGN.md §Type: "use
    // tabular figures ... for every number in ... charts") plus an explicit
    // ~5-6-tick interval -- fewer on narrow widths, per the brief -- fixes
    // both the smear and the digit-jitter.
    final axisStyle = Theme.of(context).textTheme.bodySmall?.tabular;
    return SizedBox(
      height: 180,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final tickCount = constraints.maxWidth < 260
              ? 3
              : constraints.maxWidth < 420
                  ? 5
                  : 6;
          final interval = _niceInterval(movementTimeMs / (tickCount - 1));
          return LineChart(
            LineChartData(
              minX: 0,
              maxX: movementTimeMs,
              titlesData: FlTitlesData(
                topTitles: const AxisTitles(),
                rightTitles: const AxisTitles(),
                leftTitles: AxisTitles(
                  sideTitles: SideTitles(
                    showTitles: true,
                    reservedSize: 44,
                    getTitlesWidget: (v, _) => Text(v.toStringAsFixed(1), style: axisStyle),
                  ),
                ),
                bottomTitles: AxisTitles(
                  sideTitles: SideTitles(
                    showTitles: true,
                    reservedSize: 28,
                    interval: interval,
                    // "0 ms ... 800 ms" -- a space before the unit, unlike the
                    // old "0ms"/"20ms" run-together format.
                    getTitlesWidget: (v, _) => Text('${v.round()} ms', style: axisStyle),
                  ),
                ),
              ),
              borderData: FlBorderData(show: false),
              // Known bug (fixed here): fl_chart's default touch tooltip shows
              // the raw, unrounded double (e.g. "1.8273991822...") with no
              // unit. Format to 2 dp + "m/s" (peak speed's real unit),
              // matching docs/APP_DESIGN.md's tabular-figures rule for every
              // number shown in charts.
              lineTouchData: LineTouchData(
                touchTooltipData: LineTouchTooltipData(
                  getTooltipItems: (spots) => [
                    for (final s in spots)
                      LineTooltipItem(
                        '${s.y.toStringAsFixed(2)} m/s',
                        axisStyle ?? const TextStyle(),
                      ),
                  ],
                ),
              ),
              lineBarsData: [
                LineChartBarData(
                  spots: spots,
                  isCurved: true,
                  color: color,
                  barWidth: 3,
                  dotData: const FlDotData(show: false),
                  belowBarData: BarAreaData(show: true, color: color.withValues(alpha: 0.15)),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}

/// Rounds [raw] (a movement-time-derived tick spacing, in ms) up to a "nice"
/// step so ticks land on 0/100/200/... or 0/250/500/... rather than odd
/// fractional milliseconds, while still yielding roughly the requested tick
/// count (never fewer ticks than the caller asked for, by construction: the
/// returned step is always >= [raw]).
double _niceInterval(double raw) {
  if (raw <= 0) return 1;
  const steps = [25.0, 50.0, 100.0, 150.0, 200.0, 250.0, 500.0, 1000.0];
  for (final s in steps) {
    if (raw <= s) return s;
  }
  return (raw / 500).ceil() * 500.0;
}
