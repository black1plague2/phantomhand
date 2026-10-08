import 'package:go_router/go_router.dart';
import 'package:opus_app/features/sessions/reports_list_screen.dart';
import 'package:opus_app/features/sessions/session_report_screen.dart';

/// The sessions feature's own [RouteBase] list (`docs/IMPROVEMENT_BRIEF.md`
/// §3). See `patient_routes.dart`'s doc comment for why owning a
/// `/patients/:patientId/...` path from a different feature folder is fine.
///
/// **NOTE FOR A1 (app shell/bottom nav):** `/reports` below is the Reports
/// tab's destination (Opus decision: bottom nav = Patients / Monitor /
/// Programs / Reports) -- wire the "Reports" `NavigationDestination`/
/// `NavigationRailDestination` in `adaptive_shell.dart` to `context.go('/reports')`
/// (or nest it as this branch's initial location if the shell uses
/// `StatefulShellRoute`). Not added to the shell itself since that file is
/// A1's scope.
final List<RouteBase> sessionRoutes = [
  GoRoute(
    path: '/reports',
    builder: (context, state) => const ReportsListScreen(),
  ),
  GoRoute(
    path: '/patients/:patientId/sessions/:sessionId',
    builder: (context, state) => SessionReportScreen(
      patientId: state.pathParameters['patientId']!,
      sessionId: state.pathParameters['sessionId']!,
    ),
  ),
];
