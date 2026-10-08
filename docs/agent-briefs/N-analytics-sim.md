# Brief N: Analytics + synthetic patients · model: Sonnet (N4 and fixtures: Haiku)

> **Status (Opus, 2026-09-19): N1–N4 are DONE and tagged `analytics-v0.2.0`** (the N4 line below still says
> v0.1.0). 49 pytest tests pass. Beyond this brief, v0.2 added quality gating (`rate_hz < 45`, tracking loss
> >= 15 %), per-trial trunk lean and neglect index with MDC bands, a 30 Hz `quest_low_rate` profile and wrist
> rotation. **One open track-N bug:** the longitudinal generator gives each session in a series a different
> `patient_ref`, so the Flutter app has to work around it to show one patient's history. Fix it here, in the
> generator, not in the app.

**Goal:** trustworthy movement-quality metrics, validated against known ground truth.

## Stack
Python 3.12 · `uv` or venv · numpy, scipy, pandas, pyarrow, jsonschema, pytest, hypothesis · ruff.

## Tasks
- **N1** `sim/synthetic_patients/`: generate full session directories (session.json, events.ndjson, kin_###.json) per the contracts.
  - Reach model: minimum-jerk point-to-point, bell-shaped speed
  - Impairment knobs: RT slowing, MT scaling, submovement count/overlap, tremor (4–12 Hz), reduced max reach %, trunk compensation (head drift toward target), side asymmetry / neglect (misses on one side), fatigue drift, tracking dropouts (conf = 0 spans), sensor jitter
  - Profiles: `healthy`, `mild`, `moderate`, `severe`, `left_neglect`, `noisy_tracking`; a 6-week longitudinal series with recovery
  - Write `ground_truth` into the chunks and a `truth.json` per session
- **N2** `analytics/opus_analytics/`: loaders + schema validation; trial segmentation from events; filtering (Butterworth, documented cutoff); metrics listed in `orchard_reach.manifest.json` → `metrics.json` with `unit`, `method_version`, `quality` (ok/degraded/invalid from tracking loss and sample gaps).
  SPARC per Balasubramanian et al. 2015 (fc = 10 Hz, amplitude threshold 0.05); LDLJ per Hogan and Sternad 2009.
- **N3** `analytics/VALIDATION.md`: error tables per metric and profile, plus the method (goal G4: < 5 %). Include failure cases (e.g. SPARC under heavy dropout).
- **N4** (Haiku) CLI `opus-analyze <session_dir>`, pytest fixtures, README. Tag `analytics-v0.1.0`.

## Done when
`pytest` passes, the validation report meets G4 or documents why not, and sample outputs are exported to `contracts/fixtures/sessions/` for the app and the Unity replay.
