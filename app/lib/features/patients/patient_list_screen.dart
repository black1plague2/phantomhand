import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/patient.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';
import 'package:opus_app/data/repositories/patients_repository.dart';
import 'package:opus_app/l10n/app_localizations.dart';
import 'package:opus_app/shared/clinical/condition_short_form.dart';
import 'package:opus_app/shared/design/v2_colors.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';

/// Design v2 §5 "Patients": title "Patients", search field, list rows: name
/// (15/600), condition short form ("Stroke · right"), right side: last
/// session date + a 40 px success bar. No descriptions, no side-filter
/// dropdown, no age/diagnosis subtitle, no side chip -- all of that was
/// helper text/decoration the design doc explicitly asks to remove.
class PatientListScreen extends ConsumerStatefulWidget {
  const PatientListScreen({super.key});

  @override
  ConsumerState<PatientListScreen> createState() => _PatientListScreenState();
}

class _PatientListScreenState extends ConsumerState<PatientListScreen> {
  String _search = '';
  late Future<List<_PatientRow>> _future;

  @override
  void initState() {
    super.initState();
    _future = _load();
  }

  Future<List<_PatientRow>> _load() async {
    final patientsRepo = ref.read(patientsRepositoryProvider);
    final sessionsRepo = ref.read(sessionsRepositoryProvider);
    final patients = await patientsRepo.listPatients(PatientFilter(search: _search));
    final rows = <_PatientRow>[];
    for (final p in patients) {
      final sessions = await sessionsRepo.listSessionsForPatient(p.id);
      DateTime? lastDate;
      int? successPct;
      if (sessions.isNotEmpty) {
        final sorted = [...sessions]..sort((a, b) => b.startedAt.compareTo(a.startedAt));
        lastDate = sorted.first.startedAt;
        final metrics = await sessionsRepo.getSessionMetrics(sorted.first.sessionId);
        if (metrics != null) {
          final withOutcome = metrics.trials.where((t) => t.outcome != null).toList();
          if (withOutcome.isNotEmpty) {
            final successes = withOutcome.where((t) => t.outcome == 'success').length;
            successPct = (100 * successes / withOutcome.length).round();
          }
        }
      }
      rows.add(_PatientRow(patient: p, lastSessionAt: lastDate, successPct: successPct));
    }
    return rows;
  }

  void _reload() => setState(() => _future = _load());

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Scaffold(
      backgroundColor: V2Colors.black,
      appBar: AppBar(
        backgroundColor: V2Colors.black,
        foregroundColor: V2Colors.text,
        title: Text(l10n.patientListTitle),
      ),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Semantics(
              textField: true,
              label: l10n.patientSearchHint,
              child: TextField(
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: V2Colors.text),
                decoration: InputDecoration(
                  isDense: true,
                  hintText: l10n.patientSearchHint,
                  hintStyle: Theme.of(context).textTheme.bodyMedium?.copyWith(color: V2Colors.textDim),
                  prefixIcon: const Icon(Icons.search, color: V2Colors.textDim),
                  filled: true,
                  fillColor: V2Colors.panel,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(8),
                    borderSide: const BorderSide(color: V2Colors.line),
                  ),
                ),
                onChanged: (v) {
                  _search = v;
                  _reload();
                },
              ),
            ),
            const SizedBox(height: 16),
            Expanded(
              child: FutureBuilder<List<_PatientRow>>(
                future: _future,
                builder: (context, snapshot) {
                  if (snapshot.connectionState == ConnectionState.waiting) {
                    return const Center(child: CircularProgressIndicator());
                  }
                  if (snapshot.hasError) {
                    return ErrorRetryView(
                      message: snapshot.error is MockRepositoryException
                          ? snapshot.error.toString()
                          : l10n.commonErrorGeneric,
                      onRetry: _reload,
                    );
                  }
                  final rows = snapshot.data ?? const [];
                  if (rows.isEmpty) {
                    return Center(
                      child: Text(l10n.patientNoResults, style: const TextStyle(color: V2Colors.textDim)),
                    );
                  }
                  return ListView.separated(
                    itemCount: rows.length,
                    separatorBuilder: (_, _) => const Divider(height: 1, color: V2Colors.line),
                    itemBuilder: (context, i) => _PatientListTile(row: rows[i]),
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _PatientRow {
  const _PatientRow({required this.patient, required this.lastSessionAt, required this.successPct});
  final Patient patient;
  final DateTime? lastSessionAt;
  final int? successPct;
}

class _PatientListTile extends StatelessWidget {
  const _PatientListTile({required this.row});
  final _PatientRow row;

  @override
  Widget build(BuildContext context) {
    final p = row.patient;
    final textTheme = Theme.of(context).textTheme;
    // Design v2 §5: the SHORT form ("Stroke · right"), never the full
    // clinical description. `p.diagnosis` is a paragraph
    // ("Post-stroke (ischemic, MCA), mild residual right-sided weakness")
    // which wrapped to two lines on the 360 dp pilot phone -- the exact
    // clutter the user complained about. Derived, not hardcoded per patient:
    // see `shared/clinical/condition_short_form.dart`.
    final condition = patientConditionLine(p);
    return InkWell(
      onTap: () => context.go('/patients/${p.id}'),
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
                    p.displayName,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelLarge?.copyWith(color: V2Colors.text),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    condition,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
                  ),
                ],
              ),
            ),
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  row.lastSessionAt == null ? '-' : _formatDate(row.lastSessionAt!),
                  style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
                ),
                const SizedBox(height: 6),
                _SuccessBar(successPct: row.successPct),
              ],
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

/// A 40 px success bar (§5): a thin horizontal fill, `good` up to the
/// success % (v3 amendment: colour by meaning -- this bar's fill IS a
/// success rate), `line` for the remainder. No numeric label -- the doc's
/// "no descriptions" rule; the bar itself is the value.
class _SuccessBar extends StatelessWidget {
  const _SuccessBar({required this.successPct});
  final int? successPct;

  @override
  Widget build(BuildContext context) {
    final pct = (successPct ?? 0).clamp(0, 100) / 100;
    return ClipRRect(
      borderRadius: BorderRadius.circular(2),
      child: SizedBox(
        width: 40,
        height: 4,
        child: Stack(
          children: [
            Container(color: V2Colors.line),
            if (successPct != null)
              FractionallySizedBox(
                widthFactor: pct,
                child: Container(color: V2Colors.good),
              ),
          ],
        ),
      ),
    );
  }
}
