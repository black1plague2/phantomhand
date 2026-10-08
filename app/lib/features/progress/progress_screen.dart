import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/outcome_measure.dart';
import 'package:opus_app/l10n/app_localizations.dart';
import 'package:opus_app/shared/metrics/dose_adherence.dart';
import 'package:opus_app/shared/metrics/metric_format.dart';
import 'package:opus_app/shared/metrics/progress_data.dart';
import 'package:opus_app/shared/widgets/charts/trend_line_chart.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';
import 'package:riverpod/src/providers/future_provider.dart';

/// Longitudinal trend across sessions with an MDC ("minimal detectable
/// change") band, dose adherence against the active program's prescription,
/// and standardized outcome measures plotted alongside the VR metrics with
/// the same MDC wording -- brief A5/A6, `docs/IMPROVEMENT_BRIEF.md` §3 items
/// 8-9.
///
/// KNOWN SIMPLIFICATION: every MDC band on this screen is a synthetic-data
/// estimate, never a validated clinical constant -- see
/// `shared/metrics/progress_data.dart`'s `mdcFor` doc for the two sources
/// (opus_analytics' real, but synthetic-test-retest-derived, `mdc95` for
/// `trunk_lean_cm`/`neglect_index`; the older SD95 stand-in for every other
/// metric) and why every band built from either carries the visible
/// "(synthetic estimate)" caption below. The outcome-measure change labels
/// use a separate, even-more-clearly-flagged placeholder -- see
/// `outcomeMdcPlaceholder`'s doc in `shared/metrics/metric_format.dart`.
class ProgressScreen extends ConsumerWidget {
  const new({required this.patientId, super.key});
  final String patientId;

  static const _metricOptions = ['sparc', 'reaction_time_ms', 'movement_time_ms', 'success_rate', 'trunk_lean_cm'];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final progressAsync = ref.watch(progressDataProvider(patientId));
    final outcomesAsync = ref.watch(_outcomesProvider(patientId));

    return progressAsync.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) => ErrorRetryView(message: e.toString(), onRetry: () => ref.invalidate(progressDataProvider(patientId))),
      data: (data) {
        // Dose adherence and outcome measures are meaningful even for a
        // patient with 0 VR sessions recorded yet (a program can be
        // prescribed, or outcome measures entered, before the first VR
        // session) -- so this no longer early-returns out of the whole
        // screen when `data.isEmpty`; only the VR-metric trend section below
        // shows its own "not enough sessions" line per metric.
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            _DoseAdherenceSection(patientId: patientId),
            const Divider(height: 32),
            Text(l10n.progressTitle, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 4),
            Text(l10n.progressMdcBand, style: Theme.of(context).textTheme.bodySmall),
            Text(
              '(Synthetic estimate: computed from mock/dev data, not a validated clinical MDC.)',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: (Theme.of(context).extension<OpusTokens>() ?? OpusTokens.light).slate,
                  ),
            ),
            const SizedBox(height: 12),
            if (data.isEmpty)
              const Text('Not enough sessions for a trend yet.')
            else
              for (final metricId in _metricOptions) _MetricTrend(metricId: metricId, points: data),
            const SizedBox(height: 24),
            Text(l10n.outcomesHistory, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 4),
            Text(
              'Plotted the same way as the VR metrics above -- with a threshold band and the same '
              '"beyond normal variation" wording.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 12),
            outcomesAsync.when(
              loading: () => const LinearProgressIndicator(),
              error: (e, _) => Text(e.toString()),
              data: (entries) {
                if (entries.isEmpty) return const Text('No outcome measures recorded yet.');
                final byType = <OutcomeMeasureType, List<OutcomeMeasureEntry>>{};
                for (final e in entries) {
                  byType.putIfAbsent(e.type, () => []).add(e);
                }
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    for (final entry in byType.entries) _OutcomeMeasureTrend(type: entry.key, entries: entry.value),
                  ],
                );
              },
            ),
          ],
        );
      },
    );
  }
}

/// `docs/IMPROVEMENT_BRIEF.md` §3 item 8: "prescribed vs completed minutes
/// per week ... with the 30 min/session and >= 4 days/week targets shown in
/// plain language. No clinical claims beyond the prescription."
class _DoseAdherenceSection extends ConsumerWidget {
  const new({required this.patientId});
  final String patientId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>() ?? OpusTokens.light;
    final doseAsync = ref.watch(doseAdherenceProvider(patientId));
    return doseAsync.when(
      loading: () => const SizedBox(height: 24, child: LinearProgressIndicator()),
      error: (e, _) => Text(e.toString()),
      data: (dose) {
        if (dose == null) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Dose adherence', style: theme.textTheme.titleLarge),
              const SizedBox(height: 4),
              const Text('No active program to measure dose against.'),
            ],
          );
        }
        final weekLabel = '${dose.weekStart.year}-${dose.weekStart.month.toString().padLeft(2, '0')}-'
            '${dose.weekStart.day.toString().padLeft(2, '0')}';
        final avgMinutesText = dose.averageMinutesPerSession.toStringAsFixed(0);
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Dose adherence', style: theme.textTheme.titleLarge),
            const SizedBox(height: 4),
            Text(
              'Prescribed: ${dose.prescribedSessionsPerWeek}x/week, up to '
              '${dose.prescribedMinutesPerSession} min/session.',
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: 4),
            Text(
              dose.hasCompletedSessions
                  ? 'Week of $weekLabel: ${dose.completedDays} day(s) completed, '
                      '${dose.completedMinutes.toStringAsFixed(0)} min total ($avgMinutesText min/session average).'
                  : 'No completed sessions recorded yet.',
              style: theme.textTheme.bodyMedium?.tabular,
            ),
            if (dose.hasCompletedSessions) ...[
              const SizedBox(height: 8),
              _PlainLanguageTarget(
                met: dose.averageMinutesPerSession >= referenceMinMinutesPerSession,
                text: '$avgMinutesText min/session average '
                    '(reference target: >= $referenceMinMinutesPerSession min/session)',
                t: t,
              ),
              _PlainLanguageTarget(
                met: dose.completedDays >= referenceMinDaysPerWeek,
                text: '${dose.completedDays} day(s) this week '
                    '(reference target: >= $referenceMinDaysPerWeek days/week)',
                t: t,
              ),
            ],
          ],
        );
      },
    );
  }
}

/// One reference-target line: a plain "met"/"below" statement, never
/// dressed up as a clinical outcome claim -- just whether the number cleared
/// the literature-derived reference point named in
/// `docs/IMPROVEMENT_BRIEF.md` §1.
class _PlainLanguageTarget extends StatelessWidget {
  const new({required this.met, required this.text, required this.t});
  final bool met;
  final String text;
  final OpusTokens t;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 2),
        child: Text(
          '${met ? 'Meets' : 'Below'} reference: $text',
          style: Theme.of(context).textTheme.bodySmall?.copyWith(color: met ? t.leaf : t.slate),
        ),
      );
}

class _MetricTrend extends StatelessWidget {
  const new({required this.metricId, required this.points});
  final String metricId;
  final List<SessionMetricPoint> points;

  @override
  Widget build(BuildContext context) {
    final values = [for (final p in points) p.values[metricId]].whereType<double>().toList();
    if (values.isEmpty) return const SizedBox.shrink();
    // Real mdc95 when analytics publishes one for this metric, else the
    // SD95 stand-in -- see `progress_data.dart`'s `mdcFor` doc.
    final mdc = mdcFor(metricId, points.first);

    final theme = Theme.of(context);
    final trend = values.length >= 2 && values.last != values.first
        ? (_isImprovement(metricId, values.first, values.last) ? 'improved' : 'declined')
        : null;

    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(metricLabel(metricId), style: theme.textTheme.titleMedium),
              const Spacer(),
              if (trend != null)
                Chip(
                  label: Text(trend == 'improved' ? 'Improving' : 'Declining'),
                  backgroundColor: trend == 'improved'
                      ? theme.colorScheme.primaryContainer
                      : theme.colorScheme.errorContainer,
                ),
            ],
          ),
          TrendLineChart(
            points: [for (var i = 0; i < values.length; i++) (i.toDouble(), values[i])],
            mdcBand: mdc,
            // Run 8 fix: label the x-axis. Unlike the patient overview's
            // compact recovery line, this picker isn't tied to any one
            // program's cadence (a patient's sessions may not be weekly), so
            // "Session" (the actual unit of `points`' x index) is the honest
            // label here rather than assuming "Week".
            xAxisLabel: 'Session',
          ),
        ],
      ),
    );
  }

  // Run 8 fix: delegates to `metric_format.dart`'s `metricIsImprovement`,
  // which knows every metric's real clinical direction (previously this
  // defaulted anything other than `reaction_time_ms`/`movement_time_ms` to
  // "higher is better", which was wrong for trunk lean, tracking loss,
  // endpoint error, etc.) and handles magnitude-based metrics like
  // `neglect_index` correctly.
  bool _isImprovement(String metricId, double first, double last) => metricIsImprovement(metricId, first, last);
}

/// One standardized outcome measure's trend, styled identically to
/// [_MetricTrend] above it (same chart widget, same MDC-band shading, same
/// change-label wording/color) so the two visually read as one continuous
/// "recovery timeline" -- `docs/IMPROVEMENT_BRIEF.md` §3 item 9: "plotted
/// alongside the VR metrics ... using the same MDC language." The band width
/// is [outcomeMdcPlaceholder], not a cited clinical MDC -- see that
/// function's doc for why, and the visible "(reference threshold, not a
/// published MDC)" caveat below so this is never mistaken for a verified
/// clinical claim.
class _OutcomeMeasureTrend extends StatelessWidget {
  const new({required this.type, required this.entries});
  final OutcomeMeasureType type;
  final List<OutcomeMeasureEntry> entries;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>() ?? OpusTokens.light;
    final sorted = [...entries]..sort((a, b) => a.date.compareTo(b.date));
    final direction = sorted.length >= 2
        ? outcomeMeasureChangeDirection(type: type, first: sorted.first.score, last: sorted.last.score)
        : null;

    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(outcomeMeasureLabel(type), style: theme.textTheme.titleMedium),
              const Spacer(),
              if (direction != null)
                Text(
                  changeLabel(direction),
                  style: theme.textTheme.labelMedium?.copyWith(color: changeColor(direction, t)),
                ),
            ],
          ),
          if (sorted.length < 2)
            Text('Not enough entries for a trend yet.', style: theme.textTheme.bodySmall?.copyWith(color: t.slate))
          else ...[
            TrendLineChart(
              points: [for (var i = 0; i < sorted.length; i++) (i.toDouble(), sorted[i].score)],
              mdcBand: outcomeMdcPlaceholder(type),
              color: t.ink,
              xAxisLabel: 'Entry', // one outcome-measure entry per point, not necessarily weekly.
            ),
            Text(
              'Threshold shown is a placeholder (10% of the ${type.unit} scale range), not a published MDC.',
              style: theme.textTheme.bodySmall?.copyWith(color: t.slate),
            ),
          ],
        ],
      ),
    );
  }
}

final FutureProviderFamily<List<OutcomeMeasureEntry>, String> _outcomesProvider = FutureProvider.family<List<OutcomeMeasureEntry>, String>(
  (ref, patientId) => ref.watch(outcomesRepositoryProvider).listEntries(patientId),
);
