# PH-U-MODELS run 2 (2026-10-08): the user's real models into the Phantom Hand scene (rigged hand, model table, builder hooks, model shots)

Worktree `H:\Chenta\phantomhand\.claude\worktrees\agent-aeec594e4e861c042`, branch `worktree-agent-aeec594e4e861c042`, base 4a1c93e. **Nothing was run in Unity** (the one open editor belongs to the Unity driver, who bakes after Opus merges this patch). The result is an uncommitted working-tree diff. Every statement below is tagged **[V]** verified in this session (file read / Python measurement / compile / test run), **[I]** inference, or **[N]** written, never run.

## 0. Status

| # | Deliverable | Status | Evidence |
|---|---|---|---|
| 1 | Rigged hand into the arm (worklist C, every row) | done as code, **bake NOT run** | compiled [V]; orientation / scale / curl maths run on the FBX numbers [V]; importer + presenter paths [N] |
| 2 | Builder hooks B4 / B5, table test for both tables | done as code, **NOT run** | `Shell.Editor` compiled [V]; test body [N] |
| 3 | The other models: role or reason | done (table in section 6) | all 15 ids looked at, rendered from their FBX + texture [V] |
| 4 | Tests (EditMode) | written; 27 pure tests **run and green** standalone, 15 more [N] | section 7 |
| 5 | `PhantomHandSceneBuilder.CaptureModelShots()` | done as code, **NOT run** | compiled [V] |

## 1. Goal and boundaries

Put the user's models into the scene: the rigged skin hand replaces the procedural / glove hand, the wrappers get baked from the scene builder, the table model replaces the box table. File ownership as in the brief (Editor/**, Runtime/Presentation/** except Ui/ and StrokeDriver.cs, Tests/**, the builder's hook lines + its asmdef reference + one capture method). No Unity, no MCP, no commit, no new .meta, `DevAgentSettings.asset` untouched. The rigged hand's licence is unknown: it was read in place only (no copy outside the repo, no upload; the Python renders used for checking were deleted at the end).

## 2. Plan (step -> verify)

1. Read the worklist C rows and verify each against the worktree -> every line number moved; table in section 3.9.
2. Parse `handRig_02.fbx` myself (binary FBX 7400 reader, Python stdlib + numpy) -> axes, scale, bones, poses, skin weights, registration (section 3).
3. Pure maths first (`PhHandFit`) -> verify: run it on the measured bind pose in four plausible import spaces (standalone, real NUnit) -> 27/27.
4. Presenter side (`VirtualArmRig`, `PhRiggedHand`, `PhModelInfo`) -> verify: standalone compile; Python simulation of the curl on the test hierarchy.
5. Importer (`BuildRiggedHand`, partial class) + builder hooks + capture -> verify: standalone compile of all five assemblies.
6. Tests -> pure ones run standalone; Unity-object ones compile.

## 3. The FBX, measured (not assumed)

### 3.1 How
A ~190-line binary-FBX reader (node record = EndOffset/NumProperties/PropertyListLen u32, NameLen u8; typed properties, zlib arrays) in the session scratchpad (not shipped). Objects, Connections, Properties70 (Lcl TRS), Deformer Skin/Cluster (Indexes, Weights, Transform, TransformLink), Geometry (Vertices, PolygonVertexIndex, UV). Bone positions below are `TransformLink x 0.01` (the file unit is cm: `UnitScaleFactor 1`, Armature scale 100 = Blender metres).

### 3.2 Facts [V]
- Binary FBX 7400, 1,559,820 bytes. Axes in GlobalSettings: Up +Y, Front +Z, Coord +X (a right-handed Y-up file; Unity is left-handed, so the importer must reflect it).
- 72 Model nodes: 68 LimbNode + Mesh `handSmooth.001` + Null `Armature` (Lcl rotation -90, 0, 0, scale 100) + Camera + Light. Mesh: 14,499 verts, 28,994 tris (14,494 quads + 2 pentagons), 1 material, UV in 0.01..0.98, per-corner UV / normal / colour layers.
- Hierarchy: `Armature` -> `pulse.R` (root bone) -> `hand.R` (wrist) -> `hand.R.001` -> `<finger>_base.R` -> `_01` .. `_03` -> `_03.R_end`; `thumb_base.R` hangs off `hand.R`; a parallel `<finger>_Ctrl.R`, `_Ctrl_01.._03` chain sits beside each `_01` chain; `<finger>_tip.R` (IK targets) hang off `Armature`.
- **Skin weights: 48 clusters but only 22 bones carry vertices**: `pulse.R`, `hand.R`, `<finger>_base/_01/_02/_03` (16) and `thumb_base/_01/_02/_03` (4). The other 26 clusters (`hand.R.001`, 20 `_Ctrl*`, 5 `_tip`) have **0 vertices**. So the "two skinned chains per finger" of the worklist is only a cluster listing: driving `_base/_01/_02/_03` is enough, the `_Ctrl` chain is decoration. (Worklist C said both chains were skinned: corrected.)
- **Two poses.** The node pose (Properties70, what Unity most likely instantiates [I]) is a **clenched fist** (e.g. `index_02.R` node (-36.0, 97.4, 35.4) cm vs bind (-1.1, 112.6, 40.9) cm); the skin is bound to a flat **open hand** (cluster `TransformLink`). 442 animation curves, 3.2 s. Node pose != bind pose for every finger bone, so the importer restores the bind pose (section 4).
- **Registration trap:** `cluster.Transform` is NOT the mesh bind matrix. For all 48 clusters `TransformLink_i * Transform_i` is the same matrix (spread 2.8e-5) and equals the mesh node's world matrix (scale 100). Using the first cluster's `Transform` as the mesh matrix misplaced the skin by ~0.17 m against the bones (found and fixed during this run; every number below uses `Wm * v * 0.01`).
- Size: bind-pose mesh bbox (file world, m) x -0.262..0.155, y 0.095..1.576, z -0.280..0.739; wrist (`hand.R`) to the middle fingertip = **1.4215 m** (the hand is modelled ~7.5x life size). Scale for 19 cm: **0.13366**.
- Bind-pose bones (file world, m): `hand.R` (0.0000, 0.1733, 0.0579), `index_01` (-0.0415, 0.8016, 0.3340), `middle_01` (-0.0565, 0.8286, 0.1738), `ring_01` (-0.0768, 0.8100, 0.0196), `pinky_01` (-0.0977, 0.7164, -0.1272), `thumb_01` (-0.1697, 0.4927, 0.3530), `thumb_end` (-0.2262, 0.8544, 0.7204), `middle_03.R_end` (-0.1534, 1.5687, 0.2501); fingertip vertex (-0.1377, 1.5753, 0.2487).

### 3.3 Orientation, derived [V]
In the file frame (right-handed): fingers +Y, thumb +Z (pinky z -0.127 < ring 0.020 < middle 0.174 < index 0.334 < thumb 0.35..0.72), palm side **-X** (the fist curls toward -X; rendering the skin with `hand_Co` from -X shows palm creases and no nails, from +X nails and knuckles). Right-hand rule in a right-handed frame: palm normal = thumb x forward = (0,0,1) x (0,1,0) = (-1,0,0): matches -> a **right hand**, no mirroring needed.
In Unity's left-handed numbers the same hand has palm normal = cross(forward, thumb). The importer therefore measures forward = wrist -> middle fingertip, thumb = pinky knuckle -> index knuckle (orthogonalised), palm = cross(forward, thumb), and maps (thumb, dorsal, forward) to (-x, +y, +z). This holds for **any** reflection Unity applies at import (x flip, z flip, node rotations): the pure tests re-create four such spaces from the file data and require the same wrapper-space result from all (section 7).
Independent palm-side check at bake time: the rig's thumb is opposed ~2.2 cm toward the palm (mean thumb bone y -0.023 vs knuckles -0.001 in wrapper space); a mirrored model would show it on the dorsal side and the importer logs a WARNING.

### 3.4 What the wrapper looks like after the fit (Python, same maths as `PhHandFit`) [V]
Wrapper space = arm-local: wrist `hand.R` at the origin, +z fingers, +y dorsal (palm down), thumb -x, wrist at P (arm axis height at the wrist, table plane y = -0.021).

| quantity | value |
|---|---|
| scale | 0.133658 (file m -> wrapper m) |
| bbox (wrapper m) | x -0.0757..0.0627, y -0.0354..0.0223, z -0.0120..0.1900 (13.8 x 5.8 x 20.2 cm incl. the 1.2 cm of wrist before the origin) |
| wrist band (3 cm from the cut) | width 6.45 cm (ArmGeometry wrist 5.6 cm; sleeve cuff 6.1 cm), underside y -0.0214, top y +0.0210, centre x +0.0007 -> correction dx -0.0007, dy +0.0009 (the wrist bone already sits at the arm axis height) |
| palm length (wrist -> middle knuckle) | 0.0892 m (old constant 0.098) |
| palm width (knuckle-to-knuckle + 1.8 cm) | 0.0783 m (old 0.092); palm centre x +0.0041 |
| palmTopY / fingerTopY | +0.0188 / +0.0112 (old constants 0.025 / 0.021); finger box length 0.0888 |
| thumb box | centre (-0.0514, 0.0771), size (0.0613, 0.0750) |
| knuckles (wrapper) | index (-0.0254, -0.0009, 0.0883), middle (-0.0037, 0.0005, 0.0892), ring (0.0167, 0.0005, 0.0842), pinky (0.0349, -0.0009, 0.0695) |
| thumb bones | `thumb_01` (-0.0301, -0.0229, 0.0496), `thumb_end` (-0.0714, -0.0317, 0.1046): the thumb dips 1.5-2.5 cm below the knuckle plane (see open issues) |
| fingertips | middle end bone (0.0000, -0.0022, 0.1894): the middle finger lies on the z axis |
| curl 1 (schedule below) | middle tip dy -2.9 cm, dz -12.5 cm; index dy -4.1 / dz -11.0; ring -2.5 / -11.3; pinky -4.0 / -7.8 |
Hand underside: wrist ring bottom on the table plane (levelled to y -0.0205), palm heel and thumb 1 cm / 1.5 cm below it (hidden under the tabletop), fingers 0.6-1.2 cm above it (flat open pose with a slight arch). The dorsal profile continues the forearm top (0.021 at the wrist -> 0.019 palm -> 0.011 fingers): chosen on purpose, lifting the palm heel to the table would put a 1.1 cm step at the wrist.

### 3.5 Textures [V]
`hand_Co.jpg` 2048^2 albedo (average colour at the mesh UVs (0.579, 0.413, 0.334): a mid-brown skin, darker than the flat PH_Skin (0.74, 0.58, 0.48)), `hand_No.png` 2048^2 tangent-space normal map (mean 128, 128, 246) imported as a plain texture (`textureType 0`, sRGB on): the importer forces Normal-map type; `hand_Ro` / `hand_Sp` unused (constant smoothness 0.25).

### 3.6 Curl schedule (decision)
Bones driven: `{index,middle,ring,pinky}_01/_02/_03` by 72 / 79 / 51 degrees (the old procedural hand's 82/95/60 closed minus its relaxed 10/16/9) and `thumb_01/_02/_03` by 15 / 35 / 30, about the axis `cross(boneDirection, palmNormal)` (a positive angle turns the bone toward the palm), per bone from the rest pose. `_base`, `hand.R*`, `pulse.R`, `_Ctrl*`, `_tip*` stay put. [V] maths (Python simulation and tests); the thumb numbers are a guess [I].

### 3.7 Poses: why `RestoreBindPose`
Unity renders `sum(w * bone.localToWorld * bindpose) * v`, `bindpose_i = bone_i.worldToLocal * renderer.localToWorld` at bind. So `bone_i.localToWorld = renderer.localToWorld * bindpose_i^-1` reproduces the bind pose for any renderer matrix. The importer sets every skinned bone this way (parents first) and logs how far it moved them (0 cm = Unity had already instantiated the bind pose, the likely outcome is ~tens of cm because the node pose is a fist) [I: not observed].

### 3.8 Unit
If Unity imports in cm the measured wrist -> tip length is 142.15 instead of 1.4215 and the scale 0.001337 instead of 0.13366: harmless, the fit divides by the measured length.

### 3.9 Worklist section C, row by row (verified against this worktree)
| Row | Worklist said | Now |
|---|---|---|
| FBX import | meta importCameras 1, importLights 1, importAnimation 1 | still 1/1/1 in the .meta (not edited: hand-edited .meta avoided; the importer forces `importCameras=0, importLights=0, importAnimation=0, isReadable=1` with `SaveAndReimport`; the rig type stays Generic and the Animator it adds is removed from the wrapper) |
| Importer | BuildHand :353-430 mirrors the glove | unchanged glove path kept as fallback; new `PhantomModelImporter.RiggedHand.cs` (partial class) |
| Mirror, axis, scale | "my FK puts the fingers along +Z => +Y": re-orient | fingers are +Y in the file's world (not Armature +Z): measured, fitted, no mirror |
| VirtualArmRig.AddModel | :164-177 MeshRenderer only | Renderer-generic `RegisterModel` |
| BuildHandModel | :179-184 passes materials.skin | passes null for a textured hand (`PhModelInfo.texturedSkin`); the forearm uses its tone-matched wrapper skin |
| ApplyCurl / PoseHash | :328-341, :362-373 | bone driver `PhRiggedHand`; squash kept when the rig is missing / has < 12 bones; PoseHash includes the bones |
| Hit proxy / palm top | BuildHitProxy :235-259 uses consts | palm length / width / centre / finger length / thumb box and palmTop come from `PhModelInfo` (glove defaults = old constants) |
| Left hand / licence | none for MVP | unchanged; licence unknown, nothing copied |

## 4. Deliverable 1: the rigged hand

Files:
- `Runtime/Presentation/PhHandFit.cs` (new): `HandFrame`, `TryFrame`, `PalmSideWitness`, `FarthestAlong`, `Percentile`, `Wrist(...)`, `LevelShiftY`, `CenterShiftX`, `TopY`, bone names, curl schedule, `FlexionAxis`. Pure (Vector3 / Mathf only).
- `Runtime/Presentation/PhRiggedHand.cs` (new): `Bind()` finds the 15 driven bones by name, snapshots rest rotations, derives each axis in its parent's frame from the rest pose and the wrapper's local down; `SetCurl`, `PoseHash`. `MinJoints = 12`.
- `Runtime/Presentation/PhModelInfo.cs` (new, moved out of PhMaterials.cs): `PhModelInfo` gains `rigged, texturedSkin, skinTone, palmWidthM, palmCenterX, fingerLenM, thumbCenterXZ, thumbSizeXZ` (defaults = the glove's constants). **Latent bug of run 1 fixed:** Unity can only serialize a MonoBehaviour into a prefab when its file is named after the class; `PhModelInfo` sat in `PhMaterials.cs`, so every baked wrapper (forearm, sleeve, hand) would have lost it as a missing script and `GetComponent<PhModelInfo>()` would have returned null at runtime (silently: no hit-proxy measurements, flat skin override on the textured hand). The class was moved, not changed.
- `Runtime/Presentation/PhMaterials.cs`: `PhModels.All`, `PhModels.HandSource` + `ChooseHandSource` (and the `PhModelInfo` class removed).
- `Runtime/Presentation/VirtualArmRig.cs`: `RegisterModel` (any `Renderer`: material override, shadows off, alpha / A2 fade registration), `SetHandModel` (info, rig bind, hit proxy rebuild), no flat override for a textured hand, forearm = wrapper's tone-matched skin, `ApplyCurl` bones first / squash fallback, `PoseHash` with bones, hit proxy and `PalmTopWorld` from the measured info.
- `Editor/PhantomModelImporter.cs` (`partial`): `Build` calls `BuildHandWrapper`; `EnsureWrappers` also rebuilds when the FBX exists but the baked PH_Hand is not rigged; table material smoothness 0.12 -> 0.08 (the matte test needs <= 0.1); `SkinMaterial` takes the shared tone.
- `Editor/PhantomModelImporter.RiggedHand.cs` (new): `BuildRiggedHand` and helpers.

`BuildRiggedHand` step by step (what the log prints, in order):
1. `[hand] source: Rigged|Glove|None` (`PhModels.ChooseHandSource(fbxPresent, glovePresent)`); a failed rigged bake logs the reason and falls back to the glove `BuildHand` (324213).
2. FBX import forced: `importCameras=0, importLights=0, importAnimation=0, isReadable=1` (rig type left Generic, logged); `hand_No.png` -> Normal map; both textures Android ASTC (6x6 albedo, 5x5 normal) @ 1024.
3. Plain clone of the FBX under `PH_Hand/RiggedHand`, removal of any Camera / Light (count logged, expected 0) and of the Animator the Generic rig adds (count logged, expected 1), SMR renamed `Hand`, its stats logged (verts, tris, bones, transforms, lossyScale).
4. `RestoreBindPose` (section 3.7), log: bones set, max move in cm, rotated count.
5. Measure in import space: wrist `hand.R`, middle end bone, farthest skin vertex along wrist -> middle end (BakeMesh; its scale convention is cross-checked against the bones and rescaled with a WARNING if it is off by > 3 %), index / pinky knuckles. `PhHandFit.TryFrame` -> forward, thumb, palm, scale; log incl. the palm-side cross-check.
6. Container rotation = `Inverse(LookRotation(forward, dorsal))`, scale, position so the wrist is the pivot; wrist band centred (dx) and levelled (dy) with the forearm; log.
7. Measured into `PhModelInfo` (palm length / width / centre, palmTopY, fingerTopY, finger length, thumb box, skin tone), final bounds logged with a WARNING when the fingertip is not at z = 0.19 or the thumb is not on -x.
8. `PHM_HandSkin.mat` (URP Lit, `_BaseMap` hand_Co, `_BumpMap` hand_No + `_NORMALMAP`, smoothness 0.25), SMR: shadows off, `quality = Bone4` (the project's Android quality level skins with 2 bones), motion vectors off, `updateWhenOffscreen = true` (bounds follow the curl).
9. Components `ModelSlot` (Joint), `PhModelInfo`, `PhRiggedHand`; save `Assets/Art/PhantomHand/Models/Resources/PhantomModels/PH_Hand.prefab`.
10. Self-test: instantiate the saved wrapper, bind, curl 0 -> 1, log joints bound and the middle fingertip displacement (expected dy -2.9 cm, dz -12.5 cm; WARNING if the fingertip does not go down).

Expected log values (from section 3.4) for the Unity driver: `wrist -> fingertip 1.4215 source units, scale 0.13366` (or 142.15 / 0.001337 in cm), `wrist band ... width 6.4 cm ... underside y -0.0214, top y 0.0210; corrected by dx -0.0007, dy 0.0009`, `final bounds min (-0.0757,-0.0354,-0.0120) max (0.0627,0.0223,0.1900)`, `palm length 0.0892, palm width 0.0783, palm centre x 0.0041, palmTopY 0.0188, fingerTopY 0.0112, thumb box centre (-0.0514,0.0771) size (0.0613,0.0750); thumb on -x OK`, `palm side cross-check OK (the thumb sits 2.2 cm toward the palm side)`, `skin tone ... (0.579,0.413,0.334)`, `self-test: 15 of 15 joints bound; ... dy -2.9 cm, dz -12.5 cm ... OK`. Numbers differ in the last digit by float rounding and by the 3 % band edge.

## 5. Deliverable 2: builder hooks + table test
- B4: in `PhantomHandSceneBuilder.BuildScene` right before the table, `bool modelsReady = Opus.Games.PhantomHand.EditorTools.PhantomModelImporter.EnsureWrappers();` (before `BuildPresentation`, which loads the wrappers at Bind). `Shell.Editor.asmdef` + `"PhantomHand.Editor"` (no cycle: it references only Presentation and Opus.Art.Runtime). The result string ends with `models=<bool>`.
- B5: `PhModels.SpawnTable(table, (0, TableTopY, TableCenterZ))`; when it returns null the box table and its four legs are built exactly as before. The wrapper carries its own top collider; the static-batching loop afterwards still covers it.
- `PhantomHandRigValidatorTest.Table_Is75cmHigh_AndMatte` now finds `TableTopSurface` (box) or the `PH_Table` wrapper (renderer + `TopCollider`) and asserts, for both: tableTop anchor y 0.75, renderer top 0.75, `_Smoothness <= 0.1`, a BoxCollider, **and** (new, stricter) the collider's top face at 0.75.

## 6. Deliverable 3: every model under `MetaAssets/Prefabs/` (looked at: FBX parsed, textured, rendered; LOD S ~1k tris, M ~4k, L ~15k)

| id | what it is [V] | role in the Phantom Hand room |
|---|---|---|
| 1746344 | low dark-lacquer table, copper edge, four turned legs | **PH_Table** (done, replaces the boxes). Fitted non-uniformly to 1.2 x 0.7 x 0.75 m (native 2.0 x 0.89 x 1.52: the vertical stretch is 1.4x the width scale and 1.8x the depth scale, so the legs look long and thin); smoothness lowered to 0.08 (matte) |
| 182879 | large grey speckled river stone | **PH_Stone**, THE threat stone, one for both conditions |
| 324213 | black leather LEFT glove | automatic fallback hand when `handRig_02.fbx` is missing (mirrored, flat skin colour) |
| 553886 | black arm / elbow guard with a mesh vent | PH_Forearm (under the sleeve, now tone-matched to the textured hand) |
| 935160 | black compression sleeve, yellow logo | PH_Sleeve |
| 997491 | wide paint brush, wooden handle, orange label | PH_Brush |
| 83035 | smooth pale-grey flat stone | **unused**: a second stone in the room would either be a second threat object (different look per condition would confound SYNC vs ASYNC) or a pre-threat cue lying around; no benign role |
| 1534923 | cream polished egg-shaped stone | **unused**, same reason |
| 1571125 | brown leather glove with a stud | **unused**: a loose glove / hand-shaped object next to the arm competes with the embodiment target; as a hand it is no better than 324213 (no rig, one glove) |
| 286115 | plain white static open hand (old "ghost_hand") | **unused**: superseded by the rigged hand; the only conceivable use is the A2 reveal fallback (ghost outline of the tracked right hand, spec 12 A2), which is not built |
| 12913, 632623 | red apple; pale green apple with AI-texture artefacts | **unused**: Orchard grasp targets, a fruit on the table is a reach target / distractor |
| 43333 | wicker basket with rope handles | **unused**. Only candidate for a room prop (floor planter replacing the procedural pot, S LOD, one draw call) if Opus keeps it out of the Orchard delete list |
| 808342 | light-wood folding trestle table | **unused**: we have the requested table; a second table is clutter |
| 1777303 | stylised tree with purple fruit and orange lanterns (Z-up) | **unused**: strongest colours in the set, would pull the eye away from the arm |
The room stays: procedural plant, picture, rug, bare walls. No new props were added in this patch because every candidate either belongs to the Orchard removal list (E1: 12913, 632623, 43333, 808342, 1777303, 286115) or competes with the arm / threat. No new shadow casters; the SMR, the table and the stone have shadows off; only the stone keeps its blob shadow.

## 7. Deliverable 4: tests
New, EditMode, assembly `PhantomHand.Tests.Editor` (no asmdef change):
- `Tests/Editor/PhHandFitTests.cs` (**pure, 27 cases, RUN standalone: 27/27**): frame from the measured bone positions in four import spaces (x flip, z flip, two with rotation + offset) -> fingers +z, thumb -x, palm down, wrist at the origin, expected local positions of knuckles / thumb / end bones; scale 0.13366; the quaternion algebra (`cross(dorsal, forward) == -thumb`); mirrored hand flagged by the palm-side witness; degenerate input; farthest vertex; percentile; wrist band stats; level / centre shifts and clamps; `TopY`; bone names; curl schedule (15 bones, 72/79/51, 15/35/30, `_base/_Ctrl/_tip/hand.R` = 0); `ChildBone`; `FlexionAxis` incl. a tilted thumb; wrapper names (`PH_Hand ... PH_Table`, `PhantomModels/`); `ChooseHandSource` (rigged > glove > none).
- `Tests/Editor/PhRiggedHandTests.cs` (Unity objects, **compiled, [N] not run**, 15 cases): `Bind` finds 15 bones; curl 1 on a hand-built hierarchy with the FBX bone names puts the index tip exactly at `k + 0.030 d(72) + 0.020 d(151) + 0.020 d(202)` (cross-checked in Python: match), curl 0 restores it; metacarpal / `_Ctrl` / `_tip` / forearm bones do not move; thumb closes toward the palm; wrapper rotated 90 degrees uses its own local down; 3-bone rig does not bind; PoseHash; arm with a rig drives bones and does not squash; glove mesh and an unusable rig squash; `RegisterModel` with a `SkinnedMeshRenderer` built in the test (shadows off, own material kept, alpha 0.5 swaps the twin, 0 disables, 1 restores), override applies to skinned and plain renderers; hit proxy / palm top follow measured info; the glove info keeps the old constants; the procedural hand keeps its own proxy. `PhModels.UseModels` is switched off during these tests so `Build()` stays procedural whatever has been baked.
- `Tests/Editor/PhantomHandRigValidatorTest.cs`: the table test (section 5).
Existing PlayMode tests (`PhantomHandPresentationTests`, 20) compile unchanged; **re-run them after the bake** (the arm then uses the models).

## 8. Deliverable 5: `PhantomHandSceneBuilder.CaptureModelShots()`
Static, edit mode, restores the scene. Writes to `logs/sessions/screens/ph/models/`: `arm_eye`, `hand_top`, `hand_side_curl0`, `hand_side_curl50`, `hand_side_curl100` (the table is hidden for these three because closing fingers dive into it), `brush_contact`, `brush_contact_close`, `stone_telegraph`, `stone_impact`, `table`, `room`; returns the paths joined by `;`.

## 9. Commands for the Unity driver (in order)
Prerequisite: Opus has merged this patch, the editor compiled clean (`GetCompilationStatus` -> clean), the lock is yours, no Play mode.
1. `Opus.Games.PhantomHand.EditorTools.PhantomModelImporter.RunBatch()` (static, no arguments; the same as the menu *Tools/OPUS/Phantom Hand/Build Model Wrappers*). Read the Console: `[PhantomModelImporter] done (force=True):` and every `[hand]` line against section 4, then the `[Forearm]`, `[Sleeve]`, `[brush]`, `[stone]`, `[table]` bounds lines. Expect no `FAILED`; every `WARNING` is a lead (section 10).
2. `Opus.Shell.Editor.PhantomHandSceneBuilder.BuildScene()` (returns `built Assets/Scenes/PhantomHand.unity: ... models=True`; rebuilds from the Orchard rig copy as before). If the other agent's hooks (B1-B3, U4/U5) are not merged yet this still works for the model part.
3. `Opus.Shell.Editor.PhantomHandSceneBuilder.CaptureModelShots()` -> 12 PNGs, read them (section 10).
4. EditMode: `Opus.Games.PhantomHand.Tests.PhHandFitTests` (27), `Opus.Games.PhantomHand.Tests.PhRiggedHandTests` (15), `Opus.Games.PhantomHand.Tests.PhantomHandRigValidatorTest` (the table test passes only after step 2), then all EditMode.
5. PlayMode: `Opus.Games.PhantomHand.Tests.PlayMode.PhantomHandPresentationTests` (20) against the baked models.
The wrappers are normal assets and persist; re-run step 1 only when a source changes. `EnsureWrappers()` (step 2) also re-bakes PH_Hand if the FBX exists but the baked hand is still the glove.

## 10. What to look for, and what a defect means
| shot | expected | defect -> likely cause |
|---|---|---|
| `arm_eye` | black sleeve, then a brown textured hand lying palm down, fingers pointing away (toward the far wall), thumb on the viewer's left, knuckles / nails visible, forearm skin at the cuff the same brown | **fingers point up / sideways** -> container rotation wrong (LookRotation basis or Unity import axes: check `[hand] frame` line); **palm creases visible (palm up) / hand looks inside-out (hollow)** -> model imported mirrored (the log warns `thumb sits on the DORSAL side`), flip the thumb / palm sign in `TryFrame`; **tiny or huge hand** -> scale (`wrist -> fingertip`, final bounds z must be 0.19); **pink / black / flat orange hand** -> pink = shader missing (`Universal Render Pipeline/Lit` not found aborts the bake), black = albedo not assigned, flat orange = the old skin override still applied (`PhModelInfo.texturedSkin` false); forearm clearly lighter / darker than the hand -> tone sample failed (`skin tone` log) |
| `hand_top` | five fingers slightly spread, nails up, wrist ring meeting the sleeve, no spikes or torn triangles | **mangled / fist-like / spiky hand** -> pose restore failed (bindposes not what was assumed): read `[hand] pose restore` (a move of 0 cm with a mangled mesh means Unity already had a different bind); **gap or step at the wrist** -> wrist band numbers (dx/dy); by the numbers the hand's open ring (6.45 x 4.3 cm, cut 1.2 cm behind the wrist) is ~3 mm wider than the sleeve cuff (6.1 x 4.6 cm, starts 1.2 cm behind the wrist), so a thin skin-coloured rim at the cuff is expected, a visible hole is not |
| `hand_side_curl0/50/100` | curl 0 = flat open hand; 50 and 100 = the finger chain closes toward the table, thumb moves a little | **fingers bend up (away from the table)** -> flexion axis sign (self-test line shows `dy` > 0); **nothing moves** -> rig not bound (`self-test: ... did not bind`, squash fallback in use) or `PhRiggedHand` missing on the wrapper; **fingers rotate sideways** -> the palm-normal assumption (wrapper local down) does not hold, i.e. the container rotation (orientation) is wrong |
| `brush_contact*` | wide paint brush resting on the sleeve at motor A, handle tilted back, bristles on the surface | brush floating or sunk -> `ArmGeometry.TopY` vs the sleeve model (known 1.08x loft); brush looks huge -> 7.3 cm head (MODELS run 1 issue 4) |
| `stone_telegraph`, `stone_impact` | grey speckled stone 11 cm over the palm centre, then sitting ON the hand with dust, not sunk, not floating | sunk -> `palmTopY` too low; floating -> too high; offset to one side -> `palmCenterX` |
| `table` | dark lacquer top with copper edge, four turned legs, top at 0.75 | pink -> table material; stretched / thick legs are the known non-uniform fit |
| `room` | table, arm, plant, picture in one frame, nothing overlapping the seat | table too big / small -> `TableW/D/H` |

## 11. Verification: what was compiled, what was run, what is only written
Harness (scratchpad, outside the repo): `mkrsp.py` builds a csc response file from the **main checkout's** Bee rsp for each asmdef (Unity module references, defines, `Library/Bee/.../*.ref.dll` of the project assemblies, Newtonsoft, nunit) with the **worktree's** sources, and points `PhantomHand.Presentation` / `PhantomHand.Editor` at the DLLs just built. Compiler: `dotnet "C:\Program Files\dotnet\sdk\10.0.401\Roslyn\bincore\csc.dll" @<Asm>.local.rsp` (C# 9, `-warn:4`).
```
== Presentation   (19 sources)  no errors, no warnings from my files (3 pre-existing CS0618 FindFirstObjectByType in Ui/)   PhantomHand.Presentation.dll 80,896 bytes
== Editor         (3 sources)   no errors, no warnings                                                                     PhantomHand.Editor.dll 46,592 bytes
== Shell.Editor   (8 sources)   no errors (CS0618/CS0108/CS0219 pre-existing, filtered)                                    Shell.Editor.dll 64,000 bytes
== Tests.Editor   (9 sources)   no errors                                                                                 PhantomHand.Tests.Editor.dll 108,032 bytes
== Tests.PlayMode (1 source)    no errors                                                                                 PhantomHand.Tests.PlayMode.dll 27,648 bytes
```
Run: a 45-line reflection runner over the real `nunit.framework.dll` (Unity's `com.unity.ext.nunit net40/unity-custom`) under .NET 10, loading Unity's managed `UnityEngine.CoreModule.dll` (`Vector3` / `Mathf` / `Matrix4x4` are managed; `Quaternion.AngleAxis / LookRotation` are native and fail outside the engine, which is why `PhHandFit` is quaternion-free):
```
> dotnet bin\run.dll Opus.Games.PhantomHand.Tests.PhHandFitTests
== Opus.Games.PhantomHand.Tests.PhHandFitTests
  PASS BoneNames_FollowTheFbxHierarchy
  PASS ChildBone_FollowsTheDigit
  PASS CurlSchedule_DrivesFifteenBones_AndNothingElse
  PASS FarthestAlong_FindsTheMiddleFingertipVertex
  PASS FlexionAxis_TurnsTheBoneTowardThePalm_AndIsZeroWhenDegenerate
  PASS Frame_MatchesTheRotationTheImporterApplies(0)   (1, 2, 3 likewise)
  PASS Frame_OfAMirroredHand_ComesOutPalmUp_AndThePalmSideWitnessSaysSo
  PASS Frame_PutsTheFingersOnPlusZ_TheThumbOnMinusX_AndThePalmDown(0)   (1, 2, 3 likewise)
  PASS Frame_Scale_MakesWristToMiddleFingertipTheHandLength(0)   (2 likewise)
  PASS Frame_ThumbBones_LieOnMinusX_AndTowardThePalmSide(0)   (1, 2, 3 likewise)
  PASS HandSource_PrefersTheRiggedHand_FallsBackToTheGlove_ThenToTheProceduralHand
  PASS LevelAndCentreShifts_AreSmallForTheMeasuredHand_AndClampedWhenTheMeasurementFails
  PASS Percentile_InterpolatesBetweenSortedValues
  PASS TopY_ReadsTheDorsalHeightInsideTheWindow
  PASS TryFrame_DegenerateInput_ReturnsFalse
  PASS WrapperNames_AreTheFixedPhNames_InTheResourcesFolder
  PASS WristStats_UseOnlyTheBandAtTheCutEnd
DONE passed=27 failed=0 total=27
```
(The block above collapses the repeated `(n)` cases; the real output has one PASS line per case.) Mutation check: with the palm sign flipped in `TryFrame` (`Cross(t, f)` instead of `Cross(f, t)`) the same run fails at least 12 cases (the output was cut at 12: rotation algebra x4, mirrored witness, frame x4, thumb x3 ...), so the tests do detect a wrong chirality.
Python (also standalone): the importer's fit on the FBX bind pose reproduces `det(R) = +1` and the same wrapper-space numbers in the x-flip and the z-flip emulation; the curl simulation of the `PhRiggedHand` maths on the test hierarchy matches the closed-form tip (`match True`).
**Written, never run (needs Unity):** `PhRiggedHandTests` (15), the table test, `PhantomModelImporter.BuildRiggedHand` and all its Unity API calls (`ModelImporter` flags, `SaveAndReimport`, `Instantiate`, `bindposes`, `BakeMesh`, `Quaternion.LookRotation`, `SaveAsPrefabAsset`), `RestoreBindPose`, `SelfTestRiggedWrapper`, `VirtualArmRig` model paths, `CaptureModelShots`, the builder hooks, and every visual statement. **No visual result is claimed anywhere.**

## 12. Quest budget (estimate, not measured in a build)
Per eye, whole scene: hand 1 (one SkinnedMeshRenderer, one material, 14.5k verts, 4 bones per vertex), forearm 1, sleeve 1, bands 2, brush 1, stone 1 (+ blob shadow 1 while active), table 1 (was 5 boxes), room static-batched by material ~8, picture / rug / plant ~6, probe ruler 3 + dot 1 + outline 1 while shown, one world-space UI panel at a time ~5: about 30-40 worst case, under 60. Triangles: hand 29k, forearm ~4k, sleeve ~4k, brush 15k, stone 4k, table 4k = ~60k. Textures: hand_Co + hand_No at 1024 ASTC (6x6 / 5x5) ~1.2 MB, the others as before. Memory note: the FBX is Read/Write for the bake (CPU copy ~1.5 MB). No shadow casters added.

## 13. Open issues and risks
**The three things most likely to be wrong when the bake first runs**
1. **Bind-pose restore.** If Unity's bindposes are not `bone.worldToLocal * renderer.localToWorld` as assumed (or the SMR carries a scale that differs from its bones), the hand comes out mangled (fist, spikes). Check the `[hand] pose restore` line and `hand_top`.
2. **BakeMesh / import-space measurements.** The skin tip, wrist band and bounds come from `SkinnedMeshRenderer.BakeMesh`; its scale convention is cross-checked against the bones and auto-corrected with a WARNING, but a wrong space would show as final bounds z != 0.19. Same family: palm UP if Unity mirrors the model differently than assumed (the log's palm-side cross-check is the alarm).
3. **Look of the hand on the table.** The bind pose is a flat open hand with the thumb 1.5-2.5 cm below the knuckle plane (buried in the tabletop) and the fingers 0.6-1.2 cm above it, the normal map's green channel may be inverted (OpenGL vs DirectX, undecidable from the file), and the junction of the hand's open wrist ring (cut at z = -0.012, 6.45 x 4.3 cm) with the sleeve cuff (starts at d = 0.012, 6.1 x 4.6 cm): 0 mm gap, a ~1.6 mm rim each side, the interior is hidden only by the forearm tube.
Other issues:
4. Curl digs the fingertips into the table (the wrist stays on it). Simulated on the real chains (bone ends): at curl 0.5 the index / middle / ring tips are 4.4 / 5.1 / 4.5 cm below the table plane, at curl 1.0 2.3-3.0 cm below (the chain swings back up past 180 degrees). From above the fingers vanish into the tabletop while closing. The squash fallback avoided this. Lift / re-pose when U7 (agency) needs a visible clench.
5. Thumb curl numbers (15/35/30) are a guess; finger numbers are the old procedural ones.
6. `skinWeights` of the Android quality level is 2: overridden per renderer to 4 bones.
7. `updateWhenOffscreen = true` costs a small per-frame bounds update; switch off if the profiler objects.
8. The FBX .meta files were not hand-edited (cameras / lights / animation are forced by the importer on the first bake); until then the editor imports the FBX with the camera and light.
9. Table: dark glossy texture at smoothness 0.08, legs stretched ~1.4x (non-uniform fit, MODELS run 1 issue 5 still open).
10. Licence / credit of `handRig_02.fbx` unknown (docs/MANUAL_TODO.md); the repo is public. Decide before pushing the FBX.
11. `PhantomHandSceneBuilder` still builds from a copy of `OrchardReach.unity` (E2 #1): not touched.
12. `EnsureWrappers()` is called inside `BuildScene`; a first bake there runs with an unsaved scene copy open: run step 1 of section 9 first so the log is read separately.

## 14. Next step
Opus: review the diff, merge, commit. Unity driver: section 9 in order, then paste the `[hand]` log lines and the 12 screenshots (or the defect from section 10) into `logs/sessions/2026-10-08-PH-U-MODELS-run3.md`; tune (in this order if needed) the rotation sign, `HandLenM`, the curl schedule, the thumb relax, and the table fit.

## CONTRACT / SPEC requests
None. (PH_STATUS Next item 3 and the worklist section C rows are answered by sections 3.9 and 4.)

## Changed and new files
Modified (6): `game/Assets/Games/PhantomHand/Editor/PhantomModelImporter.cs`, `game/Assets/Games/PhantomHand/Runtime/Presentation/PhMaterials.cs`, `game/Assets/Games/PhantomHand/Runtime/Presentation/VirtualArmRig.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhantomHandRigValidatorTest.cs`, `game/Assets/Shell/Editor/PhantomHandSceneBuilder.cs`, `game/Assets/Shell/Editor/Shell.Editor.asmdef`.
New: `game/Assets/Games/PhantomHand/Editor/PhantomModelImporter.RiggedHand.cs`, `game/Assets/Games/PhantomHand/Runtime/Presentation/PhHandFit.cs`, `game/Assets/Games/PhantomHand/Runtime/Presentation/PhRiggedHand.cs`, `game/Assets/Games/PhantomHand/Runtime/Presentation/PhModelInfo.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhHandFitTests.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhRiggedHandTests.cs`, `logs/sessions/2026-10-08-PH-U-MODELS-run2.md` (this file). No .meta files were written by hand: the editor generates them for the six new .cs files on import; commit those together with the patch (without them the new scripts get new GUIDs on every machine).
