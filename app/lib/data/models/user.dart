import 'package:freezed_annotation/freezed_annotation.dart';

part 'user.freezed.dart';
part 'user.g.dart';

/// Mock roles per brief A-flutter-app.md §A3. Phase 3 maps these to the
/// backend's RBAC roles 1:1 -- keep this enum in sync with the OpenAPI schema
/// when that lands.
enum AppRole { admin, clinician, therapist, nurse, patient }

@freezed
abstract class AppUser with _$AppUser {
  const factory({
    required String id,
    required String displayName,
    required AppRole role,
  }) = _AppUser;

  factory fromJson(Map<String, Object?> json) => _$AppUserFromJson(json);
}
