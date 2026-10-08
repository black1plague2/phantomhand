import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import 'package:opus_app/shared/design/v2_colors.dart';

/// One week's session count for the dose-vs-target bar chart.
class WeeklyDoseDatum {
  const WeeklyDoseDatum({required this.label, required this.sessions});
  final String label;
  final int sessions;
}

/// Design v2 §5 "Patient > Weekly dose": bars + target line, sessions per
/// week vs. the program's weekly target.
class WeeklyDoseBars extends StatelessWidget {
  const WeeklyDoseBars({required this.weeks, required this.target, super.key});

  final List<WeeklyDoseDatum> weeks;
  final int target;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    if (weeks.isEmpty) {
      return SizedBox(
        height: 160,
        child: Center(
          child: Text('No sessions yet.', style: textTheme.bodyMedium?.copyWith(color: V2Colors.textDim)),
        ),
      );
    }
    final axisStyle = textTheme.bodySmall
        ?.copyWith(color: V2Colors.textDim, fontFeatures: const [FontFeature.tabularFigures()]);
    final maxSessions = weeks.map((w) => w.sessions).reduce((a, b) => a > b ? a : b);
    final maxY = [maxSessions, target].reduce((a, b) => a > b ? a : b) * 1.25;
    return SizedBox(
      height: 200,
      child: BarChart(
        BarChartData(
          maxY: maxY <= 0 ? 1 : maxY,
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
                reservedSize: 32,
                getTitlesWidget: (v, _) => Text(v.round().toString(), style: axisStyle),
              ),
            ),
            bottomTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 22,
                getTitlesWidget: (v, _) {
                  final i = v.round();
                  if (i < 0 || i >= weeks.length) return const SizedBox.shrink();
                  return Text(weeks[i].label, style: axisStyle);
                },
              ),
            ),
          ),
          extraLinesData: ExtraLinesData(
            horizontalLines: [
              HorizontalLine(
                y: target.toDouble(),
                color: V2Colors.crimson,
                strokeWidth: 1.5,
                dashArray: const [6, 4],
              ),
            ],
          ),
          barTouchData: BarTouchData(
            touchTooltipData: BarTouchTooltipData(
              getTooltipColor: (_) => V2Colors.panelRaised,
              getTooltipItem: (group, groupIndex, rod, rodIndex) => BarTooltipItem(
                '${rod.toY.toStringAsFixed(0)} sessions',
                axisStyle ?? const TextStyle(color: V2Colors.text),
              ),
            ),
          ),
          barGroups: [
            for (var i = 0; i < weeks.length; i++)
              BarChartGroupData(
                x: i,
                barRods: [
                  BarChartRodData(
                    toY: weeks[i].sessions.toDouble(),
                    color: V2Colors.oxblood,
                    width: 16,
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
