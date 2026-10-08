"""MDC95 bands on neglect_index and trunk_lean_cm, plus per-trial trunk_lean_cm computation --
see VALIDATION.md section 7 for the test-retest derivation of the MDC95 constants."""
from __future__ import annotations

from synthetic_patients.generate import write_session
from opus_analytics.analyze import analyze_session
from opus_analytics.metrics.session_metrics import NEGLECT_INDEX_MDC95, TRUNK_LEAN_CM_MDC95


def test_session_neglect_index_carries_mdc95(tmp_path):
    out_dir = tmp_path / "left_neglect"
    write_session(out_dir, "left_neglect", seed=8, params={"trialCount": 16, "side": "alternate"})
    result = analyze_session(out_dir)
    ni = result["session"]["metrics"]["neglect_index"]
    assert ni["value"] is not None
    assert ni["mdc95"] == NEGLECT_INDEX_MDC95


def test_session_trunk_lean_cm_carries_mdc95_and_n(tmp_path):
    out_dir = tmp_path / "moderate"
    write_session(out_dir, "moderate", seed=8, params={"trialCount": 16})
    result = analyze_session(out_dir)
    lean = result["session"]["metrics"]["trunk_lean_cm"]
    assert lean["value"] is not None
    assert lean["mdc95"] == TRUNK_LEAN_CM_MDC95
    assert lean["n"] > 0


def test_per_trial_trunk_lean_cm_is_nonnegative_and_present(tmp_path):
    out_dir = tmp_path / "severe"
    write_session(out_dir, "severe", seed=4, params={"trialCount": 12})
    result = analyze_session(out_dir)
    seen_value = False
    for t in result["trials"]:
        lean = t["metrics"]["trunk_lean_cm"]
        assert "value" in lean
        if lean["value"] is not None:
            seen_value = True
            assert lean["value"] >= 0.0
    assert seen_value


def test_trunk_lean_cm_generally_larger_for_more_impaired_profile(tmp_path):
    """severe has a much larger trunk_compensation knob than healthy; the session-mean
    trunk_lean_cm should reflect that (matches the trunk_displacement_cm ordering in
    VALIDATION.md section 1.3)."""
    healthy_dir = tmp_path / "healthy"
    severe_dir = tmp_path / "severe"
    write_session(healthy_dir, "healthy", seed=13, params={"trialCount": 20})
    write_session(severe_dir, "severe", seed=13, params={"trialCount": 20})
    healthy_result = analyze_session(healthy_dir)
    severe_result = analyze_session(severe_dir)
    healthy_lean = healthy_result["session"]["metrics"]["trunk_lean_cm"]["value"]
    severe_lean = severe_result["session"]["metrics"]["trunk_lean_cm"]["value"]
    assert severe_lean > healthy_lean
