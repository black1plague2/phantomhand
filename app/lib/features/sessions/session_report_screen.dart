import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/embodiment.dart';
import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/mock/phantom_demo_repository.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/features/sessions/embodiment_report.dart';
import 'package:opus_app/l10n/app_localizations.dart';
import 'package:opus_app/shared/clinical/phantom_demo_strings.dart';
import 'package:opus_app/shared/design/v2_colors.dart';
import 'package:opus_app/shared/metrics/events_derived_metrics.dart';
import 'package:opus_app/shared/metrics/haptic_analysis.dart';
import 'package:opus_app/shared/widgets/charts/cue_pie.dart';
import 'package:opus_app/shared/widgets/charts/outcome_donut.dart';
import 'package:opus_app/shared/widgets/charts/per_trial_bars.dart';
import 'package:opus_app/shared/widgets/charts/speed_profile_chart.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';
import 'package:opus_app/shared/widgets/section.dart';
import 'package:riverpod/src/providers/future_provider.dart';

class SessionReportScreen extends ConsumerStatefulWidget {
  const new({required this.patientId, required this.sessionId, super.key});
  final String patientId;
  final String sessionId;

  @override
  ConsumerState<SessionReportScreen> createState() => _SessionReportScreenState();
}

class _SessionReportScreenState extends ConsumerState<SessionReportScreen> {
  int _selectedTrialIndex = 0;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final metricsAsync = ref.watch(_metricsProvider(widget.sessionId));
    final envelopeAsync = ref.watch(_envelopeProvider(widget.sessionId));
    // Design v2 §5 "Session report": title = date, app-bar subtitle-free.
    final title = envelopeAsync.maybeWhen(
      data: (env) => env == null ? 'Session' : _formatDate(env.startedAt),
      orElse: () => 'Session',
    );

    return Scaffold(
      backgroundColor: V2Colors.black,
      appBar: AppBar(
        backgroundColor: V2Colors.black,
        foregroundColor: V2Colors.text,
        title: Text(title),
      ),
      body: metricsAsync.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetryView(
          message: e.toString(),
          onRetry: () => ref.invalidate(_metricsProvider(widget.sessionId)),
        ),
        data: (metrics) {
          if (metrics == null) {
            // A Phantom Hand run without a metrics.json (no PC analysed it): the headset's own summary is the report.
            final own = ref.watch(_embodimentProvider(widget.sessionId));
            if (own.isLoading && !own.hasValue) return const Center(child: CircularProgressIndicator());
            if (own.value != null) {
              final lang = Localizations.localeOf(context).languageCode;
              return ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  if (envelopeAsync.value?.mode == SessionMode.simulation) _SimulatedRunLabel(lang: lang),
                  EmbodimentReport(embodiment: own.value!, lang: lang),
                ],
              );
            }
            // A hub session (real headset upload) has no metrics.json until
            // the clinician runs analysis. Rather than an empty screen, show
            // whatever `events.ndjson` already lets us derive (reaction
            // time / movement time per trial -- see
            // `shared/metrics/events_derived_metrics.dart`) under a clear
            // "Full biomarkers after analysis" banner, with "Run analysis"
            // (desktop) or an explanation (web) alongside it -- per
            // `docs/agent-briefs/A-next-run.md` item 2 and A2 run13 item 3.
            final openedMatch = ref
                .watch(openedSessionDirectoriesProvider)
                .where((r) => r.envelope.sessionId == widget.sessionId)
                .firstOrNull;
            final sessionDir = hubCapable
                ? ref.read(hubControllerProvider.notifier).sessionDirFor(widget.sessionId) ?? openedMatch?.sessionDirPath
                : null;
            final eventsAsync = ref.watch(_eventsProvider(widget.sessionId));
            return eventsAsync.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => ErrorRetryView(
                message: e.toString(),
                onRetry: () => ref.invalidate(_eventsProvider(widget.sessionId)),
              ),
              data: (events) {
                final derivedTrials = buildEventDerivedTrials(events);
                final hapticSummary = summarizeHapticCues(events);
                final outcomeCounts = OutcomeCounts.fromOutcomes(derivedTrials.map((t) => t.outcome));
                return ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    // Design v2 §3: ONE short line, no helper paragraph.
                    const _AnalysisPendingLine(),
                    const SizedBox(height: 16),
                    Section(title: 'Outcomes', child: OutcomeDonut(counts: outcomeCounts)),
                    const SizedBox(height: 16),
                    Section(title: 'Haptic cues', child: CuePie(summary: hapticSummary)),
                    const SizedBox(height: 16),
                    Section(
                      title: l10n.sessionReportTrialTable,
                      child: derivedTrials.isEmpty
                          ? const Text('No trials yet.', style: TextStyle(color: V2Colors.textDim))
                          : _TrialTable(
                              trials: derivedTrials,
                              selectedIndex: 0,
                              onSelect: (_) {},
                            ),
                    ),
                    if (sessionDir != null) ...[
                      const SizedBox(height: 16),
                      // The screen's one primary action in this state.
                      _RunAnalysisPrompt(
                        sessionDir: sessionDir,
                        onDone: () {
                          ref.invalidate(_metricsProvider(widget.sessionId));
                          ref.invalidate(_reachTracesProvider(widget.sessionId));
                        },
                      ),
                    ],
                  ],
                );
              },
            );
          }
          // A Phantom Hand session: its result is the embodiment report. Its
          // metrics.json has no trials, so the trial layout below would be empty.
          final embodimentAsync = ref.watch(_embodimentProvider(widget.sessionId));
          // A session without trials waits for the report rather than flash the empty trial layout.
          if (metrics.trials.isEmpty && embodimentAsync.isLoading && !embodimentAsync.hasValue) {
            return const Center(child: CircularProgressIndicator());
          }
          final embodiment = embodimentAsync.value;
          if (embodiment != null) {
            final lang = Localizations.localeOf(context).languageCode;
            return ListView(
              padding: const EdgeInsets.all(16),
              children: [
                if (envelopeAsync.value?.mode == SessionMode.simulation) _SimulatedRunLabel(lang: lang),
                EmbodimentReport(embodiment: embodiment, lang: lang),
              ],
            );
          }
          final trials = metrics.trials;
          if (_selectedTrialIndex >= trials.length) _selectedTrialIndex = 0;

          final eventsAsync = ref.watch(_eventsProvider(widget.sessionId));
          final hapticSummary = eventsAsync.maybeWhen(
            data: summarizeHapticCues,
            orElse: () => HapticCueSummary.empty,
          );
          final outcomeCounts = OutcomeCounts.fromOutcomes(trials.map((t) => t.outcome));
          final rtValues = [
            for (var i = 0; i < trials.length; i++)
              if (trials[i].metrics['reaction_time_ms']?.value != null)
                PerTrialBarDatum(trial: i, value: trials[i].metrics['reaction_time_ms']!.value!, outcome: trials[i].outcome),
          ];
          final rtOnly = rtValues.map((d) => d.value).toList();
          final peakSpeeds = [
            for (final t in trials)
              if (t.metrics['peak_speed_mps']?.value != null) t.metrics['peak_speed_mps']!.value!,
          ];

          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              // §5 "Session report" tiles: Trials / Success % / Mean RT / Mean peak speed.
              GridView.count(
                crossAxisCount: 2,
                shrinkWrap: true,
                physics: const NeverScrollableScrollPhysics(),
                crossAxisSpacing: 8,
                mainAxisSpacing: 8,
                childAspectRatio: 1.6,
                children: [
                  StatTile(label: 'Trials', value: '${trials.length}'),
                  StatTile(label: 'Success %', value: '${outcomeCounts.successPct}', unit: '%'),
                  StatTile(
                    label: 'Mean RT',
                    value: rtOnly.isEmpty ? '-' : (rtOnly.reduce((a, b) => a + b) / rtOnly.length).toStringAsFixed(0),
                    unit: rtOnly.isEmpty ? null : 'ms',
                  ),
                  StatTile(
                    label: 'Mean peak speed',
                    value: peakSpeeds.isEmpty
                        ? '-'
                        : (peakSpeeds.reduce((a, b) => a + b) / peakSpeeds.length).toStringAsFixed(2),
                    unit: peakSpeeds.isEmpty ? null : 'm/s',
                  ),
                ],
              ),
              const SizedBox(height: 16),
              Section(title: 'Outcomes', child: OutcomeDonut(counts: outcomeCounts)),
              const SizedBox(height: 16),
              Section(title: 'Reaction time per trial', child: PerTrialBars(data: rtValues)),
              const SizedBox(height: 16),
              Section(title: 'Haptic cues', child: CuePie(summary: hapticSummary)),
              const SizedBox(height: 16),
              // v3 amendment (BINDING): "Session report: Trial dropdown".
              // Folds the old separate "Speed profile" section into this one
              // (keeps the screen at 4 sections total) -- picking a trial,
              // either from the dropdown or by tapping its table row, shows
              // that trial's speed profile right below the table.
              Section(
                title: l10n.sessionReportTrialTable,
                action: trials.isEmpty
                    ? null
                    : DropdownButton<int>(
                        value: _selectedTrialIndex,
                        underline: const SizedBox.shrink(),
                        dropdownColor: V2Colors.panelRaised,
                        items: [
                          for (var i = 0; i < trials.length; i++)
                            DropdownMenuItem(value: i, child: Text('Trial ${trials[i].trial}')),
                        ],
                        onChanged: (i) {
                          if (i != null) setState(() => _selectedTrialIndex = i);
                        },
                      ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    _TrialTable(
                      trials: trials,
                      selectedIndex: _selectedTrialIndex,
                      onSelect: (i) => setState(() => _selectedTrialIndex = i),
                    ),
                    if (trials.isNotEmpty) ...[
                      const SizedBox(height: 12),
                      _SpeedProfileForTrial(trial: trials[_selectedTrialIndex]),
                    ],
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }

  String _formatDate(DateTime d) {
    final local = d.toLocal();
    return '${local.year}-${local.month.toString().padLeft(2, '0')}-${local.day.toString().padLeft(2, '0')}';
  }
}

/// The plain "Simulated run" label of a Phantom Hand session recorded against
/// the simulator (the demo participant's): simulated data is called simulated
/// wherever it is shown.
class _SimulatedRunLabel extends StatelessWidget {
  const new({required this.lang});
  final String lang;

  @override
  Widget build(BuildContext context) {
    final s = PhantomDemoStrings.forLang(lang);
    final textTheme = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            key: const ValueKey('ph-simulated-run'),
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            decoration: BoxDecoration(
              border: Border.all(color: V2Colors.textDim, width: 1.5),
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.science_outlined, size: 18, color: V2Colors.text),
                const SizedBox(width: 8),
                Flexible(child: Text(s.t('simulatedRun'), style: textTheme.labelLarge?.copyWith(color: V2Colors.text))),
              ],
            ),
          ),
          const SizedBox(height: 6),
          Text(s.t('simulatedNote'), style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim)),
        ],
      ),
    );
  }
}

/// Design v2 §3: one short line naming the state, no icon, no paragraph.
/// A real hub session has no `metrics.json` until analysis runs, so the
/// screen shows what `events.ndjson` already supports (reaction/movement
/// time per trial) and says so in one line rather than implying these are
/// the full biomarkers.
class _AnalysisPendingLine extends StatelessWidget {
  const _AnalysisPendingLine();

  @override
  Widget build(BuildContext context) {
    return Text(
      'Full biomarkers after analysis.',
      style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: V2Colors.textDim),
    );
  }
}

class _RunAnalysisPrompt extends ConsumerStatefulWidget {
  const new({required this.sessionDir, required this.onDone});
  final String sessionDir;
  final VoidCallback onDone;

  @override
  ConsumerState<_RunAnalysisPrompt> createState() => _RunAnalysisPromptState();
}

class _RunAnalysisPromptState extends ConsumerState<_RunAnalysisPrompt> {
  bool _running = false;
  String? _error;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        FilledButton(
          style: FilledButton.styleFrom(
            backgroundColor: V2Colors.oxblood,
            foregroundColor: V2Colors.text,
            minimumSize: const Size.fromHeight(48),
          ),
          onPressed: _running ? null : _run,
          child: Text(_running ? 'Running…' : 'Run analysis'),
        ),
        if (_error != null) ...[
          const SizedBox(height: 8),
          Text(_error!, style: const TextStyle(color: V2Colors.crimson)),
        ],
      ],
    );
  }

  Future<void> _run() async {
    setState(() {
      _running = true;
      _error = null;
    });
    final result = await ref.read(hubControllerProvider.notifier).runAnalysis(widget.sessionDir);
    if (!mounted) return;
    setState(() => _running = false);
    if (result.exitCode == 0) {
      widget.onDone();
    } else {
      // §3: one line, what happened and the fix.
      setState(() => _error = 'Analysis failed. Check the session folder and try again.');
    }
  }
}

/// Design v2 §5 "Session report > Trials": the table is exactly
/// `#, outcome, RT, MT, peak, SPARC, error cm`. A3 run1 removed the `Hand`,
/// `Quality` and `Haptic cues` columns -- `Hand` repeats the program's own
/// setting for every row, `Quality` was a badge on every row (§1 forbids
/// that), and the haptic story is the "Haptic cues" section above -- and
/// added the `error cm` column §5 asks for, which was missing entirely.
class _TrialTable extends StatelessWidget {
  const new({
    required this.trials,
    required this.selectedIndex,
    required this.onSelect,
  });
  final List<TrialMetrics> trials;
  final int selectedIndex;
  final ValueChanged<int> onSelect;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    final headStyle = textTheme.labelSmall?.copyWith(color: V2Colors.textDim);
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: DataTable(
        headingRowHeight: 36,
        dataRowMinHeight: 36,
        dataRowMaxHeight: 48,
        columnSpacing: 20,
        headingRowColor: WidgetStateProperty.all(V2Colors.panelRaised),
        dividerThickness: 1,
        columns: [
          DataColumn(label: Text('#', style: headStyle)),
          DataColumn(label: Text('Outcome', style: headStyle)),
          DataColumn(label: Text('RT', style: headStyle)),
          DataColumn(label: Text('MT', style: headStyle)),
          DataColumn(label: Text('Peak', style: headStyle)),
          // v3 amendment (BINDING): "SPARC"/"endpoint error" never shown by
          // those names -- "Smoothness" / "Accuracy (cm)".
          DataColumn(label: Text('Smoothness', style: headStyle)),
          DataColumn(label: Text('Accuracy cm', style: headStyle)),
        ],
        rows: [
          for (var i = 0; i < trials.length; i++) _rowFor(context, i, trials[i]),
        ],
      ),
    );
  }

  DataRow _rowFor(BuildContext context, int i, TrialMetrics t) {
    final textTheme = Theme.of(context).textTheme;
    final cellStyle = textTheme.bodySmall?.copyWith(color: V2Colors.text);
    Widget cell(String text, {Color? color}) =>
        Text(text, style: cellStyle?.copyWith(color: color ?? V2Colors.text));
    return DataRow(
      selected: i == selectedIndex,
      color: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected) ? V2Colors.panelRaised : null,
      ),
      onSelectChanged: (_) => onSelect(i),
      cells: [
        DataCell(cell('${t.trial}')),
        // Outcome carries its fixed §1 colour, and the word itself -- colour
        // is never the only carrier of meaning.
        DataCell(cell(
          t.outcome == null ? '-' : V2Colors.outcomeLabelV3(t.outcome!),
          color: t.outcome == null ? V2Colors.textDim : V2Colors.forOutcomeV3(t.outcome!),
        )),
        DataCell(cell(_fmt('reaction_time_ms', t.metrics['reaction_time_ms']))),
        DataCell(cell(_fmt('movement_time_ms', t.metrics['movement_time_ms']))),
        DataCell(cell(_fmt('peak_speed_mps', t.metrics['peak_speed_mps']))),
        DataCell(cell(_fmt('sparc', t.metrics['sparc']))),
        DataCell(cell(_fmt('endpoint_error_cm', t.metrics['endpoint_error_cm']))),
      ],
    );
  }

  /// Bare number (no unit suffix): the column header already names the
  /// metric and its unit, so repeating "ms" on every one of 20 rows is the
  /// per-row clutter §3 asks us to remove.
  String _fmt(String metricKey, dynamic metricValue) {
    if (metricValue == null || metricValue.value == null) return '-';
    final v = metricValue.value as double;
    return switch (metricKey) {
      'reaction_time_ms' || 'movement_time_ms' => v.toStringAsFixed(0),
      _ => v.toStringAsFixed(2),
    };
  }
}

class _SpeedProfileForTrial extends StatelessWidget {
  const new({required this.trial});
  final TrialMetrics trial;

  @override
  Widget build(BuildContext context) {
    final mt = trial.metrics['movement_time_ms']?.value;
    final peak = trial.metrics['peak_speed_mps']?.value;
    final tPeakPct = trial.metrics['time_to_peak_speed_pct']?.value;
    if (mt == null || peak == null || tPeakPct == null) {
      return const Text('Not enough data for a speed profile on this trial.');
    }
    return SpeedProfileChart(movementTimeMs: mt, peakSpeedMps: peak, timeToPeakPct: tPeakPct);
  }
}

final FutureProviderFamily<SessionMetrics?, String> _metricsProvider = FutureProvider.family<SessionMetrics?, String>(
  (ref, id) => ref.watch(sessionsRepositoryProvider).getSessionMetrics(id),
);

/// The `embodiment` block of a session's `metrics.json`, read from the raw file
/// because [SessionMetrics] drops it. Null for any other session: no folder on
/// disk (the bundled fixtures), no file, bad JSON, or no `embodiment` in it.
final FutureProviderFamily<Embodiment?, String> _embodimentProvider = FutureProvider.family<Embodiment?, String>((ref, id) async {
  // The demo participant's recorded session is bundled: no folder, hub or platform needed.
  if (id == demoPhantomSessionId) return const PhantomDemoAssets().embodiment();
  if (!hubCapable) return null;
  final opened = ref.watch(openedSessionDirectoriesProvider).where((r) => r.envelope.sessionId == id).firstOrNull;
  final dir = ref.read(hubControllerProvider.notifier).sessionDirFor(id) ?? opened?.sessionDirPath;
  if (dir == null) return null;
  // First the headset's own summary of the run (the last witness_summary in events.ndjson): the numbers the wearer saw on
  // the results panel at the end, and the report must say the same. A PC's metrics.json marks most of them "partial" when
  // an induction was cut short with "next phase", and the report then had no verdict.
  try {
    Embodiment? own;
    for (final line in await File('$dir/events.ndjson').readAsLines()) {
      if (!line.contains('"witness_summary"')) continue;
      final e = jsonDecode(line);
      if (e is Map && e['type'] == 'witness_summary') own = Embodiment.tryParseWitness(e['data']) ?? own;
    }
    if (own != null) return own;
  } on Exception catch (_) {
    // no events yet
  }
  try {
    return Embodiment.tryParseMetrics(jsonDecode(await File('$dir/metrics.json').readAsString()));
  } on Exception catch (_) {
    return null;
  }
});

final FutureProviderFamily<ReachTraceSet, String> _reachTracesProvider = FutureProvider.family<ReachTraceSet, String>(
  (ref, id) => ref.watch(sessionsRepositoryProvider).getReachTraces(id),
);

final FutureProviderFamily<List<SessionEvent>, String> _eventsProvider = FutureProvider.family<List<SessionEvent>, String>(
  (ref, id) => ref.watch(sessionsRepositoryProvider).getSessionEvents(id),
);

final FutureProviderFamily<SessionEnvelope?, String> _envelopeProvider = FutureProvider.family(
  (ref, id) => ref.watch(sessionsRepositoryProvider).getSessionEnvelope(id),
);
