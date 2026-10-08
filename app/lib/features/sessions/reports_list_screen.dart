import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/patient.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';
import 'package:opus_app/shared/design/v2_colors.dart';

/// The Reports tab (Opus decision, `docs/APP_DESIGN.md` bottom nav = Patients
/// / Monitor / Programs / Reports): every session across every patient,
/// newest first -- real hub-uploaded sessions, sessions a clinician opened
/// via "Open a session folder..." and the bundled mock/historical fixtures,
/// all through the one [CompositeSessionsRepository] every other screen
/// already uses (`sessionsRepositoryProvider`), so a session appears here the
/// moment it appears anywhere else.
///
/// Unlike a per-patient timeline, there's no single `patientId` to scope the
/// list by, so this builds the flat list itself: list every patient, then
/// list that patient's sessions, then merge+sort -- the same approach
/// `progress_data.dart`/`dose_adherence.dart` already take for cross-patient
/// aggregation.
/// v3 amendment periods for the `Period` filter dropdown -- `null` = "All time".
const _reportPeriods = <String, int?>{
  'All time': null,
  'Last 7 days': 7,
  'Last 30 days': 30,
  'Last 90 days': 90,
};

class ReportsListScreen extends ConsumerStatefulWidget {
  const ReportsListScreen({super.key});

  @override
  ConsumerState<ReportsListScreen> createState() => _ReportsListScreenState();
}

class _ReportsListScreenState extends ConsumerState<ReportsListScreen> {
  /// `null` = every patient.
  String? _patientFilter;
  String _periodLabel = 'All time';

  @override
  Widget build(BuildContext context) {
    final entriesAsync = ref.watch(_allSessionEntriesProvider);
    return Scaffold(
      backgroundColor: V2Colors.black,
      appBar: AppBar(
        backgroundColor: V2Colors.black,
        foregroundColor: V2Colors.text,
        title: const Text('Reports'),
      ),
      body: entriesAsync.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetryView(
          message: e.toString(),
          onRetry: () => ref.invalidate(_allSessionEntriesProvider),
        ),
        data: (entries) {
          if (entries.isEmpty) {
            // Design v2 §3: an empty state is ONE short line saying what to do.
            return const Center(
              child: Text('No sessions yet.', style: TextStyle(color: V2Colors.textDim)),
            );
          }
          // v3 amendment (BINDING): "Reports: `Patient` + `Period` dropdowns
          // as filters" -- applied client-side over the already-fetched list.
          final patientNames = {for (final e in entries) e.patientName}.toList()..sort();
          final days = _reportPeriods[_periodLabel];
          final cutoff = days == null ? null : DateTime.now().subtract(Duration(days: days));
          final filtered = entries.where((e) {
            if (_patientFilter != null && e.patientName != _patientFilter) return false;
            if (cutoff != null && e.envelope.startedAt.isBefore(cutoff)) return false;
            return true;
          }).toList();
          return Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: Row(
                  children: [
                    Expanded(
                      child: DropdownButton<String?>(
                        isExpanded: true,
                        value: _patientFilter,
                        underline: const SizedBox.shrink(),
                        dropdownColor: V2Colors.panelRaised,
                        items: [
                          const DropdownMenuItem(value: null, child: Text('All patients')),
                          for (final name in patientNames) DropdownMenuItem(value: name, child: Text(name)),
                        ],
                        onChanged: (v) => setState(() => _patientFilter = v),
                      ),
                    ),
                    const SizedBox(width: 12),
                    DropdownButton<String>(
                      value: _periodLabel,
                      underline: const SizedBox.shrink(),
                      dropdownColor: V2Colors.panelRaised,
                      items: [
                        for (final label in _reportPeriods.keys) DropdownMenuItem(value: label, child: Text(label)),
                      ],
                      onChanged: (v) {
                        if (v != null) setState(() => _periodLabel = v);
                      },
                    ),
                  ],
                ),
              ),
              Expanded(
                child: filtered.isEmpty
                    ? const Center(
                        child: Text('No sessions match these filters.', style: TextStyle(color: V2Colors.textDim)),
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.all(16),
                        itemCount: filtered.length,
                        separatorBuilder: (context, _) => const Divider(height: 1, color: V2Colors.line),
                        itemBuilder: (context, i) => _SessionRow(entry: filtered[i]),
                      ),
              ),
            ],
          );
        },
      ),
    );
  }
}

/// Design v2 §5 "Reports": "list of sessions (patient, date, success %),
/// newest first." Nothing more. A3 run1 removed the reach-trace glyph
/// leading, the Live/New `Chip` badges (§1 forbids "pill badges on every
/// row") and the trailing chevron.
class _SessionRow extends StatelessWidget {
  const _SessionRow({required this.entry});
  final _SessionListEntry entry;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return InkWell(
      onTap: () => context.go('/patients/${entry.envelope.patientRef}/sessions/${entry.envelope.sessionId}'),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 12),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    entry.patientName,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelLarge?.copyWith(color: V2Colors.text),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    _formatDate(entry.envelope.startedAt),
                    style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
                  ),
                ],
              ),
            ),
            Text(
              entry.successPct == null ? '-' : '${entry.successPct!.round()}%',
              style: textTheme.bodyMedium?.copyWith(color: V2Colors.text),
            ),
          ],
        ),
      ),
    );
  }

  String _formatDate(DateTime d) {
    final local = d.toLocal();
    return '${local.year}-${local.month.toString().padLeft(2, '0')}-${local.day.toString().padLeft(2, '0')}';
  }
}

/// One row's worth of joined data -- the session envelope plus what only the
/// list screen (not the repository) needs to know: the patient's display
/// name (sessions only carry `patientRef`, an id) and whether it's live/new.
class _SessionListEntry {
  const _SessionListEntry({
    required this.envelope,
    required this.patientName,
    required this.successPct,
  });
  final SessionEnvelope envelope;
  final String patientName;
  final double? successPct;
}

/// Every session across every patient, newest first. Re-fetches patients +
/// their sessions rather than caching, mirroring the freshness behaviour
/// `sessionsRepositoryProvider` itself already has (it rebuilds on hub
/// connect/disconnect and on `openedSessionDirectoriesProvider` changes) --
/// watching those providers here (transitively, through `listPatients`/
/// `listSessionsForPatient`) keeps this list live too.
final _allSessionEntriesProvider = FutureProvider.autoDispose<List<_SessionListEntry>>((ref) async {
  final patientsRepo = ref.watch(patientsRepositoryProvider);
  final sessionsRepo = ref.watch(sessionsRepositoryProvider);
  final patients = await patientsRepo.listPatients();
  final byId = <String, Patient>{for (final p in patients) p.id: p};

  final entries = <_SessionListEntry>[];
  for (final patient in patients) {
    final sessions = await sessionsRepo.listSessionsForPatient(patient.id);
    for (final s in sessions) {
      final metrics = await sessionsRepo.getSessionMetrics(s.sessionId);
      double? successPct;
      if (metrics != null && metrics.trials.isNotEmpty) {
        final withOutcome = metrics.trials.where((t) => t.outcome != null).toList();
        if (withOutcome.isNotEmpty) {
          final successes = withOutcome.where((t) => t.outcome == 'success').length;
          successPct = 100 * successes / withOutcome.length;
        }
      }
      entries.add(
        _SessionListEntry(
          envelope: s,
          patientName: byId[s.patientRef]?.displayName ?? s.patientRef,
          successPct: successPct,
        ),
      );
    }
  }
  entries.sort((a, b) => b.envelope.startedAt.compareTo(a.envelope.startedAt));
  return entries;
});
