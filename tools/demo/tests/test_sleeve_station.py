"""Tests for tools/demo/sleeve_station.py ("brush your own arm": the sleeve demo with no headset).

    H:\\Chenta\\phantomhand\\sim\\live\\.venv\\Scripts\\python.exe -m pytest tools/demo/tests/test_sleeve_station.py -q -p no:cacheprovider

Order: pure stroke logic and its five hard limits (no sockets, no clock), the ack watchdog, the wire against a fake socket, then the
integration tests against the REAL sleeve twin (sim/sleeve/twin.py) run as a subprocess for Node A only, on throwaway ports:
--port-offset 15200-15290 only (Node A = 8790 + offset, beacon = 8791 + offset, control = 8793 + offset). Never the default ports,
8787, or the offsets other tests use (12100, 14010, 13000-13040, 31000). The twin is always terminated in a `finally`.
"""
from __future__ import annotations

import json
import queue
import random
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple

import pytest

from conftest import REPO_ROOT

import live_plot as LP  # noqa: E402
import sleeve_station as SS  # noqa: E402

STATION = REPO_ROOT / "tools" / "demo" / "sleeve_station.py"
A_ADDR = ("10.1.2.3", 8790)


# ------------------------------------------------------------------ helpers
def walk(eng: SS.StrokeEngine, frm: float, to: float, t0: float, speed: float = 12.0, dt: float = 10.0) -> Tuple[List[SS.Cue], float]:
    """Move the brush from `frm` to `to` cm at `speed` cm/s, one sample every `dt` ms. Returns (taps sent, end time)."""
    n = max(1, int(abs(to - frm) / speed * 1000.0 / dt))
    cues: List[SS.Cue] = []
    for k in range(n + 1):
        cues += eng.move(frm + (to - frm) * k / n, t0 + k * dt)
    return cues, t0 + n * dt


def tap(move: Callable[..., Any], motor: int, t: float) -> Any:
    """Lift, touch down 3 cm before the band and jump 3 cm past it at time t: one crossing, so a tap for `motor` is attempted at t."""
    c = SS.BANDS_CM[motor]
    move(None, t - 1.0)
    move(c - 3.0, t - 1.0)
    return move(c + 3.0, t)


def assert_limits(sent: List[SS.Cue]) -> None:
    """Every software limit, checked on what actually went out."""
    by_motor: Dict[int, List[float]] = {0: [], 1: []}
    for c in sent:
        by_motor[c.motor].append(c.t_ms)
    for m, v in by_motor.items():
        assert all(b - a >= 250.0 for a, b in zip(v, v[1:])), f"motor {m}: two taps closer than 250 ms"
        for a in v:                                   # the worst 10 s windows start at a pulse start or end at a pulse end
            for lo in (a, a + 200.0 - 10000.0):
                on = sum(max(0.0, min(s + 200.0, lo + 10000.0) - max(s, lo)) for s in v)
                assert on <= 5000.0 + 1e-6, f"motor {m}: {on} ms on in the 10 s from {lo}"
    ts = sorted(c.t_ms for c in sent)
    for a in ts:
        assert sum(1 for b in ts if a <= b <= a + 1000.0) <= 4, f"more than 4 taps in the second from {a}"
    assert all(c.intensity <= 150 and c.duration_ms == 200 for c in sent)


class FakeSock:
    def __init__(self) -> None:
        self.out: List[Tuple[bytes, Any]] = []
        self.inbox: List[Any] = []
        self.closed = False

    def sendto(self, data: bytes, addr: Any) -> int:
        self.out.append((data, addr))
        return len(data)

    def recvfrom(self, n: int) -> Tuple[bytes, Any]:
        if not self.inbox:
            raise BlockingIOError()
        item = self.inbox.pop(0)
        if isinstance(item, Exception):
            raise item
        return item, A_ADDR

    def close(self) -> None:
        self.closed = True


class FakeSleeve:
    def __init__(self) -> None:
        self.out: List[Dict[str, Any]] = []
        self.acks: List[Tuple[str, bool, float]] = []
        self.closed = False

    def send(self, msg: Dict[str, Any]) -> bool:
        self.out.append(msg)
        return True

    def keepalive(self) -> None:
        self.out.append({"type": "keepalive"})

    def stop(self) -> None:
        self.out.append({"type": "stop"})

    def recv_acks(self) -> List[Tuple[str, bool, float]]:
        a, self.acks = self.acks, []
        return a

    def close(self) -> None:
        self.closed = True


# ------------------------------------------------------------------ band crossing
def test_a_band_fires_when_the_brush_passes_it_in_either_direction():
    eng = SS.StrokeEngine()
    up, t = walk(eng, 0.0, 25.0, 0.0)                                  # wrist -> elbow at 12 cm/s
    assert [c.motor for c in up] == [0, 1]
    assert up[0].t_ms == pytest.approx(354, abs=15) and up[1].t_ms - up[0].t_ms == pytest.approx(833, abs=15)   # 10 cm at 12 cm/s
    eng.move(None, t + 1.0)
    down, _ = walk(eng, 25.0, 0.0, t + 1000.0)                         # elbow -> wrist
    assert [c.motor for c in down] == [1, 0]
    assert all(c.t_ms == c.due_ms for c in up + down)                  # in sync: sent when the brush passes


def test_hovering_or_jittering_on_a_band_fires_once_and_it_re_arms_when_the_brush_leaves():
    eng = SS.StrokeEngine()
    cues = list(eng.move(0.0, 0.0))
    for k in range(1, 400):                                            # 4 s of 100 Hz jitter, +-1.2 cm around band A (edge zone included)
        cues += eng.move(5.0 + 1.2 * (-1) ** k, 10.0 * k)
    assert [c.motor for c in cues] == [0]
    eng.move(7.0, 4100.0)                                              # 2 cm from the centre: re-armed
    assert [c.motor for c in eng.move(5.0, 4200.0)] == [0]             # coming back is a new pass


def test_a_fast_jump_over_both_bands_fires_each_once_in_path_order():
    up = SS.StrokeEngine()
    up.move(0.0, 0.0)
    assert [c.motor for c in up.move(24.0, 20.0)] == [0, 1]            # 24 cm between two samples
    down = SS.StrokeEngine()
    down.move(24.0, 0.0)
    assert [c.motor for c in down.move(0.0, 20.0)] == [1, 0]


def test_a_lifted_brush_forgets_its_path_and_touching_down_on_a_band_taps_it():
    eng = SS.StrokeEngine()
    assert [c.motor for c in eng.move(5.0, 0.0)] == [0]                # touch-down on band A
    assert eng.move(5.0, 50.0) == []                                   # staying there is hovering
    eng.move(None, 100.0)
    assert eng.prev is None
    assert [c.motor for c in eng.move(5.0, 400.0)] == [0]              # a new touch, a new tap
    eng.move(None, 800.0)
    assert eng.move(20.0, 900.0) == []                                 # landing beyond the bands passes nothing


# ------------------------------------------------------------------ delayed mode
def test_delayed_mode_holds_a_tap_for_600_ms_and_nothing_goes_out_early():
    eng = SS.StrokeEngine(delayed=True)
    assert eng.move(5.0, 1000.0) == []
    assert eng.poll(1599.0) == []
    (c,) = eng.poll(1600.0)
    assert (c.motor, c.due_ms, c.t_ms) == (0, 1600.0, 1600.0)
    eng.move(None, 1700.0)
    assert eng.move(15.0, 2000.0) == []
    (late,) = eng.poll(2612.0)                                         # polled 12 ms late: still sent, and the record says so
    assert (late.motor, late.due_ms, late.t_ms) == (1, 2600.0, 2612.0)


def test_a_delayed_stroke_is_the_same_stroke_600_ms_later():
    sync, delayed = SS.StrokeEngine(), SS.StrokeEngine(delayed=True)
    a: List[SS.Cue] = []
    b: List[SS.Cue] = []
    for k in range(260):
        pos = min(25.0, 0.12 * k)
        a += sync.move(pos, 10.0 * k)
        b += delayed.move(pos, 10.0 * k)
    assert [(c.motor, c.t_ms + 600.0) for c in a] == [(c.motor, c.t_ms) for c in b] and len(a) == 2


def test_switching_condition_cancels_taps_still_waiting_so_they_never_land_in_the_other_one():
    eng = SS.StrokeEngine(delayed=True)
    eng.move(5.0, 0.0)
    eng.set_delayed(False)
    assert eng.pending == [] and eng.poll(10000.0) == []


def test_the_built_in_script_is_six_slow_strokes_wrist_to_elbow_three_in_sync_then_three_delayed():
    s = list(SS.script_samples())
    lifts = [i for i, (_, p, _) in enumerate(s) if p is None]
    assert len(lifts) == 6
    strokes = [s[(lifts[i - 1] + 1 if i else 0):lifts[i]] for i in range(6)]
    for i, st in enumerate(strokes):
        pos = [p for _, p, _ in st]
        assert all(d is (i >= 3) for _, _, d in st) and pos == sorted(pos) and pos[0] == 0.0 and 24.8 <= pos[-1] <= 25.0
        assert (pos[50] - pos[0]) / ((st[50][0] - st[0][0]) / 1000.0) == pytest.approx(12.0)      # cm/s
        assert i == 0 or st[0][0] - strokes[i - 1][-1][0] > 600.0                                  # the brush is lifted between strokes


# ------------------------------------------------------------------ the five limits
def test_two_taps_to_one_motor_need_250_ms_between_them():
    eng = SS.StrokeEngine()
    assert len(tap(eng.move, 0, 1000.0)) == 1
    assert tap(eng.move, 0, 1249.0) == [] and eng.dropped["gap"] == 1          # 249 ms: dropped
    assert len(tap(eng.move, 0, 1250.0)) == 1                                  # exactly 250 ms after the one that went out
    assert len(tap(eng.move, 1, 1251.0)) == 1                                  # the gap is per motor


def test_at_most_four_taps_in_any_second_across_both_motors():
    eng = SS.StrokeEngine()
    sent: List[SS.Cue] = []
    for i, t in enumerate((0.0, 250.0, 500.0, 750.0, 1000.0, 1001.0)):
        sent += tap(eng.move, i % 2, t)
    # 1000 ms still sits in the closed second that starts at 0, so it is dropped; 1001 ms is outside it
    assert [c.t_ms for c in sent] == [0.0, 250.0, 500.0, 750.0, 1001.0]
    assert eng.dropped == {"gap": 0, "rate": 1, "duty": 0}


def test_a_motor_never_runs_more_than_half_of_any_ten_seconds():
    eng = SS.StrokeEngine()
    sent: List[SS.Cue] = []
    t = 0.0
    while t < 40000.0:
        sent += tap(eng.move, 0, t)
        t += 260.0                                                     # fast enough to hit the duty rule, slow enough to miss the other two
    assert eng.dropped["duty"] > 0 and eng.dropped["gap"] == 0 and eng.dropped["rate"] == 0
    assert len([c for c in sent if c.t_ms < 10000.0]) == 25            # 25 x 200 ms = exactly 5 s of the first 10 s
    assert_limits(sent)


def test_intensity_is_capped_at_150_and_a_tap_is_a_200_ms_pulse():
    assert SS.StrokeEngine(intensity=255).intensity == 150
    assert SS.StrokeEngine(intensity=90).intensity == 90
    assert SS.StrokeEngine(intensity=-5).intensity == 0
    (c,) = tap(SS.StrokeEngine(intensity=255).move, 0, 100.0)
    w = c.wire("x")
    assert (c.intensity, c.duration_ms, w["intensity"], w["duration_ms"], w["pattern"]) == (150, 200, 150, 200, "pulse")


def test_dropped_taps_are_counted_by_reason_and_never_queued():
    eng = SS.StrokeEngine()
    for t in (1000.0, 1100.0, 1200.0):
        tap(eng.move, 0, t)
    assert eng.dropped == {"gap": 2, "rate": 0, "duty": 0}
    assert eng.pending == [] and eng.poll(9000.0) == []                # nothing waits to be sent later
    assert SS.Station(FakeSleeve(), eng).dropped == 2


def test_random_frantic_brushing_cannot_break_a_limit():
    rng = random.Random(11)
    eng, sent, t = SS.StrokeEngine(), [], 0.0
    for _ in range(12000):                                             # ~2 min of mouse work: jumps, lifts, condition flips
        t += rng.choice((2.0, 5.0, 10.0, 10.0, 20.0))
        if rng.random() < 0.01:
            eng.set_delayed(not eng.delayed)
        sent += eng.move(None if rng.random() < 0.05 else rng.uniform(-3.0, 28.0), t)
    sent += eng.poll(t + 1000.0)
    assert len(sent) > 50 and sum(eng.dropped.values()) > 50           # the limiter really was in the way
    assert_limits(sent)


# ------------------------------------------------------------------ acks and the offline gate
def test_the_clock_resolves_well_below_a_millisecond():
    """Ack latencies are a few ms: time.monotonic() steps 15.6 ms on Windows before Python 3.13 and would hide them."""
    seen = sorted({SS.now_ms() for _ in range(50000)})
    assert len(seen) > 100 and min(b - a for a, b in zip(seen, seen[1:])) < 1.0


@pytest.mark.parametrize("ack,played", [
    ({"ok": True, "accepted": False, "status": "rejected"}, True),     # ok wins over everything
    ({"ok": False, "accepted": True, "status": "accepted"}, False),
    ({"accepted": True, "status": "rejected"}, True),                  # then accepted
    ({"accepted": False, "status": "executed"}, False),
    ({"status": "executed"}, True), ({"status": "accepted"}, True),    # then status
    ({"status": "rejected"}, False), ({"status": "error"}, False),
    ({}, True),                                                        # a bare receipt, as HapticClient treats it
])
def test_ack_precedence_ok_then_accepted_then_status(ack, played):
    assert SS.ack_ok(ack) is played


def test_link_goes_offline_after_2_s_without_an_ack_lets_one_trial_through_and_recovers_on_an_ack():
    lk = SS.Link()
    assert not lk.offline(1e9)                                         # no tap, no alarm
    assert lk.allow(0.0)
    lk.sent(0.0)
    assert not lk.offline(1999.0) and lk.offline(2000.0)
    assert lk.allow(2000.0)                                            # the trial
    lk.sent(2000.0)
    assert not lk.allow(2001.0) and not lk.allow(3999.0)
    assert lk.allow(4000.0)                                            # the next trial, 2 s later
    lk.acked()
    assert not lk.offline(4001.0) and lk.allow(4001.0) and lk.allow(4002.0)


def test_station_holds_taps_while_offline_and_resumes_on_an_ack():
    sl = FakeSleeve()
    st = SS.Station(sl)
    tap(st.pointer, 0, 1000.0)                                         # sent, never acked
    tap(st.pointer, 1, 2500.0)                                         # 1.5 s after the first: still online, sent
    assert st.sent == 2 and not st.link.offline(2999.0) and st.link.offline(3000.0)
    tap(st.pointer, 0, 3100.0)                                         # offline: this one is the trial
    assert st.sent == 3 and st.held == 0
    tap(st.pointer, 1, 3600.0)                                         # the trial was 0.5 s ago: held, not sent
    assert st.sent == 3 and st.held == 1
    sl.acks = [("st_0003", True, 3700.0)]
    st.tick(3700.0)
    assert not st.link.offline(3701.0) and st.acked == 1
    tap(st.pointer, 0, 4000.0)
    assert st.sent == 4 and st.held == 1


def test_station_counts_sent_acked_refused_and_the_ack_latency():
    sl = FakeSleeve()
    st = SS.Station(sl)
    st.pointer(None, 0.0)
    st.pointer(2.0, 1.0)
    st.pointer(24.0, 2.0)                                              # one stroke: A then B at t = 2
    assert st.sent == 2 and st.flash == {0: 2.0, 1: 2.0}
    sl.acks = [("st_0001", True, 12.0), ("st_0002", False, 14.0), ("zzz", True, 15.0)]
    st.tick(20.0)
    assert (st.acked, st.refused, st.latency_ms, st.last_ack_ms) == (1, 1, [10.0], 15.0)    # an unknown id is life, not a count
    s = st.summary()
    assert (s["cues_sent"], s["acks_accepted"], s["acks_refused"], s["acks_missing"], s["mean_ack_latency_ms"]) == (2, 1, 1, 0, 10.0)


def test_keepalive_goes_out_once_a_second():
    sl = FakeSleeve()
    st = SS.Station(sl)
    for t in (0.0, 400.0, 999.0, 1000.0, 1500.0, 2000.0):
        st.tick(t)
    assert sl.out == [{"type": "keepalive"}] * 3                       # at 0, 1000 and 2000 ms


# ------------------------------------------------------------------ the wire
def test_only_node_a_gets_only_three_message_forms_and_stop_is_the_last():
    sock = FakeSock()
    st = SS.Station(SS.Sleeve(A_ADDR, sock))
    st.pointer(0.0, 0.0)
    st.pointer(24.0, 20.0)                                             # one stroke: A then B
    st.tick(20.0)
    st.tick(1500.0)
    st.close()
    st.close()                                                         # idempotent
    assert {addr for _, addr in sock.out} == {A_ADDR}
    msgs = [json.loads(d) for d, _ in sock.out]
    cues = [m for m in msgs if "type" not in m]
    assert [m["motor"] for m in cues] == [0, 1]
    assert all(set(m) == {"cue_id", "motor", "intensity", "duration_ms", "pattern"} for m in cues)
    assert all(m["intensity"] <= 150 and m["duration_ms"] == 200 and m["pattern"] == "pulse" for m in cues)
    assert {m.get("type") for m in msgs} <= {None, "keepalive", "stop"}            # no subscribe, ping, display, ...
    assert [d for d, _ in sock.out if b"keepalive" in d] == [b'{"type":"keepalive"}'] * 2
    assert [d for d, _ in sock.out[-2:]] == [b'{"type":"stop"}'] * 2 and sock.closed


def test_the_cue_validates_against_the_device_command_schema():
    jsonschema = pytest.importorskip("jsonschema")
    schema = json.loads((REPO_ROOT / "contracts" / "schemas" / "haptic-device-command.schema.json").read_text(encoding="utf-8"))
    for m in (0, 1):
        (c,) = tap(SS.StrokeEngine().move, m, 100.0)
        jsonschema.Draft202012Validator(schema).validate(c.wire("st_0001"))


def test_recv_acks_reads_both_dialects_skips_telemetry_and_survives_a_windows_connection_reset():
    sock = FakeSock()
    sock.inbox += [
        json.dumps({"type": "sensor_data", "device_id": "CHETNA_HAPTIC_001", "sensors": {}}).encode(),
        ConnectionResetError(10054, "an earlier datagram met a closed port"),
        json.dumps({"type": "ack", "device_id": "CHETNA_HAPTIC_001", "cue_id": "st_0001", "accepted": True, "timestamp_ms": 5}).encode(),
        json.dumps({"type": "ack", "cue_id": "st_0002", "status": "rejected", "ok": False, "ack_id": "st_0002"}).encode(),
        b'{"type":"ack" oops', b'\xff"ack"', b"\xff\xfe",
    ]
    got = SS.Sleeve(A_ADDR, sock).recv_acks()
    assert [(c, ok) for c, ok, _ in got] == [("st_0001", True), ("st_0002", False)]


def test_stop_goes_out_on_any_exception_and_the_start_line_warns_about_the_headset(monkeypatch, capsys):
    made: List[FakeSleeve] = []

    class Boom(FakeSleeve):
        def __init__(self, addr: Any):
            super().__init__()
            made.append(self)

        def recv_acks(self):
            raise RuntimeError("boom")

    monkeypatch.setattr(SS, "Sleeve", Boom)
    with pytest.raises(RuntimeError):
        SS.main(["--a-ip", "127.0.0.1:24999", "--script"])
    assert {"type": "stop"} in made[0].out and made[0].closed
    assert "LAST sender" in capsys.readouterr().err and "LAST sender" in SS.__doc__


def test_the_text_claims_nothing_beyond_touch_sight_timing():
    text = Path(SS.__file__).read_text(encoding="utf-8").lower()
    for stem in ("ownership", "conscious", "therap", "rehab", "diagnos", "illusion", "healing", "cure "):
        assert stem not in text, stem
    assert "IN SYNC" in Path(SS.__file__).read_text(encoding="utf-8") and "Sleeve offline" in Path(SS.__file__).read_text(encoding="utf-8")


def test_live_plot_warns_that_it_takes_the_telemetry_stream_from_the_headset(capsys, tmp_path):
    assert "WARNING" in LP.__doc__ and "LAST sender" in LP.__doc__ and "headset" in LP.__doc__
    LP.main(["--a-ip", f"127.0.0.1:{8790 + 15270}", "--b-ip", f"127.0.0.1:{8792 + 15270}", "--discovery-port", "0",
             "--save-png", str(tmp_path / "p.png"), "--duration", "0.2", "--size", "320x180", "--no-pil"])
    err = capsys.readouterr().err
    assert err.count("live_plot: WARNING") == 1 and "LAST sender" in err and "headset" in err


# ------------------------------------------------------------------ against the real twin (subprocess, Node A only)
class TwinProc:
    """sim.sleeve.twin, Node A only, on port block `offset`; a `with` block always terminates it."""

    def __init__(self, offset: int, dialect: str, tmp: Path):
        assert 15200 <= offset <= 15290, "throwaway port blocks only"
        self.offset, self.log = offset, tmp / f"twin_{offset}.jsonl"
        self.a_port, self.disc_port, self.ctl_port = 8790 + offset, 8791 + offset, 8793 + offset
        self.lines: "queue.Queue[str]" = queue.Queue()
        self.proc = subprocess.Popen(
            [sys.executable, "-m", "sim.sleeve.twin", "--kind", "haptic", "--port-offset", str(offset), "--dialect", dialect,
             "--no-stdin", "--duration", "180", "--log", str(self.log)],
            cwd=str(REPO_ROOT), stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
        threading.Thread(target=self._pump, daemon=True).start()
        self.ready: Dict[str, Any] = {}

    def _pump(self) -> None:
        for line in self.proc.stdout:                                  # type: ignore[union-attr]
            self.lines.put(line)

    def __enter__(self) -> "TwinProc":
        try:
            self.ready = json.loads(self.lines.get(timeout=20.0))      # the readiness signal, never a sleep
            assert self.ready.get("event") == "ready" and self.ready["ports"]["haptic"] == self.a_port, self.ready
        except BaseException:
            self.close()
            raise
        return self

    def __exit__(self, *exc: Any) -> None:
        self.close()

    def close(self) -> None:
        if self.proc.poll() is None:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.proc.kill()
                self.proc.wait(timeout=5)

    def control(self, cmd: str) -> Dict[str, Any]:
        import socket
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.settimeout(3.0)
        try:
            s.sendto(cmd.encode(), ("127.0.0.1", self.ctl_port))
            return json.loads(s.recvfrom(4096)[0])
        finally:
            s.close()

    def rx(self, until: Optional[Callable[[List[Dict[str, Any]]], bool]] = None, timeout: float = 3.0) -> List[Dict[str, Any]]:
        """The datagrams the node received, from its own log; waits (bounded) until `until(rx)` holds."""
        end = time.monotonic() + timeout
        while True:
            rx: List[Dict[str, Any]] = []
            for line in (self.log.read_text(encoding="utf-8").splitlines() if self.log.exists() else []):
                try:
                    rec = json.loads(line)
                except ValueError:                                                      # a line the twin is writing right now
                    continue
                if rec.get("ev") == "rx":
                    rx.append(rec)
            if until is None or until(rx) or time.monotonic() > end:
                return rx
            time.sleep(0.05)


@pytest.mark.parametrize("dialect,offset", [("reference", 15210), ("team", 15220)])
def test_script_run_against_the_twin_gets_every_tap_acked_in_both_dialects(dialect, offset, tmp_path):
    with TwinProc(offset, dialect, tmp_path) as tw:
        assert tw.ready.get("dialect", "reference") == dialect
        found = SS.discover(tw.disc_port, 8.0)                                       # the beacon names Node A's command port
        assert found is not None and found[1] == tw.a_port
        if dialect == "reference":                                                   # its beacon says ip 127.0.0.1; the team beacon has no ip field,
            assert found[0] == "127.0.0.1"                                           # so the host is the datagram's source (loopback or LAN copy)
        r = subprocess.run([sys.executable, str(STATION), "--a-ip", f"127.0.0.1:{tw.a_port}", "--script"],
                           capture_output=True, text=True, timeout=90, cwd=str(REPO_ROOT))
        rx = tw.rx(lambda rx: sum(1 for x in rx if x["msg"].get("type") == "stop") >= 2)
    assert r.returncode == 0, r.stdout + r.stderr
    lines = r.stdout.strip().splitlines()
    assert len(lines) == 1, r.stdout
    s = json.loads(lines[0])
    print(dialect, "->", lines[0])
    assert s["pass"] is True and s["cues_sent"] == 12 and s["acks_accepted"] == 12
    assert s["acks_refused"] == 0 and s["acks_missing"] == 0 and s["dropped_by_limiter"] == 0 and s["held_offline"] == 0
    assert 0.0 < s["mean_ack_latency_ms"] < 80.0
    # what the node itself received
    msgs = [x["msg"] for x in rx]
    cues = [x for x in rx if "type" not in x["msg"]]
    assert [x["msg"]["motor"] for x in cues] == [0, 1] * 6
    assert all(set(x["msg"]) == {"cue_id", "motor", "intensity", "duration_ms", "pattern"} for x in cues)
    assert all(x["msg"]["intensity"] <= 150 and x["msg"]["duration_ms"] == 200 for x in cues)
    assert {m.get("type") for m in msgs} == {None, "keepalive", "stop"}              # the whole vocabulary on the wire
    assert sum(1 for m in msgs if m.get("type") == "keepalive") >= 15                 # about one a second for ~17 s
    assert [m.get("type") for m in msgs[-2:]] == ["stop", "stop"]
    assert len({tuple(x["from"]) for x in rx}) == 1                                   # one socket, one sender
    # the 600 ms really is on the wire: A-to-A spacing is the stroke period, plus 600 ms where the delayed strokes begin
    a_times = [x["t_ms"] for x in cues if x["msg"]["motor"] == 0]
    gaps = [b - a for a, b in zip(a_times, a_times[1:])]
    assert all(abs(g - e) < 250 for g, e in zip(gaps, [2690, 2690, 3290, 2690, 2690])), gaps      # 600 ms apart, so +-250 still tells them apart


def test_station_goes_offline_when_the_sleeve_goes_silent_and_recovers_when_it_returns(tmp_path):
    def pump(st: SS.Station, seconds: float) -> None:
        end = SS.now_ms() + seconds * 1000.0
        while SS.now_ms() < end:
            st.tick(SS.now_ms())
            time.sleep(0.002)

    with TwinProc(15230, "reference", tmp_path) as tw:
        st = SS.Station(SS.Sleeve(("127.0.0.1", tw.a_port)))
        try:
            tap(st.pointer, 0, SS.now_ms())
            pump(st, 0.4)
            assert st.acked == 1 and not st.link.offline(SS.now_ms())
            assert tw.control("off-a")["ok"]                                            # the node goes silent
            tap(st.pointer, 1, SS.now_ms())
            pump(st, 2.3)
            assert st.sent == 2 and st.acked == 1 and st.link.offline(SS.now_ms())     # no ack for 2 s: offline
            tap(st.pointer, 0, SS.now_ms())                                             # the trial tap
            tap(st.pointer, 1, SS.now_ms())                                             # held: one trial per 2 s
            assert st.sent == 3 and st.held == 1
            assert tw.control("on-a")["ok"]                                             # the node returns
            pump(st, 2.1)
            tap(st.pointer, 1, SS.now_ms())                                             # the next trial
            pump(st, 0.5)
            assert st.acked == 2 and not st.link.offline(SS.now_ms())                  # acks are back: online again
        finally:
            st.close()


def test_with_no_sleeve_at_all_nothing_is_acked_and_taps_are_held_not_blasted():
    st = SS.Station(SS.Sleeve(("127.0.0.1", 8790 + 15250)))                            # nothing listens here
    try:
        s = SS.run_script(st, strokes=2, sync=1)
    finally:
        st.close()
    assert s["pass"] is False and s["acks_accepted"] == 0
    assert s["held_offline"] >= 1 and s["cues_sent"] <= 3
    assert SS.discover(8791 + 15280, 0.3) is None                                       # no beacon, no address
