"""Guards the recorded ph_l3_* fixtures against going stale.

Each fixture is a COMPLETE session dir (session.json, events, kin/sens files) plus the metrics.json that analytics wrote
for it. Re-analyse a scratch copy with the CURRENT analytics and require the same metrics (except `computed_at`). When
analytics changes a threshold or a flag, this goes red until the fixtures are re-recorded:
    python tools/demo/run_pipeline.py --game phantom_hand --sim --no-unity --faults --with-main --out <dir>
"""
from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

import pytest

import phantom_pipeline as PP

FIXROOT = Path(__file__).resolve().parent / "fixtures"
NAMES = ["ph_l3_main", "ph_l3_fault_node_a_off", "ph_l3_fault_node_b_absent", "ph_l3_fault_hub_absent"]
VOLATILE = {"computed_at"}


def _strip(d):
    return {k: v for k, v in d.items() if k not in VOLATILE}


def _analyse(src: Path, dst: Path) -> dict:
    shutil.copytree(src, dst)
    (dst / "metrics.json").unlink()
    proc = subprocess.run([str(PP.ANALYTICS_PY), "-m", "opus_analytics", str(dst), "--summary"],
                          capture_output=True, text=True, cwd=str(PP.ANALYTICS_DIR))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    return json.loads((dst / "metrics.json").read_text(encoding="utf-8"))


@pytest.mark.parametrize("name", NAMES)
def test_fixture_is_a_complete_session(name):
    d = FIXROOT / name
    assert (d / "session.json").exists() and (d / "events.ndjson").exists() and (d / "metrics.json").exists()
    assert any(d.glob("sens_*.json")), "fixture has no sensor files"


@pytest.mark.parametrize("name", NAMES)
def test_stored_metrics_equal_current_analytics(name, tmp_path):
    src = FIXROOT / name
    stored = json.loads((src / "metrics.json").read_text(encoding="utf-8"))
    fresh = _analyse(src, tmp_path / "copy")
    assert _strip(fresh) == _strip(stored), f"{name}: metrics.json is stale - re-record the fixtures (see module docstring)"


def test_staleness_guard_detects_a_stale_metrics_file(tmp_path):
    """The guard itself must be able to fail: a metrics.json with the old (un-flagged) delivery quality differs."""
    src = FIXROOT / "ph_l3_fault_node_a_off"
    stored = json.loads((src / "metrics.json").read_text(encoding="utf-8"))
    stale = json.loads(json.dumps(stored))
    stale["embodiment"]["sync"]["cue_delivery_rate"]["quality"] = "ok"
    stale["embodiment"]["sync"]["cue_delivery_rate"]["quality_reasons"] = []
    fresh = _analyse(src, tmp_path / "copy")
    assert _strip(fresh) != _strip(stale)
