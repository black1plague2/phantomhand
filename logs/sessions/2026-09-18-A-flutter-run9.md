# Track A — Flutter app, run 9 (2026-09-18/19, verified by Opus)

Resume point for the next A agent. Previous: `2026-09-18-A-flutter-run8.md`.

## What this run was for
Opus's review of run 8 found four defects in the patient-overview / progress screens. The run-8 agent
wrote the fixes but died (usage limit) before anything was verified or committed. This run is Opus
verifying that uncommitted work, fixing what it found, and committing.

## The four defects and how they were fixed

1. **Trunk-lean trend label was clinically inverted.** The overview said "Worse beyond normal variation"
   while trunk lean *fell* from ~1.2 cm to ~0.4 cm (an improvement). Root cause: `metric_format.dart`
   had a two-element `_lowerIsBetterMetrics` set (`reaction_time_ms`, `movement_time_ms`) and silently
   defaulted **everything else** to "higher is better" — wrong for trunk lean, tracking loss, endpoint
   error, submovement count and path-length ratio.
   Fix: an explicit `MetricDirection` per metric (`higherIsBetter` / `lowerIsBetter` /
   `lowerMagnitudeIsBetter`), with every metric the display table knows about listed explicitly, so a new
   metric cannot silently inherit the wrong direction.
   `lowerMagnitudeIsBetter` exists for signed metrics: `neglect_index` can be negative depending on which
   side is under-attended, so a plain "lower is better" rule would score worsening neglect as improvement.
   Those comparisons use `.abs()` on both endpoints.

2. **SPARC and reaction-time recovery charts were always empty** ("No sessions recorded yet") for a
   patient with 7 real sessions, while reach area and trunk lean plotted fine from the same fixtures.
   Root cause (confirmed by reading a real `metrics.json`, not inferred): analytics v0.2 publishes
   `sparc` / `reaction_time_ms` **per trial only**; `metrics.session.metrics` carries only
   `rate_hz`, `success_rate`, `neglect_index`, `trunk_lean_cm`, `fatigue_slope`,
   `reach_envelope_area_m2`, `tracking_loss_pct`. This is the schema's design, not a producer bug.
   Fix: `progress_data.dart` falls back to the mean of that session's per-trial values for any metric the
   session aggregate lacks, with the trial-to-trial sample SD as `sd` (and no fabricated `mdc95` —
   `mdcFor` already falls back to its documented SD95 stand-in).

3. **Charts had no x-axis unit.** Added `xAxisLabel`, with an honest unit per screen rather than one
   blanket label: `Week` on the patient overview (that screen's longitudinal fixtures are one session per
   week), `Session` on the progress picker (a patient's sessions are not necessarily weekly), `Entry` for
   outcome measures, `Trial` inside a session report.

4. Reach-area axis precision (same `trend_line_chart.dart` change).

## Verification (real output, Opus)

```
flutter analyze   -> 0 errors, 0 warnings (118 style infos, all pre-existing, all in test/)
flutter test      -> 156 passed (unit + 38 goldens)
```

New test file `app/test/metrics/metric_format_test.dart` covers the direction table, the
inverted-trunk-lean bug specifically, and the `neglect_index` magnitude rule.

## One thing Opus removed
`app/test/goldens/_tmp_full_profile_test.dart` — a leftover scratch harness from run 8. It was not
referenced anywhere, and it **hung the whole suite for 10 minutes** before timing out (it was the only
failing test in the first full run). Deleted, not committed. The real golden coverage is
`app/test/goldens/screen_goldens_test.dart`, which passes.

## Still open for track A
- The analytics generator gives each longitudinal fixture session a different `patient_ref`; the app has
  to work around this to show one patient's history. Fix belongs in the generator (track N), flagged by
  this track in run 8.
- Router audit (brief §3: GoRouter inside a Riverpod provider, `refreshListenable`, one redirect
  function, per-feature `RouteBase` lists) has still not been done.
- Dose adherence (≥ 30 min/session) surfaced in the progress view — brief §1, not started.
