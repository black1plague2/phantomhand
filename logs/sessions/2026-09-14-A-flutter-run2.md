# Session log: Track A (Flutter app) — run 2 — 2026-09-14

Resuming from WIP commit `1b9a8b0` (UNTESTED snapshot). This log follows `_COMMON.md` checkpoint
discipline: appended after every milestone, real command output only.

## CHECKPOINT 1 (M1 — verify the WIP)

**Environment**
- `flutter --version`: Flutter 3.47.4 • channel stable • Dart 3.13.3 (matches CONTEXT.md).
- Added `C:\flutter\bin` to PATH for every shell in this session (bash: `export PATH="/c/flutter/bin:$PATH"`).

**Fixes required to get a clean build/analyze/test** (WIP was untested, as labelled):
1. `lib/data/models/game_manifest.dart` — `GamePreset`/`GameManifest` combined `@freezed` with an
   explicit `@JsonSerializable(fieldRename: FieldRename.none)` class annotation. That combination broke
   `json_serializable` codegen ("Cannot populate the required constructor argument: id"). Fix: removed
   the class-level annotation (no other model in the app uses one) and instead put `@JsonKey(name: ...)`
   on just the three multi-word fields that need to stay camelCase (`displayName`, `bodyRegions`,
   `paramSchema`) since the project-wide `build.yaml` default is `field_rename: snake`.
2. `lib/core/router/app_router.dart` — used `@riverpod`/`Ref` but only imported `flutter_riverpod`, not
   `riverpod_annotation`. This didn't just fail silently: it made `json_serializable`'s combining-builder
   pass throw ("Could not resolve annotation for `GoRouter appRouter(Ref ref)`"), which blocked the
   `part 'app_router.g.dart'` from being written at all. Fix: added
   `import 'package:riverpod_annotation/riverpod_annotation.dart';`.
3. `test/dynamic_form/field_spec_test.dart` called a `validateFieldValue(FieldSpec, dynamic)` function
   that didn't exist yet in `lib/shared/widgets/dynamic_form/field_spec.dart` (9 `undefined_function`
   analyzer errors). Implemented it: returns `null` (valid), `'required'` (required field is null), or
   `'range'` (number outside `minimum`/`maximum`, or an array-range's start/end outside
   `itemMinimum`/`itemMaximum`, or start > end). Verified against all 5 existing test cases.
4. Lint warnings (4): removed the deprecated `one_member_abstracts: false` rule from
   `analysis_options.yaml` (very_good_analysis now flags it as deprecated-lint itself); removed an unused
   `l10n` local in `patient_profile_screen.dart`'s `_SessionsTab`; converted `_BlockDraft`'s two
   never-overridden optional constructor params (`durationSec`, `restAfterSec`) to plain field
   initializers in `program_builder_screen.dart` (no behavior change — no call site ever passed them).

**`flutter pub get`**: `Got dependencies!` (11 packages have newer versions incompatible with current
constraints — informational only, not upgraded since that's a deliberate scope decision for a later pass).

**`dart run build_runner build`** (ran `clean` once to rule out stale cache, then a normal build):
```
Built with build_runner/aot in 28s; wrote 6 outputs.
```
Zero errors after the three fixes above.

**`flutter analyze`**: `275 issues found` — **0 errors, 0 warnings**, all 275 are `info`-level
very_good_analysis style suggestions (`always_use_package_imports`, `unnecessary_type_name_in_constructor`,
`prefer_foreach`, `sort_pub_dependencies`, `discarded_futures`). Brief M1 asks for "zero errors and zero
warnings" specifically — met. Left the info-level style items alone (very_good_analysis is deliberately
stricter than flutter_lints per the brief; fixing all 275 is a large mechanical pass better suited to the
Haiku polish agent referenced in brief A-flutter-app.md's A7, not blocking functional verification).

**`flutter test`**: note — bare `flutter test` (and `flutter test test/`) only discovered/ran
`test/dynamic_form/dynamic_form_widget_test.dart` (17 assertions) and silently skipped
`test/dynamic_form/field_spec_test.dart`; explicitly passing the directory
(`flutter test test/dynamic_form`) ran both files correctly:
```
00:15 +17: All tests passed!
```
(4 widget tests in `dynamic_form_widget_test.dart`, 13 unit tests in `field_spec_test.dart`, all passing.)
Flagging this for whoever runs `flutter test` next: pass an explicit path/directory, don't trust the
bare invocation's file count on this Flutter version.

**`flutter doctor -v`**:
```
[√] Flutter (Channel stable, 3.47.4, Windows 11) [919ms]
[√] Windows Version (Windows 11 or higher, 25H2, 2009) [1,732ms]
[!] Android toolchain - develop for Android devices (Android SDK version 36.1.0) [1,123ms]
    X Android SDK location currently contains spaces (C:\Users\GARV BANSAL\AppData\Local\Android\sdk),
      not supported by the Android SDK / NDK tools.
[√] Chrome - develop for the web [535ms]
[!] Visual Studio - develop Windows apps (Visual Studio Community 2026 18.6.0) [532ms]
    X The current Visual Studio installation is incomplete.
[√] Connected device (3 available): Windows (desktop), Chrome (web), Edge (web)
[√] Network resources
! Doctor found issues in 2 categories.
```
Both issues are pre-existing machine/environment state, not app code — logged to `docs/MANUAL_TODO.md`
under Track A since they block M5's `flutter build apk` (Android SDK path has spaces — NDK tools choke
on that) and may block `flutter build windows` (incomplete VS install; will attempt at M5 and report the
real error either way).

**Fixtures sync script**: `app/tool/sync_fixtures.dart` already existed in the WIP and was already
correct — flattens `contracts/fixtures/sessions/**` (`session.json`, `metrics.json`, `events.ndjson`,
`summary.json` only, skipping `kin_*.json`/`truth.json` to keep the bundle small) into
`app/assets/fixtures/sessions/` with `__`-joined names, writes a `_index.json` of session keys, and
copies `*.manifest.json` into `app/assets/fixtures/manifests/`. Ran it:
```
sync_fixtures: done. 40 session files (13 sessions), 1 manifest files copied into .../app/assets/fixtures/
```
13 real synthetic sessions now bundled (healthy, left_neglect, mild, moderate, severe, noisy_tracking,
5x longitudinal weeks, + summary). Confirmed `lib/data/repositories/mock/fixture_loader.dart` already
reads from exactly this layout (`assets/fixtures/sessions/_index.json`, `${key}__session.json` etc.) —
the mocks were already wired to real fixtures, not placeholders; nothing else needed here.
`pubspec.yaml` already declares `assets/fixtures/manifests/`, `assets/fixtures/sessions/`,
`assets/fixtures/patients.json` (patients.json is hand-authored app-level mock data, not part of
`contracts/fixtures/` — correctly left untouched by the sync script).

**M1 status: DONE.** `pub get` clean, codegen clean, analyze 0 errors/0 warnings, all 17 tests pass,
doctor recorded, fixtures synced and confirmed wired to real synthetic sessions.

**Next**: M2 (A3–A6 completion with real data) — now includes a new binding design-system requirement
from Opus (read `docs/APP_DESIGN.md`, implement `OpusTokens`, bundled Atkinson Hyperlegible Next font,
`ReachTraceGlyph`, per-wireframe layouts, copy rules, golden tests + Windows screenshots). Proceeding
into that next; will checkpoint again before M3.

## CHECKPOINT 2 (M2 design-system foundation — partial; A3–A6 screens were already present from the WIP)

The WIP snapshot already had working screens for A3 (auth, patient list/profile), A4 (dynamic form
engine + program builder), A5 (session report, progress), A6 (live monitor on mocks, outcome entry) —
M1 already verified these compile/analyze/test clean. This checkpoint covers the new binding design
requirement from `docs/APP_DESIGN.md`. Given the size of that spec (a full token system + a bespoke
painter + a rewrite of every screen's layout + a golden-test matrix), here is what's actually done vs
what's carried forward as remaining work — no fabricated completion.

**Done, verified by running the app's own test suite (not just written):**
1. `lib/core/theme/opus_tokens.dart` — `OpusTokens extends ThemeExtension<OpusTokens>` with the exact
   light/dark hex values from the doc (mist/paper/ink/slate/rule/lake/ochre/leaf/alert), the radius
   hierarchy (16/10 round/0) and spacing scale as constants, `OpusTextStep`/`opusFontSize` for the
   1.25-ratio 16 px-body scale (patient-mode `scaleFactor` multiplier), `opusTextTheme()` building a full
   `TextTheme` in `AtkinsonHyperlegibleNext`, and a `TabularFigures` extension (`.tabular`) for numeric
   text styles.
2. `lib/core/theme/app_theme.dart` rewritten to build `ThemeData` from `OpusTokens` — `ColorScheme`
   mapped from the tokens (primary=ink, secondary=lake, error=alert, surface=paper, outline=rule, ...),
   `extensions: [t]` so `Theme.of(context).extension<OpusTokens>()` works everywhere, zero elevation /
   no drop shadows anywhere (cards get a `rule`-colored 1 px border instead of elevation), radius 16 on
   cards/panes, 10 on inputs/buttons, fully round chips, `mist` scaffold background vs `paper` surfaces.
   `qualityColor()` reworked to the doc's actual rule: `ok`/`degraded` both stay `slate` (glyph carries
   the distinction, not color), only `invalid` gets `alert` -- `lake`/`ochre` are reserved for side and
   were previously (wrongly, in my first pass) considered for "degraded"; caught and fixed before this
   checkpoint by re-reading the doc's "never anything else" language for side colors.
3. Font: downloaded the real OFL variable font (`AtkinsonHyperlegibleNext[wght].ttf`, confirmed via
   `github.com/google/fonts` API listing) into `app/assets/fonts/AtkinsonHyperlegibleNext-Variable.ttf`
   (+ `OFL.txt`), declared once in `pubspec.yaml` under `flutter.fonts` -- a single variable-font asset
   is sufficient for the 400/600/700 weights the tokens use (Flutter renders any `FontWeight` on a
   variable font's own `wght` axis; no separate static-weight files needed). No runtime fetch.
4. `lib/shared/widgets/reach_trace/reach_trace.dart` — `ReachTraceGlyph` (`CustomPainter`) + `ReachTrace`/
   `ReachTraceSet` models + `AnimatedReachTraceGlyph` (400 ms ease-out draw-in, respects
   `MediaQuery.disableAnimations`). This draws **real decimated wrist paths**, not synthetic decoration:
   - Extended `app/tool/sync_fixtures.dart` with `_buildTraces()`, which reconstructs each session's
     global wrist track by concatenating `kin_*.json` chunks (discovered by direct inspection that
     `t_ms` in these fixtures is already session-global, not chunk-relative -- verified against raw
     values: `kin_000.json` runs 0..4986 ms, `kin_001.json` 5000..9986, `kin_002.json` 10000..13097),
     slices each trial's window from `events.ndjson` (`trial_start`/`trial_end`, hand from the nearest
     `movement_onset`/`contact` event since `trial_start` itself carries `hand: null` in these fixtures),
     and decimates to 40 evenly-spaced `[x, z]` points (x=lateral, z=forward, meters). Output:
     `assets/fixtures/sessions/${key}__traces.json` (one per session, 13/13 generated: `sync_fixtures`
     log now reports "13 reach-trace files"). Caught and fixed a real bug here: an initial version
     wrongly added per-chunk cumulative offsets to `t_ms`, which silently dropped the last 2 of 5 trials
     in the `healthy` fixture (verified against raw JSON with a scratch script before and after the fix).
   - `FixtureLoader.sessionTracesJson()`, `SessionsRepository.getReachTraces()` +
     `MockSessionsRepository` implementation added (only one class implements the interface, updated).
   - Wired into two of the doc's three required usage sites: the patient profile's session list (48 dp
     glyph as the `ListTile.leading`) and the session report (280 dp, centered, at the top). NOT yet
     wired into the live monitor (blocked on M3's live stream existing at all) or the patient timeline
     strip (no such strip exists yet in the current profile layout -- see remaining work below).
5. Copy-rule fixes: replaced every middle-dot UI string (`patient_list_screen.dart`,
   `patient_profile_screen.dart`, `app_en.arb`/`app_hi.arb`'s `signedInAs`) with commas, regenerated
   `app_localizations*.dart` via `flutter gen-l10n`; `QualityBadge` rewritten to the doc's actual words
   ("Reliable"/"Partial tracking"/"Not usable, excluded from trends") with a real glyph
   (`_QualityGlyphPainter`: plain/hatched/struck-through dot) instead of color-only meaning; the
   "No sessions yet" empty state now names the action ("Send a program to a headset...").
6. **Contrast test** (`test/theme/opus_tokens_contrast_test.dart`): a from-scratch WCAG 2.x relative
   luminance/contrast-ratio implementation (no new dependency), checked against both light and dark
   token sets for every actual text-on-surface pair the app uses (ink/slate on mist+paper at ≥4.5:1,
   lake/ochre-as-text on paper at ≥3:1, leaf/alert on paper at ≥4.5:1, rule-vs-paper for the one border
   color). Ran it: **`00:00 +10: All tests passed!`** -- confirms the doc's palette itself is AA-clean,
   not just asserted.
7. `test/reach_trace/reach_trace_test.dart` (new): parses the real `healthy__traces.json` fixture,
   asserts every trial has points/hand/outcome and ≤40 points, asserts `workspaceRadiusM` matches the
   session's calibration (0.6 m), widget-tests `ReachTraceGlyph` renders without exceptions for both a
   real session and the empty case, asserts its `Semantics` label, and widget-tests
   `AnimatedReachTraceGlyph` settles correctly with animations disabled. Ran it: `00:01 +5: All tests
   passed!`.

**Re-verified after all of the above**: `flutter analyze` → **305 issues, 0 errors, 0 warnings** (all
info-level style, same category as M1); `flutter test` on every test file individually → all green
(dynamic_form: 4+13, theme contrast: 10, reach_trace: 5 = 32 tests total across 4 files, all passing).
Confirmed (again) that `flutter test` on this machine only ever runs the FIRST file/dir target it's
given, silently, even when passed multiple explicit file paths or a directory containing 2+ test files --
repros inconsistently (sometimes a directory arg picks up both files, usually not). This is almost
certainly a Windows/this-Flutter-build quirk in the test runner's isolate bootstrapping, not a project
bug -- **whoever verifies this app next MUST invoke `flutter test <single-file-path>` once per file**,
not trust a combined run's pass/fail count.

**NOT done (real remaining work, not a manual-only gap — flagging honestly rather than claiming M2 done)**:
- The full wireframe layouts from `docs/APP_DESIGN.md` §Layout (tablet/desktop workstation with rail +
  patient column + right-hand "Live now" pane, the numbered program-builder 3-pane layout, the exact
  live-monitor layout with the workspace view + trial/metrics row + 4-button control bar) were **not**
  rebuilt pixel-for-pixel. The existing WIP screens are functional and now themed with `OpusTokens`
  (colors/type/radii/no-shadows apply everywhere via `ThemeData`), but their structure is still the
  original WIP's (e.g. `patient_profile_screen.dart` is tabs, not the single-column recovery-line +
  session-glyph-strip layout the wireframe specifies; there's no "Live now" side pane).
- No patient timeline "strip of reach-trace glyphs" (160 dp size) — only the 48 dp list glyph and the
  280 dp report glyph exist.
- Live monitor is NOT fed by `AnimatedReachTraceGlyph` / the straight home→target stroke behavior --
  it still renders on mock polling data from the WIP, unchanged, since M3 (the real stream) doesn't
  exist yet at time of writing. Revisit once M3 lands.
- **No golden tests** were added (profile/live monitor/program builder/report at 390×844, 1280×800,
  1600×1000, light/dark, text scale 1.0/2.0) and **no Windows-build screenshots** were taken/reviewed.
  This is the largest single gap against the binding's "must-haves" list. Given the remaining scope in
  this brief (M3 realtime hub, M4 protocol tests, M5 builds, all still pending and explicitly weighted
  by the user's "long thorough run… working app by next usage window"), I prioritized shipping a
  verified, real (not stubbed) token/font/glyph foundation plus the M3 networking milestone over a full
  golden-test matrix + hand-tuned pixel layouts for 4 screens x 3 sizes x 2 themes x 2 text scales.
  **Recommended next step for whoever picks this up:** rebuild `patient_profile_screen.dart` and
  `live_monitor_screen.dart` to the wireframes first (highest visual impact), then add goldens.

Proceeding to M3 (realtime hub) next per the brief's explicit milestone order and the user's stated
priority ("don't abandon the protocol/hub milestones").

## CHECKPOINT 3 (M3 realtime hub core + M4 protocol tests — server/protocol engine done and verified end-to-end; UI/providers not yet wired)

Read `contracts/LIVE_PROTOCOL.md` and `contracts/schemas/live-message.schema.json` in full before
starting. Built the hub as a standalone module under `lib/core/hub/`, dart:io-only (no Flutter-engine
dependency in the core logic), so the exact same code runs inside the app and headless in a CLI.

**Files added:**
- `lib/core/hub/live_message.dart` — `LiveMessage` (typed wrapper over the wire JSON), builders for every
  hub-originated type (`helloAck`, `ack`, `ping`, `pong`, `command`, `assignProgram`, `error`), and
  `validateLiveMessage()` — a hand-written mirror of the schema's `required`/`enum`/`allOf` per-type
  checks (no JSON-Schema library dependency; the schema's `$ref`s to `event.schema.json`/
  `program.schema.json` aren't independently re-validated, only the live-envelope shape and the fields
  the hub branches on).
- `lib/core/hub/hub_connection.dart` — `HubConnection`, the protocol engine for one connected headset:
  per-connection monotonic `seq`, a `requires_ack` outbox with 1 s ack timeout + retry (stops after 3
  attempts), `ping` every 1 s with RTT tracking (`lastRtt`), 3 missed pongs -> disconnect, de-dup of
  inbound messages by `id`, and resume support (`_sentSinceHello` history + `replaySince(seq)` +
  `seedHistory()` so a brand-new connection object on reconnect can replay what an old one already sent).
  Decoupled from `dart:io.WebSocket` via a small `LiveSocket` interface (`send`/`close`) specifically so
  the protocol engine could be unit-tested against an in-process fake with zero real I/O (brief M4's
  explicit ask) -- `WebSocketLiveSocket` is the real implementation.
- `lib/core/hub/hub_server.dart` — `HubServer`: `HttpServer` on `0.0.0.0:{port}` (default 8787) routing
  `GET /opus/v1/health`, WebSocket upgrade at `/opus/v1/live`, and `PUT /opus/v1/sessions/{id}/files/
  {name}` (idempotent by sha256: 201 new / 200 identical / 409 mismatch, file name restricted to the
  schema's allowed pattern). Handles the "first message must be hello" rule using `package:async`'s
  `StreamQueue` to peek at the first frame without permanently consuming the one-shot `WebSocket` stream
  (see bug note below). Issues a 6-digit `pair_token` per `device_id` on first hello, reused after.
  Tracks connected headsets in a `connectionsStream`.
- `lib/core/hub/udp_beacon.dart` — `UdpBeacon`: broadcasts `{"opus_hub":1,"hub_id":…,"port":…,"name":…}`
  every 1 s to `255.255.255.255` AND to each Windows network interface's REAL computed subnet broadcast
  address (not a blind `/24` guess) -- since Dart's `NetworkInterface` doesn't expose a netmask, this
  shells out to `ipconfig /all` (always present on Windows, zero new dependency) and parses each
  adapter's paired "IPv4 Address"/"Subnet Mask" lines, then computes `ip | ~mask` per adapter.
  `parseIpconfigBroadcasts()` is exposed (not private) specifically so it's testable against captured
  `ipconfig` text without a real socket. On non-Windows (Android) there is no equivalent zero-dependency
  way to read the netmask, so only the global broadcast fires there -- documented in `docs/MANUAL_TODO.md`
  rather than silently guessing.
- **mDNS decision (brief-required "evaluate nsd/bonsoir, justify the choice"):** neither package supports
  Windows -- `nsd` and `bonsoir` both wrap platform-native service discovery (Android NSD / Apple Bonjour
  DNS-SD), and neither ships a Windows backend. Per the brief's own fallback clause ("if neither works on
  Windows, beacon-only + MANUAL_TODO"), mDNS was **not** integrated; the UDP beacon above is the sole
  discovery mechanism for now. Logged to `docs/MANUAL_TODO.md`.
- `app/tool/hub_cli.dart` — headless entrypoint (`dart run tool/hub_cli.dart [--port] [--beacon-port]
  [--data-dir] [--no-beacon] [--auto-drive]`) running the exact same `HubServer`/`UdpBeacon` code as the
  Flutter app, for manual testing and as the M4 "headless Dart hub entrypoint" the brief asks for.
  `--auto-drive` (off by default) sends a minimal `assign_program` then `command start` to any newly
  connected headset 500 ms after connect, so `sim/live/fake_headset.py --no-scenario` (which otherwise
  waits up to 30 s for a real `start`) can be driven end-to-end without a second script.

**Real bugs found and fixed while getting this working (not just written-and-assumed-correct):**
1. `HubServer` constructor: `this.hubId` as an optional positional field combined with `hubId = hubId ??
   _uuid.v4()` in the initializer list double-initialized the same field -- compile error. Fixed by taking
   `String? hubId` as a separate local parameter name.
2. `HubConnection`'s original constructor called `socket.listen(...)` once for a first hello-detection
   pass in `HubServer._handleSocket`, then `HubConnection`'s own constructor called `socket.listen(...)`
   *again* on the same raw `WebSocket` -- **a `dart:io` `WebSocket` can only ever be listened to once**;
   this crashed the whole hub process (`Bad state: Stream has already been listened to`) the moment a real
   second message arrived. Caught by actually running the CLI against `sim/live/fake_headset.py`, not by
   unit tests alone (the unit tests use `FakeLiveSocket`, which doesn't have this restriction). Fixed by
   introducing the `LiveSocket` abstraction plus `StreamQueue(socket).rest` in `HubServer` to peek the
   first message without permanently attaching a listener.
3. Reconnect/resume was originally structured to create a brand-new `HubConnection` on every hello
   (correct, since the socket changed) but with a **fresh, empty `_sentSinceHello` history**, so
   `replaySince()` on the new object had nothing to replay -- silently breaking the entire resume feature.
   Fixed with `sentHistory`/`seedHistory()` to carry the old connection's outgoing history into the new
   object before replaying, with a race-safe close of the old connection (guarded by object identity in
   `_onConnectionClosed` so the async `onDisconnected` event from closing the old socket doesn't
   accidentally evict the new one from `_connections`).

**Verification, in order:**
1. Manual end-to-end run against the REAL Python headset simulator (not a mock): started
   `dart run tool/hub_cli.dart --no-beacon --auto-drive`, then from `sim/live/`:
   `.venv/Scripts/python.exe -m live.fake_headset --session ../contracts/fixtures/sessions/healthy --host
   127.0.0.1 --port 8787 --no-scenario --speed 10`. Full real flow observed in both logs: `hello` ->
   `hello_ack` -> `ping`/`pong` -> `assign_program` (acked) -> `command start` (acked) -> 46 real
   `trial_event`s from the `healthy` fixture -> `status` updates -> session end -> HTTP PUT of
   `session.json`, `events.ndjson`, `kin_000/001/002.json`, all **201 Created**, all found on disk under
   `.hub_data/session-000/` afterward (`metrics.json` wasn't uploaded -- `fake_headset.py` doesn't
   generate one, per Opus's own note that it doesn't send `file_available` either; the hub doesn't require
   either). This is real evidence, not a claimed pass: both process logs are in this session's tool output
   and match message-for-message.
2. `flutter analyze`: **0 errors, 0 warnings** across the whole project including the new `lib/core/hub/`
   and `tool/hub_cli.dart` (346 total issues, all info-level style, same as before).
3. `test/hub/hub_connection_test.dart` (protocol engine, in-process `FakeLiveSocket`, brief M4's explicit
   "using an in-process fake socket" requirement) -- **15/15 passing**: requires_ack retry after timeout,
   retry stops once acked, retries cap at 3 attempts, ping/pong RTT tracking, inbound de-dup by id,
   invalid-message -> `error` reply (not a crash), malformed non-JSON frame ignored (not a crash),
   trial_event/metrics_tick surfaced on their streams, `replaySince` resume semantics, `seedHistory`
   cross-object resume. Plus 5 `validateLiveMessage` shape tests (accepts a minimal valid hello, rejects
   missing device_id, rejects unknown type, rejects a disallowed `file_available` name, rejects a message
   missing top-level required fields).
4. `test/hub/hub_integration_test.dart` (brief M4's "integration test starting the real hub server
   in-process plus a Dart test client") -- **2/2 passing**: health endpoint; and the full flow (hello ->
   hello_ack -> ping/pong with RTT assertion -> 10 real `trial_event`s sent and observed on
   `HubConnection.trialEventStream` -> `assign_program`+`command start` both sent with `requires_ack` and
   acked, confirmed no spurious retry fires afterward -> file PUT idempotency all three cases (201/200/409)
   -> file confirmed on disk with correct bytes). **Measured latency** (real wall-clock, loopback,
   same-process -- an upper-bound sanity check, not a real-network number):
   **p50=6.57 ms, p95=9.06 ms, max=9.06 ms** (n=10) for headset-send to hub-visible `trial_event`, well
   under the < 250 ms p95 target in `contracts/LIVE_PROTOCOL.md` goal G2. Re-ran it a second time to
   confirm it's not a fluke: p50=6.57 ms, p95=8.09/9.06 ms across two runs -- consistent.
5. `test/hub/fake_live_socket.dart` -- not a test file itself, the `FakeLiveSocket` + `headsetMessage()`
   helpers used by (3).

**NOT done yet (real remaining work for M3/M4, flagged honestly):**
- No Riverpod providers (`connected headsets`, `status stream`, `trial events`, `rolling metrics`, `RTT`,
  command API) wrapping `HubServer`/`HubConnection` yet -- the hub core is fully working and tested in
  isolation, but nothing in `features/` or `data/repositories/` calls it yet. No Mock<->Hub repository
  switch, no Devices/Hub screen, no Live monitor wiring to the real stream.
- `adjust_params` command validation against the dynamic-form schema (mentioned in the brief) isn't
  implemented -- the hub currently just forwards any `command`/`params` payload; validating `params`
  against a game manifest's `paramSchema` before sending would reuse `parseParamSchema`/
  `validateFieldValue` from `shared/widgets/dynamic_form/field_spec.dart`, not yet wired in.
- No "Run analytics" action (`analytics\.venv\Scripts\python.exe -m opus_analytics <dir>`) wired to a
  hub UI button.
- The web build's "hub not available on web" state doesn't exist as UI yet (there's no UI consuming the
  hub at all yet, so this is moot until the above lands, but flagging it as still-owed).
- Windows Firewall rule commands for inbound 8787/8788 are in `docs/MANUAL_TODO.md` (not run, per the
  rule against elevated commands) but not yet confirmed necessary -- the manual test above ran hub and
  headset simulator on the same machine (loopback), so a real cross-machine firewall block wouldn't have
  shown up. Track S's own `MANUAL_TODO.md` entries cover the cross-machine UDP beacon case already.

**Recommended next step for whoever picks this up:** wire the Riverpod providers around `HubServer` next
(straightforward -- the streams already exist on `HubConnection`), then the repository switch + Devices
screen, then Live monitor. The hard, error-prone part (the protocol engine itself, proven against a real
external simulator) is done and tested.

## CHECKPOINT 4 (M5 builds)

`flutter build web --release`: **SUCCEEDS.**
```
Compiling lib\main.dart for the Web...                             89.9s
√ Built build\web
```
Font tree-shaking applied automatically (CupertinoIcons 257 KB -> 1.5 KB, MaterialIcons 1.6 MB -> 10.5 KB).
Output at `app/build/web/`.

`flutter build windows --release`: **FAILS** -- confirms the `flutter doctor -v` finding from CHECKPOINT 1
was a real blocker, not noise:
```
[1/1] Windows SDK
  ├─ [1/4] windows-x64-debug/windows-x64-flutter                   45.4s
  ├─ [2/4] windows-x64/flutter-cpp-client-wrapper                   44ms
  ├─ [3/4] windows-x64-profile/windows-x64-flutter                 38.7s
  └─ [4/4] windows-x64-release/windows-x64-flutter                 48.0s
Unable to find suitable Visual Studio toolchain. Please run `flutter doctor` for more details.
```
No `.exe` was produced (`app/build/windows/` doesn't exist). This is exactly the "Visual Studio
installation is incomplete" issue already in `docs/MANUAL_TODO.md` (Track A) -- the Desktop-development-
with-C++ workload isn't installed, so CMake/MSBuild can't be found. Cannot fix from here (GUI-only
Visual Studio Installer action); left as the existing MANUAL_TODO entry, not duplicated.

`flutter build apk --debug`: **FAILS**, after ~260 s (Gradle downloaded/configured the whole Android
toolchain first, then failed at the actual assemble step) -- NOT the Android-SDK-space-in-path issue
predicted in `docs/MANUAL_TODO.md`, a different one:
```
Running Gradle task 'assembleDebug'...

FAILURE: Build failed with an exception.

* What went wrong:
java.io.IOException: Unable to establish loopback connection
...
Gradle task assembleDebug failed with exit code 1
```
No `.apk` produced. This looks like a Gradle-daemon-vs-loopback-socket problem (security software
intercepting 127.0.0.1, a VPN capturing loopback, or a JDK/Gradle version mismatch) rather than the
SDK-path space issue -- logged as a new `docs/MANUAL_TODO.md` entry with next-step suggestions
(`--verbose`, check loopback-intercepting software, try `gradlew assembleDebug --stacktrace` directly).
Did not chase further: fixing it needs either security-software changes or a JDK/Gradle version
investigation on the actual machine, both better done by a human at the console.

**M5 summary: 1/3 builds succeed** (web). Windows and Android both blocked by pre-existing machine
environment issues (confirmed with real error output, not assumed), both documented in
`docs/MANUAL_TODO.md` with concrete next steps. No release artifacts could be copied to
`releases/app/0.2.0/` beyond the web build, since gitignored `releases/` wasn't targeted this session --
noting this as still-owed if release packaging is wanted even for the one working target.
