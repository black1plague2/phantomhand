import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/patient.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/l10n/app_localizations.dart';
import 'package:opus_app/shared/design/v2_colors.dart';
import 'package:opus_app/shared/metrics/dose_adherence.dart';
import 'package:opus_app/shared/metrics/progress_data.dart';
import 'package:opus_app/shared/widgets/charts/outcome_donut.dart';
import 'package:opus_app/shared/widgets/charts/trend_line_chart.dart';
import 'package:opus_app/shared/widgets/charts/weekly_dose_bars.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';
import 'package:opus_app/shared/widgets/section.dart';
import 'package:riverpod/src/providers/future_provider.dart';

/// Design v2 §5 "Patient": the screen is the patient's name plus **exactly
/// six sections** -- tiles, Reaction time, Smoothness, Outcomes, Weekly dose,
/// Sessions -- and one primary action ("New program").
///
/// A3 run1 deleted the five-tab `TabBar`
/// (Overview/Programs/Sessions/Progress/Outcomes) this screen used to wrap
/// around that content. The tabs were navigation chrome §5 does not have,
/// they hid four of the six sections behind a swipe, and on the 360 dp pilot
/// phone the scrollable tab strip ate a whole row of vertical space. Programs
/// and outcome entry are still reachable at their own routes
/// (`/patients/:id/programs/new`, `/patients/:id/outcomes/new`); the session
/// list is now section 6 of this screen, which is where §5 puts it.
class PatientProfileScreen extends ConsumerWidget {
  const new({required this.patientId, super.key});
  final String patientId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final patientAsync = ref.watch(_patientProvider(patientId));

    return Scaffold(
      backgroundColor: V2Colors.black,
      appBar: AppBar(
        backgroundColor: V2Colors.black,
        foregroundColor: V2Colors.text,
        title: patientAsync.when(
          data: (p) => Text(p?.displayName ?? patientId),
          loading: () => Text(l10n.patientProfileTitle),
          error: (_, _) => Text(l10n.patientProfileTitle),
        ),
      ),
      body: patientAsync.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetryView(
          message: e.toString(),
          onRetry: () => ref.invalidate(_patientProvider(patientId)),
        ),
        data: (patient) {
          if (patient == null) return const Center(child: Text('Patient not found.'));
          final body = _OverviewTab(patient: patient);
          // docs/APP_DESIGN.md workstation layout (>= 900 dp): "the right
          // 'Live now' pane appears only when a headset is connected [and
          // running a session for this patient]. Otherwise the centre
          // column takes the width." -- checked live via HubController
          // rather than a one-time value, since a headset can connect/
          // disconnect while this screen is open.
          return LayoutBuilder(
            builder: (context, constraints) {
              if (constraints.maxWidth < 900 || !hubCapable) return body;
              return Consumer(
                builder: (context, ref, _) {
                  ref.watch(hubControllerProvider); // rebuild on connect/disconnect
                  final conn = ref
                      .read(hubControllerProvider.notifier)
                      .runningConnectionForPatient(patientId);
                  if (conn == null) return body;
                  return Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Expanded(child: body),
                      const VerticalDivider(width: 1),
                      SizedBox(width: 280, child: _LiveNowPane(patientId: patientId, connection: conn)),
                    ],
                  );
                },
              );
            },
          );
        },
      ),
    );
  }
}

/// The workstation layout's right-hand "Live now" pane (`docs/APP_DESIGN.md`):
/// a compact summary of the headset currently running a session for this
/// patient, with a way to open the full live monitor. Deliberately does not
/// duplicate `LiveMonitorScreen`'s live-drawing workspace view here -- the
/// pane is a glance/handoff surface ("is a session running, how's it going"),
/// the full live monitor (opened via the button) is where a clinician
/// actually supervises trial-by-trial.
class _LiveNowPane extends StatelessWidget {
  const new({required this.patientId, required this.connection});
  final String patientId;
  final HubConnection connection;

  @override
  Widget build(BuildContext context) {
    final status = connection.lastStatus;
    final state = status?['state'] as String?;
    final sessionId = connection.activeSessionId!;
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Live now', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 12),
          Text('${connection.deviceId} · paired'),
          const SizedBox(height: 4),
          Text('State: ${state ?? 'connecting…'}'),
          if (connection.lastRtt != null) Text('${connection.lastRtt!.inMilliseconds} ms delay'),
          const Spacer(),
          FilledButton(
            style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(48)),
            onPressed: () => context.go('/patients/$patientId/live/$sessionId'),
            child: const Text('Open live monitor'),
          ),
        ],
      ),
    );
  }
}

final FutureProviderFamily<Patient?, String> _patientProvider = FutureProvider.family<Patient?, String>(
  (ref, id) => ref.watch(patientsRepositoryProvider).getPatient(id),
);

/// The patient overview: `docs/APP_DESIGN.md`'s workstation wireframe center
/// column, minus the right-hand "Live now" pane (that's composed in above,
/// around `tabs`) and the rail (the app shell). Per the wireframe: the
/// patient's name as the heading, a one-line clinical summary, the recovery
/// line (longitudinal trend + shaded MDC band + honest change wording), the
/// time-ordered session glyph strip, then outcome measures. One scrolling
/// column at every width -- the wireframe's "workstation vs phone" distinction
/// is about how much horizontal room this column gets (governed by the
/// rail/Live-now pane logic in the parent `build` above), not a different
/// layout for this tab itself.
/// Design v2 §5 "Patient": title = patient name (rendered in the app bar, not
/// repeated here); tiles (Sessions / Success % / Dose this week), then
/// sections for Reaction time, Smoothness, Outcomes (donut), Weekly dose,
/// and Sessions -- ending in the screen's one primary action, "New program".
class _OverviewTab extends ConsumerWidget {
  const _OverviewTab({required this.patient});
  final Patient patient;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final overviewAsync = ref.watch(_overviewProvider(patient.id));
    return overviewAsync.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) => ErrorRetryView(message: e.toString(), onRetry: () => ref.invalidate(_overviewProvider(patient.id))),
      data: (data) => ListView(
        padding: const EdgeInsets.all(16),
        children: [
          GridView.count(
            crossAxisCount: 3,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            crossAxisSpacing: 8,
            mainAxisSpacing: 8,
            childAspectRatio: 1.1,
            children: [
              StatTile(label: 'Sessions', value: '${data.sessions.length}'),
              StatTile(label: 'Success %', value: '${data.outcomeCounts.successPct}', unit: '%'),
              StatTile(
                label: 'Dose this week',
                value: data.dose == null ? '-' : '${data.dose!.completedDays}',
                unit: data.dose == null ? null : '/ ${data.dose!.prescribedSessionsPerWeek}',
              ),
            ],
          ),
          const SizedBox(height: 16),
          // v3 amendment (BINDING): "a Metric dropdown switches one big trend
          // chart between Reaction time / Movement time / Peak speed /
          // Smoothness / Trunk lean, instead of 5 stacked charts" -- was two
          // separate always-visible sections (Reaction time, Smoothness);
          // now one section, keeping the screen at 4 sections total (this +
          // Outcomes + Weekly dose + Sessions).
          _MetricTrendSection(patientId: patient.id),
          const SizedBox(height: 16),
          Section(title: 'Outcomes', child: OutcomeDonut(counts: data.outcomeCounts)),
          const SizedBox(height: 16),
          Section(
            title: 'Weekly dose',
            child: WeeklyDoseBars(weeks: data.weeklyDose, target: data.dose?.prescribedSessionsPerWeek ?? 0),
          ),
          const SizedBox(height: 16),
          Section(
            title: 'Sessions',
            child: data.sessions.isEmpty
                ? const Text('No sessions yet.', style: TextStyle(color: V2Colors.textDim))
                : Column(
                    children: [
                      for (final s in data.sessions.take(8))
                        _SessionListRow(patientId: patient.id, session: s),
                    ],
                  ),
          ),
          const SizedBox(height: 16),
          SizedBox(
            width: double.infinity,
            child: FilledButton(
              style: FilledButton.styleFrom(
                backgroundColor: V2Colors.oxblood,
                foregroundColor: V2Colors.text,
                minimumSize: const Size.fromHeight(48),
              ),
              onPressed: () => context.go('/patients/${patient.id}/programs/new'),
              child: const Text('New program'),
            ),
          ),
        ],
      ),
    );
  }
}

/// One row in the "Sessions" section (§5: "list (date, success %, trials) ->
/// session report").
class _SessionListRow extends ConsumerWidget {
  const _SessionListRow({required this.patientId, required this.session});
  final String patientId;
  final SessionEnvelope session;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final metricsAsync = ref.watch(_sessionMetricsProvider(session.sessionId));
    final successPct = metricsAsync.maybeWhen(
      data: (m) {
        if (m == null) return null;
        final withOutcome = m.trials.where((t) => t.outcome != null).toList();
        if (withOutcome.isEmpty) return null;
        return (100 * withOutcome.where((t) => t.outcome == 'success').length / withOutcome.length).round();
      },
      orElse: () => null,
    );
    final local = session.startedAt.toLocal();
    final dateText = '${local.year}-${local.month.toString().padLeft(2, '0')}-${local.day.toString().padLeft(2, '0')}';
    return InkWell(
      onTap: () => context.go('/patients/$patientId/sessions/${session.sessionId}'),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 10),
        child: Row(
          children: [
            Expanded(
              child: Text(
                dateText,
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: V2Colors.text),
              ),
            ),
            Text(
              successPct == null ? 'Pending' : '$successPct%',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
            ),
          ],
        ),
      ),
    );
  }
}

/// v3 amendment (BINDING): one "Trend" section with a `Metric` dropdown
/// (Reaction time / Movement time / Peak speed / Smoothness / Trunk lean)
/// instead of a stacked chart per metric. Local UI state (which metric is
/// selected) lives here so switching it doesn't refetch [progressDataProvider].
class _MetricTrendSection extends StatefulWidget {
  const _MetricTrendSection({required this.patientId});
  final String patientId;

  @override
  State<_MetricTrendSection> createState() => _MetricTrendSectionState();
}

/// Wire metric keys (`progress_data.dart`'s point values) offered in the
/// dropdown, in the order the v3 amendment lists them.
const _trendMetricIds = ['reaction_time_ms', 'movement_time_ms', 'peak_speed_mps', 'sparc', 'trunk_lean_cm'];

/// Maps a wire metric key to the [OpusTokens.metricColorV3]/`metricLabelV3`
/// id vocabulary (camelCase, no unit suffix) used by the fixed per-metric
/// colour/label tables.
String _v3MetricId(String wireKey) => switch (wireKey) {
      'reaction_time_ms' => 'reactionTime',
      'movement_time_ms' => 'movementTime',
      'peak_speed_mps' => 'peakSpeed',
      'sparc' => 'smoothness',
      'trunk_lean_cm' => 'trunkLean',
      _ => wireKey,
    };

class _MetricTrendSectionState extends State<_MetricTrendSection> {
  String _metricId = 'reaction_time_ms';

  @override
  Widget build(BuildContext context) {
    return Section(
      title: 'Trend',
      action: DropdownButton<String>(
        value: _metricId,
        underline: const SizedBox.shrink(),
        dropdownColor: V2Colors.panelRaised,
        items: [
          for (final id in _trendMetricIds)
            DropdownMenuItem(value: id, child: Text(V2Colors.metricLabelV3(_v3MetricId(id)))),
        ],
        onChanged: (id) {
          if (id != null) setState(() => _metricId = id);
        },
      ),
      child: _MetricTrend(patientId: widget.patientId, metricId: _metricId),
    );
  }
}

/// A single-metric trend line, coloured by the metric's fixed v3 colour
/// (`OpusTokens.metricColorV3`, "so the doctor learns them"). Reads the same
/// [progressDataProvider] the full Progress tab uses, honest "no
/// sessions"/"not enough sessions" text below 2 points.
class _MetricTrend extends ConsumerWidget {
  const _MetricTrend({required this.patientId, required this.metricId});
  final String patientId;
  final String metricId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final dataAsync = ref.watch(progressDataProvider(patientId));
    return dataAsync.when(
      loading: () => const SizedBox(height: 32, child: LinearProgressIndicator()),
      error: (_, _) => const SizedBox.shrink(),
      data: (points) {
        final values = [for (final p in points) p.values[metricId]].whereType<double>().toList();
        if (values.length < 2) {
          return Text(
            values.isEmpty ? 'No sessions recorded yet.' : 'Not enough sessions for a trend yet.',
            style: const TextStyle(color: V2Colors.textDim),
          );
        }
        final mdc = mdcFor(metricId, points.first);
        return TrendLineChart(
          points: [for (var i = 0; i < values.length; i++) (i.toDouble(), values[i])],
          mdcBand: mdc,
          xAxisLabel: 'Session',
          color: V2Colors.metricColorV3(_v3MetricId(metricId)),
        );
      },
    );
  }
}

/// Aggregate overview data for [_OverviewTab]: every session, the aggregate
/// outcome counts across all of them (for the Outcomes donut), the per-week
/// session counts (for the Weekly dose bars) and the current dose-adherence
/// snapshot.
class _PatientOverviewData {
  const _PatientOverviewData({
    required this.sessions,
    required this.outcomeCounts,
    required this.weeklyDose,
    required this.dose,
  });
  final List<SessionEnvelope> sessions;
  final OutcomeCounts outcomeCounts;
  final List<WeeklyDoseDatum> weeklyDose;
  final DoseAdherenceData? dose;
}

final FutureProviderFamily<_PatientOverviewData, String> _overviewProvider =
    FutureProvider.family<_PatientOverviewData, String>((ref, patientId) async {
  final sessionsRepo = ref.watch(sessionsRepositoryProvider);
  final sessions = await sessionsRepo.listSessionsForPatient(patientId);
  final sortedNewestFirst = [...sessions]..sort((a, b) => b.startedAt.compareTo(a.startedAt));

  final outcomes = <String?>[];
  for (final s in sessions) {
    final metrics = await sessionsRepo.getSessionMetrics(s.sessionId);
    if (metrics != null) outcomes.addAll(metrics.trials.map((t) => t.outcome));
  }

  final byWeek = <DateTime, int>{};
  for (final s in sessions) {
    final d = DateTime(s.startedAt.year, s.startedAt.month, s.startedAt.day);
    final weekStart = d.subtract(Duration(days: d.weekday - 1));
    byWeek[weekStart] = (byWeek[weekStart] ?? 0) + 1;
  }
  final sortedWeeks = byWeek.keys.toList()..sort();
  final recentWeeks = sortedWeeks.length > 8 ? sortedWeeks.sublist(sortedWeeks.length - 8) : sortedWeeks;
  final weeklyDose = [
    for (final w in recentWeeks) WeeklyDoseDatum(label: '${w.month}/${w.day}', sessions: byWeek[w]!),
  ];

  DoseAdherenceData? dose;
  try {
    dose = await ref.watch(doseAdherenceProvider(patientId).future);
  } catch (_) {
    dose = null;
  }

  return _PatientOverviewData(
    sessions: sortedNewestFirst,
    outcomeCounts: OutcomeCounts.fromOutcomes(outcomes),
    weeklyDose: weeklyDose,
    dose: dose,
  );
});

final FutureProviderFamily<SessionMetrics?, String> _sessionMetricsProvider =
    FutureProvider.family<SessionMetrics?, String>(
  (ref, id) => ref.watch(sessionsRepositoryProvider).getSessionMetrics(id),
);

