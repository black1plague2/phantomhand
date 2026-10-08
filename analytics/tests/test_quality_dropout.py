"""Quality flag must degrade/invalidate under tracking dropout, and stay ok when tracking is clean."""
from synthetic_patients.generate import write_session
from opus_analytics.analyze import analyze_session


def test_healthy_profile_mostly_ok_quality(tmp_path):
    out_dir = tmp_path / "healthy"
    write_session(out_dir, "healthy", seed=7, params={"trialCount": 12})
    result = analyze_session(out_dir)

    ok_count = 0
    total = 0
    for trial in result["trials"]:
        mt = trial["metrics"]["movement_time_ms"]
        if mt["value"] is not None:
            total += 1
            if mt["quality"] == "ok":
                ok_count += 1
    assert total > 0
    assert ok_count / total > 0.8, "healthy profile (near-zero dropout) should mostly report ok quality"


def test_noisy_tracking_profile_degrades_quality(tmp_path):
    out_dir = tmp_path / "noisy"
    write_session(out_dir, "noisy_tracking", seed=42, params={"trialCount": 20})
    result = analyze_session(out_dir)

    non_ok = 0
    total = 0
    for trial in result["trials"]:
        mt = trial["metrics"]["movement_time_ms"]
        if mt["value"] is not None:
            total += 1
            if mt["quality"] != "ok":
                non_ok += 1
    assert total > 0
    assert non_ok > 0, "high-dropout profile should produce at least some degraded/invalid quality trials"


def test_tracking_loss_pct_reflects_dropout_rate(tmp_path):
    out_dir = tmp_path / "noisy2"
    write_session(out_dir, "noisy_tracking", seed=42, params={"trialCount": 20})
    result = analyze_session(out_dir)
    session_loss = result["session"]["metrics"]["tracking_loss_pct"]["value"]
    assert session_loss is not None
    assert session_loss > 0.5, "noisy_tracking has an 18% per-chunk dropout rate; session tracking loss should be clearly nonzero"


def test_timeout_trials_flagged_invalid(tmp_path):
    out_dir = tmp_path / "severe"
    write_session(out_dir, "severe", seed=99, params={"trialCount": 20})
    result = analyze_session(out_dir)
    timeouts = [t for t in result["trials"] if t["outcome"] == "timeout"]
    if timeouts:
        for t in timeouts:
            assert t["metrics"]["movement_time_ms"]["quality"] == "invalid"
