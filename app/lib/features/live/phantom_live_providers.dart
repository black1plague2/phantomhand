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

final Expando<HubPhantomLiveRepository> _repoByConnection = Expando<HubPhantomLiveRepository>('phantomLiveRepoByConnection');

/// The operator-card repository for [sessionId], or `null` when this session
/// is an ordinary trial-based one: the scripted demo for
/// [mockPhantomSessionId], or the connected headset running that session when
/// it reports Phantom Hand state. One repository per connection (the hub
/// state republishes several times a second, and a new instance would drop
/// the accumulated traces, the same lesson as `effectiveLiveRepositoryProvider`).
final ProviderFamily<PhantomLiveRepository?, String> phantomLiveRepositoryProvider =
    Provider.family<PhantomLiveRepository?, String>((ref, sessionId) {
  if (sessionId == mockPhantomSessionId) return MockPhantomLiveRepository();
  if (hubCapable) {
    ref.watch(hubControllerProvider); // rebuild when a headset connects / reports
    final HubConnection? connection = ref.read(hubControllerProvider.notifier).connectionForSession(sessionId);
    if (connection != null && statusIsPhantom(connection.lastStatus)) {
      return _repoByConnection[connection] ??= HubPhantomLiveRepository(connection);
    }
  }
  return null;
});
