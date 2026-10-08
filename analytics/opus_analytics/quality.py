"""Quality flag rules (goal G5): every metric value carries ok / degraded / invalid,
plus a `quality_reasons` list explaining any non-"ok" (and, where useful, "ok") flag.

- invalid: critical events missing for the metric (e.g. no movement_onset/contact for a
  movement-phase metric), fewer than 5 usable samples, or tracking loss > INVALID_LOSS_FRAC
  of the analysis window.
- degraded: tracking loss between DEGRADED_LOSS_FRAC and INVALID_LOSS_FRAC, or a short
  (<0.15s) contiguous tracking gap inside the window.
- ok: otherwise.

SPARC/LDLJ get an additional, stricter gate (`flag_smoothness_metric`): these are
derivative-of-derivative (jerk) quantities, far more sensitive to low sample density or
frozen/held dropout spans than position- or timing-based metrics, so they can still be
materially wrong even when the generic per-window flag above says "degraded" (see
VALIDATION.md section 2's documented SPARC-under-dropout failure case). Two triggers:
  - rate_hz < RATE_HZ_MIN (45 Hz): the brief's own hand-tracking validation literature ties
    SPARC/LDLJ reliability to tracking rate; below 45Hz there are too few genuinely-new
    samples per stroke to resolve the velocity spectrum.
  - tracking loss in the movement window > SPARC_LDLJ_DEGRADE_LOSS_FRAC: derived empirically
    in `analytics/scripts/derive_dropout_threshold.py` (paired dropout-on vs dropout-off runs,
    same seed, isolating dropout from impairment) as the realized per-trial tracking-loss
    fraction at which mean SPARC %% error (vs the dropout-off control) first exceeds ~10%.
    See VALIDATION.md section 3 for the full sweep table and the chosen value.
"""
from __future__ import annotations

import numpy as np

DEGRADED_LOSS_FRAC = 0.05
INVALID_LOSS_FRAC = 0.30
MIN_SAMPLES = 5

RATE_HZ_MIN = 45.0
# Derived in analytics/scripts/derive_dropout_threshold.py: first realized-loss-fraction bin
# whose mean SPARC %% error (vs a dropout-off control, same seed) exceeded 10%% was [0.15, 0.18).
# Documented in VALIDATION.md section 3.
SPARC_LDLJ_DEGRADE_LOSS_FRAC = 0.15


def flag_for_window(conf: np.ndarray, events_complete: bool,
                     min_samples: int = MIN_SAMPLES) -> tuple[str, float, list[str]]:
    """Returns (quality, tracking_loss_frac, reasons)."""
    if not events_complete:
        loss = 1.0 if len(conf) == 0 else float(np.mean(conf <= 0.0))
        return "invalid", loss, ["required_event_missing"]
    if len(conf) < min_samples:
        return "invalid", 1.0, [f"fewer_than_{min_samples}_usable_samples"]
    loss_frac = float(np.mean(conf <= 0.0))
    if loss_frac > INVALID_LOSS_FRAC:
        return "invalid", loss_frac, [f"tracking_loss_{loss_frac:.2f}_gt_invalid_{INVALID_LOSS_FRAC}"]
    if loss_frac > DEGRADED_LOSS_FRAC:
        return "degraded", loss_frac, [f"tracking_loss_{loss_frac:.2f}_gt_degraded_{DEGRADED_LOSS_FRAC}"]
    # a short contiguous gap still degrades trust in derivative-based metrics (velocity/jerk)
    lost = conf <= 0.0
    if lost.any():
        run = 0
        max_run = 0
        for v in lost:
            run = run + 1 if v else 0
            max_run = max(max_run, run)
        if max_run >= 3:
            return "degraded", loss_frac, [f"contiguous_tracking_gap_{max_run}_samples"]
    return "ok", loss_frac, []


def flag_smoothness_metric(base_quality: str, base_reasons: list[str], loss_frac: float,
                            rate_hz: float | None) -> tuple[str, list[str]]:
    """Applies the SPARC/LDLJ-specific rate_hz and dropout gates on top of a metric's already-
    computed window quality/reasons. Never *upgrades* an invalid/degraded base quality; only
    adds "degraded" + a reason when the base quality was "ok" but one of these gates trips.
    """
    quality = base_quality
    reasons = list(base_reasons)
    if rate_hz is not None and rate_hz < RATE_HZ_MIN:
        reasons.append(f"rate_hz_{rate_hz:.1f}_below_{RATE_HZ_MIN:.0f}")
        if quality == "ok":
            quality = "degraded"
    if loss_frac is not None and loss_frac > SPARC_LDLJ_DEGRADE_LOSS_FRAC:
        reasons.append(f"tracking_loss_{loss_frac:.2f}_gt_sparc_ldlj_threshold_{SPARC_LDLJ_DEGRADE_LOSS_FRAC}")
        if quality == "ok":
            quality = "degraded"
    return quality, reasons
