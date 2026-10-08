"""Tests for tools/demo/check_session.py.

Run with the analytics venv (it has jsonschema/referencing/numpy, and opus_analytics importable):

    analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_check_session.py -v
"""
import json
import shutil
from pathlib import Path

import conftest  # noqa: F401 -- registers tools/demo on sys.path

import check_session

HEALTHY_FIXTURE = conftest.HEALTHY_FIXTURE


def _copy_session(dst: Path) -> Path:
    shutil.copytree(HEALTHY_FIXTURE, dst)
    return dst


def test_healthy_session_passes(tmp_path, capsys):
    session_dir = _copy_session(tmp_path / "healthy_copy")

    exit_code = check_session.main([str(session_dir)])

    out = capsys.readouterr().out
    assert exit_code == 0
    assert "[FAIL]" not in out
    # every check should have actually run and reported something
    assert "trial_start and trial_end" in out
    assert "contact event" in out
    assert "kinematics range" in out
    assert "endpoint_error_cm" in out
    assert "rate_hz" in out
    assert "reaction time" in out


def test_missing_contact_events_fails(tmp_path, capsys):
    session_dir = _copy_session(tmp_path / "no_contact_copy")

    events_path = session_dir / "events.ndjson"
    lines = events_path.read_text(encoding="utf-8").splitlines()
    stripped = [line for line in lines if line.strip() and json.loads(line)["type"] != "contact"]
    assert len(stripped) < len(lines), "fixture had no contact events to strip -- test setup is broken"
    events_path.write_text("\n".join(stripped) + "\n", encoding="utf-8")

    # metrics.json from the original fixture is now stale relative to the stripped events; delete
    # it so check_session's schema check validates the events/session/kin we actually changed, not
    # a leftover metrics.json (which is unaffected by this edit anyway, but avoids confusion).
    metrics_path = session_dir / "metrics.json"
    if metrics_path.exists():
        metrics_path.unlink()

    exit_code = check_session.main([str(session_dir)])

    out = capsys.readouterr().out
    assert exit_code == 1
    assert "[FAIL]" in out
    assert "no contact event" in out

    # schema validity must still hold -- proving shape and meaning are independent checks, same as
    # analytics/tests/test_analysability.py's regression test for the same underlying defect class.
    assert "[PASS] schema validation" in out


def test_not_a_directory_fails_cleanly(tmp_path, capsys):
    missing = tmp_path / "does_not_exist"
    exit_code = check_session.main([str(missing)])
    out = capsys.readouterr().out
    assert exit_code == 1
    assert "[FAIL]" in out
    assert "is not a directory" in out
