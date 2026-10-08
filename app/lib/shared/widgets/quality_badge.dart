import 'package:flutter/material.dart';

import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';

/// A glyph + word for a metric's `quality` flag (`ok`/`degraded`/`invalid`,
/// `contracts/schemas/metrics.schema.json`), never color alone
/// (`docs/APP_DESIGN.md`): **Reliable** (plain dot) · **Partial tracking**
/// (hatched dot) · **Not usable** (struck-through dot, and the label itself
/// says the value is excluded from trends). Every metric value shown anywhere
/// in the app should carry one of these -- goal G5 ("no metric without a
/// quality flag").
class QualityBadge extends StatelessWidget {
  const new({required this.quality, this.reasons = const [], super.key});

  final String quality;
  final List<String> reasons;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>() ?? OpusTokens.light;
    final color = qualityColor(quality, t);
    final label = switch (quality) {
      'ok' => 'Reliable',
      'degraded' => 'Partial tracking',
      'invalid' => 'Not usable, excluded from trends',
      _ => quality,
    };
    // Plain-language reasons, not opus_analytics' raw wire-format reason
    // codes (e.g. `rate_hz_30.0_below_45`) -- Opus's review note: a
    // clinician reading a tooltip shouldn't have to decode a machine code.
    final plainReasons = reasons.map(qualityReasonLabel).toList();
    final badge = Semantics(
      label: plainReasons.isEmpty ? label : '$label: ${plainReasons.join(', ')}',
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          _QualityGlyph(quality: quality, color: color),
          const SizedBox(width: 6),
          Text(label, style: Theme.of(context).textTheme.labelMedium?.copyWith(color: color)),
        ],
      ),
    );
    if (plainReasons.isEmpty) return badge;
    return Tooltip(message: plainReasons.join(', '), child: badge);
  }
}

/// Translates one raw `quality_reasons` code from
/// `contracts/schemas/metrics.schema.json` (as written by `opus_analytics`)
/// into a plain-language sentence a clinician can read directly in a
/// tooltip, instead of a machine-oriented code like
/// `rate_hz_30.0_below_45`.
///
/// Every known code as of `opus_analytics` v0.2.0 (2026-09-17) is handled
/// explicitly; an unrecognized future code still degrades to *something*
/// readable (spaces instead of underscores) rather than ever showing a raw
/// `snake_case_code` verbatim.
String qualityReasonLabel(String code) {
  final rateMatch = RegExp(r'^rate_hz_([\d.]+)_below_(\d+)$').firstMatch(code);
  if (rateMatch != null) {
    final rate = rateMatch.group(1);
    final threshold = rateMatch.group(2);
    return 'Tracking was $rate updates per second; smoothness is less reliable below $threshold/s.';
  }
  if (code == 'trial_timed_out_before_movement_onset') {
    return 'The trial timed out before the patient began moving.';
  }
  return code.replaceAll('_', ' ');
}

/// The glyph half of a [QualityBadge]: a plain dot (`ok`), a hatched dot
/// (`degraded`), or a struck-through dot (`invalid`) -- always paired with the
/// word, so the meaning never depends on color perception alone.
class _QualityGlyph extends StatelessWidget {
  const new({required this.quality, required this.color});
  final String quality;
  final Color color;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: 12,
        height: 12,
        child: CustomPaint(painter: _QualityGlyphPainter(quality: quality, color: color)),
      );
}

class _QualityGlyphPainter extends CustomPainter {
  const new({required this.quality, required this.color});
  final String quality;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.width / 2;
    final fill = Paint()..color = color;
    canvas.drawCircle(center, radius, fill);
    if (quality == 'degraded') {
      final hatch = Paint()
        ..color = Colors.white.withValues(alpha: 0.6)
        ..strokeWidth = 1;
      for (var dx = -radius; dx <= radius; dx += 3) {
        canvas.drawLine(
          Offset(center.dx + dx, center.dy - radius),
          Offset(center.dx + dx, center.dy + radius),
          hatch,
        );
      }
      canvas.drawCircle(center, radius, Paint()..color = color..style = PaintingStyle.stroke..strokeWidth = 1.5);
    } else if (quality == 'invalid') {
      final strike = Paint()
        ..color = Colors.white
        ..strokeWidth = 1.5;
      canvas.drawLine(Offset(center.dx - radius, center.dy), Offset(center.dx + radius, center.dy), strike);
    }
  }

  @override
  bool shouldRepaint(covariant _QualityGlyphPainter oldDelegate) =>
      oldDelegate.quality != quality || oldDelegate.color != color;
}

/// A labeled value + unit + [QualityBadge], the atomic display unit for any
/// metric anywhere in the app (session report, progress, live monitor).
class MetricCard extends StatelessWidget {
  const new({
    required this.label,
    required this.value,
    required this.unit,
    required this.quality,
    this.qualityReasons = const [],
    super.key,
  });

  final String label;
  final String value;
  final String unit;
  final String quality;
  final List<String> qualityReasons;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(label, style: theme.textTheme.labelLarge),
            const SizedBox(height: 4),
            Text.rich(
              TextSpan(
                text: value,
                style: theme.textTheme.headlineSmall?.tabular,
                children: [
                  if (unit.isNotEmpty) TextSpan(text: ' $unit', style: theme.textTheme.bodyMedium),
                ],
              ),
            ),
            const SizedBox(height: 6),
            QualityBadge(quality: quality, reasons: qualityReasons),
          ],
        ),
      ),
    );
  }
}
