# opus_analytics

Loaders + schema validation, event-based trial segmentation, Butterworth filtering, and Orchard
Reach movement-quality metrics (SPARC, LDLJ, and the rest of `orchard_reach.manifest.json`'s
`metrics` list) for Chetna session directories.

## Setup

```
cd analytics
python -m venv .venv
./.venv/Scripts/python.exe -m pip install -e .[dev]
./.venv/Scripts/python.exe -m pip install -e ../sim   # synthetic_patients, needed for tests
```

## CLI

```
./.venv/Scripts/python.exe -m opus_analytics <session_dir>
```

Validates `session.json` / `events.ndjson` / `kin_*.json` against `contracts/schemas/*`, computes
per-trial and per-session metrics, writes `<session_dir>/metrics.json`, and prints a summary.
Pass `--no-validate` to skip schema checks (useful for quick iteration on huge validation runs)
and `--quiet` to suppress the printed summary.

Phantom Hand sessions (events `phase_start`/`threat_impact`/..., `sens_###.json`) additionally get an
`embodiment` object in metrics.json (`metrics/embodiment.py`, 03-SPEC section 8/12); `--summary` prints the
sync / async / difference witness table. Orchard sessions are unchanged.

## Package layout

- `schemas.py` -- locates and loads the read-only `contracts/schemas/*.json` files, generic
  jsonschema validation helper.
- `io_session.py` -- `load_session(dir)`: reads + validates `session.json`/`events.ndjson`/
  `kin_*.json`, concatenates chunks into one array per joint.
- `segmentation.py` -- `segment_trials(events)`: groups events.ndjson rows by `trial` index into
  `Trial` objects with all the key timestamps (target_shown, movement_onset, contact, ...).
- `filtering.py` -- 4th-order zero-phase Butterworth low-pass (6 Hz cutoff) for position traces.
- `kinematics.py` -- velocity/speed/accel/jerk derivation, path length, submovement counting.
- `metrics/sparc.py`, `metrics/ldlj.py` -- SPARC (Balasubramanian et al. 2015) and LDLJ
  (Hogan & Sternad 2009), reference-algorithm implementations.
- `metrics/trial_metrics.py`, `metrics/session_metrics.py` -- per-trial and per-session metric
  computation, each value carrying `{value, unit, method_version, quality, quality_reasons?,
  mdc95?, n?}`. Per-trial: also `trunk_lean_cm` (max head displacement *toward* the target,
  directional, vs. trial-start head position -- distinct from `trunk_displacement_cm`'s
  max-radial-displacement-from-target-shown). Per-session: also `rate_hz` (the session's
  declared tracking rate, flagged `degraded` below 45Hz) and `trunk_lean_cm` (mean of the
  per-trial values, with an `mdc95` band); `neglect_index` also carries `mdc95`.
- `quality.py` -- ok/degraded/invalid quality-flag rules from tracking loss and event
  completeness, each with a `quality_reasons` list; plus `flag_smoothness_metric`, a stricter
  SPARC/LDLJ-only gate on `rate_hz < 45` and tracking loss above an empirically-derived
  threshold (see VALIDATION.md section 6).
- `analyze.py` -- `analyze_session(dir)` / `write_metrics(dir)` orchestration.

## metrics.json shape

```
{
  "session_id": "...",
  "computed_at": "...",
  "analytics_version": "0.1.0",
  "sample_rate_hz": 72.0,
  "validation": {"session.json": [], "events.ndjson": [], "kin_chunks": []},
  "trials": [
    {"block": 0, "trial": 0, "hand": "right", "outcome": "success", "target": {...},
     "t_start_ms": 1000.0, "t_end_ms": 2500.0,
     "metrics": {
       "reaction_time_ms": {"value": 312.0, "unit": "ms", "method_version": "...", "quality": "ok"},
       "sparc": {"value": -1.4, "unit": "unitless", "method_version": "...", "quality": "degraded",
                 "quality_reasons": ["rate_hz_30.0_below_45"]},
       "trunk_lean_cm": {"value": 2.1, "unit": "cm", "method_version": "...", "quality": "ok"},
       ...
     }}
  ],
  "session": {
    "metrics": {
      "rate_hz": {"value": 72.0, "unit": "Hz", "method_version": "...", "quality": "ok"},
      "success_rate": {"value": 0.85, "unit": "unitless", "method_version": "...", "quality": "ok"},
      "neglect_index": {"value": 0.12, "unit": "unitless", "method_version": "...", "quality": "ok", "mdc95": 0.171},
      "trunk_lean_cm": {"value": 1.8, "unit": "cm", "method_version": "...", "quality": "ok", "mdc95": 0.202, "n": 18},
      ...
    }
  }
}
```

Metrics conform to `contracts/schemas/metrics.schema.json`; the extra keys above (`quality_reasons`,
`mdc95`, `n`, and the session-level `rate_hz` metric) validate without a schema change because
neither `metricValue` nor `session` sets `additionalProperties: false`.

## Synthetic profiles

`sim/synthetic_patients/profiles.py`: `healthy, mild, moderate, severe, left_neglect,
noisy_tracking, quest_low_rate`. The last is "healthy" biomechanics recorded at 30Hz (Quest
hand-tracking LOW mode, via `chunking.apply_rate_hold`'s zero-order hold) instead of 72Hz, to
exercise the `rate_hz < 45` quality gate independently of impairment/noise knobs. `l_wrist`/
`r_wrist`/`l_palm`/`r_palm` frames also carry a synthesized `rot` quaternion (see
`orientation.py`) for the Unity demo hand mesh; it feeds no metric.

## Tests

```
./.venv/Scripts/python.exe -m pytest -q
```

`tests/test_reach_model.py` and `tests/test_metrics_known_cases.py` check known analytic cases
(minimum-jerk closed-form peak speed, 1-submovement detection, SPARC/LDLJ sanity + monotonicity)
including hypothesis property tests; `tests/test_schema_validity.py` generates a session per
profile (including `quest_low_rate`) and asserts it and its `metrics.json` validate against
`contracts/schemas/*`; `tests/test_quality_dropout.py` checks the quality flag degrades under
tracking dropout and stays `ok` for clean data; `tests/test_wrist_orientation.py` checks every
wrist/palm frame has a unit-norm quaternion, is schema-valid, and is seed-deterministic.

See `VALIDATION.md` for the full per-metric error report against synthetic ground truth,
including the rate_hz/dropout threshold derivations and MDC95 values.
