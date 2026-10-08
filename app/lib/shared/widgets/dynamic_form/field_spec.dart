/// Parses an arbitrary JSON-Schema `paramSchema` (as declared by ANY game's
/// manifest, per `contracts/schemas/game-manifest.schema.json`) into a flat,
/// orderable list of [FieldSpec]s that `dynamic_form.dart` renders.
///
/// This file has zero knowledge of any specific game. It only understands
/// generic JSON Schema (`type`, `enum`, `minimum`/`maximum`, `items`,
/// `default`) plus the `x-ui` extension the manifest schema defines
/// (`widget`, `group`, `order`, `unit`, `help`, `title`). When `x-ui` is missing or
/// incomplete, [FieldWidgetKind] is inferred purely from the JSON `type` --
/// see [_inferWidget] -- so a manifest with no UI hints at all (like the
/// `reach_game` test fixture) still renders something usable.
library;

enum FieldWidgetKind { segmented, stepper, range, slider, switchToggle, dropdown, text, unsupported }

class FieldSpec {
  const new({
    required this.key,
    required this.jsonType,
    required this.widget,
    required this.required,
    this.enumValues,
    this.minimum,
    this.maximum,
    this.minItems,
    this.maxItems,
    this.itemMinimum,
    this.itemMaximum,
    this.defaultValue,
    this.group = 'General',
    this.order = 0,
    this.unit,
    this.help,
    this.title,
  });

  final String key;
  final String jsonType;
  final FieldWidgetKind widget;
  final bool required;
  final List<dynamic>? enumValues;
  final num? minimum;
  final num? maximum;
  final int? minItems;
  final int? maxItems;
  final num? itemMinimum;
  final num? itemMaximum;
  final dynamic defaultValue;
  final String group;
  final int order;
  final String? unit;
  final String? help;

  /// Optional `x-ui.title`: a plain string or a locale map `{en, hi}`.
  final Object? title;

  /// Label for [locale]: `x-ui.title` (locale, then `en`, then any value),
  /// or null when the manifest ships none.
  String? titleFor(String locale) {
    final t = title;
    if (t is String) return t.isEmpty ? null : t;
    if (t is Map) {
      final v = t[locale] ?? t['en'] ?? (t.values.isNotEmpty ? t.values.first : null);
      if (v is String && v.isNotEmpty) return v;
    }
    return null;
  }

  bool get isArrayRange => jsonType == 'array' && widget == FieldWidgetKind.range;
}

/// Parses `manifest.paramSchema` (the whole JSON-Schema object, with
/// `type: "object"`, `properties`, `required`) into ordered field specs
/// grouped by `x-ui.group` in first-seen order, each group's fields sorted by
/// `x-ui.order`.
List<FieldSpec> parseParamSchema(Map<String, dynamic> paramSchema) {
  final properties = (paramSchema['properties'] as Map?)?.cast<String, dynamic>() ?? {};
  final requiredKeys = (paramSchema['required'] as List?)?.cast<String>().toSet() ?? {};

  final specs = <FieldSpec>[];
  properties.forEach((key, rawProp) {
    final prop = (rawProp as Map).cast<String, dynamic>();
    specs.add(_parseField(key, prop, requiredKeys.contains(key)));
  });

  // Stable ordering: group by first-seen order, then by x-ui.order within group.
  final groupFirstSeen = <String, int>{};
  for (var i = 0; i < specs.length; i++) {
    groupFirstSeen.putIfAbsent(specs[i].group, () => i);
  }
  specs.sort((a, b) {
    final groupCompare = groupFirstSeen[a.group]!.compareTo(groupFirstSeen[b.group]!);
    if (groupCompare != 0) return groupCompare;
    return a.order.compareTo(b.order);
  });
  return specs;
}

FieldSpec _parseField(String key, Map<String, dynamic> prop, bool isRequired) {
  final xUi = (prop['x-ui'] as Map?)?.cast<String, dynamic>();
  final jsonType = prop['type'] as String? ?? (prop['enum'] != null ? 'string' : 'string');
  final enumValues = (prop['enum'] as List?)?.cast<dynamic>();

  num? itemMin;
  num? itemMax;
  if (prop['items'] is Map) {
    final items = (prop['items'] as Map).cast<String, dynamic>();
    itemMin = items['minimum'] as num?;
    itemMax = items['maximum'] as num?;
  }

  final explicitWidget = xUi?['widget'] as String?;
  final widget = explicitWidget != null
      ? _widgetFromName(explicitWidget)
      : _inferWidget(jsonType: jsonType, hasEnum: enumValues != null);

  return FieldSpec(
    key: key,
    jsonType: jsonType,
    widget: widget,
    required: isRequired,
    enumValues: enumValues,
    minimum: prop['minimum'] as num?,
    maximum: prop['maximum'] as num?,
    minItems: prop['minItems'] as int?,
    maxItems: prop['maxItems'] as int?,
    itemMinimum: itemMin,
    itemMaximum: itemMax,
    defaultValue: prop['default'],
    group: xUi?['group'] as String? ?? 'General',
    order: (xUi?['order'] as num?)?.toInt() ?? 0,
    unit: xUi?['unit'] as String?,
    help: xUi?['help'] as String?,
    title: xUi?['title'],
  );
}

/// Validates a submitted [value] against [spec]'s required/range constraints.
/// Returns `null` when the value is valid, or a short reason code the form UI
/// maps to a localized message: `'required'` (missing a required field) or
/// `'range'` (a number, or an array-range's start/end, falls outside
/// `minimum`/`maximum` or `itemMinimum`/`itemMaximum`, or start > end).
String? validateFieldValue(FieldSpec spec, dynamic value) {
  if (value == null) {
    return spec.required ? 'required' : null;
  }
  if (spec.isArrayRange) {
    final bounds = (value as List).cast<num>();
    final start = bounds[0];
    final end = bounds[1];
    if (start > end) return 'range';
    if (spec.itemMinimum != null && (start < spec.itemMinimum! || end < spec.itemMinimum!)) {
      return 'range';
    }
    if (spec.itemMaximum != null && (start > spec.itemMaximum! || end > spec.itemMaximum!)) {
      return 'range';
    }
    return null;
  }
  if (value is num) {
    if (spec.minimum != null && value < spec.minimum!) return 'range';
    if (spec.maximum != null && value > spec.maximum!) return 'range';
  }
  return null;
}

/// Validates a whole `adjust_params` payload against a game's `paramSchema`
/// before it's sent to a headset (`HubController.adjustParams`) -- reuses the
/// same per-field rules the program-builder form already enforces, so a
/// clinician adjusting difficulty mid-session gets the same "range"/"required"
/// feedback for free. Returns `{key: reason}` for every failing field; empty
/// if [params] is fully valid.
Map<String, String> validateParamsAgainstSchema(Map<String, dynamic> paramSchema, Map<String, dynamic> params) {
  final errors = <String, String>{};
  for (final spec in parseParamSchema(paramSchema)) {
    final reason = validateFieldValue(spec, params[spec.key]);
    if (reason != null) errors[spec.key] = reason;
  }
  return errors;
}

FieldWidgetKind _widgetFromName(String name) => switch (name) {
      'segmented' => FieldWidgetKind.segmented,
      'stepper' => FieldWidgetKind.stepper,
      'range' => FieldWidgetKind.range,
      'slider' => FieldWidgetKind.slider,
      'switch' => FieldWidgetKind.switchToggle,
      'dropdown' => FieldWidgetKind.dropdown,
      'text' => FieldWidgetKind.text,
      _ => FieldWidgetKind.unsupported,
    };

/// Fallback used whenever a property has no `x-ui.widget` (or an unrecognized
/// one) -- required by brief A4: "fall back based on JSON type".
FieldWidgetKind _inferWidget({required String jsonType, required bool hasEnum}) {
  if (jsonType == 'boolean') return FieldWidgetKind.switchToggle;
  if (hasEnum) return FieldWidgetKind.segmented;
  switch (jsonType) {
    case 'integer':
      return FieldWidgetKind.stepper;
    case 'number':
      return FieldWidgetKind.slider;
    case 'array':
      return FieldWidgetKind.range;
    case 'string':
    default:
      return FieldWidgetKind.text;
  }
}
