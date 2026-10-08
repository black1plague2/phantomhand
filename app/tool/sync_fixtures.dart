// Copies contracts/ fixtures into app/assets/fixtures so the mock repositories
// (lib/data/repositories/mock/*) can load them as Flutter assets.
//
// Run from the `app/` directory:
//   dart run tool/sync_fixtures.dart
//
// Source of truth is always `contracts/fixtures/`; this script never writes there.
// Safe to re-run any time (e.g. after the analytics/sim agent adds more synthetic
// sessions) -- it wipes and rebuilds assets/fixtures/ from the current source tree.
//
// Layout choice: Flutter's pubspec `assets:` directory entries are NOT recursive
// (a declared folder only pulls in files directly inside it, not sub-folders), and
// contracts/fixtures/sessions/ nests up to 3 levels deep (e.g.
// sessions/longitudinal/week_03/session_0/session.json). Rather than hand-list every
// nested folder in pubspec.yaml (fragile as the sim agent adds more synthetic
// patients/weeks), this script FLATTENS each session into one directory
// (assets/fixtures/sessions/) using `__`-joined filenames, e.g.:
//   sessions/healthy/session.json                      -> healthy__session.json
//   sessions/longitudinal/week_03/session_0/metrics.json -> longitudinal__week_03__session_0__metrics.json
// Only session.json, metrics.json, events.ndjson and summary.json are copied
// verbatim -- the app renders computed metrics/trial events, not raw
// kinematics frames, so full kin_*.json chunks are never bundled (they run
// 80-130 KB each x 3-4 per session). Instead, for every session this script
// DECIMATES the real wrist paths per trial (~40 points each) straight out of
// kin_*.json + events.ndjson into one small `${key}__traces.json` -- this is
// the data `ReachTraceGlyph` (docs/APP_DESIGN.md) draws: real patient paths,
// not synthetic decoration. `truth.json` (the sim's internal ground truth) is
// never bundled.
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

const _keepFiles = {'session.json', 'metrics.json', 'events.ndjson', 'summary.json'};
const _tracePointsPerTrial = 40;

void main() {
  final appDir = Directory.current;
  final repoRoot = Directory(appDir.path).parent;
  final contractsFixtures = Directory('${repoRoot.path}/contracts/fixtures');

  if (!contractsFixtures.existsSync()) {
    stderr.writeln(
      'sync_fixtures: ${contractsFixtures.path} not found. '
      'Run this from app/ inside the OPUS repo.',
    );
    exitCode = 1;
    return;
  }

  final sourceSessions = Directory('${contractsFixtures.path}/sessions');
  final destSessions = Directory('${appDir.path}/assets/fixtures/sessions');
  final destManifests = Directory('${appDir.path}/assets/fixtures/manifests');

  if (destSessions.existsSync()) destSessions.deleteSync(recursive: true);
  destSessions.createSync(recursive: true);
  if (destManifests.existsSync()) destManifests.deleteSync(recursive: true);
  destManifests.createSync(recursive: true);

  var sessionFileCount = 0;
  var traceCount = 0;
  if (sourceSessions.existsSync()) {
    for (final entity in sourceSessions.listSync(recursive: true)) {
      if (entity is! File) continue;
      final name = entity.uri.pathSegments.last;
      if (!_keepFiles.contains(name)) continue;
      final rel = entity.path
          .substring(sourceSessions.path.length + 1)
          .replaceAll(r'\', '/');
      final flatName = rel.replaceAll('/', '__');
      entity.copySync('${destSessions.path}/$flatName');
      sessionFileCount++;
    }

    // Second pass: every directory that has a session.json is one leaf
    // session -- build its reach-trace fixture there.
    for (final entity in sourceSessions.listSync(recursive: true)) {
      if (entity is! File || entity.uri.pathSegments.last != 'session.json') continue;
      final sessionDir = entity.parent;
      final rel = sessionDir.path
          .substring(sourceSessions.path.length + 1)
          .replaceAll(r'\', '/');
      final flatKey = rel.replaceAll('/', '__');
      final traces = _buildTraces(sessionDir);
      if (traces != null) {
        File('${destSessions.path}/${flatKey}__traces.json').writeAsStringSync(jsonEncode(traces));
        traceCount++;
      }
    }
  } else {
    stdout.writeln('sync_fixtures: ${sourceSessions.path} missing, skipping sessions.');
  }

  // Manifests live loose at the top of contracts/fixtures/ (e.g. orchard_reach.manifest.json).
  var manifestCount = 0;
  for (final entity in contractsFixtures.listSync()) {
    if (entity is File && entity.path.endsWith('.manifest.json')) {
      final name = entity.uri.pathSegments.last;
      entity.copySync('${destManifests.path}/$name');
      manifestCount++;
    }
  }

  // An index so the mock repositories can discover session fixture keys without
  // listing the asset bundle (Flutter's AssetManifest lookup is awkward at runtime).
  final sessionKeys = destSessions
      .listSync()
      .whereType<File>()
      .map((f) => f.uri.pathSegments.last)
      .where((n) => n.endsWith('__session.json'))
      .map((n) => n.substring(0, n.length - '__session.json'.length))
      .toList()
    ..sort();
  File('${destSessions.path}/_index.json')
      .writeAsStringSync(jsonEncode(sessionKeys));

  stdout.writeln(
    'sync_fixtures: done. $sessionFileCount session files '
    '(${sessionKeys.length} sessions), $manifestCount manifest files, '
    '$traceCount reach-trace files copied into ${appDir.path}/assets/fixtures/',
  );
}

/// Reconstructs the session-global wrist track by concatenating `kin_*.json`
/// chunks in `seq` order. Each chunk's `t_ms` is already session-global (e.g.
/// `kin_000.json` runs 0..~5000, `kin_001.json` runs ~5000..~10000, etc. --
/// verified directly against the synthetic fixtures, since `contracts/schemas/
/// kinematics-chunk.schema.json` doesn't say either way), so chunks are simply
/// concatenated in order with no added offset. Then slices out each trial's
/// window (from `events.ndjson` `trial_start`/`trial_end`, using the tracked
/// hand's wrist joint) and decimates it to `_tracePointsPerTrial`
/// evenly-spaced `[x, z]` points (x = lateral, z = forward -- the schema's
/// calibration-space axes, i.e. the top-down plane `ReachTraceGlyph` draws).
/// Returns `null` when the session has no `chunks` to read (nothing to
/// decimate).
Map<String, dynamic>? _buildTraces(Directory sessionDir) {
  final sessionFile = File('${sessionDir.path}/session.json');
  if (!sessionFile.existsSync()) return null;
  final session = jsonDecode(sessionFile.readAsStringSync()) as Map<String, dynamic>;
  final chunkCount = session['chunks'] as int? ?? 0;
  if (chunkCount == 0) return null;

  // Concatenate every joint's [t_ms(global), x, z] samples across chunks.
  final track = <String, List<List<double>>>{}; // joint -> [[tGlobal, x, z], ...]
  for (var seq = 0; seq < chunkCount; seq++) {
    final chunkFile = File('${sessionDir.path}/kin_${seq.toString().padLeft(3, '0')}.json');
    if (!chunkFile.existsSync()) continue;
    final chunk = jsonDecode(chunkFile.readAsStringSync()) as Map<String, dynamic>;
    final tMs = (chunk['t_ms'] as List).cast<num>();
    final frames = (chunk['frames'] as Map).cast<String, dynamic>();
    for (final joint in const ['l_wrist', 'r_wrist']) {
      final jointFrames = frames[joint] as Map<String, dynamic>?;
      if (jointFrames == null) continue;
      final pos = (jointFrames['pos'] as List).cast<List<dynamic>>();
      final list = track.putIfAbsent(joint, () => []);
      for (var i = 0; i < pos.length && i < tMs.length; i++) {
        final p = pos[i].cast<num>();
        list.add([tMs[i].toDouble(), p[0].toDouble(), p[2].toDouble()]);
      }
    }
  }
  if (track.isEmpty) return null;

  final eventsFile = File('${sessionDir.path}/events.ndjson');
  if (!eventsFile.existsSync()) return null;
  final events = eventsFile
      .readAsLinesSync()
      .where((l) => l.trim().isNotEmpty)
      .map((l) => jsonDecode(l) as Map<String, dynamic>)
      .toList();

  final trialIndices = events
      .where((e) => e['type'] == 'trial_start')
      .map((e) => e['trial'] as int)
      .toSet()
      .toList()
    ..sort();

  final calibration = session['calibration'] as Map<String, dynamic>?;
  final armLength = (calibration?['arm_length_m'] as Map?)?.cast<String, dynamic>();
  final workspaceRadius = [
    (armLength?['left'] as num?)?.toDouble() ?? 0.6,
    (armLength?['right'] as num?)?.toDouble() ?? 0.6,
  ].reduce(math.max);

  final trials = <Map<String, dynamic>>[];
  for (final trial in trialIndices) {
    final start = events.firstWhere((e) => e['type'] == 'trial_start' && e['trial'] == trial);
    final end = events.firstWhere(
      (e) => e['type'] == 'trial_end' && e['trial'] == trial,
      orElse: () => const {},
    );
    if (end.isEmpty) continue;
    final tStart = (start['t_ms'] as num).toDouble();
    final tEnd = (end['t_ms'] as num).toDouble();

    // The hand is carried on movement_onset/contact events for this trial,
    // not trial_start/trial_end -- fall back to 'right' if truly absent.
    final handEvent = events.firstWhere(
      (e) => e['trial'] == trial && e['hand'] != null,
      orElse: () => const {},
    );
    final hand = (handEvent['hand'] as String?) ?? 'right';
    final joint = hand == 'left' ? 'l_wrist' : 'r_wrist';

    final samples = (track[joint] ?? [])
        .where((s) => s[0] >= tStart && s[0] <= tEnd)
        .toList();
    if (samples.isEmpty) continue;

    final decimated = _decimate(samples, _tracePointsPerTrial);
    trials.add({
      'trial': trial,
      'hand': hand,
      'outcome': end['outcome'] as String? ?? 'unknown',
      // Round to 4dp (0.1 mm at this scale) -- plenty for a glyph, keeps the file small.
      'points': decimated.map((s) => [_round4(s[1]), _round4(s[2])]).toList(),
    });
  }
  if (trials.isEmpty) return null;

  return {'workspaceRadiusM': _round4(workspaceRadius), 'trials': trials};
}

double _round4(double v) => (v * 10000).round() / 10000;

/// Picks up to [count] evenly-spaced samples from [samples] (already
/// time-ordered), always keeping the first and last point of the reach.
List<List<double>> _decimate(List<List<double>> samples, int count) {
  if (samples.length <= count) return samples;
  final result = <List<double>>[];
  for (var i = 0; i < count; i++) {
    final idx = (i * (samples.length - 1) / (count - 1)).round();
    result.add(samples[idx]);
  }
  return result;
}
