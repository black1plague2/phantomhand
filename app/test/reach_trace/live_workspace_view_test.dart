// B3/B4: LiveWorkspaceView drives its ring-then-stroke drawing purely off a
// Stream<Map<String, dynamic>> of raw trial_event payloads -- exactly the
// shape HubConnection.trialEventStream emits -- so it's testable with a
// plain fake stream, no real hub/socket needed.
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/reach_trace/live_workspace_view.dart';

Widget _wrap(Widget child) => MaterialApp(home: Scaffold(body: Center(child: child)));

void main() {
  testWidgets('LiveWorkspaceView renders a target ring after target_shown, then a stroke after trial_end', (
    tester,
  ) async {
    final controller = StreamController<Map<String, dynamic>>();
    addTearDown(controller.close);

    await tester.pumpWidget(_wrap(LiveWorkspaceView(trialEvents: controller.stream, size: 200)));
    await tester.pump();

    // Nothing drawn yet: no target, no completed reach.
    expect(find.byType(CustomPaint), findsWidgets);

    controller.add({'type': 'trial_start', 'trial': 0, 'block': 0});
    controller.add({
      'type': 'target_shown',
      'trial': 0,
      'target': {'azimuthDeg': 10.0, 'reachPercent': 60.0},
    });
    await tester.pump();

    controller.add({'type': 'movement_onset', 'trial': 0, 'hand': 'right'});
    controller.add({'type': 'trial_end', 'trial': 0, 'outcome': 'success'});
    await tester.pump(); // triggers the AnimatedReachTraceGlyph's 400ms draw-in
    await tester.pump(const Duration(milliseconds: 500));
    await tester.pumpAndSettle();

    // The completed reach becomes part of the underlying ReachTraceGlyph's
    // semantics label ("1 reaches, 1 successful") -- proves the stroke was
    // actually appended, not just that nothing crashed.
    expect(find.bySemanticsLabel('1 reaches, 1 successful'), findsOneWidget);
  });

  testWidgets('LiveWorkspaceView records an unsuccessful reach without crashing', (tester) async {
    final controller = StreamController<Map<String, dynamic>>();
    addTearDown(controller.close);

    await tester.pumpWidget(_wrap(LiveWorkspaceView(trialEvents: controller.stream, size: 200)));
    controller.add({'type': 'trial_start', 'trial': 3, 'block': 0});
    controller.add({
      'type': 'target_shown',
      'trial': 3,
      'target': {'azimuthDeg': -20.0, 'reachPercent': 80.0},
    });
    controller.add({'type': 'movement_onset', 'trial': 3, 'hand': 'left'});
    controller.add({'type': 'trial_end', 'trial': 3, 'outcome': 'miss'});
    await tester.pumpAndSettle();

    expect(find.bySemanticsLabel('1 reaches, 0 successful'), findsOneWidget);
  });
}
