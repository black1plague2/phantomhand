// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'patient.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_Patient _$PatientFromJson(Map<String, dynamic> json) => _Patient(
  id: json['id'] as String,
  displayName: json['display_name'] as String,
  age: (json['age'] as num).toInt(),
  gender: json['gender'] as String,
  affectedSide: $enumDecode(_$AffectedSideEnumMap, json['affected_side']),
  diagnosis: json['diagnosis'] as String,
  onsetDate: DateTime.parse(json['onset_date'] as String),
  programIds:
      (json['program_ids'] as List<dynamic>?)
          ?.map((e) => e as String)
          .toList() ??
      const [],
  sessionIds:
      (json['session_ids'] as List<dynamic>?)
          ?.map((e) => e as String)
          .toList() ??
      const [],
);

Map<String, dynamic> _$PatientToJson(_Patient instance) => <String, dynamic>{
  'id': instance.id,
  'display_name': instance.displayName,
  'age': instance.age,
  'gender': instance.gender,
  'affected_side': _$AffectedSideEnumMap[instance.affectedSide]!,
  'diagnosis': instance.diagnosis,
  'onset_date': instance.onsetDate.toIso8601String(),
  'program_ids': instance.programIds,
  'session_ids': instance.sessionIds,
};

const _$AffectedSideEnumMap = {
  AffectedSide.left: 'left',
  AffectedSide.right: 'right',
  AffectedSide.both: 'both',
  AffectedSide.none: 'none',
};
