import 'package:freezed_annotation/freezed_annotation.dart';

part 'game_manifest.freezed.dart';
part 'game_manifest.g.dart';

/// One entry of `manifest.presets[]` (contracts/schemas/game-manifest.schema.json).
/// `label` and `params` are left as raw JSON: `label` is a locale map
/// (`{"en": "...", "hi": "..."}`), `params` is a partial param set matching
/// whatever shape `paramSchema` declares for this game -- there is nothing
/// game-specific to type here by design (ARCHITECTURE.md §2).
// Manifests (contracts/schemas/game-manifest.schema.json) use camelCase keys,
// unlike most other contracts which are snake_case. The project-wide `build.yaml`
// default (`field_rename: snake`) only rewrites multi-word field names, so the
// single-word fields below (id, label, params, version, posture, presets,
// events, metrics) are unaffected; the three multi-word fields get an explicit
// `@JsonKey(name: ...)` to keep them camelCase instead of being snake_cased.
@freezed
abstract class GamePreset with _$GamePreset {
  const factory({
    required String id,
    required Map<String, dynamic> label,
    required Map<String, dynamic> params,
  }) = _GamePreset;

  factory fromJson(Map<String, Object?> json) => _$GamePresetFromJson(json);
}

/// A game's manifest, exactly as declared by `contracts/schemas/game-manifest.schema.json`.
/// `paramSchema` is kept as a raw JSON Schema map -- see `shared/widgets/dynamic_form/`
/// for the engine that renders ANY such schema via its `x-ui` hints. This class
/// deliberately has zero knowledge of `orchard_reach` or any other specific game.
@freezed
abstract class GameManifest with _$GameManifest {
  const factory({
    required String id,
    required String version,
    @JsonKey(name: 'displayName') required Map<String, dynamic> displayName,
    @JsonKey(name: 'paramSchema') required Map<String, dynamic> paramSchema, Map<String, dynamic>? description,
    @Default([]) List<String> input,
    String? posture,
    @JsonKey(name: 'bodyRegions') @Default([]) List<String> bodyRegions,
    @Default([]) List<GamePreset> presets,
    @Default([]) List<String> events,
    @Default([]) List<String> metrics,
  }) = _GameManifest;

  factory fromJson(Map<String, Object?> json) => _$GameManifestFromJson(json);

  const new _();

  /// Best-effort localized display name, falling back to English then the
  /// first available value then the id.
  String nameFor(String localeCode) =>
      (displayName[localeCode] as String?) ??
      (displayName['en'] as String?) ??
      (displayName.values.isNotEmpty ? displayName.values.first.toString() : id);
}
