import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/l10n/app_localizations.dart';

/// One live trace (EMG envelope or |accel|) over the last [windowMs], drawn so
/// it can prove a flinch: a y scale ([yMin]..[yMax]) labelled at the left with
/// the numbers actually drawn, a dashed resting [baseline], and, when
/// [labelMarkers] is set, the latest stone-impact / muscle-burst markers named
/// where they happen. The caller picks the scale: fixed for |accel|, following
/// the data for EMG (the plot never rescales by itself). Painted directly (no
/// chart package) so the window is exact: x runs from `endMs - windowMs` to
/// `endMs`, so the newest sample is always on the right edge. [height] null =
/// fill the height the parent gives.
class PhantomTracePlot extends StatelessWidget {
  const new({
    required this.points,
    required this.markers,
    required this.endMs,
    required this.windowMs,
    required this.color,
    required this.gridColor,
    required this.restingColor,
    required this.impactColor,
    required this.burstColor,
    required this.semanticLabel,
    required this.yMin,
    required this.yMax,
    this.baseline,
    this.labelMarkers = false,
    this.height,
    this.lineWidth = 2,
    this.big = false,
    super.key,
  });

  final List<TracePoint> points;
  final List<TraceMarker> markers;

  /// Session time at the right edge of the plot.
  final double endMs;
  final double windowMs;
  final Color color;
  final Color gridColor;
  final Color restingColor;
  final Color impactColor;
  final Color burstColor;
  final String semanticLabel;

  /// y range, in the signal's own unit; values outside it sit on the edge.
  final double yMin;
  final double yMax;

  /// Resting level to draw as a dashed line, or null while it is unknown.
  final double? baseline;
  final bool labelMarkers;
  final double? height;
  final double lineWidth;

  /// Audience size: larger axis and marker labels.
  final bool big;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>()!;
    final style = theme.textTheme.bodySmall!.copyWith(fontSize: big ? 16 : null, color: t.slate);
    final scaler = MediaQuery.textScalerOf(context);
    // Room for four tabular digits at the label size plus a gap; the same for
    // every plot so the time axes of stacked plots line up.
    final gutter = scaler.scale(style.fontSize!) * 3.4;
    final rowHeight = scaler.scale(style.fontSize! * 1.4) + 2;
    final startMs = endMs - windowMs;

    TraceMarker? latest(TraceMarkerKind kind) {
      TraceMarker? best;
      for (final m in markers) {
        if (m.kind != kind || m.tMs < startMs || m.tMs > endMs) continue;
        if (best == null || m.tMs > best.tMs) best = m;
      }
      return best;
    }

    final impact = labelMarkers ? latest(TraceMarkerKind.threatImpact) : null;
    final burst = labelMarkers ? latest(TraceMarkerKind.emgBurst) : null;

    final area = Row(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SizedBox(
          width: gutter,
          child: Padding(
            padding: const EdgeInsets.only(right: 6),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(yMax.toStringAsFixed(0), style: style, softWrap: false),
                Text(yMin.toStringAsFixed(0), style: style, softWrap: false),
              ],
            ),
          ),
        ),
        Expanded(
          child: LayoutBuilder(
            builder: (context, box) {
              double x(double tMs) => (tMs - startMs) / windowMs * box.maxWidth;
              Widget label(TraceMarker m, String text, int row) => Positioned.fill(
                    child: CustomSingleChildLayout(
                      delegate: _MarkerLabelLayout(x: x(m.tMs), top: 2 + row * rowHeight),
                      child: Text(text, style: style.copyWith(color: t.ink)),
                    ),
                  );
              return Stack(
                children: [
                  Positioned.fill(
                    child: CustomPaint(
                      painter: _TracePainter(
                        points: points,
                        markers: markers,
                        endMs: endMs,
                        windowMs: windowMs,
                        color: color,
                        gridColor: gridColor,
                        restingColor: restingColor,
                        impactColor: impactColor,
                        burstColor: burstColor,
                        yMin: yMin,
                        yMax: yMax,
                        baseline: baseline,
                        lineWidth: lineWidth,
                      ),
                    ),
                  ),
                  if (points.isEmpty) Center(child: Text(l.phNoSignal, style: style)),
                  if (impact != null) label(impact, l.phMarkerImpact, 0),
                  if (burst != null) label(burst, l.phMarkerBurst, 1),
                ],
              );
            },
          ),
        ),
      ],
    );

    return Semantics(
      label: semanticLabel,
      image: true,
      child: Column(
        mainAxisSize: height == null ? MainAxisSize.max : MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (height == null) Expanded(child: area) else SizedBox(height: height, child: area),
          const SizedBox(height: 4),
          Row(
            children: [
              SizedBox(width: gutter),
              Expanded(child: Text(l.phAxisAgo((windowMs / 1000).round()), style: style, maxLines: 1, overflow: TextOverflow.ellipsis)),
              Text(l.phAxisNow, style: style),
            ],
          ),
        ],
      ),
    );
  }
}

/// Puts a marker's name just left of its line, where the trace is still at rest
/// before the stone lands, or just right of it when there is no room. Stays
/// inside the plot either way.
class _MarkerLabelLayout extends SingleChildLayoutDelegate {
  const new({required this.x, required this.top});
  final double x;
  final double top;

  @override
  BoxConstraints getConstraintsForChild(BoxConstraints constraints) => constraints.loosen();

  @override
  Offset getPositionForChild(Size size, Size childSize) {
    final left = x - 4 - childSize.width;
    final dx = left >= 0 ? left : x + 4;
    return Offset(dx.clamp(0.0, math.max(0, size.width - childSize.width)), top);
  }

  @override
  bool shouldRelayout(_MarkerLabelLayout old) => old.x != x || old.top != top;
}

class _TracePainter extends CustomPainter {
  _TracePainter({
    required this.points,
    required this.markers,
    required this.endMs,
    required this.windowMs,
    required this.color,
    required this.gridColor,
    required this.restingColor,
    required this.impactColor,
    required this.burstColor,
    required this.yMin,
    required this.yMax,
    required this.baseline,
    required this.lineWidth,
  });

  final List<TracePoint> points;
  final List<TraceMarker> markers;
  final double endMs;
  final double windowMs;
  final Color color;
  final Color gridColor;
  final Color restingColor;
  final Color impactColor;
  final Color burstColor;
  final double yMin;
  final double yMax;
  final double? baseline;
  final double lineWidth;

  @override
  void paint(Canvas canvas, Size size) {
    final startMs = endMs - windowMs;
    double x(double t) => (t - startMs) / windowMs * size.width;
    // Fixed scale: values past either end sit on the edge instead of being rescaled.
    const edge = 1.0;
    double y(double v) => (size.height - edge) - ((v - yMin) / (yMax - yMin)).clamp(0.0, 1.0) * (size.height - 2 * edge);

    final grid = Paint()
      ..color = gridColor
      ..strokeWidth = 1;
    for (final f in [0.0, 0.5, 1.0]) {
      final gy = f * (size.height - 1) + 0.5;
      canvas.drawLine(Offset(0, gy), Offset(size.width, gy), grid);
    }

    // The resting level, dashed so it reads as a reference and not as data.
    final rest = baseline;
    if (rest != null) {
      final ry = y(rest);
      final dash = Paint()
        ..color = restingColor
        ..strokeWidth = 1.5;
      for (var dx = 0.0; dx < size.width; dx += 10) {
        canvas.drawLine(Offset(dx, ry), Offset(math.min(dx + 6, size.width), ry), dash);
      }
    }

    // Markers next, so the trace draws over them.
    for (final m in markers) {
      if (m.tMs < startMs || m.tMs > endMs) continue;
      final mx = x(m.tMs);
      final c = m.kind == TraceMarkerKind.threatImpact ? impactColor : burstColor;
      final paint = Paint()
        ..color = c
        ..strokeWidth = 2;
      if (m.kind == TraceMarkerKind.threatImpact) {
        canvas.drawLine(Offset(mx, 0), Offset(mx, size.height), paint);
      } else {
        // dashed, so impact and burst stay distinguishable without colour
        for (var dy = 0.0; dy < size.height; dy += 8) {
          canvas.drawLine(Offset(mx, dy), Offset(mx, math.min(dy + 4, size.height)), paint);
        }
      }
      final tri = Path()
        ..moveTo(mx - 5, 0)
        ..lineTo(mx + 5, 0)
        ..lineTo(mx, 7)
        ..close();
      canvas.drawPath(tri, Paint()..color = c);
    }

    if (points.length >= 2) {
      final path = Path();
      var first = true;
      for (final p in points) {
        if (p.tMs < startMs) continue;
        final px = x(p.tMs);
        final py = y(p.value);
        if (first) {
          path.moveTo(px, py);
          first = false;
        } else {
          path.lineTo(px, py);
        }
      }
      canvas.drawPath(
        path,
        Paint()
          ..color = color
          ..style = PaintingStyle.stroke
          ..strokeWidth = lineWidth
          ..strokeJoin = StrokeJoin.round
          ..strokeCap = StrokeCap.round,
      );
    }
  }

  @override
  bool shouldRepaint(covariant _TracePainter old) =>
      old.points != points ||
      old.markers != markers ||
      old.endMs != endMs ||
      old.color != color ||
      old.baseline != baseline ||
      old.yMin != yMin ||
      old.yMax != yMax;
}
