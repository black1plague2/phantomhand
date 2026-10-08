"""Per-trial metric computation."""
from __future__ import annotations

import numpy as np

from .. import quality
from ..kinematics import count_submovements, derive_kinematics, path_length, slice_window, tracking_loss_fraction
from .ldlj import log_dimensionless_jerk
from .ldlj import METHOD_VERSION as LDLJ_VERSION
from .sparc import METHOD_VERSION as SPARC_VERSION
from .sparc import sparc

TRIAL_METHOD_VERSION = "trial_metrics_v0.1.0"
PAD_MS = 80.0


ALL_METRIC_IDS = (
    "reaction_time_ms", "movement_time_ms", "peak_speed_mps", "time_to_peak_speed_pct",
    "sparc", "ldlj", "n_submovements", "path_length_ratio", "endpoint_error_cm",
    "trunk_displacement_cm", "trunk_lean_cm", "tracking_loss_pct",
)


def _mv(value, unit, method_version, q, reasons=None):
    mv = {"value": (None if value is None or (isinstance(value, float) and np.isnan(value)) else value),
          "unit": unit, "method_version": method_version, "quality": q}
    if reasons:
        mv["quality_reasons"] = list(reasons)
    return mv


def _invalid_all(out: dict, metric_ids, reasons) -> None:
    for m in metric_ids:
        out[m] = _mv(None, "unitless", TRIAL_METHOD_VERSION, "invalid", reasons)


def compute_trial_metrics(trial, joints: dict, fs: float) -> dict:
    """Returns {metric_id: {value, unit, method_version, quality, quality_reasons?}} for one trial.

    `fs` is the session's declared tracking rate_hz (see io_session.load_session /
    kinematics-chunk.schema.json's `rate_hz`); it feeds both the Butterworth filter cutoff and
    the SPARC/LDLJ rate_hz<45 quality gate (quality.flag_smoothness_metric).
    """
    out: dict = {}

    if not trial.attempted:
        # timed out before ever moving: nothing to measure kinematically.
        _invalid_all(out, ALL_METRIC_IDS, ["trial_timed_out_before_movement_onset"])
        return out

    out["reaction_time_ms"] = _mv(trial.reaction_time_ms, "ms", TRIAL_METHOD_VERSION,
                                   "ok" if trial.reaction_time_ms is not None else "invalid",
                                   None if trial.reaction_time_ms is not None else ["missing_movement_onset"])

    events_complete = trial.t_contact is not None
    hand = trial.hand or "right"
    joint_key = f"{hand[0]}_index_tip"
    joint = joints.get(joint_key)

    rest = tuple(m for m in ALL_METRIC_IDS if m != "reaction_time_ms")
    if joint is None or trial.t_movement_onset is None or trial.t_contact is None:
        _invalid_all(out, rest, ["required_event_missing"])
        return out

    t0, t1 = trial.t_movement_onset, trial.t_contact
    win_padded = slice_window(joint, t0 - PAD_MS, t1 + PAD_MS)
    win_move = slice_window(joint, t0, t1)
    loss_frac_move = tracking_loss_fraction(win_move["conf"])
    q, _, reasons = quality.flag_for_window(win_move["conf"], events_complete)

    out["movement_time_ms"] = _mv(trial.movement_time_ms, "ms", TRIAL_METHOD_VERSION, q, reasons)
    out["tracking_loss_pct"] = _mv(round(loss_frac_move * 100.0, 3), "%", TRIAL_METHOD_VERSION,
                                    "ok" if events_complete else "invalid",
                                    None if events_complete else ["required_event_missing"])

    rest_kin = tuple(m for m in rest if m not in ("movement_time_ms", "tracking_loss_pct"))
    if win_padded["pos"].shape[0] < quality.MIN_SAMPLES:
        _invalid_all(out, rest_kin, [f"fewer_than_{quality.MIN_SAMPLES}_usable_samples"])
        return out

    kin = derive_kinematics(win_padded["pos"], fs)
    t_padded = win_padded["t_ms"]
    move_mask = (t_padded >= t0) & (t_padded <= t1)
    speed_move = kin["speed"][move_mask]
    pos_move = kin["pos"][move_mask]

    if len(speed_move) < quality.MIN_SAMPLES:
        _invalid_all(out, rest_kin, [f"fewer_than_{quality.MIN_SAMPLES}_usable_samples"])
        return out

    peak_idx = int(np.argmax(speed_move))
    peak_speed = float(speed_move[peak_idx])
    mt_s = (t1 - t0) / 1000.0
    time_to_peak_pct = float(100.0 * peak_idx / max(1, len(speed_move) - 1))

    out["peak_speed_mps"] = _mv(peak_speed, "m/s", TRIAL_METHOD_VERSION, q, reasons)
    out["time_to_peak_speed_pct"] = _mv(time_to_peak_pct, "%", TRIAL_METHOD_VERSION, q, reasons)

    sparc_q, sparc_reasons = quality.flag_smoothness_metric(q, reasons, loss_frac_move, fs)
    sparc_val = sparc(speed_move, fs)
    out["sparc"] = _mv(sparc_val, "unitless", SPARC_VERSION,
                        "invalid" if np.isnan(sparc_val) else sparc_q,
                        ["nan_result"] if np.isnan(sparc_val) else sparc_reasons)

    ldlj_val = log_dimensionless_jerk(speed_move, fs)
    out["ldlj"] = _mv(ldlj_val, "unitless", LDLJ_VERSION,
                       "invalid" if np.isnan(ldlj_val) else sparc_q,
                       ["nan_result"] if np.isnan(ldlj_val) else sparc_reasons)

    nsub = count_submovements(speed_move, fs)
    out["n_submovements"] = _mv(nsub, "count", TRIAL_METHOD_VERSION, q, reasons)

    straight = float(np.linalg.norm(pos_move[-1] - pos_move[0])) if len(pos_move) >= 2 else 0.0
    plen = path_length(pos_move)
    ratio = (plen / straight) if straight > 1e-6 else float("nan")
    out["path_length_ratio"] = _mv(ratio, "unitless", TRIAL_METHOD_VERSION,
                                    "invalid" if np.isnan(ratio) else q,
                                    ["zero_length_straight_line"] if np.isnan(ratio) else reasons)

    target = trial.target or {}
    target_pos = target.get("pos")
    if target_pos is not None and len(pos_move):
        endpoint = pos_move[-1]
        err_cm = float(np.linalg.norm(np.array(target_pos) - endpoint) * 100.0)
        out["endpoint_error_cm"] = _mv(err_cm, "cm", TRIAL_METHOD_VERSION, q, reasons)
    else:
        out["endpoint_error_cm"] = _mv(None, "cm", TRIAL_METHOD_VERSION, "invalid", ["no_target_position"])

    head = joints.get("head")
    if head is not None:
        head_win = slice_window(head, trial.t_target_shown or t0, t1)
        if head_win["pos"].shape[0] >= 2:
            baseline = head_win["pos"][0][[0, 2]]
            horiz = head_win["pos"][:, [0, 2]] - baseline[None, :]
            disp_cm = float(np.max(np.linalg.norm(horiz, axis=1)) * 100.0)
            out["trunk_displacement_cm"] = _mv(disp_cm, "cm", TRIAL_METHOD_VERSION, q, reasons)
        else:
            out["trunk_displacement_cm"] = _mv(None, "cm", TRIAL_METHOD_VERSION, "invalid",
                                                ["insufficient_head_samples"])
    else:
        out["trunk_displacement_cm"] = _mv(None, "cm", TRIAL_METHOD_VERSION, "invalid", ["no_head_joint"])

    # Per-trial trunk lean: max head displacement *toward the target* (a signed, directional
    # projection onto the home->target horizontal bearing), relative to head position at trial
    # start -- distinct from trunk_displacement_cm above, which is the max *radial* (any-direction)
    # displacement from a target_shown baseline. "Toward the target" is what a real-time lean
    # vignette / haptic cue (docs/IMPROVEMENT_BRIEF.md sec 1/2) needs to gate on.
    target_az = target.get("azimuthDeg") if target else None
    if head is not None and target_az is not None:
        lean_win = slice_window(head, trial.t_trial_start or t0, t1)
        if lean_win["pos"].shape[0] >= 2:
            baseline = lean_win["pos"][0][[0, 2]]
            horiz = lean_win["pos"][:, [0, 2]] - baseline[None, :]
            bearing = np.array([np.sin(np.radians(target_az)), np.cos(np.radians(target_az))])
            toward_target_m = horiz @ bearing
            lean_cm = float(max(0.0, np.max(toward_target_m)) * 100.0)
            out["trunk_lean_cm"] = _mv(lean_cm, "cm", TRIAL_METHOD_VERSION, q, reasons)
        else:
            out["trunk_lean_cm"] = _mv(None, "cm", TRIAL_METHOD_VERSION, "invalid",
                                        ["insufficient_head_samples"])
    else:
        out["trunk_lean_cm"] = _mv(None, "cm", TRIAL_METHOD_VERSION, "invalid",
                                    ["no_head_joint_or_target_azimuth"])

    return out
