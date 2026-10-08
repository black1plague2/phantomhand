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
from phantom_replay import (DEFAULT_FIXTURE, NodeLink, PhantomHeadset, ReplayClock, TelemetryListener,  # noqa: E402
                            accel_magnitude, ack_delivered, build_sens_chunks, compress_schedule, discover_nodes,
                            emg_level, prepare_session_copy, real_time_windows)
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
    def __init__(self, offset: int, kinds=("haptic", "bio"), dialect: str = "reference", telemetry_port: int = 0):
        self.log = EventLog(None, keep=True)
        self.twin = Twin(kinds, seed=3, port_offset=offset, log=self.log, dialect=dialect, telemetry_port=telemetry_port)
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
                      off_a_at=None, telemetry_port=None) -> tuple:
    hub = CapturingHub(port=_free_port(), beacon=False, scenario=None, beacon_port=_free_port(), out_dir=hub_out)
    await hub.start()
    try:
        found = await discover_nodes(twin.twin.ports["discovery"], ("haptic", "bio") if node_b else ("haptic",), 5.0)
        hs = PhantomHeadset(session, node_a=found["haptic"], node_b=found.get("bio"),
                            control=("127.0.0.1", twin.twin.ports["control"]), host="127.0.0.1", port=hub.port,
                            compress_gap_ms=200.0, status_period_s=0.2, off_a_at=off_a_at, telemetry_port=telemetry_port)
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


# ------------------------------------------------------------------------------ the electronics team's firmware dialect
def test_ack_delivered_follows_the_contract_precedence_ok_then_accepted_then_status():
    assert ack_delivered({"ok": True, "status": "rejected"}) is True            # ok wins over everything
    assert ack_delivered({"ok": False, "accepted": True}) is False
    assert ack_delivered({"accepted": True, "status": "rejected"}) is True       # then accepted
    assert ack_delivered({"accepted": False, "status": "executed"}) is False
    assert ack_delivered({"status": "executed"}) is True and ack_delivered({"status": "accepted"}) is True
    assert ack_delivered({"status": "rejected"}) is False and ack_delivered({"status": "error"}) is False
    assert ack_delivered({}) is False and ack_delivered(None) is False            # no verdict, no ack: not delivered


def test_headset_against_the_team_dialect_twin_delivers_by_accepted_and_records_accel_only_imu(tmp_path):
    import jsonschema
    session = mini_session(tmp_path)
    with TwinRunner(OFFSET + 40, dialect="team") as t:
        hs, hub = asyncio.run(run_headset(session, t, tmp_path / "hub"))
        executed = [r for r in t.strokes if r.get("status") == "executed"]
        keepalives = {r["node"] for r in t.log.records
                      if r.get("ev") == "rx" and (r.get("msg") or {}).get("type") == "keepalive"}
    cues = [e for e in hs.events_out if e["type"] == "haptic_cue"]
    assert len(cues) == 8 and len(executed) == 8                       # their ack has no ok/status: delivered comes from `accepted`
    assert all(e["data"]["delivered"] is True and isinstance(e["data"]["ack_latency_ms"], int) for e in cues)
    assert hs.report["cues_acked"] == 8 and hs.report["cue_rejected"] == []
    assert keepalives == {"A", "B"}                                    # the bare keepalive goes to each node
    assert hs.report["emg_bursts_from_node"] == 0 and hs.report["flinch_sent"] == 1     # no emg_burst messages in their firmware
    assert hub.state.invalid_count == 0, hub.state.invalid_messages
    files = sorted(session.glob("sens_*.json"))
    assert files
    imu_n, emg = 0, []
    for f in files:
        d = json.loads(f.read_text())
        jsonschema.validate(d, SENSOR_SCHEMA)
        if "imu" in d:
            imu_n += len(d["imu"]["t_ms"])
            assert set(d["imu"]["gx"]) == set(d["imu"]["gy"]) == set(d["imu"]["gz"]) == {0.0}      # accel only: gyro 0.0, as Unity records it
            assert any(abs(v) > 1.0 for v in d["imu"]["az"])
        if "emg_env" in d:
            emg += list(zip(d["emg_env"]["t_ms"], d["emg_env"]["value"]))
    assert imu_n > 100
    impact = next(e["t_ms"] for e in hs.events_out if e["type"] == "threat_impact")
    peak = max(v for t_, v in emg if impact <= t_ <= impact + 1500)
    rest = sorted(v for t_, v in emg if impact - 2000 <= t_ < impact)
    assert rest and peak > 3 * rest[len(rest) // 2]                    # the flinch is still visible in 4-value chunks


# ------------------------------------------------------------------------------ the real boards' fixed telemetry port
def _free_udp_port() -> int:
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("0.0.0.0", 0))
    p = s.getsockname()[1]
    s.close()
    return p


def test_telemetry_listener_hands_a_datagram_to_the_exact_source_then_the_only_link_with_that_ip_else_nobody():
    got: List[tuple] = []
    on_msg = lambda name, msg: got.append((name, msg["type"]))                       # noqa: E731
    boards = {"haptic": NodeLink("haptic", ("10.0.0.1", 8790), on_msg), "bio": NodeLink("bio", ("10.0.0.2", 8790), on_msg)}
    lst = TelemetryListener(boards)
    assert lst.owner(("10.0.0.1", 8790)) is boards["haptic"]                         # exact (ip, port)
    assert lst.owner(("10.0.0.2", 51234)) is boards["bio"]                           # another source port: the only link on that IP
    assert lst.owner(("10.0.0.3", 8790)) is None                                     # a stranger
    twin = {"haptic": NodeLink("haptic", ("127.0.0.1", 39790), on_msg), "bio": NodeLink("bio", ("127.0.0.1", 39792), on_msg)}
    lst = TelemetryListener(twin)
    assert lst.owner(("127.0.0.1", 39792)) is twin["bio"]                            # both links on one IP: only the exact port counts
    assert lst.owner(("127.0.0.1", 5555)) is None                                    # ambiguous: nobody
    lst.datagram_received(json.dumps({"type": "sensor_chunk", "emg_envelope": [420.0]}).encode(), ("127.0.0.1", 39792))
    lst.datagram_received(json.dumps({"type": "sensor_data", "sensors": {}}).encode(), ("127.0.0.1", 5555))
    bio, haptic = twin["bio"], twin["haptic"]
    assert got == [("bio", "sensor_chunk")] and bio.rx_count == 1 and bio.first_data.is_set() and bio.last_rx > 0
    assert haptic.rx_count == 0 and not haptic.first_data.is_set() and haptic.last_rx == 0.0     # the stray went nowhere


def test_headset_against_the_team_twin_with_a_telemetry_port_still_gets_imu_and_emg(tmp_path):
    # their boards send sensor_data / sensor_chunk to the last sender's IP on a FIXED port, not to the port a command came
    # from: with no listener on that port the headset would get acks and no stream at all
    session = mini_session(tmp_path)
    port = _free_udp_port()
    with TwinRunner(OFFSET + 50, dialect="team", telemetry_port=port) as t:
        hs, hub = asyncio.run(run_headset(session, t, tmp_path / "hub", telemetry_port=port))
    assert hs.report["cues_acked"] == hs.report["cues_sent"] == 8                     # acks still come back to each command socket
    assert hs.report["sensor_data"] > 100 and hs.report["sens_chunks"] > 25
    assert hs.report["sens_samples"]["imu"] > 100 and hs.report["sens_samples"]["emg"] > 100
    gs = [s["game_state"]["nodes"] for s in hub.statuses if "game_state" in s]
    assert gs and all(n["haptic"]["connected"] and n["bio"]["connected"] for n in gs[2:])      # the listener feeds last_rx


def test_a_telemetry_port_that_cannot_be_bound_is_one_line_on_stderr_and_the_run_goes_on(tmp_path, capsys):
    session = mini_session(tmp_path)
    taken = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    taken.bind(("0.0.0.0", 0))
    port = taken.getsockname()[1]

    async def go(t: TwinRunner):
        found = await discover_nodes(t.twin.ports["discovery"], ("haptic", "bio"), 5.0)
        hs = PhantomHeadset(session, node_a=found["haptic"], node_b=found["bio"], telemetry_port=port)
        await hs.start_nodes()
        try:
            return hs.telemetry, await hs.wait_node_streams(5.0)
        finally:
            for link in hs.links.values():
                link.close()

    try:
        with TwinRunner(OFFSET + 60) as t:                                           # reference dialect: it streams to the source port
            listener, streams = asyncio.run(go(t))
    finally:
        taken.close()
    assert listener is None and streams == {"haptic": True, "bio": True}
    lines = [l for l in capsys.readouterr().err.splitlines() if "telemetry listener" in l]
    assert len(lines) == 1 and str(port) in lines[0]
