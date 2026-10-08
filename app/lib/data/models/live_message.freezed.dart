// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint, type=warning, deprecated_member_use, deprecated_member_use_from_same_package
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'live_message.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$LiveMessage {

 LiveSessionStatus get status; int get trialIndex; int get totalTrials; Map<String, double> get rollingMetrics; String? get lastOutcome;
/// Create a copy of LiveMessage
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$LiveMessageCopyWith<LiveMessage> get copyWith => _$LiveMessageCopyWithImpl<LiveMessage>(this as LiveMessage, _$identity);

  /// Serializes this LiveMessage to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as LiveMessage;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is LiveMessage&&(identical(other.status, _this.status) || other.status == _this.status)&&(identical(other.trialIndex, _this.trialIndex) || other.trialIndex == _this.trialIndex)&&(identical(other.totalTrials, _this.totalTrials) || other.totalTrials == _this.totalTrials)&&const DeepCollectionEquality().equals(other.rollingMetrics, _this.rollingMetrics)&&(identical(other.lastOutcome, _this.lastOutcome) || other.lastOutcome == _this.lastOutcome));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as LiveMessage;
  return Object.hash(runtimeType,_this.status,_this.trialIndex,_this.totalTrials,const DeepCollectionEquality().hash(_this.rollingMetrics),_this.lastOutcome);
}

@override
String toString() {
  final _this = this as LiveMessage;
  return 'LiveMessage(status: ${_this.status}, trialIndex: ${_this.trialIndex}, totalTrials: ${_this.totalTrials}, rollingMetrics: ${_this.rollingMetrics}, lastOutcome: ${_this.lastOutcome})';
}


}

/// @nodoc
abstract mixin class $LiveMessageCopyWith<$Res>  {
  factory $LiveMessageCopyWith(LiveMessage value, $Res Function(LiveMessage) _then) = _$LiveMessageCopyWithImpl;
@useResult
$Res call({
 LiveSessionStatus status, int trialIndex, int totalTrials, Map<String, double> rollingMetrics, String? lastOutcome
});




}
/// @nodoc
class _$LiveMessageCopyWithImpl<$Res>
    implements $LiveMessageCopyWith<$Res> {
  _$LiveMessageCopyWithImpl(this._self, this._then);

  final LiveMessage _self;
  final $Res Function(LiveMessage) _then;

/// Create a copy of LiveMessage
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? status = null,Object? trialIndex = null,Object? totalTrials = null,Object? rollingMetrics = null,Object? lastOutcome = freezed,}) {
  return _then(LiveMessage(
status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as LiveSessionStatus,trialIndex: null == trialIndex ? _self.trialIndex : trialIndex // ignore: cast_nullable_to_non_nullable
as int,totalTrials: null == totalTrials ? _self.totalTrials : totalTrials // ignore: cast_nullable_to_non_nullable
as int,rollingMetrics: null == rollingMetrics ? _self.rollingMetrics : rollingMetrics // ignore: cast_nullable_to_non_nullable
as Map<String, double>,lastOutcome: freezed == lastOutcome ? _self.lastOutcome : lastOutcome // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [LiveMessage].
extension LiveMessagePatterns on LiveMessage {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _LiveMessage value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _LiveMessage() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _LiveMessage value)  $default,){
final _that = this;
switch (_that) {
case _LiveMessage():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _LiveMessage value)?  $default,){
final _that = this;
switch (_that) {
case _LiveMessage() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( LiveSessionStatus status,  int trialIndex,  int totalTrials,  Map<String, double> rollingMetrics,  String? lastOutcome)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _LiveMessage() when $default != null:
return $default(_that.status,_that.trialIndex,_that.totalTrials,_that.rollingMetrics,_that.lastOutcome);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( LiveSessionStatus status,  int trialIndex,  int totalTrials,  Map<String, double> rollingMetrics,  String? lastOutcome)  $default,) {final _that = this;
switch (_that) {
case _LiveMessage():
return $default(_that.status,_that.trialIndex,_that.totalTrials,_that.rollingMetrics,_that.lastOutcome);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( LiveSessionStatus status,  int trialIndex,  int totalTrials,  Map<String, double> rollingMetrics,  String? lastOutcome)?  $default,) {final _that = this;
switch (_that) {
case _LiveMessage() when $default != null:
return $default(_that.status,_that.trialIndex,_that.totalTrials,_that.rollingMetrics,_that.lastOutcome);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _LiveMessage implements LiveMessage {
  const _LiveMessage({required this.status, required this.trialIndex, required this.totalTrials, required  Map<String, double> rollingMetrics, this.lastOutcome}): _rollingMetrics = rollingMetrics;
  factory _LiveMessage.fromJson(Map<String, dynamic> json) => _$LiveMessageFromJson(json);

@override final  LiveSessionStatus status;
@override final  int trialIndex;
@override final  int totalTrials;
 final  Map<String, double> _rollingMetrics;
@override Map<String, double> get rollingMetrics {
  if (_rollingMetrics is EqualUnmodifiableMapView) return _rollingMetrics;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_rollingMetrics);
}

@override final  String? lastOutcome;

/// Create a copy of LiveMessage
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$LiveMessageCopyWith<_LiveMessage> get copyWith => __$LiveMessageCopyWithImpl<_LiveMessage>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$LiveMessageToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _LiveMessage&&(identical(other.status, status) || other.status == status)&&(identical(other.trialIndex, trialIndex) || other.trialIndex == trialIndex)&&(identical(other.totalTrials, totalTrials) || other.totalTrials == totalTrials)&&const DeepCollectionEquality().equals(other.rollingMetrics, _rollingMetrics)&&(identical(other.lastOutcome, lastOutcome) || other.lastOutcome == lastOutcome));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,status,trialIndex,totalTrials,const DeepCollectionEquality().hash(_rollingMetrics),lastOutcome);
}

@override
String toString() {
    return 'LiveMessage(status: $status, trialIndex: $trialIndex, totalTrials: $totalTrials, rollingMetrics: $rollingMetrics, lastOutcome: $lastOutcome)';
}


}

/// @nodoc
abstract mixin class _$LiveMessageCopyWith<$Res> implements $LiveMessageCopyWith<$Res> {
  factory _$LiveMessageCopyWith(_LiveMessage value, $Res Function(_LiveMessage) _then) = __$LiveMessageCopyWithImpl;
@override @useResult
$Res call({
 LiveSessionStatus status, int trialIndex, int totalTrials, Map<String, double> rollingMetrics, String? lastOutcome
});




}
/// @nodoc
class __$LiveMessageCopyWithImpl<$Res>
    implements _$LiveMessageCopyWith<$Res> {
  __$LiveMessageCopyWithImpl(this._self, this._then);

  final _LiveMessage _self;
  final $Res Function(_LiveMessage) _then;

/// Create a copy of LiveMessage
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? status = null,Object? trialIndex = null,Object? totalTrials = null,Object? rollingMetrics = null,Object? lastOutcome = freezed,}) {
  return _then(_LiveMessage(
status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as LiveSessionStatus,trialIndex: null == trialIndex ? _self.trialIndex : trialIndex // ignore: cast_nullable_to_non_nullable
as int,totalTrials: null == totalTrials ? _self.totalTrials : totalTrials // ignore: cast_nullable_to_non_nullable
as int,rollingMetrics: null == rollingMetrics ? _self._rollingMetrics : rollingMetrics // ignore: cast_nullable_to_non_nullable
as Map<String, double>,lastOutcome: freezed == lastOutcome ? _self.lastOutcome : lastOutcome // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
