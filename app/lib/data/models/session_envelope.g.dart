// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'session_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SessionDevice _$SessionDeviceFromJson(Map<String, dynamic> json) =>
    _SessionDevice(
      model: json['model'] as String,
      deviceId: json['device_id'] as String?,
      os: json['os'] as String?,
      trackingRateHz: (json['tracking_rate_hz'] as num?)?.toDouble(),
    );

Map<String, dynamic> _$SessionDeviceToJson(_SessionDevice instance) =>
    <String, dynamic>{
      'model': instance.model,
      'device_id': instance.deviceId,
      'os': instance.os,
      'tracking_rate_hz': instance.trackingRateHz,
    };

_SessionCalibration _$SessionCalibrationFromJson(Map<String, dynamic> json) =>
    _SessionCalibration(
      affectedSide: json['affected_side'] as String,
      dominantSide: json['dominant_side'] as String?,
      armLengthM: json['arm_length_m'] as Map<String, dynamic>?,
      posture: json['posture'] as String?,
    );

Map<String, dynamic> _$SessionCalibrationToJson(_SessionCalibration instance) =>
    <String, dynamic>{
      'affected_side': instance.affectedSide,
      'dominant_side': instance.dominantSide,
      'arm_length_m': instance.armLengthM,
      'posture': instance.posture,
    };

_SessionBlockRecord _$SessionBlockRecordFromJson(Map<String, dynamic> json) =>
    _SessionBlockRecord(
      index: (json['index'] as num).toInt(),
      gameId: json['game_id'] as String,
      gameVersion: json['game_version'] as String,
      params: json['params'] as Map<String, dynamic>,
      startedTMs: (json['started_t_ms'] as num?)?.toDouble(),
      endedTMs: (json['ended_t_ms'] as num?)?.toDouble(),
      completed: json['completed'] as bool? ?? false,
    );

Map<String, dynamic> _$SessionBlockRecordToJson(_SessionBlockRecord instance) =>
    <String, dynamic>{
      'index': instance.index,
      'game_id': instance.gameId,
      'game_version': instance.gameVersion,
      'params': instance.params,
      'started_t_ms': instance.startedTMs,
      'ended_t_ms': instance.endedTMs,
      'completed': instance.completed,
    };

_SessionEnvelope _$SessionEnvelopeFromJson(Map<String, dynamic> json) =>
    _SessionEnvelope(
      sessionId: json['session_id'] as String,
      contractsVersion: json['contracts_version'] as String,
      patientRef: json['patient_ref'] as String,
      programRef: json['program_ref'] as String,
      startedAt: DateTime.parse(json['started_at'] as String),
      device: SessionDevice.fromJson(json['device'] as Map<String, dynamic>),
      calibration: SessionCalibration.fromJson(
        json['calibration'] as Map<String, dynamic>,
      ),
      blocks: (json['blocks'] as List<dynamic>)
          .map((e) => SessionBlockRecord.fromJson(e as Map<String, dynamic>))
          .toList(),
      mode: $enumDecodeNullable(_$SessionModeEnumMap, json['mode']),
      endedAt: json['ended_at'] == null
          ? null
          : DateTime.parse(json['ended_at'] as String),
      endReason: json['end_reason'] as String?,
      patientReported: json['patient_reported'] as Map<String, dynamic>?,
    );

Map<String, dynamic> _$SessionEnvelopeToJson(_SessionEnvelope instance) =>
    <String, dynamic>{
      'session_id': instance.sessionId,
      'contracts_version': instance.contractsVersion,
      'patient_ref': instance.patientRef,
      'program_ref': instance.programRef,
      'started_at': instance.startedAt.toIso8601String(),
      'device': instance.device.toJson(),
      'calibration': instance.calibration.toJson(),
      'blocks': instance.blocks.map((e) => e.toJson()).toList(),
      'mode': _$SessionModeEnumMap[instance.mode],
      'ended_at': instance.endedAt?.toIso8601String(),
      'end_reason': instance.endReason,
      'patient_reported': instance.patientReported,
    };

const _$SessionModeEnumMap = {
  SessionMode.clinic: 'clinic',
  SessionMode.home: 'home',
  SessionMode.standalone: 'standalone',
  SessionMode.simulation: 'simulation',
};
