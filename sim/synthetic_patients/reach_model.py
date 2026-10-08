"""Minimum-jerk reach model with impairment knobs.

Core model: Flash & Hogan (1985) minimum-jerk point-to-point trajectory.
For a 1-D displacement D over duration MT, the position shape function is

    s(tau) = 10*tau**3 - 15*tau**4 + 6*tau**5,   tau = clip(t/MT, 0, 1)

with peak (Euclidean) speed occurring at tau=0.5:

    v_peak = 1.875 * D / MT

A reach with submovements is modelled as the primary minimum-jerk segment
(carrying most of the displacement) plus N-1 additional, later, smaller
minimum-jerk velocity "bumps" along the same movement axis, overlapping in
time with the tail of the previous segment. This is a simplified but
standard way to synthesize submovement-decomposable reaches: the total
number of local maxima in the speed profile equals the number of
submovements by construction, which is the ground truth we later check
n_submovements against.
"""
from __future__ import annotations

import numpy as np
from dataclasses import dataclass, field


def minjerk_shape(tau: np.ndarray) -> np.ndarray:
    tau = np.clip(tau, 0.0, 1.0)
    return 10 * tau**3 - 15 * tau**4 + 6 * tau**5


def minjerk_vel_shape(tau: np.ndarray) -> np.ndarray:
    """d/dtau of minjerk_shape (NOT yet divided by duration)."""
    tau = np.clip(tau, 0.0, 1.0)
    return 30 * tau**2 - 60 * tau**3 + 30 * tau**4


@dataclass
class SubmovementPlan:
    """A chain of 1-D (along-axis) minimum-jerk velocity bumps summing to D."""

    total_distance_m: float
    total_time_s: float
    n_submovements: int
    submovement_frac: float  # fraction of D carried by *each* corrective submovement
    rng: np.random.Generator

    onsets_s: list = field(default_factory=list)
    durations_s: list = field(default_factory=list)
    amplitudes_m: list = field(default_factory=list)

    def build(self) -> None:
        n = max(1, self.n_submovements)
        if n == 1:
            self.onsets_s = [0.0]
            self.durations_s = [self.total_time_s]
            self.amplitudes_m = [self.total_distance_m]
            return

        n_corrective = n - 1
        frac_each = min(self.submovement_frac, 0.9 / max(n_corrective, 1))
        corrective_total_frac = frac_each * n_corrective
        primary_frac = 1.0 - corrective_total_frac
        primary_dur = self.total_time_s * (0.55 + 0.1 * self.rng.random())
        primary_dur = min(primary_dur, self.total_time_s * 0.85)

        self.onsets_s = [0.0]
        self.durations_s = [primary_dur]
        self.amplitudes_m = [self.total_distance_m * primary_frac]

        remaining_span = self.total_time_s - primary_dur * 0.4  # overlap tail
        for i in range(n_corrective):
            # each corrective submovement starts somewhat before the previous ends
            frac_pos = (i + 1) / n_corrective
            onset = primary_dur * 0.6 + frac_pos * (self.total_time_s - primary_dur * 0.6)
            onset = min(onset, self.total_time_s - self.total_time_s / (2 * n))
            dur = max(self.total_time_s / (n * 1.5), 0.08)
            self.onsets_s.append(onset)
            self.durations_s.append(dur)
            self.amplitudes_m.append(self.total_distance_m * frac_each)

    def velocity_at(self, t_s: np.ndarray) -> np.ndarray:
        v = np.zeros_like(t_s, dtype=float)
        for onset, dur, amp in zip(self.onsets_s, self.durations_s, self.amplitudes_m):
            tau = (t_s - onset) / dur
            active = (tau >= 0) & (tau <= 1)
            v[active] += amp / dur * minjerk_vel_shape(tau[active])
        return v

    def position_scalar(self, t_s: np.ndarray) -> np.ndarray:
        """Cumulative (trapezoid) integral of velocity_at -> scalar displacement along axis."""
        v = self.velocity_at(t_s)
        pos = np.zeros_like(t_s, dtype=float)
        if len(t_s) > 1:
            dt = np.diff(t_s)
            pos[1:] = np.cumsum(0.5 * (v[1:] + v[:-1]) * dt)
        return pos


@dataclass
class TrialKinematics:
    t_s: np.ndarray  # time since movement onset (can be negative for pre-onset hold)
    pos: np.ndarray  # (N,3) true (noise-free) position of the reaching joint
    speed: np.ndarray  # (N,) true speed magnitude
    truth: dict


def generate_reach(
    p0: np.ndarray,
    p1_target: np.ndarray,
    reach_scale: float,
    mt_s: float,
    n_submovements: int,
    submovement_frac: float,
    sample_rate_hz: float,
    pre_onset_s: float,
    post_hold_s: float,
    tremor_hz_range: tuple[float, float],
    tremor_amp_m: float,
    rng: np.random.Generator,
) -> TrialKinematics:
    """Generate the true (noise-free at sensor level) reach trajectory.

    p1_target is the nominal target position; reach_scale < 1 models reduced
    max reach (undershoot), so the *actual* reach endpoint is
    p0 + reach_scale * (p1_target - p0).
    """
    p0 = np.asarray(p0, dtype=float)
    p1_target = np.asarray(p1_target, dtype=float)
    p1_actual = p0 + reach_scale * (p1_target - p0)
    direction = p1_actual - p0
    distance = float(np.linalg.norm(direction))
    if distance < 1e-9:
        unit = np.zeros(3)
    else:
        unit = direction / distance

    plan = SubmovementPlan(
        total_distance_m=distance,
        total_time_s=mt_s,
        n_submovements=n_submovements,
        submovement_frac=submovement_frac,
        rng=rng,
    )
    plan.build()

    dt = 1.0 / sample_rate_hz
    t_move = np.arange(0.0, mt_s + dt / 2, dt)
    t_pre = np.arange(-pre_onset_s, 0.0, dt)
    t_post = np.arange(mt_s + dt, mt_s + post_hold_s + dt / 2, dt)
    t_s = np.concatenate([t_pre, t_move, t_post])

    scalar_pos = np.zeros_like(t_s)
    move_mask = (t_s >= 0) & (t_s <= mt_s)
    scalar_pos[move_mask] = plan.position_scalar(t_s[move_mask])
    scalar_pos[t_s > mt_s] = distance  # holds at endpoint after MT

    pos = p0[None, :] + unit[None, :] * scalar_pos[:, None]

    # Tremor: sinusoidal perpendicular jitter, active only during movement + hold,
    # amplitude ramps in with an envelope so it doesn't appear during the pre-onset rest.
    if tremor_amp_m > 0 and tremor_hz_range[1] > 0:
        # build 2 axes perpendicular to unit
        arbitrary = np.array([0.0, 1.0, 0.0]) if abs(unit[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
        perp1 = np.cross(unit, arbitrary)
        norm1 = np.linalg.norm(perp1)
        perp1 = perp1 / norm1 if norm1 > 1e-9 else np.array([1.0, 0.0, 0.0])
        perp2 = np.cross(unit, perp1)
        freq = rng.uniform(*tremor_hz_range)
        phase1 = rng.uniform(0, 2 * np.pi)
        phase2 = rng.uniform(0, 2 * np.pi)
        envelope = np.clip((t_s - (-0.05)) / 0.05, 0.0, 1.0)  # ramp on quickly after ~50ms pre-onset
        envelope[t_s < -0.05] = 0.0
        tremor1 = tremor_amp_m * np.sin(2 * np.pi * freq * t_s + phase1) * envelope
        tremor2 = tremor_amp_m * 0.6 * np.sin(2 * np.pi * freq * 1.3 * t_s + phase2) * envelope
        pos = pos + perp1[None, :] * tremor1[:, None] + perp2[None, :] * tremor2[:, None]

    # numeric true speed (central difference) for ground truth peak speed etc.
    speed = np.zeros(len(t_s))
    if len(t_s) > 2:
        vel = np.gradient(pos, t_s, axis=0)
        speed = np.linalg.norm(vel, axis=1)

    move_idx = np.where(move_mask)[0]
    peak_idx = move_idx[np.argmax(speed[move_idx])] if len(move_idx) else 0
    peak_speed = float(speed[peak_idx]) if len(speed) else 0.0
    time_to_peak_pct = float(100.0 * t_s[peak_idx] / mt_s) if mt_s > 0 else 0.0

    truth = {
        "p0": p0.tolist(),
        "p1_target": p1_target.tolist(),
        "p1_actual": p1_actual.tolist(),
        "distance_m": distance,
        "movement_time_s": mt_s,
        "peak_speed_mps": peak_speed,
        "time_to_peak_speed_pct": time_to_peak_pct,
        "n_submovements": max(1, n_submovements),
        "endpoint_error_cm": float(np.linalg.norm(p1_actual - p1_target) * 100.0),
        "path_length_ratio": 1.0,  # true min-jerk path is a straight line -> ratio 1 (tremor adds a little)
    }
    return TrialKinematics(t_s=t_s, pos=pos, speed=speed, truth=truth)
