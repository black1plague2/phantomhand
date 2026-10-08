"""Tests for sim/live/phantom_replay.py (fake_headset --game phantom_hand).

Pure helpers are tested directly; the headset is tested end to end against the REAL sleeve twin
(sim/sleeve/twin.py, in-process, port offset 12100+) and the real fake_hub, with explicit readiness (the
twin's discovery datagram, first stream datagram, hello_ack). No sleeps stand in for readiness.

    cd sim/live && .venv/Scripts/python.exe -m pytest test_phantom_replay.py -q
"""
from __future__ import annotations

import asyncio
import json
import socket
import sys
from pathlib import Path
from typing import Any, Dict, List

import pytest

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO))

from fake_hub import FakeHub  # noqa: E402
from phantom_replay import (DEFAULT_FIXTURE, NodeLink, PhantomHeadset, ReplayClock, accel_magnitude,  # noqa: E402
                            build_sens_chunks, compress_schedule, discover_nodes, emg_level,
                            prepare_session_copy, real_time_windows)
from sim.sleeve.twin import EventLog, Twin, TwinServer  # noqa: E402

SENSOR_SCHEMA = json.loads((REPO / "contracts" / "schemas" / "sensor-file.schema.json").read_text(encoding="utf-8"))
OFFSET = 12100


def load_events(path: Path) -> List[Dict[str, Any]]:
    return [json.loads(l) for l in (path / "events.ndjson").read_text(encoding="utf-8").splitlines() if l.strip()]


# ------------------------------------------------------------------------------ pure helpers
def test_compress_caps_long_gaps_only_and_keeps_order():
    t = [0, 100, 5000, 5200, 20000]
    w = compress_schedule(t, cap_ms=1000)
    assert w == pytest.approx([0.0, 0.1, 1.1, 1.3, 2.3])
    assert w == sorted(w)


def test_compress_speed_divides_and_zero_cap_disables():
    t = [0, 4000]
    assert compress_schedule(t, 0, speed=2.0) == pytest.approx([0.0, 2.0])
    assert compress_schedule(t, 500, speed=2.0) == pytest.approx([0.0, 0.25])


def test_compress_keeps_real_windows_uncompressed():
    t = [0, 10000, 20000]
    w = compress_schedule(t, 1000, real_windows=[(9000, 10500)])
    assert w == pytest.approx([0.0, 10.0, 11.0])        # first gap ends inside the window: real; second compressed


def test_compress_nonmonotonic_input_never_goes_back():
    w = compress_schedule([0, 500, 400, 900], 1000)
    assert w == sorted(w)


def test_real_time_windows_surround_each_threat_impact():
    ev = [{"type": "threat_impact", "t_ms": 34000}, {"type": "stroke", "t_ms": 1}]
    assert real_time_windows(ev) == [(34000 - 2600.0, 34000 + 1800.0)]


def test_replay_clock_interpolates_and_reports_slope():
    c = ReplayClock([0.0, 1.0, 2.0], [0.0, 1000.0, 11000.0])
    assert c.session_ms(0.5) == pytest.approx(500.0)
    assert c.session_ms(1.5) == pytest.approx(6000.0)
    assert c.slope(0.5) == pytest.approx(1000.0)
    assert c.slope(1.5) == pytest.approx(10000.0)
    assert c.session_ms(5.0) == pytest.approx(11000.0 + 3000.0)


def test_accel_magnitude_and_emg_level():
    msg = {"sensors": {f"imu_accel_{a}": {"value": v} for a, v in zip("xyz", (3.0, 4.0, 0.0))}}
    assert accel_magnitude(msg) == pytest.approx(5.0)
    assert accel_magnitude({"sensors": {}}) is None
    assert emg_level(420.0) == 0.0 and emg_level(5000.0) == 1.0 and 0.0 < emg_level(900.0) < 1.0


def test_prepare_session_copy_fresh_id_no_metrics_renumbered(tmp_path):
    dst, sid = prepare_session_copy(DEFAULT_FIXTURE, tmp_path / "s")
    names = sorted(p.name for p in dst.iterdir())
    assert "metrics.json" not in names and not any(n.startswith("sens_") for n in names)
    assert json.loads((dst / "session.json").read_text(encoding="utf-8"))["session_id"] == sid
    assert sid != "7a1b9c52-3d0e-4f2a-9b66-5d1c0a3e8f10"
    assert [e["seq"] for e in load_events(dst)] == list(range(len(load_events(dst))))


def test_prepare_session_copy_without_bio_drops_emg_everywhere(tmp_path):
    dst, _ = prepare_session_copy(DEFAULT_FIXTURE, tmp_path / "s", drop_bio=True)
    ev = load_events(dst)
    assert not [e for e in ev if e["type"] == "emg_burst"]
    tr = [e for e in ev if e["type"] == "threat_response"]
    assert tr and all(e["data"]["emg_peak_x"] is None and e["data"]["emg_latency_ms"] is None for e in tr)


def test_build_sens_chunks_matches_the_sensor_schema_and_the_input():
    import jsonschema
    emg = [(i * 10.0, 420.0 + i) for i in range(1200)]          # 12 s at 100 Hz
    imu = [(i * 10.0, 0.0, 0.0, 9.81, 0.0, 0.0, 0.0) for i in range(1200)]
    chunks = build_sens_chunks("sid", emg, imu)
    assert [c["chunk_seq"] for c in chunks] == [0, 1, 2]
    for c in chunks:
        jsonschema.validate(c, SENSOR_SCHEMA)
        n = len(c["emg_env"]["t_ms"])
        assert n == len(c["emg_env"]["value"]) and len(c["imu"]["az"]) == len(c["imu"]["t_ms"])
    assert sum(len(c["emg_env"]["t_ms"]) for c in chunks) == 1200
    assert build_sens_chunks("sid", [], []) == []
    only_imu = build_sens_chunks("sid", [], imu[:10])
    assert "emg_env" not in only_imu[0]
    jsonschema.validate(only_imu[0], SENSOR_SCHEMA)


# ------------------------------------------------------------------------------ with the real twin
class TwinRunner:
    def __init__(self, offset: int, kinds=("haptic", "bio")):
        self.log = EventLog(None, keep=True)
        self.twin = Twin(kinds, seed=3, port_offset=offset, log=self.log)
        self.srv = TwinServer(self.twin, control=True)

    def __enter__(self):
        self.srv.start()
        return self

    def __exit__(self, *a):
        self.srv.stop()

    @property
    def strokes(self) -> List[Dict[str, Any]]:
        return [r for r in self.log.records if r.get("ev") == "stroke"]


def test_discover_nodes_waits_for_the_announcement():
    async def go():
        port = 12150
        got = asyncio.ensure_future(discover_nodes(port, ("haptic", "bio"), timeout=5.0))
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        msgs = [{"type": "device_discovery", "device_kind": "haptic", "command_port": 8790},
                {"type": "device_discovery", "device_kind": "bio", "command_port": 8792}]
        # a node announces at 1 Hz; do the same until the listener reports (retransmit, not a readiness sleep)
        while not got.done():
            for m in msgs:
                s.sendto(json.dumps(m).encode(), ("127.0.0.1", port))
            await asyncio.wait([got], timeout=0.05)
        s.close()
        return await got

    res = asyncio.run(go())
    assert res["haptic"][1] == 8790 and res["bio"][1] == 8792


def test_discover_nodes_times_out_with_what_it_saw():
    res = asyncio.run(discover_nodes(12151, ("haptic",), timeout=0.3))
    assert res == {}


def test_node_link_request_acks_and_times_out():
    with TwinRunner(OFFSET) as t:
        async def go():
            link = NodeLink("haptic", ("127.0.0.1", t.twin.ports["haptic"]), lambda n, m: None)
            await link.open()
            ack, rtt = await link.request({"cue_id": "x1", "motor": 0, "intensity": 150, "duration_ms": 200,
                                           "pattern": "pulse", "cue": "stroke"}, "x1")
            bad, none = await link.request({"cue_id": "x2", "motor": 3, "intensity": 150, "duration_ms": 200,
                                            "pattern": "pulse", "cue": "stroke"}, "x2")
            link.close()
            dead = NodeLink("haptic", ("127.0.0.1", 12199), lambda n, m: None)   # nothing listens there
            await dead.open()
            miss, mt = await dead.request({"cue_id": "x3"}, "x3", timeout=0.2)
            dead.close()
            return ack, rtt, bad, miss, mt

        ack, rtt, bad, miss, mt = asyncio.run(go())
    assert ack["ok"] is True and 0 < rtt < 200
    assert bad["ok"] is False and bad["error_code"] == "MOTOR_UNAVAILABLE"      # a rejection is an answer, not a timeout
    assert miss is None and mt is None


def mini_session(tmp_path: Path, until_ms: float = 36000) -> Path:
    """The fixture up to the first threat response: calibrate .. async induction .. threat. ~7 s at cap 200 ms."""
    src, _ = prepare_session_copy(DEFAULT_FIXTURE, tmp_path / "full")
    dst = tmp_path / "mini"
    dst.mkdir()
    ev = [e for e in load_events(src) if e["t_ms"] <= until_ms]
    for i, e in enumerate(ev):
        e["seq"] = i
    (dst / "events.ndjson").write_text("".join(json.dumps(e) + "\n" for e in ev), encoding="utf-8")
    (dst / "session.json").write_bytes((src / "session.json").read_bytes())
    return dst


class CapturingHub(FakeHub):
    def __init__(self, *a, **kw):
        super().__init__(*a, **kw)
        self.statuses: List[Dict[str, Any]] = []

    async def handle_message(self, data, ws):
        await super().handle_message(data, ws)
        m = json.loads(data)
        if m.get("type") == "status":
            self.statuses.append(m["payload"])


async def run_headset(session: Path, twin: TwinRunner, hub_out: Path, node_b: bool = True,
                      off_a_at=None) -> tuple:
    hub = CapturingHub(port=_free_port(), beacon=False, scenario=None, beacon_port=_free_port(), out_dir=hub_out)
    await hub.start()
    try:
        found = await discover_nodes(twin.twin.ports["discovery"], ("haptic", "bio") if node_b else ("haptic",), 5.0)
        hs = PhantomHeadset(session, node_a=found["haptic"], node_b=found.get("bio"),
                            control=("127.0.0.1", twin.twin.ports["control"]), host="127.0.0.1", port=hub.port,
                            compress_gap_ms=200.0, status_period_s=0.2, off_a_at=off_a_at)
        hs.state.session_id = json.loads((session / "session.json").read_text(encoding="utf-8"))["session_id"]
        await hs.start_nodes()
        streams = await hs.wait_node_streams(5.0)
        assert streams == {"haptic": True, **({"bio": True} if node_b else {})}
        await hs.connect_hub("127.0.0.1", hub.port)
        await hs.replay_session()
        await hs.disconnect()
        return hs, hub
    finally:
        await hub.stop()


def _free_port() -> int:
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


def test_headset_drives_the_twin_with_real_strokes_and_patches_the_events(tmp_path):
    session = mini_session(tmp_path)
    with TwinRunner(OFFSET + 10) as t:
        hs, hub = asyncio.run(run_headset(session, t, tmp_path / "hub"))
        executed = [r for r in t.strokes if r.get("status") == "executed"]
        displays = [r for r in t.log.records if r.get("ev") == "rx" and (r.get("msg") or {}).get("type") == "display"]
    cues = [e for e in hs.events_out if e["type"] == "haptic_cue"]
    assert len(cues) == 8 and hs.report["cues_sent"] == 8
    assert all(e["data"]["delivered"] is True and isinstance(e["data"]["ack_latency_ms"], int) for e in cues)
    assert len(executed) == 8                                          # the twin saw and executed every cue
    assert {r["motor"] for r in executed} == {0, 1} and all(r["intensity_applied"] == 150 for r in executed)
    assert [d["msg"]["text"] for d in displays] == ["ASYNC"]           # OLED line at the (only) induction
    assert hs.report["flinch_sent"] == 1 and hs.report["emg_bursts_from_node"] >= 1
    # the hub got every event exactly once and every status carried game_state + trace
    assert len(hub.state.received_trial_events) == len(hs.events_out)
    assert hub.state.invalid_count == 0, hub.state.invalid_messages
    gs = [s for s in hub.statuses if "game_state" in s]
    assert gs and {s["game_state"]["condition"] for s in gs} >= {None, "async"}
    assert {s["game_state"]["phase"] for s in gs} >= {"calibrate", "induction", "threat"}
    assert all(s["game_state"]["nodes"]["haptic"]["connected"] for s in gs[2:])
    traces = [s["trace"] for s in gs]
    assert all(len(x["emg_env"]) <= 100 and len(x["accel_mag"]) <= 100 and x["fs_hz"] == 20 for x in traces)
    assert sum(len(x["emg_env"]) for x in traces) > 20 and sum(len(x["accel_mag"]) for x in traces) > 20
    # sensor files recorded from what the nodes streamed: the flinch must be visible in them
    files = sorted(session.glob("sens_*.json"))
    assert files and hs.report["sens_files"] == len(files)
    emg: List[tuple] = []
    for f in files:
        d = json.loads(f.read_text())
        if "emg_env" in d:
            emg += list(zip(d["emg_env"]["t_ms"], d["emg_env"]["value"]))
    impact = next(e["t_ms"] for e in hs.events_out if e["type"] == "threat_impact")
    peak = max(v for t_, v in emg if impact <= t_ <= impact + 1500)
    rest = sorted(v for t_, v in emg if impact - 2000 <= t_ < impact)
    assert rest and peak > 3 * rest[len(rest) // 2], (peak, rest[len(rest) // 2])


def test_node_a_off_midrun_marks_cues_undelivered_and_status_offline(tmp_path):
    session = mini_session(tmp_path)
    with TwinRunner(OFFSET + 20) as t:
        hs, hub = asyncio.run(run_headset(session, t, tmp_path / "hub", off_a_at=0.5))
    cues = [e["data"]["delivered"] for e in hs.events_out if e["type"] == "haptic_cue"]
    assert True in cues and False in cues
    assert cues.index(False) > cues.index(True) and all(c is False for c in cues[cues.index(False):])   # a cut, not noise
    gs = [s["game_state"]["nodes"]["haptic"]["connected"] for s in hub.statuses if "game_state" in s]
    assert True in gs and False in gs and hs.report["status_haptic_disconnected"] > 0
    undelivered = [e["data"] for e in hs.events_out if e["type"] == "haptic_cue" and not e["data"]["delivered"]]
    assert all(d["ack_latency_ms"] is None for d in undelivered)


def test_node_b_absent_means_no_emg_anywhere(tmp_path):
    session = mini_session(tmp_path)
    # a headset that never saw Node B records no EMG: the prepared copy has no emg_burst, EMG fields are null
    dst, _ = prepare_session_copy(DEFAULT_FIXTURE, tmp_path / "nb", drop_bio=True)
    ev = [e for e in load_events(dst) if e["t_ms"] <= 36000]
    (session / "events.ndjson").write_text("".join(json.dumps(e) + "\n" for e in ev), encoding="utf-8")
    with TwinRunner(OFFSET + 30, kinds=("haptic",)) as t:
        hs, hub = asyncio.run(run_headset(session, t, tmp_path / "hub", node_b=False))
    gs = [s["game_state"]["nodes"]["bio"] for s in hub.statuses if "game_state" in s]
    assert gs and all(b["connected"] is False and b["emg_level"] is None for b in gs)
    assert all(not s["trace"]["emg_env"] for s in hub.statuses if "trace" in s)
    for f in session.glob("sens_*.json"):
        assert "emg_env" not in json.loads(f.read_text())
    assert not [e for e in hs.events_out if e["type"] == "emg_burst"]
