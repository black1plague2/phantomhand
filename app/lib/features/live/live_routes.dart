import 'package:go_router/go_router.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/features/live/live_monitor_screen.dart';
import 'package:opus_app/features/live/monitor_home_screen.dart';
import 'package:opus_app/features/live/phantom_demo_screen.dart';

/// The live-monitor feature's own [RouteBase] list (`docs/IMPROVEMENT_BRIEF.md`
/// §3). See `patient_routes.dart`'s doc comment for why owning a
/// `/patients/:patientId/...` path from a different feature folder is fine.
///
/// `/monitor` is the top-level Monitor tab (Opus decision 2026-09-19: bottom
/// nav = Patients / Monitor / Programs / Reports) -- wired here since this
/// feature already owns the live-monitor screen it delegates to.
final List<RouteBase> liveRoutes = [
  GoRoute(
    path: '/monitor',
    builder: (context, state) => const MonitorHomeScreen(),
  ),
  // "Run Phantom Hand demo": the recorded run, the results mirror, then the demo participant's report.
  GoRoute(
    path: demoPhantomRoute,
    builder: (context, state) => const PhantomDemoScreen(),
  ),
  GoRoute(
    path: '/patients/:patientId/live/:sessionId',
    builder: (context, state) => LiveMonitorScreen(
      patientId: state.pathParameters['patientId']!,
      sessionId: state.pathParameters['sessionId']!,
    ),
  ),
];
