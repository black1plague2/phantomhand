"""LDLJ: Log Dimensionless Jerk, per Hogan & Sternad (2009), "Sensitivity of smoothness measures
to movement duration, amplitude, and arrests", J Motor Behavior.

Velocity-based dimensionless jerk normalizes the integrated squared jerk by movement duration
and speed amplitude so it is comparable across movements of different size/speed:

    DJ = -(T^3 / A^2) * integral( (d^2 v/dt^2)^2 dt )         [T = duration, A = speed amplitude]
    LDLJ = -ln(|DJ|)

This is the standard practical form (matching the widely used reference implementation
of the Balasubramanian/Hogan-Sternad smoothness metrics, e.g. the "smoothness" toolbox),
using the discrete second difference of the speed profile as the jerk term. LDLJ values
are typically negative; values closer to 0 indicate smoother (less jerky) movement.
"""
from __future__ import annotations

import numpy as np

METHOD_VERSION = "ldlj_hogan_sternad2009_speed_v1"


def log_dimensionless_jerk(speed: np.ndarray, fs: float) -> float:
    speed = np.asarray(speed, dtype=float)
    n = len(speed)
    if n < 4:
        return float("nan")
    amp = np.max(speed) - np.min(speed)
    if amp <= 1e-9:
        return float("nan")

    duration_s = n / fs
    jerk = np.diff(speed, 2) * (fs ** 2)  # discrete second derivative of speed
    scale = (duration_s ** 3) / (amp ** 2)
    dj = -scale * np.sum(jerk ** 2) / fs
    if dj == 0:
        return float("nan")
    return float(-np.log(abs(dj)))
