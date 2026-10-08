import 'dart:convert';
import 'dart:io';

import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';
import 'package:opus_app/shared/widgets/reach_trace/trace_builder.dart';

/// Real sessions a headset has uploaded to the hub (`docs/agent-briefs/
/// A-next-run.md` item 2: "sessions uploaded to the hub appear in the
/// sessions list and patient timeline"). Reads directly off disk under
/// `HubController.sessionsDir` -- one subdirectory per `session_id`
/// (`session.json`, `events.ndjson`, `kin_###.json`, optionally
/// `metrics.json` once "Run analysis" has produced it) -- exactly what
/// `HubServer._handleFileUpload` writes, so no separate index/cache is
/// needed; the filesystem *is* the store.
///
/// Re-scans the directory on every call rather than caching, since the hub
/// can receive new files at any time while the app is open -- these
/// sessions change during a run in a way the bundled mock fixtures never do.
class HubSessionsRepository implements SessionsRepository {
  new(this.sessionsDir);

  /// `HubController.sessionsDir` -- may not exist yet if no session has
  /// uploaded anything.
  final String sessionsDir;

  Iterable<Directory> _sessionDirs() {
    final root = Directory(sessionsDir);
    if (!root.existsSync()) return const [];
    return root.listSync().whereType<Directory>();
  }

  Future<SessionEnvelope?> _readEnvelope(Directory dir) async {
    final file = File('${dir.path}/session.json');
    if (!file.existsSync()) return null;
    try {
      final json = jsonDecode(await file.readAsString()) as Map<String, dynamic>;
      return SessionEnvelope.fromJson(json);
    } catch (_) {
      return null; // mid-upload / malformed -- treat as not-yet-visible.
    }
  }

  Directory? _dirForSession(String sessionId) {
    final dir = Directory('$sessionsDir/$sessionId');
    return dir.existsSync() ? dir : null;
  }

  @override
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId) async {
    final out = <SessionEnvelope>[];
    for (final dir in _sessionDirs()) {
      final envelope = await _readEnvelope(dir);
      if (envelope != null && envelope.patientRef == patientId) out.add(envelope);
    }
    out.sort((a, b) => b.startedAt.compareTo(a.startedAt));
    return out;
  }

  @override
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId) async {
    final dir = _dirForSession(sessionId);
    return dir == null ? null : _readEnvelope(dir);
  }

  @override
  Future<SessionMetrics?> getSessionMetrics(String sessionId) async {
    final dir = _dirForSession(sessionId);
    if (dir == null) return null;
    final file = File('${dir.path}/metrics.json');
    // Not uploaded by the headset (LIVE_PROTOCOL.md lists session.json,
    // events.ndjson, kin_###.json) -- it only exists once the clinician runs
    // "Run analysis" (`HubController.runAnalysis`), same file name/shape the
    // Devices screen's action produces.
    if (!file.existsSync()) return null;
    try {
      return SessionMetrics.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }

  @override
  Future<List<SessionEvent>> getSessionEvents(String sessionId) async {
    final dir = _dirForSession(sessionId);
    if (dir == null) return const [];
    final file = File('${dir.path}/events.ndjson');
    if (!file.existsSync()) return const [];
    final lines = await file.readAsLines();
    return lines
        .where((l) => l.trim().isNotEmpty)
        .map((l) => SessionEvent(jsonDecode(l) as Map<String, dynamic>))
        .toList();
  }

  @override
  Future<ReachTraceSet> getReachTraces(String sessionId) async {
    final dir = _dirForSession(sessionId);
    if (dir == null) return ReachTraceSet.empty;
    return buildReachTracesFromDirectory(dir);
  }
}

/// Merges the bundled mock/synthetic sessions with real hub-uploaded ones so
/// screens see one list regardless of source. Hub sessions are additive: a
/// `sessionId` that exists in both (shouldn't happen in practice -- mock
/// fixtures use fixture-derived ids, real sessions use the headset's own
/// UUID/id) prefers the hub copy, since it is the live, ground-truth one.
class CompositeSessionsRepository implements SessionsRepository {
  new({required this.mock, required this.hub, this.opened = const []});

  final SessionsRepository mock;

  /// `null` when the hub has never been started this run (desktop/Android
  /// only; always `null` on web) -- falls back to mock-only behavior.
  final HubSessionsRepository? hub;

  /// Session directories a clinician explicitly opened via "Open a session
  /// folder..." (Devices screen) -- independent of whether this app
  /// instance's own hub is running, since these came from somewhere else
  /// (`tool/hub_cli.dart`'s default `app/.hub_data/`, a pipeline run, a
  /// copied folder). Checked after [hub] so a live hub session always wins
  /// if the same id somehow appears in both.
  final List<SessionsRepository> opened;

  @override
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId) async {
    final mockSessions = await mock.listSessionsForPatient(patientId);
    final hubSessions = hub == null ? <SessionEnvelope>[] : await hub!.listSessionsForPatient(patientId);
    final openedSessions = <SessionEnvelope>[
      for (final o in opened) ...await o.listSessionsForPatient(patientId),
    ];
    final seen = <String>{};
    final merged = <SessionEnvelope>[];
    for (final s in [...hubSessions, ...openedSessions, ...mockSessions]) {
      if (seen.add(s.sessionId)) merged.add(s);
    }
    merged.sort((a, b) => b.startedAt.compareTo(a.startedAt));
    return merged;
  }

  @override
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId) async {
    final hubResult = await hub?.getSessionEnvelope(sessionId);
    if (hubResult != null) return hubResult;
    for (final o in opened) {
      final result = await o.getSessionEnvelope(sessionId);
      if (result != null) return result;
    }
    return mock.getSessionEnvelope(sessionId);
  }

  @override
  Future<SessionMetrics?> getSessionMetrics(String sessionId) async {
    final hubResult = await hub?.getSessionMetrics(sessionId);
    if (hubResult != null) return hubResult;
    for (final o in opened) {
      final result = await o.getSessionMetrics(sessionId);
      if (result != null) return result;
    }
    return mock.getSessionMetrics(sessionId);
  }

  @override
  Future<List<SessionEvent>> getSessionEvents(String sessionId) async {
    final hubEvents = await hub?.getSessionEvents(sessionId);
    if (hubEvents != null && hubEvents.isNotEmpty) return hubEvents;
    for (final o in opened) {
      final result = await o.getSessionEvents(sessionId);
      if (result.isNotEmpty) return result;
    }
    return mock.getSessionEvents(sessionId);
  }

  @override
  Future<ReachTraceSet> getReachTraces(String sessionId) async {
    final hubTraces = await hub?.getReachTraces(sessionId);
    if (hubTraces != null && hubTraces.trials.isNotEmpty) return hubTraces;
    for (final o in opened) {
      final result = await o.getReachTraces(sessionId);
      if (result.trials.isNotEmpty) return result;
    }
    return mock.getReachTraces(sessionId);
  }
}
