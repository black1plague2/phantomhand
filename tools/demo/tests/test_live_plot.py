"""Tests for tools/demo/live_plot.py (laptop live plot, PRD FR-AP-01 fallback).

Data handling and drawing are tested directly; the network feeds are tested against the REAL sleeve twin
(sim/sleeve/twin.py, in-process, port offsets 13000+), waiting on LiveData.wait_for (condition variable), never on
a fixed sleep.

    analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_live_plot.py -q
"""
from __future__ import annotations

import json
import socket
import struct
import sys
import threading
import zlib
from pathlib import Path
from typing import Any, Dict, List, Tuple

import pytest

from conftest import REPO_ROOT

sys.path.insert(0, str(REPO_ROOT))

import live_plot as LP  # noqa: E402
from sim.sleeve.twin import EventLog, Twin, TwinServer  # noqa: E402


def chunk_msg(vals: List[float]) -> Dict[str, Any]:
    return {"type": "sensor_chunk", "device_id": "CHETNA_BIO_001", "sample_rate_hz": 100, "emg_envelope": vals}


def accel_msg(x: float, y: float, z: float) -> Dict[str, Any]:
    return {"type": "sensor_data", "sensors": {f"imu_accel_{a}": {"value": v, "unit": "m/s2"} for a, v in zip("xyz", (x, y, z))}}


# ------------------------------------------------------------------ LiveData
def test_chunk_samples_are_spread_back_in_time_ending_at_arrival():
    d = LP.LiveData()
    d.ingest_node("B", chunk_msg([float(i) for i in range(10)]), t=100.0)
    ts = [t for t, _ in d.emg]
    assert ts == pytest.approx([100.0 - (9 - i) * 0.01 for i in range(10)])
    assert [v for _, v in d.emg] == [float(i) for i in range(10)]


def test_accel_magnitude_and_counts():
    d = LP.LiveData()
    d.ingest_node("A", accel_msg(3.0, 4.0, 0.0), 5.0)
    d.ingest_node("A", {"type": "sensor_data", "sensors": {}}, 5.1)           # malformed: ignored, not a crash
    assert list(d.acc) == [(5.0, 5.0)]
    assert d.counts["sensor_data"] == 1


def test_snapshot_keeps_only_the_window_and_uses_negative_ages():
    d = LP.LiveData(window_s=10.0)
    d.ingest_node("B", chunk_msg([1.0] * 10), 0.0)
    d.ingest_node("B", chunk_msg([2.0] * 10), 95.0)
    s = d.snapshot(100.0)
    assert {v for _, v in s["emg"]} == {2.0}
    assert all(-10.0 <= t <= 0.0 for t, _ in s["emg"])


def test_bursts_and_node_staleness():
    d = LP.LiveData()
    d.ingest_node("B", {"type": "emg_burst", "peak": 900.0}, 50.0)
    assert d.snapshot(51.0)["bursts"] == [(-1.0, 900.0)]
    assert d.snapshot(51.0)["b_ok"] and not d.snapshot(51.0)["a_ok"]
    assert not d.snapshot(50.0 + LP.STALE_S + 0.1)["b_ok"]               # silent for > 1.5 s means offline


def test_hub_status_gives_condition_phase_and_marks_each_impact_once():
    d = LP.LiveData()
    doc = {"status": {"game_state": {"phase": "threat", "condition": "sync", "remaining_s": 3.0,
                                     "nodes": {"haptic": {"connected": True}, "bio": {"connected": True, "emg_level": 0.1}}}},
           "events": [{"type": "stroke", "seq": 1}, {"type": "threat_impact", "seq": 2}]}
    d.ingest_hub(doc, 10.0)
    d.ingest_hub(doc, 10.5)                                               # same events polled again: no duplicate mark
    s = d.snapshot(10.6)
    assert (s["condition"], s["phase"]) == ("sync", "threat")
    assert len(s["impacts"]) == 1
    d.ingest_hub({**doc, "events": doc["events"] + [{"type": "threat_impact", "seq": 3}]}, 11.0)
    assert len(d.snapshot(11.1)["impacts"]) == 2


def test_hub_without_game_state_or_route_is_reported_not_hidden():
    d = LP.LiveData()
    d.ingest_hub({"status": None, "events": []}, 1.0)
    assert "no game_state" in d.snapshot(1.0)["hub_state"]
    d.hub_error("hub has no /opus/v1/live/last_status (HTTP 404)")
    s = d.snapshot(1.0)
    assert s["condition"] is None and "404" in s["hub_state"]


def test_parse_hostport_defaults_to_the_real_node_port():
    assert LP.parse_hostport("10.0.0.5") == ("10.0.0.5", 8790)
    assert LP.parse_hostport("127.0.0.1:39790") == ("127.0.0.1", 39790)
    assert LP.parse_hostport(None) is None


# ------------------------------------------------------------------ drawing
class Recorder:
    name = "rec"

    def __init__(self):
        self.texts: List[str] = []
        self.rects: List[Tuple] = []
        self.lines = 0
        self.polys: List[int] = []

    def rect(self, x0, y0, x1, y1, col): self.rects.append((x0, y0, x1, y1, col))
    def line(self, *a, **k): self.lines += 1
    def polyline(self, pts, col, width=2): self.polys.append(len(pts))
    def text(self, x, y, s, col, size=12): self.texts.append(s)


def snap_with(**over):
    d = LP.LiveData()
    d.ingest_node("B", chunk_msg([420.0 + i for i in range(10)]), 100.0)
    d.ingest_node("A", accel_msg(0, 0, 9.81), 100.0)
    s = d.snapshot(100.0)
    s.update(over)
    return s


def test_scene_shows_sync_async_and_node_status():
    r = Recorder()
    LP.draw_scene(r, snap_with(condition="sync", phase="induction"), 1280, 720)
    assert "SYNC" in r.texts and "ASYNC" not in r.texts
    assert any("Node A" in t and "OK" in t for t in r.texts) and any("Node B" in t and "OK" in t for t in r.texts)
    assert r.polys == [10, 1]
    r2 = Recorder()
    LP.draw_scene(r2, snap_with(condition="async", phase="induction"), 1280, 720)
    assert "ASYNC" in r2.texts
    r3 = Recorder()
    LP.draw_scene(r3, snap_with(condition=None, phase=None, hub_state="no --hub given", a_ok=False), 1280, 720)
    assert "--" in r3.texts and any("OFFLINE" in t for t in r3.texts) and any("no --hub given" in t for t in r3.texts)


def test_scene_marks_bursts_and_impacts():
    r = Recorder()
    LP.draw_scene(r, snap_with(bursts=[(-2.0, 900.0)], impacts=[-2.5]), 1280, 720)
    assert "emg_burst" in r.texts and "threat_impact" in r.texts


def test_scene_with_no_data_says_so():
    r = Recorder()
    LP.draw_scene(r, LP.LiveData().snapshot(1.0), 1280, 720)
    assert r.texts.count("no data") == 2


def png_info(b: bytes) -> Tuple[int, int, bytes]:
    assert b[:8] == b"\x89PNG\r\n\x1a\n"
    w, h = struct.unpack(">II", b[16:24])
    i, idat = 8, b""
    while i < len(b):
        n = struct.unpack(">I", b[i:i + 4])[0]
        tag = b[i + 4:i + 8]
        body = b[i + 8:i + 8 + n]
        assert struct.unpack(">I", b[i + 8 + n:i + 12 + n])[0] == zlib.crc32(tag + body) & 0xFFFFFFFF
        if tag == b"IDAT":
            idat += body
        i += 12 + n
    return w, h, zlib.decompress(idat)


def test_raster_png_is_valid_sized_and_not_blank(tmp_path):
    p = tmp_path / "f.png"
    backend = LP.render_png(snap_with(condition="sync"), str(p), 640, 360, prefer_pil=False)
    assert backend == "raster"
    w, h, raw = png_info(p.read_bytes())
    assert (w, h) == (640, 360) and len(raw) == 360 * (1 + 640 * 3)
    px = {raw[1 + y * (1 + 640 * 3) + x * 3: 1 + y * (1 + 640 * 3) + x * 3 + 3] for y in range(0, 360, 7) for x in range(0, 640, 7)}
    assert len(px) >= 4                                                    # background, panel, trace, badge ...
    assert bytes(LP.SYNC_C) in px                                          # the SYNC badge is drawn in green


def test_render_png_uses_whichever_backend_exists_and_writes_a_valid_file(tmp_path):
    p = tmp_path / "g.png"
    backend = LP.render_png(snap_with(condition="async"), str(p), 800, 450)
    assert backend in ("pil", "raster")
    w, h, _ = png_info(p.read_bytes()) if backend == "raster" else (800, 450, None)
    assert (w, h) == (800, 450) and p.stat().st_size > 1000


# ------------------------------------------------------------------ feeds against the real twin
class TwinCtx:
    def __init__(self, offset: int, kinds=("haptic", "bio")):
        self.twin = Twin(kinds, seed=5, port_offset=offset, log=EventLog(None))
        self.srv = TwinServer(self.twin, control=True)

    def __enter__(self):
        self.srv.start()
        return self

    def __exit__(self, *a):
        self.srv.stop()

    def control(self, cmd: str) -> Dict[str, Any]:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.settimeout(2.0)
        s.sendto(cmd.encode(), ("127.0.0.1", self.twin.ports["control"]))
        data, _ = s.recvfrom(4096)
        s.close()
        return json.loads(data)


def test_feeds_with_explicit_addresses_receive_both_streams_and_a_flinch_burst():
    with TwinCtx(13000) as t:
        d = LP.LiveData()
        f = LP.Feeds(d, ("127.0.0.1", t.twin.ports["haptic"]), ("127.0.0.1", t.twin.ports["bio"]), 0, None)
        f.start()
        try:
            assert d.wait_for(lambda x: x.counts["sensor_chunk"] >= 3 and x.counts["sensor_data"] >= 20, 8.0)
            assert t.control("flinch")["ok"]
            assert d.wait_for(lambda x: x.counts["emg_burst"] >= 1, 8.0)
            s = d.snapshot(__import__("time").monotonic())
            assert s["a_ok"] and s["b_ok"]
            assert max(v for _, v in s["emg"]) > 3 * 420 * 0.5 and len(s["bursts"]) >= 1
            assert 9.0 < sum(v for _, v in s["acc"]) / len(s["acc"]) < 10.6          # quiet arm: |a| ~ 9.81
        finally:
            f.stop()


def test_feeds_discover_nodes_by_announcement_and_a_second_viewer_does_not_steal_the_stream():
    with TwinCtx(13010) as t:
        d1, d2 = LP.LiveData(), LP.LiveData()
        port = t.twin.ports["discovery"]
        f1 = LP.Feeds(d1, None, None, port, None)
        f2 = LP.Feeds(d2, ("127.0.0.1", t.twin.ports["haptic"]), None, 0, None)
        f1.start()
        f2.start()
        try:
            assert d1.wait_for(lambda x: x.counts["sensor_chunk"] >= 2 and x.counts["sensor_data"] >= 10, 10.0)
            assert set(f1.discovered) == {"A", "B"}
            assert d2.wait_for(lambda x: x.counts["sensor_data"] >= 10, 5.0)        # both subscribed, both streaming
            assert d1.wait_for(lambda x: x.counts["sensor_data"] >= 40, 5.0)
        finally:
            f1.stop()
            f2.stop()


def test_a_node_that_goes_silent_is_shown_offline():
    with TwinCtx(13020) as t:
        d = LP.LiveData()
        f = LP.Feeds(d, ("127.0.0.1", t.twin.ports["haptic"]), ("127.0.0.1", t.twin.ports["bio"]), 0, None)
        f.start()
        try:
            assert d.wait_for(lambda x: x.counts["sensor_chunk"] >= 2 and x.counts["sensor_data"] >= 5, 8.0)
            t.control("off-b")
            import time as _t
            deadline = _t.monotonic() + 6.0
            seen_off = False
            while _t.monotonic() < deadline:
                seen_off = not d.snapshot(_t.monotonic())["b_ok"]
                if seen_off:
                    break
                d.wait_for(lambda x: False, 0.1)           # bounded condition wait, not a readiness sleep
            assert seen_off and d.snapshot(_t.monotonic())["a_ok"]
        finally:
            f.stop()


def test_hub_poller_reads_condition_from_a_hub_route():
    from http.server import BaseHTTPRequestHandler, HTTPServer
    body = json.dumps({"status": {"game_state": {"phase": "induction", "condition": "async", "remaining_s": 9.0,
                                                 "nodes": {}}}, "events": []}).encode()

    class H(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path == "/opus/v1/live/last_status":
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            else:
                self.send_response(404)
                self.end_headers()

        def log_message(self, *a):
            pass

    srv = HTTPServer(("127.0.0.1", 0), H)
    th = threading.Thread(target=srv.serve_forever, daemon=True)
    th.start()
    try:
        d = LP.LiveData()
        f = LP.Feeds(d, None, None, 0, f"127.0.0.1:{srv.server_address[1]}")
        f.start()
        assert d.wait_for(lambda x: x.condition == "async", 5.0)
        f.stop()
    finally:
        srv.shutdown()


def test_cli_save_png_headless_end_to_end(tmp_path):
    with TwinCtx(13030) as t:
        out = tmp_path / "plot.png"
        rc = LP.main(["--a-ip", f"127.0.0.1:{t.twin.ports['haptic']}", "--b-ip", f"127.0.0.1:{t.twin.ports['bio']}",
                      "--discovery-port", "0", "--save-png", str(out), "--duration", "2.5", "--size", "640x360", "--no-pil"])
    assert rc == 0
    w, h, raw = png_info(out.read_bytes())
    assert (w, h) == (640, 360)


def test_viewer_started_before_the_node_exists_still_connects_when_it_appears():
    """Regression (found in the S2 smoke run): on Windows a datagram sent to a port nobody listens on yields
    ConnectionResetError on the next recvfrom; the feed thread used to die on it and never connected."""
    port_block = 13040
    d = LP.LiveData()
    f = LP.Feeds(d, ("127.0.0.1", 8790 + port_block), ("127.0.0.1", 8792 + port_block), 0, None)
    f.start()
    try:
        assert not d.wait_for(lambda x: x.counts["sensor_data"] > 0, 2.5)        # at least two subscribe attempts hit a dead port
        with TwinCtx(port_block):
            assert d.wait_for(lambda x: x.counts["sensor_chunk"] >= 2 and x.counts["sensor_data"] >= 10, 10.0)
    finally:
        f.stop()
