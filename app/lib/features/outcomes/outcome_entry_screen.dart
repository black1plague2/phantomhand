import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/outcome_measure.dart';
import 'package:opus_app/l10n/app_localizations.dart';

/// FMA-UE, ARAT, Box and Block, MAS entry -- brief A6.
class OutcomeEntryScreen extends ConsumerStatefulWidget {
  const new({required this.patientId, super.key});
  final String patientId;

  @override
  ConsumerState<OutcomeEntryScreen> createState() => _OutcomeEntryScreenState();
}

class _OutcomeEntryScreenState extends ConsumerState<OutcomeEntryScreen> {
  OutcomeMeasureType _type = OutcomeMeasureType.fmaUe;
  double _score = 0;
  DateTime _date = DateTime.now();
  bool _saving = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final labels = {
      OutcomeMeasureType.fmaUe: l10n.outcomesFmaUe,
      OutcomeMeasureType.arat: l10n.outcomesArat,
      OutcomeMeasureType.boxAndBlock: l10n.outcomesBoxBlock,
      OutcomeMeasureType.mas: l10n.outcomesMas,
    };
    final (min, max) = _type.range;
    _score = _score.clamp(min, max);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.outcomesNewEntry)),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            DropdownButtonFormField<OutcomeMeasureType>(
              initialValue: _type,
              items: [for (final t in OutcomeMeasureType.values) DropdownMenuItem(value: t, child: Text(labels[t]!))],
              onChanged: (t) => setState(() {
                _type = t!;
                _score = _type.range.$1;
              }),
            ),
            const SizedBox(height: 16),
            ListTile(
              title: Text(l10n.outcomesDate),
              subtitle: Text(_date.toIso8601String().split('T').first),
              trailing: const Icon(Icons.calendar_today),
              onTap: () async {
                final picked = await showDatePicker(
                  context: context,
                  initialDate: _date,
                  firstDate: DateTime.now().subtract(const Duration(days: 365 * 2)),
                  lastDate: DateTime.now(),
                );
                if (picked != null) setState(() => _date = picked);
              },
            ),
            const SizedBox(height: 16),
            Text('${l10n.outcomesScore}: ${_score.toStringAsFixed(0)} / ${max.toStringAsFixed(0)} ${_type.unit}'),
            Slider(
              value: _score,
              min: min,
              max: max,
              divisions: (max - min).round().clamp(1, 200),
              label: _score.toStringAsFixed(0),
              onChanged: (v) => setState(() => _score = v),
            ),
            const SizedBox(height: 24),
            FilledButton(
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(48)),
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                  : Text(l10n.outcomesSave),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _save() async {
    setState(() => _saving = true);
    await ref.read(outcomesRepositoryProvider).addEntry(
          OutcomeMeasureEntry(
            id: '',
            patientId: widget.patientId,
            type: _type,
            date: _date,
            score: _score,
            enteredBy: 'mock-clinician',
          ),
        );
    if (mounted) context.pop();
  }
}
