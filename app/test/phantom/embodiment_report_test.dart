// B19: the embodiment report (lib/features/sessions/embodiment_report.dart),
// against the two required fixtures
//   contracts/fixtures/sessions/phantom_hand_min/metrics.json
//   tools/demo/tests/fixtures/ph_l3_main/metrics.json
// plus the three L3 fault fixtures next to the second one.
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/embodiment.dart';
import 'package:opus_app/features/sessions/embodiment_report.dart';

import 'witness_test_support.dart';

const _min = 'contracts/fixtures/sessions/phantom_hand_min/metrics.json';
const _main = 'tools/demo/tests/fixtures/ph_l3_main/metrics.json';
const _nodeA = 'tools/demo/tests/fixtures/ph_l3_fault_node_a_off/metrics.json';
const _nodeB = 'tools/demo/tests/fixtures/ph_l3_fault_node_b_absent/metrics.json';
const _hub = 'tools/demo/tests/fixtures/ph_l3_fault_hub_absent/metrics.json';

Embodiment _load(String rel) => Embodiment.tryParseMetrics(jsonDecode(File(repoPath(rel)).readAsStringSync()))!;

/// A leaf metric as `metrics.json` writes it.
Map<String, dynamic> _v(num? value, {String quality = 'ok', List<String>? reasons, String unit = 'x'}) => {
      'value': value,
      'unit': unit,
      'quality': quality,
      'quality_reasons': ?reasons,
    };

Embodiment _make({Map<String, dynamic> sync = const {}, Map<String, dynamic> async = const {}}) =>
    Embodiment.tryParse({'condition_order': ['async', 'sync'], 'sync': sync, 'async': async})!;

const _phone = Size(390, 844);
const _laptop = Size(1280, 800);

Future<void> _pump(
  WidgetTester tester,
  Embodiment e, {
  String lang = 'en',
  Size size = _phone,
  double textScale = 1,
}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    witnessApp(
      ListView(padding: const EdgeInsets.all(16), children: [EmbodimentReport(embodiment: e, lang: lang)]),
      textScale: textScale,
    ),
  );
  await tester.pump();
}

List<String> _texts(WidgetTester tester) => [
      for (final t in tester.widgetList<Text>(find.byType(Text))) t.data ?? t.textSpan?.toPlainText() ?? '',
    ];

String _verdictOn(WidgetTester tester) => tester.widget<Text>(find.byKey(const ValueKey('ph-embodiment-verdict'))).data!;

Finder _delta(EmbodimentRowId id) => find.byKey(ValueKey('ph-embodiment-${id.name}-delta'));

Finder _textIn(Finder where, String text) => find.descendant(of: where, matching: find.text(text));

final _forbidden = RegExp(
  r'significan|proves?\b|proven|(creat|measur|prov|simulat|manufactur)\w*\W+(\w+\W+){0,3}consciousness|did not change|नहीं बदला',
  caseSensitive: false,
);

void main() {
  setUpAll(loadWitnessTestFonts);

  group('rows', () {
    test('phantom_hand_min: the five rows, each on its preferred metric, all trustworthy', () {
      final rows = embodimentRows(_load(_min));
      expect([for (final r in rows) r.id], EmbodimentRowId.values);
      expect([for (final r in rows) r.sourceKey], ['drift_change_cm', 'ownership', 'flinch_emg_peak_x', 'flinch_emg_latency_ms', 'witness_q4']);
      expect(rows.every((r) => r.comparable && !r.usedFallback && !r.partial), isTrue);
      expect(rows[3].delta, closeTo(-44, 1e-9), reason: 'speed: 96 ms vs 140 ms');
    });

    test('what counts as a gap: R3 thresholds, q4 never counts', () {
      final rows = embodimentRows(_load(_min));
      expect([for (final r in rows) r.counts], [true, true, true, true, false]);
      expect([for (final r in rows) r.gapIsBig], [true, true, true, true, false], reason: 'q4 6 vs 6');
      expect([for (final r in rows) r.syncWins].take(4), everyElement(isTrue), reason: 'in sync: more drift, higher, larger, quicker');
      final main = embodimentRows(_load(_main));
      expect([for (final r in main) r.counts], [true, true, false, false, false], reason: 'flinch size 6.02x vs 6.04x: no clear difference');
    });

    test('the thresholds are ">=" and in one place', () {
      EmbodimentRow row(double s, double a, {String key = 'ownership'}) =>
          embodimentRows(_make(sync: {key: _v(s)}, async: {key: _v(a)}))[key == 'ownership' ? 1 : 0];
      expect(row(4.99, 4).counts, isFalse, reason: '0.99 points');
      expect(row(5, 4).counts, isTrue, reason: 'exactly 1 point');
      expect(row(0.5, 0, key: 'drift_change_cm').counts, isTrue, reason: 'exactly 0.5 cm');
      expect(row(0.49, 0, key: 'drift_change_cm').counts, isFalse);
      final size = embodimentRows(_make(sync: {'flinch_emg_peak_x': _v(7.5)}, async: {'flinch_emg_peak_x': _v(10)}))[2];
      expect(size.counts, isTrue, reason: '25 % of the larger value');
      expect(embodimentRows(_make(sync: {'flinch_emg_peak_x': _v(7.6)}, async: {'flinch_emg_peak_x': _v(10)}))[2].counts, isFalse);
      expect(embodimentDriftGapCm, 0.5);
      expect(embodimentAnswerGap, 1.0);
      expect(embodimentRelativeGap, 0.25);
    });

    test('muscle sensor off: the arm IMU stands in for both flinch rows', () {
      final rows = embodimentRows(_load(_nodeB));
      expect(rows[2].sourceKey, 'flinch_imu_peak');
      expect(rows[3].sourceKey, 'flinch_imu_latency_ms');
      expect(rows[2].usedFallback && rows[3].usedFallback, isTrue);
      expect(rows[2].hasNumbers && rows[3].hasNumbers, isTrue);
      expect(rows[0].usedFallback, isFalse);
    });

    test('sleeve missed touches (node A off): the in-sync numbers are Partial', () {
      final rows = embodimentRows(_load(_nodeA));
      expect(rows[0].sync.state, EmbodimentCellState.partial);
      expect(rows[0].async.state, EmbodimentCellState.ok);
      expect(rows[0].sync.reasons, contains('touch_incomplete'));
      expect(rows[0].partial && rows[1].partial && rows[2].partial && rows[3].partial, isTrue);
      expect(rows.any((r) => r.counts), isFalse, reason: 'partial numbers never count');
    });

    test('a questionnaire mean off the 1..7 scale is not usable (D2)', () {
      final rows = embodimentRows(_make(sync: {'ownership': _v(-2, unit: 'likert_1_7')}, async: {'ownership': _v(2.5, unit: 'likert_1_7')}));
      expect(rows[1].sync.state, EmbodimentCellState.invalid);
      expect(rows[1].sync.value, isNull);
      expect(rows[1].sync.reasons, contains('off_scale'));
      expect(rows[1].counts, isFalse);
    });

    test('a number-less "ok" is missing, never 0', () {
      final rows = embodimentRows(_make(sync: {'drift_change_cm': _v(null)}, async: {'drift_change_cm': _v(1)}));
      expect(rows[0].sync.state, EmbodimentCellState.missing);
      expect(rows[0].sync.value, isNull);
      expect(rows[0].delta, isNull);
    });
  });

  group('verdict', () {
    test('phantom_hand_min: all four primary rows favour In sync', () {
      final v = embodimentVerdict(embodimentRows(_load(_min)));
      expect(v.kind, EmbodimentVerdictKind.inSync);
      expect(
        v.text,
        'In sync, the hand felt more like yours (5.5 vs 2.5), '
        'your sense of where your hand was moved 2.0 cm further toward the virtual hand, '
        'the flinch was larger (6.4× vs 2.1×) and the flinch came sooner (96 vs 140 ms).',
      );
    });

    test('ph_l3_main: equal flinch is left out, the verdict is the R3 sentence', () {
      final v = embodimentVerdict(embodimentRows(_load(_main)));
      expect(v.kind, EmbodimentVerdictKind.inSync);
      expect(
        v.text,
        'In sync, the hand felt more like yours (5.5 vs 2.5) and your sense of where your hand was moved 2.0 cm further toward the virtual hand.',
      );
    });

    test('hub absent (no wrist values): same verdict as the main run', () {
      final v = embodimentVerdict(embodimentRows(_load(_hub)));
      expect(v.kind, EmbodimentVerdictKind.inSync);
    });

    test('"no clear difference" is a normal outcome', () {
      final same = {'ownership': _v(4), 'drift_change_cm': _v(1), 'flinch_emg_peak_x': _v(5), 'flinch_emg_latency_ms': _v(100)};
      final v = embodimentVerdict(embodimentRows(_make(sync: same, async: same)));
      expect(v.kind, EmbodimentVerdictKind.none);
      expect(v.text, 'No clear difference between in sync and delayed this time.');
    });

    test('mixed: In sync wins one metric, Delayed another', () {
      final v = embodimentVerdict(
        embodimentRows(
          _make(
            sync: {'ownership': _v(5.5), 'drift_change_cm': _v(1), 'flinch_emg_peak_x': _v(2)},
            async: {'ownership': _v(2.5), 'drift_change_cm': _v(1), 'flinch_emg_peak_x': _v(6)},
          ),
        ),
      );
      expect(v.kind, EmbodimentVerdictKind.mixed);
      expect(v.text, 'Mixed: in sync, the hand felt more like yours (5.5 vs 2.5); when delayed, the flinch was larger (6.0× vs 2.0×).');
    });

    test('only Delayed wins: still "Mixed", never a claim for In sync', () {
      final v = embodimentVerdict(embodimentRows(_make(sync: {'ownership': _v(2)}, async: {'ownership': _v(5)})));
      expect(v.kind, EmbodimentVerdictKind.mixed);
      expect(v.text, 'Mixed: when delayed, the hand felt more like yours (5.0 vs 2.0).');
    });

    test('nothing trustworthy to compare: no verdict, said plainly (node A off)', () {
      final v = embodimentVerdict(embodimentRows(_load(_nodeA)));
      expect(v.kind, EmbodimentVerdictKind.unreliable);
      expect(v.text, 'Not enough reliable numbers for a verdict this time.');
    });

    test('no difference but some numbers partial: says so', () {
      final v = embodimentVerdict(
        embodimentRows(
          _make(
            sync: {'ownership': _v(4), 'drift_change_cm': _v(1, quality: 'degraded', reasons: ['touch_incomplete'])},
            async: {'ownership': _v(4), 'drift_change_cm': _v(1)},
          ),
        ),
      );
      expect(v.kind, EmbodimentVerdictKind.none);
      expect(v.text, endsWith('this time. Some numbers are partial.'));
    });

    test('IMU numbers can carry the verdict when the muscle sensor was off', () {
      final v = embodimentVerdict(
        embodimentRows(
          _make(
            sync: {'flinch_emg_peak_x': _v(null, quality: 'missing', reasons: ['node_absent_bio']), 'flinch_imu_peak': _v(3.1)},
            async: {'flinch_emg_peak_x': _v(null, quality: 'missing', reasons: ['node_absent_bio']), 'flinch_imu_peak': _v(1.2)},
          ),
        ),
      );
      expect(v.kind, EmbodimentVerdictKind.inSync);
      expect(v.text, 'In sync, the flinch was larger (3.1 vs 1.2 m/s²).');
    });

    test('Hindi verdicts', () {
      final hi = embodimentVerdict(embodimentRows(_load(_main)), lang: 'hi');
      expect(hi.text, startsWith('साथ-साथ में, '));
      expect(hi.text, contains('5.5 बनाम 2.5'));
      expect(embodimentVerdict(embodimentRows(_load(_nodeA)), lang: 'hi').kind, EmbodimentVerdictKind.unreliable);
    });
  });

  group('the report on screen', () {
    testWidgets('phantom_hand_min: verdict, key, five rows, delta chips, values, q4 pointer', (tester) async {
      await _pump(tester, _load(_min));
      expect(tester.takeException(), isNull);
      expect(find.text('Your result'), findsOneWidget);
      expect(_verdictOn(tester), startsWith('In sync, the hand felt more like yours (5.5 vs 2.5)'));
      for (final name in [
        'Where your hand felt to be',
        'It felt like my hand',
        'Flinch after the stone, size',
        'Flinch after the stone, speed',
        'The awareness that noticed was the same',
      ]) {
        expect(find.text(name), findsOneWidget);
      }
      expect(_textIn(_delta(EmbodimentRowId.drift), '+2.0 cm'), findsOneWidget);
      expect(_textIn(_delta(EmbodimentRowId.ownership), '+3.0'), findsOneWidget);
      expect(_textIn(_delta(EmbodimentRowId.flinchSize), '+4.3×'), findsOneWidget);
      expect(_textIn(_delta(EmbodimentRowId.flinchSpeed), '-40 ms'), findsOneWidget, reason: 'In sync minus Delayed, ms to 10');
      expect(_textIn(_delta(EmbodimentRowId.awareness), 'about the same'), findsOneWidget);
      expect(find.textContaining('5.5'), findsWidgets);
      expect(find.text('A pointer, not proof'), findsOneWidget);
      expect(find.text('Partial'), findsNothing);
      expect(find.text('No data'), findsNothing);
    });

    testWidgets('ph_l3_main: equal flinch size reads "about the same", not an error', (tester) async {
      await _pump(tester, _load(_main));
      expect(_textIn(_delta(EmbodimentRowId.flinchSize), 'about the same'), findsOneWidget);
      expect(_textIn(_delta(EmbodimentRowId.flinchSpeed), 'about the same'), findsOneWidget);
      expect(_textIn(_delta(EmbodimentRowId.drift), '+2.0 cm'), findsOneWidget);
    });

    testWidgets('Partial numbers: flagged, "~" on the delta, reasons on tap', (tester) async {
      await _pump(tester, _load(_nodeA));
      expect(find.text('Partial'), findsWidgets);
      expect(_textIn(_delta(EmbodimentRowId.drift), '~+2.0 cm'), findsOneWidget);
      expect(find.text('Not enough reliable numbers for a verdict this time.'), findsOneWidget);
      final reasons = find.byKey(const ValueKey('ph-embodiment-drift-reasons'));
      expect(reasons, findsNothing);
      await tester.tap(find.byKey(const ValueKey('ph-embodiment-drift-partial')));
      await tester.pump();
      expect(reasons, findsOneWidget);
      expect(
        _textIn(reasons, 'In sync: Same starting point used for both, Some touches were not felt'),
        findsOneWidget,
      );
      await tester.tap(find.byKey(const ValueKey('ph-embodiment-drift-partial')));
      await tester.pump();
      expect(reasons, findsNothing);
    });

    testWidgets('muscle sensor off: the IMU stands in and the row says so', (tester) async {
      await _pump(tester, _load(_nodeB));
      expect(find.text('Shown instead: arm jolt'), findsOneWidget);
      expect(find.text('Shown instead: arm jolt timing'), findsOneWidget);
      expect(find.text('No data'), findsNothing);
    });

    testWidgets('no number on either side: "No data" twice and the reason, without a tap', (tester) async {
      final off = _v(null, quality: 'missing', reasons: ['node_absent_bio']);
      await _pump(
        tester,
        _make(
          sync: {'flinch_emg_peak_x': off, 'ownership': _v(5)},
          async: {'flinch_emg_peak_x': off, 'ownership': _v(2)},
        ),
      );
      final row = find.byKey(const ValueKey('ph-embodiment-flinchSize'));
      expect(find.descendant(of: row, matching: find.textContaining('No data')), findsNWidgets(2));
      expect(_textIn(find.byKey(const ValueKey('ph-embodiment-flinchSize-missing')), 'In sync: Muscle sensor was off\nDelayed: Muscle sensor was off'), findsOneWidget);
      expect(find.descendant(of: row, matching: find.byType(CustomPaint)), findsNothing, reason: 'no dumbbell without a number');
    });

    testWidgets('one side missing: the other stays, the missing side says why', (tester) async {
      await _pump(
        tester,
        _make(
          sync: {'flinch_emg_latency_ms': _v(96, unit: 'ms')},
          async: {'flinch_emg_latency_ms': _v(null, quality: 'missing', reasons: ['no_withdrawal_detected'])},
        ),
      );
      final row = find.byKey(const ValueKey('ph-embodiment-flinchSpeed'));
      expect(find.descendant(of: row, matching: find.textContaining('96 ms')), findsOneWidget);
      expect(find.descendant(of: row, matching: find.textContaining('No data')), findsOneWidget);
      expect(_delta(EmbodimentRowId.flinchSpeed), findsNothing, reason: 'no delta without both numbers');
      expect(_textIn(find.byKey(const ValueKey('ph-embodiment-flinchSpeed-missing')), 'Delayed: No hand movement seen'), findsOneWidget);
    });

    testWidgets('demo mode: a question that was not asked in a round says so, not "not answered" (03-SPEC D18)', (tester) async {
      await _pump(
        tester,
        _make(
          sync: {'ownership': _v(6, unit: 'likert_1_7', reasons: ['single_item_demo_mode']), 'witness_q4': _v(4, unit: 'likert_1_7')},
          async: {
            'ownership': _v(3, unit: 'likert_1_7', reasons: ['single_item_demo_mode']),
            'witness_q4': _v(null, quality: 'missing', reasons: ['q4_not_asked']),
          },
        ),
      );
      expect(_textIn(find.byKey(const ValueKey('ph-embodiment-awareness-missing')), 'Delayed: Not asked in this round'), findsOneWidget);
      // one planned item is a full number: no "Partial", and it carries the verdict
      final ownership = find.byKey(const ValueKey('ph-embodiment-ownership'));
      expect(find.descendant(of: ownership, matching: find.textContaining('Partial')), findsNothing);
      expect(_verdictOn(tester), contains('the hand felt more like yours (6.0 vs 3.0)'));
    });

    testWidgets('an off-scale answer says "Not usable"; an unknown reason code is shown as it is', (tester) async {
      await _pump(
        tester,
        _make(
          sync: {'ownership': _v(-2, unit: 'likert_1_7'), 'witness_q4': _v(null, quality: 'missing', reasons: ['weird_new_reason'])},
          async: {'ownership': _v(2.5, unit: 'likert_1_7'), 'witness_q4': _v(6)},
        ),
      );
      expect(find.descendant(of: find.byKey(const ValueKey('ph-embodiment-ownership')), matching: find.textContaining('Not usable')), findsOneWidget);
      expect(_textIn(find.byKey(const ValueKey('ph-embodiment-awareness-missing')), 'In sync: weird_new_reason'), findsOneWidget);
    });

    testWidgets('Hindi', (tester) async {
      await _pump(tester, _load(_nodeA), lang: 'hi');
      expect(find.text('आपका परिणाम'), findsOneWidget);
      expect(find.text('आपका हाथ कहाँ महसूस हुआ'), findsOneWidget);
      expect(find.text('यह मेरा हाथ लगा'), findsOneWidget);
      expect(find.text('एक संकेत, प्रमाण नहीं'), findsOneWidget);
      expect(find.text('आंशिक'), findsWidgets);
      expect(find.textContaining('साथ-साथ'), findsWidgets);
      await _pump(tester, _load(_min), lang: 'hi');
      expect(_verdictOn(tester), startsWith('साथ-साथ में, '));
    });

    testWidgets('no overflow: phones (also 2x text), laptop; every fixture; both languages', (tester) async {
      for (final rel in [_min, _main, _nodeA, _nodeB, _hub]) {
        for (final lang in ['en', 'hi']) {
          for (final (size, scale) in [(const Size(360, 800), 1.0), (const Size(360, 800), 2.0), (_phone, 1.0), (_laptop, 1.0)]) {
            await _pump(tester, _load(rel), lang: lang, size: size, textScale: scale);
            expect(tester.takeException(), isNull, reason: '$rel $lang $size x$scale');
          }
        }
      }
    });

    testWidgets('wording: no "significant", no "proves", nothing about creating or measuring consciousness', (tester) async {
      for (final rel in [_min, _main, _nodeA, _nodeB, _hub]) {
        for (final lang in ['en', 'hi']) {
          await _pump(tester, _load(rel), lang: lang);
          expect(_texts(tester).where(_forbidden.hasMatch), isEmpty, reason: '$rel $lang');
        }
      }
      expect(_forbidden.hasMatch('A significant difference'), isTrue);
      expect(_forbidden.hasMatch('This proves it'), isTrue);
      expect(_forbidden.hasMatch('A pointer, not proof'), isFalse);
    });
  });
}
