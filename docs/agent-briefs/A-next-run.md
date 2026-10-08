# Track A: next-run milestones (living brief; resume from the first unfinished step)

Prior log: `logs/sessions/2026-09-14-A-flutter-run2.md` (CHECKPOINTs 1–4). Binding: `docs/APP_DESIGN.md`, `contracts/LIVE_PROTOCOL.md`, `docs/agent-briefs/A-flutter-app.md`.

## Current state (Opus, 2026-09-19 — supersedes the run2 block below)
Latest log: `logs/sessions/2026-09-18-A-flutter-run9.md`. Verified this session: `flutter analyze` **0 err /
0 warn**, `flutter test` **156 passed** (unit + 38 goldens), commit `ff5dd83`.

Done since the run2 block: B2 (tokens, font, ReachTraceGlyph, layouts, program builder, copy rules, contrast
test), B3 (hub providers, devices incl. the haptic sleeve tile, live monitor on a real stream), B4 goldens,
web release `releases/app/0.5.1/web`, session report with haptic cue analysis, patient profile with real
recovery charts and per-metric clinical trend directions.

**Correction (Opus, 2026-09-19):** an earlier version of this block listed the router audit and dose
adherence as "never verified" / "not started". **Both were wrong** — I copied them forward from the stale
run2 block without checking the tree. Both shipped in `b5241c2` (2026-09-18) and were re-verified in A run10:
- Router: `GoRouter` is built inside a `@Riverpod(keepAlive: true)` provider, `refreshListenable` is a
  `ChangeNotifier` using `ref.listen` (not `ref.watch`) on auth state, there is one top-level `redirect`, and
  each feature exports its own `RouteBase` list. `app/test/router/app_router_test.dart` proves the GoRouter
  instance survives an auth change. Brief §3 is **met**.
- Dose adherence: `app/lib/shared/metrics/dose_adherence.dart` + `_DoseAdherenceSection` in the progress
  screen, prescribed vs completed minutes and days, with "reference target" wording rather than a clinical
  claim. `app/test/progress/dose_adherence_test.dart` covers 4 cases. Brief §1's dose item is **met**.

**Actually not done, in priority order:**
1. **The three-way integration demo** — `tool/hub_cli.dart` ↔ a live Unity demo session ↔
   `sim/haptic/fake_haptic.py`, running together. A run10 proved the app leg end to end against
   `sim/live/fake_headset.py`: 54 events, session + events + 4 kin files uploaded, per-message latency logged
   (0–2 ms on localhost), ping RTT p50 3.0 ms, and the stored session passes `contracts/validate.py --session`.
   The Unity leg has never been joined to it.
2. Longitudinal fixtures give each session a different `patient_ref`; the app works around it. Real fix is in
   the track-N generator.

**Lesson for whoever edits this file:** check the tree before writing a "not done" list into it. This brief
has now propagated the same two stale claims across at least three runs.

**Traps:** don't leave scratch `*_test.dart` in `app/test/` (one hung the suite 10 min before timing out);
hub tests use port 8797 (8787 is Unity's); goldens are 390×844 / 1280×800 / 1600×1000, light/dark, 1.0/2.0.

## State (from run2 checkpoints — HISTORICAL, see the block above)
- WIP verified: analyze/test pass (per run2). Design system foundation partially applied. Screens A3–A6 present.
- Hub core done under `lib/core/hub/` (live_message, hub_connection protocol engine, hub_server WS+HTTP PUT+health, udp_beacon with real subnet broadcast via ipconfig parsing), plus the headless `tool/hub_cli.dart [--auto-drive]`. Verified end to end against `sim/live/fake_headset.py`. mDNS skipped (nsd/bonsoir have no Windows backend) → beacon only.
- Hub **UI/providers not wired**.
- Builds: web ✅. Windows ❌ (Visual Studio C++ workload missing → MANUAL_TODO). APK ❌ ("Unable to establish loopback connection" from Gradle; investigate: JDK version, `org.gradle.daemon`, firewall/VPN on 127.0.0.1).

## Milestones
- **B1 Re-verify:** `flutter analyze` (0 errors/warnings), `flutter test`, `dart run tool/hub_cli.dart --auto-drive` + `sim/live/fake_headset.py --no-scenario` round trip; record latency p50/p95.
- **B2 Finish APP_DESIGN:** OpusTokens light/dark; bundled Atkinson Hyperlegible Next (400/600/700) + tabular figures; `ReachTraceGlyph` from real fixture kin chunks (cached decimated paths); workstation layout (rail + patient column: recovery line + MDC band + session glyph strip + a "Live now" pane only when a headset is connected); phone layout; program builder (library | numbered blocks | dynamic form + live workspace preview); copy rules; contrast unit test.
- **B3 Wire the hub into the app:** Riverpod providers (hub server lifecycle on Windows/Android; web shows "Hub isn't available in the browser. Open the desktop app to connect headsets."), connected headsets, status stream, trial events, rolling metrics, RTT; command API (assign_program, start, pause, resume, stop, recenter, skip_block, adjust_params validated by the dynamic form, show_message) with inline ack states. Devices screen (hub address, pairing code, headsets with battery/fps/tracking rate/hands/RTT). Live monitor bound to the real stream (reach strokes drawn live). Sessions received via the hub → sessions list/report; "Run analysis" on desktop calls `analytics\.venv\Scripts\python.exe -m opus_analytics <dir>`.
- **B4 Tests:** widget tests for the devices + live monitor against an in-process hub + a Dart fake headset client; golden tests (profile, live monitor, program builder, report) at 390×844 / 1280×800 / 1600×1000, light/dark, text scale 1.0/2.0. Review the goldens yourself and fix overflow/contrast.
- **B5 Builds:** web release; retry APK after diagnosing the Gradle loopback error (try `--no-daemon`, check `java -version` vs AGP requirement, `gradle.properties` org.gradle.jvmargs); Windows only if VS C++ has been installed. Artifacts → `releases/app/0.2.0/`. Screenshots of the running web build (Chrome via `flutter run -d web-server` + the Browser pane, if available) → `logs/sessions/screens/app/`.

CHECKPOINT after each step to `logs/sessions/<date>-A-flutter-run<N>.md`.
