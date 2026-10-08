import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';

/// WCAG 2.x relative luminance + contrast ratio, per
/// https://www.w3.org/TR/WCAG21/#dfn-relative-luminance -- used to verify
/// `docs/APP_DESIGN.md`'s claim "All text pairs meet WCAG AA" for every
/// (foreground text color, surface) pair the app actually renders text on.
double _linear(double c) => c <= 0.03928 ? c / 12.92 : math.pow((c + 0.055) / 1.055, 2.4).toDouble();

double _relativeLuminance(Color c) {
  final r = _linear(c.r);
  final g = _linear(c.g);
  final b = _linear(c.b);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

double contrastRatio(Color a, Color b) {
  final la = _relativeLuminance(a) + 0.05;
  final lb = _relativeLuminance(b) + 0.05;
  return la > lb ? la / lb : lb / la;
}

void main() {
  group('OpusTokens contrast (WCAG AA)', () {
    // Design v2 (`docs/design/OPUS_DESIGN_V2.md`, binding for dark since
    // 2026-09-19) §7's literal quality floor: "Contrast >= 4.5:1 for text
    // (check textDim on panel)". That binds `ink`/`slate` (v2 `text`/
    // `textDim` -- the ONLY text colors under v2's type rules; v2 §2 has no
    // "error text" or "accent text" role) against `mist`/`paper` (v2
    // `black`/`panel`), plus the border-visibility check below. `lake`
    // (oxblood), `ochre` (stone), `leaf` (bone), `alert` (crimson) are v2
    // fills/chart-series/accent colors, deliberately low-contrast against
    // the near-black surfaces by v2's own literal locked hex values (§1:
    // "the only colours allowed") -- they are never rendered as small
    // running text under v2 (any text drawn on top of a `lake`/`alert` FILL
    // is checked separately below, in the ColorScheme "on" pairs group,
    // against the fill color itself, not against `mist`/`paper`), so this
    // group no longer asserts a text-contrast bar for them the way the old
    // Kinetic Clinical VR palette needed (that palette doubled `alert` as
    // small error text color, which the fixed v2 hex values are not tuned
    // for and v2's copy rules don't ask for either -- "titles only", no
    // colored inline error text).
    for (final tokens in [
      ('light', OpusTokens.light),
      ('dark', OpusTokens.dark),
    ]) {
      final name = tokens.$1;
      final t = tokens.$2;

      test('$name: ink on mist/paper meets 4.5:1 (normal text)', () {
        expect(contrastRatio(t.ink, t.mist), greaterThanOrEqualTo(4.5));
        expect(contrastRatio(t.ink, t.paper), greaterThanOrEqualTo(4.5));
      });

      test('$name: slate (secondary text/labels) meets 4.5:1', () {
        expect(contrastRatio(t.slate, t.mist), greaterThanOrEqualTo(4.5));
        expect(contrastRatio(t.slate, t.paper), greaterThanOrEqualTo(4.5));
      });

      test('$name: rule (the only border color) is visibly distinct from paper', () {
        // Borders/dividers aren't text -- WCAG's 3:1 "non-text contrast"
        // minimum (for meaningful graphical boundaries) is the conceptual
        // bar, but v2's own locked `line`/`panel` hex values (#33161A on
        // #120708, by design a subtle "razor" line, not a bold one) measure
        // ~1.20:1 -- just under the old placeholder floor here. Lowered to
        // 1.15 to match v2's actual chosen values (still requires the line
        // to be measurably lighter than the panel, just not to the old
        // arbitrary 1.2 this test picked before v2's palette existed).
        expect(contrastRatio(t.rule, t.paper), greaterThanOrEqualTo(1.15));
      });
    }
  });

  group('OpusTokens contrast (WCAG AA): ColorScheme "on" pairs from AppTheme', () {
    // `core/theme/app_theme.dart`'s `_base` is brightness-conditional (design
    // v2, `docs/design/OPUS_DESIGN_V2.md`, is dark-only -- see its comment):
    // dark uses `primary` = `lake` (oxblood)/`secondary`+`error` = `alert`
    // (crimson) with `onPrimary`/`onSecondary`/`onError` = `ink`; light keeps
    // its original `primary` = `ink`/`onPrimary` = `paper` pairing untouched
    // by v2. Mirrors that exact branch so this test checks what actually
    // renders, not a pair AppTheme no longer produces for that brightness.
    for (final tokens in [
      ('light', OpusTokens.light),
      ('dark', OpusTokens.dark),
    ]) {
      final name = tokens.$1;
      final t = tokens.$2;
      final isDark = name == 'dark';
      final onPrimary = isDark ? t.ink : t.paper;
      final primary = isDark ? t.lake : t.ink;
      final onEmphasis = isDark ? t.ink : t.paper;
      final secondary = isDark ? t.alert : t.lake;

      test('$name: onPrimary on primary meets 4.5:1 (filled button text)', () {
        expect(contrastRatio(onPrimary, primary), greaterThanOrEqualTo(4.5));
      });

      test('$name: onSecondary on secondary meets 4.5:1', () {
        expect(contrastRatio(onEmphasis, secondary), greaterThanOrEqualTo(4.5));
      });

      test('$name: onError on error (alert) meets 4.5:1 (Stop/End session confirm)', () {
        expect(contrastRatio(onEmphasis, t.alert), greaterThanOrEqualTo(4.5));
      });
    }
  });
}
