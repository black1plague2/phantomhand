# PH-U-MODELS run 1 (2026-10-08): wire the user's 3D models into the Phantom Hand presenters

Goal: hand/forearm/sleeve, brush, stone and table models in the presenters, procedural geometry kept as fallback, Alpha (A2) fade still working, PoseHash/WorldPos semantics unchanged.
Unity MCP was disconnected for the whole run, so NO asset (prefab/mesh/material) has been generated yet. Code is finished and compiles; the importer has to be run once in the editor.

## CHECKPOINT 1: analysis (no Unity needed)
Parsed the 8 binary FBX files (Python) and rendered them with their atlas textures. Findings that change the brief:
- Every texture is a shared atlas (many objects per 2048 PNG). 324213 "hand" is a **black leather glove (left hand!)**, 553886 is a **padded arm/elbow guard** (black, orange band), 935160 is a black compression sleeve with a yellow logo. None is skin-coloured, none is a bare forearm.
- All meshes: Z-up file data, Unity importer node transform = Rx(-90), scale 100 (so world size ~2 units). Unity-space vertex = (-xF, zF, -yF) (importer mirrors x). All geometry is read from the instantiated `<id>_<lod>.prefab` through its real matrices, so this assumption only matters for the two direction constants below.
- Hand: fingertips are at z=0 (model bottom), glove cuff at the top; palm side normal (Unity space) ~ (0.6,-0.76,-0.23) (verified by rendering both sides: strap and buttons are on the back); chirality: viewed dorsal-up with fingers away, the thumb is on the right, so the mesh is a LEFT hand and must be mirrored for the right arm.
- Arm guard / sleeve: curved, lopsided tubes (centre offset up to 65 % of the radius), wrist end narrow (z=0), elbow end wide, open at both ends.
- Brush: z=0 handle end, z=2 bristle end, straight axis, wide head 7.3 cm x 3.8 cm at 22 cm length. Table top is flat at native y max 0.891 (thin wood edge), no recess.

Design decision: bake everything into MESH DATA at editor time (axis alignment + non-uniform "loft" of the arm tubes onto `ArmGeometry`), so the wrappers have identity child transforms and the presenters need no runtime fitting; the brush path / WorldPos / TopY keep using `ArmGeometry`, which the baked sleeve and forearm follow to within ~1 mm (sleeve 1.08x, forearm 0.90x of the ArmGeometry half-extents). Verified the fit in a numpy port (rendered: tapered sleeve over the forearm, hand + cuff joined at the wrist, thumb on -x, dorsal up, fingers +z); a few skin patches poked through at 0.97/1.07 so the constants were widened to 0.90/1.08.

## CHECKPOINT 2: code written, compile verified
Files changed/created (all in my ownership):
- `game/Assets/Games/PhantomHand/Editor/PhantomModelImporter.cs` (new) + `PhantomHand.Editor.asmdef` (added refs `PhantomHand.Presentation`, `Opus.Art.Runtime`)
- `game/Assets/Games/PhantomHand/Runtime/Presentation/PhMaterials.cs`: new `PhModelInfo` component and `PhModels` static class (`UseModels` flag, `Load/Spawn/SpawnTable`, wrapper names)
- `VirtualArmRig.cs`, `BrushRig.cs`, `ThreatDrop.cs`: model paths with procedural fallback
- `ArmGeometry.cs`: unchanged (the models follow it)

Compile evidence: the Unity GUI editor was running but only recompiles when focused, and the Shell.Runtime assembly currently fails for a reason outside my files (`Assets\Shell\Runtime\PhantomLiveStatus.cs`: `Opus.Games.PhantomHand` / `PhPhase` / `ConditionResult` / `ThreatResponse` not found, i.e. Shell.Runtime.asmdef lacks a reference to PhantomHand.Runtime; owned by the U5 track). The Editor.log compile at 07:3x showed errors ONLY for that file. For my final sources I compiled the two assemblies standalone with Unity's own Roslyn and the Bee response files (Presentation.rsp, Editor.rsp, outputs redirected to the scratchpad):
```
dotnet.exe DotNetSdkRoslyn/csc.dll -nologo -noconfig @pres.rsp   -> Pres.dll (75,776 bytes), no errors
dotnet.exe DotNetSdkRoslyn/csc.dll -nologo -noconfig @ed.rsp     -> Ed.dll  (29,696 bytes), no errors   (rsp lists PhantomModelImporter.cs; Presentation reference replaced by the fresh Pres.dll)
```
(`warning CS0618` FindFirstObjectByType in `Ui/` is not mine.)

## What the importer produces (Tools/OPUS/Phantom Hand/Build Model Wrappers, or `-executeMethod Opus.Games.PhantomHand.EditorTools.PhantomModelImporter.RunBatch`)
Output under `Assets/Art/PhantomHand/Models/`: `Meshes/PHM_*.asset`, `Materials/PHM_*.mat` (URP Lit; textured ones get an Android ASTC 6x6 / 1024 override), `Resources/PhantomModels/PH_*.prefab` (ModelSlot root + one child mesh GameObject; `PhModelInfo` where needed). It also switches Read/Write on for the source FBX it reads (meta change on those `*_L/M.fbx`).

| Slot (Resources name) | Source | LOD | Target (design values, to be re-measured from the importer log) |
|---|---|---|---|
| PH_Hand (child "Hand") | 324213 | L | wrist crease at origin, fingertip at z=0.19 m, mirrored to a right hand, palm-down, dorsal +y, finger underside on the table plane (y -0.0205), flattened to 0.75 thickness, palm width ~8.9 cm, dorsal top ~y +0.025; glove cuff lofted to ArmGeometry wrist section and trimmed at z=-4.5 cm; flat skin material (the glove texture is black) |
| PH_Forearm (child "Forearm") | 553886 | M | tube d=0..0.25 m (runtime z-scale = forearm_length/0.25), 0.90x ArmGeometry, domed elbow end, skin material |
| PH_Sleeve (child "Sleeve") | 935160 | M | tube d=0.012..0.195 m (runtime z-scale = sleeveTo/0.195 with sleeveTo = min(L-0.02, A+S+0.045) as before), 1.08x ArmGeometry, keeps its black texture, logo on the right side |
| PH_Brush (child "BrushModel") | 997491 | L | 22 cm, bristle tip at origin, handle +y, head wide along x (7.3 cm) |
| PH_Stone (child "StoneModel") | 182879 | M | unit mean diameter (ThreatDrop keeps scaling by 0.11 m, sphere collider radius 0.46 and Rigidbody unchanged) |
| PH_Table (child "TableModel") | 1746344 | M | pivot top centre, top at y=0 (place at TableTop anchor 0.75), 1.2 x 0.7 m top (same as the current box table) x 0.75 m tall (non-uniform fit, legs are stretched), top BoxCollider 1.2 x 0.04 x 0.7 |
Unused: 83035 (stone), 1534923 (flat stone). Motor bands remain the procedural overlay rings (BandA/BandB, 1.10-1.14x, over the sleeve at 1.08x).

Runtime behaviour: `VirtualArmRig.Build` uses the models only if hand+forearm+sleeve all load, otherwise the whole procedural arm (so a half-built set never mixes). Model hand has no bones: `Curl` is approximated by squashing/thickening the hand (and is part of `PoseHash`, procedural joints untouched), hit-proxy palm/finger boxes are resized to the model's measured dorsal height (`PhModelInfo.palmTopY/fingerTopY`) and `PalmTopWorld` uses it. Renderer GameObject names: `Forearm` (U3 Alpha test finds it), `Sleeve`, `Hand`. Alpha: renderers register with `PhMaterials.FadeCopy`, the models' materials are URP Lit so the transparent twin works (textured sleeve keeps its texture). ThreatDrop's stone fade/ResetStoneMaterial use the model material the same way. BrushRig model: one rigid mesh, a 3 % squash replaces the bristle bend. `PhModels.UseModels = false` forces procedural everywhere (tests/A-B).

## Builder hook lines for PhantomHandSceneBuilder (manager wires; I did not touch it)
1. Before building presenters/environment (editor, once): `Opus.Games.PhantomHand.EditorTools.PhantomModelImporter.EnsureWrappers();`  (only available in the Shell.Editor assembly if it references `PhantomHand.Editor`; otherwise run the menu item once, the wrappers are normal assets and persist).
2. Table: replace the `TableTopSurface` box + 4 `Leg` boxes with `var tm = Opus.Games.PhantomHand.Presentation.PhModels.SpawnTable(table, new Vector3(0f, TableTopY, TableCenterZ)); if (tm == null) { /* keep the procedural boxes */ }` (the wrapper has its own top collider; the old box needs `collider:true` only in the fallback).
3. Nothing else: arm, brush and stone pick the models up through `Resources` automatically; `PhModels.UseModels` defaults to true. Note: if the scene builder assigns `ArmMaterials.skin`, it is applied to hand+forearm (otherwise the wrapper's PHM_Skin); `ArmMaterials.sleeve` is ignored when the sleeve model is used.

## Draw calls (per eye, arm + props)
Procedural: palm 1 + fingers 14 + forearm + sleeve + 2 bands = 19, brush 3, stone 1, table 5 boxes (maybe batched). Models: hand, forearm, sleeve, 2 bands = 5, brush 1, stone 1, table 1 (+ blob shadow unchanged) => ~14 fewer calls and one shared skin material for hand+forearm. Triangles: hand L ~15k minus the trimmed cuff (~12k est.), forearm M ~4k, sleeve M ~4k, brush L ~15k, stone M ~4k, table M ~4k, about 43k tris total, fine for Quest. Textures: sleeve/brush/stone/table are 2048 atlases forced to 1024 ASTC 6x6 on Android (4 x ~0.5 MB). Not measured in a build; no Unity session was available.

## Acceptance table
| Item | Status |
|---|---|
| Importer + presenters compile | PASS (standalone Roslyn with Bee rsp; Editor.log compile showed only the foreign Shell.Runtime errors) |
| Wrappers generated | NOT RUN (Unity MCP down; editor GUI could not be driven) |
| Measured sizes in Unity | NOT RUN (values above are the design values from the numpy port; importer logs the real bounds/thumb-side check when run) |
| U3 PlayMode tests (20) | NOT RUN (not allowed; with no wrappers the code paths are the unchanged procedural ones) |
| Visual check | NOT RUN |

## Open issues / risks
1. Run the importer once and read its log: it prints the baked bounds and a thumb-side line ("thumb on -x OK" or a WARNING to flip the mirror). If the hand renders as a left hand, flip the mirror in `BuildHand`.
2. Two direction constants (`HandPalmNormal`, `SleeveLogoDir`) assume Unity space = (-xF, zF, -yF); if the hand comes out palm-up or upside down, negate `HandPalmNormal`.
3. The hand is a black glove mesh tinted flat skin colour (no glove texture, no detail); a better textured skin hand model would help. The 553886 "forearm" is an elbow guard re-lofted to a plain tube, mostly hidden under the sleeve.
4. Brush head is 7.3 cm wide (22 cm total length, per the brief): wider than the old 3.4 cm head; scale `BrushLenM` down if it looks big.
5. Table non-uniform fit stretches the legs; alternative is a 0.75 m uniform scale (1.7 x 1.3 m top) which would reach the seat.
6. `Shell.Runtime` currently does not compile (PhantomLiveStatus.cs missing asmdef reference, U5 track); Unity cannot reload until fixed, so the importer menu will not be available before then.
7. Stone model has no compound collider change (ThreatDrop's sphere collider is kept as before); stone is a flatter ovoid, mean-diameter normalised.

## Next step
Fix Shell.Runtime references (U5), let Unity reload, run `Tools/OPUS/Phantom Hand/Build Model Wrappers`, read the Console log, then run the U3 PlayMode tests and capture arm/brush/threat screenshots to tune `ForearmFit/SleeveFit`, hand cuff blend and brush length.

## O2 review (Opus, 2026-10-08)
Decision: **PROVISIONAL — committed code only, UNRUN** (no MCP: no wrapper assets baked, presenters fall back to procedural). Findings for the user: 324213 is a LEFT black leather glove, 553886 an arm guard.
