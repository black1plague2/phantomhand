import 'dart:async';
import 'dart:convert';

import 'package:opus_app/core/hub/hub_connection.dart';

/// An in-process, no-I/O stand-in for a real WebSocket, so the protocol
/// engine (`HubConnection`: ack/retry, ping/pong, resume, de-dup) can be
/// unit-tested deterministically without a real socket or event loop I/O.
/// [sent] captures every decoded message this side sent (what the "headset"
/// would have received); call [receive] to simulate the headset sending a
/// message back.
class FakeLiveSocket implements LiveSocket {
  final List<Map<String, dynamic>> sent = [];
  final _incomingController = StreamController<dynamic>.broadcast();
  bool closed = false;

  /// The stream to pass as `HubConnection(... incoming: fake.incoming)`.
  Stream<dynamic> get incoming => _incomingController.stream;

  @override
  void send(String data) {
    if (closed) return;
    sent.add(jsonDecode(data) as Map<String, dynamic>);
  }

  @override
  Future<void> close() async {
    closed = true;
    await _incomingController.close();
  }

  /// Simulates the headset sending [json] to the hub.
  void receive(Map<String, dynamic> json) => _incomingController.add(jsonEncode(json));

  /// Simulates a malformed frame (not valid JSON, or valid JSON that isn't a
  /// map) arriving.
  void receiveRaw(String data) => _incomingController.add(data);
}

/// Builds a minimal valid inbound message map (from the headset's side),
/// filling in `v`/`from` so tests only need to specify what they care about.
Map<String, dynamic> headsetMessage({
  required String type,
  required String id,
  required int seq,
  Map<String, dynamic>? payload,
  String? sessionId,
  bool requiresAck = false,
}) =>
    {
      'v': 1,
      'type': type,
      'id': id,
      'seq': seq,
      'ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
      'from': 'headset',
      'session_id': ?sessionId,
      if (requiresAck) 'requires_ack': true,
      'payload': payload ?? <String, dynamic>{},
    };
