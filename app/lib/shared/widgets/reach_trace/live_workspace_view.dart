import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

/// The live-monitor workspace view (`docs/APP_DESIGN.md`'s "Live monitor"
/// wireframe): a top-down view, same geometry as [ReachTraceGlyph], that
/// listens to raw `trial_event` payloads from a connected headset
/// (`contracts/LIVE_PROTOCOL.md` -- "the live channel carries no
/// kinematics"). `target_shown` draws a ring at the target; `trial_end` turns
/// that ring into a straight home->target stroke (the real curved path
/// replaces it once its `kin_###.json` chunk is uploaded and the session is
/// re-opened from the report -- this widget only ever draws the live,
/// kinematics-free approximation).
class LiveWorkspaceView extends StatefulWidget {
  const new({
    required this.trialEvents,
    this.workspaceRadiusM = 0.6,
    this.size = 260,
    super.key,
  });

  final Stream<Map<String, dynamic>> trialEvents;
  final double workspaceRadiusM;
  final double size;

  @override
  State<LiveWorkspaceView> createState() => _LiveWorkspaceViewState();
}

class _LiveWorkspaceViewState extends State<LiveWorkspaceView> {
  StreamSubscription<Map<String, dynamic>>? _sub;
  final List<ReachTrace> _completed = [];
  ReachTracePoint? _pendingTarget;
  String? _pendingHand;
  int _trial = 0;

  @override
  void initState() {
    super.initState();
    _sub = widget.trialEvents.listen(_onEvent);
  }

  @override
  void didUpdateWidget(covariant LiveWorkspaceView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.trialEvents != widget.trialEvents) {
      _sub?.cancel();
      _sub = widget.trialEvents.listen(_onEvent);
    }
  }

  @override
  void dispose() {
    _sub?.cancel();
    super.dispose();
  }

  /// Converts a `target_shown` payload's `azimuthDeg`/`reachPercent` (as
  /// emitted by the reference game manifests, see the `healthy` fixture) into
  /// the same `[x, z]` calibration-space meters [ReachTrace] uses (x =
  /// lateral, z = forward), so it can share [ReachTraceGlyph]'s painter.
  ReachTracePoint? _targetPoint(Map<String, dynamic> payload) {
    final target = (payload['target'] as Map?)?.cast<String, dynamic>();
    if (target == null) return null;
    final azimuthDeg = (target['azimuthDeg'] as num?)?.toDouble();
    final reachPercent = (target['reachPercent'] as num?)?.toDouble();
    if (azimuthDeg == null || reachPercent == null) return null;
    final r = (reachPercent / 100).clamp(0.0, 1.0) * widget.workspaceRadiusM;
    final rad = azimuthDeg * math.pi / 180;
    return ReachTracePoint(r * math.sin(rad), r * math.cos(rad));
  }

  void _onEvent(Map<String, dynamic> payload) {
    final type = payload['type'] as String?;
    switch (type) {
      case 'trial_start':
        if (payload['trial'] is num) _trial = (payload['trial'] as num).toInt();
        _pendingTarget = null;
        _pendingHand = null;
      case 'target_shown':
        setState(() => _pendingTarget = _targetPoint(payload));
      case 'movement_onset':
        _pendingHand = payload['hand'] as String?;
      case 'trial_end':
        final target = _pendingTarget;
        if (target != null) {
          setState(() {
            _completed.add(
              ReachTrace(
                trial: _trial,
                hand: _pendingHand ?? (payload['hand'] as String? ?? 'right'),
                outcome: payload['outcome'] as String? ?? 'miss',
                points: [const ReachTracePoint(0, 0), target],
              ),
            );
            _pendingTarget = null;
          });
        }
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>() ?? OpusTokens.light;
    final traces = ReachTraceSet(workspaceRadiusM: widget.workspaceRadiusM, trials: List.unmodifiable(_completed));
    return SizedBox(
      width: widget.size,
      height: widget.size,
      child: Stack(
        alignment: Alignment.center,
        children: [
          AnimatedReachTraceGlyph(traces: traces, size: widget.size),
          if (_pendingTarget != null)
            CustomPaint(
              size: Size(widget.size, widget.size),
              painter: _TargetRingPainter(
                target: _pendingTarget!,
                workspaceRadiusM: widget.workspaceRadiusM,
                color: t.ink,
              ),
            ),
        ],
      ),
    );
  }
}

class _TargetRingPainter extends CustomPainter {
  const new({required this.target, required this.workspaceRadiusM, required this.color});
  final ReachTracePoint target;
  final double workspaceRadiusM;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final scale = (math.min(size.width, size.height) / 2) / math.max(workspaceRadiusM, 0.01);
    final o = center + Offset(target.x * scale, -target.z * scale);
    canvas.drawCircle(
      o,
      math.max(4, size.width / 24),
      Paint()
        ..color = color.withValues(alpha: 0.7)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2,
    );
  }

  @override
  bool shouldRepaint(covariant _TargetRingPainter oldDelegate) => oldDelegate.target != target;
}
