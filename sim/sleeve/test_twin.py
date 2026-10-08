"""Tests for sim/sleeve/twin.py (twin-lite: fake Node A + Node B).

Two layers:
  * core tests drive `Twin` with an injected fake clock (exact maths for limits, duty, watchdog, expiry, rates);
  * UDP tests run the real `TwinServer` on port offsets 11000+ (never 8787/8788/8790/8791) and wait for
    readiness explicitly (bound sockets / latched discovery datagram), never a fixed sleep for a peer.

Shape checks are inline because contracts v0.2 schemas for sensor_chunk / emg_burst / device_discovery may not
exist yet (see CONTRACT REQUEST in logs/sessions/2026-10-07-PH-S-S1-run1.md).
"""

import itertools
import json
import os
import socket
import statistics
import subprocess
import sys
import time
from pathlib import Path

import pytest

import twin as T

REPO = Path(__file__).resolve().parents[2]
_offsets = itertools.count(11000, 10)


# --------------------------------------------------------------------------- core harness (fake clock)

class Clock:
    def __init__(self, t=100000.0):
        self.t = t

    def __call__(self):
        return self.t


class H:
    """Fake-clock harness: captures every datagram the twin sends."""

    def __init__(self, kinds=("haptic",), seed=1, **kw):
        self.clk = Clock()
        self.sent = []          # (kind, msg, addr, mono_ms)
        self.bcast = []         # (kind, msg)
        self.events = []        # stdout events
        kw.setdefault("log", T.EventLog(None, keep=True))
        self.tw = T.Twin(kinds, seed=seed, now_fn=self.clk,
                         send=lambda k, d, a: self.sent.append((k, json.loads(d), a, self.clk.t)),
                         broadcast=lambda k, d: self.bcast.append((k, json.loads(d))),
                         emit=self.events.append, **kw)

    @property
    def log(self):
        return self.tw.log.records

    def run(self, ms, dt=2.0):
        end = self.clk.t + ms
        while self.clk.t < end:
            self.clk.t = min(end, self.clk.t + dt)
            self.tw.step()

    def send(self, msg, addr=("10.0.0.1", 5000), kind="haptic"):
        data = msg if isinstance(msg, bytes) else json.dumps(msg).encode()
        self.tw.on_datagram(kind, data, addr)

    def msgs(self, typ, kind=None, addr=None):
        return [m for k, m, a, _ in self.sent
                if m.get("type") == typ and (kind is None or k == kind) and (addr is None or a == addr)]

    def strokes(self):
        return [r for r in self.log if r.get("ev") == "stroke"]

    def ev(self, name):
        return [r for r in self.log if r.get("ev") == "event" and r.get("name") == name]


def stroke(motor=0, intensity=150, dur=200, cue_id="s1", **kw):
    d = {"cue_id": cue_id, "motor": motor, "intensity": intensity, "duration_ms": dur, "pattern": "pulse",
         "cue": "stroke", "play_at_ms": 143250}
    d.update(kw)
    return d


def acks(h, cue_id):
    return [m for m in h.msgs("ack") if m.get("cue_id") == cue_id]


# --------------------------------------------------------------------------- discovery

def test_discovery_haptic_both_dialects():
    h = H(("haptic",))
    h.run(10)
    ks = [m for k, m in h.bcast if k == "haptic"]
    assert ks, "no discovery sent"
    d = ks[0]
    assert d["type"] == "device_discovery" and d["device_id"] == "SLEEVE_001" and d["device_kind"] == "haptic"
    assert d["firmware_version"] == "0.5.0" and d["command_port"] == 8790 and d["status"] == "available"
    assert d["motor_count"] == 2 and isinstance(d["timestamp_ms"], int)
    assert d["opus_haptic"] == 1 and d["port"] == 8790       # legacy hello in the same datagram


def test_discovery_bio_and_both_at_1hz():
    h = H(("haptic", "bio"))
    h.run(3100)
    b = [m for k, m in h.bcast if k == "bio"]
    a = [m for k, m in h.bcast if k == "haptic"]
    assert len(b) == len(a) == 4          # t=0,1,2,3 s
    d = b[0]
    assert d["device_id"] == "CHETNA_BIO_001" and d["device_kind"] == "bio" and d["motor_count"] == 0
    assert d["type"] == "device_discovery" and d["firmware_version"] == "0.5.0"
    assert d["command_port"] == 8792 and "opus_haptic" not in d
    assert [m["timestamp_ms"] for m in b[:3]] == [0, 1000, 2000] or b[1]["timestamp_ms"] - b[0]["timestamp_ms"] in range(990, 1011)


def test_kind_haptic_runs_no_bio():
    h = H(("haptic",))
    assert h.tw.B is None
    r = h.tw.control("squeeze")
    assert r["ok"] is False and "bio" in r["error"]


# --------------------------------------------------------------------------- Node A commands and limits

@pytest.mark.parametrize("wrap", ["bare", "haptic_envelope"])
def test_stroke_ack_both_dialects_and_latency_model(wrap):
    h = H()
    lat = []
    for i in range(60):
        cmd = stroke(motor=i % 2, dur=60, cue_id=f"c{i}")
        if wrap == "haptic_envelope":
            cmd["type"] = "haptic"
        t0 = h.clk.t
        h.send(cmd)
        h.run(30)
        a = acks(h, f"c{i}")
        assert len(a) == 1
        a = a[0]
        # dialect 1: contract (cue_id/status); dialect 2: game (ack_id/ok)
        assert a["status"] == "executed" and a["ack_id"] == f"c{i}" and a["ok"] is True and a["v"] == 1
        assert a["motor"] == i % 2
        sent_at = [t for k, m, ad, t in h.sent if m.get("cue_id") == f"c{i}"][0]
        lat.append(sent_at - t0)
        h.run(150 - 30)    # keep each motor under the duty limit
    # configured model: 8 ms +/- 4 (tick granularity 2 ms)
    assert 3.5 <= min(lat) and max(lat) <= 14.5, (min(lat), max(lat))
    assert 6.0 <= statistics.mean(lat) <= 10.0
    assert statistics.pstdev(lat) > 0.5          # there is jitter


def test_stroke_log_records_spinup_and_timing():
    h = H()
    h.send(stroke(cue_id="L1"))
    h.run(40)
    r = h.strokes()[0]
    assert r["status"] == "executed" and r["cue_id"] == "L1" and r["cue"] == "stroke"
    assert r["spinup_ms"] == 30
    assert r["motor_on_ms"] - r["recv_ts_ms"] == 30
    assert r["motor_off_ms"] - r["recv_ts_ms"] == 200
    assert r["intensity_in"] == 150 and r["intensity_applied"] == 150
    assert r["ack_send_ts_ms"] - r["recv_ts_ms"] == pytest.approx(r["ack_latency_ms"], abs=1.0)
    assert 4.0 <= r["ack_latency_ms"] <= 12.0 and r["play_at_ms"] == 143250
    rx = [x for x in h.log if x["ev"] == "rx"]
    assert rx and rx[0]["msg"]["cue_id"] == "L1" and rx[0]["t_ms"] == r["recv_ts_ms"]


def test_intensity_capped_at_150_and_duration_clamped():
    h = H()
    h.send(stroke(motor=0, intensity=255, dur=1000, cue_id="a"))
    h.send(stroke(motor=1, intensity=200, dur=10, cue_id="b"))
    h.run(20)
    ra, rb = h.strokes()
    assert ra["intensity_applied"] == 150 and ra["duration_applied"] == 400
    assert rb["intensity_applied"] == 150 and rb["duration_applied"] == 50
    assert ra["motor_off_ms"] - ra["recv_ts_ms"] == 400 and rb["motor_off_ms"] - rb["recv_ts_ms"] == 50
    # and the motor really stops at the clamped end
    h.run(500)
    assert not any(m["active"] for m in h.tw.A.motors)


def test_min_gap_100ms_per_motor():
    h = H()
    h.send(stroke(motor=0, cue_id="g1"))
    h.run(99)
    h.send(stroke(motor=0, cue_id="g2"))            # 99 ms after g1 start: rejected
    h.send(stroke(motor=1, cue_id="g3"))            # other motor unaffected
    h.run(1)
    h.send(stroke(motor=0, cue_id="g4"))            # exactly 100 ms after g1: accepted
    h.run(30)
    st = {r["cue_id"]: r for r in h.strokes()}
    assert st["g1"]["status"] == "executed"
    assert st["g2"]["status"] == "rejected" and st["g2"]["error_code"] == "CUE_GAP"
    assert st["g3"]["status"] == "executed"
    assert st["g4"]["status"] == "executed"
    rej = acks(h, "g2")[0]
    assert rej["ok"] is False and rej["status"] == "rejected" and rej["error_code"] == "CUE_GAP"


def test_duty_cycle_50pct_per_10s():
    h = H()
    accepted_on = []                 # (start, end) of accepted pulses
    first_reject = None
    for i in range(40):
        t = h.clk.t
        h.send(stroke(motor=0, dur=400, cue_id=f"d{i}"))
        r = h.strokes()[-1]
        if r["status"] == "executed":
            accepted_on.append((t, t + 400))
        elif first_reject is None:
            first_reject = i
            assert r["error_code"] == "DUTY_CYCLE_LIMIT"
        h.run(500)
    assert first_reject is not None and 10 <= first_reject <= 14, first_reject
    # property: in no 10 s window did accepted ON time exceed 50 % + one pulse
    for s, _ in accepted_on:
        on = sum(max(0.0, min(e, s + 10000) - max(b, s)) for b, e in accepted_on)
        assert on <= 5000 + 400, on
    # recovers after cooling down
    h.run(6000)
    h.send(stroke(motor=0, dur=200, cue_id="after"))
    assert h.strokes()[-1]["status"] == "executed"
    # the other motor has its own budget
    h2 = H()
    for i in range(14):
        h2.send(stroke(motor=0, dur=400, cue_id=f"x{i}"))
        h2.run(500)
    h2.send(stroke(motor=1, dur=400, cue_id="other"))
    assert h2.strokes()[-1]["status"] == "executed"


def test_rejects_unfitted_invalid_missing_pattern():
    h = H()
    h.send(stroke(motor=2, cue_id="m2"))
    h.send(stroke(motor=3, cue_id="m3"))
    h.send(stroke(motor=4, cue_id="m4"))
    h.send({"cue_id": "miss", "motor": 0})
    h.send(stroke(motor=0, cue_id="pat", pattern="wobble"))
    h.run(30)
    codes = {r["cue_id"]: r["error_code"] for r in h.strokes()}
    assert codes == {"m2": "MOTOR_UNAVAILABLE", "m3": "MOTOR_UNAVAILABLE", "m4": "INVALID_MOTOR",
                     "miss": "MISSING_FIELD", "pat": "INVALID_PATTERN"}
    assert not any(m["active"] for m in h.tw.A.motors)        # nothing ever vibrated
    assert all(a["ok"] is False for a in h.msgs("ack"))


def test_legacy_cue_envelope_and_ping_ack():
    h = H()
    h.send({"v": 1, "type": "cue", "id": "leg1", "cue": "x", "intensity": 0.6, "duration_ms": 300,
            "pattern": "pulse"})
    h.send({"v": 1, "type": "ping", "id": "p1"})
    h.run(30)
    a = acks(h, "leg1")[0]
    assert a["ok"] and a["ack_id"] == "leg1"
    assert h.strokes()[0]["intensity_applied"] == 150          # 0.6*255 = 153 -> capped
    p = acks(h, "p1")[0]
    assert p["status"] == "accepted" and p["ok"] is True


def test_malformed_datagram_counted_and_error_acked():
    h = H()
    h.send(b"{not json")
    h.run(30)
    assert h.tw.A.stats["rx_invalid"] == 1
    a = h.msgs("ack")[0]
    assert a["status"] == "error" and a["error_code"] == "BAD_JSON" and a["ok"] is False
    assert not any(m["active"] for m in h.tw.A.motors)


def test_unknown_type_never_a_motor_command():
    h = H()
    h.send({"type": "mystery", "motor": 0, "intensity": 100, "duration_ms": 200})
    h.run(10)
    assert not any(m["active"] for m in h.tw.A.motors) and not h.strokes()
    assert h.tw.A.stats["rx_invalid"] == 1


# --------------------------------------------------------------------------- display

def test_display_oled_line_limits():
    h = H()
    h.send({"type": "display", "text": "SYNC"})
    assert h.events[-1] == {"event": "oled", "text": "SYNC"} and h.tw.A.display_text == "SYNC"
    h.run(100)
    h.send({"type": "display", "text": "123456789012"})        # 12 chars: ok (2nd within 1 s)
    assert h.tw.A.display_text == "123456789012"
    h.send({"type": "display", "text": "3rd in 1s"})            # > 2/s: rejected
    assert h.tw.A.display_text == "123456789012"
    h.run(1100)
    h.send({"type": "display", "text": "1234567890123"})        # 13 chars: rejected
    assert h.tw.A.display_text == "123456789012"
    h.send({"type": "display", "text": "ASYNC"})                # window clear again
    assert h.tw.A.display_text == "ASYNC"
    assert len(h.ev("display_rejected")) == 2
    assert [e["text"] for e in h.events if e["event"] == "oled"] == ["SYNC", "123456789012", "ASYNC"]


# --------------------------------------------------------------------------- watchdog

def test_watchdog_without_keepalive_forces_motors_off_at_2s():
    h = H()
    h.send(stroke(motor=0, cue_id="w"))
    h.tw.control("stick-a 0")                   # fault: driver latched on after its pulse
    h.run(1900)
    assert h.tw.A.motors[0]["active"] and not h.ev("watchdog")      # latched on, not yet 2 s
    h.run(250)
    wd = h.ev("watchdog")
    assert len(wd) == 1 and wd[0]["motors_forced_off"] == [0]
    assert not h.tw.A.motors[0]["active"]
    # off no later than ~2 s after the last datagram
    off = [e for e in h.ev("motor_off") if e["reason"] == "watchdog"][0]
    assert 2000 <= off["t_ms"] - h.strokes()[0]["recv_ts_ms"] <= 2010
    # re-arms on the next command
    h.tw.control("unstick-a 0")
    h.send(stroke(motor=0, cue_id="w2"))
    h.run(2500)
    assert len(h.ev("watchdog")) == 2


def test_watchdog_keepalive_ping_and_subscribe_feed_it():
    h = H()
    me = ("10.0.0.9", 6000)
    h.send(stroke(motor=0, cue_id="k"), addr=me)
    h.tw.control("stick-a 0")
    for i in range(10):                          # 4.5 s of silence-except-ping every 500 ms
        h.run(500)
        h.send({"type": "ping", "id": f"p{i}"} if i % 2 == 0 else {"type": "subscribe"}, addr=me)
    assert not h.ev("watchdog")
    assert h.tw.A.motors[0]["active"]            # still (fault-)latched: only stop/watchdog clears it
    h.send({"type": "stop"}, addr=me)
    assert not h.tw.A.motors[0]["active"]


def test_watchdog_not_armed_before_first_command():
    h = H()
    h.run(5000)
    assert not h.ev("watchdog")


# --------------------------------------------------------------------------- subscribers

def test_subscribers_fanout_cap_and_expiry():
    h = H(("haptic", "bio"))
    peers = [("10.0.0.%d" % i, 7000 + i) for i in range(1, 5)]
    for kind in ("haptic", "bio"):
        for p in peers:
            h.send({"type": "subscribe"}, addr=p, kind=kind)
        assert len(h.tw.A.live_subs(h.clk.t)) <= 3 and len(h.tw.B.live_subs(h.clk.t)) <= 3
    assert h.tw.A.stats["sub_rejected"] == 1 and h.tw.B.stats["sub_rejected"] == 1
    h.run(1000)
    for p in peers[:3]:
        assert any(a == p for k, m, a, _ in h.sent if m.get("type") == "sensor_data"), p
        assert any(a == p for k, m, a, _ in h.sent if m.get("type") == "sensor_chunk"), p
    assert not any(a == peers[3] for k, m, a, _ in h.sent)
    n_sd = {p: len(h.msgs("sensor_data", addr=p)) for p in peers[:3]}
    assert all(95 <= n <= 101 for n in n_sd.values()), n_sd            # every subscriber gets the full 100 Hz
    # refresh only peer 0 (subscribe) and peer 1 (ping on A) -> peer 2 expires at 5 s
    for _ in range(8):
        h.run(500)
        h.send({"type": "subscribe"}, addr=peers[0], kind="haptic")
        h.send({"type": "ping", "id": "k"}, addr=peers[1], kind="haptic")
    live = set(h.tw.A.live_subs(h.clk.t))
    assert live == {peers[0], peers[1]}
    assert [e["addr"] for e in h.ev("sub_expired") if e["node"] == "A"] == [list(peers[2])]
    # a slot is free again: the previously rejected peer can now join
    h.send({"type": "subscribe"}, addr=peers[3], kind="haptic")
    assert peers[3] in h.tw.A.live_subs(h.clk.t)
    # an expired subscriber receives nothing further
    mark = len(h.sent)
    h.run(500)
    assert not any(a == peers[2] and m.get("type") == "sensor_data" for k, m, a, _ in h.sent[mark:])


def test_commands_do_not_subscribe_and_acks_go_to_sender_only():
    h = H()
    sub, other = ("10.0.0.1", 1111), ("10.0.0.2", 2222)
    h.send({"type": "subscribe"}, addr=sub)
    h.send(stroke(cue_id="o1"), addr=other)
    h.run(1000)
    assert [a for k, m, a, _ in h.sent if m.get("cue_id") == "o1"] == [other]
    assert not h.msgs("sensor_data", addr=other)
    assert h.msgs("sensor_data", addr=sub)
    assert h.msgs("status", addr=sub)           # 1 Hz status goes to subscribers too


# --------------------------------------------------------------------------- streams

def _is_num(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool)


def check_sensor_data(m):
    assert m["type"] == "sensor_data" and m["device_id"] == "SLEEVE_001" and isinstance(m["timestamp_ms"], int)
    s = m["sensors"]
    for k, unit in (("imu_accel_x", "m/s2"), ("imu_accel_y", "m/s2"), ("imu_accel_z", "m/s2"),
                    ("imu_gyro_x", "rad/s"), ("imu_gyro_y", "rad/s"), ("imu_gyro_z", "rad/s")):
        assert set(s[k]) == {"value", "unit", "status"} and _is_num(s[k]["value"])
        assert s[k]["unit"] == unit and s[k]["status"] == "ok"


def check_sensor_chunk(m):
    assert m["type"] == "sensor_chunk" and m["device_id"] == "CHETNA_BIO_001" and m["device_kind"] == "bio"
    assert isinstance(m["timestamp_ms"], int) and m["sample_rate_hz"] == 100
    assert m["unit"] == "raw_adc" and m["status"] == "ok"
    assert len(m["emg_envelope"]) == 10 and all(_is_num(v) and 0 <= v <= 4095 for v in m["emg_envelope"])


def test_node_a_sensor_data_100hz_shape_and_quiet_arm():
    h = H()
    peer = ("10.0.0.1", 5000)
    h.send({"type": "subscribe"}, addr=peer)
    h.run(2000)
    sd = h.msgs("sensor_data")
    assert 195 <= len(sd) <= 201
    for m in sd:
        check_sensor_data(m)
    ts = [m["timestamp_ms"] for m in sd]
    assert set(b - a for a, b in zip(ts, ts[1:])) <= {9, 10, 11}
    mags = [(m["sensors"]["imu_accel_x"]["value"] ** 2 + m["sensors"]["imu_accel_y"]["value"] ** 2
             + m["sensors"]["imu_accel_z"]["value"] ** 2) ** 0.5 for m in sd]
    assert all(9.4 < g < 10.2 for g in mags)                    # quiet arm: ~1 g
    assert statistics.pstdev(mags) < 0.2


def test_node_b_sensor_chunk_every_100ms_baseline():
    h = H(("bio",))
    peer = ("10.0.0.1", 5000)
    h.send({"type": "subscribe"}, addr=peer, kind="bio")
    h.run(3000)
    ch = h.msgs("sensor_chunk")
    assert 29 <= len(ch) <= 30
    for m in ch:
        check_sensor_chunk(m)
    assert set(b["timestamp_ms"] - a["timestamp_ms"] for a, b in zip(ch, ch[1:])) <= {99, 100, 101}
    env = [v for m in ch for v in m["emg_envelope"]]
    assert abs(statistics.mean(env) - T.EMG_BASELINE) < 3.0
    assert 3.0 < statistics.pstdev(env) < 10.0
    assert not h.msgs("emg_burst")                               # quiet baseline never fires


def _env_with_time(h):
    """(device_ms, value) for every EMG sample, derived from chunk timestamps (chunk ts = FIRST sample, PRD §9.3)."""
    out = []
    for m in h.msgs("sensor_chunk"):
        for i, v in enumerate(m["emg_envelope"]):
            out.append((m["timestamp_ms"] + i * 10, v))
    return out


def test_motor_artefact_on_emg_while_node_a_pulses():
    h = H(("haptic", "bio"))
    peer = ("10.0.0.1", 5000)
    h.send({"type": "subscribe"}, addr=peer, kind="bio")
    h.run(1000)
    for i in range(6):
        h.send(stroke(motor=i % 2, dur=400, cue_id=f"a{i}"), addr=peer)
        h.run(1000)
    env = _env_with_time(h)
    boot = h.tw.B.boot_ms
    wins = [(h.tw.wall(0) and (r["motor_on_ms"] - h.tw.wall(boot)), r["motor_off_ms"] - h.tw.wall(boot))
            for r in h.strokes()]
    inside = [v for t, v in env if any(on + 10 <= t <= off for on, off in wins)]
    outside = [v for t, v in env if not any(on - 100 <= t <= off + 100 for on, off in wins)]
    assert len(inside) > 100 and len(outside) > 100
    diff = statistics.mean(inside) - statistics.mean(outside)
    assert 30 <= diff <= 70, diff                                # raised mean at intensity 150
    assert statistics.pstdev(inside) > statistics.pstdev(outside)    # and raised variance
    assert not h.msgs("emg_burst")                               # artefact must not fake a burst


def test_no_artefact_without_pulses_or_when_node_a_off():
    h = H(("haptic", "bio"))
    h.send({"type": "subscribe"}, addr=("10.0.0.1", 5000), kind="bio")
    h.tw.control("off-a")
    h.send(stroke(), addr=("10.0.0.1", 5000))                    # ignored: powered off
    h.run(1500)
    env = [v for _, v in _env_with_time(h)]
    assert abs(statistics.mean(env) - T.EMG_BASELINE) < 3.0
    assert not h.msgs("ack")


# --------------------------------------------------------------------------- flinch / squeeze

def test_flinch_emg_burst_at_120ms_and_imu_jolt_at_150ms():
    h = H(("haptic", "bio"))
    peer = ("10.0.0.1", 5000)
    h.send({"type": "subscribe"}, addr=peer, kind="bio")
    h.send({"type": "subscribe"}, addr=peer, kind="haptic")
    h.run(2000)
    assert not h.msgs("emg_burst")
    t0 = h.clk.t
    dev0 = h.tw.A.dev_ms(t0)
    assert h.tw.control("flinch")["ok"]
    h.run(1500)
    bursts = h.msgs("emg_burst")
    assert len(bursts) == 1
    b = bursts[0]
    assert b["device_id"] == "CHETNA_BIO_001"
    assert t0 - h.tw.B.boot_ms + 105 <= b["timestamp_ms"] <= t0 - h.tw.B.boot_ms + 200      # onset ~ +120 ms
    assert b["peak"] >= 3 * T.EMG_BASELINE and b["baseline_rms"] == T.EMG_BASELINE
    # IMU: quiet before +150 ms, big jolt after
    sd = h.msgs("sensor_data")
    def dev(m):
        s = m["sensors"]
        return max(abs(s["imu_accel_x"]["value"]), abs(s["imu_accel_y"]["value"]),
                   abs(s["imu_accel_z"]["value"] - 9.81))
    before = [dev(m) for m in sd if dev0 - 500 <= m["timestamp_ms"] < dev0 + 148]
    after = [(m["timestamp_ms"] - dev0, dev(m)) for m in sd if dev0 + 148 <= m["timestamp_ms"] <= dev0 + 450]
    assert max(before) < 0.5
    assert max(d for _, d in after) > 8.0
    first_big = min(t for t, d in after if d > 2.0)
    assert 150 <= first_big <= 200, first_big
    # EMG envelope itself: quiet before +120 ms
    env = _env_with_time(h)
    base0 = h.tw.B.dev_ms(t0)
    assert max(v for t, v in env if base0 - 500 <= t < base0 + 110) < T.EMG_BASELINE + 40
    assert max(v for t, v in env if base0 + 120 <= t <= base0 + 470) > 3 * T.EMG_BASELINE * 0.9


def test_squeeze_sustained_burst_over_3x_baseline():
    h = H(("bio",))
    h.send({"type": "subscribe"}, addr=("10.0.0.1", 5000), kind="bio")
    h.run(1000)
    h.tw.control("squeeze")
    h.run(1500)
    env = _env_with_time(h)
    hot = [v for _, v in env if v > 2 * T.EMG_BASELINE]
    assert len(hot) >= 60                                         # ~0.8 s above 2x baseline
    assert max(v for _, v in env) >= 3.5 * T.EMG_BASELINE
    b = h.msgs("emg_burst")
    assert len(b) == 1 and b[0]["peak"] >= 3 * T.EMG_BASELINE


def test_flinch_with_only_haptic_or_only_bio_applies_what_exists():
    h = H(("bio",))
    assert h.tw.control("flinch")["applied"] == ["emg+120ms"]
    h = H(("haptic",))
    assert h.tw.control("flinch")["applied"] == ["imu+150ms"]


# --------------------------------------------------------------------------- control: power, jitter, loss

def test_off_a_on_a_silences_and_restores():
    h = H(("haptic",))
    peer = ("10.0.0.1", 5000)
    h.send({"type": "subscribe"}, addr=peer)
    h.run(1200)
    assert h.msgs("sensor_data") and h.bcast
    h.tw.control("off-a")
    n_sent, n_b = len(h.sent), len(h.bcast)
    h.send(stroke(cue_id="dead"), addr=peer)
    h.run(2500)
    assert len(h.sent) == n_sent and len(h.bcast) == n_b            # fully silent
    assert not any(m["active"] for m in h.tw.A.motors)
    assert any(r["ev"] == "rx" and r["powered"] is False and r["msg"].get("cue_id") == "dead" for r in h.log)
    h.tw.control("on-a")
    assert h.tw.A.live_subs(h.clk.t) == []                           # a reboot forgets subscribers
    h.run(1100)
    assert len(h.bcast) > n_b                                        # discovery is back
    mark = len(h.sent)
    h.send({"type": "subscribe"}, addr=peer)
    h.send(stroke(cue_id="alive"), addr=peer)
    h.run(100)
    assert acks(h, "alive") and any(m.get("type") == "sensor_data" for _, m, _, _ in h.sent[mark:])


def test_off_b_on_b_only_affects_node_b():
    h = H(("haptic", "bio"))
    pb, pa = ("10.0.0.1", 5000), ("10.0.0.1", 5001)
    h.send({"type": "subscribe"}, addr=pb, kind="bio")
    h.send({"type": "subscribe"}, addr=pa, kind="haptic")
    h.run(500)
    h.tw.control("off-b")
    mark = len(h.sent)
    h.run(1000)
    new = [m["type"] for _, m, _, _ in h.sent[mark:]]
    assert "sensor_chunk" not in new and "sensor_data" in new
    h.tw.control("on-b")
    h.send({"type": "subscribe"}, addr=pb, kind="bio")
    mark = len(h.sent)
    h.run(500)
    assert "sensor_chunk" in [m["type"] for _, m, _, _ in h.sent[mark:]]


def test_jitter_and_latency_commands_change_ack_timing():
    def spread(cmds):
        h = H(seed=3)
        for c in cmds:
            assert h.tw.control(c)["ok"]
        lat = []
        for i in range(40):
            t0 = h.clk.t
            h.send(stroke(motor=i % 2, dur=60, cue_id=f"j{i}"))
            h.run(100)
            lat.append([t for k, m, a, t in h.sent if m.get("cue_id") == f"j{i}"][0] - t0)
            h.run(100)
        return lat
    tight = spread(["jitter 0", "latency 8"])
    wide = spread(["jitter 30", "latency 40"])
    assert max(tight) - min(tight) <= 3.0 and statistics.mean(tight) == pytest.approx(8, abs=2)
    assert max(wide) - min(wide) > 20 and statistics.mean(wide) == pytest.approx(40, abs=8)
    assert H().tw.control("jitter x")["ok"] is False


def test_loss_pct_drops_inbound_and_outbound_but_not_discovery():
    h = H(("haptic",))
    peer = ("10.0.0.1", 5000)
    h.send({"type": "subscribe"}, addr=peer)
    assert h.tw.control("loss 100")["loss_pct"] == 100
    h.send(stroke(cue_id="lost"), addr=peer)
    h.run(2200)
    assert not h.strokes()                                  # inbound command dropped
    assert not h.msgs("sensor_data", addr=peer)             # outbound stream dropped too
    assert len(h.bcast) >= 2                                # discovery unaffected
    h.tw.control("loss 0")
    h.send({"type": "subscribe"}, addr=peer)
    h.send(stroke(cue_id="ok"), addr=peer)
    h.run(100)
    assert acks(h, "ok")
    # statistical: 50 % outbound loss with a fixed seed
    h2 = H(("haptic",), seed=5)
    h2.send({"type": "subscribe"}, addr=peer)
    h2.tw.control("loss 50")
    h2.run(4000)
    n = len(h2.msgs("sensor_data"))
    assert 0.35 * 400 <= n <= 0.65 * 400, n
    assert h2.tw.A.stats["tx_lost"] > 100


def test_status_and_unknown_and_quit_controls():
    h = H(("haptic", "bio"))
    r = h.tw.control("status")
    assert r["ok"] and r["state"]["kinds"] == ["haptic", "bio"] and r["state"]["haptic"]["powered"] is True
    assert h.tw.control("bogus")["ok"] is False
    assert h.tw.control("")["ok"] is False
    assert h.tw.quit_requested is False
    h.tw.control("quit")
    assert h.tw.quit_requested is True
    assert {"event": "control", "cmd": "quit", "ok": True} in h.events


def test_port_offset_maps_ports():
    assert H(("haptic", "bio")).tw.ports == {"haptic": 8790, "bio": 8792, "discovery": 8791, "control": 8793}
    tw = T.Twin(("haptic", "bio"), port_offset=20000)
    assert tw.ports == {"haptic": 28790, "bio": 28792, "discovery": 28791, "control": 28793}
    assert tw.A.port == 28790 and tw.B.port == 28792
    assert tw.A.discovery(0)["command_port"] == 28790 and tw.B.discovery(0)["command_port"] == 28792


def test_determinism_same_seed_same_stream():
    def run(seed):
        h = H(("haptic", "bio"), seed=seed)
        h.send({"type": "subscribe"}, addr=("a", 1), kind="bio")
        h.send({"type": "subscribe"}, addr=("a", 1), kind="haptic")
        h.run(500, dt=10)
        return json.dumps([m for _, m, _, _ in h.sent if m["type"] in ("sensor_chunk", "sensor_data")])
    assert run(7) == run(7) and run(7) != run(8)


# --------------------------------------------------------------------------- real UDP

class Peer:
    def __init__(self):
        self.s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.s.bind(("127.0.0.1", 0))
        self.s.settimeout(0.05)

    def send(self, msg, port):
        self.s.sendto(json.dumps(msg).encode(), ("127.0.0.1", port))

    def recv_until(self, pred, timeout=3.0):
        """Collect messages until pred(msg) is true or timeout; returns (match|None, all)."""
        end = time.monotonic() + timeout
        got = []
        while time.monotonic() < end:
            try:
                data, _ = self.s.recvfrom(65535)
            except socket.timeout:
                continue
            m = json.loads(data)
            got.append(m)
            if pred(m):
                return m, got
        return None, got

    def close(self):
        self.s.close()


@pytest.fixture
def server():
    made = []

    def make(kinds, **kw):
        off = next(_offsets)
        tw = T.Twin(kinds, port_offset=off, log=T.EventLog(None, keep=True), emit=made_events.append, **kw)
        srv = T.TwinServer(tw, host="127.0.0.1")
        srv.start()               # returns only after every socket is bound: that is the readiness point
        made.append(srv)
        return srv
    made_events = []
    make.events = made_events
    yield make
    for s in made:
        s.stop()


def test_udp_ready_event_and_discovery_latched(server):
    off_probe = next(_offsets)
    # bind the discovery listener first (readiness), then start the twin on that offset
    disc = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    disc.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    disc.bind(("127.0.0.1", 8791 + off_probe))
    disc.settimeout(3.0)
    tw = T.Twin(("haptic", "bio"), port_offset=off_probe, emit=server.events.append)
    srv = T.TwinServer(tw)
    srv.start()
    try:
        ready = [e for e in server.events if e.get("event") == "ready"][0]
        assert ready["kinds"] == ["haptic", "bio"]
        assert ready["ports"] == {"haptic": 8790 + off_probe, "bio": 8792 + off_probe,
                                  "discovery": 8791 + off_probe, "control": 8793 + off_probe}
        seen = {}
        end = time.monotonic() + 3.0
        while len(seen) < 2 and time.monotonic() < end:
            try:
                data, _ = disc.recvfrom(4096)
            except socket.timeout:
                break
            m = json.loads(data)
            seen[m["device_kind"]] = m
        assert seen["haptic"]["device_id"] == "SLEEVE_001" and seen["haptic"]["opus_haptic"] == 1
        assert seen["haptic"]["command_port"] == 8790 + off_probe
        assert seen["bio"]["device_id"] == "CHETNA_BIO_001" and seen["bio"]["command_port"] == 8792 + off_probe
    finally:
        srv.stop()
        disc.close()


def test_udp_stroke_ack_rtt_and_stream_rate(server):
    srv = server(("haptic",))
    port = srv.twin.ports["haptic"]
    p = Peer()
    try:
        p.send({"type": "subscribe"}, port)
        first, _ = p.recv_until(lambda m: m.get("type") == "sensor_data")
        assert first is not None, "no stream after subscribe"
        rtts = []
        for i in range(8):
            t0 = time.perf_counter()
            p.send(stroke(motor=i % 2, cue_id=f"u{i}"), port)
            a, _ = p.recv_until(lambda m, i=i: m.get("type") == "ack" and m.get("cue_id") == f"u{i}")
            assert a is not None and a["status"] == "executed"
            rtts.append((time.perf_counter() - t0) * 1000)
            time.sleep(0.12)         # respect the 100 ms motor gap between strokes (pacing, not a peer wait)
        assert 3.0 <= statistics.median(rtts) <= 30.0, rtts
        # stream rate over ~1 s wall
        n, t_end = 0, time.monotonic() + 1.0
        while time.monotonic() < t_end:
            try:
                data, _ = p.s.recvfrom(65535)
            except socket.timeout:
                continue
            n += json.loads(data).get("type") == "sensor_data"
        assert 85 <= n <= 115, n
    finally:
        p.close()


def test_udp_node_b_chunks_flinch_via_control_port(server):
    srv = server(("haptic", "bio"))
    ports = srv.twin.ports
    pb, ctl = Peer(), Peer()
    try:
        pb.send({"type": "subscribe"}, ports["bio"])
        c, _ = pb.recv_until(lambda m: m.get("type") == "sensor_chunk")
        assert c is not None
        check_sensor_chunk(c)
        ctl.s.sendto(b"flinch", ("127.0.0.1", ports["control"]))
        reply, _ = ctl.recv_until(lambda m: "ok" in m)
        assert reply == {"ok": True, "cmd": "flinch", "applied": ["emg+120ms", "imu+150ms"]}
        burst, _ = pb.recv_until(lambda m: m.get("type") == "emg_burst", timeout=3.0)
        assert burst is not None and burst["peak"] >= 3 * T.EMG_BASELINE
    finally:
        pb.close()
        ctl.close()


def test_udp_watchdog_real_2s_with_and_without_keepalive(server):
    srv = server(("haptic",))
    port, cport = srv.twin.ports["haptic"], srv.twin.ports["control"]
    p, ctl = Peer(), Peer()

    def control(cmd):
        ctl.s.sendto(cmd.encode(), ("127.0.0.1", cport))
        r, _ = ctl.recv_until(lambda m: "ok" in m)
        return r

    try:
        control("stick-a 0")
        p.send(stroke(cue_id="wd1"), port)
        a, _ = p.recv_until(lambda m: m.get("type") == "ack")
        assert a is not None
        t_cmd = time.monotonic()
        # keepalive for 3 s: motor stays (latched) and no watchdog
        t_last = t_cmd
        while time.monotonic() - t_cmd < 3.0:
            p.send({"type": "ping", "id": "ka"}, port)
            t_last = time.monotonic()
            time.sleep(0.5)
        st = control("status")["state"]["haptic"]
        assert st["watchdog_events"] == 0 and st["active"][0] is True
        # now stop sending: watchdog fires ~2 s after the last datagram
        end = t_last + 4.0
        while time.monotonic() < end:
            st = control("status")["state"]["haptic"]
            if st["watchdog_events"]:
                break
            time.sleep(0.1)
        waited = time.monotonic() - t_last
        assert st["watchdog_events"] == 1 and st["active"][0] is False
        assert 1.8 <= waited <= 2.6, waited
    finally:
        p.close()
        ctl.close()


def test_cli_subprocess_ready_line_log_and_exit(tmp_path):
    off = next(_offsets)
    log = tmp_path / "twin.jsonl"
    proc = subprocess.Popen(
        [sys.executable, "-m", "sim.sleeve.twin", "--kind", "both", "--port-offset", str(off), "--seed", "3",
         "--log", str(log), "--no-stdin", "--duration", "6"],
        cwd=str(REPO), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        ready = json.loads(proc.stdout.readline())      # blocks until the twin prints readiness
        assert ready["event"] == "ready" and ready["kinds"] == ["haptic", "bio"] and ready["seed"] == 3
        assert ready["ports"]["haptic"] == 8790 + off and ready["ports"]["control"] == 8793 + off
        p = Peer()
        p.send(stroke(cue_id="cli1"), ready["ports"]["haptic"])
        a, _ = p.recv_until(lambda m: m.get("type") == "ack")
        assert a and a["cue_id"] == "cli1" and a["status"] == "executed"
        p.send({"type": "display", "text": "SYNC"}, ready["ports"]["haptic"])
        p.s.sendto(b"quit", ("127.0.0.1", ready["ports"]["control"]))
        out, err = proc.communicate(timeout=10)
        assert '"event": "oled"' in out and '"text": "SYNC"' in out and '"event": "exit"' in out
        p.close()
    finally:
        if proc.poll() is None:
            proc.kill()
    recs = [json.loads(l) for l in log.read_text().splitlines()]
    s = [r for r in recs if r["ev"] == "stroke"][0]
    assert s["cue_id"] == "cli1" and s["status"] == "executed" and s["spinup_ms"] == 30
    assert s["ack_send_ts_ms"] >= s["recv_ts_ms"]


# --------------------------------------------------------------------------- LAN beacons and the control port (cross-machine twin)
# Canned OS text, no network access: the parser must read ipconfig / ip / ifconfig output of any language.

IPCONFIG_EN = """\
Windows IP Configuration


Unknown adapter Tailscale:

   Connection-specific DNS Suffix  . : tail1234.ts.net
   IPv4 Address. . . . . . . . . . . : 100.101.102.103
   Subnet Mask . . . . . . . . . . . : 255.255.255.255
   Default Gateway . . . . . . . . . :

Wireless LAN adapter Wi-Fi:

   Connection-specific DNS Suffix  . : lan
   IPv6 Address. . . . . . . . . . . : 2401:4900:1::7
   Link-local IPv6 Address . . . . . : fe80::1c2b:3d4e:5f60:7182%12
   IPv4 Address. . . . . . . . . . . : 192.168.242.190
   Subnet Mask . . . . . . . . . . . : 255.255.255.0
   Default Gateway . . . . . . . . . : fe80::a8b7:c6d5:e4f3:2a1b%12
                                       192.168.242.1

Ethernet adapter vEthernet (Default Switch):

   Link-local IPv6 Address . . . . . : fe80::3c4d:5e6f:7081:92a3%25
   IPv4 Address. . . . . . . . . . . : 172.26.16.1
   Subnet Mask . . . . . . . . . . . : 255.255.240.0

Ethernet adapter VirtualBox Host-Only Network:

   IPv4 Address. . . . . . . . . . . : 192.168.56.1
   Subnet Mask . . . . . . . . . . . : 255.255.255.0

Ethernet adapter Ethernet:

   Media State . . . . . . . . . . . : Media disconnected
   Connection-specific DNS Suffix  . :

Ethernet adapter Ethernet 2:

   Autoconfiguration IPv4 Address. . : 169.254.7.8
   Subnet Mask . . . . . . . . . . . : 255.255.0.0
   Default Gateway . . . . . . . . . :
"""

IPCONFIG_DE = """\
Windows-IP-Konfiguration

Drahtlos-LAN-Adapter WLAN:

   Verbindungsspezifisches DNS-Suffix: fritz.box
   IPv4-Adresse  . . . . . . . . . . : 192.168.178.23
   Subnetzmaske  . . . . . . . . . . : 255.255.255.0
   Standardgateway . . . . . . . . . : 192.168.178.1
"""

IP_ADDR_LINUX = """\
1: lo    inet 127.0.0.1/8 scope host lo\\       valid_lft forever preferred_lft forever
2: eth0    inet 192.168.1.10/24 brd 192.168.1.255 scope global dynamic eth0\\       valid_lft 86377sec preferred_lft 86377sec
3: wlan0    inet 10.0.0.7/16 brd 10.0.255.255 scope global wlan0\\       valid_lft forever preferred_lft forever
4: tun0    inet 10.8.0.2/32 scope global tun0\\       valid_lft forever preferred_lft forever
"""

IFCONFIG_UNIX = """\
lo0: flags=8049<UP,LOOPBACK,RUNNING,MULTICAST> mtu 16384
\tinet 127.0.0.1 netmask 0xff000000
en0: flags=8863<UP,BROADCAST,SMART,RUNNING,SIMPLEX,MULTICAST> mtu 1500
\tinet6 fe80::1c3e:aaaa:bbbb:cccc%en0 prefixlen 64 secured scopeid 0x6
\tinet 192.168.1.5 netmask 0xffffff00 broadcast 192.168.1.255
eth0: flags=4163<UP,BROADCAST,RUNNING,MULTICAST>  mtu 1500
        inet 172.16.4.9  netmask 255.255.240.0  broadcast 172.16.15.255
eth1      Link encap:Ethernet  HWaddr 00:11:22:33:44:55
          inet addr:192.168.7.2  Bcast:192.168.7.255  Mask:255.255.255.0
"""


def test_parse_ipconfig_english_all_adapters():
    assert T.parse_ipv4_interfaces(IPCONFIG_EN) == [
        ("100.101.102.103", "255.255.255.255"), ("192.168.242.190", "255.255.255.0"), ("172.26.16.1", "255.255.240.0"),
        ("192.168.56.1", "255.255.255.0"), ("169.254.7.8", "255.255.0.0")]


def test_parse_ipconfig_is_language_independent():
    assert T.parse_ipv4_interfaces(IPCONFIG_DE) == [("192.168.178.23", "255.255.255.0")]


def test_parse_ip_addr_linux():
    assert T.parse_ipv4_interfaces(IP_ADDR_LINUX) == [
        ("127.0.0.1", "255.0.0.0"), ("192.168.1.10", "255.255.255.0"), ("10.0.0.7", "255.255.0.0"),
        ("10.8.0.2", "255.255.255.255")]


def test_parse_ifconfig_macos_and_net_tools():
    assert T.parse_ipv4_interfaces(IFCONFIG_UNIX) == [
        ("127.0.0.1", "255.0.0.0"), ("192.168.1.5", "255.255.255.0"), ("172.16.4.9", "255.255.240.0"),
        ("192.168.7.2", "255.255.255.0")]


@pytest.mark.parametrize("junk", [
    "", "   ", None, "\x00\xff\xfe", "inet 999.1.1.1/24", "inet 10.0.0.1/99", "inet 10.0.0.1 netmask 0xzz",
    "IPv4 Address: 1.2.3.4.5\nSubnet Mask: 255.0.255.0", "255.255.255.0\n192.168.1.1", "Subnet Mask . . : 255.255.255.0",
    "IPv4 Address: 10.0.0.1\nSubnet Mask: 0.0.0.0", "IPv4 Address: 10.0.0.1\nSubnet Mask: 0.0.0.255"])
def test_parse_garbage_never_raises_and_finds_nothing(junk):
    assert T.parse_ipv4_interfaces(junk) == []


def test_beacon_targets_one_directed_broadcast_per_usable_interface():
    ifaces = T.parse_ipv4_interfaces(IPCONFIG_EN)
    # the /32 VPN address and the link-local one have no usable broadcast; the three real networks do
    assert T.beacon_targets(ifaces, "0.0.0.0") == [("192.168.242.190", "192.168.242.255"),
                                                   ("172.26.16.1", "172.26.31.255"), ("192.168.56.1", "192.168.56.255")]
    assert T.beacon_targets(ifaces, "192.168.242.190") == [("192.168.242.190", "192.168.242.255")]   # bound to one address
    assert T.beacon_targets(ifaces, "192.168.9.9") == []                                             # not one of ours
    assert T.beacon_targets([("127.0.0.1", "255.0.0.0"), ("0.0.0.0", "0.0.0.0"), ("1.2.3.4", "bad")], "0.0.0.0") == []


def test_beacon_targets_extra_addresses_use_the_interface_that_owns_their_subnet():
    ifaces = T.parse_ipv4_interfaces(IPCONFIG_EN)
    got = T.beacon_targets(ifaces, "0.0.0.0", ["192.168.242.77", "10.9.9.9", "not-an-ip", " 192.168.242.77 ", ""])
    assert got[3:] == [("192.168.242.190", "192.168.242.77"), (None, "10.9.9.9")]       # duplicates and junk dropped
    assert len(got) == 5


def test_with_ip_patches_only_an_ip_field_and_never_raises():
    d = json.dumps({"type": "device_discovery", "ip": "1.1.1.1", "x": 1}, separators=(",", ":")).encode()
    assert json.loads(T._with_ip(d, "192.168.0.5")) == {"type": "device_discovery", "ip": "192.168.0.5", "x": 1}
    no_ip = json.dumps({"type": "device_discovery", "device_id": "CHETNA_HAPTIC_001"}, separators=(",", ":")).encode()
    assert T._with_ip(no_ip, "192.168.0.5") == no_ip              # team discovery has no ip field: byte for byte
    assert T._with_ip(b"not json", "x") == b"not json" and T._with_ip(b"[1]", "x") == b"[1]"


class FakeSock:
    def __init__(self, fail=False):
        self.sent, self.fail = [], fail

    def sendto(self, data, addr):
        if self.fail:
            raise OSError("network is unreachable")
        self.sent.append((data, addr))

    def close(self):
        pass


def _server(host="127.0.0.1", dialect="reference", **kw):
    tw = T.Twin(("haptic", "bio"), port_offset=next(_offsets), dialect=dialect, emit=lambda ev: None)
    srv = T.TwinServer(tw, host=host, **kw)
    srv.disc_sock = FakeSock()
    return srv


def test_loopback_host_sends_exactly_the_old_two_beacons_and_never_enumerates(monkeypatch):
    monkeypatch.setattr(T, "local_ipv4_interfaces", lambda: pytest.fail("listed the interfaces on a loopback bind"))
    srv = _server("127.0.0.1", broadcast_addrs=["192.168.1.255"])
    srv._open_lan_beacons()
    assert srv.lan_beacons == []
    data = json.dumps(srv.twin.A.discovery(0), separators=(",", ":")).encode()
    srv._broadcast("haptic", data)
    port = srv.twin.ports["discovery"]
    assert srv.disc_sock.sent == [(data, ("255.255.255.255", port)), (data, ("127.0.0.1", port))]    # byte-identical


def test_lan_host_adds_one_beacon_per_interface_from_a_socket_bound_to_it(monkeypatch):
    monkeypatch.setattr(T, "local_ipv4_interfaces", lambda: T.parse_ipv4_interfaces(IPCONFIG_EN))
    srv = _server("0.0.0.0", broadcast_addrs=["192.168.242.77", "10.9.9.9"])
    bound = {}
    srv._beacon_socket = lambda src: bound.setdefault(src, FakeSock())
    srv._open_lan_beacons()
    assert [(src, dst) for _, src, dst in srv.lan_beacons] == [
        ("192.168.242.190", "192.168.242.255"), ("172.26.16.1", "172.26.31.255"), ("192.168.56.1", "192.168.56.255"),
        ("192.168.242.190", "192.168.242.77"), (None, "10.9.9.9")]
    assert sorted(bound) == ["172.26.16.1", "192.168.242.190", "192.168.56.1"]        # one socket per interface
    data = json.dumps(srv.twin.A.discovery(0), separators=(",", ":")).encode()
    srv._broadcast("haptic", data)
    port = srv.twin.ports["discovery"]
    # the two old beacons are untouched, then the extra destination that no interface owns (unbound socket, unpatched)
    assert srv.disc_sock.sent == [(data, ("255.255.255.255", port)), (data, ("127.0.0.1", port)),
                                  (data, ("10.9.9.9", port))]
    wifi = bound["192.168.242.190"].sent
    assert [a for _, a in wifi] == [("192.168.242.255", port), ("192.168.242.77", port)]
    for src, sock in bound.items():                 # every interface announces itself with its own address
        assert all(json.loads(d)["ip"] == src for d, _ in sock.sent)
    assert [a for _, a in bound["172.26.16.1"].sent] == [("172.26.31.255", port)]


def test_team_beacons_without_an_ip_field_stay_byte_identical_on_every_interface(monkeypatch):
    monkeypatch.setattr(T, "local_ipv4_interfaces", lambda: T.parse_ipv4_interfaces(IPCONFIG_DE))
    srv = _server("0.0.0.0", dialect="team")
    bound = {}
    srv._beacon_socket = lambda src: bound.setdefault(src, FakeSock())
    srv._open_lan_beacons()
    data = json.dumps(srv.twin.A.discovery(0), separators=(",", ":")).encode()
    srv._broadcast("haptic", data)
    assert bound["192.168.178.23"].sent == [(data, ("192.168.178.255", srv.twin.ports["discovery"]))]


def test_a_failing_interface_never_stops_the_others():
    srv = _server("0.0.0.0")
    bad, good = FakeSock(fail=True), FakeSock()
    srv.lan_beacons = [(bad, "10.0.0.2", "10.0.0.255"), (good, "10.1.0.2", "10.1.0.255")]
    srv._broadcast("haptic", b'{"ip":"x"}')
    assert [a for _, a in good.sent] == [("10.1.0.255", srv.twin.ports["discovery"])]


def test_unreadable_interface_list_falls_back_to_a_slash24_guess(monkeypatch, capsys):
    monkeypatch.setattr(T, "local_ipv4_interfaces", lambda: [])
    monkeypatch.setattr(T, "lan_ip", lambda: "192.168.9.40")
    srv = _server("0.0.0.0")
    srv._beacon_socket = lambda src: FakeSock()
    srv._open_lan_beacons()
    assert [(s, d) for _, s, d in srv.lan_beacons] == [("192.168.9.40", "192.168.9.255")]
    assert "assuming /24" in capsys.readouterr().err
    one = _server("192.168.9.41")                       # a single --host the listing did not show
    one._beacon_socket = lambda src: FakeSock()
    one._open_lan_beacons()
    assert [(s, d) for _, s, d in one.lan_beacons] == [("192.168.9.41", "192.168.9.255")]


def test_no_network_at_all_means_no_extra_beacons_and_no_crash(monkeypatch):
    monkeypatch.setattr(T, "local_ipv4_interfaces", lambda: [])
    monkeypatch.setattr(T, "lan_ip", lambda: "127.0.0.1")
    srv = _server("0.0.0.0")
    srv._open_lan_beacons()
    assert srv.lan_beacons == []


def test_local_ipv4_interfaces_survives_missing_tools(monkeypatch):
    def boom(*a, **k):
        raise FileNotFoundError("no such tool")
    monkeypatch.setattr(T.subprocess, "run", boom)
    assert T.local_ipv4_interfaces() == []


def test_beacon_socket_binds_the_interface_address_so_that_is_the_source():
    srv = _server("0.0.0.0")
    lst = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    lst.bind(("127.0.0.1", 0))
    lst.settimeout(2.0)
    try:
        s = srv._beacon_socket("127.0.0.2")
    except OSError:
        lst.close()
        pytest.skip("this OS does not bind 127.0.0.2")
    try:
        s.sendto(b"x", lst.getsockname())
        _, src = lst.recvfrom(16)
        assert src[0] == "127.0.0.2"
    finally:
        s.close()
        lst.close()


def test_a_beacon_socket_that_cannot_bind_is_skipped_not_fatal(monkeypatch, capsys):
    srv = _server("0.0.0.0")
    with pytest.raises(OSError):
        srv._beacon_socket("203.0.113.9")               # TEST-NET-3: not an address of this PC
    monkeypatch.setattr(T, "local_ipv4_interfaces", lambda: [("203.0.113.9", "255.255.255.0")])
    srv._open_lan_beacons()
    assert srv.lan_beacons == [] and "no beacon from 203.0.113.9" in capsys.readouterr().err


def test_control_socket_binds_loopback_whatever_the_host_is():
    # host 127.0.0.2 is not the default 127.0.0.1 but is loopback-only, so this needs no firewall exception
    tw = T.Twin(("haptic", "bio"), port_offset=next(_offsets), emit=lambda ev: None)
    srv = T.TwinServer(tw, host="127.0.0.2")
    try:
        srv.start()
    except OSError:
        srv.stop()
        pytest.skip("this OS does not bind 127.0.0.2")
    try:
        assert srv.ctl_sock.getsockname() == ("127.0.0.1", tw.ports["control"])
        assert {k: s.getsockname()[0] for k, s in srv.socks.items()} == {"haptic": "127.0.0.2", "bio": "127.0.0.2"}
        assert srv.lan_beacons == []                          # 127.x is loopback: no LAN announcements
    finally:
        srv.stop()


def test_control_port_is_not_reachable_through_the_lan_address_when_nodes_bind_all_interfaces():
    lan = T.lan_ip()
    if T.is_loopback_host(lan):
        pytest.skip("no LAN address on this PC")
    tw = T.Twin(("haptic",), port_offset=next(_offsets), emit=lambda ev: None)
    srv = T.TwinServer(tw, host="0.0.0.0")
    srv.start()
    p = Peer()
    try:
        assert srv.socks["haptic"].getsockname()[0] == "0.0.0.0"
        assert srv.ctl_sock.getsockname()[0] == "127.0.0.1"
        p.s.sendto(b"status", ("127.0.0.1", tw.ports["control"]))
        ok, _ = p.recv_until(lambda m: m.get("cmd") == "status")
        assert ok is not None, "the control port must answer on loopback"
        probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)    # unbound: a socket bound to loopback cannot reach the LAN
        probe.settimeout(0.6)
        try:
            probe.sendto(b"status", (lan, tw.ports["control"]))      # the same command through the LAN address
            reply = probe.recvfrom(4096)
        except (socket.timeout, ConnectionResetError):               # silence, or Windows' "port unreachable": not served
            reply = None
        finally:
            probe.close()
        assert reply is None
    finally:
        p.close()
        srv.stop()


def test_cli_accepts_lan_flags():
    a = T.build_parser().parse_args(["--host", "0.0.0.0", "--broadcast-addr", "10.0.0.255", "--broadcast-addr", "10.0.0.7"])
    assert a.host == "0.0.0.0" and a.broadcast_addr == ["10.0.0.255", "10.0.0.7"] and a.dialect == "reference"
    assert T.build_parser().parse_args([]).broadcast_addr == []


# --------------------------------------------------------------------------- contracts v0.2.1: keepalive, display mode, --dialect team
# Authority: contracts/HAPTIC_PROTOCOL.md v1.3 and docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md section A (their firmware's text).

DIALECTS = ["reference", "team"]
TEAM_ACK_KEYS = {"type", "device_id", "cue_id", "accepted", "timestamp_ms"}


def team(kinds=("haptic",), **kw):
    return H(kinds, dialect="team", **kw)


def accepted(h, cue_id):
    [a] = acks(h, cue_id)
    return a["accepted"]


def test_reference_dialect_is_the_default_and_keeps_its_identity():
    h = H(("haptic", "bio"))
    assert h.tw.dialect == "reference" and type(h.tw.A) is T.NodeA and type(h.tw.B) is T.NodeB
    assert h.tw.A.device_id == "SLEEVE_001" and h.tw.B.device_id == "CHETNA_BIO_001"
    assert T.build_parser().parse_args([]).dialect == "reference"
    assert T.build_parser().parse_args(["--dialect", "team"]).dialect == "team"
    with pytest.raises(ValueError):
        T.Twin(("haptic",), dialect="bogus")
    with pytest.raises(SystemExit):
        T.build_parser().parse_args(["--dialect", "bogus"])
    assert "dialect" not in h.tw.control("status")["state"]            # the reference state dict is unchanged


# ---- 1. keepalive: a known message in EVERY dialect

@pytest.mark.parametrize("dialect", DIALECTS)
def test_keepalive_is_a_known_silent_message_on_both_nodes(dialect):
    h = H(("haptic", "bio"), dialect=dialect)
    me = ("10.0.0.9", 6000)
    h.send({"type": "keepalive"}, addr=me, kind="haptic")
    h.send({"type": "keepalive"}, addr=me, kind="bio")
    h.run(100)
    assert h.tw.A.stats["rx_invalid"] == 0 and h.tw.B.stats["rx_invalid"] == 0
    assert not h.ev("unknown_type") and not h.msgs("ack")             # counted nowhere, answered by nothing
    if dialect == "team":
        assert h.tw.A.ignored == 0 and h.tw.B.ignored == 0


@pytest.mark.parametrize("dialect", DIALECTS)
def test_keepalive_feeds_the_watchdog_like_ping(dialect):
    h = H(dialect=dialect)
    me = ("10.0.0.9", 6000)
    h.send(stroke(motor=0, cue_id="k"), addr=me)
    h.tw.control("stick-a 0")                      # fault: driver latched on after its pulse
    for _ in range(9):                             # 4.5 s with nothing but a keepalive every 500 ms
        h.run(500)
        h.send({"type": "keepalive"}, addr=me)
    assert not h.ev("watchdog") and h.tw.A.motors[0]["active"]
    h.run(2100)                                    # the keepalives stop: the watchdog forces the motor off at 2 s
    wd = h.ev("watchdog")
    assert len(wd) == 1 and wd[0]["motors_forced_off"] == [0]


def test_keepalive_refreshes_a_subscriber_like_ping_but_does_not_subscribe():
    h = H()
    sub, other = ("10.0.0.1", 1111), ("10.0.0.2", 2222)
    h.send({"type": "subscribe"}, addr=sub)
    for _ in range(12):                            # 6 s: longer than the 5 s expiry
        h.run(500)
        h.send({"type": "keepalive"}, addr=sub)
        h.send({"type": "keepalive"}, addr=other)
    assert h.tw.A.live_subs(h.clk.t) == [sub]     # refreshed; the non-subscriber was not added (spec D3)
    assert not h.ev("sub_expired")


# ---- 2. display: `mode` as well as `text`, same 12-char rule, in EVERY dialect

@pytest.mark.parametrize("dialect", DIALECTS)
def test_display_accepts_mode_text_or_both(dialect):
    h = H(dialect=dialect)
    h.send({"type": "display", "mode": "SYNC"})                          # the team firmware's field alone
    assert h.tw.A.display_text == "SYNC"
    h.run(600)
    h.send({"type": "display", "text": "ASYNC", "mode": "ASYNC"})        # what the game sends: both, same value
    assert h.tw.A.display_text == "ASYNC"
    h.run(1100)
    h.send({"type": "display", "mode": "1234567890123"})                 # 13 chars: the same rule applies to mode
    h.send({"type": "display", "text": "OK", "mode": "1234567890123"})   # one bad field rejects the whole message
    h.send({"type": "display"})                                          # neither field
    assert h.tw.A.display_text == "ASYNC" and len(h.ev("display_rejected")) == 3
    h.run(1100)
    h.send({"type": "display", "text": "SYNC", "mode": "ASYNC"})         # both, different: the dialect's own field wins
    own = "SYNC" if dialect == "reference" else "ASYNC"
    assert h.tw.A.display_text == own
    assert [e["text"] for e in h.events if e["event"] == "oled"] == ["SYNC", "ASYNC", own]


@pytest.mark.parametrize("dialect", DIALECTS)
def test_display_keeps_its_rate_limit_with_mode(dialect):
    h = H(dialect=dialect)
    for word in ("A", "B", "C"):
        h.send({"type": "display", "mode": word})
    assert h.tw.A.display_text == "B" and len(h.ev("display_rejected")) == 1      # 2 per second


# ---- 3. --dialect team

def test_team_ids_and_discovery_are_exactly_their_form():
    h = team(("haptic", "bio"))
    h.run(10)
    a = [m for k, m in h.bcast if k == "haptic"][0]
    b = [m for k, m in h.bcast if k == "bio"][0]
    keys = {"type", "device_id", "device_kind", "firmware_version", "command_port", "status", "motor_count",
            "timestamp_ms"}
    assert set(a) == keys and set(b) == keys                           # no ip, no legacy opus_haptic / port / fw
    assert (a["device_id"], a["device_kind"], a["motor_count"], a["command_port"]) == ("CHETNA_HAPTIC_001", "haptic", 2, 8790)
    assert (b["device_id"], b["device_kind"], b["motor_count"], b["command_port"]) == ("CHETNA_BIO_001", "bio", 0, 8792)
    assert a["type"] == "device_discovery" and a["status"] == "available" and a["firmware_version"] == "0.5.0"


def test_team_ack_is_exactly_their_form_and_play_at_ms_is_ignored():
    h = team()
    me = ("10.0.0.9", 6000)
    h.send(stroke(motor=0, cue_id="ok1", play_at_ms=10 ** 12), addr=me)     # meant for the far future: plays on arrival
    h.run(30)
    [a] = acks(h, "ok1")
    assert set(a) == TEAM_ACK_KEYS and a["device_id"] == "CHETNA_HAPTIC_001" and a["accepted"] is True
    assert isinstance(a["timestamp_ms"], int)
    assert [x for k, m, x, _ in h.sent if m.get("cue_id") == "ok1"] == [me]       # to the sender
    r = h.strokes()[0]
    assert r["status"] == "executed" and r["motor_on_ms"] - r["recv_ts_ms"] == 30       # the twin's own log still says what happened
    assert h.tw.A.motors[0]["active"]


def test_team_motor_still_running_is_accepted_false():
    h = team()
    h.send(stroke(motor=0, dur=400, cue_id="b1"))
    h.run(150)                                       # past the 100 ms gap, the 400 ms pulse is still running
    h.send(stroke(motor=0, dur=200, cue_id="b2"))
    h.run(30)
    assert accepted(h, "b1") is True and accepted(h, "b2") is False
    assert h.strokes()[-1]["error_code"] == "MOTOR_BUSY"
    assert h.strokes()[0]["status"] == "executed" and h.tw.A.motors[0]["active"]         # b1 was not disturbed
    assert h.tw.A.stroke_count == 1                  # the reference dialect would have re-triggered the pulse


def test_team_gap_under_100ms_is_accepted_false():
    h = team()
    h.send(stroke(motor=0, dur=50, cue_id="g1"))
    h.run(60)                                        # pulse over (50 ms) but only 60 ms since its start
    h.send(stroke(motor=0, dur=50, cue_id="g2"))
    h.run(40)
    h.send(stroke(motor=0, dur=50, cue_id="g3"))     # exactly 100 ms after g1
    h.run(30)
    assert [accepted(h, c) for c in ("g1", "g2", "g3")] == [True, False, True]
    assert h.strokes()[1]["error_code"] == "CUE_GAP"


def test_team_duty_budget_used_up_is_accepted_false():
    h = team()
    for i in range(16):
        h.send(stroke(motor=0, dur=400, cue_id=f"d{i}"))
        h.run(500)
    flags = [accepted(h, f"d{i}") for i in range(16)]
    first = flags.index(False)
    assert 10 <= first <= 14 and not any(flags[first:first + 3])
    assert [r["error_code"] for r in h.strokes() if r["status"] == "rejected"][0] == "DUTY_CYCLE_LIMIT"


def test_team_invalid_motor_or_intensity_is_accepted_false_and_nothing_vibrates():
    h = team()
    bad = [stroke(motor=2, cue_id="m2"), stroke(motor=3, cue_id="m3"), stroke(motor=4, cue_id="m4"),
           stroke(motor=-1, cue_id="mneg"), stroke(motor="0", cue_id="mstr"),
           stroke(intensity=256, cue_id="i256"), stroke(intensity=-1, cue_id="ineg"),
           stroke(intensity="high", cue_id="istr"), {"cue_id": "imiss", "motor": 0, "duration_ms": 200, "pattern": "pulse"}]
    for m in bad:
        h.send(m)
    h.send(b'{"cue_id":"nan1","motor":0,"intensity":NaN,"duration_ms":200}')
    h.send(b'{"cue_id":"inf1","motor":0,"intensity":Infinity,"duration_ms":200}')
    h.run(40)
    ids = [m["cue_id"] for m in bad] + ["nan1", "inf1"]
    assert [accepted(h, c) for c in ids] == [False] * len(ids)
    for a in h.msgs("ack"):
        assert set(a) == TEAM_ACK_KEYS               # nothing else: no status, no ack_id, no ok, no error_code
    assert not any(m["active"] for m in h.tw.A.motors) and h.tw.A.stroke_count == 0
    h.send(stroke(motor=0, cue_id="alive"))          # and the node is still serving (NaN used to kill its receive thread)
    h.run(30)
    assert accepted(h, "alive") is True


def test_team_intensity_above_150_is_capped_not_rejected_and_duration_is_clamped():
    h = team()
    h.send(stroke(motor=0, intensity=255, dur=1000, cue_id="cap"))
    h.run(30)
    assert accepted(h, "cap") is True
    r = h.strokes()[0]
    assert r["intensity_applied"] == 150 and r["duration_applied"] == 400


def test_team_stop_stops_everything_extra_fields_ignored():
    h = team()
    h.send(stroke(motor=0, dur=400, cue_id="s0"))
    h.send(stroke(motor=1, dur=400, cue_id="s1"))
    h.run(20)
    assert all(m["active"] for m in h.tw.A.motors[:2])
    h.send({"type": "stop", "v": 1, "id": "x", "ts_ms": 5})           # the game still sends the v1 form
    assert not any(m["active"] for m in h.tw.A.motors) and h.tw.A.ignored == 0


def test_team_telemetry_goes_only_to_the_last_sender():
    h = team(("haptic", "bio"))
    p1, p2, p3 = ("10.0.0.1", 1111), ("10.0.0.2", 2222), ("10.0.0.3", 3333)
    for p in (p1, p2):
        h.send({"type": "keepalive"}, addr=p, kind="haptic")
        h.send({"type": "keepalive"}, addr=p, kind="bio")
    h.run(500)
    for kind, typ in (("haptic", "sensor_data"), ("bio", "sensor_chunk")):
        assert not h.msgs(typ, kind=kind, addr=p1) and h.msgs(typ, kind=kind, addr=p2)   # p1 was replaced, not added
    assert h.tw.A.stats["sub_rejected"] == 0 and len(h.tw.A.live_subs(h.clk.t)) == 1
    h.send(stroke(cue_id="c"), addr=p3)               # any packet counts, a command too: p3 takes over node A
    mark = len(h.sent)
    h.run(500)
    after = h.sent[mark:]
    assert any(a == p3 and m["type"] == "sensor_data" for k, m, a, _ in after)
    assert not any(a == p2 and m["type"] == "sensor_data" for k, m, a, _ in after)
    assert any(a == p2 and m["type"] == "sensor_chunk" for k, m, a, _ in after)       # node B still streams to p2
    h.run(5200)                                       # nothing heard for > 5 s: the target expires (the twin's assumption)
    assert h.ev("sub_expired") and h.tw.A.live_subs(h.clk.t) == []


# ---- 3b. --telemetry-port: the real boards send telemetry to the last sender's IP on a FIXED port, acks to its source port

def test_team_telemetry_port_sends_telemetry_to_that_port_and_acks_to_the_source_port():
    h = team(("haptic", "bio"), telemetry_port=8790)
    p1, p2 = ("10.0.0.1", 1111), ("10.0.0.2", 2222)
    for p in (p1, p2):                                # p2 speaks last, to both nodes
        h.send({"type": "keepalive"}, addr=p, kind="haptic")
        h.send({"type": "keepalive"}, addr=p, kind="bio")
    h.send(stroke(cue_id="t1"), addr=p2)
    h.run(500)
    fixed2 = ("10.0.0.2", 8790)                        # the last sender's IP, the fixed port: not 2222
    for kind, typ in (("haptic", "sensor_data"), ("bio", "sensor_chunk")):
        assert h.msgs(typ, kind=kind, addr=fixed2)
        assert {a for k, m, a, _ in h.sent if m["type"] == typ} == {fixed2}       # to nobody else, not to a source address
    assert [a for k, m, a, _ in h.sent if m.get("cue_id") == "t1"] == [p2]        # the ack still goes to the source address
    assert accepted(h, "t1") is True
    h.send({"type": "keepalive"}, addr=p1, kind="haptic")                         # any packet switches the target: another IP
    mark = len(h.sent)
    h.run(500)
    after = [(a, m["type"]) for k, m, a, _ in h.sent[mark:]]
    assert ("10.0.0.1", 8790) in [a for a, t in after if t == "sensor_data"]
    assert fixed2 not in [a for a, t in after if t == "sensor_data"]
    assert fixed2 in [a for a, t in after if t == "sensor_chunk"]                  # node B still streams to p2


def test_telemetry_port_is_ignored_by_the_reference_dialect_and_checked_as_a_port():
    h = H(("haptic", "bio"), telemetry_port=8790)
    me = ("10.0.0.1", 5000)
    h.send({"type": "subscribe"}, addr=me, kind="haptic")
    h.send({"type": "subscribe"}, addr=me, kind="bio")
    h.run(500)
    assert h.msgs("sensor_data", addr=me) and h.msgs("sensor_chunk", addr=me)     # still the subscriber's source address
    assert not [1 for k, m, a, _ in h.sent if a == ("10.0.0.1", 8790)]
    assert T.build_parser().parse_args([]).telemetry_port == 0
    assert T.build_parser().parse_args(["--telemetry-port", "8794"]).telemetry_port == 8794
    for bad in (-1, 65536):                            # sendto() would raise OverflowError in the tick thread
        with pytest.raises(ValueError):
            T.Twin(("haptic",), dialect="team", telemetry_port=bad)


def test_node_b_chunk_stamp_is_the_first_value_in_the_reference_dialect_and_the_last_in_the_team_dialect():
    stamps = {}
    for dialect in DIALECTS:
        h = H(("bio",), dialect=dialect)
        h.send({"type": "keepalive" if dialect == "team" else "subscribe"}, addr=("10.0.0.1", 5000), kind="bio")
        h.run(500)
        stamps[dialect] = [m["timestamp_ms"] for m in h.msgs("sensor_chunk")[:3]]
    # a value is stored every 10 ms from device time 10. Reference chunks hold 10 values: 1-10, 11-20 ... -> the first is stamped
    assert stamps["reference"] == [10, 110, 210]
    # team chunks hold 4: values 1-4 (stored at 10..40), 5-8 ... -> millis() of the 4th, about 30 ms after the first
    assert stamps["team"] == [40, 80, 120]


def test_team_nodes_send_no_status_and_no_emg_burst():
    h = team(("haptic", "bio"))
    me = ("10.0.0.1", 1111)
    h.send({"type": "keepalive"}, addr=me, kind="haptic")
    h.send({"type": "keepalive"}, addr=me, kind="bio")
    h.run(1200)
    h.tw.control("flinch")
    h.run(1500)
    h.send({"type": "status_request"}, addr=me, kind="haptic")
    h.run(50)
    assert not h.msgs("status") and not h.msgs("emg_burst")
    assert h.msgs("sensor_data") and h.msgs("sensor_chunk")
    assert h.tw.B.burst_events >= 1 and h.ev("emg_burst")        # the burst happened; it is only not announced
    assert h.tw.A.ignored == 1                                   # status_request: not in their firmware


def test_team_node_b_sends_4_value_chunks_25_per_second():
    h = team(("bio",))
    h.send({"type": "keepalive"}, addr=("10.0.0.1", 1111), kind="bio")
    h.run(2000)
    ch = h.msgs("sensor_chunk")
    assert 49 <= len(ch) <= 51
    for m in ch:
        assert set(m) == {"type", "device_id", "device_kind", "timestamp_ms", "sample_rate_hz", "emg_envelope", "unit", "status"}
        assert m["device_id"] == "CHETNA_BIO_001" and m["device_kind"] == "bio" and m["sample_rate_hz"] == 100
        assert len(m["emg_envelope"]) == 4 and m["unit"] == "raw_adc" and m["status"] == "ok"
    ts = [m["timestamp_ms"] for m in ch]
    assert set(b - a for a, b in zip(ts, ts[1:])) <= {39, 40, 41}                  # 40 ms apart (each chunk is stamped at its last value)
    env = [v for m in ch for v in m["emg_envelope"]]
    assert abs(statistics.mean(env) - T.EMG_BASELINE) < 3.0


def test_team_node_a_sensor_data_is_accel_only_at_100hz():
    h = team()
    h.send({"type": "keepalive"}, addr=("10.0.0.1", 1111))
    h.run(2000)
    sd = h.msgs("sensor_data")
    assert 195 <= len(sd) <= 201
    for m in sd:
        assert set(m) == {"type", "device_id", "timestamp_ms", "sensors"} and m["device_id"] == "CHETNA_HAPTIC_001"
        assert set(m["sensors"]) == {"imu_accel_x", "imu_accel_y", "imu_accel_z"}
        assert all(set(v) == {"value", "unit", "status"} and v["unit"] == "m/s2" and v["status"] == "ok"
                   for v in m["sensors"].values())


def test_team_ignores_what_their_firmware_does_not_know():
    h = team()
    me = ("10.0.0.1", 1111)
    for m in ({"type": "ping", "id": "p"}, {"type": "subscribe"}, {"type": "mystery"}, {"type": "config", "enabled": False},
              {"v": 1, "type": "cue", "id": "c", "cue": "x", "intensity": 0.6, "duration_ms": 300, "pattern": "pulse"},
              dict(stroke(cue_id="env"), type="haptic")):
        h.send(m, addr=me)
    h.send(b"{not json", addr=me)                     # garbage: no ack either
    h.send(b"[1, 2]", addr=me)
    h.run(100)
    assert not h.msgs("ack") and not h.strokes() and not any(m["active"] for m in h.tw.A.motors)
    assert h.tw.A.ignored == 6 and h.tw.A.stats["rx_invalid"] == 2
    st = h.tw.control("status")["state"]
    assert st["dialect"] == "team" and st["haptic"]["ignored"] == 6
    # ... while the same node still plays a plain cue
    h.send(stroke(cue_id="plain"), addr=me)
    h.run(30)
    assert accepted(h, "plain") is True


def test_team_messages_validate_against_the_contract_schema():
    jsonschema = pytest.importorskip("jsonschema")
    schema = json.loads((REPO / "contracts" / "schemas" / "haptic-message.schema.json").read_text(encoding="utf-8"))
    valid = jsonschema.Draft202012Validator(schema).is_valid
    for m in ({"type": "keepalive"}, {"type": "display", "mode": "SYNC"}, {"type": "display", "text": "SYNC", "mode": "SYNC"}):
        assert valid(m), m
    h = team(("haptic", "bio"))
    me = ("10.0.0.1", 1111)
    h.send({"type": "keepalive"}, addr=me, kind="haptic")
    h.send({"type": "keepalive"}, addr=me, kind="bio")
    h.send({"type": "display", "mode": "SYNC"}, addr=me)
    h.send(stroke(motor=0, dur=400, cue_id="v1"), addr=me)
    h.run(150)
    h.send(stroke(motor=0, dur=200, cue_id="v2"), addr=me)               # rejected: busy
    h.tw.control("flinch")
    h.run(1500)
    out = [m for _, m in h.bcast] + [m for _, m, _, _ in h.sent]
    assert {"device_discovery", "ack", "sensor_data", "sensor_chunk"} <= {m["type"] for m in out}
    for m in out:
        assert valid(m), m


def test_cli_team_dialect_subprocess_speaks_their_form(tmp_path):
    off = next(_offsets)
    proc = subprocess.Popen(
        [sys.executable, "-m", "sim.sleeve.twin", "--kind", "both", "--dialect", "team", "--port-offset", str(off),
         "--no-stdin", "--duration", "10"], cwd=str(REPO), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    pa, pb = Peer(), Peer()
    try:
        ready = json.loads(proc.stdout.readline())
        assert ready["event"] == "ready" and ready["dialect"] == "team" and "lan" not in ready
        pb.send({"type": "keepalive"}, ready["ports"]["bio"])
        c, _ = pb.recv_until(lambda m: m.get("type") == "sensor_chunk")
        assert c is not None and len(c["emg_envelope"]) == 4 and c["device_id"] == "CHETNA_BIO_001"
        pa.send(stroke(cue_id="t1"), ready["ports"]["haptic"])
        a, _ = pa.recv_until(lambda m: m.get("type") == "ack")
        assert a is not None and set(a) == TEAM_ACK_KEYS and a["cue_id"] == "t1" and a["accepted"] is True
        assert a["device_id"] == "CHETNA_HAPTIC_001"
        pb.s.sendto(b"quit", ("127.0.0.1", ready["ports"]["control"]))
        out, _ = proc.communicate(timeout=10)
        assert '"event": "exit"' in out
    finally:
        pa.close()
        pb.close()
        if proc.poll() is None:
            proc.kill()


def test_udp_team_telemetry_arrives_on_the_fixed_port_and_the_ack_on_the_command_socket(server):
    listener, cmd = Peer(), Peer()                    # the receiver's fixed telemetry port, and a different socket the commands leave from
    port = listener.s.getsockname()[1]
    srv = server(("haptic", "bio"), dialect="team", telemetry_port=port)
    try:
        cmd.send({"type": "keepalive"}, srv.twin.ports["bio"])
        cmd.send(stroke(cue_id="u1"), srv.twin.ports["haptic"])
        ack, got = cmd.recv_until(lambda m: m.get("type") == "ack")
        assert ack is not None and ack["cue_id"] == "u1" and ack["accepted"] is True
        data, _ = listener.recv_until(lambda m: m.get("type") == "sensor_data")
        chunk, _ = listener.recv_until(lambda m: m.get("type") == "sensor_chunk")
        assert data is not None and chunk is not None
        _, rest = cmd.recv_until(lambda m: False, timeout=0.3)                   # a bounded look, not a readiness wait
        assert not [m for m in got + rest if m.get("type") in ("sensor_data", "sensor_chunk")]
    finally:
        listener.close()
        cmd.close()
