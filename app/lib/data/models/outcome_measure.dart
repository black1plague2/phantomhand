import 'package:freezed_annotation/freezed_annotation.dart';

part 'outcome_measure.freezed.dart';
part 'outcome_measure.g.dart';

/// The four standardized outcome measures called out in the brief (A6) and
/// ARCHITECTURE.md §6. No `contracts/schemas/` entry exists for these yet
/// (they're clinician-entered, not device telemetry) -- this is an app-only
/// model; flag to Opus if/when a shared schema is wanted for Phase 3 export.
enum OutcomeMeasureType { fmaUe, arat, boxAndBlock, mas }

extension OutcomeMeasureRange on OutcomeMeasureType {
  /// (min, max) for the scale's total score, used to drive the entry form
  /// and the trend chart's y-axis.
  (double, double) get range => switch (this) {
        OutcomeMeasureType.fmaUe => (0, 66),
        OutcomeMeasureType.arat => (0, 57),
        OutcomeMeasureType.boxAndBlock => (0, 150),
        OutcomeMeasureType.mas => (0, 4),
      };

  String get unit => switch (this) {
        OutcomeMeasureType.boxAndBlock => 'blocks/min',
        _ => 'points',
      };
}

@freezed
abstract class OutcomeMeasureEntry with _$OutcomeMeasureEntry {
  const factory({
    required String id,
    required String patientId,
    required OutcomeMeasureType type,
    required DateTime date,
    required double score,
    String? side,
    String? enteredBy,
    String? notes,
  }) = _OutcomeMeasureEntry;

  factory fromJson(Map<String, Object?> json) =>
      _$OutcomeMeasureEntryFromJson(json);
}
