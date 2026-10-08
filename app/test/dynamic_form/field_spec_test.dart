import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/widgets/dynamic_form/field_spec.dart';

Map<String, dynamic> _loadManifest(String path) =>
    jsonDecode(File(path).readAsStringSync()) as Map<String, dynamic>;

void main() {
  group('parseParamSchema on the real Orchard Reach manifest', () {
    final manifest = _loadManifest('assets/fixtures/manifests/orchard_reach.manifest.json');
    final paramSchema = (manifest['paramSchema'] as Map).cast<String, dynamic>();
    final specs = parseParamSchema(paramSchema);

    test('parses every declared property', () {
      final propertyCount = (paramSchema['properties'] as Map).length;
      expect(specs.length, propertyCount);
    });

    test('honors explicit x-ui widgets', () {
      final side = specs.firstWhere((s) => s.key == 'side');
      expect(side.widget, FieldWidgetKind.segmented);
      final trialCount = specs.firstWhere((s) => s.key == 'trialCount');
      expect(trialCount.widget, FieldWidgetKind.stepper);
      final reachPercent = specs.firstWhere((s) => s.key == 'reachPercent');
      expect(reachPercent.widget, FieldWidgetKind.range);
      final holdMs = specs.firstWhere((s) => s.key == 'holdMs');
      expect(holdMs.widget, FieldWidgetKind.slider);
      final adaptive = specs.firstWhere((s) => s.key == 'adaptive');
      expect(adaptive.widget, FieldWidgetKind.switchToggle);
    });

    test('marks required fields', () {
      expect(specs.firstWhere((s) => s.key == 'side').required, isTrue);
      expect(specs.firstWhere((s) => s.key == 'trialCount').required, isTrue);
      expect(specs.firstWhere((s) => s.key == 'holdMs').required, isFalse);
    });

    test('carries unit and help text through from x-ui', () {
      final reachPercent = specs.firstWhere((s) => s.key == 'reachPercent');
      expect(reachPercent.unit, '% arm length');
      final neglectBias = specs.firstWhere((s) => s.key == 'neglectBias');
      expect(neglectBias.help, isNotNull);
    });

    test('groups fields and sorts by x-ui.order within a group', () {
      final workspaceFields = specs.where((s) => s.group == 'Workspace').toList();
      expect(workspaceFields.map((s) => s.key), ['reachPercent', 'azimuthRangeDeg', 'elevationRangeDeg']);
    });
  });

  group('parseParamSchema falls back on JSON type for a manifest with NO x-ui hints', () {
    // contracts/fixtures/valid/game-manifest.2.json, copied verbatim -- proves
    // the engine renders a completely different, made-up manifest with zero
    // Orchard-specific code (brief A4).
    final manifest = _loadManifest('test/fixtures/reach_game.manifest.json');
    final paramSchema = (manifest['paramSchema'] as Map).cast<String, dynamic>();
    final specs = parseParamSchema(paramSchema);

    test('infers segmented for an enum field with no widget hint', () {
      final side = specs.firstWhere((s) => s.key == 'side');
      expect(side.widget, FieldWidgetKind.segmented);
      expect(side.enumValues, ['left', 'right']);
    });

    test('infers slider for a plain number field with no widget hint', () {
      final speed = specs.firstWhere((s) => s.key == 'speed');
      expect(speed.widget, FieldWidgetKind.slider);
      expect(speed.minimum, 0.1);
      expect(speed.maximum, 2.0);
    });

    test('fields default to the General group when no x-ui.group is given', () {
      expect(specs.every((s) => s.group == 'General'), isTrue);
    });
  });

  group('validateFieldValue', () {
    const requiredField = FieldSpec(key: 'side', jsonType: 'string', widget: FieldWidgetKind.segmented, required: true);
    const rangeField = FieldSpec(key: 'speed', jsonType: 'number', widget: FieldWidgetKind.slider, required: false, minimum: 0, maximum: 10);
    const rangeArrayField = FieldSpec(
      key: 'reachPercent',
      jsonType: 'array',
      widget: FieldWidgetKind.range,
      required: false,
      itemMinimum: 20,
      itemMaximum: 110,
    );

    test('required field with null value fails', () {
      expect(validateFieldValue(requiredField, null), 'required');
    });

    test('optional field with null value passes', () {
      expect(validateFieldValue(rangeField, null), isNull);
    });

    test('number out of range fails', () {
      expect(validateFieldValue(rangeField, 20), 'range');
      expect(validateFieldValue(rangeField, -1), 'range');
      expect(validateFieldValue(rangeField, 5), isNull);
    });

    test('range array item out of bounds fails', () {
      expect(validateFieldValue(rangeArrayField, [10, 50]), 'range');
      expect(validateFieldValue(rangeArrayField, [40, 200]), 'range');
      expect(validateFieldValue(rangeArrayField, [40, 85]), isNull);
    });

    test('range array with start > end fails', () {
      expect(validateFieldValue(rangeArrayField, [90, 40]), 'range');
    });
  });
}
