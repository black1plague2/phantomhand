import 'package:opus_app/data/models/program.dart';

abstract class ProgramsRepository {
  Future<List<Program>> listProgramsForPatient(String patientId);
  Future<Program?> getProgram(String programId);
  /// Upserts by `programId`. The dynamic-form-driven program builder (A4)
  /// is the only writer of these on mocks; Phase 3 posts to the backend
  /// instead.
  Future<Program> saveProgram(Program program);
}
