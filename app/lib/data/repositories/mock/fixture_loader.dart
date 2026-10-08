import 'dart:convert';

import 'package:flutter/services.dart' show rootBundle;

import 'package:opus_app/core/env/env.dart';

/// Shared asset-loading + simulated-latency/error helper for every mock
/// repository. Fixtures are copied into `assets/fixtures/` by
/// `tool/sync_fixtures.dart` from `contracts/fixtures/` (see that script for
/// why session files are flattened with `__`).
class FixtureLoader {
  const new();

  static const _sessionsDir = 'assets/fixtures/sessions';
  static const _manifestsDir = 'assets/fixtures/manifests';

  /// Every synthetic session key available, e.g. `healthy`,
  /// `longitudinal__week_03__session_0`.
  Future<List<String>> sessionKeys() async {
    final raw = await _tryLoad('$_sessionsDir/_index.json');
    if (raw == null) return const [];
    return (jsonDecode(raw) as List).cast<String>();
  }

  Future<Map<String, dynamic>?> sessionEnvelopeJson(String key) =>
      _loadJsonMap('$_sessionsDir/${key}__session.json');

  Future<Map<String, dynamic>?> sessionMetricsJson(String key) =>
      _loadJsonMap('$_sessionsDir/${key}__metrics.json');

  Future<List<Map<String, dynamic>>> sessionEventsNdjson(String key) async {
    final raw = await _tryLoad('$_sessionsDir/${key}__events.ndjson');
    if (raw == null) return const [];
    return raw
        .split('\n')
        .map((l) => l.trim())
        .where((l) => l.isNotEmpty)
        .map((l) => jsonDecode(l) as Map<String, dynamic>)
        .toList();
  }

  Future<Map<String, dynamic>?> longitudinalSummaryJson() =>
      _loadJsonMap('$_sessionsDir/longitudinal__summary.json');

  /// The decimated real wrist paths per trial for session [key] (see
  /// `tool/sync_fixtures.dart`'s `_buildTraces`), used by `ReachTraceGlyph`.
  /// `null` when the session has no `chunks` (nothing to decimate).
  Future<Map<String, dynamic>?> sessionTracesJson(String key) =>
      _loadJsonMap('$_sessionsDir/${key}__traces.json');

  Future<Map<String, dynamic>?> manifestJson(String gameId) =>
      _loadJsonMap('$_manifestsDir/$gameId.manifest.json');

  Future<Map<String, dynamic>?> _loadJsonMap(String path) async {
    final raw = await _tryLoad(path);
    if (raw == null) return null;
    return jsonDecode(raw) as Map<String, dynamic>;
  }

  Future<String?> _tryLoad(String path) async {
    try {
      return await rootBundle.loadString(path);
    } catch (_) {
      return null;
    }
  }

  /// Simulates network latency so loading states are real and testable.
  Future<void> latency() => Future<void>.delayed(Env.mockLatency);
}

/// Thrown by mock repositories when their error-injection toggle is on
/// (see `MockErrorInjector`), so screens can exercise real error/retry UI.
class MockRepositoryException implements Exception {
  const new(this.message);
  final String message;
  @override
  String toString() => message;
}

/// Simple, overridable-in-tests error toggle shared by all mock repositories,
/// per brief A2 "Simulated latency and error toggles".
class MockErrorInjector {
  new({this.enabled = false});
  bool enabled;

  void maybeThrow(String context) {
    if (enabled) {
      throw MockRepositoryException('Simulated network error (mock repository): $context');
    }
  }
}
