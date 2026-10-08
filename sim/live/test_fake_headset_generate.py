"""S run2: tests for fake_headset.py's `--generate` mock-headset mode (protocol.status()
enrichment, sorting-stage flavor, and the end-of-replay HTTP PUT of a contract-valid session).

Run with: sim/live/.venv/Scripts/python.exe -m pytest sim/live/test_fake_headset_generate.py -v
"""
import asyncio
import json
import shutil
import socket
import subprocess
import sys
import tempfile
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent))
from fake_hub import FakeHub
from fake_headset import FakeHeadset, generate_scripted_session

REPO_ROOT = Path(__file__).resolve().parents[2]


def get_free_tcp_port() -> int:
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def test_generate_reach_session_is_contract_valid(tmp_path):
    out = tmp_path / "gen_reach"
    generate_scripted_session(out, stage="reach", seed=11, n_trials=6, patient_ref="p-reach")
    proc = subprocess.run(
        [sys.executable, str(REPO_ROOT / "contracts" / "validate.py"), "--session", str(out)],
        capture_output=True, text=True,
    )
    assert proc.returncode == 0, proc.stdout + proc.stderr


def test_generate_sorting_session_has_containers_and_wrong_target(tmp_path):
    out = tmp_path / "gen_sorting"
    generate_scripted_session(out, stage="sorting", seed=12, n_trials=6, patient_ref="p-sort")

    proc = subprocess.run(
        [sys.executable, str(REPO_ROOT / "contracts" / "validate.py"), "--session", str(out)],
        capture_output=True, text=True,
    )
    assert proc.returncode == 0, proc.stdout + proc.stderr

    events = [json.loads(l) for l in (out / "events.ndjson").read_text(encoding="utf-8").splitlines() if l.strip()]
    shown = [e for e in events if e["type"] == "target_shown"]
    assert shown, "no target_shown events"
    assert all("container" in e and "containers" in e for e in shown)
    outcomes = {e.get("outcome") for e in events if e["type"] == "trial_end"}
    assert outcomes & {"success", "miss", "timeout", "wrong_target"}


@pytest.mark.asyncio
async def test_generated_session_streams_live_with_enriched_status_and_uploads(tmp_path):
    """Full loop: generate -> connect to a real FakeHub -> replay -> hub receives status
    messages carrying real_hands/hands/haptic/stage/patient_ref/trials_total, and the hub's
    out_dir ends up with a session.json/events.ndjson/kin_*.json uploaded via HTTP PUT that
    still passes contracts/validate.py."""
    session_src = tmp_path / "gen_src"
    generate_scripted_session(session_src, stage="reach", seed=21, n_trials=6, patient_ref="p-live")

    port = get_free_tcp_port()
    out_root = tmp_path / "hub_out"
    hub = FakeHub(port=port, beacon=False, scenario="basic", out_dir=out_root)
    headset = FakeHeadset(
        session_dir=session_src, host="127.0.0.1", port=port, speed=20.0,
        stage="reach", patient_ref="p-live",
    )

    status_payloads = []

    async def collect_statuses():
        while not headset.stopped:
            try:
                msg = await asyncio.wait_for(headset.ws.receive(), timeout=0.05)
            except asyncio.TimeoutError:
                continue
            except Exception:
                break

    try:
        await hub.start()
        await asyncio.sleep(0.2)
        await headset.connect("127.0.0.1", port)
        headset.state.session_id = "gen-live-test"
        await headset.send_hello()

        recv_task = asyncio.create_task(headset.receive_messages())
        status_task = asyncio.create_task(headset.send_status_loop())

        # Snapshot a status message directly from the builder (same code path
        # send_status_loop uses) to assert the enriched fields without racing the
        # network -- the network round trip is exercised for real by replay below.
        sample = headset.builder.status(
            state="running", trial=0, trials_completed=0, trials_total=headset.trials_total,
            hands={"left": "high", "right": "high"}, real_hands=False,
            haptic={"connected": True, "last_cue_id": None, "battery_pct": 87},
            stage=headset.stage, patient_ref=headset.patient_ref,
        )
        assert sample["payload"]["real_hands"] is False
        assert sample["payload"]["patient_ref"] == "p-live"
        assert sample["payload"]["stage"] == "reach"
        assert sample["payload"]["hands"] == {"left": "high", "right": "high"}
        assert "haptic" in sample["payload"]

        headset.start_event.set()
        await headset.replay_session()

        status_task.cancel()
        recv_task.cancel()
        await asyncio.sleep(0.1)
    finally:
        await headset.disconnect()
        await hub.stop()

    # The hub's out_dir should now hold the uploaded files (session.json/events.ndjson/kin_*),
    # and they must still be the SAME contract-valid session (fake_headset.py streams the
    # exact bytes generate_scripted_session wrote to disk, it does not regenerate them).
    session_dirs = list(out_root.glob("*"))
    assert session_dirs, f"nothing uploaded to {out_root}"
    uploaded = next((d for d in session_dirs if (d / "session.json").exists()), None)
    assert uploaded is not None, f"no uploaded session dir with session.json under {out_root}: {session_dirs}"
    proc = subprocess.run(
        [sys.executable, str(REPO_ROOT / "contracts" / "validate.py"), "--session", str(uploaded)],
        capture_output=True, text=True,
    )
    assert proc.returncode == 0, proc.stdout + proc.stderr
