"""Tests for tools/demo/watch_and_analyse.py.

Run with the analytics venv (it has jsonschema/referencing/numpy):

    analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_watch_and_analyse.py -v
"""
import json
import shutil
from pathlib import Path

import conftest  # noqa: F401 -- registers tools/demo on sys.path

import watch_and_analyse as wa

HEALTHY_FIXTURE = conftest.HEALTHY_FIXTURE


def _make_ended_session(dst: Path, ended: bool = True, with_kin: bool = True) -> Path:
    shutil.copytree(HEALTHY_FIXTURE, dst)
    metrics_path = dst / "metrics.json"
    if metrics_path.exists():
        metrics_path.unlink()  # simulate "not yet analysed"

    session_path = dst / "session.json"
    envelope = json.loads(session_path.read_text(encoding="utf-8"))
    envelope["ended_at"] = "2026-09-19T04:00:00Z" if ended else None
    session_path.write_text(json.dumps(envelope), encoding="utf-8")

    if not with_kin:
        for kin_file in dst.glob("kin_*.json"):
            kin_file.unlink()

    return dst


def test_is_session_ready_true_when_ended_with_events_and_kin(tmp_path):
    session_dir = _make_ended_session(tmp_path / "s1")
    assert wa.is_session_ready(session_dir) is True


def test_is_session_ready_false_when_not_ended(tmp_path):
    session_dir = _make_ended_session(tmp_path / "s2", ended=False)
    assert wa.is_session_ready(session_dir) is False


def test_is_session_ready_false_when_no_kin_chunks(tmp_path):
    session_dir = _make_ended_session(tmp_path / "s3", with_kin=False)
    assert wa.is_session_ready(session_dir) is False


def test_is_session_ready_false_once_metrics_exists(tmp_path):
    session_dir = _make_ended_session(tmp_path / "s4")
    (session_dir / "metrics.json").write_text("{}", encoding="utf-8")
    assert wa.is_session_ready(session_dir) is False


def test_watch_once_analyses_a_ready_session(tmp_path):
    watch_dir = tmp_path / "hub_data"
    watch_dir.mkdir()
    session_dir = _make_ended_session(watch_dir / "session-abc")

    processed: set[str] = set()
    wa.watch_once(watch_dir, processed)

    assert (session_dir / "metrics.json").exists()
    assert "session-abc" in processed

    summary = wa.summarize(session_dir)
    assert "trials" in summary
    assert "succeeded" in summary


def test_watch_once_is_idempotent(tmp_path, monkeypatch):
    watch_dir = tmp_path / "hub_data"
    watch_dir.mkdir()
    session_dir = _make_ended_session(watch_dir / "session-xyz")

    processed: set[str] = set()
    wa.watch_once(watch_dir, processed)
    metrics_mtime_1 = (session_dir / "metrics.json").stat().st_mtime_ns

    calls = []
    real_process = wa.process_session

    def spy(path):
        calls.append(path)
        return real_process(path)

    monkeypatch.setattr(wa, "process_session", spy)

    # Second poll: the session is already in `processed`, so process_session must not be called
    # again and metrics.json must be untouched.
    wa.watch_once(watch_dir, processed)
    assert calls == []
    assert (session_dir / "metrics.json").stat().st_mtime_ns == metrics_mtime_1

    # A *fresh* processed set (simulating a brand-new watcher process starting up) must also skip
    # re-analysis, because metrics.json already exists -- this is the idempotency guarantee that
    # matters across process restarts, not just within one.
    fresh_processed: set[str] = set()
    wa.watch_once(watch_dir, fresh_processed)
    assert calls == []
    assert "session-xyz" in fresh_processed


def test_watch_once_ignores_unready_sessions(tmp_path):
    watch_dir = tmp_path / "hub_data"
    watch_dir.mkdir()
    _make_ended_session(watch_dir / "still-running", ended=False)

    processed: set[str] = set()
    wa.watch_once(watch_dir, processed)

    assert processed == set()
    assert not (watch_dir / "still-running" / "metrics.json").exists()


def test_watch_once_on_missing_dir_does_not_raise(tmp_path):
    processed: set[str] = set()
    wa.watch_once(tmp_path / "does_not_exist", processed)
    assert processed == set()
