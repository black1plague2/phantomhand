// B11: the audience witness mirror (lib/features/live/phantom_witness_mirror.dart).
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:opus_app/features/live/phantom_witness_mirror.dart';

import 'witness_test_support.dart';

const _phone = Size(390, 844);
const _phone360 = Size(360, 800);
const _laptop = Size(1280, 800);

void _view(WidgetTester tester, Size size) {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

Future<void> _pump(
  WidgetTester tester,
  PhantomWitness? summary, {
  String lang = 'en',
  Size size = _laptop,
  Brightness brightness = Brightness.dark,
  double textScale = 1,
}) async {
  _view(tester, size);
  await tester.pumpWidget(
    witnessApp(PhantomWitnessMirror(summary: summary, lang: lang), brightness: brightness, textScale: textScale),
  );
  await tester.pump();
}

List<String> _texts(WidgetTester tester) => [
      for (final t in tester.widgetList<Text>(find.byType(Text))) t.data ?? t.textSpan?.toPlainText() ?? '',
    ];

Finder _in(String card, String row) => find.byKey(ValueKey('ph-witness-$card-$row'));

Finder _textIn(Finder where, String text) => find.descendant(of: where, matching: find.text(text));

double _closingOpacity(WidgetTester tester) =>
    tester.widget<FadeTransition>(find.byKey(const ValueKey('ph-witness-closing'))).opacity.value;

/// Texts the mirror must never show (spec D11): creating / measuring / proving
/// consciousness, or "the witness did not change".
final _forbidden = RegExp(
  r"(creat|measur|prov|simulat|manufactur)\w*\W+(\w+\W+){0,3}consciousness|did not change|didn't change|नहीं बदला",
  caseSensitive: false,
);

PhantomWitness _witness({
  Map<String, dynamic> sync = const {},
  Map<String, dynamic> async = const {},
  String? closingEn,
  String? closingHi,
}) =>
    PhantomWitness.tryParse({'sync': sync, 'async': async, 'closing_en': ?closingEn, 'closing_hi': ?closingHi})!;

void main() {
  setUpAll(loadWitnessTestFonts);

  group('content (English)', () {
    testWidgets('title, chip, two cards, three groups each, pointer caption', (tester) async {
      await _pump(tester, fullWitness());
      expect(tester.takeException(), isNull);
      expect(find.text('What changed?'), findsOneWidget);
      expect(find.text('Preliminary'), findsOneWidget);
      expect(find.text('In sync'), findsOneWidget);
      expect(find.text('Delayed'), findsOneWidget);
      for (final word in ['Body', 'Mind', 'The one who noticed']) {
        expect(find.text(word), findsNWidgets(2), reason: '$word on both cards');
      }
      expect(find.text('Where your hand felt to be'), findsNWidgets(2));
      expect(find.text('Flinch after the stone'), findsNWidgets(2));
      expect(find.text('It felt like my hand'), findsNWidgets(2));
      expect(find.text('The awareness that noticed was the same'), findsNWidgets(2));
      expect(find.text('A pointer, not proof'), findsNWidgets(2));
    });

    testWidgets('numbers: signed cm with direction words, ms with strength chip, -3..+3 bars', (tester) async {
      await _pump(tester, fullWitness());
      // In sync: drift +2.0 cm toward, 96 ms Strong, ownership 5.5 -> +1.5, q4 6 -> +2.0, q5 6.5 -> +2.5.
      final sync = find.byKey(const ValueKey('ph-witness-card-sync'));
      expect(_textIn(sync, '+2.0 cm'), findsOneWidget);
      expect(_textIn(sync, 'toward the virtual hand'), findsOneWidget);
      expect(_textIn(sync, '96 ms'), findsOneWidget);
      expect(_textIn(sync, 'Strong'), findsOneWidget);
      expect(_textIn(sync, '+1.5'), findsOneWidget);
      expect(_textIn(sync, '+2.0'), findsOneWidget);
      expect(_textIn(sync, '+2.5'), findsOneWidget);
      // Delayed: drift 0.0 -> no shift, 140 ms Weak, ownership 2.5 -> -1.5, q4 6 -> +2.0.
      final delayed = find.byKey(const ValueKey('ph-witness-card-async'));
      expect(_textIn(delayed, '0.0 cm'), findsOneWidget);
      expect(_textIn(delayed, 'no shift'), findsOneWidget);
      expect(_textIn(delayed, '140 ms'), findsOneWidget);
      expect(_textIn(delayed, 'Weak'), findsOneWidget);
      expect(_textIn(delayed, '-1.5'), findsOneWidget);
      expect(_textIn(delayed, '+2.0'), findsOneWidget);
    });

    testWidgets('drift: away from the virtual hand, a tiny shift reads as no shift, never "-0.0"', (tester) async {
      await _pump(tester, _witness(sync: {'drift_change_cm': -2.4}, async: {'drift_change_cm': -0.04}));
      expect(_textIn(_in('sync', 'drift'), '-2.4 cm'), findsOneWidget);
      expect(_textIn(_in('sync', 'drift'), 'away from the virtual hand'), findsOneWidget);
      expect(_textIn(_in('async', 'drift'), '0.0 cm'), findsOneWidget);
      expect(_textIn(_in('async', 'drift'), 'no shift'), findsOneWidget);
      expect(find.textContaining('-0.0'), findsNothing);
      await _pump(tester, _witness(sync: {'drift_change_cm': 0.2}));
      expect(_textIn(_in('sync', 'drift'), '+0.2 cm'), findsOneWidget);
      expect(_textIn(_in('sync', 'drift'), 'no shift'), findsOneWidget, reason: '0.25 cm or less = no shift, like the headset');
    });

    testWidgets('the neutral answer (wire 4) reads 0.0', (tester) async {
      await _pump(tester, _witness(sync: {'ownership': 4, 'witness_q4': 4}));
      expect(_textIn(_in('sync', 'ownership'), '0.0'), findsOneWidget);
      expect(_textIn(_in('sync', 'awareness'), '0.0'), findsOneWidget);
    });
  });

  group('missing values read "No data", never 0', () {
    testWidgets('no summary at all', (tester) async {
      await _pump(tester, null);
      expect(tester.takeException(), isNull);
      // drift, flinch, ownership, q4 on each of the two cards; no q5 row.
      expect(find.text('No data'), findsNWidgets(8));
      expect(find.text('I caused that movement'), findsNothing);
      for (final chip in ['Strong', 'Weak', 'None']) {
        expect(find.text(chip), findsNothing);
      }
      expect(find.textContaining('cm'), findsNothing);
      expect(find.textContaining(' ms'), findsNothing);
    });

    testWidgets('an event whose numbers are all null looks the same', (tester) async {
      await _pump(tester, PhantomWitness.tryParse({'sync': <String, dynamic>{}, 'async': null, 'condition_order': <String>[]}));
      expect(find.text('No data'), findsNWidgets(8));
    });

    testWidgets('the real fixture has no flinch latency / strength: those say "No data", the rest shows', (tester) async {
      await _pump(tester, fixtureWitness());
      expect(_textIn(_in('sync', 'flinch'), 'No data'), findsOneWidget);
      expect(_textIn(_in('async', 'flinch'), 'No data'), findsOneWidget);
      expect(find.text('Strong'), findsNothing);
      expect(_textIn(_in('sync', 'drift'), '+2.0 cm'), findsOneWidget);
      expect(_textIn(_in('async', 'drift'), '0.0 cm'), findsOneWidget);
      expect(_textIn(_in('sync', 'ownership'), '+1.5'), findsOneWidget);
      expect(_textIn(_in('async', 'ownership'), '-1.5'), findsOneWidget);
      expect(find.text('No data'), findsNWidgets(2));
    });

    testWidgets('a strength word without a latency: the latency says "No data", the chip stays', (tester) async {
      await _pump(tester, _witness(sync: {'flinch_strength': 'none'}));
      expect(_textIn(_in('sync', 'flinch'), 'No data'), findsOneWidget);
      expect(_textIn(_in('sync', 'flinch'), 'None'), findsOneWidget);
    });

    testWidgets('a questionnaire value off the 1..7 contract scale is "No data", not a clamped number', (tester) async {
      await _pump(tester, _witness(sync: {'ownership': 0, 'witness_q4': -2}));
      expect(_textIn(_in('sync', 'ownership'), 'No data'), findsOneWidget);
      expect(_textIn(_in('sync', 'awareness'), 'No data'), findsOneWidget);
    });
  });

  group('the agency row (q5)', () {
    testWidgets('absent when neither condition has agency_q5', (tester) async {
      await _pump(tester, fixtureWitness());
      expect(find.text('I caused that movement'), findsNothing);
      expect(_in('sync', 'agency'), findsNothing);
    });

    testWidgets('present on both cards when one has it; the other says "No data"', (tester) async {
      await _pump(tester, fullWitness());
      expect(find.text('I caused that movement'), findsNWidgets(2));
      expect(_textIn(_in('sync', 'agency'), '+2.5'), findsOneWidget);
      expect(_textIn(_in('async', 'agency'), 'No data'), findsOneWidget);
    });
  });

  group('layout', () {
    testWidgets('side by side from 900 dp, stacked below', (tester) async {
      for (final (width, sideBySide) in [(1280.0, true), (900.0, true), (899.0, false), (390.0, false)]) {
        // Tall enough that the wide layout is never scaled down (rects are compared unscaled).
        await _pump(tester, fullWitness(), size: Size(width, 1600));
        expect(tester.takeException(), isNull, reason: 'width $width');
        final a = tester.getRect(find.byKey(const ValueKey('ph-witness-card-sync')));
        final b = tester.getRect(find.byKey(const ValueKey('ph-witness-card-async')));
        if (sideBySide) {
          expect(a.top, closeTo(b.top, 0.5), reason: 'same top at $width');
          expect(a.right, lessThan(b.left), reason: 'In sync left of Delayed at $width');
          expect(a.height, closeTo(b.height, 0.5), reason: 'equal height at $width');
        } else {
          expect(a.left, closeTo(b.left, 0.5), reason: 'same left at $width');
          expect(a.bottom, lessThan(b.top), reason: 'In sync above Delayed at $width');
        }
      }
    });

    testWidgets('1280x800: no scrolling; the screen shrinks by at most 5 % (normal run) or 15 % (with the agency row)', (tester) async {
      await _pump(tester, fixtureWitness());
      var h = tester.getSize(find.byKey(const ValueKey('ph-witness-content'))).height;
      print('1280x800 content height, no agency: ${h.toStringAsFixed(0)} dp (scale ${(800 / h).clamp(0, 1).toStringAsFixed(2)})');
      expect(find.byType(SingleChildScrollView), findsNothing, reason: 'the audience screen never scrolls');
      expect(800 / h, greaterThanOrEqualTo(0.95), reason: 'normal run: shrunk by at most 5 %');
      await _pump(tester, fullWitness());
      h = tester.getSize(find.byKey(const ValueKey('ph-witness-content'))).height;
      print('1280x800 content height, with agency row: ${h.toStringAsFixed(0)} dp (scale ${(800 / h).clamp(0, 1).toStringAsFixed(2)})');
      expect(800 / h, greaterThanOrEqualTo(0.85), reason: 'with the agency row: shrunk by at most 15 %');
    });

    testWidgets('phone: the page scrolls and the closing lines are reachable', (tester) async {
      await _pump(tester, fullWitness(), size: _phone);
      expect(find.byType(SingleChildScrollView), findsOneWidget);
      expect(find.byType(FittedBox), findsNothing, reason: 'no shrink on phones');
      await tester.drag(find.byType(SingleChildScrollView), const Offset(0, -3000));
      await tester.pump();
      expect(find.text('You noticed every change.'), findsOneWidget);
    });

    testWidgets('no overflow: phones, laptop, large monitor, both languages, 2x text on a phone', (tester) async {
      final cases = <(Size, double)>[
        (_phone360, 1),
        (_phone360, 2),
        (_phone, 1),
        (_phone, 2),
        (_laptop, 1),
        (const Size(1600, 1000), 1),
        (const Size(1024, 700), 1.5),
      ];
      for (final lang in ['en', 'hi']) {
        for (final summary in [fullWitness(), fixtureWitness(), null]) {
          for (final (size, scale) in cases) {
            await _pump(tester, summary, lang: lang, size: size, textScale: scale);
            expect(tester.takeException(), isNull, reason: '$lang $size x$scale ${summary == null ? 'empty' : ''}');
          }
        }
      }
    });
  });

  group('the closing lines', () {
    testWidgets('fade in 3 s after the mirror appears, over 1 s', (tester) async {
      await _pump(tester, fullWitness());
      expect(_closingOpacity(tester), 0);
      await tester.pump(const Duration(milliseconds: 2900));
      expect(_closingOpacity(tester), 0, reason: 'still hidden at 2.9 s');
      await tester.pump(const Duration(milliseconds: 600));
      expect(_closingOpacity(tester), closeTo(0.5, 0.05), reason: 'half-way through the fade at 3.5 s');
      await tester.pump(const Duration(milliseconds: 600));
      expect(_closingOpacity(tester), 1, reason: 'fully visible from 4 s');
      expect(phantomWitnessClosingDelay, const Duration(seconds: 3));
    });

    testWidgets('a new summary restarts the fade; the same object does not', (tester) async {
      final first = fullWitness();
      await _pump(tester, first);
      await tester.pump(const Duration(seconds: 5));
      expect(_closingOpacity(tester), 1);
      await tester.pumpWidget(witnessApp(PhantomWitnessMirror(summary: first, lang: 'en')));
      await tester.pump();
      expect(_closingOpacity(tester), 1, reason: 'same object: no restart');
      await tester.pumpWidget(witnessApp(PhantomWitnessMirror(summary: fullWitness(), lang: 'en')));
      await tester.pump();
      expect(_closingOpacity(tester), 0, reason: 'new object: restart');
    });

    testWidgets('default lines when the event carries none (English)', (tester) async {
      await _pump(tester, fullWitness());
      expect(find.text('The body changed. The touch changed. The feeling of "mine" changed.'), findsOneWidget);
      expect(find.text('You noticed every change.'), findsOneWidget);
      expect(find.text('Tattva 5: consciousness is beyond the body and mind.'), findsOneWidget);
    });

    testWidgets('default lines when the event carries none (Hindi)', (tester) async {
      await _pump(tester, fullWitness(), lang: 'hi');
      for (final line in PhantomWitnessStrings.hi.closing) {
        expect(find.text(line), findsOneWidget);
      }
      expect(PhantomWitnessStrings.hi.closing, hasLength(3));
    });

    testWidgets('the headset lines are shown as sent, one paragraph per line, in the chosen language', (tester) async {
      final w = _witness(
        sync: {'ownership': 5},
        closingEn: 'First line.\n\nSecond line.\n  Third line.  ',
        closingHi: 'पहली पंक्ति।\nदूसरी पंक्ति।',
      );
      await _pump(tester, w);
      expect(find.text('First line.'), findsOneWidget);
      expect(find.text('Second line.'), findsOneWidget);
      expect(find.text('Third line.'), findsOneWidget);
      expect(find.text('पहली पंक्ति।'), findsNothing);
      await _pump(tester, w, lang: 'hi');
      expect(find.text('पहली पंक्ति।'), findsOneWidget);
      expect(find.text('दूसरी पंक्ति।'), findsOneWidget);
      expect(find.text('First line.'), findsNothing);
    });

    testWidgets('only the other language sent: the default of this language is used, not a mix', (tester) async {
      await _pump(tester, _witness(sync: {'ownership': 5}, closingEn: 'Only English.'), lang: 'hi');
      expect(find.text('Only English.'), findsNothing);
      expect(find.text(PhantomWitnessStrings.hi.closing.first), findsOneWidget);
    });

    testWidgets('a pre-D11 closing line ("did not change") from an old headset build is never shown', (tester) async {
      final stale = _witness(
        sync: {'ownership': 5},
        closingEn: 'What can be manufactured and dissolved is an appearance, not the Self. The witness that noticed the change did not change.',
        closingHi: 'जो बनाया और मिटाया जा सकता है वह एक आभास है, स्वयं नहीं। जिसने इस बदलाव को देखा, वह नहीं बदला।',
      );
      for (final lang in ['en', 'hi']) {
        await _pump(tester, stale, lang: lang);
        expect(_texts(tester).where(_forbidden.hasMatch), isEmpty, reason: lang);
        expect(find.text(PhantomWitnessStrings.forLang(lang).closing.first), findsOneWidget, reason: lang);
      }
    });
  });

  group('Hindi', () {
    testWidgets('titles, cards, groups, pointer caption', (tester) async {
      await _pump(tester, fullWitness(), lang: 'hi');
      expect(tester.takeException(), isNull);
      expect(find.text('क्या बदला?'), findsOneWidget);
      expect(find.text('साथ-साथ'), findsOneWidget);
      expect(find.text('देरी से'), findsOneWidget);
      expect(find.text('प्रारंभिक'), findsOneWidget);
      expect(find.text('शरीर'), findsNWidgets(2));
      expect(find.text('मन'), findsNWidgets(2));
      expect(find.text('देखने वाला'), findsNWidgets(2));
      expect(find.text('एक संकेत, प्रमाण नहीं'), findsNWidgets(2));
      expect(find.text('तेज़'), findsOneWidget);
      expect(find.text('हल्का'), findsOneWidget);
      expect(find.text('वर्चुअल हाथ की ओर'), findsOneWidget);
      expect(find.text('वह गति मैंने ही कराई'), findsNWidgets(2));
    });

    testWidgets('no data in Hindi; an unknown language falls back to English', (tester) async {
      await _pump(tester, null, lang: 'hi');
      expect(find.text('कोई डेटा नहीं'), findsNWidgets(8));
      await _pump(tester, null, lang: 'fr');
      expect(find.text('No data'), findsNWidgets(8));
      expect(find.text('What changed?'), findsOneWidget);
    });
  });

  group('wording rule (spec D11)', () {
    test('no string of either language says the demo creates, measures or proves consciousness, or that the witness did not change', () {
      for (final s in [PhantomWitnessStrings.en, PhantomWitnessStrings.hi]) {
        final all = [
          s.title, s.preliminary, s.inSync, s.delayed, s.body, s.mind, s.noticer, s.drift, s.toward, s.away, s.noShift, //
          s.flinch, s.strong, s.weak, s.none, s.ownership, s.agency, s.awareness, s.pointer, s.noData, ...s.closing,
        ];
        expect(all.where(_forbidden.hasMatch), isEmpty);
        expect(all.every((t) => t.trim().isNotEmpty), isTrue);
      }
      expect(PhantomWitnessStrings.en.pointer, 'A pointer, not proof');
      expect(PhantomWitnessStrings.hi.pointer, 'एक संकेत, प्रमाण नहीं');
    });

    testWidgets('nothing on screen matches it either, for every data case and language', (tester) async {
      for (final lang in ['en', 'hi']) {
        for (final summary in [fullWitness(), fixtureWitness(), null]) {
          await _pump(tester, summary, lang: lang);
          expect(_texts(tester).where(_forbidden.hasMatch), isEmpty);
        }
      }
    });

    test('the patterns do catch the wording we forbid', () {
      for (final bad in [
        'We created consciousness.',
        'This measures consciousness',
        'It proves that consciousness is beyond the body',
        'The witness that noticed the change did not change.',
        'जिसने इस बदलाव को देखा, वह नहीं बदला।',
      ]) {
        expect(_forbidden.hasMatch(bad), isTrue, reason: bad);
      }
      expect(_forbidden.hasMatch('Tattva 5: consciousness is beyond the body and mind.'), isFalse);
      expect(_forbidden.hasMatch('A pointer, not proof'), isFalse);
    });
  });
}
