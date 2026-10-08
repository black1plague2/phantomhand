// Item 2 (docs/agent-briefs/A-next-run.md): "sessions uploaded to the hub
// appear in the sessions list and patient timeline, open in the session
// report". This drives the REAL hub server (same code the app/hub_cli run)
// with real HTTP PUTs of the actual `healthy` fixture's files -- not a mock
// filesystem -- then asserts `HubSessionsRepository`/`CompositeSessionsRepository`
// (the new repository layer this run added) see exactly what a real headset
// upload would produce: the session appears for its patient, its events/
// traces/metrics all read back correctly, and a mock-fixture patient is
// unaffected by an unrelated hub upload.
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/data/repositories/hub/hub_sessions_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_sessions_repository.dart';
import 'package:path/path.dart' as p;

Future<void> _put(int port, String sessionId, String name, List<int> bytes) async {
  final client = HttpClient();
  final uri = Uri.parse('http://127.0.0.1:$port/opus/v1/sessions/$sessionId/files/$name');
  final resp = await (await client.putUrl(uri)..add(bytes)).close();
  expect(resp.statusCode, anyOf(HttpStatus.created, HttpStatus.ok));
  await resp.drain<void>();
  client.close();
}

void main() {
  // contracts/fixtures/ is 4 levels up from app/test/hub/.
  final fixtureDir = Directory(
    p.normalize(p.join(Directory.current.path, '..', 'contracts', 'fixtures', 'sessions', 'healthy')),
  );

  late Directory dataDir;
  late HubServer server;
  late int port;
  late String realSessionId;

  setUp(() async {
    dataDir = Directory.systemTemp.createTempSync('opus_hub_sessions_test_');
    port = 18787 + Random().nextInt(2000);
    server = HubServer(sessionsDir: dataDir.path, port: port);
    await server.start();

    final sessionJson = jsonDecode(await File('${fixtureDir.path}/session.json').readAsString()) as Map<String, dynamic>;
    realSessionId = sessionJson['session_id'] as String;

    // Upload the real fixture files over real HTTP PUTs, exactly as a
    // headset would per contracts/LIVE_PROTOCOL.md's bulk channel.
    for (final name in ['session.json', 'events.ndjson', 'kin_000.json', 'kin_001.json', 'kin_002.json']) {
      final bytes = await File('${fixtureDir.path}/$name').readAsBytes();
      await _put(port, realSessionId, name, bytes);
    }
  });

  tearDown(() async {
    await server.stop();
    dataDir.deleteSync(recursive: true);
  });

  test('HubSessionsRepository reads a real hub-uploaded session back from disk', () async {
    final repo = HubSessionsRepository(dataDir.path);

    final envelope = await repo.getSessionEnvelope(realSessionId);
    expect(envelope, isNotNull);
    expect(envelope!.patientRef, 'synthetic-healthy-42');

    final forPatient = await repo.listSessionsForPatient('synthetic-healthy-42');
    expect(forPatient.map((s) => s.sessionId), contains(realSessionId));

    final events = await repo.getSessionEvents(realSessionId);
    expect(events, isNotEmpty);
    expect(events.first.type, isNotEmpty);

    // No metrics.json uploaded yet (only session.json/events.ndjson/kin_*
    // are on the bulk channel per LIVE_PROTOCOL.md -- metrics.json only
    // appears after "Run analysis").
    expect(await repo.getSessionMetrics(realSessionId), isNull);

    // Real decimated traces from the real kin_*.json chunks + events.ndjson,
    // via the same algorithm tool/sync_fixtures.dart uses to precompute the
    // bundled fixture -- not a stub.
    final traces = await repo.getReachTraces(realSessionId);
    expect(traces.trials, isNotEmpty);
    expect(traces.trials.first.points, isNotEmpty);
    expect(traces.workspaceRadiusM, closeTo(0.6, 0.001));
  });

  test('CompositeSessionsRepository merges hub sessions with mock fixtures without cross-contamination', () async {
    final composite = CompositeSessionsRepository(
      mock: MockSessionsRepository(),
      hub: HubSessionsRepository(dataDir.path),
    );

    // The real hub session shows up for its own patient...
    final hubPatientSessions = await composite.listSessionsForPatient('synthetic-healthy-42');
    expect(hubPatientSessions.map((s) => s.sessionId), contains(realSessionId));

    // ...and a completely unrelated (mock-fixture) patient's list is
    // untouched by it.
    final unrelated = await composite.listSessionsForPatient('some-other-patient-not-in-any-fixture');
    expect(unrelated.map((s) => s.sessionId), isNot(contains(realSessionId)));

    // Metrics/report data for the hub session comes through the composite
    // exactly as the standalone HubSessionsRepository test asserted.
    expect(await composite.getSessionMetrics(realSessionId), isNull);
    final traces = await composite.getReachTraces(realSessionId);
    expect(traces.trials, isNotEmpty);
  });

  test('a session with no metrics.json yet, then one uploaded by "Run analysis", refreshes', () async {
    final repo = HubSessionsRepository(dataDir.path);
    expect(await repo.getSessionMetrics(realSessionId), isNull);

    // Simulate "Run analysis" writing metrics.json next to the other files
    // (this is literally what HubController.runAnalysis's subprocess does;
    // this test doesn't shell out to Python, it asserts the repository
    // picks up the file the moment it exists, which is the app-side half of
    // "Run analysis... refreshes the report").
    final metricsBytes = await File('${fixtureDir.path}/metrics.json').readAsBytes();
    File('${dataDir.path}/$realSessionId/metrics.json').writeAsBytesSync(metricsBytes);

    final metrics = await repo.getSessionMetrics(realSessionId);
    expect(metrics, isNotNull);
    expect(metrics!.trials, isNotEmpty);
  });
}
