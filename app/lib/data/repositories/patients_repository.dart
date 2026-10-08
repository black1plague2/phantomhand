import 'package:opus_app/data/models/patient.dart';

class PatientFilter {
  const new({this.search = '', this.affectedSide, this.diagnosisContains});
  final String search;
  final AffectedSide? affectedSide;
  final String? diagnosisContains;
}

abstract class PatientsRepository {
  Future<List<Patient>> listPatients([PatientFilter filter = const PatientFilter()]);
  Future<Patient?> getPatient(String id);
}
