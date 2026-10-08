// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint, type=warning, deprecated_member_use, deprecated_member_use_from_same_package
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'game_manifest.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$GamePreset {

 String get id; Map<String, dynamic> get label; Map<String, dynamic> get params;
/// Create a copy of GamePreset
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$GamePresetCopyWith<GamePreset> get copyWith => _$GamePresetCopyWithImpl<GamePreset>(this as GamePreset, _$identity);

  /// Serializes this GamePreset to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as GamePreset;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is GamePreset&&(identical(other.id, _this.id) || other.id == _this.id)&&const DeepCollectionEquality().equals(other.label, _this.label)&&const DeepCollectionEquality().equals(other.params, _this.params));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as GamePreset;
  return Object.hash(runtimeType,_this.id,const DeepCollectionEquality().hash(_this.label),const DeepCollectionEquality().hash(_this.params));
}

@override
String toString() {
  final _this = this as GamePreset;
  return 'GamePreset(id: ${_this.id}, label: ${_this.label}, params: ${_this.params})';
}


}

/// @nodoc
abstract mixin class $GamePresetCopyWith<$Res>  {
  factory $GamePresetCopyWith(GamePreset value, $Res Function(GamePreset) _then) = _$GamePresetCopyWithImpl;
@useResult
$Res call({
 String id, Map<String, dynamic> label, Map<String, dynamic> params
});




}
/// @nodoc
class _$GamePresetCopyWithImpl<$Res>
    implements $GamePresetCopyWith<$Res> {
  _$GamePresetCopyWithImpl(this._self, this._then);

  final GamePreset _self;
  final $Res Function(GamePreset) _then;

/// Create a copy of GamePreset
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? label = null,Object? params = null,}) {
  return _then(GamePreset(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,label: null == label ? _self.label : label // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,params: null == params ? _self.params : params // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,
  ));
}

}


/// Adds pattern-matching-related methods to [GamePreset].
extension GamePresetPatterns on GamePreset {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _GamePreset value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _GamePreset() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _GamePreset value)  $default,){
final _that = this;
switch (_that) {
case _GamePreset():
return $default(_that);case _:
  throw StateError('Unexpected subclass');

}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _GamePreset value)?  $default,){
final _that = this;
switch (_that) {
case _GamePreset() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String id,  Map<String, dynamic> label,  Map<String, dynamic> params)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _GamePreset() when $default != null:
return $default(_that.id,_that.label,_that.params);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String id,  Map<String, dynamic> label,  Map<String, dynamic> params)  $default,) {final _that = this;
switch (_that) {
case _GamePreset():
return $default(_that.id,_that.label,_that.params);case _:
  throw StateError('Unexpected subclass');

}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String id,  Map<String, dynamic> label,  Map<String, dynamic> params)?  $default,) {final _that = this;
switch (_that) {
case _GamePreset() when $default != null:
return $default(_that.id,_that.label,_that.params);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _GamePreset implements GamePreset {
  const _GamePreset({required this.id, required  Map<String, dynamic> label, required  Map<String, dynamic> params}): _label = label,_params = params;
  factory _GamePreset.fromJson(Map<String, dynamic> json) => _$GamePresetFromJson(json);

@override final  String id;
 final  Map<String, dynamic> _label;
@override Map<String, dynamic> get label {
  if (_label is EqualUnmodifiableMapView) return _label;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_label);
}

 final  Map<String, dynamic> _params;
@override Map<String, dynamic> get params {
  if (_params is EqualUnmodifiableMapView) return _params;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_params);
}


/// Create a copy of GamePreset
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$GamePresetCopyWith<_GamePreset> get copyWith => __$GamePresetCopyWithImpl<_GamePreset>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$GamePresetToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _GamePreset&&(identical(other.id, id) || other.id == id)&&const DeepCollectionEquality().equals(other.label, _label)&&const DeepCollectionEquality().equals(other.params, _params));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,id,const DeepCollectionEquality().hash(_label),const DeepCollectionEquality().hash(_params));
}

@override
String toString() {
    return 'GamePreset(id: $id, label: $label, params: $params)';
}


}

/// @nodoc
abstract mixin class _$GamePresetCopyWith<$Res> implements $GamePresetCopyWith<$Res> {
  factory _$GamePresetCopyWith(_GamePreset value, $Res Function(_GamePreset) _then) = __$GamePresetCopyWithImpl;
@override @useResult
$Res call({
 String id, Map<String, dynamic> label, Map<String, dynamic> params
});




}
/// @nodoc
class __$GamePresetCopyWithImpl<$Res>
    implements _$GamePresetCopyWith<$Res> {
  __$GamePresetCopyWithImpl(this._self, this._then);

  final _GamePreset _self;
  final $Res Function(_GamePreset) _then;

/// Create a copy of GamePreset
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? label = null,Object? params = null,}) {
  return _then(_GamePreset(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,label: null == label ? _self._label : label // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,params: null == params ? _self._params : params // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,
  ));
}


}


/// @nodoc
mixin _$GameManifest {

 String get id; String get version;@JsonKey(name: 'displayName') Map<String, dynamic> get displayName;@JsonKey(name: 'paramSchema') Map<String, dynamic> get paramSchema; Map<String, dynamic>? get description; List<String> get input; String? get posture;@JsonKey(name: 'bodyRegions') List<String> get bodyRegions; List<GamePreset> get presets; List<String> get events; List<String> get metrics;
/// Create a copy of GameManifest
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$GameManifestCopyWith<GameManifest> get copyWith => _$GameManifestCopyWithImpl<GameManifest>(this as GameManifest, _$identity);

  /// Serializes this GameManifest to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as GameManifest;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is GameManifest&&(identical(other.id, _this.id) || other.id == _this.id)&&(identical(other.version, _this.version) || other.version == _this.version)&&const DeepCollectionEquality().equals(other.displayName, _this.displayName)&&const DeepCollectionEquality().equals(other.paramSchema, _this.paramSchema)&&const DeepCollectionEquality().equals(other.description, _this.description)&&const DeepCollectionEquality().equals(other.input, _this.input)&&(identical(other.posture, _this.posture) || other.posture == _this.posture)&&const DeepCollectionEquality().equals(other.bodyRegions, _this.bodyRegions)&&const DeepCollectionEquality().equals(other.presets, _this.presets)&&const DeepCollectionEquality().equals(other.events, _this.events)&&const DeepCollectionEquality().equals(other.metrics, _this.metrics));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as GameManifest;
  return Object.hash(runtimeType,_this.id,_this.version,const DeepCollectionEquality().hash(_this.displayName),const DeepCollectionEquality().hash(_this.paramSchema),const DeepCollectionEquality().hash(_this.description),const DeepCollectionEquality().hash(_this.input),_this.posture,const DeepCollectionEquality().hash(_this.bodyRegions),const DeepCollectionEquality().hash(_this.presets),const DeepCollectionEquality().hash(_this.events),const DeepCollectionEquality().hash(_this.metrics));
}

@override
String toString() {
  final _this = this as GameManifest;
  return 'GameManifest(id: ${_this.id}, version: ${_this.version}, displayName: ${_this.displayName}, paramSchema: ${_this.paramSchema}, description: ${_this.description}, input: ${_this.input}, posture: ${_this.posture}, bodyRegions: ${_this.bodyRegions}, presets: ${_this.presets}, events: ${_this.events}, metrics: ${_this.metrics})';
}


}

/// @nodoc
abstract mixin class $GameManifestCopyWith<$Res>  {
  factory $GameManifestCopyWith(GameManifest value, $Res Function(GameManifest) _then) = _$GameManifestCopyWithImpl;
@useResult
$Res call({
 String id, String version,@JsonKey(name: 'displayName') Map<String, dynamic> displayName,@JsonKey(name: 'paramSchema') Map<String, dynamic> paramSchema, Map<String, dynamic>? description, List<String> input, String? posture,@JsonKey(name: 'bodyRegions') List<String> bodyRegions, List<GamePreset> presets, List<String> events, List<String> metrics
});




}
/// @nodoc
class _$GameManifestCopyWithImpl<$Res>
    implements $GameManifestCopyWith<$Res> {
  _$GameManifestCopyWithImpl(this._self, this._then);

  final GameManifest _self;
  final $Res Function(GameManifest) _then;

/// Create a copy of GameManifest
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? version = null,Object? displayName = null,Object? paramSchema = null,Object? description = freezed,Object? input = null,Object? posture = freezed,Object? bodyRegions = null,Object? presets = null,Object? events = null,Object? metrics = null,}) {
  return _then(GameManifest(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,version: null == version ? _self.version : version // ignore: cast_nullable_to_non_nullable
as String,displayName: null == displayName ? _self.displayName : displayName // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,paramSchema: null == paramSchema ? _self.paramSchema : paramSchema // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,input: null == input ? _self.input : input // ignore: cast_nullable_to_non_nullable
as List<String>,posture: freezed == posture ? _self.posture : posture // ignore: cast_nullable_to_non_nullable
as String?,bodyRegions: null == bodyRegions ? _self.bodyRegions : bodyRegions // ignore: cast_nullable_to_non_nullable
as List<String>,presets: null == presets ? _self.presets : presets // ignore: cast_nullable_to_non_nullable
as List<GamePreset>,events: null == events ? _self.events : events // ignore: cast_nullable_to_non_nullable
as List<String>,metrics: null == metrics ? _self.metrics : metrics // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}

}


/// Adds pattern-matching-related methods to [GameManifest].
extension GameManifestPatterns on GameManifest {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _GameManifest value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _GameManifest() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _GameManifest value)  $default,){
final _that = this;
switch (_that) {
case _GameManifest():
return $default(_that);case _:
  throw StateError('Unexpected subclass');

}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _GameManifest value)?  $default,){
final _that = this;
switch (_that) {
case _GameManifest() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String id,  String version, @JsonKey(name: 'displayName')  Map<String, dynamic> displayName, @JsonKey(name: 'paramSchema')  Map<String, dynamic> paramSchema,  Map<String, dynamic>? description,  List<String> input,  String? posture, @JsonKey(name: 'bodyRegions')  List<String> bodyRegions,  List<GamePreset> presets,  List<String> events,  List<String> metrics)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _GameManifest() when $default != null:
return $default(_that.id,_that.version,_that.displayName,_that.paramSchema,_that.description,_that.input,_that.posture,_that.bodyRegions,_that.presets,_that.events,_that.metrics);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String id,  String version, @JsonKey(name: 'displayName')  Map<String, dynamic> displayName, @JsonKey(name: 'paramSchema')  Map<String, dynamic> paramSchema,  Map<String, dynamic>? description,  List<String> input,  String? posture, @JsonKey(name: 'bodyRegions')  List<String> bodyRegions,  List<GamePreset> presets,  List<String> events,  List<String> metrics)  $default,) {final _that = this;
switch (_that) {
case _GameManifest():
return $default(_that.id,_that.version,_that.displayName,_that.paramSchema,_that.description,_that.input,_that.posture,_that.bodyRegions,_that.presets,_that.events,_that.metrics);case _:
  throw StateError('Unexpected subclass');

}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String id,  String version, @JsonKey(name: 'displayName')  Map<String, dynamic> displayName, @JsonKey(name: 'paramSchema')  Map<String, dynamic> paramSchema,  Map<String, dynamic>? description,  List<String> input,  String? posture, @JsonKey(name: 'bodyRegions')  List<String> bodyRegions,  List<GamePreset> presets,  List<String> events,  List<String> metrics)?  $default,) {final _that = this;
switch (_that) {
case _GameManifest() when $default != null:
return $default(_that.id,_that.version,_that.displayName,_that.paramSchema,_that.description,_that.input,_that.posture,_that.bodyRegions,_that.presets,_that.events,_that.metrics);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _GameManifest extends GameManifest {
  const _GameManifest({required this.id, required this.version, @JsonKey(name: 'displayName') required  Map<String, dynamic> displayName, @JsonKey(name: 'paramSchema') required  Map<String, dynamic> paramSchema,  Map<String, dynamic>? description,  List<String> input = const [], this.posture, @JsonKey(name: 'bodyRegions')  List<String> bodyRegions = const [],  List<GamePreset> presets = const [],  List<String> events = const [],  List<String> metrics = const []}): _displayName = displayName,_paramSchema = paramSchema,_description = description,_input = input,_bodyRegions = bodyRegions,_presets = presets,_events = events,_metrics = metrics,super._();
  factory _GameManifest.fromJson(Map<String, dynamic> json) => _$GameManifestFromJson(json);

@override final  String id;
@override final  String version;
 final  Map<String, dynamic> _displayName;
@override@JsonKey(name: 'displayName') Map<String, dynamic> get displayName {
  if (_displayName is EqualUnmodifiableMapView) return _displayName;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_displayName);
}

 final  Map<String, dynamic> _paramSchema;
@override@JsonKey(name: 'paramSchema') Map<String, dynamic> get paramSchema {
  if (_paramSchema is EqualUnmodifiableMapView) return _paramSchema;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_paramSchema);
}

 final  Map<String, dynamic>? _description;
@override Map<String, dynamic>? get description {
  final value = _description;
  if (value == null) return null;
  if (_description is EqualUnmodifiableMapView) return _description;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(value);
}

 final  List<String> _input;
@override@JsonKey() List<String> get input {
  if (_input is EqualUnmodifiableListView) return _input;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_input);
}

@override final  String? posture;
 final  List<String> _bodyRegions;
@override@JsonKey(name: 'bodyRegions') List<String> get bodyRegions {
  if (_bodyRegions is EqualUnmodifiableListView) return _bodyRegions;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_bodyRegions);
}

 final  List<GamePreset> _presets;
@override@JsonKey() List<GamePreset> get presets {
  if (_presets is EqualUnmodifiableListView) return _presets;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_presets);
}

 final  List<String> _events;
@override@JsonKey() List<String> get events {
  if (_events is EqualUnmodifiableListView) return _events;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_events);
}

 final  List<String> _metrics;
@override@JsonKey() List<String> get metrics {
  if (_metrics is EqualUnmodifiableListView) return _metrics;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_metrics);
}


/// Create a copy of GameManifest
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$GameManifestCopyWith<_GameManifest> get copyWith => __$GameManifestCopyWithImpl<_GameManifest>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$GameManifestToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _GameManifest&&(identical(other.id, id) || other.id == id)&&(identical(other.version, version) || other.version == version)&&const DeepCollectionEquality().equals(other.displayName, _displayName)&&const DeepCollectionEquality().equals(other.paramSchema, _paramSchema)&&const DeepCollectionEquality().equals(other.description, _description)&&const DeepCollectionEquality().equals(other.input, _input)&&(identical(other.posture, posture) || other.posture == posture)&&const DeepCollectionEquality().equals(other.bodyRegions, _bodyRegions)&&const DeepCollectionEquality().equals(other.presets, _presets)&&const DeepCollectionEquality().equals(other.events, _events)&&const DeepCollectionEquality().equals(other.metrics, _metrics));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,id,version,const DeepCollectionEquality().hash(_displayName),const DeepCollectionEquality().hash(_paramSchema),const DeepCollectionEquality().hash(_description),const DeepCollectionEquality().hash(_input),posture,const DeepCollectionEquality().hash(_bodyRegions),const DeepCollectionEquality().hash(_presets),const DeepCollectionEquality().hash(_events),const DeepCollectionEquality().hash(_metrics));
}

@override
String toString() {
    return 'GameManifest(id: $id, version: $version, displayName: $displayName, paramSchema: $paramSchema, description: $description, input: $input, posture: $posture, bodyRegions: $bodyRegions, presets: $presets, events: $events, metrics: $metrics)';
}


}

/// @nodoc
abstract mixin class _$GameManifestCopyWith<$Res> implements $GameManifestCopyWith<$Res> {
  factory _$GameManifestCopyWith(_GameManifest value, $Res Function(_GameManifest) _then) = __$GameManifestCopyWithImpl;
@override @useResult
$Res call({
 String id, String version,@JsonKey(name: 'displayName') Map<String, dynamic> displayName,@JsonKey(name: 'paramSchema') Map<String, dynamic> paramSchema, Map<String, dynamic>? description, List<String> input, String? posture,@JsonKey(name: 'bodyRegions') List<String> bodyRegions, List<GamePreset> presets, List<String> events, List<String> metrics
});




}
/// @nodoc
class __$GameManifestCopyWithImpl<$Res>
    implements _$GameManifestCopyWith<$Res> {
  __$GameManifestCopyWithImpl(this._self, this._then);

  final _GameManifest _self;
  final $Res Function(_GameManifest) _then;

/// Create a copy of GameManifest
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? version = null,Object? displayName = null,Object? paramSchema = null,Object? description = freezed,Object? input = null,Object? posture = freezed,Object? bodyRegions = null,Object? presets = null,Object? events = null,Object? metrics = null,}) {
  return _then(_GameManifest(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,version: null == version ? _self.version : version // ignore: cast_nullable_to_non_nullable
as String,displayName: null == displayName ? _self._displayName : displayName // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,paramSchema: null == paramSchema ? _self._paramSchema : paramSchema // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,description: freezed == description ? _self._description : description // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,input: null == input ? _self._input : input // ignore: cast_nullable_to_non_nullable
as List<String>,posture: freezed == posture ? _self.posture : posture // ignore: cast_nullable_to_non_nullable
as String?,bodyRegions: null == bodyRegions ? _self._bodyRegions : bodyRegions // ignore: cast_nullable_to_non_nullable
as List<String>,presets: null == presets ? _self._presets : presets // ignore: cast_nullable_to_non_nullable
as List<GamePreset>,events: null == events ? _self._events : events // ignore: cast_nullable_to_non_nullable
as List<String>,metrics: null == metrics ? _self._metrics : metrics // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}


}

// dart format on
