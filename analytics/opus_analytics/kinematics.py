"""Per-trial kinematic derivation: slicing joint traces, filtering, velocity/speed/jerk."""
from __future__ import annotations

import numpy as np

from .filtering import butterworth_lowpass


def slice_window(joint: dict, t_start_ms: float, t_end_ms: float) -> dict:
    t = joint["t_ms"]
    mask = (t >= t_start_ms) & (t <= t_end_ms)
    idx = np.where(mask)[0]
    if len(idx) == 0:
        return {"t_ms": np.array([]), "pos": np.zeros((0, 3)), "conf": np.array([])}
    return {"t_ms": t[idx], "pos": joint["pos"][idx], "conf": joint["conf"][idx]}


def tracking_loss_fraction(conf: np.ndarray) -> float:
    if len(conf) == 0:
        return 1.0
    return float(np.mean(conf <= 0.0))


def derive_kinematics(pos: np.ndarray, fs: float, filter_pos: bool = True) -> dict:
    """pos: (N,3) raw recorded positions, uniformly sampled at fs Hz.
    Returns filtered position, velocity (N,3), speed (N,), accel (N,3), jerk (N,3).
    """
    if pos.shape[0] < 3:
        z = np.zeros_like(pos)
        return {"pos": pos, "vel": z, "speed": np.zeros(pos.shape[0]), "accel": z, "jerk": z}

    filt = butterworth_lowpass(pos, fs) if filter_pos else pos
    dt = 1.0 / fs
    vel = np.gradient(filt, dt, axis=0)
    speed = np.linalg.norm(vel, axis=1)
    accel = np.gradient(vel, dt, axis=0)
    jerk = np.gradient(accel, dt, axis=0)
    return {"pos": filt, "vel": vel, "speed": speed, "accel": accel, "jerk": jerk}


def path_length(pos: np.ndarray) -> float:
    if pos.shape[0] < 2:
        return 0.0
    return float(np.sum(np.linalg.norm(np.diff(pos, axis=0), axis=1)))


def count_submovements(speed: np.ndarray, fs: float, min_prominence_frac: float = 0.10,
                        min_separation_s: float = 0.10) -> int:
    """Count local maxima in the speed profile that are separate submovements:
    a peak must be at least `min_prominence_frac` of the global peak speed and
    separated from the previous accepted peak by >= min_separation_s, with the
    speed dipping by at least the prominence threshold in between.
    """
    if len(speed) < 3:
        return 1 if len(speed) else 0
    peak_speed = float(np.max(speed))
    if peak_speed <= 1e-9:
        return 0
    thresh = peak_speed * min_prominence_frac
    min_sep = max(1, int(round(min_separation_s * fs)))

    # local maxima
    candidates = []
    for i in range(1, len(speed) - 1):
        if speed[i] >= speed[i - 1] and speed[i] >= speed[i + 1] and speed[i] >= thresh:
            candidates.append(i)
    if not candidates:
        return 1 if peak_speed > 0 else 0

    accepted = [candidates[0]]
    for c in candidates[1:]:
        last = accepted[-1]
        if c - last < min_sep:
            if speed[c] > speed[last]:
                accepted[-1] = c
            continue
        dip = np.min(speed[last:c + 1])
        if (min(speed[last], speed[c]) - dip) >= thresh * 0.5:
            accepted.append(c)
        elif speed[c] > speed[last]:
            accepted[-1] = c
    return len(accepted)
