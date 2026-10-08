import 'package:go_router/go_router.dart';
import 'package:opus_app/features/settings/settings_screen.dart';

/// The settings feature's own [RouteBase] list (`docs/IMPROVEMENT_BRIEF.md` §3).
final List<RouteBase> settingsRoutes = [
  GoRoute(path: '/settings', builder: (context, state) => const SettingsScreen()),
];
