"""Deterministic, schema-valid (xyzw, unit-norm) wrist/palm orientation synthesis.

Requested by Opus/Unity (2026-09-17): the demo hand mesh needs a recorded wrist rotation to
orient itself when there's no headset. This is NOT physically-measured forearm kinematics --
it's a plausible, smooth, seed-deterministic orientation so a demo hand visibly turns to face
the reach direction and pronates into the grasp. It feeds no opus_analytics metric (metrics
are all position-based) and is purely additive to the kinematics-chunk `rot` field, which the
schema already allows per-joint.

Convention: local +Z axis points along the direction of travel ("forward"), local +Y is
approximately world up before the twist is applied. A twist about the forward axis represents
pronation as the hand closes toward a grasp (tied to the existing grasp-aperture profile, so it
needs no extra state). A small sinusoidal wobble (using the same tremor band as position, but a
fixed deterministic phase per trial index -- no extra RNG draws, so adding this does not
perturb any other generator output's random sequence) adds visible tremor to the orientation
for impaired profiles.
"""
from __future__ import annotations

import numpy as np

HOME_FORWARD = np.array([0.0, -0.35, 1.0])
_UP = np.array([0.0, 1.0, 0.0])
MOVING_SPEED_EPS = 1e-4
PRONATION_DEG = 30.0
TREMOR_RAD_PER_M = 40.0
TREMOR_RAD_CAP = 0.2
TRIAL_PHASE_STEP = 2.39996323  # arbitrary irrational-ish constant, just decorrelates trials


def quat_normalize(q: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(q)
    if n < 1e-9:
        return np.array([0.0, 0.0, 0.0, 1.0])
    return q / n


def quat_from_axis_angle(axis: np.ndarray, angle_rad: float) -> np.ndarray:
    axis = np.asarray(axis, dtype=float)
    n = np.linalg.norm(axis)
    if n < 1e-9:
        return np.array([0.0, 0.0, 0.0, 1.0])
    axis = axis / n
    s = np.sin(angle_rad / 2.0)
    return np.array([axis[0] * s, axis[1] * s, axis[2] * s, np.cos(angle_rad / 2.0)])


def quat_mul(q1: np.ndarray, q2: np.ndarray) -> np.ndarray:
    x1, y1, z1, w1 = q1
    x2, y2, z2, w2 = q2
    return np.array([
        w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2,
        w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
        w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2,
        w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2,
    ])


def _mat_to_quat(m: np.ndarray) -> np.ndarray:
    tr = m[0, 0] + m[1, 1] + m[2, 2]
    if tr > 0:
        s = np.sqrt(tr + 1.0) * 2
        w = 0.25 * s
        x = (m[2, 1] - m[1, 2]) / s
        y = (m[0, 2] - m[2, 0]) / s
        z = (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = np.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        w = (m[2, 1] - m[1, 2]) / s
        x = 0.25 * s
        y = (m[0, 1] + m[1, 0]) / s
        z = (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = np.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        w = (m[0, 2] - m[2, 0]) / s
        x = (m[0, 1] + m[1, 0]) / s
        y = 0.25 * s
        z = (m[1, 2] + m[2, 1]) / s
    else:
        s = np.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        w = (m[1, 0] - m[0, 1]) / s
        x = (m[0, 2] + m[2, 0]) / s
        y = (m[1, 2] + m[2, 1]) / s
        z = 0.25 * s
    return quat_normalize(np.array([x, y, z, w]))


def look_quat(forward: np.ndarray, up: np.ndarray = _UP) -> np.ndarray:
    """Rotation whose local +Z axis points along `forward` (world-space)."""
    f = np.asarray(forward, dtype=float)
    nf = np.linalg.norm(f)
    f = f / nf if nf > 1e-9 else np.array([0.0, 0.0, 1.0])
    r = np.cross(up, f)
    nr = np.linalg.norm(r)
    if nr < 1e-6:
        # forward ~parallel to up: pick an arbitrary stable perpendicular
        r = np.cross(np.array([1.0, 0.0, 0.0]), f)
        nr = np.linalg.norm(r)
        if nr < 1e-6:
            r = np.array([1.0, 0.0, 0.0])
            nr = 1.0
    r = r / nr
    u = np.cross(f, r)
    m = np.array([[r[0], u[0], f[0]],
                  [r[1], u[1], f[1]],
                  [r[2], u[2], f[2]]])
    return _mat_to_quat(m)


def synth_wrist_quats(pos: np.ndarray, aperture: np.ndarray, trial_index: int,
                       tremor_hz_range: tuple[float, float], tremor_amp_m: float,
                       dt: float) -> np.ndarray:
    """pos: (N,3) the wrist's own recorded/true position trajectory (used for direction of
    travel). aperture: (N,) grasp-aperture profile (reused to drive pronation twist -- no
    extra state needed). Returns (N,4) unit quaternions, xyzw.
    """
    n = pos.shape[0]
    if n == 0:
        return np.zeros((0, 4))

    home_forward = HOME_FORWARD / np.linalg.norm(HOME_FORWARD)
    if n >= 2:
        vel = np.gradient(pos, axis=0)
    else:
        vel = np.zeros_like(pos)
    speed = np.linalg.norm(vel, axis=1)
    moving = speed > MOVING_SPEED_EPS

    forward = np.tile(home_forward, (n, 1))
    if moving.any():
        forward[moving] = vel[moving] / speed[moving][:, None]
    # forward-fill through non-moving frames so orientation doesn't snap back to home mid-pause
    last = home_forward.copy()
    for i in range(n):
        if moving[i]:
            last = forward[i]
        else:
            forward[i] = last

    ap_lo, ap_hi = float(np.min(aperture)), float(np.max(aperture))
    ap_range = max(ap_hi - ap_lo, 1e-6)
    ap_norm = np.clip((aperture - ap_lo) / ap_range, 0.0, 1.0)  # 0 = closed, 1 = open

    tremor_hz = tremor_hz_range[1] if tremor_hz_range and tremor_hz_range[1] > 0 else 0.0
    tremor_rad = float(np.clip(tremor_amp_m * TREMOR_RAD_PER_M, 0.0, TREMOR_RAD_CAP))
    phase = trial_index * TRIAL_PHASE_STEP
    t_arr = np.arange(n) * dt

    quats = np.zeros((n, 4))
    for i in range(n):
        q = look_quat(forward[i])
        twist = np.radians(-PRONATION_DEG) * (1.0 - ap_norm[i])
        q = quat_mul(q, quat_from_axis_angle(forward[i], twist))
        if tremor_rad > 0 and tremor_hz > 0:
            angle = tremor_rad * np.sin(2 * np.pi * tremor_hz * t_arr[i] + phase)
            q = quat_mul(q, quat_from_axis_angle(_UP, angle))
        quats[i] = quat_normalize(q)
    return quats


def home_quat() -> np.ndarray:
    return quat_normalize(look_quat(HOME_FORWARD))
