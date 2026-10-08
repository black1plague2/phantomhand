import 'package:flutter/material.dart';

import 'package:opus_app/data/models/game_manifest.dart';
import 'package:opus_app/shared/widgets/dynamic_form/dynamic_form_controller.dart';
import 'package:opus_app/shared/widgets/dynamic_form/field_spec.dart';
import 'package:opus_app/shared/widgets/section.dart';

/// Renders ANY manifest `paramSchema` via its `x-ui` hints (falling back on
/// JSON type when hints are missing/unknown) -- see `field_spec.dart` for the
/// parsing/inference rules. This widget has no per-game branches: every
/// OPUS game, present or future, is prescribed through this same form
/// (ARCHITECTURE.md §2, brief A4 "No per-game widgets").
class DynamicForm extends StatefulWidget {
  const new({
    required this.controller,
    this.presets = const [],
    this.locale = 'en',
    super.key,
  });

  final DynamicFormController controller;
  final List<GamePreset> presets;
  final String locale;

  @override
  State<DynamicForm> createState() => _DynamicFormState();
}

class _DynamicFormState extends State<DynamicForm> {
  @override
  void initState() {
    super.initState();
    widget.controller.addListener(_onControllerChanged);
  }

  @override
  void dispose() {
    widget.controller.removeListener(_onControllerChanged);
    super.dispose();
  }

  void _onControllerChanged() => setState(() {});

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final groups = <String, List<FieldSpec>>{};
    for (final f in controller.fields) {
      groups.putIfAbsent(f.group, () => []).add(f);
    }

    // Design v2 §5 "Programs": "builder form grouped into sections by param
    // group (Task, Difficulty, Feedback, Safety), each field a plain label +
    // control". One `Section` per group, in manifest order. A3 run1 removed
    // the preset `ActionChip` row that used to sit above the first group --
    // §4 "Delete duplicate/decorative buttons"; presets are still part of the
    // manifest model and `DynamicFormController.applyPreset` still works, they
    // just aren't a row of chips on top of the form any more.
    final entries = groups.entries.toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < entries.length; i++) ...[
          if (i > 0) const SizedBox(height: 16),
          Section(
            title: entries[i].key,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final spec in entries[i].value) _FieldTile(spec: spec, controller: controller, locale: widget.locale),
              ],
            ),
          ),
        ],
      ],
    );
  }
}

class _FieldTile extends StatelessWidget {
  const new({required this.spec, required this.controller, this.locale = 'en'});

  final FieldSpec spec;
  final DynamicFormController controller;
  final String locale;

  @override
  Widget build(BuildContext context) {
    final label = _fieldLabel(spec, locale);
    final error = controller.errorOf(spec.key);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // §5: "each field a plain label + control". A3 run1 removed the
          // per-field help and reset-to-default icon buttons -- two extra
          // buttons on every one of 22 fields, which is the "too many
          // buttons" the design change was called for. The help text still
          // reaches the clinician as the field's accessibility hint.
          Semantics(
            label: label + (spec.required ? ', required' : ''),
            hint: spec.help,
            child: Text(
              _labelWithUnit(label, spec.unit),
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          ),
          _buildInput(context),
          if (error != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                error == 'required' ? 'This value is required.' : 'Value out of range.',
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildInput(BuildContext context) {
    final value = controller.valueOf(spec.key);
    switch (spec.widget) {
      case FieldWidgetKind.switchToggle:
        return Semantics(
          toggled: value == true,
          child: Switch(
            value: value == true,
            onChanged: (v) => controller.setValue(spec.key, v),
          ),
        );
      case FieldWidgetKind.segmented:
        final options = spec.enumValues ?? const [];
        // A2 run13 fix: `SegmentedButton` sizes every segment to its natural
        // (unconstrained) width, so a schema-driven field with enough enum
        // options -- e.g. the OrchardReach manifest's `stage` (5 values) or
        // `sortRule` (6 values), added after this widget was first built for
        // 2-4-option fields like `side`/`graspType` -- overflows on phone
        // width (real golden-test crash: "RenderFlex overflowed by 5.0
        // pixels" on `program_builder phone`). Since this form is
        // deliberately zero-game-specific (docs/APP_DESIGN.md), it can't
        // special-case which fields have many options -- wrapping every
        // segmented control in a horizontal scroll view fixes it uniformly,
        // for any option count, on any viewport.
        return SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: SegmentedButton<dynamic>(
            segments: [
              for (final o in options) ButtonSegment(value: o, label: Text(o.toString())),
            ],
            selected: value != null ? {value} : {},
            emptySelectionAllowed: true,
            onSelectionChanged: (s) => controller.setValue(spec.key, s.isEmpty ? null : s.first),
          ),
        );
      case FieldWidgetKind.stepper:
        final v = (value as num?)?.toInt() ?? 0;
        return Row(
          children: [
            IconButton(
              constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
              icon: const Icon(Icons.remove_circle_outline),
              onPressed: () => controller.setValue(spec.key, v - 1),
            ),
            SizedBox(width: 48, child: Text('$v', textAlign: TextAlign.center)),
            IconButton(
              constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
              icon: const Icon(Icons.add_circle_outline),
              onPressed: () => controller.setValue(spec.key, v + 1),
            ),
          ],
        );
      case FieldWidgetKind.slider:
        final min = (spec.minimum ?? 0).toDouble();
        final max = (spec.maximum ?? 100).toDouble();
        final v = (value as num?)?.toDouble() ?? min;
        return Row(
          children: [
            Expanded(
              child: Slider(
                value: v.clamp(min, max),
                min: min,
                max: max,
                label: v.toStringAsFixed(v == v.roundToDouble() ? 0 : 1),
                onChanged: (nv) => controller.setValue(spec.key, spec.jsonType == 'integer' ? nv.round() : nv),
              ),
            ),
            SizedBox(width: 56, child: Text(v.toStringAsFixed(v == v.roundToDouble() ? 0 : 1))),
          ],
        );
      case FieldWidgetKind.range:
        final min = (spec.itemMinimum ?? 0).toDouble();
        final max = (spec.itemMaximum ?? 100).toDouble();
        final list = (value as List?)?.cast<num>() ?? [min, max];
        final rv = RangeValues(list[0].toDouble().clamp(min, max), list[1].toDouble().clamp(min, max));
        return Row(
          children: [
            Expanded(
              child: RangeSlider(
                values: rv,
                min: min,
                max: max,
                labels: RangeLabels(rv.start.toStringAsFixed(0), rv.end.toStringAsFixed(0)),
                onChanged: (nv) => controller.setValue(spec.key, [nv.start, nv.end]),
              ),
            ),
            SizedBox(width: 90, child: Text('${rv.start.toStringAsFixed(0)}–${rv.end.toStringAsFixed(0)}')),
          ],
        );
      case FieldWidgetKind.dropdown:
        final options = spec.enumValues ?? const [];
        return DropdownButtonFormField<dynamic>(
          initialValue: value,
          items: [for (final o in options) DropdownMenuItem(value: o, child: Text(o.toString()))],
          onChanged: (v) => controller.setValue(spec.key, v),
        );
      case FieldWidgetKind.text:
      case FieldWidgetKind.unsupported:
        if (spec.enumValues != null) {
          // Fallback for an enum-typed field with an unrecognized explicit widget.
          return DropdownButtonFormField<dynamic>(
            initialValue: value,
            items: [for (final o in spec.enumValues!) DropdownMenuItem(value: o, child: Text(o.toString()))],
            onChanged: (v) => controller.setValue(spec.key, v),
          );
        }
        final controllerText = value?.toString() ?? '';
        return TextFormField(
          initialValue: controllerText,
          keyboardType: (spec.jsonType == 'integer' || spec.jsonType == 'number')
              ? const TextInputType.numberWithOptions(decimal: true)
              : TextInputType.text,
          onChanged: (text) {
            if (spec.jsonType == 'integer') {
              controller.setValue(spec.key, int.tryParse(text));
            } else if (spec.jsonType == 'number') {
              controller.setValue(spec.key, double.tryParse(text));
            } else {
              controller.setValue(spec.key, text);
            }
          },
        );
    }
  }
}

/// Label for a field: `x-ui.title` in [locale] when present, else the
/// humanized key (with a trailing unit word like "Ms"/"Cm" stripped when a
/// unit is shown separately).
String _fieldLabel(FieldSpec spec, String locale) {
  final t = spec.titleFor(locale);
  if (t != null) return t;
  final h = _humanize(spec.key);
  if (spec.unit == null) return h;
  final words = h.split(' ');
  if (words.length > 1 && const {'ms', 'cm', 'hz', 's'}.contains(words.last.toLowerCase())) {
    words.removeLast();
  }
  return words.join(' ');
}

/// Appends " (unit)" unless the label already ends with that unit.
String _labelWithUnit(String label, String? unit) {
  if (unit == null || unit.isEmpty) return label;
  final l = label.toLowerCase();
  final u = unit.toLowerCase();
  if (l.endsWith('($u)') || l.endsWith(' $u')) return label;
  return '$label ($unit)';
}

/// `reachPercent` -> "Reach Percent", `side` -> "Side". Manifests never ship a
/// human label for each param (only a group/help), so this is the one place
/// that turns a schema key into UI text.
String _humanize(String key) {
  final withSpaces = key
      .replaceAllMapped(RegExp('([a-z0-9])([A-Z])'), (m) => '${m[1]} ${m[2]}')
      .replaceAll('_', ' ');
  return withSpaces
      .split(' ')
      .where((w) => w.isNotEmpty)
      .map((w) => w[0].toUpperCase() + w.substring(1))
      .join(' ');
}
