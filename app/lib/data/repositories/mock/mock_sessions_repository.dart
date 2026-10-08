import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';
import 'package:opus_app/data/repositories/sessions_repository.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

class MockSessionsRepository implements SessionsRepository {
  new({FixtureLoader? loader, this.errorInjector})
      : _loader = loader ?? const FixtureLoader();

  final FixtureLoader _loader;
  final MockErrorInjector? errorInjector;

  /// sessionId -> fixture key (e.g. `healthy`), built lazily the first time
  /// any session is requested, then reused.
  final Map<String, String> _idToKey = {};
  final Map<String, SessionEnvelope> _envelopeCache = {};
  bool _indexed = false;

  Future<void> _ensureIndexed() async {
    if (_indexed) return;
    final keys = await _loader.sessionKeys();
    for (final key in keys) {
      final json = await _loader.sessionEnvelopeJson(key);
      if (json == null) continue;
      final envelope = SessionEnvelope.fromJson(json);
      _envelopeCache[key] = envelope;
      _idToKey[envelope.sessionId] = key;
    }
    _indexed = true;
  }

  @override
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId) async {
    errorInjector?.maybeThrow('listSessionsForPatient');
    await _loader.latency();
    await _ensureIndexed();
    final sessions = _envelopeCache.values.where((s) => s.patientRef == patientId).toList()
      ..sort((a, b) => b.startedAt.compareTo(a.startedAt));
    return sessions;
  }

  @override
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId) async {
    errorInjector?.maybeThrow('getSessionEnvelope');
    await _loader.latency();
    await _ensureIndexed();
    final key = _idToKey[sessionId];
    return key == null ? null : _envelopeCache[key];
  }

  @override
  Future<SessionMetrics?> getSessionMetrics(String sessionId) async {
    errorInjector?.maybeThrow('getSessionMetrics');
    await _loader.latency();
    await _ensureIndexed();
    final key = _idToKey[sessionId];
    if (key == null) return null;
    final json = await _loader.sessionMetricsJson(key);
    return json == null ? null : SessionMetrics.fromJson(json);
  }

  @override
  Future<List<SessionEvent>> getSessionEvents(String sessionId) async {
    errorInjector?.maybeThrow('getSessionEvents');
    await _loader.latency();
    await _ensureIndexed();
    final key = _idToKey[sessionId];
    if (key == null) return const [];
    final raw = await _loader.sessionEventsNdjson(key);
    return raw.map(SessionEvent.new).toList();
  }

  @override
  Future<ReachTraceSet> getReachTraces(String sessionId) async {
    errorInjector?.maybeThrow('getReachTraces');
    await _loader.latency();
    await _ensureIndexed();
    final key = _idToKey[sessionId];
    if (key == null) return ReachTraceSet.empty;
    final json = await _loader.sessionTracesJson(key);
    return json == null ? ReachTraceSet.empty : ReachTraceSet.fromJson(json);
  }
}
