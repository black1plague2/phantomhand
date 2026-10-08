import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import 'package:opus_app/shared/design/v2_colors.dart';
import 'package:opus_app/shared/metrics/haptic_analysis.dart';

/// Design v2 §5 "Session report > Haptic cues": pie by cue type (trunk lean
/// / hand out of view / success) + delivered %. Colours come from the muted
/// chart-series palette (fixed outcome colours are reserved for trial
/// outcomes, not cue types), in the order the countsByType map iterates.
class CuePie extends StatelessWidget {
  const CuePie({required this.summary, super.key});

  final HapticCueSummary summary;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    if (summary.isEmpty) {
      return SizedBox(
        height: 140,
        child: Center(
          child: Text('No cues this session.', style: textTheme.bodyMedium?.copyWith(color: V2Colors.textDim)),
        ),
      );
    }
    final entries = summary.countsByType.entries.toList();
    final deliveredPct = summary.totalCount == 0 ? 0 : (100 * summary.deliveredCount / summary.totalCount).round();
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        SizedBox(
          width: 120,
          height: 120,
          child: PieChart(
            PieChartData(
              sectionsSpace: 2,
              centerSpaceRadius: 30,
              sections: [
                for (var i = 0; i < entries.length; i++)
                  PieChartSectionData(
                    value: entries[i].value.toDouble(),
                    color: V2Colors.chartSeries[i % V2Colors.chartSeries.length],
                    radius: 24,
                    showTitle: false,
                  ),
              ],
            ),
          ),
        ),
        const SizedBox(width: 20),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              for (var i = 0; i < entries.length; i++)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Row(
                    children: [
                      Container(
                        width: 10,
                        height: 10,
                        decoration: BoxDecoration(
                          color: V2Colors.chartSeries[i % V2Colors.chartSeries.length],
                          shape: BoxShape.circle,
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          hapticCueLabel(entries[i].key),
                          style: textTheme.bodyMedium?.copyWith(color: V2Colors.text),
                        ),
                      ),
                      Text(
                        '${entries[i].value}',
                        style: textTheme.bodyMedium?.copyWith(
                          color: V2Colors.textDim,
                          fontFeatures: const [FontFeature.tabularFigures()],
                        ),
                      ),
                    ],
                  ),
                ),
              const SizedBox(height: 4),
              Text(
                '$deliveredPct% delivered',
                style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
