# Track N — analytics + synthetic patients, run 3 (2026-09-19)

Scope: `analytics/`, `sim/`, this log only. Did not touch `game/` or `app/`, did not run Unity, did not
`git commit`/edit `CONTEXT.md`/`logs/CHANGELOG.md`/`releases/VERSIONS.md`.

Context read first: `CLAUDE.md`, `CONTEXT.md` ★ START HERE box, `docs/agent-briefs/N-analytics-sim.md`,
`analytics/VALIDATION.md`, `logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md`.

## CHECKPOINT 1 — Task 1: longitudinal `patient_ref` bug

Root cause found in `sim/synthetic_patients/session.py`'s `generate_session`: the session envelope
hardcoded `"patient_ref": f"synthetic-{profile_name}-{seed}"` with no way to override it.
`sim/synthetic_patients/longitudinal.py`'s `write_longitudinal_series` accepted a `patient_ref` parameter
but never passed it down to `write_session` — it only used it for `summary.json`'s top-level field. Every
week's `session.json` therefore got a different id (`synthetic-longitudinal-9000`, `-9100`, ... one per
week, derived from `base_seed + week*100`), which is exactly why the app has to work around it
(`app/lib/shared/metrics/progress_data.dart`'s `progressDataProvider` groups sessions via
`sessionsRepo.listSessionsForPatient(patientId)` — i.e. it depends on session envelopes actually sharing
one `patient_ref` per patient; verified this by reading, not editing, that file).

**Fix** (3 files, all backward-compatible via `patient_ref: str | None = None` defaults):
- `sim/synthetic_patients/session.py`: `generate_session(...)` takes `patient_ref` and uses it when set,
  falling back to the old `synthetic-{profile}-{seed}` scheme otherwise.
- `sim/synthetic_patients/generate.py`: `write_session(...)` takes `patient_ref` and threads it through.
- `sim/synthetic_patients/longitudinal.py`: `write_longitudinal_series` now actually passes its own
  `patient_ref` parameter into every `write_session` call (previously it silently dropped it).

**Fixtures regenerated**: `./analytics/.venv/Scripts/python.exe -m synthetic_patients fixtures --out
contracts/fixtures/sessions --trials 6` (via `-c "import sys; sys.path.insert(0,'sim'); ..."` since
`synthetic_patients` isn't installed as a package on that venv's path). This regenerates every fixture
(random `session_id` each run, harmless diff noise), so I `git checkout --`-reverted the 7 non-longitudinal
profile directories (only their random `session_id` changed, confirmed by diff — `patient_ref` for those
was never affected since they don't go through `write_longitudinal_series`) and kept only
`contracts/fixtures/sessions/longitudinal/` regenerated.

Verified:
```
$ for f in contracts/fixtures/sessions/longitudinal/week_0*/session_0/session.json; do
    python -c "import json; print('$f', json.load(open('$f'))['patient_ref'])"; done
...week_00/session_0/session.json synthetic-longitudinal-001
...week_01/session_0/session.json synthetic-longitudinal-001
...week_02/session_0/session.json synthetic-longitudinal-001
...week_03/session_0/session.json synthetic-longitudinal-001
...week_04/session_0/session.json synthetic-longitudinal-001
...week_05/session_0/session.json synthetic-longitudinal-001
...week_06/session_0/session.json synthetic-longitudinal-001
```
All 7 now share one `patient_ref`, matching `summary.json`'s own `patient_ref` field (which was already
correct — only the per-session envelopes were wrong).

Each regenerated week validated individually:
```
$ for d in contracts/fixtures/sessions/longitudinal/week_*/session_0; do
    ./analytics/.venv/Scripts/python.exe contracts/validate.py --session "$d"; done
[PASS] session_0/metrics.json   (x7, one per week)
[PASS] All validations passed   (x7)
```
Full `contracts/validate.py` (no args) also passes (see CHECKPOINT 4).

**Not touched, flagged instead** (`docs/MANUAL_TODO.md`): `app/assets/fixtures/sessions/longitudinal__*`
is a separate, Flutter-owned copy of longitudinal fixtures (flattened filenames + a `traces.json` this
track doesn't produce) generated before this fix, still carrying the old per-session `patient_ref`
scheme (confirmed via `progress_data.dart`'s own comment citing patient id
`synthetic-longitudinal-9000`). Regenerating it is Track A's call, not mine (`app/` is off-limits) —
left as an actionable MANUAL_TODO item with the exact command.

## CHECKPOINT 2 — Task 2: `endpoint_error_cm` sanity check

Read `analytics/opus_analytics/metrics/trial_metrics.py:120-125`: `endpoint_error_cm` is a plain
`||target_pos - endpoint_pos|| * 100` — no analytics-side bug. Confirmed via
`contracts/fixtures/sessions/*` (synthetic, same-frame by construction): 0.05-0.8 cm mean absolute error
across all profiles (already documented, VALIDATION.md section 1.3, unchanged by this run).

Checked the real recorded session data at `app/.hub_data/` (5 dirs, all `device_id:
"run12-full-pipeline"`, `started_at` 2026-09-18T19:03-19:15 UTC — the most recent is
`fdf86238-1616-4eb1-ad3c-04b1301e5c06` at 19:15:36). Ran:
```
$ ./analytics/.venv/Scripts/python.exe -m opus_analytics ../app/.hub_data/fdf86238-...
```
`endpoint_error_cm` values: trial 3 = 79.14 cm, trial 4 = 132.94 cm, trial 5 = 90.74 cm (others invalid —
timeouts / too few samples). **Still ~1 m, not a few cm** — essentially unchanged from the
FULL-PIPELINE-RUN.md log's 79.15 / 132.94 / 90.59.

Investigated why, since the Unity side is supposed to have fixed this (per this run's brief). Read (not
edited) `game/Assets/Games/OrchardReach/Runtime/OrchardReachModule.cs`: `TargetToTrialTarget()` now calls
`SubtractChest(_currentPlacement.ToArray())` before writing the target into the event, with a comment
explicitly citing "79-133 cm" as the bug this fixes. So the fix **is** in the code. But:
- Raw `fdf86238` data: `target_shown.target.pos.y` is 1.0-1.18 (world seated chest/eye height);
  `kin_000.json`'s `r_wrist.pos` is in the -0.3 to 0.2 range (chest-relative). Still the pre-fix frame
  mismatch.
- File mtimes: `OrchardReachModule.cs` was last modified 2026-09-19 00:52:09 IST; every `app/.hub_data/`
  session directory's `session.json` predates that (`fdf86238` itself is 00:45:46 IST, ~7 minutes earlier).

**Conclusion**: the fault was never on the analytics side (confirmed correct against synthetic
ground-truth), and the Unity-side fix looks correct by reading the code, but no session has been recorded
*after* that fix landed to confirm it end to end — every real session on disk predates it. Wrote this up
in `analytics/VALIDATION.md` section 10 with the actual before numbers, and a `docs/MANUAL_TODO.md` item
asking Track U to re-run the full-pipeline test so this can be confirmed against fresh data. Did not run
Unity myself (out of scope for this track).

Also documented (VALIDATION.md section 11): the same session's `reaction_time_ms` values under 1 ms
(0.31-0.87 ms) are a demo-driver artifact, not real reaction times — `reaction_time_ms` itself is computed
correctly (0% error vs. synthetic ground truth); the input timestamps are just from a scripted PlayMode
test that satisfies "movement onset" almost instantly.

## CHECKPOINT 3 — Task 3: analysability regression test

Added `analytics/tests/test_analysability.py`, two tests:
- `test_well_formed_session_yields_non_null_core_metrics`: generates a clean `healthy` synthetic session,
  asserts >80% of trials have non-null values (quality in {ok, degraded}) for
  reaction_time_ms/movement_time_ms/peak_speed_mps/sparc/ldlj/endpoint_error_cm/tracking_loss_pct, and that
  session-level rate_hz/success_rate/tracking_loss_pct are non-null.
- `test_session_missing_contact_is_reported_invalid_not_silently_null`: generates a session, strips every
  `contact` event from `events.ndjson` (reproducing the actual game bug from FULL-PIPELINE-RUN.md), asserts
  the stripped events.ndjson **still validates against `event.schema.json` line by line** (proving schema
  validity and analysability are independent — the crux of the regression), then asserts every
  contact-dependent metric comes back `quality: "invalid"` with a non-empty `quality_reasons` explaining
  why, never a bare unexplained `null`.

Both new tests exercise existing code paths (`opus_analytics/quality.py`'s `required_event_missing` /
`flag_for_window`, already correct) — this adds coverage that would have caught the original defect, it
doesn't change behaviour.

```
$ ./analytics/.venv/Scripts/python.exe -m pytest tests/test_analysability.py -v
tests/test_analysability.py::test_well_formed_session_yields_non_null_core_metrics PASSED
tests/test_analysability.py::test_session_missing_contact_is_reported_invalid_not_silently_null PASSED
2 passed in 62.21s
```
(A `Windows fatal exception: code 0x8007000e` printed above the test run is from `hypothesis`'s WMI probe
in `platform.py` at collection time on this machine — cosmetic, pytest still reports a clean pass; it
predates this session's changes and appears on every run in this venv.)

## CHECKPOINT 4 — Full verification

```
$ ./analytics/.venv/Scripts/python.exe -m pytest -q          (analytics/)
51 passed in 117.62s

$ ./.venv/Scripts/python.exe -m pytest -q                     (sim/live/)
26 passed in 0.39s

$ ./.venv/Scripts/python.exe -m pytest -q                     (sim/haptic/)
14 passed in 1.38s

$ ./analytics/.venv/Scripts/python.exe contracts/validate.py
[PASS] All validations passed
```
49 -> 51 in analytics (the 2 new tests); sim/live and sim/haptic unchanged (26, 14) as expected — this
run touched neither.

## Task 4: VALIDATION.md updated

Added sections 10-12 to `analytics/VALIDATION.md`:
- §10: `endpoint_error_cm` frame mismatch — analytics-side confirmation it's correct, the Unity fix, the
  actual before numbers from real data, and why it's still unconfirmed end to end (fix postdates every
  recorded session).
- §11: reaction times from the Unity demo driver are a driver artifact, not a measurement.
- §12: the new analysability regression test and what it guards against.

## What's done / not done

- Task 1: **done**, verified (patient_ref shared across all 7 longitudinal fixture weeks, schema-valid,
  full suites green).
- Task 2: **done** as an analytics-side investigation — confirmed analytics is not at fault, confirmed the
  Unity fix exists in code, but could not confirm it end-to-end against fresh data because no session has
  been recorded since the fix landed (needs a Unity re-run, written to `docs/MANUAL_TODO.md`; out of this
  track's scope to do itself).
- Task 3: **done**, new test file passes, added to the suite (49 -> 51).
- Task 4: **done**, VALIDATION.md updated.

## Contract change requests

None. `contracts/schemas/*` were not touched and nothing in this run's findings implies they should be.

## Docs found wrong / worth a second look

- `docs/agent-briefs/N-analytics-sim.md`'s status header still says "N4 ... Tag `analytics-v0.1.0`" in the
  task list even though its own status line at the top says v0.2.0 is current — pre-existing, cosmetic,
  didn't touch it (not asked to, and it's clearly documented as stale by the status line right above it).
