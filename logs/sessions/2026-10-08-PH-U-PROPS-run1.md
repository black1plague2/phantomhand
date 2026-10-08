# PH-U-PROPS run 1 (2026-10-08, builder session): seven GLB props into the game. Code and offline tests only, Unity NOT driven

Worktree `H:\Chenta\phantomhand\.claude\worktrees\agent-a2ab8723234c2dba5` (base 9a011a3). Nothing was run in Unity (the open editor belongs to the Unity driver); no `tools/unity_mcp.py`, no lock file, no package change. The result is an uncommitted working-tree diff.
Tags: **[V]** verified this session (file read, Python reference reader, standalone compile, standalone test run), **[I]** inference, **[N]** written, never run in Unity.

## 0. Outcome
| Item | State | Evidence |
|---|---|---|
| `GlbReader` (pure C#, Newtonsoft only) | done, tested on the 7 real files | 51 NUnit cases pass; bounds, counts and winding agree with an independent numpy reader [V] |
| Prop bake in `PhantomModelImporter` (new partial file) | written, compiled, **bake NOT run** | compile [V]; every Unity API call [N] |
| Scene placement in `PhantomHandSceneBuilder` | written, compiled, **build NOT run** | compile [V]; placement maths checked in Python [V]; scene result [N] |
| `DarkenController` renderer list | done | pure scaling tested [V]; MaterialPropertyBlock path [N] |
| `CaptureModelShots` `room_eye`, `table_props` | written, compiled | [N] |
| Tests (a) reader on 7 files, (b) plausibility, (c) darken | (a) (b) run offline, (c) half offline | section 5 |

## 1. Things in the brief that are not as written (read this first)
1. **The brief's "triangles" column counts indexed primitives only [V].** Plant, window and picture each have NON-indexed primitives (three.js wrote no `indices` for them): plant stems 216 + leaves 180 tris, window sill 236, picture moulding 240. A reader that skips them would bake a pot with soil only, a window without its sill, a picture without its frame. Real totals: plant **1244** (table 848), window **322** (86), picture **256** (16); the other four match. The test checks both: the table's number from the raw JSON (indexed only) and the reader's total.
2. **The "raw accessor bounds" are not the real sizes [V].** With the node matrices applied: lamp **0.384 x 0.882 x 0.384 m** (table: 1.22 m tall, so the real lamp is 28 % lower than the table's 1.22 m and the literal 25 % check would have thrown); bowl **0.1535 x 0.090 x 0.1775 m** (table: "about 14 cm wide": that is the bowl alone, the striker lying beside it adds depth); brush **0.032 x 0.193 x 0.032** (table 20.4 cm long); window 1.02 x **1.20** x 0.18 (table 1.17); picture 0.50 x 0.40 x **0.025** (table 0.028). Plant (0.563 x 0.579 x 0.572) and cup (0.144 x 0.0685 x 0.144) agree. `PropFit.Specs` therefore holds the node-transformed sizes and the 25 % check is made against those (a literal check against the table would have refused the lamp).
3. **There is no ceiling in the room** (two walls, a floor, no sky). A pendant lamp "hanging" from nothing would float its canopy under the sky, so a ceiling slab is added at the canopy height (only when the lamp exists), see section 3.4.
4. **Five of the seven models carry a normal map** (walnut in brush, bowl and picture; linen on the lamp shade; terracotta on the pot) although the brief lists only base colour. They are baked (Normal-map import type, `_BumpMap`, `_BumpScale`, `_NORMALMAP`, tangents recomputed): wishlist 5.2 rule 4/5 asks for exactly that and the cost is small. Remove `NormalPng` handling in `SavePropMaterial` if the extra texture sample is unwanted.
5. The brush is 19.3 cm, not 22 cm like the Meta brush; `BrushRig` needs no change (tip at the origin, handle +y), but its comment "22 cm" is now stale (not touched).

## 2. Files
Modified (6): `Editor/PhantomModelImporter.cs`, `Runtime/Presentation/PhMaterials.cs` (`PhModels`: six names, `All` now 12), `Runtime/Scene/DarkenController.cs`, `Shell/Editor/PhantomHandSceneBuilder.cs`, `Tests/Editor/PhHandFitTests.cs` (one test: `PhModels.All` is the 12 names), `Tests/Editor/PhantomHand.Tests.Editor.asmdef` (+ reference `PhantomHand.Editor`, so the tests can reach the reader).
New (7): `Editor/GlbReader.cs`, `Editor/PropFit.cs`, `Editor/PhantomModelImporter.Props.cs`, `Tests/Editor/GlbReaderTests.cs`, `Tests/Editor/PropFitTests.cs`, `Tests/Editor/DarkenControllerTests.cs`, this log. No `.meta` written (Unity writes them for the new `.cs` files and the new `Models/Textures` folder; commit them with the patch).
Untouched on purpose: `BrushRig.cs`, the validator test (the lighting rules it checks still hold: key stays `lights[0]`, directional, warm, no shadows; trilight ambient; fog 0.04), `Packages/`, `contracts/`, `app/`, anything Orchard Reach.

## 3. Design
### 3.1 GlbReader (`Editor/GlbReader.cs`)
Container + JSON (Newtonsoft `JObject`) + BIN chunk. Scene = the default scene's nodes; node = `matrix` (column-major) or TRS (M = T R S); world matrix per node; vertex = M p, normal = inverse transpose (cofactors / determinant). Primitives: mode 4, POSITION / NORMAL / TEXCOORD_0 float, indices ubyte / ushort / uint **or none**, `byteOffset` of accessor and view, `byteStride`, bounds-checked. Merged **per material** into one part each (glTF material order), no welding.
Handedness (comment in the file): x mirrored (positions and normals, front of the asset stays +z = Unity forward, as glTFast does), winding of every triangle reversed (a node whose own matrix mirrors cancels that), texture v -> 1 - v. Metres stay metres.
Materials: name, baseColorFactor (linear), baseColorTexture and normalTexture (+ scale) as embedded PNG bytes, metallic, roughness (glTF default 1), emissiveFactor x `KHR_materials_emissive_strength`, `BLEND`, `doubleSided`. Throws `NotSupportedException` ("<file>: <feature> is not supported by GlbReader") for: other/required extensions, skins, animations, morph targets, modes other than triangles, other attributes (TEXCOORD_1, COLOR_0, TANGENT, ...), sparse or normalized accessors, non-float attributes, external buffers/images, non-PNG images, MASK, metallicRoughness / occlusion / emissive textures, a second UV set, a primitive without material; `InvalidDataException` for broken files (bad magic, truncated, out-of-range accessor or index, node cycle).
### 3.2 PropFit (`Editor/PropFit.cs`, pure) and the bake (`PhantomModelImporter.Props.cs`)
`PropFit.Specs`: the 7 files, wrapper names (= file names), pivots, expected sizes. `Recentre` moves the pivot to the origin: lamp = top centre, plant / bowl / cup = bottom centre, picture / window = centre of the back face (min z; the front stays +z), brush = lowest bristle vertex (centred on the lowest 5 % of the `PH_Bristle` vertices; refuses a file whose bristles are not at the -y end; the head is a lathe, x and z extents both +-0.016, so there is no wide axis to turn to +x). "Centre" is the middle of the bounding box (plant pot sits 2.9 cm off it, bowl axis 3.9 / 18.8 mm: harmless). `SizePlausible`: every axis within 25 % of `Specs`, NaN fails.
Bake per model: `GlbReader.Read` -> `Recentre` -> size check (throws, nothing baked) -> ONE mesh `PHP_<Name>` with a submesh per material (tangents when a normal map exists) -> per material `PHP_<Name>_<Material>` (URP Lit, `_BaseColor` = linear factor `.gamma` because the project is in linear colour space, `_Smoothness` = 1 - roughness, `_Metallic`, normal map, emission = factor x strength `.gamma` + `_EMISSION`, doubleSided -> `_Cull` 0, BLEND -> `PhMaterials.FadeCopy`) -> textures `Models/Textures/PHP_<Name>_<Material>_BaseColor|Normal.png` (the embedded bytes, written only when different; max 512, mips, sRGB for colour, Normal-map type for normals, Android ASTC 6x6 through the existing `SetAstc`) -> wrapper `PH_*` via a new `SaveWrapper(..., Material[], ...)` overload (child `<Name>Model`, shadows off, no collider, `ModelSlot` with `realWorldSizeM` = largest extent). One `[prop]` line per model. Authored size kept, no scale.
Wiring: `Build()` loops `PropFit.Specs` (the brush through `BuildBrush`, which bakes `PH_Brush.glb` when it exists, else the Meta brush as before); `AllPresent()` = the six Meta wrappers + every prop whose `.glb` exists (a missing file is only logged `skipped`); `EnsureWrappers()` also rebuilds when the saved `PH_Brush` is still the Meta brush (`BrushNeedsRebuild`: mesh name != `PHP_Brush`). `RunBatch()` rebuilds everything.
### 3.3 DarkenController
New `public Renderer[] renderers` (default empty). `Capture()` records `_BaseColor` and `_EmissionColor` of every material slot of those renderers; every `Apply()` sets a per-slot `MaterialPropertyBlock` with RGB x level (`DarkenController.Scale`, alpha kept, the same scaling the ambient colours and fog get); at level 1 the block is removed (`SetPropertyBlock(null, slot)`), the shared materials are never touched. Without renderers it behaves exactly as before.
### 3.4 Scene (all numbers metres, world frame; constants at the top of `PhantomHandSceneBuilder`)
`EnsureWrappers()` now runs BEFORE the room (the plant and picture need the wrappers). `Place()` spawns a wrapper at a pose and records it; the result string of `BuildScene` ends `, props=PH_FramedPicture+PH_Plant+...`.
| Prop | Pose | Fallback |
|---|---|---|
| Picture | pivot (0.9, 1.55, 2.65) = old spot on the front wall's inner face, yaw 180 (front to -z) | the two boxes |
| Plant | pivot (-1.8, 0, 2.2) = old spot, floor | cylinder + two spheres |
| Window | pivot (-2.45, 1.45, 1.65) on `Wall_Left`'s inner face, yaw 90 (front to +x); spans y 0.85-2.05, z 1.14-2.16; an unlit quad `WindowGlow` (`PH_WindowDusk` (0.74, 0.47, 0.33), glass bounds + 6 cm, 4 cm behind the glass, found from the glass submesh) | nothing |
| Pendant | axis (0.10, 0.50); shade bottom y = 0.75 + 0.75 = **1.50**, canopy top y 2.382, `Ceiling` slab 7 x 7 m with its underside at 2.380 (same material as the walls); the linen shade is swapped for the unlit `PH_LampShade` (1.0, 0.88, 0.68) with the linen weave texture; bulb stays emissive | nothing (no light, no ceiling, key stays 1.05) |
| Pendant light | `PendantLight`: point, (1.0, 0.76, 0.48), intensity 0.7, range 2.2, **no shadows**, at (0.10, 1.44, 0.50) = 6 cm under the shade; the key light drops 1.05 -> 0.80 | - |
| Singing bowl | pivot (0.49, 0.75, 0.64), yaw 180 (striker on the near side) | nothing |
| Tea cup | pivot (0.34, 0.75, 0.71), yaw 205 (handle to the right and toward the seat) | nothing |
All under `Environment` (static flags by the existing loop), no colliders, shadows off. Registered with the `DarkenController`: lights `{ key, PendantLight }` (key first) and renderers `{ lamp, WindowGlow }`.
**Why these numbers [V, Python scans in the scratchpad]**
- Lamp: the table's far half starts at z 0.47. Sight lines from the seated eye (0, 1.18, 0.02) and six head offsets (+-8 cm x, +-5 cm y, +-8 cm z), 244 sample points (7 x 7 grids on the instruction, questionnaire, results and HUD panels, the virtual and the real arm, the stone drop volume), tested against the lamp's solid of revolution (dome profile from the vertices): z 0.48-0.52 clear for all 1708 lines; z 0.54: 1 line (results panel top edge) is hit; z 0.60: 12. Hence z 0.50. Below the eye the arm, brush, stone, questionnaire and instruction panel are never behind the shade (it hangs 0.32 m above the eye).
- Bowl and cup (plan view, table x +-0.6, z 0.12-0.82): real-arm area = the arm-rest outline's bounding box, x 0.136-0.224, z 0.15-0.58 (half width 0.044 about `ArmX`, elbow to fingertips); virtual arm x -0.014-0.074. Bowl (axis (0.494, 0.659), r 0.07, striker x 0.413-0.567, z 0.551-0.611): real arm **0.189**, virtual arm 0.339, ruler (z 0.325-0.375) 0.176, table edges 3.3 cm. Cup (saucer r 0.072): real arm **0.102**, virtual arm 0.224, ruler 0.263, edge 3.8 cm. Bowl-cup gap 2.0 cm (striker-cup 5.1 cm). Chosen by a grid search that maximises the smallest of these.
- Window: from the seat looking straight ahead it spans 65.4 to 48.9 degrees left of the view axis (centre 56.4): inside a ~110 degree headset field only at its far third [I: Quest 3 field of view]; looking down at the table it is out of view. Plant is 39.5 left, picture 18.9 right.
Quest budget [I]: the 7 models add 9,822 triangles and about 21 draw calls (submeshes) + the glow quad; the removed primitives (plant, picture boxes) batched anyway. 10 new 512 px textures (3.2 MB of PNG source).

## 4. Measured models [V] (node transforms applied; Unity axes = x mirrored; `[prop]` format of the importer, from the same `GlbReader` + `PropFit` calls run in a console probe)
```
[prop] PH_Brush <- PH_Brush.glb: 2112 tris, 1240 verts, 3 materials (PH_Bristle, PH_Brass, PH_Wood_Walnut); bounds min (-0.0160, 0.0000, -0.0161) max (0.0160, 0.1930, 0.0159) size (0.0320, 0.1930, 0.0320) m, expected (0.0320, 0.1930, 0.0320) +-25 %: plausible; pivot BristleTip (was at (0.0000, 0.0000, 0.0001))
[prop] PH_PendantLamp <- PH_PendantLamp.glb: 2304 tris, 1387 verts, 4 materials (PH_Brass, PH_Cord, PH_Linen_Shade, PH_Bulb_Emissive); bounds min (-0.1920, -0.8820, -0.1920) max (0.1920, 0.0000, 0.1920) size (0.3840, 0.8820, 0.3840) m, ...: plausible; pivot TopCentre (was at (0.0000, 0.0000, 0.0000))
[prop] PH_Plant <- PH_Plant.glb: 1244 tris, 1794 verts, 4 materials (PH_Terracotta, PH_Soil, PH_Stem, PH_Leaf); bounds min (-0.2816, 0.0000, -0.2858) max (0.2816, 0.5788, 0.2858) size (0.5631, 0.5788, 0.5717) m, ...: plausible; pivot BottomCentre (was at (-0.0290, 0.0000, 0.0042))
[prop] PH_Window <- PH_Window.glb: 322 tris, 880 verts, 2 materials (PH_Paint_Frame, PH_Glass); bounds min (-0.5100, -0.6000, 0.0000) max (0.5100, 0.6000, 0.1800) size (1.0200, 1.2000, 0.1800) m, ...: plausible; pivot BackCentre (was at (0.0000, 0.6000, -0.0600))
[prop] PH_SingingBowl <- PH_SingingBowl.glb: 2080 tris, 1168 verts, 3 materials (PH_Fabric_Oxblood, PH_Brass, PH_Wood_Walnut); bounds min (-0.0767, 0.0000, -0.0888) max (0.0767, 0.0900, 0.0888) size (0.1535, 0.0900, 0.1775) m, ...: plausible; pivot BottomCentre (was at (0.0039, 0.0000, 0.0188))
[prop] PH_TeaCup <- PH_TeaCup.glb: 1504 tris, 864 verts, 1 materials (PH_Ceramic_Glaze); bounds min (-0.0720, 0.0000, -0.0720) max (0.0720, 0.0685, 0.0720) size (0.1440, 0.0685, 0.1440) m, ...: plausible; pivot BottomCentre (was at (0.0000, 0.0000, 0.0000))
[prop] PH_FramedPicture <- PH_FramedPicture.glb: 256 tris, 752 verts, 4 materials (PH_Wood_Walnut, PH_Passepartout, PH_Art_Canvas, PH_Glass); bounds min (-0.2500, -0.2000, 0.0000) max (0.2500, 0.2000, 0.0250) size (0.5000, 0.4000, 0.0250) m, ...: plausible; pivot BackCentre (was at (0.0000, 0.0000, -0.0000))
```
Real (node-transformed) bounds before the pivot move, glTF axes: brush x +-0.016, y 0-0.193; lamp y -0.882-0; plant x -0.2526-0.3105, y 0-0.5788, z -0.2817-0.29; window x +-0.51, y 0-1.2, z -0.06-0.12 (sill 0.12 out on +z = the room side); bowl x -0.0806-0.0729, y 0-0.09, z -0.07-0.1075 (striker on +z); cup x/z +-0.072, y 0-0.0685 (handle at +x 0.030-0.055); picture x +-0.25, y +-0.2, z 0-0.025.

## 5. Verification, honestly
Harness (scratchpad `...\9f034333-...\scratchpad\props\`, outside the repo): `mkrsp2.py` makes a csc response file per asmdef from the main checkout's Bee rsp (Unity module references, Newtonsoft, nunit, the project ref DLLs) with the WORKTREE sources and points downstream assemblies at the DLLs just built; `build.ps1` compiles all six (csc 10.0.401, C# 9 defaults); `run\` is a reflection runner over the real `nunit.framework.dll` (`[Test]`, `[TestCase]`, `[SetUp]`, `[Category("UnityEngine")]` = SKIP); `probe\` prints the `[prop]` lines; `mutate.py` breaks the reader on purpose; `glbbounds.py` / `glbdump.py` / `glbimages.py` / `nodebounds.py` are the independent numpy reference; `lampvis*.py`, `layout.py`, `numbers.py` are the placement scans.
Compile of the worktree sources against the real Unity DLLs [V]:
```
== PhantomHand.Runtime         errors=0 warnings=0   (DarkenController, PhModels live in the next one)
== PhantomHand.Presentation    errors=0 warnings=3   (the 3 pre-existing CS0618 in Ui/)
== PhantomHand.Editor          errors=0 warnings=0   (GlbReader, PropFit, PhantomModelImporter + .RiggedHand + .Props, CompactTestRun)
== Shell.Editor                errors=0 warnings=14  (14 pre-existing CS0618 FindFirstObjectByType, same count as the unmodified tree)
== PhantomHand.Tests.Editor    errors=0 warnings=0   (16 sources)
== PhantomHand.Tests.PlayMode  errors=0 warnings=0
```
(The unmodified worktree gave the same warning counts: baseline run before the first edit.)
Tests, real output of `dotnet run.dll <game dir> <fixtures>` [V]:
```
== ...GlbReaderTests          51 pass
== ...PropFitTests            14 pass
== ...DarkenControllerTests    3 pass, 3 SKIP (need the native engine: EveryMaterialSlot_..., UnlitAndEmissiveRenderers_..., WithoutRenderers_...)
== ...PhHandFitTests          30 pass   (incl. WrapperNames_AreTheFixedPhNames_InTheResourcesFolder with the 12 names)
DONE passed=98 failed=0 skipped(engine)=3 total=101
```
and the other 14 pure fixtures of the same assembly (Agency, DriftProbe, Finale, IllusionStrength, Module, PhantomHandParams, PhaseStateMachine, Questionnaire, StrokeDriver, StrokeScheduler, ThreatResponseAnalyzer, UiModel, WitnessCopy, WitnessSummary), as a regression check:
```
DONE passed=205 failed=0 skipped(engine)=0 total=205
```
Not run here (they need GameObjects or the scene): `UiPanelTests`, `PhRiggedHandTests`, `PhantomHandRigValidatorTest`, and the 3 skipped darken tests. `Color.linear/.gamma` are native calls in this Unity version (the first version of `Scale` failed offline with "ECall methods must be packaged into a system module"), which is why `Scale` is plain multiplication, like the ambient and fog scaling next to it.
What the tests cover: (a) the 7 real files: triangle totals (indexed from the raw JSON, plus non-indexed, plus the reader's total), vertices and triangles per material, material names and order, bounds against the numpy reference (+-0.15 mm), face normal vs vertex normals (every triangle but the degenerate lathe caps agrees; with the winding reversed, none does, so the check can fail), unit normals, embedded PNG signature and 512 x 512, emissive x strength = (1.6, 0.863, 0.311), BLEND glass alpha 0.25, doubleSided; plus small GLBs built in the test: x mirror + winding + v flip on a triangle, matrix and TRS nodes down a tree (order scale-rotate-translate, quaternion), non-uniform scale (inverse-transpose normals), a node that mirrors itself, ubyte / ushort / uint indices, no indices, interleaved `byteStride` and offsets, merging with index offsets, material defaults, 20 unsupported features each naming file and feature, broken files. (b) `PropFit`: 25 % per axis, cm-as-m, a lamp on its side, NaN, the brief's 1.22 m lamp refused, all 7 pivots and sizes on the real files, the window's 18 cm sill on +z, the brush tip from the bristle vertices after shifting the whole model, a brush upside down and a model without bristles refused. (c) `Scale`: identity, zero keeps alpha, RGB x level incl. emission > 1, monotonic.
Mutation check [V]: 14 deliberate breaks of `GlbReader` (no winding reversal, x not mirrored in positions / in normals, non-indexed primitives dropped, no v flip, matrix not transposed, byteStride ignored, mirrored node keeps the swap, emissive strength ignored, accessor byteOffset ignored, TRS scale on rows, indices not offset, normals by M instead of its inverse transpose, quaternion w sign): every one fails at least one test (1 to 16 each). The first run of the check found one survivor (TRS scale applied to rows instead of columns); `ATrsNode_ScalesFirst_ThenRotates_ThenTranslates` closes it, and `ANonUniformScale_TransformsNormalsWithTheInverseTranspose` covers the normal matrix.

## 6. Written, never run (needs Unity)
- `PhantomModelImporter.Props.cs` entirely: `AssetDatabase.ImportAsset`, `TextureImporter` settings, `SetAstc`, `Mesh.SetVertices / SetNormals / SetUVs / SetTriangles / RecalculateTangents`, `EditorUtility.CopySerialized`, `AssetDatabase.CreateAsset`, `Material` properties and keywords, `PhMaterials.FadeCopy`, `SaveWrapper` (array overload), `BrushNeedsRebuild`, and the changed `Build`, `AllPresent`, `EnsureWrappers`, `BuildBrush`.
- `PhantomHandSceneBuilder`: `Place`, `BuildWindowGlow` (`Mesh.GetSubMesh(i).bounds`), `HangLamp` (`Renderer.bounds`), the lights, `dark.renderers`, the new order (`EnsureWrappers` before the room), `CaptureModelShots` `room_eye`, `table_props`.
- `DarkenController`: `Capture` of the renderer list, `Renderer.SetPropertyBlock(block, slot)` and `SetPropertyBlock(null, slot)` (the null form is documented as clearing; the test `UnlitAndEmissiveRenderers_...` asserts `HasPropertyBlock() == false`), the three engine tests of `DarkenControllerTests`.
- All visual statements below.

## 7. For the Unity driver
Order: `recompile` (the new asmdef reference and 6 new `.cs` files) -> `PhantomModelImporter.RunBatch()` -> read the `[prop]` lines -> `BuildScene()` (expect `models=True`, `props=PH_FramedPicture+PH_Plant+PH_Window+PH_PendantLamp+PH_SingingBowl+PH_TeaCup`) -> `CaptureModelShots()` (13 pictures: the 11 old + `room_eye`, `table_props`) -> `CaptureShots()` (the `dark_probe` picture) -> `test EditMode` (filter `GlbReaderTests|PropFitTests|DarkenControllerTests|PhHandFitTests|PhantomHandRigValidatorTest`) and PlayMode `PhantomHandPresentationTests`.
Expected in the bake log [N, numbers V]: the 7 `[prop]` lines of section 4 (the brush one replaces `[brush]`), a handful of `texture ... -> NormalMap/ sRGB, max 512` and `-> Android ASTC_6x6 @ 512` lines on the first run (10 textures), no `FAILED`. A second `RunBatch()` writes no texture lines (idempotent). Afterwards `Models/Textures/` holds 10 PNGs, `Meshes/` 7 `PHP_*.asset`, `Materials/` 21 `PHP_*.mat`; `Meshes/PHM_Brush.asset` (0.98 MB) and `Materials/PHM_Brush.mat` are orphans of the old Meta brush (delete when happy).
Expected in the pictures [N]:
- `room_eye` (eye, straight ahead, 80 degrees vertical): front wall, the small framed picture (walnut frame, cream mat, beige canvas, faint glass tint) right of centre, the terracotta plant with green stems and leaves low on the left, the window at the far left edge (its far third: white frame, sill ledge, warm orange-brown glass), the pendant above the table centre: dome shade glowing cream-orange (unlit), seen from below with the yellow bulb inside, the cord going up out of frame; a dark ceiling. Darker room than before (key 0.80) with a warm pool on the table.
- `table_props`: the oxblood cushion with the brass bowl and the walnut striker on its near side, the cup and saucer to its left with the handle toward the right and the seat; a warm pool from above.
- `table`, `room`: the same plus the lamp and the ceiling edge; the brush pictures (`brush_contact*`): a round brush with a brass ferrule and a walnut handle instead of the paint brush, 19 cm.
- `dark_probe` (`CaptureShots`): only ruler and dot; shade, bulb and window glow black.
Likely trouble and what it means: a prop sunk or floating -> pivot / `TableTopY`; the window or picture showing its back or the sill inside the wall -> the yaw sign (window +90, picture 180); shade not glowing -> the material name test `Contains("Linen")` in `HangLamp`; table too hot / too dull -> `PendantLight` intensity 0.7 / range 2.2 and the key 0.80 are first guesses; ceiling spot above the lamp -> the point light has no shadows and shines through the shade (use a spot light or a smaller range); a pink prop -> the `PHP_*` material shader; a glass pane that is opaque -> `FadeCopy` keyword / queue.

## 8. Open items and risks
1. First-guess values to tune by eye: pendant light (colour, 0.7, 2.2), key 0.80, `PH_LampShade` tint, `PH_WindowDusk` colour, window Z 1.65, cup yaw 205.
2. The lamp's `MaterialPropertyBlock`s on a statically batched renderer: batches are split per renderer while dark; works in Unity but unverified here.
3. Colour: glTF linear factors are converted with `Color.gamma` (project colour space is Linear, `m_ActiveColorSpace: 1`); HDR emission (1.6, 0.863, 0.311) assumed to take the same conversion as other colour properties, and the build has HDR off (it clamps at 1).
4. `PH_Glass` (window, picture) is a transparent Lit surface: with the lights at zero it is black at 25 % alpha: invisible in the probe phases, as wanted.
5. The ceiling slab is new geometry the brief did not list; the walls (3 m) now stick 0.62 m above it, invisible from inside.
6. A first `EnsureWrappers()` from `BuildScene` re-bakes everything (hand included) if only the props are missing, as it already does for the hand; run `RunBatch()` first so the log is read separately.
7. `BrushRig.cs` still says "22 cm" in a comment.

## Changed and new files
Modified (6): `game/Assets/Games/PhantomHand/Editor/PhantomModelImporter.cs`, `game/Assets/Games/PhantomHand/Runtime/Presentation/PhMaterials.cs`, `game/Assets/Games/PhantomHand/Runtime/Scene/DarkenController.cs`, `game/Assets/Shell/Editor/PhantomHandSceneBuilder.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhHandFitTests.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhantomHand.Tests.Editor.asmdef`.
New (7): `game/Assets/Games/PhantomHand/Editor/GlbReader.cs`, `game/Assets/Games/PhantomHand/Editor/PropFit.cs`, `game/Assets/Games/PhantomHand/Editor/PhantomModelImporter.Props.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/GlbReaderTests.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PropFitTests.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/DarkenControllerTests.cs`, `logs/sessions/2026-10-08-PH-U-PROPS-run1.md`.

## 9. Merge and first run in the editor (Opus, 8 Oct 14:08-14:35)

The builder's diff was applied to the main checkout and run in the open editor (the builder could not: section 7). Real output, in order:

- `recompile`: `status: clean, errorCount: 0`.
- `PhantomModelImporter.RunBatch`: the seven `[prop]` lines, each `plausible`, sizes equal to `PropFit.Specs` to the millimetre
  (brush 0.0320 x 0.1930 x 0.0320, lamp 0.3840 x 0.8820 x 0.3840, plant 0.5631 x 0.5788 x 0.5717, window 1.0200 x 1.2000 x 0.1800,
  bowl 0.1535 x 0.0900 x 0.1775, cup 0.1440 x 0.0685 x 0.1440, picture 0.5000 x 0.4000 x 0.0250 m); 10 textures written
  (`ASTC_6x6 @ 512`); 12 wrappers under `Resources/PhantomModels`.
- `BuildScene`: `models=True, props=PH_FramedPicture+PH_Plant+PH_Window+PH_PendantLamp+PH_SingingBowl+PH_TeaCup`.
- Pictures looked at (`logs/sessions/screens/ph/models/`): `room_eye`, `room_eye_right`, `table_props`, `room`, `arm_eye`,
  `brush_contact`, `brush_contact_close`; and `u4/probe_holding_en`, `u4/witness_en_t4_5s`.
  Seen: window with the dusk glow on the left wall, plant, framed picture, cup and saucer, brass bowl on its cushion with the striker,
  the round brush with its bristle tip on the sleeve. In the dark probe only the text, the ring, the dot and the ruler are visible:
  shade, bulb and window glow are black.

Changes made on top of the builder's diff, each after looking at a picture:

1. `PhantomModelImporter.Props.cs`: the emission colour is no longer passed through `Color.gamma` (`_EmissionColor` is an HDR
   property, taken as linear), and the material's `globalIlluminationFlags` is set to `None`: a new material starts as
   `EmissiveIsBlack`, and URP's material validation then switches the `_EMISSION` keyword off.
2. `PhantomHandSceneBuilder`: `Wall_Right`, `Wall_Back` and their baseboards. With the ceiling in place the missing walls showed as
   a black void at the right edge of `room_eye`; a wearer who turns would look into it.
3. `LampShadeAboveTableM` 0.75 -> 1.10 (shade bottom at 1.85 m, ceiling at 2.73 m), pendant light 0.7 / 2.2 -> 1.3 / 2.6. At 0.75
   the shade sat on the top edge of the results panel in the seated view (`witness_en_t4_5s`).
4. `SceneStats()` (menu Tools/OPUS/PhantomHand Scene Stats). Output: `renderers=19, draw entries=31, triangles=11912, realtime
   lights=2; wrappers spawned at run time (PH_Hand+PH_Forearm+PH_Sleeve+PH_Brush+PH_Stone): draw entries=7, triangles=43105;
   together draw entries=38, triangles=55017` against the guide's budget of 100 draw calls and 300 000 triangles (UI panels not
   counted, nothing measured on a headset).
5. Comments: `BrushRig.cs` (19 cm), `PhaseStateMachine.cs` (D14, not D12).

Tests after the merge (before change 4 and the flinch check in `PH_FullRun`): EditMode 689/689, PlayMode Phantom Hand 26/26
(`PH_FullRun`: cues 112/114, 14 frames over 120 ms). Not done: nothing ran on a headset; frame rate with the second realtime light
is unmeasured; `PHM_Brush.asset` / `PHM_Brush.mat` (the Meta brush bake) are now unused but kept as the fallback's output.
