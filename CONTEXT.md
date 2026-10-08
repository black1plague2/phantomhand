# Chetna — Living Context (READ FIRST, UPDATE LAST)

> Handoff file. Any session, account, or agent picking this project up reads this file first and
> updates it before stopping. If this file is stale, the project is in an unknown state: fix that first.
>
> **Update rule:** at the end of every work block, update §2 (Current state), §3 (Next actions),
> §5 (Blockers), add a line to `logs/CHANGELOG.md`, and write a session log in `logs/sessions/`.

---

## ★★★★★ PHANTOM HAND (2026-10-08, Opus) — NEWEST, read first
**What it is:** Phantom Hand = PRD v2 (`docs/agent-briefs/ph/PRD_v2.md`), spec `docs/agent-briefs/ph/03-SPEC.md` v3.1, Theme 5 entry.
A virtual arm, offset 15 cm, is stroked by a VR brush while the sleeve vibrates on the real forearm in step; a stone drops on it
and the real arm flinches; a 600 ms touch delay dissolves the feeling; the witness screen shows both states with their numbers.
Path: Quest 3 (Unity) → laptop hub (Flutter app) → session files → Python analytics. Two ESP-WROOM-32 sleeve nodes on a power bank.
**User decisions (binding):**
- The local v2/v3 app design won the merge with GitHub `main` (commit cf80040).
- Unity only through the Unity MCP with the open editor, one driver at a time. This overrides "batch mode only" for PH (see `docs/agent-briefs/ph/PH_STATUS.md` Flags 3).
- Phone = app hub over USB: `adb forward tcp:8787 tcp:8787`. Phone serial 164cd676, M2101K6P, 393 dp (`logs/sessions/2026-10-08-PH-A-A1b-run1.md`).
- Electronics and firmware are owned by a separate electronics team (2026-10-07). `firmware/` F1/F2 code is a reference only, uncompiled. Interface: `docs/PH_ELECTRONICS_INTERFACE.md`.
- Additions A1–A5 approved (spec §12). D9: default condition order `async_first`. D10: passthrough for the reveal only (addition A2).
**Where:** branch `claude/project-thread-qrz2a9`, PR #4 on black1plague2/chetna (manager brief 2026-10-08). Local HEAD `d59d75c`.
**Done (O2 PASS; logs `logs/sessions/2026-10-0[78]-PH-*`; full table in `docs/PH_STATUS.md`):**
- O1 contracts v0.2 `d7dbb83`; U1 SDK `c900521`; U2 logic+scene `8f7a734`; U3 arm/brush/threat `a265205`; S1 twin `e053f7b`;
  S2 E2E harness + live plot `01551de`; S2b fixtures `d59d75c`; N1 embodiment `082ec6a`; N1b delivery quality `becdd04`;
  A1 live card `aa2d58c`; A1b `248cf23`; A1c `533e875`; A1d hub `last_status` `a7c50bb`.
- Tests (latest): EditMode 290/290 (`logs/sessions/2026-10-08-PH-U-U3-run1.md`); PlayMode PhantomHand 20/20 (same log);
  flutter +303, analyze 0 errors (`logs/sessions/2026-10-08-PH-A-A1d-run1.md`); analytics 83 (`logs/sessions/2026-10-08-PH-N-N1b-run1.md`);
  tools/demo 114 and sim/live 45 (`logs/sessions/2026-10-08-PH-S-S2b-run1.md`); sim/sleeve 38 (`logs/sessions/2026-10-07-PH-S-S1-run1.md`).
- L3 smoke 7/7 with `--no-unity` (fixture stamps, not real timing): `logs/sessions/2026-10-08-PH-S-S2-run1.md`.
**Not done:** U4 (partial, uncommitted, no log) · U5 · U6 · S3, A2, N2 (stretch) · H2, H4, H5 · F1/F2 are the electronics team's.
**Next:** U4 resume → swap in the 8 Meta models (paths in `docs/PH_STATUS.md`; hand has no rig) → U5 composition + live link →
L3 with Unity (`run_pipeline.py --game phantom_hand --sim`, no `--no-unity`) → gate G2 (one full 4-min run) → additions A1–A3.
**Known traps:**
- Usage limits killed builders twice (A1 run1 was killed and resumed as run2 from its log; U4 was interrupted). Resume from the log, never from memory.
- The Unity MCP drops when the editor restarts; reconnect with `/mcp` → `meta-xr-unity-runtime` before any Unity run.
- A test run removed the phone's adb forward. Restore it with `adb forward tcp:8787 tcp:8787`. Nothing on the PC may bind 8787 while it is active.
- Orchard PlayMode runs overwrite tracked screenshots under `logs/sessions/screens/unity`. Run `git checkout` on them afterwards.
- Still open: HapticIntegration `low_confidence` regression (U3 log: cueOrder `[trunk_lean, success x4]`, low_confidence 0 in 40 s);
  LiveLink 8787 conflict (adb holds the port); audit test 6.2 cm vs 5 cm flake. U3 O2 follow-ups in `logs/sessions/2026-10-08-PH-U-U3-run1.md`.
- FLAG FOR OPUS: fixture drift in the N1 log is 2.4/0.4 (fixture witness numbers) vs 2.00/0.00 (analytics computed); see `docs/PH_STATUS.md` Flags.

## ★★ LATEST (2026-09-19 ~04:30 IST, Opus) — read this before the older box below
**Real-hands play over Quest Link works end to end** (commit `f733a43`, local; push pending — needs a rebase on 3 remote doc commits from vaibhav700c).
- `game/Assets/Shell/Runtime/OpusSessionRunner.cs` (auto-added by the controller): finds the hub (127.0.0.1 in editor, else UDP beacon 8788), streams `status` 2 Hz + every `trial_event` + `metrics_tick`, records session.json/events.ndjson/kin_*.json on ONE clock under `persistentDataPath/opus_sessions/`, uploads at the end (retries on reconnect), auto-starts once the head is tracked, both-hands pinch = new session. HUD = `OpusHud.cs` (uGUI world canvas; legacy TextMesh is invisible in stereo).
- Controller: HandMode Auto (headset detection via XR loader — `XRSettings.isDeviceActive` is false in Awake over Link, which silently ran Link on the demo driver), real-hands pinch-AT-fruit grab (grabRadiusM 0.10), carry, release over basket, auto-Recenter to designed eye (rig is floor-level at y 1.06 → eyes were ~2.2 m up). Fruit scale 2 absolute. Ghost hand removed. Fruit ballooning bug fixed (detach with worldPositionStays:false inherited 1/0.03 scale).
- **Verified real session** `app/.hub_data/c93ae4fa-…` (user's own hands): 6 trials, 5 success, validate PASS, analytics: RT 250–1304 ms, MT 1.2–2.7 s, peak 1.3–2.1 m/s, SPARC −2.0…−3.4, endpoint error 6.6–11.1 cm (the 79–133 cm frame bug is confirmed fixed), tracking loss 0 %. Open: trial 3 has null reach metrics (S agent investigating).
- **Haptic sleeve is real**: `firmware/opus_sleeve/opus_sleeve.ino` v0.3.0 on ESP32 at 10.179.145.125 (MAC 20:43:A8:63:5B:B4), implements the Electronics team's contract AND the game's format; verified from the PC: 4/4 cues `executed`, ack 25–91 ms. Game `hapticManualHost` = 10.179.145.125 (different subnet from PC 172.18.228.97, so discovery broadcast can't cross). Chip-temperature gating disabled (sensor uncalibrated, read ≥70 °C idle).
- Android APK: Gradle "Unable to establish loopback connection" happens OUTSIDE the sandbox too. Likely cause: Java AF_UNIX temp path contains a space (`GARV BANSAL`). Build recipe under test: `JAVA_TOOL_OPTIONS=-Djdk.net.unixdomain.tmpdir=C:\gtmp`, TMP/TEMP=C:\gtmp, Bash `dangerouslyDisableSandbox`.
- Rules added today: the user explicitly wants the OPEN Unity editor driven through the Unity MCP (overrides "batch mode only"); only ONE agent drives it. cmd.exe syntax for anything the user runs (`cd /d "..."`, `C:\flutter\bin\dart.bat`).
- 11:45 update: all 4 agents were killed by a usage limit at ~06:00 (reset 07:40) and relaunched at 11:45 from their logs: U run15 (`logs/sessions/2026-09-19-U-unity-run15.md`), A1 run13, A2 run13, S run3 (same scopes). **Phone verified 05:30**: debug APK (recipe above) installs + launches on CPH2381 `b83663ef` (pkg com.opus.opus_app), hub starts (code E02BF7) and is reachable from the PC at 172.18.226.79:8787 — phone wlan0 172.18.226.79/20 is on the SAME Wi-Fi as the PC; wlan1 10.179.145.149 is the phone hotspot where the ESP32 lives. Bugs found on device: Devices showed the hotspot IP as hub address; Android beacon assumed /24 (network is /20) — A1 fixed to broadcast /16–/24. Screens: `logs/sessions/screens/app/device/`.
- In flight (2026-09-19 04:30): U run14 (basket drop DONE in code, compiled; stages A–E/feedback/HUD NOT started — Unity MCP went down, user to re-activate), A1 run12 (Kinetic Clinical VR theme, 4-tab shell, live monitor, devices, phone build), A2 run12 (patients, session report, reports tab, program builder), S run2 (mock headset, check_session, watcher, sleeve_test, START_DEMO.md). Design: `docs/design/stitch/…/kinetic_clinical_vr/DESIGN.md` is canonical.

- **12:55 DESIGN CHANGE (binding): `docs/design/OPUS_DESIGN_V2.md`** — user rejected the Stitch/Kinetic look (too neon, too much text, too many buttons). v2 = true black + dark red (oxblood #7A1A20, crimson #A8242C emphasis only), titles only (no subtitles/helper text), sectioned containers with 1px borders, one primary action per screen, real charts (donut outcomes, RT/SPARC trends with MDC band, per-trial bars, weekly dose, cue pie). Atkinson Hyperlegible Next kept. The Stitch folder is now only a reference for WHICH information each screen shows. A1 (theme/shell/Monitor/Devices/phone) + A3 (Patients/Patient/Reports/Session report/Programs + chart kit) implementing it. A2 run13 done + verified + committed `e943c50` (174/174 tests). Unity restarted after a domain-reload hang (the MCP bridge hangs reloads — see §5 history); new U code compiles clean; the user must `/mcp` → reconnect meta-xr-unity-runtime (port 48736) before U run16.

## ★★★★ RUN 19 (2026-09-19 ~17:25 IST, Opus) — NEWEST, read first

User brief: continue from where everything stopped; make the pipeline demoable; **the APP on the phone must show
live data coming from the Unity game** (not hub logs); UI is "very clustered and very dull" → more colour,
readable for a doctor, fewer subtitles, dropdowns + charts, less per page, layman; Unity via MCP with tests;
electronics e2e on the new 4-motor schema; then offline setup + demo instructions.

Environment measured 17:15: phone `b83663ef` on USB, wlan0 **172.17.221.245/21**, PC **172.17.221.211** (same
subnet). Phone hotspot OFF → **ESP32 sleeve 10.179.145.125 unreachable** (needs power + a shared network).
Unity editor had been closed; ports 48735/48736 were held by an orphaned `fake_haptic.py` (PID 27364, a child of
the dead editor 19948 that inherited its sockets) — Opus killed it; ports free; Unity reopening (PID 40924).

**Live path decision:** `adb -s b83663ef forward tcp:8787 tcp:8787` → on the PC `127.0.0.1:8787` IS the phone's
hub over USB. The editor runner already prefers 127.0.0.1, so **Unity (Link or demo driver) → USB → phone app**
with no Wi-Fi/firewall/discovery dependency. Nothing on the PC may bind 8787 while this forward is active.

Design: **v3 AMENDMENT** added at the top of `docs/design/OPUS_DESIGN_V2.md` (semantic colour palette + fixed
per-metric colours, dropdowns instead of stacked charts, ≤ 4 sections/screen, layman labels, zero subtitles).

Agents dispatched 17:25 (disjoint ownership): **A-UI run2** (`logs/sessions/2026-09-19-AUI-run2.md`, all of
`app/` + the phone), **S run5** (`…-S-pipeline-run5.md`, sim/tools/analytics/runbook: 4-motor emulator + fixed
sleeve_test + `prove_live_to_phone.py`), **U run17** (`…-U-unity-run17.md`, game/ + editor: EditMode incl. the
unrun KinematicsRecorder regression, HUD-in-demo fix, PlayMode, live session into the phone hub).
Firmware v0.4.0 (4 channels, MOTOR_COUNT 1) is written but **uncompiled** — no ESP32 toolchain on this PC.

## ★★★ RUN 18 (2026-09-19 ~15:30 IST, Opus) — READ THIS FIRST, it is the newest state

**The prototype demo is DONE and passed** (user confirmed 15:20). The freeze is lifted; this run is
enhancement + testing, not demo-critical work.

### What the user asked for this run
1. Verify the game -> app connection: is game data actually shown in the app?
2. Simplify the app, add visual easiness, enhance the UI.
3. Add **pre-added levels** of the game to the app. User chose **difficulty tiers Easy / Medium / Hard**
   (not the 5-stage system) when asked.
4. Test end to end.
5. Start an **Electronics agent** using the new 4-motor schema and produce firmware + Arduino IDE steps.

### ANSWER to "is game data shown in the app?" (measured, with real files)
| Leg | State | Evidence |
|---|---|---|
| Real Unity game -> **PC-hosted** hub | ✅ PROVEN | `app/.hub_data/f5235445-e948-4ee7-b60b-4d8d7ea71c64/` — `device.model` `quest-link-editor`, 72 Hz, 9 kin chunks, 6 trials, `validate.py --session` PASS |
| Simulator -> **phone** app | ✅ PROVEN | on the phone: `run-as com.opus.opus_app files/sessions/session-000`, 16 kin chunks |
| **Real game -> phone app** | ❌ **NEVER DONE** | the phone's `session-000` says `"mode":"simulation"`, `"device":{"model":"synthetic","device_id":"sim-0"}` — that is `fake_headset.py`, not the game |

**The two legs have never been joined.** This is the single most important open item. The test that settles it
must assert the phone received a session whose `device.model` is **not** `synthetic`. Assigned to track S.

The app itself renders received data correctly — Patient screen shows tiles, outcome donut and legend from a
real received session (screenshot `logs/sessions/screens/app/device/2026-09-19-conn/app-home.png`).

### Levels: implemented by Opus this run (do not redo)
`presets` was **already** in `contracts/schemas/game-manifest.schema.json` and already parsed by the Dart model
(`GameManifest.presets` -> `List<GamePreset>`), and `DynamicFormController.applyPreset()` already existed. Only
the UI never surfaced it. So no contract change was needed.
- Replaced the old presets (`gentle`, `neglect_left`) with **`easy` / `medium` / `hard`** on `stage: B_grasp_place`,
  varying `trialCount`, `reachPercent`, `azimuthRangeDeg`, `elevationRangeDeg`, `targetSizeScale`, `holdMs`,
  `timeLimitMs`, `ghostGuide`. Manifest version **0.4.0**.
- Synced to all three copies: `game/Assets/Games/OrchardReach/manifest.json`,
  `contracts/fixtures/orchard_reach.manifest.json`, **`app/assets/fixtures/manifests/orchard_reach.manifest.json`**
  (that last one is what the app actually loads — easy to miss).
- `contracts/validate.py` green after the change.
- **REMAINING (Opus, in progress when this was written):** the Programs screen UI does not yet render the preset
  tiles. Insert a "Level" Section above the `DynamicForm` in
  `app/lib/features/programs/program_builder_screen.dart` (build method, around line 100), three tiles reading
  `_manifest!.presets`, tapping one calls `controller.applyPreset(preset.params)` + `setState`. Then rebuild the
  APK and verify on the phone.
- Architectural note: presets deliberately live in the **manifest**, not in app code, so GOAL.md **G1** ("adding a
  game changes 0 lines outside the game folder") still holds. Never hardcode game-specific presets in the app.

### Haptic schema: 0-3 landed by USER DECISION (overrides the earlier deferral)
Opus had deferred the 4-motor extension during the demo freeze. The user lifted that. Done:
`contracts/schemas/haptic-device-command.schema.json` `motor` enum is now **`[0,1,2,3]`**, `validate.py` green.
Software may still only *send* 0-1 until motor 2/3 physical placement is decided (`docs/ELECTRONICS_HANDOFF.md` §3.4).

### Haptic sleeve — verified on REAL hardware today
See `logs/sessions/2026-09-19-OPUS-sleeve-check.md`. Headlines:
- The PC **can** reach the ESP32 at 10.179.145.125 now (ping 0% loss). Older notes saying it cannot are **stale**.
- `buzz`, `pulse`, `ramp` all execute. 3/3 with commands spaced >= 1 s.
- **`tools/demo/sleeve_test.py` reports a FALSE 2/4 failure** — it fires faster than the firmware's
  `MIN_CUE_GAP_MS = 100`, so alternate commands are correctly rejected with `CUE_GAP`. Firmware right, tool wrong.
- The previously recorded "4/4 cues executed" in this file **passed on luck** and is timing-dependent.
- Only motor 0 is fitted (`MOTOR_COUNT 1`, `ROUTE_MOTOR1_TO_MOTOR0 1`) — forearm cues play on the upper arm.

### ⚠️ Unity MCP is DOWN — root cause found, needs a human
`mcp__meta-xr-unity-runtime__*` times out. Diagnosed 15:30:
- Ports **48735 / 48736** are held by **orphaned LISTEN sockets owned by dead PID 19948** (the previous Unity
  editor). `Get-NetTCPConnection` shows the owning process as gone.
- The **current** editor is PID **31732** (`game - OrchardReach - Android - Unity 6.4`), alive and responding,
  but it cannot bind the bridge ports while the dead process's sockets hold them.
- Fix is to clear the orphan, then restart the editor so it rebinds. Note `unity` PID 28768 is the Claude CLI
  MCP helper (`unity.exe mcp`), **not** an editor — but it is the likely inheritor of the orphaned handle.
See `docs/MANUAL_TODO.md` for the exact commands.

### Agents running when this was written (5 parallel, disjoint file ownership)
| Track | Log | Owns |
|---|---|---|
| E electronics | `logs/sessions/2026-09-19-E-electronics-run1.md` | `firmware/**`, `docs/ELECTRONICS_HANDOFF.md` |
| A-UI | `logs/sessions/2026-09-19-AUI-run1.md` | app features patients/sessions/live/devices/settings, `core/theme`, `shared/widgets/charts`, `app/lib/app` — **and the phone (sole adb/APK owner)** |
| S pipeline | `logs/sessions/2026-09-19-S-pipeline-run5.md` | `sim/**`, `tools/**`, `analytics/**`, `docs/TESTING_RUNBOOK.md` |
| U unity | `logs/sessions/2026-09-19-U-unity-run17.md` | `game/**` **except** `manifest.json` (Opus-owned this round) |
| Opus | — | `app/lib/features/programs/**`, `app/lib/shared/widgets/dynamic_form/**`, `app/assets/fixtures/**`, `contracts/**`, all manifests |

### Known bug still open: the Unity HUD is never created in demo mode
Root cause confirmed. `OpusHud.Create()` builds the panel in code (`OpusHud.cs:28`) — it is **not** an authored
scene object, so nothing shows in the Hierarchy at edit time. It is called from `OpusSessionRunner.cs:129`, but
`OpusSessionRunner.cs:112-116` returns **before** that line whenever
`!controller.UsingRealHands && !runWithDemoDriver`, and `runWithDemoDriver` defaults to **false**. So any
demo-driver run has **no HUD at all**. Do **not** just flip the guard — it stops batch tests double-driving
sessions. Create the HUD unconditionally and keep only the session/hub half gated. Assigned to track U.

## ★★ RUN 17 (2026-09-19 ~14:10 IST, Opus) — phone-first debugging run, 4 agents in parallel

User's brief this run: debug the app **on the real phone over USB** (not simulation), make the type smaller and
the screens readable, strip the excess metrics, and prove game → UDP → hub → phone works in real time. Unity is
to be driven through the **Unity MCP**, never ADB; the phone is debugged over **ADB/USB**, never an emulator.

### Environment measured at the start of this run (supersedes the 05:30 numbers above — the Wi-Fi changed)
| Thing | Value |
|---|---|
| Phone | CPH2381, serial `b83663ef`, USB, `com.opus.opus_app` installed and running |
| Phone wlan0 | **172.17.221.245/21** |
| PC | **172.17.221.211** (same subnet — they can reach each other) |
| Phone display | 1080x2412 @ density 480 → **360 dp logical width**, system `font_scale` 1.0 |
| Unity | GUI editor open (PID 19948); **MCP bridge UP**, `GetCompilationStatus` → `clean`, 0 errors |

### Root cause of "the app is not readable" (found by Opus, fixed centrally before dispatching agents)
The type scale in `docs/design/OPUS_DESIGN_V2.md` §2 was authored against the **390 dp** golden width, but the
pilot phone renders at **360 dp**. On top of an already-large scale this produced two-line list rows, wrapped
section titles and a cramped bottom nav. Baseline evidence:
`logs/sessions/screens/app/device/2026-09-19-baseline/01-current.png`.

Opus changed the single central scale in `app/lib/core/theme/opus_tokens.dart` (`opusFontSize`) and updated the
design doc to match — **done before the agents started, so no two agents share that file**:

| Step | Was | Now |
|---|---|---|
| axis / table meta | 12 | **11** |
| body / row | 15 | **13** |
| section title | 17 | **14** |
| screen title | 28 | **22** |
| big value | 34 | **26** |

Goldens must gain a **360x800** phone breakpoint; 390x844 alone hid this.

Second finding, same screenshot: the Patients list renders the **full clinical description** per row
("Post-stroke (ischemic, MCA), mild residual right-sided weakness · right") wrapped over two lines, where
OPUS_DESIGN_V2 §5 specifies the **short form** ("Stroke · right"). The binding design and the user's complaint
agree; the code is what is wrong. Assigned to A3.

### Agents in flight (dispatched 14:10, strict file ownership so none share a file)
| Track | Log | Owns | Scope |
|---|---|---|---|
| A1 run14 | `logs/sessions/2026-09-19-A1-flutter-run14.md` | `app/lib/core/**`, `app/lib/app/**`, features `live`/`devices`/`settings` + their tests — **and the phone (sole adb/APK owner)** | baseline device screenshots → 360 dp goldens → declutter Monitor/Devices/Settings → rebuild+reinstall → prove the live path on device |
| A3 run1 | `logs/sessions/2026-09-19-A3-flutter-run1.md` | features `patients`/`sessions`/`programs`, `app/lib/shared/**` + their tests | cut every screen to exactly OPUS_DESIGN_V2 §5, short-form conditions, 360 dp goldens, cover the events-derived report branch A2 left untested |
| U run16 | `logs/sessions/2026-09-19-U-unity-run16.md` | `game/**` + the Unity editor (sole owner) | **first: resolve the unconfirmed Play-mode state run 15 left** → verify basket drop → stages/feedback/HUD → full tests → e2e session re-confirming the five "shape not meaning" defects |
| S run4 | `logs/sessions/2026-09-19-S-pipeline-run4.md` | `sim/**`, `tools/**`, `analytics/**` | one-command game→UDP→hub→phone harness with `--hub <ip:port>` and per-leg latency → fix the sleeve dispatch mismatch → answer the trial-3 null-metrics question → fix the longitudinal `patient_ref` bug |

Port discipline this run: A1 holds **8787/8788** against the phone; S uses ephemeral ports only.

### Rules reasserted for this run
- The phone is debugged over **USB/ADB only** (no emulator, no simulation); **Unity only through the Unity MCP** (no ADB, no batch mode while the GUI editor is open).
- The MCP bridge dropping during a domain reload is expected, not an outage — wait ~15 s and retry.
- Agents never commit. Opus reviews, commits and pushes.
- Test before altering; paste real output; update this file and the session logs after every iteration.

## ★ START HERE — current state (updated 2026-09-19 ~04:00 IST, Opus)
> Everything below this box is history, newest at the bottom. This box is the truth. §2/§3/§5 further down
> are **stale** (2026-09-14) and kept only as history — do not plan from them.

**Deadline:** prototype demo ~**2026-09-19 23:00 IST**. Priority: a working, demonstrable end-to-end prototype, all testable in simulation.

### What works (every line below was re-verified by Opus in this session, with real command output)
| Component | Status | Evidence / version |
|---|---|---|
| Contracts | 10 schemas incl. live-message, haptic-message, haptic-device-command | `python contracts/validate.py` → all validations passed |
| Analytics | quality gating (rate_hz < 45, tracking loss ≥ 15 %), trunk lean + neglect with MDC (synthetic), 30 Hz profile, wrist rotation | pytest **49 passed**; tag `analytics-v0.2.0` |
| Live link sim | fake hub/headset, ping-pong RTT, drop/resume, UDP discovery | pytest **26 passed** (`sim/live`) |
| Haptic sleeve sim | UDP 8790/8791, device + cue formats, acks with cue_id, safety checker | pytest **14 passed** (`sim/haptic`) |
| Flutter app | hub (with per-message latency logging), live monitor, devices incl. haptic sleeve, program builder, session report with haptic analysis, patient profile with real recovery charts and correct clinical trend directions, router per brief §3, dose adherence | analyze **0 err / 0 warn**; `flutter test` **157 passed**; commits `ff5dd83`, `a29a7f8` |
| Unity game | seated layout, Meta models + Building Blocks rig, ghost hand, live link vs fake hub, haptics end to end, **trunk-lean vignette + audio (visually confirmed)**, apple lands inside the basket | EditMode **107/107**, PlayMode **6/6**, release 0.0000 m, `inside=True`; cue send→ack avg **3.31–36 ms**, max **72 ms** across runs (G9 < 100 ms); commits `d88e1b1`, `a29a7f8` |

Vignette proof: `logs/sessions/screens/unity/run11/during_trunk_lean.png` — amber border, reach field clear.

All four gaps from the run-10 review are closed: the lean vignette/audio now exist, the debug marker is
hidden behind the real hand mesh, the low-confidence double pulse honours its 120 ms gap, and the basket
placement lands inside the bounds. See `logs/sessions/2026-09-18-U-unity-run11.md` for the root causes —
including two bugs that had been passing on luck (a haptic discovery race that dropped **every** cue, and a
basket "near-miss" that was really the test sampling the next trial's target).

### ✅ The three-way integration demo is DONE (2026-09-19)
The Flutter hub, a Unity headset session and the haptic sleeve simulator ran **simultaneously**, and for the
first time a session recorded by the game reached analytics and produced real biomarkers. Full write-up and
numbers: `logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md`. How to run it yourself: `docs/TESTING_RUNBOOK.md` §6.

```
FullPipeline_AppHub_Headset_Sleeve_ProducesAnalysableSession   PASSED
6 trials · 34 trial events · 4 haptic cues · 2 kinematics chunks · 4 files uploaded to the hub
live-link RTT p50 165 ms (G2 < 250 ms) · contracts/validate.py --session exit 0
analytics: MT 930–1138 ms, peak 1.33–2.25 m/s, SPARC −1.50…−1.54, LDLJ −6.95…−7.37
```

It found **five defects that every previous test missed, because the contracts check shape, not meaning** —
every one produced files that passed schema validation (all fixed, commit `a2c441e`):
1. the game never recorded kinematics at all (so no session was ever analysable);
2. `trial_end` was never emitted on the *successful* path, so every trial was unclosed and every metric null;
3. `contact` was never emitted, which analytics gates all movement metrics on;
4. trial events and kinematics ran on two different clocks;
5. targets were recorded world-relative while kinematics are chest-relative, making `endpoint_error_cm` read
   79–133 cm for reaches that visibly hit the apple.

### Next (in order)
1. **Rebuild and republish the web bundle.** `releases/app/0.5.1` was built 74 minutes *before* `ff5dd83`, so
   the published app still shows the inverted trunk-lean label, empty SPARC/RT charts and the coarse reach-area
   axis. Verified by hand in a browser: `logs/sessions/2026-09-19-WEB-APP-MANUAL-TEST.md`. **Do not demo 0.5.1.** Flutter hub
   ↔ a live Unity demo session ↔ `sim/haptic/fake_haptic.py`, running together, with measured latency and
   screenshots. Each leg is proven alone: the app leg replayed 54 events from `fake_headset` and the stored
   session passed `contracts/validate.py --session`; Unity passes against `fake_hub`. They have never been
   joined. This needs the Unity editor, so it cannot run in parallel with another Unity agent.
2. Fix the analytics generator so longitudinal fixtures share one `patient_ref` (track N; the app works
   around it today).
3. Unity shell flow (U3): pairing, calibration, block runner, rest, results — still not built.
4. Swap the sleeve simulator for the real ESP32 once the Electronics team's firmware answers on UDP 8790.

### Rules that bite if forgotten
- **Unity: batch mode only**, GUI editor closed, **Meta AI Agent Bridge disabled**, Unity Hub running.
  A process `unity.exe mcp` with no window is the Claude CLI MCP helper, **not** an editor — don't kill it.
- Unity tests: `-runTests` **without** `-quit`, or the runner is killed before it runs.
- Anything that runs during `Awake` may run in EditMode tests via reflection: use `DestroyImmediate`
  there, never `Destroy` (cost 1 red test in run 11).
- Docs are UTF-8 (★ — ≤ §). Edit them with the Edit tool or Python `encoding='utf-8'`, never PowerShell
  `Set-Content` without `-Encoding UTF8`. This has now corrupted files twice (`6f1cc42`, and a stray
  cp1252 byte in `logs/CHANGELOG.md` repaired in `d88e1b1`).
- Agents never commit; Opus verifies and commits. Never claim an APK works.
- Don't leave scratch `*_test.dart` files in `app/test/` — one hung the whole suite for 10 minutes.
- MDC bands are synthetic estimates; label them so in the UI.
- **A green PlayMode run can be luck.** Two tests here passed once and failed the next run unchanged
  (UDP discovery timing, and a grasp offset). If a test depends on a network peer or on a frame-timed value,
  make it wait for the condition explicitly. Re-run before believing a fix.
- **Don't write a "not done" list into a brief without checking the tree.** `A-next-run.md` carried two false
  "not started" claims across three runs, and I repeated them before an agent caught it.

### Human-only blockers (see `docs/MANUAL_TODO.md`)
- Electronics team: answers to §4 of `docs/ELECTRONICS_HANDOFF.md` (motor placement, gaps, ports).
- APKs: Gradle "Unable to establish loopback connection" (machine security/firewall). Windows app: VS 2022
  C++ workload needs a UAC click.


## 1. What this is
- Rebuild of the VR rehab pipeline (old: Aastheen/Verse). See `GOAL.md` and `docs/ARCHITECTURE.md`.
- Root: `C:\Users\GARV BANSAL\Documents\VR_games\OPUS` (own git repo → GitHub `black1plague2/opus-rehab`, private)
- Old references (read-only, never modify from here):
  - Unity: `Documents\VR_games\balloon` (repo `black1plague2/aastheen-balloon`, branch `pose-baseline`), `Documents\VR_games\anubhav`
  - Flutter + FastAPI: GitHub `Nainikap/aastheen` (`aastheen_frontend/`, `aashteen_backend/`)

## 2. Current state (updated 2026-09-14, session 001)
| Track | State | Last good version |
|---|---|---|
| Docs / contracts | ☑ Phase 0 docs + 6 schemas (metrics added) pushed to GitHub `black1plague2/opus-rehab` | commit bf847bc |
| Contracts tooling (C1–C2) | ☑ fixtures + `contracts/validate.py` + CI; Opus-verified exit 0. TODO: metrics.schema fixtures | commit bf847bc |
| Research harvest (D2) | ☑ RESEARCH §4 (stats unverified; Reddit blocked for agents) | commit bf847bc |
| Analytics + synthetic patients (N1–N3) | ☑ generator + opus_analytics + VALIDATION.md; Opus re-ran pytest: 22 passed. Accuracy target (G4) met for RT/MT everywhere; peak speed misses for severe/noisy profiles; n_submovements and SPARC unreliable under noisy tracking (documented). ☑ metrics.json conforms to metrics.schema (13/13, 28 tests) | tag `analytics-v0.1.0` |
| Flutter app (A1–A6) | ◐ Flutter 3.47.4 / Dart 3.13.3 installed at `C:\flutter`; Sonnet agent resumed with real synthetic fixtures | n/a |
| Unity game + SDK (U1–U6) | ◐ Project moved to `game/` (6000.4.6f1). Sonnet agent dispatched with brief U | n/a |
| Backend / bridge | Deferred to Phase 3 (contracts only) | n/a |

### Run 2 (started 2026-09-14 23:25 IST, after the usage-limit reset)
- Run 1 agents (Unity, Flutter) were killed by the usage limit at ~22:45; their partial work is committed as WIP `1b9a8b0` (UNTESTED).
- Opus added **Live Protocol v1** (`contracts/LIVE_PROTOCOL.md`, `live-message.schema.json`, schema-checked): Flutter app = hub (WS+HTTP :8787, UDP beacon :8788, mDNS), headset = client, with resume/ack/RTT.
- Running in parallel: **U run2** (Sonnet: logs → practices → tests → realistic scene → Play-mode visual audit → live link → APK → MODEL_REQUESTS.md), **A run2** (Sonnet: verify WIP → A3–A6 → hub → protocol tests → builds), **S** (Haiku: fake_hub / fake_headset / e2e latency harness).
- Agents write CHECKPOINTs to their session logs (`logs/sessions/2026-09-14-*-run2.md`, `-S-live-tools.md`). If an agent dies, resume a new agent from its last checkpoint.
- Human-only gaps: `docs/MANUAL_TODO.md`.

### Run 2 results (2026-09-15 ~00:30 IST)
- **S (live tools):** ☑ verified (pytest 27 passed, commit afc7046). Haiku's first version was a no-op and was reworked by Sonnet.
- **U run2:** ☑ compile clean, 92 EditMode test entries pass, replay session validates, headset LiveClient + tests, MODEL_REQUESTS.md (commit cb9495a). ☐ Building Blocks rig, scenes, visual audit, live integration, APK → **U run3 running**.
- Incidents: the editor opened the repo root by mistake (stray project deleted), then a 13-min domain-reload hang (force-restart; pre-release com.unity.ai.* packages removed).
- **Correction:** SDK 205 `HandTrackingFrequency.HIGH` is Fast Motion Mode → keep LOW (UNITY_PRACTICES updated).
- **A run2:** still running (hub + design direction docs/APP_DESIGN.md).

### Usage-limit stop #2 (2026-09-15 00:52 IST; limit resets 04:20 IST)
- Local WIP commit `f27505d` holds everything, including the user's 6 Meta models in `game/Assets/MetaAssets`. **Push failed with 403**: the gh token was revoked (planned); the user must run `gh auth login`, then `git push`.
- **U run3** died early (was on R1 platform switch / R3 notes). No run3 log file was written. Resume from `logs/sessions/2026-09-14-U-unity-run2.md` + the U run3 prompt milestones R1–R7 + the "Imported asset review" in `docs/MODEL_REQUESTS.md`.
- **A run2** died after CHECKPOINT 4 (M5 builds). Hub server + protocol engine verified end to end; hub UI/providers not yet wired; APP_DESIGN partially applied. Resume from `logs/sessions/2026-09-14-A-flutter-run2.md`.
- On resume: 1) verify each track's last checkpoint claims (analyze/test, compile/tests), 2) dispatch U run4 (Sonnet) + A run3 (Sonnet) in parallel, 3) then the integration run: Flutter hub ↔ Unity Play mode ↔ fake tools, with latency numbers.

### Usage-limit stop #3 (2026-09-15 ~12:30 IST): honest state, snapshot `7241d00` (pushed)
- **Verified working:** contracts + validator; analytics v0.1 (28 tests); live sim tools (27 tests, e2e with drop/resume); Unity SDK + Orchard logic (62 EditMode tests), headset LiveClient (8 tests), Meta model import tool, Building Blocks rig in `Assets/Scenes/OrchardReach.unity`; Flutter hub server + protocol (hub↔fake headset 46/46 events), live monitor on real stream, web release build.
- **Not done:** Unity RigValidatorTest run, fruit HandGrabInteractable, shell/UI scenes, **Play-mode visual audit (no Unity screenshots exist)**, Unity↔hub live integration, APKs (Unity + Flutter), Windows app build (VS C++ needs a UAC click), full APP_DESIGN layouts.
- **Visual QA findings (Opus review of run4 goldens):** patient_profile goldens capture only a loading spinner (async data not pumped/settled). Live monitor shows raw metric ids (`reaction_time_ms`), which violates APP_DESIGN copy rules; button icons render as boxes in goldens (icon font not loaded in tests). Reach-trace strokes show spiky joins (needs a round stroke join/cap + path smoothing).
- **Environment:** at stop, Unity PID 24532 shows "Opening project..." (restarted during run4, likely by the Project Setup Tool fix); bridge down. Flutter APK is blocked by a machine-level `Selector.open()` AF_UNIX failure (not the JDK; see MANUAL_TODO); Windows build needs the VS C++ workload.
- **Resume order:** U: `docs/agent-briefs/U-next-run.md` from R2's validator test → R5 → R6. A: fix the golden issues above → finish run4 items (program→headset, hub sessions in lists) from `logs/sessions/2026-09-15-A-flutter-run4.md`.

## 3. Next actions (in order)
1. Wait for the 4 agents → Opus reviews each session log + diff → commit per track → update this file.
2. When the user says Unity is ready: `unity status`, then dispatch the Sonnet agent with brief U (U1 first). Only that agent touches Unity.
3. C3 codegen (Haiku) after the app and SDK folders exist.
4. Phase 2 once N1 fixtures exist: U5 replay of synthetic sessions in the XR Simulator.

## 4. Key decisions (do not re-litigate without the user)
| Date | Decision |
|---|---|
| 2026-09-14 | Folder `Documents\VR_games\OPUS`, new independent repo |
| 2026-09-14 | App: **Flutter, rebuilt clean** (Riverpod, go_router, Drift, freezed, generated API client) |
| 2026-09-14 | First game: **hand tracking, upper-limb reach & grasp**, kinematics-first |
| 2026-09-14 | Backend (Phase 3): **FastAPI + managed Postgres** (+ object storage for raw telemetry, Python analytics worker) |
| 2026-09-14 | Build order: standalone game + app + analytics first, bridge later |
| 2026-09-14 | All testing in simulation (Meta XR Simulator + synthetic patients) until hardware is available |
| 2026-09-14 | Models: Opus plans, assigns and reviews only. Sonnet does implementation. Haiku does mechanical work |
| 2026-09-14 | Unity: same editor as the reference project, **6000.4.6f1** |
| 2026-09-14 | Game is **Meta Quest–only, pure VR**, built with **Meta XR SDK + Interaction SDK + Building Blocks**; hand tracking at High frequency, latest version, raw skeleton joints recorded with confidence. No Unity XR Hands/XRI, no passthrough/MR — **"High frequency" was superseded 2026-09-15: SDK 205's HIGH *is* Fast Motion Mode, so the rule is LOW + FMM off (see `docs/UNITY_PRACTICES.md`)** |
| 2026-09-14 | Unity practices researched → `docs/UNITY_PRACTICES.md` (binding): ISDK OVR + Core + Simulator all at 205.0.0 on Unity OpenXR (mirrors Meta's official ISDK sample); HT frequency **LOW** (this row originally said High; superseded 2026-09-15, see above), measured at runtime; Fast Motion Mode off; record the raw ISDK hand; Building Blocks via editor + rig-validator test; synthetic hands via an ISDK DataModifier |

## 5. Blockers / open questions
- Unity CLI lives at `%LOCALAPPDATA%\Unity\bin\unity.exe` (not on PATH). On 2026-09-14 the Unity MCP server was registered as `unity-editor-mcp` (user scope, pinned to `OPUS/game`) by editing `~/.claude.json` directly (backup: `~/.claude.json.bak-2026-09-14`), because `unity mcp configure claude-code` shells out to a `claude` CLI that is broken here (npm claude.exe "not a valid application") and it didn't quote the space in the path. It loads in **new** Claude Code sessions. Project moved to `game/` on 2026-09-14.
- Flutter SDK: user approved; cloned (stable) into `C:\flutter` on 2026-09-14. Then resume brief A.
- SECURITY: the token was removed from the `anubhav` remote URL on 2026-09-14. It is the **gh CLI's own login token**, so the user must revoke it on GitHub and re-run `gh auth login`. After that, pushes use the new token.

## 6. How to resume (any account)
1. Read this file → `docs/PLAN.md` → latest `logs/sessions/*.md`.
2. `git log --oneline -15` and `git tag` to see versions; `releases/VERSIONS.md` lists tested builds.
3. Pick the first unchecked item in `docs/PLAN.md` §3 for your track; follow its brief in `docs/agent-briefs/`.

### Run 5 (2026-09-17)
- Found on resume: U run4 CHECKPOINT 4 = RigValidatorTests 7/7 passing (R2 closed); A run4 reached CHECKPOINT 2 (program->headset, hub sessions in app, devices screen, goldens; layouts partial).
- Unity was closed; Opus reopened OPUS/game (PID 28100).
- Dispatched **U run5** (Sonnet: fruit grabbable + dressed scene -> Play-mode visual audit screenshots -> live link vs fake_hub) and **A run5** (Sonnet: fix visual-QA findings -> finish layouts -> reviewed goldens -> analyze/tests/web build -> live browser screenshots). Logs: logs/sessions/2026-09-17-*-run5.md.


### Run 6 (2026-09-17)
- A run5 verified by Opus: analyze 0 err/0 warn, goldens 32 passed; metric names, icons and smooth traces fixed. Remaining app gaps: patient overview missing recovery line + glyph strip; speed-profile x-axis labels overlap.
- Unity hang root cause CONFIRMED (Meta AI Agent Bridge on reload); bridge disabled; Unity now driven headless in batch mode (see U-next-run.md). U run6 dispatched: tests -> dress scene -> headless screenshots + PlayMode capture -> live link test.


### Usage-limit stop #4 (2026-09-17, resets 21:20 IST)
- **Latest verified state:** Unity run7 (commit 647c0b6): 77/77 EditMode incl. SeatedLayoutTests (eye 1.15 m, 30 deg down; table top 0.60 m, near edge 0.35 m; 200/200 targets in reach), PlayMode audit + live link green; static seated view correct. App run5 verified (analyze 0/0, goldens pass).
- **U run8 died while still reading code** (no meaningful changes). Resume = the same brief: (1) the PlayMode capture camera must be the validated eye pose (assert within 2 cm); (2) the synthetic hand must actually reach the apple (index tip within 5 cm at grasp), carry it, release over the basket, apple at rest inside the basket, all with PlayMode assertions; (3) replace the sphere hand proxy with an ISDK/OVR hand visual or an articulated low-poly hand; re-capture 01-05 to logs/sessions/screens/unity/run9 and describe each image. BATCH MODE ONLY (see U-next-run.md root-cause section).
- App next: patient overview recovery line + glyph strip; speed-profile x-axis label overlap.
- Machine-level blocker for BOTH APKs: Gradle 'Unable to establish loopback connection' (Java Selector/AF_UNIX); Windows app needs the VS C++ workload (UAC). See MANUAL_TODO.


### Haptics added (2026-09-18) � prototype deadline ~2026-09-19 23:00 IST
- New requirement: optional vibrotactile feedback for form guidance. **Software only** (Unity + Flutter + a software fake). The tennis-sleeve hardware (ESP32 + motors + IMU) is a separate Electronics team; we only define and implement the interface.
- Contract: contracts/HAPTIC_PROTOCOL.md + schemas/haptic-message.schema.json (UDP 8790 JSON; cues trunk_lean / low_confidence / success; status, ack, config, stop, optional imu placeholder).
- event.schema.json gained haptic_cue, so every cue lands in events.ndjson and feeds analytics + the app's session report.
- Haptics are ADDITIVE: the visual vignette + audio stay primary and must work with no sleeve. Default off until hardware exists.
- Trunk-lean detection does NOT exist in the game yet (only the manifest param and the analytics metric), so the Unity track builds the detector, the vignette/audio, and the HapticClient together.
- Tracks: U = detector + HapticClient + triggers + SyntheticHandDriver demo triggers; A = Devices "Haptic sleeve" card + live-monitor indicator + cue/lean analysis in the session report; S = sim/haptic/fake_haptic.py.

### Resume after usage limit (2026-09-18 21:45 IST)
- Verified + committed so far on haptics: contract v1.1 + electronics brief (solder-free power-bank build), sleeve simulator (14 tests, ~1 ms ack), app haptic UI + session-report cue analysis (goldens pass).
- **Unity run9** died without a log: HapticClient/FormFeedback/demo triggers written, PlayMode 2/5 pass, demo session produced 0 haptic_cue/form_warning events -> **U run10** dispatched to root-cause and fix (uses the real fake_haptic.py).
- **App**: patient profile renders blue bars (rejected) -> **A run8** dispatched.
- **Manual (now):** the user opened the Unity GUI at 21:40; it blocks batch runs. U run10 does file-level work until it's closed.

