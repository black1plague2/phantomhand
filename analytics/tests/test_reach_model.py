"""Known analytic cases: a pure minimum-jerk reach (no submovements, no tremor, no noise)
has exactly 1 submovement, matches the closed-form peak-speed formula, and lands in a known
SPARC smoothness range.
"""
import numpy as np
import pytest

from synthetic_patients.reach_model import generate_reach
from opus_analytics.kinematics import count_submovements
from opus_analytics.metrics.sparc import sparc
from opus_analytics.metrics.ldlj import log_dimensionless_jerk

FS = 72.0


def _pure_reach(distance_m=0.4, mt_s=0.8, seed=0):
    rng = np.random.default_rng(seed)
    p0 = np.array([0.0, 0.0, 0.0])
    p1 = np.array([distance_m, 0.0, 0.0])
    return generate_reach(
        p0=p0, p1_target=p1, reach_scale=1.0, mt_s=mt_s,
        n_submovements=1, submovement_frac=0.0, sample_rate_hz=FS,
        pre_onset_s=0.0, post_hold_s=0.0, tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0, rng=rng,
    )


def test_pure_minjerk_peak_speed_matches_closed_form():
    distance, mt = 0.4, 0.8
    reach = _pure_reach(distance, mt)
    expected_peak = 1.875 * distance / mt
    assert reach.truth["peak_speed_mps"] == pytest.approx(expected_peak, rel=0.02)


def test_pure_minjerk_time_to_peak_is_half_movement_time():
    reach = _pure_reach(0.4, 0.8)
    assert reach.truth["time_to_peak_speed_pct"] == pytest.approx(50.0, abs=3.0)


def test_pure_minjerk_has_one_submovement():
    reach = _pure_reach(0.4, 0.8)
    move_mask = (reach.t_s >= 0) & (reach.t_s <= 0.8)
    n = count_submovements(reach.speed[move_mask], FS)
    assert n == 1
    assert reach.truth["n_submovements"] == 1


def test_pure_minjerk_endpoint_matches_target():
    reach = _pure_reach(0.4, 0.8)
    assert reach.truth["endpoint_error_cm"] == pytest.approx(0.0, abs=1e-6)
    assert np.allclose(reach.truth["p1_actual"], reach.truth["p1_target"])


def test_pure_minjerk_sparc_in_known_smooth_range():
    """A single smooth bell-shaped speed profile should have a SPARC value close to that of
    the ideal minimum-jerk bell curve (reference implementations report roughly -1.5 to -2.5
    for a clean single-peaked reach at these durations/sample rates)."""
    reach = _pure_reach(0.4, 0.8)
    move_mask = (reach.t_s >= 0) & (reach.t_s <= 0.8)
    val = sparc(reach.speed[move_mask], FS)
    assert -3.0 < val < -1.0


def test_pure_minjerk_ldlj_is_finite_and_less_negative_than_jerky_reach():
    reach_smooth = _pure_reach(0.4, 0.8)
    move_mask = (reach_smooth.t_s >= 0) & (reach_smooth.t_s <= 0.8)
    ldlj_smooth = log_dimensionless_jerk(reach_smooth.speed[move_mask], FS)
    assert np.isfinite(ldlj_smooth)

    rng = np.random.default_rng(1)
    p0, p1 = np.array([0.0, 0.0, 0.0]), np.array([0.4, 0.0, 0.0])
    jerky = generate_reach(
        p0=p0, p1_target=p1, reach_scale=1.0, mt_s=0.8, n_submovements=5,
        submovement_frac=0.18, sample_rate_hz=FS, pre_onset_s=0.0, post_hold_s=0.0,
        tremor_hz_range=(6.0, 9.0), tremor_amp_m=0.004, rng=rng,
    )
    jerky_move_mask = (jerky.t_s >= 0) & (jerky.t_s <= 0.8)
    ldlj_jerky = log_dimensionless_jerk(jerky.speed[jerky_move_mask], FS)
    assert np.isfinite(ldlj_jerky)
    # smoother movement -> LDLJ closer to zero (less negative)
    assert ldlj_smooth > ldlj_jerky


def test_reach_scale_undershoot_creates_endpoint_error():
    reach = _pure_reach(0.4, 0.8)  # baseline, reach_scale=1.0 -> 0 error, already covered above
    rng = np.random.default_rng(2)
    p0, p1 = np.array([0.0, 0.0, 0.0]), np.array([0.4, 0.0, 0.0])
    undershoot = generate_reach(
        p0=p0, p1_target=p1, reach_scale=0.8, mt_s=0.8, n_submovements=1,
        submovement_frac=0.0, sample_rate_hz=FS, pre_onset_s=0.0, post_hold_s=0.0,
        tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0, rng=rng,
    )
    assert undershoot.truth["endpoint_error_cm"] == pytest.approx(0.2 * 0.4 * 100, rel=0.01)
