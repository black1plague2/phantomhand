import 'dart:math' as math;

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/data/models/phantom_live.dart';

// The y range of the live EMG plot (traceRange): a tight axis around the data, a
// floor on its span, held between status messages. The signals are made up at the
// levels the owner reported from the first real run: rest near 230 with about 20
// counts of noise, contractions of a few tens to a few hundred.

/// Rest noise of about 20 counts peak to peak, deterministic.
double _noise(int i) => 7 * math.sin(i * 1.7) + 3 * math.sin(i * 0.61 + 1);

/// A one-second contraction: a half sine of [height] counts from sample [from].
double _bump(int i, int from, double height) => i >= from && i < from + 20 ? height * math.sin(math.pi * (i - from) / 20) : 0;

List<TracePoint> _pts(int n, double Function(int i) value) => [for (var i = 0; i < n; i++) TracePoint(i * 50.0, value(i))];

/// What the live card does with the EMG: 20 Hz samples arrive half a second at a
/// time into a 20 s window, and the range is held from one message to the next.
class _Feed {
  final window = <TracePoint>[];
  final history = <({TraceRange range, double lowest, double highest})>[];
  TraceRange? _range;
  int _i = 0;

  void add(double seconds, double Function(int i) value) {
    for (var message = 0; message < (seconds * 2).round(); message++) {
      for (var k = 0; k < 10; k++, _i++) {
        window.add(TracePoint(_i * 50.0, value(_i)));
      }
      window.removeWhere((p) => p.tMs < window.last.tMs - phantomTraceWindowMs);
      _range = traceRange(window, previous: _range);
      final values = [for (final p in window) p.value];
      history.add((range: _range!, lowest: values.reduce(math.min), highest: values.reduce(math.max)));
    }
  }

  List<double> get mins => [for (final h in history) h.range.min];
  List<double> get maxs => [for (final h in history) h.range.max];
}

int _changes(List<double> v) => [for (var k = 1; k < v.length; k++) if (v[k] != v[k - 1]) k].length;

void main() {
  group('one window', () {
    test('flat rest with noise: an axis just around it, so noise is a small part of the height', () {
      final pts = _pts(400, (i) => 230 + _noise(i));
      final r = traceRange(pts)!;
      final lowest = pts.map((p) => p.value).reduce(math.min);
      final highest = pts.map((p) => p.value).reduce(math.max);
      expect(r.min, lessThanOrEqualTo(lowest));
      expect(r.max, greaterThanOrEqualTo(highest));
      expect(r.max - r.min, inInclusiveRange(traceMinSpan, traceMinSpan + 20), reason: 'the floor, rounded out to the labels');
      expect((highest - lowest) / (r.max - r.min), lessThan(0.4), reason: 'noise does not fill the height');
      expect(r.rest, closeTo(230, 3));
      expect(r.peakOverRest, inInclusiveRange(1.0, 1.1));
      expect(r.min % 10, 0, reason: 'round labels');
      expect(r.max % 10, 0);
    });

    test('one clear spike: the axis reaches it and the spike takes most of the height', () {
      final r = traceRange(_pts(400, (i) => 230 + _noise(i) + _bump(i, 330, 260)))!;
      expect(r.max, greaterThanOrEqualTo(r.peak), reason: 'never clipped');
      expect(r.max, lessThan(r.peak * 1.15), reason: 'and not far above it');
      expect(r.rest, closeTo(230, 3), reason: 'a spike does not move rest');
      expect(r.peakOverRest, closeTo(2.15, 0.1));
      final rise = r.peak - r.rest!;
      expect(rise / (r.max - r.min), greaterThan(0.7));
      expect(rise / (phantomEmgScale.max - phantomEmgScale.min), lessThan(0.1), reason: 'under a tenth of the old fixed 0 to 3000 axis');
    });

    test('all zeros: the floor span at the bottom, no division by zero', () {
      final r = traceRange(_pts(400, (_) => 0))!;
      expect((r.min, r.max), (0, traceMinSpan));
      expect(r.rest, 0);
      expect(r.peakOverRest, isNull);
      for (final v in [r.low, r.high, r.min, r.max, r.peak]) {
        expect(v.isFinite, isTrue);
      }
    });

    test('one sample: a floor-span axis around it, no resting level, no ratio', () {
      final r = traceRange(_pts(1, (_) => 230))!;
      expect((r.min, r.max), (200, 260));
      expect(r.rest, isNull);
      expect(r.peak, 230);
      expect(r.peakOverRest, isNull);
    });

    test('no samples: no range', () {
      expect(traceRange(const []), isNull);
    });

    test('under 2 s of samples: a range but no resting level yet', () {
      final r = traceRange(_pts(39, (i) => 230 + _noise(i)))!;
      expect(r.rest, isNull);
      expect(r.peakOverRest, isNull);
      expect(r.max - r.min, greaterThanOrEqualTo(traceMinSpan));
    });

    test('values pinned at 4095: the floor span just below the top, inside the ADC range', () {
      final r = traceRange(_pts(400, (_) => 4095))!;
      expect(r.max, 4095);
      expect(r.max - r.min, inInclusiveRange(traceMinSpan, traceMinSpan + 10));
      expect(r.rest, 4095);
      expect(r.peakOverRest, 1);
    });

    test('a signal that uses the whole ADC range gets exactly that range', () {
      final r = traceRange(_pts(400, (i) => i.isEven ? 0 : 4095))!;
      expect((r.min, r.max), (0, 4095));
    });
  });

  group('held between status messages', () {
    test('a spike widens the axis in the very message that carries it', () {
      final feed = _Feed()
        ..add(10, (i) => 230 + _noise(i))
        ..add(0.5, (i) => 230 + _noise(i) + _bump(i, 200, 260));
      // sample 200 is the first of the 21st message: the rise is in that message
      final before = feed.history[feed.history.length - 2].range;
      final at = feed.history.last;
      expect(before.max, lessThanOrEqualTo(280));
      expect(at.range.max, greaterThanOrEqualTo(at.highest));
      expect(at.range.max, greaterThan(before.max + 100));
    });

    test('rest noise does not make the axis move: once the window is full the labels stay put', () {
      final feed = _Feed()..add(60, (i) => 230 + _noise(i) + 8 * math.sin(i / 150));
      final full = feed.history.length - 80; // the last 40 s
      expect(_changes(feed.mins.sublist(full)) + _changes(feed.maxs.sublist(full)), lessThanOrEqualTo(2));
    });

    // 230 down to 100 in a minute has been seen; the same slope up checks the mirror edge.
    for (final (name, from, to) in const [('down', 230.0, 100.0), ('up', 100.0, 230.0)]) {
      test('slow drift $name: follows without clipping, one way only, and is not read as a spike', () {
        final feed = _Feed()..add(60, (i) => from + (to - from) * i / 1200 + _noise(i));
        for (final h in feed.history) {
          expect(h.range.min, lessThanOrEqualTo(h.lowest));
          expect(h.range.max, greaterThanOrEqualTo(h.highest));
          if (h.range.rest != null) {
            expect(h.range.rest, inInclusiveRange(h.lowest, h.highest));
            expect(h.range.peakOverRest, lessThan(1.3), reason: '130 counts in a minute is not a doubling');
          }
        }
        final sign = to < from ? -1 : 1;
        expect((feed.maxs.last - feed.maxs.first) * sign, greaterThan(60), reason: 'moved with the signal');
        expect((feed.mins.last - feed.mins.first) * sign, greaterThan(60));
        for (var k = 1; k < feed.history.length; k++) {
          expect((feed.mins[k] - feed.mins[k - 1]) * sign, greaterThanOrEqualTo(0), reason: 'message $k');
          expect((feed.maxs[k] - feed.maxs[k - 1]) * sign, greaterThanOrEqualTo(0), reason: 'message $k');
        }
        expect(_changes(feed.maxs), lessThan(feed.history.length ~/ 4), reason: 'changes in steps, not with every message');
        expect(_changes(feed.mins), lessThan(feed.history.length ~/ 4));
      });
    }

    test('a spike leaving the window: the axis comes back down smoothly and never goes up again', () {
      // 5 s of rest, a one-second contraction at 5 s, then rest until 45 s.
      final feed = _Feed()..add(45, (i) => 230 + _noise(i) + _bump(i, 100, 260));
      final top = feed.maxs;
      final arrival = top.indexWhere((m) => m >= 480);
      expect(arrival, 10, reason: 'the message that carries samples 100 to 109');
      expect(top[arrival - 1], lessThanOrEqualTo(280));

      // Held for the whole 20 s the spike is in the window: it can only creep up, never down.
      final leaves = top.indexWhere((m) => m < top[arrival], arrival);
      expect(leaves, greaterThanOrEqualTo(arrival + 38));
      for (var k = arrival + 1; k < leaves; k++) {
        expect(top[k], greaterThanOrEqualTo(top[k - 1]), reason: 'message $k');
      }

      // Then down, step by step, never up (no flicker), each step a small share of the span.
      for (var k = leaves; k < top.length; k++) {
        expect(top[k], lessThanOrEqualTo(top[k - 1]), reason: 'message $k');
        final span = feed.history[k - 1].range.max - feed.history[k - 1].range.min;
        expect(top[k - 1] - top[k], lessThanOrEqualTo(0.25 * span), reason: 'message $k');
      }
      final settled = top.indexWhere((m) => m <= 290, leaves);
      expect(settled, isNot(-1), reason: 'back near rest');
      expect(settled - leaves, greaterThanOrEqualTo(8), reason: 'at least 4 s to get there, not a jump');
      for (final h in feed.history) {
        expect(h.range.max, greaterThanOrEqualTo(h.highest));
        expect(h.range.min, lessThanOrEqualTo(h.lowest));
      }
    });

    test('the newest peak is the last 5 s; the axis keeps the older one in view', () {
      // rest 3 s, a one-second contraction at 3 s, rest until 12 s
      final feed = _Feed()..add(12, (i) => 230 + _noise(i) + _bump(i, 60, 260));
      final soon = feed.history[(5 * 2) - 1]; // 5 s in, 1 s after the contraction
      final later = feed.history.last;
      expect(soon.range.rest, isNotNull);
      expect(soon.range.peakOverRest, greaterThan(2));
      expect(later.range.peakOverRest, lessThan(1.15), reason: 'the contraction is over 8 s old');
      expect(later.range.max, greaterThanOrEqualTo(480), reason: 'but the axis still shows it');
    });
  });

  group('the buffer keeps the range', () {
    TraceChunk chunk(double t0, List<double> emg) => TraceChunk(emgEnv: emg, accelMag: List.filled(emg.length, 9.8), t0Ms: t0);

    test('none until an EMG sample arrives; accel alone does not make one', () {
      final b = TraceBuffer();
      expect(b.emgRange, isNull);
      b.addChunk(const TraceChunk(emgEnv: [], accelMag: [9.8, 9.8], t0Ms: 0));
      expect(b.emgRange, isNull);
    });

    test('follows each chunk, and its numbers are those of the samples in the buffer', () {
      final b = TraceBuffer()..addChunk(chunk(0, List.generate(400, (i) => 230 + _noise(i))));
      final rest = b.emgRange!;
      expect(rest.rest, restingLevel(b.emg));
      expect(rest.max, lessThan(300));

      b.addChunk(chunk(20000, List.generate(20, (i) => 230 + _bump(i, 0, 260))));
      expect(b.emgRange!.max, greaterThan(480), reason: 'widened in the same chunk');
      expect(b.emgRange!.peakOverRest, greaterThan(2));
    });

    test('a restarted session (clear, or a chunk far in the past) does not inherit the old, wide range', () {
      final b = TraceBuffer()..addChunk(chunk(60000, List.generate(400, (i) => 230 + _bump(i, 300, 1500))));
      expect(b.emgRange!.max, greaterThan(1500));
      b.addChunk(chunk(0, List.filled(20, 230)));
      expect(b.emgRange!.max, lessThan(300), reason: 'the clock went back, so the buffer started over');
      b.clear();
      expect(b.emgRange, isNull);
    });
  });
}
