// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint, type=warning, deprecated_member_use, deprecated_member_use_from_same_package
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'patient.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$Patient {

 String get id; String get displayName; int get age; String get gender; AffectedSide get affectedSide; String get diagnosis; DateTime get onsetDate; List<String> get programIds; List<String> get sessionIds;
/// Create a copy of Patient
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PatientCopyWith<Patient> get copyWith => _$PatientCopyWithImpl<Patient>(this as Patient, _$identity);

  /// Serializes this Patient to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  final _this = this as Patient;
  return identical(this, other) || (other.runtimeType == runtimeType&&other is Patient&&(identical(other.id, _this.id) || other.id == _this.id)&&(identical(other.displayName, _this.displayName) || other.displayName == _this.displayName)&&(identical(other.age, _this.age) || other.age == _this.age)&&(identical(other.gender, _this.gender) || other.gender == _this.gender)&&(identical(other.affectedSide, _this.affectedSide) || other.affectedSide == _this.affectedSide)&&(identical(other.diagnosis, _this.diagnosis) || other.diagnosis == _this.diagnosis)&&(identical(other.onsetDate, _this.onsetDate) || other.onsetDate == _this.onsetDate)&&const DeepCollectionEquality().equals(other.programIds, _this.programIds)&&const DeepCollectionEquality().equals(other.sessionIds, _this.sessionIds));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
  final _this = this as Patient;
  return Object.hash(runtimeType,_this.id,_this.displayName,_this.age,_this.gender,_this.affectedSide,_this.diagnosis,_this.onsetDate,const DeepCollectionEquality().hash(_this.programIds),const DeepCollectionEquality().hash(_this.sessionIds));
}

@override
String toString() {
  final _this = this as Patient;
  return 'Patient(id: ${_this.id}, displayName: ${_this.displayName}, age: ${_this.age}, gender: ${_this.gender}, affectedSide: ${_this.affectedSide}, diagnosis: ${_this.diagnosis}, onsetDate: ${_this.onsetDate}, programIds: ${_this.programIds}, sessionIds: ${_this.sessionIds})';
}


}

/// @nodoc
abstract mixin class $PatientCopyWith<$Res>  {
  factory $PatientCopyWith(Patient value, $Res Function(Patient) _then) = _$PatientCopyWithImpl;
@useResult
$Res call({
 String id, String displayName, int age, String gender, AffectedSide affectedSide, String diagnosis, DateTime onsetDate, List<String> programIds, List<String> sessionIds
});




}
/// @nodoc
class _$PatientCopyWithImpl<$Res>
    implements $PatientCopyWith<$Res> {
  _$PatientCopyWithImpl(this._self, this._then);

  final Patient _self;
  final $Res Function(Patient) _then;

/// Create a copy of Patient
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? displayName = null,Object? age = null,Object? gender = null,Object? affectedSide = null,Object? diagnosis = null,Object? onsetDate = null,Object? programIds = null,Object? sessionIds = null,}) {
  return _then(Patient(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,displayName: null == displayName ? _self.displayName : displayName // ignore: cast_nullable_to_non_nullable
as String,age: null == age ? _self.age : age // ignore: cast_nullable_to_non_nullable
as int,gender: null == gender ? _self.gender : gender // ignore: cast_nullable_to_non_nullable
as String,affectedSide: null == affectedSide ? _self.affectedSide : affectedSide // ignore: cast_nullable_to_non_nullable
as AffectedSide,diagnosis: null == diagnosis ? _self.diagnosis : diagnosis // ignore: cast_nullable_to_non_nullable
as String,onsetDate: null == onsetDate ? _self.onsetDate : onsetDate // ignore: cast_nullable_to_non_nullable
as DateTime,programIds: null == programIds ? _self.programIds : programIds // ignore: cast_nullable_to_non_nullable
as List<String>,sessionIds: null == sessionIds ? _self.sessionIds : sessionIds // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}

}


/// Adds pattern-matching-related methods to [Patient].
extension PatientPatterns on Patient {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _Patient value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _Patient() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _Patient value)  $default,){
final _that = this;
switch (_that) {
case _Patient():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _Patient value)?  $default,){
final _that = this;
switch (_that) {
case _Patient() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String id,  String displayName,  int age,  String gender,  AffectedSide affectedSide,  String diagnosis,  DateTime onsetDate,  List<String> programIds,  List<String> sessionIds)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _Patient() when $default != null:
return $default(_that.id,_that.displayName,_that.age,_that.gender,_that.affectedSide,_that.diagnosis,_that.onsetDate,_that.programIds,_that.sessionIds);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String id,  String displayName,  int age,  String gender,  AffectedSide affectedSide,  String diagnosis,  DateTime onsetDate,  List<String> programIds,  List<String> sessionIds)  $default,) {final _that = this;
switch (_that) {
case _Patient():
return $default(_that.id,_that.displayName,_that.age,_that.gender,_that.affectedSide,_that.diagnosis,_that.onsetDate,_that.programIds,_that.sessionIds);case _:
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String id,  String displayName,  int age,  String gender,  AffectedSide affectedSide,  String diagnosis,  DateTime onsetDate,  List<String> programIds,  List<String> sessionIds)?  $default,) {final _that = this;
switch (_that) {
case _Patient() when $default != null:
return $default(_that.id,_that.displayName,_that.age,_that.gender,_that.affectedSide,_that.diagnosis,_that.onsetDate,_that.programIds,_that.sessionIds);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _Patient implements Patient {
  const _Patient({required this.id, required this.displayName, required this.age, required this.gender, required this.affectedSide, required this.diagnosis, required this.onsetDate,  List<String> programIds = const [],  List<String> sessionIds = const []}): _programIds = programIds,_sessionIds = sessionIds;
  factory _Patient.fromJson(Map<String, dynamic> json) => _$PatientFromJson(json);

@override final  String id;
@override final  String displayName;
@override final  int age;
@override final  String gender;
@override final  AffectedSide affectedSide;
@override final  String diagnosis;
@override final  DateTime onsetDate;
 final  List<String> _programIds;
@override@JsonKey() List<String> get programIds {
  if (_programIds is EqualUnmodifiableListView) return _programIds;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_programIds);
}

 final  List<String> _sessionIds;
@override@JsonKey() List<String> get sessionIds {
  if (_sessionIds is EqualUnmodifiableListView) return _sessionIds;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_sessionIds);
}


/// Create a copy of Patient
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$PatientCopyWith<_Patient> get copyWith => __$PatientCopyWithImpl<_Patient>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$PatientToJson(this, );
}

@override
bool operator ==(Object other) {
    return identical(this, other) || (other.runtimeType == runtimeType&&other is _Patient&&(identical(other.id, id) || other.id == id)&&(identical(other.displayName, displayName) || other.displayName == displayName)&&(identical(other.age, age) || other.age == age)&&(identical(other.gender, gender) || other.gender == gender)&&(identical(other.affectedSide, affectedSide) || other.affectedSide == affectedSide)&&(identical(other.diagnosis, diagnosis) || other.diagnosis == diagnosis)&&(identical(other.onsetDate, onsetDate) || other.onsetDate == onsetDate)&&const DeepCollectionEquality().equals(other.programIds, _programIds)&&const DeepCollectionEquality().equals(other.sessionIds, _sessionIds));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode {
    return Object.hash(runtimeType,id,displayName,age,gender,affectedSide,diagnosis,onsetDate,const DeepCollectionEquality().hash(_programIds),const DeepCollectionEquality().hash(_sessionIds));
}

@override
String toString() {
    return 'Patient(id: $id, displayName: $displayName, age: $age, gender: $gender, affectedSide: $affectedSide, diagnosis: $diagnosis, onsetDate: $onsetDate, programIds: $programIds, sessionIds: $sessionIds)';
}


}

/// @nodoc
abstract mixin class _$PatientCopyWith<$Res> implements $PatientCopyWith<$Res> {
  factory _$PatientCopyWith(_Patient value, $Res Function(_Patient) _then) = __$PatientCopyWithImpl;
@override @useResult
$Res call({
 String id, String displayName, int age, String gender, AffectedSide affectedSide, String diagnosis, DateTime onsetDate, List<String> programIds, List<String> sessionIds
});




}
/// @nodoc
class __$PatientCopyWithImpl<$Res>
    implements _$PatientCopyWith<$Res> {
  __$PatientCopyWithImpl(this._self, this._then);

  final _Patient _self;
  final $Res Function(_Patient) _then;

/// Create a copy of Patient
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? displayName = null,Object? age = null,Object? gender = null,Object? affectedSide = null,Object? diagnosis = null,Object? onsetDate = null,Object? programIds = null,Object? sessionIds = null,}) {
  return _then(_Patient(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,displayName: null == displayName ? _self.displayName : displayName // ignore: cast_nullable_to_non_nullable
as String,age: null == age ? _self.age : age // ignore: cast_nullable_to_non_nullable
as int,gender: null == gender ? _self.gender : gender // ignore: cast_nullable_to_non_nullable
as String,affectedSide: null == affectedSide ? _self.affectedSide : affectedSide // ignore: cast_nullable_to_non_nullable
as AffectedSide,diagnosis: null == diagnosis ? _self.diagnosis : diagnosis // ignore: cast_nullable_to_non_nullable
as String,onsetDate: null == onsetDate ? _self.onsetDate : onsetDate // ignore: cast_nullable_to_non_nullable
as DateTime,programIds: null == programIds ? _self._programIds : programIds // ignore: cast_nullable_to_non_nullable
as List<String>,sessionIds: null == sessionIds ? _self._sessionIds : sessionIds // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}


}

// dart format on
