import 'package:opus_app/data/models/program.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';
import 'package:opus_app/data/repositories/programs_repository.dart';
import 'package:uuid/uuid.dart';

/// In-memory only (no persistence across app restarts on mocks). Seeded with
/// one example program per synthetic patient, modeled on
/// `contracts/fixtures/valid/program.orchard.json`, so the program list and
/// session-report screens have something to link against out of the box.
class MockProgramsRepository implements ProgramsRepository {
  new({FixtureLoader? loader, this.errorInjector})
      : _loader = loader ?? const FixtureLoader() {
    _seed();
  }

  final FixtureLoader _loader;
  final MockErrorInjector? errorInjector;
  final List<Program> _programs = [];
  static const _uuid = Uuid();

  void _seed() {
    const patientIds = [
      'synthetic-healthy-42',
      'synthetic-mild-42',
      'synthetic-moderate-42',
      'synthetic-severe-42',
      'synthetic-left_neglect-42',
      'synthetic-noisy_tracking-42',
      'synthetic-longitudinal-9000',
    ];
    for (final patientId in patientIds) {
      _programs.add(
        Program(
          programId: 'prog_${_uuid.v4()}',
          patientRef: patientId,
          createdBy: 'mock-clinician',
          title: 'Orchard Reach — gentle start',
          notesForPatient: 'Start with the gentle preset and progress as tolerated.',
          schedule: ProgramSchedule(
            startDate: DateTime.now().subtract(const Duration(days: 14)),
          ),
          blocks: const [
            ProgramBlock(
              gameId: 'orchard_reach',
              gameVersionRange: '^0.1.0',
              params: {
                'side': 'right',
                'trialCount': 10,
                'reachPercent': [40, 65],
                'azimuthRangeDeg': [-20, 20],
                'elevationRangeDeg': [0, 20],
                'timeLimitMs': 30000,
              },
              durationSec: 600,
              restAfterSec: 120,
            ),
          ],
          status: ProgramStatus.active,
        ),
      );
    }
  }

  @override
  Future<List<Program>> listProgramsForPatient(String patientId) async {
    errorInjector?.maybeThrow('listProgramsForPatient');
    await _loader.latency();
    return _programs.where((p) => p.patientRef == patientId).toList();
  }

  @override
  Future<Program?> getProgram(String programId) async {
    errorInjector?.maybeThrow('getProgram');
    await _loader.latency();
    for (final p in _programs) {
      if (p.programId == programId) return p;
    }
    return null;
  }

  @override
  Future<Program> saveProgram(Program program) async {
    errorInjector?.maybeThrow('saveProgram');
    await _loader.latency();
    final withId = program.programId.isEmpty
        ? program.copyWith(programId: 'prog_${_uuid.v4()}')
        : program;
    _programs.removeWhere((p) => p.programId == withId.programId);
    _programs.add(withId);
    return withId;
  }
}
