# 2026-09-14 - Track N (analytics + synthetic patients) - N1/N2/N3 + minimal N4

Agent: Sonnet implementation agent, Track N.

## What was done

### N1 - `sim/synthetic_patients/`
Python package generating schema-valid Orchard Reach session directories:
- `reach_model.py`: minimum-jerk (Flash & Hogan 1985) point-to-point reach with a
  `SubmovementPlan` (primary + overlapping corrective minimum-jerk velocity bumps, so the
  injected submovement count is exact ground truth), perpendicular sinusoidal tremor
  (4-12 Hz band, profile-controlled), and reduced-max-reach undershoot (`reach_scale`).
- `profiles.py`: `healthy, mild, moderate, severe, left_neglect, noisy_tracking` knob sets
  (RT mean/sd, MT scale, reach scale, submovement count/overlap, tremor Hz/amplitude, trunk
  compensation, sensor jitter std, dropout rate, fatigue rate, neglect bias, miss rate) plus
  `interpolated_profile(week)` for the 6-week longitudinal recovery series (moderate -> healthy).
- `session.py`: assembles a full session (calibration -> trials -> block/session end) into
  events (schema-shaped) and 9-joint (head + both wrists/index/thumb tips/palms) position
  arrays at 72 Hz on one global clock shared with events; models reaction time, movement time,
  aiming error, grasp aperture (index-thumb distance), trunk drift toward target, fatigue
  drift across the session, and left/right neglect bias (worse RT/reach/miss-rate for targets
  on the affected side).
- `chunking.py`: turns true joint trajectories into recorded ones (Gaussian sensor jitter +
  per-chunk, per-joint dropout spans with position frozen at last-good-sample, matching
  realistic tracker loss), splits into 5s/360-frame `kin_###.json` chunks.
  `ground_truth` in each chunk references `truth.json` (kept there to avoid duplicating large
  per-trial truth in every chunk).
- `generate.py` / `longitudinal.py` / `__main__.py`: session writer, 6-week series writer
  (`summary.json`), and a CLI (`python -m synthetic_patients session|all-profiles|longitudinal|fixtures`).
- Deterministic: every generator takes an explicit seed (`numpy.random.default_rng`).
- `truth.json` per session: profile knobs + per-trial ground truth (RT, MT, peak speed,
  time-to-peak %, n_submovements, endpoint error, path_length_ratio, trunk displacement) plus
  dropout spans.

### N2 - `analytics/opus_analytics/`
- `schemas.py` / `io_session.py`: locates `contracts/schemas/*` (never edited), validates
  `session.json`, every `events.ndjson` line, and every `kin_*.json` chunk with `jsonschema`,
  concatenates chunks into one array per joint.
- `segmentation.py`: groups `events.ndjson` by `trial` into `Trial` objects with all key
  timestamps (target_shown, movement_onset, contact, grasp, release, placed, trial_end).
- `filtering.py`: 4th-order zero-phase Butterworth low-pass, 6 Hz cutoff (documented reasoning
  in the docstring), degrades gracefully (returns unfiltered) on too-short windows instead of
  crashing.
- `kinematics.py`: velocity/speed/accel/jerk via `np.gradient`, path length, and a
  prominence + min-separation submovement counter.
- `metrics/sparc.py`: SPARC per Balasubramanian et al. (2015) -- FFT of the speed profile,
  normalize by DC, restrict to <= fc (10 Hz), amplitude-threshold sub-band (0.05), negative arc
  length -- the reference algorithm from the paper's own open-source implementation.
- `metrics/ldlj.py`: Log Dimensionless Jerk per Hogan & Sternad (2009), velocity-based form
  `-ln(|-(T^3/A^2) * sum(jerk^2)/fs|)` using the discrete second difference of speed as jerk.
- `metrics/trial_metrics.py` / `metrics/session_metrics.py`: every metric in
  `orchard_reach.manifest.json`'s `metrics` list, each value as
  `{value, unit, method_version, quality}`.
- `quality.py`: ok/degraded/invalid from tracking-loss fraction + event completeness.
- `analyze.py` + `__main__.py`: `python -m opus_analytics <session_dir>` loads, validates,
  computes, and writes `metrics.json` next to the session.

### N3 - `analytics/VALIDATION.md`
Generated from `analytics/scripts/run_validation.py` (120 trials/profile, 4 seeds). Headline:
RT/MT 0.00% error (event-timestamp based); peak_speed_mps and time_to_peak_speed_pct meet the
< 5% (G4) target for 4/6 profiles, slipping to 6-11% for severe/noisy_tracking/moderate
(documented: differentiation noise amplification, near-tied-peak sensitivity); endpoint_error_cm
and trunk_displacement_cm are validated in absolute cm (<=1.4cm mean abs error, except
noisy_tracking's 5.4cm trunk error) because their ground truth is often near zero, making %
error a misleading yardstick (documented explicitly, both % and abs reported); n_submovements
exact-match is 100% for healthy/mild but degrades hard for severe (8%) and noisy_tracking (27%)
-- **known, documented limitation**: the peak-counting submovement detector cannot distinguish
tremor ripple from genuine corrective submovements, flagged as a v0.2 item; SPARC/LDLJ have no
scalar ground truth and are validated qualitatively (correct monotonic ordering
healthy < mild < moderate < severe) -- and a documented **failure case** matching the brief's
ask: SPARC under noisy_tracking's heavy dropout is *not* reliably worse than mild despite no
injected tremor, because frozen (held-last-sample) dropout spans create spurious
flat-then-step artifacts that distort the spectral arc length; consumers must treat SPARC/LDLJ
with `quality != "ok"` as indicative only.

### N4 (minimal, done by this agent rather than deferred to Haiku, given the working CLI was already produced by N2)
`python -m opus_analytics <session_dir>` is the working CLI (validates, computes, writes
`metrics.json`, prints a summary). `python -m synthetic_patients` covers the generator side.
`analytics/README.md` documents setup, CLI usage, package layout, the `metrics.json` shape, and
how to run the tests. Did **not** tag `analytics-v0.1.0` (git operations are the orchestrator's
job per common rule 6) and did not build a separate `opus-analyze` console-script entry point
beyond the `python -m` invocation named in the task -- flagging this as a small remaining N4
gap if the orchestrator wants a installed console script too.

### Fixtures
`contracts/fixtures/sessions/<profile>/` (healthy, mild, moderate, severe, left_neglect,
noisy_tracking; 5 trials each) + `contracts/fixtures/sessions/longitudinal/week_00..06/session_0/`
(5 trials each) + `longitudinal/summary.json`. Every fixture has `metrics.json` written by
`opus_analytics`. Total size: **6.8 MB** (well under the ~15MB budget; trial counts were
reduced, not downsampled, per the instruction not to downsample fixture data).

## Files touched
- `sim/pyproject.toml`, `sim/.gitignore`, `sim/synthetic_patients/*.py` (new package)
- `analytics/pyproject.toml`, `analytics/.gitignore`, `analytics/requirements.txt`,
  `analytics/README.md`, `analytics/VALIDATION.md`, `analytics/opus_analytics/*.py` (new package,
  incl. `metrics/` subpackage), `analytics/tests/*.py`, `analytics/scripts/run_validation.py`
- `contracts/fixtures/sessions/**` (new fixture data + generated `metrics.json`)
- `analytics/.venv/` (local venv, gitignored) with numpy/scipy/pandas/jsonschema/pytest/hypothesis
- Did not touch `contracts/schemas/*` (read-only per rule 2).

## Test evidence (real output)

```
$ cd analytics && ./.venv/Scripts/python.exe -m pytest -q
......................                                                   [100%]
22 passed in 19.43s
```

22 tests: `test_reach_model.py` (6, known analytic minimum-jerk cases: closed-form peak speed,
time-to-peak=50%, exactly 1 submovement, zero endpoint error, SPARC in expected smooth range,
LDLJ finite and ordering vs a jerky/tremor reach), `test_metrics_known_cases.py` (5, incl. 2
hypothesis property tests over random distance/MT/seed and submovement count, 25/20 examples
each, plus a fixed-formula LDLJ regression pin), `test_schema_validity.py` (7, one per profile +
one longitudinal week, asserting `load_session(..., validate=True).is_valid`),
`test_quality_dropout.py` (4: healthy mostly `ok`, noisy_tracking produces `degraded`/`invalid`,
session tracking_loss_pct > 50% under noisy_tracking, timeout trials flagged `invalid`).

```
$ ./.venv/Scripts/python.exe -m opus_analytics contracts/fixtures/sessions/healthy --quiet
wrote contracts\fixtures\sessions\healthy\metrics.json
```
(ran for all 6 profile fixtures + all 7 longitudinal weeks; zero schema-validation warnings
printed for any of them.)

## Contract change requests
None blocking. One note (also in `VALIDATION.md` §5): `docs/ARCHITECTURE.md` §2 lists
`metrics.schema.json` as a contract but it does not exist under `contracts/schemas/`. This
package defined its own `metrics.json` shape directly from the brief's "unit, method_version,
quality" requirement (documented in `analytics/README.md`). If Opus adds
`metrics.schema.json`, this shape should be reconciled with it.

## Known gaps / next steps
1. Submovement detector needs a real decomposition (not peak-counting) to be trustworthy for
   severe/noisy profiles -- see VALIDATION.md §1.2.
2. SPARC/LDLJ under dropout should probably be computed only on the *non-frozen* sub-span of a
   trial's movement window (currently computed over the whole padded window including any
   frozen frames) -- flagged in VALIDATION.md §2, left for a follow-up since fixing it changes
   the metric's effective duration/window semantics and deserves its own review.
3. No console-script entry point beyond `python -m opus_analytics` / `python -m
   synthetic_patients` (task says N4 CLI polish "can be minimal"; a working `python -m` command
   exists for both).
4. Did not build/tag `analytics-v0.1.0` -- orchestrator's job per common rule 6.

## Proposed CHANGELOG lines (for Opus)
- Added `sim/synthetic_patients` (deterministic minimum-jerk synthetic-patient generator, 6
  impairment profiles + 6-week longitudinal series) and `analytics/opus_analytics` (loaders,
  schema validation, segmentation, Butterworth filtering, SPARC/LDLJ + full Orchard Reach
  metrics set, quality flags), with `analytics/VALIDATION.md` reporting per-metric error vs
  ground truth and `contracts/fixtures/sessions/**` sample sessions + metrics.json for the app
  and Unity replay to consume.
