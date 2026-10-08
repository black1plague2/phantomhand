// `docs/IMPROVEMENT_BRIEF.md` §3 item 8: "prescribed vs completed minutes per
// week (from program.schedule + session durations), with the 30 min/session
// and >= 4 days/week targets shown in plain language." Exercises the real
// `doseAdherenceProvider` end to end against small fake repositories (not the
// real mocks, so the fixture data/dates can't drift the test's own
// assumptions).
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/program.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/programs_repository.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/shared/metrics/dose_adherence.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';
import 'package:riverpod/riverpod.dart';

class _FakeProgramsRepository implements ProgramsRepository {
  _FakeProgramsRepository(this.programs);
  final List<Program> programs;
  @override
  Future<List<Program>> listProgramsForPatient(String patientId) =>
      Future.value(programs.where((p) => p.patientRef == patientId).toList());
  @override
  Future<Program?> getProgram(String programId) => throw UnimplementedError();
  @override
  Future<Program> saveProgram(Program program) => throw UnimplementedError();
}

class _FakeSessionsRepository implements SessionsRepository {
  _FakeSessionsRepository(this.sessions);
  final List<SessionEnvelope> sessions;
  @override
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId) =>
      Future.value(sessions.where((s) => s.patientRef == patientId).toList());
  @override
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId) => throw UnimplementedError();
  @override
  Future<SessionMetrics?> getSessionMetrics(String sessionId) => throw UnimplementedError();
  @override
  Future<List<SessionEvent>> getSessionEvents(String sessionId) => throw UnimplementedError();
  @override
  Future<ReachTraceSet> getReachTraces(String sessionId) => throw UnimplementedError();
}

Program _program({
  required String patientId,
  ProgramStatus status = ProgramStatus.active,
  int sessionsPerWeek = 4,
  int maxSessionMinutes = 30,
}) =>
    Program(
      programId: 'prog-$patientId-${status.name}',
      patientRef: patientId,
      createdBy: 'test',
      schedule: ProgramSchedule(
        startDate: DateTime(2026, 1, 1),
        sessionsPerWeek: sessionsPerWeek,
        maxSessionMinutes: maxSessionMinutes,
      ),
      blocks: const [],
      status: status,
    );

SessionEnvelope _session({
  required String patientId,
  required DateTime startedAt,
  required Duration duration,
}) =>
    SessionEnvelope(
      sessionId: 'sess-${startedAt.toIso8601String()}',
      contractsVersion: '1.0.0',
      patientRef: patientId,
      programRef: 'prog-$patientId-active',
      startedAt: startedAt,
      endedAt: startedAt.add(duration),
      device: const SessionDevice(model: 'Quest 3'),
      calibration: const SessionCalibration(affectedSide: 'right'),
      blocks: const [],
    );

void main() {
  const patientId = 'test-patient';

  test('no program -> null (nothing to measure dose against)', () async {
    final container = ProviderContainer(
      overrides: [
        programsRepositoryProvider.overrideWithValue(_FakeProgramsRepository(const [])),
        sessionsRepositoryProvider.overrideWithValue(_FakeSessionsRepository(const [])),
      ],
    );
    addTearDown(container.dispose);

    final dose = await container.read(doseAdherenceProvider(patientId).future);
    expect(dose, isNull);
  });

  test('a program with zero completed sessions reports the prescription and zero completion', () async {
    final container = ProviderContainer(
      overrides: [
        programsRepositoryProvider.overrideWithValue(
          _FakeProgramsRepository([_program(patientId: patientId, sessionsPerWeek: 5, maxSessionMinutes: 25)]),
        ),
        sessionsRepositoryProvider.overrideWithValue(_FakeSessionsRepository(const [])),
      ],
    );
    addTearDown(container.dispose);

    final dose = await container.read(doseAdherenceProvider(patientId).future);
    expect(dose, isNotNull);
    expect(dose!.prescribedSessionsPerWeek, 5);
    expect(dose.prescribedMinutesPerSession, 25);
    expect(dose.hasCompletedSessions, isFalse);
    expect(dose.completedDays, 0);
    expect(dose.completedMinutes, 0);
  });

  test('sums completed minutes/days for the most recent week that has sessions, ignoring older weeks', () async {
    // Two sessions in an older week (should be ignored), three sessions (three
    // distinct days) in the most recent week, each 35 minutes -- picked to be
    // both >= the 30 min/session reference and >= 4 days/week only when all
    // three THIS week are counted, not the older ones.
    final olderWeekMonday = DateTime(2026, 8, 3); // an arbitrary past Monday
    final recentWeekMonday = DateTime(2026, 9, 14); // a later Monday
    final sessions = [
      _session(patientId: patientId, startedAt: olderWeekMonday, duration: const Duration(minutes: 10)),
      _session(
        patientId: patientId,
        startedAt: olderWeekMonday.add(const Duration(days: 1)),
        duration: const Duration(minutes: 10),
      ),
      _session(patientId: patientId, startedAt: recentWeekMonday, duration: const Duration(minutes: 35)),
      _session(
        patientId: patientId,
        startedAt: recentWeekMonday.add(const Duration(days: 2)),
        duration: const Duration(minutes: 35),
      ),
      _session(
        patientId: patientId,
        startedAt: recentWeekMonday.add(const Duration(days: 4)),
        duration: const Duration(minutes: 35),
      ),
    ];
    final container = ProviderContainer(
      overrides: [
        programsRepositoryProvider.overrideWithValue(
          _FakeProgramsRepository([_program(patientId: patientId, sessionsPerWeek: 4, maxSessionMinutes: 30)]),
        ),
        sessionsRepositoryProvider.overrideWithValue(_FakeSessionsRepository(sessions)),
      ],
    );
    addTearDown(container.dispose);

    final dose = await container.read(doseAdherenceProvider(patientId).future);
    expect(dose, isNotNull);
    expect(dose!.hasCompletedSessions, isTrue);
    expect(dose.weekStart, recentWeekMonday);
    expect(dose.completedDays, 3);
    expect(dose.completedMinutes, closeTo(105, 0.01));
    expect(dose.averageMinutesPerSession, closeTo(35, 0.01));
    // Meets both reference targets (docs/IMPROVEMENT_BRIEF.md §1: >= 30
    // min/session, >= 4 days/week) -- except frequency, which this fixture
    // deliberately falls short of (3 < 4) to exercise the "below" branch too.
    expect(dose.averageMinutesPerSession >= referenceMinMinutesPerSession, isTrue);
    expect(dose.completedDays >= referenceMinDaysPerWeek, isFalse);
  });

  test('prefers the active program when a patient has more than one', () async {
    final container = ProviderContainer(
      overrides: [
        programsRepositoryProvider.overrideWithValue(
          _FakeProgramsRepository([
            _program(patientId: patientId, status: ProgramStatus.archived, sessionsPerWeek: 2, maxSessionMinutes: 10),
            _program(patientId: patientId, status: ProgramStatus.active, sessionsPerWeek: 6, maxSessionMinutes: 45),
          ]),
        ),
        sessionsRepositoryProvider.overrideWithValue(_FakeSessionsRepository(const [])),
      ],
    );
    addTearDown(container.dispose);

    final dose = await container.read(doseAdherenceProvider(patientId).future);
    expect(dose!.prescribedSessionsPerWeek, 6);
    expect(dose.prescribedMinutesPerSession, 45);
  });
}
