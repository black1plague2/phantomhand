// B19: the parser of the `embodiment` block of a Phantom Hand metrics.json
// (lib/data/models/embodiment.dart), against the two real fixtures
// contracts/fixtures/sessions/phantom_hand_min/metrics.json and
// tools/demo/tests/fixtures/ph_l3_main/metrics.json.
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/embodiment.dart';
import 'package:path/path.dart' as p;

String _repo(String rel) => p.normalize(p.join(Directory.current.path, '..', rel));

Object? _json(String rel) => jsonDecode(File(_repo(rel)).readAsStringSync());

Embodiment _fixture(String rel) => Embodiment.tryParseMetrics(_json(rel))!;

const _min = 'contracts/fixtures/sessions/phantom_hand_min/metrics.json';
const _main = 'tools/demo/tests/fixtures/ph_l3_main/metrics.json';

void main() {
  test('the headset\'s own end-of-run summary is a report: plain numbers per condition, the EMG onset under its report name', () {
    final e = Embodiment.tryParseWitness({
      'condition_order': ['sync', 'async'],
      'sync': {'drift_change_cm': -1.2, 'flinch_latency_ms': 80.0, 'flinch_strength': 'strong', 'flinch_emg_peak_x': 5.5, 'ownership': 5.5},
      'async': {'drift_change_cm': 3.0, 'flinch_latency_ms': 108.0, 'flinch_emg_peak_x': 6.1, 'ownership': 4.5},
      'sync_minus_async': {'drift_change_cm': -4.2, 'ownership': 1.0},
    })!;
    expect(e.conditionOrder, ['sync', 'async']);
    expect(e.sync['drift_change_cm']!.value, -1.2);
    expect(e.sync['drift_change_cm']!.unit, 'cm');
    expect(e.sync['drift_change_cm']!.quality, EmbodimentQuality.ok);
    expect(e.sync['flinch_emg_latency_ms']!.value, 80.0);
    expect(e.sync.containsKey('flinch_strength'), isFalse, reason: 'a word is not a number');
    expect(e.async['flinch_emg_peak_x']!.value, 6.1);
    expect(e.difference['ownership']!.value, 1.0);
    expect(Embodiment.tryParseWitness({'closing_en': 'x'}), isNull);
    expect(Embodiment.tryParseWitness(null), isNull);
  });

  group('phantom_hand_min metrics.json', () {
    test('both conditions, the contrast and the order', () {
      final e = _fixture(_min);
      expect(e.conditionOrder, ['async', 'sync']);
      expect(e.sync['drift_change_cm']!.value, 2.4);
      expect(e.sync['drift_change_cm']!.unit, 'cm');
      expect(e.sync['drift_change_cm']!.quality, EmbodimentQuality.ok);
      expect(e.async['drift_change_cm']!.value, 0.4);
      expect(e.sync['ownership']!.value, 5.5);
      expect(e.sync['ownership']!.unit, 'likert_1_7');
      expect(e.async['ownership']!.value, 2.5);
      expect(e.sync['flinch_emg_peak_x']!.value, 6.4);
      expect(e.async['flinch_emg_peak_x']!.value, 2.1);
      expect(e.sync['flinch_emg_latency_ms']!.value, 96);
      expect(e.async['flinch_emg_latency_ms']!.value, 140);
      expect(e.sync['witness_q4']!.value, 6);
      expect(e.difference['ownership']!.value, 3.0);
      expect(e.difference['drift_change_cm']!.value, 2.0);
      expect(e.difference['witness_q4']!.value, 0);
    });

    test('a missing value keeps its quality and reasons, and has no number', () {
      final v = _fixture(_min).async['flinch_wrist_latency_ms']!;
      expect(v.value, isNull);
      expect(v.quality, EmbodimentQuality.missing);
      expect(v.reasons, ['no_withdrawal_detected']);
    });

    test('pair metrics ({mean, p95}) are not leaves and are skipped', () {
      final e = _fixture(_min);
      expect(e.sync.containsKey('stroke_timing_err_ms'), isFalse);
      expect(e.sync['cue_delivery_rate']!.value, 1.0);
      expect(e.sync['emg_windows_excluded']!.value, 2);
    });
  });

  group('tools/demo ph_l3_main metrics.json (real pipeline output)', () {
    test('values, reasons on ok values, imu latency, missing wrist values', () {
      final e = _fixture(_main);
      expect(e.conditionOrder, ['async', 'sync']);
      expect(e.sync['drift_change_cm']!.value, 2.0);
      expect(e.sync['drift_change_cm']!.quality, EmbodimentQuality.ok);
      expect(e.sync['drift_change_cm']!.reasons, ['shared_pre_probe']);
      expect(e.async['drift_change_cm']!.value, 0.0, reason: 'a real zero stays a zero');
      expect(e.sync['flinch_emg_peak_x']!.value, closeTo(6.019, 0.001));
      expect(e.async['flinch_emg_peak_x']!.value, closeTo(6.040, 0.001));
      expect(e.sync['flinch_imu_latency_ms']!.value, closeTo(147.24, 0.01));
      expect(e.sync['flinch_wrist_peak_mps']!.value, isNull);
      expect(e.sync['flinch_wrist_peak_mps']!.quality, EmbodimentQuality.missing);
      expect(e.sync['flinch_wrist_peak_mps']!.reasons, ['no_samples_in_window']);
      expect(e.difference['flinch_emg_peak_x']!.value, closeTo(-0.0209, 0.0001));
      expect(e.difference['flinch_wrist_peak_mps']!.reasons, ['sync_value_missing', 'async_value_missing']);
    });

    test('async_delay_ms and stroke_timing_err_ms are pairs: skipped', () {
      final e = _fixture(_main);
      expect(e.sync.containsKey('async_delay_ms'), isFalse);
      expect(e.async.containsKey('stroke_timing_err_ms'), isFalse);
    });
  });

  group('tolerant parsing', () {
    test('not an embodiment: null', () {
      expect(Embodiment.tryParse(null), isNull);
      expect(Embodiment.tryParse('x'), isNull);
      expect(Embodiment.tryParse(<String, dynamic>{}), isNull);
      expect(Embodiment.tryParse({'condition_order': ['sync']}), isNull);
      expect(Embodiment.tryParse({'sync': 1, 'async': 'x'}), isNull);
      expect(Embodiment.tryParseMetrics(null), isNull);
      expect(Embodiment.tryParseMetrics({'trials': <Object?>[]}), isNull, reason: 'an Orchard Reach metrics.json');
      expect(Embodiment.tryParseMetrics({'embodiment': 'x'}), isNull);
      expect(Embodiment.tryParseMetrics([1]), isNull);
    });

    test('one condition only: the other has no metrics', () {
      final e = Embodiment.tryParse({
        'sync': {
          'ownership': {'value': 5, 'quality': 'ok'},
        },
      })!;
      expect(e.sync['ownership']!.value, 5);
      expect(e.async, isEmpty);
      expect(e.difference, isEmpty);
      expect(e.conditionOrder, isEmpty);
    });

    test('absent or unknown quality: a number is flagged degraded, no number is missing', () {
      final e = Embodiment.tryParse({
        'sync': {
          'a': {'value': 1.0},
          'b': {'value': 1.0, 'quality': 'weird'},
          'c': {'value': null},
          'd': {'quality': 'ok'},
          'e': {'value': 2.0, 'quality': 'invalid', 'quality_reasons': ['x']},
        },
        'async': <String, dynamic>{},
      })!;
      expect(e.sync['a']!.quality, EmbodimentQuality.degraded);
      expect(e.sync['b']!.quality, EmbodimentQuality.degraded);
      expect(e.sync['c']!.quality, EmbodimentQuality.missing);
      expect(e.sync['d']!.quality, EmbodimentQuality.ok, reason: 'kept as sent; the report treats a number-less ok as missing');
      expect(e.sync['d']!.value, isNull);
      expect(e.sync['e']!.quality, EmbodimentQuality.invalid);
    });

    test('wrong types: no number, no crash; non-string reasons dropped; non-map entries skipped', () {
      final e = Embodiment.tryParse({
        'sync': {
          'a': {'value': '2.0', 'quality': 'ok'},
          'b': {'value': double.nan, 'quality': 'ok'},
          'c': {'value': double.infinity, 'quality': 'ok'},
          'd': {'value': 1, 'quality': 'degraded', 'quality_reasons': ['touch_incomplete', 3, null]},
          'e': 5,
          'f': null,
          'g': {'mean': {'value': 1}, 'p95': {'value': 2}},
        },
        'async': <String, dynamic>{},
        'condition_order': ['async', 'x', 3, 'sync'],
      })!;
      expect(e.sync['a']!.value, isNull);
      expect(e.sync['b']!.value, isNull);
      expect(e.sync['c']!.value, isNull);
      expect(e.sync['d']!.reasons, ['touch_incomplete']);
      expect(e.sync.keys, unorderedEquals(['a', 'b', 'c', 'd']));
      expect(e.conditionOrder, ['async', 'sync']);
    });
  });
}
