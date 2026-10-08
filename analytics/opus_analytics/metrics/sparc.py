"""SPARC: SPectral ARC length, per Balasubramanian, Melendez-Calderon, Roby-Brami & Burdet (2015),
"A robust and sensitive metric for quantifying movement smoothness", J NeuroEngineering Rehabil.

This is the reference algorithm from the paper's own open-source implementation
(https://github.com/siva82kb/SPARC), reproduced here: FFT the speed profile (zero-padded),
normalize the magnitude spectrum by its DC value, restrict to frequencies <= fc, then find the
amplitude-threshold-based sub-band (first/last index where the normalized magnitude >= amp_th)
and return the negative arc length of the spectrum over that sub-band.

Defaults per the brief: fc = 10 Hz, amplitude threshold = 0.05.
"""
from __future__ import annotations

import numpy as np

METHOD_VERSION = "sparc_balasubramanian2015_fc10_amp0.05_v1"


def sparc(speed: np.ndarray, fs: float, padlevel: int = 4, fc: float = 10.0, amp_th: float = 0.05) -> float:
    """speed: (N,) speed profile (m/s) over the movement, uniformly sampled at fs Hz.
    Returns the (negative) spectral arc length. Values closer to 0 = smoother;
    more negative = less smooth. Typical smooth reaches: around -1.5 to -2.5.
    """
    speed = np.asarray(speed, dtype=float)
    n = len(speed)
    if n < 4 or np.allclose(speed, 0.0):
        return float("nan")

    nfft = int(2 ** (np.ceil(np.log2(n)) + padlevel))
    freqs = np.arange(0, nfft) * (fs / nfft)
    mag = np.abs(np.fft.fft(speed, nfft))
    max_mag = np.max(mag)
    if max_mag <= 1e-12:
        return float("nan")
    mag = mag / max_mag

    fc_idx = np.nonzero(freqs <= fc)[0]
    f_sel = freqs[fc_idx]
    mag_sel = mag[fc_idx]

    above = np.nonzero(mag_sel >= amp_th)[0]
    if len(above) == 0:
        return float("nan")
    lo, hi = above[0], above[-1]
    f_sel = f_sel[lo:hi + 1]
    mag_sel = mag_sel[lo:hi + 1]

    if len(f_sel) < 2 or (f_sel[-1] - f_sel[0]) <= 0:
        return float("nan")

    df_norm = np.diff(f_sel) / (f_sel[-1] - f_sel[0])
    dmag = np.diff(mag_sel)
    arc_length = -np.sum(np.sqrt(df_norm ** 2 + dmag ** 2))
    return float(arc_length)
