import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/data/models/patient.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';

/// The top-level Programs tab (Opus decision 2026-09-19: bottom nav =
/// Patients / Monitor / Programs / Reports). The program *builder* itself
/// (`features/programs/program_builder_screen.dart`, A2's scope) only makes
/// sense for one already-chosen patient -- its route is
/// `/patients/:patientId/programs/new` -- so this tab is a patient picker
/// that hands off to that existing builder rather than a new builder UI.
///
/// Deliberately placed in `core/router` (this track's scope) rather than
/// `features/programs` (A2's scope, not touched this run) since it is purely
/// shell-level navigation glue: a thin list re-using
/// [patientsRepositoryProvider], the same provider `features/patients`
/// already exposes.
class ProgramsHomeScreen extends ConsumerWidget {
  const ProgramsHomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final patientsAsync = ref.watch(_patientsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Programs')),
      body: patientsAsync.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetryView(message: e.toString(), onRetry: () => ref.invalidate(_patientsProvider)),
        data: (patients) {
          if (patients.isEmpty) {
            // v2 §3: an empty state is ONE short line saying what to do.
            return const Center(child: Text('No patients yet'));
          }
          // Decluttered 2026-09-19 (run14) after seeing this screen on the
          // pilot phone at its real 360 dp width: the old row was a 40 px
          // CircleAvatar + name + an "N programs" subtitle + a full
          // FilledButton.tonal "Build program", which left the name column
          // ~120 dp wide. Every name wrapped to two lines, "0 programs"
          // wrapped to two more, and six rows filled the screen --
          // `.../2026-09-19-run14/before/03_programs.png`. The whole row is
          // already a single tap target that opens the builder, so the button
          // was a duplicate of the row's own action, the avatar was
          // decorative, and the program count was a subtitle (v2 §3 forbids
          // both). Name + chevron only.
          return ListView.separated(
            padding: const EdgeInsets.all(16),
            itemCount: patients.length,
            separatorBuilder: (context, _) => const Divider(height: 1),
            itemBuilder: (context, i) {
              final p = patients[i];
              return ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(p.displayName, maxLines: 1, overflow: TextOverflow.ellipsis),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.go('/patients/${p.id}/programs/new'),
              );
            },
          );
        },
      ),
    );
  }
}

final _patientsProvider = FutureProvider.autoDispose<List<Patient>>(
  (ref) => ref.watch(patientsRepositoryProvider).listPatients(),
);
