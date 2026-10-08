// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint, type=warning, deprecated_member_use, deprecated_member_use_from_same_package
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'program.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ProgramSchedule {

 DateTime get startDate; DateTime? get endDate; int get sessionsPerWeek; int get maxSessionMinutes; ProgramLocation get location;
/// Create a copy of ProgramSchedule
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ProgramScheduleCopyWith<ProgramSchedule> get copyWith => _$ProgramScheduleCopyWithImpl<ProgramSchedule>(this as ProgramSchedule, _$identity);

  /// Serializes this ProgramSchedule to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as ProgramSchedule;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ProgramSchedule&&(identical(other.startDate, _this.startDate) || other.startDate == _this.startDate)&&(identical(other.endDate, _this.endDate) || other.endDate == _this.endDate)&&(identical(other.sessionsPerWeek, _this.sessionsPerWeek) || other.sessionsPerWeek == _this.sessionsPerWeek)&&(identical(other.maxSessionMinutes, _this.maxSessionMinutes) || other.maxSessionMinutes == _this.maxSessionMinutes)&&(identical(other.location, _this.location) || other.location == _this.location));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as ProgramSchedule;
  return Object.hash(runtimeType,_this.startDate,_this.endDate,_this.sessionsPerWeek,_this.maxSessionMinutes,_this.location);
}

@override
String toString() {
  final _this = this as ProgramSchedule;
  return 'ProgramSchedule(startDate: ${_this.startDate}, endDate: ${_this.endDate}, sessionsPerWeek: ${_this.sessionsPerWeek}, maxSessionMinutes: ${_this.maxSessionMinutes}, location: ${_this.location})';
}


}

/// @nodoc
abstract mixin class $ProgramScheduleCopyWith<$Res>  {
  factory $ProgramScheduleCopyWith(ProgramSchedule value, $Res Function(ProgramSchedule) _then) = _$ProgramScheduleCopyWithImpl;
@useResult
$Res call({
 DateTime startDate, DateTime? endDate, int sessionsPerWeek, int maxSessionMinutes, ProgramLocation location
});




}
/// @nodoc
class _$ProgramScheduleCopyWithImpl<$Res>
    implements $ProgramScheduleCopyWith<$Res> {
  _$ProgramScheduleCopyWithImpl(this._self, this._then);

  final ProgramSchedule _self;
  final $Res Function(ProgramSchedule) _then;

/// Create a copy of ProgramSchedule
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? startDate = null,Object? endDate = freezed,Object? sessionsPerWeek = null,Object? maxSessionMinutes = null,Object? location = null,}) {
  return _then(ProgramSchedule(
startDate: null == startDate ? _self.startDate : startDate // ignore: cast_nullable_to_non_nullable
as DateTime,endDate: freezed == endDate ? _self.endDate : endDate // ignore: cast_nullable_to_non_nullable
as DateTime?,sessionsPerWeek: null == sessionsPerWeek ? _self.sessionsPerWeek : sessionsPerWeek // ignore: cast_nullable_to_non_nullable
as int,maxSessionMinutes: null == maxSessionMinutes ? _self.maxSessionMinutes : maxSessionMinutes // ignore: cast_nullable_to_non_nullable
as int,location: null == location ? _self.location : location // ignore: cast_nullable_to_non_nullable
as ProgramLocation,
  ));
}

}


/// Adds pattern-matching-related methods to [ProgramSchedule].
extension ProgramSchedulePatterns on ProgramSchedule {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ProgramSchedule value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ProgramSchedule() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ProgramSchedule value)  $default,){
final _that = this;
switch (_that) {
case _ProgramSchedule():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ProgramSchedule value)?  $default,){
final _that = this;
switch (_that) {
case _ProgramSchedule() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( DateTime startDate,  DateTime? endDate,  int sessionsPerWeek,  int maxSessionMinutes,  ProgramLocation location)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ProgramSchedule() when $default != null:
return $default(_that.startDate,_that.endDate,_that.sessionsPerWeek,_that.maxSessionMinutes,_that.location);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( DateTime startDate,  DateTime? endDate,  int sessionsPerWeek,  int maxSessionMinutes,  ProgramLocation location)  $default,) {final _that = this;
switch (_that) {
case _ProgramSchedule():
return $default(_that.startDate,_that.endDate,_that.sessionsPerWeek,_that.maxSessionMinutes,_that.location);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( DateTime startDate,  DateTime? endDate,  int sessionsPerWeek,  int maxSessionMinutes,  ProgramLocation location)?  $default,) {final _that = this;
switch (_that) {
case _ProgramSchedule() when $default != null:
return $default(_that.startDate,_that.endDate,_that.sessionsPerWeek,_that.maxSessionMinutes,_that.location);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ProgramSchedule implements ProgramSchedule {
  const _ProgramSchedule({required this.startDate, this.endDate, this.sessionsPerWeek = 3, this.maxSessionMinutes = 20, this.location = ProgramLocation.either});
  factory _ProgramSchedule.fromJson(Map<String, dynamic> json) => _$ProgramScheduleFromJson(json);

@override final  DateTime startDate;
@override final  DateTime? endDate;
@override@JsonKey() final  int sessionsPerWeek;
@override@JsonKey() final  int maxSessionMinutes;
@override@JsonKey() final  ProgramLocation location;

/// Create a copy of ProgramSchedule
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ProgramScheduleCopyWith<_ProgramSchedule> get copyWith => __$ProgramScheduleCopyWithImpl<_ProgramSchedule>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ProgramScheduleToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _ProgramSchedule&&(identical(other.startDate, startDate) || other.startDate == startDate)&&(identical(other.endDate, endDate) || other.endDate == endDate)&&(identical(other.sessionsPerWeek, sessionsPerWeek) || other.sessionsPerWeek == sessionsPerWeek)&&(identical(other.maxSessionMinutes, maxSessionMinutes) || other.maxSessionMinutes == maxSessionMinutes)&&(identical(other.location, location) || other.location == location));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,startDate,endDate,sessionsPerWeek,maxSessionMinutes,location);
}

@override
String toString() {
    return 'ProgramSchedule(startDate: $startDate, endDate: $endDate, sessionsPerWeek: $sessionsPerWeek, maxSessionMinutes: $maxSessionMinutes, location: $location)';
}


}

/// @nodoc
abstract mixin class _$ProgramScheduleCopyWith<$Res> implements $ProgramScheduleCopyWith<$Res> {
  factory _$ProgramScheduleCopyWith(_ProgramSchedule value, $Res Function(_ProgramSchedule) _then) = __$ProgramScheduleCopyWithImpl;
@override @useResult
$Res call({
 DateTime startDate, DateTime? endDate, int sessionsPerWeek, int maxSessionMinutes, ProgramLocation location
});




}
/// @nodoc
class __$ProgramScheduleCopyWithImpl<$Res>
    implements _$ProgramScheduleCopyWith<$Res> {
  __$ProgramScheduleCopyWithImpl(this._self, this._then);

  final _ProgramSchedule _self;
  final $Res Function(_ProgramSchedule) _then;

/// Create a copy of ProgramSchedule
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? startDate = null,Object? endDate = freezed,Object? sessionsPerWeek = null,Object? maxSessionMinutes = null,Object? location = null,}) {
  return _then(_ProgramSchedule(
startDate: null == startDate ? _self.startDate : startDate // ignore: cast_nullable_to_non_nullable
as DateTime,endDate: freezed == endDate ? _self.endDate : endDate // ignore: cast_nullable_to_non_nullable
as DateTime?,sessionsPerWeek: null == sessionsPerWeek ? _self.sessionsPerWeek : sessionsPerWeek // ignore: cast_nullable_to_non_nullable
as int,maxSessionMinutes: null == maxSessionMinutes ? _self.maxSessionMinutes : maxSessionMinutes // ignore: cast_nullable_to_non_nullable
as int,location: null == location ? _self.location : location // ignore: cast_nullable_to_non_nullable
as ProgramLocation,
  ));
}


}


/// @nodoc
mixin _$ProgramBlock {

 String get gameId; String get gameVersionRange; Map<String, dynamic> get params; int get durationSec; int get restAfterSec;
/// Create a copy of ProgramBlock
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ProgramBlockCopyWith<ProgramBlock> get copyWith => _$ProgramBlockCopyWithImpl<ProgramBlock>(this as ProgramBlock, _$identity);

  /// Serializes this ProgramBlock to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as ProgramBlock;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ProgramBlock&&(identical(other.gameId, _this.gameId) || other.gameId == _this.gameId)&&(identical(other.gameVersionRange, _this.gameVersionRange) || other.gameVersionRange == _this.gameVersionRange)&&const DeepCollectionEquality().equals(other.params, _this.params)&&(identical(other.durationSec, _this.durationSec) || other.durationSec == _this.durationSec)&&(identical(other.restAfterSec, _this.restAfterSec) || other.restAfterSec == _this.restAfterSec));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as ProgramBlock;
  return Object.hash(runtimeType,_this.gameId,_this.gameVersionRange,const DeepCollectionEquality().hash(_this.params),_this.durationSec,_this.restAfterSec);
}

@override
String toString() {
  final _this = this as ProgramBlock;
  return 'ProgramBlock(gameId: ${_this.gameId}, gameVersionRange: ${_this.gameVersionRange}, params: ${_this.params}, durationSec: ${_this.durationSec}, restAfterSec: ${_this.restAfterSec})';
}


}

/// @nodoc
abstract mixin class $ProgramBlockCopyWith<$Res>  {
  factory $ProgramBlockCopyWith(ProgramBlock value, $Res Function(ProgramBlock) _then) = _$ProgramBlockCopyWithImpl;
@useResult
$Res call({
 String gameId, String gameVersionRange, Map<String, dynamic> params, int durationSec, int restAfterSec
});




}
/// @nodoc
class _$ProgramBlockCopyWithImpl<$Res>
    implements $ProgramBlockCopyWith<$Res> {
  _$ProgramBlockCopyWithImpl(this._self, this._then);

  final ProgramBlock _self;
  final $Res Function(ProgramBlock) _then;

/// Create a copy of ProgramBlock
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? gameId = null,Object? gameVersionRange = null,Object? params = null,Object? durationSec = null,Object? restAfterSec = null,}) {
  return _then(ProgramBlock(
gameId: null == gameId ? _self.gameId : gameId // ignore: cast_nullable_to_non_nullable
as String,gameVersionRange: null == gameVersionRange ? _self.gameVersionRange : gameVersionRange // ignore: cast_nullable_to_non_nullable
as String,params: null == params ? _self.params : params // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,durationSec: null == durationSec ? _self.durationSec : durationSec // ignore: cast_nullable_to_non_nullable
as int,restAfterSec: null == restAfterSec ? _self.restAfterSec : restAfterSec // ignore: cast_nullable_to_non_nullable
as int,
  ));
}

}


/// Adds pattern-matching-related methods to [ProgramBlock].
extension ProgramBlockPatterns on ProgramBlock {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ProgramBlock value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ProgramBlock() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ProgramBlock value)  $default,){
final _that = this;
switch (_that) {
case _ProgramBlock():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ProgramBlock value)?  $default,){
final _that = this;
switch (_that) {
case _ProgramBlock() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String gameId,  String gameVersionRange,  Map<String, dynamic> params,  int durationSec,  int restAfterSec)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ProgramBlock() when $default != null:
return $default(_that.gameId,_that.gameVersionRange,_that.params,_that.durationSec,_that.restAfterSec);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String gameId,  String gameVersionRange,  Map<String, dynamic> params,  int durationSec,  int restAfterSec)  $default,) {final _that = this;
switch (_that) {
case _ProgramBlock():
return $default(_that.gameId,_that.gameVersionRange,_that.params,_that.durationSec,_that.restAfterSec);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String gameId,  String gameVersionRange,  Map<String, dynamic> params,  int durationSec,  int restAfterSec)?  $default,) {final _that = this;
switch (_that) {
case _ProgramBlock() when $default != null:
return $default(_that.gameId,_that.gameVersionRange,_that.params,_that.durationSec,_that.restAfterSec);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ProgramBlock implements ProgramBlock {
  const _ProgramBlock({required this.gameId, required this.gameVersionRange, required  Map<String, dynamic> params, this.durationSec = 180, this.restAfterSec = 30}): _params = params;
  factory _ProgramBlock.fromJson(Map<String, dynamic> json) => _$ProgramBlockFromJson(json);

@override final  String gameId;
@override final  String gameVersionRange;
 final  Map<String, dynamic> _params;
@override Map<String, dynamic> get params {
  if (_params is EqualUnmodifiableMapView) return _params;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableMapView(_params);
}

@override@JsonKey() final  int durationSec;
@override@JsonKey() final  int restAfterSec;

/// Create a copy of ProgramBlock
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ProgramBlockCopyWith<_ProgramBlock> get copyWith => __$ProgramBlockCopyWithImpl<_ProgramBlock>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ProgramBlockToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _ProgramBlock&&(identical(other.gameId, gameId) || other.gameId == gameId)&&(identical(other.gameVersionRange, gameVersionRange) || other.gameVersionRange == gameVersionRange)&&const DeepCollectionEquality().equals(other.params, _params)&&(identical(other.durationSec, durationSec) || other.durationSec == durationSec)&&(identical(other.restAfterSec, restAfterSec) || other.restAfterSec == restAfterSec));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,gameId,gameVersionRange,const DeepCollectionEquality().hash(_params),durationSec,restAfterSec);
}

@override
String toString() {
    return 'ProgramBlock(gameId: $gameId, gameVersionRange: $gameVersionRange, params: $params, durationSec: $durationSec, restAfterSec: $restAfterSec)';
}


}

/// @nodoc
abstract mixin class _$ProgramBlockCopyWith<$Res> implements $ProgramBlockCopyWith<$Res> {
  factory _$ProgramBlockCopyWith(_ProgramBlock value, $Res Function(_ProgramBlock) _then) = __$ProgramBlockCopyWithImpl;
@override @useResult
$Res call({
 String gameId, String gameVersionRange, Map<String, dynamic> params, int durationSec, int restAfterSec
});




}
/// @nodoc
class __$ProgramBlockCopyWithImpl<$Res>
    implements _$ProgramBlockCopyWith<$Res> {
  __$ProgramBlockCopyWithImpl(this._self, this._then);

  final _ProgramBlock _self;
  final $Res Function(_ProgramBlock) _then;

/// Create a copy of ProgramBlock
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? gameId = null,Object? gameVersionRange = null,Object? params = null,Object? durationSec = null,Object? restAfterSec = null,}) {
  return _then(_ProgramBlock(
gameId: null == gameId ? _self.gameId : gameId // ignore: cast_nullable_to_non_nullable
as String,gameVersionRange: null == gameVersionRange ? _self.gameVersionRange : gameVersionRange // ignore: cast_nullable_to_non_nullable
as String,params: null == params ? _self._params : params // ignore: cast_nullable_to_non_nullable
as Map<String, dynamic>,durationSec: null == durationSec ? _self.durationSec : durationSec // ignore: cast_nullable_to_non_nullable
as int,restAfterSec: null == restAfterSec ? _self.restAfterSec : restAfterSec // ignore: cast_nullable_to_non_nullable
as int,
  ));
}


}


/// @nodoc
mixin _$Program {

 String get programId; String get patientRef; String get createdBy; ProgramSchedule get schedule; List<ProgramBlock> get blocks; String? get title; String? get notesForPatient; bool get requiresSupervision; ProgramStatus get status;
/// Create a copy of Program
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ProgramCopyWith<Program> get copyWith => _$ProgramCopyWithImpl<Program>(this as Program, _$identity);

  /// Serializes this Program to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as Program;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is Program&&(identical(other.programId, _this.programId) || other.programId == _this.programId)&&(identical(other.patientRef, _this.patientRef) || other.patientRef == _this.patientRef)&&(identical(other.createdBy, _this.createdBy) || other.createdBy == _this.createdBy)&&(identical(other.schedule, _this.schedule) || other.schedule == _this.schedule)&&const DeepCollectionEquality().equals(other.blocks, _this.blocks)&&(identical(other.title, _this.title) || other.title == _this.title)&&(identical(other.notesForPatient, _this.notesForPatient) || other.notesForPatient == _this.notesForPatient)&&(identical(other.requiresSupervision, _this.requiresSupervision) || other.requiresSupervision == _this.requiresSupervision)&&(identical(other.status, _this.status) || other.status == _this.status));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as Program;
  return Object.hash(runtimeType,_this.programId,_this.patientRef,_this.createdBy,_this.schedule,const DeepCollectionEquality().hash(_this.blocks),_this.title,_this.notesForPatient,_this.requiresSupervision,_this.status);
}

@override
String toString() {
  final _this = this as Program;
  return 'Program(programId: ${_this.programId}, patientRef: ${_this.patientRef}, createdBy: ${_this.createdBy}, schedule: ${_this.schedule}, blocks: ${_this.blocks}, title: ${_this.title}, notesForPatient: ${_this.notesForPatient}, requiresSupervision: ${_this.requiresSupervision}, status: ${_this.status})';
}


}

/// @nodoc
abstract mixin class $ProgramCopyWith<$Res>  {
  factory $ProgramCopyWith(Program value, $Res Function(Program) _then) = _$ProgramCopyWithImpl;
@useResult
$Res call({
 String programId, String patientRef, String createdBy, ProgramSchedule schedule, List<ProgramBlock> blocks, String? title, String? notesForPatient, bool requiresSupervision, ProgramStatus status
});


$ProgramScheduleCopyWith<$Res> get schedule;

}
/// @nodoc
class _$ProgramCopyWithImpl<$Res>
    implements $ProgramCopyWith<$Res> {
  _$ProgramCopyWithImpl(this._self, this._then);

  final Program _self;
  final $Res Function(Program) _then;

/// Create a copy of Program
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? programId = null,Object? patientRef = null,Object? createdBy = null,Object? schedule = null,Object? blocks = null,Object? title = freezed,Object? notesForPatient = freezed,Object? requiresSupervision = null,Object? status = null,}) {
  return _then(Program(
programId: null == programId ? _self.programId : programId // ignore: cast_nullable_to_non_nullable
as String,patientRef: null == patientRef ? _self.patientRef : patientRef // ignore: cast_nullable_to_non_nullable
as String,createdBy: null == createdBy ? _self.createdBy : createdBy // ignore: cast_nullable_to_non_nullable
as String,schedule: null == schedule ? _self.schedule : schedule // ignore: cast_nullable_to_non_nullable
as ProgramSchedule,blocks: null == blocks ? _self.blocks : blocks // ignore: cast_nullable_to_non_nullable
as List<ProgramBlock>,title: freezed == title ? _self.title : title // ignore: cast_nullable_to_non_nullable
as String?,notesForPatient: freezed == notesForPatient ? _self.notesForPatient : notesForPatient // ignore: cast_nullable_to_non_nullable
as String?,requiresSupervision: null == requiresSupervision ? _self.requiresSupervision : requiresSupervision // ignore: cast_nullable_to_non_nullable
as bool,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as ProgramStatus,
  ));
}
/// Create a copy of Program
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$ProgramScheduleCopyWith<$Res> get schedule {
  
  return $ProgramScheduleCopyWith<$Res>(_self.schedule, (value) {
    return _then(_self.copyWith(schedule: value));
  });
}
}


/// Adds pattern-matching-related methods to [Program].
extension ProgramPatterns on Program {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _Program value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _Program() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _Program value)  $default,){
final _that = this;
switch (_that) {
case _Program():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _Program value)?  $default,){
final _that = this;
switch (_that) {
case _Program() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String programId,  String patientRef,  String createdBy,  ProgramSchedule schedule,  List<ProgramBlock> blocks,  String? title,  String? notesForPatient,  bool requiresSupervision,  ProgramStatus status)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _Program() when $default != null:
return $default(_that.programId,_that.patientRef,_that.createdBy,_that.schedule,_that.blocks,_that.title,_that.notesForPatient,_that.requiresSupervision,_that.status);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String programId,  String patientRef,  String createdBy,  ProgramSchedule schedule,  List<ProgramBlock> blocks,  String? title,  String? notesForPatient,  bool requiresSupervision,  ProgramStatus status)  $default,) {final _that = this;
switch (_that) {
case _Program():
return $default(_that.programId,_that.patientRef,_that.createdBy,_that.schedule,_that.blocks,_that.title,_that.notesForPatient,_that.requiresSupervision,_that.status);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String programId,  String patientRef,  String createdBy,  ProgramSchedule schedule,  List<ProgramBlock> blocks,  String? title,  String? notesForPatient,  bool requiresSupervision,  ProgramStatus status)?  $default,) {final _that = this;
switch (_that) {
case _Program() when $default != null:
return $default(_that.programId,_that.patientRef,_that.createdBy,_that.schedule,_that.blocks,_that.title,_that.notesForPatient,_that.requiresSupervision,_that.status);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _Program implements Program {
  const _Program({required this.programId, required this.patientRef, required this.createdBy, required this.schedule, required  List<ProgramBlock> blocks, this.title, this.notesForPatient, this.requiresSupervision = false, this.status = ProgramStatus.draft}): _blocks = blocks;
  factory _Program.fromJson(Map<String, dynamic> json) => _$ProgramFromJson(json);

@override final  String programId;
@override final  String patientRef;
@override final  String createdBy;
@override final  ProgramSchedule schedule;
 final  List<ProgramBlock> _blocks;
@override List<ProgramBlock> get blocks {
  if (_blocks is EqualUnmodifiableListView) return _blocks;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_blocks);
}

@override final  String? title;
@override final  String? notesForPatient;
@override@JsonKey() final  bool requiresSupervision;
@override@JsonKey() final  ProgramStatus status;

/// Create a copy of Program
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ProgramCopyWith<_Program> get copyWith => __$ProgramCopyWithImpl<_Program>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ProgramToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _Program&&(identical(other.programId, programId) || other.programId == programId)&&(identical(other.patientRef, patientRef) || other.patientRef == patientRef)&&(identical(other.createdBy, createdBy) || other.createdBy == createdBy)&&(identical(other.schedule, schedule) || other.schedule == schedule)&&const DeepCollectionEquality().equals(other.blocks, _blocks)&&(identical(other.title, title) || other.title == title)&&(identical(other.notesForPatient, notesForPatient) || other.notesForPatient == notesForPatient)&&(identical(other.requiresSupervision, requiresSupervision) || other.requiresSupervision == requiresSupervision)&&(identical(other.status, status) || other.status == status));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,programId,patientRef,createdBy,schedule,const DeepCollectionEquality().hash(_blocks),title,notesForPatient,requiresSupervision,status);
}

@override
String toString() {
    return 'Program(programId: $programId, patientRef: $patientRef, createdBy: $createdBy, schedule: $schedule, blocks: $blocks, title: $title, notesForPatient: $notesForPatient, requiresSupervision: $requiresSupervision, status: $status)';
}


}

/// @nodoc
abstract mixin class _$ProgramCopyWith<$Res> implements $ProgramCopyWith<$Res> {
  factory _$ProgramCopyWith(_Program value, $Res Function(_Program) _then) = __$ProgramCopyWithImpl;
@override @useResult
$Res call({
 String programId, String patientRef, String createdBy, ProgramSchedule schedule, List<ProgramBlock> blocks, String? title, String? notesForPatient, bool requiresSupervision, ProgramStatus status
});


@override $ProgramScheduleCopyWith<$Res> get schedule;

}
/// @nodoc
class __$ProgramCopyWithImpl<$Res>
    implements _$ProgramCopyWith<$Res> {
  __$ProgramCopyWithImpl(this._self, this._then);

  final _Program _self;
  final $Res Function(_Program) _then;

/// Create a copy of Program
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? programId = null,Object? patientRef = null,Object? createdBy = null,Object? schedule = null,Object? blocks = null,Object? title = freezed,Object? notesForPatient = freezed,Object? requiresSupervision = null,Object? status = null,}) {
  return _then(_Program(
programId: null == programId ? _self.programId : programId // ignore: cast_nullable_to_non_nullable
as String,patientRef: null == patientRef ? _self.patientRef : patientRef // ignore: cast_nullable_to_non_nullable
as String,createdBy: null == createdBy ? _self.createdBy : createdBy // ignore: cast_nullable_to_non_nullable
as String,schedule: null == schedule ? _self.schedule : schedule // ignore: cast_nullable_to_non_nullable
as ProgramSchedule,blocks: null == blocks ? _self._blocks : blocks // ignore: cast_nullable_to_non_nullable
as List<ProgramBlock>,title: freezed == title ? _self.title : title // ignore: cast_nullable_to_non_nullable
as String?,notesForPatient: freezed == notesForPatient ? _self.notesForPatient : notesForPatient // ignore: cast_nullable_to_non_nullable
as String?,requiresSupervision: null == requiresSupervision ? _self.requiresSupervision : requiresSupervision // ignore: cast_nullable_to_non_nullable
as bool,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as ProgramStatus,
  ));
}

/// Create a copy of Program
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$ProgramScheduleCopyWith<$Res> get schedule {
  
  return $ProgramScheduleCopyWith<$Res>(_self.schedule, (value) {
    return _then(_self.copyWith(schedule: value));
  });
}
}

// dart format on
