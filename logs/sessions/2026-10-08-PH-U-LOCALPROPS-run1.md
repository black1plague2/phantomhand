# PH-U-LOCALPROPS run 1 (2026-10-08, builder session): four Asset Store packs into the room at run time, nothing committed that depends on them. Code and offline tests only, Unity NOT driven

Worktree `H:\Chenta\phantomhand\.claude\worktrees\agent-a3b26c7c7f195487e` (base 5e2e44c, branch main). Nothing was run in Unity: no editor, no `tools/unity_mcp.py`, no lock file, no package change, nothing of Orchard Reach touched. The result is an uncommitted working-tree diff (4 modified, 5 new files + this log).
Tags: **[V]** verified this session (offline compile against the real Unity DLLs, offline NUnit run, mutation and negative controls), **[N]** written, never run in Unity.

## 0. Outcome
| Item | State | Evidence |
|---|---|---|
| `.gitignore`: `Models/Local/` and `Local.meta` | done | test `GitIgnore_...` + negative controls [V] |
| `PhantomModelImporter.Local.cs` (`BuildLocal`, menu "Tools/OPUS/Bake local Asset Store props") | written, compiled, **bake NOT run** | compile [V]; every Unity API call [N] |
| `PhLocalFit` (pure bake rules: axes, units, pivots, ranges, book stack) | done | 38 tests; 29 mutations, 28 caught, 1 equivalent [V] |
| `PhModels`: `LocalFolder`, `Books`, `Sideboard`, `LoadLocal`, `UseLocal`, local-first `Load` | done | compile [V]; `Resources.Load` itself [N] |
| `PhLocalProps` (Awake -> Spawn, Clear) | written, compiled, **never run** | pure `Plan` [V]; 4 Spawn/Clear engine tests written, skipped offline [N] |
| Scene builder: component + 2 slots, `CaptureModelShots` (Spawn/Clear, `room_eye_right` re-aimed, `table_left`) | written, compiled, **build NOT run** | compile [V]; clearance and field-of-view arithmetic [V]; scene result [N] |
| Tests | 38 + 13 pass offline, 6 need the engine | section 5 |

## 1. Things in the brief that are not as written (read this first)
1. **"same log, section 2" for the offline harness**: the harness is section 5 of `2026-10-08-PH-U-PROPS-run1.md` (section 2 is the file list). It was still in the session scratchpad; reused with the paths pointed at this worktree.
2. **Stone convention.** The brief says "largest extent 1 m (the same convention as `BuildStone`)". `BuildStone` normalises the MEAN diameter (`(x+y+z)/3`), not the largest extent. I took the explicit number: **largest extent = 1 m**, so ThreatDrop's 11 cm is the stone's length. The `[local]` line prints both (`largest extent 1 m, mean 0.xxx m`). For the mean convention change `PhLocalFit.StoneScale` (one line, `StoneScale_MakesTheLargestExtentOne_NotTheMean` pins today's choice).
3. **"meshes are referenced from the pack, not copied" cannot hold for `PH_Stone`.** `ThreatDrop.Build` reads only `MeshFilter.sharedMesh` and `MeshRenderer.sharedMaterial` of the wrapper and puts them on its own sphere primitive at scale 0.11; it ignores the wrapper's child transform. A wrapper that referenced the raw pack mesh would show at the pack's native size and offset. So the stone alone gets a normalised copy of its mesh, `Local/Meshes/PHL_Stone.asset` (inside the git-ignored folder, so never published). The table, books and sideboard keep the pack's meshes.
4. **`Load` returning the local wrapper would have put the pack table into the committed scene.** `PhantomHandSceneBuilder` builds the table with `PhModels.SpawnTable` -> `Spawn` -> `Load`. On the build PC that instantiates the local wrapper (with its pack mesh and material references) into `PhantomHand.unity`: exactly what the brief forbids. Fix: `PhModels.UseLocal` (default true, like `UseModels`); `BuildScene()` now sets it false in a try/finally and calls the old body, renamed `BuildSceneCore()`. `LoadLocal` and `PhLocalProps` honour it, so `UseLocal = false` is also the A/B switch for pictures.
5. **`PhLocalProps` had to go in `Runtime/Presentation`**, not `Runtime/Scene`: `PhModels` lives in `PhantomHand.Presentation`, which references `PhantomHand.Runtime` (where `Scene/` is), not the other way round.
6. **Where the local table is parented.** The brief says "under itself". The local table is a sibling of the scene's `PH_Table` (under `Environment/Table`, still below the component's object), because `CaptureModelShots` hides `GameObject.Find("Table")` for the side-curl shots and must hide both.
7. **The slot test needs the builder's constants**, so `PhantomHand.Tests.Editor.asmdef` gained one reference, `Shell.Editor` (the only asmdef edit; no cycle: `Shell.Editor` does not reference the tests).
8. **`room_eye_right` re-aim.** Old aim (2.45, 1.18, -0.6) puts a 1 m sideboard 49 to 23 degrees left of the view axis (half field 56 degrees) and the 2.5 m limit case out of frame. New aim (2.45, 1.0, 0.1), see section 4.
9. Not in the brief, added because the situation needs it (veto any of them): `LocalSideboardFlipFront` (the front of a file cannot be read from its bounds); a self-check inside the bake that measures the finished wrapper and refuses to save when it differs from the prediction (the one piece of maths, the quaternion, that no offline test can reach); removal of a stale local wrapper when its pack is missing or its bake is rejected (so the game falls back to the committed look instead of showing a half-broken one); `SetAstc(.., 1024)` on every texture of a local wrapper, like the Meta bakes (this edits the PACK's texture import settings, Android override only, on the build PC only); Read/Write on `Stone_3.fbx`'s import (needed to read its vertices, as `LoadGeo` does for the Meta stone); a test that scans every committed asset for the GUIDs of anything inside the five ignored folders.

## 2. Files
Modified (4): `game/.gitignore` (+3 lines, CRLF kept), `game/Assets/Games/PhantomHand/Runtime/Presentation/PhMaterials.cs` (`PhModels`: names, `UseLocal`, `Load`, `LoadLocal`; `All` untouched; +19/-1), `game/Assets/Shell/Editor/PhantomHandSceneBuilder.cs` (+33/-2 lines), `game/Assets/Games/PhantomHand/Tests/Editor/PhantomHand.Tests.Editor.asmdef` (+`Shell.Editor`).
New (5): `Editor/PhLocalFit.cs`, `Editor/PhantomModelImporter.Local.cs`, `Runtime/Presentation/PhLocalProps.cs`, `Tests/Editor/PhLocalFitTests.cs`, `Tests/Editor/PhLocalPropsTests.cs` (all under `game/Assets/Games/PhantomHand/`). No `.meta` written: Unity creates them (commit them with the patch). Untouched on purpose: `Packages/com.opus.sdk`, `Shell/Tests`, `Shell/Runtime`, `app`, `analytics`, `tools`, `contracts`, `CREDITS.md`, other docs.

## 3. Design
**Wrapper shape.** `PH_x` root (`ModelSlot`) -> `Fit` container -> a plain copy of the pack object (its own node chain kept: the importer's -90 X / unit scale lives on its root). The bake instantiates the copy at the origin, strips it (scripts, joints, colliders, bodies, shadows), measures the world box of its enabled mesh renderers (corners of each mesh's bounds, no readable mesh needed), lets `PhLocalFit` decide, and sets the container's `localScale` (along the SOURCE axes), `localRotation = Inverse(LookRotation(ZSrc, YSrc))` and `localPosition` exactly as `PhLocalFit.Fit.Map` defines them (q_wrapper = R (Scale * q) + Offset; the frame is always a rotation, `XSrc = YSrc x ZSrc`, never a mirror). Then it measures the finished wrapper and throws if it differs from `MappedBounds` by more than 2 mm.
**Axes.** Up is the source's Y (as authored), then Z, then X, the first that gives plausible numbers; of the other two the longer is the width. Table: a height more than 1.3 x the longer side is not a height; the table is stretched per axis to 1.2 x 0.7 x 0.75 m, refused when the stretch factors differ by more than 2.5x (the old lower bound on the height was dead logic: a height below 0.2 x the length already forces factors 3.1x apart; the mutation check found it and it was removed). Sideboard: ranges 0.4-2.5 / 0.3-2.2 / 0.2-0.9 m; the unit factors are tried in the order 1, x0.01, x100 (so a 80 x 50 x 40 file is "centimetres read as metres"), the size corrected at most once. Books: each laid on its thinnest side (a book already flat keeps its orientation), stacked bottom to top, yaw offsets 0/8/-6/11 degrees (positive turns +z toward +x, like `Quaternion.Euler(0, yaw, 0)`), pivot = bottom centre of the stack, rejected unless the stack is 5-40 cm in every extent, WARNING in the log line over the 30 cm the slot is cleared for.
**Materials.** URP Lit stays the pack's own. Anything else becomes `PHL_<Wrapper>_<Material>` (URP Lit, main texture with its tiling, colour, normal map, smoothness 0.15, metallic 0) in `Local/Materials`. The log line lists the kept URP materials with smoothness and metallic.
**Run time.** `PhModels.Load(name)` = local wrapper if `Resources.Load("PhantomModelsLocal/<name>")` finds one (and `UseLocal`), else the committed one. `PhLocalProps` serialises only `Slot { string wrapper; Vector3 position; float yaw }` (a reflection test guards that no object reference can ever be added). `Awake` -> `Spawn()`: for each slot whose local wrapper exists, `Instantiate` under itself at the slot pose, then `HideFlags.DontSave` on it and every child; if a local `PH_Table` exists and the scene has a `PH_Table` model, the local one goes to its pose under the same parent and the scene's is switched off. `Clear()` destroys what it made and restores the table's previous active state. No local wrapper: no object created, nothing switched, no log line.
**Scene.** `LocalSlots()` (public, builder constants): `PH_Books` at (-0.40, 0.75, 0.62) yaw 0; `PH_Sideboard` at (2.45, 0, 0.90) yaw 270. Yaw 270 sends the wrapper's front (+z) to -x, into the room; the wrapper's pivot is the middle of the back face's bottom edge, so back on the wall (x = 2.45, `WallRightInnerX`). The committed scene gets `env.AddComponent<PhLocalProps>()` after the static flags loop, nothing else.

## 4. Placement arithmetic [V] (plan view, metres; same zone definitions as the bowl and cup of PROPS run 1; scripts `layout_books.py`, `aim_right.py` in the scratchpad)
Zones from the builder's constants: real-arm area x .136-.224, z .15-.58 (ArmX 0.18 +- 0.044, ElbowZ to ElbowZ + ForearmLen + HandLen); virtual arm x -.014-.074 (ArmX - OffsetM); ruler x +-0.5, z .325-.375; table x +-0.6, z .12-.82.
Books, centre (-0.40, 0.62), square footprint:
| footprint | real arm | virtual arm | ruler | table edge (left / far) |
|---|---|---|---|---|
| 0.30 m (design, `LocalBookDesignM`) | **0.386** | **0.236** | **0.095** | 0.050 / 0.050 |
| 0.25 m (4 books 22 x 15 cm, yaws 0/8/-6/11 give 0.245 x 0.189) | 0.411 | 0.261 | 0.120 | 0.075 / 0.075 |
| 0.40 m (the bake's limit) | 0.336 | 0.186 | 0.045 | 0.000 / 0.000 (on the edge) |
A grid search over the far-left corner (edges >= 3 cm, maximise min(ruler gap, 2 x edge)) gives exactly this centre. The test pins >= 0.30 / 0.20 / 0.09 and an edge margin >= 0.04 for the design footprint, and a control (a stack on the virtual arm) proves the gap arithmetic reports 0.
Sideboard: pivot (2.45, 0, 0.90). At the largest size the bake accepts (2.5 m wide, 0.9 deep) it spans z -0.35 to 2.15 and x 1.55 to 2.45: inside the room (x +-2.45, z -1.55 to 2.65), 0.95 m from the table edge (0.6), >= 1.05 m from the ruler and the arms; the window (z 1.14-2.16) is on the other wall.
`room_eye_right` (eye (0, 1.18, 0.02), 1600 x 900, vertical fov 80, so horizontal half field 56.2 degrees), angles of the sideboard box corners from the view axis:
| case (w x d x h) | old aim (2.45, 1.18, -0.6) | new aim (2.45, 1.0, 0.1) |
|---|---|---|
| 1.0 x 0.45 x 0.8 | h -48.8..-23.0 in frame | h -32.5..-6.7, v -26..-4.5 in frame |
| 0.9 x 0.4 x 2.0 (wall unit) | -47.2..-24.2 | -31.9..-7.8, v -26..+26 |
| 1.6 x 0.5 x 0.9 | -54.9..-16.1 | -38.7..0.0 |
| 2.5 x 0.9 x 2.2 (limit) | -68.2..-0.8 **partly out** | -53.5..+16.1, v -33..+38 in frame |
The back-right corner that closes the room stays in frame (h +36, v -22..+37) and so does the table's far right corner (h -50).

## 5. Verification, honestly
Harness (scratchpad `...\9f034333-...\scratchpad\localprops\`, outside the repo): `mkrsp3.py` + `build.ps1` (csc 10.0.401 over the MAIN checkout's Bee response files, with the WORKTREE sources; the test assembly also references `Shell.Editor`), `run\Runner.cs` (reflection runner over the real `nunit.framework.dll`; `[Category("UnityEngine")]` = SKIP; I added `--all`/`--quiet`), `runtests.ps1`, `mutate_local.py`, `negcontrol.py`, `layout_books.py`, `aim_right.py`.
Baseline before the first edit [V] (unmodified tree): compile 0 errors everywhere (Presentation 3 and Shell.Editor 19 `CS0618` warnings: HEAD has moved on since the old log's 14); tests `DONE passed=303 failed=55 skipped(engine)=3` (the 55 are `PhantomHandRigValidatorTest` 22, `PhRiggedHandTests` 15, `UiPanelTests` 18: they need the engine, `UnityEditor` or `UnityEngine.UI`).
Final compile [V]:
```
== PhantomHand.Runtime : errors=0 warnings=0 dll=79360 bytes
== PhantomHand.Presentation : errors=0 warnings=3 dll=89600 bytes
== PhantomHand.Editor : errors=0 warnings=0 dll=96256 bytes
== Shell.Editor : errors=0 warnings=19 dll=86528 bytes
== PhantomHand.Tests.Editor : errors=0 warnings=0 dll=254976 bytes
== PhantomHand.Tests.PlayMode : errors=0 warnings=0 dll=36864 bytes
```
Final tests, real runner output [V]:
```
   -> PhLocalFitTests: pass=38 fail=0 skip(engine)=0
   -> PhLocalPropsTests: pass=13 fail=0 skip(engine)=6
   (the 20 other fixtures: unchanged; PhHandFitTests 30, PropFitTests 14, GlbReaderTests 51, DarkenControllerTests 3 + 3 skipped, ...)
DONE passed=354 failed=55 skipped(engine)=9 total=418
```
354 = 303 + 38 + 13. The failing set is **identical** to the baseline's (compared test by test). The 6 skipped new tests need the engine: `Load_FallsBackToTheCommittedWrapper_...`, `Load_GivesTheCommittedWrapper_WhenTheLocalOnesAreSwitchedOff_...`, `Spawn_WithNoLocalWrapper_CreatesNothing_AndChangesNothing`, `Spawn_PutsTheWrappersUnderItself_NeverSaved_AndClearTakesThemAway`, `Spawn_PutsALocalTableAtThePoseOfTheScenesTable_...`, `Spawn_LeavesTheBoxTableAlone_...`.
What the 51 new tests cover: `PhLocalFit` (every frame is a rotation, never a mirror; axis choice; the table maps to the box with its top at y = 0 for upright, long-along-z, far-from-origin, lying and "y too long" sources; stretch limit; sideboard pivot/front/width-along-z/cm x0.01/x100/Z-up/flip/limits both ways; stone scale; book stack height, centring, standing and flat books, yaw sign against `Quaternion.Euler`, 5-40 cm both ends), the slot arithmetic, `Plan` (nothing exists -> nothing happens), the reflection guard, the `.gitignore` coverage, the GUID scan, `LocalResourcesDir` = where `Resources.Load` looks.
Mutation check [V] (`mutate_local.py`): 29 deliberate breaks of `PhLocalFit` and `PhLocalProps.Plan` (frame mirrored, scale ignored, wrong pivots, up order, missing x100 / x0.01, height and squeeze rules off, width = shorter side, thin-axis tie, yaw sign, books not stacked, flat book turned, stone by smallest extent, exclusive ranges, axis-name sign, zero counted as finite, stack limits ignored, unit ignored, flip mirrored / absent, table factors on wrong axes, plan ignoring existence / names / second slot): **28 make 1 to 16 tests fail; 1 survives and is an equivalent mutant** (trying x100 before x0.01 changes nothing: no box fits at two unit factors, they are 100x apart and the ranges span 6x). The first run also found the dead lower height rule (section 3) and one skipped pattern, fixed.
Negative controls of the two file-scanning tests [V] (`negcontrol.py`, a fake project as working directory): clean project passes; a scene pointing at a `Books` asset FAILS; a committed material pointing at a local wrapper FAILS; a prefab INSIDE the pack pointing at the pack passes (allowed); a reference buried in binary noise FAILS; an ignore file without the `Local` lines, without the `Furniture_ges1` lines, or with `Locale` FAILS.
**Only the editor can verify** [N]: that `BuildLocal` runs at all (`AssetDatabase`, `ModelImporter`, `PrefabUtility.SaveAsPrefabAsset`, `Mesh` API, `Instantiate` of an FBX, `Material` keywords, `SetAstc` on pack textures); what the packs really measure (the axes, scale, front, materials are unknown here); that the quaternion in `ApplyFit` matches `PhLocalFit.Fit.Map` (derived by hand and checked on one case; the in-bake self-check will refuse a wrong one); `Resources.Load`, `Instantiate`, `HideFlags.DontSave` and `SetActive` behaviour of `PhLocalProps` (the 4 Spawn/Clear engine tests and the 2 `PhModels.Load` ones are written, not run); `CaptureModelShots` and every picture; the rebuilt scene (that it holds only the component).

## 6. For the Unity driver
Order: `recompile` (5 new `.cs`, one asmdef reference; expect clean) -> `PhantomModelImporter.BuildLocal()` (or the menu) -> read the `[local]` lines -> `PhantomHandSceneBuilder.BuildScene()` (the result string is unchanged; check `Environment` now carries `PhLocalProps` with 2 slots and `Table/PH_Table` is still the Meta table) -> `CaptureModelShots()` (**15 pictures**: the 14 of the merged PROPS run, including `room_eye_right`, + `table_left`) -> EditMode tests, filter `PhLocalFitTests|PhLocalPropsTests|PhHandFitTests|PropFitTests|PhantomHandRigValidatorTest` (all of it must be green, including the 6 that are skipped offline and `NoCommittedAsset_ReferencesAPackOrALocalWrapper`, which is the real check after the rebuild) -> PlayMode `PhantomHandPresentationTests`.
First `BuildLocal` also logs `[local] Stone_3.fbx import: Read/Write enabled` and a few `texture ... -> Android ASTC_6x6 @ 1024` lines; a second run logs neither.
What the `[local]` lines should say (numbers depend on the packs):
- `[local] PH_Stone <- Stone_3.fbx: bounds ... scale applied x<k> on every axis (largest extent 1 m, mean 0.5-0.9 m, centred), up axis n/a (a stone), materials converted 1.., mesh PHL_Stone baked (N verts, 1 submesh)`. A rock is not a ball: mean well below 1 is normal.
- `[local] PH_Table <- DWP_Table_01_URP_Metallic.prefab: ... per source axis (fx, fy, fz) (width xA on x|z, height xB on y, depth xC on z|x; top surface at y = 0, 1.2 x 0.7 x 0.75 m), up axis +y of the source (width axis .., depth axis ..), materials converted 0, kept URP Lit <names> (smoothness s, metallic m)`. Factors near 1 (within 0.5 to 2) mean the pack table is table-shaped; `REJECTED (stretch factors ...)` means it is not and the Meta table stays.
- `[local] PH_Books <- N of 4 books (book_0001a, ...): stack bounds ... size (x, y, z) m (5-40 cm each) ... yaw offsets 0/8/-6/11, up axis per book: the thin axis +y,..`. Over 0.30 m wide: the WARNING says the slot's clearances no longer hold.
- `[local] PH_Sideboard <- tumba_fur.FBX: ... scale applied 1 (authored size), up axis +y of the source (width axis x, depth axis z, front = source +z (authored forward); baked size (w, h, d) m, back face on wrapper z = 0, bottom at y = 0)`. If `tumba_fur` is rejected the line says why and `sek1.FBX` is tried (a tall wall unit: height up to 2.2 m fits under the 2.73 m ceiling).
- A missing pack: `[local] <name>: <path> missing, skipped`. Nothing else may say `FAILED`.
What the pictures should show: `table` / `arm_eye`: the pack table in place of the Meta table, same top height, the arm resting as before; `room_eye_right`: the sideboard against the right wall on the floor, front to the room, the room's back-right corner at the right edge, the table's far right corner at the left edge; `table_left`: a small slightly-turned stack of books on the far left corner of the table, not over the edge; `stone_telegraph` / `stone_impact`: Stone_3 (11 cm long) instead of the Meta stone, textured.
Likely trouble and what it means:
- **Pink or untextured object**: the material kept as URP Lit but with a custom shader, or the conversion found no `_MainTex` / `_BaseMap`; read `materials converted n, kept URP Lit ...`.
- **Pack table black, very glossy or mirror-like**: `URP_Metallic` with a high metallic value and no reflection probe or skybox in this room (reflection intensity 0.3). The kept-material values in the line show it; fix by a local material copy with metallic about 0 (the committed scene test wants smoothness <= 0.1 only for the committed table).
- **Sideboard stands with its back to the room**: the front of the file is not its +z. Set `PhantomModelImporter.LocalSideboardFlipFront = true`, bake again. Do **not** change the slot's yaw: the pivot is on the face opposite the assumed front, so yaw 90 would put the body through the wall.
- **Sideboard / table lying on its side**: Y is not up in the file and the bounds still passed the ranges; the line's `up axis` and bounds show it; needs a code change (`PhLocalFit.UpOrder`).
- **`self-check failed for PH_x: measured min .. max .., expected ..`**: the container transform did not land where `PhLocalFit` predicted; nothing was saved. Suspect `ApplyFit` (`Quaternion.Inverse(Quaternion.LookRotation(ZSrc, YSrc))`); the message gives both boxes.
- **`REJECTED (...)`**: the reason and the measured bounds are in the line; an earlier wrapper of that name is removed (`(the wrapper of an earlier bake was removed)`).
- **`has no visible mesh`**: the pack object has only disabled or skinned renderers. **`the mesh has no readable vertices`**: Stone_3's Read/Write did not stick; reimport and bake again.
- **A prop at the wrong place in play**: slots are world positions of the scene root's frame (identity); `[PhLocalProps] PH_Books, PH_Sideboard ...` in the console at scene start proves Spawn ran (no line = no local wrapper found). With a pack table the line says `PH_Table ... (the scene's table is switched off)`.
- **The probe phases**: the local props are lit URP materials and go dark with the lights; a pack material with emission or an unlit shader would glow. `CaptureShots()`' `dark_probe` does not spawn them; check with `PhLocalProps.Spawn()` then `SetDark`.
- **`NoCommittedAsset_...` fails after `BuildScene`**: something in the scene or a committed asset points into a pack folder; the message lists file -> meta. `PhModels.UseLocal` must be false during the build; look for a path that does not go through `BuildScene()`.
A/B: `PhModels.UseLocal = false` gives the committed look in any capture.

## 7. Not done / open
1. Nothing ran in Unity (section 5). No headset, no frame-rate or draw-call figure for the packs (`SceneStats()` counts Stone and Table through `PhModels.Load`, but not Books and Sideboard; the Quest budget is 100 draw calls / 300 k triangles, unknown until baked).
2. `CaptureDemoSequence`, `CaptureU3Shots` and `CaptureShots` (incl. `dark_probe`) do not call `Spawn()`; pitch frames from them show the committed table and no props. Add the two lines if the pitch should show the pack look.
3. The sideboard's front and the up axis are decided from bounds only (section 6).
4. The 4 books use fixed yaw offsets and the order a, b, c, d; no sorting by size.
5. Pack texture import settings are edited (Android ASTC cap) and `Stone_3.fbx` is made readable: both live in the ignored folders, but they are changes to the packs' `.meta` files on the build PC.
6. `PhModels.UseLocal` is a static: an editor session that sets it false and forgets leaves the local props off until the next domain reload.

## Changed and new files
Modified (4): `game/.gitignore`, `game/Assets/Games/PhantomHand/Runtime/Presentation/PhMaterials.cs`, `game/Assets/Shell/Editor/PhantomHandSceneBuilder.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhantomHand.Tests.Editor.asmdef`.
New (6): `game/Assets/Games/PhantomHand/Editor/PhLocalFit.cs`, `game/Assets/Games/PhantomHand/Editor/PhantomModelImporter.Local.cs`, `game/Assets/Games/PhantomHand/Runtime/Presentation/PhLocalProps.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhLocalFitTests.cs`, `game/Assets/Games/PhantomHand/Tests/Editor/PhLocalPropsTests.cs`, `logs/sessions/2026-10-08-PH-U-LOCALPROPS-run1.md`.

## 9. In the editor (orchestrator session, 8 Oct 19:36-19:45): bake, scene, pictures

Everything marked [N] above that the game needs was run in the open editor through `tools/unity_mcp.py`.

| Step | Result |
|---|---|
| Compile + EditMode with the five new files | 0 errors; 750/750 (693 before) |
| `PhantomModelImporter.BuildLocal`, first run | `PH_Stone` from `Stone_3.fbx` (x3.5866, 123 verts); `PH_Table` from the Dark Wave table (source 1.242 x 0.941 x 2.442 m, squeezed per axis to 1.2 x 0.7 x 0.75 m: width x0.491, height x0.797, depth x0.564); `PH_Books` 4 of 4 (stack 0.273 x 0.148 x 0.200 m, authored size); `PH_Sideboard` **rejected**: `tumba_fur.FBX` 3.973 x 3.393 x 1.617 and `sek1.FBX` 3.579 x 6.608 x 1.462 fit no range at x1, x0.01 or x100 |
| Fix 1: the furniture pack is in feet | `PhLocalFit.UnitFactors` gained 0.3048. `tumba_fur` is then a 1.211 x 1.034 x 0.493 m chest of drawers and is baked. One test case pinned the old list (a 2.51 cube is a 0.77 m cube in feet): now a 10 m cube |
| Fix 2: the stone came out white | The FBX carries an untextured URP stand-in of the pack's material, and the bake kept it as "URP Lit". The stone bake now takes `Assets/Free/Stones/Materials/<name>.mat` (Standard shader, with its pictures) as the source, which is converted like any non-URP material. A general guard was added too: a URP Lit material with nothing in `_BaseMap` and a picture in `_MainTex` is converted, not kept |
| `PhantomHandSceneBuilder.BuildScene` | "built Assets/Scenes/PhantomHand.unity ... models=True, props=PH_FramedPicture+PH_Plant+PH_Window+PH_PendantLamp+PH_SingingBowl+PH_TeaCup"; the scene gained the `PhLocalProps` component with its two slots and nothing else from the packs (the two file-scanning tests are part of the 750) |
| `CaptureModelShots`, pictures looked at | `room_eye`: pack table, book stack on the far left corner, cup and bowl on the right, chest at the right edge. `room_eye_right`: the chest stands on the floor against the right wall, drawers to the room. `table_left`: the four books lie flat on the table top. `stone_impact`: a grey textured rock on the hand. `table`: the table's frame and legs, arm and sleeve unchanged |

Not checked: the look in a headset; the frame rate with the pack table (its five textures are capped at 1024, ASTC 6x6); the second
furniture file (`sek1`, a 1.09 x 2.01 x 0.45 m wall unit in feet) is not used.

