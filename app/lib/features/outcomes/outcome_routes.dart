import 'package:go_router/go_router.dart';
import 'package:opus_app/features/outcomes/outcome_entry_screen.dart';

/// The outcomes feature's own [RouteBase] list (`docs/IMPROVEMENT_BRIEF.md`
/// §3). See `patient_routes.dart`'s doc comment for why owning a
/// `/patients/:patientId/...` path from a different feature folder is fine.
final List<RouteBase> outcomeRoutes = [
  GoRoute(
    path: '/patients/:patientId/outcomes/new',
    builder: (context, state) => OutcomeEntryScreen(patientId: state.pathParameters['patientId']!),
  ),
];
