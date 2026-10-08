// B11: the tolerant parser of the Phantom Hand `witness_summary` event
// (lib/data/models/phantom_witness.dart). Real fixtures are read from the repo:
// contracts/fixtures/valid/event.11.json and the witness line of
// contracts/fixtures/sessions/phantom_hand_min/events.ndjson.
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:path/path.dart' as p;

String _contracts(String rel) => p.normalize(p.join(Directory.current.path, '..', 'contracts', rel));

Map<String, dynamic> _json(String path) => jsonDecode(File(path).readAsStringSync()) as Map<String, dynamic>;

Map<String, dynamic> _witnessLineOfSessionFixture() {
  final line = File(_contracts('fixtures/sessions/phantom_hand_min/events.ndjson'))
      .readAsLinesSync()
      .firstWhere((l) => l.contains('"witness_summary"'));
  return jsonDecode(line) as Map<String, dynamic>;
}

/// The format the headset sends (decided 8 Oct): every key present.
Map<String, dynamic> _full() => {
      'condition_order': ['async', 'sync'],
      'sync': {
        'drift_change_cm': 2.0,
        'flinch_latency_ms': 96,
        'flinch_strength': 'strong',
        'flinch_emg_peak_x': 6.4,
        'flinch_wrist_peak_mps': 0.42,
        'ownership': 5.5,
        'control': 3.0,
        'witness_q4': 6,
        'agency_q5': 6.5,
      },
      'async': {
        'drift_change_cm': 0.0,
        'flinch_latency_ms': 140,
        'flinch_strength': 'weak',
        'flinch_emg_peak_x': 2.1,
        'flinch_wrist_peak_mps': null,
        'ownership': 2.5,
        'control': 3.0,
        'witness_q4': 6,
      },
      'sync_minus_async': {'drift_change_cm': 2.0, 'ownership': 3.0},
      'closing_en': 'The body changed.\nYou noticed every change.',
      'closing_hi': 'शरीर बदला।\nआपने हर बदलाव देखा।',
    };

void main() {
  group('fixtures from the repo', () {
    test('contracts/fixtures/valid/event.11.json parses; flinch latency / strength are simply absent', () {
      final raw = _json(_contracts('fixtures/valid/event.11.json'));
      final w = PhantomWitness.tryParseEvent(raw)!;
      final data = raw['data'] as Map<String, dynamic>;
      final rawSync = data['sync'] as Map<String, dynamic>;
      final rawAsync = data['async'] as Map<String, dynamic>;

      expect(w.conditionOrder, ['async', 'sync']);
      expect(w.sync.ownership, 5.5);
      expect(w.async.ownership, 2.5);
      expect(w.sync.control, 3.0);
      expect(w.sync.flinchEmgPeakX, 6.4);
      expect(w.async.flinchEmgPeakX, 2.1);
      // The fixture uses the new spelling; the drift sign is whatever the file says.
      expect(w.sync.witnessQ4, 6.0);
      expect(w.async.witnessQ4, 6.0);
      expect(w.sync.driftChangeCm, (rawSync['drift_change_cm'] as num).toDouble());
      expect(w.async.driftChangeCm, (rawAsync['drift_change_cm'] as num).toDouble());
      // Not in the fixture -> null, never 0.
      expect(w.sync.flinchLatencyMs, isNull);
      expect(w.sync.flinchStrength, isNull);
      expect(w.sync.agencyQ5, isNull);
      expect(w.closingEn, isNull);
      expect(w.closingHi, isNull);
    });

    test('phantom_hand_min events.ndjson witness_summary line parses (post - pre drift 2.0 / 0.0)', () {
      final w = PhantomWitness.tryParseEvent(_witnessLineOfSessionFixture())!;
      expect(w.conditionOrder, ['async', 'sync']);
      expect(w.sync.driftChangeCm, 2.0);
      expect(w.async.driftChangeCm, 0.0);
      expect(w.sync.ownership, 5.5);
      expect(w.async.ownership, 2.5);
      expect(w.sync.flinchEmgPeakX, 6.4);
      expect(w.async.flinchEmgPeakX, 2.1);
      expect(w.sync.witnessQ4, 6.0);
      expect(w.async.witnessQ4, 6.0);
    });
  });

  group('the decided wire format', () {
    test('every key is read', () {
      final w = PhantomWitness.tryParse(_full())!;
      expect(w.conditionOrder, ['async', 'sync']);
      expect(w.sync.driftChangeCm, 2.0);
      expect(w.sync.flinchLatencyMs, 96.0);
      expect(w.sync.flinchStrength, PhantomFlinchStrength.strong);
      expect(w.sync.flinchEmgPeakX, 6.4);
      expect(w.sync.flinchWristPeakMps, 0.42);
      expect(w.sync.ownership, 5.5);
      expect(w.sync.control, 3.0);
      expect(w.sync.witnessQ4, 6.0);
      expect(w.sync.agencyQ5, 6.5);
      expect(w.async.flinchStrength, PhantomFlinchStrength.weak);
      expect(w.async.flinchWristPeakMps, isNull, reason: 'explicit null stays null');
      expect(w.async.agencyQ5, isNull, reason: 'q5 is optional');
      expect(w.closingEn, 'The body changed.\nYou noticed every change.');
      expect(w.closingHi, 'शरीर बदला।\nआपने हर बदलाव देखा।');
    });

    test('tryParseEvent unwraps data and ignores other event types', () {
      final event = {'t_ms': 117000, 'seq': 70, 'block': 0, 'trial': null, 'type': 'witness_summary', 'data': _full()};
      expect(PhantomWitness.tryParseEvent(event), isNotNull);
      expect(PhantomWitness.tryParseEvent({...event, 'type': 'threat_impact'}), isNull);
      expect(PhantomWitness.tryParseEvent({'type': 'witness_summary'}), isNull, reason: 'no data');
      expect(PhantomWitness.tryParseEvent(null), isNull);
      expect(PhantomWitness.tryParseEvent('witness_summary'), isNull);
    });
  });

  group('older spelling and missing keys', () {
    test('q4 is read when witness_q4 is absent; witness_q4 wins when both exist', () {
      final old = PhantomWitness.tryParse({
        'sync': {'q4': 5, 'ownership': 5},
        'async': {'q4': 3, 'witness_q4': 6},
      })!;
      expect(old.sync.witnessQ4, 5.0);
      expect(old.async.witnessQ4, 6.0);
    });

    test('the pre-8-Oct headset payload (q4, flinch_latency_ms, flinch_strength, closing_*) still parses', () {
      final w = PhantomWitness.tryParse({
        'sync': {
          'drift_change_cm': 1.5,
          'flinch_latency_ms': 120.0,
          'flinch_strength': 'strong',
          'flinch_emg_peak_x': 4.0,
          'flinch_wrist_peak_mps': null,
          'ownership': 6.0,
          'control': 3.0,
          'q4': 6.0,
        },
        'async': {'drift_change_cm': null, 'flinch_latency_ms': null, 'flinch_strength': 'none', 'ownership': 2.0, 'q4': 6.0},
        'sync_minus_async': <String, dynamic>{},
        'closing_en': 'x',
        'closing_hi': 'y',
      })!;
      expect(w.sync.witnessQ4, 6.0);
      expect(w.async.flinchStrength, PhantomFlinchStrength.none);
      expect(w.async.driftChangeCm, isNull);
      expect(w.async.flinchLatencyMs, isNull);
      expect(w.conditionOrder, isEmpty, reason: 'old payloads carry no condition_order');
    });

    test('empty condition objects give an all-null condition, not zeros', () {
      final w = PhantomWitness.tryParse({'sync': <String, dynamic>{}, 'async': <String, dynamic>{}})!;
      for (final c in [w.sync, w.async]) {
        expect(c.driftChangeCm, isNull);
        expect(c.flinchLatencyMs, isNull);
        expect(c.flinchStrength, isNull);
        expect(c.flinchEmgPeakX, isNull);
        expect(c.flinchWristPeakMps, isNull);
        expect(c.ownership, isNull);
        expect(c.control, isNull);
        expect(c.witnessQ4, isNull);
        expect(c.agencyQ5, isNull);
      }
    });

    test('one condition only: the other is all-null', () {
      final w = PhantomWitness.tryParse({
        'sync': {'ownership': 5.0},
      })!;
      expect(w.sync.ownership, 5.0);
      expect(w.async.ownership, isNull);
      expect(w.async.driftChangeCm, isNull);
    });

    test('a data map with neither sync nor async is not a witness summary', () {
      expect(PhantomWitness.tryParse(<String, dynamic>{}), isNull);
      expect(PhantomWitness.tryParse({'condition_order': ['sync']}), isNull);
      expect(PhantomWitness.tryParse({'sync': 'x', 'async': 3}), isNull);
      expect(PhantomWitness.tryParse(null), isNull);
      expect(PhantomWitness.tryParse('data'), isNull);
      expect(PhantomWitness.tryParse([1, 2]), isNull);
    });

    test('PhantomWitness.empty has no numbers at all', () {
      expect(PhantomWitness.empty.sync.ownership, isNull);
      expect(PhantomWitness.empty.async.driftChangeCm, isNull);
      expect(PhantomWitness.empty.conditionOrder, isEmpty);
    });
  });

  group('wrong types and impossible values become null', () {
    test('strings, bools, NaN and infinity are not numbers', () {
      final w = PhantomWitness.tryParse({
        'sync': {
          'drift_change_cm': '2.0',
          'flinch_latency_ms': true,
          'flinch_emg_peak_x': double.nan,
          'flinch_wrist_peak_mps': double.infinity,
          'ownership': <num>[5],
          'control': 'x',
        },
        'async': <String, dynamic>{},
      })!;
      expect(w.sync.driftChangeCm, isNull);
      expect(w.sync.flinchLatencyMs, isNull);
      expect(w.sync.flinchEmgPeakX, isNull);
      expect(w.sync.flinchWristPeakMps, isNull);
      expect(w.sync.ownership, isNull);
      expect(w.sync.control, isNull);
    });

    test('questionnaire means outside the contract scale 1..7 are dropped, not clamped (D2)', () {
      // A sender still on the raw -3..+3 scale would give 0 and negatives.
      final w = PhantomWitness.tryParse({
        'sync': {'ownership': 0, 'control': -2, 'witness_q4': 8, 'agency_q5': 7.5},
        'async': {'ownership': 1, 'control': 7, 'witness_q4': 4.5},
      })!;
      expect(w.sync.ownership, isNull);
      expect(w.sync.control, isNull);
      expect(w.sync.witnessQ4, isNull);
      expect(w.sync.agencyQ5, isNull);
      expect(w.async.ownership, 1.0, reason: '1 is on the scale');
      expect(w.async.control, 7.0, reason: '7 is on the scale');
      expect(w.async.witnessQ4, 4.5, reason: 'a mean may be fractional');
    });

    test('a negative flinch latency or peak is dropped; a negative drift is a real value', () {
      final w = PhantomWitness.tryParse({
        'sync': {'flinch_latency_ms': -38, 'flinch_emg_peak_x': -1, 'drift_change_cm': -1.4},
        'async': <String, dynamic>{},
      })!;
      expect(w.sync.flinchLatencyMs, isNull);
      expect(w.sync.flinchEmgPeakX, isNull);
      expect(w.sync.driftChangeCm, -1.4);
    });

    test('flinch_strength: the three words (any case), anything else is null', () {
      expect(PhantomFlinchStrength.parse('strong'), PhantomFlinchStrength.strong);
      expect(PhantomFlinchStrength.parse('Weak'), PhantomFlinchStrength.weak);
      expect(PhantomFlinchStrength.parse(' none '), PhantomFlinchStrength.none);
      expect(PhantomFlinchStrength.parse('huge'), isNull);
      expect(PhantomFlinchStrength.parse(1), isNull);
      expect(PhantomFlinchStrength.parse(null), isNull);
    });

    test('condition_order keeps only sync / async; closing lines are trimmed, blank = null', () {
      final w = PhantomWitness.tryParse({
        'condition_order': ['async', 'x', 3, 'sync'],
        'sync': <String, dynamic>{},
        'closing_en': '  line one\nline two \n',
        'closing_hi': '   ',
      })!;
      expect(w.conditionOrder, ['async', 'sync']);
      expect(w.closingEn, 'line one\nline two');
      expect(w.closingHi, isNull);
      final bad = PhantomWitness.tryParse({'condition_order': 'async', 'sync': <String, dynamic>{}, 'closing_en': 5})!;
      expect(bad.conditionOrder, isEmpty);
      expect(bad.closingEn, isNull);
    });
  });

  group('-3..+3 as the headset shows it (wire value minus 4, X1)', () {
    test('conversion', () {
      expect(phantomSignedAnswer(1), -3);
      expect(phantomSignedAnswer(4), 0);
      expect(phantomSignedAnswer(7), 3);
      expect(phantomSignedAnswer(5.5), 1.5);
      expect(phantomSignedAnswer(2.5), -1.5);
      expect(phantomSignedAnswer(null), isNull);
    });
  });
}
