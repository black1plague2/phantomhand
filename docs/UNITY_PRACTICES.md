# Unity + Meta Quest Practices (researched 2026-09-14, Opus)

Binding for Track U. Where this conflicts with older briefs, **this file wins**.
Evidence: Meta docs, Meta's official sample repos (checked via GitHub API today), Meta's package registry, and Unity/Meta forums.
Reddit/Quora are not readable by agents (blocked), so forum and practitioner threads were used instead.

## 1. Package stack (proven by Meta's own current sample)
`oculus-samples/Unity-InteractionSDK-Samples` (updated 2026-08-20, Unity 6000.0.66f2) uses exactly:
`com.meta.xr.sdk.interaction.ovr` (pulls Meta XR Core) + `com.unity.xr.openxr 1.16.1` + `com.unity.xr.management`.
Note: Meta package versions are **not** always in lockstep. Check each one on the registry (`https://npm.developer.oculus.com/<package>`) before pinning.
Core 205 depends on `com.unity.xr.hands 1.7.2` transitively; that's expected, but we still read hands through ISDK `IHand`.

**Use:**
| Package | Version | Why |
|---|---|---|
| `com.meta.xr.sdk.interaction.ovr` | **205.0.0** (registry latest, 2026-07-22); fall back to 203.0.0 (sample-proven) if 205 misbehaves on 6000.4 | ISDK + Core SDK (OVRManager, Building Blocks, Project Setup Tool) |
| `com.meta.xr.sdk.core` | same version as interaction | pinned explicitly so all Meta packages match |
| ~~`com.meta.xr.simulator`~~ | **do not add** | Deprecated since v83 (last UPM version 81.0.1, built for Unity 2022.3). The Meta XR Simulator is now a **standalone app**; the Core SDK sets it as the active OpenXR runtime (Meta > Meta XR Simulator). Adding `205.0.0` broke package resolution on 2026-09-14 |
| `com.unity.xr.openxr` | latest 1.16.x+ compatible with 6000.4 | Meta: Oculus XR Plugin is **deprecated**; OpenXR is the backend for new projects |
| `com.unity.nuget.newtonsoft-json`, `com.unity.test-framework`, URP | current | SDK/test needs |

**Do not use:** `com.meta.xr.sdk.all` (the old balloon project hit AAR namespace build conflicts with it), `com.unity.xr.oculus` (deprecated),
Unity XR Hands / XRI as the hand source (Meta's Unity-XR path loses Meta-only features), or mismatched Meta package versions.
`com.unity.xr.meta-openxr` is only needed for AR Foundation/MR features, which we don't use; add it only if the Project Setup Tool requires it.

## 2. Project configuration
- **XR Plug-in Management → Android:** OpenXR only. OpenXR features enabled **on the Android tab** (a common "works in editor, not in build" cause is enabling them on Standalone only), mirroring the official sample: **Meta XR Feature**, **Hand Tracking Subsystem**, **Meta Hand Tracking Aim**, **Hand Interaction Profile**, **Meta XR Foveation**. Add Oculus Touch Controller Profile only if the shell needs controllers.
- **Meta Project Setup Tool** (Meta > Tools > Project Setup Tool): fix every Required and Recommended item for Android; record leftovers in the log.
- **OculusProjectConfig / OVRManager "Quest Features":**
  - `handTrackingSupport` = **Hands Only**
  - `handTrackingFrequency` = **Low (Default)**. ⚠️ **Corrected 2026-09-15 from SDK 205 source:** `OVRProjectConfig.HandTrackingFrequency.HIGH` is labelled *"High (Fast Motion Mode)"*, and its tooltip says FMM *is* the former "High Frequency Hand Tracking", recommended only if fast motion causes tracking loss. Meta documents that FMM raises jitter, which is bad for slow, precise rehab reaches. The earlier "set High" rule came from older docs where they were separate; the Track U agent caught it.
    Keep `OVRManager.fastMotionModeHandPosesEnabled = false`. Treat frequency as an **experiment flag** (`OPUS_HT_FAST_MOTION` build define) to A/B on real hardware with the accuracy protocol (§6), not a default.
  - `handTrackingVersion` = **Latest / V2**
  - `targetDeviceTypes` = Quest 3, Quest 3S, Quest Pro (+ Quest 2 only if still offered)
  - passthrough, scene, anchors, body/face/eye tracking = **off** (pure VR; body tracking is a Phase 2 option for trunk compensation)
  - **Fast Motion Mode = OFF.** Meta states it raises jitter and is *not* recommended for precise, slow movements (rehab reaches)
- Android: ARM64, IL2CPP, Vulkan, min/target API as the Setup Tool dictates. URP with Meta's recommended quality settings, MSAA 4×, no HDR/post, fixed foveation on, **72 Hz** baseline, SpaceWarp off.
- Verify that the generated AndroidManifest contains the hand-tracking feature/permission (the OVR manifest tool, "Update AndroidManifest").

**Frequency verification (important):** forum threads report no reliable OpenXR-side way to confirm the hand-tracking frequency.
So **measure it**: count `IHand.CurrentDataVersion` increments per second at runtime. Forums report roughly 30 Hz in Low mode and 60 Hz in High/FMM; this is **unverified on our hardware**.
Write the measured value to `session.json → device.tracking_rate_hz`. Warn when it's below 80 % of the expected rate *for the configured mode* (not a fixed 55).
Analytics must use each chunk's `rate_hz` (never assume 72). At ~30 Hz the SPARC 10 Hz cutoff is still below Nyquist (15 Hz) but close to it; `opus_analytics` should flag `quality: degraded` for SPARC/LDLJ when `rate_hz < 45` until hardware validation says otherwise.

## 3. Rig: Building Blocks
- Order: **Camera Rig → Hand Tracking → Interaction rig (OVR comprehensive) → Hand Grab / Grab Interaction** on the fruit prefab. Delete the default Main Camera.
- Building Blocks are **editor-only GUI**; Meta explicitly says not to add the `BuildingBlock` component manually or at runtime. So:
  1. Apply the blocks once in the editor. Drive the menu/window through the Unity MCP (`reflection-method-call`/menu execution, or ISDK Quick Actions "Add Comprehensive Interaction Rig"). Save the scene and keep the `BuildingBlock` components.
  2. Add an **EditMode rig-validator test** asserting what must exist: OVRManager with the settings in §2, OVRCameraRig, both hand data sources, HandGrabInteractors L/R, poke/ray for UI, and no Main Camera duplicates. Treat this test as the reproducibility guarantee.
- UI canvases: ISDK PointableCanvas + poke/ray. (The old project lost a day to a missing GraphicRaycaster on the OVR interaction rig; the validator should check it.)

## 4. Accurate hand data capture (core of the product)
Use the ISDK `IHand` API (reference v74+):
- Attach the recorder to the **raw hand**: the `Hand` fed directly by `FromOVRHandDataSource`, **before** any `HandFilter`/smoothing modifier. Gameplay visuals may use a filtered hand; recorded data must not.
- Subscribe to `WhenHandUpdated`; record a frame **only when `CurrentDataVersion` changes**, so there are no duplicated frames and the timing is true.
- Poses: batch `GetJointPosesFromWrist` (× `Scale`) + `GetRootPose` (world), or `GetJointPose` per joint in world space. Then transform into calibration space (chest reference). Map `HandJointId` → contract joints (`l_wrist`=HandWristRoot, `*_index_tip`=HandIndexTip, `*_thumb_tip`=HandThumbTip, `*_palm`=HandPalm), with a table in code.
- Confidence → `conf`: `!IsConnected || !IsTrackedDataValid` → 0; `IsHighConfidence` → 1; else 0.5. Emit `tracking_lost`/`tracking_regained` on transitions. Store per-finger `GetFingerIsHighConfidence` and `Scale` in chunk extras.
- Grasp signals: `GetFingerPinchStrength(Index/Middle)` per frame plus HandGrabInteractor select/unselect → `grasp`/`release` events.
- Head: `IHmd` (`FromOVRHmdDataSource`) `GetRootPose` in the same update.
- Timestamp from one monotonic session clock sampled inside the update callback.

## 5. Interaction design
- Fruit: `HandGrabInteractable` with authored hand-grab poses (ISDK **Hand Pose recorder** tool). Pinch and palm rules follow `graspType`.
- Near-field only (reach targets within the calibrated envelope). Poke for shell UI, ray only for far panels.
- Follow patterns from **Unity-FirstHand** and **Unity-MoveFast** (hand-first UX, but an older SDK 68, so copy patterns, not configs). Use **Unity-NorthStar** for Unity 6 + hand tracking performance setup.

## 6. Testing without a headset
- **Meta XR Simulator** hand support is limited to 4 canned poses (aim/poke/pinch/grab via keys 1–4 or the mouse) plus session record/replay.
  → Use it for **flow/interaction smoke tests** (grab works, UI works, session completes), not for kinematics.
- **Kinematics tests:** `SyntheticHandDriver` implements an ISDK hand **data source/modifier** (`DataSource<HandDataAsset>` / `DataModifier<HandDataAsset>`) that injects joint poses from `sim/` synthetic sessions. Interactors, recorder, and game consume it exactly like real tracking. Test: replay a synthetic session → recorded output ≈ input (position error < 1 mm, no dropped/duplicated frames).
- Later, with hardware: validate accuracy against a reference, following the reaching protocol in `DiarKarim/MetaOculusQuestPerformance` (Quest vs motion capture, target reaching + joint bending).

## 7. Reference repos (read for patterns; respect the Oculus SDK license, don't bulk-copy assets)
| Repo | Use it for |
|---|---|
| [oculus-samples/Unity-InteractionSDK-Samples](https://github.com/oculus-samples/Unity-InteractionSDK-Samples) | **Canonical current config**: packages, OpenXR features, example scenes (HandGrab, Pose detection, ComprehensiveRig) |
| [oculus-samples/Unity-FirstHand](https://github.com/oculus-samples/Unity-FirstHand) | Hand-first interaction UX |
| [oculus-samples/Unity-MoveFast](https://github.com/oculus-samples/Unity-MoveFast) | Hand-tracked exercise/fitness flow |
| [oculus-samples/Unity-NorthStar](https://github.com/oculus-samples/Unity-NorthStar) | Unity 6 + ISDK + OpenXR + performance |
| [oculus-samples/Unity-PerformanceSettings](https://github.com/oculus-samples/Unity-PerformanceSettings) | CPU/GPU levels, dynamic resolution |
| [oculus-samples/Unity-Movement](https://github.com/oculus-samples/Unity-Movement) | Body tracking (Phase 2 trunk compensation) |
| [DiarKarim/MetaOculusQuestPerformance](https://github.com/DiarKarim/MetaOculusQuestPerformance) | Accuracy validation protocol vs mocap |

## Sources
[Hand tracking overview](https://developers.meta.com/horizon/documentation/unity/unity-handtracking-overview/) ·
[IHand API](https://developers.meta.com/horizon/reference/interaction/v74/interface_oculus_interaction_input_i_hand/) ·
[Hand Grab](https://developers.meta.com/horizon/documentation/unity/unity-isdk-hand-grab-interaction/) ·
[ISDK with Unity XR (tradeoffs)](https://developers.meta.com/horizon/documentation/unity/unity-isdk-getting-started-unityxr/) ·
[Unity & OpenXR compatibility](https://developers.meta.com/horizon/documentation/unity/unity-and-openxr-compatibility/) ·
[Fast Motion Mode](https://developers.meta.com/horizon/documentation/unity/fast-motion-mode/) ·
[Hands 2.2 latency](https://developers.meta.com/horizon/blog/hand-tracking-22-response-time-meta-quest-developers/) ·
[Building Blocks](https://developers.meta.com/horizon/documentation/unity/bb-overview/) ·
[BuildingBlock class](https://developers.meta.com/horizon/reference/unity/v85/class_meta_x_r_building_blocks_building_block/) ·
[XR Simulator hand tracking](https://developers.meta.com/horizon/documentation/unity/xrsim-hand-tracking/) ·
[Forum: frequency in OpenXR](https://discussions.unity.com/t/how-to-control-hand-tracking-mode-frequency-in-openxr-for-meta-devices/951530) ·
[Forum: hands not working in v78 builds](https://discussions.unity.com/t/hand-tracking-not-working-on-meta-quest-when-building-with-meta-xr-sdk-v78/1685157) ·
[Forum: high-frequency HT](https://communityforums.atmeta.com/discussions/dev-unity/how-do-i-enable-high-frequency-hand-tracking/869538)
