import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/shared/metrics/haptic_analysis.dart';

SessionEvent _event(Map<String, dynamic> raw) => SessionEvent({
      't_ms': 0.0,
      'seq': 0,
      'block': 0,
      'trial': null,
      'type': 'session_start',
      ...raw,
    });

void main() {
  group('summarizeHapticCues', () {
    test('returns HapticCueSummary.empty for a session with no events', () {
      expect(summarizeHapticCues(const []), same(HapticCueSummary.empty));
    });

    test('returns HapticCueSummary.empty for a session with no haptic_cue events', () {
      final events = [
        _event({'t_ms': 0.0, 'type': 'session_start'}),
        _event({'t_ms': 5000.0, 'type': 'session_end'}),
      ];
      final summary = summarizeHapticCues(events);
      expect(summary.isEmpty, isTrue);
      expect(summary.totalCount, 0);
    });

    test('counts cues by type, delivered vs logged-only, and cues per minute', () {
      final events = [
        _event({'t_ms': 0.0, 'type': 'session_start'}),
        _event({
          't_ms': 1000.0,
          'trial': 0,
          'type': 'haptic_cue',
          'data': {'cue': 'trunk_lean', 'intensity': 0.6, 'delivered': true},
        }),
        _event({
          't_ms': 2000.0,
          'trial': 0,
          'type': 'haptic_cue',
          'data': {'cue': 'success', 'intensity': 0.6, 'delivered': true},
        }),
        _event({
          't_ms': 3000.0,
          'trial': 1,
          'type': 'haptic_cue',
          'data': {'cue': 'trunk_lean', 'intensity': 0.7, 'delivered': false},
        }),
        _event({'t_ms': 60000.0, 'type': 'session_end'}),
      ];
      final summary = summarizeHapticCues(events);

      expect(summary.isEmpty, isFalse);
      expect(summary.totalCount, 3);
      expect(summary.countsByType, {'trunk_lean': 2, 'success': 1});
      expect(summary.deliveredCount, 2);
      expect(summary.loggedOnlyCount, 1);
      expect(summary.hadDeliveredCues, isTrue);
      expect(summary.hadUndeliveredCues, isTrue);
      // 3 cues over exactly 1 minute (0ms -> 60000ms).
      expect(summary.cuesPerMinute, 3.0);
      expect(summary.trunkLeanTrials, {0, 1});
      expect(summary.byTrial[0]!.map((o) => o.cue), ['trunk_lean', 'success']);
      expect(summary.byTrial[1]!.single.delivered, isFalse);
    });

    test('a session where every cue was only logged (no sleeve) reports that honestly', () {
      final events = [
        _event({'t_ms': 0.0, 'type': 'session_start'}),
        _event({
          't_ms': 1000.0,
          'trial': 0,
          'type': 'haptic_cue',
          'data': {'cue': 'low_confidence', 'intensity': 0.4, 'delivered': false},
        }),
        _event({'t_ms': 10000.0, 'type': 'session_end'}),
      ];
      final summary = summarizeHapticCues(events);
      expect(summary.hadDeliveredCues, isFalse);
      expect(summary.hadUndeliveredCues, isTrue);
    });
  });

  group('hapticCueLabel', () {
    test('maps every v1 cue id to plain language', () {
      expect(hapticCueLabel('trunk_lean'), 'Trunk lean');
      expect(hapticCueLabel('low_confidence'), 'Hand out of view');
      expect(hapticCueLabel('success'), 'Success');
    });

    test('falls back to a readable form for an unrecognized id', () {
      expect(hapticCueLabel('future_cue'), 'future cue');
    });
  });

  test('the healthy fixture actually parses real haptic_cue events end to end', () async {
    // Regression guard for the hand-written cues added to
    // assets/fixtures/sessions/healthy__events.ndjson -- if that file's
    // shape ever drifts from event.schema.json's haptic_cue contract, this
    // fails loudly instead of the report silently showing "no cues".
    final raw = await _loadFixture('assets/fixtures/sessions/healthy__events.ndjson');
    final events = raw
        .split('\n')
        .map((l) => l.trim())
        .where((l) => l.isNotEmpty)
        .map((l) => SessionEvent(_decode(l)))
        .toList();
    final summary = summarizeHapticCues(events);
    expect(summary.isEmpty, isFalse);
    expect(summary.countsByType['trunk_lean'], greaterThan(0));
    expect(summary.hadDeliveredCues, isTrue);
    expect(summary.hadUndeliveredCues, isTrue);
  });
}

Future<String> _loadFixture(String path) => File(path).readAsString();

Map<String, dynamic> _decode(String line) => jsonDecode(line) as Map<String, dynamic>;
