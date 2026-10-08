/// Turns a patient's free-text `diagnosis` into the **short form** Design v2
/// §5 asks list rows to show ("Stroke · right"), instead of the full clinical
/// description the fixtures carry ("Post-stroke (ischemic, MCA), mild residual
/// right-sided weakness").
///
/// Derived from the condition data, never hardcoded per patient: a small,
/// ordered keyword table maps the diagnosis text to one short clinical noun.
/// The order matters -- the first match wins -- because real diagnoses name
/// several things at once ("Right MCA stroke with left hemispatial neglect"
/// is a stroke, not a "neglect"). Anything that matches nothing falls back to
/// the diagnosis's first clause, trimmed to one short phrase, so an unknown
/// condition still reads as a label rather than a paragraph.
library;

import 'package:opus_app/data/models/patient.dart';

/// Ordered `(matcher, short label)` pairs. Lowercased substring matching --
/// deliberately dumb and inspectable rather than a regex soup; these are the
/// condition families OPUS is built for (`docs/RESEARCH.md` §upper-limb
/// neurorehab), and an unmatched diagnosis degrades gracefully below.
const List<(String, String)> _conditionKeywords = [
  ('stroke', 'Stroke'),
  ('hemipar', 'Stroke'),
  ('hemipleg', 'Stroke'),
  ('cva', 'Stroke'),
  ('traumatic brain', 'TBI'),
  ('tbi', 'TBI'),
  ('spinal cord', 'SCI'),
  ('sci', 'SCI'),
  ('multiple sclerosis', 'MS'),
  ('parkinson', 'Parkinson'),
  ('cerebral palsy', 'Cerebral palsy'),
];

/// "Post-stroke (ischemic, MCA), mild residual right-sided weakness"
/// -> "Stroke". Returns `null` for an empty diagnosis.
String? conditionShortForm(String? diagnosis) {
  final text = diagnosis?.trim();
  if (text == null || text.isEmpty) return null;
  final lower = text.toLowerCase();
  for (final (needle, label) in _conditionKeywords) {
    if (lower.contains(needle)) return label;
  }
  // Unknown condition: first clause only, so a long description never reaches
  // a list row. Split on the punctuation real diagnoses use to add detail.
  final firstClause = text.split(RegExp(r'[,(;:]')).first.trim();
  if (firstClause.isEmpty) return text;
  final words = firstClause.split(RegExp(r'\s+'));
  final short = words.length <= 3 ? firstClause : words.take(3).join(' ');
  return short[0].toUpperCase() + short.substring(1);
}

/// The full §5 list-row string: "Stroke · right". Drops the side entirely
/// when it carries no information (`AffectedSide.none`), and falls back to
/// the side alone if the diagnosis is empty.
String patientConditionLine(Patient patient) {
  final condition = conditionShortForm(patient.diagnosis);
  final side = patient.affectedSide == AffectedSide.none ? null : patient.affectedSide.name;
  if (condition == null) return side ?? '';
  if (side == null) return condition;
  return '$condition · $side';
}
