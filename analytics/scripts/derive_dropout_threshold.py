"""Derive the tracking-loss fraction at which SPARC becomes unreliable (>~10% error vs a
clean-tracking control), to set `SPARC_LDLJ_DEGRADE_LOSS_FRAC` in `opus_analytics/quality.py`.

Method (paired, same seed / same movement, dropout on vs off):
  For a sweep of `dropout_rate` values, generate two sessions from the "healthy" profile knobs
  (clean biomechanics -- no tremor/extra submovements -- so any SPARC drift is attributable to
  tracking dropout, not impairment) that are IDENTICAL except for `dropout_rate`: one at the
  swept dropout_rate (the "affected" run) and one at dropout_rate=0.0 (the "control" run,
  the closest thing to a SPARC ground truth this generator can produce, since SPARC itself has
  no closed-form truth -- see VALIDATION.md section 2). Same seed -> same underlying true
  trajectory in both; only the recording-noise dropout differs, so the two runs are trial-index
  aligned. Each trial's *actually realized* tracking-loss fraction (opus_analytics'
  `tracking_loss_pct` metric, on the movement window used for SPARC) is recorded, and %
  |SPARC_affected - SPARC_control| / |SPARC_control| is binned by that realized fraction
  (dropout_rate is a per-chunk probability, not a directly comparable per-trial quantity).

Not part of the installed package; a standalone script, like run_validation.py.
Run from analytics/ with its venv active:
    .venv/Scripts/python.exe scripts/derive_dropout_threshold.py
"""
from __future__ import annotations

import json
import statistics as stats
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "sim"))
sys.path.insert(0, str(REPO_ROOT / "analytics"))

from synthetic_patients.generate import write_session
from synthetic_patients.profiles import PROFILES
from opus_analytics.analyze import analyze_session

OUT_DIR = REPO_ROOT / "sim" / "out" / "dropout_sweep"
TRIALS_PER_SESSION = 40
SEEDS = [11, 22, 33, 44]
DROPOUT_RATES = [0.0, 0.03, 0.06, 0.10, 0.15, 0.20, 0.25, 0.30, 0.35]

BASE = dict(PROFILES["healthy"])


def _pct_error(a, b):
    if a is None or b is None or abs(b) < 1e-9:
        return None
    return 100.0 * abs(a - b) / abs(b)


def main():
    # bin edges on the realized per-trial tracking-loss fraction (0..1) of the movement window
    bin_edges = [0.0, 0.02, 0.04, 0.06, 0.08, 0.10, 0.12, 0.15, 0.18, 0.22, 0.26, 0.30, 1.01]
    bin_errors: dict[int, list[float]] = {i: [] for i in range(len(bin_edges) - 1)}

    for seed in SEEDS:
        control_profile = dict(BASE, dropout_rate=0.0)
        control_dir = OUT_DIR / f"seed_{seed}" / "control"
        write_session(control_dir, "healthy", seed=seed, params={"trialCount": TRIALS_PER_SESSION},
                      profile_override=control_profile)
        control_result = analyze_session(control_dir, validate=False)
        control_by_idx = {t["trial"]: t["metrics"] for t in control_result["trials"]}

        for rate in DROPOUT_RATES:
            profile = dict(BASE, dropout_rate=rate)
            out_dir = OUT_DIR / f"seed_{seed}" / f"dropout_{rate}"
            write_session(out_dir, "healthy", seed=seed, params={"trialCount": TRIALS_PER_SESSION},
                          profile_override=profile)
            result = analyze_session(out_dir, validate=False)

            for t in result["trials"]:
                idx = t["trial"]
                cm = control_by_idx.get(idx)
                if cm is None:
                    continue
                sparc_ctrl = cm["sparc"]["value"]
                sparc_aff = t["metrics"]["sparc"]["value"]
                loss_frac = t["metrics"]["tracking_loss_pct"]["value"]
                if sparc_ctrl is None or sparc_aff is None or loss_frac is None:
                    continue
                loss_frac = loss_frac / 100.0
                err = _pct_error(sparc_aff, sparc_ctrl)
                if err is None:
                    continue
                for b in range(len(bin_edges) - 1):
                    if bin_edges[b] <= loss_frac < bin_edges[b + 1]:
                        bin_errors[b].append(err)
                        break

    print("realized_loss_frac_bin | n | mean_sparc_pct_error | median")
    crossover = None
    for b in range(len(bin_edges) - 1):
        vals = bin_errors[b]
        lo, hi = bin_edges[b], bin_edges[b + 1]
        if not vals:
            print(f"[{lo:.2f},{hi:.2f}) | 0 | - | -")
            continue
        mean_e = stats.mean(vals)
        med_e = stats.median(vals)
        print(f"[{lo:.2f},{hi:.2f}) | {len(vals)} | {mean_e:.2f}% | {med_e:.2f}%")
        if crossover is None and mean_e > 10.0:
            crossover = lo
    print()
    print(f"First bin whose mean SPARC error exceeds 10%: loss_frac >= {crossover}"
          if crossover is not None else "No bin exceeded 10% mean error in this sweep")


if __name__ == "__main__":
    main()
