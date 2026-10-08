import 'package:freezed_annotation/freezed_annotation.dart';

part 'session_envelope.freezed.dart';
part 'session_envelope.g.dart';

enum SessionMode { clinic, home, standalone, simulation }

@freezed
abstract class SessionDevice with _$SessionDevice {
  const factory({
    required String model,
    String? deviceId,
    String? os,
    double? trackingRateHz,
  }) = _SessionDevice;

  factory fromJson(Map<String, Object?> json) => _$SessionDeviceFromJson(json);
}

@freezed
abstract class SessionCalibration with _$SessionCalibration {
  const factory({
    required String affectedSide,
    String? dominantSide,
    Map<String, dynamic>? armLengthM,
    String? posture,
  }) = _SessionCalibration;

  factory fromJson(Map<String, Object?> json) =>
      _$SessionCalibrationFromJson(json);
}

@freezed
abstract class SessionBlockRecord with _$SessionBlockRecord {
  const factory({
    required int index,
    required String gameId,
    required String gameVersion,
    required Map<String, dynamic> params,
    double? startedTMs,
    double? endedTMs,
    @Default(false) bool completed,
  }) = _SessionBlockRecord;

  factory fromJson(Map<String, Object?> json) =>
      _$SessionBlockRecordFromJson(json);
}

/// `session.json` as written by the shell (`contracts/schemas/session-envelope.schema.json`).
/// This is metadata only -- trial-level detail lives in `events.ndjson`
/// (read as raw JSON lines by the sessions feature, see `EventsRepository`)
/// and computed metrics live in `SessionMetrics` (`metrics.dart`).
@freezed
abstract class SessionEnvelope with _$SessionEnvelope {
  const factory({
    required String sessionId,
    required String contractsVersion,
    required String patientRef,
    required String programRef,
    required DateTime startedAt, required SessionDevice device, required SessionCalibration calibration, required List<SessionBlockRecord> blocks, SessionMode? mode,
    DateTime? endedAt,
    String? endReason,
    Map<String, dynamic>? patientReported,
  }) = _SessionEnvelope;

  factory fromJson(Map<String, Object?> json) =>
      _$SessionEnvelopeFromJson(json);
}
