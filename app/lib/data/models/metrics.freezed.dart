// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint, type=warning, deprecated_member_use, deprecated_member_use_from_same_package
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'metrics.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$MetricValue {

 double? get value; String get unit; String get methodVersion; String get quality; List<String> get qualityReasons; double? get sd; int? get n; double? get mdc95;
/// Create a copy of MetricValue
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$MetricValueCopyWith<MetricValue> get copyWith => _$MetricValueCopyWithImpl<MetricValue>(this as MetricValue, _$identity);

  /// Serializes this MetricValue to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as MetricValue;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is MetricValue&&(identical(other.value, _this.value) || other.value == _this.value)&&(identical(other.unit, _this.unit) || other.unit == _this.unit)&&(identical(other.methodVersion, _this.methodVersion) || other.methodVersion == _this.methodVersion)&&(identical(other.quality, _this.quality) || other.quality == _this.quality)&&const DeepCollectionEquality().equals(other.qualityReasons, _this.qualityReasons)&&(identical(other.sd, _this.sd) || other.sd == _this.sd)&&(identical(other.n, _this.n) || other.n == _this.n)&&(identical(other.mdc95, _this.mdc95) || other.mdc95 == _this.mdc95));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as MetricValue;
  return Object.hash(runtimeType,_this.value,_this.unit,_this.methodVersion,_this.quality,const DeepCollectionEquality().hash(_this.qualityReasons),_this.sd,_this.n,_this.mdc95);
}

@override
String toString() {
  final _this = this as MetricValue;
  return 'MetricValue(value: ${_this.value}, unit: ${_this.unit}, methodVersion: ${_this.methodVersion}, quality: ${_this.quality}, qualityReasons: ${_this.qualityReasons}, sd: ${_this.sd}, n: ${_this.n}, mdc95: ${_this.mdc95})';
}


}

/// @nodoc
abstract mixin class $MetricValueCopyWith<$Res>  {
  factory $MetricValueCopyWith(MetricValue value, $Res Function(MetricValue) _then) = _$MetricValueCopyWithImpl;
@useResult
$Res call({
 double? value, String unit, String methodVersion, String quality, List<String> qualityReasons, double? sd, int? n, double? mdc95
});




}
/// @nodoc
class _$MetricValueCopyWithImpl<$Res>
    implements $MetricValueCopyWith<$Res> {
  _$MetricValueCopyWithImpl(this._self, this._then);

  final MetricValue _self;
  final $Res Function(MetricValue) _then;

/// Create a copy of MetricValue
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? value = freezed,Object? unit = null,Object? methodVersion = null,Object? quality = null,Object? qualityReasons = null,Object? sd = freezed,Object? n = freezed,Object? mdc95 = freezed,}) {
  return _then(MetricValue(
value: freezed == value ? _self.value : value // ignore: cast_nullable_to_non_nullable
as double?,unit: null == unit ? _self.unit : unit // ignore: cast_nullable_to_non_nullable
as String,methodVersion: null == methodVersion ? _self.methodVersion : methodVersion // ignore: cast_nullable_to_non_nullable
as String,quality: null == quality ? _self.quality : quality // ignore: cast_nullable_to_non_nullable
as String,qualityReasons: null == qualityReasons ? _self.qualityReasons : qualityReasons // ignore: cast_nullable_to_non_nullable
as List<String>,sd: freezed == sd ? _self.sd : sd // ignore: cast_nullable_to_non_nullable
as double?,n: freezed == n ? _self.n : n // ignore: cast_nullable_to_non_nullable
as int?,mdc95: freezed == mdc95 ? _self.mdc95 : mdc95 // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}

}


/// Adds pattern-matching-related methods to [MetricValue].
extension MetricValuePatterns on MetricValue {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _MetricValue value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _MetricValue() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _MetricValue value)  $default,){
final _that = this;
switch (_that) {
case _MetricValue():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _MetricValue value)?  $default,){
final _that = this;
switch (_that) {
case _MetricValue() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( double? value,  String unit,  String methodVersion,  String quality,  List<String> qualityReasons,  double? sd,  int? n,  double? mdc95)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _MetricValue() when $default != null:
return $default(_that.value,_that.unit,_that.methodVersion,_that.quality,_that.qualityReasons,_that.sd,_that.n,_that.mdc95);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( double? value,  String unit,  String methodVersion,  String quality,  List<String> qualityReasons,  double? sd,  int? n,  double? mdc95)  $default,) {final _that = this;
switch (_that) {
case _MetricValue():
return $default(_that.value,_that.unit,_that.methodVersion,_that.quality,_that.qualityReasons,_that.sd,_that.n,_that.mdc95);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( double? value,  String unit,  String methodVersion,  String quality,  List<String> qualityReasons,  double? sd,  int? n,  double? mdc95)?  $default,) {final _that = this;
switch (_that) {
case _MetricValue() when $default != null:
return $default(_that.value,_that.unit,_that.methodVersion,_that.quality,_that.qualityReasons,_that.sd,_that.n,_that.mdc95);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _MetricValue implements MetricValue {
  const _MetricValue({required this.value, required this.unit, required this.methodVersion, required this.quality,  List<String> qualityReasons = const [], this.sd, this.n, this.mdc95}): _qualityReasons = qualityReasons;
  factory _MetricValue.fromJson(Map<String, dynamic> json) => _$MetricValueFromJson(json);

@override final  double? value;
@override final  String unit;
@override final  String methodVersion;
@override final  String quality;
 final  List<String> _qualityReasons;
@override@JsonKey() List<String> get qualityReasons {
  if (_qualityReasons is EqualUnmodifiableListView) return _qualityReasons;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_qualityReasons);
}

@override final  double? sd;
@override final  int? n;
@override final  double? mdc95;

/// Create a copy of MetricValue
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$MetricValueCopyWith<_MetricValue> get copyWith => __$MetricValueCopyWithImpl<_MetricValue>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$MetricValueToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _MetricValue&&(identical(other.value, value) || other.value == value)&&(identical(other.unit, unit) || other.unit == unit)&&(identical(other.methodVersion, methodVersion) || other.methodVersion == methodVersion)&&(identical(other.quality, quality) || other.quality == quality)&&const DeepCollectionEquality().equals(other.qualityReasons, _qualityReasons)&&(identical(other.sd, sd) || other.sd == sd)&&(identical(other.n, n) || other.n == n)&&(identical(other.mdc95, mdc95) || other.mdc95 == mdc95));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,value,unit,methodVersion,quality,const DeepCollectionEquality().hash(_qualityReasons),sd,n,mdc95);
}

@override
String toString() {
    return 'MetricValue(value: $value, unit: $unit, methodVersion: $methodVersion, quality: $quality, qualityReasons: $qualityReasons, sd: $sd, n: $n, mdc95: $mdc95)';
}


}

/// @nodoc
abstract mixin class _$MetricValueCopyWith<$Res> implements $MetricValueCopyWith<$Res> {
  factory _$MetricValueCopyWith(_MetricValue value, $Res Function(_MetricValue) _then) = __$MetricValueCopyWithImpl;
@override @useResult
$Res call({
 double? value, String unit, String methodVersion, String quality, List<String> qualityReasons, double? sd, int? n, double? mdc95
});




}
/// @nodoc
class __$MetricValueCopyWithImpl<$Res>
    implements _$MetricValueCopyWith<$Res> {
  __$MetricValueCopyWithImpl(this._self, this._then);

  final _MetricValue _self;
  final $Res Function(_MetricValue) _then;

/// Create a copy of MetricValue
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? value = freezed,Object? unit = null,Object? methodVersion = null,Object? quality = null,Object? qualityReasons = null,Object? sd = freezed,Object? n = freezed,Object? mdc95 = freezed,}) {
  return _then(_MetricValue(
value: freezed == value ? _self.value : value // ignore: cast_nullable_to_non_nullable
as double?,unit: null == unit ? _self.unit : unit // ignore: cast_nullable_to_non_nullable
as String,methodVersion: null == methodVersion ? _self.methodVersion : methodVersion // ignore: cast_nullable_to_non_nullable
as String,quality: null == quality ? _self.quality : quality // ignore: cast_nullable_to_non_nullable
as String,qualityReasons: null == qualityReasons ? _self._qualityReasons : qualityReasons // ignore: cast_nullable_to_non_nullable
as List<String>,sd: freezed == sd ? _self.sd : sd // ignore: cast_nullable_to_non_nullable
as double?,n: freezed == n ? _self.n : n // ignore: cast_nullable_to_non_nullable
as int?,mdc95: freezed == mdc95 ? _self.mdc95 : mdc95 // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}


}


/// @nodoc
mixin _$TrialMetrics {

 int get block; int get trial; Map<String, MetricValue> get metrics; String? get hand; String? get outcome; Map<String, dynamic>? get target; double? get tStartMs; double? get tEndMs;
/// Create a copy of TrialMetrics
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$TrialMetricsCopyWith<TrialMetrics> get copyWith => _$TrialMetricsCopyWithImpl<TrialMetrics>(this as TrialMetrics, _$identity);

  /// Serializes this TrialMetrics to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as TrialMetrics;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is TrialMetrics&&(identical(other.block, _this.block) || other.block == _this.block)&&(identical(other.trial, _this.trial) || other.trial == _this.trial)&&const DeepCollectionEquality().equals(other.metrics, _this.metrics)&&(identical(other.hand, _this.hand) || other.hand == _this.hand)&&(identical(other.outcome, _this.outcome) || other.outcome == _this.outcome)&&const DeepCollectionEquality().equals(other.target, _this.target)&&(identical(other.tStartMs, _this.tStartMs) || other.tStartMs == _this.tStartMs)&&(identical(other.tEndMs, _this.tEndMs) || other.tEndMs == _this.tEndMs));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as TrialMetrics;
  return Object.hash(runtimeType,_this.block,_this.trial,const DeepCollectionEquality().hash(_this.metrics),_this.hand,_this.outcome,const DeepCollectionEquality().hash(_this.target),_this.tStartMs,_this.tEndMs);
}

@override
String toString() {
  final _this = this as TrialMetrics;
  return 'TrialMetrics(block: ${_this.block}, trial: ${_this.trial}, metrics: ${_this.metrics}, hand: ${_this.hand}, outcome: ${_this.outcome}, target: ${_this.target}, tStartMs: ${_this.tStartMs}, tEndMs: ${_this.tEndMs})';
}


}

/// @nodoc
abstract mixin class $TrialMetricsCopyWith<$Res>  {
  factory $TrialMetricsCopyWith(TrialMetrics value, $Res Function(TrialMetrics) _then) = _$TrialMetricsCopyWithImpl;
@useResult
$Res call({
 int block, int trial, Map<String, MetricValue> metrics, String? hand, String? outcome, Map<String, dynamic>? target, double? tStartMs, double? tEndMs
});




}
/// @nodoc
class _$TrialMetricsCopyWithImpl<$Res>
    implements $TrialMetricsCopyWith<$Res> {
  _$TrialMetricsCopyWithImpl(this._self, this._then);

  final TrialMetrics _self;
  final $Res Function(TrialMetrics) _then;

/// Create a copy of TrialMetrics
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? block = null,Object? trial = null,Object? metrics = null,Object? hand = freezed,Object? outcome = freezed,Object? target = freezed,Object? tStartMs = freezed,Object? tEndMs = freezed,}) {
  return _then(TrialMetrics(
block: null == block ? _self.block : block // ignore: cast_nullable_to_non_nullable
as int,trial: null == trial ? _self.trial : trial // ignore: cast_nullable_to_non_nullable
as int,metrics: null == metrics ? _self.metrics : metrics // ignore: cast_nullable_to_non_nullable
as Map<String, MetricValue>,hand: freezed == hand ? _self.hand : hand // ignore: cast_nullable_to_non_nullable
as String?,outcome: freezed == outcome ? _self.outcome : outcome // ignore: cast_nullable_to_non_nullable
as String?,target: freezed == target ? _self.target : target // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,tStartMs: freezed == tStartMs ? _self.tStartMs : tStartMs // ignore: cast_nullable_to_non_nullable
as double?,tEndMs: freezed == tEndMs ? _self.tEndMs : tEndMs // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}

}


/// Adds pattern-matching-related methods to [TrialMetrics].
extension TrialMetricsPatterns on TrialMetrics {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _TrialMetrics value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _TrialMetrics() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _TrialMetrics value)  $default,){
final _that = this;
switch (_that) {
case _TrialMetrics():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _TrialMetrics value)?  $default,){
final _that = this;
switch (_that) {
case _TrialMetrics() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( int block,  int trial,  Map<String, MetricValue> metrics,  String? hand,  String? outcome,  Map<String, dynamic>? target,  double? tStartMs,  double? tEndMs)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _TrialMetrics() when $default != null:
return $default(_that.block,_that.trial,_that.metrics,_that.hand,_that.outcome,_that.target,_that.tStartMs,_that.tEndMs);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( int block,  int trial,  Map<String, MetricValue> metrics,  String? hand,  String? outcome,  Map<String, dynamic>? target,  double? tStartMs,  double? tEndMs)  $default,) {final _that = this;
switch (_that) {
case _TrialMetrics():
return $default(_that.block,_that.trial,_that.metrics,_that.hand,_that.outcome,_that.target,_that.tStartMs,_that.tEndMs);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( int block,  int trial,  Map<String, MetricValue> metrics,  String? hand,  String? outcome,  Map<String, dynamic>? target,  double? tStartMs,  double? tEndMs)?  $default,) {final _that = this;
switch (_that) {
case _TrialMetrics() when $default != null:
return $default(_that.block,_that.trial,_that.metrics,_that.hand,_that.outcome,_that.target,_that.tStartMs,_that.tEndMs);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _TrialMetrics implements TrialMetrics {
  const _TrialMetrics({required this.block, required this.trial, required  Map<String, MetricValue> metrics, this.hand, this.outcome,  Map<String, dynamic>? target, this.tStartMs, this.tEndMs}): _metrics = metrics,_target = target;
  factory _TrialMetrics.fromJson(Map<String, dynamic> json) => _$TrialMetricsFromJson(json);

@override final  int block;
@override final  int trial;
 final  Map<String, MetricValue> _metrics;
@override Map<String, MetricValue> get metrics {
  if (_metrics is EqualUnmodifiableMapView) return _metrics;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_metrics);
}

@override final  String? hand;
@override final  String? outcome;
 final  Map<String, dynamic>? _target;
@override Map<String, dynamic>? get target {
  final value = _target;
  if (value == null) return null;
  if (_target is EqualUnmodifiableMapView) return _target;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(value);
}

@override final  double? tStartMs;
@override final  double? tEndMs;

/// Create a copy of TrialMetrics
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$TrialMetricsCopyWith<_TrialMetrics> get copyWith => __$TrialMetricsCopyWithImpl<_TrialMetrics>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$TrialMetricsToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _TrialMetrics&&(identical(other.block, block) || other.block == block)&&(identical(other.trial, trial) || other.trial == trial)&&const DeepCollectionEquality().equals(other.metrics, _metrics)&&(identical(other.hand, hand) || other.hand == hand)&&(identical(other.outcome, outcome) || other.outcome == outcome)&&const DeepCollectionEquality().equals(other.target, _target)&&(identical(other.tStartMs, tStartMs) || other.tStartMs == tStartMs)&&(identical(other.tEndMs, tEndMs) || other.tEndMs == tEndMs));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,block,trial,const DeepCollectionEquality().hash(_metrics),hand,outcome,const DeepCollectionEquality().hash(_target),tStartMs,tEndMs);
}

@override
String toString() {
    return 'TrialMetrics(block: $block, trial: $trial, metrics: $metrics, hand: $hand, outcome: $outcome, target: $target, tStartMs: $tStartMs, tEndMs: $tEndMs)';
}


}

/// @nodoc
abstract mixin class _$TrialMetricsCopyWith<$Res> implements $TrialMetricsCopyWith<$Res> {
  factory _$TrialMetricsCopyWith(_TrialMetrics value, $Res Function(_TrialMetrics) _then) = __$TrialMetricsCopyWithImpl;
@override @useResult
$Res call({
 int block, int trial, Map<String, MetricValue> metrics, String? hand, String? outcome, Map<String, dynamic>? target, double? tStartMs, double? tEndMs
});




}
/// @nodoc
class __$TrialMetricsCopyWithImpl<$Res>
    implements _$TrialMetricsCopyWith<$Res> {
  __$TrialMetricsCopyWithImpl(this._self, this._then);

  final _TrialMetrics _self;
  final $Res Function(_TrialMetrics) _then;

/// Create a copy of TrialMetrics
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? block = null,Object? trial = null,Object? metrics = null,Object? hand = freezed,Object? outcome = freezed,Object? target = freezed,Object? tStartMs = freezed,Object? tEndMs = freezed,}) {
  return _then(_TrialMetrics(
block: null == block ? _self.block : block // ignore: cast_nullable_to_non_nullable
as int,trial: null == trial ? _self.trial : trial // ignore: cast_nullable_to_non_nullable
as int,metrics: null == metrics ? _self._metrics : metrics // ignore: cast_nullable_to_non_nullable
as Map<String, MetricValue>,hand: freezed == hand ? _self.hand : hand // ignore: cast_nullable_to_non_nullable
as String?,outcome: freezed == outcome ? _self.outcome : outcome // ignore: cast_nullable_to_non_nullable
as String?,target: freezed == target ? _self._target : target // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,tStartMs: freezed == tStartMs ? _self.tStartMs : tStartMs // ignore: cast_nullable_to_non_nullable
as double?,tEndMs: freezed == tEndMs ? _self.tEndMs : tEndMs // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}


}


/// @nodoc
mixin _$SessionAggregateMetrics {

 Map<String, MetricValue> get metrics; Map<String, Map<String, MetricValue>>? get bySide; Map<String, dynamic>? get workspaceHeatmap;
/// Create a copy of SessionAggregateMetrics
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SessionAggregateMetricsCopyWith<SessionAggregateMetrics> get copyWith => _$SessionAggregateMetricsCopyWithImpl<SessionAggregateMetrics>(this as SessionAggregateMetrics, _$identity);

  /// Serializes this SessionAggregateMetrics to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as SessionAggregateMetrics;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SessionAggregateMetrics&&const DeepCollectionEquality().equals(other.metrics, _this.metrics)&&const DeepCollectionEquality().equals(other.bySide, _this.bySide)&&const DeepCollectionEquality().equals(other.workspaceHeatmap, _this.workspaceHeatmap));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as SessionAggregateMetrics;
  return Object.hash(runtimeType,const DeepCollectionEquality().hash(_this.metrics),const DeepCollectionEquality().hash(_this.bySide),const DeepCollectionEquality().hash(_this.workspaceHeatmap));
}

@override
String toString() {
  final _this = this as SessionAggregateMetrics;
  return 'SessionAggregateMetrics(metrics: ${_this.metrics}, bySide: ${_this.bySide}, workspaceHeatmap: ${_this.workspaceHeatmap})';
}


}

/// @nodoc
abstract mixin class $SessionAggregateMetricsCopyWith<$Res>  {
  factory $SessionAggregateMetricsCopyWith(SessionAggregateMetrics value, $Res Function(SessionAggregateMetrics) _then) = _$SessionAggregateMetricsCopyWithImpl;
@useResult
$Res call({
 Map<String, MetricValue> metrics, Map<String, Map<String, MetricValue>>? bySide, Map<String, dynamic>? workspaceHeatmap
});




}
/// @nodoc
class _$SessionAggregateMetricsCopyWithImpl<$Res>
    implements $SessionAggregateMetricsCopyWith<$Res> {
  _$SessionAggregateMetricsCopyWithImpl(this._self, this._then);

  final SessionAggregateMetrics _self;
  final $Res Function(SessionAggregateMetrics) _then;

/// Create a copy of SessionAggregateMetrics
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? metrics = null,Object? bySide = freezed,Object? workspaceHeatmap = freezed,}) {
  return _then(SessionAggregateMetrics(
metrics: null == metrics ? _self.metrics : metrics // ignore: cast_nullable_to_non_nullable
as Map<String, MetricValue>,bySide: freezed == bySide ? _self.bySide : bySide // ignore: cast_nullable_to_non_nullable
as Map<String, Map<String, MetricValue>>?,workspaceHeatmap: freezed == workspaceHeatmap ? _self.workspaceHeatmap : workspaceHeatmap // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,
  ));
}

}


/// Adds pattern-matching-related methods to [SessionAggregateMetrics].
extension SessionAggregateMetricsPatterns on SessionAggregateMetrics {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SessionAggregateMetrics value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SessionAggregateMetrics() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SessionAggregateMetrics value)  $default,){
final _that = this;
switch (_that) {
case _SessionAggregateMetrics():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SessionAggregateMetrics value)?  $default,){
final _that = this;
switch (_that) {
case _SessionAggregateMetrics() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( Map<String, MetricValue> metrics,  Map<String, Map<String, MetricValue>>? bySide,  Map<String, dynamic>? workspaceHeatmap)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SessionAggregateMetrics() when $default != null:
return $default(_that.metrics,_that.bySide,_that.workspaceHeatmap);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( Map<String, MetricValue> metrics,  Map<String, Map<String, MetricValue>>? bySide,  Map<String, dynamic>? workspaceHeatmap)  $default,) {final _that = this;
switch (_that) {
case _SessionAggregateMetrics():
return $default(_that.metrics,_that.bySide,_that.workspaceHeatmap);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( Map<String, MetricValue> metrics,  Map<String, Map<String, MetricValue>>? bySide,  Map<String, dynamic>? workspaceHeatmap)?  $default,) {final _that = this;
switch (_that) {
case _SessionAggregateMetrics() when $default != null:
return $default(_that.metrics,_that.bySide,_that.workspaceHeatmap);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SessionAggregateMetrics implements SessionAggregateMetrics {
  const _SessionAggregateMetrics({required  Map<String, MetricValue> metrics,  Map<String, Map<String, MetricValue>>? bySide,  Map<String, dynamic>? workspaceHeatmap}): _metrics = metrics,_bySide = bySide,_workspaceHeatmap = workspaceHeatmap;
  factory _SessionAggregateMetrics.fromJson(Map<String, dynamic> json) => _$SessionAggregateMetricsFromJson(json);

 final  Map<String, MetricValue> _metrics;
@override Map<String, MetricValue> get metrics {
  if (_metrics is EqualUnmodifiableMapView) return _metrics;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_metrics);
}

 final  Map<String, Map<String, MetricValue>>? _bySide;
@override Map<String, Map<String, MetricValue>>? get bySide {
  final value = _bySide;
  if (value == null) return null;
  if (_bySide is EqualUnmodifiableMapView) return _bySide;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(value);
}

 final  Map<String, dynamic>? _workspaceHeatmap;
@override Map<String, dynamic>? get workspaceHeatmap {
  final value = _workspaceHeatmap;
  if (value == null) return null;
  if (_workspaceHeatmap is EqualUnmodifiableMapView) return _workspaceHeatmap;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(value);
}


/// Create a copy of SessionAggregateMetrics
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SessionAggregateMetricsCopyWith<_SessionAggregateMetrics> get copyWith => __$SessionAggregateMetricsCopyWithImpl<_SessionAggregateMetrics>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SessionAggregateMetricsToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _SessionAggregateMetrics&&const DeepCollectionEquality().equals(other.metrics, _metrics)&&const DeepCollectionEquality().equals(other.bySide, _bySide)&&const DeepCollectionEquality().equals(other.workspaceHeatmap, _workspaceHeatmap));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,const DeepCollectionEquality().hash(_metrics),const DeepCollectionEquality().hash(_bySide),const DeepCollectionEquality().hash(_workspaceHeatmap));
}

@override
String toString() {
    return 'SessionAggregateMetrics(metrics: $metrics, bySide: $bySide, workspaceHeatmap: $workspaceHeatmap)';
}


}

/// @nodoc
abstract mixin class _$SessionAggregateMetricsCopyWith<$Res> implements $SessionAggregateMetricsCopyWith<$Res> {
  factory _$SessionAggregateMetricsCopyWith(_SessionAggregateMetrics value, $Res Function(_SessionAggregateMetrics) _then) = __$SessionAggregateMetricsCopyWithImpl;
@override @useResult
$Res call({
 Map<String, MetricValue> metrics, Map<String, Map<String, MetricValue>>? bySide, Map<String, dynamic>? workspaceHeatmap
});




}
/// @nodoc
class __$SessionAggregateMetricsCopyWithImpl<$Res>
    implements _$SessionAggregateMetricsCopyWith<$Res> {
  __$SessionAggregateMetricsCopyWithImpl(this._self, this._then);

  final _SessionAggregateMetrics _self;
  final $Res Function(_SessionAggregateMetrics) _then;

/// Create a copy of SessionAggregateMetrics
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? metrics = null,Object? bySide = freezed,Object? workspaceHeatmap = freezed,}) {
  return _then(_SessionAggregateMetrics(
metrics: null == metrics ? _self._metrics : metrics // ignore: cast_nullable_to_non_nullable
as Map<String, MetricValue>,bySide: freezed == bySide ? _self._bySide : bySide // ignore: cast_nullable_to_non_nullable
as Map<String, Map<String, MetricValue>>?,workspaceHeatmap: freezed == workspaceHeatmap ? _self._workspaceHeatmap : workspaceHeatmap // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>?,
  ));
}


}


/// @nodoc
mixin _$SessionMetrics {

 String get sessionId; String get analyticsVersion; DateTime get computedAt; List<TrialMetrics> get trials; SessionAggregateMetrics get session;
/// Create a copy of SessionMetrics
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SessionMetricsCopyWith<SessionMetrics> get copyWith => _$SessionMetricsCopyWithImpl<SessionMetrics>(this as SessionMetrics, _$identity);

  /// Serializes this SessionMetrics to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as SessionMetrics;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SessionMetrics&&(identical(other.sessionId, _this.sessionId) || other.sessionId == _this.sessionId)&&(identical(other.analyticsVersion, _this.analyticsVersion) || other.analyticsVersion == _this.analyticsVersion)&&(identical(other.computedAt, _this.computedAt) || other.computedAt == _this.computedAt)&&const DeepCollectionEquality().equals(other.trials, _this.trials)&&(identical(other.session, _this.session) || other.session == _this.session));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as SessionMetrics;
  return Object.hash(runtimeType,_this.sessionId,_this.analyticsVersion,_this.computedAt,const DeepCollectionEquality().hash(_this.trials),_this.session);
}

@override
String toString() {
  final _this = this as SessionMetrics;
  return 'SessionMetrics(sessionId: ${_this.sessionId}, analyticsVersion: ${_this.analyticsVersion}, computedAt: ${_this.computedAt}, trials: ${_this.trials}, session: ${_this.session})';
}


}

/// @nodoc
abstract mixin class $SessionMetricsCopyWith<$Res>  {
  factory $SessionMetricsCopyWith(SessionMetrics value, $Res Function(SessionMetrics) _then) = _$SessionMetricsCopyWithImpl;
@useResult
$Res call({
 String sessionId, String analyticsVersion, DateTime computedAt, List<TrialMetrics> trials, SessionAggregateMetrics session
});


$SessionAggregateMetricsCopyWith<$Res> get session;

}
/// @nodoc
class _$SessionMetricsCopyWithImpl<$Res>
    implements $SessionMetricsCopyWith<$Res> {
  _$SessionMetricsCopyWithImpl(this._self, this._then);

  final SessionMetrics _self;
  final $Res Function(SessionMetrics) _then;

/// Create a copy of SessionMetrics
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? sessionId = null,Object? analyticsVersion = null,Object? computedAt = null,Object? trials = null,Object? session = null,}) {
  return _then(SessionMetrics(
sessionId: null == sessionId ? _self.sessionId : sessionId // ignore: cast_nullable_to_non_nullable
as String,analyticsVersion: null == analyticsVersion ? _self.analyticsVersion : analyticsVersion // ignore: cast_nullable_to_non_nullable
as String,computedAt: null == computedAt ? _self.computedAt : computedAt // ignore: cast_nullable_to_non_nullable
as DateTime,trials: null == trials ? _self.trials : trials // ignore: cast_nullable_to_non_nullable
as List<TrialMetrics>,session: null == session ? _self.session : session // ignore: cast_nullable_to_non_nullable
as SessionAggregateMetrics,
  ));
}
/// Create a copy of SessionMetrics
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SessionAggregateMetricsCopyWith<$Res> get session {
  
  return $SessionAggregateMetricsCopyWith<$Res>(_self.session, (value) {
    return _then(_self.copyWith(session: value));
  });
}
}


/// Adds pattern-matching-related methods to [SessionMetrics].
extension SessionMetricsPatterns on SessionMetrics {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SessionMetrics value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SessionMetrics() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SessionMetrics value)  $default,){
final _that = this;
switch (_that) {
case _SessionMetrics():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SessionMetrics value)?  $default,){
final _that = this;
switch (_that) {
case _SessionMetrics() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String sessionId,  String analyticsVersion,  DateTime computedAt,  List<TrialMetrics> trials,  SessionAggregateMetrics session)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SessionMetrics() when $default != null:
return $default(_that.sessionId,_that.analyticsVersion,_that.computedAt,_that.trials,_that.session);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String sessionId,  String analyticsVersion,  DateTime computedAt,  List<TrialMetrics> trials,  SessionAggregateMetrics session)  $default,) {final _that = this;
switch (_that) {
case _SessionMetrics():
return $default(_that.sessionId,_that.analyticsVersion,_that.computedAt,_that.trials,_that.session);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String sessionId,  String analyticsVersion,  DateTime computedAt,  List<TrialMetrics> trials,  SessionAggregateMetrics session)?  $default,) {final _that = this;
switch (_that) {
case _SessionMetrics() when $default != null:
return $default(_that.sessionId,_that.analyticsVersion,_that.computedAt,_that.trials,_that.session);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SessionMetrics implements SessionMetrics {
  const _SessionMetrics({required this.sessionId, required this.analyticsVersion, required this.computedAt, required  List<TrialMetrics> trials, required this.session}): _trials = trials;
  factory _SessionMetrics.fromJson(Map<String, dynamic> json) => _$SessionMetricsFromJson(json);

@override final  String sessionId;
@override final  String analyticsVersion;
@override final  DateTime computedAt;
 final  List<TrialMetrics> _trials;
@override List<TrialMetrics> get trials {
  if (_trials is EqualUnmodifiableListView) return _trials;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_trials);
}

@override final  SessionAggregateMetrics session;

/// Create a copy of SessionMetrics
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SessionMetricsCopyWith<_SessionMetrics> get copyWith => __$SessionMetricsCopyWithImpl<_SessionMetrics>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SessionMetricsToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _SessionMetrics&&(identical(other.sessionId, sessionId) || other.sessionId == sessionId)&&(identical(other.analyticsVersion, analyticsVersion) || other.analyticsVersion == analyticsVersion)&&(identical(other.computedAt, computedAt) || other.computedAt == computedAt)&&const DeepCollectionEquality().equals(other.trials, _trials)&&(identical(other.session, session) || other.session == session));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,sessionId,analyticsVersion,computedAt,const DeepCollectionEquality().hash(_trials),session);
}

@override
String toString() {
    return 'SessionMetrics(sessionId: $sessionId, analyticsVersion: $analyticsVersion, computedAt: $computedAt, trials: $trials, session: $session)';
}


}

/// @nodoc
abstract mixin class _$SessionMetricsCopyWith<$Res> implements $SessionMetricsCopyWith<$Res> {
  factory _$SessionMetricsCopyWith(_SessionMetrics value, $Res Function(_SessionMetrics) _then) = __$SessionMetricsCopyWithImpl;
@override @useResult
$Res call({
 String sessionId, String analyticsVersion, DateTime computedAt, List<TrialMetrics> trials, SessionAggregateMetrics session
});


@override $SessionAggregateMetricsCopyWith<$Res> get session;

}
/// @nodoc
class __$SessionMetricsCopyWithImpl<$Res>
    implements _$SessionMetricsCopyWith<$Res> {
  __$SessionMetricsCopyWithImpl(this._self, this._then);

  final _SessionMetrics _self;
  final $Res Function(_SessionMetrics) _then;

/// Create a copy of SessionMetrics
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? sessionId = null,Object? analyticsVersion = null,Object? computedAt = null,Object? trials = null,Object? session = null,}) {
  return _then(_SessionMetrics(
sessionId: null == sessionId ? _self.sessionId : sessionId // ignore: cast_nullable_to_non_nullable
as String,analyticsVersion: null == analyticsVersion ? _self.analyticsVersion : analyticsVersion // ignore: cast_nullable_to_non_nullable
as String,computedAt: null == computedAt ? _self.computedAt : computedAt // ignore: cast_nullable_to_non_nullable
as DateTime,trials: null == trials ? _self._trials : trials // ignore: cast_nullable_to_non_nullable
as List<TrialMetrics>,session: null == session ? _self.session : session // ignore: cast_nullable_to_non_nullable
as SessionAggregateMetrics,
  ));
}

/// Create a copy of SessionMetrics
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SessionAggregateMetricsCopyWith<$Res> get session {
  
  return $SessionAggregateMetricsCopyWith<$Res>(_self.session, (value) {
    return _then(_self.copyWith(session: value));
  });
}
}

// dart format on
