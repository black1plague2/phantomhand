import 'package:uuid/uuid.dart';

const _uuid = Uuid();

/// One JSON text frame of OPUS Live Protocol v1
/// (`contracts/schemas/live-message.schema.json`, `contracts/LIVE_PROTOCOL.md`).
/// Kept as a thin typed wrapper over the raw map -- `payload` is deliberately
/// left as `Map<String, dynamic>` since its shape depends on `type` (see
/// [validateLiveMessage] for the per-type checks the schema's `allOf`
/// describes).
class LiveMessage {
  new({
    required this.type,
    required this.from,
    required this.seq,
    this.sessionId,
    this.requiresAck = false,
    Map<String, dynamic>? payload,
    String? id,
    double? tsMs,
  })  : id = id ?? _uuid.v4(),
        tsMs = tsMs ?? DateTime.now().millisecondsSinceEpoch.toDouble(),
        payload = payload ?? const {};

  final String type;
  final String id;
  final int seq;
  final double tsMs;
  final String from; // 'headset' | 'hub'
  final String? sessionId;
  final bool requiresAck;
  final Map<String, dynamic> payload;

  Map<String, dynamic> toJson() => {
        'v': 1,
        'type': type,
        'id': id,
        'seq': seq,
        'ts_ms': tsMs,
        'from': from,
        if (sessionId != null) 'session_id': sessionId,
        if (requiresAck) 'requires_ack': true,
        'payload': payload,
      };

  static LiveMessage? tryFromJson(Map<String, dynamic> json) {
    final error = validateLiveMessage(json);
    if (error != null) return null;
    return LiveMessage(
      type: json['type'] as String,
      id: json['id'] as String,
      seq: json['seq'] as int,
      tsMs: (json['ts_ms'] as num).toDouble(),
      from: json['from'] as String,
      sessionId: json['session_id'] as String?,
      requiresAck: json['requires_ack'] as bool? ?? false,
      payload: (json['payload'] as Map?)?.cast<String, dynamic>() ?? const {},
    );
  }

  // ---- Convenience builders for the hub side (from = 'hub') ----

  static LiveMessage helloAck(int seq, {required String pairToken, int? resumeFromSeq}) => LiveMessage(
        type: 'hello_ack',
        from: 'hub',
        seq: seq,
        payload: {'pair_token': pairToken, 'resume_from_seq': ?resumeFromSeq},
      );

  static LiveMessage ack(int seq, {required String ackId, required bool ok, String? error}) => LiveMessage(
        type: 'ack',
        from: 'hub',
        seq: seq,
        payload: {
          'ack_id': ackId,
          'ok': ok,
          'error': error,
          'recv_ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
        },
      );

  static LiveMessage ping(int seq) =>
      LiveMessage(type: 'ping', from: 'hub', seq: seq, payload: {
        'echo_ts_ms': DateTime.now().millisecondsSinceEpoch.toDouble(),
      });

  static LiveMessage pong(int seq, {required double echoTsMs}) =>
      LiveMessage(type: 'pong', from: 'hub', seq: seq, payload: {'echo_ts_ms': echoTsMs});

  static LiveMessage command(
    int seq, {
    required String sessionId,
    required String command,
    Map<String, dynamic>? params,
    String? text,
  }) =>
      LiveMessage(
        type: 'command',
        from: 'hub',
        seq: seq,
        sessionId: sessionId,
        requiresAck: true,
        payload: {'command': command, 'params': ?params, 'text': ?text},
      );

  static LiveMessage assignProgram(
    int seq, {
    required Map<String, dynamic> program,
    required String patientRef,
    List<Map<String, dynamic>>? manifests,
  }) =>
      LiveMessage(
        type: 'assign_program',
        from: 'hub',
        seq: seq,
        requiresAck: true,
        payload: {'program': program, 'patient_ref': patientRef, 'manifests': ?manifests},
      );

  static LiveMessage error(int seq, {required String code, required String message}) => LiveMessage(
        type: 'error',
        from: 'hub',
        seq: seq,
        payload: {'code': code, 'message': message},
      );
}

const _messageTypes = {
  'hello', 'hello_ack', 'status', 'trial_event', 'metrics_tick', 'session_started',
  'session_ended', 'file_available', 'assign_program', 'command', 'ack', 'ping', 'pong', 'error',
};

/// Mirrors `contracts/schemas/live-message.schema.json`'s shape checks (the
/// hub can't do real JSON-Schema `$ref` resolution at the edge without a
/// dependency, so this hand-checks the same `required`/`enum`/`allOf` rules
/// for the fields the hub actually branches on). Returns `null` when valid,
/// otherwise a short human-readable reason (used to build a `error` reply).
String? validateLiveMessage(Map<String, dynamic> json) {
  if (json['v'] != 1) return 'missing or wrong "v" (must be 1)';
  final type = json['type'];
  if (type is! String || !_messageTypes.contains(type)) return 'missing or unknown "type"';
  if (json['id'] is! String) return 'missing "id"';
  if (json['seq'] is! int || (json['seq'] as int) < 0) return 'missing or invalid "seq"';
  if (json['ts_ms'] is! num) return 'missing "ts_ms"';
  final from = json['from'];
  if (from != 'headset' && from != 'hub') return 'missing or invalid "from"';

  final payload = json['payload'];
  switch (type) {
    case 'hello':
      if (payload is! Map) return 'hello requires "payload"';
      if (payload['device_id'] is! String) return 'hello.payload requires "device_id"';
      if (payload['role'] != 'headset' && payload['role'] != 'hub') return 'hello.payload requires valid "role"';
      if (payload['versions'] is! Map) return 'hello.payload requires "versions"';
    case 'status':
      if (payload is! Map) return 'status requires "payload"';
      const validStates = {
        'idle', 'paired', 'calibrating', 'ready', 'running', 'paused', 'rest', 'finished', 'error',
      };
      if (!validStates.contains(payload['state'])) return 'status.payload requires valid "state"';
      // contracts v0.2: optional game_state (all four keys required when present) and trace (fs_hz 20, <= 100).
      final gameState = payload['game_state'];
      if (gameState != null) {
        if (gameState is! Map || gameState['phase'] is! String || (gameState['phase'] as String).isEmpty) {
          return 'status.payload.game_state requires "phase"';
        }
        if (!gameState.containsKey('condition') ||
            !(gameState['condition'] == null || gameState['condition'] == 'sync' || gameState['condition'] == 'async')) {
          return 'status.payload.game_state requires "condition" (sync|async|null)';
        }
        if (!gameState.containsKey('remaining_s') || !(gameState['remaining_s'] == null || gameState['remaining_s'] is num)) {
          return 'status.payload.game_state requires "remaining_s" (number|null)';
        }
        final nodes = gameState['nodes'];
        if (nodes is! Map ||
            nodes['haptic'] is! Map ||
            (nodes['haptic'] as Map)['connected'] is! bool ||
            nodes['bio'] is! Map ||
            (nodes['bio'] as Map)['connected'] is! bool) {
          return 'status.payload.game_state requires "nodes.haptic.connected" and "nodes.bio.connected"';
        }
      }
      final trace = payload['trace'];
      if (trace != null) {
        if (trace is! Map ||
            trace['emg_env'] is! List ||
            trace['accel_mag'] is! List ||
            trace['t0_ms'] is! num ||
            trace['fs_hz'] != 20 ||
            (trace['emg_env'] as List).length > 100 ||
            (trace['accel_mag'] as List).length > 100) {
          return 'status.payload.trace requires emg_env[<=100], accel_mag[<=100], t0_ms, fs_hz 20';
        }
      }
    case 'trial_event':
      if (payload is! Map) return 'trial_event requires "payload"';
    case 'metrics_tick':
      if (payload is! Map || payload['window_trials'] is! int || payload['metrics'] is! Map) {
        return 'metrics_tick.payload requires "window_trials" and "metrics"';
      }
    case 'file_available':
      if (payload is! Map) return 'file_available requires "payload"';
      final name = payload['name'];
      final validName = name is String &&
          (name == 'session.json' ||
              name == 'events.ndjson' ||
              name == 'metrics.json' ||
              RegExp(r'^(kin|sens)_\d{3}\.json$').hasMatch(name));
      if (!validName) return 'file_available.payload.name is not one of the allowed file names';
      if (payload['bytes'] is! int) return 'file_available.payload requires "bytes"';
      if (payload['sha256'] is! String) return 'file_available.payload requires "sha256"';
    case 'assign_program':
      if (payload is! Map) return 'assign_program requires "payload"';
      if (payload['program'] is! Map) return 'assign_program.payload requires "program"';
      if (payload['patient_ref'] is! String) return 'assign_program.payload requires "patient_ref"';
    case 'command':
      if (payload is! Map) return 'command requires "payload"';
      const validCommands = {
        'start', 'pause', 'resume', 'stop', 'recenter', 'skip_block', 'adjust_params', 'show_message',
        // contracts v0.2 (Phantom Hand operator commands)
        'phase_next', 'set_condition_order', 'abort_phase', 'next_person',
      };
      if (!validCommands.contains(payload['command'])) return 'command.payload requires valid "command"';
      if (payload['command'] == 'set_condition_order') {
        final params = payload['params'];
        final order = params is Map ? params['condition_order'] : null;
        if (order != 'sync_first' && order != 'async_first') {
          return 'set_condition_order requires params.condition_order (sync_first|async_first)';
        }
      }
    case 'ack':
      if (payload is! Map) return 'ack requires "payload"';
      if (payload['ack_id'] is! String) return 'ack.payload requires "ack_id"';
      if (payload['ok'] is! bool) return 'ack.payload requires "ok"';
    case 'error':
      if (payload is! Map) return 'error requires "payload"';
      if (payload['code'] == null || payload['message'] == null) {
        return 'error.payload requires "code" and "message"';
      }
  }
  return null;
}
