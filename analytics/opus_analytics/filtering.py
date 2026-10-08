"""Zero-phase Butterworth low-pass filtering of position traces.

Cutoff: 6 Hz, 4th-order Butterworth, applied with scipy.signal.filtfilt (zero phase).
6 Hz is a standard choice for voluntary upper-limb reaching kinematics (movement energy
is essentially all below ~5-6 Hz; this also attenuates the 4-12 Hz tremor band's lower
edge somewhat, which is expected -- tremor amplitude/frequency should be read from the
*unfiltered* signal or a band-pass, not from this smoothing filter).
"""
from __future__ import annotations

import numpy as np
from scipy.signal import butter, filtfilt

CUTOFF_HZ = 6.0
ORDER = 4
METHOD_VERSION = "butter4_zerophase_6hz_v1"


def butterworth_lowpass(pos: np.ndarray, fs: float, cutoff_hz: float = CUTOFF_HZ, order: int = ORDER) -> np.ndarray:
    """pos: (N,) or (N,3). Returns filtered array of the same shape.

    Falls back to returning the input unchanged if there are too few samples for
    filtfilt's default padding (avoids crashing on very short/degraded trials --
    those should be flagged degraded/invalid by quality.py instead).
    """
    pos = np.asarray(pos, dtype=float)
    n = pos.shape[0]
    nyquist = fs / 2.0
    if nyquist <= cutoff_hz:
        return pos.copy()
    b, a = butter(order, cutoff_hz / nyquist, btype="low")
    padlen = 3 * max(len(a), len(b))
    if n <= padlen:
        return pos.copy()
    if pos.ndim == 1:
        return filtfilt(b, a, pos)
    return np.stack([filtfilt(b, a, pos[:, i]) for i in range(pos.shape[1])], axis=1)
