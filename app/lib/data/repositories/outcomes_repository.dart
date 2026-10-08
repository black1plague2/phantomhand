import 'package:opus_app/data/models/outcome_measure.dart';

abstract class OutcomesRepository {
  Future<List<OutcomeMeasureEntry>> listEntries(String patientId, {OutcomeMeasureType? type});
  Future<OutcomeMeasureEntry> addEntry(OutcomeMeasureEntry entry);
}
