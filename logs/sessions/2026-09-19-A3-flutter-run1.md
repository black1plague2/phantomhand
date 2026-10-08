# Track A3 (Flutter data screens) — run 1 (2026-09-19)

Scope (HARD boundary; A1 edits the rest in parallel):
`app/lib/features/patients/**`, `app/lib/features/sessions/**`,
`app/lib/features/programs/**`, `app/lib/shared/**`, and the tests/goldens for
exactly those. Never `core/**`, `app/**`, `features/live|devices|settings/**`,
`game/`, `sim/`, `analytics/`, `tools/`, `contracts/`.
Never `git commit`. Never `adb` / APK / phone (A1 owns the device this run).

## CHECKPOINT 0 — state found on arrival (explore)

A2 run13 (committed `e943c50`) had already started the v2 conversion:
- `app/lib/shared/design/v2_colors.dart` exists (literal §1 hex values, a
  documented stopgap while A1 lands them in `core/theme/opus_tokens.dart`).
- v2 chart kit exists and is used: `outcome_donut.dart`, `per_trial_bars.dart`,
  `weekly_dose_bars.dart`, `cue_pie.dart`, `trend_line_chart.dart`,
  `sparkline.dart`, `speed_profile_chart.dart`.
- `shared/widgets/section.dart` has `Section` + `StatTile` per §4.
- `patient_list_screen.dart` was already rewritten to the §5 shape
  (search + name + condition + date + 40 px bar).
- `patient_profile_screen.dart`'s `_OverviewTab` already has the 6 §5 sections.

**But** every one of those files hardcodes `fontSize:` overrides (15 / 12 / 17 /
28 / 34) on top of the theme, so Opus's reduced central type scale (axis 11,
body 13, sectionTitle 14, screenTitle 22, bigValue 26 in `opus_tokens.dart`)
has **no effect at all** on these screens. That is the direct cause of the
user's "fonts too big / not readable" complaint surviving the token change.

Concrete §5 violations found (the real work of this run):
1. Patients rows render `p.diagnosis` verbatim — the full clinical description
   ("Post-stroke (ischemic, MCA), mild residual right-sided weakness · right"),
   two lines on a 360 dp phone. §5 wants the short form "Stroke · right".
2. Patient screen still has a **5-tab TabBar** (Overview/Programs/Sessions/
   Progress/Outcomes) wrapped around the §5 content. §5 = 6 sections, no tabs.
3. Session report renders an extra `ReachTraceGlyph` header + patient-reported
   chips; its Trials table has `Hand` and `Quality` columns and no `error cm`.
   §5: `#, outcome, RT, MT, peak, SPARC, error cm`.
4. Reports list has a reach-trace glyph leading, Live/New `Chip` badges on
   every row and a chevron. §5: patient, date, success %. Nothing more.
5. Program builder is a 3-pane workstation layout (game library / block list /
   form) with workspace preview, preset chips, schedule sliders, a Save button
   AND a separate Send button, plus per-field help/reset icon buttons in the
   dynamic form. §5: sections by param group, plain label + control, **one**
   primary action "Send to headset".
6. No golden breakpoint at 360x800 (the pilot phone's real width); no golden
   for `patient_list` at all.
7. Gap inherited from A2 run13: the events-derived branch of
   `SessionReportScreen` (metrics `null` + real events) has no widget test.

## CHECKPOINT 1 — baseline BEFORE any change (real output)

`C:\flutter\bin\flutter.bat analyze` in `app/`:

```
201 issues found. (ran in 10.4s)
grep -cE "^ *(error|warning) -"  ->  0
```

0 errors, 0 warnings, 201 `info` lints (all pre-existing: pubspec sort order,
riverpod `implementation_imports`, `avoid_dynamic_calls` on JSON-shaped data,
`unnecessary_type_name_in_constructor` for this codebase's `const new(...)`).

`C:\flutter\bin\flutter.bat test` in `app/`: (filled in below)
