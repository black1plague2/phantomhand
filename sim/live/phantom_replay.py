"""Phantom Hand mode for the fake headset: `python fake_headset.py --game phantom_hand ...`.

Replays a Phantom Hand fixture session (default contracts/fixtures/sessions/phantom_hand_min) through the
live protocol AND drives the sleeve twin (or real Node A / Node B) the way the Unity game would:

  * every `haptic_cue` event in the fixture becomes a REAL stroke datagram to Node A at replay time; the ack
    (or its absence) is measured and written back into that event (`delivered`, `ack_latency_ms`), so the
    uploaded events.ndjson describes what actually happened on the wire;
  * the headset subscribes to both nodes (`subscribe` every 1 s + a `ping` keepalive to Node A for the 2 s
    watchdog, 03-SPEC section 4), builds the live `trace` (EMG env + |accel|, 20 Hz) and `game_state`
    (phase, condition, remaining_s, nodes{haptic, bio}) from the real streams and puts them into `status`
    messages (LIVE_PROTOCOL v0.2);
  * OLED text `SYNC`/`ASYNC` on each induction, `flinch` on the twin's control port at `threat_impact`;
  * hub outages are survived: events keep going to the outbox, the websocket is re-established with
    `resume_from_seq`, and the end-of-session uploads are retried until they land (PRD 11 fail-safe);
  * Node B absent => no EMG: emg_burst events dropped, threat_response EMG fields null, no EMG in the
    sensor file (the headset records sens_###.json from what the nodes streamed), `bio.connected=false` in every status.

Idle gaps are compressed (`compress_gap_ms`) so a 147 s demo run takes about a minute; the session clock the
hub sees (events' `t_ms`) is untouched, only the wall time between events shrinks.

No sleeps for readiness: discovery and subscriptions are awaited on the datagram that proves them.
"""
from __future__ import annotations

import asyncio
import copy
import json
import math
import shutil
import socket
import time
import uuid
from collections import deque
from pathlib import Path
from typing import Any, Awaitable, Callable, Deque, Dict, List, Optional, Tuple

from fake_headset import FakeHeadset

DEFAULT_FIXTURE = Path(__file__).resolve().parents[2] / "contracts" / "fixtures" / "sessions" / "phantom_hand_min"
CUE_INTENSITY = 150          # 03-SPEC D6: 150 x haptic_max_intensity (1.0)
CUE_DURATION_MS = 200        # PRD 9.2 stroke
ACK_TIMEOUT_S = 0.25
STALE_S = 1.5                # a node is "connected" if it spoke in the last 1.5 s
EMG_BASELINE_ADC = 420.0     # sim calibration of emg_level: rest ... MVC (twin baseline 420)
EMG_MVC_ADC = 1500.0
TRACE_FS_HZ = 20


# ----------------------------------------------------------------------------------- pure helpers
def real_time_windows(events: List[Dict[str, Any]], before_ms: float = 2600.0, after_ms: float = 1800.0
                      ) -> List[Tuple[float, float]]:
    """Session-time windows that must replay at 1x: the 2 s EMG/IMU baseline before each threat_impact and the
    1.5 s response window after it (analytics reads exactly those, 03-SPEC section 8)."""
    out = []
    for e in events:
        if e.get("type") == "threat_impact":
            t = float(e.get("t_ms") or 0)
            out.append((t - before_ms, t + after_ms))
    return out


def compress_schedule(times_ms: List[float], cap_ms: float, speed: float = 1.0,
                      real_windows: Optional[List[Tuple[float, float]]] = None) -> List[float]:
    """Wall offsets (seconds from replay start) for events at session times `times_ms`.

    Gaps longer than `cap_ms` are replaced by `cap_ms` (cap_ms <= 0 disables compression) unless the gap ends
    inside one of `real_windows` (kept 1x so recorded sensor windows stay dense); everything is divided by
    `speed`. Order is preserved even if the input is not monotonic."""
    wall: List[float] = []
    acc = 0.0
    prev = times_ms[0] if times_ms else 0.0
    for t in times_ms:
        gap = max(0.0, t - prev)
        keep = any(lo < t <= hi for lo, hi in (real_windows or []))
        if cap_ms and cap_ms > 0 and not keep:
            gap = min(gap, cap_ms)
        acc += gap
        wall.append(acc / 1000.0 / max(speed, 1e-6))
        prev = max(prev, t)
    return wall


class ReplayClock:
    """wall seconds since replay start -> session ms, interpolated through the schedule's anchor points."""

    def __init__(self, wall_s: List[float], session_ms: List[float]):
        self.w = list(wall_s)
        self.s = list(session_ms)

    def slope(self, wall_s: float) -> float:
        """session ms per wall second at `wall_s` (1000 = real time at speed 1)."""
        if len(self.w) < 2 or wall_s < self.w[0] or wall_s >= self.w[-1]:
            return 1000.0
        lo, hi = 0, len(self.w) - 1
        while hi - lo > 1:
            mid = (lo + hi) // 2
            if self.w[mid] <= wall_s:
                lo = mid
            else:
                hi = mid
        dw = self.w[hi] - self.w[lo]
        return 1000.0 if dw <= 0 else (self.s[hi] - self.s[lo]) / dw

    def session_ms(self, wall_s: float) -> float:
        if not self.w:
            return 0.0
        if wall_s <= self.w[0]:
            return self.s[0]
        if wall_s >= self.w[-1]:
            return self.s[-1] + (wall_s - self.w[-1]) * 1000.0
        lo, hi = 0, len(self.w) - 1
        while hi - lo > 1:
            mid = (lo + hi) // 2
            if self.w[mid] <= wall_s:
                lo = mid
            else:
                hi = mid
        w0, w1, s0, s1 = self.w[lo], self.w[hi], self.s[lo], self.s[hi]
        if w1 <= w0:
            return s1
        return s0 + (s1 - s0) * (wall_s - w0) / (w1 - w0)


def accel_magnitude(msg: Dict[str, Any]) -> Optional[float]:
    s = msg.get("sensors") or {}
    try:
        x, y, z = (float(s[f"imu_accel_{a}"]["value"]) for a in "xyz")
    except (KeyError, TypeError, ValueError):
        return None
    return math.sqrt(x * x + y * y + z * z)


def emg_level(env: float) -> float:
    return max(0.0, min(1.0, (env - EMG_BASELINE_ADC) / (EMG_MVC_ADC - EMG_BASELINE_ADC)))


def prepare_session_copy(src: Path, dst: Path, *, drop_bio: bool = False, new_session_id: Optional[str] = None
                         ) -> Tuple[Path, str]:
    """Copy the fixture into `dst` with a fresh session id (uploads from repeated runs never collide), no
    metrics.json, events renumbered. With `drop_bio` the Node B data is removed exactly as a headset that
    never saw Node B would have recorded it. Returns (dst, session_id)."""
    dst = Path(dst)
    if dst.exists():
        shutil.rmtree(dst)
    dst.mkdir(parents=True)
    sid = new_session_id or str(uuid.uuid4())
    for f in sorted(Path(src).iterdir()):
        if f.name == "metrics.json" or not f.is_file():
            continue
        if f.name == "session.json":
            d = json.loads(f.read_text(encoding="utf-8"))
            d["session_id"] = sid
            (dst / f.name).write_text(json.dumps(d, indent=2), encoding="utf-8")
        elif f.name == "events.ndjson":
            evs = [json.loads(l) for l in f.read_text(encoding="utf-8").splitlines() if l.strip()]
            if drop_bio:
                evs = [e for e in evs if e.get("type") != "emg_burst"]
                for e in evs:
                    if e.get("type") == "threat_response":
                        e["data"].update({"emg_peak_x": None, "emg_latency_ms": None, "quality": "degraded"})
            for i, e in enumerate(evs):
                e["seq"] = i
            (dst / f.name).write_text("".join(json.dumps(e) + "\n" for e in evs), encoding="utf-8")
        elif f.name.startswith("sens_") and f.suffix == ".json":
            continue          # the headset records its own from the node streams (build_sens_chunks)
        elif f.name.startswith("kin_") and f.suffix == ".json":
            d = json.loads(f.read_text(encoding="utf-8"))
            if "session_id" in d:
                d["session_id"] = sid
            (dst / f.name).write_text(json.dumps(d), encoding="utf-8")
        else:
            shutil.copy2(f, dst / f.name)
    return dst, sid


def build_sens_chunks(session_id: str, emg: List[Tuple[float, float]], imu: List[Tuple[float, ...]],
                      emg_device: str = "CHETNA_BIO_001", imu_device: str = "SLEEVE_001",
                      span_ms: float = 5000.0, source: str = "udp") -> List[Dict[str, Any]]:
    """Slice recorded node samples into sens_###.json documents (contracts/schemas/sensor-file.schema.json).
    emg = [(session_ms, adc)], imu = [(session_ms, ax, ay, az, gx, gy, gz)]; both on the events clock."""
    starts = [x[0][0] for x in (emg, imu) if x]
    if not starts:
        return []
    t_min = min(starts)
    t_max = max([x[-1][0] for x in (emg, imu) if x])
    chunks: List[Dict[str, Any]] = []
    seq = 0
    lo = t_min
    while lo <= t_max:
        hi = lo + span_ms
        e = [x for x in emg if lo <= x[0] < hi]
        m = [x for x in imu if lo <= x[0] < hi]
        if e or m:
            doc: Dict[str, Any] = {"session_id": session_id, "chunk_seq": seq,
                                   "t0_ms": round(max(0.0, min(x[0][0] for x in (e, m) if x)), 1), "source": source}
            if e:
                doc["emg_env"] = {"device_id": emg_device, "rate_hz": 100, "unit": "raw_adc",
                                  "t_ms": [round(x[0], 1) for x in e], "value": [round(x[1], 1) for x in e]}
            if m:
                doc["imu"] = {"device_id": imu_device, "rate_hz": 100, "t_ms": [round(x[0], 1) for x in m],
                              **{k: [round(x[i + 1], 4) for x in m]
                                 for i, k in enumerate(("ax", "ay", "az", "gx", "gy", "gz"))}}
            chunks.append(doc)
            seq += 1
        lo = hi
    return chunks


# ----------------------------------------------------------------------------------- node link
class NodeLink(asyncio.DatagramProtocol):
    """One UDP socket to one node (Node A or Node B). The node answers to the sender, so acks and the
    sensor stream come back on this socket."""

    def __init__(self, name: str, addr: Tuple[str, int], on_message: Callable[[str, Dict[str, Any]], None]):
        self.name = name
        self.addr = addr
        self.on_message = on_message
        self.transport: Optional[asyncio.DatagramTransport] = None
        self.last_rx = 0.0
        self.rx_count = 0
        self.pending: Dict[str, Tuple[asyncio.Future, float]] = {}
        self.first_data = asyncio.Event()

    async def open(self) -> None:
        loop = asyncio.get_running_loop()
        self.transport, _ = await loop.create_datagram_endpoint(lambda: self, local_addr=("0.0.0.0", 0))

    def close(self) -> None:
        if self.transport:
            self.transport.close()
            self.transport = None

    def send(self, obj: Dict[str, Any]) -> None:
        if self.transport:
            self.transport.sendto(json.dumps(obj).encode("utf-8"), self.addr)

    def connection_made(self, transport: Any) -> None:
        self.transport = transport

    def error_received(self, exc: Exception) -> None:   # ICMP port unreachable on Windows: a missing node is not fatal
        pass

    def datagram_received(self, data: bytes, addr: Tuple[str, int]) -> None:
        now = time.monotonic()
        try:
            msg = json.loads(data.decode("utf-8"))
        except (ValueError, UnicodeDecodeError):
            return
        if not isinstance(msg, dict):
            return
        self.last_rx = now
        self.rx_count += 1
        if msg.get("type") in ("sensor_data", "sensor_chunk"):
            self.first_data.set()
        if msg.get("type") == "ack":
            ref = msg.get("cue_id") or msg.get("ack_id")
            hit = self.pending.pop(ref, None) if ref else None
            if hit and not hit[0].done():
                hit[0].set_result((msg, (now - hit[1]) * 1000.0))
            return
        self.on_message(self.name, msg)

    def connected(self, now: Optional[float] = None) -> bool:
        return self.rx_count > 0 and ((now or time.monotonic()) - self.last_rx) < STALE_S

    async def request(self, msg: Dict[str, Any], key: str, timeout: float = ACK_TIMEOUT_S
                      ) -> Tuple[Optional[Dict[str, Any]], Optional[float]]:
        """Send and wait for the ack carrying `key` (cue_id). (None, None) on timeout."""
        fut: asyncio.Future = asyncio.get_running_loop().create_future()
        t0 = time.monotonic()
        self.pending[key] = (fut, t0)
        self.send(msg)
        try:
            return await asyncio.wait_for(fut, timeout)
        except asyncio.TimeoutError:
            self.pending.pop(key, None)
            return None, None


class _DiscoveryProtocol(asyncio.DatagramProtocol):
    def __init__(self) -> None:
        self.found: Dict[str, Tuple[str, int]] = {}
        self.waiters: Dict[str, asyncio.Future] = {}

    def want(self, kind: str) -> asyncio.Future:
        fut = asyncio.get_running_loop().create_future()
        if kind in self.found:
            fut.set_result(self.found[kind])
        self.waiters[kind] = fut
        return fut

    def datagram_received(self, data: bytes, addr: Tuple[str, int]) -> None:
        try:
            msg = json.loads(data.decode("utf-8"))
        except (ValueError, UnicodeDecodeError):
            return
        if not isinstance(msg, dict) or msg.get("type") != "device_discovery":
            return
        kind, port = msg.get("device_kind"), msg.get("command_port")
        if kind not in ("haptic", "bio") or not isinstance(port, int):
            return
        # an announcement may carry the node's own `ip` (the twin does); a broadcast datagram's source address can be
        # another interface of the same machine, so prefer the advertised address, else the datagram's source
        ip = msg.get("ip") if isinstance(msg.get("ip"), str) and msg.get("ip") else addr[0]
        self.found[kind] = (ip, port)
        fut = self.waiters.get(kind)
        if fut and not fut.done():
            fut.set_result(self.found[kind])


async def discover_nodes(disc_port: int, kinds: Tuple[str, ...] = ("haptic", "bio"), timeout: float = 5.0
                         ) -> Dict[str, Tuple[str, int]]:
    """Listen on the discovery port (PRD 9.1, 1 Hz announcements) until every wanted kind has announced or
    `timeout` passes. Returns {kind: (ip, command_port)} for those seen. Waits on the datagram, not a sleep."""
    loop = asyncio.get_running_loop()
    raw = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    raw.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    raw.bind(("0.0.0.0", disc_port))
    raw.setblocking(False)
    proto = _DiscoveryProtocol()
    transport, _ = await loop.create_datagram_endpoint(lambda: proto, sock=raw)
    try:
        futs = [proto.want(k) for k in kinds]
        await asyncio.wait(futs, timeout=timeout)
        return dict(proto.found)
    finally:
        transport.close()


# ----------------------------------------------------------------------------------- the headset
class PhantomHeadset(FakeHeadset):
    """FakeHeadset that plays a Phantom Hand session and drives the nodes."""

    def __init__(self, session_dir: Path, *, node_a: Optional[Tuple[str, int]] = None,
                 node_b: Optional[Tuple[str, int]] = None, control: Optional[Tuple[str, int]] = None,
                 compress_gap_ms: float = 2500.0, status_period_s: float = 0.5,
                 progress_hooks: Optional[List[Tuple[float, Callable[[], Awaitable[None]]]]] = None,
                 off_a_at: Optional[float] = None, upload_deadline_s: float = 90.0, **kw: Any):
        kw.setdefault("stage", "reach")
        super().__init__(session_dir, **kw)
        self.node_addr = {"haptic": node_a, "bio": node_b}
        self.control_addr = control
        self.compress_gap_ms = compress_gap_ms
        self.status_period_s = status_period_s
        self.upload_deadline_s = upload_deadline_s
        self.links: Dict[str, NodeLink] = {}
        self.progress_hooks = sorted(progress_hooks or [], key=lambda h: h[0])
        if off_a_at is not None and control is not None:
            self.progress_hooks.append((off_a_at, lambda: self.control_cmd("off-a")))
            self.progress_hooks.sort(key=lambda h: h[0])
        self.phase: Optional[str] = None
        self.condition: Optional[str] = None
        self.events_out: List[Dict[str, Any]] = [copy.deepcopy(e) for e in self.events]
        self.clock: Optional[ReplayClock] = None
        self.t_replay0 = 0.0
        self.next_phase_t: List[Tuple[float, str]] = []
        self.trace_emg: Deque[Tuple[float, float]] = deque(maxlen=400)   # (session_ms, adc)
        self.trace_acc: Deque[Tuple[float, float]] = deque(maxlen=400)
        self._emg_acc: List[float] = []
        self._acc_acc: List[float] = []
        self._trace_cursor = {"emg": 0.0, "acc": 0.0}
        self.last_emg_level: Optional[float] = None
        self.rec_emg: List[Tuple[float, float]] = []
        self.rec_imu: List[Tuple[float, ...]] = []
        self.lost = asyncio.Event()
        self.ws_ready = asyncio.Event()
        self.done = False
        self.report: Dict[str, Any] = {
            "cues_sent": 0, "cues_acked": 0, "cue_rtt_ms": [], "cue_rejected": [], "statuses_sent": 0,
            "status_haptic_disconnected": 0, "status_bio_disconnected": 0, "reconnects": 0,
            "upload_attempts": {}, "emg_bursts_from_node": 0, "flinch_sent": 0, "oled": [],
            "nodes": {"haptic": node_a is not None, "bio": node_b is not None}, "gap_cap_ms": compress_gap_ms,
            "sens_chunks": 0, "sensor_data": 0, "events_sent": 0, "events_dropped_no_bio": 0,
            "hub_connected_samples": [0, 0],
        }
        self.status_log: List[Dict[str, Any]] = []   # compact copy of what we reported (phase, cond, nodes)

    # ---- node plumbing
    def _on_node_message(self, name: str, msg: Dict[str, Any]) -> None:
        typ = msg.get("type")
        now = time.monotonic()
        sess = self.clock.session_ms(now - self.t_replay0) if self.clock else 0.0
        real_time = (self.clock is not None
                     and abs(self.clock.slope(now - self.t_replay0) - 1000.0 * self.speed) < 120.0)
        if typ == "sensor_data":
            self.report["sensor_data"] += 1
            sd = msg.get("sensors") or {}
            if real_time:
                try:
                    self.rec_imu.append((sess, *(float(sd[f"imu_{k}"]["value"]) for k in
                                                 ("accel_x", "accel_y", "accel_z", "gyro_x", "gyro_y", "gyro_z"))))
                except (KeyError, TypeError, ValueError):
                    pass
            m = accel_magnitude(msg)
            if m is not None:
                self._acc_acc.append(m)
                if len(self._acc_acc) >= 5:                       # 100 Hz -> 20 Hz
                    self.trace_acc.append((sess, sum(self._acc_acc) / len(self._acc_acc)))
                    self._acc_acc = []
        elif typ == "sensor_chunk":
            self.report["sens_chunks"] += 1
            vals = [v for v in (msg.get("emg_envelope") or []) if isinstance(v, (int, float))]
            if real_time:      # 100 Hz samples; the chunk arrives right after its last sample
                self.rec_emg.extend((sess - (len(vals) - 1 - i) * 10.0 * self.speed, float(v))
                                    for i, v in enumerate(vals))
            for v in msg.get("emg_envelope") or []:
                if isinstance(v, (int, float)):
                    self._emg_acc.append(float(v))
                    if len(self._emg_acc) >= 5:
                        mean = sum(self._emg_acc) / len(self._emg_acc)
                        self.trace_emg.append((sess, mean))
                        self.last_emg_level = emg_level(mean)
                        self._emg_acc = []
        elif typ == "emg_burst":
            self.report["emg_bursts_from_node"] += 1

    async def start_nodes(self) -> None:
        for kind, addr in self.node_addr.items():
            if addr is None:
                continue
            link = NodeLink(kind, addr, self._on_node_message)
            await link.open()
            self.links[kind] = link
            link.send({"type": "subscribe"})

    async def wait_node_streams(self, timeout: float = 5.0) -> Dict[str, bool]:
        """Readiness: each configured node has delivered at least one stream datagram to us."""
        res = {}
        for kind, link in self.links.items():
            try:
                await asyncio.wait_for(link.first_data.wait(), timeout)
                res[kind] = True
            except asyncio.TimeoutError:
                res[kind] = False
        return res

    async def _keepalive_loop(self) -> None:
        n = 0
        while not self.done:
            for kind, link in self.links.items():
                link.send({"type": "subscribe"})
                if kind == "haptic":
                    n += 1
                    link.send({"type": "ping", "id": f"ka-{n}"})
            await asyncio.sleep(1.0)

    async def control_cmd(self, cmd: str) -> None:
        if self.control_addr is None:
            return
        loop = asyncio.get_running_loop()
        fut: asyncio.Future = loop.create_future()

        class _P(asyncio.DatagramProtocol):
            def datagram_received(self, data: bytes, addr: Tuple[str, int]) -> None:
                if not fut.done():
                    fut.set_result(data)

            def error_received(self, exc: Exception) -> None:
                if not fut.done():
                    fut.set_result(b"")

        tr, _ = await loop.create_datagram_endpoint(_P, local_addr=("0.0.0.0", 0))
        try:
            tr.sendto(cmd.encode("utf-8"), self.control_addr)
            await asyncio.wait_for(fut, 1.0)
        except asyncio.TimeoutError:
            pass
        finally:
            tr.close()

    async def handle_message(self, msg: Dict[str, Any]) -> None:
        """The session id is the game's own (fixture copy), as in Unity. FakeHeadset adopts the hello_ack's id, and a
        hub that does not echo one (the Flutter hub) would make it 'session-000' and mis-file the uploads."""
        keep = self.state.session_id
        await super().handle_message(msg)
        if msg.get("type") == "hello_ack" and keep:
            self.state.session_id = keep

    # ---- hub connection with outage survival
    async def _rx_wrapper(self) -> None:
        await self.receive_messages()
        self.connected = False
        self.ws_ready.clear()
        self.lost.set()

    async def connect_hub(self, host: str, port: int) -> None:
        await self.connect(host, port)
        self.ws_ready.set()
        await self.send_hello(resume_from_seq=None)
        self._rx_task = asyncio.create_task(self._rx_wrapper())

    async def _keeper(self) -> None:
        while not self.done:
            await self.lost.wait()
            if self.done:
                return
            delay = 0.25
            while not self.done:
                try:
                    if self.http_session is not None:
                        await self.http_session.close()
                        self.http_session = None
                    await self.connect(self.resolved_host or self.host, self.resolved_port)
                    await self.send_hello(resume_from_seq=self.state.last_acked_seq)
                    self.lost.clear()
                    self.ws_ready.set()
                    self._rx_task = asyncio.create_task(self._rx_wrapper())
                    self.report["reconnects"] += 1
                    await self.flush_trial_outbox()
                    break
                except Exception:
                    await asyncio.sleep(delay)          # backoff between reconnect attempts (0.25 s -> 2 s)
                    delay = min(delay * 2, 2.0)

    async def upload_retry(self, name: str, data: bytes) -> bool:
        deadline = time.monotonic() + self.upload_deadline_s
        attempts = 0
        while time.monotonic() < deadline:
            attempts += 1
            self.report["upload_attempts"][name] = attempts
            if self.connected and await self.upload_file(name, data):
                return True
            try:                                         # wait for the keeper to restore the link, then retry
                await asyncio.wait_for(self.ws_ready.wait(), 1.0)
            except asyncio.TimeoutError:
                pass
            await asyncio.sleep(0.2)
        return False

    # ---- status
    def _drain_trace(self, which: str) -> Tuple[List[float], float]:
        buf = self.trace_emg if which == "emg" else self.trace_acc
        cur = self._trace_cursor[which]
        new = [(t, v) for t, v in buf if t > cur][-100:]
        if not new:
            return [], 0.0
        self._trace_cursor[which] = new[-1][0]
        return [round(v, 2) for _, v in new], new[0][0]

    def build_status(self) -> Dict[str, Any]:
        now = time.monotonic()
        sess = self.clock.session_ms(now - self.t_replay0) if self.clock else 0.0
        remaining = None
        for t, _ph in self.next_phase_t:
            if t > sess:
                remaining = round((t - sess) / 1000.0, 1)
                break
        hap = self.links.get("haptic")
        bio = self.links.get("bio")
        hap_ok = bool(hap and hap.connected(now))
        bio_ok = bool(bio and bio.connected(now))
        msg = self.builder.status(
            state="paused" if self.paused else "running", game_id="phantom_hand",
            elapsed_s=sess / 1000.0, hands={"left": "high", "right": "high"}, real_hands=False,
            haptic={"connected": hap_ok, "last_cue_id": self.live_status.get("last_cue_id"), "battery_pct": None},
            patient_ref=self.patient_ref,
        )
        emg_vals, emg_t0 = self._drain_trace("emg")
        acc_vals, acc_t0 = self._drain_trace("acc")
        p = msg["payload"]
        p["game_state"] = {
            "phase": self.phase or "calibrate", "condition": self.condition, "remaining_s": remaining,
            "nodes": {"haptic": {"connected": hap_ok},
                      "bio": {"connected": bio_ok, "emg_level": round(self.last_emg_level, 3)
                              if (bio_ok and self.last_emg_level is not None) else None}},
        }
        t0s = [t for t in (emg_t0 if emg_vals else None, acc_t0 if acc_vals else None) if t is not None]
        p["trace"] = {"emg_env": emg_vals, "accel_mag": acc_vals, "t0_ms": int(min(t0s)) if t0s else int(sess),
                      "fs_hz": TRACE_FS_HZ}
        msg["session_id"] = self.state.session_id
        return msg

    async def _status_loop(self) -> None:
        while not self.done:
            msg = self.build_status()
            gs = msg["payload"]["game_state"]
            sent = await self.send_message(msg)
            self.report["hub_connected_samples"][0 if sent else 1] += 1
            if sent:
                self.report["statuses_sent"] += 1
                if not gs["nodes"]["haptic"]["connected"] and self.report["nodes"]["haptic"]:
                    self.report["status_haptic_disconnected"] += 1
                if not gs["nodes"]["bio"]["connected"]:
                    self.report["status_bio_disconnected"] += 1
                self.status_log.append({"phase": gs["phase"], "condition": gs["condition"],
                                        "haptic": gs["nodes"]["haptic"]["connected"],
                                        "bio": gs["nodes"]["bio"]["connected"]})
            await asyncio.sleep(self.status_period_s)

    # ---- replay
    async def _stroke(self, ev: Dict[str, Any]) -> None:
        d = ev.setdefault("data", {})
        link = self.links.get("haptic")
        self.report["cues_sent"] += 1
        if link is None:
            d["delivered"], d["ack_latency_ms"] = False, None
            return
        cue_id = f"ph-{self.report['cues_sent']:03d}-{uuid.uuid4().hex[:6]}"
        msg = {"cue_id": cue_id, "motor": int(d.get("motor", 0)), "intensity": CUE_INTENSITY,
               "duration_ms": CUE_DURATION_MS, "pattern": "pulse", "cue": "stroke",
               "play_at_ms": int(time.time() * 1000)}
        ack, rtt = await link.request(msg, cue_id)
        self.live_status["last_cue_id"] = cue_id
        ok = bool(ack and ack.get("ok", ack.get("status") in ("accepted", "executed")))
        d["delivered"] = ok
        d["ack_latency_ms"] = int(round(rtt)) if (ok and rtt is not None) else None
        if ok:
            self.report["cues_acked"] += 1
            self.report["cue_rtt_ms"].append(round(rtt, 2))
        elif ack:
            self.report["cue_rejected"].append({"cue_id": cue_id, "error": ack.get("error_code")})

    async def _emit(self, ev: Dict[str, Any]) -> None:
        typ = ev.get("type")
        if typ == "phase_start":
            d = ev.get("data") or {}
            self.phase, self.condition = d.get("phase"), d.get("condition")
            link = self.links.get("haptic")
            if link and self.phase == "induction" and self.condition:
                link.send({"type": "display", "text": self.condition.upper()})
                self.report["oled"].append(self.condition.upper())
        elif typ == "haptic_cue":
            await self._stroke(ev)
        elif typ == "threat_impact" and self.control_addr is not None:
            await self.control_cmd("flinch")
            self.report["flinch_sent"] += 1
        await self.send_trial_event(ev)
        self.report["events_sent"] += 1
        self._note_event_for_metrics(ev)

    async def replay_session(self) -> None:
        self.state.session_id = self.state.session_id or self.session_data.get("session_id")
        events = self.events_out
        times = [float(e.get("t_ms") or 0) for e in events]
        wall = compress_schedule(times, self.compress_gap_ms, self.speed, real_time_windows(events))
        self.clock = ReplayClock(wall, [max(times[: i + 1]) for i in range(len(times))])
        self.next_phase_t = [(float(e["t_ms"]), e["data"]["phase"]) for e in events if e.get("type") == "phase_start"]
        self.t_replay0 = time.monotonic()
        self.replay_start_time = time.time()
        self._keeper_task = asyncio.create_task(self._keeper())
        status_task = asyncio.create_task(self._status_loop())
        keep_task = asyncio.create_task(self._keepalive_loop()) if self.links else None
        hooks = list(self.progress_hooks)
        try:
            n = len(events)
            for i, ev in enumerate(events):
                dt = wall[i] - (time.monotonic() - self.t_replay0)
                if dt > 0:
                    await asyncio.sleep(dt)                  # schedule: wait for the event's moment
                await self._emit(ev)
                while hooks and (i + 1) / n >= hooks[0][0]:
                    await hooks.pop(0)[1]()
            # persist what actually happened on the wire
            self._events_raw_bytes = "".join(json.dumps(e) + "\n" for e in events).encode("utf-8")
            files: List[Tuple[str, bytes]] = [("session.json", self._session_raw_bytes),
                                              ("events.ndjson", self._events_raw_bytes)]
            files += [(f.name, f.read_bytes()) for f in sorted(self.session_dir.glob("kin_*.json"))]
            chunks = build_sens_chunks(self.state.session_id, self.rec_emg, self.rec_imu)
            self.report["sens_files"] = len(chunks)
            self.report["sens_samples"] = {"emg": len(self.rec_emg), "imu": len(self.rec_imu)}
            for c in chunks:
                data = json.dumps(c).encode("utf-8")
                (self.session_dir / f"sens_{c['chunk_seq']:03d}.json").write_bytes(data)
                files.append((f"sens_{c['chunk_seq']:03d}.json", data))
            self.report["upload_ok"] = {}
            for name, data in files:
                self.report["upload_ok"][name] = await self.upload_retry(name, data)
        finally:
            self.done = True
            for t in (status_task, keep_task, self._keeper_task):
                if t:
                    t.cancel()
                    try:
                        await t
                    except (asyncio.CancelledError, Exception):
                        pass
            for link in self.links.values():
                link.close()
        self.report["session_id"] = self.state.session_id
        self.report["cue_rtt_p50_ms"] = (sorted(self.report["cue_rtt_ms"])[len(self.report["cue_rtt_ms"]) // 2]
                                         if self.report["cue_rtt_ms"] else None)


async def run_phantom_cli(args: Any) -> int:
    """Entry for `fake_headset.py --game phantom_hand` (the stand-alone, no-runner use)."""
    import tempfile
    src = Path(args.session) if args.session else DEFAULT_FIXTURE
    work = Path(tempfile.mkdtemp(prefix="opus_ph_headset_")) / "session"
    node_a = _parse_hostport(args.node_a)
    node_b = _parse_hostport(args.node_b)
    if args.discovery_port and (node_a is None or node_b is None):
        found = await discover_nodes(args.discovery_port, tuple(k for k, v in (("haptic", node_a), ("bio", node_b)) if v is None))
        node_a = node_a or found.get("haptic")
        node_b = node_b or found.get("bio")
    sdir, sid = prepare_session_copy(src, work, drop_bio=node_b is None)
    hs = PhantomHeadset(sdir, node_a=node_a, node_b=node_b, control=_parse_hostport(args.control),
                        compress_gap_ms=args.compress_gap_ms, host=args.host, port=args.port, speed=args.speed,
                        beacon_port=args.beacon_port,
                        patient_ref=args.patient_ref, off_a_at=args.off_a_at)
    hs.state.session_id = sid
    await hs.start_nodes()
    await hs.wait_node_streams()
    hub_host, hub_port = await hs.discover_hub()
    await hs.connect_hub(hub_host, hub_port)
    ping = asyncio.create_task(hs.measure_latency())
    try:
        await hs.replay_session()
    finally:
        ping.cancel()
        await hs.disconnect()
    print(json.dumps({k: v for k, v in hs.report.items() if k != "cue_rtt_ms"}, indent=2))
    return 0 if all(hs.report.get("upload_ok", {}).values()) else 1


def _parse_hostport(s: Optional[str]) -> Optional[Tuple[str, int]]:
    if not s:
        return None
    host, _, port = s.partition(":")
    return host, int(port or 8790)
