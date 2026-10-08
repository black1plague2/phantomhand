import 'package:go_router/go_router.dart';
import 'package:opus_app/features/devices/devices_screen.dart';

/// The devices feature's own [RouteBase] list (`docs/IMPROVEMENT_BRIEF.md` §3).
final List<RouteBase> deviceRoutes = [
  GoRoute(path: '/devices', builder: (context, state) => const DevicesScreen()),
];
