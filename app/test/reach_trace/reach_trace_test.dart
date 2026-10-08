import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

Map<String, dynamic> _load(String path) => jsonDecode(File(path).readAsStringSync()) as Map<String, dynamic>;

void main() {
  group('ReachTrace.fromJson on the real healthy-session traces fixture', () {
    final traces = ReachTraceSet.fromJson(_load('assets/fixtures/sessions/healthy__traces.json'));

    test('parses every trial with hand, outcome and decimated points', () {
      expect(traces.trials, isNotEmpty);
      for (final trial in traces.trials) {
        expect(trial.hand, anyOf('left', 'right'));
        expect(trial.points, isNotEmpty);
        expect(trial.points.length, lessThanOrEqualTo(40));
      }
    });

    test('workspaceRadiusM matches the session calibration (0.6 m)', () {
      expect(traces.workspaceRadiusM, closeTo(0.6, 0.001));
    });
  });

  group('ReachTraceGlyph', () {
    testWidgets('renders without error for a real session and an empty session', (tester) async {
      final traces = ReachTraceSet.fromJson(_load('assets/fixtures/sessions/healthy__traces.json'));

      await tester.pumpWidget(
        MaterialApp(home: Scaffold(body: ReachTraceGlyph(traces: traces, size: 160))),
      );
      expect(find.byType(ReachTraceGlyph), findsOneWidget);
      expect(tester.takeException(), isNull);

      await tester.pumpWidget(
        const MaterialApp(home: Scaffold(body: ReachTraceGlyph(traces: ReachTraceSet.empty))),
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets('exposes a semantic label describing the reach data', (tester) async {
      final traces = ReachTraceSet.fromJson(_load('assets/fixtures/sessions/healthy__traces.json'));
      await tester.pumpWidget(
        MaterialApp(home: Scaffold(body: ReachTraceGlyph(traces: traces))),
      );
      final semantics = tester.getSemantics(find.byType(ReachTraceGlyph));
      expect(semantics.label, contains('reaches'));
    });

    testWidgets('AnimatedReachTraceGlyph settles fully drawn when animations are disabled', (tester) async {
      final traces = ReachTraceSet.fromJson(_load('assets/fixtures/sessions/healthy__traces.json'));
      await tester.pumpWidget(
        MediaQuery(
          data: const MediaQueryData(disableAnimations: true),
          child: MaterialApp(home: Scaffold(body: AnimatedReachTraceGlyph(traces: traces))),
        ),
      );
      await tester.pump();
      expect(tester.takeException(), isNull);
    });
  });
}
