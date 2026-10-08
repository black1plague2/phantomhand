// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'outcome_measure.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_OutcomeMeasureEntry _$OutcomeMeasureEntryFromJson(Map<String, dynamic> json) =>
    _OutcomeMeasureEntry(
      id: json['id'] as String,
      patientId: json['patient_id'] as String,
      type: $enumDecode(_$OutcomeMeasureTypeEnumMap, json['type']),
      date: DateTime.parse(json['date'] as String),
      score: (json['score'] as num).toDouble(),
      side: json['side'] as String?,
      enteredBy: json['entered_by'] as String?,
      notes: json['notes'] as String?,
    );

Map<String, dynamic> _$OutcomeMeasureEntryToJson(
  _OutcomeMeasureEntry instance,
) => <String, dynamic>{
  'id': instance.id,
  'patient_id': instance.patientId,
  'type': _$OutcomeMeasureTypeEnumMap[instance.type]!,
  'date': instance.date.toIso8601String(),
  'score': instance.score,
  'side': instance.side,
  'entered_by': instance.enteredBy,
  'notes': instance.notes,
};

const _$OutcomeMeasureTypeEnumMap = {
  OutcomeMeasureType.fmaUe: 'fmaUe',
  OutcomeMeasureType.arat: 'arat',
  OutcomeMeasureType.boxAndBlock: 'boxAndBlock',
  OutcomeMeasureType.mas: 'mas',
};
