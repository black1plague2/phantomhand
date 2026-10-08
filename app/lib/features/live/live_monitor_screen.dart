import 'dart:async';

import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/live_message.dart';
import 'package:opus_app/data/repositories/hub/hub_live_repository.dart';
import 'package:opus_app/data/repositories/live_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/features/live/phantom_live_providers.dart';
import 'package:opus_app/features/live/phantom_live_screen.dart';
import 'package:opus_app/shared/metrics/metric_format.dart';
import 'package:riverpod/src/providers/provider.dart';
import 'package:riverpod/src/providers/stream_provider.dart';

/// Live monitor -- design v2 (`docs/design/OPUS_DESIGN_V2.md` §5 "Monitor"):
/// title + live dot/latency in the app bar, a big "Trial X of N", 4 metric
/// tiles, a small live outcomes donut, a reaction-time sparkline, an 8-line
/// plain-language event feed, and Pause (outlined) / Stop (filled crimson)
/// side by side. No subtitles or helper paragraphs anywhere (v2 §3 "titles
/// only"). Falls back to `MockLiveRepository`'s synthetic stream when no
/// headset is connected/running this session, so the screen still demos on
/// its own.
class LiveMonitorScreen extends ConsumerWidget {
  const new({required this.patientId, required this.sessionId, super.key});
  final String patientId;
  final String sessionId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // A session whose game reports `game_state` (Phantom Hand) or the scripted
    // demo gets the generic game-state operator card instead of the
    // trial-by-trial monitor (FR-AP-01).
    final phantom = ref.watch(phantomLiveRepositoryProvider(sessionId));
    if (phantom != null) {
      return PhantomLiveScreen(repository: phantom, demo: sessionId == mockPhantomSessionId);
    }
    final t = Theme.of(context).extension<OpusTokens>()!;
    final liveRepo = ref.watch(effectiveLiveRepositoryProvider(sessionId));
    final stream = ref.watch(_liveStreamProvider(sessionId));
    final connection = hubCapable ? ref.watch(hubControllerProvider.notifier).connectionForSession(sessionId) : null;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Monitor'),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 16),
            child: Center(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _LiveDot(live: connection != null, color: t.alert),
                  const SizedBox(width: 6),
                  Text(
                    connection?.lastRtt != null ? '${connection!.lastRtt!.inMilliseconds} ms' : '--',
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(fontFeatures: const [FontFeature.tabularFigures()]),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
      body: stream.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(child: Text(e.toString())),
        data: (message) {
          if (message == null) return const Center(child: Text('No session data yet'));
          return _MonitorBody(
            connection: connection,
            message: message,
            onPause: () => message.status == LiveSessionStatus.paused ? liveRepo.resume(sessionId) : liveRepo.pause(sessionId),
            onStop: () => _confirmStop(context, () => liveRepo.stop(sessionId)),
          );
        },
      ),
    );
  }

  void _confirmStop(BuildContext context, VoidCallback onConfirm) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Stop session'),
        actions: [
          TextButton(onPressed: () => Navigator.of(ctx).pop(), child: const Text('Cancel')),
          FilledButton(
            onPressed: () {
              onConfirm();
              Navigator.of(ctx).pop();
              if (context.mounted) context.pop();
            },
            child: const Text('Stop'),
          ),
        ],
      ),
    );
  }
}

class _LiveDot extends StatefulWidget {
  const _LiveDot({required this.live, required this.color});
  final bool live;
  final Color color;

  @override
  State<_LiveDot> createState() => _LiveDotState();
}

class _LiveDotState extends State<_LiveDot> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(vsync: this, duration: const Duration(seconds: 1))
    ..repeat(reverse: true);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.live) {
      return Container(width: 8, height: 8, decoration: BoxDecoration(color: Colors.transparent, shape: BoxShape.circle, border: Border.all(color: widget.color.withValues(alpha: 0.5))));
    }
    // v2 §6: "The live dot pulses. Nothing else moves."
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) => Container(
        width: 8,
        height: 8,
        decoration: BoxDecoration(color: widget.color.withValues(alpha: 0.4 + 0.6 * _controller.value), shape: BoxShape.circle),
      ),
    );
  }
}

/// A section per v2 §4: `panel` bg, 1px `line` border, radius 12, 16px
/// padding, section title top-left.
class _Section extends StatelessWidget {
  const _Section({required this.title, required this.child});
  final String title;
  final Widget child;

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
          Text(title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 12),
          child,
        ],
      ),
    );
  }
}

/// One `panelRaised` tile in the 2/3-column metric grid. v3 amendment
/// (BINDING): "KPI tiles get a 3 px left colour bar in their metric colour +
/// the value in that colour" -- [color] is the metric's fixed semantic
/// colour (`OpusTokens.metricColorV3`), same on every screen that shows it.
class _Tile extends StatelessWidget {
  const _Tile({required this.label, required this.value, this.color});
  final String label;
  final String value;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>()!;
    final theme = Theme.of(context);
    final valueColor = color ?? t.ink;
    return Container(
      decoration: BoxDecoration(
        color: t.panelRaised,
        borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
        border: color == null ? null : Border(left: BorderSide(color: color!, width: 3)),
      ),
      padding: EdgeInsets.fromLTRB(color == null ? 12 : 9, 12, 12, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(value, style: theme.textTheme.headlineSmall?.copyWith(color: valueColor)),
          const SizedBox(height: 2),
          Text(label, style: theme.textTheme.bodySmall),
        ],
      ),
    );
  }
}

/// Owns the session's own history (outcome tally, per-trial reaction-time
/// series, last-8 plain-language event lines) by subscribing directly to
/// [connection]'s streams -- none of that is carried by the single current
/// [LiveMessage] snapshot the rest of the screen renders from, the same
/// reason `_HapticCueIndicator` (removed this pass, folded into the events
/// feed below) used to keep its own subscription.
class _MonitorBody extends StatefulWidget {
  const _MonitorBody({required this.connection, required this.message, required this.onPause, required this.onStop});
  final dynamic connection;
  final LiveMessage message;
  final VoidCallback onPause;
  final VoidCallback onStop;

  @override
  State<_MonitorBody> createState() => _MonitorBodyState();
}

class _MonitorBodyState extends State<_MonitorBody> {
  StreamSubscription<Map<String, dynamic>>? _trialSub;
  final Map<String, int> _outcomeCounts = {};
  final List<double> _rtSeries = [];
  final List<String> _events = [];

  @override
  void initState() {
    super.initState();
    _listen();
  }

  @override
  void didUpdateWidget(covariant _MonitorBody oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.connection != widget.connection) {
      _trialSub?.cancel();
      _outcomeCounts.clear();
      _rtSeries.clear();
      _events.clear();
      _listen();
    }
  }

  void _listen() {
    final connection = widget.connection;
    if (connection == null) return;
    _trialSub = (connection.trialEventStream as Stream<Map<String, dynamic>>).listen((payload) {
      final type = payload['type'] as String?;
      final trial = payload['trial'];
      final trialNum = trial is num ? trial.toInt() + 1 : null;
      String? line;
      switch (type) {
        case 'target_shown':
          line = trialNum != null ? 'Trial $trialNum started' : null;
        case 'contact':
          line = 'Reached the target';
        case 'trial_end':
          final outcome = payload['outcome'] as String? ?? 'unknown';
          _outcomeCounts[outcome] = (_outcomeCounts[outcome] ?? 0) + 1;
          final rt = payload['reaction_time_ms'];
          if (rt is num) _rtSeries.add(rt.toDouble());
          line = trialNum != null ? 'Trial $trialNum: ${_outcomeLabel(outcome)}' : _outcomeLabel(outcome);
        case 'haptic_cue':
          final cue = (payload['data'] as Map?)?['cue'] as String?;
          line = cue != null ? 'Cue: $cue' : null;
        case 'session_end':
          line = 'Session ended';
      }
      if (line != null && mounted) {
        setState(() {
          _events.insert(0, line!);
          if (_events.length > 8) _events.removeLast();
        });
      } else if (mounted) {
        setState(() {});
      }
    });
  }

  String _outcomeLabel(String outcome) => OpusTokens.outcomeLabelV3(outcome).toLowerCase();

  @override
  void dispose() {
    _trialSub?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>()!;
    final message = widget.message;
    final metrics = message.rollingMetrics;

    return Column(
      children: [
        Expanded(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'Trial ${message.trialIndex} of ${message.totalTrials}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 16),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: _Tile(
                        label: metricLabel('reaction_time_ms'),
                        value: _metricOr(metrics, 'reaction_time_ms'),
                        color: OpusTokens.metricColorV3('reactionTime'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: _Tile(
                        label: metricLabel('peak_speed_mps'),
                        value: _metricOr(metrics, 'peak_speed_mps'),
                        color: OpusTokens.metricColorV3('peakSpeed'),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: _Tile(
                        label: metricLabel('trunk_lean_cm'),
                        value: _metricOr(metrics, 'trunk_lean_cm'),
                        color: OpusTokens.metricColorV3('trunkLean'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: _Tile(
                        label: 'Success',
                        value: _successPct(),
                        color: OpusTokens.metricColorV3('success'),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                _Section(title: 'Outcomes', child: _OutcomesDonut(counts: _outcomeCounts)),
                const SizedBox(height: 16),
                _Section(
                  title: 'Reaction time',
                  child: _RtSparkline(series: _rtSeries, color: OpusTokens.metricColorV3('reactionTime')),
                ),
                const SizedBox(height: 16),
                _Section(
                  title: 'Events',
                  child: _events.isEmpty
                      ? Text('No events yet', style: Theme.of(context).textTheme.bodyMedium)
                      : Column(
                          children: [
                            for (var i = 0; i < _events.length; i++) ...[
                              if (i > 0) Divider(height: 1, color: t.rule),
                              Padding(
                                padding: const EdgeInsets.symmetric(vertical: 8),
                                child: Align(
                                  alignment: Alignment.centerLeft,
                                  child: Text(_events[i], style: Theme.of(context).textTheme.bodyMedium),
                                ),
                              ),
                            ],
                          ],
                        ),
                ),
              ],
            ),
          ),
        ),
        SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
            child: Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(56)),
                    onPressed: message.status == LiveSessionStatus.stopped ? null : widget.onPause,
                    child: Text(message.status == LiveSessionStatus.paused ? 'Resume' : 'Pause'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton(
                    style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56), backgroundColor: t.alert, foregroundColor: t.ink),
                    onPressed: message.status == LiveSessionStatus.stopped ? null : widget.onStop,
                    child: const Text('Stop'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  String _metricOr(Map<String, double> metrics, String key) {
    final v = metrics[key];
    return v == null ? '--' : metricValueText(key, v);
  }

  String _successPct() {
    final total = _outcomeCounts.values.fold(0, (a, b) => a + b);
    if (total == 0) return '--';
    final success = _outcomeCounts['success'] ?? 0;
    return '${(100 * success / total).round()}%';
  }
}

/// Small live donut per v2 §5 (success/timeout/dropped/wrong basket), fixed
/// outcome colors from `OpusTokens.outcomeColor`.
class _OutcomesDonut extends StatelessWidget {
  const _OutcomesDonut({required this.counts});
  final Map<String, int> counts;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>()!;
    final total = counts.values.fold(0, (a, b) => a + b);
    if (total == 0) {
      return Text('No trials yet', style: Theme.of(context).textTheme.bodyMedium);
    }
    const order = ['success', 'timeout', 'dropped', 'wrong_basket'];
    final sections = <PieChartSectionData>[
      for (final outcome in order)
        if ((counts[outcome] ?? 0) > 0)
          PieChartSectionData(
            value: (counts[outcome] ?? 0).toDouble(),
            color: OpusTokens.outcomeColorV3(outcome),
            showTitle: false,
            radius: 20,
          ),
    ];
    final successPct = (100 * (counts['success'] ?? 0) / total).round();
    return Row(
      children: [
        SizedBox(
          width: 88,
          height: 88,
          child: Stack(
            alignment: Alignment.center,
            children: [
              PieChart(PieChartData(sections: sections, centerSpaceRadius: 24, sectionsSpace: 1)),
              Text('$successPct%', style: Theme.of(context).textTheme.labelLarge),
            ],
          ),
        ),
        const SizedBox(width: 16),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final outcome in order)
                if ((counts[outcome] ?? 0) > 0)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 2),
                    child: Row(
                      children: [
                        Container(width: 8, height: 8, decoration: BoxDecoration(color: OpusTokens.outcomeColorV3(outcome), shape: BoxShape.circle)),
                        const SizedBox(width: 8),
                        Text('${OpusTokens.outcomeLabelV3(outcome)} ${counts[outcome]}', style: Theme.of(context).textTheme.bodySmall?.copyWith(color: t.ink)),
                      ],
                    ),
                  ),
            ],
          ),
        ),
      ],
    );
  }
}

/// Reaction-time sparkline (per-trial values this session), v2 §5.
class _RtSparkline extends StatelessWidget {
  const _RtSparkline({required this.series, required this.color});
  final List<double> series;
  final Color color;

  @override
  Widget build(BuildContext context) {
    if (series.length < 2) {
      return Text('Not enough trials yet', style: Theme.of(context).textTheme.bodyMedium);
    }
    final spots = [for (var i = 0; i < series.length; i++) FlSpot(i.toDouble(), series[i])];
    return SizedBox(
      height: 64,
      child: LineChart(
        LineChartData(
          gridData: const FlGridData(show: false),
          titlesData: const FlTitlesData(show: false),
          borderData: FlBorderData(show: false),
          lineTouchData: const LineTouchData(enabled: false),
          lineBarsData: [
            LineChartBarData(
              spots: spots,
              isCurved: false,
              color: color,
              barWidth: 2,
              dotData: const FlDotData(show: false),
            ),
          ],
        ),
      ),
    );
  }
}

/// Picks a real [HubLiveRepository] bound to whichever connected headset is
/// currently running [sessionId], falling back to the shared mock repository
/// when no headset is connected/running that session -- so the live monitor
/// (and its pause/resume/stop buttons) always target the same repository
/// instance that's producing the stream below.
final ProviderFamily<LiveRepository, String> effectiveLiveRepositoryProvider = Provider.family<LiveRepository, String>((ref, sessionId) {
  if (hubCapable) {
    ref.watch(hubControllerProvider); // rebuild when a headset connects/disconnects
    final connection = ref.read(hubControllerProvider.notifier).connectionForSession(sessionId);
    // One repository per connection. The hub state now changes on every status/trial event (hub_server.dart
    // re-publishes snapshots), so this provider rebuilds several times a second; returning a NEW
    // HubLiveRepository each time re-subscribed the stream below, wiped the accumulated trial count/metrics and
    // flashed the loading state (blank Monitor on the pilot phone, 2026-09-19). Returning the identical
    // instance makes Riverpod skip notifying dependents, so the stream keeps its state.
    if (connection != null) return _liveRepoByConnection[connection] ??= HubLiveRepository(connection);
  }
  return ref.watch(liveRepositoryProvider);
});

final Expando<HubLiveRepository> _liveRepoByConnection = Expando<HubLiveRepository>('liveRepoByConnection');

final StreamProviderFamily<LiveMessage?, String> _liveStreamProvider = StreamProvider.family<LiveMessage?, String>(
  (ref, sessionId) => ref.watch(effectiveLiveRepositoryProvider(sessionId)).watch(sessionId),
);
