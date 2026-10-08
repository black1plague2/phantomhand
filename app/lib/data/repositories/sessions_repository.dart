import 'package:opus_app/data/models/metrics.dart';
import 'package:opus_app/data/models/session_envelope.dart';
import 'package:opus_app/shared/widgets/reach_trace/reach_trace.dart';

/// A trial event, one line of `events.ndjson`
/// (`contracts/schemas/event.schema.json`). Kept as a thin typed wrapper over
/// the raw map rather than a full freezed model since `data` is explicitly
/// game-specific/open-ended in the schema.
class SessionEvent {
  new(this.raw)
      : tMs = (raw['t_ms'] as num).toDouble(),
        seq = raw['seq'] as int,
        block = raw['block'] as int,
        trial = raw['trial'] as int?,
        type = raw['type'] as String,
        hand = raw['hand'] as String?,
        outcome = raw['outcome'] as String?,
        target = (raw['target'] as Map?)?.cast<String, dynamic>();

  final Map<String, dynamic> raw;
  final double tMs;
  final int seq;
  final int block;
  final int? trial;
  final String type;
  final String? hand;
  final String? outcome;
  final Map<String, dynamic>? target;
}

/// All methods are addressed by `session_id` (the real
/// `SessionEnvelope.sessionId` UUID), matching how Phase 3's REST API will
/// address sessions -- the mock implementation hides its fixture-file naming
/// (`healthy`, `longitudinal__week_03__session_0`, ...) behind an internal
/// index built from `listSessionsForPatient`/`listAllSessions`.
abstract class SessionsRepository {
  Future<List<SessionEnvelope>> listSessionsForPatient(String patientId);
  Future<SessionEnvelope?> getSessionEnvelope(String sessionId);
  Future<SessionMetrics?> getSessionMetrics(String sessionId);
  Future<List<SessionEvent>> getSessionEvents(String sessionId);

  /// The real, decimated wrist-path traces for [sessionId]'s trials, that
  /// `ReachTraceGlyph` (`docs/APP_DESIGN.md`) draws. Empty when the session
  /// has no kinematics chunks to decimate.
  Future<ReachTraceSet> getReachTraces(String sessionId);
}
