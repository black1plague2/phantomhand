import 'dart:async';
import 'dart:math';

import 'package:opus_app/data/models/live_message.dart';
import 'package:opus_app/data/repositories/live_repository.dart';

/// Generates a synthetic trial-by-trial stream at ~1 Hz (well under
/// ARCHITECTURE.md §7's "<=10 Hz" cap) so the live monitor screen (A6) has
/// something moving without a real device or backend.
class MockLiveRepository implements LiveRepository {
  final Map<String, _LiveSessionState> _sessions = {};

  @override
  Stream<LiveMessage> watch(String sessionId, {int totalTrials = 20}) {
    final state = _sessions.putIfAbsent(sessionId, () => _LiveSessionState(totalTrials));
    return state.controller.stream;
  }

  @override
  void pause(String sessionId) => _sessions[sessionId]?.pause();

  @override
  void resume(String sessionId) => _sessions[sessionId]?.resume();

  @override
  void stop(String sessionId) => _sessions[sessionId]?.stop();
}

class _LiveSessionState {
  new(this.totalTrials) {
    // The doc comment on LiveRepository.watch() promises "cancelling the
    // subscription stops the mock generator" -- that wasn't actually true
    // before this fix (the Timer.periodic ran regardless of listeners, a
    // real leak found via this run's golden tests: they failed with "A Timer
    // is still pending even after the widget tree was disposed" once a test
    // run exercised this repository across multiple widget trees).
    // `onListen`/`onCancel` on the broadcast controller now start/stop the
    // timer with real listener presence, matching the documented contract.
    controller.onListen = _start;
    controller.onCancel = () => _timer?.cancel();
  }

  final int totalTrials;
  final controller = StreamController<LiveMessage>.broadcast();
  final _rand = Random();
  Timer? _timer;
  int _trial = 0;
  LiveSessionStatus _status = LiveSessionStatus.running;
  double _rt = 400;
  double _speed = 1;

  void _start() {
    _emit();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (_status != LiveSessionStatus.running) return;
      if (_trial >= totalTrials) {
        _status = LiveSessionStatus.stopped;
        _emit();
        _timer?.cancel();
        return;
      }
      _trial++;
      _rt = (_rt + _rand.nextDouble() * 40 - 20).clamp(250, 900);
      _speed = (_speed + _rand.nextDouble() * 0.1 - 0.05).clamp(0.4, 2.0);
      _emit(lastOutcome: _rand.nextDouble() > 0.15 ? 'success' : 'miss');
    });
  }

  void pause() {
    _status = LiveSessionStatus.paused;
    _emit();
  }

  void resume() {
    if (_status == LiveSessionStatus.stopped) return;
    _status = LiveSessionStatus.running;
    _emit();
  }

  void stop() {
    _status = LiveSessionStatus.stopped;
    _emit();
    _timer?.cancel();
  }

  void _emit({String? lastOutcome}) {
    controller.add(
      LiveMessage(
        status: _status,
        trialIndex: _trial,
        totalTrials: totalTrials,
        rollingMetrics: {
          'reaction_time_ms': double.parse(_rt.toStringAsFixed(1)),
          'peak_speed_mps': double.parse(_speed.toStringAsFixed(2)),
        },
        lastOutcome: lastOutcome,
      ),
    );
  }
}
