# Brief U: Unity SDK + Orchard Reach · model: Sonnet · sole Unity CLI owner

**Goal:** a standalone Quest app (`game/`) where a new game is a plug-in, with Orchard Reach as the first game, fully testable without a headset.

## Inputs
- `contracts/schemas/*`, `contracts/fixtures/orchard_reach.manifest.json`
- Reference only: `VR_games/balloon/Assets/Editor/AARNamespacePatcher.cs` (from garden_rehab), GhostHandGuide idea

## Tooling
- Unity CLI: `C:\Users\GARV BANSAL\AppData\Local\Unity\bin\unity.exe` (v1.0.0-beta.8, **not on PATH**; prepend the folder). Useful: `unity status`, `unity open <project>`, `unity command`/`unity list` (tools on a connected editor), `unity test <project>`, `unity build <project>`, `unity install-modules`.
- Editors installed include **6000.4.6f1** (use this one), plus 6000.3.24f1, 6000.6.0f1, and others.
- Unity MCP skills (assets-*, gameobject-*, scene-*, script-*, tests-run, console-get-logs) work once the editor is open with the MCP plugin.
- The Meta XR Simulator can be driven through the `meta-xr-operator` MCP tools.

> **Read `docs/UNITY_PRACTICES.md` first. It is binding and overrides this brief where they differ** (exact packages, OpenXR features, Fast Motion Mode off, raw-hand recording, simulator limits).

## Platform decision (user, 2026-09-14): overrides anything below
**Meta Quest only, pure VR, Meta XR SDK + Building Blocks, hand tracking first.**
- Stack: Meta XR Core SDK + **Meta XR Interaction SDK** (`com.meta.xr.sdk.core`, `com.meta.xr.sdk.interaction.ovr`; add Audio/Platform only if needed), OpenXR plugin with the **Meta XR feature group** (Meta's current recommended backend). Do **not** use Unity XR Hands/XRI as the hand source.
- Build the rig with **Building Blocks** (Meta > Tools > Building Blocks): Camera Rig, Hand Tracking, Interaction Rig (hand grab / pinch / poke), and Grab Interaction for the fruit. Apply them via the Building Blocks editor API/menu (script it in an Editor utility so it's reproducible). If a block can't be applied headless, do it once in the editor and commit the scene.
- Pure VR: no passthrough, no MR/scene/anchors, no 2D/desktop fallback. Target devices: Quest 3, 3S, Pro (and Quest 2 if the SDK allows). Android / ARM64 / IL2CPP / Vulkan, min API per the Meta SDK.
- Maximum hand-tracking accuracy (set and verify in OVRManager/OpenXR settings):
  - Hand Tracking Support = **Hands Only** (or Controllers and Hands if the shell needs controllers; justify)
  - **Hand Tracking Frequency = High**, Hand Tracking Version = **Latest (v2+)**, Tracking Origin = Floor Level, seated recenter handled in calibration
  - enable the **Hand Skeleton** at the full joint set; read joints from `OVRSkeleton` / Interaction SDK `IHand` (`GetJointPose`) in the same space every frame
  - confidence: `OVRHand.IsTracked`, `IsDataHighConfidence`, `HandConfidence`, per-finger confidence → `conf` column (0 when not tracked, 0.5 low, 1 high); log `tracking_lost/regained` events
  - sample in the `OVRManager` input-update path (not a coroutine), timestamp with the display/predicted time, record at the device's hand-tracking rate (don't downsample); keep the Interaction SDK's filtering off for the **recorded** data (raw joints), but it's fine for gameplay visuals
  - grasp detection through Interaction SDK HandGrab/pinch with tuned thresholds; log pinch strength
  - lock 72/90 Hz with fixed foveation; keep frame time headroom so hand latency stays low
- Simulation: Meta XR Simulator (it supports hands). Where simulator hands are too limited, the `SyntheticHandDriver` (U5) feeds the same `IHand` abstraction, so the recorder and game run unchanged.
- The kinematics joint mapping (`l_wrist`, `r_index_tip`, …) maps from Meta hand joint IDs, documented in code.

## Tasks
- **U1** The Unity **6000.4.6f1** project already exists at `OPUS/game` (created by the user, moved from the root on 2026-09-14; Assets is empty, and Library rebuilds on first open). Open it with `unity open game` or via the Unity CLI and configure it: URP 3D. Packages: OpenXR, Meta XR Core SDK, XR Hands, XR Interaction Toolkit (hands), Newtonsoft JSON, Addressables, Test Framework, Meta XR Simulator. Android/Quest build settings. Unity `.gitignore`. Verify: an empty scene enters Play mode in XR Simulator.
- **U2** Embedded package `game/Packages/com.opus.sdk`:
  - `IGameModule`, `GameManifest` (loads manifest.json), `ParamBinder` (Newtonsoft + schema defaults + range validation), `GameRegistry` (discovers modules via attribute, no hand edits)
  - `SessionClock` (monotonic ms), `TrialRecorder` → `events.ndjson`, `KinematicsRecorder` → `kin_###.json` 5 s chunks (joints per schema, conf from XR Hands tracking state)
  - `SessionWriter` → `session.json`; `Outbox` (session dir under `Application.persistentDataPath/sessions`), `ITransport` + `MockTransport`
  - EditMode tests: param binding/defaults/invalid, event ordering, chunk rollover, crash-resume of outbox
- **U3** `Shell` scene: mock pairing screen (loads `program` fixture), calibration (arm-length + chest reference, with an editor stub), block runner, rest screen, results summary, patient-reported pain/fatigue.
- **U4** `Assets/Games/OrchardReach`: target placement in normalized workspace (azimuth/elevation/reach%), grasp detection (pinch/whole-hand), basket placement, distractors, neglect bias, 2-down/1-up adaptive, trunk-lean warning, ghost guide. PlayMode tests for placement math and trial state machine.
- **U5** `SyntheticHandDriver`: replays `sim/` generated kinematics into the hand rig in Editor/Simulator so a full session runs hands-free. PlayMode test: replay a fixture → session dir validates against the schemas (call `contracts/validate.py`).
- **U6** XR Simulator smoke run (use `meta-xr-operator` MCP for head/controller poses and screenshots). Save screenshots to `logs/sessions/`. Tag `sdk-v0.1.0` and `game-orchard-v0.1.0`.

## Done when
A hands-free simulated session produces a schema-valid session directory, all EditMode/PlayMode tests pass, and an Android APK builds.
