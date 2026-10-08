import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import 'package:opus_app/shared/design/v2_colors.dart';

/// Design v2 §5 "Monitor > Reaction time": a minimal, axis-free trend line
/// for per-trial values within the *current* session -- no gridlines, no
/// axis labels, just the shape, latest point crimson.
class Sparkline extends StatelessWidget {
  const Sparkline({required this.values, this.height = 48, super.key});

  final List<double> values;
  final double height;

  @override
  Widget build(BuildContext context) {
    if (values.isEmpty) {
      return SizedBox(
        height: height,
        child: Center(
          child: Text(
            'No trials yet.',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
          ),
        ),
      );
    }
    final minY = values.reduce((a, b) => a < b ? a : b);
    final maxY = values.reduce((a, b) => a > b ? a : b);
    final pad = (maxY - minY).abs() < 1e-9 ? 1.0 : (maxY - minY) * 0.1;
    return SizedBox(
      height: height,
      child: LineChart(
        LineChartData(
          minY: minY - pad,
          maxY: maxY + pad,
          gridData: const FlGridData(show: false),
          titlesData: const FlTitlesData(show: false),
          borderData: FlBorderData(show: false),
          lineTouchData: const LineTouchData(enabled: false),
          lineBarsData: [
            LineChartBarData(
              spots: [for (var i = 0; i < values.length; i++) FlSpot(i.toDouble(), values[i])],
              color: V2Colors.oxblood,
              barWidth: 2,
              dotData: FlDotData(
                show: true,
                getDotPainter: (spot, percent, bar, index) => FlDotCirclePainter(
                  radius: index == values.length - 1 ? 3.5 : 0,
                  color: V2Colors.crimson,
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
