// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'live_message.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_LiveMessage _$LiveMessageFromJson(Map<String, dynamic> json) => _LiveMessage(
  status: $enumDecode(_$LiveSessionStatusEnumMap, json['status']),
  trialIndex: (json['trial_index'] as num).toInt(),
  totalTrials: (json['total_trials'] as num).toInt(),
  rollingMetrics: (json['rolling_metrics'] as Map<String, dynamic>).map(
    (k, e) => MapEntry(k, (e as num).toDouble()),
  ),
  lastOutcome: json['last_outcome'] as String?,
);

Map<String, dynamic> _$LiveMessageToJson(_LiveMessage instance) =>
    <String, dynamic>{
      'status': _$LiveSessionStatusEnumMap[instance.status]!,
      'trial_index': instance.trialIndex,
      'total_trials': instance.totalTrials,
      'rolling_metrics': instance.rollingMetrics,
      'last_outcome': instance.lastOutcome,
    };

const _$LiveSessionStatusEnumMap = {
  LiveSessionStatus.running: 'running',
  LiveSessionStatus.paused: 'paused',
  LiveSessionStatus.stopped: 'stopped',
};
