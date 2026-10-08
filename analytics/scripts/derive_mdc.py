"""Derive MDC95 (Minimal Detectable Change, 95%% CI) for the session-level `neglect_index` and
the session-level trunk-lean summary (`trunk_lean_cm`), by test-retest: repeated synthetic
sessions of the *same* profile (same simulated "patient"/clinical state) with different seeds
give a distribution of the metric under no real change; its spread is the measurement-error
SEM proxy, and MDC95 = 1.96 * sqrt(2) * SEM (standard two-measurement MDC formula, e.g. Beckerman
et al. 1998 / Haley & Fragala-Pinkham 2006 -- used throughout rehab outcome-measure literature,
including for FMA-UE/ARAT/Box&Block, which is the "same MDC language" IMPROVEMENT_BRIEF.md sec 1
asks be applied to VR-derived metrics too).

Not part of the installed package; a standalone script (see run_validation.py /
derive_dropout_threshold.py for the same pattern). Run from analytics/ with its venv active:
    .venv/Scripts/python.exe scripts/derive_mdc.py
"""
from __future__ import annotations

import statistics as stats
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "sim"))
sys.path.insert(0, str(REPO_ROOT / "analytics"))

from synthetic_patients.generate import write_session
from synthetic_patients.profiles import PROFILES
from opus_analytics.analyze import analyze_session

OUT_DIR = REPO_ROOT / "sim" / "out" / "mdc_sweep"
TRIALS_PER_SESSION = 24
SEEDS = list(range(5001, 5001 + 10))  # 10 repeated "sessions" per profile, same clinical state
PROFILES_TO_TEST = ["healthy", "mild", "moderate", "severe", "left_neglect"]


def session_trunk_lean_mean(result: dict) -> float | None:
    vals = [t["metrics"]["trunk_lean_cm"]["value"] for t in result["trials"]
            if t["metrics"]["trunk_lean_cm"]["value"] is not None
            and t["metrics"]["trunk_lean_cm"]["quality"] != "invalid"]
    return stats.mean(vals) if vals else None


def mdc95_from_repeats(values: list[float]) -> tuple[float, float]:
    """Returns (sem_proxy, mdc95). sem_proxy = sample SD across repeated same-state sessions."""
    sd = stats.pstdev(values) if len(values) > 1 else float("nan")
    mdc95 = 1.96 * (2 ** 0.5) * sd
    return sd, mdc95


def main():
    print(f"{'profile':<14} | {'metric':<16} | n | mean | sd (SEM proxy) | MDC95")
    report = {}
    for profile_name in PROFILES_TO_TEST:
        neglect_vals, lean_vals = [], []
        for seed in SEEDS:
            out_dir = OUT_DIR / profile_name / f"seed_{seed}"
            write_session(out_dir, profile_name, seed=seed, params={"trialCount": TRIALS_PER_SESSION})
            result = analyze_session(out_dir, validate=False)
            ni = result["session"]["metrics"]["neglect_index"]
            if ni["value"] is not None:
                neglect_vals.append(ni["value"])
            lean = session_trunk_lean_mean(result)
            if lean is not None:
                lean_vals.append(lean)

        report[profile_name] = {}
        for metric_name, vals in (("neglect_index", neglect_vals), ("trunk_lean_cm_mean", lean_vals)):
            if len(vals) < 2:
                print(f"{profile_name:<14} | {metric_name:<16} | {len(vals)} | - | - | -")
                continue
            mean_v = stats.mean(vals)
            sd, mdc95 = mdc95_from_repeats(vals)
            print(f"{profile_name:<14} | {metric_name:<16} | {len(vals)} | {mean_v:.3f} | {sd:.3f} | {mdc95:.3f}")
            report[profile_name][metric_name] = {"n": len(vals), "mean": mean_v, "sd": sd, "mdc95": mdc95}

    # pooled (across profiles) MDC95, used as the single constant shipped in session_metrics.py
    # -- pooling averages out profile-specific variance so one conservative number can be quoted
    # regardless of which patient a session belongs to.
    all_neglect = [v["neglect_index"]["mdc95"] for v in report.values() if "neglect_index" in v]
    all_lean = [v["trunk_lean_cm_mean"]["mdc95"] for v in report.values() if "trunk_lean_cm_mean" in v]
    print()
    if all_neglect:
        print(f"pooled mean MDC95(neglect_index) across profiles = {stats.mean(all_neglect):.3f}")
    if all_lean:
        print(f"pooled mean MDC95(trunk_lean_cm_mean) across profiles = {stats.mean(all_lean):.3f} cm")


if __name__ == "__main__":
    main()
