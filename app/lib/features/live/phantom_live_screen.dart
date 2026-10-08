import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show SystemChrome, SystemUiMode;
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:opus_app/features/live/phantom_trace_plot.dart';
import 'package:opus_app/features/live/phantom_witness_mirror.dart';
import 'package:opus_app/l10n/app_localizations.dart';

/// Live operator card for a session whose game reports `status.game_state`
/// (Phantom Hand; FR-AP-01). Everything on it comes from the generic
/// `game_state` / `trace` fields, never from game parameters, so it contains no
/// game-specific *parameter* widgets. Design v3 (black + dark red, titles only,
/// at most 4 sections, layman labels, a fixed colour per signal).
///
/// Owns the subscription to [repository], the accumulated [PhantomLiveModel]
/// (trace window, order lock) and the per-command sent/acked/failed state, and
/// renders them through [PhantomLiveView].
class PhantomLiveScreen extends StatefulWidget {
  const new({
    required this.repository,
    this.demo = false,
    this.initialObserver = false,
    this.windowMs = phantomTraceWindowMs,
    this.clock,
    super.key,
  });

  final PhantomLiveRepository repository;

  /// Scripted session with no headset (shows a "Demo" tag).
  final bool demo;

  /// Start in the full-screen audience view (tests/goldens).
  final bool initialObserver;
  final double windowMs;

  /// Time source for the age of the last update (tests); defaults to the wall clock.
  final DateTime Function()? clock;

  @override
  State<PhantomLiveScreen> createState() => _PhantomLiveScreenState();
}

class _PhantomLiveScreenState extends State<PhantomLiveScreen> {
  late PhantomLiveModel _model = PhantomLiveModel(windowMs: widget.windowMs);
  final CommandTracker _tracker = CommandTracker();
  StreamSubscription<PhantomLiveSnapshot>? _sub;
  late bool _observer = widget.initialObserver;

  /// Re-reads the clock once a second: a stalled link sends nothing, so
  /// without it "No update 4 s" would never appear.
  Timer? _tick;

  @override
  void initState() {
    super.initState();
    _subscribe();
    _tick = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() {});
    });
    if (_observer) _setImmersive(true);
  }

  @override
  void didUpdateWidget(covariant PhantomLiveScreen old) {
    super.didUpdateWidget(old);
    if (old.repository != widget.repository) {
      unawaited(_sub?.cancel());
      _model = PhantomLiveModel(windowMs: widget.windowMs);
      _tracker.clear();
      _subscribe();
    }
  }

  void _subscribe() {
    _sub = widget.repository.watch().listen((s) {
      if (!mounted) return;
      setState(() => _model.apply(s));
    });
  }

  @override
  void dispose() {
    _tick?.cancel();
    unawaited(_sub?.cancel());
    if (_observer) _setImmersive(false);
    super.dispose();
  }

  void _setImmersive(bool on) {
    // Hides the system bars on the phone/tablet while the audience view is up.
    // A no-op (or a harmless platform error) on desktop and in tests.
    try {
      unawaited(SystemChrome.setEnabledSystemUIMode(on ? SystemUiMode.immersiveSticky : SystemUiMode.edgeToEdge));
    } catch (_) {}
  }

  void _toggleObserver() {
    setState(() => _observer = !_observer);
    _setImmersive(_observer);
  }

  Future<void> _send(PhantomCommand c, {Map<String, dynamic>? params}) async {
    await _tracker.run(
      c,
      () => widget.repository.send(c, params: params),
      onChange: () {
        if (mounted) setState(() {});
      },
    );
    // The headset confirmed a new order: remember it locally even before its
    // next status echoes it.
    if (c == PhantomCommand.conditionOrder && _tracker.status[c] == CommandStatus.acked) {
      final v = params?['condition_order'];
      if (v is String && mounted) setState(() => _model.conditionOrder = v);
    }
  }

  Future<void> _confirmThenSend(PhantomCommand c, String title) async {
    final l = AppLocalizations.of(context)!;
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(title),
        actions: [
          TextButton(onPressed: () => Navigator.of(ctx).pop(false), child: Text(l.commonCancel)),
          FilledButton(onPressed: () => Navigator.of(ctx).pop(true), child: Text(l.phConfirm)),
        ],
      ),
    );
    if (ok ?? false) await _send(c);
  }

  void _onCommand(PhantomCommand c) {
    final l = AppLocalizations.of(context)!;
    switch (c) {
      case PhantomCommand.end:
        unawaited(_confirmThenSend(c, l.phConfirmEnd));
      case PhantomCommand.nextPerson:
        // Nothing to lose once the run is over; ask while someone is mid-run.
        if (_model.latest?.runState.active ?? false) {
          unawaited(_confirmThenSend(c, l.phConfirmNextPerson));
        } else {
          unawaited(_send(c));
        }
      default:
        unawaited(_send(c));
    }
  }

  void _onOrder(String order) => unawaited(_send(PhantomCommand.conditionOrder, params: {'condition_order': order}));

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final t = Theme.of(context).extension<OpusTokens>()!;
    final health = PhantomLinkHealth.of(_model.latest, (widget.clock ?? DateTime.now)());
    final view = PhantomLiveView(
      model: _model,
      statuses: _tracker.status,
      observer: _observer,
      stale: health.stale,
      onCommand: _onCommand,
      onOrder: _onOrder,
    );
    if (_observer) {
      return Scaffold(
        backgroundColor: t.mist,
        body: SafeArea(
          child: Stack(
            children: [
              view,
              Positioned(
                top: 4,
                right: 4,
                child: IconButton.filledTonal(
                  tooltip: l.phObserverExit,
                  onPressed: _toggleObserver,
                  icon: const Icon(Icons.fullscreen_exit),
                ),
              ),
            ],
          ),
        ),
      );
    }
    return Scaffold(
      appBar: AppBar(
        title: FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerLeft, child: Text(l.phTitle)),
        actions: [
          Center(
            child: Padding(
              padding: const EdgeInsets.only(right: 4),
              child: widget.demo ? Text(l.phDemo, style: Theme.of(context).textTheme.bodySmall) : _LinkPill(health: health),
            ),
          ),
          IconButton(tooltip: l.phObserver, onPressed: _toggleObserver, icon: const Icon(Icons.fullscreen)),
          const SizedBox(width: 4),
        ],
      ),
      body: view,
    );
  }
}

/// "Quest 18 ms" / "No update 4 s" / "Offline": a dot and words, so the state
/// is never colour-only. Green while statuses keep arriving, amber when they
/// stop (the hub still believes in the link for a few seconds), red once it is gone.
class _LinkPill extends StatelessWidget {
  const new({required this.health});
  final PhantomLinkHealth health;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final (color, text) = switch (health.state) {
      PhantomLinkState.live => (OpusTokens.good, health.rttMs == null ? l.phLinkQuest : l.phLinkQuestMs(health.rttMs!)),
      PhantomLinkState.stale => (OpusTokens.warn, l.phLinkStale(health.ageS ?? 0)),
      PhantomLinkState.offline => (OpusTokens.bad, l.phOffline),
    };
    return Container(
      key: const ValueKey('ph-link'),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(border: Border.all(color: color), borderRadius: BorderRadius.circular(OpusTokens.radiusControl)),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          _Dot(color: color, size: 8),
          const SizedBox(width: 6),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 128),
            child: FittedBox(
              fit: BoxFit.scaleDown,
              child: Text(text, style: theme.textTheme.bodySmall?.copyWith(color: theme.extension<OpusTokens>()!.ink)),
            ),
          ),
        ],
      ),
    );
  }
}

class _Dot extends StatelessWidget {
  const _Dot({required this.color, this.size = 10});
  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) =>
      Container(width: size, height: size, decoration: BoxDecoration(color: color, shape: BoxShape.circle));
}

/// Layman label for a phase id (unknown ids are shown as given).
String phantomPhaseLabel(AppLocalizations l, String? phase) => switch (phase) {
      // `idle` is what the headset reports before the first Start.
      null || 'idle' => l.phPhaseWaiting,
      'calibrate' => l.phPhaseCalibrate,
      'probe_pre' => l.phPhaseProbePre,
      'induction' => l.phPhaseInduction,
      'self_touch' => l.phPhaseSelfTouch,
      'agency' => l.phPhaseAgency,
      'threat' => l.phPhaseThreat,
      'probe_post' => l.phPhaseProbePost,
      'questionnaire' => l.phPhaseQuestionnaire,
      'dissolve' => l.phPhaseDissolve,
      'reveal' => l.phPhaseReveal,
      'witness' => l.phPhaseWitness,
      'done' => l.phPhaseDone,
      final other => other,
    };

/// `m:ss`, or an em dash when the headset reports no countdown.
String phantomTimeLeft(double? seconds) {
  if (seconds == null) return '—';
  final s = seconds.ceil().clamp(0, 99 * 60 + 59);
  return '${s ~/ 60}:${(s % 60).toString().padLeft(2, '0')}';
}

Color phantomConditionColor(PhantomCondition? c) => switch (c) {
      PhantomCondition.sync => OpusTokens.info,
      PhantomCondition.async => OpusTokens.warn,
      null => const Color(0xFF8F8384),
    };

/// The card itself: pure rendering of a [PhantomLiveModel] and the per-command
/// statuses; all actions are callbacks. Lays out as one column on phones, two
/// columns (status + controls | signals) from 900 dp, and as a big-type
/// audience screen when [observer] is true.
class PhantomLiveView extends StatelessWidget {
  const new({
    required this.model,
    required this.statuses,
    required this.onCommand,
    required this.onOrder,
    this.observer = false,
    this.stale = false,
    this.showControls = true,
    super.key,
  });

  final PhantomLiveModel model;
  final Map<PhantomCommand, CommandStatus> statuses;
  final void Function(PhantomCommand) onCommand;
  final void Function(String order) onOrder;
  final bool observer;

  /// The link is quiet or gone: the traces are drawn grey, they are not current.
  final bool stale;

  /// False leaves the Controls section out (the recorded demo has nothing to press).
  final bool showControls;

  @override
  Widget build(BuildContext context) {
    final snap = model.latest;
    final game = snap?.game;
    final status = _StatusSection(game: game, observer: observer, connected: snap?.connected ?? false);
    // [fill]: wide and tall enough for the two plots to share the pane's height.
    Widget signals({bool fill = false}) => _SignalsSection(buffer: model.buffer, observer: observer, stale: stale, fill: fill);
    // Witness phase on the audience screen: this person's numbers, not the traces.
    // The top band keeps the exit button clear of the mirror's header.
    if (observer && game?.phase == 'witness') {
      return Padding(
        padding: const EdgeInsets.only(top: 48),
        child: PhantomWitnessMirror(summary: model.witness, lang: Localizations.localeOf(context).languageCode),
      );
    }
    if (observer) {
      return LayoutBuilder(
        builder: (context, box) {
          final wide = box.maxWidth >= 900;
          if (wide) {
            final fill = box.maxHeight >= _fillMinHeight;
            return Padding(
              padding: const EdgeInsets.all(24),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SizedBox(width: 460, child: SingleChildScrollView(child: status)),
                  const SizedBox(width: 24),
                  Expanded(child: fill ? signals(fill: true) : SingleChildScrollView(child: signals())),
                ],
              ),
            );
          }
          return SingleChildScrollView(
            padding: const EdgeInsets.fromLTRB(16, 48, 16, 16),
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [status, const SizedBox(height: 16), signals()]),
          );
        },
      );
    }
    final controls = showControls
        ? _ControlsSection(
            rules: model.rules,
            statuses: statuses,
            order: model.conditionOrder,
            onCommand: onCommand,
            onOrder: onOrder,
          )
        : null;
    return LayoutBuilder(
      builder: (context, box) {
        if (box.maxWidth >= 900) {
          final fill = box.maxHeight >= _fillMinHeight;
          return Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SizedBox(
                  width: 420,
                  child: SingleChildScrollView(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [status, if (controls != null) ...[const SizedBox(height: 16), controls]],
                    ),
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(child: fill ? signals(fill: true) : SingleChildScrollView(child: signals())),
              ],
            ),
          );
        }
        return SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            // Controls above the traces so the operator never scrolls to Start / Next / Abort / End.
            children: [status, const SizedBox(height: 16), if (controls != null) ...[controls, const SizedBox(height: 16)], signals()],
          ),
        );
      },
    );
  }
}

/// Pane height from which the two plots stretch to fill it: with the minimum
/// plot height and the section chrome (also at 2x text) they still fit above
/// this; shorter windows scroll with fixed-height plots instead.
const _fillMinHeight = 560.0;

/// A section per design v2 §4: panel background, 1 px line, radius 12, 16 px
/// padding, title top-left.
class _Section extends StatelessWidget {
  const _Section({required this.title, required this.child, this.fill = false});
  final String? title;
  final Widget child;

  /// The child takes the height left below the title (the parent must bound it).
  final bool fill;

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
          if (title != null) ...[
            Text(title!, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 12),
          ],
          if (fill) Expanded(child: child) else child,
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Status: phase, condition chip, time left, node chips
// ---------------------------------------------------------------------------

class _StatusSection extends StatelessWidget {
  const _StatusSection({required this.game, required this.observer, required this.connected});
  final PhantomGameState? game;
  final bool observer;
  final bool connected;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;
    final phase = phantomPhaseLabel(l, game?.phase);
    final condition = game?.condition;
    final big = observer;
    final phaseStyle = (big ? theme.textTheme.headlineMedium : theme.textTheme.titleLarge)?.copyWith(fontSize: big ? 40 : null);
    final time = Column(
      crossAxisAlignment: CrossAxisAlignment.end,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(l.phTimeLeft, style: theme.textTheme.bodySmall),
        FittedBox(
          fit: BoxFit.scaleDown,
          alignment: Alignment.centerRight,
          child: Text(
            phantomTimeLeft(game?.remainingS),
            key: const ValueKey('ph-time-left'),
            style: theme.textTheme.headlineSmall?.copyWith(fontSize: big ? 72 : null, color: t.ink),
          ),
        ),
      ],
    );
    return _Section(
      title: observer ? null : l.phSectionStatus,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(phase, key: const ValueKey('ph-phase'), style: phaseStyle),
          const SizedBox(height: 12),
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              // No chip before the first induction: "no condition" says nothing.
              if (condition != null) ...[
                Flexible(child: _ConditionChip(condition: condition, large: big)),
                const SizedBox(width: 12),
              ],
              Expanded(child: time),
            ],
          ),
          const SizedBox(height: 16),
          _NodeChips(game: game, connected: connected, observer: observer),
        ],
      ),
    );
  }
}

/// SYNC / ASYNC, readable from 2 m: 28 sp bold on the phone card, 56 sp in the
/// audience view. Text carries the meaning; the colour is a border and tint.
class _ConditionChip extends StatelessWidget {
  const _ConditionChip({required this.condition, required this.large});
  final PhantomCondition condition;
  final bool large;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;
    final color = phantomConditionColor(condition);
    final label = switch (condition) {
      PhantomCondition.sync => l.phCondSync,
      PhantomCondition.async => l.phCondAsync,
    };
    final fontSize = large ? 56.0 : 28.0;
    return Container(
      key: const ValueKey('ph-condition'),
      padding: EdgeInsets.symmetric(horizontal: large ? 28 : 16, vertical: large ? 14 : 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.16),
        borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
        border: Border.all(color: color, width: 2),
      ),
      child: FittedBox(
        fit: BoxFit.scaleDown,
        child: Text(
          label,
          style: theme.textTheme.headlineSmall?.copyWith(fontSize: fontSize, fontWeight: FontWeight.w800, color: t.ink, letterSpacing: 1),
        ),
      ),
    );
  }
}

class _NodeChips extends StatelessWidget {
  const _NodeChips({required this.game, required this.connected, required this.observer});
  final PhantomGameState? game;
  final bool connected;
  final bool observer;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;
    final haptic = game?.hapticConnected ?? false;
    final bio = game?.bioConnected ?? false;
    final emg = game?.emgLevel;
    // An offline node tells the operator what to do about it (not on the audience screen).
    final fix = observer ? null : l.phNodeFix;
    Widget chip(String name, bool on, {Widget? extra, Key? key}) {
      return Container(
        key: key,
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
        decoration: BoxDecoration(
          color: t.panelRaised,
          borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
          border: Border(left: BorderSide(color: on ? OpusTokens.good : OpusTokens.bad, width: 3)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(name, style: theme.textTheme.labelLarge),
            const SizedBox(height: 2),
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(on ? Icons.check_circle : Icons.cancel, size: 16, color: on ? OpusTokens.good : OpusTokens.bad),
                const SizedBox(width: 6),
                Flexible(child: Text(on ? l.phConnected : l.phOffline, style: theme.textTheme.bodyMedium)),
              ],
            ),
            if (!on && fix != null) ...[const SizedBox(height: 6), Text(fix, style: theme.textTheme.bodySmall)],
            if (extra != null) ...[const SizedBox(height: 8), extra],
          ],
        ),
      );
    }

    final emgBar = bio && emg != null
        ? Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(child: Text(l.phEmgLevel, style: theme.textTheme.bodySmall)),
                  Text('${(emg.clamp(0.0, 1.0) * 100).round()}%', style: theme.textTheme.labelMedium),
                ],
              ),
              const SizedBox(height: 4),
              ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(
                  value: emg.clamp(0.0, 1.0),
                  minHeight: 8,
                  color: OpusTokens.metricColorV3('emg'),
                  backgroundColor: t.rule,
                ),
              ),
            ],
          )
        : null;
    return LayoutBuilder(
      builder: (context, box) {
        final children = [
          chip(l.phNodeHaptic, haptic, key: const ValueKey('ph-node-haptic')),
          chip(l.phNodeBio, bio, extra: emgBar, key: const ValueKey('ph-node-bio')),
        ];
        // Side by side when there is room for two readable chips, else stacked.
        if (box.maxWidth >= 300) {
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [Expanded(child: children[0]), const SizedBox(width: 8), Expanded(child: children[1])],
          );
        }
        return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [children[0], const SizedBox(height: 8), children[1]]);
      },
    );
  }
}

// ---------------------------------------------------------------------------
// Signals: EMG envelope + |accel|
// ---------------------------------------------------------------------------

class _SignalsSection extends StatelessWidget {
  const _SignalsSection({required this.buffer, required this.observer, required this.stale, this.fill = false});
  final TraceBuffer buffer;
  final bool observer;

  /// Draw the traces grey: the link is quiet or gone, so they are not current.
  final bool stale;

  /// The two plots share the height the parent gives (wide layouts); otherwise
  /// they have a fixed height inside a scroll view.
  final bool fill;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;
    final end = buffer.latestMs ?? buffer.windowMs;
    final plotHeight = fill ? null : (observer ? (MediaQuery.sizeOf(context).height * 0.28).clamp(150.0, 320.0) : 128.0);
    Widget slot(Widget w) => fill ? Expanded(child: w) : w;
    Widget plot({
      required String id,
      required String label,
      required List<TracePoint> pts,
      required Color color,
      required String unit,
      required ({double min, double max}) scale,
      required int decimals,
      bool labelMarkers = false,
    }) {
      // The resting level and the latest value as a multiple of it, both read off the trace itself.
      final resting = restingLevel(pts);
      final ratio = resting != null && resting > 0 && pts.isNotEmpty ? pts.last.value / resting : null;
      // Grey when the link is quiet or gone: a frozen trace must not look live.
      final tint = stale ? t.slate : color;
      return slot(
        Column(
          key: ValueKey('ph-plot-$id'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _Dot(color: tint, size: 12),
                const SizedBox(width: 8),
                Expanded(child: Text(label, style: theme.textTheme.labelLarge)),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      pts.isEmpty ? '--' : '${pts.last.value.toStringAsFixed(decimals)}${unit.isEmpty ? '' : ' $unit'}',
                      style: theme.textTheme.labelLarge?.copyWith(color: t.ink),
                    ),
                    if (ratio != null)
                      Text(l.phResting(ratio.toStringAsFixed(1)), key: ValueKey('ph-resting-$id'), style: theme.textTheme.bodySmall),
                  ],
                ),
              ],
            ),
            const SizedBox(height: 8),
            slot(
              PhantomTracePlot(
                points: pts,
                markers: buffer.markers,
                endMs: end,
                windowMs: buffer.windowMs,
                color: tint,
                gridColor: t.rule,
                restingColor: t.slate,
                impactColor: OpusTokens.bad,
                burstColor: t.ink,
                semanticLabel: '$label, ${l.phTraceWindow((buffer.windowMs / 1000).round())}',
                yMin: scale.min,
                yMax: scale.max,
                baseline: resting,
                labelMarkers: labelMarkers,
                height: plotHeight,
                lineWidth: observer ? 3 : 2,
                big: observer,
              ),
            ),
          ],
        ),
      );
    }

    return _Section(
      title: observer ? null : l.phSectionSignals,
      fill: fill,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          plot(
            id: 'emg',
            label: l.phTraceEmg,
            pts: buffer.emg,
            color: OpusTokens.metricColorV3('emg'),
            unit: '',
            scale: phantomEmgScale,
            decimals: 0,
            labelMarkers: true,
          ),
          const SizedBox(height: 16),
          plot(
            id: 'accel',
            label: l.phTraceAccel,
            pts: buffer.accel,
            color: OpusTokens.metricColorV3('accel'),
            unit: l.phUnitAccel,
            scale: phantomAccelScale,
            decimals: 1,
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Controls
// ---------------------------------------------------------------------------

class _ControlsSection extends StatelessWidget {
  const _ControlsSection({
    required this.rules,
    required this.statuses,
    required this.order,
    required this.onCommand,
    required this.onOrder,
  });

  final PhantomCommandRules rules;
  final Map<PhantomCommand, CommandStatus> statuses;
  final String order;
  final void Function(PhantomCommand) onCommand;
  final void Function(String) onOrder;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;

    bool enabled(PhantomCommand c) => rules.isEnabled(c) && statuses[c] != CommandStatus.sent;

    Widget button(PhantomCommand c, String label, {bool primary = false, bool destructive = false}) {
      final status = statuses[c];
      final on = enabled(c);
      final child = Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(label, textAlign: TextAlign.center),
          if (status != null) _StatusLine(status: status),
        ],
      );
      final minSize = const Size.fromHeight(OpusTokens.touchTargetMin + 8);
      final VoidCallback? press = on ? () => onCommand(c) : null;
      final key = ValueKey('ph-cmd-${c.wire}');
      if (primary) {
        return FilledButton(key: key, style: FilledButton.styleFrom(minimumSize: minSize), onPressed: press, child: child);
      }
      if (destructive) {
        return FilledButton(
          key: key,
          style: FilledButton.styleFrom(minimumSize: minSize, backgroundColor: t.alert, foregroundColor: Colors.white),
          onPressed: press,
          child: child,
        );
      }
      return OutlinedButton(key: key, style: OutlinedButton.styleFrom(minimumSize: minSize), onPressed: press, child: child);
    }

    Widget row(Widget a, Widget? b) => Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [Expanded(child: a), const SizedBox(width: 8), Expanded(child: b ?? const SizedBox.shrink())],
          ),
        );

    final orderEnabled = enabled(PhantomCommand.conditionOrder);
    return _Section(
      title: l.phSectionControls,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          row(button(PhantomCommand.start, l.phCmdStart, primary: true), button(PhantomCommand.phaseNext, l.phCmdNext)),
          row(button(PhantomCommand.abortPhase, l.phCmdAbort), button(PhantomCommand.pause, l.phCmdPause)),
          row(button(PhantomCommand.resume, l.phCmdResume), button(PhantomCommand.end, l.phCmdEnd, destructive: true)),
          row(button(PhantomCommand.nextPerson, l.phCmdNextPerson), null),
          const SizedBox(height: 4),
          Text(l.phCondOrder, style: theme.textTheme.labelLarge),
          const SizedBox(height: 8),
          SegmentedButton<String>(
            key: const ValueKey('ph-order'),
            showSelectedIcon: false,
            emptySelectionAllowed: true,
            segments: [
              ButtonSegment(value: 'sync_first', label: Text(l.phOrderSyncFirst, maxLines: 2, textAlign: TextAlign.center)),
              ButtonSegment(value: 'async_first', label: Text(l.phOrderAsyncFirst, maxLines: 2, textAlign: TextAlign.center)),
            ],
            selected: {order},
            onSelectionChanged: orderEnabled ? (s) => onOrder(s.first) : null,
          ),
          if (statuses[PhantomCommand.conditionOrder] != null) ...[
            const SizedBox(height: 6),
            Align(alignment: Alignment.centerLeft, child: _StatusLine(status: statuses[PhantomCommand.conditionOrder]!)),
          ],
        ],
      ),
    );
  }
}

/// "Sent / Confirmed / Failed" with an icon, so the state is not colour-only.
class _StatusLine extends StatelessWidget {
  const _StatusLine({required this.status});
  final CommandStatus status;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final (icon, text, color) = switch (status) {
      CommandStatus.sent => (Icons.schedule, l.phSent, OpusTokens.info),
      CommandStatus.acked => (Icons.check, l.phAcked, OpusTokens.good),
      CommandStatus.failed => (Icons.close, l.phFailed, OpusTokens.bad),
    };
    final ink = Theme.of(context).extension<OpusTokens>()!.ink;
    return Padding(
      padding: const EdgeInsets.only(top: 2),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 14, color: color),
          const SizedBox(width: 4),
          Flexible(
            child: Text(text, key: ValueKey('ph-status-${status.name}'), style: Theme.of(context).textTheme.bodySmall?.copyWith(color: ink)),
          ),
        ],
      ),
    );
  }
}
