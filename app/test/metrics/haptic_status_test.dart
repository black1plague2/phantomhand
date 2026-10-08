import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/metrics/haptic_status.dart';

void main() {
  group('parseHapticStatus', () {
    test('a headset with no haptic block at all reports notConnected, no jargon', () {
      final status = parseHapticStatus({'state': 'running'});
      expect(status.state, HapticSleeveState.notConnected);
      expect(status.summary, 'Haptic sleeve: not connected.');
    });

    test('a null status (no headset connected yet) also reports notConnected', () {
      final status = parseHapticStatus(null);
      expect(status.state, HapticSleeveState.notConnected);
    });

    test('a connected sleeve surfaces device id, battery and motors', () {
      final status = parseHapticStatus({
        'state': 'running',
        'haptic': {'connected': true, 'device_id': 'sleeve-01', 'battery_pct': 82, 'motors_ok': true, 'cues_sent': 5},
      });
      expect(status.state, HapticSleeveState.connected);
      expect(status.deviceId, 'sleeve-01');
      expect(status.batteryPct, 82);
      expect(status.cuesSent, 5);
      expect(status.summary, contains('sleeve-01'));
      expect(status.summary, contains('battery 82%'));
      expect(status.summary, contains('motors ok'));
    });

    test('motors_ok: false is called out in plain language', () {
      final status = parseHapticStatus({
        'haptic': {'connected': true, 'device_id': 'sleeve-01', 'motors_ok': false},
      });
      expect(status.summary, contains('motors need attention'));
    });

    test('an explicit connected: false with history reports disconnected mid-session', () {
      final status = parseHapticStatus(
        {
          'haptic': {'connected': false, 'device_id': 'sleeve-01'},
        },
        wasConnectedBefore: true,
      );
      expect(status.state, HapticSleeveState.disconnectedMidSession);
      expect(status.summary, contains('logged but not felt'));
    });

    test('an explicit connected: false with no prior history reports notConnected', () {
      final status = parseHapticStatus({
        'haptic': {'connected': false},
      });
      expect(status.state, HapticSleeveState.notConnected);
    });
  });
}
