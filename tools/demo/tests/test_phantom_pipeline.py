"""Tests for tools/demo/phantom_pipeline.py and the `--game phantom_hand` CLI of run_pipeline.py.

Real components, offset ports 14000+ (never 8787/8788/8790/8791): the twin as a subprocess (readiness = its ready
line), the real fake_hub, the real PhantomHeadset. A truncated session must come out NOT GREEN: a harness that
cannot fail is not a harness.

    analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_phantom_pipeline.py -q
"""
from __future__ import annotations

import argparse
import asyncio
import json
import sys
from pathlib import Path

import pytest

from conftest import REPO_ROOT

sys.path.insert(0, str(REPO_ROOT))
import phantom_pipeline as PP  # noqa: E402
import run_pipeline as RP  # noqa: E402
import l3_checks as L3  # noqa: E402


# ------------------------------------------------------------------ CLI
def test_cli_phantom_flags_parse_and_defaults_bind_nothing_reserved():
    a = RP.build_parser().parse_args(["--game", "phantom_hand", "--sim", "--no-unity", "--faults"])
    assert a.game == "phantom_hand" and a.sim and a.no_unity and a.faults and not a.hardware
    assert a.hub is None and a.port_offset == 31000
    assert a.hub_port == 0                                   # ephemeral unless asked
    for base in (8790, 8791, 8792, 8793):
        assert base + a.port_offset not in (8787, 8788, 8790, 8791)
    assert RP.build_parser().parse_args([]).game is None     # the OrchardReach behaviour is the default


def test_cli_hardware_and_spinup_flags():
    a = RP.build_parser().parse_args(["--game", "phantom_hand", "--hardware", "--spinup", "--a-ip", "192.168.1.5"])
    assert a.hardware and a.spinup and a.a_ip == "192.168.1.5" and a.discovery_port == 8791


def test_main_refuses_phantom_without_a_mode(monkeypatch, capsys):
    monkeypatch.setattr(sys, "argv", ["run_pipeline.py", "--game", "phantom_hand"])
    assert RP.main() == 2
    assert "--sim" in capsys.readouterr().err


# ------------------------------------------------------------------ spin-up measurement
def test_spike_delay_finds_first_deviation_after_the_send():
    s = [(1.00, 9.81), (1.01, 9.80), (1.02, 9.82), (1.03, 11.5), (1.04, 8.0)]
    assert PP.spike_delay_ms(s, t_send=1.015, base_mean=9.81, base_sd=0.02) == pytest.approx(15.0)
    assert PP.spike_delay_ms(s[:3], 1.0, 9.81, 0.02) is None                      # nothing moved: no spike
    assert PP.spike_delay_ms([(0.5, 14.0)], t_send=1.0, base_mean=9.81, base_sd=0.02) is None   # before the send


def test_measure_spinup_against_the_twin_sees_its_30ms_model(tmp_path):
    async def go():
        tw = PP.TwinProc("haptic", 14000, 3, tmp_path / "t.jsonl")
        try:
            await tw.wait_ready()
            return await PP.measure_spinup(("127.0.0.1", tw.ports["haptic"]), n=4), tw
        finally:
            await tw.stop()

    res, _ = asyncio.run(go())
    assert res["detected"] == 4 and all(t["acked"] for t in res["trials"])
    # twin models 30 ms spin-up; the IMU samples at 100 Hz and the clock ticks ~15 ms on Windows -> 25..70 ms
    assert 25.0 <= res["median_start_delay_ms"] <= 70.0, res


# ------------------------------------------------------------------ the harness itself
def test_twin_subprocess_is_ready_when_it_says_so_and_stops_on_quit(tmp_path):
    async def go():
        tw = PP.TwinProc("both", 14010, 1, tmp_path / "t.jsonl")
        ready = await tw.wait_ready()
        assert ready["ports"]["haptic"] == 8790 + 14010 and ready["kinds"] == ["haptic", "bio"]
        found = await PP.discover_nodes(tw.ports["discovery"], ("haptic", "bio"), 6.0)
        await tw.stop()
        return found, tw.proc.poll()

    found, rc = asyncio.run(go())
    assert set(found) == {"haptic", "bio"} and rc == 0


def truncated_fixture(tmp_path: Path) -> Path:
    from phantom_replay import DEFAULT_FIXTURE, prepare_session_copy
    full, _ = prepare_session_copy(DEFAULT_FIXTURE, tmp_path / "full")
    dst = tmp_path / "trunc"
    dst.mkdir()
    ev = [json.loads(l) for l in (full / "events.ndjson").read_text(encoding="utf-8").splitlines() if l.strip()]
    ev = [e for e in ev if e["t_ms"] <= 36000]
    (dst / "events.ndjson").write_text("".join(json.dumps(e) + "\n" for e in ev), encoding="utf-8")
    for f in ("session.json", "kin_000.json"):
        (dst / f).write_bytes((full / f).read_bytes())
    return dst


def test_a_truncated_run_goes_through_the_whole_harness_and_is_not_green(tmp_path):
    args = argparse.Namespace(out=str(tmp_path / "out"), hub=None, hub_port=0, hardware=False, no_unity=True,
                              port_offset=14020, seed=2, discovery_port=8791, fixture=str(truncated_fixture(tmp_path)),
                              ph_speed=1.0, compress_gap_ms=200.0)
    res = asyncio.run(PP.one_run(args, name="trunc"))
    rows = {r.check: r for r in res["rows"]}
    # the machinery worked: nodes discovered, cues acked by the twin, live statuses carried game_state, files landed
    ack = rows["Stroke cues acked by the twin"]
    assert "8/8 delivered" in ack.observed and "8 executed" in ack.observed      # every cue sent was acked by the twin ...
    assert not ack.ok and "only 8 stroke cues" in ack.observed                   # ... but 8 cues is not a full run
    assert rows["Live status RTT to hub"].ok, rows["Live status RTT to hub"].observed
    assert rows["Session valid and uploaded to the hub"].ok, rows["Session valid and uploaded to the hub"].observed
    assert rows["ASYNC delay"].ok
    # ... and the run was cut short: no sync block, no witness -> the table must say so
    assert not rows["Phases in order, both conditions"].ok
    assert not all(r.ok for r in res["rows"])
    assert (Path(args.out) / "trunc" / "status_records.json").exists()
    assert res["headset_report"]["cues_acked"] == res["headset_report"]["cues_sent"] > 0


def test_write_reports_emits_json_and_markdown(tmp_path):
    rows = [L3.Row("a", "t", "o", True), L3.Row("b", "t", "o", False)]
    PP.write_reports(tmp_path, [{"name": "x", "rows": rows, "fault_rows": [L3.Row("f", "t", "o", True, gating=False)]}])
    j = json.loads((tmp_path / "l3_report.json").read_text(encoding="utf-8"))
    assert j[0]["rows"][1]["ok"] is False and j[0]["fault_rows"][0]["gating"] is False
    assert "| FAIL |" in (tmp_path / "l3_report.md").read_text(encoding="utf-8")
