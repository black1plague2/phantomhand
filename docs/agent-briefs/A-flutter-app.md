# Brief A: Flutter app on mocks · model: Sonnet (A7 and polish: Haiku)

**Goal:** a clean, accessible clinician app (tablet, phone, web) that already feels real on mock data, with a repository layer that the Phase 3 API drops into.

## Stack (decided)
Flutter stable (at `C:\flutter\bin`; add to PATH in the session) · Dart 3 · **Riverpod** (codegen) · **go_router** · **freezed/json_serializable** · **Drift** (offline cache) · `fl_chart` latest or `syncfusion_flutter_charts` (pick one and justify in the log) · `intl` + ARB (en, hi) · Material 3, dynamic type, high contrast · `flutter_test` + golden tests · `very_good_analysis` lints.

## Structure
```
app/lib/
  core/ (theme, router, l10n, a11y, env)
  data/ (repositories interfaces, mock/ impl reading assets/fixtures, dto from contracts)
  features/ auth · patients · programs · sessions · progress · live · outcomes · devices · settings
  shared/widgets (charts, metric cards with quality badge, dynamic_form/)
```

## Tasks
- **A1** Scaffold + CI (`flutter analyze`, `flutter test`). App shell with adaptive nav (rail on tablet/web, bottom bar on phone).
- **A2** Mock repositories fed by `contracts/fixtures` and `sim/` synthetic sessions (copied into `app/assets/fixtures` by a script). Simulated latency and error toggles.
- **A3** Auth (mock roles: admin, clinician, therapist, nurse, patient), patient list (search/filter/affected side/diagnosis), patient profile.
- **A4** **Dynamic form engine**: renders any manifest `paramSchema` using `x-ui` hints (segmented, stepper, range, slider, switch), with validation, presets, and "explain this param" help. Program builder: ordered blocks, schedule, supervision flag. **No per-game widgets.**
- **A5** Session report: trial table, speed profile chart for a trial, SPARC/RT trends, workspace heatmap (L/R), quality badges, patient-reported scores. Progress view: longitudinal trends with MDC bands, outcome measures overlaid.
- **A6** Live monitor on a mock stream (status, current trial, rolling metrics, pause/stop buttons). Outcome-measure entry forms (FMA-UE, ARAT, Box & Block, MAS).
- **A7** (Haiku) a11y audit (semantics labels, 200 % text, contrast), golden tests, Hindi strings, web + Android debug builds. Tag `app-v0.1.0`.

## Done when
All screens work on mocks, the dynamic form renders the Orchard manifest with zero game-specific code, tests pass, and web + APK builds succeed.
