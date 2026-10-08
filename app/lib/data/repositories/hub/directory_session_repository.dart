import 'dart:convert';
import 'dart:io';

import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';
import 'package:opus_app/shared/widgets/reach_trace/trace_builder.dart';

/// A [SessionsRepository] backed by exactly one on-disk session directory
/// (`session.json`, `events.ndjson`, `kin_###.json`, optionally
/// `metrics.json`) -- as opposed to [HubSessionsRepository]'s `sessionsDir`,
/// which is the *parent* of many session-id subfolders belonging to the
/// app's own currently-running hub.
///
/// This is what backs "Open a session folder..." (Devices screen): the hub
/// stores every session it receives under a directory like
/// `app/.hub_data/<session-id>/` (the default for `tool/hub_cli.dart`, and
/// where a full three-way pipeline run -- Unity headset + hub_cli + the
/// haptic simulator -- leaves its output), but the *running Flutter app's*
/// own `HubController` only ever looks inside whatever directory its own
/// `HubServer` was started against
/// (`getApplicationSupportDirectory()/sessions`, only populated once this
/// app instance's hub has been started and a headset has connected to it).
/// A session a clinician received in a different run, or via the CLI hub
/// used for testing, is otherwise invisible to the app. Pointing this
/// repository straight at that one directory lets the existing session
/// report screen render it via the same `SessionsRepository` contract every
/// other screen already relies on -- no new screen, no new data model.
class DirectorySessionsRepository implements SessionsRepository {
  DirectorySessionsRepository._(this.dir, this._envelope);

  final Directory dir;
  final SessionEnvelope _envelope;

  /// The session this repository was opened against -- read once at
  /// [open], not re-read on every call, since a folder a clinician
  /// explicitly opened isn't expected to change identity under them (unlike
  /// a live hub's `sessionsDir`, which a `HubSessionsRepository` re-scans
  /// every call because new sessions can land in it at any time).
  SessionEnvelope get envelope => _envelope;

  /// Opens [path] as a session directory. Returns `null` -- rather than
  /// throwing -- if it doesn't look like one at all (no `session.json`, or
  /// it fails to parse), so the caller can show "Not a valid session
  /// folder" instead of crashing on a clinician's typo or a folder picked by
  /// mistake.
  static Future<DirectorySessionsRepository?> open(String path) async {
    final dir = Directory(path);
    final file = File('${dir.path}/session.json');
    if (!dir.existsSync() || !file.existsSync()) return null;
    try {
      final json = jsonDecode(await file.readAsString()) as Map<String, dynamic>;
      final envelope = SessionEnvelope.fromJson(json);
      return DirectorySessionsRepository._(dir, envelope);
    } catch (_) {
      return null;
    }
  }

  @override
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId) async =>
      _envelope.patientRef == patientId ? [_envelope] : const [];

  @override
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId) async =>
      sessionId == _envelope.sessionId ? _envelope : null;

  @override
  Future<SessionMetrics?> getSessionMetrics(String sessionId) async {
    if (sessionId != _envelope.sessionId) return null;
    final file = File('${dir.path}/metrics.json');
    if (!file.existsSync()) return null;
    try {
      return SessionMetrics.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }

  @override
  Future<List<SessionEvent>> getSessionEvents(String sessionId) async {
    if (sessionId != _envelope.sessionId) return const [];
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
    if (sessionId != _envelope.sessionId) return ReachTraceSet.empty;
    return buildReachTracesFromDirectory(dir);
  }

  /// The directory itself, for "Run analysis" (mirrors
  /// `HubController.sessionDirFor`) -- a session opened this way has no
  /// `HubController` to ask, since it may not belong to this app instance's
  /// own hub at all.
  String get sessionDirPath => dir.path;
}
