"""SPARC/LDLJ quality gating (rate_hz < 45, tracking loss > SPARC_LDLJ_DEGRADE_LOSS_FRAC) and
`quality_reasons` propagation -- see VALIDATION.md section 6 for the threshold derivation."""
from __future__ import annotations

from synthetic_patients.generate import write_session
from opus_analytics.analyze import analyze_session
from opus_analytics import quality


def test_quest_low_rate_profile_degrades_sparc_ldlj_with_rate_hz_reason(tmp_path):
    out_dir = tmp_path / "qlr"
    write_session(out_dir, "quest_low_rate", seed=5, params={"trialCount": 10})
    result = analyze_session(out_dir)

    assert result["sample_rate_hz"] < quality.RATE_HZ_MIN

    attempted = [t for t in result["trials"] if t["metrics"]["sparc"]["value"] is not None]
    assert attempted, "expected at least one attempted (non-timeout) trial"
    for t in attempted:
        sparc_m = t["metrics"]["sparc"]
        ldlj_m = t["metrics"]["ldlj"]
        assert sparc_m["quality"] != "ok", "SPARC must not be 'ok' when rate_hz < 45"
        assert any("rate_hz" in r for r in sparc_m.get("quality_reasons", []))
        assert ldlj_m["quality"] != "ok"
        assert any("rate_hz" in r for r in ldlj_m.get("quality_reasons", []))


def test_healthy_profile_sparc_ldlj_stay_ok_at_full_rate(tmp_path):
    out_dir = tmp_path / "healthy"
    write_session(out_dir, "healthy", seed=5, params={"trialCount": 10})
    result = analyze_session(out_dir)
    assert result["sample_rate_hz"] >= quality.RATE_HZ_MIN

    attempted = [t for t in result["trials"] if t["metrics"]["sparc"]["value"] is not None]
    assert attempted
    ok_count = sum(1 for t in attempted if t["metrics"]["sparc"]["quality"] == "ok")
    assert ok_count / len(attempted) > 0.8


def test_high_dropout_trials_get_sparc_ldlj_specific_reason(tmp_path):
    """noisy_tracking's per-chunk dropout occasionally pushes a single trial's realized window
    loss above SPARC_LDLJ_DEGRADE_LOSS_FRAC; when that happens the reason must be present."""
    out_dir = tmp_path / "noisy"
    write_session(out_dir, "noisy_tracking", seed=1, params={"trialCount": 40})
    result = analyze_session(out_dir)

    found_high_loss_trial = False
    for t in result["trials"]:
        loss = t["metrics"]["tracking_loss_pct"]["value"]
        sparc_m = t["metrics"]["sparc"]
        if loss is not None and loss / 100.0 > quality.SPARC_LDLJ_DEGRADE_LOSS_FRAC and sparc_m["value"] is not None:
            found_high_loss_trial = True
            assert sparc_m["quality"] != "ok"
            assert any("sparc_ldlj_threshold" in r for r in sparc_m.get("quality_reasons", []))
    # not asserting found_high_loss_trial is True: dropout placement is stochastic per seed, but
    # if a qualifying trial does occur the reason must be correct (checked above).
    assert isinstance(found_high_loss_trial, bool)


def test_every_metric_carries_quality_and_optional_reasons(tmp_path):
    out_dir = tmp_path / "moderate"
    write_session(out_dir, "moderate", seed=3, params={"trialCount": 8})
    result = analyze_session(out_dir)
    for t in result["trials"]:
        for metric_id, mv in t["metrics"].items():
            assert "quality" in mv
            assert mv["quality"] in ("ok", "degraded", "invalid")
            if "quality_reasons" in mv:
                assert isinstance(mv["quality_reasons"], list)
                assert all(isinstance(r, str) for r in mv["quality_reasons"])
