// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint, type=warning, deprecated_member_use, deprecated_member_use_from_same_package
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'session_envelope.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$SessionDevice {

 String get model; String? get deviceId; String? get os; double? get trackingRateHz;
/// Create a copy of SessionDevice
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SessionDeviceCopyWith<SessionDevice> get copyWith => _$SessionDeviceCopyWithImpl<SessionDevice>(this as SessionDevice, _$identity);

  /// Serializes this SessionDevice to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as SessionDevice;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SessionDevice&&(identical(other.model, _this.model) || other.model == _this.model)&&(identical(other.deviceId, _this.deviceId) || other.deviceId == _this.deviceId)&&(identical(other.os, _this.os) || other.os == _this.os)&&(identical(other.trackingRateHz, _this.trackingRateHz) || other.trackingRateHz == _this.trackingRateHz));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as SessionDevice;
  return Object.hash(runtimeType,_this.model,_this.deviceId,_this.os,_this.trackingRateHz);
}

@override
String toString() {
  final _this = this as SessionDevice;
  return 'SessionDevice(model: ${_this.model}, deviceId: ${_this.deviceId}, os: ${_this.os}, trackingRateHz: ${_this.trackingRateHz})';
}


}

/// @nodoc
abstract mixin class $SessionDeviceCopyWith<$Res>  {
  factory $SessionDeviceCopyWith(SessionDevice value, $Res Function(SessionDevice) _then) = _$SessionDeviceCopyWithImpl;
@useResult
$Res call({
 String model, String? deviceId, String? os, double? trackingRateHz
});




}
/// @nodoc
class _$SessionDeviceCopyWithImpl<$Res>
    implements $SessionDeviceCopyWith<$Res> {
  _$SessionDeviceCopyWithImpl(this._self, this._then);

  final SessionDevice _self;
  final $Res Function(SessionDevice) _then;

/// Create a copy of SessionDevice
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? model = null,Object? deviceId = freezed,Object? os = freezed,Object? trackingRateHz = freezed,}) {
  return _then(SessionDevice(
model: null == model ? _self.model : model // ignore: cast_nullable_to_non_nullable
as String,deviceId: freezed == deviceId ? _self.deviceId : deviceId // ignore: cast_nullable_to_non_nullable
as String?,os: freezed == os ? _self.os : os // ignore: cast_nullable_to_non_nullable
as String?,trackingRateHz: freezed == trackingRateHz ? _self.trackingRateHz : trackingRateHz // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}

}


/// Adds pattern-matching-related methods to [SessionDevice].
extension SessionDevicePatterns on SessionDevice {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SessionDevice value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SessionDevice() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SessionDevice value)  $default,){
final _that = this;
switch (_that) {
case _SessionDevice():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SessionDevice value)?  $default,){
final _that = this;
switch (_that) {
case _SessionDevice() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String model,  String? deviceId,  String? os,  double? trackingRateHz)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SessionDevice() when $default != null:
return $default(_that.model,_that.deviceId,_that.os,_that.trackingRateHz);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String model,  String? deviceId,  String? os,  double? trackingRateHz)  $default,) {final _that = this;
switch (_that) {
case _SessionDevice():
return $default(_that.model,_that.deviceId,_that.os,_that.trackingRateHz);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String model,  String? deviceId,  String? os,  double? trackingRateHz)?  $default,) {final _that = this;
switch (_that) {
case _SessionDevice() when $default != null:
return $default(_that.model,_that.deviceId,_that.os,_that.trackingRateHz);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SessionDevice implements SessionDevice {
  const _SessionDevice({required this.model, this.deviceId, this.os, this.trackingRateHz});
  factory _SessionDevice.fromJson(Map<String, dynamic> json) => _$SessionDeviceFromJson(json);

@override final  String model;
@override final  String? deviceId;
@override final  String? os;
@override final  double? trackingRateHz;

/// Create a copy of SessionDevice
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SessionDeviceCopyWith<_SessionDevice> get copyWith => __$SessionDeviceCopyWithImpl<_SessionDevice>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SessionDeviceToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _SessionDevice&&(identical(other.model, model) || other.model == model)&&(identical(other.deviceId, deviceId) || other.deviceId == deviceId)&&(identical(other.os, os) || other.os == os)&&(identical(other.trackingRateHz, trackingRateHz) || other.trackingRateHz == trackingRateHz));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,model,deviceId,os,trackingRateHz);
}

@override
String toString() {
    return 'SessionDevice(model: $model, deviceId: $deviceId, os: $os, trackingRateHz: $trackingRateHz)';
}


}

/// @nodoc
abstract mixin class _$SessionDeviceCopyWith<$Res> implements $SessionDeviceCopyWith<$Res> {
  factory _$SessionDeviceCopyWith(_SessionDevice value, $Res Function(_SessionDevice) _then) = __$SessionDeviceCopyWithImpl;
@override @useResult
$Res call({
 String model, String? deviceId, String? os, double? trackingRateHz
});




}
/// @nodoc
class __$SessionDeviceCopyWithImpl<$Res>
    implements _$SessionDeviceCopyWith<$Res> {
  __$SessionDeviceCopyWithImpl(this._self, this._then);

  final _SessionDevice _self;
  final $Res Function(_SessionDevice) _then;

/// Create a copy of SessionDevice
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? model = null,Object? deviceId = freezed,Object? os = freezed,Object? trackingRateHz = freezed,}) {
  return _then(_SessionDevice(
model: null == model ? _self.model : model // ignore: cast_nullable_to_non_nullable
as String,deviceId: freezed == deviceId ? _self.deviceId : deviceId // ignore: cast_nullable_to_non_nullable
as String?,os: freezed == os ? _self.os : os // ignore: cast_nullable_to_non_nullable
as String?,trackingRateHz: freezed == trackingRateHz ? _self.trackingRateHz : trackingRateHz // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}


}


/// @nodoc
mixin _$SessionCalibration {

 String get affectedSide; String? get dominantSide; Map<String, dynamic>? get armLengthM; String? get posture;
/// Create a copy of SessionCalibration
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SessionCalibrationCopyWith<SessionCalibration> get copyWith => _$SessionCalibrationCopyWithImpl<SessionCalibration>(this as SessionCalibration, _$identity);

  /// Serializes this SessionCalibration to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as SessionCalibration;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SessionCalibration&&(identical(other.affectedSide, _this.affectedSide) || other.affectedSide == _this.affectedSide)&&(identical(other.dominantSide, _this.dominantSide) || other.dominantSide == _this.dominantSide)&&const DeepCollectionEquality().equals(other.armLengthM, _this.armLengthM)&&(identical(other.posture, _this.posture) || other.posture == _this.posture));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as SessionCalibration;
  return Object.hash(runtimeType,_this.affectedSide,_this.dominantSide,const DeepCollectionEquality().hash(_this.armLengthM),_this.posture);
}

@override
String toString() {
  final _this = this as SessionCalibration;
  return 'SessionCalibration(affectedSide: ${_this.affectedSide}, dominantSide: ${_this.dominantSide}, armLengthM: ${_this.armLengthM}, posture: ${_this.posture})';
}


}

/// @nodoc
abstract mixin class $SessionCalibrationCopyWith<$Res>  {
  factory $SessionCalibrationCopyWith(SessionCalibration value, $Res Function(SessionCalibration) _then) = _$SessionCalibrationCopyWithImpl;
@useResult
$Res call({
 String affectedSide, String? dominantSide, Map<String, dynamic>? armLengthM, String? posture
});




}
/// @nodoc
class _$SessionCalibrationCopyWithImpl<$Res>
    implements $SessionCalibrationCopyWith<$Res> {
  _$SessionCalibrationCopyWithImpl(this._self, this._then);

  final SessionCalibration _self;
  final $Res Function(SessionCalibration) _then;

/// Create a copy of SessionCalibration
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? affectedSide = null,Object? dominantSide = freezed,Object? armLengthM = freezed,Object? posture = freezed,}) {
  return _then(SessionCalibration(
affectedSide: null == affectedSide ? _self.affectedSide : affectedSide // ignore: cast_nullable_to_non_nullable
as String,dominantSide: freezed == dominantSide ? _self.dominantSide : dominantSide // ignore: cast_nullable_to_non_nullable
as String?,armLengthM: freezed == armLengthM ? _self.armLengthM : armLengthM // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,posture: freezed == posture ? _self.posture : posture // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [SessionCalibration].
extension SessionCalibrationPatterns on SessionCalibration {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SessionCalibration value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SessionCalibration() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SessionCalibration value)  $default,){
final _that = this;
switch (_that) {
case _SessionCalibration():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SessionCalibration value)?  $default,){
final _that = this;
switch (_that) {
case _SessionCalibration() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String affectedSide,  String? dominantSide,  Map<String, dynamic>? armLengthM,  String? posture)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SessionCalibration() when $default != null:
return $default(_that.affectedSide,_that.dominantSide,_that.armLengthM,_that.posture);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String affectedSide,  String? dominantSide,  Map<String, dynamic>? armLengthM,  String? posture)  $default,) {final _that = this;
switch (_that) {
case _SessionCalibration():
return $default(_that.affectedSide,_that.dominantSide,_that.armLengthM,_that.posture);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String affectedSide,  String? dominantSide,  Map<String, dynamic>? armLengthM,  String? posture)?  $default,) {final _that = this;
switch (_that) {
case _SessionCalibration() when $default != null:
return $default(_that.affectedSide,_that.dominantSide,_that.armLengthM,_that.posture);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SessionCalibration implements SessionCalibration {
  const _SessionCalibration({required this.affectedSide, this.dominantSide,  Map<String, dynamic>? armLengthM, this.posture}): _armLengthM = armLengthM;
  factory _SessionCalibration.fromJson(Map<String, dynamic> json) => _$SessionCalibrationFromJson(json);

@override final  String affectedSide;
@override final  String? dominantSide;
 final  Map<String, dynamic>? _armLengthM;
@override Map<String, dynamic>? get armLengthM {
  final value = _armLengthM;
  if (value == null) return null;
  if (_armLengthM is EqualUnmodifiableMapView) return _armLengthM;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(value);
}

@override final  String? posture;

/// Create a copy of SessionCalibration
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SessionCalibrationCopyWith<_SessionCalibration> get copyWith => __$SessionCalibrationCopyWithImpl<_SessionCalibration>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SessionCalibrationToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _SessionCalibration&&(identical(other.affectedSide, affectedSide) || other.affectedSide == affectedSide)&&(identical(other.dominantSide, dominantSide) || other.dominantSide == dominantSide)&&const DeepCollectionEquality().equals(other.armLengthM, _armLengthM)&&(identical(other.posture, posture) || other.posture == posture));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,affectedSide,dominantSide,const DeepCollectionEquality().hash(_armLengthM),posture);
}

@override
String toString() {
    return 'SessionCalibration(affectedSide: $affectedSide, dominantSide: $dominantSide, armLengthM: $armLengthM, posture: $posture)';
}


}

/// @nodoc
abstract mixin class _$SessionCalibrationCopyWith<$Res> implements $SessionCalibrationCopyWith<$Res> {
  factory _$SessionCalibrationCopyWith(_SessionCalibration value, $Res Function(_SessionCalibration) _then) = __$SessionCalibrationCopyWithImpl;
@override @useResult
$Res call({
 String affectedSide, String? dominantSide, Map<String, dynamic>? armLengthM, String? posture
});




}
/// @nodoc
class __$SessionCalibrationCopyWithImpl<$Res>
    implements _$SessionCalibrationCopyWith<$Res> {
  __$SessionCalibrationCopyWithImpl(this._self, this._then);

  final _SessionCalibration _self;
  final $Res Function(_SessionCalibration) _then;

/// Create a copy of SessionCalibration
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? affectedSide = null,Object? dominantSide = freezed,Object? armLengthM = freezed,Object? posture = freezed,}) {
  return _then(_SessionCalibration(
affectedSide: null == affectedSide ? _self.affectedSide : affectedSide // ignore: cast_nullable_to_non_nullable
as String,dominantSide: freezed == dominantSide ? _self.dominantSide : dominantSide // ignore: cast_nullable_to_non_nullable
as String?,armLengthM: freezed == armLengthM ? _self._armLengthM : armLengthM // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,posture: freezed == posture ? _self.posture : posture // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}


/// @nodoc
mixin _$SessionBlockRecord {

 int get index; String get gameId; String get gameVersion; Map<String, dynamic> get params; double? get startedTMs; double? get endedTMs; bool get completed;
/// Create a copy of SessionBlockRecord
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SessionBlockRecordCopyWith<SessionBlockRecord> get copyWith => _$SessionBlockRecordCopyWithImpl<SessionBlockRecord>(this as SessionBlockRecord, _$identity);

  /// Serializes this SessionBlockRecord to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as SessionBlockRecord;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SessionBlockRecord&&(identical(other.index, _this.index) || other.index == _this.index)&&(identical(other.gameId, _this.gameId) || other.gameId == _this.gameId)&&(identical(other.gameVersion, _this.gameVersion) || other.gameVersion == _this.gameVersion)&&const DeepCollectionEquality().equals(other.params, _this.params)&&(identical(other.startedTMs, _this.startedTMs) || other.startedTMs == _this.startedTMs)&&(identical(other.endedTMs, _this.endedTMs) || other.endedTMs == _this.endedTMs)&&(identical(other.completed, _this.completed) || other.completed == _this.completed));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as SessionBlockRecord;
  return Object.hash(runtimeType,_this.index,_this.gameId,_this.gameVersion,const DeepCollectionEquality().hash(_this.params),_this.startedTMs,_this.endedTMs,_this.completed);
}

@override
String toString() {
  final _this = this as SessionBlockRecord;
  return 'SessionBlockRecord(index: ${_this.index}, gameId: ${_this.gameId}, gameVersion: ${_this.gameVersion}, params: ${_this.params}, startedTMs: ${_this.startedTMs}, endedTMs: ${_this.endedTMs}, completed: ${_this.completed})';
}


}

/// @nodoc
abstract mixin class $SessionBlockRecordCopyWith<$Res>  {
  factory $SessionBlockRecordCopyWith(SessionBlockRecord value, $Res Function(SessionBlockRecord) _then) = _$SessionBlockRecordCopyWithImpl;
@useResult
$Res call({
 int index, String gameId, String gameVersion, Map<String, dynamic> params, double? startedTMs, double? endedTMs, bool completed
});




}
/// @nodoc
class _$SessionBlockRecordCopyWithImpl<$Res>
    implements $SessionBlockRecordCopyWith<$Res> {
  _$SessionBlockRecordCopyWithImpl(this._self, this._then);

  final SessionBlockRecord _self;
  final $Res Function(SessionBlockRecord) _then;

/// Create a copy of SessionBlockRecord
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? index = null,Object? gameId = null,Object? gameVersion = null,Object? params = null,Object? startedTMs = freezed,Object? endedTMs = freezed,Object? completed = null,}) {
  return _then(SessionBlockRecord(
index: null == index ? _self.index : index // ignore: cast_nullable_to_non_nullable
as int,gameId: null == gameId ? _self.gameId : gameId // ignore: cast_nullable_to_non_nullable
as String,gameVersion: null == gameVersion ? _self.gameVersion : gameVersion // ignore: cast_nullable_to_non_nullable
as String,params: null == params ? _self.params : params // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,startedTMs: freezed == startedTMs ? _self.startedTMs : startedTMs // ignore: cast_nullable_to_non_nullable
as double?,endedTMs: freezed == endedTMs ? _self.endedTMs : endedTMs // ignore: cast_nullable_to_non_nullable
as double?,completed: null == completed ? _self.completed : completed // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [SessionBlockRecord].
extension SessionBlockRecordPatterns on SessionBlockRecord {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SessionBlockRecord value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SessionBlockRecord() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SessionBlockRecord value)  $default,){
final _that = this;
switch (_that) {
case _SessionBlockRecord():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SessionBlockRecord value)?  $default,){
final _that = this;
switch (_that) {
case _SessionBlockRecord() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( int index,  String gameId,  String gameVersion,  Map<String, dynamic> params,  double? startedTMs,  double? endedTMs,  bool completed)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SessionBlockRecord() when $default != null:
return $default(_that.index,_that.gameId,_that.gameVersion,_that.params,_that.startedTMs,_that.endedTMs,_that.completed);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( int index,  String gameId,  String gameVersion,  Map<String, dynamic> params,  double? startedTMs,  double? endedTMs,  bool completed)  $default,) {final _that = this;
switch (_that) {
case _SessionBlockRecord():
return $default(_that.index,_that.gameId,_that.gameVersion,_that.params,_that.startedTMs,_that.endedTMs,_that.completed);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( int index,  String gameId,  String gameVersion,  Map<String, dynamic> params,  double? startedTMs,  double? endedTMs,  bool completed)?  $default,) {final _that = this;
switch (_that) {
case _SessionBlockRecord() when $default != null:
return $default(_that.index,_that.gameId,_that.gameVersion,_that.params,_that.startedTMs,_that.endedTMs,_that.completed);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SessionBlockRecord implements SessionBlockRecord {
  const _SessionBlockRecord({required this.index, required this.gameId, required this.gameVersion, required  Map<String, dynamic> params, this.startedTMs, this.endedTMs, this.completed = false}): _params = params;
  factory _SessionBlockRecord.fromJson(Map<String, dynamic> json) => _$SessionBlockRecordFromJson(json);

@override final  int index;
@override final  String gameId;
@override final  String gameVersion;
 final  Map<String, dynamic> _params;
@override Map<String, dynamic> get params {
  if (_params is EqualUnmodifiableMapView) return _params;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_params);
}

@override final  double? startedTMs;
@override final  double? endedTMs;
@override@JsonKey() final  bool completed;

/// Create a copy of SessionBlockRecord
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SessionBlockRecordCopyWith<_SessionBlockRecord> get copyWith => __$SessionBlockRecordCopyWithImpl<_SessionBlockRecord>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SessionBlockRecordToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _SessionBlockRecord&&(identical(other.index, index) || other.index == index)&&(identical(other.gameId, gameId) || other.gameId == gameId)&&(identical(other.gameVersion, gameVersion) || other.gameVersion == gameVersion)&&const DeepCollectionEquality().equals(other.params, _params)&&(identical(other.startedTMs, startedTMs) || other.startedTMs == startedTMs)&&(identical(other.endedTMs, endedTMs) || other.endedTMs == endedTMs)&&(identical(other.completed, completed) || other.completed == completed));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,index,gameId,gameVersion,const DeepCollectionEquality().hash(_params),startedTMs,endedTMs,completed);
}

@override
String toString() {
    return 'SessionBlockRecord(index: $index, gameId: $gameId, gameVersion: $gameVersion, params: $params, startedTMs: $startedTMs, endedTMs: $endedTMs, completed: $completed)';
}


}

/// @nodoc
abstract mixin class _$SessionBlockRecordCopyWith<$Res> implements $SessionBlockRecordCopyWith<$Res> {
  factory _$SessionBlockRecordCopyWith(_SessionBlockRecord value, $Res Function(_SessionBlockRecord) _then) = __$SessionBlockRecordCopyWithImpl;
@override @useResult
$Res call({
 int index, String gameId, String gameVersion, Map<String, dynamic> params, double? startedTMs, double? endedTMs, bool completed
});




}
/// @nodoc
class __$SessionBlockRecordCopyWithImpl<$Res>
    implements _$SessionBlockRecordCopyWith<$Res> {
  __$SessionBlockRecordCopyWithImpl(this._self, this._then);

  final _SessionBlockRecord _self;
  final $Res Function(_SessionBlockRecord) _then;

/// Create a copy of SessionBlockRecord
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? index = null,Object? gameId = null,Object? gameVersion = null,Object? params = null,Object? startedTMs = freezed,Object? endedTMs = freezed,Object? completed = null,}) {
  return _then(_SessionBlockRecord(
index: null == index ? _self.index : index // ignore: cast_nullable_to_non_nullable
as int,gameId: null == gameId ? _self.gameId : gameId // ignore: cast_nullable_to_non_nullable
as String,gameVersion: null == gameVersion ? _self.gameVersion : gameVersion // ignore: cast_nullable_to_non_nullable
as String,params: null == params ? _self._params : params // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,startedTMs: freezed == startedTMs ? _self.startedTMs : startedTMs // ignore: cast_nullable_to_non_nullable
as double?,endedTMs: freezed == endedTMs ? _self.endedTMs : endedTMs // ignore: cast_nullable_to_non_nullable
as double?,completed: null == completed ? _self.completed : completed // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}


/// @nodoc
mixin _$SessionEnvelope {

 String get sessionId; String get contractsVersion; String get patientRef; String get programRef; DateTime get startedAt; SessionDevice get device; SessionCalibration get calibration; List<SessionBlockRecord> get blocks; SessionMode? get mode; DateTime? get endedAt; String? get endReason; Map<String, dynamic>? get patientReported;
/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SessionEnvelopeCopyWith<SessionEnvelope> get copyWith => _$SessionEnvelopeCopyWithImpl<SessionEnvelope>(this as SessionEnvelope, _$identity);

  /// Serializes this SessionEnvelope to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as SessionEnvelope;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SessionEnvelope&&(identical(other.sessionId, _this.sessionId) || other.sessionId == _this.sessionId)&&(identical(other.contractsVersion, _this.contractsVersion) || other.contractsVersion == _this.contractsVersion)&&(identical(other.patientRef, _this.patientRef) || other.patientRef == _this.patientRef)&&(identical(other.programRef, _this.programRef) || other.programRef == _this.programRef)&&(identical(other.startedAt, _this.startedAt) || other.startedAt == _this.startedAt)&&(identical(other.device, _this.device) || other.device == _this.device)&&(identical(other.calibration, _this.calibration) || other.calibration == _this.calibration)&&const DeepCollectionEquality().equals(other.blocks, _this.blocks)&&(identical(other.mode, _this.mode) || other.mode == _this.mode)&&(identical(other.endedAt, _this.endedAt) || other.endedAt == _this.endedAt)&&(identical(other.endReason, _this.endReason) || other.endReason == _this.endReason)&&const DeepCollectionEquality().equals(other.patientReported, _this.patientReported));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as SessionEnvelope;
  return Object.hash(runtimeType,_this.sessionId,_this.contractsVersion,_this.patientRef,_this.programRef,_this.startedAt,_this.device,_this.calibration,const DeepCollectionEquality().hash(_this.blocks),_this.mode,_this.endedAt,_this.endReason,const DeepCollectionEquality().hash(_this.patientReported));
}

@override
String toString() {
  final _this = this as SessionEnvelope;
  return 'SessionEnvelope(sessionId: ${_this.sessionId}, contractsVersion: ${_this.contractsVersion}, patientRef: ${_this.patientRef}, programRef: ${_this.programRef}, startedAt: ${_this.startedAt}, device: ${_this.device}, calibration: ${_this.calibration}, blocks: ${_this.blocks}, mode: ${_this.mode}, endedAt: ${_this.endedAt}, endReason: ${_this.endReason}, patientReported: ${_this.patientReported})';
}


}

/// @nodoc
abstract mixin class $SessionEnvelopeCopyWith<$Res>  {
  factory $SessionEnvelopeCopyWith(SessionEnvelope value, $Res Function(SessionEnvelope) _then) = _$SessionEnvelopeCopyWithImpl;
@useResult
$Res call({
 String sessionId, String contractsVersion, String patientRef, String programRef, DateTime startedAt, SessionDevice device, SessionCalibration calibration, List<SessionBlockRecord> blocks, SessionMode? mode, DateTime? endedAt, String? endReason, Map<String, dynamic>? patientReported
});


$SessionDeviceCopyWith<$Res> get device;$SessionCalibrationCopyWith<$Res> get calibration;

}
/// @nodoc
class _$SessionEnvelopeCopyWithImpl<$Res>
    implements $SessionEnvelopeCopyWith<$Res> {
  _$SessionEnvelopeCopyWithImpl(this._self, this._then);

  final SessionEnvelope _self;
  final $Res Function(SessionEnvelope) _then;

/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? sessionId = null,Object? contractsVersion = null,Object? patientRef = null,Object? programRef = null,Object? startedAt = null,Object? device = null,Object? calibration = null,Object? blocks = null,Object? mode = freezed,Object? endedAt = freezed,Object? endReason = freezed,Object? patientReported = freezed,}) {
  return _then(SessionEnvelope(
sessionId: null == sessionId ? _self.sessionId : sessionId // ignore: cast_nullable_to_non_nullable
as String,contractsVersion: null == contractsVersion ? _self.contractsVersion : contractsVersion // ignore: cast_nullable_to_non_nullable
as String,patientRef: null == patientRef ? _self.patientRef : patientRef // ignore: cast_nullable_to_non_nullable
as String,programRef: null == programRef ? _self.programRef : programRef // ignore: cast_nullable_to_non_nullable
as String,startedAt: null == startedAt ? _self.startedAt : startedAt // ignore: cast_nullable_to_non_nullable
as DateTime,device: null == device ? _self.device : device // ignore: cast_nullable_to_non_nullable
as SessionDevice,calibration: null == calibration ? _self.calibration : calibration // ignore: cast_nullable_to_non_nullable
as SessionCalibration,blocks: null == blocks ? _self.blocks : blocks // ignore: cast_nullable_to_non_nullable
as List<SessionBlockRecord>,mode: freezed == mode ? _self.mode : mode // ignore: cast_nullable_to_non_nullable
as SessionMode?,endedAt: freezed == endedAt ? _self.endedAt : endedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,endReason: freezed == endReason ? _self.endReason : endReason // ignore: cast_nullable_to_non_nullable
as String?,patientReported: freezed == patientReported ? _self.patientReported : patientReported // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,
  ));
}
/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SessionDeviceCopyWith<$Res> get device {
  
  return $SessionDeviceCopyWith<$Res>(_self.device, (value) {
    return _then(_self.copyWith(device: value));
  });
}/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SessionCalibrationCopyWith<$Res> get calibration {
  
  return $SessionCalibrationCopyWith<$Res>(_self.calibration, (value) {
    return _then(_self.copyWith(calibration: value));
  });
}
}


/// Adds pattern-matching-related methods to [SessionEnvelope].
extension SessionEnvelopePatterns on SessionEnvelope {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SessionEnvelope value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SessionEnvelope() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SessionEnvelope value)  $default,){
final _that = this;
switch (_that) {
case _SessionEnvelope():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SessionEnvelope value)?  $default,){
final _that = this;
switch (_that) {
case _SessionEnvelope() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String sessionId,  String contractsVersion,  String patientRef,  String programRef,  DateTime startedAt,  SessionDevice device,  SessionCalibration calibration,  List<SessionBlockRecord> blocks,  SessionMode? mode,  DateTime? endedAt,  String? endReason,  Map<String, dynamic>? patientReported)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SessionEnvelope() when $default != null:
return $default(_that.sessionId,_that.contractsVersion,_that.patientRef,_that.programRef,_that.startedAt,_that.device,_that.calibration,_that.blocks,_that.mode,_that.endedAt,_that.endReason,_that.patientReported);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String sessionId,  String contractsVersion,  String patientRef,  String programRef,  DateTime startedAt,  SessionDevice device,  SessionCalibration calibration,  List<SessionBlockRecord> blocks,  SessionMode? mode,  DateTime? endedAt,  String? endReason,  Map<String, dynamic>? patientReported)  $default,) {final _that = this;
switch (_that) {
case _SessionEnvelope():
return $default(_that.sessionId,_that.contractsVersion,_that.patientRef,_that.programRef,_that.startedAt,_that.device,_that.calibration,_that.blocks,_that.mode,_that.endedAt,_that.endReason,_that.patientReported);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String sessionId,  String contractsVersion,  String patientRef,  String programRef,  DateTime startedAt,  SessionDevice device,  SessionCalibration calibration,  List<SessionBlockRecord> blocks,  SessionMode? mode,  DateTime? endedAt,  String? endReason,  Map<String, dynamic>? patientReported)?  $default,) {final _that = this;
switch (_that) {
case _SessionEnvelope() when $default != null:
return $default(_that.sessionId,_that.contractsVersion,_that.patientRef,_that.programRef,_that.startedAt,_that.device,_that.calibration,_that.blocks,_that.mode,_that.endedAt,_that.endReason,_that.patientReported);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SessionEnvelope implements SessionEnvelope {
  const _SessionEnvelope({required this.sessionId, required this.contractsVersion, required this.patientRef, required this.programRef, required this.startedAt, required this.device, required this.calibration, required  List<SessionBlockRecord> blocks, this.mode, this.endedAt, this.endReason,  Map<String, dynamic>? patientReported}): _blocks = blocks,_patientReported = patientReported;
  factory _SessionEnvelope.fromJson(Map<String, dynamic> json) => _$SessionEnvelopeFromJson(json);

@override final  String sessionId;
@override final  String contractsVersion;
@override final  String patientRef;
@override final  String programRef;
@override final  DateTime startedAt;
@override final  SessionDevice device;
@override final  SessionCalibration calibration;
 final  List<SessionBlockRecord> _blocks;
@override List<SessionBlockRecord> get blocks {
  if (_blocks is EqualUnmodifiableListView) return _blocks;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_blocks);
}

@override final  SessionMode? mode;
@override final  DateTime? endedAt;
@override final  String? endReason;
 final  Map<String, dynamic>? _patientReported;
@override Map<String, dynamic>? get patientReported {
  final value = _patientReported;
  if (value == null) return null;
  if (_patientReported is EqualUnmodifiableMapView) return _patientReported;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(value);
}


/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SessionEnvelopeCopyWith<_SessionEnvelope> get copyWith => __$SessionEnvelopeCopyWithImpl<_SessionEnvelope>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SessionEnvelopeToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _SessionEnvelope&&(identical(other.sessionId, sessionId) || other.sessionId == sessionId)&&(identical(other.contractsVersion, contractsVersion) || other.contractsVersion == contractsVersion)&&(identical(other.patientRef, patientRef) || other.patientRef == patientRef)&&(identical(other.programRef, programRef) || other.programRef == programRef)&&(identical(other.startedAt, startedAt) || other.startedAt == startedAt)&&(identical(other.device, device) || other.device == device)&&(identical(other.calibration, calibration) || other.calibration == calibration)&&const DeepCollectionEquality().equals(other.blocks, _blocks)&&(identical(other.mode, mode) || other.mode == mode)&&(identical(other.endedAt, endedAt) || other.endedAt == endedAt)&&(identical(other.endReason, endReason) || other.endReason == endReason)&&const DeepCollectionEquality().equals(other.patientReported, _patientReported));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,sessionId,contractsVersion,patientRef,programRef,startedAt,device,calibration,const DeepCollectionEquality().hash(_blocks),mode,endedAt,endReason,const DeepCollectionEquality().hash(_patientReported));
}

@override
String toString() {
    return 'SessionEnvelope(sessionId: $sessionId, contractsVersion: $contractsVersion, patientRef: $patientRef, programRef: $programRef, startedAt: $startedAt, device: $device, calibration: $calibration, blocks: $blocks, mode: $mode, endedAt: $endedAt, endReason: $endReason, patientReported: $patientReported)';
}


}

/// @nodoc
abstract mixin class _$SessionEnvelopeCopyWith<$Res> implements $SessionEnvelopeCopyWith<$Res> {
  factory _$SessionEnvelopeCopyWith(_SessionEnvelope value, $Res Function(_SessionEnvelope) _then) = __$SessionEnvelopeCopyWithImpl;
@override @useResult
$Res call({
 String sessionId, String contractsVersion, String patientRef, String programRef, DateTime startedAt, SessionDevice device, SessionCalibration calibration, List<SessionBlockRecord> blocks, SessionMode? mode, DateTime? endedAt, String? endReason, Map<String, dynamic>? patientReported
});


@override $SessionDeviceCopyWith<$Res> get device;@override $SessionCalibrationCopyWith<$Res> get calibration;

}
/// @nodoc
class __$SessionEnvelopeCopyWithImpl<$Res>
    implements _$SessionEnvelopeCopyWith<$Res> {
  __$SessionEnvelopeCopyWithImpl(this._self, this._then);

  final _SessionEnvelope _self;
  final $Res Function(_SessionEnvelope) _then;

/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? sessionId = null,Object? contractsVersion = null,Object? patientRef = null,Object? programRef = null,Object? startedAt = null,Object? device = null,Object? calibration = null,Object? blocks = null,Object? mode = freezed,Object? endedAt = freezed,Object? endReason = freezed,Object? patientReported = freezed,}) {
  return _then(_SessionEnvelope(
sessionId: null == sessionId ? _self.sessionId : sessionId // ignore: cast_nullable_to_non_nullable
as String,contractsVersion: null == contractsVersion ? _self.contractsVersion : contractsVersion // ignore: cast_nullable_to_non_nullable
as String,patientRef: null == patientRef ? _self.patientRef : patientRef // ignore: cast_nullable_to_non_nullable
as String,programRef: null == programRef ? _self.programRef : programRef // ignore: cast_nullable_to_non_nullable
as String,startedAt: null == startedAt ? _self.startedAt : startedAt // ignore: cast_nullable_to_non_nullable
as DateTime,device: null == device ? _self.device : device // ignore: cast_nullable_to_non_nullable
as SessionDevice,calibration: null == calibration ? _self.calibration : calibration // ignore: cast_nullable_to_non_nullable
as SessionCalibration,blocks: null == blocks ? _self._blocks : blocks // ignore: cast_nullable_to_non_nullable
as List<SessionBlockRecord>,mode: freezed == mode ? _self.mode : mode // ignore: cast_nullable_to_non_nullable
as SessionMode?,endedAt: freezed == endedAt ? _self.endedAt : endedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,endReason: freezed == endReason ? _self.endReason : endReason // ignore: cast_nullable_to_non_nullable
as String?,patientReported: freezed == patientReported ? _self._patientReported : patientReported // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,
  ));
}

/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SessionDeviceCopyWith<$Res> get device {
  
  return $SessionDeviceCopyWith<$Res>(_self.device, (value) {
    return _then(_self.copyWith(device: value));
  });
}/// Create a copy of SessionEnvelope
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SessionCalibrationCopyWith<$Res> get calibration {
  
  return $SessionCalibrationCopyWith<$Res>(_self.calibration, (value) {
    return _then(_self.copyWith(calibration: value));
  });
}
}

// dart format on
