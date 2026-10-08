"""Assemble a full Orchard Reach session: trials -> events.ndjson + kin_###.json chunks + truth.json."""
from __future__ import annotations

import json
import uuid
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .orientation import home_quat, synth_wrist_quats
from .reach_model import generate_reach

SAMPLE_RATE_HZ = 72.0
DT_S = 1.0 / SAMPLE_RATE_HZ
CHUNK_SECONDS = 5.0
FRAMES_PER_CHUNK = int(round(SAMPLE_RATE_HZ * CHUNK_SECONDS))  # 360

JOINTS = ["head", "l_wrist", "r_wrist", "l_index_tip", "r_index_tip",
          "l_thumb_tip", "r_thumb_tip", "l_palm", "r_palm"]

# Joints that carry a synthesized `rot` quaternion (wrist + palm; requested by Opus/Unity for
# the demo hand mesh). Head and fingertips are position-only, as before.
ROT_JOINTS = ["l_wrist", "r_wrist", "l_palm", "r_palm"]

ARM_LENGTH_M = 0.60
SUCCESS_RADIUS_M = 0.08
CONTRACTS_VERSION = "0.1"
GENERATOR_VERSION = "0.1.0"


def shoulder_pos(side: str) -> np.ndarray:
    sign = -1.0 if side == "left" else 1.0
    return np.array([sign * 0.18, 0.0, 0.02])


def home_pos(side: str) -> np.ndarray:
    sign = -1.0 if side == "left" else 1.0
    return shoulder_pos(side) + np.array([sign * 0.03, -0.32, 0.08])


def target_pos(side: str, azimuth_deg: float, elevation_deg: float, reach_percent: float) -> np.ndarray:
    az = np.radians(azimuth_deg)
    el = np.radians(elevation_deg)
    d = np.array([np.sin(az) * np.cos(el), np.sin(el), np.cos(az) * np.cos(el)])
    return shoulder_pos(side) + (reach_percent / 100.0) * ARM_LENGTH_M * d


def _sample_target_params(rng: np.random.Generator, params: dict) -> tuple[float, float, float]:
    lo, hi = params.get("reachPercent", [50, 85])
    reach_pct = rng.uniform(lo, hi)
    lo, hi = params.get("azimuthRangeDeg", [-45, 45])
    az = rng.uniform(lo, hi)
    lo, hi = params.get("elevationRangeDeg", [0, 40])
    el = rng.uniform(lo, hi)
    return reach_pct, az, el


def _neglect_extra(neglect_bias: float, azimuth_deg: float) -> float:
    if neglect_bias == 0:
        return 0.0
    if neglect_bias < 0:
        side_factor = float(np.clip(-azimuth_deg / 45.0, 0.0, 1.0))
    else:
        side_factor = float(np.clip(azimuth_deg / 45.0, 0.0, 1.0))
    return abs(neglect_bias) * side_factor


def _baseline_mt_s(distance_m: float) -> float:
    return 0.30 + 0.85 * distance_m


@dataclass
class TrialResult:
    index: int
    side: str
    events: list  # list of dict, local t_ms relative to trial start
    tip: np.ndarray  # (N,3)
    thumb: np.ndarray
    wrist: np.ndarray
    palm: np.ndarray
    head_offset: np.ndarray  # (N,3) added on top of neutral head pos
    wrist_rot: np.ndarray  # (N,4) xyzw, active-hand wrist/palm orientation (see orientation.py)
    duration_s: float
    truth: dict


def _aperture_profile(n: int, dt: float, contact_idx: int, release_idx: int) -> np.ndarray:
    """Grasp aperture (m) between index tip and thumb tip: open -> closed (hold) -> open."""
    open_m, closed_m = 0.08, 0.012
    ap = np.full(n, open_m)
    close_span = max(1, int(round(0.15 / dt)))
    for i in range(n):
        if i < contact_idx - close_span:
            ap[i] = open_m
        elif i < contact_idx:
            frac = (i - (contact_idx - close_span)) / close_span
            ap[i] = open_m + (closed_m - open_m) * frac
        elif i < release_idx:
            ap[i] = closed_m
        elif i < release_idx + close_span:
            frac = (i - release_idx) / close_span
            ap[i] = closed_m + (open_m - closed_m) * frac
        else:
            ap[i] = open_m
    return ap


def generate_trial(index: int, side: str, params: dict, profile: dict,
                    fatigue_frac: float, rng: np.random.Generator,
                    iti_s: float) -> TrialResult:
    reach_pct, az, el = _sample_target_params(rng, params)
    p0 = home_pos(side)
    p1 = target_pos(side, az, el, reach_pct)
    distance = float(np.linalg.norm(p1 - p0))

    neglect_extra = _neglect_extra(profile["neglect_bias"], az)
    timeout_prob = float(np.clip(0.35 * profile["miss_rate"] + 0.3 * neglect_extra, 0.0, 0.35))
    is_timeout = rng.random() < timeout_prob

    rt_mean = profile["rt_ms_mean"] * (1 + neglect_extra * 1.2) * (1 + fatigue_frac)
    rt_ms = max(120.0, rng.normal(rt_mean, profile["rt_ms_sd"]))

    mt_scale_eff = profile["mt_scale"] * (1 + neglect_extra * 0.8) * (1 + 0.5 * fatigue_frac)
    mt_s = _baseline_mt_s(distance) * mt_scale_eff * rng.lognormal(0.0, 0.06)

    reach_scale_eff = profile["reach_scale"] * (1 - neglect_extra * 0.5) * (1 - 0.3 * fatigue_frac)
    reach_scale_eff = float(np.clip(reach_scale_eff, 0.15, 1.02))

    hold_ms = params.get("holdMs", 300)
    time_limit_ms = params.get("timeLimitMs", 15000)

    events: list[dict] = []
    events.append({"t_ms": 0.0, "type": "trial_start"})
    target_shown_t = 100.0
    events.append({"t_ms": target_shown_t, "type": "target_shown",
                   "target": {"id": f"fruit_{index}", "pos": p1.tolist(), "azimuthDeg": az,
                              "elevationDeg": el, "reachPercent": reach_pct}})

    if is_timeout:
        trial_end_t = target_shown_t + time_limit_ms
        events.append({"t_ms": trial_end_t, "type": "trial_end", "outcome": "timeout"})
        n = int(round((trial_end_t / 1000.0 + iti_s) / DT_S))
        tip = np.tile(p0, (n, 1))
        thumb = np.tile(p0 + np.array([0, 0, 0.02]), (n, 1))
        wrist = np.tile(p0, (n, 1))
        palm = np.tile(p0, (n, 1))
        head_offset = np.zeros((n, 3))
        wrist_rot = np.tile(home_quat(), (n, 1))
        truth = {
            "index": index, "side": side, "outcome": "timeout",
            "reaction_time_ms": None, "movement_time_ms": None,
            "peak_speed_mps": None, "time_to_peak_speed_pct": None,
            "n_submovements": None, "endpoint_error_cm": None,
            "path_length_ratio": None, "target_azimuth_deg": az,
            "target_elevation_deg": el, "target_reach_percent": reach_pct,
        }
        return TrialResult(index, side, events, tip, thumb, wrist, palm, head_offset, wrist_rot,
                            trial_end_t / 1000.0 + iti_s, truth)

    onset_t = target_shown_t + rt_ms
    events.append({"t_ms": onset_t, "type": "movement_onset", "hand": side})

    aim_err_std = profile["jitter_std_m"] * 1.5 + neglect_extra * 0.02
    aim_offset = rng.normal(0, aim_err_std, size=3)
    aim_offset -= np.dot(aim_offset, (p1 - p0) / (np.linalg.norm(p1 - p0) + 1e-9)) * (p1 - p0) / (np.linalg.norm(p1 - p0) + 1e-9)
    p1_aim = p1 + aim_offset

    post_hold_s = (hold_ms + 150) / 1000.0
    reach = generate_reach(
        p0=p0, p1_target=p1_aim, reach_scale=reach_scale_eff, mt_s=mt_s,
        n_submovements=profile["n_submovements"], submovement_frac=profile["submovement_frac"],
        sample_rate_hz=SAMPLE_RATE_HZ, pre_onset_s=0.0, post_hold_s=post_hold_s,
        tremor_hz_range=profile["tremor_hz_range"], tremor_amp_m=profile["tremor_amp_m"], rng=rng,
    )
    endpoint = reach.truth["p1_actual"]
    endpoint_error_cm = float(np.linalg.norm(np.array(endpoint) - p1) * 100.0)
    success = endpoint_error_cm <= SUCCESS_RADIUS_M * 100.0

    contact_t = onset_t + mt_s * 1000.0
    grasp_t = contact_t + 20.0
    release_t = contact_t + hold_ms
    placed_t = release_t + 50.0
    trial_end_t = placed_t + 50.0

    outcome = "success" if success else "miss"
    events.append({"t_ms": contact_t, "type": "contact", "hand": side})
    events.append({"t_ms": grasp_t, "type": "grasp", "hand": side})
    events.append({"t_ms": release_t, "type": "release", "hand": side})
    if success:
        events.append({"t_ms": placed_t, "type": "placed", "hand": side})
    events.append({"t_ms": trial_end_t, "type": "trial_end", "outcome": outcome})

    # return-to-home segment
    return_mt = 0.5
    ret = generate_reach(
        p0=np.array(endpoint), p1_target=p0, reach_scale=1.0, mt_s=return_mt,
        n_submovements=1, submovement_frac=0.0, sample_rate_hz=SAMPLE_RATE_HZ,
        pre_onset_s=0.0, post_hold_s=max(iti_s - return_mt, 0.05),
        tremor_hz_range=(0.0, 0.0), tremor_amp_m=0.0, rng=rng,
    )

    n_pre = int(round(onset_t / 1000.0 / DT_S))
    reach_frames = reach.pos
    return_frames = ret.pos
    n_total = n_pre + len(reach_frames) + len(return_frames)

    tip = np.tile(p0, (n_total, 1))
    tip[n_pre:n_pre + len(reach_frames)] = reach_frames
    tip[n_pre + len(reach_frames):n_pre + len(reach_frames) + len(return_frames)] = return_frames

    contact_idx = n_pre + int(round(mt_s / DT_S))
    release_idx = n_pre + int(round((mt_s * 1000.0 + (hold_ms if success else hold_ms * 0.3)) / 1000.0 / DT_S))
    release_idx = min(max(release_idx, contact_idx + 1), n_total - 1)
    aperture = _aperture_profile(n_total, DT_S, min(contact_idx, n_total - 1), release_idx)

    thumb_dir = np.array([0.0, 0.0, 1.0])
    thumb = tip + thumb_dir[None, :] * aperture[:, None]
    wrist = home_pos(side)[None, :] + (tip - home_pos(side)[None, :]) * 0.80
    palm = wrist * 0.4 + tip * 0.6

    # trunk compensation: horizontal drift of head toward the target, scaled by
    # how far beyond a "comfortable" 70% reach the target demands.
    comfortable_frac = 0.70
    excess = max(0.0, reach_pct / 100.0 - comfortable_frac) / max(1e-6, 1.0 - comfortable_frac)
    excess = float(np.clip(excess, 0.0, 1.0))
    amp = profile["trunk_compensation"] * excess * 0.12  # meters, max ~12cm at full knob+excess
    horiz_dir = np.array([np.sin(np.radians(az)), 0.0, np.cos(np.radians(az))])
    tip_progress = np.clip((tip - p0) @ (p1 - p0) / (np.dot(p1 - p0, p1 - p0) + 1e-9), 0.0, 1.0)
    head_offset = horiz_dir[None, :] * (amp * tip_progress)[:, None]
    trunk_displacement_cm = float(amp * 100.0)

    wrist_rot = synth_wrist_quats(
        wrist, aperture, trial_index=index,
        tremor_hz_range=profile["tremor_hz_range"], tremor_amp_m=profile["tremor_amp_m"], dt=DT_S,
    )

    truth = {
        "index": index, "side": side, "outcome": outcome,
        "reaction_time_ms": rt_ms, "movement_time_ms": mt_s * 1000.0,
        "peak_speed_mps": reach.truth["peak_speed_mps"],
        "time_to_peak_speed_pct": reach.truth["time_to_peak_speed_pct"],
        "n_submovements": reach.truth["n_submovements"],
        "endpoint_error_cm": endpoint_error_cm,
        "path_length_ratio": reach.truth["path_length_ratio"],
        "trunk_displacement_cm": trunk_displacement_cm,
        "target_azimuth_deg": az, "target_elevation_deg": el, "target_reach_percent": reach_pct,
    }
    return TrialResult(index, side, events, tip, thumb, wrist, palm, head_offset, wrist_rot,
                        n_total * DT_S, truth)


def _pick_side(side_param: str, index: int, rng: np.random.Generator) -> str:
    if side_param == "left":
        return "left"
    if side_param == "right":
        return "right"
    if side_param == "alternate":
        return "left" if index % 2 == 0 else "right"
    return "left" if rng.random() < 0.5 else "right"  # "both": random per trial


def generate_session(profile_name: str, profile: dict, seed: int, params: dict | None = None,
                      session_id: str | None = None, mode: str = "simulation",
                      started_at: str = "2026-01-01T09:00:00Z",
                      patient_ref: str | None = None) -> tuple[dict, list, dict, dict, list]:
    """Returns (session_envelope, events, joints_true {joint: (N,3) array}, truth,
    trial_frame_ranges, rot_true {joint: (N,4) xyzw array, wrist/palm only})."""
    rng = np.random.default_rng(seed)
    params = dict(params or {})
    params.setdefault("side", "alternate")
    params.setdefault("trialCount", 20)
    params.setdefault("reachPercent", [50, 85])
    params.setdefault("azimuthRangeDeg", [-45, 45])
    params.setdefault("elevationRangeDeg", [0, 40])
    params.setdefault("holdMs", 300)
    params.setdefault("timeLimitMs", 15000)

    session_id = session_id or str(uuid.uuid4())
    trial_count = params["trialCount"]
    iti_s = 1.0

    all_events: list[dict] = []
    joint_series: dict[str, list[np.ndarray]] = {j: [] for j in JOINTS}
    rot_series: dict[str, list[np.ndarray]] = {j: [] for j in ROT_JOINTS}
    truths = []
    trial_frame_ranges: list[dict] = []
    global_t_ms = 0.0
    seq = 0

    def _emit(t_ms: float, type_: str, **kw):
        nonlocal seq
        ev = {"t_ms": round(t_ms, 3), "seq": seq, "block": 0, **kw}
        ev["type"] = type_
        if "trial" not in ev:
            ev["trial"] = None
        all_events.append(ev)
        seq += 1

    static_left_home = home_pos("left")
    static_right_home = home_pos("right")
    head_base = np.array([0.0, 0.55, -0.02])

    n_calib = 4  # ~55.6ms of frames at 72Hz, held at calibration
    calib_ms = n_calib * DT_S * 1000.0
    for j in JOINTS:
        if j.endswith("wrist") or j.endswith("palm"):
            home = static_left_home if j.startswith("l_") else static_right_home
        elif j.endswith("index_tip") or j.endswith("thumb_tip"):
            home = (static_left_home if j.startswith("l_") else static_right_home) + np.array([0, 0, 0.02])
        else:
            home = head_base
        joint_series[j].append(np.tile(home, (n_calib, 1)))
    for j in ROT_JOINTS:
        rot_series[j].append(np.tile(home_quat(), (n_calib, 1)))
    frames_so_far = n_calib

    _emit(0.0, "session_start")
    _emit(0.0, "calibration_start")
    _emit(calib_ms * 0.8, "calibration_end")
    _emit(calib_ms, "block_start", block=0)
    global_t_ms = calib_ms

    for i in range(trial_count):
        side = _pick_side(params["side"], i, rng)
        fatigue_frac = profile["fatigue_rate"] * (i / max(1, trial_count - 1))
        tr = generate_trial(i, side, params, profile, fatigue_frac, rng, iti_s)
        for ev in tr.events:
            ev = dict(ev)
            ev["t_ms"] = ev["t_ms"] + global_t_ms
            ev["trial"] = i
            ev.setdefault("hand", None)
            ev.setdefault("outcome", None)
            _emit(ev.pop("t_ms"), ev.pop("type"), **ev)

        n = tr.tip.shape[0]
        active_wrist = f"{side[0]}_wrist"
        active_index = f"{side[0]}_index_tip"
        active_thumb = f"{side[0]}_thumb_tip"
        active_palm = f"{side[0]}_palm"
        idle_side = "right" if side == "left" else "left"
        idle_home = static_right_home if side == "left" else static_left_home

        for j in JOINTS:
            if j == active_index:
                joint_series[j].append(tr.tip)
            elif j == active_thumb:
                joint_series[j].append(tr.thumb)
            elif j == active_wrist:
                joint_series[j].append(tr.wrist)
            elif j == active_palm:
                joint_series[j].append(tr.palm)
            elif j == f"{idle_side[0]}_wrist" or j == f"{idle_side[0]}_palm":
                joint_series[j].append(np.tile(idle_home, (n, 1)))
            elif j in (f"{idle_side[0]}_index_tip", f"{idle_side[0]}_thumb_tip"):
                joint_series[j].append(np.tile(idle_home + np.array([0, 0, 0.02]), (n, 1)))
            elif j == "head":
                joint_series[j].append(head_base[None, :] + tr.head_offset)

        for j in ROT_JOINTS:
            if j == active_wrist or j == active_palm:
                rot_series[j].append(tr.wrist_rot)
            else:
                rot_series[j].append(np.tile(home_quat(), (n, 1)))

        truths.append(tr.truth)
        trial_frame_ranges.append({"index": i, "start": frames_so_far, "end": frames_so_far + n})
        frames_so_far += n
        global_t_ms += n * DT_S * 1000.0

    n_tail = 1
    tail_ms = n_tail * DT_S * 1000.0
    for j in JOINTS:
        last = joint_series[j][-1][-1]
        joint_series[j].append(np.tile(last, (n_tail, 1)))
    for j in ROT_JOINTS:
        last = rot_series[j][-1][-1]
        rot_series[j].append(np.tile(last, (n_tail, 1)))

    _emit(global_t_ms, "block_end", block=0)
    _emit(global_t_ms + tail_ms, "session_end")
    global_t_ms += tail_ms

    joints_concat = {j: np.concatenate(arrs, axis=0) for j, arrs in joint_series.items()}
    rot_concat = {j: np.concatenate(arrs, axis=0) for j, arrs in rot_series.items()}

    session_envelope = {
        "session_id": session_id,
        "contracts_version": CONTRACTS_VERSION,
        "patient_ref": patient_ref or f"synthetic-{profile_name}-{seed}",
        "program_ref": "synthetic-program",
        "mode": mode,
        "started_at": started_at,
        "ended_at": None,
        "end_reason": "completed",
        "device": {"model": "synthetic", "device_id": "sim-0", "os": "sim",
                   "tracking_rate_hz": profile.get("tracking_rate_hz", SAMPLE_RATE_HZ)},
        "versions": {"shell": "0.0.0-sim", "sdk": "0.0.0-sim", "games": {"orchard_reach": "0.1.0"}},
        "calibration": {
            "affected_side": "both" if profile.get("neglect_bias", 0) != 0 else "right",
            "dominant_side": "right",
            "arm_length_m": {"left": ARM_LENGTH_M, "right": ARM_LENGTH_M},
            "posture": "seated",
            "chest_reference": [0.0, 0.0, 0.0],
        },
        "blocks": [{
            "index": 0, "game_id": "orchard_reach", "game_version": "0.1.0",
            "params": params, "started_t_ms": 60.0, "ended_t_ms": global_t_ms, "completed": True,
        }],
        "chunks": int(np.ceil(joints_concat["head"].shape[0] / FRAMES_PER_CHUNK)),
    }

    truth = {
        "profile": profile_name,
        "seed": seed,
        "generator_version": GENERATOR_VERSION,
        "knobs": {k: (list(v) if isinstance(v, tuple) else v) for k, v in profile.items()},
        "sample_rate_hz": SAMPLE_RATE_HZ,
        "tracking_rate_hz": profile.get("tracking_rate_hz", SAMPLE_RATE_HZ),
        "trials": truths,
    }

    return session_envelope, all_events, joints_concat, truth, trial_frame_ranges, rot_concat
