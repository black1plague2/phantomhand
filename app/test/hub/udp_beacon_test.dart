import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/udp_beacon.dart';

void main() {
  group('directedBroadcastsForPrefixRange', () {
    test('172.18.226.79 across /16..24 includes the real /20 broadcast and the wrong /24 guess', () {
      // Real on-device network (Opus, 2026-09-19): wlan0 172.18.226.79/20,
      // real broadcast 172.18.239.255. A naive /24 guess would compute
      // 172.18.226.255, which never reached the PC on the same /20 network.
      final result = directedBroadcastsForPrefixRange([172, 18, 226, 79], 16, 24).map((a) => a.address).toSet();

      expect(result, contains('172.18.239.255')); // real /20 broadcast
      expect(result, contains('172.18.226.255')); // old /24 guess, still included
      expect(result, contains('172.18.255.255')); // /16 broadcast
    });

    test('dedupes addresses shared across adjacent prefix lengths', () {
      // /24 and /25 that share the same fixed high bits pattern can collapse
      // to the same broadcast for some host-bit values; verify no duplicates
      // are emitted regardless.
      final result = directedBroadcastsForPrefixRange([10, 0, 0, 1], 16, 24).toList();
      final addresses = result.map((a) => a.address).toList();
      expect(addresses.toSet().length, addresses.length);
    });

    test('single prefix length matches the classic /24 computation', () {
      final result = directedBroadcastsForPrefixRange([192, 168, 1, 42], 24, 24).map((a) => a.address);
      expect(result, ['192.168.1.255']);
    });
  });
}
