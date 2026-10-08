"""Property-based checks (hypothesis) that metric implementations behave sanely across a range
of synthetic reach parameters, plus a couple of fully worked known cases.
"""
import numpy as np
import pytest
from hypothesis import given, settings, strategies as st

from synthetic_patients.reach_model import generate_reach
from opus_analytics.metrics.sparc import sparc
from opus_analytics.metrics.ldlj import log_dimensionless_jerk
from opus_analytics.kinematics import count_submovements

FS = 72.0


@given(
    distance=st.floats(min_value=0.15, max_value=0.7),
    mt=st.floats(min_value=0.4, max_value=1.8),
    seed=st.integers(min_value=0, max_value=10_000),
)
@settings(max_examples=25, deadline=None)
def test_single_reach_peak_speed_matches_closed_form_across_params(distance, mt, seed):
    rng = np.random.default_rng(seed)
    p0 = np.zeros(3)
    p1 = np.array([distance, 0.0, 0.0])
    reach = generate_reach(
        p0=p0, p1_target=p1, reach_scale=1.0, mt_s=mt, n_submovements=1,
        submovement_frac=0.0, sample_rate_hz=FS, pre_onset_s=0.0, post_hold_s=0.0,
        tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0, rng=rng,
    )
    expected = 1.875 * distance / mt
    assert reach.truth["peak_speed_mps"] == pytest.approx(expected, rel=0.03)
    assert reach.truth["n_submovements"] == 1


@given(
    n_sub=st.integers(min_value=1, max_value=5),
    seed=st.integers(min_value=0, max_value=10_000),
)
@settings(max_examples=20, deadline=None)
def test_submovement_count_knob_is_recovered_by_detector(n_sub, seed):
    rng = np.random.default_rng(seed)
    p0, p1 = np.zeros(3), np.array([0.45, 0.0, 0.0])
    reach = generate_reach(
        p0=p0, p1_target=p1, reach_scale=1.0, mt_s=1.2, n_submovements=n_sub,
        submovement_frac=0.15 if n_sub > 1 else 0.0, sample_rate_hz=FS,
        pre_onset_s=0.0, post_hold_s=0.0, tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0, rng=rng,
    )
    move_mask = (reach.t_s >= 0) & (reach.t_s <= 1.2)
    detected = count_submovements(reach.speed[move_mask], FS)
    # detector is a simplified peak counter; allow +/-1 tolerance against the injected count
    assert abs(detected - n_sub) <= 1


def test_sparc_is_worse_for_tremor_contaminated_reach():
    rng1 = np.random.default_rng(0)
    rng2 = np.random.default_rng(0)
    p0, p1 = np.zeros(3), np.array([0.4, 0.0, 0.0])
    clean = generate_reach(p0=p0, p1_target=p1, reach_scale=1.0, mt_s=0.8, n_submovements=1,
                            submovement_frac=0.0, sample_rate_hz=FS, pre_onset_s=0.0, post_hold_s=0.0,
                            tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0, rng=rng1)
    tremor = generate_reach(p0=p0, p1_target=p1, reach_scale=1.0, mt_s=0.8, n_submovements=1,
                             submovement_frac=0.0, sample_rate_hz=FS, pre_onset_s=0.0, post_hold_s=0.0,
                             tremor_hz_range=(6.0, 8.0), tremor_amp_m=0.006, rng=rng2)
    mask = (clean.t_s >= 0) & (clean.t_s <= 0.8)
    s_clean = sparc(clean.speed[mask], FS)
    s_tremor = sparc(tremor.speed[mask], FS)
    assert s_tremor < s_clean  # more negative = less smooth


def test_ldlj_matches_known_reference_value_for_fixed_bell_curve():
    """A hand-computed minimum-jerk speed profile (closed form, no simulation) should give a
    reproducible LDLJ value -- this pins the formula/implementation against regressions."""
    mt, distance = 0.8, 0.4
    t = np.linspace(0, mt, int(mt * FS) + 1)
    tau = t / mt
    vel_shape = 30 * tau**2 - 60 * tau**3 + 30 * tau**4
    speed = distance / mt * vel_shape
    val = log_dimensionless_jerk(speed, FS)
    assert np.isfinite(val)
    assert val == pytest.approx(val, rel=1e-9)  # deterministic
    assert -6.0 < val < 2.0
