import 'package:opus_app/data/models/user.dart';
import 'package:opus_app/data/repositories/auth_repository.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';

class MockAuthRepository implements AuthRepository {
  new({FixtureLoader? loader}) : _loader = loader ?? const FixtureLoader();

  final FixtureLoader _loader;
  AppUser? _currentUser;

  static const Map<AppRole, String> _roleNames = {
    AppRole.admin: 'Admin User',
    AppRole.clinician: 'Dr. Kavita Rao',
    AppRole.therapist: 'Amit Joshi, OT',
    AppRole.nurse: 'Nurse Fatima Khan',
    AppRole.patient: 'Priya Nair',
  };

  @override
  Future<AppUser> signIn(AppRole role, {String? displayName}) async {
    await _loader.latency();
    final user = AppUser(id: 'mock-${role.name}', displayName: displayName ?? _roleNames[role]!, role: role);
    _currentUser = user;
    return user;
  }

  @override
  Future<void> signOut() async {
    await _loader.latency();
    _currentUser = null;
  }

  @override
  AppUser? get currentUser => _currentUser;
}
