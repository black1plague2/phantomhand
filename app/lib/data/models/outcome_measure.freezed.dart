// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint, type=warning, deprecated_member_use, deprecated_member_use_from_same_package
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'outcome_measure.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$OutcomeMeasureEntry {

 String get id; String get patientId; OutcomeMeasureType get type; DateTime get date; double get score; String? get side; String? get enteredBy; String? get notes;
/// Create a copy of OutcomeMeasureEntry
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$OutcomeMeasureEntryCopyWith<OutcomeMeasureEntry> get copyWith => _$OutcomeMeasureEntryCopyWithImpl<OutcomeMeasureEntry>(this as OutcomeMeasureEntry, _$identity);

  /// Serializes this OutcomeMeasureEntry to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as OutcomeMeasureEntry;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is OutcomeMeasureEntry&&(identical(other.id, _this.id) || other.id == _this.id)&&(identical(other.patientId, _this.patientId) || other.patientId == _this.patientId)&&(identical(other.type, _this.type) || other.type == _this.type)&&(identical(other.date, _this.date) || other.date == _this.date)&&(identical(other.score, _this.score) || other.score == _this.score)&&(identical(other.side, _this.side) || other.side == _this.side)&&(identical(other.enteredBy, _this.enteredBy) || other.enteredBy == _this.enteredBy)&&(identical(other.notes, _this.notes) || other.notes == _this.notes));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as OutcomeMeasureEntry;
  return Object.hash(runtimeType,_this.id,_this.patientId,_this.type,_this.date,_this.score,_this.side,_this.enteredBy,_this.notes);
}

@override
String toString() {
  final _this = this as OutcomeMeasureEntry;
  return 'OutcomeMeasureEntry(id: ${_this.id}, patientId: ${_this.patientId}, type: ${_this.type}, date: ${_this.date}, score: ${_this.score}, side: ${_this.side}, enteredBy: ${_this.enteredBy}, notes: ${_this.notes})';
}


}

/// @nodoc
abstract mixin class $OutcomeMeasureEntryCopyWith<$Res>  {
  factory $OutcomeMeasureEntryCopyWith(OutcomeMeasureEntry value, $Res Function(OutcomeMeasureEntry) _then) = _$OutcomeMeasureEntryCopyWithImpl;
@useResult
$Res call({
 String id, String patientId, OutcomeMeasureType type, DateTime date, double score, String? side, String? enteredBy, String? notes
});




}
/// @nodoc
class _$OutcomeMeasureEntryCopyWithImpl<$Res>
    implements $OutcomeMeasureEntryCopyWith<$Res> {
  _$OutcomeMeasureEntryCopyWithImpl(this._self, this._then);

  final OutcomeMeasureEntry _self;
  final $Res Function(OutcomeMeasureEntry) _then;

/// Create a copy of OutcomeMeasureEntry
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? patientId = null,Object? type = null,Object? date = null,Object? score = null,Object? side = freezed,Object? enteredBy = freezed,Object? notes = freezed,}) {
  return _then(OutcomeMeasureEntry(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,patientId: null == patientId ? _self.patientId : patientId // ignore: cast_nullable_to_non_nullable
as String,type: null == type ? _self.type : type // ignore: cast_nullable_to_non_nullable
as OutcomeMeasureType,date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as DateTime,score: null == score ? _self.score : score // ignore: cast_nullable_to_non_nullable
as double,side: freezed == side ? _self.side : side // ignore: cast_nullable_to_non_nullable
as String?,enteredBy: freezed == enteredBy ? _self.enteredBy : enteredBy // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [OutcomeMeasureEntry].
extension OutcomeMeasureEntryPatterns on OutcomeMeasureEntry {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _OutcomeMeasureEntry value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _OutcomeMeasureEntry() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _OutcomeMeasureEntry value)  $default,){
final _that = this;
switch (_that) {
case _OutcomeMeasureEntry():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _OutcomeMeasureEntry value)?  $default,){
final _that = this;
switch (_that) {
case _OutcomeMeasureEntry() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String id,  String patientId,  OutcomeMeasureType type,  DateTime date,  double score,  String? side,  String? enteredBy,  String? notes)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _OutcomeMeasureEntry() when $default != null:
return $default(_that.id,_that.patientId,_that.type,_that.date,_that.score,_that.side,_that.enteredBy,_that.notes);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String id,  String patientId,  OutcomeMeasureType type,  DateTime date,  double score,  String? side,  String? enteredBy,  String? notes)  $default,) {final _that = this;
switch (_that) {
case _OutcomeMeasureEntry():
return $default(_that.id,_that.patientId,_that.type,_that.date,_that.score,_that.side,_that.enteredBy,_that.notes);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String id,  String patientId,  OutcomeMeasureType type,  DateTime date,  double score,  String? side,  String? enteredBy,  String? notes)?  $default,) {final _that = this;
switch (_that) {
case _OutcomeMeasureEntry() when $default != null:
return $default(_that.id,_that.patientId,_that.type,_that.date,_that.score,_that.side,_that.enteredBy,_that.notes);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _OutcomeMeasureEntry implements OutcomeMeasureEntry {
  const _OutcomeMeasureEntry({required this.id, required this.patientId, required this.type, required this.date, required this.score, this.side, this.enteredBy, this.notes});
  factory _OutcomeMeasureEntry.fromJson(Map<String, dynamic> json) => _$OutcomeMeasureEntryFromJson(json);

@override final  String id;
@override final  String patientId;
@override final  OutcomeMeasureType type;
@override final  DateTime date;
@override final  double score;
@override final  String? side;
@override final  String? enteredBy;
@override final  String? notes;

/// Create a copy of OutcomeMeasureEntry
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$OutcomeMeasureEntryCopyWith<_OutcomeMeasureEntry> get copyWith => __$OutcomeMeasureEntryCopyWithImpl<_OutcomeMeasureEntry>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$OutcomeMeasureEntryToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _OutcomeMeasureEntry&&(identical(other.id, id) || other.id == id)&&(identical(other.patientId, patientId) || other.patientId == patientId)&&(identical(other.type, type) || other.type == type)&&(identical(other.date, date) || other.date == date)&&(identical(other.score, score) || other.score == score)&&(identical(other.side, side) || other.side == side)&&(identical(other.enteredBy, enteredBy) || other.enteredBy == enteredBy)&&(identical(other.notes, notes) || other.notes == notes));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,id,patientId,type,date,score,side,enteredBy,notes);
}

@override
String toString() {
    return 'OutcomeMeasureEntry(id: $id, patientId: $patientId, type: $type, date: $date, score: $score, side: $side, enteredBy: $enteredBy, notes: $notes)';
}


}

/// @nodoc
abstract mixin class _$OutcomeMeasureEntryCopyWith<$Res> implements $OutcomeMeasureEntryCopyWith<$Res> {
  factory _$OutcomeMeasureEntryCopyWith(_OutcomeMeasureEntry value, $Res Function(_OutcomeMeasureEntry) _then) = __$OutcomeMeasureEntryCopyWithImpl;
@override @useResult
$Res call({
 String id, String patientId, OutcomeMeasureType type, DateTime date, double score, String? side, String? enteredBy, String? notes
});




}
/// @nodoc
class __$OutcomeMeasureEntryCopyWithImpl<$Res>
    implements _$OutcomeMeasureEntryCopyWith<$Res> {
  __$OutcomeMeasureEntryCopyWithImpl(this._self, this._then);

  final _OutcomeMeasureEntry _self;
  final $Res Function(_OutcomeMeasureEntry) _then;

/// Create a copy of OutcomeMeasureEntry
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? patientId = null,Object? type = null,Object? date = null,Object? score = null,Object? side = freezed,Object? enteredBy = freezed,Object? notes = freezed,}) {
  return _then(_OutcomeMeasureEntry(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,patientId: null == patientId ? _self.patientId : patientId // ignore: cast_nullable_to_non_nullable
as String,type: null == type ? _self.type : type // ignore: cast_nullable_to_non_nullable
as OutcomeMeasureType,date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as DateTime,score: null == score ? _self.score : score // ignore: cast_nullable_to_non_nullable
as double,side: freezed == side ? _self.side : side // ignore: cast_nullable_to_non_nullable
as String?,enteredBy: freezed == enteredBy ? _self.enteredBy : enteredBy // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
