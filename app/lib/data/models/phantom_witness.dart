/// The Phantom Hand `witness_summary` trial event (B11, the audience "witness
/// mirror"): this participant's numbers for the two conditions, as the headset
/// sends them in `data` (`contracts/schemas/event.schema.json`,
/// `docs/agent-briefs/ph/03-SPEC.md` §7/§12).
///
/// Pure Dart (no Flutter, no I/O, no codegen). The parser is tolerant on
/// purpose: any value may be missing, null or of the wrong type and then comes
/// back as null, which the mirror draws as "No data" -- never as 0.
///
/// Wire format (decided 8 Oct): `condition_order`, `sync` / `async` objects
/// with `drift_change_cm` (+ = toward the virtual hand), `flinch_latency_ms`,
/// `flinch_strength` (strong | weak | none), `flinch_emg_peak_x`,
/// `flinch_wrist_peak_mps`, `ownership`, `control`, `witness_q4`, optional
/// `agency_q5` (questionnaire values are means on the contract scale 1..7),
/// `sync_minus_async`, and `closing_en` / `closing_hi` (lines joined by "\n").
/// Older spelling accepted: `q4` for `witness_q4`.
library;

/// Contract scale of the questionnaire values on the wire (integer answers
/// 1..7, so a mean lies in [1, 7]).
const phantomAnswerMin = 1.0;
const phantomAnswerMax = 7.0;

/// Wire value minus this = the -3..+3 number the headset shows (X1).
const phantomAnswerMid = 4.0;

/// A questionnaire mean on the wire (1..7) as the -3..+3 number the headset
/// shows; null stays null.
double? phantomSignedAnswer(double? wire) => wire == null ? null : wire - phantomAnswerMid;

/// How strong the flinch after the stone was (headset words).
enum PhantomFlinchStrength {
  strong,
  weak,
  none;

  static PhantomFlinchStrength? parse(Object? raw) {
    if (raw is! String) return null;
    for (final v in PhantomFlinchStrength.values) {
      if (v.name == raw.trim().toLowerCase()) return v;
    }
    return null;
  }
}

double? _finite(Object? raw) => raw is num && raw.isFinite ? raw.toDouble() : null;

/// A questionnaire mean: only values inside the contract scale count. A value
/// outside 1..7 means the sender is not on the contract scale (D2/X1); it is
/// dropped, not clamped, so the mirror never shows a number nobody measured.
double? _answer(Object? raw) {
  final v = _finite(raw);
  return v != null && v >= phantomAnswerMin && v <= phantomAnswerMax ? v : null;
}

double? _nonNegative(Object? raw) {
  final v = _finite(raw);
  return v != null && v >= 0 ? v : null;
}

String? _text(Object? raw) {
  if (raw is! String) return null;
  final t = raw.trim();
  return t.isEmpty ? null : t;
}

/// What was measured or answered in one condition. Null = no data.
class PhantomWitnessCondition {
  const new({
    this.driftChangeCm,
    this.flinchLatencyMs,
    this.flinchStrength,
    this.flinchEmgPeakX,
    this.flinchWristPeakMps,
    this.ownership,
    this.control,
    this.witnessQ4,
    this.agencyQ5,
  });

  /// Where the hand felt to be: post minus pre drift, cm, + = toward the
  /// virtual hand.
  final double? driftChangeCm;

  /// Flinch after the stone (EMG, or hand tracking when there is no muscle
  /// sensor), ms.
  final double? flinchLatencyMs;
  final PhantomFlinchStrength? flinchStrength;

  /// Peak muscle burst as a multiple of resting level.
  final double? flinchEmgPeakX;
  final double? flinchWristPeakMps;

  /// "It felt like my hand": mean of q1, q2 on the wire scale 1..7.
  final double? ownership;

  /// Control question q3 (wire scale 1..7).
  final double? control;

  /// q4, "the awareness that noticed was the same" (wire scale 1..7). A
  /// pointer, not proof.
  final double? witnessQ4;

  /// q5, "I caused that movement" (wire scale 1..7), only when the agency
  /// phase ran.
  final double? agencyQ5;

}

/// Parses one condition object (`data.sync` / `data.async`); anything that is
/// not a map gives an all-null condition.
PhantomWitnessCondition _parseCondition(Object? raw) {
  if (raw is! Map) return const PhantomWitnessCondition();
  return PhantomWitnessCondition(
    driftChangeCm: _finite(raw['drift_change_cm']),
    flinchLatencyMs: _nonNegative(raw['flinch_latency_ms']),
    flinchStrength: PhantomFlinchStrength.parse(raw['flinch_strength']),
    flinchEmgPeakX: _nonNegative(raw['flinch_emg_peak_x']),
    flinchWristPeakMps: _nonNegative(raw['flinch_wrist_peak_mps']),
    ownership: _answer(raw['ownership']),
    control: _answer(raw['control']),
    witnessQ4: _answer(raw['witness_q4']) ?? _answer(raw['q4']),
    agencyQ5: _answer(raw['agency_q5']),
  );
}

/// One `witness_summary` event.
class PhantomWitness {
  const new({
    required this.sync,
    required this.async,
    this.conditionOrder = const [],
    this.closingEn,
    this.closingHi,
  });

  /// No numbers at all (renders every value as "No data").
  static const empty = PhantomWitness(
    sync: PhantomWitnessCondition(),
    async: PhantomWitnessCondition(),
  );

  final PhantomWitnessCondition sync;
  final PhantomWitnessCondition async;

  /// The order the two conditions were run in, `sync` / `async`.
  final List<String> conditionOrder;

  /// Closing lines the headset sent (several lines joined by "\n"), or null.
  final String? closingEn;
  final String? closingHi;

  /// Parses the `data` map of a `witness_summary` event; null when it is not a
  /// map or has neither a `sync` nor an `async` object (the schema requires
  /// one of them).
  static PhantomWitness? tryParse(Object? data) {
    if (data is! Map) return null;
    if (data['sync'] is! Map && data['async'] is! Map) return null;
    final order = data['condition_order'];
    return PhantomWitness(
      sync: _parseCondition(data['sync']),
      async: _parseCondition(data['async']),
      conditionOrder: order is List
          ? [
              for (final c in order)
                if (c == 'sync' || c == 'async') c as String,
            ]
          : const [],
      closingEn: _text(data['closing_en']),
      closingHi: _text(data['closing_hi']),
    );
  }

  /// Parses a whole `trial_event` payload (`{type, data, ...}`); null for any
  /// other event type.
  static PhantomWitness? tryParseEvent(Object? event) {
    if (event is! Map || event['type'] != 'witness_summary') return null;
    return tryParse(event['data']);
  }
}
