import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/data/repositories/mock/phantom_demo_repository.dart';
import 'package:opus_app/features/live/phantom_live_screen.dart';
import 'package:opus_app/features/live/phantom_witness_mirror.dart';
import 'package:opus_app/l10n/app_localizations.dart';
import 'package:opus_app/shared/clinical/phantom_demo_strings.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';

/// "Run Phantom Hand demo": the whole Phantom Hand scope in the app with one
/// tap and no headset, sleeve or hub. It replays one recorded run (SIMULATED
/// data, a scripted participant) in compressed time:
///   1. the operator card through every phase of the run, with a plain-words
///      caption per phase,
///   2. the audience results mirror, fed with the run's own `witness_summary`,
///   3. the demo participant's session report (opened in place of this screen).
/// A Stop / Skip bar is on screen throughout. Nothing here talks to a hub, so it
/// plays the same in mock mode and in hub mode with nothing connected.
class PhantomDemoScreen extends ConsumerWidget {
  const new({this.mirrorSeconds = phantomDemoMirrorSeconds, super.key});

  /// How long the results mirror stays up before the report opens.
  final int mirrorSeconds;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final lang = Localizations.localeOf(context).languageCode;
    final s = PhantomDemoStrings.forLang(lang);
    final script = ref.watch(_scriptProvider);
    return script.when(
      loading: () => _Frame(
        s: s,
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [const CircularProgressIndicator(), const SizedBox(height: 16), Text(s.t('loading'))],
          ),
        ),
      ),
      error: (e, _) => _Frame(s: s, body: ErrorRetryView(message: e.toString(), onRetry: () => ref.invalidate(_scriptProvider))),
      data: (script) => script == null
          ? _Frame(s: s, body: ErrorRetryView(message: s.t('unplayable'), onRetry: () => ref.invalidate(_scriptProvider)))
          : _DemoRun(script: script, mirrorSeconds: mirrorSeconds),
    );
  }
}

/// The recording, built once per visit.
final FutureProvider<PhantomDemoScript?> _scriptProvider = FutureProvider.autoDispose<PhantomDemoScript?>(
  (ref) => const PhantomDemoAssets().script(),
);

/// Title and badge on top; whatever [body] and [bottom] are below.
class _Frame extends StatelessWidget {
  const new({required this.s, required this.body, this.bottom});

  final PhantomDemoStrings s;
  final Widget body;
  final Widget? bottom;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerLeft, child: Text(AppLocalizations.of(context)!.phTitle)),
        actions: [
          Center(child: _Badge(text: s.t('badge'))),
          const SizedBox(width: 12),
        ],
      ),
      body: Column(
        children: [
          Expanded(child: body),
          ?bottom,
        ],
      ),
    );
  }
}

/// Leaves the demo: back to where it was started, or to the patient list when
/// it was the first screen.
void _leave(BuildContext context) {
  if (context.canPop()) {
    context.pop();
  } else {
    context.go('/patients');
  }
}

/// "Demo, simulated data": a bordered pill with words, never colour alone.
class _Badge extends StatelessWidget {
  const new({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;
    return Container(
      key: const ValueKey('ph-demo-badge'),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(
        color: t.panelRaised,
        border: Border.all(color: t.slate, width: 1.5),
        borderRadius: BorderRadius.circular(OpusTokens.radiusControl),
      ),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 190),
        child: FittedBox(
          fit: BoxFit.scaleDown,
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.science_outlined, size: 16, color: t.ink),
              const SizedBox(width: 6),
              Text(text, style: theme.textTheme.labelLarge?.copyWith(color: t.ink)),
            ],
          ),
        ),
      ),
    );
  }
}

enum _Stage { live, mirror }

class _DemoRun extends StatefulWidget {
  const new({required this.script, required this.mirrorSeconds});

  final PhantomDemoScript script;
  final int mirrorSeconds;

  @override
  State<_DemoRun> createState() => _DemoRunState();
}

class _DemoRunState extends State<_DemoRun> {
  final PhantomLiveModel _model = PhantomLiveModel();
  late final MockPhantomLiveRepository _repo;
  StreamSubscription<PhantomLiveSnapshot>? _sub;
  Timer? _mirrorTimer;
  _Stage _stage = _Stage.live;

  @override
  void initState() {
    super.initState();
    // The recording plays itself: start it, then the repository's clock does the rest.
    final engine = PhantomMockEngine(demo: widget.script)..apply(PhantomCommand.start);
    // The first frame already shows the first phase, not "Waiting to start".
    _model.apply(engine.snapshot());
    _repo = MockPhantomLiveRepository(engine: engine, ackDelay: Duration.zero);
    _sub = _repo.watch().listen(_onSnapshot);
  }

  @override
  void dispose() {
    unawaited(_sub?.cancel());
    _mirrorTimer?.cancel();
    super.dispose();
  }

  void _onSnapshot(PhantomLiveSnapshot s) {
    if (!mounted) return;
    setState(() => _model.apply(s));
    if (_stage == _Stage.live && s.runState == PhantomRunState.finished) _toMirror();
  }

  void _toMirror() {
    unawaited(_sub?.cancel());
    _sub = null;
    setState(() => _stage = _Stage.mirror);
    _mirrorTimer = Timer(Duration(seconds: widget.mirrorSeconds), _toReport);
  }

  void _toReport() {
    _mirrorTimer?.cancel();
    if (!mounted) return;
    // In place of this screen: Back from the report returns to where the demo was started.
    context.pushReplacement(demoPhantomReportPath);
  }

  void _skip() => _stage == _Stage.live ? _toMirror() : _toReport();

  @override
  Widget build(BuildContext context) {
    final lang = Localizations.localeOf(context).languageCode;
    final s = PhantomDemoStrings.forLang(lang);
    final game = _model.latest?.game;
    final caption = _stage == _Stage.mirror ? s.t('cap_mirror') : s.caption(game?.phase, game?.condition);
    return _Frame(
      s: s,
      body: _stage == _Stage.live
          ? PhantomLiveView(model: _model, statuses: const {}, onCommand: (_) {}, onOrder: (_) {}, showControls: false)
          : PhantomWitnessMirror(summary: widget.script.witness, lang: lang),
      bottom: _CaptionBar(caption: caption, s: s, onSkip: _skip, onStop: () => _leave(context)),
    );
  }
}

/// The caption of the phase on screen, and the always-visible Skip / Stop.
class _CaptionBar extends StatelessWidget {
  const new({required this.caption, required this.s, required this.onSkip, required this.onStop});

  final String? caption;
  final PhantomDemoStrings s;
  final VoidCallback onSkip;
  final VoidCallback onStop;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;
    final style = theme.textTheme.bodyMedium?.copyWith(color: t.ink, height: 1.35);
    // Room for three lines, so the traces above do not jump from phase to phase.
    final lineHeight = MediaQuery.textScalerOf(context).scale(style?.fontSize ?? 14) * (style?.height ?? 1.35);
    return DecoratedBox(
      decoration: BoxDecoration(color: t.paper, border: Border(top: BorderSide(color: t.rule))),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              ConstrainedBox(
                constraints: BoxConstraints(minHeight: lineHeight * 3),
                child: Text(caption ?? '', key: const ValueKey('ph-demo-caption'), style: style),
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  Expanded(
                    child: FilledButton.tonal(
                      key: const ValueKey('ph-demo-skip'),
                      style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(OpusTokens.touchTargetMin)),
                      onPressed: onSkip,
                      child: Text(s.t('skip')),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: OutlinedButton(
                      key: const ValueKey('ph-demo-stop'),
                      style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(OpusTokens.touchTargetMin)),
                      onPressed: onStop,
                      child: Text(s.t('stop')),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
