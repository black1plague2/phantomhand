// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'metrics.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_MetricValue _$MetricValueFromJson(Map<String, dynamic> json) => _MetricValue(
  value: (json['value'] as num?)?.toDouble(),
  unit: json['unit'] as String,
  methodVersion: json['method_version'] as String,
  quality: json['quality'] as String,
  qualityReasons:
      (json['quality_reasons'] as List<dynamic>?)
          ?.map((e) => e as String)
          .toList() ??
      const [],
  sd: (json['sd'] as num?)?.toDouble(),
  n: (json['n'] as num?)?.toInt(),
  mdc95: (json['mdc95'] as num?)?.toDouble(),
);

Map<String, dynamic> _$MetricValueToJson(_MetricValue instance) =>
    <String, dynamic>{
      'value': instance.value,
      'unit': instance.unit,
      'method_version': instance.methodVersion,
      'quality': instance.quality,
      'quality_reasons': instance.qualityReasons,
      'sd': instance.sd,
      'n': instance.n,
      'mdc95': instance.mdc95,
    };

_TrialMetrics _$TrialMetricsFromJson(Map<String, dynamic> json) =>
    _TrialMetrics(
      block: (json['block'] as num).toInt(),
      trial: (json['trial'] as num).toInt(),
      metrics: (json['metrics'] as Map<String, dynamic>).map(
        (k, e) => MapEntry(k, MetricValue.fromJson(e as Map<String, dynamic>)),
      ),
      hand: json['hand'] as String?,
      outcome: json['outcome'] as String?,
      target: json['target'] as Map<String, dynamic>?,
      tStartMs: (json['t_start_ms'] as num?)?.toDouble(),
      tEndMs: (json['t_end_ms'] as num?)?.toDouble(),
    );

Map<String, dynamic> _$TrialMetricsToJson(_TrialMetrics instance) =>
    <String, dynamic>{
      'block': instance.block,
      'trial': instance.trial,
      'metrics': instance.metrics.map((k, e) => MapEntry(k, e.toJson())),
      'hand': instance.hand,
      'outcome': instance.outcome,
      'target': instance.target,
      't_start_ms': instance.tStartMs,
      't_end_ms': instance.tEndMs,
    };

_SessionAggregateMetrics _$SessionAggregateMetricsFromJson(
  Map<String, dynamic> json,
) => _SessionAggregateMetrics(
  metrics: (json['metrics'] as Map<String, dynamic>).map(
    (k, e) => MapEntry(k, MetricValue.fromJson(e as Map<String, dynamic>)),
  ),
  bySide: (json['by_side'] as Map<String, dynamic>?)?.map(
    (k, e) => MapEntry(
      k,
      (e as Map<String, dynamic>).map(
        (k, e) => MapEntry(k, MetricValue.fromJson(e as Map<String, dynamic>)),
      ),
    ),
  ),
  workspaceHeatmap: json['workspace_heatmap'] as Map<String, dynamic>?,
);

Map<String, dynamic> _$SessionAggregateMetricsToJson(
  _SessionAggregateMetrics instance,
) => <String, dynamic>{
  'metrics': instance.metrics.map((k, e) => MapEntry(k, e.toJson())),
  'by_side': instance.bySide?.map(
    (k, e) => MapEntry(k, e.map((k, e) => MapEntry(k, e.toJson()))),
  ),
  'workspace_heatmap': instance.workspaceHeatmap,
};

_SessionMetrics _$SessionMetricsFromJson(Map<String, dynamic> json) =>
    _SessionMetrics(
      sessionId: json['session_id'] as String,
      analyticsVersion: json['analytics_version'] as String,
      computedAt: DateTime.parse(json['computed_at'] as String),
      trials: (json['trials'] as List<dynamic>)
          .map((e) => TrialMetrics.fromJson(e as Map<String, dynamic>))
          .toList(),
      session: SessionAggregateMetrics.fromJson(
        json['session'] as Map<String, dynamic>,
      ),
    );

Map<String, dynamic> _$SessionMetricsToJson(_SessionMetrics instance) =>
    <String, dynamic>{
      'session_id': instance.sessionId,
      'analytics_version': instance.analyticsVersion,
      'computed_at': instance.computedAt.toIso8601String(),
      'trials': instance.trials.map((e) => e.toJson()).toList(),
      'session': instance.session.toJson(),
    };
