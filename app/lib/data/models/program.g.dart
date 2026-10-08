// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'program.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ProgramSchedule _$ProgramScheduleFromJson(Map<String, dynamic> json) =>
    _ProgramSchedule(
      startDate: DateTime.parse(json['start_date'] as String),
      endDate: json['end_date'] == null
          ? null
          : DateTime.parse(json['end_date'] as String),
      sessionsPerWeek: (json['sessions_per_week'] as num?)?.toInt() ?? 3,
      maxSessionMinutes: (json['max_session_minutes'] as num?)?.toInt() ?? 20,
      location:
          $enumDecodeNullable(_$ProgramLocationEnumMap, json['location']) ??
          ProgramLocation.either,
    );

Map<String, dynamic> _$ProgramScheduleToJson(_ProgramSchedule instance) =>
    <String, dynamic>{
      'start_date': instance.startDate.toIso8601String(),
      'end_date': instance.endDate?.toIso8601String(),
      'sessions_per_week': instance.sessionsPerWeek,
      'max_session_minutes': instance.maxSessionMinutes,
      'location': _$ProgramLocationEnumMap[instance.location]!,
    };

const _$ProgramLocationEnumMap = {
  ProgramLocation.clinic: 'clinic',
  ProgramLocation.home: 'home',
  ProgramLocation.either: 'either',
};

_ProgramBlock _$ProgramBlockFromJson(Map<String, dynamic> json) =>
    _ProgramBlock(
      gameId: json['game_id'] as String,
      gameVersionRange: json['game_version_range'] as String,
      params: json['params'] as Map<String, dynamic>,
      durationSec: (json['duration_sec'] as num?)?.toInt() ?? 180,
      restAfterSec: (json['rest_after_sec'] as num?)?.toInt() ?? 30,
    );

Map<String, dynamic> _$ProgramBlockToJson(_ProgramBlock instance) =>
    <String, dynamic>{
      'game_id': instance.gameId,
      'game_version_range': instance.gameVersionRange,
      'params': instance.params,
      'duration_sec': instance.durationSec,
      'rest_after_sec': instance.restAfterSec,
    };

_Program _$ProgramFromJson(Map<String, dynamic> json) => _Program(
  programId: json['program_id'] as String,
  patientRef: json['patient_ref'] as String,
  createdBy: json['created_by'] as String,
  schedule: ProgramSchedule.fromJson(json['schedule'] as Map<String, dynamic>),
  blocks: (json['blocks'] as List<dynamic>)
      .map((e) => ProgramBlock.fromJson(e as Map<String, dynamic>))
      .toList(),
  title: json['title'] as String?,
  notesForPatient: json['notes_for_patient'] as String?,
  requiresSupervision: json['requires_supervision'] as bool? ?? false,
  status:
      $enumDecodeNullable(_$ProgramStatusEnumMap, json['status']) ??
      ProgramStatus.draft,
);

Map<String, dynamic> _$ProgramToJson(_Program instance) => <String, dynamic>{
  'program_id': instance.programId,
  'patient_ref': instance.patientRef,
  'created_by': instance.createdBy,
  'schedule': instance.schedule.toJson(),
  'blocks': instance.blocks.map((e) => e.toJson()).toList(),
  'title': instance.title,
  'notes_for_patient': instance.notesForPatient,
  'requires_supervision': instance.requiresSupervision,
  'status': _$ProgramStatusEnumMap[instance.status]!,
};

const _$ProgramStatusEnumMap = {
  ProgramStatus.draft: 'draft',
  ProgramStatus.active: 'active',
  ProgramStatus.paused: 'paused',
  ProgramStatus.completed: 'completed',
  ProgramStatus.archived: 'archived',
};
