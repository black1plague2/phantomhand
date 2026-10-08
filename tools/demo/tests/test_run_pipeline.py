"""Tests for tools/demo/run_pipeline.py's MEANING assertions.

The 2026-09-19 full-pipeline run found five defects that every earlier test missed,
because the contracts check shape, not meaning -- every broken session still passed
`contracts/validate.py`. These tests corrupt a real, schema-valid session in exactly
those five ways and prove `check_session_meaning` FAILS on each one. A check that
cannot fail is not a check.

Run with the analytics venv (run_pipeline's simulator imports are soft, so these
pure functions import fine without aiohttp):

    analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_run_pipeline.py -q
"""
from __future__ import annotations

import json
import shutil
from pathlib import Path

import pytest

from conftest import HEALTHY_FIXTURE, REPO_ROOT  # noqa: F401
from run_pipeline import (
    Checks,
    build_parser,
    check_session_meaning,
    kin_time_range,
    load_events,
    session_rate_hz,
)

REAL_SESSION = REPO_ROOT / "app" / ".hub_data" / "c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7"


@pytest.fixture
def session(tmp_path: Path) -> Path:
    """A working copy of the real recorded session (the user's own hands, 6 trials)."""
    if not REAL_SESSION.exists():
        pytest.skip(f"recorded session not present: {REAL_SESSION}")
    dst = tmp_path / "session"
    shutil.copytree(REAL_SESSION, dst)
    return dst


def failed_descriptions(checks: Checks) -> str:
    return " | ".join(d for _, _, d in checks.failures)


# --------------------------------------------------------------- shape readers
def test_kin_time_range_reads_the_real_chunk_layout(session: Path) -> None:
    """kin chunks put t_ms at the TOP level as a parallel array, not on per-sample
    objects. Reading it wrongly silently yields None and the clock check is skipped
    -- which is how a 'two different clocks' bug survives."""
    rng = kin_time_range(session)
    assert rng is not None
    lo, hi = rng
    assert lo >= 0
    assert hi > lo
    assert hi > 1000  # a real session is seconds long, not milliseconds


def test_session_rate_hz_reads_device_tracking_rate(session: Path) -> None:
    rate = session_rate_hz(session)
    assert rate == pytest.approx(72.0)


def test_real_session_passes_every_meaning_check(session: Path) -> None:
    checks = Checks()
    check_session_meaning(session, checks)
    assert checks.ok, failed_descriptions(checks)


# --------------------------------------------------------------- the five defects
def rewrite_events(session: Path, keep) -> None:
    events = load_events(session)
    kept = [e for e in events if keep(e)]
    assert len(kept) < len(events), "corruption fixture changed nothing"
    (session / "events.ndjson").write_text(
        "".join(json.dumps(e) + "\n" for e in kept), encoding="utf-8"
    )


def test_defect_1_no_kinematics_is_caught(session: Path) -> None:
    """Defect #1: the game recorded no kinematics at all, so no session was analysable."""
    for chunk in session.glob("kin_*.json"):
        chunk.unlink()
    checks = Checks()
    check_session_meaning(session, checks)
    assert not checks.ok
    assert "kinematics recorded" in failed_descriptions(checks)


def test_defect_2_missing_trial_end_is_caught(session: Path) -> None:
    """Defect #2: trial_end was never emitted on the successful path, so every trial
    was unclosed and every metric null -- and the files still validated."""
    rewrite_events(session, lambda e: e.get("type") != "trial_end")
    checks = Checks()
    check_session_meaning(session, checks)
    assert not checks.ok
    assert "trial_start AND trial_end" in failed_descriptions(checks)


def test_defect_3_missing_contact_is_caught(session: Path) -> None:
    """Defect #3: contact was never emitted, which analytics gates all movement
    metrics on."""
    rewrite_events(session, lambda e: e.get("type") != "contact")
    checks = Checks()
    check_session_meaning(session, checks)
    assert not checks.ok
    assert "has a contact event" in failed_descriptions(checks)


def test_defect_4_events_on_a_different_clock_is_caught(session: Path) -> None:
    """Defect #4: trial events and kinematics ran on two different clocks. Shift every
    event a day into the future -- still perfectly schema-valid, still meaningless."""
    events = load_events(session)
    day_ms = 24 * 60 * 60 * 1000.0
    for e in events:
        if e.get("t_ms") is not None:
            e["t_ms"] = e["t_ms"] + day_ms
    (session / "events.ndjson").write_text(
        "".join(json.dumps(e) + "\n" for e in events), encoding="utf-8"
    )
    checks = Checks()
    check_session_meaning(session, checks)
    assert not checks.ok
    assert "overlaps the kinematics clock" in failed_descriptions(checks)


def test_low_rate_hz_is_caught(session: Path) -> None:
    """rate_hz < 45 must be surfaced: SPARC/LDLJ are `degraded` below it (CLAUDE.md)."""
    env = json.loads((session / "session.json").read_text(encoding="utf-8"))
    env["device"]["tracking_rate_hz"] = 30.0
    (session / "session.json").write_text(json.dumps(env), encoding="utf-8")
    checks = Checks()
    check_session_meaning(session, checks)
    assert not checks.ok
    assert "rate_hz 30.0 >= 45" in failed_descriptions(checks)


# --------------------------------------------------------------- CLI contract
def test_cli_accepts_hub_ip_port_so_a1_never_edits_code() -> None:
    args = build_parser().parse_args(["--hub", "172.17.221.245:8787"])
    assert args.hub == "172.17.221.245:8787"
    host, _, port = args.hub.partition(":")
    assert host == "172.17.221.245"
    assert int(port) == 8787


def test_cli_defaults_bind_nothing_reserved() -> None:
    """Port discipline: no default in the parser may be 8787/8788/8790/8791/8797."""
    args = build_parser().parse_args([])
    assert args.hub is None            # -> ephemeral local hub
    assert args.sleeve_host is None    # -> local simulator
    assert args.sleeve_port is None    # -> ephemeral UDP
