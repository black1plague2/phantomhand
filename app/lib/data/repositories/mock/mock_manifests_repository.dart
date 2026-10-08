import 'package:opus_app/data/models/game_manifest.dart';
import 'package:opus_app/data/repositories/manifests_repository.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';

/// Known game ids bundled under `assets/fixtures/manifests/` by
/// `tool/sync_fixtures.dart`. Update this list when a new game manifest is
/// added to `contracts/fixtures/*.manifest.json` -- Flutter has no runtime
/// "list files in this asset folder" API, so a small explicit registry here
/// is simpler than parsing `AssetManifest.json`.
class MockManifestsRepository implements ManifestsRepository {
  new({FixtureLoader? loader, this.errorInjector})
      : _loader = loader ?? const FixtureLoader();

  static const knownGameIds = ['orchard_reach', 'phantom_hand'];

  final FixtureLoader _loader;
  final MockErrorInjector? errorInjector;
  final Map<String, GameManifest> _cache = {};

  @override
  Future<List<GameManifest>> listManifests() async {
    errorInjector?.maybeThrow('listManifests');
    await _loader.latency();
    final result = <GameManifest>[];
    for (final id in knownGameIds) {
      final m = await _load(id);
      if (m != null) result.add(m);
    }
    return result;
  }

  @override
  Future<GameManifest?> getManifest(String gameId) async {
    errorInjector?.maybeThrow('getManifest');
    await _loader.latency();
    return _load(gameId);
  }

  Future<GameManifest?> _load(String gameId) async {
    if (_cache.containsKey(gameId)) return _cache[gameId];
    final json = await _loader.manifestJson(gameId);
    if (json == null) return null;
    final manifest = GameManifest.fromJson(json);
    _cache[gameId] = manifest;
    return manifest;
  }
}
