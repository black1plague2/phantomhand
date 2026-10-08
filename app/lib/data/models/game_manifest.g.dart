// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'game_manifest.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_GamePreset _$GamePresetFromJson(Map<String, dynamic> json) => _GamePreset(
  id: json['id'] as String,
  label: json['label'] as Map<String, dynamic>,
  params: json['params'] as Map<String, dynamic>,
);

Map<String, dynamic> _$GamePresetToJson(_GamePreset instance) =>
    <String, dynamic>{
      'id': instance.id,
      'label': instance.label,
      'params': instance.params,
    };

_GameManifest _$GameManifestFromJson(
  Map<String, dynamic> json,
) => _GameManifest(
  id: json['id'] as String,
  version: json['version'] as String,
  displayName: json['displayName'] as Map<String, dynamic>,
  paramSchema: json['paramSchema'] as Map<String, dynamic>,
  description: json['description'] as Map<String, dynamic>?,
  input:
      (json['input'] as List<dynamic>?)?.map((e) => e as String).toList() ??
      const [],
  posture: json['posture'] as String?,
  bodyRegions:
      (json['bodyRegions'] as List<dynamic>?)
          ?.map((e) => e as String)
          .toList() ??
      const [],
  presets:
      (json['presets'] as List<dynamic>?)
          ?.map((e) => GamePreset.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  events:
      (json['events'] as List<dynamic>?)?.map((e) => e as String).toList() ??
      const [],
  metrics:
      (json['metrics'] as List<dynamic>?)?.map((e) => e as String).toList() ??
      const [],
);

Map<String, dynamic> _$GameManifestToJson(_GameManifest instance) =>
    <String, dynamic>{
      'id': instance.id,
      'version': instance.version,
      'displayName': instance.displayName,
      'paramSchema': instance.paramSchema,
      'description': instance.description,
      'input': instance.input,
      'posture': instance.posture,
      'bodyRegions': instance.bodyRegions,
      'presets': instance.presets.map((e) => e.toJson()).toList(),
      'events': instance.events,
      'metrics': instance.metrics,
    };
