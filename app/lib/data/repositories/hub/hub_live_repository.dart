import 'dart:async';

import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/live_message.dart' as hub;
import 'package:opus_app/data/models/live_message.dart';
import 'package:opus_app/data/repositories/live_repository.dart';

/// Real `LiveRepository` implementation backed by one connected headset's
/// [HubConnection] (brief B3: "Live monitor bound to the real stream").
/// Translates the wire-level `status`/`trial_event`/`metrics_tick` messages
/// (`contracts/LIVE_PROTOCOL.md`) into the app's pre-contract [LiveMessage]
/// shape that `LiveMonitorScreen` already renders, so the screen needs no
/// change to consume either a mock or a real session.
///
/// `pause`/`resume`/`stop` send real `command` messages (`requires_ack`) --
/// unlike [LiveRepository]'s synchronous interface, failures/timeouts aren't
/// surfaced here (the interface returns `void`); callers that need the ack
/// result should use `HubController.pause/resume/stopSession` directly, which
/// this class's counterparts simply forward to fire-and-forget.
class HubLiveRepository implements LiveRepository {
  new(this.connection);

  final HubConnection connection;

  final _controller = StreamController<LiveMessage>.broadcast();
  StreamSubscription<Map<String, dynamic>>? _statusSub;
  StreamSubscription<Map<String, dynamic>>? _trialSub;
  StreamSubscription<Map<String, dynamic>>? _metricsSub;

  LiveSessionStatus _status = LiveSessionStatus.running;
  int _trialIndex = 0;
  String? _lastOutcome;
  Map<String, double> _rollingMetrics = const {};
  bool _subscribed = false;
  int? _totalTrials;

  @override
  Stream<LiveMessage> watch(String sessionId, {int totalTrials = 20}) {
    if (!_subscribed) {
      _subscribed = true;
      _statusSub = connection.statusStream.listen((payload) {
        // The headset reports its real plan; the 20 default was only ever a mock placeholder.
        final total = payload['trials_total'];
        if (total is num && total > 0) _totalTrials = total.toInt();
        final done = payload['trials_completed'];
        if (done is num && _trialIndex == 0) _trialIndex = done.toInt();
        final state = payload['state'] as String?;
        _status = switch (state) {
          'paused' => LiveSessionStatus.paused,
          'finished' || 'error' => LiveSessionStatus.stopped,
          _ => LiveSessionStatus.running,
        };
        _emit(totalTrials);
      });
      _trialSub = connection.trialEventStream.listen((payload) {
        final trial = payload['trial'];
        if (trial is num) _trialIndex = trial.toInt() + 1; // events are 0-indexed, UI is 1-indexed
        final type = payload['type'] as String?;
        if (type == 'trial_end') _lastOutcome = payload['outcome'] as String?;
        if (type == 'session_end') _status = LiveSessionStatus.stopped;
        _emit(totalTrials);
      });
      _metricsSub = connection.metricsStream.listen((payload) {
        final metrics = (payload['metrics'] as Map?)?.cast<String, dynamic>() ?? const {};
        _rollingMetrics = {
          for (final e in metrics.entries)
            if (e.value is num) e.key: (e.value as num).toDouble(),
        };
        _emit(totalTrials);
      });
      // Seed one frame immediately from whatever status the connection
      // already has (a clinician opening the live monitor after the headset
      // already sent status shouldn't see a blank screen until the next tick).
      final last = connection.lastStatus;
      if (last != null) {
        final state = last['state'] as String?;
        _status = switch (state) {
          'paused' => LiveSessionStatus.paused,
          'finished' || 'error' => LiveSessionStatus.stopped,
          _ => LiveSessionStatus.running,
        };
      }
      _emit(totalTrials);
    }
    return _controller.stream;
  }

  void _emit(int totalTrials) {
    if (_controller.isClosed) return;
    _controller.add(
      LiveMessage(
        status: _status,
        trialIndex: _trialIndex,
        totalTrials: _totalTrials ?? totalTrials,
        rollingMetrics: _rollingMetrics,
        lastOutcome: _lastOutcome,
      ),
    );
  }

  @override
  void pause(String sessionId) => connection.send(
        hub.LiveMessage.command(connection.nextSeq, sessionId: sessionId, command: 'pause'),
      );

  @override
  void resume(String sessionId) => connection.send(
        hub.LiveMessage.command(connection.nextSeq, sessionId: sessionId, command: 'resume'),
      );

  @override
  void stop(String sessionId) => connection.send(
        hub.LiveMessage.command(connection.nextSeq, sessionId: sessionId, command: 'stop'),
      );

  /// Releases the stream subscriptions. Does not close [connection] -- the
  /// hub owns its lifecycle.
  void dispose() {
    _statusSub?.cancel();
    _trialSub?.cancel();
    _metricsSub?.cancel();
    unawaited(_controller.close());
  }
}
