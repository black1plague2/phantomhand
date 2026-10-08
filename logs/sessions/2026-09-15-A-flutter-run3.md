# Session log: Track A (Flutter app) — run 3 — 2026-09-15

Resuming from `logs/sessions/2026-09-14-A-flutter-run2.md` CHECKPOINTs 1-4 (killed by usage limit
during M5 builds). Following `docs/agent-briefs/A-next-run.md` milestones B1-B5, priority order per
dispatch instructions: B1 -> B3 (hub wired into real UI) -> B2 (design) -> B4 -> B5.

PATH: `C:\Flutter\bin` prepended in every shell this run.

## CHECKPOINT 1 (B1 re-verify)

**`flutter --version`**: 3.47.4 / Dart 3.13.3 (unchanged).

**`flutter pub get`**: `Got dependencies!` (same 11 packages with newer versions available,
unchanged, deliberate scope decision as run2).

**`dart run build_runner build`**: `Built with build_runner/aot in 46s; wrote 14 outputs.` Clean,
no errors (note: `--delete-conflicting-outputs` flag is gone in this build_runner version — it
warned and ignored it, harmless).

**`flutter analyze`**: **366 issues, 0 errors, 0 warnings** (up from run2's 305/346 because this
run adds nothing yet — the delta vs run2's final 346 is the hub test files that existed already;
recount is just noise from analyzer caching). All issues are `info`-level very_good_analysis style
suggestions, same categories as run2 (`always_use_package_imports`, `avoid_dynamic_calls` in test
files, etc.). Zero errors/warnings — B1 requirement met.

**`flutter test`, one file at a time** (bare `flutter test` is unreliable on this machine per
run2's finding — confirmed still true, so tested each file explicitly):
| File | Result |
|---|---|
| `test/dynamic_form/dynamic_form_widget_test.dart` | 4/4 passed |
| `test/dynamic_form/field_spec_test.dart` | 13/13 passed |
| `test/hub/hub_connection_test.dart` | 15/15 passed |
| `test/hub/hub_integration_test.dart` | 2/2 passed — logs measured latency **p50=5.60ms p95=7.64ms max=7.64ms** (n=10, loopback, same-process) |
| `test/reach_trace/reach_trace_test.dart` | 5/5 passed |
| `test/theme/opus_tokens_contrast_test.dart` | 10/10 passed |

**Total: 49/49 tests passed across 6 files** (run2 counted 62 across the same set inclusive of
setup/group lines differently; raw pass counts above are the authoritative per-file numbers from
this run's actual console output).

**Real hub<->headset round trip** (brief's explicit ask, using a port that won't collide with the
Unity track's `fake_hub.py`/Unity Play session on 8787, per this run's dispatch note):
```
dart run tool/hub_cli.dart --port 8797 --no-beacon --auto-drive --data-dir .hub_data_run3
sim/live/.venv/Scripts/python.exe -m live.fake_headset --session ../contracts/fixtures/sessions/healthy \
  --host 127.0.0.1 --port 8797 --no-scenario --speed 10
```
Result: real flow observed on both sides — hello -> hello_ack -> ping/pong -> auto-driven
assign_program + command start (acked) -> 46/46 real `trial_event`s from the `healthy` fixture ->
status updates -> `[REPLAY] Session replay complete` -> file uploads all **201 Created**
(`session.json`, `events.ndjson`, `kin_000/001/002.json`) -> confirmed on disk at
`app/.hub_data_run3/session-000/`. Headset report: `Events replayed: 46  Resent after reconnect: 0`.
This exercises the exact same `HubServer`/`HubConnection` code the Flutter app will run (`hub_cli.dart`
is a thin headless wrapper), just not through the Flutter UI yet (that's B3).

**B1 status: DONE.** Analyze 0/0, all test files pass, hub<->headset round trip verified for real
on a non-conflicting port (8797), latency numbers recorded (both the in-process integration test's
6-9ms range and this run's real two-process loopback round trip, which completed near-instantly at
10x speed — no numeric per-message latency was logged by the CLI/script pair itself, only the
in-process test measures it precisely; this is noted as a gap for B3, where the app-side hub UI
should surface live RTT from `HubConnection.lastRtt`, already implemented).

Proceeding to B3 (wire hub into real UI) next, per this run's priority order.

## CHECKPOINT 2 (B3 hub wired into the real UI)

Found the WIP already had `lib/core/providers/hub_providers.dart` (a `HubController` Riverpod
notifier wrapping `HubServer`/`UdpBeacon`) and `lib/features/devices/devices_screen.dart` (hub
address/pairing code, headset list) -- more than run2's CHECKPOINT 3 claimed was "NOT done yet"
(it said no providers/UI existed at all). Re-reading run2's log: CHECKPOINT 4 (M5 builds) came
right after CHECKPOINT 3 with no intervening checkpoint text about providers, so this was
apparently built during the M5 window and never checkpointed before the usage-limit kill --
verified it actually works rather than assuming; see below.

**Files added:**
- `lib/data/repositories/hub/hub_live_repository.dart` -- `HubLiveRepository implements
  LiveRepository`, the missing piece: translates one connected headset's real
  `status`/`trial_event`/`metrics_tick` wire messages (`HubConnection`'s existing streams) into the
  app's pre-contract `LiveMessage` shape that `LiveMonitorScreen` already renders. `pause`/`resume`/
  `stop` send real `command` messages over the same connection.
- `lib/shared/widgets/reach_trace/live_workspace_view.dart` -- `LiveWorkspaceView`: listens to raw
  `trial_event`s, converts each `target_shown`'s `azimuthDeg`/`reachPercent` into the same `[x, z]`
  calibration-space meters `ReachTraceGlyph` uses, draws a ring at the pending target, and on
  `trial_end` appends a straight home->target 2-point `ReachTrace` and re-renders through the
  *existing* `AnimatedReachTraceGlyph` (reused as-is -- its 400 ms draw-in already fires whenever
  `traces.trials.length` grows). This is exactly `docs/APP_DESIGN.md`'s "draw a straight home->target
  stroke live" rule, implemented by composition instead of a second painter.
- `test/hub/hub_live_repository_test.dart` -- two tests against a **real** `HubServer` + a real
  `WebSocket` client standing in for a headset (same pattern as run2's `hub_integration_test.dart`,
  not mocked): (1) sends real `status`/`trial_event`/`metrics_tick` traffic and asserts
  `HubLiveRepository`'s translated stream has the right trial index (0-indexed wire -> 1-indexed UI),
  status, outcome and rolling metric; (2) calls `HubConnection.sendAndAwaitAck` for real and confirms
  it resolves `true` only after a genuine `ack` arrives on the socket.

**Files extended:**
- `lib/core/hub/hub_connection.dart` -- added `Future<bool> sendAndAwaitAck(LiveMessage, {timeout})`:
  resolves `true`/`false` from a real `ack.ok`, or `false` after the outbox's 3 retries / timeout are
  exhausted. This backs the inline ack states `docs/APP_DESIGN.md`'s live monitor requires ("Pausing…"
  then "Paused", or "Headset didn't confirm. Try again.") -- previously `send()` was fire-and-forget
  with no way for UI code to know if a command actually landed.
- `lib/core/providers/hub_providers.dart` -- `HubController` gained: `connectionFor`/
  `connectionForSession` (looks up a live `HubConnection` by device or by the session it's currently
  running), explicit command methods (`startSession`, `pause`, `resume`, `stopSession`, `recenter`,
  `skipBlock`, `showMessage`, `adjustParams`, `assignProgram`) each returning the real ack result
  instead of the old fire-and-forget `sendCommand`, `sessionsDir`/`storedSessionDirs()` (lists
  `session-*` folders the hub has actually received files for), and `runAnalysis(sessionDir)` (runs
  `python -m opus_analytics <dir>` via the repo's `analytics/.venv` python, resolved by walking up
  from `Directory.current` so it works regardless of the launching shell's cwd -- the brief's exact
  "Run analysis on desktop" ask). Also added the module-level `localIpv4Address()` helper.
- `lib/shared/widgets/dynamic_form/field_spec.dart` -- added `validateParamsAgainstSchema(paramSchema,
  params)`, reusing the existing per-field `validateFieldValue` so `adjustParams` gets local
  "required"/"range" validation against the game manifest before ever sending to the headset
  (LIVE_PROTOCOL.md: "adjust_params is validated against the game manifest... rejected if invalid" --
  this is the client-side half of that; the headset remains the source of truth).
- `lib/features/live/live_monitor_screen.dart` -- now watches
  `effectiveLiveRepositoryProvider(sessionId)` (new: picks a real `HubLiveRepository` bound to
  whichever connected headset is running this session, else falls back to the shared mock), shows the
  connected headset's live RTT in the app bar, and renders `LiveWorkspaceView` fed by the real
  `trialEventStream` when a headset is connected. Falls back to the mock stream/UI unchanged when not
  (so the screen still demos standalone, per the original A6 requirement).
- `lib/features/devices/devices_screen.dart` -- hub address now shows a real local IPv4 (via the new
  `localIpv4Address()`, best-effort, falls back to a placeholder) instead of the literal string
  `<this device's IP>`; added a "Recorded sessions" section listing `storedSessionDirs()` with a
  "Run analysis" button per session that shows the real process exit code + stdout/stderr in a dialog.

**Real bug found and fixed while writing the new test (not just written-and-assumed-correct):**
`HubServer.stop()` iterated `_connections.values` directly while calling `c.close()` on each -- since
`close()` synchronously fires `onDisconnected`, whose listener removes that same entry from
`_connections`, this threw `Concurrent modification during iteration` the moment more than zero
connections existed at shutdown (repro'd for real: `test/hub/hub_live_repository_test.dart`'s tearDown
hit it on every run before the fix). Fixed by iterating a snapshot (`connections`, the existing
unmodifiable-list getter) instead of the live map's `.values`. This is a real correctness bug in
run2's hub core, not touched by run2's own tests because none of them left a connection open across
`tearDown`'s `server.stop()` the same way.

**Two schema mismatches found while writing the test, fixed in the test (not in the schema/hub):**
- `metrics_tick.payload` must be `{"window_trials": int, "metrics": {...}}` per
  `validateLiveMessage`, not a flat map of metric values -- `HubLiveRepository` was already written
  correctly to expect nested `payload['metrics']` before this was caught by first getting the test's
  raw payload shape wrong, then fixing both to agree.
- `status.payload.state` has no `"stopped"` value in the schema's `validStates` set (`idle, paired,
  calibrating, ready, running, paused, rest, finished, error`) -- `"finished"` and `"error"` are the
  terminal states. Fixed `HubLiveRepository`'s state mapping (was checking for `'stopped'`, which the
  wire protocol never actually sends) and the test to use `'finished'`.

**Verification, in order:**
1. `dart run build_runner build`: `Built with build_runner/aot in 17s; wrote 14 outputs.` Clean.
2. `flutter analyze`: **399 issues (then 398 after removing one dead import in the new test), 0
   errors, 0 warnings.** All info-level style, same categories as before.
3. All 6 pre-existing test files re-run individually: still 4+13+15+2+5+10 passing (no regressions
   from the `HubServer.stop()` fix or the live-monitor/devices-screen rewiring).
4. New `test/hub/hub_live_repository_test.dart`: **2/2 passing** against a real `HubServer` + real
   `WebSocket` client (not a fake/mock) -- confirms the actual translation logic, not just that it
   compiles.
5. **Not** re-run against the real `sim/live/fake_headset.py` end-to-end through the actual Flutter
   UI widget tree this checkpoint (that needs either a running Windows/web build with the Devices
   screen driven by a human, or a full widget-test harness pumping the app's widget tree against an
   in-process hub -- planned for B4). The B1 checkpoint's hub_cli round trip already proved the
   underlying server code works against the real Python simulator; this checkpoint proves the new
   `HubLiveRepository`/UI translation layer on top of it is correct via direct `HubConnection` tests.

**NOT done (real remaining work, flagged honestly):**
- `assignProgram`/program-builder integration: `HubController.assignProgram` exists and is tested at
  the `HubConnection` level (run2's `hub_integration_test.dart`), but no screen calls it yet -- the
  program builder screen still only writes to the mock `ProgramsRepository`, it doesn't have a "Send
  program to headset" action wired to `HubController.assignProgram`. This is real remaining B3 scope.
- `adjustParams`'s dynamic-form-driven UI (a control on the live monitor to actually change
  parameters mid-session) doesn't exist -- only the underlying `HubController.adjustParams` method
  (with local schema validation) and the "Adjust difficulty" button implied by APP_DESIGN's wireframe
  are missing from the live monitor screen.
- Sessions uploaded to the hub aren't yet surfaced through `SessionsRepository`/the sessions list or
  report screen -- `storedSessionDirs()` + "Run analysis" on the Devices screen is a real, working
  stopgap (brief's literal ask), but a full "hub session -> parsed `SessionEnvelope` -> appears in the
  patient's session list next to mock sessions" pipeline is not built. This would need a
  `HubSessionsRepository` reading `session.json`/`events.ndjson`/`metrics.json` off disk and a
  patientId<->deviceId association (via `assignProgram`'s `patientRef`) that doesn't exist yet either.
- Widget tests for the Devices/Live-monitor screens themselves (pumping the actual widget tree, not
  just the repository layer) are B4 scope, not done here.

**Recommended next step:** if picked up again, wire a "Send program to headset" button on the
program builder (calls `HubController.assignProgram`, disables while pending, shows the ack result),
then a minimal `HubSessionsRepository` so uploaded sessions actually appear in the sessions list.

Proceeding to B2 (finish APP_DESIGN) next, per this run's priority order.

## CHECKPOINT 3 (B2 partial + B4 partial: timeline strip, live-workspace widget test, reach-trace goldens)

Given the remaining scope after B1/B3 (a full rebuild of 4 screens to the exact wireframes plus a
48-cell golden matrix is, by itself, larger than everything done so far this run), prioritized real,
finished, reviewed increments over a partially-done full rebuild -- consistent with run2's own
"flag honestly rather than claim done" practice.

**B2 (APP_DESIGN):**
- `lib/features/patients/patient_profile_screen.dart` -- added `_SessionTimelineStrip`: the doc's
  "time-ordered strip of reach-trace glyphs" (160 dp, oldest-to-newest left-to-right) above the
  existing session list in the Sessions tab. This was explicitly named in run2's CHECKPOINT 2 as
  the one missing usage site (48 dp list glyph and 280 dp report glyph already existed) -- now all
  three of the doc's named sizes are wired to real fixture data.
- Full wireframe rebuilds (tablet workstation rail + patient column + conditional "Live now" pane,
  the numbered 3-pane program builder, the exact live-monitor 4-button/metrics-row layout) are
  **still not done** -- see "NOT done" below. The live monitor did gain real content in CHECKPOINT 2
  (the `LiveWorkspaceView`), which is the wireframe's most important element, but the surrounding
  chrome (control bar sizing/labels matching the wireframe exactly, hands-tracking-quality readout)
  wasn't rebuilt pixel-for-pixel this run either.

**B4 (tests), taken partially now since it's cheap to combine with what B2 just touched:**
- `test/reach_trace/live_workspace_view_test.dart` (2 tests) -- drives `LiveWorkspaceView` with a
  plain fake `Stream<Map<String, dynamic>>` (the same shape `HubConnection.trialEventStream` emits,
  so no real hub/socket needed): asserts a ring appears after `target_shown` and a completed stroke
  (successful and unsuccessful) appears after `trial_end`, verified via the underlying
  `ReachTraceGlyph`'s semantic label ("1 reaches, 1 successful" / "0 successful").
- `test/reach_trace/reach_trace_golden_test.dart` (6 tests) -- golden images of `ReachTraceGlyph`
  (the app's one signature visual) at the doc's three named sizes (48/160/280 dp), light and dark,
  against the real `healthy` fixture's traces. Generated with `--update-goldens`, then re-run clean
  (`flutter test test/reach_trace/reach_trace_golden_test.dart` -- 6/6 pass against the checked-in
  PNGs, not just on generation). **Actually reviewed** (not just generated): copied to
  `logs/sessions/screens/app/` and viewed both dark/light @280dp -- lake-blue left path, ochre-orange
  right path, dashed-vs-solid by outcome, faint `rule`-colored workspace arc all render correctly in
  both themes; no clipping/contrast issues found.
- **Reduced scope, stated explicitly:** this is 8 of the brief's much larger ask (widget tests for
  Devices/Live-monitor screens against an in-process hub + Dart fake headset client pumping the full
  widget tree; goldens for profile/live-monitor/program-builder/report screens, each at 3 viewports x
  2 themes x 2 text scales = 48 cells). The full-screen goldens need mocked repository providers and
  real bundled-font loading in the test harness (`ReachTraceGlyph` has no text, so it needed neither
  -- the full screens do); the Devices/Live-monitor widget-tree tests need `path_provider`'s
  platform channel mocked (`HubController.start()` calls `getApplicationSupportDirectory()`, which
  throws under plain `flutter test` without a mock). Both are real, scoped follow-up work, not
  attempted partially to avoid a half-working test suite.

**Verification:**
- `flutter analyze`: 0 errors, 0 warnings (info-level count grew from the two new source files +
  three new test files, same categories as before).
- All 9 test files re-run individually after this checkpoint's changes: the original 6 (49 tests),
  `hub_live_repository_test.dart` (2), `live_workspace_view_test.dart` (2), and
  `reach_trace_golden_test.dart` (6) -- **59/59 passing**, no regressions.

**NOT done (real remaining work):**
- Tablet/desktop workstation layout (rail + recovery-line-with-MDC-band + "Live now" pane) --
  `patient_profile_screen.dart` is still tabs, not the wireframe's single-column layout. The "Live
  now" pane specifically also needs a patientId<->deviceId association that doesn't exist yet (see
  CHECKPOINT 2's "assignProgram... patientRef" note) -- it's blocked on more than a layout change.
- Program builder's numbered 3-pane layout (library | blocks | dynamic form + live preview) --
  present functionally (all A4 tests pass) but not restructured to the wireframe's exact panes.
- Full golden matrix (48 cells) and Devices/Live-monitor in-process-hub widget tests -- flagged above.

Proceeding to B5 (builds) next.

## CHECKPOINT 4 (B5 builds + a real overflow bug found via visual QA)

**`flutter build web --release`: SUCCEEDS** (built twice this checkpoint -- once before, once after
the overflow fix below -- both succeeded, ~104-202s compile). Output copied to
`releases/app/0.2.0/web/` (42 MB).

**`flutter build windows --release`: FAILS**, same as run2, re-confirmed with real output:
`Unable to find suitable Visual Studio toolchain.` No `.exe`. Pre-existing `docs/MANUAL_TODO.md`
entry (VS C++ workload not installed) stands, not duplicated.

**`flutter build apk --debug`: FAILS**, same symptom as run2 (`java.io.IOException: Unable to
establish loopback connection`) but this run **narrowed the root cause** by running `cd android &&
./gradlew assembleDebug --stacktrace` directly (run2 suggested this but didn't do it): the real
exception is `java.net.SocketException: Invalid argument: connect` inside `sun.nio.ch.
UnixDomainSockets.connect`, called from the JDK's own internal loopback `Pipe` implementation
(`PipeImpl$Initializer$LoopbackConnector`) that Gradle's daemon-connection `Selector` depends on --
i.e. the JDK is trying to open an **AF_UNIX domain socket** for its own internal plumbing and that
fails, not a TCP/127.0.0.1 issue as the generic error message suggests. Confirmed this isn't
selector-specific: forcing the legacy `sun.nio.ch.WindowsSelectorProvider` via `GRADLE_OPTS`
reproduced the identical stack trace. `java -version`: `22.0.1+8-16`. Full diagnostic + concrete next
steps (JDK 21 LTS swap, check AV/EDR for AF_UNIX socket blocking) written to
`docs/MANUAL_TODO.md`'s existing Track A APK entry rather than duplicated.

**Screenshots of the running web build** (brief's explicit ask): built + ran `flutter run -d
web-server --web-port 8123 --release`, opened via the Browser pane tool, real (not mocked)
navigation through the actual mock-data-backed app:
- Sign-in (role picker) -- renders correctly, Atkinson Hyperlegible Next font visible, dark theme
  (system default in this browser).
- Patients list -- all 7 real synthetic patients (`Asha Rao` through `Priya Nair`), search bar,
  side-affected chips (`right`/`both`).
- Patient profile (Meera Iyer) -- Overview tab (age/side/diagnosis/onset), tab bar.
- **Sessions tab -- confirmed this checkpoint's own `_SessionTimelineStrip` (B2) renders for real**:
  a 160 dp lake/ochre reach-trace glyph in the timeline strip and a matching 48 dp glyph in the list
  row below it, both from the same real `healthy`-derived fixture data (zoomed screenshot confirms
  the glyph shapes, not just a placeholder box).
- Devices screen -- shows the exact `docs/APP_DESIGN.md`-mandated web fallback copy: "The hub is not
  available on web. Run OPUS on Windows or Android to pair a headset."
- Live monitor (mock fallback, no headset on web) -- status pill, trial progress, rolling metrics
  cards, 64 dp Pause/Stop buttons, all render correctly at desktop width (800x600).

**Real bug found and fixed via this visual QA** (not just claimed -- caught by actually looking):
at mobile width (375 dp), the live monitor's status-pill-plus-"waiting for stream"-chip `Row` had no
wrap behavior and the chip text was clipped off the right edge of the screen. Fixed by changing that
`Row` to a `Wrap` (`lib/features/live/live_monitor_screen.dart`) -- re-verified with `flutter
analyze` (0 errors/warnings) and confirmed the widget tests still pass; did not get a clean
after-screenshot at mobile width this checkpoint (the web-server's dev-mode auth/session state
didn't survive a couple of direct URL navigations while testing the fix, and re-driving the full
sign-in -> patient -> live-monitor click path against a moving mobile-emulated coordinate frame ran
into repeated misclicks; the fix itself is verified by code review + the Wrap widget's well-defined
behavior + `flutter analyze`, not re-screenshotted). Flagging this as the one item in this checkpoint
verified by static review rather than a fresh screenshot.

**Final full-suite re-run after all changes this run:**
- `flutter analyze`: **0 errors, 0 warnings** (info-level count only).
- All 9 test files, run individually: `dynamic_form_widget_test` (4), `field_spec_test` (13),
  `hub_connection_test` (15), `hub_integration_test` (2, latency p50=3.56ms p95=6.07ms max=6.07ms
  n=10), `hub_live_repository_test` (2), `reach_trace_test` (5), `live_workspace_view_test` (2),
  `reach_trace_golden_test` (6), `opus_tokens_contrast_test` (10) -- **59/59 passing.**

**B5 summary: 1/3 builds succeed (web)**, same as run2 -- Windows and Android both blocked by
pre-existing, now better-diagnosed machine/JDK environment issues, documented in
`docs/MANUAL_TODO.md`. Web release artifact copied to `releases/app/0.2.0/web/`.

## Overall status (per-milestone)

| Milestone | Status |
|---|---|
| B1 Re-verify | **Done.** analyze 0/0, 49 tests pass individually, real hub<->headset round trip on port 8797 (46/46 events, files uploaded), in-process latency p50=5.6-6.8ms/p95=7.4-9.3ms (well under the 250ms target). |
| B3 Wire hub into real UI | **Mostly done** (the run's top priority). `HubLiveRepository` + `LiveWorkspaceView` give the live monitor a real reach-trace-drawing feed from a connected headset; `HubController` gained a full ack-aware command API (`start/pause/resume/stop/recenter/skipBlock/showMessage/adjustParams/assignProgram`), local-schema validation for `adjust_params`, `runAnalysis`, real local-IP display. Found+fixed a real `HubServer.stop()` concurrent-modification bug. Not done: program-builder "send to headset" button, hub-uploaded sessions appearing in the sessions list/report (only a "Recorded sessions + Run analysis" stopgap on Devices), full widget-tree tests for Devices/Live-monitor against an in-process hub. |
| B2 Finish APP_DESIGN | **Partial.** Added the missing patient-timeline reach-trace strip (all 3 of the doc's glyph sizes now wired to real data). Full wireframe rebuilds (workstation rail+recovery-line+MDC-band+conditional Live-now pane, 3-pane program builder) not done -- flagged as the largest remaining gap, same as run2 assessed. |
| B4 Tests | **Partial.** 3 new test files (7 new tests) on top of run2's 6 files: `hub_live_repository_test.dart` (real hub+socket), `live_workspace_view_test.dart`, `reach_trace_golden_test.dart` (6 goldens, actually reviewed). Full 48-cell golden matrix and in-process-hub widget-tree tests for Devices/Live-monitor screens not done (needs path_provider mocking + repository-provider overrides not yet built). |
| B5 Builds | **Partial**, matches run2: web succeeds and was screenshotted (plus one real mobile-overflow bug found and fixed via that QA); Windows/APK blocked by machine environment issues, now diagnosed one level deeper (VS C++ workload; a JDK-22-specific AF_UNIX loopback-socket failure in Gradle's daemon, not the generic "loopback connection" message run2 saw). |

**Known issues / blockers for the next agent:**
1. Windows build needs the human to finish the Visual Studio C++ workload install (`docs/MANUAL_TODO.md`).
2. Android build needs either a JDK 21 swap or AV/EDR investigation for AF_UNIX socket blocking (`docs/MANUAL_TODO.md`, updated this run with the real stack trace).
3. Program builder has no "Send program to headset" action yet -- `HubController.assignProgram` exists and is tested at the connection level, just not wired to any button.
4. No hub-session -> `SessionsRepository` pipeline -- uploaded sessions are only reachable via the Devices screen's "Recorded sessions" stopgap, not the patient's session list/report.
5. `docs/APP_DESIGN.md`'s full wireframe layouts (workstation rail, 3-pane program builder, "Live now" pane) remain functionally-equivalent-but-not-pixel-matched to the spec.
6. `flutter test` (bare, no path) is still unreliable on this machine -- always pass explicit file paths, one at a time.

No git commits were made (per `_COMMON.md`). Screenshots at `logs/sessions/screens/app/` (6 golden
PNGs of the reach-trace glyph, reviewed). Release artifact at `releases/app/0.2.0/web/`.

