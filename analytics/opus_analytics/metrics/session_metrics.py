"""Session-level metric aggregation across trials."""
from __future__ import annotations

import numpy as np

from .. import quality as _quality

SESSION_METHOD_VERSION = "session_metrics_v0.1.0"

# MDC95 (Minimal Detectable Change, 95%% CI) constants, derived by
# analytics/scripts/derive_mdc.py: repeated same-profile synthetic sessions (different seeds,
# same simulated clinical state, 24 trials/session, 10 seeds/profile) give a test-retest SD;
# MDC95 = 1.96 * sqrt(2) * SD. Pooled (mean) across profiles (healthy/mild/moderate/severe/
# left_neglect) for one conservative constant per metric. See VALIDATION.md section 4 for the
# full per-profile table and re-run instructions.
NEGLECT_INDEX_MDC95 = 0.171
TRUNK_LEAN_CM_MDC95 = 0.202


def _mv(value, unit, method_version, q, extra=None):
    if isinstance(value, float) and np.isnan(value):
        value = None
    mv = {"value": value, "unit": unit, "method_version": method_version, "quality": q}
    if extra:
        mv.update(extra)
    return mv


def _convex_hull_area(points_xy: np.ndarray) -> float:
    if len(points_xy) < 3:
        return 0.0
    try:
        from scipy.spatial import ConvexHull
        return float(ConvexHull(points_xy).volume)  # 'volume' is the 2-D area for a 2-D hull
    except Exception:
        xmin, ymin = points_xy.min(axis=0)
        xmax, ymax = points_xy.max(axis=0)
        return float((xmax - xmin) * (ymax - ymin))


def compute_session_metrics(trials, trial_metrics_by_index: dict, fs: float | None = None) -> dict:
    out: dict = {}

    if fs is not None:
        rate_reasons = [f"rate_hz_{fs:.1f}_below_{_quality.RATE_HZ_MIN:.0f}"] if fs < _quality.RATE_HZ_MIN else None
        out["rate_hz"] = _mv(float(fs), "Hz", SESSION_METHOD_VERSION,
                              "degraded" if fs < _quality.RATE_HZ_MIN else "ok",
                              {"quality_reasons": rate_reasons} if rate_reasons else None)

    outcomes = [t.outcome for t in trials if t.outcome is not None]
    n_success = sum(1 for o in outcomes if o == "success")
    success_rate = (n_success / len(outcomes)) if outcomes else float("nan")
    out["success_rate"] = _mv(success_rate, "unitless", SESSION_METHOD_VERSION,
                               "ok" if outcomes else "invalid")

    # Workspace asymmetry / neglect index: success-rate asymmetry between right- and left-side
    # targets. MDC95 (see module docstring / VALIDATION.md sec 4) is attached whenever the value
    # itself is computed, so a consumer comparing two sessions can tell a real change from noise.
    left_out = [t.outcome == "success" for t in trials if t.target and t.target.get("azimuthDeg", 0) < 0 and t.outcome]
    right_out = [t.outcome == "success" for t in trials if t.target and t.target.get("azimuthDeg", 0) >= 0 and t.outcome]
    if left_out and right_out:
        sr_l = float(np.mean(left_out))
        sr_r = float(np.mean(right_out))
        neglect_index = sr_r - sr_l
        q = "ok" if (len(left_out) >= 3 and len(right_out) >= 3) else "degraded"
        extra = {"mdc95": NEGLECT_INDEX_MDC95}
    else:
        neglect_index = float("nan")
        q = "invalid"
        extra = None
    out["neglect_index"] = _mv(neglect_index, "unitless", SESSION_METHOD_VERSION, q, extra)

    # Session-level trunk-lean summary (mean of the per-trial trunk_lean_cm metric -- see
    # trial_metrics.py). MDC95 likewise attached only when the value itself is computed.
    lean_vals = []
    for t in trials:
        m = trial_metrics_by_index.get(t.index, {}).get("trunk_lean_cm")
        if m and m["value"] is not None and m["quality"] != "invalid":
            lean_vals.append(m["value"])
    if lean_vals:
        out["trunk_lean_cm"] = _mv(float(np.mean(lean_vals)), "cm", SESSION_METHOD_VERSION, "ok",
                                    {"mdc95": TRUNK_LEAN_CM_MDC95, "n": len(lean_vals)})
    else:
        out["trunk_lean_cm"] = _mv(float("nan"), "cm", SESSION_METHOD_VERSION, "invalid")

    mts, idxs = [], []
    for t in trials:
        m = trial_metrics_by_index.get(t.index, {}).get("movement_time_ms")
        if m and m["value"] is not None and m["quality"] != "invalid":
            mts.append(m["value"])
            idxs.append(t.index)
    if len(mts) >= 4:
        slope = float(np.polyfit(idxs, mts, 1)[0])
        out["fatigue_slope"] = _mv(slope, "ms/trial", SESSION_METHOD_VERSION, "ok")
    else:
        out["fatigue_slope"] = _mv(float("nan"), "ms/trial", SESSION_METHOD_VERSION, "invalid")

    endpoints = []
    for t in trials:
        em = trial_metrics_by_index.get(t.index, {})
        if t.outcome == "success" and t.target and t.target.get("pos"):
            endpoints.append(t.target["pos"])
    if len(endpoints) >= 3:
        pts = np.array(endpoints)[:, [0, 2]]
        area = _convex_hull_area(pts)
        out["reach_envelope_area_m2"] = _mv(area, "m^2", SESSION_METHOD_VERSION, "ok")
    else:
        out["reach_envelope_area_m2"] = _mv(float("nan"), "m^2", SESSION_METHOD_VERSION, "invalid")

    loss_vals = []
    for t in trials:
        m = trial_metrics_by_index.get(t.index, {}).get("tracking_loss_pct")
        if m and m["value"] is not None:
            loss_vals.append(m["value"])
    session_loss = float(np.mean(loss_vals)) if loss_vals else float("nan")
    out["tracking_loss_pct"] = _mv(session_loss, "%", SESSION_METHOD_VERSION,
                                    "ok" if loss_vals else "invalid")

    return out
