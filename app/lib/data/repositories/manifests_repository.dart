import 'package:opus_app/data/models/game_manifest.dart';

/// Phase 3: manifests are served by the backend (ARCHITECTURE.md §2). On
/// mocks they're bundled fixtures copied from `contracts/fixtures/*.manifest.json`.
abstract class ManifestsRepository {
  Future<List<GameManifest>> listManifests();
  Future<GameManifest?> getManifest(String gameId);
}
