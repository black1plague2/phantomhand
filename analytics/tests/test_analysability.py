"""Regression coverage for the 2026-09-19 full-pipeline defect: a session missing `trial_end`
(or `contact`) passed `contracts/validate.py` cleanly while every single metric came back null
(see `logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md`, defect #2/#3). Schema validity checks
*shape*, not *analysability* -- nothing before this test encoded the difference.

Two properties are asserted:
  1. A well-formed session (every required event present) yields real, non-null values for the
     core per-trial and session-level metrics -- i.e. the pipeline can actually answer a
     clinical question, not just produce schema-shaped nulls.
  2. A session that is missing a required event (`contact`, which is what the real game bug
     dropped) is reported as `quality: "invalid"` with a clear, specific `quality_reasons` entry
     for every metric that depends on it -- silently-null-with-no-explanation is the failure
     mode this guards against -- and the file *still* validates against the schema, proving the
     two checks are genuinely independent (a consumer must check `quality`, not just call
     `contracts/validate.py` and assume the numbers are usable).
"""
from __future__ import annotations

import json

from synthetic_patients.generate import write_session

from opus_analytics.analyze import analyze_session
from opus_analytics.io_session import load_session
from opus_analytics.schemas import find_contracts_dir, validate_instance

# Metrics a clinician actually reads; these must not be silently null for a clean trial.
CORE_TRIAL_METRICS = (
    "reaction_time_ms", "movement_time_ms", "peak_speed_mps", "sparc", "ldlj",
    "endpoint_error_cm", "tracking_loss_pct",
)
CORE_SESSION_METRICS = ("rate_hz", "success_rate", "tracking_loss_pct")


def _event_schema():
    from opus_analytics import schemas
    return schemas.event_schema()


def test_well_formed_session_yields_non_null_core_metrics(tmp_path):
    """Sanity check for property 1: a clean, complete synthetic session (all required events
    present, low tracking loss) must produce real numbers -- not nulls -- for the metrics a
    clinician actually looks at, on the large majority of its trials."""
    out_dir = tmp_path / "healthy"
    write_session(out_dir, "healthy", seed=11, params={"trialCount": 16})
    result = analyze_session(out_dir)

    assert len(result["trials"]) > 0, "session produced no trials at all"

    for metric_id in CORE_TRIAL_METRICS:
        non_null = [t for t in result["trials"] if t["metrics"][metric_id]["value"] is not None]
        assert len(non_null) / len(result["trials"]) > 0.8, (
            f"{metric_id}: expected the large majority of trials in a clean session to have a "
            f"real value, got {len(non_null)}/{len(result['trials'])}"
        )
        # And where it IS non-null, quality should say so (not "ok" value with "invalid" flag).
        for t in non_null:
            assert t["metrics"][metric_id]["quality"] in ("ok", "degraded"), (
                f"trial {t['trial']} {metric_id} has a value but quality="
                f"{t['metrics'][metric_id]['quality']!r} -- a non-null value should never be "
                f"tagged invalid"
            )

    session_metrics = result["session"]["metrics"]
    for metric_id in CORE_SESSION_METRICS:
        assert session_metrics[metric_id]["value"] is not None, (
            f"session-level {metric_id} is null for a clean, complete session"
        )


def test_session_missing_contact_is_reported_invalid_not_silently_null(tmp_path):
    """Regression for the actual game bug: reproduce a session where every trial is missing
    `contact` (the real defect dropped it on the successful-placement path) by generating a
    normal session and stripping every `contact` event out of events.ndjson before analysis.

    Property asserted: every metric that needs `contact` comes back quality="invalid" with a
    specific, non-empty `quality_reasons` (not just a bare null), AND the resulting events.ndjson
    is still schema-valid -- proving schema validity alone would not have caught this."""
    src_dir = tmp_path / "healthy_src"
    write_session(src_dir, "healthy", seed=11, params={"trialCount": 10})

    broken_dir = tmp_path / "healthy_missing_contact"
    broken_dir.mkdir()
    for name in ("session.json",):
        (broken_dir / name).write_text((src_dir / name).read_text(encoding="utf-8"), encoding="utf-8")
    for kin_file in src_dir.glob("kin_*.json"):
        (broken_dir / kin_file.name).write_text(kin_file.read_text(encoding="utf-8"), encoding="utf-8")

    kept_events = []
    dropped_any = False
    with open(src_dir / "events.ndjson", "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            ev = json.loads(line)
            if ev.get("type") == "contact":
                dropped_any = True
                continue
            kept_events.append(ev)
    assert dropped_any, "fixture generator did not emit any contact events -- test setup is stale"

    with open(broken_dir / "events.ndjson", "w", encoding="utf-8") as f:
        for ev in kept_events:
            f.write(json.dumps(ev) + "\n")

    # The stripped events.ndjson must still be schema-valid, line by line -- this is the crux of
    # the regression: shape validity and analysability are independent properties.
    schema = _event_schema()
    for ev in kept_events:
        errs = validate_instance(ev, schema)
        assert errs == [], f"stripped event unexpectedly fails schema validation: {errs}"

    data = load_session(broken_dir, validate=True)
    assert data.is_valid, f"events.ndjson with contact removed must still pass contracts validation: {data.validation_errors}"

    result = analyze_session(broken_dir, validate=True)
    assert len(result["trials"]) > 0

    contact_dependent = tuple(m for m in CORE_TRIAL_METRICS if m not in ("reaction_time_ms",))
    for t in result["trials"]:
        for metric_id in contact_dependent:
            mv = t["metrics"][metric_id]
            assert mv["value"] is None, (
                f"trial {t['trial']} {metric_id} has a value {mv['value']!r} despite no `contact` "
                f"event in the session -- it should be unmeasurable"
            )
            assert mv["quality"] == "invalid", (
                f"trial {t['trial']} {metric_id} is null but quality={mv['quality']!r}, expected "
                f"'invalid' -- a null value must always be explained, not just silently absent"
            )
            reasons = mv.get("quality_reasons")
            assert reasons, (
                f"trial {t['trial']} {metric_id} is invalid but carries no quality_reasons -- "
                f"a consumer (or a future debugging session) has no way to know why"
            )
