# Phantom Hand — prompt status (PH_STATUS)

Written 2026-10-08 by ph-scribe (Haiku) from the logs listed in each row. Numbers are copied from those logs; the
log path is next to every number. Where two logs disagree, both are listed and marked **FLAG FOR OPUS**.
Updated the same afternoon (docs scribe) to commit `aec2aed`, and again to commit `2629398` (wave 6, 13:10 to 14:45; state at 14:50 IST), from `git log` and the session logs named in each row.
The wave 6 rows (APK, PROPS, FLINCH, E2E-L3) and the changes to U5, U6, MODELS and AUDIT also draw on the wave 6 notes in `logs/sessions/2026-10-08-PH-O-RESUME-run1.md`.

- **Status** vocabulary: `done` (O2 review by Opus = PASS), `partial`, `not started`, `awaiting review` (H1 rule),
  `reference only`, `handed to electronics team`.
- **Commit** = short hash. Up to `5c0b12d` the hashes come from the old repository (branch `claude/project-thread-qrz2a9`,
  `.git/logs/HEAD` at the time); this repository starts at `eb34f3b`, one import of `5c0b12d`, so those old hashes do not
  exist here. From `018682b` on the hashes are in this repository's `main` (`git log`); the newest is `2629398` (14:35 IST; pushed 14:36).
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
| U5 | Composition root, session runner, live link, L3 | MVP | Sonnet | **awaiting review** (H1 rule; was PROVISIONAL): ran for the first time on 8 Oct. The full run (scene, hub link, twin nodes, upload) works end to end in the editor and plays all 14 phases (29b0c1d; again at 2629398). It found four defects, fixed in 15cca8d: the live outbox re-sent every `trial_event` once a second, the threat quality and the questionnaire values were off the contract, sensor files were written after the session ended. Until 3fefd61 `PH_FullRun` was red on one bar (cues acked 88-91 % against 95 %, main-thread stalls of 150-400 ms). **The cause is found (3fefd61):** this PC drops its CPU to about 35 % of nominal speed for long stretches while the editor is in play mode. It is a host problem, not a game defect. Ruled out by experiment: the test tool's polling, the recorders' file writes (now on a background task), other jobs on the PC (a run on a quiet machine was no better); a read-through of the per-frame code found nothing that can block the main thread for 100 ms. Why the CPU throttles (heat or a power limit) is not determined; power and thermal settings are the owner's call (MANUAL_TODO). The test now asserts that every SENT cue is acked (>= 98 %); when the 95 % bar on all cues is missed on a host that spent more than 3 % of the run in frames over 50 ms, it ends Inconclusive, not red. A second defect was fixed in 3fefd61: stroke acks reached main-thread-only code (cue adapter, event file, live link) on the transport's receive thread; they are queued to the main thread now. Since 2629398 the test also sends a simulated flinch (FLINCH row) | 193a15c (old repo); f9b7ae8; 15cca8d; 29b0c1d; 3fefd61; 2629398 | logs/sessions/2026-10-08-PH-U-U45FIX-run1.md; logs/sessions/2026-10-08-PH-E2E-L3-run1.md | EditMode 689/689 and PlayMode, Phantom Hand filter (`PhantomHand` or `PH_`), 26/26 at 2629398 (`PH_FullRun`: 199 s, 14 phases, cues 114/114, 1 frame over 120 ms, RTT p50 4.4 / p95 8.4 ms). Cues acked in earlier runs: 88-91 % (morning), 83.3 %, 84.2 %, 68 % while throttled; 112/114 (98.2 %, 14 frames over 120 ms) without the throttle. Older counts: 618/618 and 25/26 at 29b0c1d; 383/383 and 20/21 at 15cca8d. After the outbox fix: hub `trial_event` count 295-296 for 298 events, upload 1.8-3.2 s, RTT p50 5-7 ms (15cca8d message). |
| MODELS | 3D models in the game: importer + presenters with procedural fallback (not a numbered prompt in `00-README.md`) | MVP (manager brief 2026-10-08; tag not in 00-README) | Sonnet for run 2 (RESUME log); runs 1 and 3 not recorded | **awaiting review** (H1 rule; was PROVISIONAL): wrappers baked and used in the game (7f64e29): the rigged hand (CPU skinning; `BakeMesh` had collapsed it to a 2 mm speck), table, brush, stone, sleeve, forearm; the procedural arm stays when a wrapper is missing. Open (cosmetic, noted at 7f64e29 with the Meta brush): forearm tone lighter than the hand, brush hides part of the hand at stroke start. Seven room and table props (brush, lamp, plant, window, bowl, cup, picture) were added as GLB files (9a011a3) and are in the scene since ce05e51 (PROPS row); the brush is now the team's `PH_Brush.glb`, the Meta brush is the fallback. Meta asset terms unchecked (`CREDITS.md`) | ad9c08c (old repo; models were added in 34a5992); 7f64e29; 9a011a3; ce05e51 | logs/sessions/2026-10-08-PH-U-MODELS-run3.md (run 2: importer design; run 1: first importer); logs/sessions/2026-10-08-PH-U-PROPS-run1.md | EditMode 514/514 and PlayMode `PhantomHandPresentationTests` 20/20 in the open editor (7f64e29 message); 11 review pictures in `logs/sessions/screens/ph/models`. Later counts: PROPS row. |
| E-HANDOFF | Electronics team handoff (their boards are flashed with `node_a_haptic` v0.5.0 and `node_b_bio` v0.5.0, per their §A; see FLAG 6) | not a prompt | electronics team | **handed to electronics team**: handoff received, Opus consistency check done (§B), **network untested** on their side (their §A table). The software follows their dialect since 8c88d0d (contracts v0.2.1), f9b7ae8 (SDK) and 9666dd8 (twin `--dialect team`); the real boards have never run with the game | 5c0b12d (old repo); 8c88d0d; f9b7ae8; 9666dd8 | docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md | §B: 13 topics, 5 requests. Their stated ports 8790/8791; cap 150; 50–400 ms pulse; 100 ms gap; 50 % duty per 10 s; 2 s watchdog (same doc, §B intro). No test counts. |
| U6 | APK, demo mode, performance | MVP | Sonnet | **partial**: code (9fd4e18): APK build method for Phantom Hand, `phantom_endpoints.json` override, Android multicast lock, Bootstrap offers only games in the build. Two APKs were built from it in the open editor on 8 Oct: a release APK (14:45 to 15:04), checked by content only, and a development APK (rebuilt 15:09 to 15:22), not checked (APK row). Neither was installed, and nothing was run on a headset. The lock and the file have never run on a headset, and no frame rate has been measured on one. Demo mode exists (`demo_mode`, D18) | 9fd4e18; 3fefd61 | logs/sessions/2026-10-08-PH-U-U6CODE-run1.md | EditMode 511/511 in the editor at 9fd4e18 (383 + 86 of this change set + 42 of the models code; commit message). |
| U7 | Agency phase | stretch (after G2) | Sonnet | **awaiting review** (H1 rule): built, opt-in (`agency_enabled`, `autonomous_close_enabled`) in 29b0c1d (D14): `AgencyRun`, once in the last condition, hand-tracking fallback on the index fingertip, q5 only after a real self-close. Never run with a real Node B, a person or a headset | 29b0c1d | logs/sessions/2026-10-08-PH-U-AUDIT-run1.md | `AgencyTests` (24 pure cases, AUDIT log) are part of EditMode 618/618 (29b0c1d). |
| AUDIT | Theme 5 audit applied in the game: witness regroup, Dissolve + Reveal (fallback), slow brush, rating after both conditions, stone volume (additions A2 and A5, D11-D20) | follow-up (not in the brief's list) | Sonnet (RESUME log, wave 4 ledger) | **awaiting review** (H1 rule): committed in 29b0c1d. `additionsEnabled` is now on by default; the self-touch phase is not planned (D13); the Reveal is the fallback, no passthrough exists; never on a headset | 29b0c1d | logs/sessions/2026-10-08-PH-U-AUDIT-run1.md | At 29b0c1d: EditMode 618/618; PlayMode, Phantom Hand filter, 25/26: the one red test was the full run's cue bar, 88-91 % against 95 % (commit message); its cause was found later (the PC's CPU throttle, U5 row). At 2629398: 689/689 and 26/26. |
| APK | First Quest development APK, built in the open editor (the build side of U6) | MVP (part of U6) | Opus (main session; wave 6 notes in the RESUME log) | **partial**: built, never installed, never run on a headset. No claim that it works (02-RULES 1.2: only a human run counts). Built on 8 Oct, 13:07 to 13:43 (36 min; the first Android build on this PC, including the switch of the editor's build target to Android, which is still the target); IL2CPP, ARM64, Vulkan. File `releases/game/0.1.0/chetna-phantom-hand-dev.apk` as built (git-ignored; the build script set it aside as `chetna-phantom-hand-dev.apk.prev` when the rebuild started, see the end of this cell), 100 393 577 bytes (95.7 MB); Unity build result Succeeded, 0 errors, 14 warnings. Checked by content only (aapt2 badging and a zip listing): package `com.DefaultCompany.OPUS`, versionName 1.0, target SDK 34, native code arm64-v8a with `libil2cpp.so`, `libOVRPlugin.so` and `libopenxr_loader.so`, permissions `com.oculus.permission.HAND_TRACKING` and `CHANGE_WIFI_MULTICAST_STATE`, feature `oculus.software.handtracking`, debuggable. Not done: no install (no Quest is attached to this PC, only the phone). The development build puts the editor bridge's address and token into the APK: do not share a `-dev.apk`. This first APK was built before the room props were merged (from 14:08), so it has the old room. The build script set it aside as `chetna-phantom-hand-dev.apk.prev` when the rebuild started (the build method sets an old APK aside before it builds). The rebuild with the room props made two files. Release APK: `releases/game/0.1.0/chetna-phantom-hand.apk` (git-ignored), 85 951 560 bytes (82.0 MB), built in the open editor 14:45 to 15:04 from commit 2629398, so it has the room props, the flinch-checked full run code and the ack thread fix; Unity build result Succeeded, 0 errors, 10 warnings. Checked by content only (aapt2 badging and a zip listing): package `com.DefaultCompany.OPUS`, versionName 1.0, target SDK 34, native code arm64-v8a with `libil2cpp.so`, `libunity.so`, `libOVRPlugin.so`, `libopenxr_loader.so` and `libInteractionSdk.so`, not debuggable, permissions `com.oculus.permission.HAND_TRACKING`, `INTERNET` and `CHANGE_WIFI_MULTICAST_STATE`, feature `oculus.software.handtracking`, no Meta XR Operator layer. Whether it carries the editor bridge's address and token was not checked. It was never installed and never run on a headset. This is the APK to install for the demo. Development APK: `chetna-phantom-hand-dev.apk`, 131 430 360 bytes (125.3 MB), rebuilt 15:09 to 15:22; Unity build result Succeeded, 0 errors, 9 warnings. Not checked by content. While it was building, a second session saved uncommitted edits to three runtime files (`PhMaterials.cs`, `VirtualArmRig.cs`, `ThreatDrop.cs`, saved 15:11:05), so this file may or may not contain them. It is not known to match a commit and is to be rebuilt once those edits are committed. No APK has been installed or run on a headset | 3fefd61 | none for the build itself; the facts are in the wave 6 notes of logs/sessions/2026-10-08-PH-O-RESUME-run1.md | No test applies to an APK. Unity build results: first development APK Succeeded, 0 errors, 14 warnings; release APK Succeeded, 0 errors, 10 warnings; rebuilt development APK Succeeded, 0 errors, 9 warnings. |
| PROPS | The team's seven GLB props in the room, and the room closed | follow-up of MODELS (not in the brief's list) | Sonnet builder (about 62 min, in a worktree, Unity not driven); merged, changed and run in the editor by Opus (wave 6 notes in the RESUME log) | **awaiting review** (H1 rule): committed in ce05e51. New editor code: `GlbReader.cs` (own reader for the part of binary glTF these files use, no glTF package), `PropFit.cs` (file to wrapper, pivot, size check), `PhantomModelImporter.Props.cs`; `DarkenController` also darkens self-lit parts, so the probe phases stay dark. Scene: window with a dusk glow on the left wall, pendant lamp over the table (shade bottom at 1.85 m) with a point light, plant, framed picture, singing bowl and tea cup on the table's far right corner; the brush is now `PH_Brush.glb` (19 cm, round); ceiling, right wall and back wall added, so the room is closed. Run in the editor and the pictures looked at; nothing seen or measured on a headset, frame rate with the second real-time light included. Some light and colour values are still first guesses (PROPS log section 8). The files carry no author or licence: `CREDITS.md` takes them to be the team's own models, to be confirmed (MANUAL_TODO). The four Unity Asset Store packs are still not imported (not downloaded on this PC; store licence, they must stay out of git) | ce05e51 (source models: 9a011a3) | logs/sessions/2026-10-08-PH-U-PROPS-run1.md (section 9 = what was run in the editor) | EditMode 689/689 (was 618: +71 = GlbReader 51, PropFit 14, DarkenController 6); PlayMode Phantom Hand 26/26. Budget (`PhantomHandSceneBuilder.SceneStats`): 38 draw entries and 55 017 triangles with the run-time wrappers, against the asset guide's 100 draw calls and 300 000 triangles (UI panels not counted). Pictures: `logs/sessions/screens/ph/models/` (`room_eye`, `room_eye_right`, `table_props`, `room`, ...). |
| FLINCH | The full run simulates a flinch and checks what the game measured; the result page says "Not asked in this round" | follow-up of U5 and N1 (not in the brief's list) | Opus (wave 6 notes in the RESUME log) | **awaiting review** (H1 rule): committed in 2629398. At each stone impact `PH_FullRun` sends the twin a `flinch` command (EMG burst x6 from +120 ms, IMU jolt at +150 ms) and asserts what the game itself measured: EMG peak at least 3x the resting level, an onset 60-900 ms after the impact, an IMU jolt, and a flinch on the witness screen for both conditions. Until then the full run only ever produced "no flinch", so the path node stream, session time, `ThreatResponseAnalyzer`, witness screen had never run. It is a path check, not an effect: the twin flinches at every impact in both conditions. Analytics: in a demo-mode session q3 (never asked) and q4 of the first condition (asked once, after the last) get the reasons `q3_not_asked` / `q4_not_asked` instead of `*_absent`; a q4 missing after the last condition stays `q4_absent`. App: the result page shows such a reason as "Not asked in this round" (Hindi string added, needs a native check). Ran in the editor against the twin; the real boards have never run with the game | 2629398 | logs/sessions/2026-10-08-PH-E2E-L3-run1.md (step 1); the commit message | The game measured EMG peak x5.97 and x6.02, onset 124.6 and 121.9 ms, IMU jolt 14.1 and 14.3 m/s2, witness "strong" for both conditions; node time and session time agree to a few ms. PlayMode Phantom Hand 26/26; EditMode 689/689; analytics `tests/test_embodiment.py` 34 passed; app `test/phantom/embodiment_report_test.dart` 28 passed (one new test). |
| U8 | Synthetic participant sweep | stretch | Sonnet | not started | — | none | none |
| F1 | Node A firmware v0.5.0 (`firmware/opus_sleeve/opus_sleeve.ino`) | not tagged in 00-README | Sonnet | **reference only — owned by the electronics team (user decision 2026-10-07)**. Uncompiled: no arduino-cli on the authoring machine. | ef387e5 | logs/sessions/2026-10-07-PH-F-F1-run1.md | none. Compile not run. The `sim/haptic` 26 passed in the log tests the Python fake, not the .ino. |
| F2 | Node B bio_node firmware v0.5.0 | not tagged in 00-README | Sonnet | **reference only — owned by the electronics team (user decision 2026-10-07)**. Partial Node B code, uncompiled. | ef387e5 (same commit as F1) | none (no F2 log) | none |
| S1 | Twin-lite: fake Node A + Node B (`sim/sleeve/twin.py`) | MVP | Sonnet | done (O2 PASS, one manager fix: `sensor_chunk.timestamp_ms` = first sample) | e053f7b | logs/sessions/2026-10-07-PH-S-S1-run1.md | `sim/sleeve` 38 passed; `sim/haptic` 26 passed (baseline kept). |
| S2 | E2E harness + laptop live plot (`tools/demo/`, `sim/live/phantom_replay.py`) | MVP | Sonnet | done (O2 PASS). Unity PlayMode path wired; run through the harness in batch mode it has **never run** (the editor stays open). On 8 Oct the Unity leg ran in two steps instead (E2E-L3 row). | 01551de | logs/sessions/2026-10-08-PH-S-S2-run1.md | `tools/demo` 105 passed; `sim/live` 45 passed; L3 `--sim --no-unity` smoke 7/7 (stroke timing rows use fixture stamps). |
| S2b | Fresh L3 fixtures, gating cue-delivery flag row, fixture freshness guard | follow-up of S2 (not in the brief's list) | Sonnet | done (O2 PASS) | d59d75c | logs/sessions/2026-10-08-PH-S-S2b-run1.md | `tools/demo/tests` 114 passed (baseline 105, +9); `sim/live` 45 passed. |
| SDKB | SDK change set for the real firmware dialect: `accepted` acks, `keepalive`, display `mode` + `text`, node match by `device_kind`, `hubPort`, UDP 10054, stroke reschedule on resume | follow-up of the §B check (not in the brief's list) | Sonnet (RESUME log) | **awaiting review** (H1 rule): merged by Opus with one review change (`keepalive` keeps going to a silent node). The headset still uses hub port 8787: U45FIX steps 10-11 (port plumbing, app pause) were skipped | f9b7ae8 | logs/sessions/2026-10-08-PH-U-SDKB-run1.md | EditMode 378/378 after the merge (343 + 35 new; RESUME log). |
| XMACHINE | Twin and harness across machines: twin on the LAN, team dialect (`--dialect team`), harness `--lan`, firewall report script, per-folder venvs, tool paths | follow-up (not in the brief's list) | Sonnet (RESUME log) | **awaiting review** (H1 rule): Opus re-ran three suites before the commit (RESUME log). A second physical PC has never been used | 9666dd8 | logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md | See "Latest test counts" below: `sim/sleeve` 94, `sim/live` 47 (+3 e2e), `tools/demo/tests` 121 passed + 8 skipped, L3 `--no-unity` 7/7 in plain, `--dialect team` and `--lan`. |
| STATION | `tools/demo/sleeve_station.py`: brush your own arm with the sleeve only, no headset (tier T3 of the runbook) | follow-up (R4; not in the brief's list) | not recorded in the log | **awaiting review** (H1 rule): green against the twin in both dialects; never run on a real board | a667def | logs/sessions/2026-10-08-PH-S-STATION-run1.md | 38 tests passed (a667def message). |
| E2E-L3 | Gate G2, L3 table with a recording made by the game: the first replay of a Unity run through the harness, analytics and the L3 checks, and the three mismatches it showed (03-SPEC D21-D23) | MVP (gate G2) | Opus (main session; wave 6 notes in the RESUME log) | **partial**: the 7 harness checks are green in simulation, on the stock fixture and on today's Unity recording (session a7c5d8c6), and the three fault runs are green on the stock fixture with the fake headset (14:41 to 14:46, `--sim --no-unity --faults`: Fault A, Node A off at 50 %, 5/5; Fault B, Node B absent, 3/3; Fault C, hub absent 30 s, 4/4; final line `PHANTOM HAND L3 GREEN`; the log's "Fault rows" section). So the G2 L3 table is green in simulation. The Unity leg ran in two steps because the editor stays open: the PlayMode full run, then its recording replayed with `--fixture`. Not done: Unity in batch mode through the harness has still never run; a faulted run of the game itself (Unity) has not been done, and the game's behaviour under those faults is covered by its EditMode and PlayMode tests only (the fault runs used the stock fixture, not the Unity recording); an earlier fault attempt (13:29 to 13:38) failed two rows while the APK was building on an overloaded, throttled PC and is not counted; the log records the default twin dialect only; nothing ran on a headset or on the real boards (L4, L5). Gate G2 itself (one recorded full run on a Quest with the sleeve) is open. The first replay (13:23, session d170f66d) gave 4/7, and no red row was a game defect: the phase check wanted one ProbePre and a `done` phase_start, but the game probes before every condition and ends with `block_end` / `session_end` (D21); the ASYNC check compared raw send stamps, but every cue leaves `tactile_lead_ms` early (D22); the acks were measured while the PC was overloaded by the APK build. Analytics on that recording showed the ownership item as `degraded` in every demo run: in demo mode q1 alone is the planned measure, so it is quality ok with the reason `single_item_demo_mode` now (D23) | 3fefd61; 2629398 | logs/sessions/2026-10-08-PH-E2E-L3-run1.md | Stock fixture, `--sim --no-unity`: 7/7. Unity recording (`--fixture`): 7/7, 14 phases; SYNC timing error mean 5.6 ms, p95 9.6 ms (n=25); felt ASYNC delay 509-692 ms, 100 % of 25 strokes in range; RTT p50 3.4 ms (Unity's own run: p50 4.4, p95 8.4 ms); 74/74 files uploaded; embodiment flags missing 7, ok 25, none degraded. Analytics 84 passed (3fefd61); `tools/demo/tests` 164 passed + 8 skipped (repo-root `.venv`). |
| S3 | Full twin + participant model | stretch (after G2) | Sonnet | not started | — | none | none |
| A1 | Live operator card with EMG + accel traces | MVP | Sonnet | done (O2 PASS) | aa2d58c | logs/sessions/2026-10-08-PH-A-A1-run2.md | `flutter test` +301 (baseline 207, +94 incl. 21 goldens); `flutter analyze` 0 errors, 0 warnings, 235 infos; hub test on port 8797: 5 passed. |
| A1b | Narrow screens: Controls above traces; debug APK on phone | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | 248cf23 | logs/sessions/2026-10-08-PH-A-A1b-run1.md | `flutter test` 301 passed; analyze 235 infos, 0 errors, 0 warnings. Debug APK built; installed and launched on serial 164cd676 (M2101K6P, 393 dp), driven by the builder via adb (see Flags). |
| A1c | Friendly EN/HI labels from manifest `x-ui.title` | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | 533e875 | logs/sessions/2026-10-08-PH-A-A1c-run1.md | `flutter test` +302 All passed; `contracts/validate.py` All PASS. Hindi titles machine-written, need native review (MANUAL_TODO). |
| A1d | Hub `GET /opus/v1/live/last_status` | follow-up of A1 (MVP) | Sonnet | done (O2 PASS) | a7c50bb | logs/sessions/2026-10-08-PH-A-A1d-run1.md | `flutter test` +303; hub_phantom_test +6 (port 8797); analyze 236 infos, 0 errors, 0 warnings. |
| SETUP | Flutter 3.47.4 at `H:\flutter` on the new machine: tests, Windows build, debug APK, operator app on the team phone | follow-up (new machine; not in the brief's list) | Sonnet (RESUME log) | **awaiting review** (H1 rule): all steps ran. The repo APK could not update the phone's old app (another PC's debug key), so a side-by-side build `com.opus.opus_app.pc` runs there: hub health, live card with phases, both chips and traces for a replayed session (screens in `logs/sessions/screens/ph/phone`). That build predates aec2aed | a667def (log and screens) | logs/sessions/2026-10-08-PH-A-SETUP-run1.md | `flutter test` +303; `flutter analyze` 0 errors, 0 warnings, 236 infos; Windows release build and debug APK built. |
| A-CARD, A-MIRROR, A-INTEG | Operator app after the R3 research: live card rework (B1-B8, B12), audience mirror of the headset's results (B11), embodiment report in the session report (B19); one integrator merged them | follow-up of A1; B11 and B19 are the A2 stretch below | Sonnet (the three logs) | **awaiting review** (H1 rule): committed in aec2aed. Not run on a phone or a Quest; Hindi strings are machine quality. 2629398 changed one file of it (`embodiment_report.dart`): a reason such as `q3_not_asked` shows as "Not asked in this round" (FLINCH row; the Hindi string needs a native check) | aec2aed; 2629398 | logs/sessions/2026-10-08-PH-A-INTEG-run1.md (and -CARD-, -MIRROR-) | `flutter test` +455 all passed; `flutter analyze` 0 errors, 0 warnings, 233 infos (aec2aed message; INTEG log, step 10). After 2629398 only `test/phantom/embodiment_report_test.dart` was run: 28 passed (one new test); the full `flutter test` and `flutter analyze` were not re-run. Open: the mirror shows "No data" when the card is opened after the witness event (INTEG log). |
| A2 | Embodiment report + witness mirror (FR-AP-02) | stretch (after G2) | Sonnet | built outside this prompt: see A-CARD, A-MIRROR, A-INTEG (aec2aed); Flutter tests only | aec2aed | logs/sessions/2026-10-08-PH-A-MIRROR-run1.md | see that row |
| N1 | Embodiment metrics (FR-AN-01) | MVP | Sonnet | done (O2 PASS) | 082ec6a | logs/sessions/2026-10-08-PH-N-N1-run1.md | analytics `76 passed` (baseline 51 + 25 new); `tests/test_embodiment.py` 25 passed at O2. |
| N1b | Embodiment delivery quality (cue_delivery_rate, touch_incomplete) | follow-up of N1 (MVP) | Sonnet | done (O2 PASS) | becdd04 | logs/sessions/2026-10-08-PH-N-N1b-run1.md | analytics `83 passed` (full); `tests/test_embodiment.py` 32 passed; `tools/demo/tests` 105 passed (at that time). |
| N2 | Validation vs synthetic truth | stretch (needs S3) | Sonnet | not started | — | none | none |
| H1 | Scribe after every run (log tidy, CHANGELOG, MANUAL_TODO) | MVP (every run) | Haiku | partial: this pass (2026-10-08) wrote CHANGELOG lines and the MANUAL_TODO PH section. Log headings were **not** reordered (no run log edited). | — | none (this pass) | none |
| H2 | Gate summary (G1, G2, G3) | not tagged in 00-README | Haiku | not started | — | none | none |
| H3 | CONTEXT.md "START HERE" refresh | not tagged in 00-README | Haiku | done for the 2026-10-08 Phantom Hand box (written on Opus's facts list; Opus reviews the diff); rewritten after aec2aed in 15 lines that point here, to the runbook and to the judge sheet, and again after 2629398 in 10 lines | — | none | none |
| H4 | Human runbooks (`docs/PH_ON_DEVICE_RUNBOOK.md`, checklist) | not tagged (needs U6 and L4/L5) | Haiku | **partial**: the runbook is written (a667def) and was updated after aec2aed and again after 2629398 (editor run, APK, late cues); most steps are tagged UNRUN or HUMAN; a separate checklist is not written | a667def | none | none |
| H5 | Demo fact sheet (`docs/PH_FACTS.md`) | not tagged (needs L4/L5) | Haiku | not started | — | none | none |

Notes on the table:
- O2 gate reviews (O2–O4 in `prompts/OPUS.md`) are recorded in the "O2 review" section at the end of each log.
- The electronics team's interface and bring-up guide: `docs/PH_ELECTRONICS_INTERFACE.md` (commit 88936b9, not a
  prompt run).
- Judge wording and what we never claim: `docs/PH_JUDGE_SHEET.md` (ab533e2, 808b4d3, 17ba2ee). Research notes R1-R4:
  `docs/agent-briefs/ph/research/` (R2 landed in c57e529, R3 in ab533e2, R4 in 98b5cde and 808b4d3, R1 in 17ba2ee).
- Rows marked "awaiting review" ran their tests, but no O2 PASS is written in a session log; only Opus sets PASS.

## Latest test counts (one line per suite)

### Latest on the new machine, after commit 2629398 (2026-10-08, 14:35 IST; run by the main session 14:28 to 14:41; rows marked "not re-run" are older)

| Suite | Count | Source |
|---|---|---|
| Unity EditMode (all) | 689/689 passed (was 618 at 29b0c1d; +71 = GlbReader 51, PropFit 14, DarkenController 6) | commit ce05e51 and 2629398 messages; logs/sessions/2026-10-08-PH-U-PROPS-run1.md, section 9 |
| Unity PlayMode, Phantom Hand filter (`PhantomHand` or `PH_`) | 26/26 passed. `PH_FullRun` in that run: 199 s, 14 phases, cues 114/114, 1 frame over 120 ms, RTT p50 4.4 / p95 8.4 ms. On a throttled PC the same filter reads 25 passed + `PH_FullRun` Inconclusive (3fefd61; U5 row) | commit 2629398 message; logs/sessions/2026-10-08-PH-E2E-L3-run1.md, step 1 |
| Flutter app `flutter test` | +455 All tests passed at aec2aed (last full run; not re-run in full since). After 2629398 only `test/phantom/embodiment_report_test.dart` was run: 28 passed (one new test) | commit aec2aed and 2629398 messages; logs/sessions/2026-10-08-PH-A-INTEG-run1.md, step 10 |
| Flutter `flutter analyze` | 0 errors, 0 warnings, 233 infos at aec2aed (not re-run) | commit aec2aed message; logs/sessions/2026-10-08-PH-A-INTEG-run1.md, step 10 |
| Analytics `pytest` | 84 passed (full suite, at 3fefd61, before the "not asked" change); `tests/test_embodiment.py` 34 passed at 2629398 (the full suite was not re-run after it) | commit 3fefd61 and 2629398 messages |
| `sim/haptic` (Python fake) | 26 passed (at 9666dd8; not re-run in wave 6) | logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md |
| `sim/sleeve` | 94 passed (at 9666dd8; not re-run in wave 6) | logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md; re-run by Opus before the commit (RESUME log) |
| `sim/live` | 47 passed, plus 3 e2e (at 9666dd8; not re-run in wave 6) | logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md |
| `tools/demo/tests` | 164 passed + 8 skipped, run with the repo-root `.venv` (the only venv with both aiohttp and scipy; `analytics/.venv` has no aiohttp, `sim/live/.venv` no scipy). Before: 121 passed, 8 skipped (110 + 11 in two venvs) at 9666dd8; `test_sleeve_station.py` adds 38 (a667def) | commit 3fefd61 message; earlier: logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md, commit a667def message |
| `contracts/validate.py` | all validations passed (wave 6; the count was not recorded). Before: 112 PASS, 0 FAIL at 9666dd8 | commit 3fefd61 message; earlier: logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md |
| L3 `--sim --no-unity` | Stock fixture: 7/7. Unity recording (`--fixture`, session a7c5d8c6, 14:36 to 14:41): 7/7. Fault runs (`--faults`, stock fixture, fake headset, 14:41 to 14:46): Fault A 5/5, Fault B 3/3, Fault C 4/4, final line `PHANTOM HAND L3 GREEN`; a faulted run of the game itself (Unity) has not been done. Earlier, stock fixture: plain, `--dialect team`, `--lan` 7/7 each at 9666dd8 | logs/sessions/2026-10-08-PH-E2E-L3-run1.md; earlier: logs/sessions/2026-10-08-PH-S-XMACHINE-run1.md |

The sim suites (`sim/haptic`, `sim/sleeve`, `sim/live`) were last run at 9666dd8; a667def re-ran only the sleeve station, `test_lan_mode`/`test_tool_paths` and `test_live_plot` tests (38, 15 and 18 passed; PH-S-STATION-run1). In wave 6 the main session ran EditMode, PlayMode, analytics, `tools/demo/tests`, `contracts/validate.py` and the L3 table (rows above). L3 with Unity: the Unity leg ran as the PlayMode full run in the open editor plus the replay of its recording; Unity in batch mode through the harness has never run.

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

1. **Install the development APK on a Quest 3 and run it (a person with a headset).** Nothing has ever run on a headset.
   No APK was installed, and this PC has no Quest attached. The release APK (`releases/game/0.1.0/chetna-phantom-hand.apk`, 82.0 MB,
   git-ignored; built 14:45 to 15:04 from 2629398, so it has the room props; checked by content only; APK row) is the APK to install
   for the demo. The development APK (`chetna-phantom-hand-dev.apk`, 125.3 MB, rebuilt 15:09 to 15:22) was not checked by content.
   Three runtime files (`PhMaterials.cs`, `VirtualArmRig.cs`, `ThreatDrop.cs`) were saved with uncommitted edits at 15:11:05 while it
   was building, so it may or may not contain them and is not known to match a commit: rebuild it once those edits are committed. A
   development APK carries the editor bridge's address and token, so do not share it. The first development APK (13:43, old room)
   was set aside as `chetna-phantom-hand-dev.apk.prev`. Steps and checks: `docs/PH_ON_DEVICE_RUNBOOK.md` 4.7-4.10. The first headset
   run also decides what has run in the editor
   only: multicast lock, endpoint file, the 3D models and the room props (frame rate with the second real-time light is unmeasured),
   the finale, the agency fallback, the slow brush.
2. **The CPU throttle on this PC (owner's call).** The late taps in the editor are explained (U5 row): this PC drops its CPU to about
   35 % of nominal speed for long stretches while the editor is in play mode. Why (heat or a power limit) is not determined, and no
   power or thermal setting was changed (MANUAL_TODO). Until then a throttled `PH_FullRun` ends Inconclusive by design.
3. **L3, what is still open (E2E-L3 row).** The fault runs are recorded in the L3 log ("Fault rows"), on the stock fixture with the
   fake headset: Fault A (Node A off at 50 %) 5/5, Fault B (Node B absent) 3/3, Fault C (hub absent 30 s) 4/4. A faulted run of the
   game itself (Unity) has not been done; the game's behaviour under those faults is covered by its EditMode and PlayMode tests only.
   Unity in batch mode through the harness (`python tools/demo/run_pipeline.py --game phantom_hand
   --sim` without `--no-unity`, editor closed) has never run. The L3 log records the default twin dialect only. L4 (real boards) and
   L5 (Quest) are untouched.
4. **Real passthrough for the Reveal needs a headset check (D10, D13).** Only the fallback exists (the arm glides onto the tracked
   hand); the game has no `OVRPassthroughLayer` code.
5. **Props: confirm they are the team's own models, then look at them on a headset.** The seven props (`PH_Brush`, `PH_PendantLamp`,
   `PH_Plant`, `PH_Window`, `PH_SingingBowl`, `PH_TeaCup`, `PH_FramedPicture`) are in the game since ce05e51 (PROPS row). The files
   carry no author or licence; `CREDITS.md` takes them to be the team's own models: confirm (MANUAL_TODO). Some light and colour
   values are first guesses (PROPS log, section 8).
6. **Credits and licences (public repo).** Four Unity Asset Store packs named in `CREDITS.md` (Pack Gesta Furniture #1, Stones,
   Dark Wave Paint Table 01, Mobile Books) are still not imported at 14:45 (after importing them in Unity, tell the session so they
   can be wired and git-ignored) and must stay out of git; the Meta asset-library terms
   (`game/Assets/MetaAssets/`) are unchecked; show the rigged hand's credit line at the demo (CC BY-SA 4.0).
7. **Electronics (humans).** The Wi-Fi/UDP test of the real boards with the game; the 5 requests (§B) and the four firmware questions;
   the bench run with the wearer for the slow brush; one supply per board. The team's firmware streams to the last sender only, so no
   second tool may talk to the nodes during a session (MANUAL_TODO, Phantom Hand human steps).
8. **Operator app on a device.** aec2aed (live card rework, audience results mirror, embodiment report) ran in Flutter tests only.
   Rebuild the Windows exe and the APK, look at the card on the phone (`com.opus.opus_app.pc` still runs the build before aec2aed)
   and at the mirror against a real `witness_summary`. Settle the fixture mismatch: `contracts/fixtures/sessions/phantom_hand_min/metrics.json`
   says drift 2.4 / 0.4, its own events say 2.0 / 0.0 (INTEG log; Flag 1 below). After 2629398 only the embodiment report test file was
   run (28 passed): run the full `flutter test`. The Hindi string "Not asked in this round" needs a native check (FLINCH row).
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
   - `997491` paint brush — `game/Assets/MetaAssets/Prefabs/997491/997491_L.fbx` (slot PH_Brush; since ce05e51 the slot is filled from `PH_Brush.glb` when that file exists, and this Meta brush is the fallback)
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
