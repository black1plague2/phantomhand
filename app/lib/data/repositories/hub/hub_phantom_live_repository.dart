import 'dart:async';

import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/live_message.dart' as hub;
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';

/// [PhantomLiveRepository] over one connected headset's [HubConnection]:
/// turns `status` (with `game_state` + `trace`) and `trial_event`
/// (`threat_impact`, `emg_burst`, `witness_summary`) into
/// [PhantomLiveSnapshot]s, and sends the operator commands as `requires_ack`
/// `command` messages.
class HubPhantomLiveRepository implements PhantomLiveRepository {
  HubPhantomLiveRepository(this.connection) {
    _controller = StreamController<PhantomLiveSnapshot>.broadcast(onListen: _attach, onCancel: _detach);
  }

  final HubConnection connection;
  late final StreamController<PhantomLiveSnapshot> _controller;
  StreamSubscription<Map<String, dynamic>>? _statusSub;
  StreamSubscription<Map<String, dynamic>>? _eventSub;
  StreamSubscription<void>? _discSub;
  PhantomRunState _runState = PhantomRunState.idle;
  PhantomGameState? _game;
  bool _connected = true;

  /// When the latest status was handed to us (also the replayed `lastStatus`
  /// on attach, so a card opened onto a silent link turns stale within seconds).
  DateTime? _statusAt;

  @override
  Stream<PhantomLiveSnapshot> watch() => _controller.stream;

  void _attach() {
    _statusSub ??= connection.statusStream.listen(_onStatus);
    _eventSub ??= connection.trialEventStream.listen(_onEvent);
    _discSub ??= connection.onDisconnected.listen((_) {
      _connected = false;
      _emit();
    });
    final last = connection.lastStatus;
    if (last != null) {
      _onStatus(last);
    } else {
      _emit();
    }
  }

  void _detach() {
    unawaited(_statusSub?.cancel());
    unawaited(_eventSub?.cancel());
    unawaited(_discSub?.cancel());
    _statusSub = null;
    _eventSub = null;
    _discSub = null;
  }

  void _onStatus(Map<String, dynamic> payload) {
    _statusAt = DateTime.now();
    _runState = PhantomRunState.parse(payload['state']);
    _game = PhantomGameState.tryParse(payload['game_state']) ?? _game;
    final order = (payload['params'] as Map?)?['condition_order'];
    _emit(chunk: TraceChunk.tryParse(payload['trace']), order: order is String ? order : null);
  }

  void _onEvent(Map<String, dynamic> payload) {
    final witness = PhantomWitness.tryParseEvent(payload);
    if (witness != null) {
      _emit(witness: witness);
      return;
    }
    final marker = TraceMarker.fromEvent(payload);
    if (marker != null) _emit(markers: [marker]);
  }

  void _emit({TraceChunk? chunk, List<TraceMarker> markers = const [], PhantomWitness? witness, String? order}) {
    if (_controller.isClosed) return;
    _controller.add(
      PhantomLiveSnapshot(
        runState: _runState,
        connected: _connected,
        game: _game,
        chunk: chunk,
        markers: markers,
        witness: witness,
        conditionOrder: order,
        rttMs: connection.lastRtt?.inMilliseconds,
        receivedAt: _statusAt,
      ),
    );
  }

  @override
  Future<bool> send(PhantomCommand command, {Map<String, dynamic>? params}) {
    if (!_connected) return Future.value(false);
    return connection.sendAndAwaitAck(
      hub.LiveMessage.command(
        connection.nextSeq,
        sessionId: connection.activeSessionId ?? '',
        command: command.wire,
        params: params,
      ),
    );
  }

  Future<void> dispose() async {
    _detach();
    await _controller.close();
  }
}
