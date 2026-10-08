// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'user.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AppUser _$AppUserFromJson(Map<String, dynamic> json) => _AppUser(
  id: json['id'] as String,
  displayName: json['display_name'] as String,
  role: $enumDecode(_$AppRoleEnumMap, json['role']),
);

Map<String, dynamic> _$AppUserToJson(_AppUser instance) => <String, dynamic>{
  'id': instance.id,
  'display_name': instance.displayName,
  'role': _$AppRoleEnumMap[instance.role]!,
};

const _$AppRoleEnumMap = {
  AppRole.admin: 'admin',
  AppRole.clinician: 'clinician',
  AppRole.therapist: 'therapist',
  AppRole.nurse: 'nurse',
  AppRole.patient: 'patient',
};
