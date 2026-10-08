import 'package:opus_app/data/models/phantom_live.dart';

/// Source of the generic game-state operator card (FR-AP-01): a stream of
/// [PhantomLiveSnapshot]s plus the operator commands. Backed by the real hub
/// connection (`HubPhantomLiveRepository`) or by a scripted mock session
/// (`MockPhantomLiveRepository`) so the UI runs with no hub.
abstract class PhantomLiveRepository {
  /// Snapshots as the headset reports them (status <= 5 Hz). Cancelling the
  /// last subscription releases any timers/streams.
  Stream<PhantomLiveSnapshot> watch();

  /// Sends one operator command and resolves `true` when the headset acks it
  /// with `ok`, `false` on a rejected ack, a timeout or a lost connection.
  /// [params] is only used by [PhantomCommand.conditionOrder]
  /// (`{"condition_order": "sync_first" | "async_first"}`).
  Future<bool> send(PhantomCommand command, {Map<String, dynamic>? params});
}
