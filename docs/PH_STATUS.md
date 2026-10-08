# Phantom Hand — prompt status (PH_STATUS)

Written 2026-10-08 by ph-scribe (Haiku) from the logs listed in each row. Numbers are copied from those logs; the
log path is next to every number. Where two logs disagree, both are listed and marked **FLAG FOR OPUS**.

- **Status** vocabulary: `done` (O2 review by Opus = PASS), `partial`, `not started`, `awaiting review` (H1 rule),
  `reference only`, `handed to electronics team`.
- **Commit** = short hash from the local reflog of branch `claude/project-thread-qrz2a9` (`.git/logs/HEAD`); the
  top entry is `d59d75c`.
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
| U4 | Calibration, probe, questionnaire, witness, HUD | MVP | Sonnet | **partial**, interrupted by a usage limit, **uncommitted, no log** | — | none | none |
| U5 | Composition root, session runner, live link, L3 | MVP | Sonnet | not started | — | none | none |
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

1. **U4 resume.** Partial, interrupted by a usage limit, uncommitted, no log. Resume from the code on disk and the
   U3 log (`logs/sessions/2026-10-08-PH-U-U3-run1.md`: `ArmThreatPresenter.SetCalibration` is the hook U4 calls).
   Write the U4 log as it goes. Unity: one driver, via the Unity MCP with the open editor (see the PH box in CONTEXT.md).
2. **Swap in the 8 Meta models the user added.** All unrigged, about 2 units normalised, Z-up, 15k tris at `_L`,
   with `_M` and `_S` LODs. Model IDs and paths (folders confirmed present on disk, 2026-10-08):
   - `1534923` flat stone — `game/Assets/MetaAssets/Prefabs/1534923/1534923_L.fbx`
   - `1746344` table — `game/Assets/MetaAssets/Prefabs/1746344/1746344_L.fbx`
   - `182879` stone — `game/Assets/MetaAssets/Prefabs/182879/`
   - `324213` hand (**no rig**) — `game/Assets/MetaAssets/Prefabs/324213/324213_L.fbx`
   - `553886` forearm — `game/Assets/MetaAssets/Prefabs/553886/553886_L.fbx`
   - `83035` stone — `game/Assets/MetaAssets/Prefabs/83035/83035_L.fbx`
   - `935160` sleeve — `game/Assets/MetaAssets/Prefabs/935160/935160_L.fbx`
   - `997491` paint brush — `game/Assets/MetaAssets/Prefabs/997491/997491_L.fbx`
   The ~2-unit size, Z-up, and 15k-tri figures come from the manager's brief (2026-10-08), not from a measured log.
   The hand has no rig, so fingers cannot pose (fine for the MVP; blocks addition A5 hand-closing). See MANUAL_TODO.
3. **U5** composition root, session runner, live link (reads `OPUS_PH_HUB`, `OPUS_PH_NODE_A`, `OPUS_PH_NODE_B`,
   `OPUS_PH_DISCOVERY_PORT`; S2 log, CROSS-TRACK request, accepted at O2).
4. **L3 with Unity:** `python tools/demo/run_pipeline.py --game phantom_hand --sim` (without `--no-unity`), then
   `--faults`. The L3 rows so far are fixture-stamped (S2 log, Open issue 2).
5. **Gate G2:** one full 4-minute run recorded end to end (PRD §14; `docs/agent-briefs/ph/00-README.md`).
6. **Additions A1–A3** (PRD v2 §5.1; spec `03-SPEC.md` §12). These are the *additions* named A1–A5, **not** the
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

**Not a disagreement, recorded for clarity:**
- The git history was read from `.git/logs/HEAD` (no Bash tool in this pass). Its top entries match the manager's
  `git log --oneline -25` for d59d75c, becdd04, a7c50bb, 01551de and a265205.
- The A1b log says 392.7 dp; the manager brief says 393 dp. Same device width rounded.
