# Session log: Track A (Flutter app) -- run 8 -- 2026-09-18

Resuming from `logs/sessions/2026-09-18-A-flutter-run7.md` (committed b5241c2, last verified: analyze
0/0, 32/32 goldens, haptics session report correct). Opus rejected the patient profile:
`logs/sessions/screens/app/run7/patient_profile_desktop_light_1.0x.png` showed the Recovery/Sessions/
Outcome measures sections as solid blue full-width bars, and the summary line ("No side affected,
Post-stroke (ischemic, MCA), no residual deficit, 45 weeks since onset") was clinically incoherent.
PATH: `C:\flutter\bin` prepended in every shell this run.

## CHECKPOINT 1 -- root cause of the blue bars

Read `app/lib/features/patients/patient_profile_screen.dart` and
`app/test/goldens/screen_goldens_test.dart`. The "bars" were not a broken chart or an unresolved
skeleton placeholder in production code -- they were **`LinearProgressIndicator`s stuck mid-load**,
a golden-harness bug, not a widget bug:

- `_RecoveryLine`, `_OverviewSessionsStrip`, `_OverviewOutcomesSummary` each watch their own
  `FutureProvider` and show a `LinearProgressIndicator` while loading (correct, intentional code).
- `_goldenFor`'s wait loop (`screen_goldens_test.dart`) only checked
  `find.byType(CircularProgressIndicator).evaluate().isEmpty` before snapping the golden. That
  indicator is `PatientProfileScreen`'s *outer* `patientAsync.when(loading: ...)` spinner. Once the
  outer patient future resolves (pump 1), the loop broke immediately -- before the *nested*
  `FutureProvider`s inside the newly-built overview tab (one more async hop below the outer one) had
  a chance to resolve and repaint. Those nested widgets show `LinearProgressIndicator`, which the old
  check never looked for, so the golden was captured while 4 (Recovery) + 1 (Sessions) + 1 (Outcome
  measures) `LinearProgressIndicator`s were still on screen -- exactly the 4/1/1 solid bars in the
  rejected screenshot.
- A second, related layer: `_SessionTimelineStrip`'s per-session `ReachTraceGlyph` loads via yet
  another `FutureProvider` (`_reachTracesProvider`) whose loading state is a **plain, invisible
  `SizedBox`** (no indicator at all). Waiting only for "no indicator found" could still fire before
  this fourth-level future resolves.

**Confirmed this is a test-harness-only bug, not a production one**: `PatientProfileScreen` itself has
no early-exit logic; a real running app (or any widget test using plain `pump()`/`pumpAndSettle()`
without this harness's specific break condition) keeps pumping frames normally until every provider
resolves. The "REAL app" check the brief asked for is exactly what a normal Flutter frame loop already
guarantees; the bug only existed in this file's manual settle-loop shortcut.

**Fix** (`app/test/goldens/screen_goldens_test.dart`, `_goldenFor`): the break condition now requires
*both* `CircularProgressIndicator` and `LinearProgressIndicator` absent, AND a minimum of 4 pumps
(`i >= 3`) before ever breaking, so the invisible fourth-level `ReachTraceGlyph` future also gets a
guaranteed few turns instead of relying on indicator detection alone. Comments in the file explain
why (see the diff).

## CHECKPOINT 2 -- fixture patient coherence + contract re-sync

- `app/assets/fixtures/patients.json`: `synthetic-healthy-42` ("Asha Rao") had `affected_side: "none"`
  + diagnosis `"Post-stroke (ischemic, MCA), no residual deficit"` -- internally contradictory (why
  would a "no residual deficit" patient with no affected side be doing upper-limb rehab?) and
  inconsistent with its **own** session fixture (`assets/fixtures/sessions/healthy__session.json` has
  `"affected_side": "right"` at line 24). Fixed: `affected_side: "right"`, diagnosis
  `"Post-stroke (ischemic, MCA), mild residual right-sided weakness"` -- now consistent with its own
  session data and clinically coherent.
- `app/assets/fixtures/manifests/orchard_reach.manifest.json`: version bumped `0.1.0` -> `0.2.0` to
  match the canonical `contracts/fixtures/orchard_reach.manifest.json` (already at 0.2.0 with
  `hapticsEnabled`/`hapticMaxIntensity`, added upstream by Opus per run7's flag). Diffed every field
  (paramSchema keys, events, metrics, sdkVersion, input) between the two files with a small Node
  script -- only `version` differed; the app-local copy's haptics fields were already correct from
  run7. `contracts/validate.py`: **[PASS] All validations passed** (ran before and after every fixture
  edit this run).
- **Second, bigger fixture bug found while wiring the longitudinal patient for Checkpoint 3**: all 7
  `app/assets/fixtures/sessions/longitudinal__week_0{0..6}__session_0__session.json` files are meant to
  be one patient's 7-week history, but weeks 1-6 each had a **different, incrementing `patient_ref`**
  (`synthetic-longitudinal-9100`, `-9200`, ... `-9600`) instead of the one patient id
  (`synthetic-longitudinal-9000`, "Priya Nair" in `patients.json`) that week 0 and the outcomes seed
  data use. This is a real, pre-existing generator bug -- **also present in the canonical
  `contracts/fixtures/sessions/longitudinal/week_0{1..6}/session_0/session.json`** (confirmed
  identical `patient_ref` values there), so it's not an app/-only artifact. Only the `app/`-local
  copies were fixed this run (scope = `app/` only per the brief); flagged in `MANUAL_TODO.md` for
  Opus to fix the canonical `contracts/` source and its own sync path. Before this fix,
  `listSessionsForPatient('synthetic-longitudinal-9000')` only ever returned 1 of the 7 sessions, so
  the one fixture actually built for "a real multi-point trend" (per this screen's own doc comment)
  couldn't demonstrate one.

## CHECKPOINT 3 -- making the profile render real content, not just "not blank"

Regenerating goldens after Checkpoint 1's fix alone (still using `synthetic-healthy-42`, 1 session)
made the blue bars disappear, but replaced them with the *correctly honest* "not enough sessions for a
trend yet" / "No outcome measures recorded yet." empty states -- because `MockOutcomesRepository` only
seeds outcome-measure entries for `synthetic-longitudinal-9000` (see that file's `_seed()`), and the
`healthy` fixture is deliberately a single session. That satisfies "renders, doesn't crash" but not the
brief's "shows a recovery line chart with the MDC band... outcome measures as rows" -- a screenshot of
empty-state text isn't the thing being asked for.

Switched the `patient_profile` golden specifically to `synthetic-longitudinal-9000` (the fixture
actually built with 7 weekly sessions + seeded Fugl-Meyer outcome entries), while the other three
screens (`program_builder`/`live_monitor`/`session_report`) keep using the `healthy` fixture's
patient/session (they need its richer events/haptics data, untouched this run). This required
extending `screen_goldens_test.dart`'s `_PreloadedFixtures`/`_Instant*Repository` classes to be
patient/session-id-aware (they previously ignored their `id` arguments entirely and returned one
hardcoded blob) -- see the diff for `longitudinalPatient`/`longitudinalSessions`/
`longitudinalMetricsById`/`longitudinalTracesById`/`longitudinalOutcomes` and the branching added to
each `_Instant*Repository` method.

This surfaced two more real rendering bugs once the chart actually had >= 2 points to draw, both fixed
in app code (not just the test harness):

1. **`_RecoveryLine`'s header overflowed on phone width.** `Row(children: [title, Spacer(), changeLabel])`
   never overflowed before because every previously-tested patient had < 2 sessions, so the change-label
   branch never built. With real data, "Worse beyond normal variation" etc. next to a metric title didn't
   fit 358 dp. Fixed with `SizedBox(width: double.infinity, child: Wrap(alignment: spaceBetween, ...))` --
   same layout when both fit on one line, wraps to a second line (not truncated, not clipped) when they
   don't. (First attempt used a bare `Wrap` without the `SizedBox`, which shrink-wrapped to content width
   inside the `Column(crossAxisAlignment: start)` and defeated `spaceBetween`, rendering
   "Reach areaWithin normal variation" jammed together with no gap -- caught by reading the regenerated
   golden, fixed, regenerated again.)
2. **`TrendLineChart`'s x-axis ticks duplicated.** With no explicit `interval`, fl_chart picked a
   sub-1.0 spacing for the small 0..6 session-index range, and `getTitlesWidget`'s `v.round().toString()`
   collapsed several fractional ticks onto the same integer label ("0 0 0 0 0 1 1 1 1 ..."). Fixed by
   computing an explicit whole-number `interval` (`math.max(1, ((maxX - minX) / 6).round())`) -- every
   caller (`patient_profile_screen.dart`, `progress_screen.dart`, `session_report_screen.dart`) already
   passes integer indices as x, so this is safe everywhere `TrendLineChart` is used, not just here.

## CHECKPOINT 4 -- reach-trace flaky golden (item 4)

Investigated `test/reach_trace/reach_trace_golden_test.dart`'s `ReachTraceGlyph` (the widget under
test) and confirmed it's a plain `StatelessWidget`/`CustomPainter` with no animation, no RNG, and
deterministic `List`-ordered trials -- no obvious source of true pixel nondeterminism found in the
time available. Found and fixed a different, concrete bug instead: **this file's own header comment
claimed it needed no `@Tags(['golden'])` annotation because it's fast, and `screen_goldens_test.dart`'s
header comment claimed *it* was tagged `golden` "so a normal `flutter test` run can exclude this
file's ~4-5 minute runtime" -- but grepping both files for `@Tags` found neither actually had the
annotation.** So `flutter test --exclude-tags golden` (used for "the fast suite") was silently running
all 38 golden tests (32 + 6) every time, undermining the whole point of `docs/MANUAL_TODO.md`'s run7
flag ("this file has no `@Tags(['golden'])`... so `--exclude-tags golden` doesn't skip it" -- true, but
for a different reason than assumed: neither file had it). Fixed both files' headers with real
`@Tags(['golden'])` + `library;` directives (silences the `library_annotations` info lint), and added
`app/dart_test.yaml` declaring the tag so `--exclude-tags golden` runs without a "tag wasn't specified"
warning.

Also pinned `tester.view.physicalSize`/`devicePixelRatio` in the reach-trace test (matching
`screen_goldens_test.dart`'s own existing defensive pattern) as a plausible mitigation for shared-binding
state bleeding between this file's 6 tests -- not a confirmed root cause, flagged honestly as such in
the code comment and below.

**Stability evidence (real output, not claimed):**
- `flutter test test/reach_trace/reach_trace_golden_test.dart` run 3x solo: **6/6 passed, all 3 runs.**
- `flutter test test/reach_trace/ test/hub/` (mixed with the other file MANUAL_TODO said co-flaked) run
  3x: runs 1-2 clean (35/35 each); **run 3 had exactly one failure**:
  `test/hub/hub_connection_test.dart`'s "requires_ack message is retried after ackTimeout if unacked"
  -- the *other* pre-existing flake `docs/MANUAL_TODO.md` already documented from run7
  ("once test/hub/hub_connection_test.dart's ack-retry-timer test also flaked"), not a reach-trace
  failure. Across all 6 runs this session (3 solo + 3 combined = 18 total executions of the 6
  reach-trace tests), **zero reach-trace golden failures**. Not proof the flake is gone forever (6 runs
  is not a large sample), but a real, honest improvement over the ~2-of-3-runs failure rate run7
  reported, and the `@Tags` fix means even if it recurs, it no longer blocks the default fast suite.

## CHECKPOINT 5 -- golden regeneration + visual QA (real output, PNGs actually read)

`flutter test --tags golden --update-goldens --concurrency=1`: **38/38 passed** (32 screen goldens + 6
reach-trace). Re-ran without `--update-goldens` twice more as a stability check: **38/38 both times.**

Copied all 38 PNGs to `logs/sessions/screens/app/run8/`. Read every `patient_profile_*` golden with the
Read tool; concrete descriptions:

- **`patient_profile_desktop_light_1.0x.png`** (and `_dark_1.0x.png`, `_tablet_light_1.0x.png`): header
  "Priya Nair" / "Right side affected, Post-stroke hemiparesis, on a 6-week Orchard Reach program, 43
  weeks since onset" (coherent). Recovery section: "Smoothness (SPARC)" and "Reaction time" show "No
  sessions recorded yet." (honest -- this patient's session metrics don't publish those two fields, not
  a bug); "Reach area" shows a **real `fl_chart` line chart**: dashed gridlines, y-axis tick labels
  (0.0-0.1 range, tabular figures), x-axis 0-5 (6 distinct whole-number ticks, no duplicates), a dark
  zig-zagging line with 6 visible data-point dots, and the right-aligned label "Within normal variation"
  in slate -- no MDC band drawn for this metric (no SD/mdc95 published for it in the fixture, so
  `mdcFor` correctly returns null; not a bug). "Trunk lean" shows the same chart shape but *with* a
  visible shaded grey MDC band across the whole width, a downward-trending line from 1.2 to 0.4 across
  7 points (x 0-6), and the right-aligned label "Worse beyond normal variation" in `alert` red. Cut off
  below the fold (viewport-limited, same known/accepted limitation as other screens' below-fold content
  per run7): "Sessions" heading is visible but its glyph strip is below the 1000 px desktop viewport.
- **`patient_profile_phone_light_1.0x.png`** (390x844): same content, "Reach area" chart visible with
  title + "Within normal variation" on one line (no overflow -- confirms the `Wrap`/`SizedBox` fix),
  "Trunk lean" heading + band visible at the very bottom of the frame. No `RenderFlex` overflow banner
  in the golden or in the earlier reproduction run's console output (that exception was fixed in
  Checkpoint 3 and is confirmed gone in every regenerated golden).
- **`patient_profile_phone_light_2.0x.png`**: 200% text scale; heading wraps to 2 lines ("Priya" /
  "Nair"), summary line wraps to 5 lines, all legible, no overflow exception during the test run.
- Earlier, intermediate regeneration (before the longitudinal-patient switch, kept only as a diagnostic
  step and not in the final `run8` folder): with `synthetic-healthy-42` the Recovery section correctly
  showed 4x "No sessions recorded yet." / "Not enough sessions for a trend yet." (honest empty state,
  proving the blue-bar bug itself was fixed) and the Sessions strip showed one real `ReachTraceGlyph`
  (not a bar) -- confirms Checkpoint 1's fix works independent of which patient is used; the
  longitudinal-patient switch in Checkpoint 3 was purely to satisfy "must show real chart content," not
  a dependency of the underlying bug fix.
- Other screens' goldens re-read for regressions: `session_report_desktop_light_1.0x.png` unchanged in
  content vs. run7's description (trial table + haptics section intact); `program_builder_desktop_light_1.0x.png`
  and `live_monitor_desktop_light_1.0x.png` pixel-identical to their pre-run8 renders (confirmed by
  direct comparison) -- the wait-loop and `TrendLineChart` changes did not regress them.

## CHECKPOINT 6 -- verification

`flutter analyze` (full project, final state): **0 errors, 0 warnings, 111 info** -- same count as
run7's verified baseline. (An intermediate state briefly had 112 due to a `double` literal
(`math.max(1.0, ...)`) in the new `TrendLineChart` interval code triggering `prefer_int_literals`;
fixed to `math.max(1, ...).toDouble()`, confirmed back to 111 with a line-by-line diff against run7's
baseline output, not just the total count.)

`flutter test --exclude-tags golden --concurrency=1`: **89/89 passed** (same count/tests as run7 --
this run touched no non-golden test files). Confirmed this flag now actually excludes the golden files
(previously silently didn't, see Checkpoint 4) -- run finished in ~23s instead of several minutes.

`flutter test --tags golden --concurrency=1`: **38/38 passed** (see Checkpoint 5).

`contracts/validate.py`: **[PASS] All validations passed** (re-ran after all fixture edits).

`flutter build web --release`: `Compiling lib\main.dart for the Web... 93.2s`, `√ Built build\web`,
exit 0. Copied `app/build/web/*` to `releases/app/0.5.1/web` (42 MB). Verified the fixture fixes
actually shipped in the build (not just in source): `grep hapticsEnabled` on the built manifest asset
matches once; `grep synthetic-longitudinal-9000` on all 7 built longitudinal session assets matches
once each (confirming the patient_ref fix propagated); `affected_side":"right"` present in the built
`patients.json` asset.

## Flags for Opus / MANUAL_TODO (full entries added to `docs/MANUAL_TODO.md`)
1. **`contracts/fixtures/sessions/longitudinal/week_0{1..6}/session_0/session.json` have the same
   incrementing-`patient_ref` bug** as the app/-local copies this run fixed (`synthetic-longitudinal-9100`
   .. `-9600` instead of the one patient id `synthetic-longitudinal-9000` that week 0 and the seeded
   outcome measures use). Only the `app/`-local copies were fixed this run (scope = `app/`); the
   canonical `contracts/` fixtures need the same fix from Opus, plus whatever sync step produced the
   app/-local copies needs to not regenerate the bug.
2. `screen_goldens_test.dart` and `reach_trace_golden_test.dart` were never actually `@Tags(['golden'])`-annotated
   despite both files' own header comments claiming so since run5/6 -- fixed this run (real tags +
   `dart_test.yaml`). Worth double-checking no other doc/session-log claims about tag-based test
   filtering in this repo have the same doc-vs-code mismatch.
3. `test/hub/hub_connection_test.dart`'s ack-retry-timer test flaked once more this run (1 of 6 runs of
   its file) -- same pre-existing issue MANUAL_TODO already tracks from run7, unrelated to anything
   touched this run, reproduced again as supporting evidence, not a new finding.
4. No visual QA exists yet for `patient_profile`'s "Sessions" glyph strip / "Outcome measures" rows
   specifically *within the fixed golden viewports* -- they render (confirmed via the intermediate
   `synthetic-healthy-42` screenshots and via the "no outcome measures" / real glyph render described
   in Checkpoint 5), but for the `synthetic-longitudinal-9000` patient now used, the taller real charts
   push those sections below the fold in every viewport size this suite captures. A future run could
   either scroll the `ListView` before capturing an additional golden, or add a plain (non-golden)
   widget test that scrolls to the bottom and asserts on `ReachTraceGlyph`/outcome-measure text
   presence, to get real pixel/assertion coverage of those two sections specifically for this patient.
