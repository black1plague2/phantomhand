# 2026-09-17 - Track N (analytics + synthetic patients) - run 2

Agent: Sonnet implementation agent, Track N. Picks up from `2026-09-14-N-analytics-sim.md`
(N1/N2/N3 baseline) per `docs/IMPROVEMENT_BRIEF.md` sec 1/5.5 and the run-2 task brief.

## Scope

Input: `analytics/opus_analytics`, `sim/synthetic_patients`, `contracts/fixtures/sessions/*`.
Touched only: `analytics/`, `sim/synthetic_patients/`, `contracts/fixtures/sessions/`, this log.
Did **not** touch `contracts/schemas/*` (confirmed unnecessary -- see CHECKPOINT 5).

## CHECKPOINT 1 -- read-in

Read `OPUS/CLAUDE.md`, `docs/IMPROVEMENT_BRIEF.md` (sec 1, sec 5.5), `analytics/VALIDATION.md`,
`analytics/README.md`, `contracts/schemas/metrics.schema.json`, and the run-1 log. Key finding:
`metrics.schema.json` **now exists** under `contracts/schemas/` (run-1's VALIDATION.md sec 5 said
it did not yet exist -- Opus must have added it since). Its `metricValue` and `session` object
definitions do not set `additionalProperties: false`, so extra keys validate without a schema
change -- this shaped most of the plan below (no schema edits needed anywhere).

Mid-task addition from Opus (Unity track): kinematics chunks had no `rot` for wrist joints,
blocking the demo hand mesh. Folded into this run since it touches the same generator files.

## CHECKPOINT 2 -- synthetic-patient knobs (`sim/synthetic_patients/`)

- `profiles.py`: added `tracking_rate_hz` knob (72.0) to all 6 existing profiles; added a new
  profile `quest_low_rate` (healthy biomechanics, `tracking_rate_hz=30.0`, isolating the rate
  effect from impairment/noise).
- `chunking.py`: `apply_rate_hold` -- zero-order-hold degrade on the true trajectory before
  jitter/dropout (frame count / t_ms unchanged, only density of new samples drops); wired into
  `apply_recording_noise`. `build_chunks` now takes `rate_hz` (declared device rate, written to
  the chunk's `rate_hz` field -- what `opus_analytics` reads for gating) separate from the
  internal 72Hz generation grid.
- `session.py` / `generate.py`: threaded `tracking_rate_hz` from profile -> `session.device` and
  `truth.json`; `generate.py` passes it to `build_chunks`.
- `orientation.py` (new): deterministic (no RNG draws -- per-trial phase decorrelation uses
  trial index, not randomness), seed-reproducible wrist/palm orientation synthesis
  (`synth_wrist_quats`) -- forward = direction of travel (forward-filled through pauses),
  pronation twist tied to the existing grasp-aperture profile, small tremor wobble reusing the
  position tremor band. `session.py` computes `TrialResult.wrist_rot` and assembles it into
  `l_wrist`/`r_wrist`/`l_palm`/`r_palm` (head/fingertips unchanged); `chunking.py` passes `rot`
  through unmodified (demo-visual metadata, feeds no metric) into the chunk's `frames[joint].rot`.

Verify (real output):
```
$ ./.venv/Scripts/python.exe -m pytest -q tests/test_wrist_orientation.py
...........
11 passed in 7.51s
```
Manual spot check: every `l_wrist`/`r_wrist`/`l_palm`/`r_palm` rot in a generated chunk has
`norm in [0.9999994, 1.0000006]`; `head` has no `rot` key (unchanged).

## CHECKPOINT 3 -- quality gating (`analytics/opus_analytics/quality.py`, `metrics/trial_metrics.py`)

- Every metric now carries `quality_reasons` (list of short machine strings) whenever quality is
  not a plain "ok" -- `metrics.schema.json`'s `quality_reasons` field existed but was unused
  before this run.
- New `quality.flag_smoothness_metric`: SPARC/LDLJ get an additional gate on top of the generic
  window quality (unchanged 5%/30% degraded/invalid rule):
  - `rate_hz < 45` (`RATE_HZ_MIN`, from the brief's own citation).
  - tracking loss in the movement window > `SPARC_LDLJ_DEGRADE_LOSS_FRAC = 0.15`, **derived**
    (not assumed) by `analytics/scripts/derive_dropout_threshold.py`: paired dropout-on vs
    dropout-off runs (same seed, "healthy" biomechanics so dropout is isolated from impairment),
    swept dropout_rate 0-35%, bin SPARC %% error by the trial's *realized* tracking-loss
    fraction. First bin whose mean error exceeds ~10%% was `[0.15, 0.18)` -- see VALIDATION.md
    sec 6 for the full table.
- Session-level `rate_hz` metric added (`session.metrics.rate_hz`, flagged `degraded` below
  45Hz) -- alongside the pre-existing root-level `sample_rate_hz` field from run 1, which already
  satisfied "propagate rate_hz into metrics.json" on its own; this makes it visible under
  `session.metrics` too, with its own quality flag.

Verify (real output):
```
$ ./.venv/Scripts/python.exe scripts/derive_dropout_threshold.py
realized_loss_frac_bin | n    | mean_sparc_pct_error | median
[0.00,0.02)            | 1388 | 0.00%                | 0.00%
[0.02,0.04)            | 1    | 0.92%                | 0.92%
[0.04,0.06)            | 0    | -                    | -
[0.06,0.08)            | 4    | 0.03%                | 0.03%
[0.08,0.10)            | 0    | -                    | -
[0.10,0.12)            | 5    | 0.35%                | 0.32%
[0.12,0.15)            | 0    | -                    | -
[0.15,0.18)            | 1    | 10.23%               | 10.23%
[0.18,0.22)            | 2    | 9.80%                | 9.80%
[0.22,0.26)            | 7    | 14.11%               | 12.93%
[0.26,0.30)            | 3    | 15.39%               | 14.70%
[0.30,1.01)            | 29   | 24.38%               | 7.08%

First bin whose mean SPARC error exceeds 10%: loss_frac >= 0.15
```

```
$ ./.venv/Scripts/python.exe -m pytest -q tests/test_quality_gating.py
........
8 passed in 82.30s
```

**Important finding beyond the brief's ask** (documented in VALIDATION.md sec 8, not acted on in
code -- flagged as a v0.2 follow-up instead, to keep this run's change scoped to what the brief
named): `peak_speed_mps` is *also* badly wrong under `quest_low_rate` (57.2% mean error, worse
than any impairment profile) -- a zero-order-hold discretization artifact in `np.gradient`, not
sensor noise. The brief's gate only named SPARC/LDLJ, so `peak_speed_mps`'s quality is currently
*not* downgraded by `rate_hz < 45`; recommend extending `flag_smoothness_metric`'s rate_hz check
to `peak_speed_mps` (and probably `time_to_peak_speed_pct`, `n_submovements`) in a follow-up.

## CHECKPOINT 4 -- trunk lean, neglect index MDC (`metrics/trial_metrics.py`, `metrics/session_metrics.py`)

- New per-trial metric `trunk_lean_cm`: max head displacement **toward the target** (signed
  projection onto the home->target horizontal bearing, clipped at 0), baseline = head position
  at trial start -- distinct from the pre-existing `trunk_displacement_cm` (max *radial*
  displacement from a target-shown baseline).
- New session-level `trunk_lean_cm` (mean of valid per-trial values) and MDC95 on both it and
  the pre-existing `neglect_index`. MDC95 = 1.96*sqrt(2)*SEM, SEM approximated by test-retest SD
  across 10 repeated same-profile synthetic sessions (24 trials each); pooled (mean) across
  healthy/mild/moderate/severe/left_neglect for one constant per metric, in
  `analytics/scripts/derive_mdc.py`.

Verify (real output):
```
$ ./.venv/Scripts/python.exe scripts/derive_mdc.py
profile        | metric           | n | mean | sd (SEM proxy) | MDC95
healthy        | neglect_index    | 10 | 0.000 | 0.000 | 0.000
healthy        | trunk_lean_cm_mean | 10 | 0.421 | 0.028 | 0.079
mild           | neglect_index    | 10 | -0.007 | 0.063 | 0.175
mild           | trunk_lean_cm_mean | 10 | 0.787 | 0.050 | 0.138
moderate       | neglect_index    | 10 | 0.020 | 0.136 | 0.378
moderate       | trunk_lean_cm_mean | 10 | 1.194 | 0.077 | 0.212
severe         | neglect_index    | 10 | 0.000 | 0.000 | 0.000
severe         | trunk_lean_cm_mean | 10 | 1.524 | 0.142 | 0.394
left_neglect   | neglect_index    | 10 | 0.837 | 0.109 | 0.301
left_neglect   | trunk_lean_cm_mean | 10 | 0.895 | 0.067 | 0.184

pooled mean MDC95(neglect_index) across profiles = 0.171
pooled mean MDC95(trunk_lean_cm_mean) across profiles = 0.202 cm
```
Constants shipped: `NEGLECT_INDEX_MDC95 = 0.171`, `TRUNK_LEAN_CM_MDC95 = 0.202` (both in
`opus_analytics/metrics/session_metrics.py`, attached as `mdc95` on the relevant metricValue
whenever it is computed).

```
$ ./.venv/Scripts/python.exe -m pytest -q tests/test_mdc.py
....
4 passed in ...s
```

## CHECKPOINT 5 -- schema / fixtures / full verification

Confirmed no schema change needed: `metricValue` and `session` in
`contracts/schemas/metrics.schema.json` do not set `additionalProperties: false`, so
`quality_reasons`, `mdc95`, `n`, and the session-level `rate_hz` metric all validate as-is.
**No contract change request.**

Regenerated all fixtures (`python -m synthetic_patients fixtures`, 7 profiles now incl.
`quest_low_rate`, + longitudinal weeks 00-06) and their `metrics.json`
(`python -m opus_analytics <dir>` per fixture). Also re-ran `run_validation.py` (found and fixed
a pre-existing bug unrelated to this run's changes: it read `trial["index"]`, but
`analyze.py`'s trial objects key on `"trial"`, not `"index"` -- this script could not have
produced a JSON report against the current `analyze.py` output shape until fixed).

Full test suite (real output):
```
$ cd analytics && ./.venv/Scripts/python.exe -m pytest -q
.................................................
49 passed in 159.52s (0:02:39)
```

Contract validation, every fixture session + the full fixtures/manifests suite (real output,
abbreviated -- ran individually for all 7 profiles + 7 longitudinal weeks, all `[PASS] All
validations passed`):
```
$ analytics/.venv/Scripts/python.exe contracts/validate.py --session contracts/fixtures/sessions/quest_low_rate
[PASS] quest_low_rate/session.json
[PASS] quest_low_rate/events.ndjson: 54 events validated
[PASS] quest_low_rate/kin_000.json
[PASS] quest_low_rate/kin_001.json
[PASS] quest_low_rate/kin_002.json
[PASS] quest_low_rate/kin_003.json
[PASS] quest_low_rate/metrics.json

[PASS] All validations passed

$ analytics/.venv/Scripts/python.exe contracts/validate.py
[PASS] invalid/session-envelope.2.json: correctly rejected
...
[PASS] All validations passed
```
(All 6 other profile sessions, all 7 longitudinal weeks, and the base fixtures/manifests suite
also individually re-run with the same `[PASS] All validations passed` result.)

Fixture size: `du -sh contracts/fixtures/sessions` = **13M** (budget: < 15MB). Grew from run 1's
6.8MB mainly from the new `rot` arrays (4 floats x 4 joints x every frame) and the 7th profile;
did not reduce trial counts further to stay under budget since 13M has headroom.

## Files touched

- `sim/synthetic_patients/profiles.py` (rate knob + `quest_low_rate` profile)
- `sim/synthetic_patients/chunking.py` (`apply_rate_hold`, `rot` passthrough, `build_chunks`
  `rate_hz` param)
- `sim/synthetic_patients/session.py` (rate_hz plumbing, `TrialResult.wrist_rot`, `ROT_JOINTS`
  assembly)
- `sim/synthetic_patients/generate.py` (thread `tracking_rate_hz` + `rot_true` through)
- `sim/synthetic_patients/orientation.py` (new)
- `analytics/opus_analytics/quality.py` (`quality_reasons`, `flag_smoothness_metric`,
  `RATE_HZ_MIN`, `SPARC_LDLJ_DEGRADE_LOSS_FRAC`)
- `analytics/opus_analytics/analyze.py` (pass `fs` to `compute_session_metrics`)
- `analytics/opus_analytics/metrics/trial_metrics.py` (`quality_reasons` everywhere, SPARC/LDLJ
  gate wiring, new `trunk_lean_cm`)
- `analytics/opus_analytics/metrics/session_metrics.py` (`rate_hz`, `trunk_lean_cm`, `mdc95` on
  `neglect_index`/`trunk_lean_cm`, MDC95 constants)
- `analytics/scripts/derive_dropout_threshold.py` (new), `analytics/scripts/derive_mdc.py` (new)
- `analytics/scripts/run_validation.py` (bugfix: `trial["trial"]` not `trial["index"]`)
- `analytics/tests/test_wrist_orientation.py`, `test_quality_gating.py`, `test_mdc.py` (new)
- `analytics/README.md`, `analytics/VALIDATION.md` (documented all of the above)
- `contracts/fixtures/sessions/**` (regenerated: 7 profiles incl. new `quest_low_rate` +
  longitudinal weeks 00-06, all with fresh `metrics.json`)
- Did **not** touch `contracts/schemas/*`.

## Contract change requests

None. See CHECKPOINT 5.

## Known gaps / next steps (for Opus / a follow-up N run)

1. `peak_speed_mps` (and likely `time_to_peak_speed_pct`, `n_submovements`) should probably get
   the same `rate_hz < 45` gate as SPARC/LDLJ -- VALIDATION.md sec 8 shows 57% mean error under
   `quest_low_rate`, not currently reflected in `quality`.
2. `SPARC_LDLJ_DEGRADE_LOSS_FRAC = 0.15` is evidence-based but the mid-range sweep bins
   (0.04-0.15 realized loss) were sparse (0-5 trials); a larger sweep (more seeds/trials) in
   `derive_dropout_threshold.py` would tighten the estimate.
3. MDC95 constants are pooled (mean) across profiles for a single conservative number; a
   consumer with per-diagnosis population data could refine per-profile (values ranged
   0.175-0.378 for neglect_index across mild/moderate/left_neglect, excluding the two
   zero-variance profiles -- see VALIDATION.md sec 7).
4. `n_submovements`'s known peak-counting limitation (run-1 finding) is unchanged by this run.
5. Analytics/`sim` package versions were not bumped despite the methodology changes (no test
   depended on the exact version string; flagging in case Opus wants a version bump policy).
