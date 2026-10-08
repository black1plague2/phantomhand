import 'dart:convert';

import 'package:flutter/services.dart' show rootBundle;
import 'package:opus_app/data/models/embodiment.dart';
import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

/// The bundled recording of the Phantom Hand demo: one clean full run of the
/// game against the sleeve simulator (a scripted participant, so SIMULATED
/// data). `session.json` (only `patient_ref` is the demo patient's id),
/// `events.ndjson` and `metrics.json` (the analytics' own output) are the
/// recording's files; `live_trace.json` is its EMG envelope and |accel| in the
/// 50 ms bins the live card plots.
const phantomDemoAssetDir = 'assets/demo/phantom_hand';

/// Reads the bundled recording. Kept apart from the fixtures under
/// `assets/fixtures/sessions/`, which `tool/sync_fixtures.dart` rewrites.
class PhantomDemoAssets {
  const new();

  Future<String> _text(String name) => rootBundle.loadString('$phantomDemoAssetDir/$name');

  Future<Map<String, dynamic>> _map(String name) async => jsonDecode(await _text(name)) as Map<String, dynamic>;

  Future<SessionEnvelope> envelope() async => SessionEnvelope.fromJson(await _map('session.json'));

  /// The recording's `metrics.json`, undecoded (the embodiment block).
  Future<Map<String, dynamic>> metricsJson() => _map('metrics.json');

  /// The lines of `events.ndjson`.
  Future<List<Map<String, dynamic>>> events() async => [
        for (final line in const LineSplitter().convert(await _text('events.ndjson')))
          if (line.trim().isNotEmpty) jsonDecode(line) as Map<String, dynamic>,
      ];

  /// The timeline the demo plays; null when the files do not hold a playable run.
  Future<PhantomDemoScript?> script() async => PhantomDemoScript.tryBuild(
        events: await events(),
        trace: PhantomRecordedTrace.tryParse(jsonDecode(await _text('live_trace.json'))),
      );

  Future<Embodiment?> embodiment() async => Embodiment.tryParseMetrics(await metricsJson());
}

/// Serves the demo patient's one session ([demoPhantomSessionId]) to the
/// screens that list and report sessions, in mock mode and in hub mode alike
/// (it sits in the composite repository next to the hub and the opened
/// folders). Knows nothing about any other patient or session.
class PhantomDemoSessionsRepository implements SessionsRepository {
  const new([this.assets = const PhantomDemoAssets()]);

  final PhantomDemoAssets assets;

  @override
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId) async =>
      patientId == demoPhantomPatientId ? [await assets.envelope()] : const [];

  @override
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId) async =>
      sessionId == demoPhantomSessionId ? await assets.envelope() : null;

  @override
  Future<SessionMetrics?> getSessionMetrics(String sessionId) async =>
      sessionId == demoPhantomSessionId ? SessionMetrics.fromJson(await assets.metricsJson()) : null;

  @override
  Future<List<SessionEvent>> getSessionEvents(String sessionId) async =>
      sessionId == demoPhantomSessionId ? [for (final e in await assets.events()) SessionEvent(e)] : const [];

  @override
  Future<ReachTraceSet> getReachTraces(String sessionId) async => ReachTraceSet.empty;
}
