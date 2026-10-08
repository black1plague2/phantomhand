import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
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

final Expando<HubPhantomLiveRepository> _repoByConnection = Expando<HubPhantomLiveRepository>('phantomLiveRepoByConnection');

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
final ProviderFamily<PhantomLiveRepository?, String> phantomLiveRepositoryProvider =
    Provider.family<PhantomLiveRepository?, String>((ref, key) {
  if (key == mockPhantomSessionId) return MockPhantomLiveRepository();
  if (hubCapable) {
    ref.watch(hubControllerProvider); // rebuild when a headset connects / reports
    final hub = ref.read(hubControllerProvider.notifier);
    final HubConnection? connection =
        key.startsWith(_deviceKeyPrefix) ? hub.connectionFor(key.substring(_deviceKeyPrefix.length)) : hub.connectionForSession(key);
    if (connection != null && statusIsPhantom(connection.lastStatus)) {
      return _repoByConnection[connection] ??= HubPhantomLiveRepository(connection);
    }
  }
  return null;
});
