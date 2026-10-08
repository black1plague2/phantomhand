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
