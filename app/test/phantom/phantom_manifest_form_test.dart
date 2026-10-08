// A1 item 1: phantom_hand shows up in the program builder from its manifest
// alone (dynamic form, x-ui groups), with zero game-specific param widgets.
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/repositories/mock/mock_manifests_repository.dart';
import 'package:opus_app/shared/widgets/dynamic_form/dynamic_form.dart';
import 'package:opus_app/shared/widgets/dynamic_form/dynamic_form_controller.dart';

void main() {
  test('the mock manifests repository lists phantom_hand next to orchard_reach', () async {
    final all = await MockManifestsRepository().listManifests();
    expect(all.map((m) => m.id), containsAll(['orchard_reach', 'phantom_hand']));
  });

  testWidgets('the phantom_hand manifest renders through the generic DynamicForm, grouped by x-ui.group', (tester) async {
    final manifest = jsonDecode(File('assets/fixtures/manifests/phantom_hand.manifest.json').readAsStringSync()) as Map<String, dynamic>;
    final schema = (manifest['paramSchema'] as Map).cast<String, dynamic>();
    final controller = DynamicFormController(paramSchema: schema);
    addTearDown(controller.dispose);

    await tester.pumpWidget(MaterialApp(home: Scaffold(body: SingleChildScrollView(child: DynamicForm(controller: controller)))));
    await tester.pumpAndSettle();

    expect(find.text('Illusion'), findsOneWidget);
    expect(find.text('Timing'), findsOneWidget);
    expect(find.byType(Slider), findsWidgets);
    expect(find.byType(SegmentedButton<dynamic>), findsWidgets);
    expect(find.byType(Switch), findsWidgets);
    expect(controller.valueOf('condition_order'), 'async_first');
    expect(controller.valueOf('offset_cm'), 15);
    expect(controller.isValid, isTrue);
  });
}
