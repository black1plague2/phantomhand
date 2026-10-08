import 'package:freezed_annotation/freezed_annotation.dart';

part 'patient.freezed.dart';
part 'patient.g.dart';

enum AffectedSide { left, right, both, none }

@freezed
abstract class Patient with _$Patient {
  const factory({
    required String id,
    required String displayName,
    required int age,
    required String gender,
    required AffectedSide affectedSide,
    required String diagnosis,
    required DateTime onsetDate,
    @Default([]) List<String> programIds,
    @Default([]) List<String> sessionIds,
  }) = _Patient;

  factory fromJson(Map<String, Object?> json) => _$PatientFromJson(json);
}
