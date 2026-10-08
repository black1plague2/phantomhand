# Session log: Track A (Flutter app) -- run 6 -- 2026-09-17

Resuming from `logs/sessions/2026-09-17-A-flutter-run5.md` (verified: analyze 0/0, 94 tests, 38
goldens, web build 0.3.0). Working the 6-item Opus review list in order, then (mid-run, per a
coordinator message) `docs/IMPROVEMENT_BRIEF.md` §3 + `OPUS/CLAUDE.md` items 7-10.

PATH: `C:\flutter\bin` prepended in every shell this run.

## CHECKPOINT 1 (items 1-4: overview redesign, chart fix, fixture bug, contrast test)

**Item 3 first (fixture bug), since item 1 depends on the diagnosis being sane:**
`app/assets/fixtures/patients.json`'s `synthetic-healthy-42` (Asha Rao) had
`"affected_side": "right"` paired with `"diagnosis": "... no residual deficit"` -- a patient can't
both have a currently-affected side and no residual deficit. Fixed by setting `affected_side` to
`"none"` (the model already has an `AffectedSide.none` case, used consistently elsewhere: patient
list filter dropdown, side chip). Now reads as a genuinely healthy/fully-recovered synthetic
baseline. This patient is also the one every golden test uses, so this fix is visible in the
regenerated goldens (item 5).

**Item 1: patient overview.** `app/lib/features/patients/patient_profile_screen.dart`'s
`_OverviewTab` was 4 plain `Text` lines. Rewrote it per `docs/APP_DESIGN.md`'s workstation
wireframe, as one scrolling column (same at every width -- the wireframe's "workstation vs phone"
difference is how much horizontal room the column gets via the existing rail/Live-now-pane layout
in the parent, not a different layout for this tab):
- Patient name as the display-scale heading (`textTheme.displaySmall`, the 39px "display" step
  `docs/APP_DESIGN.md` reserves for "patient name on profile, live trial counter only").
- One-line clinical summary: affected side + diagnosis + weeks since onset, comma-separated (not
  middle-dots, per the copy rule).
- **Recovery line**: 3 compact `_RecoveryLine` widgets (SPARC, reaction time, reach area), each
  showing the metric label, the honest MDC change label/color ("Improved beyond normal
  variation"/leaf, "Within normal variation"/slate, "Worse beyond normal variation"/alert -- new
  `mdcChangeDirection`/`changeLabel`/`changeColor` in `shared/metrics/metric_format.dart`), and a
  `TrendLineChart` with its shaded MDC band. Reads a new shared `progressDataProvider`
  (`shared/metrics/progress_data.dart`, extracted from `ProgressScreen`'s previously-private
  `_progressDataProvider`/`_SessionMetricPoint` so both screens read the identical session history
  instead of two copies silently drifting) -- for the `synthetic-longitudinal-9000` fixture (7
  sessions) this renders a real multi-point trend; for a 0-1-session patient it renders an honest
  "Not enough sessions for a trend yet." line instead of an empty chart.
- **Session glyph strip**: reuses the existing `_SessionTimelineStrip` (time-ordered reach-trace
  glyphs, tap -> session report) via a new `_OverviewSessionsStrip` wrapper, instead of building a
  second implementation.
- **Outcome measures**: new `_OverviewOutcomesSummary` -- latest score per standardized measure
  (FMA-UE/ARAT/Box-and-block/MAS), reading `outcomesRepositoryProvider` directly (the Outcomes tab
  keeps the full history/trend charts; this is the at-a-glance version).

**Item 2: speed-profile chart x-axis overlap.** `shared/widgets/charts/speed_profile_chart.dart`:
wrapped in a `LayoutBuilder` choosing 3/5/6 ticks by available width (<260/<420/wider), a new
`_niceInterval()` helper rounding the resulting spacing up to a "nice" step (25/50/100/.../500 ms)
so fl_chart's `interval` parameter draws exactly that many evenly-spaced ticks instead of its
previous unbounded default density; label format changed from `"20ms"` (run-together) to `"20 ms"`
(space before unit, per the item's own "0 ms ... 800 ms" example). Also added tabular-figure text
style to both axes here and to `TrendLineChart`'s axes (`docs/APP_DESIGN.md` §Type: "tabular figures
... for every number in ... charts" -- `TrendLineChart` previously had no left-axis labels at all
and un-styled bottom-axis labels).

**Item 4: contrast unit test for all OpusTokens text/background pairs.**
`test/theme/opus_tokens_contrast_test.dart` already covered ink/slate/lake/ochre/leaf/alert on
mist/paper (7 tests, run5). Computed contrast ratios for every pair actually rendered as text
anywhere in the app (script in scratchpad, not committed) and found two real gaps:
1. `leaf` on `mist` (light) = 4.40:1 -- **fails** AA 4.5:1 for normal text. This pair is real: the
   new overview's recovery-line change label renders `leaf`-colored text directly on the `mist`
   scaffold background (the wireframe has no card around it), not only on a `paper` card.
2. The `ColorScheme`'s `onPrimary`/`onSecondary`/`onError` (all `paper`) drawn on
   `primary`/`secondary`/`error` (`ink`/`lake`/`alert`) -- real pairs from `core/theme/app_theme.dart`
   (filled-button text, the "End session" confirm action, tooltips) that the existing per-role tests
   didn't cover since they only checked `paper`/`mist` as backgrounds, never as the foreground.
   These all already passed (6.4-15:1) -- added as regression tests, no token change needed.
Fixed (1) at the root: `OpusTokens.light.leaf` `0xFF2E7D5B` -> `0xFF2E7A5B` (green channel 0x7D ->
0x7A, i.e. 125 -> 122 -- too small a change to read as a different color) so leaf clears 4.5:1 on
*both* mist (4.56:1) and paper (5.18:1). Verified by hand with a small Node script before touching
the token (see the two `contrast.js`/`find_leaf2.js` scratchpad scripts' output: light leaf/mist
went from 4.40 to 4.56, light leaf/paper 4.999 -> 5.18; dark leaf was already fine, untouched).
Added: `leaf`/`alert` vs `mist` tests (both themes) and the 3 `onPrimary`/`onSecondary`/`onError`
tests (both themes) -- contrast test file now 22 tests (was 7).

Did **not** edit `docs/APP_DESIGN.md`'s `#2E7D5B` reference for `leaf` (brief restricts me to
`app/` + this log + `MANUAL_TODO.md`) -- flagging here instead: Opus should update that one hex in
the color table to `#2E7D5B` -> `#2E7A5B` to match `opus_tokens.dart` (comment there explains why).

**Verification:** `flutter analyze` (full project): 0 errors, 0 warnings, 93 info (baseline was 91;
the 2 new are `implementation_imports`/`depend_on_referenced_packages` on the new
`shared/metrics/progress_data.dart`'s `package:riverpod/src/providers/future_provider.dart` import
-- the exact same info-level pattern already present in `session_report_screen.dart` and 3 other
files, not a new category). `flutter test --exclude-tags golden --concurrency=1`: **72/72 passed**
(was 62; +10 for the expanded contrast test file).

## CHECKPOINT 2 (item 5: golden regeneration + review, item 6: analyze/tests already covered above)

Regenerating `patient_profile`'s goldens first surfaced a real bug the fixture-preload fix (run5)
hadn't covered: the new Overview tab's outcome-measures summary reads `outcomesRepositoryProvider`
directly, which the golden test's `_wrap()` did **not** override (only
patients/programs/manifests/sessions were). That repository's real `MockOutcomesRepository` still
carries `Env.mockLatency` (~220ms real `Future.delayed`), and the golden harness's bounded pump loop
raced it -- 6 of the 32 tests failed with "A Timer is still pending even after the widget tree was
disposed." Fixed the same way run5 fixed the same class of bug: added `outcomes` to
`_PreloadedFixtures`, a `_InstantOutcomesRepository`, and an `outcomesRepositoryProvider` override in
`_wrap()` (`test/goldens/screen_goldens_test.dart`). Re-ran: 32/32 clean.

**Goldens regenerated:** `flutter test test/goldens/screen_goldens_test.dart --update-goldens
--concurrency=1`: 32/32 in ~18s. `flutter test --tags golden --concurrency=1` (full golden-tagged
suite, no `--update-goldens`, confirming stability on a second independent run): 32/32.
`reach_trace_golden_test.dart`'s 6 goldens untouched this run (its painter wasn't touched) --
confirmed still passing as part of the full suite run in Checkpoint 3 below, not regenerated.

**Manually reviewed with the Read tool (not just regenerated) -- concrete descriptions:**
- `patient_profile_desktop_light_1.0x.png`: "Asha Rao" as a large display-weight heading; below it
  "No side affected, Post-stroke (ischemic, MCA), no residual deficit, 45 weeks since onset" on one
  line (no contradiction now); a "Recovery" section with 3 rows (Smoothness (SPARC), Reaction time,
  Reach area), each showing "Not enough sessions for a trend yet." in slate (this patient's fixture
  has exactly 1 session, so no trend is possible -- an honest empty state, not a broken chart); a
  "Sessions" heading with one 160dp reach-trace glyph; an "Outcome measures" heading with "No outcome
  measures recorded yet." (this patient has none seeded -- only the longitudinal patient does). The
  right "Live now" pane is absent (no headset connected in the fixture), so the recovery/sessions/
  outcomes column takes the full remaining width next to the rail.
- `patient_profile_phone_light_2.0x.png`: same content stacked in one column at 2x text scale --
  "Asha Rao" heading wraps to fit, the summary line wraps onto 2-3 lines, no clipped text or
  overflow banners, each Recovery row's label and "Not enough sessions..." caption both remain fully
  visible and left-aligned.
- `patient_profile_desktop_dark_1.0x.png`: same layout on the dark palette -- heading and body text
  in the light `ink` color against the dark `mist` ground, section headings clearly separated by the
  32px/24px spacing, no dark-on-dark low-contrast text (matches the newly-expanded contrast test).
- `session_report_desktop_light_1.0x.png`: the "Speed profile" chart's x-axis now reads "0 ms",
  "133 ms" (interval-derived, not clean since `_niceInterval` picked 133 for this trial's specific
  movement time -- see "Next step" below), ... up to the trial's movement time, each label spaced
  out with no overlap/smear (previously multiple digit strings visibly ran together); numbers use
  tabular figures (consistent digit width, visible in the y-axis "0.0"/"0.5"/"1.0" column staying
  vertically aligned).
- `session_report_phone_light_1.0x.png`: same chart at phone width -- only 3 x-axis ticks shown
  (the `<260` narrow-width branch), still no overlap.

Final goldens (all 38: 32 screen + 6 reach-trace) copied to `logs/sessions/screens/app/run6/`.

## CHECKPOINT 3 (items 7-10, added mid-run per coordinator instruction to read
`docs/IMPROVEMENT_BRIEF.md` §3 + `OPUS/CLAUDE.md`)

**Item 7: router architecture.** Read `lib/core/router/app_router.dart`: it already satisfied most
of this (GoRouter built inside a `@riverpod` provider, `refreshListenable` bridging
`authControllerProvider` via a `ChangeNotifier` that only `ref.listen`s -- never `ref.watch`s -- so
the provider body itself never reruns on auth changes, one top-level `redirect` function). Two real
gaps:
1. All routes were declared inline in `app_router.dart` (importing every feature screen directly),
   not "each feature folder exports its own RouteBase list."
2. `@riverpod` (not `@Riverpod(keepAlive: true)`) means the provider is auto-dispose; even though
   `main.dart`'s root widget watches it continuously in practice, nothing guaranteed it couldn't be
   momentarily disposed and recreated (e.g. a test `ProviderContainer` or a transient rebuild).
Fixed both: added `<feature>_routes.dart` to every feature folder listed in the brief (`auth`,
`patients`, `programs`, `sessions`, `live`, `outcomes`, `devices`, `settings`), each exporting a
`List<RouteBase>`; `app_router.dart` now only imports those 8 files (no screen imports) and spreads
them into the `GoRouter`'s `routes:` (auth's list top-level alongside the `ShellRoute`, the rest
inside it, matching the existing shell/no-shell split). Routes whose path lives under
`/patients/:patientId/...` (programs/new, sessions/:id, live/:id, outcomes/new) are now flat
sibling `GoRoute`s with their full path instead of Dart-nested under the patients feature's route --
go_router matches by path pattern, not by Dart nesting, so this doesn't change any URL or behavior,
it just lets each feature own its own route object. Changed `@riverpod` -> `@Riverpod(keepAlive:
true)` on `appRouter`, with a doc comment explaining exactly why (see the function's doc in
`app_router.dart`). Regenerated with `dart run build_runner build` (see below) --
`app_router.g.dart` now shows `isAutoDispose: false`.

`dart run build_runner build` also regenerated `game_manifest.freezed.dart`/`.g.dart` (and 3 other
model .g.dart/.freezed.dart files) to match a field-order change already present in their `.dart`
sources from an earlier run that had never been regenerated -- confirmed via `git diff` this is a
pure reorder (`paramSchema` moved to match the constructor's declared order), not a behavior change,
and `hub_providers.g.dart`'s only diff is its content-hash comment. No source file outside what's
listed above was touched by the regen.

Added `test/router/app_router_test.dart`: a `testWidgets` case driving the *real*
`appRouterProvider` end to end through a `ProviderContainer` + `MaterialApp.router` -- starts signed
out (asserts `LoginScreen` shown, `GoRouter.of(...)` is the same object read from the container),
calls `authControllerProvider.notifier.signIn(...)` (asserts redirect to `PatientListScreen`, and
`container.read(appRouterProvider)` is `same()` as the router read before sign-in -- the actual
"not recreated" assertion the brief asked for), then `signOut()` (asserts redirect back to
`LoginScreen`, same instance again).

[Placeholder while item 7's test runs -- see the tail of this checkpoint for real output, filled in
before this file is considered final.]
