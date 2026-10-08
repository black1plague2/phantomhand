"""Generate a larger validation dataset per profile, run opus_analytics, and compute
per-metric error against the synthetic ground truth. Prints a markdown table to stdout
(redirect into VALIDATION.md's data section) plus raw JSON for inspection.

Not part of the installed package; a standalone throwaway script for producing the
evidence behind analytics/VALIDATION.md. Run from analytics/ with its venv active:
    .venv/Scripts/python.exe scripts/run_validation.py
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

OUT_DIR = REPO_ROOT / "sim" / "out" / "validation"
TRIALS_PER_SESSION = 30
SEEDS = [1001, 2002, 3003, 4004]

GROUND_TRUTH_METRICS = [
    ("reaction_time_ms", "reaction_time_ms"),
    ("movement_time_ms", "movement_time_ms"),
    ("peak_speed_mps", "peak_speed_mps"),
    ("time_to_peak_speed_pct", "time_to_peak_speed_pct"),
    ("endpoint_error_cm", "endpoint_error_cm"),
    ("trunk_displacement_cm", "trunk_displacement_cm"),
]


def pct_error(measured, truth):
    if truth is None or measured is None:
        return None
    if abs(truth) < 1e-9:
        return None  # relative error is meaningless near a zero ground truth; use abs_error instead
    return 100.0 * abs(measured - truth) / abs(truth)


def abs_error(measured, truth):
    if truth is None or measured is None:
        return None
    return abs(measured - truth)


def main():
    report = {}
    for profile_name in PROFILES:
        errors_by_metric = {m: [] for m, _ in GROUND_TRUTH_METRICS}
        abs_errors_by_metric = {m: [] for m, _ in GROUND_TRUTH_METRICS}
        nsub_exact, nsub_total, nsub_abs_diff = 0, 0, []
        quality_counts = {"ok": 0, "degraded": 0, "invalid": 0}
        sparc_vals, ldlj_vals = [], []
        n_trials_total, n_attempted = 0, 0

        for seed in SEEDS:
            out_dir = OUT_DIR / profile_name / f"seed_{seed}"
            truth = write_session(out_dir, profile_name, seed=seed, params={"trialCount": TRIALS_PER_SESSION})
            result = analyze_session(out_dir, validate=False)

            truth_by_index = {t["index"]: t for t in truth["trials"]}
            for trial in result["trials"]:
                n_trials_total += 1
                t_truth = truth_by_index.get(trial["trial"], {})
                if t_truth.get("reaction_time_ms") is None:
                    continue  # timeout trial, no ground truth movement to compare
                n_attempted += 1
                m = trial["metrics"]
                for metric_id, truth_key in GROUND_TRUTH_METRICS:
                    mv = m[metric_id]
                    if mv["quality"] == "invalid" or mv["value"] is None:
                        continue
                    tv = t_truth.get(truth_key)
                    err = pct_error(mv["value"], tv)
                    if err is not None:
                        errors_by_metric[metric_id].append(err)
                    aerr = abs_error(mv["value"], tv)
                    if aerr is not None:
                        abs_errors_by_metric[metric_id].append(aerr)

                if m["n_submovements"]["value"] is not None and t_truth.get("n_submovements") is not None:
                    nsub_total += 1
                    diff = abs(m["n_submovements"]["value"] - t_truth["n_submovements"])
                    nsub_abs_diff.append(diff)
                    if diff == 0:
                        nsub_exact += 1

                if m["sparc"]["value"] is not None:
                    sparc_vals.append(m["sparc"]["value"])
                if m["ldlj"]["value"] is not None:
                    ldlj_vals.append(m["ldlj"]["value"])
                quality_counts[m["movement_time_ms"]["quality"]] = quality_counts.get(m["movement_time_ms"]["quality"], 0) + 1

        summary = {"n_trials_total": n_trials_total, "n_attempted": n_attempted, "quality_counts": quality_counts}
        for metric_id, _ in GROUND_TRUTH_METRICS:
            vals = errors_by_metric[metric_id]
            avals = abs_errors_by_metric[metric_id]
            summary[metric_id] = {
                "n": len(vals),
                "mean_pct_error": round(stats.mean(vals), 2) if vals else None,
                "median_pct_error": round(stats.median(vals), 2) if vals else None,
                "max_pct_error": round(max(vals), 2) if vals else None,
                "n_abs": len(avals),
                "mean_abs_error": round(stats.mean(avals), 3) if avals else None,
                "median_abs_error": round(stats.median(avals), 3) if avals else None,
            }
        summary["n_submovements"] = {
            "n": nsub_total,
            "exact_match_rate_pct": round(100 * nsub_exact / nsub_total, 1) if nsub_total else None,
            "mean_abs_diff": round(stats.mean(nsub_abs_diff), 2) if nsub_abs_diff else None,
        }
        summary["sparc"] = {"n": len(sparc_vals), "mean": round(stats.mean(sparc_vals), 3) if sparc_vals else None,
                             "sd": round(stats.pstdev(sparc_vals), 3) if len(sparc_vals) > 1 else None}
        summary["ldlj"] = {"n": len(ldlj_vals), "mean": round(stats.mean(ldlj_vals), 3) if ldlj_vals else None,
                            "sd": round(stats.pstdev(ldlj_vals), 3) if len(ldlj_vals) > 1 else None}
        report[profile_name] = summary

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
