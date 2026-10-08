/// Parses the optional `haptic` block a headset's `status` message
/// (`contracts/LIVE_PROTOCOL.md` `status.payload`) may carry, reporting the
/// haptic sleeve's own connection state alongside the headset's -- brief
/// item 2: "extend the hub's status handling to carry an optional `haptic`
/// block: connected, device_id, battery_pct, motors_ok, cues_sent".
///
/// The wire status payload is a plain `Map<String, dynamic>` end to end
/// (`HubConnection.statusStream`/`HeadsetInfo.status`); nothing in the
/// low-rate live channel needed a schema change since `payload` has no
/// `additionalProperties: false` restriction -- a headset with no sleeve
/// simply omits `haptic`, and this file is the one place that maps the raw
/// map into the three plain-language states `docs/APP_DESIGN.md` calls for.
library;

/// The haptic sleeve's connection state as shown on the Devices screen and
/// the live monitor's cue indicator. `notConnected` covers "never paired
/// this session" (no `haptic` block at all, or the headset reports
/// `connected: false` for the first time); `disconnectedMidSession` is used
/// when the app has already seen this headset report a connected sleeve
/// before -- callers that don't track that history (a single status
/// snapshot) fall back to `notConnected`, which is still an honest,
/// non-alarming plain-language state per the copy rules ("plain language, no
/// jargon"), just less specific than "disconnected mid-session".
enum HapticSleeveState { notConnected, connected, disconnectedMidSession }

class HapticStatus {
  const HapticStatus({
    required this.state,
    this.deviceId,
    this.batteryPct,
    this.motorsOk,
    this.cuesSent,
  });

  final HapticSleeveState state;
  final String? deviceId;
  final num? batteryPct;
  final bool? motorsOk;
  final int? cuesSent;

  /// Plain-language one-liner for the Devices screen's "Haptic sleeve" card,
  /// per `docs/APP_DESIGN.md`'s copy rules (plain, clinical, sentence case,
  /// no jargon, data quality/state in words not color alone).
  String get summary => switch (state) {
        HapticSleeveState.notConnected => 'Haptic sleeve: not connected.',
        HapticSleeveState.disconnectedMidSession =>
          'Haptic sleeve disconnected. Cues are being logged but not felt.',
        HapticSleeveState.connected => [
            'Haptic sleeve connected',
            if (deviceId != null) deviceId,
            if (batteryPct != null) 'battery ${batteryPct!.toStringAsFixed(0)}%',
            if (motorsOk == false) 'motors need attention' else if (motorsOk == true) 'motors ok',
          ].whereType<String>().join(', '),
      };
}

/// Reads the `haptic` block out of one headset's `status` payload
/// (`HeadsetInfo.status`/`HubConnection.lastStatus`). Returns
/// [HapticSleeveState.notConnected] with no other fields when the headset's
/// game doesn't report haptics at all (no sleeve, or a game/version that
/// predates the haptics feature) -- never treated as an error.
///
/// [wasConnectedBefore] lets a caller that tracks history across status
/// updates (e.g. the Devices screen keeping the last-seen state per headset)
/// distinguish "never connected" from "disconnected mid-session"; omit it (or
/// pass `false`) when only a single snapshot is available.
HapticStatus parseHapticStatus(Map<String, dynamic>? status, {bool wasConnectedBefore = false}) {
  final haptic = (status?['haptic'] as Map?)?.cast<String, dynamic>();
  if (haptic == null) return const HapticStatus(state: HapticSleeveState.notConnected);

  final connected = haptic['connected'] as bool? ?? false;
  if (connected) {
    return HapticStatus(
      state: HapticSleeveState.connected,
      deviceId: haptic['device_id'] as String?,
      batteryPct: haptic['battery_pct'] as num?,
      motorsOk: haptic['motors_ok'] as bool?,
      cuesSent: haptic['cues_sent'] as int?,
    );
  }
  return HapticStatus(
    state: wasConnectedBefore ? HapticSleeveState.disconnectedMidSession : HapticSleeveState.notConnected,
    deviceId: haptic['device_id'] as String?,
  );
}
