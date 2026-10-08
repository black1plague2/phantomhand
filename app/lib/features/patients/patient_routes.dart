import 'package:go_router/go_router.dart';
import 'package:opus_app/features/patients/patient_list_screen.dart';
import 'package:opus_app/features/patients/patient_profile_screen.dart';

/// The patients feature's own [RouteBase] list -- list + profile only. The
/// profile's sub-screens (program builder, session report, live monitor,
/// outcome entry) are declared as their *own* top-level routes by their own
/// feature folders (`docs/IMPROVEMENT_BRIEF.md` §3), not nested here, even
/// though their paths are `/patients/:patientId/...` -- go_router matches
/// routes by path pattern, not by which Dart list they're declared in, so
/// each feature can own its exact URL without every feature importing every
/// other feature's screen.
final List<RouteBase> patientRoutes = [
  GoRoute(path: '/patients', builder: (context, state) => const PatientListScreen()),
  GoRoute(
    path: '/patients/:patientId',
    builder: (context, state) => PatientProfileScreen(patientId: state.pathParameters['patientId']!),
  ),
];
