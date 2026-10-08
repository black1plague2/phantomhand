"""6-week longitudinal series: one session per week, knobs interpolating from moderate -> healthy."""
from __future__ import annotations

import json
from pathlib import Path

from .generate import write_session
from .profiles import LONGITUDINAL_WEEKS, interpolated_profile


def default_patient_ref(base_seed: int = 9000) -> str:
    """The one patient id a longitudinal series belongs to.

    Derived from `base_seed` so the series id can never drift from the sessions it
    contains. It used to be a hardcoded `"synthetic-longitudinal-001"` default while
    callers wrote the sessions under a seed-derived id, so `summary.json` claimed a
    patient that none of its own sessions belonged to (observed on disk: every
    session.json said `synthetic-longitudinal-9000`, summary.json said
    `synthetic-longitudinal-001`). Any code joining the series to a patient -- the
    app's recovery trend charts, for one -- then silently found nothing.
    """
    return f"synthetic-longitudinal-{base_seed}"


def write_longitudinal_series(out_dir: Path, base_seed: int = 9000,
                               patient_ref: str | None = None,
                               sessions_per_week: int = 2, trials_per_session: int = 16) -> dict:
    """Writes week_00 .. week_06, each `sessions_per_week` sessions, into out_dir.
    Returns a summary dict (also written as summary.json) with per-week mean truth values,
    useful for checking the recovery trend end to end.

    Every session AND summary.json carry the same `patient_ref` (a longitudinal series
    is by definition one patient over time). This is verified by reading every
    session.json back off disk before returning -- see the RuntimeError below.
    """
    patient_ref = patient_ref or default_patient_ref(base_seed)
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    weekly_summaries = []

    for week in range(LONGITUDINAL_WEEKS + 1):
        profile = interpolated_profile(week)
        week_dir = out_dir / f"week_{week:02d}"
        session_truths = []
        for s in range(sessions_per_week):
            seed = base_seed + week * 100 + s
            session_dir = week_dir / f"session_{s}"
            started_at = f"2026-01-{1 + week * 7 + s:02d}T09:00:00Z"
            truth = write_session(
                session_dir, profile_name="longitudinal", seed=seed,
                params={"trialCount": trials_per_session, "side": "alternate"},
                profile_override=profile, started_at=started_at,
                patient_ref=patient_ref,
            )
            session_truths.append(truth)

        succ_trials = [t for s in session_truths for t in s["trials"] if t["outcome"] == "success"]
        summary = {
            "week": week,
            "knobs": {k: (list(v) if isinstance(v, tuple) else v) for k, v in profile.items()},
            "n_sessions": sessions_per_week,
            "n_success_trials": len(succ_trials),
            "mean_movement_time_ms": _mean([t["movement_time_ms"] for t in succ_trials]),
            "mean_reaction_time_ms": _mean([t["reaction_time_ms"] for t in succ_trials]),
            "mean_peak_speed_mps": _mean([t["peak_speed_mps"] for t in succ_trials]),
            "mean_endpoint_error_cm": _mean([t["endpoint_error_cm"] for t in succ_trials]),
        }
        weekly_summaries.append(summary)

    # Read every session back off disk and prove the series really is one patient.
    # Writing the files and trusting the arguments is what let the split happen in
    # the first place; this check is cheap and it bites.
    mismatched = {}
    for session_json in sorted(out_dir.glob("week_*/session_*/session.json")):
        written = json.loads(session_json.read_text(encoding="utf-8")).get("patient_ref")
        if written != patient_ref:
            mismatched[str(session_json.relative_to(out_dir))] = written
    if mismatched:
        raise RuntimeError(
            f"longitudinal series at {out_dir} is not one patient: summary says "
            f"{patient_ref!r} but these sessions disagree: {mismatched}"
        )

    result = {"patient_ref": patient_ref, "weeks": weekly_summaries}
    with open(out_dir / "summary.json", "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)
    return result


def _mean(xs):
    xs = [x for x in xs if x is not None]
    return sum(xs) / len(xs) if xs else None
