import 'package:freezed_annotation/freezed_annotation.dart';

part 'program.freezed.dart';
part 'program.g.dart';

enum ProgramStatus { draft, active, paused, completed, archived }

enum ProgramLocation { clinic, home, either }

@freezed
abstract class ProgramSchedule with _$ProgramSchedule {
  const factory({
    required DateTime startDate,
    DateTime? endDate,
    @Default(3) int sessionsPerWeek,
    @Default(20) int maxSessionMinutes,
    @Default(ProgramLocation.either) ProgramLocation location,
  }) = _ProgramSchedule;

  factory fromJson(Map<String, Object?> json) =>
      _$ProgramScheduleFromJson(json);
}

/// One ordered block of a program: `contracts/schemas/program.schema.json#blocks`.
/// `params` is validated by the dynamic form engine against the referenced
/// game's `paramSchema` at build time -- this model carries whatever the
/// clinician set, unopinionated about which game it is.
@freezed
abstract class ProgramBlock with _$ProgramBlock {
  const factory({
    required String gameId,
    required String gameVersionRange,
    required Map<String, dynamic> params,
    @Default(180) int durationSec,
    @Default(30) int restAfterSec,
  }) = _ProgramBlock;

  factory fromJson(Map<String, Object?> json) => _$ProgramBlockFromJson(json);
}

@freezed
abstract class Program with _$Program {
  const factory({
    required String programId,
    required String patientRef,
    required String createdBy,
    required ProgramSchedule schedule, required List<ProgramBlock> blocks, String? title,
    String? notesForPatient,
    @Default(false) bool requiresSupervision,
    @Default(ProgramStatus.draft) ProgramStatus status,
  }) = _Program;

  factory fromJson(Map<String, Object?> json) => _$ProgramFromJson(json);
}
