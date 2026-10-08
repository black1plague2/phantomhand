#!/usr/bin/env python3
"""tools/demo/check_session.py <session_dir>

"Is this session analysable and sane?" checker for a REAL recorded session -- the thing
`contracts/validate.py` cannot tell you, because it only checks shape, not meaning (see
`analytics/VALIDATION.md` section 12 and `logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md`: a session
can validate cleanly against every schema while being completely unanalysable, e.g. `contact` never
emitted at all).

Checks, in order:
  1. schema validation (`contracts/validate.py --session <dir>`)
  2. every trial has trial_start...trial_end, and every non-timeout trial has target_shown
  3. every successful trial has a contact event
  4. the recorded kinematics range overlaps every trial's [trial_start, trial_end] window
  5. endpoint_error_cm is plausible (< 25 cm) for every successful trial
  6. rate_hz >= 45
  7. no trial has an implausibly fast (< 50 ms) reaction time (a scripted-driver artifact, not a
     real reach -- see VALIDATION.md section 11)

Prints one `[PASS]`/`[WARN]`/`[FAIL]` line per check. Exit code is 0 if there is no FAIL (WARNs are
allowed -- e.g. "no successful trials" is not itself a failure), 1 otherwise.

Run with the analytics venv's interpreter (it has jsonschema/referencing/numpy and can import
opus_analytics directly):

    analytics/.venv/Scripts/python.exe tools/demo/check_session.py <session_dir>
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "analytics"))

from opus_analytics.io_session import load_session  # noqa: E402
from opus_analytics.metrics.trial_metrics import compute_trial_metrics  # noqa: E402
from opus_analytics.segmentation import segment_trials  # noqa: E402

RATE_HZ_MIN = 45.0
MIN_REACTION_TIME_MS = 50.0
MAX_PLAUSIBLE_ENDPOINT_ERROR_CM = 25.0


def _record(results: list[tuple[str, str]], level: str, message: str) -> None:
    results.append((level, message))
    print(f"[{level}] {message}")


def check_schema(results: list[tuple[str, str]], session_dir: Path) -> bool:
    proc = subprocess.run(
        [sys.executable, str(REPO_ROOT / "contracts" / "validate.py"), "--session", str(session_dir)],
        capture_output=True,
        text=True,
    )
    ok = proc.returncode == 0
    _record(results, "PASS" if ok else "FAIL", "schema validation (contracts/validate.py --session)")
    if not ok:
        for line in (proc.stdout + proc.stderr).strip().splitlines():
            print(f"  {line}")
    return ok


def check_trial_completeness(results: list[tuple[str, str]], trials) -> None:
    incomplete = [t.index for t in trials if t.t_trial_start is None or t.t_trial_end is None]
    if incomplete:
        _record(results, "FAIL", f"{len(incomplete)} trial(s) missing trial_start or trial_end: {incomplete}")
    else:
        _record(results, "PASS", f"all {len(trials)} trial(s) have trial_start and trial_end")

    missing_shown = [t.index for t in trials if t.outcome != "timeout" and t.t_target_shown is None]
    if missing_shown:
        _record(results, "FAIL", f"{len(missing_shown)} non-timeout trial(s) missing target_shown: {missing_shown}")
    else:
        _record(results, "PASS", "every non-timeout trial has target_shown")


def check_contact_on_success(results: list[tuple[str, str]], trials) -> None:
    successes = [t for t in trials if t.outcome == "success"]
    missing = [t.index for t in successes if t.t_contact is None]
    if not successes:
        _record(results, "WARN", "no successful trials to check contact events against")
    elif missing:
        _record(results, "FAIL", f"{len(missing)} successful trial(s) have no contact event: {missing}")
    else:
        _record(results, "PASS", f"all {len(successes)} successful trial(s) have a contact event")


def check_kin_overlap(results: list[tuple[str, str]], trials, joints: dict) -> None:
    if not joints:
        _record(results, "FAIL", "no kinematics joints loaded (no kin_*.json chunks?)")
        return

    any_joint = next(iter(joints.values()))
    kin_lo, kin_hi = float(any_joint["t_ms"].min()), float(any_joint["t_ms"].max())

    non_overlapping = [
        t.index
        for t in trials
        if t.t_trial_start is not None
        and t.t_trial_end is not None
        and (t.t_trial_end < kin_lo or t.t_trial_start > kin_hi)
    ]
    if non_overlapping:
        _record(
            results,
            "FAIL",
            f"{len(non_overlapping)} trial window(s) do not overlap the recorded kinematics range "
            f"[{kin_lo:.0f}, {kin_hi:.0f}] ms: {non_overlapping}",
        )
    else:
        _record(
            results,
            "PASS",
            f"every trial window overlaps the recorded kinematics range [{kin_lo:.0f}, {kin_hi:.0f}] ms",
        )


def check_endpoint_error(results: list[tuple[str, str]], trials, joints: dict, rate_hz: float) -> None:
    successes = [t for t in trials if t.outcome == "success"]
    if not successes:
        _record(results, "WARN", "no successful trials to check endpoint_error_cm against")
        return

    bad: list[tuple[int, float]] = []
    invalid: list[int] = []
    for t in successes:
        metrics = compute_trial_metrics(t, joints, rate_hz)
        value = metrics.get("endpoint_error_cm", {}).get("value")
        if value is None:
            invalid.append(t.index)
        elif value > MAX_PLAUSIBLE_ENDPOINT_ERROR_CM:
            bad.append((t.index, value))

    if bad:
        _record(
            results,
            "FAIL",
            f"{len(bad)} successful trial(s) have implausible endpoint_error_cm "
            f"(> {MAX_PLAUSIBLE_ENDPOINT_ERROR_CM:.0f} cm): {bad}",
        )
    elif invalid:
        _record(results, "WARN", f"{len(invalid)} successful trial(s) have no computable endpoint_error_cm: {invalid}")
    else:
        _record(
            results,
            "PASS",
            f"all {len(successes)} successful trial(s) have endpoint_error_cm <= "
            f"{MAX_PLAUSIBLE_ENDPOINT_ERROR_CM:.0f} cm",
        )


def check_rate_hz(results: list[tuple[str, str]], rate_hz: float) -> None:
    if rate_hz >= RATE_HZ_MIN:
        _record(results, "PASS", f"rate_hz {rate_hz:.1f} >= {RATE_HZ_MIN:.0f}")
    else:
        _record(results, "WARN", f"rate_hz {rate_hz:.1f} < {RATE_HZ_MIN:.0f} (SPARC/LDLJ will be gated degraded)")


def check_reaction_times(results: list[tuple[str, str]], trials) -> None:
    too_fast = [
        (t.index, t.reaction_time_ms)
        for t in trials
        if t.reaction_time_ms is not None and t.reaction_time_ms < MIN_REACTION_TIME_MS
    ]
    if too_fast:
        _record(
            results,
            "WARN",
            f"{len(too_fast)} trial(s) have reaction_time_ms < {MIN_REACTION_TIME_MS:.0f} ms "
            f"(scripted/demo-driver artifact, not a real reach): {too_fast}",
        )
    else:
        _record(results, "PASS", f"no implausibly fast (< {MIN_REACTION_TIME_MS:.0f} ms) reaction times")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session_dir", type=Path, help="path to a session directory")
    args = parser.parse_args(argv)
    session_dir: Path = args.session_dir

    results: list[tuple[str, str]] = []

    if not session_dir.is_dir():
        _record(results, "FAIL", f"{session_dir} is not a directory")
        return 1

    check_schema(results, session_dir)

    try:
        data = load_session(session_dir, validate=False)
    except Exception as exc:  # noqa: BLE001 -- report, don't crash, so later checks still show up as skipped
        _record(results, "FAIL", f"could not load session for meaning checks: {exc}")
        n_fail = sum(1 for level, _ in results if level == "FAIL")
        print(f"\n{'FAIL' if n_fail else 'PASS'}: {n_fail} failed, 0 warned, {len(results)} checks total")
        return 1

    trials = segment_trials(data.events)
    if not trials:
        _record(results, "FAIL", "no trials found in events.ndjson (no events carry a non-null 'trial')")
        n_fail = sum(1 for level, _ in results if level == "FAIL")
        print(f"\n{'FAIL' if n_fail else 'PASS'}: {n_fail} failed, 0 warned, {len(results)} checks total")
        return 1

    check_trial_completeness(results, trials)
    check_contact_on_success(results, trials)
    check_kin_overlap(results, trials, data.joints)
    check_endpoint_error(results, trials, data.joints, data.rate_hz)
    check_rate_hz(results, data.rate_hz)
    check_reaction_times(results, trials)

    n_fail = sum(1 for level, _ in results if level == "FAIL")
    n_warn = sum(1 for level, _ in results if level == "WARN")
    print(f"\n{'FAIL' if n_fail else 'PASS'}: {n_fail} failed, {n_warn} warned, {len(results)} checks total")
    return 1 if n_fail else 0


if __name__ == "__main__":
    sys.exit(main())
