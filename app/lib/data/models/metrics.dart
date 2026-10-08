import 'package:freezed_annotation/freezed_annotation.dart';

part 'metrics.freezed.dart';
part 'metrics.g.dart';

/// `contracts/schemas/metrics.schema.json#$defs/metricValue`. Every metric the
/// analytics worker produces carries these four fields -- the app never shows
/// a bare number without unit + quality (goal G5 / ARCHITECTURE.md §5).
@freezed
abstract class MetricValue with _$MetricValue {
  const factory({
    required double? value,
    required String unit,
    required String methodVersion,
    required String quality, // "ok" | "degraded" | "invalid"
    @Default([]) List<String> qualityReasons,
    double? sd,
    int? n,
    // Real per-metric MDC95 from opus_analytics v0.2.0 (as of 2026-09-17,
    // only `trunk_lean_cm`/`neglect_index` carry it -- other metrics still
    // fall back to the SD-based stand-in, see `progress_data.dart`). Opus's
    // note: "These MDC values come from SYNTHETIC test-retest and are far
    // tighter than real patients" -- never shown without the "synthetic
    // estimate" caveat next to whatever band it draws.
    double? mdc95,
  }) = _MetricValue;

  factory fromJson(Map<String, Object?> json) => _$MetricValueFromJson(json);
}

@freezed
abstract class TrialMetrics with _$TrialMetrics {
  const factory({
    required int block,
    required int trial,
    required Map<String, MetricValue> metrics, String? hand,
    String? outcome,
    Map<String, dynamic>? target,
    double? tStartMs,
    double? tEndMs,
  }) = _TrialMetrics;

  factory fromJson(Map<String, Object?> json) => _$TrialMetricsFromJson(json);
}

@freezed
abstract class SessionAggregateMetrics with _$SessionAggregateMetrics {
  const factory({
    required Map<String, MetricValue> metrics,
    Map<String, Map<String, MetricValue>>? bySide,
    Map<String, dynamic>? workspaceHeatmap,
  }) = _SessionAggregateMetrics;

  factory fromJson(Map<String, Object?> json) =>
      _$SessionAggregateMetricsFromJson(json);
}

/// `metrics.json` written by `opus_analytics` into a session directory.
@freezed
abstract class SessionMetrics with _$SessionMetrics {
  const factory({
    required String sessionId,
    required String analyticsVersion,
    required DateTime computedAt,
    required List<TrialMetrics> trials,
    required SessionAggregateMetrics session,
  }) = _SessionMetrics;

  factory fromJson(Map<String, Object?> json) => _$SessionMetricsFromJson(json);
}
