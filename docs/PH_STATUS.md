# Phantom Hand — prompt status (PH_STATUS)

Written 2026-10-08 by ph-scribe (Haiku) from the logs listed in each row. Numbers are copied from those logs; the
log path is next to every number. Where two logs disagree, both are listed and marked **FLAG FOR OPUS**.
Updated the same afternoon (docs scribe) to commit `aec2aed`, from `git log` and the session logs named in each row.

- **Status** vocabulary: `done` (O2 review by Opus = PASS), `partial`, `not started`, `awaiting review` (H1 rule),
  `reference only`, `handed to electronics team`.
- **Commit** = short hash. Up to `5c0b12d` the hashes come from the old repository (branch `claude/project-thread-qrz2a9`,
  `.git/logs/HEAD` at the time); this repository starts at `eb34f3b`, one import of `5c0b12d`, so those old hashes do not
  exist here. From `018682b` on the hashes are in this repository's `main` (`git log`); the newest is `aec2aed` (13:06 IST).
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
| U4 | Calibration, probe, questionnaire, witness, HUD | MVP | Sonnet | **awaiting review** (H1 rule; was PROVISIONAL): the tests ran for the first time on 8 Oct on the new machine. 12 `UiPanelTests` failed (10 from a Unity fake-null `??` bug, 2 real layout defects) and were fixed (f9b7ae8). The witness screen was regrouped in 29b0c1d (Body / Mind / The one who noticed, D12). O2 PASS is not written in any log; nobody has looked at the panels in a headset | 362a66c (old repo); f9b7ae8; 29b0c1d | logs/sessions/2026-10-08-PH-U-U45FIX-run1.md; logs/sessions/2026-10-08-PH-U-U4-run2.md | `UiPanelTests` + `PhantomLiveStatusTests` 21/21 (U45FIX log, step 1); EditMode all 618/618 at 29b0c1d (commit message). U4 screenshots are in `logs/sessions/screens/ph/u4` (added in 29b0c1d; review not recorded). |
| U5 | Composition root, session runner, live link, L3 | MVP | Sonnet | **awaiting review** (H1 rule; was PROVISIONAL): ran for the first time on 8 Oct. The full run (scene, hub link, twin nodes, upload) works end to end in the editor and plays all 14 phases at 29b0c1d. It found four defects, fixed in 15cca8d: the live outbox re-sent every `trial_event` once a second, the threat quality and the questionnaire values were off the contract, sensor files were written after the session ended. `PH_FullRun` stays red on one bar: cues acked 88-91 % against 95 %, the editor main thread stalls 150-400 ms about 80 times per run, cause open | 193a15c (old repo); f9b7ae8; 15cca8d; 29b0c1d | logs/sessions/2026-10-08-PH-U-U45FIX-run1.md | EditMode 618/618 and PlayMode, Phantom Hand filter (`PhantomHand` or `PH_`), 25/26 at 29b0c1d; 383/383 and 20/21 at 15cca8d. After the outbox fix: hub `trial_event` count 295-296 for 298 events, upload 1.8-3.2 s, RTT p50 5-7 ms (15cca8d message). |
| MODELS | 3D models in the game: importer + presenters with procedural fallback (not a numbered prompt in `00-README.md`) | MVP (manager brief 2026-10-08; tag not in 00-README) | Sonnet for run 2 (RESUME log); runs 1 and 3 not recorded | **awaiting review** (H1 rule; was PROVISIONAL): wrappers baked and used in the game (7f64e29): the rigged hand (CPU skinning; `BakeMesh` had collapsed it to a 2 mm speck), table, brush, stone, sleeve, forearm; the procedural arm stays when a wrapper is missing. Open (cosmetic): forearm tone lighter than the hand, brush hides part of the hand at stroke start. Seven room and table props (brush, lamp, plant, window, bowl, cup, picture) were added as GLB files and are not wired into the scene (9a011a3). Meta asset terms unchecked (`CREDITS.md`) | ad9c08c (old repo; models were added in 34a5992); 7f64e29; 9a011a3 | logs/sessions/2026-10-08-PH-U-MODELS-run3.md (run 2: importer design; run 1: first importer) | EditMode 514/514 and PlayMode `PhantomHandPresentationTests` 20/20 in the open editor (7f64e29 message); 11 review pictures in `logs/sessions/screens/ph/models`. |
| E-HANDOFF | Electronics team handoff (their boards are flashed with `node_a_haptic` v0.5.0 and `node_b_bio` v0.5.0, per their §A; see FLAG 6) | not a prompt | electronics team | **handed to electronics team**: handoff received, Opus consistency check done (§B), **network untested** on their side (their §A table). The software follows their dialect since 8c88d0d (contracts v0.2.1), f9b7ae8 (SDK) and 9666dd8 (twin `--dialect team`); the real boards have never run with the game | 5c0b12d (old repo); 8c88d0d; f9b7ae8; 9666dd8 | docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md | §B: 13 topics, 5 requests. Their stated ports 8790/8791; cap 150; 50–400 ms pulse; 100 ms gap; 50 % duty per 10 s; 2 s watchdog (same doc, §B intro). No test counts. |
| U6 | APK, demo mode, performance | MVP | Sonnet | **partial**, code only (9fd4e18): APK build method for Phantom Hand, `phantom_endpoints.json` override, Android multicast lock, Bootstrap offers only games in the build. The Quest APK has never been built; the lock and the file have never run on a headset. Demo mode exists (`demo_mode`, D18) | 9fd4e18 | logs/sessions/2026-10-08-PH-U-U6CODE-run1.md | EditMode 511/511 in the editor at 9fd4e18 (383 + 86 of this change set + 42 of the models code; commit message). |
| U7 | Agency phase | stretch (after G2) | Sonnet | **awaiting review** (H1 rule): built, opt-in (`agency_enabled`, `autonomous_close_enabled`) in 29b0c1d (D14): `AgencyRun`, once in the last condition, hand-tracking fallback on the index fingertip, q5 only after a real self-close. Never run with a real Node B, a person or a headset | 29b0c1d | logs/sessions/2026-10-08-PH-U-AUDIT-run1.md | `AgencyTests` (24 pure cases, AUDIT log) are part of EditMode 618/618 (29b0c1d). |
| AUDIT | Theme 5 audit applied in the game: witness regroup, Dissolve + Reveal (fallback), slow brush, rating after both conditions, stone volume (additions A2 and A5, D11-D20) | follow-up (not in the brief's list) | Sonnet (RESUME log, wave 4 ledger) | **awaiting review** (H1 rule): committed in 29b0c1d. `additionsEnabled` is now on by default; the self-touch phase is not planned (D13); the Reveal is the fallback, no passthrough exists; never on a headset | 29b0c1d | logs/sessions/2026-10-08-PH-U-AUDIT-run1.md | EditMode 618/618; PlayMode, Phantom Hand filter, 25/26: the one red test is the full run's cue bar, 88-91 % against 95 %, cause open (commit message). |
| U8 | Synthetic participant sweep | stretch | Sonnet | not started | — | none | none |
| F1 | Node A firmware v0.5.0 (`firmware/opus_sleeve/opus_sleeve.ino`) | not tagged in 00-README | Sonnet | **reference only — owned by the electronics team (user decision 2026-10-07)**. Uncompiled: no arduino-cli on the authoring machine. | ef387e5 | logs/sessions/2026-10-07-PH-F-F1-run1.md | none. Compile not run. The `sim/haptic` 26 passed in the log tests the Python fake, not the .ino. |
| F2 | Node B bio_node firmware v0.5.0 | not tagged in 00-README | Sonnet | **reference only — owned by the electronics team (user decision 2026-10-07)**. Partial Node B code, uncompiled. | ef387e5 (same commit as F1) | none (no F2 log) | none |
| S1 | Twin-lite: fake Node A + Node B (`sim/sleeve/twin.py`) | MVP | Sonnet | done (O2 PASS, one manager fix: `sensor_chunk.timestamp_ms` = first sample) | e053f7b | logs/sessions/2026-10-07-PH-S-S1-run1.md | `sim/sleeve` 38 passed; `sim/haptic` 26 passed (baseline kept). |
| S2 | E2E harness + laptop live plot (`tools/demo/`, `sim/live/phantom_replay.py`) | MVP | Sonnet | done (O2 PASS). Unity PlayMode path wired, **not run**. | 01551de | logs/sessions/2026-10-08-PH-S-S2-run1.md | `tools/demo` 105 passed; `sim/live` 45 passed; L3 `--sim --no-unity` smoke 7/7 (stroke timing rows use fixture stamps). |
| S2b | Fresh L3 fixtures, gating cue-delivery flag row, fixture freshness guard | follow-up of S2 (not in the brief's list) | Sonnet | done (O2 PASS) | d59d75c | logs/sessions/2026-10-08-PH-S-S2b-run1.md | `tools/demo/tests` 114 passed (baseline 105, +9); `sim/live` 45 passed. |
| SDKB | SDK change set for the real firmware dialect: `accepted` acks, `keepalive`, display `mode` + `text`, node match by `device_kind`, `hubPort`, UDP 10054, stroke reschedule on resume | follow-up of the §B check (not in the brief's list) | Sonnet (RESUME log) | **awaiting review** (H1 rule): merged by Opus with one review change (`keepalive` keeps going to a silent node). The headset still uses hub port 8787: U45FIX steps 10-11 (port plumbing, app pause) were skipped | f9b7ae8 | logs/sessions/2026-10-08-PH-U-SDKB-run1.md | EditMode 378/378 after the merge (343 + 35 new; RESUME log). |
| XMACHINE | Twin and harness across machines: twin on the LAN, team dialect (`--dialect team`), harness `--lan`, firewall report script, per-folder venvs, tool paths | follow-up (not in the brief's list) | Sonnet (RESUME log) | **awaiting review** (H1 rule): Opus re-ran three suites before the commit (RESUME log). A second physical PC has never been used | 9666dd8 | logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md | See "Latest test counts" below: `sim/sleeve` 94, `sim/live` 47 (+3 e2e), `tools/demo/tests` 121 passed + 8 skipped, L3 `--no-unity` 7/7 in plain, `--dialect team` and `--lan`. |
| STATION | `tools/demo/sleeve_station.py`: brush your own arm with the sleeve only, no headset (tier T3 of the runbook) | follow-up (R4; not in the brief's list) | not recorded in the log | **awaiting review** (H1 rule): green against the twin in both dialects; never run on a real board | a667def | logs/sessions/2026-10-08-PH-S-STATION-run1.md | 38 tests passed (a667def message). |
| S3 | Full twin + participant model | stretch (after G2) | Sonnet | not started | — | none | none |
| A1 | Live operator card with EMG + accel traces | MVP | Sonnet | done (O2 PASS) | aa2d58c | logs/sessions/2026-10-08-PH-A-A1-run2.md | `flutter test` +301 (baseline 207, +94 incl. 21 goldens); `flutter analyze` 0 errors, 0 warnings, 235 infos; hub test on port 8797: 5 passed. |
| A1b | Narrow screens: Controls above traces; debug APK on phone | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | 248cf23 | logs/sessions/2026-10-08-PH-A-A1b-run1.md | `flutter test` 301 passed; analyze 235 infos, 0 errors, 0 warnings. Debug APK built; installed and launched on serial 164cd676 (M2101K6P, 393 dp), driven by the builder via adb (see Flags). |
| A1c | Friendly EN/HI labels from manifest `x-ui.title` | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | 533e875 | logs/sessions/2026-10-08-PH-A-A1c-run1.md | `flutter test` +302 All passed; `contracts/validate.py` All PASS. Hindi titles machine-written, need native review (MANUAL_TODO). |
| A1d | Hub `GET /opus/v1/live/last_status` | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | a7c50bb | logs/sessions/2026-10-08-PH-A-A1d-run1.md | `flutter test` +303; hub_phantom_test +6 (port 8797); analyze 236 infos, 0 errors, 0 warnings. |
| SETUP | Flutter 3.47.4 at `H:\flutter` on the new machine: tests, Windows build, debug APK, operator app on the team phone | follow-up (new machine; not in the brief's list) | Sonnet (RESUME log) | **awaiting review** (H1 rule): all steps ran. The repo APK could not update the phone's old app (another PC's debug key), so a side-by-side build `com.opus.opus_app.pc` runs there: hub health, live card with phases, both chips and traces for a replayed session (screens in `logs/sessions/screens/ph/phone`). That build predates aec2aed | a667def (log and screens) | logs/sessions/2026-10-08-PH-A-SETUP-run1.md | `flutter test` +303; `flutter analyze` 0 errors, 0 warnings, 236 infos; Windows release build and debug APK built. |
| A-CARD, A-MIRROR, A-INTEG | Operator app after the R3 research: live card rework (B1-B8, B12), audience mirror of the headset's results (B11), embodiment report in the session report (B19); one integrator merged them | follow-up of A1; B11 and B19 are the A2 stretch below | Sonnet (the three logs) | **awaiting review** (H1 rule): committed in aec2aed. Not run on a phone or a Quest; Hindi strings are machine quality | aec2aed | logs/sessions/2026-10-08-PH-A-INTEG-run1.md (and -CARD-, -MIRROR-) | `flutter test` +455 all passed; `flutter analyze` 0 errors, 0 warnings, 233 infos (aec2aed message; INTEG log, step 10). Open: the mirror shows "No data" when the card is opened after the witness event (INTEG log). |
| A2 | Embodiment report + witness mirror (FR-AP-02) | stretch (after G2) | Sonnet | built outside this prompt: see A-CARD, A-MIRROR, A-INTEG (aec2aed); Flutter tests only | aec2aed | logs/sessions/2026-10-08-PH-A-MIRROR-run1.md | see that row |
| N1 | Embodiment metrics (FR-AN-01) | MVP | Sonnet | done (O2 PASS) | 082ec6a | logs/sessions/2026-10-08-PH-N-N1-run1.md | analytics `76 passed` (baseline 51 + 25 new); `tests/test_embodiment.py` 25 passed at O2. |
| N1b | Embodiment delivery quality (cue_delivery_rate, touch_incomplete) | follow-up of N1 (MVP) | Sonnet | done (O2 PASS) | becdd04 | logs/sessions/2026-10-08-PH-N-N1b-run1.md | analytics `83 passed` (full); `tests/test_embodiment.py` 32 passed; `tools/demo/tests` 105 passed (at that time). |
| N2 | Validation vs synthetic truth | stretch (needs S3) | Sonnet | not started | — | none | none |
| H1 | Scribe after every run (log tidy, CHANGELOG, MANUAL_TODO) | MVP (every run) | Haiku | partial: this pass (2026-10-08) wrote CHANGELOG lines and the MANUAL_TODO PH section. Log headings were **not** reordered (no run log edited). | — | none (this pass) | none |
| H2 | Gate summary (G1, G2, G3) | not tagged in 00-README | Haiku | not started | — | none | none |
| H3 | CONTEXT.md "START HERE" refresh | not tagged in 00-README | Haiku | done for the 2026-10-08 Phantom Hand box (written on Opus's facts list; Opus reviews the diff); rewritten after aec2aed in 15 lines that point here, to the runbook and to the judge sheet | — | none | none |
| H4 | Human runbooks (`docs/PH_ON_DEVICE_RUNBOOK.md`, checklist) | not tagged (needs U6 and L4/L5) | Haiku | **partial**: the runbook is written (a667def) and was updated after aec2aed; most steps are tagged UNRUN or HUMAN; a separate checklist is not written | a667def | none | none |
| H5 | Demo fact sheet (`docs/PH_FACTS.md`) | not tagged (needs L4/L5) | Haiku | not started | — | none | none |

Notes on the table:
- O2 gate reviews (O2–O4 in `prompts/OPUS.md`) are recorded in the "O2 review" section at the end of each log.
- The electronics team's interface and bring-up guide: `docs/PH_ELECTRONICS_INTERFACE.md` (commit 88936b9, not a
  prompt run).
- Judge wording and what we never claim: `docs/PH_JUDGE_SHEET.md` (ab533e2, 808b4d3, 17ba2ee). Research notes R1-R4:
  `docs/agent-briefs/ph/research/` (R2 landed in c57e529, R3 in ab533e2, R4 in 98b5cde and 808b4d3, R1 in 17ba2ee).
- Rows marked "awaiting review" ran their tests, but no O2 PASS is written in a session log; only Opus sets PASS.

## Latest test counts (one line per suite)

### Latest on the new machine, after commit aec2aed (2026-10-08, 13:06 IST)

| Suite | Count | Source |
|---|---|---|
| Unity EditMode (all) | 618/618 passed | commit 29b0c1d message |
| Unity PlayMode, Phantom Hand filter (`PhantomHand` or `PH_`) | 25/26 passed; the red test is `PH_FullRun`: cues acked 88-91 % against the 95 % bar, cause open | commit 29b0c1d message |
| Flutter app `flutter test` | +455 All tests passed | commit aec2aed message; logs/sessions/2026-10-08-PH-A-INTEG-run1.md, step 10 |
| Flutter `flutter analyze` | 0 errors, 0 warnings, 233 infos | same |
| Analytics `pytest` | 83 passed | logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md (final run, at 9666dd8) |
| `sim/haptic` (Python fake) | 26 passed | same |
| `sim/sleeve` | 94 passed | same; re-run by Opus before the commit (RESUME log) |
| `sim/live` | 47 passed, plus 3 e2e | same |
| `tools/demo/tests` | 121 passed, 8 skipped (110 + 11 in two venvs) at 9666dd8; `test_sleeve_station.py` adds 38 (a667def) | same; commit a667def message |
| `contracts/validate.py` | 112 PASS, 0 FAIL | same |
| L3 `--sim --no-unity`: plain, `--dialect team`, `--lan` | 7/7 checks each | same |

The full Python suites were last run at 9666dd8; a667def re-ran only the sleeve station, `test_lan_mode`/`test_tool_paths` and `test_live_plot` tests (38, 15 and 18 passed; PH-S-STATION-run1). L3 with Unity has never run.

### On the new machine, 2026-10-08, morning baseline (before any fix)

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

1. **Find why the editor main thread stalls (cause open).** `PH_FullRun_DemoMode_AgainstFakeHubAndTwin` plays all 14 phases and
   finishes, but stays red on "stroke cues acked >= 95 %" (88-91 % measured): the editor main thread stalls 150-400 ms about 80 times
   per run. The test tool's polling and the 5 s chunk writes do not explain it. At 15cca8d the late cues followed CPU load from Gradle
   and Flutter on the PC. The 95 % bar is unchanged (commit 29b0c1d message; logs/sessions/2026-10-08-PH-U-U45FIX-run1.md, final checkpoint).
2. **L3 with Unity (not run):** `python tools/demo/run_pipeline.py --game phantom_hand --sim` (without `--no-unity`), then `--faults`,
   with both twin dialects (`--dialect team`). The L3 rows so far are fixture-stamped (S2 log, open issue 2); `--no-unity` is 7/7 in
   plain, `--dialect team` and `--lan` (logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md).
3. **Quest APK: never built.** The code is in (9fd4e18): editor menu `Tools/OPUS/Build Phantom Hand APK`; steps and checks in
   `docs/PH_ON_DEVICE_RUNBOOK.md` 4.7-4.10. The first headset run also decides what has run in the editor only: multicast lock,
   endpoint file, the 3D models, the finale, the agency fallback, the slow brush.
4. **Real passthrough for the Reveal needs a headset check (D10, D13).** Only the fallback exists (the arm glides onto the tracked
   hand); the game has no `OVRPassthroughLayer` code.
5. **Wire the seven props.** `PH_Brush`, `PH_PendantLamp`, `PH_Plant`, `PH_Window`, `PH_SingingBowl`, `PH_TeaCup` and `PH_FramedPicture`
   (9a011a3) are source GLB files only. `CREDITS.md` takes them to be the team's own models: confirm (MANUAL_TODO).
6. **Credits and licences (public repo).** Four Unity Asset Store packs named in `CREDITS.md` (Pack Gesta Furniture #1, Stones,
   Dark Wave Paint Table 01, Mobile Books) are not imported and must stay out of git; the Meta asset-library terms
   (`game/Assets/MetaAssets/`) are unchecked; show the rigged hand's credit line at the demo (CC BY-SA 4.0).
7. **Electronics (humans).** The Wi-Fi/UDP test of the real boards with the game; the 5 requests (§B) and the four firmware questions;
   the bench run with the wearer for the slow brush; one supply per board. The team's firmware streams to the last sender only, so no
   second tool may talk to the nodes during a session (MANUAL_TODO, Phantom Hand human steps).
8. **Operator app on a device.** aec2aed (live card rework, audience results mirror, embodiment report) ran in Flutter tests only.
   Rebuild the Windows exe and the APK, look at the card on the phone (`com.opus.opus_app.pc` still runs the build before aec2aed)
   and at the mirror against a real `witness_summary`. Settle the fixture mismatch: `contracts/fixtures/sessions/phantom_hand_min/metrics.json`
   says drift 2.4 / 0.4, its own events say 2.0 / 0.0 (INTEG log; Flag 1 below).
9. **Gate G2:** one full 4-minute run on a Quest with the sleeve, recorded (PRD §14; `docs/agent-briefs/ph/00-README.md`). Run steps:
   `docs/PH_ON_DEVICE_RUNBOOK.md`; judge wording: `docs/PH_JUDGE_SHEET.md`.
10. **Not built, on purpose:** A3 self-touch (D13), A4 breathing arm and the voice lines for the judge run (D14). Stretch items U8, S3
    and N2 stay behind G2.

### Reference: Meta model ids and slots (kept from the earlier Next list; the rigged hand now replaces the glove, 7f64e29)

Model IDs and paths (folders confirmed present on disk, 2026-10-08; slot names from the MODELS log):
   - `1534923` flat stone — `game/Assets/MetaAssets/Prefabs/1534923/1534923_L.fbx` (unused by the MODELS importer)
   - `1746344` table — `game/Assets/MetaAssets/Prefabs/1746344/1746344_L.fbx` (slot PH_Table)
   - `182879` stone — `game/Assets/MetaAssets/Prefabs/182879/` (slot PH_Stone)
   - `324213` black LEFT leather glove, the earlier hand (slot PH_Hand); the rigged hand replaced it in 7f64e29, the glove stays the importer's fallback (MODELS log, run 3) — `game/Assets/MetaAssets/Prefabs/324213/324213_L.fbx`
   - `553886` arm guard, used as forearm (slot PH_Forearm) — `game/Assets/MetaAssets/Prefabs/553886/553886_L.fbx`
   - `83035` stone — `game/Assets/MetaAssets/Prefabs/83035/83035_L.fbx` (unused by the MODELS importer)
   - `935160` sleeve — `game/Assets/MetaAssets/Prefabs/935160/935160_L.fbx` (slot PH_Sleeve)
   - `997491` paint brush — `game/Assets/MetaAssets/Prefabs/997491/997491_L.fbx` (slot PH_Brush)
   - `1571125` brown leather glove — no role in any log yet (manager report 2026-10-08)
   The ~2-unit size, Z-up, and 15k-tri figures come from the manager's brief (2026-10-08), not from a measured log.
   The MODELS log's design values (hand fingertip at z 0.19 m, sleeve 1.08x, forearm 0.90x ArmGeometry, table 1.2 x 0.7 x 0.75 m)
   are design values from a numpy port, not measurements; the importer prints the real bounds when run.
   The 324213 glove had no rig (fingers could not pose, which blocked addition A5); the rigged hand answers that.
   Done since the earlier Next list (all committed): the Unity MCP user-scope registration (`python tools/unity_mcp.py register`, 018682b);
   the builder hooks, the Bootstrap scene and the SDK change set from the electronics §B table (f9b7ae8); the model bake with the rigged
   hand (7f64e29). In Next item 10, A3 and A4 are the *additions* of PRD v2 §5.1 / `03-SPEC.md` §12, not the prompts A1 (live card) and A2 (report).

## Flags

Status on the afternoon of 8 Oct: Flag 1 is partly settled (the fixtures' events follow Unity and analytics, 2.0 / 0.0, since 4a1c93e, but
`contracts/fixtures/sessions/phantom_hand_min/metrics.json` still says 2.4 / 0.4: PH-A-INTEG-run1, "NOT VERIFIED / OPEN" 5). Flag 5 is
answered inline. Flag 6 is answered in `03-SPEC.md` D7 (the real Node A id is `CHETNA_HAPTIC_001`; the game matches by `device_kind`).
Flag 7: the change set was merged as f9b7ae8 with the 7 items of PH-U-SDKB-run1. Flags 2-4 are unchanged; the text below is kept as written.

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
