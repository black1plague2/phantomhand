// Support for the reconnect tests (hub_reconnect_replay_test.dart and
// phantom/phantom_live_reconnect_test.dart): a headset that talks to a REAL
// HubServer over a real WebSocket, loses its link without the hub noticing
// (a Wi-Fi drop: no close frame), dials in again with `resume_from_seq` and
// replays its trial_events with the same ids, as LiveClient.cs does on
// hello_ack (game/Packages/com.opus.sdk/Runtime/Transport/LiveClient.cs). The
// messages are the recorded Phantom Hand session of the in-app demo
// (assets/demo/phantom_hand, written by the game).
import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:uuid/uuid.dart';

const _uuid = Uuid();
const _demoEvents = 'assets/demo/phantom_hand/events.ndjson';

/// A free port for a `HubServer`. Ephemeral, not 8797: hub_phantom_test.dart
/// binds that one and the test files run in parallel (8787 is Unity's).
Future<int> freePort() async {
  final probe = await ServerSocket.bind(InternetAddress.anyIPv4, 0);
  final port = probe.port;
  await probe.close();
  return port;
}

/// The [nth] event of [type] of the recorded demo session, exactly as the game
/// wrote it (`{t_ms, seq, block, trial, type, data}`): the payload of the
/// `trial_event` the headset sends. Tests run with `app/` as working directory.
Map<String, dynamic> demoEvent(String type, {int nth = 0}) => [
      for (final line in File(_demoEvents).readAsLinesSync())
        if (line.trim().isNotEmpty) jsonDecode(line) as Map<String, dynamic>,
    ].where((e) => e['type'] == type).elementAt(nth);

/// A Phantom Hand `status` as the runner sends it: `game_id`, `game_state`
/// (all four keys) and the newest trace samples, here 2 at 20 Hz, the n-th chunk
/// of a trace that starts at 65 s of session time. Sent every 100 ms the trace
/// clock runs in real time, and the recording's first stone (65.7 s) and burst
/// (66.2 s) stay inside the card's 20 s window for the length of a test.
Map<String, dynamic> phantomStatus({
  required int n,
  required String phase,
  String state = 'running',
  String? condition = 'sync',
}) =>
    {
      'state': state,
      'game_id': 'phantom_hand',
      'game_state': {
        'phase': phase,
        'condition': condition,
        'remaining_s': 12.5,
        'nodes': {
          'haptic': {'connected': true},
          'bio': {'connected': true, 'emg_level': 0.12},
        },
      },
      'trace': {
        'emg_env': List<double>.filled(2, 420),
        'accel_mag': List<double>.filled(2, 9.8),
        't0_ms': 65000.0 + n * 100,
        'fs_hz': 20,
      },
    };

/// Waits (real time) until [condition] holds; fails the test after [timeout].
/// Polls instead of sleeping a fixed time so a loaded machine only makes the
/// test slower, not wrong.
Future<void> eventually(
  bool Function() condition,
  String what, {
  Duration timeout = const Duration(seconds: 10),
}) async {
  final deadline = DateTime.now().add(timeout);
  while (!condition()) {
    if (DateTime.now().isAfter(deadline)) fail('timed out waiting for $what');
    await Future<void>.delayed(const Duration(milliseconds: 10));
  }
}

class ReconnectingHeadset {
  new({required this.port, this.deviceId = 'quest-1', this.sessionId = 'a7c5d8c6-2e5a-40cd-a3f4-cda09ef85e92'});

  final int port;
  final String deviceId;

  /// Stamped on every `status` and `trial_event`, like the runner does after Start;
  /// a test sets it to start another session.
  String sessionId;

  /// Every `trial_event` sent so far, whole envelopes: the outbox of
  /// LIVE_PROTOCOL.md ("all trial_events since the last hello_ack").
  final List<Map<String, dynamic>> outbox = [];

  /// `seq` of the last frame the hub sent on the current link: what a real
  /// headset puts in `hello.resume_from_seq` ("last seq the sender received
  /// from the peer before disconnect").
  int lastRecvSeqFromHub = -1;

  final List<WebSocket> _sockets = [];
  final Map<int, Completer<void>> _pongs = {};
  WebSocket? _link; // null: the link is lost
  Timer? _statusTimer;
  int _seq = 0;
  int _statusCount = 0;
  int _pingCount = 0;

  /// Dials in: opens a socket, sends `hello` (with [resumeFromSeq] when given)
  /// and returns the hub's `hello_ack`. Answers the hub's pings from here on.
  Future<Map<String, dynamic>> connect({int? resumeFromSeq}) async {
    final ws = await WebSocket.connect('ws://127.0.0.1:$port/opus/v1/live');
    _sockets.add(ws);
    _link = ws;
    final helloAck = Completer<Map<String, dynamic>>();
    ws.listen(
      (data) {
        if (!identical(ws, _link)) return; // a lost link is not read: no pongs either
        final frame = jsonDecode(data as String) as Map<String, dynamic>;
        final payload = (frame['payload'] as Map?) ?? const {};
        lastRecvSeqFromHub = frame['seq'] as int;
        switch (frame['type']) {
          case 'hello_ack':
            if (!helloAck.isCompleted) helloAck.complete(frame);
          case 'ping':
            _send('pong', {'echo_ts_ms': payload['echo_ts_ms']});
          case 'pong':
            _pongs.remove((payload['echo_ts_ms'] as num).toInt())?.complete();
        }
      },
      onError: (_) {},
    );
    _send('hello', {
      'device_id': deviceId,
      'role': 'headset',
      'versions': {'shell': '0.0.0-test'},
      'resume_from_seq': ?resumeFromSeq,
    });
    return await helloAck.future.timeout(const Duration(seconds: 5));
  }

  /// The link dies the way a Wi-Fi drop kills it: the headset sends and reads
  /// nothing more on it, but no close frame reaches the hub, which keeps its
  /// `HubConnection` until the next `hello` of this device (or 3 missed pongs).
  void drop() {
    stopStatuses();
    _link = null;
  }

  /// Sends a `status` built by [build] every [every] until [stopStatuses] or
  /// [drop]; [build] gets a counter that goes on across links.
  void startStatuses(Map<String, dynamic> Function(int n) build, {Duration every = const Duration(milliseconds: 100)}) {
    stopStatuses();
    _statusTimer = Timer.periodic(every, (_) => _send('status', build(_statusCount++), withSession: true));
  }

  void stopStatuses() {
    _statusTimer?.cancel();
    _statusTimer = null;
  }

  /// Sends [payload] (an event of the game) as a `trial_event` and keeps it in the outbox.
  void event(Map<String, dynamic> payload) => outbox.add(_send('trial_event', payload, withSession: true));

  /// Sends the whole outbox again on the current link: same envelopes, same ids.
  void replay() => outbox.forEach(_write);

  /// Returns once the hub has handled everything sent before this call: it
  /// answers a `ping` only after the frames that came before it on the socket.
  /// Waits on the hub's answer, not on what the hub does with those frames, so
  /// it also returns when the hub drops them.
  Future<void> roundTrip() {
    final answered = _pongs[_pingCount] = Completer<void>();
    _send('ping', {'echo_ts_ms': _pingCount++});
    return answered.future.timeout(const Duration(seconds: 5));
  }

  Future<void> close() async {
    stopStatuses();
    _link = null; // nothing more is written to a socket that is closing
    for (final ws in _sockets) {
      await ws.close();
    }
  }

  Map<String, dynamic> _send(String type, Map<String, dynamic> payload, {bool withSession = false}) {
    final message = <String, dynamic>{
      'v': 1,
      'type': type,
      'id': _uuid.v4(),
      'seq': _seq++,
      'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
      'from': 'headset',
      if (withSession) 'session_id': sessionId,
      'payload': payload,
    };
    _write(message);
    return message;
  }

  void _write(Map<String, dynamic> message) {
    final link = _link;
    if (link != null && link.readyState == WebSocket.open) link.add(jsonEncode(message));
  }
}
