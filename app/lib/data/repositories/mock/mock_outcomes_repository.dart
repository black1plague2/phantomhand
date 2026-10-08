import 'package:opus_app/data/models/outcome_measure.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';
import 'package:opus_app/data/repositories/outcomes_repository.dart';
import 'package:uuid/uuid.dart';

/// In-memory, seeded with a short improving trend per outcome type for every
/// synthetic patient so the trend chart (A5/A6) has something to plot before
/// a clinician enters real data.
class MockOutcomesRepository implements OutcomesRepository {
  new({FixtureLoader? loader, this.errorInjector})
      : _loader = loader ?? const FixtureLoader() {
    _seed();
  }

  final FixtureLoader _loader;
  final MockErrorInjector? errorInjector;
  final List<OutcomeMeasureEntry> _entries = [];
  static const _uuid = Uuid();

  void _seed() {
    final now = DateTime.now();
    const patientId = 'synthetic-longitudinal-9000';
    final fmaScores = [28.0, 33.0, 37.0, 41.0];
    for (var i = 0; i < fmaScores.length; i++) {
      _entries.add(
        OutcomeMeasureEntry(
          id: _uuid.v4(),
          patientId: patientId,
          type: OutcomeMeasureType.fmaUe,
          date: now.subtract(Duration(days: (fmaScores.length - i) * 14)),
          score: fmaScores[i],
          enteredBy: 'mock-clinician',
        ),
      );
    }
  }

  @override
  Future<List<OutcomeMeasureEntry>> listEntries(String patientId, {OutcomeMeasureType? type}) async {
    errorInjector?.maybeThrow('listEntries');
    await _loader.latency();
    return _entries
        .where((e) => e.patientId == patientId && (type == null || e.type == type))
        .toList()
      ..sort((a, b) => a.date.compareTo(b.date));
  }

  @override
  Future<OutcomeMeasureEntry> addEntry(OutcomeMeasureEntry entry) async {
    errorInjector?.maybeThrow('addEntry');
    await _loader.latency();
    final withId = entry.id.isEmpty ? entry.copyWith(id: _uuid.v4()) : entry;
    _entries.add(withId);
    return withId;
  }
}
