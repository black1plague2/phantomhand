import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// A socket can report success and still deliver nowhere, so a fresh one is bound every this many ticks.
const beaconRebindEveryTicks = 15;

/// UDP discovery beacon (`contracts/LIVE_PROTOCOL.md`): every 1 s, broadcasts
/// `{"opus_hub":1,"hub_id":…,"port":…,"name":…}` on port 8788 to the global
/// broadcast address AND to each active IPv4 interface's own subnet
/// broadcast address -- the fallback for networks where mDNS is blocked
/// (`docs/agent-briefs/A-flutter-app.md` M3: "the old pipeline's lesson").
///
/// Dart's `NetworkInterface` exposes an interface's IP address but not its
/// subnet mask, so the *real* prefix isn't directly available without a
/// platform channel. On Windows this beacon shells out to `ipconfig` (already
/// present on every Windows machine, no extra dependency) and parses each
/// adapter's "Subnet Mask" line next to its "IPv4 Address" line, then
/// computes that interface's exact broadcast address
/// (`ip | ~mask`) -- not a blind `/24` guess. On non-Windows platforms
/// (Android) there is no equivalent zero-dependency way to read the netmask,
/// so this beacon falls back to the global broadcast address only for those
/// interfaces; see `docs/MANUAL_TODO.md` for that limitation.
///
/// The socket replaces itself. After the phone dozed (on-device, 2026-10-08)
/// no beacon reached the headset while TCP 8787 still answered, and the cause
/// is unknown. So a tick that no target accepted swaps the socket at once, and
/// it is swapped every [beaconRebindEveryTicks] ticks anyway.
class UdpBeacon {
  /// [bindSocket] and [targets] let a test run the beacon without a real socket.
  new({
    required this.hubId,
    required this.port,
    required this.name,
    this.beaconPort = 8788,
    Future<RawDatagramSocket> Function()? bindSocket,
    List<InternetAddress>? targets,
  }) : _bindSocket = bindSocket ?? _bindAnyIpv4,
       _fixedTargets = targets;

  final String hubId;
  final int port;
  final String name;
  final int beaconPort;
  final Future<RawDatagramSocket> Function() _bindSocket;
  final List<InternetAddress>? _fixedTargets;

  RawDatagramSocket? _socket;
  Timer? _timer;
  int _ticksOnSocket = 0;

  Future<void> start() async {
    _socket = await _bindBroadcast();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => sendBeacon());
    unawaited(sendBeacon());
  }

  Future<void> stop() async {
    _timer?.cancel();
    _timer = null;
    _socket?.close();
    _socket = null;
  }

  /// One beacon round, run by the timer every second. Exposed (not private)
  /// so `test/hub/udp_beacon_test.dart` can drive it without waiting on real
  /// time.
  Future<void> sendBeacon() async {
    if (_timer == null) return;
    final message = utf8.encode(jsonEncode({'opus_hub': 1, 'hub_id': hubId, 'port': port, 'name': name}));

    final targets = await _targets();
    // stop() may have run while the targets were looked up.
    if (_timer == null) return;
    final socket = _socket ?? await _replaceSocket();
    if (socket == null) return;

    var accepted = 0;
    for (final target in targets) {
      try {
        if (socket.send(message, target, beaconPort) > 0) accepted++;
      } catch (_) {
        // A single unreachable/disabled interface shouldn't stop the beacon.
      }
    }

    // Nothing was accepted, or the socket is old: swap it before the next tick.
    _ticksOnSocket++;
    if (accepted == 0 || _ticksOnSocket >= beaconRebindEveryTicks) await _replaceSocket();
  }

  /// Binds a new broadcast socket; the tick count starts again for it.
  Future<RawDatagramSocket> _bindBroadcast() async {
    final socket = await _bindSocket();
    socket.broadcastEnabled = true;
    _ticksOnSocket = 0;
    return socket;
  }

  /// Closes the current socket and swaps in a new one. Null when the bind
  /// failed or stop() ran meanwhile; the next tick then tries again.
  Future<RawDatagramSocket?> _replaceSocket() async {
    _socket?.close();
    _socket = null;
    try {
      final fresh = await _bindBroadcast();
      if (_timer == null) {
        // stop() ran while it was binding, so do not keep it.
        fresh.close();
        return null;
      }
      return _socket = fresh;
    } on Exception {
      return null;
    }
  }

  /// The injected targets, else the global broadcast address plus the subnet
  /// broadcast address of each interface.
  Future<Iterable<InternetAddress>> _targets() async =>
      _fixedTargets ?? {InternetAddress('255.255.255.255'), ...await _subnetBroadcastAddresses()};

  /// Real per-interface broadcast addresses computed from `ipconfig` on
  /// Windows; empty on other platforms (global broadcast above still runs).
  Future<List<InternetAddress>> _subnetBroadcastAddresses() async {
    if (Platform.isWindows) {
      try {
        final result = await Process.run('ipconfig', const ['/all']);
        return parseIpconfigBroadcasts(result.stdout as String);
      } catch (_) {
        return const [];
      }
    }
    // Android/other: Dart can't read the netmask, so a blind `/24` guess
    // (`x.y.z.255`) is wrong whenever the real network is wider -- confirmed
    // on-device 2026-09-19: the clinic phone's wlan0 is `172.18.226.79/20`
    // (real broadcast `172.18.239.255`), so a `/24` guess of
    // `172.18.226.255` never reached the PC on the same `172.18.228.x` Wi-Fi.
    // Since the true prefix is unknown, send the directed broadcast for
    // every plausible prefix length 16..24 (9 tiny (~40-byte) datagrams/s per
    // address, deduped) plus the global broadcast above -- one of them is
    // guaranteed to be the real subnet broadcast for any /16..../24 network.
    try {
      final interfaces = await NetworkInterface.list(type: InternetAddressType.IPv4, includeLoopback: false);
      final targets = <InternetAddress>{};
      for (final i in interfaces) {
        for (final a in i.addresses) {
          if (a.rawAddress.length == 4) {
            targets.addAll(directedBroadcastsForPrefixRange(a.rawAddress, 16, 24));
          }
        }
      }
      return targets.toList();
    } catch (_) {
      return const [];
    }
  }
}

Future<RawDatagramSocket> _bindAnyIpv4() => RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);

/// Every directed broadcast address for [ip] (4-byte IPv4) across prefix
/// lengths [minPrefix]..[maxPrefix] inclusive (deduped -- adjacent prefix
/// lengths often produce the same address once enough host bits are fixed).
/// Exposed for `test/hub/udp_beacon_test.dart`.
Iterable<InternetAddress> directedBroadcastsForPrefixRange(List<int> ip, int minPrefix, int maxPrefix) {
  final seen = <String>{};
  final result = <InternetAddress>[];
  for (var prefix = minPrefix; prefix <= maxPrefix; prefix++) {
    final hostBits = 32 - prefix;
    var mask = hostBits >= 32 ? 0 : (0xFFFFFFFF << hostBits) & 0xFFFFFFFF;
    final broadcastParts = List.generate(4, (i) {
      final maskByte = (mask >> (8 * (3 - i))) & 0xFF;
      return ip[i] | (~maskByte & 0xFF);
    });
    final addr = broadcastParts.join('.');
    if (seen.add(addr)) result.add(InternetAddress(addr));
  }
  return result;
}

/// Parses `ipconfig /all` output into broadcast addresses, one per adapter
/// block that has both an "IPv4 Address" and a "Subnet Mask" line. Exposed
/// (not private) so `test/hub/udp_beacon_test.dart` can verify the parsing
/// against real captured `ipconfig` output without needing Windows/a socket.
List<InternetAddress> parseIpconfigBroadcasts(String ipconfigOutput) {
  final results = <InternetAddress>[];
  String? pendingIp;
  for (final rawLine in ipconfigOutput.split('\n')) {
    final line = rawLine.trim();
    final ipMatch = RegExp(r'IPv4 Address[.\s]*:\s*([\d.]+)').firstMatch(line);
    if (ipMatch != null) {
      pendingIp = ipMatch.group(1);
      continue;
    }
    final maskMatch = RegExp(r'Subnet Mask[.\s]*:\s*([\d.]+)').firstMatch(line);
    if (maskMatch != null && pendingIp != null) {
      final broadcast = _computeBroadcast(pendingIp, maskMatch.group(1)!);
      if (broadcast != null) results.add(broadcast);
      pendingIp = null;
    }
  }
  return results;
}

InternetAddress? _computeBroadcast(String ip, String mask) {
  final ipParts = ip.split('.').map(int.tryParse).toList();
  final maskParts = mask.split('.').map(int.tryParse).toList();
  if (ipParts.length != 4 || maskParts.length != 4 || ipParts.contains(null) || maskParts.contains(null)) {
    return null;
  }
  final broadcastParts = List.generate(4, (i) => ipParts[i]! | (~maskParts[i]! & 0xFF));
  return InternetAddress(broadcastParts.join('.'));
}
