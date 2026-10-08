import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:opus_app/data/models/phantom_live.dart';

/// One live trace (EMG envelope or |accel|) over the last [TraceBuffer.windowMs]
/// with vertical markers for `threat_impact` / `emg_burst`. Painted directly
/// (no chart package) so the window is exact: x runs from `latest - window` to
/// `latest`, so the newest sample is always on the right edge.
class PhantomTracePlot extends StatelessWidget {
  const new({
    required this.points,
    required this.markers,
    required this.endMs,
    required this.windowMs,
    required this.color,
    required this.gridColor,
    required this.impactColor,
    required this.burstColor,
    required this.semanticLabel,
    this.minSpan = 1,
    this.height = 96,
    this.lineWidth = 2,
    super.key,
  });

  final List<TracePoint> points;
  final List<TraceMarker> markers;

  /// Session time at the right edge of the plot.
  final double endMs;
  final double windowMs;
  final Color color;
  final Color gridColor;
  final Color impactColor;
  final Color burstColor;
  final String semanticLabel;

  /// Smallest y range drawn, so a flat signal does not look like noise.
  final double minSpan;
  final double height;
  final double lineWidth;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: semanticLabel,
      image: true,
      child: SizedBox(
        height: height,
        width: double.infinity,
        child: CustomPaint(
          painter: _TracePainter(
            points: points,
            markers: markers,
            endMs: endMs,
            windowMs: windowMs,
            color: color,
            gridColor: gridColor,
            impactColor: impactColor,
            burstColor: burstColor,
            minSpan: minSpan,
            lineWidth: lineWidth,
          ),
        ),
      ),
    );
  }
}

class _TracePainter extends CustomPainter {
  _TracePainter({
    required this.points,
    required this.markers,
    required this.endMs,
    required this.windowMs,
    required this.color,
    required this.gridColor,
    required this.impactColor,
    required this.burstColor,
    required this.minSpan,
    required this.lineWidth,
  });

  final List<TracePoint> points;
  final List<TraceMarker> markers;
  final double endMs;
  final double windowMs;
  final Color color;
  final Color gridColor;
  final Color impactColor;
  final Color burstColor;
  final double minSpan;
  final double lineWidth;

  @override
  void paint(Canvas canvas, Size size) {
    final startMs = endMs - windowMs;
    double x(double t) => (t - startMs) / windowMs * size.width;

    final grid = Paint()
      ..color = gridColor
      ..strokeWidth = 1;
    for (final f in [0.0, 0.5, 1.0]) {
      final y = f * (size.height - 1) + 0.5;
      canvas.drawLine(Offset(0, y), Offset(size.width, y), grid);
    }

    // y range with 10 % padding, never narrower than minSpan.
    var lo = double.infinity;
    var hi = -double.infinity;
    for (final p in points) {
      lo = math.min(lo, p.value);
      hi = math.max(hi, p.value);
    }
    if (points.isEmpty) {
      lo = 0;
      hi = minSpan;
    }
    var span = hi - lo;
    if (span < minSpan) {
      final mid = (hi + lo) / 2;
      lo = mid - minSpan / 2;
      hi = mid + minSpan / 2;
      span = minSpan;
    }
    lo -= span * 0.1;
    hi += span * 0.1;
    const topPad = 6.0;
    double y(double v) => topPad + (1 - (v - lo) / (hi - lo)) * (size.height - topPad - 2);

    // Markers first, so the trace draws over them.
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
      old.minSpan != minSpan;
}
