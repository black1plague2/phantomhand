import 'package:opus_app/data/models/live_message.dart';

/// Stands in for the WS live channel (ARCHITECTURE.md §7) until Phase 3.
abstract class LiveRepository {
  /// Starts (or attaches to) a mock live session and returns a stream of
  /// status updates. Cancelling the subscription stops the mock generator.
  Stream<LiveMessage> watch(String sessionId, {int totalTrials});
  void pause(String sessionId);
  void resume(String sessionId);
  void stop(String sessionId);
}
