// Shared by the Phantom Hand demo's widget tests and its picture test: the real
// app (router, shell, mock repositories, bundled recording) in a test window,
// and the helpers that wait for it on the test's fake clock.
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/data/repositories/mock/mock_patients_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_sessions_repository.dart';
import 'package:opus_app/data/repositories/mock/phantom_demo_repository.dart';
import 'package:opus_app/main.dart';

/// Reads every bundled asset the demo flow touches once, on the real clock, so
/// that inside a widget test the rootBundle cache answers them (a first read
/// of an asset never completes under a fake clock).
Future<void> warmDemoAssets() async {
  const demo = PhantomDemoAssets();
  await demo.script();
  await demo.envelope();
  await demo.metricsJson();
  await MockPatientsRepository().listPatients();
  await MockSessionsRepository().listSessionsForPatient('warm-up');
}

/// The hub as `HubController` reports it while it listens and nothing is
/// connected: running, no headsets, a sessions folder with nothing in it.
class IdleHub extends HubController {
  new(this.dir);

  final String dir;

  @override
  HubControllerState build() => HubControllerState.running(hubId: 'test-hub', port: 8797, headsets: const []);

  @override
  Future<void> start({int port = 8787, int beaconPort = 8788}) async {}

  @override
  String? get sessionsDir => dir;
}

/// Pumps the real app in a [size] window (phone by default), signed out: it
/// lands on the login screen, as a cold start does.
Future<void> pumpDemoApp(
  WidgetTester tester,
  ProviderContainer container, {
  Size size = const Size(390, 844),
  Key? boundary,
}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: RepaintBoundary(key: boundary, child: const OpusApp()),
    ),
  );
  await tester.pumpAndSettle();
}

/// One step of waiting for the app: first a few real milliseconds, because a
/// bundle read answered from the cache still resumes on the real event loop,
/// then [step] on the fake clock (the mock repositories' 220 ms latencies,
/// page transitions, the demo's own clock).
Future<void> tick(WidgetTester tester, [Duration step = const Duration(milliseconds: 250)]) async {
  await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 10)));
  await tester.pump(step);
}

/// [rounds] x [tick].
Future<void> settle(WidgetTester tester, {int rounds = 12}) async {
  for (var i = 0; i < rounds; i++) {
    await tick(tester);
  }
}

/// Waits [step] at a time until [finder] matches (at most [max] times),
/// calling [each] after every step. [real] also gives the real event loop a
/// turn each time (needed while assets are being read, not while the demo plays).
Future<void> pumpUntil(
  WidgetTester tester,
  Finder finder, {
  Duration step = const Duration(milliseconds: 200),
  int max = 800,
  bool real = false,
  void Function()? each,
}) async {
  for (var i = 0; i < max && finder.evaluate().isEmpty; i++) {
    if (real) {
      await tick(tester, step);
    } else {
      await tester.pump(step);
    }
    each?.call();
  }
  expect(finder, findsWidgets, reason: 'gave up waiting after $max steps of $step');
}
