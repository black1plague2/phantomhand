# Phantom Hand — prompt status (PH_STATUS)

Written 2026-10-08 by ph-scribe (Haiku) from the logs listed in each row. Numbers are copied from those logs; the
log path is next to every number. Where two logs disagree, both are listed and marked **FLAG FOR OPUS**.

- **Status** vocabulary: `done` (O2 review by Opus = PASS), `partial`, `not started`, `awaiting review` (H1 rule),
  `reference only`, `handed to electronics team`.
- **Commit** = short hash from the local reflog of branch `claude/project-thread-qrz2a9` (`.git/logs/HEAD`); the
  top entry is `5c0b12d` (second pass, 2026-10-08; the first pass said `d59d75c`).
- **Last log** = newest log for that prompt under `logs/sessions/`.
- **Owner model** = from `docs/agent-briefs/ph/00-README.md` (the logs do not record which model ran each builder).
- **MVP / stretch** = as tagged in `00-README.md` ("Stretch" section and the prompt table). Where `00-README.md`
  does not tag a prompt, the cell says so.
- Date column in `logs/CHANGELOG.md` = the date in the run's log file name.

## Prompt table

| Prompt | Name | MVP / stretch | Owner model | Status | Commit | Last log | Test counts (from the log) |
|---|---|---|---|---|---|---|---|
| O0 | Setup and baseline (PRD v2 pack installed) | setup (not tagged) | Opus | done (commit only; no O0 log exists) | 4e7ecee | none | none |
| O1 | Contracts v0.2 | MVP (setup for all tracks) | Opus | done (O2 PASS) | d7dbb83 | logs/sessions/2026-10-07-PH-O-O1-run1.md | `contracts/validate.py`: "All validations passed" (105 PASS lines); `sim/haptic` + `sim/sleeve` 64 passed; analytics 51 passed (baseline). Same log. |
| U1 | SDK: stroke cues, two-node transport, sensor client | MVP | Sonnet | done (O2 PASS) | c900521 | logs/sessions/2026-10-07-PH-U-U1-run1.md | EditMode `DONE passed=290 failed=0 total=290`; +60 new (HapticStrokeTests 21, DiscoveryTests 12, SleeveSensorClientTests 18, SensorRecorderTests 9). **FLAG FOR OPUS**: the same log's arithmetic "208 prior + 60 new" = 268, not 290 (see Flags). |
| U2 | Game logic + PhantomHand scene (Part A logic, Part B scene) | MVP | Sonnet | done (O2 PASS) | 8f7a734 | logs/sessions/2026-10-07-PH-U-U2-run1.md | PhantomHand group `DONE passed=85 failed=0`; all EditMode 208 at checkpoint 1, 290/290 at checkpoint 3 (same log). |
| U3 | Virtual arm, brush, strokes, threat | MVP | Sonnet | done (O2 PASS with follow-ups) | a265205 | logs/sessions/2026-10-08-PH-U-U3-run1.md | EditMode 290/290; PlayMode PhantomHand group 20/20; PlayMode all 25 passed, 3 failed, 28 total (the 3 failures are Orchard/Shell tests, not U3 code; same log). |
| U4 | Calibration, probe, questionnaire, witness, HUD | MVP | Sonnet | **PROVISIONAL** (O2 2026-10-08): compiles clean, **tests NOT run** (Unity MCP down); PASS waits on the test run and the CaptureU4Shots screenshots | 362a66c | logs/sessions/2026-10-08-PH-U-U4-run2.md | Editor.log: 6 `error CS` (CS0407) lines at log lines 765–1581, fixed in CHECKPOINT 2; none after line 4338 (same log, Compile evidence). Tests written, not run: `UiModelTests.cs`, `UiPanelTests.cs` (count not in log). |
| U5 | Composition root, session runner, live link, L3 | MVP | Sonnet | **PROVISIONAL** (O2 2026-10-08): compiles, **UNRUN** (Unity MCP down). Item 4 (PH_FullRun + Orchard totals) not run. | 193a15c | logs/sessions/2026-10-08-PH-U-U5-run1.md | Editor.log: `error CS` = 0 after line 10202 (same log, CHECKPOINT 1). 25 EditMode tests in `PhantomHandShellTests` written, compiled, NOT run; `PH_FullRun_DemoMode_AgainstFakeHubAndTwin` written, NOT run. |
| MODELS | User's Meta models: importer + presenters with procedural fallback (not a numbered prompt in `00-README.md`) | MVP (manager brief 2026-10-08; tag not in 00-README) | not recorded in log | **PROVISIONAL** (O2 2026-10-08): compiles (standalone Roslyn), **wrappers NOT baked**, presenters fall back to procedural; visual check NOT run | ad9c08c (models were added in 34a5992) | logs/sessions/2026-10-08-PH-U-MODELS-run1.md | Compile: `Pres.dll` 75,776 bytes no errors; `Ed.dll` 29,696 bytes no errors (same log, CHECKPOINT 2). U3 PlayMode 20 tests "NOT RUN" (same log acceptance table). |
| E-HANDOFF | Electronics team handoff (their boards are flashed with `node_a_haptic` v0.5.0 and `node_b_bio` v0.5.0, per their §A; see FLAG 6) | not a prompt | electronics team | **handed to electronics team**: handoff received, Opus consistency check done (§B), **network untested** on their side (their §A table) | 5c0b12d | docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md | §B: 13 topics, 5 requests. Their stated ports 8790/8791; cap 150; 50–400 ms pulse; 100 ms gap; 50 % duty per 10 s; 2 s watchdog (same doc, §B intro). No test counts. |
| U6 | APK, demo mode, performance | MVP | Sonnet | not started | — | none | none |
| U7 | Agency phase | stretch (after G2) | Sonnet | not started | — | none | none |
| U8 | Synthetic participant sweep | stretch | Sonnet | not started | — | none | none |
| F1 | Node A firmware v0.5.0 (`firmware/opus_sleeve/opus_sleeve.ino`) | not tagged in 00-README | Sonnet | **reference only — owned by the electronics team (user decision 2026-10-07)**. Uncompiled: no arduino-cli on the authoring machine. | ef387e5 | logs/sessions/2026-10-07-PH-F-F1-run1.md | none. Compile not run. The `sim/haptic` 26 passed in the log tests the Python fake, not the .ino. |
| F2 | Node B bio_node firmware v0.5.0 | not tagged in 00-README | Sonnet | **reference only — owned by the electronics team (user decision 2026-10-07)**. Partial Node B code, uncompiled. | ef387e5 (same commit as F1) | none (no F2 log) | none |
| S1 | Twin-lite: fake Node A + Node B (`sim/sleeve/twin.py`) | MVP | Sonnet | done (O2 PASS, one manager fix: `sensor_chunk.timestamp_ms` = first sample) | e053f7b | logs/sessions/2026-10-07-PH-S-S1-run1.md | `sim/sleeve` 38 passed; `sim/haptic` 26 passed (baseline kept). |
| S2 | E2E harness + laptop live plot (`tools/demo/`, `sim/live/phantom_replay.py`) | MVP | Sonnet | done (O2 PASS). Unity PlayMode path wired, **not run**. | 01551de | logs/sessions/2026-10-08-PH-S-S2-run1.md | `tools/demo` 105 passed; `sim/live` 45 passed; L3 `--sim --no-unity` smoke 7/7 (stroke timing rows use fixture stamps). |
| S2b | Fresh L3 fixtures, gating cue-delivery flag row, fixture freshness guard | follow-up of S2 (not in the brief's list) | Sonnet | done (O2 PASS) | d59d75c | logs/sessions/2026-10-08-PH-S-S2b-run1.md | `tools/demo/tests` 114 passed (baseline 105, +9); `sim/live` 45 passed. |
| S3 | Full twin + participant model | stretch (after G2) | Sonnet | not started | — | none | none |
| A1 | Live operator card with EMG + accel traces | MVP | Sonnet | done (O2 PASS) | aa2d58c | logs/sessions/2026-10-08-PH-A-A1-run2.md | `flutter test` +301 (baseline 207, +94 incl. 21 goldens); `flutter analyze` 0 errors, 0 warnings, 235 infos; hub test on port 8797: 5 passed. |
| A1b | Narrow screens: Controls above traces; debug APK on phone | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | 248cf23 | logs/sessions/2026-10-08-PH-A-A1b-run1.md | `flutter test` 301 passed; analyze 235 infos, 0 errors, 0 warnings. Debug APK built; installed and launched on serial 164cd676 (M2101K6P, 393 dp), driven by the builder via adb (see Flags). |
| A1c | Friendly EN/HI labels from manifest `x-ui.title` | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | 533e875 | logs/sessions/2026-10-08-PH-A-A1c-run1.md | `flutter test` +302 All passed; `contracts/validate.py` All PASS. Hindi titles machine-written, need native review (MANUAL_TODO). |
| A1d | Hub `GET /opus/v1/live/last_status` | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | a7c50bb | logs/sessions/2026-10-08-PH-A-A1d-run1.md | `flutter test` +303; hub_phantom_test +6 (port 8797); analyze 236 infos, 0 errors, 0 warnings. |
| A2 | Embodiment report + witness mirror (FR-AP-02) | stretch (after G2) | Sonnet | not started | — | none | none |
| N1 | Embodiment metrics (FR-AN-01) | MVP | Sonnet | done (O2 PASS) | 082ec6a | logs/sessions/2026-10-08-PH-N-N1-run1.md | analytics `76 passed` (baseline 51 + 25 new); `tests/test_embodiment.py` 25 passed at O2. |
| N1b | Embodiment delivery quality (cue_delivery_rate, touch_incomplete) | follow-up of N1 (MVP) | Sonnet | done (O2 PASS) | becdd04 | logs/sessions/2026-10-08-PH-N-N1b-run1.md | analytics `83 passed` (full); `tests/test_embodiment.py` 32 passed; `tools/demo/tests` 105 passed (at that time). |
| N2 | Validation vs synthetic truth | stretch (needs S3) | Sonnet | not started | — | none | none |
| H1 | Scribe after every run (log tidy, CHANGELOG, MANUAL_TODO) | MVP (every run) | Haiku | partial: this pass (2026-10-08) wrote CHANGELOG lines and the MANUAL_TODO PH section. Log headings were **not** reordered (no run log edited). | — | none (this pass) | none |
| H2 | Gate summary (G1, G2, G3) | not tagged in 00-README | Haiku | not started | — | none | none |
| H3 | CONTEXT.md "START HERE" refresh | not tagged in 00-README | Haiku | done for the 2026-10-08 Phantom Hand box (written on Opus's facts list; Opus reviews the diff) | — | none | none |
| H4 | Human runbooks (`docs/PH_ON_DEVICE_RUNBOOK.md`, checklist) | not tagged (needs U6 and L4/L5) | Haiku | not started | — | none | none |
| H5 | Demo fact sheet (`docs/PH_FACTS.md`) | not tagged (needs L4/L5) | Haiku | not started | — | none | none |

Notes on the table:
- O2 gate reviews (O2–O4 in `prompts/OPUS.md`) are recorded in the "O2 review" section at the end of each log.
- The electronics team's interface and bring-up guide: `docs/PH_ELECTRONICS_INTERFACE.md` (commit 88936b9, not a
  prompt run).

## Latest test counts (one line per suite)

### On the new machine, 2026-10-08

| Suite | Count | Log |
|---|---|---|
| Unity EditMode (all) | 343 (329 pass / 14 fail) | logs/sessions/2026-10-08-PH-O-WORKLIST-run1.md |
| Unity PlayMode (all) | 29 (25 pass / 4 fail) | logs/sessions/2026-10-08-PH-O-WORKLIST-run1.md |
| Analytics `pytest` | 83 passed | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |
| `sim/haptic` (Python fake) | 26 passed | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |
| `sim/sleeve` | 38 passed | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |
| `sim/live` | 45 passed | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |
| `tools/demo/tests` | 106 passed, 8 skipped (missing fixture) | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |
| L3 smoke `--sim --no-unity` | 7/7 checks PHANTOM HAND GREEN | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |
| L3 `--faults` | all green (A 6/6+5/5, B 7/7+3/3, C 7/7+4/4) | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |
| `contracts/validate.py` | All validations passed (105 PASS) | logs/sessions/2026-10-08-PH-S-BASELINE-run1.md |

### Previous machine results

| Suite | Count | Log |
|---|---|---|
| Unity EditMode (all) | 290/290 passed | logs/sessions/2026-10-08-PH-U-U3-run1.md |
| Unity PlayMode, PhantomHand group | 20/20 passed | logs/sessions/2026-10-08-PH-U-U3-run1.md |
| Unity PlayMode, all | 25 passed, 3 failed, 28 total (failures: LiveLink 8787 conflict, HapticIntegration low_confidence, audit grasp 6.2 cm vs 5 cm) | logs/sessions/2026-10-08-PH-U-U3-run1.md |
| Flutter app `flutter test` | +303 All tests passed | logs/sessions/2026-10-08-PH-A-A1d-run1.md |
| Flutter `flutter analyze` | 0 errors, 0 warnings, 236 infos | logs/sessions/2026-10-08-PH-A-A1d-run1.md |
| Analytics `pytest` | 83 passed | logs/sessions/2026-10-08-PH-N-N1b-run1.md |
| `tools/demo/tests` | 114 passed | logs/sessions/2026-10-08-PH-S-S2b-run1.md |
| `sim/live` | 45 passed | logs/sessions/2026-10-08-PH-S-S2b-run1.md |
| `sim/sleeve` | 38 passed | logs/sessions/2026-10-07-PH-S-S1-run1.md |
| `sim/haptic` (Python fake, baseline) | 26 passed | logs/sessions/2026-10-07-PH-S-S1-run1.md |
| `contracts/validate.py` | All validations passed | logs/sessions/2026-10-07-PH-O-O1-run1.md |

## Next (in this order)

1. **Unity MCP client registration fix (manager, 2026-10-08).** The issue: `claude mcp add` (what the editor's Register button runs, cwd `game/`) stores a **local-scope** entry keyed by git root `H:/Chenta/phantomhand`. A session opened in any other folder never loads it. Fix: register at **user scope** via `python tools/unity_mcp.py register` (reads the token from the local asset, never prints it). Once reconnected via `/mcp`, run `CompilationTools(method: "GetCompilationStatus")` — expect `{"success":true,"status":"clean","errorCount":0}` (logs/sessions/2026-10-08-PH-O-RESUME-run1.md, "Unity MCP root cause"; MANUAL_TODO, Phantom Hand human steps).
2. **Unity test driver (one agent, one driver):** run EditMode (all) and PlayMode. U4 tests (`UiModelTests`, `UiPanelTests`) and
   `CaptureU4Shots()` (source: U4 log, Next step); U5 `Opus.Shell.Tests` (25) and `PH_FullRun` (source: U5 log, item 9); the
   Orchard totals must not drop (EditMode ≥ 107, PlayMode ≥ 6, 02-RULES §2). Then bake the models (`EnsureWrappers`, see item 3).
   Each run writes its own log; Opus sets PASS/REDO.
3. **Bake the models, with the rigged hand.** The user added a rigged skin hand (`game/Assets/Art/PhantomHand/Models/RiggedHand/handRig_02.fbx`
   + `hand_Co/No/Ro/Sp` textures; right hand `hand.R`, 68 bones, ~14.5k tris; the file also carries a camera and a light to drop on
   import; licence/source unknown, credit line TBD). The arm wiring will switch to this hand. The MODELS importer was written for the
   black glove 324213, which is a LEFT hand and is mirrored in `BuildHand` (MODELS log, open issues 1–2); the rigged right hand needs
   its own check in the bake. Gloves: 324213 (black LEFT leather glove), 1571125 (brown leather glove); 553886 is an arm guard
   (manager report 2026-10-08; MODELS log, CHECKPOINT 1 for 324213 and 553886).
   Model IDs and paths (folders confirmed present on disk, 2026-10-08; slot names from the MODELS log):
   - `1534923` flat stone — `game/Assets/MetaAssets/Prefabs/1534923/1534923_L.fbx` (unused by the MODELS importer)
   - `1746344` table — `game/Assets/MetaAssets/Prefabs/1746344/1746344_L.fbx` (slot PH_Table)
   - `182879` stone — `game/Assets/MetaAssets/Prefabs/182879/` (slot PH_Stone)
   - `324213` black LEFT leather glove, used as hand (slot PH_Hand) until the rigged hand replaces it — `game/Assets/MetaAssets/Prefabs/324213/324213_L.fbx`
   - `553886` arm guard, used as forearm (slot PH_Forearm) — `game/Assets/MetaAssets/Prefabs/553886/553886_L.fbx`
   - `83035` stone — `game/Assets/MetaAssets/Prefabs/83035/83035_L.fbx` (unused by the MODELS importer)
   - `935160` sleeve — `game/Assets/MetaAssets/Prefabs/935160/935160_L.fbx` (slot PH_Sleeve)
   - `997491` paint brush — `game/Assets/MetaAssets/Prefabs/997491/997491_L.fbx` (slot PH_Brush)
   - `1571125` brown leather glove — no role in any log yet (manager report 2026-10-08)
   The ~2-unit size, Z-up, and 15k-tri figures come from the manager's brief (2026-10-08), not from a measured log.
   The MODELS log's design values (hand fingertip at z 0.19 m, sleeve 1.08x, forearm 0.90x ArmGeometry, table 1.2 x 0.7 x 0.75 m)
   are design values from a numpy port, not measurements; the importer prints the real bounds when run.
   The 324213 glove had no rig (fingers could not pose, which blocked addition A5). The rigged hand (above) is meant to answer that.
4. **Wire the builder hooks** (manager; the agents did not touch the scene builder): `PhantomHandUiInstaller.Install(root, anchors)`
   (U4 log, builder hook; the existing `AddUi` already does the equivalent); `PhantomHandSceneController` via
   `root.AddComponent<Opus.Shell.PhantomHandSceneController>()` (U5 log, builder hook); `PhantomModelImporter.EnsureWrappers()`
   and `PhModels.SpawnTable(table, ...)` in `PhantomHandSceneBuilder` (MODELS log, builder hook lines 1–2);
   `BootstrapSceneBuilder.BuildBootstrap()` once (U5 log, Bootstrap scene).
5. **SDK changes (U1), accept and implement.** From the electronics §B table (`docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md`): accept
   `accepted` on acks (#2); send `keepalive` each second in addition to `ping`/`subscribe` (#3); send both `text` and `mode` on display (#5);
   match the node by `device_kind`, not by id (#1). From the U5 log open issues (CROSS-TRACK, accepted at O2 2026-10-08):
   `LiveClient` optional `hubPort` argument, default 8787 (issue 1); the UDP receive loop must ignore WSAECONNRESET 10054 and continue
   (issue 2); `StrokeDriver` reschedule on resume, because `pause` + `resume` loses the stroke plan (issue 3).
6. **L3 with Unity:** `python tools/demo/run_pipeline.py --game phantom_hand --sim` (without `--no-unity`), then
   `--faults`. The L3 rows so far are fixture-stamped (S2 log, Open issue 2).
7. **Gate G2:** one full 4-minute run recorded end to end (PRD §14; `docs/agent-briefs/ph/00-README.md`).
8. **Human, electronics:** the 5 requests (§B) and the Wi-Fi test with their credentials (MANUAL_TODO, Phantom Hand human steps).
9. **Additions A1–A3** (PRD v2 §5.1; spec `03-SPEC.md` §12). These are the *additions* named A1–A5, **not** the
   prompts A1 (live card) and A2 (report). Stretch items U7, U8, S3, A2, N2 stay behind G2.

## Flags

**FLAG FOR OPUS (1): stored fixture drift vs computed drift.**
- `logs/sessions/2026-10-08-PH-N-N1-run1.md` says the fixture's witness numbers are 2.4 (SYNC) / 0.4 (ASYNC) and
  that they are "not reproduced"; analytics computes 2.00 / 0.00 on a copy of `contracts/fixtures/sessions/phantom_hand_min`.
- `logs/sessions/2026-10-08-PH-S-S2-run1.md` shows the same computed values (2.00 / 0.00) from the replay.
- Both logs are kept as written. The fixture's stored `metrics.json` was not read by me. Opus to decide which is right.

**FLAG FOR OPUS (2): U1 test-count arithmetic.**
- `logs/sessions/2026-10-07-PH-U-U1-run1.md` writes "208 prior + 60 new U1 tests" next to `passed=290`. 208 + 60 = 268.
- The 290 figure agrees across `logs/sessions/2026-10-07-PH-U-U1-run1.md`, `logs/sessions/2026-10-07-PH-U-U2-run1.md`
  (checkpoint 3) and `logs/sessions/2026-10-08-PH-U-U3-run1.md`, and in the commit message of 8f7a734 ("EditMode 290/290").
- The 22 tests between 268 and 290 are not named in any log. Opus to confirm.

**FLAG FOR OPUS (3): Unity rule conflict.**
- `CLAUDE.md` (Unity section) and `docs/agent-briefs/ph/02-RULES.md` §3 (Unity) say batch mode only, GUI editor closed.
- The PH logs (`logs/sessions/2026-10-07-PH-U-U1-run1.md`, `...-U-U2-run1.md`, `logs/sessions/2026-10-08-PH-U-U3-run1.md`)
  and the manager's decision say Unity is driven through the Unity MCP with the editor open, one driver.
- Recorded here as the user's decision. No rule file was edited.

**FLAG FOR OPUS (4): APK claim.**
- `02-RULES.md` §1.2 says no APK "works" claim without a human run outside the sandbox.
- `logs/sessions/2026-10-08-PH-A-A1b-run1.md` shows the builder installed and launched the debug APK on serial 164cd676
  via adb and read screenshots. This PH_STATUS row says "installed and launched, driven by the builder via adb" and
  makes no "works" claim. A human run is still needed.

**FLAG FOR OPUS (5): Shell compile state, two logs in time order.**
- `logs/sessions/2026-10-08-PH-U-MODELS-run1.md` (CHECKPOINT 2) says `Shell.Runtime` fails to compile (`PhantomLiveStatus.cs`, missing
  PhantomHand references), written before the fix.
- `logs/sessions/2026-10-08-PH-U-U5-run1.md` (CHECKPOINT 1) says the asmdef fix was made and the Editor.log has 0 `error CS` after line 10202.
- These are not a contradiction in time order, but neither log has a Unity compile since. Opus to confirm with the next compile.

**Flag 5: Shell compile state (Editor.log).** On the new machine (2026-10-08), Editor.log contains 0 `error CS` lines after the CHECKPOINT lines in both logs/sessions/2026-10-08-PH-U-MODELS-run1.md (CHECKPOINT 2) and logs/sessions/2026-10-08-PH-U-U5-run1.md (CHECKPOINT 1). Compile status: **CLEAN**. (logs/sessions/2026-10-08-PH-O-RESUME-run1.md, logs/sessions/2026-10-08-PH-O-WORKLIST-run1.md)

**FLAG FOR OPUS (6): which firmware the boards run.**
- `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` §A: the team flashed `node_a_haptic` v0.5.0 and `node_b_bio` v0.5.0 (sketch names as the team wrote them).
- The repo has `firmware/opus_sleeve/opus_sleeve.ino` v0.5.0 (`logs/sessions/2026-10-07-PH-F-F1-run1.md`, uncompiled reference, electronics-team owned).
- The repo file is not the sketch the team flashes. Their device id is `CHETNA_HAPTIC_001` (§A), the contract's is `SLEEVE_001` (03-SPEC D7; §B #1). Opus to decide.

**FLAG FOR OPUS (7): scope of the "§B" SDK change set.**
- The manager's brief lists `LiveClient` hubPort, the UDP 10054 receive loop and the stroke reschedule on resume as "from the §B table".
- In the sources they are the U5 log's open issues 1–3 (`logs/sessions/2026-10-08-PH-U-U5-run1.md`, CROSS-TRACK). §B covers items #1, #2, #3, #5 (plus requests). PH_STATUS item 5 lists them by their real source. Opus to confirm the change set.

**Not a disagreement, recorded for clarity:**
- The git history was read from `.git/logs/HEAD` (no Bash tool in this pass). Its top entries match the manager's
  `git log --oneline -25` for d59d75c, becdd04, a7c50bb, 01551de and a265205.
- The A1b log says 392.7 dp; the manager brief says 393 dp. Same device width rounded.
