@Tags(['golden'])
library;

// Run 8 fix (docs/MANUAL_TODO.md's run7 flag): this file was never actually
// tagged `golden` despite the header comment below claiming it, so `flutter
// test --exclude-tags golden` (the fast/default suite run) never skipped its
// 6 pixel-goldens -- they ran, and flaked, as part of every normal test run,
// not just a dedicated golden pass. Tagging it consistently with
// `test/goldens/screen_goldens_test.dart` fixes that part of the complaint
// (a flake in an excludable file no longer destabilizes the default run).
// Also pinned the test binding's window size/device pixel ratio below
// (matching `screen_goldens_test.dart`'s own defensive pattern): this file's
// 6 tests share one process/binding with no size pinned before this fix, so
// whatever the previous test left as the implicit default window state could
// affect this test's canvas rasterization. That's a plausible contributor to
// the reported flakiness, though a single definitive root cause for the
// sub-1% pixel diffs was not isolated in the time available this run -- see
// the session log for what was and wasn't confirmed.
//
// B2/B4: golden coverage for the app's one signature visual
// (`docs/APP_DESIGN.md`: "the patient's actual hand paths... instead of a
// generic icon, avatar, or score tile"), at the doc's three named sizes (48
// list / 160 timeline / full report) and both themes. Run with
// `flutter test test/reach_trace/reach_trace_golden_test.dart --update-goldens`
// once to generate the reference PNGs under test/reach_trace/goldens/, then
// plain `flutter test` to check against them.
//
// Reduced scope vs the brief's full 4-screen x 3-viewport x 2-theme x
// 2-textscale matrix (`docs/agent-briefs/A-next-run.md` B4): this is a pure
// CustomPaint widget with no text, so it needs no font loading and no
// repository/provider scaffolding to render meaningfully -- the full-screen
// goldens (profile/live monitor/program builder/report, each needing mocked
// repositories + real fonts) are flagged as remaining work in this run's
// session log rather than attempted partially.
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

ReachTraceSet _loadHealthyTraces() {
  final json = jsonDecode(File('assets/fixtures/sessions/healthy__traces.json').readAsStringSync())
      as Map<String, dynamic>;
  return ReachTraceSet.fromJson(json);
}

Widget _wrap({required Widget child, required Brightness brightness}) => MaterialApp(
      theme: brightness == Brightness.light ? AppTheme.light() : AppTheme.dark(),
      home: Scaffold(body: Center(child: child)),
    );

void main() {
  final traces = _loadHealthyTraces();

  for (final brightness in Brightness.values) {
    for (final size in [48.0, 160.0, 280.0]) {
      testWidgets('ReachTraceGlyph ${brightness.name} @ ${size.toInt()}dp matches golden', (tester) async {
        tester.view.physicalSize = const Size(800, 600);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });
        await tester.pumpWidget(_wrap(child: ReachTraceGlyph(traces: traces, size: size), brightness: brightness));
        await tester.pumpAndSettle();
        await expectLater(
          find.byType(ReachTraceGlyph),
          matchesGoldenFile('goldens/reach_trace_${brightness.name}_${size.toInt()}.png'),
        );
      });
    }
  }
}
