import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/user.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'auth_controller.g.dart';

@Riverpod(keepAlive: true)
class AuthController extends _$AuthController {
  @override
  AppUser? build() => ref.watch(authRepositoryProvider).currentUser;

  Future<void> signIn(AppRole role) async {
    final user = await ref.read(authRepositoryProvider).signIn(role);
    state = user;
  }

  Future<void> signOut() async {
    await ref.read(authRepositoryProvider).signOut();
    state = null;
  }
}
