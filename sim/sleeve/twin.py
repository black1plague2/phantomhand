"""Twin-lite: software fake of Phantom Hand Node A (haptic, SLEEVE_001) and Node B (bio, CHETNA_BIO_001).

Spec: docs/agent-briefs/ph/PRD_v2.md section 9, 03-SPEC.md section 4, 02-RULES.md section 4.2. Behaviour and CLI:
sim/sleeve/README.md. Stdlib only (threads + UDP sockets).

Why threads, not asyncio: on Windows asyncio.sleep() is quantised to ~15 ms, which would smear the 8 ms ack
latency and the 100 Hz stream. A 2 ms tick thread using time.sleep() keeps them honest.

Design: `Twin` is a pure, clock-injected core (`step(now_ms)`, `on_datagram(...)`, `control(...)`). `TwinServer`
binds sockets and drives the core with real time. Tests drive the core with a fake clock for the
limits/duty/watchdog/expiry maths and use the server for the real-UDP paths.
"""

from __future__ import annotations

import argparse
import heapq
import ipaddress
import json
import math
import random
import re
import socket
import subprocess
import sys
import threading
import time
from typing import Any, Callable, Dict, List, Optional, Sequence, Tuple

FIRMWARE_VERSION = "0.5.0"
NODE_A_ID = "SLEEVE_001"
NODE_B_ID = "CHETNA_BIO_001"

# --dialect: "reference" = the reference firmware (opus_sleeve, 03-SPEC); "team" = the electronics team's own sketches
# (contracts/HAPTIC_PROTOCOL.md v1.3, docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md section A). The default never changes.
DIALECT_REFERENCE = "reference"
DIALECT_TEAM = "team"
TEAM_NODE_A_ID = "CHETNA_HAPTIC_001"
TEAM_CHUNK_SAMPLES = 4        # Node B team firmware: 4 envelope values per packet, 25 packets/s

# Firmware limits, mirrored from 02-RULES.md 4.2 / 03-SPEC.md 4 (Node A v0.5.0).
MOTOR_CHANNELS = 4            # addressable (schema enum 0..3)
MOTOR_COUNT = 2               # fitted
MAX_INTENSITY = 150           # PWM cap (3 V motors on 5 V)
MIN_DURATION_MS = 50
MAX_DURATION_MS = 400
MIN_CUE_GAP_MS = 100          # per motor, start-to-start
DUTY_WINDOW_MS = 10000
DUTY_LIMIT_PCT = 50
WATCHDOG_MS = 2000
SUB_MAX = 3
SUB_EXPIRY_MS = 5000
DISPLAY_MAX_CHARS = 12
DISPLAY_MAX_PER_S = 2
VALID_PATTERNS = ("pulse", "continuous", "buzz", "ramp", "double_tap")

STREAM_PERIOD_MS = 10.0       # 100 Hz (IMU packets and EMG envelope samples)
CHUNK_SAMPLES = 10            # sensor_chunk = 100 ms
STATUS_PERIOD_MS = 1000.0
DISCOVERY_PERIOD_MS = 1000.0

# Node B signal model (raw_adc units of the envelope).
EMG_BASELINE = 420.0
EMG_SD = 6.0
EMG_BURST_K_SD = 3.0
EMG_BURST_MIN_SAMPLES = 3     # 30 ms at 100 Hz
EMG_BURST_END_SAMPLES = 2
EMG_BURST_MAX_SAMPLES = 150
ARTEFACT_GAIN = 45.0          # extra mean at intensity 150 while a pulse (+50 ms) is on
ARTEFACT_SD = 10.0
ARTEFACT_TAIL_MS = 50.0
FLINCH_EMG_DELAY_MS = 120.0
FLINCH_IMU_DELAY_MS = 150.0
FLINCH_EMG_DUR_MS = 350.0
FLINCH_EMG_MULT = 6.0
SQUEEZE_DUR_MS = 1000.0
SQUEEZE_MULT = 4.0

# Node A signal model (m/s2, rad/s).
IMU_ACCEL_SD = 0.05
IMU_GYRO_SD = 0.01
VIBRATION_ACCEL_SD = 1.0      # at intensity 150
JOLT_ACCEL_X = 25.0
JOLT_ACCEL_Z = 12.0
JOLT_GYRO = 3.0
JOLT_TAU_MS = 60.0
JOLT_HZ = 9.0
JOLT_DUR_MS = 400.0


def _num(v: Any) -> bool:
    # finite only: json.loads accepts NaN/Infinity, and int() of those used to raise inside the receive thread
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def lan_ip() -> str:
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("10.255.255.255", 1))
        return s.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        s.close()


# ---- LAN beacons. A discovery receiver (Unity's DiscoveryHub) takes the node's address from the datagram's SOURCE, and
# on a multi-homed PC (Wi-Fi + VPN + Hyper-V + VirtualBox) the limited broadcast 255.255.255.255 leaves through whichever
# adapter Windows prefers. So when the twin is not loopback-only it also announces on every local network, from a socket
# bound to that interface's own address. Stdlib only; every step is best effort and may never stop the twin.

def is_loopback_host(host: str) -> bool:
    h = (host or "").strip().lower()
    return h == "localhost" or h.startswith("127.")


def _ipv4(s: str) -> Optional[int]:
    try:
        return int(ipaddress.IPv4Address(s))
    except ValueError:
        return None


def _is_netmask(n: int) -> bool:
    inv = ~n & 0xFFFFFFFF
    return n != 0 and (inv & (inv + 1)) == 0


_QUAD = re.compile(r"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})(?![\d.])")
_IP_ADDR = re.compile(r"\binet\s+(\d{1,3}(?:\.\d{1,3}){3})/(\d{1,2})\b")                       # ip -4 -o addr show
_IFCONFIG = re.compile(r"\binet\s+(?:addr:)?(\d{1,3}(?:\.\d{1,3}){3})\s.*?(?:netmask\s+|Mask:)"
                       r"(0x[0-9a-fA-F]{8}|\d{1,3}(?:\.\d{1,3}){3})")                            # ifconfig (macOS, net-tools)


def parse_ipv4_interfaces(text: str) -> List[Tuple[str, str]]:
    """(address, netmask) of every IPv4 interface address in the text of `ipconfig` (Windows), `ip -4 -o addr` (Linux)
    or `ifconfig` (macOS, BSD). Language independent: for ipconfig an address line is paired with the next line whose
    only value is a valid netmask; the (localised) labels are never read. Anything else in, [] out; never raises."""
    out: List[Tuple[str, str]] = []
    last: Optional[str] = None
    for line in str(text or "").splitlines():
        m = _IP_ADDR.search(line)
        if m:
            bits = int(m.group(2))
            if 0 < bits <= 32 and _ipv4(m.group(1)) is not None:
                out.append((m.group(1), str(ipaddress.IPv4Address((0xFFFFFFFF << (32 - bits)) & 0xFFFFFFFF))))
            continue
        m = _IFCONFIG.search(line)
        if m:
            raw = m.group(2)
            mask = int(raw, 16) if raw.lower().startswith("0x") else _ipv4(raw)
            if mask is not None and _ipv4(m.group(1)) is not None and _is_netmask(mask):
                out.append((m.group(1), str(ipaddress.IPv4Address(mask))))
            continue
        quads = _QUAD.findall(line)
        if len(quads) != 1:
            continue
        n = _ipv4(quads[0])
        if n is None:
            continue
        if _is_netmask(n):
            if last is not None:
                out.append((last, quads[0]))
                last = None
        else:
            last = quads[0]
    return out


def local_ipv4_interfaces() -> List[Tuple[str, str]]:
    """(address, netmask) of this PC's IPv4 interfaces, read from the OS tools; [] when nothing could be read."""
    cmds = [["ipconfig"]] if sys.platform.startswith("win") else [["ip", "-4", "-o", "addr", "show"], ["ifconfig"]]
    for cmd in cmds:
        try:
            found = parse_ipv4_interfaces(subprocess.run(cmd, capture_output=True, text=True, errors="replace",
                                                         timeout=5).stdout)
        except Exception:        # tool missing, timeout, odd output: try the next one, a guess must never crash the twin
            continue
        if found:
            return found
    return []


def beacon_targets(ifaces: Sequence[Tuple[str, str]], host: str, extra: Sequence[str] = ()
                   ) -> List[Tuple[Optional[str], str]]:
    """(source address to bind or None, destination) of the beacons to add for a twin bound to `host`: the directed
    broadcast of every usable interface (loopback, link-local, /31 and /32 skipped; only the bound address when `host`
    is a single address) plus each `extra` destination (--broadcast-addr: a directed broadcast, or a peer's own address
    when the network drops broadcasts), sent from the interface whose subnet holds it (None = let the OS choose)."""
    nets: List[Tuple[str, "ipaddress.IPv4Network"]] = []
    for ip, mask in ifaces:
        try:
            ifc = ipaddress.IPv4Interface(f"{ip}/{mask}")
        except ValueError:
            continue
        if ifc.ip.is_loopback or ifc.ip.is_unspecified or ifc.ip.is_link_local or ifc.network.prefixlen >= 31:
            continue
        if host not in ("0.0.0.0", "") and str(ifc.ip) != host:
            continue
        nets.append((str(ifc.ip), ifc.network))
    out: List[Tuple[Optional[str], str]] = [(ip, str(net.broadcast_address)) for ip, net in nets]
    for e in extra:
        try:
            dst = ipaddress.IPv4Address(str(e).strip())
        except ValueError:
            continue
        out.append((next((ip for ip, net in nets if dst in net), None), str(dst)))
    return list(dict.fromkeys(out))


def _with_ip(data: bytes, ip: str) -> bytes:
    """A beacon as announced from interface `ip`: its own `ip` field (when the dialect has one) names that interface."""
    try:
        msg = json.loads(data.decode("utf-8"))
        if not isinstance(msg, dict) or "ip" not in msg:
            return data
        msg["ip"] = ip
        return json.dumps(msg, separators=(",", ":")).encode("utf-8")
    except (ValueError, UnicodeDecodeError):
        return data


class EventLog:
    """JSON-lines log. `target` is a path, "-" (stdout) or None. Always keeps `records` when keep=True."""

    def __init__(self, target: Optional[str] = None, keep: bool = False):
        self._lock = threading.Lock()
        self._fp = None
        self.keep = keep
        self.records: List[Dict[str, Any]] = []
        if target == "-":
            self._fp = sys.stdout
        elif target:
            self._fp = open(target, "a", encoding="utf-8")

    def write(self, rec: Dict[str, Any]) -> None:
        with self._lock:
            if self.keep:
                self.records.append(rec)
            if self._fp:
                self._fp.write(json.dumps(rec, separators=(",", ":")) + "\n")
                self._fp.flush()

    def close(self) -> None:
        with self._lock:
            if self._fp and self._fp is not sys.stdout:
                self._fp.close()
            self._fp = None


class Node:
    kind = ""
    device_id = ""
    has_status = True        # the 1 Hz `status` message to subscribers (the team firmware has none)

    def __init__(self, twin: "Twin", port: int):
        self.twin = twin
        self.port = port
        self.powered = True
        self.boot_ms = 0.0
        self.subs: Dict[Tuple[str, int], float] = {}
        self.stats: Dict[str, int] = {"rx": 0, "rx_dropped": 0, "rx_invalid": 0, "tx": 0, "tx_lost": 0,
                                      "sub_rejected": 0, "stream_packets": 0}
        self.next_sample = 0.0
        self.next_status = 0.0
        self.next_discovery = 0.0

    # -- helpers
    def dev_ms(self, now: float) -> int:
        return int(now - self.boot_ms)

    def power_on(self, now: float) -> None:
        self.powered = True
        self.boot_ms = now
        self.subs = {}
        self.next_sample = now + STREAM_PERIOD_MS
        self.next_status = now + STATUS_PERIOD_MS
        self.next_discovery = now
        self._reset(now)

    def power_off(self, now: float) -> None:
        self.powered = False
        self.subs = {}
        self._reset(now)

    def _reset(self, now: float) -> None:
        pass

    def live_subs(self, now: float) -> List[Tuple[str, int]]:
        return [a for a, exp in self.subs.items() if exp > now]

    def subscribe(self, addr: Tuple[str, int], now: float) -> bool:
        if addr in self.subs or len(self.live_subs(now)) < SUB_MAX:
            self.subs[addr] = now + SUB_EXPIRY_MS
            return True
        self.stats["sub_rejected"] += 1
        self.twin.event(self, "subscribe_rejected", now, addr=list(addr))
        return False

    def expire_subs(self, now: float) -> None:
        for a in [a for a, exp in self.subs.items() if exp <= now]:
            del self.subs[a]
            self.twin.event(self, "sub_expired", now, addr=list(a))

    def broadcast_stream(self, msg: Dict[str, Any], now: float) -> None:
        for a in self.live_subs(now):
            self.twin.tx(self, msg, a, now, stream=True)
            self.stats["stream_packets"] += 1

    def discovery(self, now: float) -> Dict[str, Any]:
        raise NotImplementedError

    def status_payload(self, now: float) -> Dict[str, Any]:
        raise NotImplementedError

    def on_rx(self, addr: Tuple[str, int], now: float) -> None:
        """Called for every parsed datagram a powered node accepts, before on_message (team dialect: telemetry target)."""

    def on_message(self, msg: Any, addr: Tuple[str, int], now: float) -> None:
        raise NotImplementedError

    def step(self, now: float) -> None:
        raise NotImplementedError

    def common_periodic(self, now: float) -> None:
        self.expire_subs(now)
        if self.has_status and now >= self.next_status:
            self.next_status = now + STATUS_PERIOD_MS
            self.broadcast_stream(self.status_payload(now), now)
        if now >= self.next_discovery:
            self.next_discovery = now + DISCOVERY_PERIOD_MS
            self.twin.broadcast(self, self.discovery(now))

    # -- shared control-plane handling
    def handle_common(self, typ: str, msg: Dict[str, Any], addr: Tuple[str, int], now: float) -> bool:
        if typ == "subscribe":
            self.subscribe(addr, now)
            return True
        if typ == "keepalive":
            # contracts v0.2.1: a known message, never counted as invalid. It feeds the watchdog (every datagram does),
            # refreshes a live subscriber like ping, and gets no reply.
            if addr in self.subs:
                self.subs[addr] = now + SUB_EXPIRY_MS
            return True
        if typ == "status_request":
            self.twin.tx(self, self.status_payload(now), addr, now)
            return True
        return False


class NodeA(Node):
    kind = "haptic"
    device_id = NODE_A_ID
    acks_garbage = True                   # an unparseable datagram gets an error ack (the team firmware stays silent)
    display_keys = ("text", "mode")       # which `display` field wins when both are present (the team firmware reads `mode`)
    imu_prefix: Optional[str] = None      # None = every IMU channel; the team firmware streams accel only

    def __init__(self, twin: "Twin", port: int):
        super().__init__(twin, port)
        self.rng = random.Random(twin.seed * 1009 + 11)
        self._reset(0.0)

    def _reset(self, now: float) -> None:
        self.motors = [self._new_motor() for _ in range(MOTOR_CHANNELS)]
        self.stuck: set = set()
        self.armed = False
        self.last_rx = now
        self.display_times: List[float] = []
        self.display_text = ""
        self.jolts: List[float] = []
        self.pulse_windows: List[Dict[str, float]] = []   # for the Node B artefact model
        self.last_cue_id: Optional[str] = None
        self.watchdog_events = 0
        self.stroke_count = 0

    @staticmethod
    def _new_motor() -> Dict[str, Any]:
        return {"active": False, "cmd_ms": 0.0, "on_ms": 0.0, "off_ms": 0.0, "intensity": 0,
                "last_start": None, "intervals": [], "window": None}

    # ---- queries
    def duty_pct(self, i: int, now: float) -> float:
        lo = now - DUTY_WINDOW_MS
        total = 0.0
        for s, e in self.motors[i]["intervals"]:
            e = min(e, now)
            if e > lo:
                total += e - max(s, lo)
        return 100.0 * total / DUTY_WINDOW_MS

    def vibration(self, t: float) -> float:
        """Max applied intensity of any motor physically vibrating at time t (after spin-up, before off)."""
        best = 0.0
        for w in self.pulse_windows:
            if w["on"] <= t < w["off"]:
                best = max(best, w["intensity"])
        return best

    def any_active(self) -> bool:
        return any(m["active"] for m in self.motors)

    # ---- motor control
    def _stop_motor(self, i: int, now: float, reason: str) -> None:
        m = self.motors[i]
        if not m["active"]:
            return
        m["active"] = False
        if m["intervals"]:
            s, e = m["intervals"][-1]
            m["intervals"][-1] = (s, min(e, now) if reason != "ended" else e)
        w = m["window"]
        if w is not None and reason != "ended":
            w["off"] = min(w["off"], now)
            w["end"] = min(w["end"], now + ARTEFACT_TAIL_MS)
        m["off_ms"] = min(m["off_ms"], now) if reason != "ended" else m["off_ms"]
        if reason != "ended":
            self.twin.event(self, "motor_off", now, motor=i, reason=reason)

    def stop_all(self, now: float, reason: str) -> None:
        for i in range(MOTOR_CHANNELS):
            self._stop_motor(i, now, reason)

    # ---- messages
    def ack_msg(self, cue_id: str, status: str, rx_dev: int, start_dev: int, now: float, motor: int = -1,
                err: Optional[Tuple[str, str]] = None) -> Dict[str, Any]:
        d: Dict[str, Any] = {"type": "ack", "cue_id": cue_id, "status": status,
                             "timestamp_ms": self.dev_ms(now), "received_ms": rx_dev}
        if start_dev:
            d["vibration_start_ms"] = start_dev
        if motor >= 0:
            d["motor"] = motor
        if err:
            d["error_code"], d["error_message"] = err
        d["v"] = 1
        d["id"] = f"ack-{self.twin.next_seq()}"
        d["ts_ms"] = self.dev_ms(now)
        d["ack_id"] = cue_id
        d["ok"] = status in ("accepted", "executed")
        return d

    def status_payload(self, now: float) -> Dict[str, Any]:
        d: Dict[str, Any] = {"type": "status", "device_id": self.device_id, "device_kind": "haptic",
                             "battery_pct": None, "connected": True}
        for i in range(MOTOR_CHANNELS):
            fitted = i < MOTOR_COUNT
            d[f"motor_{i}"] = {"available": fitted, "location": ["wrist", "forearm", "tbd_2", "tbd_3"][i],
                               "temperature_c": None,
                               "duty_pct": round(self.duty_pct(i, now)) if fitted else 0,
                               "active": bool(self.motors[i]["active"]) if fitted else False}
        d.update({"motor_count": MOTOR_COUNT, "motor_channels": MOTOR_CHANNELS, "imu_available": True,
                  "last_cue_id": self.last_cue_id, "timestamp_ms": self.dev_ms(now), "v": 1,
                  "id": f"st-{self.twin.next_seq()}", "ts_ms": self.dev_ms(now), "motors_ok": True,
                  "imu_ok": True, "fw": FIRMWARE_VERSION, "firmware_version": FIRMWARE_VERSION,
                  "subscribers": len(self.live_subs(now))})
        return d

    def discovery(self, now: float) -> Dict[str, Any]:
        return {"type": "device_discovery", "device_id": self.device_id, "device_kind": "haptic",
                "device_name": "Phantom Hand Node A (twin)", "firmware_version": FIRMWARE_VERSION,
                "protocol_version": "1.0", "ip": self.twin.ip, "command_port": self.port,
                "status": "available", "motor_count": MOTOR_COUNT, "motor_channels": MOTOR_CHANNELS,
                "timestamp_ms": self.dev_ms(now),
                # legacy hello dialect (HapticClient), same datagram
                "opus_haptic": 1, "port": self.port, "fw": FIRMWARE_VERSION}

    def on_message(self, msg: Any, addr: Tuple[str, int], now: float) -> None:
        if not isinstance(msg, dict):
            self.twin.tx(self, self.ack_msg("", "error", self.dev_ms(now), 0, now,
                                            err=("BAD_JSON", "not a JSON object")), addr, now,
                         delay=self.twin.ack_delay())
            self.stats["rx_invalid"] += 1
            return
        typ = msg.get("type")
        if self.handle_common(typ or "", msg, addr, now):
            return
        if typ is None or typ == "haptic":
            self.handle_haptic(msg, addr, now)
        elif typ == "cue":   # legacy v1 semantic envelope -> device command (as the firmware does)
            cmd = {"cue_id": msg.get("id") or "", "motor": msg.get("motor", 0),
                   "intensity": int(255 * float(msg.get("intensity", 0.5))),
                   "duration_ms": msg.get("duration_ms", 200), "pattern": msg.get("pattern", "pulse"),
                   "cue": msg.get("cue")}
            self.handle_haptic(cmd, addr, now)
        elif typ == "ping":
            pid = msg.get("id") or msg.get("cue_id") or ""
            if addr in self.subs:
                self.subs[addr] = now + SUB_EXPIRY_MS
            self.twin.tx(self, self.ack_msg(str(pid), "accepted", self.dev_ms(now), 0, now), addr, now,
                         delay=self.twin.ack_delay())
        elif typ == "display":
            self.handle_display(msg, now)
        elif typ == "stop":
            if msg.get("motor") is None:
                self.stop_all(now, "stop")
            elif isinstance(msg.get("motor"), int) and 0 <= msg["motor"] < MOTOR_CHANNELS:
                self._stop_motor(msg["motor"], now, "stop")
        elif typ == "config":
            if not msg.get("enabled", True):
                self.stop_all(now, "config_off")
        else:
            self.stats["rx_invalid"] += 1
            self.twin.event(self, "unknown_type", now, type=str(typ))

    def handle_display(self, msg: Dict[str, Any], now: float) -> None:
        # `text` and/or `mode` (contracts v0.2.1: the reference firmware reads text, the team firmware mode); every
        # field present must be a string of <= 12 chars; when both are present `display_keys` says which one wins
        fields = [msg[k] for k in self.display_keys if msg.get(k) is not None]
        text = fields[0] if fields else None
        bad = not fields or any(not isinstance(f, str) or len(f) > DISPLAY_MAX_CHARS for f in fields)
        self.display_times = [t for t in self.display_times if now - t < 1000.0]
        if bad or len(self.display_times) >= DISPLAY_MAX_PER_S:
            self.twin.event(self, "display_rejected", now, text=str(text)[:40], reason="text" if bad else "rate")
            self.stats["rx_invalid"] += 1
            return
        self.display_times.append(now)
        self.display_text = text
        self.twin.emit({"event": "oled", "text": text})
        self.twin.event(self, "oled", now, text=text)

    def extra_reject(self, motor: Dict[str, Any], intensity: int, now: float) -> Optional[Tuple[str, str]]:
        """Dialect hook after the shared checks: (error_code, text) to reject the cue, None to play it."""
        return None

    def handle_haptic(self, msg: Dict[str, Any], addr: Tuple[str, int], now: float) -> None:
        tw = self.twin
        rx_dev = self.dev_ms(now)
        cue_id = msg.get("cue_id") or f"auto-{tw.next_seq()}"
        self.armed = True
        delay = tw.ack_delay()
        rec: Dict[str, Any] = {"ev": "stroke", "node": "A", "t_ms": tw.wall(now), "cue_id": cue_id,
                               "cue": msg.get("cue"), "recv_ts_ms": tw.wall(now),
                               "play_at_ms": msg.get("play_at_ms"), "spinup_ms": tw.spinup_ms,
                               "motor_on_ms": None, "motor_off_ms": None,
                               "intensity_in": msg.get("intensity"), "intensity_applied": None,
                               "duration_in": msg.get("duration_ms")}

        def reject(code: str, text: str, motor: int = -1) -> None:
            ack = self.ack_msg(cue_id, "rejected", rx_dev, 0, now, motor, (code, text))
            due, lost = tw.tx(self, ack, addr, now, delay=delay)
            rec.update({"status": "rejected", "error_code": code, "motor": motor if motor >= 0 else msg.get("motor"),
                        "ack_send_ts_ms": tw.wall(due), "ack_latency_ms": round(due - now, 2), "ack_lost": lost})
            tw.log.write(rec)

        if not (_num(msg.get("motor")) and _num(msg.get("intensity")) and _num(msg.get("duration_ms"))):
            return reject("MISSING_FIELD", "motor, intensity and duration_ms are required")
        requested = int(msg["motor"])
        intensity = int(msg["intensity"])
        duration = int(msg["duration_ms"])
        pattern = msg.get("pattern") or "pulse"
        if requested < 0 or requested >= MOTOR_CHANNELS:
            return reject("INVALID_MOTOR", "motor must be 0-3")
        if requested >= MOTOR_COUNT:
            return reject("MOTOR_UNAVAILABLE", "requested motor channel is not fitted", requested)
        if pattern not in VALID_PATTERNS:
            return reject("INVALID_PATTERN", "pattern must be pulse|continuous|buzz|ramp|double_tap", requested)
        m = self.motors[requested]
        if m["last_start"] is not None and now - m["last_start"] < MIN_CUE_GAP_MS:
            return reject("CUE_GAP", "commands closer than MIN_CUE_GAP_MS", requested)
        if self.duty_pct(requested, now) >= DUTY_LIMIT_PCT:
            return reject("DUTY_CYCLE_LIMIT", "motor duty cycle limit reached, cooling down", requested)
        bad = self.extra_reject(m, intensity, now)
        if bad:
            return reject(bad[0], bad[1], requested)

        applied = max(0, min(MAX_INTENSITY, intensity))
        dur = max(MIN_DURATION_MS, min(MAX_DURATION_MS, duration))
        if m["active"]:                      # retrigger: close the running pulse at now
            self._stop_motor(requested, now, "retrigger")
        on = now + tw.spinup_ms
        off = now + dur
        window = {"on": on, "off": off, "end": off + ARTEFACT_TAIL_MS, "intensity": float(applied)}
        self.pulse_windows.append(window)
        m.update({"active": True, "cmd_ms": now, "on_ms": on, "off_ms": off, "intensity": applied,
                  "last_start": now, "window": window})
        m["intervals"].append((now, off))
        m["intervals"] = [(s, e) for s, e in m["intervals"] if e > now - DUTY_WINDOW_MS]
        self.last_cue_id = cue_id
        self.stroke_count += 1
        ack = self.ack_msg(cue_id, "executed", rx_dev, rx_dev, now, requested)
        due, lost = tw.tx(self, ack, addr, now, delay=delay)
        rec.update({"status": "executed", "motor": requested, "motor_on_ms": tw.wall(on),
                    "motor_off_ms": tw.wall(off), "intensity_applied": applied, "duration_applied": dur,
                    "ack_send_ts_ms": tw.wall(due), "ack_latency_ms": round(due - now, 2), "ack_lost": lost})
        tw.log.write(rec)

    # ---- time
    def step(self, now: float) -> None:
        if not self.powered:
            return
        for i, m in enumerate(self.motors):
            if m["active"] and now >= m["off_ms"]:
                if i in self.stuck:      # latched driver: keeps vibrating and counting duty until stopped
                    w = m["window"]
                    w["off"] = now + STREAM_PERIOD_MS
                    w["end"] = w["off"] + ARTEFACT_TAIL_MS
                    s0, _ = m["intervals"][-1]
                    m["intervals"][-1] = (s0, now + STREAM_PERIOD_MS)
                    m["off_ms"] = now + STREAM_PERIOD_MS
                else:
                    self._stop_motor(i, now, "ended")
        if self.armed and now - self.last_rx > self.twin.watchdog_ms:
            was = [i for i, m in enumerate(self.motors) if m["active"]]
            self.stop_all(now, "watchdog")
            self.armed = False
            self.watchdog_events += 1
            self.twin.event(self, "watchdog", now, motors_forced_off=was)
        self.pulse_windows = [w for w in self.pulse_windows if w["end"] > now - 2000.0]
        self.jolts = [j for j in self.jolts if now - j < JOLT_DUR_MS + 2000.0]
        if now - self.next_sample > 1000.0:      # stalled thread: do not burst-catch-up
            self.next_sample = now
        while self.next_sample <= now:
            self.emit_sample(self.next_sample, now)
            self.next_sample += STREAM_PERIOD_MS
        self.common_periodic(now)

    def imu_values(self, t: float) -> Dict[str, Tuple[float, str]]:
        r = self.rng
        ax, ay, az = r.gauss(0, IMU_ACCEL_SD), r.gauss(0, IMU_ACCEL_SD), 9.81 + r.gauss(0, IMU_ACCEL_SD)
        gx, gy, gz = r.gauss(0, IMU_GYRO_SD), r.gauss(0, IMU_GYRO_SD), r.gauss(0, IMU_GYRO_SD)
        vib = self.vibration(t)
        if vib > 0:
            s = VIBRATION_ACCEL_SD * vib / MAX_INTENSITY
            ax += r.gauss(0, s)
            ay += r.gauss(0, s)
            az += r.gauss(0, s)
            gx += r.gauss(0, 0.1 * vib / MAX_INTENSITY)
        for j in self.jolts:
            dt = t - j
            if 0 <= dt <= JOLT_DUR_MS:
                k = math.exp(-dt / JOLT_TAU_MS) * math.sin(2 * math.pi * JOLT_HZ * dt / 1000.0)
                ax += JOLT_ACCEL_X * k
                az += JOLT_ACCEL_Z * k
                gx += JOLT_GYRO * k
        return {"imu_accel_x": (ax, "m/s2"), "imu_accel_y": (ay, "m/s2"), "imu_accel_z": (az, "m/s2"),
                "imu_gyro_x": (gx, "rad/s"), "imu_gyro_y": (gy, "rad/s"), "imu_gyro_z": (gz, "rad/s"),
                "imu_temperature": (30.0 + 0.1 * math.sin(t / 5000.0), "degC")}

    def emit_sample(self, t: float, now: float) -> None:
        if not self.live_subs(now):
            return
        vals = self.imu_values(t)
        msg = {"type": "sensor_data", "device_id": self.device_id, "timestamp_ms": self.dev_ms(t),
               "sensors": {k: {"value": round(v, 4), "unit": u, "status": "ok"} for k, (v, u) in vals.items()
                           if self.imu_prefix is None or k.startswith(self.imu_prefix)}}
        self.broadcast_stream(msg, now)


class NodeB(Node):
    kind = "bio"
    device_id = NODE_B_ID
    chunk_samples = CHUNK_SAMPLES         # envelope values per sensor_chunk (the team firmware sends 4 per packet)
    sends_bursts = True                   # emg_burst messages to subscribers (the team firmware has none)
    stamps_last = False                   # sensor_chunk.timestamp_ms = device time of the LAST value, not the first (team firmware)

    def __init__(self, twin: "Twin", port: int):
        super().__init__(twin, port)
        self.rng = random.Random(twin.seed * 1013 + 17)
        self._reset(0.0)

    def _reset(self, now: float) -> None:
        self.bursts: List[Tuple[float, float, float]] = []   # (start_ms, dur_ms, mult)
        self.buf: List[float] = []
        self.det_run = 0
        self.det_below = 0
        self.det_in = False
        self.det_onset = 0.0
        self.det_peak = 0.0
        self.det_suppressed = False
        self.chunks_sent = 0
        self.burst_events = 0

    def status_payload(self, now: float) -> Dict[str, Any]:
        return {"type": "status", "device_id": self.device_id, "device_kind": "bio",
                "firmware_version": FIRMWARE_VERSION, "battery_pct": None, "connected": True,
                "timestamp_ms": self.dev_ms(now), "subscribers": len(self.live_subs(now)),
                "chunks_sent": self.chunks_sent}

    def discovery(self, now: float) -> Dict[str, Any]:
        return {"type": "device_discovery", "device_id": self.device_id, "device_kind": "bio",
                "firmware_version": FIRMWARE_VERSION, "command_port": self.port, "status": "available",
                "motor_count": 0, "timestamp_ms": self.dev_ms(now), "ip": self.twin.ip}

    def on_message(self, msg: Any, addr: Tuple[str, int], now: float) -> None:
        if not isinstance(msg, dict):
            self.stats["rx_invalid"] += 1
            return
        typ = msg.get("type")
        if self.handle_common(typ or "", msg, addr, now):
            return
        if typ == "ping":
            if addr in self.subs:
                self.subs[addr] = now + SUB_EXPIRY_MS
            ack = {"type": "ack", "cue_id": str(msg.get("id") or ""), "ack_id": str(msg.get("id") or ""),
                   "status": "accepted", "ok": True, "timestamp_ms": self.dev_ms(now)}
            self.twin.tx(self, ack, addr, now, delay=self.twin.ack_delay())
        else:    # Node B has no motors: anything else is ignored with a counter, never a command
            self.stats["rx_invalid"] += 1
            self.twin.event(self, "unknown_type", now, type=str(typ))

    def artefact_at(self, t: float) -> float:
        a = self.twin.A
        if a is None or not a.powered:
            return 0.0
        best = 0.0
        for w in a.pulse_windows:
            if w["on"] <= t < w["end"]:
                best = max(best, w["intensity"])
        return best

    def in_artefact(self, t: float) -> bool:
        return self.artefact_at(t) > 0

    def burst_excess(self, t: float) -> float:
        ex = 0.0
        for s, dur, mult in self.bursts:
            dt = t - s
            if 0 <= dt <= dur:
                attack, release = 30.0, 80.0
                shape = min(1.0, dt / attack, (dur - dt) / release if dur - dt < release else 1.0)
                ex = max(ex, (mult - 1.0) * EMG_BASELINE * max(0.0, shape))
        return ex

    def step(self, now: float) -> None:
        if not self.powered:
            return
        self.bursts = [b for b in self.bursts if now - (b[0] + b[1]) < 2000.0]
        if now - self.next_sample > 1000.0:
            self.next_sample = now
        while self.next_sample <= now:
            self.emit_sample(self.next_sample, now)
            self.next_sample += STREAM_PERIOD_MS
        self.common_periodic(now)

    def emit_sample(self, t: float, now: float) -> None:
        r = self.rng
        art = self.artefact_at(t)
        v = EMG_BASELINE + r.gauss(0, EMG_SD) + self.burst_excess(t)
        if art > 0:
            v += ARTEFACT_GAIN * art / MAX_INTENSITY + r.gauss(0, ARTEFACT_SD * art / MAX_INTENSITY)
        v = max(0.0, min(4095.0, v))
        self.detect(v, t, now)
        if not self.buf:
            self.buf_t0 = t  # PRD §9.3 / 03-SPEC §4: chunk timestamp_ms = device time of the FIRST sample (team: the LAST)
        self.buf.append(round(v, 1))
        if len(self.buf) >= self.chunk_samples:
            msg = {"type": "sensor_chunk", "device_id": self.device_id, "device_kind": "bio",
                   "timestamp_ms": self.dev_ms(t if self.stamps_last else self.buf_t0), "sample_rate_hz": 100,
                   "emg_envelope": self.buf,
                   "unit": "raw_adc", "status": "ok"}
            self.buf = []
            self.chunks_sent += 1
            self.broadcast_stream(msg, now)

    def detect(self, v: float, t: float, now: float) -> None:
        thr = EMG_BASELINE + EMG_BURST_K_SD * EMG_SD
        if v > thr:
            if not self.det_in:
                self.det_run += 1
                if self.det_run == 1:
                    self.det_onset = t
                    self.det_peak = v
                    self.det_suppressed = self.in_artefact(t)
                else:
                    self.det_peak = max(self.det_peak, v)
                if self.det_run >= EMG_BURST_MIN_SAMPLES:
                    self.det_in = True
                    self.det_below = 0
            else:
                self.det_peak = max(self.det_peak, v)
                self.det_below = 0
                self.det_run += 1
                if self.det_run >= EMG_BURST_MAX_SAMPLES:
                    self.finish_burst(now)
        else:
            if self.det_in:
                self.det_below += 1
                if self.det_below >= EMG_BURST_END_SAMPLES:
                    self.finish_burst(now)
            else:
                self.det_run = 0

    def finish_burst(self, now: float) -> None:
        if not self.det_suppressed:
            msg = {"type": "emg_burst", "device_id": self.device_id, "timestamp_ms": self.dev_ms(self.det_onset),
                   "peak": round(self.det_peak, 1), "baseline_rms": EMG_BASELINE}
            self.burst_events += 1
            if self.sends_bursts:
                self.broadcast_stream(msg, now)
            self.twin.event(self, "emg_burst", now, peak=msg["peak"], onset_dev_ms=msg["timestamp_ms"])
        else:
            self.twin.event(self, "emg_burst_suppressed", now)
        self.det_in = False
        self.det_run = 0
        self.det_below = 0


class _TeamLink:
    """What the two team-firmware nodes share (--dialect team; contracts HAPTIC_PROTOCOL v1.3): telemetry goes only to the
    sender of the LAST packet (one target, no subscriber list), no `status` message, every message type the team
    firmware does not document is ignored (counted in `ignored`, never an ack, never a motor command). The telemetry
    target is the sender's source address, or with `Twin.telemetry_port` (the real boards: 8790) the sender's IP on that
    FIXED port; acks always go to the source address."""
    has_status = False
    ignored = 0

    def on_rx(self, addr: Tuple[str, int], now: float) -> None:
        port = self.twin.telemetry_port
        self.subs = {(addr[0], port) if port > 0 else addr: now + SUB_EXPIRY_MS}


class TeamNodeA(_TeamLink, NodeA):
    """Node A as the team's `node_a_haptic` v0.5.0 behaves: bare cue commands (+ keepalive, stop, display), ack
    {type, device_id, cue_id, accepted, timestamp_ms} with accepted:false for a motor still running a pulse, the 100 ms
    gap, the 10 s duty budget or an invalid motor/intensity, accel-only sensor_data, `play_at_ms` ignored."""
    device_id = TEAM_NODE_A_ID
    acks_garbage = False
    display_keys = ("mode", "text")
    imu_prefix = "imu_accel"

    def ack_msg(self, cue_id: str, status: str, rx_dev: int, start_dev: int, now: float, motor: int = -1,
                err: Optional[Tuple[str, str]] = None) -> Dict[str, Any]:
        return {"type": "ack", "device_id": self.device_id, "cue_id": cue_id,
                "accepted": status in ("accepted", "executed"), "timestamp_ms": self.dev_ms(now)}

    def extra_reject(self, motor: Dict[str, Any], intensity: int, now: float) -> Optional[Tuple[str, str]]:
        if intensity < 0 or intensity > 255:
            return "INVALID_INTENSITY", "intensity must be 0-255"
        if motor["active"] and now < motor["off_ms"]:
            return "MOTOR_BUSY", "the motor is still running a previous pulse"
        return None

    def discovery(self, now: float) -> Dict[str, Any]:
        return {"type": "device_discovery", "device_id": self.device_id, "device_kind": "haptic",
                "firmware_version": FIRMWARE_VERSION, "command_port": self.port, "status": "available",
                "motor_count": MOTOR_COUNT, "timestamp_ms": self.dev_ms(now)}

    def on_message(self, msg: Any, addr: Tuple[str, int], now: float) -> None:
        if not isinstance(msg, dict):
            self.stats["rx_invalid"] += 1
            return
        typ = msg.get("type")
        if typ is None:
            self.handle_haptic(msg, addr, now)
        elif typ == "display":
            self.handle_display(msg, now)
        elif typ == "stop":
            self.stop_all(now, "stop")
        elif typ != "keepalive":        # keepalive: nothing to do, on_rx made the sender the target and the watchdog saw it
            self.ignored += 1


class TeamNodeB(_TeamLink, NodeB):
    """Node B as the team's `node_b_bio` v0.5.0 behaves: sensor_chunk with 4 values at 25 packets/s to the last sender,
    stamped with the device time of the chunk's LAST value (about 30 ms after the first), no status and no emg_burst
    messages. It understands keepalive only."""
    chunk_samples = TEAM_CHUNK_SAMPLES
    sends_bursts = False
    stamps_last = True

    def discovery(self, now: float) -> Dict[str, Any]:
        return {"type": "device_discovery", "device_id": self.device_id, "device_kind": "bio",
                "firmware_version": FIRMWARE_VERSION, "command_port": self.port, "status": "available",
                "motor_count": 0, "timestamp_ms": self.dev_ms(now)}

    def on_message(self, msg: Any, addr: Tuple[str, int], now: float) -> None:
        if not isinstance(msg, dict):
            self.stats["rx_invalid"] += 1
        elif msg.get("type") != "keepalive":
            self.ignored += 1


class Twin:
    """Clock-injected core. `send(kind, data, addr)` and `broadcast(kind, data)` do the I/O."""

    def __init__(self, kinds: Tuple[str, ...] = ("haptic",), seed: int = 1, port_offset: int = 0,
                 latency_ms: float = 8.0, jitter_ms: float = 4.0, spinup_ms: float = 30.0,
                 watchdog_s: float = 2.0, log: Optional[EventLog] = None,
                 now_fn: Callable[[], float] = lambda: time.monotonic() * 1000.0,
                 send: Optional[Callable[[str, bytes, Tuple[str, int]], None]] = None,
                 broadcast: Optional[Callable[[str, bytes], None]] = None,
                 emit: Optional[Callable[[Dict[str, Any]], None]] = None, ip: str = "127.0.0.1",
                 dialect: str = DIALECT_REFERENCE, telemetry_port: int = 0):
        if dialect not in (DIALECT_REFERENCE, DIALECT_TEAM):
            raise ValueError(f"unknown dialect {dialect!r} (reference | team)")
        if not 0 <= telemetry_port <= 65535:     # sendto() would raise OverflowError in the tick thread and silence the twin
            raise ValueError(f"telemetry_port {telemetry_port!r} is not a UDP port (0 = telemetry to the sender's source port)")
        self.dialect = dialect
        self.telemetry_port = telemetry_port     # team dialect only, see _TeamLink; the reference dialect never reads it
        self.kinds = tuple(kinds)
        self.seed = seed
        self.port_offset = port_offset
        self.latency_ms = latency_ms
        self.jitter_ms = jitter_ms
        self.spinup_ms = spinup_ms
        self.watchdog_ms = watchdog_s * 1000.0
        self.loss_pct = 0.0
        self.log = log or EventLog(None)
        self.now_fn = now_fn
        self._send = send or (lambda k, d, a: None)
        self._broadcast = broadcast or (lambda k, d: None)
        self._emit = emit or (lambda ev: None)
        self.ip = ip
        self.rng = random.Random(seed)
        self.quit_requested = False
        self._seq = 0
        self._pending: List[Tuple[float, int, str, bytes, Tuple[str, int]]] = []
        self.mono0 = now_fn()
        self.wall0 = time.time() * 1000.0
        team = dialect == DIALECT_TEAM
        self.A: Optional[NodeA] = (TeamNodeA if team else NodeA)(self, 8790 + port_offset) if "haptic" in kinds else None
        self.B: Optional[NodeB] = (TeamNodeB if team else NodeB)(self, 8792 + port_offset) if "bio" in kinds else None
        for n in self.nodes():
            n.power_on(self.mono0)

    # ---- plumbing
    ports = property(lambda s: {"haptic": 8790 + s.port_offset, "bio": 8792 + s.port_offset,
                                "discovery": 8791 + s.port_offset, "control": 8793 + s.port_offset})

    def nodes(self) -> List[Node]:
        return [n for n in (self.A, self.B) if n is not None]

    def next_seq(self) -> int:
        self._seq += 1
        return self._seq

    def wall(self, mono_ms: float) -> int:
        return int(self.wall0 + (mono_ms - self.mono0))

    def emit(self, ev: Dict[str, Any]) -> None:
        self._emit(ev)

    def event(self, node: Node, name: str, now: float, **kw: Any) -> None:
        self.log.write({"ev": "event", "node": "A" if node is self.A else "B", "t_ms": self.wall(now),
                        "name": name, **kw})

    def ack_delay(self) -> float:
        d = self.latency_ms + self.rng.uniform(-self.jitter_ms, self.jitter_ms)
        return max(0.0, d)

    def lose(self) -> bool:
        return self.loss_pct > 0 and self.rng.random() * 100.0 < self.loss_pct

    def tx(self, node: Node, msg: Dict[str, Any], addr: Tuple[str, int], now: float, delay: float = 0.0,
           stream: bool = False) -> Tuple[float, bool]:
        """Returns (due_mono_ms, lost). Streams are not logged per packet."""
        lost = self.lose()
        node.stats["tx"] += 1
        if lost:
            node.stats["tx_lost"] += 1
        if not stream:
            self.log.write({"ev": "tx", "node": "A" if node is self.A else "B", "t_ms": self.wall(now + delay),
                            "to": list(addr), "lost": lost, "msg": msg})
        if lost:
            return now + delay, True
        data = json.dumps(msg, separators=(",", ":")).encode("utf-8")
        if delay <= 0:
            self._send(node.kind, data, addr)
        else:
            heapq.heappush(self._pending, (now + delay, self.next_seq(), node.kind, data, addr))
        return now + delay, False

    def broadcast(self, node: Node, msg: Dict[str, Any]) -> None:
        self._broadcast(node.kind, json.dumps(msg, separators=(",", ":")).encode("utf-8"))

    # ---- inbound
    def on_datagram(self, kind: str, data: bytes, addr: Tuple[str, int], now: Optional[float] = None) -> None:
        now = self.now_fn() if now is None else now
        node = self.A if kind == "haptic" else self.B
        if node is None:
            return
        node.stats["rx"] += 1
        raw = ""
        try:
            parsed: Any = json.loads(data.decode("utf-8"))
        except (ValueError, UnicodeDecodeError):
            parsed = None
            raw = data[:200].decode("utf-8", "replace")
        dropped = False
        if not node.powered:
            dropped = True
        elif self.lose():
            dropped = True
            node.stats["rx_dropped"] += 1
        self.log.write({"ev": "rx", "node": "A" if node is self.A else "B", "t_ms": self.wall(now),
                        "from": list(addr), "dropped": dropped, "powered": node.powered,
                        "msg": parsed if parsed is not None or not raw else {"raw": raw}})
        if dropped:
            return
        if isinstance(node, NodeA):
            node.last_rx = now
        if parsed is None:
            node.stats["rx_invalid"] += 1
            if isinstance(node, NodeA) and node.acks_garbage:
                self.tx(node, node.ack_msg("", "error", node.dev_ms(now), 0, now, err=("BAD_JSON", "bad json")),
                        addr, now, delay=self.ack_delay())
            return
        node.on_rx(addr, now)
        node.on_message(parsed, addr, now)

    # ---- time
    def step(self, now: Optional[float] = None) -> None:
        now = self.now_fn() if now is None else now
        while self._pending and self._pending[0][0] <= now:
            _, _, kind, data, addr = heapq.heappop(self._pending)
            self._send(kind, data, addr)
        for n in self.nodes():
            n.step(now)
        while self._pending and self._pending[0][0] <= now:
            _, _, kind, data, addr = heapq.heappop(self._pending)
            self._send(kind, data, addr)

    # ---- control
    def control(self, line: str, now: Optional[float] = None) -> Dict[str, Any]:
        now = self.now_fn() if now is None else now
        parts = line.strip().split()
        if not parts:
            return {"ok": False, "cmd": "", "error": "empty"}
        cmd, args = parts[0].lower(), parts[1:]
        res: Dict[str, Any] = {"ok": True, "cmd": cmd}

        def need(node: Optional[Node], name: str) -> bool:
            if node is None:
                res.update(ok=False, error=f"node {name} is not running (--kind)")
                return False
            return True

        try:
            if cmd == "flinch":
                applied = []
                if self.B is not None:
                    self.B.bursts.append((now + FLINCH_EMG_DELAY_MS, FLINCH_EMG_DUR_MS, FLINCH_EMG_MULT))
                    applied.append("emg+120ms")
                if self.A is not None:
                    self.A.jolts.append(now + FLINCH_IMU_DELAY_MS)
                    applied.append("imu+150ms")
                res["applied"] = applied
            elif cmd == "squeeze":
                if need(self.B, "bio"):
                    self.B.bursts.append((now, SQUEEZE_DUR_MS, SQUEEZE_MULT))
            elif cmd in ("off-a", "on-a", "off-b", "on-b"):
                node = self.A if cmd.endswith("a") else self.B
                if need(node, "haptic" if cmd.endswith("a") else "bio"):
                    if cmd.startswith("off"):
                        node.power_off(now)
                    else:
                        node.power_on(now)
            elif cmd in ("stick-a", "unstick-a"):    # fault: driver latched on (the watchdog must save us)
                if need(self.A, "haptic"):
                    m = int(args[0]) if args else 0
                    (self.A.stuck.add if cmd == "stick-a" else self.A.stuck.discard)(m)
            elif cmd == "jitter":
                self.jitter_ms = max(0.0, float(args[0]))
                res["jitter_ms"] = self.jitter_ms
            elif cmd == "latency":
                self.latency_ms = max(0.0, float(args[0]))
                res["latency_ms"] = self.latency_ms
            elif cmd == "loss":
                self.loss_pct = min(100.0, max(0.0, float(args[0])))
                res["loss_pct"] = self.loss_pct
            elif cmd == "status":
                res["state"] = self.state(now)
            elif cmd == "quit":
                self.quit_requested = True
            else:
                res.update(ok=False, error="unknown command")
        except (IndexError, ValueError):
            res.update(ok=False, error="bad argument")
        self.emit({"event": "control", **{k: v for k, v in res.items() if k != "state"}})
        self.log.write({"ev": "event", "node": "-", "t_ms": self.wall(now), "name": "control", "cmd": line.strip(),
                        "ok": res["ok"]})
        return res

    def state(self, now: float) -> Dict[str, Any]:
        out: Dict[str, Any] = {"kinds": list(self.kinds), "latency_ms": self.latency_ms,
                               "jitter_ms": self.jitter_ms, "loss_pct": self.loss_pct, "ports": self.ports}
        for n in self.nodes():
            out[n.kind] = {"powered": n.powered, "subscribers": len(n.live_subs(now)), **n.stats}
        if self.A:
            out["haptic"].update(strokes=self.A.stroke_count, watchdog_events=self.A.watchdog_events,
                                 active=[m["active"] for m in self.A.motors[:MOTOR_COUNT]])
        if self.B:
            out["bio"].update(chunks_sent=self.B.chunks_sent, emg_bursts=self.B.burst_events)
        if self.dialect != DIALECT_REFERENCE:
            out["dialect"] = self.dialect
            for n in self.nodes():
                out[n.kind]["ignored"] = n.ignored          # type: ignore[attr-defined]
        return out


class TwinServer:
    """Real sockets + threads around a Twin."""

    def __init__(self, twin: Twin, host: str = "127.0.0.1", control: bool = True, stdin: bool = False,
                 broadcast_addrs: Sequence[str] = ()):
        self.twin = twin
        self.host = host
        self.use_control = control
        self.use_stdin = stdin
        self.broadcast_addrs = list(broadcast_addrs)
        self.lock = threading.RLock()
        self.stop_evt = threading.Event()
        self.socks: Dict[str, socket.socket] = {}
        self.threads: List[threading.Thread] = []
        self.disc_sock: Optional[socket.socket] = None
        self.ctl_sock: Optional[socket.socket] = None
        # extra beacons for a non-loopback --host: (socket, source address or None, destination), see _open_lan_beacons
        self.lan_beacons: List[Tuple[socket.socket, Optional[str], str]] = []
        twin._send = self._send
        twin._broadcast = self._broadcast
        twin.ip = host if host != "0.0.0.0" else lan_ip()

    def _send(self, kind: str, data: bytes, addr: Tuple[str, int]) -> None:
        s = self.socks.get(kind)
        if s is not None:
            try:
                s.sendto(data, addr)
            except OSError:
                pass

    def _broadcast(self, kind: str, data: bytes) -> None:
        s = self.disc_sock
        if s is None:
            return
        port = self.twin.ports["discovery"]
        for target in ("255.255.255.255", "127.0.0.1"):
            try:
                s.sendto(data, (target, port))
            except OSError:
                pass
        for sock, src, dst in self.lan_beacons:      # empty unless --host is not loopback
            try:
                sock.sendto(data if src is None else _with_ip(data, src), (dst, port))
            except OSError:
                pass

    def _open_lan_beacons(self) -> None:
        """Non-loopback --host only: announce on every local network too, each beacon sent from a socket bound to that
        interface's own address so the receiver sees the LAN address as the node's address (and the beacon's `ip` says
        the same). Whatever goes wrong here is reported on stderr and skipped; the twin always starts."""
        if is_loopback_host(self.host):
            if self.broadcast_addrs:
                sys.stderr.write("twin: --broadcast-addr ignored: --host is loopback\n")
            return
        single = self.host not in ("0.0.0.0", "")
        ifaces = local_ipv4_interfaces()
        if not ifaces or (single and not any(ip == self.host for ip, _ in ifaces)):
            guess = self.host if single else lan_ip()          # listing failed: assume a /24 around our address
            if not is_loopback_host(guess):
                ifaces = list(ifaces) + [(guess, "255.255.255.0")]
                sys.stderr.write(f"twin: {guess} not found among the local interfaces; assuming /24 "
                                 "(add --broadcast-addr if its network is different)\n")
        socks: Dict[str, Optional[socket.socket]] = {}
        for src, dst in beacon_targets(ifaces, self.host, self.broadcast_addrs):
            if src is None:
                if self.disc_sock is not None:
                    self.lan_beacons.append((self.disc_sock, None, dst))
                continue
            if src not in socks:
                try:
                    socks[src] = self._beacon_socket(src)
                except OSError as e:
                    socks[src] = None
                    sys.stderr.write(f"twin: no beacon from {src}: {e}\n")
            if socks[src] is not None:
                self.lan_beacons.append((socks[src], src, dst))     # type: ignore[arg-type]

    def _beacon_socket(self, src: str) -> socket.socket:
        """A UDP socket bound to the interface address `src` (any port), allowed to broadcast."""
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
            s.bind((src, 0))
        except OSError:
            s.close()
            raise
        return s

    def start(self) -> None:
        tw = self.twin
        for node in tw.nodes():
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.bind((self.host, node.port))
            s.settimeout(0.05)
            self.socks[node.kind] = s
            self._thread(self._rx_loop, node.kind, s)
        self.disc_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.disc_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            self.disc_sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        except OSError:
            pass
        self._open_lan_beacons()
        if self.use_control:
            # the control port has no authentication (quit, off-a, loss ...): loopback only, whatever --host says
            self.ctl_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            self.ctl_sock.bind(("127.0.0.1", tw.ports["control"]))
            self.ctl_sock.settimeout(0.05)
            self._thread(self._ctl_loop)
        self._thread(self._tick_loop)
        if self.use_stdin:
            t = threading.Thread(target=self._stdin_loop, daemon=True)
            t.start()
        ready: Dict[str, Any] = {"event": "ready", "kinds": list(tw.kinds), "ports": tw.ports, "seed": tw.seed,
                                 "pid": __import__("os").getpid()}
        if tw.dialect != DIALECT_REFERENCE:
            ready["dialect"] = tw.dialect
        if not is_loopback_host(self.host):
            ready["lan"] = {"host": self.host, "control": "127.0.0.1",
                            "beacons": [[src or "any", dst] for _, src, dst in self.lan_beacons]}
        tw.emit(ready)

    def _thread(self, fn: Callable[..., None], *args: Any) -> None:
        t = threading.Thread(target=fn, args=args, daemon=True)
        t.start()
        self.threads.append(t)

    def _rx_loop(self, kind: str, s: socket.socket) -> None:
        while not self.stop_evt.is_set():
            try:
                data, addr = s.recvfrom(4096)
            except socket.timeout:
                continue
            except OSError:
                if self.stop_evt.is_set():
                    return
                continue
            with self.lock:
                self.twin.on_datagram(kind, data, addr)

    def _ctl_loop(self) -> None:
        s = self.ctl_sock
        while not self.stop_evt.is_set():
            try:
                data, addr = s.recvfrom(1024)
            except socket.timeout:
                continue
            except OSError:
                if self.stop_evt.is_set():
                    return
                continue
            with self.lock:
                res = self.twin.control(data.decode("utf-8", "replace"))
            try:
                s.sendto(json.dumps(res).encode("utf-8"), addr)
            except OSError:
                pass

    def _stdin_loop(self) -> None:
        for line in sys.stdin:
            with self.lock:
                self.twin.control(line)
            if self.twin.quit_requested:
                return

    def _tick_loop(self) -> None:
        while not self.stop_evt.is_set():
            with self.lock:
                self.twin.step()
            time.sleep(0.002)

    def stop(self) -> None:
        self.stop_evt.set()
        for t in self.threads:
            t.join(timeout=1.0)
        for s in list(self.socks.values()) + [self.disc_sock, self.ctl_sock] + [b[0] for b in self.lan_beacons]:
            if s is not None:
                try:
                    s.close()
                except OSError:
                    pass
        self.twin.log.close()


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Phantom Hand twin-lite: fake Node A (haptic) + Node B (bio)")
    p.add_argument("--kind", choices=["haptic", "bio", "both"], default="haptic")
    p.add_argument("--port-offset", type=int, default=0)
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--log", default=None, help="JSON-lines log path, or - for stdout")
    p.add_argument("--ack-latency-ms", type=float, default=8.0)
    p.add_argument("--ack-jitter-ms", type=float, default=4.0)
    p.add_argument("--spinup-ms", type=float, default=30.0)
    p.add_argument("--watchdog-s", type=float, default=2.0)
    p.add_argument("--no-stdin", action="store_true")
    p.add_argument("--duration", type=float, default=None, help="exit after N seconds")
    p.add_argument("--host", default="127.0.0.1", help="bind address of the two node ports (0.0.0.0 for the LAN; the "
                   "control port stays on 127.0.0.1). A non-loopback host also announces on every local network")
    p.add_argument("--broadcast-addr", action="append", default=[], metavar="IP",
                   help="extra beacon destination, repeatable: a directed broadcast such as 192.168.1.255, or the "
                        "other PC's own address when the network drops broadcasts (ignored with a loopback --host)")
    p.add_argument("--dialect", choices=[DIALECT_REFERENCE, DIALECT_TEAM], default=DIALECT_REFERENCE,
                   help="reference (default) = the reference firmware; team = the electronics team's own firmware "
                        "(ids CHETNA_HAPTIC_001 / CHETNA_BIO_001, ack {cue_id, accepted}, telemetry to the last "
                        "sender only, no status / emg_burst, 4-value chunks at 25/s; contracts HAPTIC_PROTOCOL v1.3)")
    p.add_argument("--telemetry-port", type=int, default=0, metavar="N",
                   help="--dialect team only: send sensor_data / sensor_chunk to the last sender's IP on this FIXED UDP "
                        "port, whatever port its packet came from (the real boards do, on 8790); acks still go to the "
                        "sender's source port. 0 (default) = telemetry to the source address too. A twin on the same PC "
                        "as its receiver needs a port other than the node's own: the real 8790 is the node's own "
                        "command port there (--port-offset 0), so the receiver could not bind it. Ignored by the "
                        "reference dialect")
    return p


def main(argv: Optional[List[str]] = None) -> int:
    a = build_parser().parse_args(argv)
    kinds = ("haptic", "bio") if a.kind == "both" else (a.kind,)

    def emit(ev: Dict[str, Any]) -> None:
        sys.stdout.write(json.dumps(ev) + "\n")
        sys.stdout.flush()

    tw = Twin(kinds, seed=a.seed, port_offset=a.port_offset, latency_ms=a.ack_latency_ms,
              jitter_ms=a.ack_jitter_ms, spinup_ms=a.spinup_ms, watchdog_s=a.watchdog_s,
              log=EventLog(a.log), emit=emit, dialect=a.dialect, telemetry_port=a.telemetry_port)
    srv = TwinServer(tw, host=a.host, stdin=not a.no_stdin, broadcast_addrs=a.broadcast_addr)
    srv.start()
    t_end = None if a.duration is None else time.monotonic() + a.duration
    try:
        while not tw.quit_requested and (t_end is None or time.monotonic() < t_end):
            time.sleep(0.05)
    except KeyboardInterrupt:
        pass
    finally:
        srv.stop()
        emit({"event": "exit"})
    return 0


if __name__ == "__main__":
    sys.exit(main())
