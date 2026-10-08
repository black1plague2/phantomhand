import 'package:go_router/go_router.dart';
import 'package:opus_app/features/auth/login_screen.dart';

/// The auth feature's own [RouteBase] list (`docs/IMPROVEMENT_BRIEF.md` §3:
/// "each feature folder ... exports its own RouteBase list that the shell
/// composes"). Deliberately **not** nested inside the shell's `ShellRoute` --
/// login has no nav rail/bottom bar -- so `app_router.dart` spreads this list
/// as a top-level sibling of the shell, not inside it.
final List<RouteBase> authRoutes = [
  GoRoute(path: '/login', builder: (context, state) => const LoginScreen()),
];
