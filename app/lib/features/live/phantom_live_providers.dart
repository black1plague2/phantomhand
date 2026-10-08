import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/hub/hub_phantom_live_repository.dart';
import 'package:opus_app/data/repositories/mock/mock_phantom_live_repository.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:riverpod/src/providers/provider.dart';

/// `true` when a headset's last `status` says it runs a game that reports the
/// generic `game_state` (Phantom Hand): either `game_id` names it or the
/// status already carries a `game_state` object.
bool statusIsPhantom(Map<String, dynamic>? status) {
  if (status == null) return false;
  return status['game_id'] == 'phantom_hand' || status['game_state'] is Map;
}

const _deviceKeyPrefix = 'device:';

/// [phantomLiveRepositoryProvider] key for "the headset `deviceId`" instead of
/// "the session it is running": a headset has no session id before the first
/// Start and after a run has finished, which is when the card is needed most.
String phantomDeviceKey(String deviceId) => '$_deviceKeyPrefix$deviceId';

final Expando<PhantomLiveRepository> _repoByConnection = Expando<PhantomLiveRepository>('phantomLiveRepoByConnection');

/// How long after the card last heard from a headset a card on a NEW connection of
/// that headset still gets what the old card showed (witness numbers, closing
/// lines, markers). A reconnect takes seconds (backoff 0.5 to 8 s, plus discovery);
/// after a minute the participant may be another one, and the card starts empty.
/// A window of zero hands nothing on.
final Provider<Duration> phantomResumeWindowProvider = Provider<Duration>((ref) => const Duration(minutes: 1));

/// What the card showed of one headset, kept per device id so that it outlives the
/// connection it was shown on. A reconnect builds a new `HubConnection` (hub_server.dart,
/// `_onHello`), so a new repository and a new card, and the witness numbers and the
/// markers only ever arrive as trial_events: the headset replays them right after
/// hello_ack, before the new card is on screen (it needs the new connection's first
/// status, and the hub republishes its headset list only every 250 ms). The model is the
/// card's own, so it resets the way the card does (a new participant clears it).
class _DeviceMemory {
  final PhantomLiveModel model = PhantomLiveModel();

  /// `HubConnection.activeSessionId` while the model was fed: the one way a session id
  /// reaches the card side (the snapshots carry none).
  String? sessionId;
  DateTime at = DateTime.now();
}

/// The memory holds at most this many headsets (see [_RememberingRepository._remember]).
/// A clinic has a handful; the cap only bites when device ids keep changing.
const _maxRememberedDevices = 8;

final Provider<Map<String, _DeviceMemory>> _deviceMemoryProvider = Provider<Map<String, _DeviceMemory>>((ref) => {});

/// The repository of one connection, plus a memory of its device: what the cards of the
/// device showed so far is handed to a new card as its first snapshot, before the
/// connection's own state (the same idea as the status a card gets from
/// `connection.lastStatus` when it attaches). Events that arrive again afterwards (the
/// headset's replay) are taken once: a marker of the same kind within 1 ms is one marker,
/// a witness replaces the witness.
class _RememberingRepository implements PhantomLiveRepository {
  new(this._inner, this._connection, this._memories, this._window) {
    _out = StreamController<PhantomLiveSnapshot>.broadcast(onListen: _attach, onCancel: _detach);
  }

  final HubPhantomLiveRepository _inner;
  final HubConnection _connection;
  final Map<String, _DeviceMemory> _memories;
  final Duration _window;
  late final StreamController<PhantomLiveSnapshot> _out;
  StreamSubscription<PhantomLiveSnapshot>? _live;

  @override
  Stream<PhantomLiveSnapshot> watch() => _out.stream;

  /// The first card to listen (a card on a new connection, or one coming back after every
  /// card of this connection left) gets the seed; then ONE subscription to the connection's
  /// repository serves all the cards, so that every snapshot reaches the memory once.
  /// A card that joins while another is listening gets neither (as before).
  void _attach() {
    final seed = _seed();
    if (seed != null) _out.add(seed);
    _live = _inner.watch().listen((s) {
      _remember(s);
      _out.add(s);
    });
  }

  void _detach() {
    unawaited(_live?.cancel());
    _live = null;
  }

  @override
  Future<bool> send(PhantomCommand command, {Map<String, dynamic>? params}) => _inner.send(command, params: params);

  /// What the device's previous card showed, or null: nothing to hand on, it is older than
  /// the window, or the headset is in another session now (`activeSessionId` differs from
  /// the one the memory was fed in; an old participant's witness must never reach a new one).
  PhantomLiveSnapshot? _seed() {
    final device = _connection.deviceId;
    final memory = _memories[device];
    if (memory == null) return null;
    final session = _connection.activeSessionId;
    final otherSession = session != null && memory.sessionId != null && session != memory.sessionId;
    if (otherSession || DateTime.now().difference(memory.at) >= _window) {
      _memories.remove(device);
      return null;
    }
    final model = memory.model;
    final last = model.latest;
    if (last == null || (model.witness == null && model.buffer.markers.isEmpty)) return null;
    // The run state and phase of the old card ride along so that the card's own rule
    // (a headset waiting again after a run means a new participant) still applies to
    // whatever the new connection reports next.
    return PhantomLiveSnapshot(
      runState: last.runState,
      connected: true,
      game: last.game,
      markers: model.buffer.markers,
      witness: model.witness,
    );
  }

  void _remember(PhantomLiveSnapshot snapshot) {
    final device = _connection.deviceId;
    final now = DateTime.now();
    // Bounded: a device not heard from within the window is dropped here (and in [_seed]), and
    // there are never more than _maxRememberedDevices of them (the one heard from longest ago
    // goes). Each is one model: 20 s of trace, the markers inside it, one witness.
    _memories.removeWhere((id, m) => id != device && now.difference(m.at) >= _window);
    var memory = _memories[device] ??= _DeviceMemory();
    final session = _connection.activeSessionId;
    if (session != null && memory.sessionId != null && session != memory.sessionId) {
      memory = _memories[device] = _DeviceMemory(); // another session: nothing of the old one carries over
    }
    if (session != null) memory.sessionId = session;
    memory
      ..at = now
      ..model.apply(snapshot);
    if (_memories.length > _maxRememberedDevices) {
      final others = _memories.entries.where((e) => e.key != device);
      _memories.remove(others.reduce((a, b) => a.value.at.isBefore(b.value.at) ? a : b).key);
    }
  }
}

/// The connected headset whose last status reports Phantom Hand state (the
/// first one if several), or `null`. Unlike a session lookup this does not need
/// a session id, so it holds before the first Start and after a run.
String? phantomDeviceId(HubControllerState hub) {
  if (hub is! HubRunning) return null;
  for (final h in hub.headsets) {
    if (h.connected && statusIsPhantom(h.status)) return h.deviceId;
  }
  return null;
}

/// The operator-card repository for `key`, or `null` when it is an ordinary
/// trial-based one. `key` is the scripted demo ([mockPhantomSessionId]), a
/// headset ([phantomDeviceKey]) or a session id: the connected headset behind
/// it when it reports Phantom Hand state. One repository per connection (the
/// hub state republishes several times a second, and a new instance would drop
/// the accumulated traces, the same lesson as `effectiveLiveRepositoryProvider`).
/// A card on a new connection of a headset it has seen before starts from what the
/// previous card showed (see [_RememberingRepository]).
final ProviderFamily<PhantomLiveRepository?, String> phantomLiveRepositoryProvider =
    Provider.family<PhantomLiveRepository?, String>((ref, key) {
  if (key == mockPhantomSessionId) return MockPhantomLiveRepository();
  if (hubCapable) {
    ref.watch(hubControllerProvider); // rebuild when a headset connects / reports
    final hub = ref.read(hubControllerProvider.notifier);
    final HubConnection? connection =
        key.startsWith(_deviceKeyPrefix) ? hub.connectionFor(key.substring(_deviceKeyPrefix.length)) : hub.connectionForSession(key);
    if (connection != null && statusIsPhantom(connection.lastStatus)) {
      return _repoByConnection[connection] ??= _RememberingRepository(
        HubPhantomLiveRepository(connection),
        connection,
        ref.read(_deviceMemoryProvider),
        ref.read(phantomResumeWindowProvider),
      );
    }
  }
  return null;
});
