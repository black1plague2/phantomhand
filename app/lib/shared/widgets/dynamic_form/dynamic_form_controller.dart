import 'package:flutter/foundation.dart';

import 'package:opus_app/shared/widgets/dynamic_form/field_spec.dart';

/// Holds the current value set + validation state for one rendered
/// `paramSchema`. Framework-agnostic (plain [ChangeNotifier]) so it can be
/// used standalone in widget tests without a Riverpod container.
class DynamicFormController extends ChangeNotifier {
  new({
    required Map<String, dynamic> paramSchema,
    Map<String, dynamic> initialValues = const {},
  })  : fields = parseParamSchema(paramSchema),
        _values = {} {
    for (final f in fields) {
      _values[f.key] = initialValues.containsKey(f.key) ? initialValues[f.key] : f.defaultValue;
    }
    _revalidateAll();
  }

  final List<FieldSpec> fields;
  final Map<String, dynamic> _values;
  final Map<String, String> _errors = {};

  Map<String, dynamic> get values => Map.unmodifiable(_values);
  Map<String, String> get errors => Map.unmodifiable(_errors);
  bool get isValid => _errors.isEmpty;

  dynamic valueOf(String key) => _values[key];
  String? errorOf(String key) => _errors[key];

  void setValue(String key, dynamic value) {
    _values[key] = value;
    final spec = fields.firstWhere((f) => f.key == key);
    _validateOne(spec);
    notifyListeners();
  }

  void resetToDefault(String key) {
    final spec = fields.firstWhere((f) => f.key == key);
    setValue(key, spec.defaultValue);
  }

  /// Applies a manifest preset's partial param set on top of current values.
  void applyPreset(Map<String, dynamic> presetParams) {
    for (final entry in presetParams.entries) {
      _values[entry.key] = entry.value;
    }
    _revalidateAll();
    notifyListeners();
  }

  void _revalidateAll() {
    _errors.clear();
    for (final f in fields) {
      _validateOne(f);
    }
  }

  void _validateOne(FieldSpec spec) {
    _errors.remove(spec.key);
    final error = validateFieldValue(spec, _values[spec.key]);
    if (error != null) _errors[spec.key] = error;
  }
}

/// Pure validation function (also unit-tested directly): required-ness,
/// numeric range, and array item range for `range`-widget fields.
String? validateFieldValue(FieldSpec spec, dynamic value) {
  if (value == null) {
    return spec.required ? 'required' : null;
  }
  if (spec.isArrayRange && value is List) {
    for (final v in value) {
      final n = v as num;
      if (spec.itemMinimum != null && n < spec.itemMinimum!) return 'range';
      if (spec.itemMaximum != null && n > spec.itemMaximum!) return 'range';
    }
    if (value.length == 2 && (value[0] as num) > (value[1] as num)) return 'range';
    return null;
  }
  if (value is num) {
    if (spec.minimum != null && value < spec.minimum!) return 'range';
    if (spec.maximum != null && value > spec.maximum!) return 'range';
  }
  return null;
}
