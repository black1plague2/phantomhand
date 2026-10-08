/// The Phantom Hand embodiment report (B19, `docs/agent-briefs/ph/research/
/// R3-app-operator-ux.md` section C): one verdict line, then one row per
/// metric with the two conditions as a dumbbell on a common scale -- where the
/// hand felt to be, it felt like my hand, flinch size (x resting), flinch speed
/// -- and the q4 pointer row last, dimmer. Quality is shown, not hidden:
/// `degraded` = "Partial" (tap for the reasons), `missing` = "No data".
/// "No clear difference this time" is a normal outcome, not an error.
///
/// Pure view of an [Embodiment] (parsed from `metrics.json`); it lives on the
/// report screen's black ground, so it uses the v2 palette like the other
/// report widgets. The strings are an EN + HI table in this file (not the arb).
///
/// Wording: "this time", never "significant" or "proves" (n = 1); nothing says
/// the demo measures, creates or proves consciousness.
library;

import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:opus_app/data/models/embodiment.dart';
import 'package:opus_app/shared/design/v2_colors.dart';
import 'package:opus_app/shared/widgets/section.dart';

// ---------------------------------------------------------------------------
// What counts as a difference (one block, easy to tune). R3 section C.
// ---------------------------------------------------------------------------

/// A drift gap of at least this many cm counts.
const embodimentDriftGapCm = 0.5;

/// A questionnaire gap of at least this many points counts.
const embodimentAnswerGap = 1.0;

/// A flinch size / speed gap of at least this share of the larger value counts.
const embodimentRelativeGap = 0.25;

// ---------------------------------------------------------------------------
// Rows: which metric goes where, and the state of each number
// ---------------------------------------------------------------------------

enum EmbodimentRowId { drift, ownership, flinchSize, flinchSpeed, awareness }

enum EmbodimentUnit { cm, answer, timesResting, ms, metresPerSecondSquared, metresPerSecond }

typedef _Source = ({String key, EmbodimentUnit unit});

/// Per row, the metric to show, in order of preference: the first one that has
/// a number on at least one side is used (muscle sensor off -> arm IMU stands in).
const _chains = <EmbodimentRowId, List<_Source>>{
  EmbodimentRowId.drift: [(key: 'drift_change_cm', unit: EmbodimentUnit.cm)],
  EmbodimentRowId.ownership: [(key: 'ownership', unit: EmbodimentUnit.answer)],
  EmbodimentRowId.flinchSize: [
    (key: 'flinch_emg_peak_x', unit: EmbodimentUnit.timesResting),
    (key: 'flinch_imu_peak', unit: EmbodimentUnit.metresPerSecondSquared),
    (key: 'flinch_wrist_peak_mps', unit: EmbodimentUnit.metresPerSecond),
  ],
  EmbodimentRowId.flinchSpeed: [
    (key: 'flinch_emg_latency_ms', unit: EmbodimentUnit.ms),
    (key: 'flinch_imu_latency_ms', unit: EmbodimentUnit.ms),
    (key: 'flinch_wrist_latency_ms', unit: EmbodimentUnit.ms),
  ],
  EmbodimentRowId.awareness: [(key: 'witness_q4', unit: EmbodimentUnit.answer)],
};

enum EmbodimentCellState { ok, partial, invalid, missing }

/// One number of one condition, as the report may show it.
class EmbodimentCell {
  const new({required this.state, this.value, this.reasons = const []});

  final EmbodimentCellState state;

  /// Only for [EmbodimentCellState.ok] and [EmbodimentCellState.partial].
  final double? value;
  final List<String> reasons;
}

EmbodimentCell _cell(EmbodimentValue? v, EmbodimentUnit unit) {
  if (v == null) return const EmbodimentCell(state: EmbodimentCellState.missing);
  final n = v.value;
  if (v.quality == EmbodimentQuality.invalid) return EmbodimentCell(state: EmbodimentCellState.invalid, reasons: v.reasons);
  if (n == null || v.quality == EmbodimentQuality.missing) {
    return EmbodimentCell(state: EmbodimentCellState.missing, reasons: v.reasons);
  }
  // A questionnaire mean outside 1..7 is not on the contract scale (D2): not usable.
  if (unit == EmbodimentUnit.answer && (n < 1 || n > 7)) {
    return EmbodimentCell(state: EmbodimentCellState.invalid, reasons: [...v.reasons, 'off_scale']);
  }
  return EmbodimentCell(
    state: v.quality == EmbodimentQuality.degraded ? EmbodimentCellState.partial : EmbodimentCellState.ok,
    value: n,
    reasons: v.reasons,
  );
}

/// One row of the report.
class EmbodimentRow {
  const new({required this.id, required this.sourceKey, required this.unit, required this.sync, required this.async});

  final EmbodimentRowId id;

  /// The `metrics.json` key shown on this row.
  final String sourceKey;
  final EmbodimentUnit unit;
  final EmbodimentCell sync;
  final EmbodimentCell async;

  /// A stand-in metric is shown because the preferred one has no number.
  bool get usedFallback => sourceKey != _chains[id]!.first.key;
  bool get hasNumbers => sync.value != null || async.value != null;
  bool get partial => sync.state == EmbodimentCellState.partial || async.state == EmbodimentCellState.partial;

  /// Both numbers are fully trustworthy.
  bool get comparable => sync.state == EmbodimentCellState.ok && async.state == EmbodimentCellState.ok;

  /// In sync minus Delayed, when both sides have a number.
  double? get delta => sync.value == null || async.value == null ? null : sync.value! - async.value!;

  /// The gap is big enough to read as a difference (R3 thresholds).
  bool get gapIsBig {
    final d = delta;
    if (d == null) return false;
    return switch (unit) {
      EmbodimentUnit.cm => d.abs() >= embodimentDriftGapCm,
      EmbodimentUnit.answer => d.abs() >= embodimentAnswerGap,
      _ => math.max(sync.value!, async.value!) > 0 && d.abs() / math.max(sync.value!, async.value!) >= embodimentRelativeGap,
    };
  }

  /// The gap goes into the verdict: trustworthy numbers, a big gap, and not q4
  /// (a pointer, not proof).
  bool get counts => id != EmbodimentRowId.awareness && comparable && gapIsBig;

  /// In sync is the stronger side (more toward the virtual hand, higher,
  /// larger, quicker). Only meaningful when [counts].
  bool get syncWins => id == EmbodimentRowId.flinchSpeed ? delta! < 0 : delta! > 0;
}

/// The five rows of [e], in report order.
List<EmbodimentRow> embodimentRows(Embodiment e) {
  EmbodimentRow row(EmbodimentRowId id, _Source s) => EmbodimentRow(
        id: id,
        sourceKey: s.key,
        unit: s.unit,
        sync: _cell(e.sync[s.key], s.unit),
        async: _cell(e.async[s.key], s.unit),
      );
  return [
    for (final id in EmbodimentRowId.values)
      () {
        final chain = _chains[id]!;
        for (final s in chain) {
          final r = row(id, s);
          if (r.hasNumbers) return r;
        }
        return row(id, chain.first);
      }(),
  ];
}

// ---------------------------------------------------------------------------
// Verdict
// ---------------------------------------------------------------------------

enum EmbodimentVerdictKind { inSync, mixed, none, unreliable }

class EmbodimentVerdict {
  const new(this.kind, this.text);

  final EmbodimentVerdictKind kind;
  final String text;
}

String _join(List<String> items, _Strings s) {
  if (items.length < 2) return items.join();
  return '${items.sublist(0, items.length - 1).join(', ')}${s.t('and')}${items.last}';
}

/// "6.4×" / "3.1" / "96": a value as it is quoted in a verdict clause.
String _quote(double v, EmbodimentUnit unit, {required bool withUnit}) => switch (unit) {
      EmbodimentUnit.timesResting => '${v.toStringAsFixed(1)}×',
      EmbodimentUnit.ms => v.round().toString(),
      EmbodimentUnit.metresPerSecondSquared => '${v.toStringAsFixed(1)}${withUnit ? ' m/s²' : ''}',
      EmbodimentUnit.metresPerSecond => '${v.toStringAsFixed(2)}${withUnit ? ' m/s' : ''}',
      _ => v.toStringAsFixed(1),
    };

String _clause(EmbodimentRow r, _Strings s, {required bool syncIsWinner}) {
  final win = (syncIsWinner ? r.sync : r.async).value!;
  final lose = (syncIsWinner ? r.async : r.sync).value!;
  return switch (r.id) {
    EmbodimentRowId.ownership => s.f('clause_ownership', [_quote(win, r.unit, withUnit: false), _quote(lose, r.unit, withUnit: false)]),
    EmbodimentRowId.drift => s.f('clause_drift', [r.delta!.abs().toStringAsFixed(1)]),
    EmbodimentRowId.flinchSize => s.f('clause_size', [_quote(win, r.unit, withUnit: false), _quote(lose, r.unit, withUnit: true)]),
    EmbodimentRowId.flinchSpeed => s.f('clause_speed', [_quote(win, r.unit, withUnit: false), _quote(lose, r.unit, withUnit: false)]),
    EmbodimentRowId.awareness => '',
  };
}

/// The one generated line. Only trustworthy numbers (quality ok on both
/// sides) can make it say anything; q4 never does.
EmbodimentVerdict embodimentVerdict(List<EmbodimentRow> rows, {String lang = 'en'}) {
  final s = _Strings(lang);
  // The order the clauses are read in: how much it felt like yours first.
  const priority = [
    EmbodimentRowId.ownership,
    EmbodimentRowId.drift,
    EmbodimentRowId.flinchSize,
    EmbodimentRowId.flinchSpeed,
  ];
  final judged = [
    for (final id in priority) ...rows.where((r) => r.id == id),
  ];
  final counted = judged.where((r) => r.counts).toList();
  final anyPartial = rows.any((r) => r.partial);
  if (counted.isEmpty) {
    if (!judged.any((r) => r.comparable)) return EmbodimentVerdict(EmbodimentVerdictKind.unreliable, s.t('verdict_unreliable'));
    return EmbodimentVerdict(EmbodimentVerdictKind.none, s.t('verdict_none') + (anyPartial ? s.t('verdict_partial_tail') : ''));
  }
  final forSync = [for (final r in counted) if (r.syncWins) _clause(r, s, syncIsWinner: true)];
  final forDelayed = [for (final r in counted) if (!r.syncWins) _clause(r, s, syncIsWinner: false)];
  if (forDelayed.isEmpty) return EmbodimentVerdict(EmbodimentVerdictKind.inSync, s.f('verdict_sync', [_join(forSync, s)]));
  final parts = [
    if (forSync.isNotEmpty) s.f('mixed_sync', [_join(forSync, s)]),
    s.f('mixed_delayed', [_join(forDelayed, s)]),
  ];
  return EmbodimentVerdict(EmbodimentVerdictKind.mixed, s.f('verdict_mixed', [parts.join('; ')]));
}

// ---------------------------------------------------------------------------
// Strings (EN + HI). Row names, In sync / Delayed, the pointer caption, "no
// data" and the direction words are the headset's own Hindi; the rest is new
// and needs a native check.
// ---------------------------------------------------------------------------

class _Strings {
  const new(this.lang);
  final String lang;

  static const _table = <String, List<String>>{
    'title': ['Your result', 'आपका परिणाम'],
    'inSync': ['In sync', 'साथ-साथ'],
    'delayed': ['Delayed', 'देरी से'],
    'row_drift': ['Where your hand felt to be', 'आपका हाथ कहाँ महसूस हुआ'],
    'row_ownership': ['It felt like my hand', 'यह मेरा हाथ लगा'],
    'row_flinchSize': ['Flinch after the stone, size', 'पत्थर के बाद झटका, आकार'],
    'row_flinchSpeed': ['Flinch after the stone, speed', 'पत्थर के बाद झटका, गति'],
    'row_awareness': ['The awareness that noticed was the same', 'देखने वाली जागरूकता वही रही'],
    'pointer': ['A pointer, not proof', 'एक संकेत, प्रमाण नहीं'],
    'noData': ['No data', 'कोई डेटा नहीं'],
    'notUsable': ['Not usable', 'उपयोग योग्य नहीं'],
    'partial': ['Partial', 'आंशिक'],
    'same': ['about the same', 'लगभग बराबर'],
    'and': [' and ', ' और '],
    'axis_noShift': ['no shift', 'कोई खिसकाव नहीं'],
    'axis_away': ['away', 'दूर'],
    'axis_toward': ['toward the virtual hand', 'वर्चुअल हाथ की ओर'],
    'axis_resting': ['resting', 'आराम की स्थिति'],
    'axis_quicker': ['quicker', 'जल्दी'],
    'axis_slower': ['slower', 'देर से'],
    'instead': ['Shown instead: {0}', 'इसके बजाय दिखाया गया: {0}'],
    'src_flinch_imu_peak': ['arm jolt', 'बाँह का झटका'],
    'src_flinch_wrist_peak_mps': ['hand speed', 'हाथ की गति'],
    'src_flinch_imu_latency_ms': ['arm jolt timing', 'बाँह के झटके का समय'],
    'src_flinch_wrist_latency_ms': ['hand movement timing', 'हाथ की हरकत का समय'],
    'verdict_none': [
      'No clear difference between in sync and delayed this time.',
      'इस बार साथ-साथ और देरी से के बीच कोई साफ़ अंतर नहीं दिखा।',
    ],
    'verdict_unreliable': [
      'Not enough reliable numbers for a verdict this time.',
      'इस बार निष्कर्ष के लिए भरोसेमंद आँकड़े पर्याप्त नहीं हैं।',
    ],
    'verdict_partial_tail': [' Some numbers are partial.', ' कुछ आँकड़े आंशिक हैं।'],
    'verdict_sync': ['In sync, {0}.', 'साथ-साथ में, {0}।'],
    'verdict_mixed': ['Mixed: {0}.', 'मिला-जुला: {0}।'],
    'mixed_sync': ['in sync, {0}', 'साथ-साथ में, {0}'],
    'mixed_delayed': ['when delayed, {0}', 'देरी से, {0}'],
    'clause_ownership': ['the hand felt more like yours ({0} vs {1})', 'हाथ ज़्यादा अपना लगा ({0} बनाम {1})'],
    'clause_drift': [
      'your sense of where your hand was moved {0} cm further toward the virtual hand',
      'आपके हाथ की महसूस की गई जगह {0} सेमी और वर्चुअल हाथ की ओर खिसकी',
    ],
    'clause_size': ['the flinch was larger ({0} vs {1})', 'झटका बड़ा था ({0} बनाम {1})'],
    'clause_speed': ['the flinch came sooner ({0} vs {1} ms)', 'झटका जल्दी आया ({0} बनाम {1} ms)'],
    'node_absent_bio': ['Muscle sensor was off', 'मांसपेशी सेंसर बंद था'],
    'node_absent_haptic': ['Sleeve was off', 'स्लीव बंद थी'],
    'touch_incomplete': ['Some touches were not felt', 'कुछ स्पर्श महसूस नहीं हुए'],
    'cues_undelivered': ['Sleeve missed touches', 'स्लीव ने कुछ स्पर्श छोड़े'],
    'no_samples_in_window': ['No reading around the stone', 'पत्थर के आसपास कोई माप नहीं'],
    'no_withdrawal_detected': ['No hand movement seen', 'हाथ की कोई हरकत नहीं दिखी'],
    'shared_pre_probe': ['Same starting point used for both', 'दोनों के लिए एक ही शुरुआती बिंदु'],
    'value_missing': ['One side has no number', 'एक ओर कोई संख्या नहीं'],
    'question_absent': ['Question not answered', 'सवाल का जवाब नहीं मिला'],
    'question_not_asked': ['Not asked in this round', 'इस दौर में नहीं पूछा गया'],
    'probe_unconfirmed': ['Pointing check not confirmed', 'इशारे की जाँच पक्की नहीं हुई'],
    'probe_absent': ['Pointing check missing', 'इशारे की जाँच नहीं हुई'],
    'threat_impact': ['The stone did not land on the hand', 'पत्थर हाथ पर नहीं गिरा'],
    'tracking': ['Hand tracking was weak', 'हाथ की ट्रैकिंग कमज़ोर थी'],
    'off_scale': ['Answers were not on the 1 to 7 scale', 'जवाब 1 से 7 के पैमाने पर नहीं थे'],
  };

  String t(String key) {
    final v = _table[key];
    return v == null ? key : v[lang == 'hi' ? 1 : 0];
  }

  String f(String key, List<String> args) {
    var out = t(key);
    for (var i = 0; i < args.length; i++) {
      out = out.replaceAll('{$i}', args[i]);
    }
    return out;
  }

  /// A `quality_reasons` code in words; unknown codes are shown as they are.
  String reason(String code) {
    final key = switch (code) {
      'node_absent_bio' ||
      'node_absent_haptic' ||
      'touch_incomplete' ||
      'cues_undelivered' ||
      'no_samples_in_window' ||
      'no_withdrawal_detected' ||
      'shared_pre_probe' ||
      'probe_unconfirmed' ||
      'off_scale' =>
        code,
      'pre_probe_absent' || 'post_probe_absent' => 'probe_absent',
      'only_one_of_q1_q2' || 'questionnaire_absent' => 'question_absent',
      'threat_impact_absent' || 'threat_impact_not_ok' => 'threat_impact',
      _ when code.startsWith('few_delivered_strokes') => 'cues_undelivered',
      _ when code.endsWith('_value_missing') => 'value_missing',
      _ when code.endsWith('_not_asked') => 'question_not_asked',
      _ when code.endsWith('_absent') => 'question_absent',
      _ when code.startsWith('rate_hz_') || code.startsWith('tracking_loss_') || code.startsWith('sensor_gap_') => 'tracking',
      _ => null,
    };
    return key == null ? code : t(key);
  }
}

// ---------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------

/// "-2.4" / "+2.0" / "0.0" (never "-0.0").
String _signed(double v, int dp) {
  final text = v.toStringAsFixed(dp);
  if (double.parse(text) == 0) return 0.0.toStringAsFixed(dp);
  return v > 0 ? '+$text' : text;
}

/// A number with its unit as the row shows it.
String _valueText(double v, EmbodimentUnit unit) => switch (unit) {
      EmbodimentUnit.cm => '${_signed(v, 1)} cm',
      EmbodimentUnit.answer => v.toStringAsFixed(1),
      EmbodimentUnit.timesResting => '${v.toStringAsFixed(1)}×',
      EmbodimentUnit.ms => '${v.round()} ms',
      EmbodimentUnit.metresPerSecondSquared => '${v.toStringAsFixed(1)} m/s²',
      EmbodimentUnit.metresPerSecond => '${v.toStringAsFixed(2)} m/s',
    };

/// The delta chip: In sync minus Delayed, ms rounded to 10, "~" when a side is
/// partial, words when the gap is small.
String _deltaText(EmbodimentRow r, _Strings s) {
  final d = r.delta!;
  final number = switch (r.unit) {
    EmbodimentUnit.cm => '${_signed(d, 1)} cm',
    EmbodimentUnit.answer => _signed(d, 1),
    EmbodimentUnit.timesResting => '${_signed(d, 1)}×',
    EmbodimentUnit.ms => '${_signed((d / 10).round() * 10.0, 0)} ms',
    EmbodimentUnit.metresPerSecondSquared => '${_signed(d, 1)} m/s²',
    EmbodimentUnit.metresPerSecond => '${_signed(d, 2)} m/s',
  };
  if (r.partial) return '~$number';
  return r.gapIsBig ? number : s.t('same');
}

// ---------------------------------------------------------------------------
// The report
// ---------------------------------------------------------------------------

/// The "Your result" section of the session report for a Phantom Hand session
/// ([embodiment] parsed from `metrics.json`). [lang] is `en` or `hi`.
class EmbodimentReport extends StatelessWidget {
  const new({required this.embodiment, this.lang = 'en', super.key});

  final Embodiment embodiment;
  final String lang;

  @override
  Widget build(BuildContext context) {
    final s = _Strings(lang);
    final rows = embodimentRows(embodiment);
    final verdict = embodimentVerdict(rows, lang: lang);
    final base = Theme.of(context).textTheme.bodyMedium ?? const TextStyle();
    return Section(
      title: s.t('title'),
      child: Align(
        alignment: Alignment.centerLeft,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 640),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                verdict.text,
                key: const ValueKey('ph-embodiment-verdict'),
                style: base.copyWith(fontSize: 16, fontWeight: FontWeight.w700, color: V2Colors.text, height: 1.3),
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 20,
                runSpacing: 6,
                children: [
                  _Key(color: V2Colors.info, solid: true, label: s.t('inSync'), base: base),
                  _Key(color: V2Colors.warn, solid: false, label: s.t('delayed'), base: base),
                ],
              ),
              for (final r in rows)
                _RowTile(
                  key: ValueKey('ph-embodiment-${r.id.name}'),
                  row: r,
                  strings: s,
                  base: base,
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The condition marker: filled dot = In sync, ring = Delayed (never colour alone).
class _Dot extends StatelessWidget {
  const new({required this.color, required this.solid, this.size = 14});

  final Color color;
  final bool solid;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: solid ? color : null,
          border: Border.all(color: color, width: 3),
        ),
      );
}

class _Key extends StatelessWidget {
  const new({required this.color, required this.solid, required this.label, required this.base});

  final Color color;
  final bool solid;
  final String label;
  final TextStyle base;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          _Dot(color: color, solid: solid),
          const SizedBox(width: 8),
          Text(label, style: base.copyWith(fontSize: 14, fontWeight: FontWeight.w600, color: V2Colors.text)),
        ],
      );
}

class _RowTile extends StatefulWidget {
  const new({required this.row, required this.strings, required this.base, super.key});

  final EmbodimentRow row;
  final _Strings strings;
  final TextStyle base;

  @override
  State<_RowTile> createState() => _RowTileState();
}

class _RowTileState extends State<_RowTile> {
  bool _reasonsOpen = false;

  @override
  Widget build(BuildContext context) {
    final r = widget.row;
    final s = widget.strings;
    final base = widget.base;
    final pointer = r.id == EmbodimentRowId.awareness;
    // The pointer row is dimmer: it is a pointer, not a result.
    final nameColor = pointer ? V2Colors.textDim : V2Colors.text;
    final scale = MediaQuery.textScalerOf(context).scale(11) / 11;
    final axis = r.hasNumbers ? _axisFor(r, s) : null;
    // Reasons of a side with no number are always shown; those of a Partial one on tap.
    final missingLines = _reasonLines(s, [(s.t('inSync'), r.sync), (s.t('delayed'), r.async)], partial: false);
    final partialLines = _reasonLines(s, [(s.t('inSync'), r.sync), (s.t('delayed'), r.async)], partial: true);
    return Container(
      margin: const EdgeInsets.only(top: 14),
      padding: const EdgeInsets.only(top: 14),
      decoration: const BoxDecoration(border: Border(top: BorderSide(color: V2Colors.line))),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Text(
                  s.t('row_${r.id.name}'),
                  style: base.copyWith(fontSize: 14, fontWeight: FontWeight.w700, color: nameColor),
                ),
              ),
              if (r.delta != null) ...[
                const SizedBox(width: 8),
                Container(
                  key: ValueKey('ph-embodiment-${r.id.name}-delta'),
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                  decoration: BoxDecoration(
                    color: V2Colors.panelRaised,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    _deltaText(r, s),
                    style: base.copyWith(fontSize: 14, fontWeight: FontWeight.w700, color: V2Colors.text),
                  ),
                ),
              ],
            ],
          ),
          if (r.usedFallback)
            Padding(
              padding: const EdgeInsets.only(top: 2),
              child: Text(
                s.f('instead', [s.t('src_${r.sourceKey}')]),
                style: base.copyWith(fontSize: 12, color: V2Colors.textDim),
              ),
            ),
          const SizedBox(height: 6),
          Wrap(
            spacing: 20,
            runSpacing: 4,
            children: [
              _Legend(color: V2Colors.info, solid: true, label: s.t('inSync'), cell: r.sync, unit: r.unit, strings: s, base: base),
              _Legend(color: V2Colors.warn, solid: false, label: s.t('delayed'), cell: r.async, unit: r.unit, strings: s, base: base),
            ],
          ),
          if (axis != null) ...[
            const SizedBox(height: 6),
            CustomPaint(
              size: Size.fromHeight(34 + 11 * 1.3 * scale),
              painter: _DumbbellPainter(
                axis: axis,
                sync: r.sync,
                async: r.async,
                labelStyle: base.copyWith(fontSize: 11, color: V2Colors.textDim),
                textScaler: MediaQuery.textScalerOf(context),
              ),
            ),
          ],
          if (pointer)
            Text(s.t('pointer'), style: base.copyWith(fontSize: 12, fontStyle: FontStyle.italic, color: V2Colors.textDim)),
          if (r.partial) ...[
            InkWell(
              key: ValueKey('ph-embodiment-${r.id.name}-partial'),
              onTap: () => setState(() => _reasonsOpen = !_reasonsOpen),
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 48),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.contrast, size: 18, color: V2Colors.textDim),
                    const SizedBox(width: 6),
                    Text(s.t('partial'), style: base.copyWith(fontSize: 13, fontWeight: FontWeight.w700, color: V2Colors.textDim)),
                  ],
                ),
              ),
            ),
            if (_reasonsOpen) _Reasons(key: ValueKey('ph-embodiment-${r.id.name}-reasons'), lines: partialLines, base: base),
          ],
          _Reasons(key: ValueKey('ph-embodiment-${r.id.name}-missing'), lines: missingLines, base: base),
        ],
      ),
    );
  }
}

/// "In sync: Sleeve missed touches" lines for the sides whose number is
/// [partial] (flagged) or absent (missing / not usable).
List<String> _reasonLines(_Strings s, List<(String, EmbodimentCell)> sides, {required bool partial}) {
  final lines = <String>[];
  for (final (who, cell) in sides) {
    final wanted = partial ? cell.state == EmbodimentCellState.partial : cell.value == null;
    final reasons = {for (final code in cell.reasons) s.reason(code)};
    if (wanted && reasons.isNotEmpty) lines.add('$who: ${reasons.join(', ')}');
  }
  return lines;
}

/// "In sync  5.5" / "Delayed  No data".
class _Legend extends StatelessWidget {
  const new({
    required this.color,
    required this.solid,
    required this.label,
    required this.cell,
    required this.unit,
    required this.strings,
    required this.base,
  });

  final Color color;
  final bool solid;
  final String label;
  final EmbodimentCell cell;
  final EmbodimentUnit unit;
  final _Strings strings;
  final TextStyle base;

  @override
  Widget build(BuildContext context) {
    final value = cell.value;
    final text = value != null
        ? _valueText(value, unit)
        : (cell.state == EmbodimentCellState.invalid ? strings.t('notUsable') : strings.t('noData'));
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        _Dot(color: color, solid: solid, size: 12),
        const SizedBox(width: 6),
        Flexible(
          child: Text.rich(
            TextSpan(
              children: [
                TextSpan(text: '$label  ', style: base.copyWith(fontSize: 13, color: V2Colors.textDim)),
                TextSpan(
                  text: text,
                  style: base.copyWith(
                    fontSize: 18,
                    fontWeight: FontWeight.w800,
                    color: value == null ? V2Colors.textDim : V2Colors.text,
                    fontStyle: value == null ? FontStyle.italic : FontStyle.normal,
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// The reasons behind a Partial or missing number, per side, in words.
class _Reasons extends StatelessWidget {
  const new({required this.lines, required this.base, super.key});

  final List<String> lines;
  final TextStyle base;

  @override
  Widget build(BuildContext context) {
    if (lines.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 4),
      child: Text(lines.join('\n'), style: base.copyWith(fontSize: 12, color: V2Colors.textDim, height: 1.35)),
    );
  }
}

// ---------------------------------------------------------------------------
// The dumbbell
// ---------------------------------------------------------------------------

class _Axis {
  const new({
    required this.min,
    required this.max,
    required this.minLabel,
    required this.maxLabel,
    this.ref,
    this.refLabel,
  });

  final double min;
  final double max;
  final String minLabel;
  final String maxLabel;

  /// A reference tick (0 = no shift, 4 = middle of the scale, 1x = resting).
  final double? ref;
  final String? refLabel;
}

/// One axis per row, common to both conditions.
_Axis _axisFor(EmbodimentRow r, _Strings s) {
  final top = [r.sync.value, r.async.value].whereType<double>().fold<double>(0, (m, v) => math.max(m, v.abs()));
  switch (r.unit) {
    case EmbodimentUnit.cm:
      final m = math.max(4, top.ceil()).toDouble();
      return _Axis(min: -m, max: m, ref: 0, refLabel: s.t('axis_noShift'), minLabel: s.t('axis_away'), maxLabel: s.t('axis_toward'));
    case EmbodimentUnit.answer:
      return const _Axis(min: 1, max: 7, ref: 4, minLabel: '1', maxLabel: '7');
    case EmbodimentUnit.timesResting:
      final m = math.max(8, (top * 1.15).ceil()).toDouble();
      return _Axis(min: 0, max: m, ref: 1, refLabel: s.t('axis_resting'), minLabel: '0', maxLabel: '${m.round()}×');
    case EmbodimentUnit.ms:
      final m = math.max(300, (top * 1.25 / 50).ceil() * 50).toDouble();
      return _Axis(min: 0, max: m, minLabel: s.t('axis_quicker'), maxLabel: s.t('axis_slower'));
    case EmbodimentUnit.metresPerSecondSquared:
      final m = math.max(16, (top * 1.15).ceil()).toDouble();
      return _Axis(min: 0, max: m, minLabel: '0', maxLabel: '${m.round()} m/s²');
    case EmbodimentUnit.metresPerSecond:
      final m = math.max(1, (top * 1.15 * 2).ceil() / 2).toDouble();
      return _Axis(min: 0, max: m, minLabel: '0', maxLabel: '$m m/s');
  }
}

class _DumbbellPainter extends CustomPainter {
  const new({
    required this.axis,
    required this.sync,
    required this.async,
    required this.labelStyle,
    required this.textScaler,
  });

  final _Axis axis;
  final EmbodimentCell sync;
  final EmbodimentCell async;
  final TextStyle labelStyle;
  final TextScaler textScaler;

  static const _pad = 14.0;
  static const _axisY = 16.0;

  @override
  void paint(Canvas canvas, Size size) {
    const x0 = _pad;
    final x1 = size.width - _pad;
    double px(double v) => x0 + (v - axis.min) / (axis.max - axis.min) * (x1 - x0);

    canvas.drawLine(
      const Offset(x0, _axisY),
      Offset(x1, _axisY),
      Paint()
        ..color = V2Colors.line
        ..strokeWidth = 4
        ..strokeCap = StrokeCap.round,
    );
    final ref = axis.ref;
    if (ref != null) {
      canvas.drawLine(
        Offset(px(ref), _axisY - 10),
        Offset(px(ref), _axisY + 10),
        Paint()
          ..color = V2Colors.textDim
          ..strokeWidth = 2,
      );
    }
    final s = sync.value;
    final a = async.value;
    if (s != null && a != null) {
      canvas.drawLine(
        Offset(px(s), _axisY),
        Offset(px(a), _axisY),
        Paint()
          ..color = V2Colors.textDim
          ..strokeWidth = 3,
      );
    }
    // Delayed: a ring (a bigger one, so a tie still shows both). In sync: a
    // filled dot. A partial number is a dashed ring instead of a solid mark.
    if (a != null) _mark(canvas, Offset(px(a), _axisY), 10, V2Colors.warn, filled: false, dashed: async.state == EmbodimentCellState.partial);
    if (s != null) _mark(canvas, Offset(px(s), _axisY), 6.5, V2Colors.info, filled: true, dashed: sync.state == EmbodimentCellState.partial);

    const y = _axisY + 16;
    final left = _label(axis.minLabel)..paint(canvas, const Offset(x0 - 4, y));
    final right = _label(axis.maxLabel);
    right.paint(canvas, Offset(x1 + 4 - right.width, y));
    final refLabel = axis.refLabel;
    if (ref != null && refLabel != null) {
      // Centred under the tick, but never over an end label.
      final mid = _label(refLabel);
      final lo = x0 - 4 + left.width + 8;
      final hi = x1 + 4 - right.width - mid.width - 8;
      if (hi >= lo) mid.paint(canvas, Offset(math.min(math.max(px(ref) - mid.width / 2, lo), hi), y));
    }
  }

  TextPainter _label(String text) => TextPainter(
        text: TextSpan(text: text, style: labelStyle),
        textDirection: TextDirection.ltr,
        textScaler: textScaler,
      )..layout();

  void _mark(Canvas canvas, Offset c, double r, Color color, {required bool filled, required bool dashed}) {
    if (!dashed) {
      if (filled) {
        canvas.drawCircle(c, r, Paint()..color = color);
      } else {
        canvas.drawCircle(
          c,
          r,
          Paint()
            ..color = color
            ..style = PaintingStyle.stroke
            ..strokeWidth = 3,
        );
      }
      return;
    }
    final stroke = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 3;
    for (var i = 0; i < 8; i++) {
      canvas.drawArc(Rect.fromCircle(center: c, radius: r), i * math.pi / 4, math.pi / 7, false, stroke);
    }
  }

  @override
  bool shouldRepaint(_DumbbellPainter old) =>
      old.axis != axis || old.sync != sync || old.async != async || old.labelStyle != labelStyle || old.textScaler != textScaler;
}
