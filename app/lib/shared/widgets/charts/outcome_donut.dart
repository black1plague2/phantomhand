import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import 'package:opus_app/shared/design/v2_colors.dart';

/// Trial outcome counts for one session or one patient's history --
/// `success`/`timeout`/`dropped`/`wrong_basket`, the only four outcomes the
/// fixed outcome colours (`docs/design/OPUS_DESIGN_V2.md` §1) apply to.
class OutcomeCounts {
  const OutcomeCounts({
    this.success = 0,
    this.timeout = 0,
    this.dropped = 0,
    this.wrongBasket = 0,
  });

  final int success;
  final int timeout;
  final int dropped;
  final int wrongBasket;

  int get total => success + timeout + dropped + wrongBasket;

  /// Success percentage, rounded, for the donut's centre label. `0` on an
  /// empty (`total == 0`) session rather than NaN.
  int get successPct => total == 0 ? 0 : (100 * success / total).round();

  /// Builds counts from a list of trial `outcome` strings, tolerating nulls
  /// (pending-analysis trials) and unknown outcomes by ignoring them.
  factory OutcomeCounts.fromOutcomes(Iterable<String?> outcomes) {
    var success = 0, timeout = 0, dropped = 0, wrongBasket = 0;
    for (final o in outcomes) {
      switch (o) {
        case 'success':
          success++;
        case 'timeout':
          timeout++;
        case 'dropped':
          dropped++;
        case 'wrong_basket':
          wrongBasket++;
      }
    }
    return OutcomeCounts(success: success, timeout: timeout, dropped: dropped, wrongBasket: wrongBasket);
  }
}

/// Design v2 §1/§5 "OutcomeDonut": success/timeout/dropped/wrong-basket
/// donut, fixed colours, success % in the centre, a 4-row legend with counts.
/// Used by the patient overview, session report and progress screens.
class OutcomeDonut extends StatelessWidget {
  const OutcomeDonut({required this.counts, super.key});

  final OutcomeCounts counts;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    if (counts.total == 0) {
      return SizedBox(
        height: 160,
        child: Center(
          child: Text('No trials yet.', style: textTheme.bodyMedium?.copyWith(color: V2Colors.textDim)),
        ),
      );
    }
    // v3 amendment (BINDING): fixed outcome colours good/warn/#C98B5B/bad +
    // layman labels ("In basket / Too slow / Dropped / Wrong basket").
    final rows = [
      (V2Colors.good, V2Colors.outcomeLabelV3('success'), counts.success),
      (V2Colors.warn, V2Colors.outcomeLabelV3('timeout'), counts.timeout),
      (V2Colors.outcomeDroppedV3, V2Colors.outcomeLabelV3('dropped'), counts.dropped),
      (V2Colors.bad, V2Colors.outcomeLabelV3('wrong_basket'), counts.wrongBasket),
    ];
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        SizedBox(
          width: 140,
          height: 140,
          child: Stack(
            alignment: Alignment.center,
            children: [
              PieChart(
                PieChartData(
                  sectionsSpace: 2,
                  centerSpaceRadius: 42,
                  sections: [
                    for (final r in rows)
                      if (r.$3 > 0)
                        PieChartSectionData(
                          value: r.$3.toDouble(),
                          color: r.$1,
                          radius: 26,
                          showTitle: false,
                        ),
                  ],
                ),
              ),
              Padding(
                // Design v2 §7: "text scales to 2.0 without clipping" -- at
                // 2x text scale the centre label no longer fits the fixed
                // 140x140 donut (found via `--update-goldens`'s real
                // RenderFlex overflow, not a hand-picked breakpoint).
                // `FittedBox` shrinks the label back down rather than
                // clipping it, while leaving the label at its natural
                // (unscaled-down) size on every normal text scale.
                padding: const EdgeInsets.all(8),
                child: FittedBox(
                  fit: BoxFit.scaleDown,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        '${counts.successPct}%',
                        style: textTheme.headlineSmall?.copyWith(
                          color: V2Colors.text,
                        ),
                      ),
                      Text('in basket', style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim)),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(width: 20),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final r in rows) _LegendRow(color: r.$1, label: r.$2, count: r.$3),
            ],
          ),
        ),
      ],
    );
  }
}

class _LegendRow extends StatelessWidget {
  const _LegendRow({required this.color, required this.label, required this.count});
  final Color color;
  final String label;
  final int count;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          Container(width: 10, height: 10, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
          const SizedBox(width: 8),
          Expanded(
            child: Text(label, style: textTheme.bodyMedium?.copyWith(color: V2Colors.text)),
          ),
          Text(
            '$count',
            style: textTheme.bodyMedium?.copyWith(
              color: V2Colors.textDim,
              fontFeatures: const [FontFeature.tabularFigures()],
            ),
          ),
        ],
      ),
    );
  }
}
