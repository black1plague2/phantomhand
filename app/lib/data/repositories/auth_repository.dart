import 'package:opus_app/data/models/user.dart';

/// Phase 3 swaps `MockAuthRepository` for one backed by the OIDC/JWT flow
/// described in ARCHITECTURE.md §7. Nothing above this interface should
/// change.
abstract class AuthRepository {
  Future<AppUser> signIn(AppRole role, {String? displayName});
  Future<void> signOut();
  AppUser? get currentUser;
}
