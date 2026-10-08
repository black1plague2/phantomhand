# Track U: next-run milestones (living brief; resume from the first unfinished R)

Prior logs: `logs/sessions/2026-09-14-U-unity-run2.md` (run3 died before writing a log). Binding: `docs/UNITY_PRACTICES.md`, `docs/MODEL_REQUESTS.md` ("Imported asset review"), `contracts/LIVE_PROTOCOL.md`.

## Verified state (Opus, 2026-09-15)
- Project `OPUS/game`, Unity 6000.4.6f1. Compile clean. EditMode: 32 SDK + 30 Orchard tests pass. Replay session validates. Headset `LiveClient` + 8 tests exist (not wired). OculusProjectConfig HandsOnly. Pre-release `com.unity.ai.*` packages removed.
- Hand-tracking frequency: **LOW**, FMM off; A/B flag `OPUS_HT_FAST_MOTION` (SDK 205 HIGH == Fast Motion Mode).
- User-supplied models in `Assets/MetaAssets` (Standard shader → convert to URP; normalized ~2 units; tree is Z-up; hand not rigged).

## Editor-stability rules
Batch file writes → one refresh → wait for compile to finish (poll ≥ 30 s). No tool calls during "Reloading Domain". Save scenes/assets after every editor step. If a reload is > 10 min with a silent log → MANUAL_TODO, then continue with file-level work. The bridge needs auth: use the MCP tools/skills, not raw HTTP.

## Run 11 status (Opus, 2026-09-19) — read before picking an R
R4's **trunk-lean vignette + audio is DONE** (`Assets/Shell/Runtime/TrunkLeanVignette.cs`), and R5's contact
geometry now passes end to end (apple inside the basket bounds, release 1.9 cm from centre). EditMode 107/107,
PlayMode 6/6. Remaining in R4: the rest of the shell flow (pairing, calibration, block runner, rest, results,
pain/fatigue) — none of that is built. Remaining in R5: re-shoot the full state series now that the vignette
exists; only the during-lean shot has been retaken. R6 live link passes against `fake_hub.py`; it has **not**
been run against the Flutter hub CLI, which is the integration demo the project still needs. R7 unchanged
(Gradle loopback, human-only).

Two traps this track has now hit twice, both documented in `logs/sessions/2026-09-18-U-unity-run11.md`:
- **A green PlayMode run can be luck.** The with-sleeve haptic test passed in run 10 and produced zero cues in
  run 11, purely on UDP discovery timing. If a test depends on a network peer being discovered, make it wait
  for readiness explicitly.
- **Sampling a value one frame after an event can measure the next trial.** The basket "10.28 cm near-miss"
  chased for two runs was the audit test reading the apple after the next trial had already moved it.

## Milestones
- **R1 Practices remainder:** Setup Tool Required/Recommended; Android ARM64/IL2CPP/Vulkan; URP asset for Quest (MSAA 4×, no HDR/post); foveation; 72 Hz; frequency LOW + FMM off + `OPUS_HT_FAST_MOTION` define; TrackingRateMeter warns per mode (< 80 % expected).
- **R2 Rig via Building Blocks** in `Assets/Scenes/OrchardReach.unity` (Camera Rig → Hand Tracking → Interaction rig → Grab on fruit; no Main Camera). EditMode `RigValidatorTest` (OVRManager settings, rig, hand sources, HandGrabInteractors L/R, poke/ray + PointableCanvas, single camera, MetaHandSource on the RAW hand).
- **R3 Art from user models:** Editor utility `Tools/OPUS/Import Meta Assets`: URP Lit copies under `Assets/Art/Materials/URP`, texture Android ASTC 6×6 (512 fruit / 1024 others), prefab wrappers `Assets/Art/Prefabs/<slot>` with `ModelSlot` root at the correct pivot and child scale/rotation per MODEL_REQUESTS, LODGroups (M/S), colliders (apple sphere, table-top box, basket trigger). Ghost guide = ISDK rigged hand + translucent material; `286115` = static hint only. Environment: seated orchard corner, ground + grass tint, gradient skybox, warm directional + ambient/reflection probe, baked/mixed lighting, light fog, < 100 draw calls, static batching.
- **R4 Shell + flow:** pairing (UDP-discovered hubs list, manual host, code), calibration (+ editor stub), block runner → Orchard, rest, results, pain/fatigue. ISDK world-space canvases, legible text, sentence case. Grasp/place chime + particles. Trunk-lean vignette.
- **R5 Play-mode visual audit:** enter Play, drive with SyntheticHandDriver (and a simulator smoke run if activatable). Screenshots at pairing, calibration, target shown, mid-reach, grasp, place, results → `logs/sessions/screens/unity/`. Read each one, list issues (scale: apple body ≈ 7 cm vs fingertips, basket handle vs placement, table height seated, pink materials, z-fighting, UI legibility, clipping), fix, reshoot (before/after pairs). Frame stats + draw calls.
- **R6 Live link:** wire LiveClient into the shell; Android manifest permissions + cleartext LAN NSC; send `file_available` before each PUT. Play-mode integration against `sim/live/fake_hub.py --scenario basic` (sim/live/README.md; free port) → hub stored session passes `contracts/validate.py --session`. Record hub latency p50/p95 and invalid-message count (must be 0). Then against the **Flutter hub CLI** (`app/tool/hub_cli.dart --auto-drive`) if available.
- **R7 Android build:** from the open editor (menu/MCP) → `releases/game/0.2.0/opus-game.apk`; on failure, record the exact error in MANUAL_TODO.

After each R: compile clean + EditMode green + replay session validates. CHECKPOINT to `logs/sessions/<date>-U-unity-run<N>.md`.

## Root cause of the repeated editor hangs (Opus, 2026-09-17, CONFIRMED)
Four editor sessions hung in domain reloads. Every hang log ended at `Meta.MCPBridge.Editor.HttpMcpServer:Stop()` (the **Meta AI Agent Bridge** in com.meta.xr.sdk.core 205, hooked on `beforeAssemblyReload`), sometimes with `hubIPCService` write timeouts when Unity Hub wasn't running.
Test: EditorPref `Meta.XR.SDK.AI Agent Bridge.Enabled` = 0 plus Unity Hub running → the project opened in 40 s, reloads 1.2 s / 8 s, no errors.
**Rules now:** keep the Meta AI Agent Bridge **disabled**; keep Unity Hub running; drive Unity in **batch mode with the GUI editor closed** (a GUI editor locks the project):
`"C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe" -batchmode -projectPath "<OPUS>\game" -executeMethod <Class.Method> -logFile <path> -quit`
Tests: `-runTests -testPlatform EditMode|PlayMode -testResults <xml>` (no -quit). Screenshots: render the camera to a RenderTexture → PNG (no `-nographics`). After every run, check the exit code and grep the log for `error CS`.

