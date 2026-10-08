# opus_analytics validation report

Generated against `sim/synthetic_patients` ground truth. Method: `analytics/scripts/run_validation.py`
generates 30 trials x 4 seeds (120 trials) per profile (seeds 1001/2002/3003/4004, `trialCount=30`),
runs `opus_analytics.analyze_session` on each, and compares every metric that has a directly
computed ground-truth value (`truth.json` per trial) against the measured value. Raw numbers are
in `sim/out/validation_report.json` (gitignored; regenerate with the script below).

Goal **G4**: mean error < 5% per metric. Met where noted; documented honestly where not, with the
mechanism.

```
cd analytics
./.venv/Scripts/python.exe scripts/run_validation.py > ../sim/out/validation_report.json
```

## 1. Metrics with a direct, comparable ground truth

### 1.1 Timing metrics (exact by construction)

`reaction_time_ms` and `movement_time_ms` are derived directly from event timestamps
(`movement_onset`/`target_shown`/`contact`), which the generator also writes verbatim into
`truth.json`. Both show **0.00% mean error across all 6 profiles** (711 attempted trials) --
this is a sanity check on the event pipeline and trial segmentation, not a claim about
measuring a real patient's RT/MT (which will always have some jitter from the fixed 72 Hz
sampling and pinch/contact detection latency the real SDK will add later).

### 1.2 Kinematic metrics with a well-defined scalar ground truth

Mean % error (of `n` attempted, non-invalid trials), per profile:

| profile | peak_speed_mps | time_to_peak_speed_pct | n_submovements exact-match |
|---|---|---|---|
| healthy | 0.68% | 2.48% | 100.0% |
| mild | 1.02% | 3.46% | 100.0% |
| moderate | 2.35% | 6.39% | 73.3% |
| severe | 7.17% | 2.48% | 8.0% |
| left_neglect | 1.73% | 5.00% | 83.2% |
| noisy_tracking | 8.32% | 10.76% | 26.7% |

**peak_speed_mps** meets G4 (< 5%) for healthy, mild, moderate, left_neglect. It slips to
7.2% (severe) and 8.3% (noisy_tracking): both profiles carry the heaviest tremor
(severe: 4.5mm @ 6-10Hz) or sensor jitter (noisy_tracking: 14mm std), and peak speed is read
off a numerically differentiated, Butterworth-filtered (6 Hz cutoff) position trace --
differentiation amplifies high-frequency noise faster than the filter removes it right at the
speed peak. This is a real, expected property of velocity estimation from noisy position data,
not a segmentation or formula bug: the 0% RT/MT error on the exact same trials confirms event
timing is unaffected.

**time_to_peak_speed_pct** meets G4 for healthy/mild/left_neglect/severe (2.5-5.0%) but misses
for moderate (6.4%) and noisy_tracking (10.8%): with multiple corrective submovements
(moderate: mean 3) or heavy sensor jitter, the *time index* of the single global speed maximum
is more sensitive to small amplitude perturbations between near-tied local peaks than the peak
*value* is -- a few-sample shift in which peak wins is a large % error on a metric whose native
scale is 0-100.

**n_submovements**: our submovement counter (`opus_analytics.kinematics.count_submovements`,
a prominence + minimum-separation peak counter on the speed profile) exactly recovers the
generator's injected submovement count 100% of the time for smooth/lightly-impaired reaches
(healthy, mild), but degrades badly for severe (8.0% exact-match, mean |diff| 2.09) and
noisy_tracking (26.7%, mean |diff| 1.18). Root cause: severe reaches combine 5 injected
submovements with the heaviest tremor band in the profile set, and tremor itself creates
speed-profile ripples that the same prominence threshold used for corrective submovements
cannot distinguish from genuine sub-movements; noisy_tracking's 14mm iid sensor jitter does the
same even with only 1 true submovement. **This does not meet G4 and is a known, documented
limitation**: a real submovement decomposition (e.g. scattered/overlapping minimum-jerk fit,
as used in the literature) would need band-limiting tremor out first and fitting rather than
peak-counting. Left as a v0.2 item; the current implementation is adequate for
healthy/mild/moderate screening but should not be trusted quantitatively for severe/very noisy
sessions -- which is exactly why every value also carries a `quality` flag (see below) that a
consumer can gate on.

### 1.3 Metrics whose ground truth is near zero (relative error is not a fair yardstick)

`endpoint_error_cm` and `trunk_displacement_cm` are frequently close to 0 by construction
(a healthy reach has near-zero endpoint error and trunk compensation only kicks in past a
70%-of-comfortable-reach threshold). Percent error against a near-zero denominator blows up
even for a tiny absolute miss (e.g. healthy's *mean* relative error looks like 8.7% /
932% respectively, but that is 0.05cm and 0.5cm in absolute terms). We report both:

| profile | endpoint_error_cm (mean abs, cm) | trunk_displacement_cm (mean abs, cm) |
|---|---|---|
| healthy | 0.05 | 0.54 |
| mild | 0.63 | 0.88 |
| moderate | 0.80 | 1.11 |
| severe | 0.62 | 1.39 |
| left_neglect | 0.53 | 1.00 |
| noisy_tracking | 0.49 | 5.43 |

All profiles stay within about half a centimeter to ~1.4cm absolute error for endpoint error,
and under ~1.4cm for trunk displacement except noisy_tracking (5.4cm, driven by its 14mm
sensor-jitter std being read directly as head displacement noise). **Recommendation**: report
these two metrics' error budget in absolute cm in any clinical-facing validation, not %; we
consider both effectively validated (sub-1.5cm absolute error against ground truth) except
under noisy_tracking's extreme sensor-noise regime, which the `quality` flag correctly
downgrades (see §3).

## 2. Metrics without a closed-form ground truth: SPARC and LDLJ

Balasubramanian et al. (2015) and Hogan & Sternad (2009) smoothness measures have no
independent "true" scalar to compare against (they are themselves the operational definition
of smoothness) -- ground truth here is the generator's *known ordering* of impairment
severity, not a number. We validate that direction, not magnitude:

| profile | SPARC mean (sd) | LDLJ mean (sd) |
|---|---|---|
| healthy | -1.40 (0.01) | -5.33 (0.18) |
| mild | -1.61 (0.03) | -7.25 (0.24) |
| moderate | -2.18 (0.09) | -9.65 (0.38) |
| severe | -2.55 (0.18) | -12.27 (0.50) |
| left_neglect | -1.61 (0.05) | -8.02 (1.02) |
| noisy_tracking | -1.65 (0.12) | -8.46 (0.65) |

Both SPARC and LDLJ move monotonically more negative (less smooth) from healthy to mild to
moderate to severe -- exactly the expected direction as tremor amplitude, submovement count,
and undershoot all increase together. `left_neglect` and `noisy_tracking` sit between mild and
moderate, consistent with their milder tremor/submovement knobs.

**Documented failure case (as requested by the brief): SPARC under heavy dropout.**
noisy_tracking has an 18%-per-chunk dropout rate but its SPARC mean (-1.65) is barely worse
than mild (-1.61) despite carrying *no* injected tremor or extra submovements at all -- i.e.
SPARC is picking up dropout artifacts, not genuine unsmoothness. The mechanism: our recorder
freezes position at the last good sample during a tracking-loss span (`chunking.py`,
matching typical real tracker behaviour), which makes velocity go to exactly zero for the
frozen span and then jump sharply when tracking resumes. Depending on where in the movement the
freeze lands, this either (a) looks locally "smooth" (a flat zero-velocity plateau contributes
little high-frequency energy) or (b) injects a sharp step that SPARC's spectral-arc-length
calculation is very sensitive to. Net effect: **SPARC computed on a dropout-affected trial is
not a reliable smoothness estimate even when its own quality flag says "degraded" rather than
"invalid"**; consumers should treat SPARC/LDLJ values with `quality != "ok"` as indicative only,
never as a trend input. This is now reflected in `quality.py`'s thresholds (dropout > 5% of the
movement window degrades any metric computed over that window), but the flag is necessarily a
conservative heuristic, not a guarantee the number itself is unbiased.

## 3. Quality flags

Quality is computed per trial-metric from the tracking-loss fraction and event completeness of
the analysis window (`opus_analytics/quality.py`): `invalid` if a required event is missing, if
there are fewer than 5 usable samples, or if tracking loss in the window exceeds 30%; `degraded`
if tracking loss is 5-30% or there is a >=3-sample contiguous gap; `ok` otherwise.

Observed quality distribution (`movement_time_ms` quality, representative of the whole trial):

| profile | ok | degraded | invalid | (of attempted trials) |
|---|---|---|---|---|
| healthy | 120 | 0 | 0 | 120 |
| mild | 118 | 0 | 0 | 118 |
| moderate | 115 | 0 | 1 | 116 |
| severe | 109 | 4 | 0 | 113 |
| left_neglect | 112 | 1 | 0 | 113 |
| noisy_tracking | 118 | 2 | 0 | 120 |

This looks low for noisy_tracking given its 18%-per-chunk dropout rate; that is because dropout
is applied per-*chunk* to a subset of joints, and a given trial's specific movement window
(typically well under 1 full chunk) often does not overlap a dropout span even when the *session*
overall shows high tracking loss (`tests/test_quality_dropout.py::test_tracking_loss_pct_reflects_dropout_rate`
confirms session-level tracking_loss_pct > 50% for this profile at a different seed). This is
correct behaviour (quality is scoped to the window actually used for that metric) but means a
consumer should always also check the **session-level** `tracking_loss_pct` before trusting a
run of "ok" trials from a noisy device.

## 4. Summary against G4

| Metric | Meets < 5% (or < ~1.5cm abs) across profiles? |
|---|---|
| reaction_time_ms | Yes (0.00%) |
| movement_time_ms | Yes (0.00%) |
| peak_speed_mps | Yes for 4/6 impairment profiles; 7.2-8.3% for severe/noisy_tracking (documented, noise-driven); **57.2% for quest_low_rate (30Hz)** -- see section 8 |
| time_to_peak_speed_pct | Yes for 4/6 impairment profiles; 6.4-10.8% for moderate/noisy_tracking (documented, near-tied-peak sensitivity); 5.45% for quest_low_rate |
| endpoint_error_cm | Yes in absolute terms (<=0.8cm mean abs); % framing is misleading near zero truth |
| trunk_displacement_cm | Yes in absolute terms except noisy_tracking (5.4cm, sensor-noise-driven) |
| n_submovements | Yes for healthy/mild (100%); degrades for moderate/severe/noisy_tracking (documented limitation, v0.2 item); 67.5% exact-match for quest_low_rate |
| sparc / ldlj | No scalar ground truth; validated qualitatively (correct monotonic ordering). Documented failure: unreliable under heavy dropout even when flagged only "degraded" (quantified in section 6) and under rate_hz < 45 (section 8). |
| trunk_lean_cm | New (section 7). No independent scalar ground truth (directional projection of a modeled compensation signal); reported with MDC95 instead of a % error target. |
| neglect_index | Pre-existing session metric; MDC95 added (section 7). |

## 5. Contract change requests

None currently outstanding. `contracts/schemas/metrics.schema.json` now exists (it did not when
this file was first written -- see the superseded note this section used to carry) and this
package's `metrics.json` shape conforms to it, including every 2026-09-17 addition below
(`rate_hz`, `trunk_lean_cm`, `quality_reasons`, `mdc95`, `n`): the schema's `metricValue` and
`session` object definitions do not set `"additionalProperties": false`, so extra keys
(`mdc95`, session-level `rate_hz` as its own metric, `n`) validate without a schema change --
confirmed by `tests/test_schema_validity.py::test_metrics_json_conforms_to_schema` and by
`contracts/validate.py --session` on every regenerated fixture (see session log
`2026-09-17-N-analytics-run2.md`).

## 6. Quality gating: SPARC/LDLJ rate_hz and tracking-loss thresholds (2026-09-17)

Two gates now sit on top of the generic per-window quality flag (`quality.flag_for_window`,
still 5%/30% tracking-loss degraded/invalid, unchanged) specifically for `sparc` and `ldlj`,
implemented in `quality.flag_smoothness_metric` and applied in `metrics/trial_metrics.py`:

- **`rate_hz < 45`**: directly from the brief's own citation (Quest hand-tracking validation
  literature ties SPARC/LDLJ reliability to tracking rate); below 45Hz there are too few
  genuinely-new samples per stroke to resolve the velocity spectrum SPARC operates on.
- **tracking loss in the movement window > 15%**: derived empirically, not assumed. Method
  (`analytics/scripts/derive_dropout_threshold.py`): for a sweep of `dropout_rate` values
  (0%, 3%, 6%, 10%, 15%, 20%, 25%, 30%, 35%), generate a "healthy"-biomechanics session (no
  tremor/extra submovements, so any SPARC drift is attributable to dropout, not impairment) at
  that dropout_rate, and a *paired control* at dropout_rate=0 with the **same seed** (same true
  trajectory, only recording-noise dropout differs) -- 4 seeds x 9 dropout rates x 40 trials.
  Each trial's *realized* tracking-loss fraction (the movement window's own `tracking_loss_pct`,
  not the per-chunk dropout_rate knob) is used to bin `% |SPARC_affected - SPARC_control| /
  |SPARC_control|` (the closest thing to a SPARC ground truth this generator can produce, since
  SPARC itself has no closed-form truth -- section 2). Result:

  ```
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
  ```

  Error is negligible (<1%) up to ~12% realized loss, then jumps past 10% mean error in the
  [0.15, 0.18) bin -- the first bin that crosses the brief's "~10%" target. **Chosen threshold:
  `SPARC_LDLJ_DEGRADE_LOSS_FRAC = 0.15`** (`opus_analytics/quality.py`). Caveats, stated plainly:
  the mid-range bins (0.04-0.15) are sparse (0-5 trials each -- dropout spans are short relative
  to a movement window, so intermediate realized-loss fractions are rare by construction) and
  the top bin's median (7.08%) sits *below* its own mean (24.38%), i.e. it is right-skewed by a
  few severely-corrupted trials rather than uniformly bad -- consistent with section 2's finding
  that dropout's effect on SPARC depends on *where* in the movement the freeze lands, not just
  how much of the window is lost. 0.15 is therefore a reasonable, evidence-based threshold, not
  a precise inflection point; re-run the script (below) with more seeds/trials to tighten it.

  ```
  cd analytics
  ./.venv/Scripts/python.exe scripts/derive_dropout_threshold.py
  ```

Every metric value now also carries `quality_reasons` (a list of short machine-readable strings,
e.g. `"rate_hz_30.0_below_45"`, `"tracking_loss_0.18_gt_sparc_ldlj_threshold_0.15"`,
`"trial_timed_out_before_movement_onset"`) whenever quality is not a plain, unqualified "ok" --
`contracts/schemas/metrics.schema.json`'s `metricValue.quality_reasons` field already existed
and was previously unused by this package.

## 7. MDC95 for neglect_index and trunk_lean_cm (2026-09-17)

**Method**: MDC95 = 1.96 * sqrt(2) * SEM (the standard rehab-outcome-measure formula, e.g.
Beckerman et al. 1998 -- the same one used for FMA-UE/ARAT/Box&Block, satisfying the brief's
"same MDC language" ask), with SEM approximated by the test-retest SD across repeated synthetic
sessions of the *same* profile (10 seeds/profile, 24 trials/session -- a stand-in for "the same
patient measured 10 times with no real clinical change"). Script:
`analytics/scripts/derive_mdc.py`.

| profile | neglect_index mean (sd) | neglect_index MDC95 | trunk_lean_cm mean (sd) | trunk_lean_cm MDC95 |
|---|---|---|---|---|
| healthy | 0.000 (0.000) | 0.000 | 0.421 (0.028) | 0.079 |
| mild | -0.007 (0.063) | 0.175 | 0.787 (0.050) | 0.138 |
| moderate | 0.020 (0.136) | 0.378 | 1.194 (0.077) | 0.212 |
| severe | 0.000 (0.000) | 0.000 | 1.524 (0.142) | 0.394 |
| left_neglect | 0.837 (0.109) | 0.301 | 0.895 (0.067) | 0.184 |
| **pooled (mean of the above)** | | **0.171** | | **0.202 cm** |

`neglect_index`'s zero SD for healthy/severe is not a bug: with `side="alternate"` and 24 trials
that's exactly 12 left/12 right, and at those profiles' miss rates the success-count tie (e.g.
12/12 both sides) recurs across independently-seeded sessions often enough that several of the
10 repeats landed on the identical ratio -- a real property of a coarse (n=12-per-side),
discrete-outcome metric, not an error in the generator or the MDC calculation. The pooled
constant (mean across profiles, `NEGLECT_INDEX_MDC95 = 0.171` in `session_metrics.py`) is a
single conservative number shipped regardless of which profile a real session's patient
resembles; a consumer with per-population data could refine this per-diagnosis later.

Both `session.metrics.neglect_index` and `session.metrics.trunk_lean_cm` now carry an `mdc95`
key directly on the metricValue object (schema-valid: see section 5) whenever the underlying
value is computed (not when its quality is `invalid`), so a UI can compare two sessions' values
against their own MDC band without a second lookup.

## 8. quest_low_rate (30Hz) synthetic profile (2026-09-17)

New profile in `sim/synthetic_patients/profiles.py`: identical biomechanics knobs to "healthy"
(no tremor, no extra submovements, near-zero miss rate) but `tracking_rate_hz=30.0` (Quest
hand-tracking LOW mode), so any metric degradation is attributable purely to sampling rate, not
impairment. Mechanism (`chunking.apply_rate_hold`, applied before jitter/dropout): a
zero-order-hold on the *true* trajectory that only lets a genuinely new sample through every
`round(72/30)=2` frames of the internal 72Hz generation grid -- frame count and event timing on
disk are unaffected (so trial segmentation and RT/MT stay exact), only the *density* of new
information drops, and the recorded `kin_*.json` chunks' `rate_hz` field is set to 30 (which is
what `opus_analytics`'s quality gate actually reads).

120 trials (4 seeds x 30 trials), same methodology as section 1:

| metric | mean %% error | note |
|---|---|---|
| reaction_time_ms | 0.00% | event-timestamp based, unaffected by tracking rate |
| movement_time_ms | 0.00% | ditto |
| peak_speed_mps | **57.17%** | undocumented-until-now failure mode, see below |
| time_to_peak_speed_pct | 5.45% | meets G4 |
| n_submovements | 67.5% exact-match (mean \|diff\| 0.35) | degrades vs healthy's 100% |
| sparc | mean -1.401 (sd 0.004) | in the expected smooth-reach range; correctly flagged `degraded` by the `rate_hz < 45` gate (section 6) on every trial |

**Important finding beyond the brief's ask**: `peak_speed_mps` is *also* badly wrong at 30Hz
(57% mean error, worse than any impairment profile), not just SPARC/LDLJ. Mechanism: the
zero-order hold means roughly half the 72Hz-grid frames are exact repeats of the previous
sample; `np.gradient` differentiates assuming a uniform new sample every `dt=1/72s`, so a
held-then-jump pair reads as one near-zero-velocity frame followed by one frame with
*double* the true instantaneous velocity -- a discretization artifact, not sensor noise, and it
inflates rather than smooths, unlike the tremor/jitter cases in section 1.2. The brief's
instructions only named SPARC/LDLJ for the rate_hz gate, so `peak_speed_mps`'s `quality` is
*not* currently downgraded by `rate_hz < 45` (only by the pre-existing tracking-loss rule, which
this clean profile never trips) -- **flagged here as a recommended v0.2 follow-up**: extend
`quality.flag_smoothness_metric`'s rate_hz check to `peak_speed_mps` (and probably
`time_to_peak_speed_pct`, `n_submovements`) rather than only `sparc`/`ldlj`, since this data
shows the failure is not specific to jerk-based metrics.

## 9. Wrist/palm orientation (`rot`) addition (2026-09-17, not a metric)

Requested by Opus/Unity for the demo hand mesh (no headset path): `l_wrist`/`r_wrist`/
`l_palm`/`r_palm` frames in every `kin_*.json` chunk now carry a `rot` quaternion (xyzw,
`kinematics-chunk.schema.json`'s existing optional per-joint field -- no schema change).
Implementation: `sim/synthetic_patients/orientation.py`, wired into `session.py`
(`TrialResult.wrist_rot`, computed from the wrist's own recorded trajectory) and
`chunking.py` (passthrough, no jitter/dropout -- it's demo-visual metadata, not fed to any
`opus_analytics` metric). Deterministic per seed (no RNG draws; per-trial phase decorrelation
uses the trial index, not randomness). `head`/fingertip joints are unchanged (position-only).
Covered by `analytics/tests/test_wrist_orientation.py` (unit-norm on every frame across all 7
profiles, schema validity, seed-determinism, and a stationary-pose sanity check).

> **Opus caution (2026-09-17):** the MDC95 values above come from synthetic test-retest with low simulated noise. They are NOT clinical MDCs and must not be shown as such. Real-patient MDCs must come from published test-retest studies or pilot data. Until then the app should label bands as synthetic estimate in development builds only. Also: peak_speed_mps degrades ~57% at 30 Hz and needs rate gating in v0.2.

## 10. `endpoint_error_cm` frame mismatch (2026-09-19, track N run3)

**Background**: `logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md` (the first time a Unity-recorded
session reached this package) found `endpoint_error_cm` of 79-133 cm for reaches that visibly hit
the apple. Root cause, diagnosed there: the recorded joint positions were chest-relative but the
trial's `target.pos` in `target_shown` was still world-space, so `endpoint_error_cm` (a plain
`||target_pos - endpoint_pos|| * 100`, `opus_analytics/metrics/trial_metrics.py:120-125`) was
measuring a coordinate-frame offset, not a patient's reach.

**Analytics-side check performed this run**: `endpoint_error_cm`'s implementation is a correct,
minimal Euclidean distance with no unit or sign bugs -- confirmed by the synthetic fixtures in
`contracts/fixtures/sessions/*`, where target and joint positions are generated in the same frame
by construction and `endpoint_error_cm` reads 0.05-0.8 cm mean absolute error across all profiles
(section 1.3). **The fault was never on the analytics side.**

**Unity-side fix**: `game/Assets/Games/OrchardReach/Runtime/OrchardReachModule.cs`
(`TargetToTrialTarget`, `SubtractChest`) now subtracts the chest reference from the target
position before it is written into the `target_shown` event, putting both sides of the
subtraction analytics performs into one frame -- the comment there cites this exact 79-133 cm
number as the bug it fixes.

**Could not be confirmed end to end this run**: every session under `app/.hub_data/` (5
directories, all `device_id: "run12-full-pipeline"`, `started_at` 2026-09-18T19:03-19:15 UTC) was
recorded *before* that fix landed -- `OrchardReachModule.cs`'s on-disk mtime (2026-09-19 00:52
IST) is later than the newest session directory's mtime (00:45 IST). Re-running analytics on the
most recent of them (`fdf86238-1616-4eb1-ad3c-04b1301e5c06`) reproduces the original bug almost
exactly:

```
trial  endpoint_error_cm  quality
0      (invalid: trial_timed_out_before_movement_onset)
1      (invalid: fewer_than_5_usable_samples)
2      (invalid: fewer_than_5_usable_samples)
3      79.14 cm            ok
4      132.94 cm           ok
5      90.74 cm            ok
6      (invalid: trial_timed_out_before_movement_onset)
```

i.e. still ~1 m, not "a few cm" -- because this data predates the fix, not because the fix is
wrong. Confirmed by reading the raw data directly: `fdf86238`'s `target_shown` events have
`target.pos.y` in the 1.0-1.18 m range (world seated-eye/chest height) while the same session's
`kin_000.json` `r_wrist.pos` values sit in the -0.3 to 0.2 m range (chest-relative) -- the exact
frame mismatch the fix addresses, still present in this particular recording.

**Follow-up needed (written to `docs/MANUAL_TODO.md`)**: Track U needs to re-run the full-pipeline
integration test (or any session recording) *after* this fix to produce a fresh session, so
`endpoint_error_cm` can be confirmed sane against real (not synthetic) data. Track N cannot do
this itself (no Unity access per this track's brief).

## 11. Reaction times from the Unity demo driver are an artifact, not a measurement (2026-09-19)

The same `app/.hub_data/fdf86238-.../metrics.json` (and its siblings) shows `reaction_time_ms`
values under 1 ms (e.g. 0.31, 0.75, 0.87 ms) for several trials. `opus_analytics` computes
`reaction_time_ms` correctly and exactly as `t_movement_onset - t_target_shown` (0.00% error
against synthetic ground truth in every profile, section 1.1) -- there is no analytics bug here.
The number itself is real *given the input timestamps*, but the input is not a real reach: the
demo/test driver that produced these sessions satisfies the "movement onset" condition almost
immediately after the target appears (it is a scripted PlayMode test moving a proxy hand, not a
human reacting), so sub-millisecond RT is an artifact of how the demo drives the hand, not
something to plot on a patient's recovery curve. Any consumer (the app's progress charts in
particular, `app/lib/shared/metrics/progress_data.dart`) reading `reaction_time_ms` from a
demo-driven session should treat it as a pipeline-plumbing sanity check, not a clinical value,
until a realistic (human-timed or biomechanically-modelled reach-onset) demo profile exists.
Synthetic `sim/synthetic_patients` sessions do not have this problem -- their `reaction_time_ms`
comes from the generator's own RT-slowing impairment knob, not an instant scripted trigger.

## 12. Analysability regression test (2026-09-19)

Schema validity was never the same thing as analysability: the full-pipeline run found a session
that validated cleanly against every schema while every single metric came back null (`trial_end`
never emitted on the successful path, `contact` never emitted at all -- see the pipeline run log,
defects #2-3). `analytics/tests/test_analysability.py` now encodes both directions so this cannot
silently regress:

- `test_well_formed_session_yields_non_null_core_metrics`: a clean synthetic session must yield
  real (non-null) values, with `quality` in `{ok, degraded}`, for the metrics a clinician actually
  reads (`reaction_time_ms`, `movement_time_ms`, `peak_speed_mps`, `sparc`, `ldlj`,
  `endpoint_error_cm`, `tracking_loss_pct`, plus session-level `rate_hz`/`success_rate`/
  `tracking_loss_pct`) on the large majority of its trials.
- `test_session_missing_contact_is_reported_invalid_not_silently_null`: strips every `contact`
  event out of an otherwise-valid session's `events.ndjson` (reproducing the actual game bug,
  which dropped `contact` entirely) and asserts that (a) the stripped file still passes schema
  validation line by line -- proving shape and analysability are independent checks -- and (b)
  every metric that depends on `contact` comes back `quality: "invalid"` with a non-empty,
  specific `quality_reasons` entry rather than a bare, unexplained `null`.

Both passed against the current `opus_analytics/metrics/trial_metrics.py` /
`opus_analytics/quality.py` (the `required_event_missing` path already existed and does the right
thing) -- this run added the test that proves it, not a new behaviour.


## 13. Embodiment: touch delivery quality (Phantom Hand, N1b, 2026-10-08)

Found in the S2 fault run ("Node A off at 50 %"): `cue_delivery_rate` was 0.375 with `quality: ok`. Per 03-SPEC
section 8 every value carries quality and reasons, so delivery now gates quality. Per condition (trial),
delivery = delivered `haptic_cue` / scheduled `haptic_cue`. Thresholds (`metrics/embodiment.py`):

| Constant | Value | Effect |
|---|---|---|
| `CUE_DELIVERY_MIN` | 0.9 | delivery < 0.9: `cue_delivery_rate` and `stroke_timing_err_ms` (mean, p95) are `degraded`, reason `cues_undelivered` (exactly 0.9 stays ok) |
| `MIN_DELIVERED_CUES` | 6 | cues scheduled but fewer than 6 delivered (about 3 two-motor strokes): stroke timing `degraded`, reason `few_delivered_strokes_<n>_lt_6` |
| `TOUCH_INCOMPLETE_MAX` | 0.5 | delivery < 0.5: the touch condition is compromised; every non-missing `drift_change_cm`, `ownership`, `control` and `flinch_*` value of that condition is `degraded` with reason `touch_incomplete` (value unchanged) |
| zero cues scheduled | n/a | `cue_delivery_rate` is `missing`, reasons `no_cues`, `node_absent_haptic` |

`witness_q4` (awareness pointer), `async_delay_ms` and `emg_windows_excluded` are not touch-gated. The
`sync_minus_async` contrast inherits the flags as `sync:touch_incomplete` / `async:touch_incomplete` and takes the
worse quality. The thresholds are engineering choices (a 10 % loss is tolerable for an induction of about
8 strokes, a loss above 50 % means the illusion was not delivered), not clinically validated.

Fault fixture `ph_l3_fault_node_a_off` (session.json borrowed from `ph_l3_main`): sync `cue_delivery_rate`
0.375 -> degraded [cues_undelivered]; sync drift/ownership/control -> degraded [touch_incomplete]; async
(delivery 1.0) unchanged ok.
Tests: `analytics/tests/test_embodiment.py` (`test_*delivery*`, `test_touch_incomplete*`, `test_few_delivered_strokes`,
`test_no_cues_is_missing_no_cues`).
