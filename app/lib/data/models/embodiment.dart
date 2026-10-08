/// The `embodiment` block of a Phantom Hand `metrics.json` (B19, the
/// embodiment report): per condition (`sync`, `async`) and the contrast
/// `sync_minus_async`, each metric is `{value, unit, method_version, quality,
/// quality_reasons?, n?}` (`contracts/schemas/metrics.schema.json`,
/// `analytics/opus_analytics/metrics/embodiment.py`).
///
/// Pure Dart (no Flutter, no I/O, no codegen), tolerant on purpose: a metric
/// that is absent, not a map or has no number comes back as no value, never as
/// 0. Metrics that are themselves a pair (`stroke_timing_err_ms`,
/// `async_delay_ms` = `{mean, p95}`) are not leaves and are skipped here.
library;

/// `quality` of one value. `ok` is shown plainly, `degraded` is shown flagged
/// "Partial", `invalid` and `missing` are not shown as numbers.
enum EmbodimentQuality {
  ok,
  degraded,
  invalid,
  missing;

  /// Unknown or absent quality: a value is treated as `degraded` (shown, but
  /// flagged), no value as `missing`.
  static EmbodimentQuality parse(Object? raw, {required bool hasValue}) {
    for (final q in EmbodimentQuality.values) {
      if (q.name == raw) return q;
    }
    return hasValue ? degraded : missing;
  }
}

/// One metric value of one condition.
class EmbodimentValue {
  const new({required this.value, required this.unit, required this.quality, this.reasons = const []});

  /// Null when the pipeline had no number.
  final double? value;
  final String? unit;
  final EmbodimentQuality quality;
  final List<String> reasons;

  static EmbodimentValue? tryParse(Object? raw) {
    if (raw is! Map || !(raw.containsKey('value') || raw.containsKey('quality'))) return null;
    final v = raw['value'];
    final value = v is num && v.isFinite ? v.toDouble() : null;
    final reasons = raw['quality_reasons'];
    final unit = raw['unit'];
    return EmbodimentValue(
      value: value,
      unit: unit is String ? unit : null,
      quality: EmbodimentQuality.parse(raw['quality'], hasValue: value != null),
      reasons: reasons is List
          ? [
              for (final r in reasons)
                if (r is String) r,
            ]
          : const [],
    );
  }
}

Map<String, EmbodimentValue> _metrics(Object? raw) {
  if (raw is! Map) return const {};
  final out = <String, EmbodimentValue>{};
  for (final e in raw.entries) {
    final v = EmbodimentValue.tryParse(e.value);
    if (e.key is String && v != null) out[e.key as String] = v;
  }
  return out;
}

/// The whole block.
class Embodiment {
  const new({
    required this.sync,
    required this.async,
    required this.difference,
    this.conditionOrder = const [],
  });

  /// Metric key -> value, for the SYNC condition ("In sync").
  final Map<String, EmbodimentValue> sync;

  /// Same for the ASYNC condition ("Delayed").
  final Map<String, EmbodimentValue> async;

  /// `sync_minus_async`.
  final Map<String, EmbodimentValue> difference;

  /// `sync` / `async`, the order the conditions were run in.
  final List<String> conditionOrder;

  /// Parses the `embodiment` object; null when it is not a map or has neither
  /// a `sync` nor an `async` map.
  static Embodiment? tryParse(Object? raw) {
    if (raw is! Map || (raw['sync'] is! Map && raw['async'] is! Map)) return null;
    final order = raw['condition_order'];
    return Embodiment(
      sync: _metrics(raw['sync']),
      async: _metrics(raw['async']),
      difference: _metrics(raw['sync_minus_async']),
      conditionOrder: order is List
          ? [
              for (final c in order)
                if (c == 'sync' || c == 'async') c as String,
            ]
          : const [],
    );
  }

  /// Parses a whole decoded `metrics.json`; null when it has no usable
  /// `embodiment` (an Orchard Reach session).
  static Embodiment? tryParseMetrics(Object? metricsJson) => metricsJson is Map ? tryParse(metricsJson['embodiment']) : null;
}
