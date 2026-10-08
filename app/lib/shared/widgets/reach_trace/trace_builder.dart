import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

/// Builds a [ReachTraceSet] straight from a real session directory's
/// `session.json` + `events.ndjson` + `kin_###.json` chunks -- the same
/// decimation algorithm `tool/sync_fixtures.dart` uses to precompute the
/// bundled `__traces.json` fixtures, extracted here so
/// `HubSessionsRepository` (real sessions a headset just uploaded to the
/// hub) can produce the same `ReachTraceGlyph` data live, without a build
/// step. Unlike the fixture tool, this globs for `kin_*.json` files on disk
/// rather than trusting a `chunks` count field, since a real uploaded
/// session's `session.json` has no such field
/// (`contracts/schemas/session-envelope.schema.json`).
///
/// Returns [ReachTraceSet.empty] when the session has no kinematics chunks
/// yet (e.g. only `session.json` has landed so far) rather than throwing --
/// callers show "No reach data for this session" via [ReachTraceGlyph].
const tracePointsPerTrial = 40;

Future<ReachTraceSet> buildReachTracesFromDirectory(Directory sessionDir) async {
  if (!sessionDir.existsSync()) return ReachTraceSet.empty;

  final sessionFile = File('${sessionDir.path}/session.json');
  final eventsFile = File('${sessionDir.path}/events.ndjson');
  if (!sessionFile.existsSync() || !eventsFile.existsSync()) return ReachTraceSet.empty;

  final kinFiles = sessionDir
      .listSync()
      .whereType<File>()
      .where((f) => RegExp(r'kin_\d{3}\.json$').hasMatch(f.path))
      .toList()
    ..sort((a, b) => a.path.compareTo(b.path));
  if (kinFiles.isEmpty) return ReachTraceSet.empty;

  final track = <String, List<List<double>>>{};
  for (final chunkFile in kinFiles) {
    Map<String, dynamic> chunk;
    try {
      chunk = jsonDecode(await chunkFile.readAsString()) as Map<String, dynamic>;
    } catch (_) {
      continue; // partially-written / mid-upload file -- skip, try next poll.
    }
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
  if (track.isEmpty) return ReachTraceSet.empty;

  final session = jsonDecode(await sessionFile.readAsString()) as Map<String, dynamic>;
  final events = (await eventsFile.readAsLines())
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

  final trials = <ReachTrace>[];
  for (final trial in trialIndices) {
    final start = events.firstWhere((e) => e['type'] == 'trial_start' && e['trial'] == trial);
    final end = events.firstWhere(
      (e) => e['type'] == 'trial_end' && e['trial'] == trial,
      orElse: () => const {},
    );
    if (end.isEmpty) continue;
    final tStart = (start['t_ms'] as num).toDouble();
    final tEnd = (end['t_ms'] as num).toDouble();

    final handEvent = events.firstWhere(
      (e) => e['trial'] == trial && e['hand'] != null,
      orElse: () => const {},
    );
    final hand = (handEvent['hand'] as String?) ?? 'right';
    final joint = hand == 'left' ? 'l_wrist' : 'r_wrist';

    final samples = (track[joint] ?? []).where((s) => s[0] >= tStart && s[0] <= tEnd).toList();
    if (samples.isEmpty) continue;

    final decimated = _decimate(samples, tracePointsPerTrial);
    trials.add(
      ReachTrace(
        trial: trial,
        hand: hand,
        outcome: end['outcome'] as String? ?? 'unknown',
        points: decimated.map((s) => ReachTracePoint(s[1], s[2])).toList(),
      ),
    );
  }

  return ReachTraceSet(workspaceRadiusM: workspaceRadius, trials: trials);
}

List<List<double>> _decimate(List<List<double>> samples, int count) {
  if (samples.length <= count) return samples;
  final step = (samples.length - 1) / (count - 1);
  return [for (var i = 0; i < count; i++) samples[(i * step).round()]];
}
