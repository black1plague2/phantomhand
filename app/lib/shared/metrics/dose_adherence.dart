/// Dose adherence: prescribed vs completed minutes/days per week
/// (`docs/IMPROVEMENT_BRIEF.md` §3 item 8 / §1: "dose >= 30 min/session and
/// >= 4 days/week ... surface dose adherence in Flutter progress view").
/// Deliberately states only two things: (1) what the patient's own active
/// program prescribes (`ProgramSchedule`), and (2) what the recorded
/// sessions actually add up to for the most recent week that has any --
/// **no clinical claim is made about outcomes from meeting or missing
/// either**, per the item's own "No clinical claims beyond the prescription"
/// instruction. The literature-derived reference numbers (30 min/session, >=
/// 4 days/week) are shown as plain reference points, not as a promise that
/// meeting them produces a specific effect size.
library;

import 'package:collection/collection.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/program.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:riverpod/src/providers/future_provider.dart';

/// The literature-derived reference targets `docs/IMPROVEMENT_BRIEF.md` §1
/// names ("≥30 min/session, ≥4-5 days/week"). Shown as plain reference
/// points alongside the patient's actual prescription -- not asserted as
/// this app's own clinical claim.
const referenceMinMinutesPerSession = 30;
const referenceMinDaysPerWeek = 4;

class DoseAdherenceData {
  const new({
    required this.prescribedSessionsPerWeek,
    required this.prescribedMinutesPerSession,
    required this.weekStart,
    required this.hasCompletedSessions,
    required this.completedDays,
    required this.completedMinutes,
  });

  /// From the active program's `ProgramSchedule` -- the clinician's own
  /// prescription, not a literature target.
  final int prescribedSessionsPerWeek;
  final int prescribedMinutesPerSession;

  /// Monday of the most recent week that has at least one completed session
  /// (not necessarily the current calendar week -- synthetic/demo session
  /// dates rarely land in "this week", and describing the actual most-recent
  /// week of real data is more honest than always reporting zero).
  final DateTime weekStart;
  final bool hasCompletedSessions;
  final int completedDays;
  final double completedMinutes;

  double get averageMinutesPerSession => completedDays == 0 ? 0 : completedMinutes / completedDays;
}

DateTime _startOfWeek(DateTime dt) {
  final d = DateTime(dt.year, dt.month, dt.day);
  return d.subtract(Duration(days: d.weekday - 1)); // Monday
}

/// A session's duration in minutes: `endedAt - startedAt` when the session
/// has actually ended, otherwise the last recorded block's end timestamp (an
/// in-progress or abnormally-ended session still did *some* practice worth
/// counting). Returns null only when neither is available.
double? _sessionMinutes(SessionEnvelope s) {
  final endedAt = s.endedAt;
  if (endedAt != null) return endedAt.difference(s.startedAt).inSeconds / 60.0;
  final lastEndMs = s.blocks.map((b) => b.endedTMs).whereType<double>().maxOrNull;
  if (lastEndMs != null) return lastEndMs / 60000.0;
  return null;
}

/// Null when the patient has no program to measure dose against (nothing was
/// prescribed, so there's nothing to compare completion to).
final FutureProviderFamily<DoseAdherenceData?, String> doseAdherenceProvider =
    FutureProvider.family<DoseAdherenceData?, String>((ref, patientId) async {
  final programsRepo = ref.watch(programsRepositoryProvider);
  final sessionsRepo = ref.watch(sessionsRepositoryProvider);

  final programs = await programsRepo.listProgramsForPatient(patientId);
  final program =
      programs.firstWhereOrNull((p) => p.status == ProgramStatus.active) ?? programs.firstOrNull;
  if (program == null) return null;

  final sessions = await sessionsRepo.listSessionsForPatient(patientId);
  if (sessions.isEmpty) {
    return DoseAdherenceData(
      prescribedSessionsPerWeek: program.schedule.sessionsPerWeek,
      prescribedMinutesPerSession: program.schedule.maxSessionMinutes,
      weekStart: _startOfWeek(DateTime.now()),
      hasCompletedSessions: false,
      completedDays: 0,
      completedMinutes: 0,
    );
  }

  final byWeek = <DateTime, List<SessionEnvelope>>{};
  for (final s in sessions) {
    byWeek.putIfAbsent(_startOfWeek(s.startedAt), () => []).add(s);
  }
  final latestWeek = byWeek.keys.reduce((a, b) => a.isAfter(b) ? a : b);
  final weekSessions = byWeek[latestWeek]!;
  final days = weekSessions.map((s) => DateTime(s.startedAt.year, s.startedAt.month, s.startedAt.day)).toSet();
  final totalMinutes = weekSessions.map(_sessionMinutes).whereType<double>().fold<double>(0, (a, b) => a + b);

  return DoseAdherenceData(
    prescribedSessionsPerWeek: program.schedule.sessionsPerWeek,
    prescribedMinutesPerSession: program.schedule.maxSessionMinutes,
    weekStart: latestWeek,
    hasCompletedSessions: true,
    completedDays: days.length,
    completedMinutes: totalMinutes,
  );
});
