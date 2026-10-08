import 'package:go_router/go_router.dart';
import 'package:opus_app/features/programs/program_builder_screen.dart';

/// The programs feature's own [RouteBase] list (`docs/IMPROVEMENT_BRIEF.md`
/// §3). Owns the `/patients/:patientId/programs/new` URL even though it lives
/// under the patients path segment -- see `patient_routes.dart`'s doc comment
/// for why that's fine with go_router.
final List<RouteBase> programRoutes = [
  GoRoute(
    path: '/patients/:patientId/programs/new',
    builder: (context, state) => ProgramBuilderScreen(patientId: state.pathParameters['patientId']!),
  ),
];
