import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import 'package:opus_app/shared/design/v2_colors.dart';

/// One trial's bar: its value (e.g. reaction time in ms) and outcome, which
/// picks the bar's fixed colour (`docs/design/OPUS_DESIGN_V2.md` §1).
class PerTrialBarDatum {
  const PerTrialBarDatum({required this.trial, required this.value, required this.outcome});
  final int trial;
  final double value;
  final String? outcome;
}

/// Design v2 §5 "Session report > Reaction time per trial": bar chart, one
/// bar per trial, coloured by outcome. Tooltip: 2 dp + [unit].
class PerTrialBars extends StatelessWidget {
  const PerTrialBars({required this.data, this.unit = 'ms', super.key});

  final List<PerTrialBarDatum> data;
  final String unit;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    if (data.isEmpty) {
      return SizedBox(
        height: 160,
        child: Center(
          child: Text('No trials yet.', style: textTheme.bodyMedium?.copyWith(color: V2Colors.textDim)),
        ),
      );
    }
    final axisStyle = textTheme.bodySmall
        ?.copyWith(color: V2Colors.textDim, fontFeatures: const [FontFeature.tabularFigures()]);
    final maxY = data.map((d) => d.value).reduce((a, b) => a > b ? a : b);
    return SizedBox(
      height: 200,
      child: BarChart(
        BarChartData(
          maxY: maxY <= 0 ? 1 : maxY * 1.15,
          gridData: FlGridData(
            drawVerticalLine: false,
            getDrawingHorizontalLine: (_) => const FlLine(color: V2Colors.line, strokeWidth: 1),
          ),
          borderData: FlBorderData(show: false),
          titlesData: FlTitlesData(
            topTitles: const AxisTitles(),
            rightTitles: const AxisTitles(),
            leftTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 40,
                getTitlesWidget: (v, _) => Text(v.round().toString(), style: axisStyle),
              ),
            ),
            bottomTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 22,
                interval: data.length > 12 ? (data.length / 6).ceilToDouble() : 1,
                getTitlesWidget: (v, _) => Text('${v.round() + 1}', style: axisStyle),
              ),
            ),
          ),
          barTouchData: BarTouchData(
            touchTooltipData: BarTouchTooltipData(
              getTooltipColor: (_) => V2Colors.panelRaised,
              getTooltipItem: (group, groupIndex, rod, rodIndex) => BarTooltipItem(
                '${rod.toY.toStringAsFixed(2)} $unit',
                axisStyle ?? const TextStyle(color: V2Colors.text),
              ),
            ),
          ),
          barGroups: [
            for (final d in data)
              BarChartGroupData(
                x: d.trial,
                barRods: [
                  BarChartRodData(
                    toY: d.value,
                    color: V2Colors.forOutcomeV3(d.outcome ?? ''),
                    width: 12,
                    borderRadius: BorderRadius.zero,
                  ),
                ],
              ),
          ],
        ),
      ),
    );
  }
}
