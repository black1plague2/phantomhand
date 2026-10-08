import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:opus_app/core/hub/live_message.dart';

/// The minimal outbound-socket surface [HubConnection] needs: send a text
/// frame, close. Abstracted away from the concrete `dart:io` `WebSocket` so
/// the protocol engine (ack/retry/resume/ping-pong) can be unit-tested
/// end-to-end against an in-process fake, with no real socket or event loop
/// I/O -- see `test/hub/fake_live_socket.dart`.
abstract class LiveSocket {
  void send(String data);
  Future<void> close();
}

/// The real implementation, wrapping a `dart:io` `WebSocket` (server or
/// client side -- both expose the same `add`/`close` surface).
class WebSocketLiveSocket implements LiveSocket {
  new(this.socket);
  final WebSocket socket;

  @override
  void send(String data) => socket.add(data);

  @override
  Future<void> close() => socket.close();
}

/// One connected headset, wrapping its [LiveSocket] with the reliability rules
/// from `contracts/LIVE_PROTOCOL.md`: per-connection monotonic `seq`, a
/// `requires_ack` outbox with a 1 s ack timeout + retry, `ping`/`pong` every
/// 1 s with RTT tracking (3 missed pongs -> disconnected), and a resume
/// buffer so a reconnecting headset can be replayed everything after its
/// `resume_from_seq`. Receivers de-dup by `id`.
class HubConnection {
  /// [incoming] is the connection's inbound message stream. A raw
  /// `WebSocket` can only ever be listened to once (dart:io), so when the
  /// caller needs to peek at the first message before constructing this
  /// class (the hub reads `hello` before it knows the device id -- see
  /// `HubServer._handleSocket`), pass a `StreamQueue(socket).rest`; tests
  /// pass a `FakeLiveSocket`'s own stream.
  new({
    required this.deviceId,
    required this.socket,
    required this.pairToken,
    required Stream<dynamic> incoming,
    this.ackTimeout = const Duration(seconds: 1),
    this.pingInterval = const Duration(seconds: 1),
    this.maxMissedPongs = 3,
  }) {
    incoming.listen(_onData, onDone: _onDone, onError: (_) => _onDone());
    _pingTimer = Timer.periodic(pingInterval, (_) => _sendPing());
  }

  final String deviceId;
  final LiveSocket socket;
  final String pairToken;
  final Duration ackTimeout;
  final Duration pingInterval;
  final int maxMissedPongs;

  int _seq = 0;
  int get nextSeq => _seq++;

  /// Ids of every inbound message already handled -- de-dup by `id`
  /// (LIVE_PROTOCOL.md "Receivers de-dup by id").
  final Set<String> _seenIds = {};

  /// Every `requires_ack` message sent since the last `hello_ack`, keyed by
  /// its own `id`, so a reconnecting/late-acking headset can be replayed or
  /// retried. Removed once acked.
  final Map<String, _PendingAck> _outbox = {};

  /// Every message (including plain `trial_event`s) sent since the last
  /// `hello_ack`, kept for `resume_from_seq` replay -- LIVE_PROTOCOL.md:
  /// "keeps an outbox of unacked requires_ack messages and all trial_events
  /// since the last hello_ack".
  final List<LiveMessage> _sentSinceHello = [];

  Timer? _pingTimer;
  DateTime? _lastPingSentAt;
  Duration? lastRtt;
  int _missedPongs = 0;
  bool _closed = false;

  final _statusController = StreamController<Map<String, dynamic>>.broadcast();
  final _trialEventController = StreamController<Map<String, dynamic>>.broadcast();
  final _metricsController = StreamController<Map<String, dynamic>>.broadcast();
  final _disconnectedController = StreamController<void>.broadcast();
  final _messageLogController = StreamController<InboundMessageLog>.broadcast();

  Stream<Map<String, dynamic>> get statusStream => _statusController.stream;
  Stream<Map<String, dynamic>> get trialEventStream => _trialEventController.stream;
  Stream<Map<String, dynamic>> get metricsStream => _metricsController.stream;
  Stream<void> get onDisconnected => _disconnectedController.stream;

  /// Every validated inbound message (any `type`), with the one-way
  /// hub-receipt latency computed from the envelope's own `ts_ms` (the
  /// sender's clock at send time) vs this hub's wall clock on receipt --
  /// the same field LIVE_PROTOCOL.md's "Measure in the E2E harness from
  /// ts_ms + ack.recv_ts_ms" already relies on, just read directly instead
  /// of needing a paired ack. Used by `tool/hub_cli.dart` to print a
  /// per-message latency line for a real (or simulated) headset session;
  /// not used by the app UI, which only needs RTT ([lastRtt]) and the
  /// typed streams above. Clock skew between headset and hub isn't
  /// corrected for, so this is a coarse figure, not a substitute for the
  /// ping/pong RTT -- documented on [InboundMessageLog].
  Stream<InboundMessageLog> get messageLog => _messageLogController.stream;

  Map<String, dynamic>? lastStatus;
  String? activeSessionId;

  void _onData(dynamic data) {
    if (data is! String) return;
    Map<String, dynamic> json;
    try {
      json = jsonDecode(data) as Map<String, dynamic>;
    } catch (_) {
      return;
    }
    final error = validateLiveMessage(json);
    if (error != null) {
      send(LiveMessage.error(nextSeq, code: 'invalid_message', message: error));
      return;
    }
    final id = json['id'] as String;
    if (_seenIds.contains(id)) return; // de-dup
    _seenIds.add(id);

    final type = json['type'] as String;
    final payload = (json['payload'] as Map?)?.cast<String, dynamic>() ?? const {};
    final sentTsMs = (json['ts_ms'] as num).toDouble();
    final latencyMs = DateTime.now().millisecondsSinceEpoch - sentTsMs.round();
    _messageLogController.add(InboundMessageLog(type: type, seq: json['seq'] as int, latencyMs: latencyMs));
    switch (type) {
      case 'ack':
        final ackId = payload['ack_id'] as String?;
        if (ackId != null) {
          final pending = _outbox.remove(ackId);
          pending?.timer.cancel();
          final ok = payload['ok'] as bool? ?? true;
          if (!(pending?.completer?.isCompleted ?? true)) pending!.completer!.complete(ok);
        }
      case 'pong':
        if (_lastPingSentAt != null) {
          lastRtt = DateTime.now().difference(_lastPingSentAt!);
        }
        _missedPongs = 0;
      case 'ping':
        send(LiveMessage.pong(nextSeq, echoTsMs: (payload['echo_ts_ms'] as num?)?.toDouble() ?? 0));
      case 'status':
        lastStatus = payload;
        _statusController.add(payload);
        // `session_started`/`session_ended` (the dedicated wire types below)
        // are in the live-message schema's `type` enum, but neither the mock
        // headset (`sim/live/fake_headset.py`) nor the real Unity session
        // runner (`OpusSessionRunner.cs`, per CONTEXT.md) actually sends
        // them -- they only ever send `status`/`trial_event`/`metrics_tick`,
        // each carrying the session's id in the message's own top-level
        // `session_id` field (schema: every message may carry one). Without
        // this, `activeSessionId` never left `null` for a real headset, so
        // the Devices/Monitor screens could never find "the session this
        // headset is running" (`connectionForSession`/`allRunningSessionIds`
        // in `hub_providers.dart`) even while trial events were streaming --
        // found live against `sim/live/fake_headset.py` this run. Treat a
        // `status` state of idle/finished/error as "no active session" and
        // anything else (paired/calibrating/ready/running/paused/rest) as
        // active, per the schema's `status.state` enum.
        final statusSessionId = json['session_id'] as String?;
        final state = payload['state'] as String?;
        activeSessionId = (state == 'idle' || state == 'finished' || state == 'error') ? null : (statusSessionId ?? activeSessionId);
      case 'trial_event':
        _trialEventController.add(payload);
        final trialSessionId = json['session_id'] as String?;
        if (trialSessionId != null) activeSessionId = trialSessionId;
        if (payload['type'] == 'session_end') activeSessionId = null;
      case 'metrics_tick':
        _metricsController.add(payload);
      case 'session_started':
      case 'session_ended':
        // Real dedicated wire messages, if a headset ever sends them --
        // takes priority since it's the most explicit signal available.
        activeSessionId = type == 'session_started' ? (json['session_id'] as String?) : null;
    }
  }

  /// Sends [message] and, if it `requires_ack`, tracks it in the outbox with
  /// a retry after [ackTimeout] (LIVE_PROTOCOL.md: "The receiver must ack
  /// within 1 s").
  void send(LiveMessage message) {
    if (_closed) return;
    socket.send(jsonEncode(message.toJson()));
    _sentSinceHello.add(message);
    // Track when *any* ping went out (not just the periodic timer's), so RTT
    // is measured correctly whether `_sendPing` or a manual `send(ping(...))`
    // triggered it.
    if (message.type == 'ping') _lastPingSentAt = DateTime.now();
    if (message.requiresAck) {
      _outbox[message.id] = _PendingAck(
        message,
        Timer(ackTimeout, () => _retry(message.id)),
      );
    }
  }

  void _retry(String id) {
    final pending = _outbox[id];
    if (pending == null || _closed) return;
    if (pending.retries >= 3) {
      _outbox.remove(id);
      if (!(pending.completer?.isCompleted ?? true)) pending.completer!.complete(false);
      return;
    }
    socket.send(jsonEncode(pending.message.toJson()));
    _outbox[id] = _PendingAck(
      pending.message,
      Timer(ackTimeout, () => _retry(id)),
      retries: pending.retries + 1,
      completer: pending.completer,
    );
  }

  /// Sends a `requires_ack` message and resolves once the peer's `ack`
  /// arrives (`true` if `ack.ok`, `false` if it explicitly failed) or the
  /// outbox gives up after 3 retries / [timeout] elapses, whichever is first
  /// -- backs the inline "Pausing... / Paused / Headset didn't confirm. Try
  /// again." states `docs/APP_DESIGN.md`'s live monitor requires for every
  /// command. Messages that don't require an ack resolve `true` immediately.
  Future<bool> sendAndAwaitAck(LiveMessage message, {Duration timeout = const Duration(seconds: 4)}) {
    if (!message.requiresAck) {
      send(message);
      return Future.value(true);
    }
    final completer = Completer<bool>();
    send(message);
    final pending = _outbox[message.id];
    if (pending == null) {
      // Already acked/removed between send() and here (shouldn't happen
      // synchronously, but don't hang if it does).
      return Future.value(true);
    }
    _outbox[message.id] = _PendingAck(pending.message, pending.timer, retries: pending.retries, completer: completer);
    unawaited(
      Future.delayed(timeout, () {
        if (!completer.isCompleted) completer.complete(false);
      }),
    );
    return completer.future;
  }

  void _sendPing() {
    if (_closed) return;
    if (_missedPongs >= maxMissedPongs) {
      _onDone();
      return;
    }
    _lastPingSentAt = DateTime.now();
    _missedPongs++;
    send(LiveMessage.ping(nextSeq));
  }

  /// Replays every message sent since the last `hello_ack` with `seq >
  /// resumeFromSeq` (LIVE_PROTOCOL.md resume rule), used right after a
  /// reconnecting headset's new `hello` names what it already has.
  void replaySince(int resumeFromSeq) {
    for (final m in _sentSinceHello) {
      if (m.seq > resumeFromSeq) socket.send(jsonEncode(m.toJson()));
    }
  }

  void resetHelloWindow() => _sentSinceHello.clear();

  /// Everything sent since the last `hello_ack`, so a reconnect can seed a
  /// brand-new [HubConnection] (built on the new socket) with the outgoing
  /// history the *old* connection object accumulated, before replaying.
  List<LiveMessage> get sentHistory => List.unmodifiable(_sentSinceHello);

  void seedHistory(List<LiveMessage> previous) => _sentSinceHello.insertAll(0, previous);

  void _onDone() {
    if (_closed) return;
    _closed = true;
    _pingTimer?.cancel();
    for (final p in _outbox.values) {
      p.timer.cancel();
    }
    _disconnectedController.add(null);
  }

  Future<void> close() async {
    _onDone();
    await socket.close();
  }
}

/// One inbound message's hub-receipt latency, emitted on [HubConnection.messageLog].
/// This is a one-way figure (`hub receipt clock - sender's ts_ms`), not a
/// round trip -- for RTT, use [HubConnection.lastRtt] (measured by the
/// ping/pong exchange, which cancels out clock skew since both timestamps
/// are read from the same clock). Coarse if the two devices' clocks aren't
/// synchronized; still useful for spotting stalls or outliers during a
/// manual/demo run, which is its only consumer (`tool/hub_cli.dart`).
class InboundMessageLog {
  const InboundMessageLog({required this.type, required this.seq, required this.latencyMs});
  final String type;
  final int seq;
  final int latencyMs;
}

class _PendingAck {
  new(this.message, this.timer, {this.retries = 0, this.completer});
  final LiveMessage message;
  final Timer timer;
  final int retries;
  final Completer<bool>? completer;
}
