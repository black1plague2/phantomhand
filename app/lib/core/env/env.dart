/// Build-time / runtime environment flags for the mock-backed Phase 1 app.
///
/// Phase 3 swaps `mockMode` off and points repositories at the generated
/// OpenAPI client instead of `data/repositories/mock/`. Nothing in
/// `features/` should read this directly -- go through the repository
/// providers in `core/router` / `data/repositories`.
class Env {
  const new _();

  /// Always true in Track A. The Phase 3 agent flips this once the FastAPI
  /// client lands; until then every repository provider resolves to a mock.
  static const bool mockMode = true;

  /// Simulated network latency for mock repositories, so loading states are
  /// visible and testable rather than instant.
  static const Duration mockLatency = Duration(milliseconds: 220);
}
