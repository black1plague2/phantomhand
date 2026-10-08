// The Phantom Hand in-app demo, without any widget: the bundled recording
// (assets/demo/phantom_hand), the timeline built from it, the mock engine
// playing that timeline, and the demo's texts. The screens are in
// phantom_demo_flow_test.dart.
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/embodiment.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/shared/clinical/phantom_demo_strings.dart';

const _dir = 'assets/demo/phantom_hand';

List<Map<String, dynamic>> _events() => [
      for (final l in File('$_dir/events.ndjson').readAsLinesSync())
        if (l.trim().isNotEmpty) jsonDecode(l) as Map<String, dynamic>,
    ];

PhantomRecordedTrace _trace() => PhantomRecordedTrace.tryParse(jsonDecode(File('$_dir/live_trace.json').readAsStringSync()))!;

PhantomDemoScript _script() => PhantomDemoScript.tryBuild(events: _events(), trace: _trace())!;

const _fourteen = [
  'calibrate',
  'probe_pre',
  'induction',
  'threat',
  'probe_post',
  'questionnaire',
  'probe_pre',
  'induction',
  'threat',
  'probe_post',
  'questionnaire',
  'dissolve',
  'reveal',
  'witness',
];

/// Runs the engine to the end in 200 ms ticks (what the repository's timer does).
({List<PhantomLiveSnapshot> snapshots, int ticks}) _play(PhantomMockEngine e) {
  e.apply(PhantomCommand.start);
  final seen = <PhantomLiveSnapshot>[];
  var ticks = 0;
  while (e.runState != PhantomRunState.finished && ticks < 2000) {
    seen.add(e.step(const Duration(milliseconds: 200)));
    ticks++;
  }
  return (snapshots: seen, ticks: ticks);
}

void main() {
  group('the bundled recording', () {
    test('is small: every file under 50 KB (rootBundle decodes bigger ones off-thread), all together under 300 KB', () {
      var total = 0;
      for (final f in Directory(_dir).listSync().whereType<File>()) {
        final size = f.lengthSync();
        expect(size, lessThan(50 * 1024), reason: f.path);
        total += size;
      }
      expect(total, lessThan(300 * 1024));
    });

    test('session.json belongs to the demo participant, keeps the recording id and says it is simulated', () {
      final s = jsonDecode(File('$_dir/session.json').readAsStringSync()) as Map<String, dynamic>;
      expect(s['patient_ref'], demoPhantomPatientId);
      expect(s['session_id'], demoPhantomSessionId);
      expect(s['mode'], 'simulation');
      expect(((s['blocks'] as List).single as Map)['game_id'], 'phantom_hand');
    });

    test('live_trace.json is flagged simulated and holds both traces at 20 Hz for the whole 199 s run', () {
      final raw = jsonDecode(File('$_dir/live_trace.json').readAsStringSync()) as Map<String, dynamic>;
      expect(raw['simulated'], isTrue);
      final trace = _trace();
      expect(trace.fsHz, 20);
      expect(trace.emgEnv.length, trace.accelMag.length);
      expect(trace.emgEnv.length / trace.fsHz, closeTo(199, 1));
    });

    test('metrics.json is the analytics output: an embodiment block with both conditions', () {
      final e = Embodiment.tryParseMetrics(jsonDecode(File('$_dir/metrics.json').readAsStringSync()))!;
      expect(e.conditionOrder, ['async', 'sync']);
      expect(e.sync['ownership']!.value, 6.0);
      expect(e.async['ownership']!.value, 3.0);
      expect(e.sync['flinch_emg_peak_x']!.value, closeTo(6.02, 0.01));
      expect(e.async['flinch_emg_peak_x']!.value, closeTo(6.04, 0.01));
    });

    test('the demo participant is the first patient, has no personal name, and is called simulated', () {
      final list = (jsonDecode(File('assets/fixtures/patients.json').readAsStringSync()) as List).cast<Map<String, dynamic>>();
      expect(list.first['id'], demoPhantomPatientId);
      expect(list.first['display_name'], 'Demo participant (simulated)');
    });
  });

  group('the timeline built from it', () {
    test('plays the recording\'s 14 phases in its order, then done, delayed condition first', () {
      final script = _script();
      expect([for (final s in script.steps) s.phase], [..._fourteen, 'done']);
      expect(
        [for (final s in script.steps) s.condition],
        [
          null,
          for (var i = 0; i < 5; i++) PhantomCondition.async,
          for (var i = 0; i < 5; i++) PhantomCondition.sync,
          null,
          null,
          null,
          null,
        ],
      );
    });

    test('lasts 73 s for the 14 phases (60 to 90 s), long phases compressed and the stone drop at recorded speed', () {
      final script = _script();
      expect(script.totalSeconds, 73);
      expect(script.totalSeconds, inInclusiveRange(60, 90));
      final induction = script.steps.firstWhere((s) => s.phase == 'induction');
      expect((induction.realToMs - induction.realFromMs) / 1000, closeTo(60, 0.1));
      expect(induction.seconds, 10);
      final threat = script.steps.firstWhere((s) => s.phase == 'threat');
      expect((threat.realToMs - threat.realFromMs) / 1000, closeTo(threat.seconds, 0.05));
      expect(script.emg.length, 73 * PhantomDemoScript.fsHz);
      expect(script.accel.length, script.emg.length);
    });

    test('the stone and the muscle burst are the recording\'s own, inside the stone-drop phases', () {
      final script = _script();
      final events = _events();
      expect([for (final m in script.markers) m.kind], [
        TraceMarkerKind.threatImpact,
        TraceMarkerKind.emgBurst,
        TraceMarkerKind.threatImpact,
        TraceMarkerKind.emgBurst,
      ]);
      var start = 0.0;
      var n = 0;
      for (final step in script.steps) {
        if (step.phase == 'threat') {
          final impact = (events.where((e) => e['type'] == 'threat_impact').elementAt(n)['data'] as Map)['impact_ms'] as num;
          final latency = (events.where((e) => e['type'] == 'threat_response').elementAt(n)['data'] as Map)['emg_latency_ms'] as num;
          double demoMs(num real) => start + (real - step.realFromMs) / (step.realToMs - step.realFromMs) * step.seconds * 1000;
          expect(script.markers[2 * n].tMs, closeTo(demoMs(impact), 1e-6));
          expect(script.markers[2 * n + 1].tMs, closeTo(demoMs(impact + latency), 1e-6));
          expect(script.markers[2 * n + 1].tMs - script.markers[2 * n].tMs, closeTo(latency, 1.0), reason: 'recorded speed: ms stay ms');
          n++;
        }
        start += step.seconds * 1000;
      }
      expect(n, 2);
    });

    test('both flinches are clearly on the traces: about 6x resting in the EMG and a jolt in |accel|, starting at the burst marker', () {
      final script = _script();
      expect(script.emg.reduce((a, b) => a > b ? a : b), inInclusiveRange(2400, 2600));
      final restingEmg = ([...script.emg]..sort())[script.emg.length ~/ 2];
      expect(restingEmg, inInclusiveRange(400, 450));
      final restingAccel = ([...script.accel]..sort())[script.accel.length ~/ 2];
      expect(restingAccel, inInclusiveRange(9.6, 10.1));
      for (var n = 0; n < 2; n++) {
        final impact = script.markers[2 * n];
        final burst = script.markers[2 * n + 1];
        final at = (burst.tMs / 50).floor();
        final before = (impact.tMs / 50).floor();
        expect(script.emgAt(before), lessThan(restingEmg * 1.3), reason: 'quiet when the stone lands (flinch $n)');
        // The burst starts inside the marker's 50 ms bin (a mean of rest and burst), then it is at full height.
        expect(script.emgAt(at), greaterThan(restingEmg * 3), reason: 'flinch $n');
        expect([for (var i = at + 1; i < at + 5; i++) script.emgAt(i)], everyElement(greaterThan(restingEmg * 5)), reason: 'flinch $n');
        final jolt = [for (var i = before; i < at + 10; i++) script.accelAt(i)].reduce((a, b) => a > b ? a : b);
        expect(jolt, greaterThan(restingAccel + 3), reason: 'the arm jolts too (flinch $n)');
      }
    });

    test('the witness numbers are the recording\'s witness_summary, nothing re-estimated', () {
      final w = _script().witness;
      expect(w.conditionOrder, ['async', 'sync']);
      expect(w.sync.ownership, 6.0);
      expect(w.async.ownership, 3.0);
      expect(w.sync.driftChangeCm, closeTo(2.9921875, 1e-6));
      expect(w.async.driftChangeCm, closeTo(1.0, 1e-6));
      expect(w.sync.flinchLatencyMs, closeTo(121.899, 0.001));
      expect(w.async.flinchLatencyMs, closeTo(124.584, 0.001));
      expect(w.sync.flinchStrength, PhantomFlinchStrength.strong);
      expect(w.async.flinchStrength, PhantomFlinchStrength.strong);
      expect(w.sync.witnessQ4, 4.0, reason: 'q4 was asked once, after the last condition');
      expect(w.async.witnessQ4, isNull);
      expect(w.closingEn, contains('You noticed every change.'));
    });

    test('a recording that cannot play gives null: no trace, no phase, no witness', () {
      final events = _events();
      expect(PhantomDemoScript.tryBuild(events: events, trace: null), isNull);
      expect(PhantomDemoScript.tryBuild(events: [for (final e in events) if (e['type'] != 'phase_start') e], trace: _trace()), isNull);
      expect(PhantomDemoScript.tryBuild(events: [for (final e in events) if (e['type'] != 'witness_summary') e], trace: _trace()), isNull);
      expect(PhantomRecordedTrace.tryParse({'fs_hz': 20, 'emg_env': <num>[], 'accel_mag': <num>[1]}), isNull);
      expect(PhantomRecordedTrace.tryParse('nope'), isNull);
    });
  });

  group('the engine playing it', () {
    test('visits the 14 phases then done on the script\'s clock, and the mirror numbers come with the witness phase only', () {
      final script = _script();
      final run = _play(PhantomMockEngine(demo: script));
      final phases = <String>[];
      for (final s in run.snapshots) {
        final p = s.game!.phase;
        if (phases.isEmpty || phases.last != p) phases.add(p);
        expect(s.witness == null, p != 'witness', reason: 'the witness_summary rides with the witness phase ($p)');
        if (s.witness != null) expect(s.witness, same(script.witness));
      }
      expect(phases, [..._fourteen, 'done']);
      expect(run.ticks * 0.2, closeTo(73, 0.3));
      expect(run.snapshots.last.runState, PhantomRunState.finished);
      expect(run.snapshots.first.game!.phase, 'calibrate');
    });

    test('streams exactly the script\'s traces and markers', () {
      final script = _script();
      final run = _play(PhantomMockEngine(demo: script));
      final emg = [for (final s in run.snapshots) ...s.chunk!.emgEnv];
      final accel = [for (final s in run.snapshots) ...s.chunk!.accelMag];
      expect(emg.take(script.emg.length), script.emg);
      expect(accel.take(script.accel.length), script.accel);
      final markers = [for (final s in run.snapshots) ...s.markers];
      expect([for (final m in markers) m.kind], [for (final m in script.markers) m.kind]);
      for (var i = 0; i < markers.length; i++) {
        expect(markers[i].tMs, closeTo(script.markers[i].tMs, 1e-6));
      }
    });

    test('every phase shows its condition: delayed in the first block, in sync in the second, none around them', () {
      final run = _play(PhantomMockEngine(demo: _script()));
      final seen = <(String, PhantomCondition?)>[];
      for (final s in run.snapshots) {
        final pair = (s.game!.phase, s.game!.condition);
        if (seen.isEmpty || seen.last != pair) seen.add(pair);
      }
      expect(seen, [
        ('calibrate', null),
        for (final p in ['probe_pre', 'induction', 'threat', 'probe_post', 'questionnaire']) (p, PhantomCondition.async),
        for (final p in ['probe_pre', 'induction', 'threat', 'probe_post', 'questionnaire']) (p, PhantomCondition.sync),
        ('dissolve', null),
        ('reveal', null),
        ('witness', null),
        ('done', null),
      ]);
    });

    test('the synthetic demo is untouched: without a script the engine still plays its own run', () {
      final e = PhantomMockEngine();
      final run = _play(e);
      expect(run.snapshots.any((s) => s.game!.phase == 'dissolve'), isFalse);
      expect(run.snapshots.firstWhere((s) => s.witness != null).witness, same(mockPhantomWitness));
    });
  });

  group('the texts', () {
    final forbidden = RegExp(
      r'significan|proves?\b|proven|(creat|measur|prov|manufactur)\w*\W+(\w+\W+){0,3}consciousness|did not change|नहीं बदला',
      caseSensitive: false,
    );
    const keys = ['button', 'buttonHint', 'badge', 'skip', 'stop', 'loading', 'unplayable', 'simulatedRun', 'simulatedNote', 'cap_mirror'];

    test('the button, the badge and the label read as the brief says, in English and in Hindi', () {
      const en = PhantomDemoStrings.en;
      const hi = PhantomDemoStrings.hi;
      expect(en.t('button'), 'Run Phantom Hand demo');
      expect(hi.t('button'), 'फैंटम हैंड डेमो चलाएँ');
      expect(en.t('badge'), 'Demo, simulated data');
      expect(hi.t('badge'), contains('सिमुलेटेड'));
      expect(en.t('simulatedRun'), 'Simulated run');
      expect(hi.t('simulatedRun'), contains('सिमुलेटेड'));
      for (final s in [en, hi]) {
        for (final k in keys) {
          expect(s.t(k), isNot(k), reason: 'a text for $k');
        }
      }
    });

    test('a caption for every one of the 14 phases (a different one per condition in the induction), nothing about proof or consciousness', () {
      final script = _script();
      for (final s in [PhantomDemoStrings.en, PhantomDemoStrings.hi]) {
        final seen = <String>{};
        for (final step in script.steps.where((x) => x.phase != 'done')) {
          final c = s.caption(step.phase, step.condition);
          expect(c, isNotNull, reason: step.phase);
          expect(c, isNotEmpty);
          seen.add(c!);
        }
        // 9 phase ids, and the induction has one caption for each condition.
        expect(seen.length, 10);
        for (final k in keys) {
          expect(forbidden.hasMatch(s.t(k)), isFalse, reason: '$k: ${s.t(k)}');
        }
        for (final c in seen) {
          expect(forbidden.hasMatch(c), isFalse, reason: c);
        }
      }
      expect(PhantomDemoStrings.en.caption('induction', PhantomCondition.async), contains('late'));
      expect(PhantomDemoStrings.en.caption('induction', PhantomCondition.sync), contains('together'));
      expect(PhantomDemoStrings.en.caption('threat', PhantomCondition.sync), contains('stone'));
      expect(PhantomDemoStrings.en.caption('probe_pre', null), contains('Point to where your hand feels'));
      expect(PhantomDemoStrings.en.caption('done', null), isNull);
    });
  });
}
