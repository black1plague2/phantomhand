import 'dart:math' as math;

import 'package:flutter/cupertino.dart' show MediaQuery;
import 'package:flutter/material.dart';
import 'package:flutter/widgets.dart' show MediaQuery;

import 'package:opus_app/core/theme/opus_tokens.dart';

/// One trial's decimated real wrist path, as produced by
/// `tool/sync_fixtures.dart`'s `_buildTraces` from `kin_*.json` + `events.ndjson`
/// (`assets/fixtures/sessions/${key}__traces.json`). `points` are `[x, z]`
/// pairs in calibration-space meters (x = lateral, z = forward); NOT yet
/// normalized to a drawing surface -- [ReachTraceGlyph] does that against
/// [ReachTraceSet.workspaceRadiusM].
class ReachTracePoint {
  const new(this.x, this.z);
  final double x;
  final double z;
}

class ReachTrace {
  const new({required this.trial, required this.hand, required this.outcome, required this.points});

  factory fromJson(Map<String, dynamic> json) => ReachTrace(
        trial: json['trial'] as int,
        hand: json['hand'] as String,
        outcome: json['outcome'] as String,
        points: (json['points'] as List)
            .map((p) => ReachTracePoint((p[0] as num).toDouble(), (p[1] as num).toDouble()))
            .toList(),
      );

  final int trial;
  final String hand;
  final String outcome;
  final List<ReachTracePoint> points;

  bool get successful => outcome == 'success';
}

/// All of one session's reach traces, as loaded from `${key}__traces.json`.
class ReachTraceSet {
  const new({required this.workspaceRadiusM, required this.trials});

  factory fromJson(Map<String, dynamic> json) => ReachTraceSet(
        workspaceRadiusM: (json['workspaceRadiusM'] as num).toDouble(),
        trials: (json['trials'] as List)
            .map((t) => ReachTrace.fromJson((t as Map).cast<String, dynamic>()))
            .toList(),
      );

  final double workspaceRadiusM;
  final List<ReachTrace> trials;

  static const empty = ReachTraceSet(workspaceRadiusM: 0.6, trials: []);
}

/// The signature visual of OPUS (`docs/APP_DESIGN.md`): draws real patient
/// wrist paths top-down over the workspace, instead of a generic icon or
/// score tile. Left-hand paths are `lake`, right-hand `ochre`; unsuccessful
/// reaches are dashed. A faint `rule`-colored arc at [ReachTraceSet.
/// workspaceRadiusM] shows the workspace boundary.
///
/// Three usage sizes (`docs/APP_DESIGN.md`): 48 dp in session lists, 160 dp in
/// the patient timeline strip, and full-size in the report / live monitor.
class ReachTraceGlyph extends StatelessWidget {
  const new({
    required this.traces,
    this.size = 48,
    this.showWorkspaceArc = true,
    this.progress = 1,
    super.key,
  });

  /// A live monitor can pass a single in-progress [ReachTrace] and animate
  /// [progress] from 0 to 1 as the stroke draws in (400 ms ease-out per the
  /// design's one orchestrated motion) -- see `AnimatedReachTraceGlyph`.
  final ReachTraceSet traces;
  final double size;
  final bool showWorkspaceArc;
  final double progress;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>() ?? OpusTokens.light;
    final label = traces.trials.isEmpty
        ? 'No reach data for this session'
        : '${traces.trials.length} reaches, '
            '${traces.trials.where((r) => r.successful).length} successful';
    return Semantics(
      label: label,
      image: true,
      child: SizedBox(
        width: size,
        height: size,
        child: CustomPaint(
          painter: _ReachTracePainter(
            traces: traces,
            lake: t.lake,
            ochre: t.ochre,
            rule: t.rule,
            showWorkspaceArc: showWorkspaceArc,
            progress: progress,
          ),
        ),
      ),
    );
  }
}

/// Wraps [ReachTraceGlyph] with the 400 ms ease-out draw-in used on the live
/// monitor when a trial ends, honoring [MediaQuery.disableAnimations] (the
/// stroke then appears fully drawn immediately) -- the app's one orchestrated
/// motion per `docs/APP_DESIGN.md`.
class AnimatedReachTraceGlyph extends StatefulWidget {
  const new({
    required this.traces,
    this.size = 200,
    this.showWorkspaceArc = true,
    super.key,
  });

  final ReachTraceSet traces;
  final double size;
  final bool showWorkspaceArc;

  @override
  State<AnimatedReachTraceGlyph> createState() => _AnimatedReachTraceGlyphState();
}

class _AnimatedReachTraceGlyphState extends State<AnimatedReachTraceGlyph>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(vsync: this, duration: const Duration(milliseconds: 400));
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      if (MediaQuery.of(context).disableAnimations) {
        _controller.value = 1;
      } else {
        _controller.forward();
      }
    });
  }

  @override
  void didUpdateWidget(covariant AnimatedReachTraceGlyph oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.traces.trials.length != widget.traces.trials.length) {
      if (MediaQuery.of(context).disableAnimations) {
        _controller.value = 1;
      } else {
        _controller.forward(from: 0);
      }
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
        animation: _controller,
        builder: (context, _) => ReachTraceGlyph(
          traces: widget.traces,
          size: widget.size,
          showWorkspaceArc: widget.showWorkspaceArc,
          progress: Curves.easeOut.transform(_controller.value),
        ),
      );
}

class _ReachTracePainter extends CustomPainter {
  const new({
    required this.traces,
    required this.lake,
    required this.ochre,
    required this.rule,
    required this.showWorkspaceArc,
    required this.progress,
  });

  final ReachTraceSet traces;
  final Color lake;
  final Color ochre;
  final Color rule;
  final bool showWorkspaceArc;
  final double progress;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final scale = (math.min(size.width, size.height) / 2) / math.max(traces.workspaceRadiusM, 0.01);

    Offset project(ReachTracePoint p) => center + Offset(p.x * scale, -p.z * scale);

    if (showWorkspaceArc) {
      final arcPaint = Paint()
        ..color = rule
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1;
      canvas.drawArc(
        Rect.fromCircle(center: center, radius: math.min(size.width, size.height) / 2 - 1),
        math.pi,
        math.pi,
        false,
        arcPaint,
      );
    }

    for (final trial in traces.trials) {
      if (trial.points.length < 2) continue;
      final color = trial.hand == 'left' ? lake : ochre;
      final paint = Paint()
        ..color = color
        ..style = PaintingStyle.stroke
        ..strokeWidth = math.max(1.2, size.width / 32)
        ..strokeCap = StrokeCap.round
        // Visual QA finding: "reach-trace strokes have spiky joins" -- the
        // raw decimated wrist path (`trace_builder.dart`, ~40 points/trial)
        // has small sample-to-sample jitter; with the default miter join
        // every direction change between two straight segments produced a
        // sharp spike. Round joins alone soften the corners; combined with
        // the smoothing below (which removes the jitter that caused sharp
        // angles in the first place) the stroke reads as one continuous
        // curved reach instead of a jagged polyline.
        ..strokeJoin = StrokeJoin.round;

      final projected = trial.points.map(project).toList(growable: false);
      final smoothed = _smooth(projected);

      final drawCount = (smoothed.length * progress).ceil().clamp(2, smoothed.length);
      final path = Path()..moveTo(smoothed.first.dx, smoothed.first.dy);
      for (var i = 1; i < drawCount; i++) {
        path.lineTo(smoothed[i].dx, smoothed[i].dy);
      }

      if (trial.successful) {
        canvas.drawPath(path, paint);
      } else {
        _drawDashed(canvas, path, paint);
      }
    }
  }

  /// A light 3-point centered moving average, keeping the path's real
  /// endpoints fixed (so the trace still starts/ends exactly at home/target)
  /// and smoothing only the interior points. Deliberately simple (not a
  /// Catmull-Rom spline) so it never overshoots or distorts the actual shape
  /// of the recorded reach -- it just removes per-sample jitter that turns
  /// into visible spikes at each line join.
  List<Offset> _smooth(List<Offset> points) {
    if (points.length < 3) return points;
    final out = List<Offset>.filled(points.length, Offset.zero);
    out[0] = points.first;
    out[points.length - 1] = points.last;
    for (var i = 1; i < points.length - 1; i++) {
      out[i] = (points[i - 1] + points[i] * 2 + points[i + 1]) / 4;
    }
    return out;
  }

  void _drawDashed(Canvas canvas, Path path, Paint paint) {
    const dashLength = 3.0;
    const gapLength = 2.5;
    for (final metric in path.computeMetrics()) {
      var distance = 0.0;
      var draw = true;
      while (distance < metric.length) {
        final next = distance + (draw ? dashLength : gapLength);
        if (draw) {
          canvas.drawPath(metric.extractPath(distance, math.min(next, metric.length)), paint);
        }
        distance = next;
        draw = !draw;
      }
    }
  }

  @override
  bool shouldRepaint(covariant _ReachTracePainter oldDelegate) =>
      oldDelegate.traces != traces || oldDelegate.progress != progress;
}
