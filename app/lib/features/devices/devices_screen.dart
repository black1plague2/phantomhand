import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/shared/metrics/haptic_status.dart';

/// Devices screen -- design v2 (`docs/design/OPUS_DESIGN_V2.md` §5
/// "Devices"): title only, three sections -- "Hub" (address row, on/off
/// switch), "Headsets" (row per headset with state + RTT, actions in an
/// overflow menu), "Haptic sleeve". No helper paragraphs (v2 §3). On web
/// (`!hubCapable`) shows a one-line explanation instead -- the web build is a
/// viewer only until Phase 3's cloud relay exists.
class DevicesScreen extends ConsumerWidget {
  const new({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (!hubCapable) {
      return Scaffold(
        appBar: AppBar(title: const Text('Devices')),
        body: const Center(child: Text('Not available on web. Run on Windows or Android to pair a headset.')),
      );
    }

    final hub = ref.watch(hubControllerProvider);
    final isWide = MediaQuery.sizeOf(context).width >= 700;
    return Scaffold(
      appBar: AppBar(title: const Text('Devices')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          _HubSection(hub: hub),
          if (hub is HubRunning) ...[
            const SizedBox(height: 16),
            _HeadsetsSection(headsets: hub.headsets),
            const SizedBox(height: 16),
            const _RecordedSessionsSection(),
            // v2 §3: "Remove... 'open a session folder' card on phone."
            if (isWide) ...[
              const SizedBox(height: 16),
              const _OpenSessionFolderSection(),
            ],
          ],
        ],
      ),
    );
  }
}

/// A section per v2 §4: `panel` bg, 1px `line` border, radius 12, section
/// title top-left.
class _Section extends StatelessWidget {
  const _Section({required this.title, required this.child, this.action});
  final String title;
  final Widget child;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>()!;
    return Container(
      decoration: BoxDecoration(
        color: t.paper,
        border: Border.all(color: t.rule),
        borderRadius: BorderRadius.circular(OpusTokens.radiusPane),
      ),
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text(title, style: Theme.of(context).textTheme.titleMedium)),
              if (action != null) action!,
            ],
          ),
          const SizedBox(height: 12),
          child,
        ],
      ),
    );
  }
}

class _HubSection extends ConsumerWidget {
  const _HubSection({required this.hub});
  final HubControllerState hub;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final running = hub is HubRunning ? hub as HubRunning : null;
    final ips = ref.watch(_localIpsProvider).value ?? const [];
    final primaryIp = ips.isEmpty ? "this device's IP" : ips.first.address;
    return _Section(
      title: 'Hub',
      action: Switch(
        value: running != null,
        onChanged: (v) => v ? ref.read(hubControllerProvider.notifier).start() : ref.read(hubControllerProvider.notifier).stop(),
      ),
      child: running == null
          ? const Text('Off')
          : Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SelectableText('ws://$primaryIp:${running.port}/opus/v1/live'),
                if (ips.length > 1)
                  // A headset must be told the address on the SAME network
                  // it's on -- a clinic phone often has both a Wi-Fi radio
                  // (what the headset needs) and its own hotspot radio at
                  // once, so every candidate is labelled rather than picking
                  // one silently.
                  for (final l in ips.skip(1)) Text('${l.label}: ${l.address}', style: Theme.of(context).textTheme.bodySmall),
                const SizedBox(height: 4),
                Text(
                  running.hubId.replaceAll('-', '').substring(0, 6).toUpperCase(),
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
              ],
            ),
    );
  }
}

final _localIpsProvider = FutureProvider<List<({String label, String address})>>((ref) => localIpv4AddressesLabeled());

class _HeadsetsSection extends StatelessWidget {
  const _HeadsetsSection({required this.headsets});
  final List<HeadsetInfo> headsets;

  @override
  Widget build(BuildContext context) {
    return _Section(
      title: 'Headsets',
      child: headsets.isEmpty
          ? Text('None connected', style: Theme.of(context).textTheme.bodyMedium)
          : Column(
              children: [
                for (var i = 0; i < headsets.length; i++) ...[
                  if (i > 0) Divider(height: 1, color: Theme.of(context).extension<OpusTokens>()!.rule),
                  _HeadsetRow(headset: headsets[i]),
                ],
              ],
            ),
    );
  }
}

/// One headset row: state + RTT, actions in an overflow menu (v2 §5:
/// "actions in an overflow menu: Start, Pause, Stop, Recenter"; Send test
/// message/Watch live are kept in the same menu since they're the same kind
/// of one-off command, not a second primary action).
class _HeadsetRow extends ConsumerStatefulWidget {
  const _HeadsetRow({required this.headset});
  final HeadsetInfo headset;

  @override
  ConsumerState<_HeadsetRow> createState() => _HeadsetRowState();
}

class _HeadsetRowState extends ConsumerState<_HeadsetRow> {
  String? _lastResult;

  Future<void> _send(String label, Future<bool> Function() action) async {
    final ok = await action();
    if (!mounted) return;
    setState(() => _lastResult = ok ? '$label acked' : "$label: didn't confirm");
  }

  @override
  Widget build(BuildContext context) {
    final headset = widget.headset;

    if (!headset.connected) {
      return ListTile(
        contentPadding: EdgeInsets.zero,
        // v3: `bad` = disconnected (colour by meaning).
        leading: const Icon(Icons.sync_problem, color: OpusTokens.bad),
        title: Text(headset.deviceId),
        subtitle: const Text('Reconnecting'),
      );
    }

    final status = headset.status;
    final state = status?['state'] as String?;
    final haptic = parseHapticStatus(status);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ListTile(
            contentPadding: EdgeInsets.zero,
            title: Text(headset.deviceId),
            subtitle: Text([?state, if (headset.rttMs != null) '${headset.rttMs} ms'].join(' · ')),
            trailing: PopupMenuButton<String>(
              icon: const Icon(Icons.more_vert),
              itemBuilder: (context) => const [
                PopupMenuItem(value: 'start', child: Text('Start')),
                PopupMenuItem(value: 'pause', child: Text('Pause')),
                PopupMenuItem(value: 'resume', child: Text('Resume')),
                PopupMenuItem(value: 'stop', child: Text('Stop')),
                PopupMenuItem(value: 'recenter', child: Text('Recenter')),
                PopupMenuDivider(),
                PopupMenuItem(value: 'message', child: Text('Send test message')),
                PopupMenuItem(value: 'watch', child: Text('Watch live')),
              ],
              onSelected: (value) => _onSelect(context, value, headset.deviceId),
            ),
          ),
          _HapticSleeveRow(haptic: haptic),
          if (_lastResult != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(_lastResult!, style: Theme.of(context).textTheme.bodySmall),
            ),
        ],
      ),
    );
  }

  void _onSelect(BuildContext context, String value, String deviceId) {
    switch (value) {
      case 'start':
        unawaited(_send('Start', () => ref.read(hubControllerProvider.notifier).startSession(deviceId)));
      case 'pause':
        unawaited(_send('Pause', () => ref.read(hubControllerProvider.notifier).pause(deviceId)));
      case 'resume':
        unawaited(_send('Resume', () => ref.read(hubControllerProvider.notifier).resume(deviceId)));
      case 'stop':
        unawaited(_send('Stop', () => ref.read(hubControllerProvider.notifier).stopSession(deviceId)));
      case 'recenter':
        unawaited(_send('Recenter', () => ref.read(hubControllerProvider.notifier).recenter(deviceId)));
      case 'message':
        unawaited(_sendTestMessage(context, deviceId));
      case 'watch':
        context.go('/monitor');
    }
  }

  Future<void> _sendTestMessage(BuildContext context, String deviceId) async {
    final controller = TextEditingController(text: 'Test message');
    final text = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Send test message'),
        content: TextField(controller: controller, autofocus: true),
        actions: [
          TextButton(onPressed: () => Navigator.of(ctx).pop(), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.of(ctx).pop(controller.text), child: const Text('Send')),
        ],
      ),
    );
    if (text == null || text.trim().isEmpty || !mounted) return;
    unawaited(_send('Message', () => ref.read(hubControllerProvider.notifier).showMessage(deviceId, text.trim())));
  }
}

/// "Haptic sleeve" row -- v2 §5: "connected, battery, cues sent".
class _HapticSleeveRow extends StatelessWidget {
  const _HapticSleeveRow({required this.haptic});
  final HapticStatus haptic;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>()!;
    final icon = switch (haptic.state) {
      HapticSleeveState.connected => Icons.vibration,
      HapticSleeveState.disconnectedMidSession => Icons.sync_problem,
      HapticSleeveState.notConnected => Icons.vibration_outlined,
    };
    // v3 amendment (BINDING): colour by meaning -- `good` = connected,
    // `bad` = disconnected, matching the palette table's own "connected"
    // example (was the structural `lake`/`alert` roles).
    final iconColor = switch (haptic.state) {
      HapticSleeveState.connected => OpusTokens.good,
      HapticSleeveState.disconnectedMidSession => OpusTokens.bad,
      HapticSleeveState.notConnected => t.slate,
    };
    final label = switch (haptic.state) {
      HapticSleeveState.connected =>
        ['Haptic sleeve', if (haptic.batteryPct != null) '${haptic.batteryPct!.toStringAsFixed(0)}%', if (haptic.cuesSent != null) '${haptic.cuesSent} cues'].join(' · '),
      HapticSleeveState.disconnectedMidSession => 'Haptic sleeve disconnected',
      HapticSleeveState.notConnected => 'Haptic sleeve not connected',
    };
    return Row(
      children: [
        Icon(icon, size: 18, color: iconColor),
        const SizedBox(width: 8),
        Text(label, style: Theme.of(context).textTheme.bodySmall),
      ],
    );
  }
}

class _RecordedSessionsSection extends ConsumerStatefulWidget {
  const _RecordedSessionsSection();

  @override
  ConsumerState<_RecordedSessionsSection> createState() => _RecordedSessionsSectionState();
}

class _RecordedSessionsSectionState extends ConsumerState<_RecordedSessionsSection> {
  String? _runningSessionId;

  @override
  Widget build(BuildContext context) {
    final dirs = ref.read(hubControllerProvider.notifier).storedSessionDirs();
    return _Section(
      title: 'Recorded sessions',
      child: dirs.isEmpty
          ? Text('None yet', style: Theme.of(context).textTheme.bodyMedium)
          : Column(
              children: [
                for (final dir in dirs)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    title: Text(dir.uri.pathSegments.where((s) => s.isNotEmpty).last),
                    trailing: TextButton(
                      onPressed: _runningSessionId == dir.path ? null : () => _run(dir.path),
                      child: Text(_runningSessionId == dir.path ? 'Running…' : 'Run analysis'),
                    ),
                  ),
              ],
            ),
    );
  }

  Future<void> _run(String sessionDir) async {
    setState(() => _runningSessionId = sessionDir);
    final result = await ref.read(hubControllerProvider.notifier).runAnalysis(sessionDir);
    if (!mounted) return;
    setState(() => _runningSessionId = null);
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(result.exitCode == 0 ? 'Analysis complete' : 'Analysis failed (exit ${result.exitCode})'),
        content: SingleChildScrollView(
          child: Text('${result.stdout}\n${result.stderr}'.trim().isEmpty ? '(no output)' : '${result.stdout}\n${result.stderr}'),
        ),
        actions: [TextButton(onPressed: () => Navigator.of(ctx).pop(), child: const Text('Close'))],
      ),
    );
  }
}

/// "Open a session folder..." (kept on wide layouts only, v2 §3 -- see
/// `DevicesScreen`). Takes a path typed/pasted by the clinician rather than a
/// native file-picker dialog (no such package is in `pubspec.yaml` yet),
/// resolves it via [DirectorySessionsRepository.open], and on success
/// navigates straight to the existing session report route.
class _OpenSessionFolderSection extends ConsumerStatefulWidget {
  const _OpenSessionFolderSection();

  @override
  ConsumerState<_OpenSessionFolderSection> createState() => _OpenSessionFolderSectionState();
}

class _OpenSessionFolderSectionState extends ConsumerState<_OpenSessionFolderSection> {
  final _controller = TextEditingController();
  bool _opening = false;
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _open() async {
    final path = _controller.text.trim();
    if (path.isEmpty) return;
    setState(() {
      _opening = true;
      _error = null;
    });
    final envelope = await ref.read(openedSessionDirectoriesProvider.notifier).open(path);
    if (!mounted) return;
    setState(() => _opening = false);
    if (envelope == null) {
      setState(() => _error = 'No session.json found');
      return;
    }
    context.go('/patients/${envelope.patientRef}/sessions/${envelope.sessionId}');
  }

  @override
  Widget build(BuildContext context) {
    return _Section(
      title: 'Open a session folder',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _controller,
                  decoration: const InputDecoration(hintText: r'C:\...\OPUS\app\.hub_data\<session-id>', isDense: true),
                  onSubmitted: (_) => _opening ? null : _open(),
                ),
              ),
              const SizedBox(width: 12),
              FilledButton(
                onPressed: _opening ? null : _open,
                child: Text(_opening ? 'Opening…' : 'Open'),
              ),
            ],
          ),
          if (_error != null) ...[
            const SizedBox(height: 8),
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
          ],
        ],
      ),
    );
  }
}
