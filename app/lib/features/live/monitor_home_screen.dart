import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/features/live/live_monitor_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';

/// The top-level Monitor tab (bottom nav = Patients / Monitor / Programs /
/// Reports). Picks whichever session a connected headset is actively running
/// (if any) and renders the real [LiveMonitorScreen] for it -- otherwise the
/// fixed empty state design v2 specifies (`docs/design/OPUS_DESIGN_V2.md`
/// §5): "No headset connected" + "Start hub" + the Wi-Fi IP, one line, no
/// paragraph (v2 §3 "titles only").
///
/// Task 4 (run 2, BINDING user ask -- "I want to see the app get data from
/// Unity, not backend logs"): the hub now starts automatically the moment
/// this tab opens, rather than waiting for a manual "Start hub" tap, so a
/// headset connecting to the phone in the background is picked up with no
/// clinician action. `HubController.start()` is idempotent (`if
/// (!hubCapable || state.running) return;`) so calling it every time this
/// widget is built/rebuilt is safe. Converted from a stateless
/// `ConsumerWidget` to `ConsumerStatefulWidget` so the auto-start fires once
/// per mount (`initState`), not on every rebuild while the hub is starting.
class MonitorHomeScreen extends ConsumerStatefulWidget {
  const MonitorHomeScreen({super.key});

  @override
  ConsumerState<MonitorHomeScreen> createState() => _MonitorHomeScreenState();
}

class _MonitorHomeScreenState extends ConsumerState<MonitorHomeScreen> {
  @override
  void initState() {
    super.initState();
    if (hubCapable) {
      // Fire-and-forget: any failure (e.g. port already bound) still leaves
      // the manual "Start hub" button in the empty state as a fallback.
      unawaited(ref.read(hubControllerProvider.notifier).start());
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!hubCapable) {
      return const _MonitorScaffold(body: Center(child: Text('Not available on web')));
    }

    final hub = ref.watch(hubControllerProvider);
    final notifier = ref.read(hubControllerProvider.notifier);
    final liveSessionIds = hub is HubRunning ? notifier.allRunningSessionIds() : const <String>{};

    if (liveSessionIds.isEmpty) {
      return _MonitorScaffold(body: _EmptyState(hub: hub));
    }

    final sessionId = liveSessionIds.first;
    final patientId = notifier.patientRefForSession(sessionId) ?? 'unknown';
    return LiveMonitorScreen(patientId: patientId, sessionId: sessionId);
  }
}

class _MonitorScaffold extends StatelessWidget {
  const _MonitorScaffold({required this.body});
  final Widget body;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Monitor')),
        body: body,
      );
}

/// v2 §5's fixed empty state: "No headset connected" + "Start hub" + the
/// Wi-Fi IP. Shows the address once the hub is already running (nothing left
/// to start, just waiting for a headset to dial in); shows the Start button
/// when it isn't.
class _EmptyState extends ConsumerWidget {
  const _EmptyState({required this.hub});
  final HubControllerState hub;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final running = hub is HubRunning ? hub as HubRunning : null;
    final ips = ref.watch(_monitorIpsProvider).value ?? const [];
    final wifiIp = ips.isEmpty ? null : ips.first.address;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // v2 §5's empty state is exactly three things: the line, the
            // address, the button. The decorative 48px podcast glyph that used
            // to sit above the title was removed 2026-09-19 (run14) under the
            // user's "remove excess... too cluttered" brief -- it carried no
            // information the title didn't already carry.
            Text('No headset connected', style: Theme.of(context).textTheme.titleMedium),
            if (running != null && wifiIp != null) ...[
              const SizedBox(height: 8),
              // The one line the clinician actually has to read: it is what
              // they type into the headset (or into fake_headset.py --hub).
              SelectableText(
                '$wifiIp:${running.port}',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
            ],
            const SizedBox(height: 16),
            if (running == null) FilledButton(onPressed: () => ref.read(hubControllerProvider.notifier).start(), child: const Text('Start hub')),
            // The scripted Phantom Hand session (no headset, no hub): lets the
            // operator card be tried out before anything is connected.
            const SizedBox(height: 8),
            TextButton(
              key: const ValueKey('ph-try-demo'),
              onPressed: () => context.push('/patients/demo/live/$mockPhantomSessionId'),
              child: Text(AppLocalizations.of(context)!.phTryDemo),
            ),
          ],
        ),
      ),
    );
  }
}

final _monitorIpsProvider = FutureProvider<List<({String label, String address})>>((ref) => localIpv4AddressesLabeled());
