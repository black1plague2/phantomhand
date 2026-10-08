"""Impairment profiles: knob values consumed by session.py.

Every knob maps directly to a mechanism in reach_model.py / session.py:
  rt_ms_mean/sd        - reaction time (target_shown -> movement_onset), ms, lognormal-ish
  mt_scale             - multiplier on baseline movement-time-vs-distance model
  reach_scale          - fraction of nominal target distance actually reached (<1 = undershoot)
  n_submovements       - number of speed-profile peaks (1 = single smooth reach)
  submovement_frac     - fraction of distance carried by each corrective submovement
  tremor_hz_range      - (lo, hi) Hz sinusoidal perpendicular jitter band
  tremor_amp_m         - tremor amplitude, meters
  trunk_compensation   - 0..1, scales head displacement toward target when reach is demanding
  jitter_std_m         - iid Gaussian sensor noise std, meters, added to *recorded* (not true) pos
  dropout_rate         - probability per 5s chunk that a short tracking-loss span occurs
  fatigue_rate         - fractional degradation of MT/peak-speed across a session (0..~0.3)
  neglect_bias         - -1..1, negative biases toward worse performance on left-side targets
  miss_rate            - baseline probability a trial ends in miss/timeout instead of success
  tracking_rate_hz     - device's effective joint-tracking update rate, Hz (default 72.0 =
                         Quest hand-tracking HIGH-ish; sim emulates a lower rate via zero-order
                         hold on the true trajectory before jitter/dropout are applied, so both
                         event timing and array length are unaffected -- only the *density* of
                         genuinely-new samples drops, which is what degrades SPARC/LDLJ)
"""
from __future__ import annotations

PROFILES: dict[str, dict] = {
    "healthy": dict(
        rt_ms_mean=250, rt_ms_sd=30, mt_scale=1.0, reach_scale=0.99,
        n_submovements=1, submovement_frac=0.0,
        tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0,
        trunk_compensation=0.05, jitter_std_m=0.0015,
        dropout_rate=0.0, fatigue_rate=0.0, neglect_bias=0.0, miss_rate=0.02,
        tracking_rate_hz=72.0,
    ),
    "mild": dict(
        rt_ms_mean=320, rt_ms_sd=40, mt_scale=1.25, reach_scale=0.95,
        n_submovements=2, submovement_frac=0.12,
        tremor_hz_range=(4.0, 6.0), tremor_amp_m=0.0010,
        trunk_compensation=0.15, jitter_std_m=0.0025,
        dropout_rate=0.01, fatigue_rate=0.05, neglect_bias=0.0, miss_rate=0.05,
        tracking_rate_hz=72.0,
    ),
    "moderate": dict(
        rt_ms_mean=420, rt_ms_sd=60, mt_scale=1.6, reach_scale=0.85,
        n_submovements=3, submovement_frac=0.16,
        tremor_hz_range=(5.0, 8.0), tremor_amp_m=0.0025,
        trunk_compensation=0.35, jitter_std_m=0.0035,
        dropout_rate=0.03, fatigue_rate=0.12, neglect_bias=0.0, miss_rate=0.10,
        tracking_rate_hz=72.0,
    ),
    "severe": dict(
        rt_ms_mean=600, rt_ms_sd=90, mt_scale=2.3, reach_scale=0.65,
        n_submovements=5, submovement_frac=0.16,
        tremor_hz_range=(6.0, 10.0), tremor_amp_m=0.0045,
        trunk_compensation=0.6, jitter_std_m=0.0045,
        dropout_rate=0.06, fatigue_rate=0.2, neglect_bias=0.0, miss_rate=0.22,
        tracking_rate_hz=72.0,
    ),
    "left_neglect": dict(
        rt_ms_mean=300, rt_ms_sd=40, mt_scale=1.2, reach_scale=0.9,
        n_submovements=2, submovement_frac=0.12,
        tremor_hz_range=(4.0, 6.0), tremor_amp_m=0.0015,
        trunk_compensation=0.2, jitter_std_m=0.003,
        dropout_rate=0.01, fatigue_rate=0.05, neglect_bias=-0.8, miss_rate=0.05,
        tracking_rate_hz=72.0,
    ),
    "noisy_tracking": dict(
        rt_ms_mean=260, rt_ms_sd=30, mt_scale=1.05, reach_scale=0.98,
        n_submovements=1, submovement_frac=0.0,
        tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0,
        trunk_compensation=0.05, jitter_std_m=0.014,
        dropout_rate=0.18, fatigue_rate=0.0, neglect_bias=0.0, miss_rate=0.05,
        tracking_rate_hz=72.0,
    ),
    "quest_low_rate": dict(
        # Same biomechanics as "healthy" -- isolates the effect of a lower device tracking
        # rate (Quest hand-tracking LOW mode, ~30Hz) from impairment/noise knobs, so the
        # rate_hz<45 quality gate is exercised on otherwise-clean data.
        rt_ms_mean=250, rt_ms_sd=30, mt_scale=1.0, reach_scale=0.99,
        n_submovements=1, submovement_frac=0.0,
        tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0,
        trunk_compensation=0.05, jitter_std_m=0.0015,
        dropout_rate=0.0, fatigue_rate=0.0, neglect_bias=0.0, miss_rate=0.02,
        tracking_rate_hz=30.0,
    ),
}

# Recovery targets for the 6-week longitudinal series: week-0 knobs equal the
# starting profile (moderate) and interpolate (RT/MT/reach/tremor/miss) toward
# the "healthy" knobs by week 6, with submovement count stepping down.
LONGITUDINAL_START = "moderate"
LONGITUDINAL_WEEKS = 6


def interpolated_profile(week: int, weeks: int = LONGITUDINAL_WEEKS,
                          start: str = LONGITUDINAL_START, end: str = "healthy") -> dict:
    a = PROFILES[start]
    b = PROFILES[end]
    frac = min(max(week / weeks, 0.0), 1.0)
    out = {}
    for key in a:
        va, vb = a[key], b[key]
        if key == "tremor_hz_range":
            out[key] = tuple(va[i] + (vb[i] - va[i]) * frac for i in range(2))
        elif isinstance(va, (int, float)) and isinstance(vb, (int, float)):
            out[key] = va + (vb - va) * frac
        else:
            out[key] = va
    out["n_submovements"] = max(1, round(a["n_submovements"] + (b["n_submovements"] - a["n_submovements"]) * frac))
    return out
