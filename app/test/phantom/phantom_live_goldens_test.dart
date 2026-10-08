@Tags(['golden'])
library;

// A1 goldens: the Phantom Hand operator card (and its observer view) at
// 360x800 (the Android hub phone), 390x844, 1280x800 and 1600x1000; light and
// dark; text scale 1.0 and 2.0 (2.0 on both phone widths). Images land in
// test/phantom/goldens/. Regenerate with:
//   flutter test --update-goldens test/phantom/phantom_live_goldens_test.dart
import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show FontLoader;
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:opus_app/features/live/phantom_live_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';

Future<void> _loadFont(String family, File? file) async {
  if (file == null) return;
  final data = await file.readAsBytes();
  final loader = FontLoader(family)..addFont(Future.value(ByteData.view(data.buffer)));
  await loader.load();
}

File? _materialIconsFont() {
  try {
    var dir = File(Platform.resolvedExecutable).absolute.parent;
    for (var i = 0; i < 8; i++) {
      final candidate = File('${dir.path}/bin/cache/artifacts/material_fonts/MaterialIcons-Regular.otf');
      if (candidate.existsSync()) return candidate;
      if (dir.parent.path == dir.path) break;
      dir = dir.parent;
    }
  } catch (_) {}
  return null;
}

class _Repo implements PhantomLiveRepository {
  _Repo(this.snapshot);
  final PhantomLiveSnapshot snapshot;

  @override
  Stream<PhantomLiveSnapshot> watch() => Stream.value(snapshot);

  @override
  Future<bool> send(PhantomCommand command, {Map<String, dynamic>? params}) async => true;
}

/// A deterministic mid-induction moment: the full 20 s window of traces with
/// one stone landing at 15 s and a muscle burst just after it, at the levels of
/// a real run (EMG rests near 420 and the flinch reaches ~5x; |accel| rests at
/// 9.8 and jolts to ~14).
PhantomLiveSnapshot _snapshot({PhantomCondition? condition = PhantomCondition.sync, PhantomRunState state = PhantomRunState.running}) {
  const n = 400;
  final emg = List<double>.generate(n, (i) => 420 + 14 * math.sin(i / 6) + (i >= 302 ? 1900 * math.exp(-(i - 302) / 8) : 0));
  final acc = List<double>.generate(n, (i) => 9.8 + 0.3 * math.sin(i / 4) + (i >= 301 && i < 316 ? 4.5 * math.exp(-(i - 301) / 5) : 0));
  return PhantomLiveSnapshot(
    runState: state,
    connected: true,
    game: PhantomGameState(
      phase: 'induction',
      condition: condition,
      remainingS: 41.5,
      hapticConnected: true,
      bioConnected: true,
      emgLevel: 0.42,
    ),
    chunk: TraceChunk(emgEnv: emg, accelMag: acc, t0Ms: 0),
    markers: const [
      TraceMarker(kind: TraceMarkerKind.threatImpact, tMs: 15000),
      TraceMarker(kind: TraceMarkerKind.emgBurst, tMs: 15120),
    ],
    conditionOrder: 'async_first',
    rttMs: 18,
  );
}

const _viewports = {
  'phone360': Size(360, 800),
  'phone': Size(390, 844),
  'tablet': Size(1280, 800),
  'desktop': Size(1600, 1000),
};

Future<void> _golden(
  WidgetTester tester, {
  required String name,
  required String viewport,
  required Brightness brightness,
  double textScale = 1,
  bool observer = false,
  Locale locale = const Locale('en'),
}) async {
  tester.view.physicalSize = _viewports[viewport]!;
  tester.view.devicePixelRatio = 1;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      debugShowCheckedModeBanner: false,
      theme: brightness == Brightness.light ? AppTheme.light() : AppTheme.dark(),
      locale: locale,
      localizationsDelegates: const [
        ...AppLocalizations.localizationsDelegates,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      supportedLocales: AppLocalizations.supportedLocales,
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(textScale)),
        child: child!,
      ),
      home: PhantomLiveScreen(repository: _Repo(_snapshot()), initialObserver: observer),
    ),
  );
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 100));
  expect(tester.takeException(), isNull);
  await expectLater(
    find.byType(MaterialApp),
    matchesGoldenFile('goldens/${name}_${viewport}_${brightness.name}_${textScale}x.png'),
  );
}

void main() {
  setUpAll(() async {
    await _loadFont('AtkinsonHyperlegibleNext', File('assets/fonts/AtkinsonHyperlegibleNext-Variable.ttf'));
    await _loadFont('MaterialIcons', _materialIconsFont());
  });

  for (final viewport in _viewports.keys) {
    for (final brightness in Brightness.values) {
      testWidgets('card $viewport ${brightness.name} 1.0x', tags: ['golden'], (tester) async {
        await _golden(tester, name: 'phantom_card', viewport: viewport, brightness: brightness);
      });
      testWidgets('observer $viewport ${brightness.name} 1.0x', tags: ['golden'], (tester) async {
        await _golden(tester, name: 'phantom_observer', viewport: viewport, brightness: brightness, observer: true);
      });
    }
  }
  for (final viewport in ['phone360', 'phone']) {
    for (final brightness in Brightness.values) {
      testWidgets('card $viewport ${brightness.name} 2.0x', tags: ['golden'], (tester) async {
        await _golden(tester, name: 'phantom_card', viewport: viewport, brightness: brightness, textScale: 2);
      });
    }
  }
  testWidgets('card phone360 dark Hindi', tags: ['golden'], (tester) async {
    await _golden(tester, name: 'phantom_card_hi', viewport: 'phone360', brightness: Brightness.dark, locale: const Locale('hi'));
  });
}
