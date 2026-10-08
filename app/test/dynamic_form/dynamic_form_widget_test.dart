import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/dynamic_form/dynamic_form.dart';
import 'package:opus_app/shared/widgets/dynamic_form/dynamic_form_controller.dart';

Map<String, dynamic> _loadJson(String path) =>
    jsonDecode(File(path).readAsStringSync()) as Map<String, dynamic>;

Widget _wrap(Widget child) => MaterialApp(home: Scaffold(body: SingleChildScrollView(child: child)));

void main() {
  testWidgets('renders the real Orchard Reach manifest with zero game-specific code', (tester) async {
    final manifest = _loadJson('assets/fixtures/manifests/orchard_reach.manifest.json');
    final paramSchema = (manifest['paramSchema'] as Map).cast<String, dynamic>();
    final controller = DynamicFormController(paramSchema: paramSchema);
    addTearDown(controller.dispose);

    await tester.pumpWidget(_wrap(DynamicForm(controller: controller)));
    await tester.pumpAndSettle();

    // Group headers from x-ui.group.
    expect(find.text('Setup'), findsOneWidget);
    expect(find.text('Workspace'), findsOneWidget);
    expect(find.text('Task'), findsOneWidget);
    expect(find.text('Cognitive'), findsOneWidget);
    expect(find.text('Difficulty'), findsOneWidget);
    expect(find.text('Feedback'), findsOneWidget);

    // A segmented (side), a stepper (trial count) and a switch (adaptive) all rendered.
    expect(find.byType(SegmentedButton<dynamic>), findsWidgets);
    expect(find.byType(Switch), findsWidgets);
    expect(find.byType(RangeSlider), findsWidgets);

    // Defaults were applied.
    expect(controller.valueOf('side'), 'right');
    expect(controller.valueOf('trialCount'), 20);
    expect(controller.isValid, isTrue);
  });

  testWidgets('uses x-ui.title per locale and never doubles the unit', (tester) async {
    final schema = <String, dynamic>{
      'type': 'object',
      'properties': {
        'offset_cm': {
          'type': 'number',
          'default': 15,
          'x-ui': {
            'widget': 'slider',
            'unit': 'cm',
            'title': {'en': 'Arm offset', 'hi': 'बाँह की दूरी'},
          },
        },
        'plain_title': {
          'type': 'number',
          'default': 1,
          'x-ui': {'widget': 'slider', 'unit': 's', 'title': 'Brushing time (s)'},
        },
        'async_delay_ms': {
          'type': 'number',
          'default': 1,
          'x-ui': {'widget': 'slider', 'unit': 'ms'},
        },
      },
    };
    final controller = DynamicFormController(paramSchema: schema);
    addTearDown(controller.dispose);

    await tester.pumpWidget(_wrap(DynamicForm(controller: controller, locale: 'hi')));
    await tester.pumpAndSettle();
    expect(find.text('बाँह की दूरी (cm)'), findsOneWidget);
    expect(find.text('Brushing time (s)'), findsOneWidget);
    expect(find.text('Async Delay (ms)'), findsOneWidget);
  });

  testWidgets('renders a second, unrelated manifest with no x-ui hints at all', (tester) async {
    final manifest = _loadJson('test/fixtures/reach_game.manifest.json');
    final paramSchema = (manifest['paramSchema'] as Map).cast<String, dynamic>();
    final controller = DynamicFormController(paramSchema: paramSchema);
    addTearDown(controller.dispose);

    await tester.pumpWidget(_wrap(DynamicForm(controller: controller)));
    await tester.pumpAndSettle();

    // Falls back to inferred widgets: enum -> segmented, number -> slider.
    expect(find.byType(SegmentedButton<dynamic>), findsOneWidget);
    expect(find.byType(Slider), findsOneWidget);
    // No default group label configured -> falls back to "General".
    expect(find.text('General'), findsOneWidget);
  });

  testWidgets('shows a required-field validation error and clears it once set', (tester) async {
    final controller = DynamicFormController(
      paramSchema: {
        'type': 'object',
        'required': ['side'],
        'properties': {
          'side': {'enum': ['left', 'right']},
        },
      },
    );
    addTearDown(controller.dispose);

    await tester.pumpWidget(_wrap(DynamicForm(controller: controller)));
    await tester.pumpAndSettle();
    expect(controller.isValid, isFalse);
    expect(find.text('This value is required.'), findsOneWidget);

    await tester.tap(find.text('right'));
    await tester.pumpAndSettle();
    expect(controller.isValid, isTrue);
    expect(find.text('This value is required.'), findsNothing);
  });

  testWidgets('applying a preset overwrites the affected values', (tester) async {
    final manifest = _loadJson('assets/fixtures/manifests/orchard_reach.manifest.json');
    final paramSchema = (manifest['paramSchema'] as Map).cast<String, dynamic>();
    final presets = (manifest['presets'] as List).cast<Map<String, dynamic>>();
    final easy = presets.firstWhere((p) => p['id'] == 'easy');
    final controller = DynamicFormController(paramSchema: paramSchema);
    addTearDown(controller.dispose);

    controller.applyPreset((easy['params'] as Map).cast<String, dynamic>());
    expect(controller.valueOf('trialCount'), 10);
    expect(controller.valueOf('reachPercent'), [35, 60]);
  });
}
