# PH-U-MODELS run 3 (2026-10-08, driver session): the degenerate hand, root cause and fix (Unity NOT driven: blocked by the permission system)

Continues `2026-10-08-PH-U-MODELS-run2.md`. Tags: **[V]** verified in this session (file / log read, arithmetic, standalone compile, standalone test run), **[I]** inference, **[N]** written, never run in Unity.

## 0. Outcome in four lines
- **Root cause found and evidenced [V]**: `SkinnedMeshRenderer.BakeMesh(mesh, true)` returned vertices in unscaled mesh units (the hand ~1.4 cm) for a renderer whose lossy scale is 100; the importer then did `position + rotation * v`, which collapsed the whole hand onto the renderer's position. Everything after that (the 0.512 ratio, the "rescale", the speck-sized bounds, the wrong frame) followed from it.
- **Fix written [N]** (6 files, section 3): explicit CPU skinning (no BakeMesh), a hard consistency check, a plausibility gate that sends a failed bake to the glove fallback instead of saving a speck, removal of the two empty Camera / Light GameObjects, and an arm-side gate so a degenerate wrapper can never be the default hand.
- **Not done**: no re-bake, no pictures, no `test EditMode` / `test PlayMode` run in Unity. `python tools/unity_mcp.py ...` was **denied by the permission system** ("Modify Shared Resources") on the first call, and edits to the shared checkout are refused by the worktree isolation guard. A coordinator message does not count as the user's permission, so I stopped there (section 5).
- The lock `game/.ph_unity.lock` I wrote at 11:57 was removed again (section 5).

## 1. Evidence for the root cause [V]
Editor.log (the `[hand]` block of the coordinator's `RunBatch()`), the saved wrapper `PH_Hand.prefab` (YAML) and arithmetic:

1. The saved wrapper shows how Unity imported the file: `Armature` has **local scale 100** and rotation -90 about x; the bones' local translations carry the 0.01 unit factor (e.g. `index_Ctrl_03.R_end` local y 0.0018694 = 0.1869 x 0.01), so bone world positions are metres. The skinned-mesh node `Hand` has **local scale (100, 100, 100)**, position **(0.19168, 0.79780, 0.66557)**. The same scale 100 is what the log printed as `renderer lossyScale (100.000, ...)`. A mesh whose node scale is 100 and whose hand is 1.4 m in the world has vertex data of ~1.4 cm: the 0.01 factor is baked into the vertices, not into the node scale.
2. The coordinator's numbers: `farthest vertex (0.3734, 1.4086, 1.2369)` was printed **after** the importer's "rescale by 1.9537 about the wrist". Undoing it: wrist + (vertex - wrist) / 1.9537 = **(0.1911, 0.8056, 0.6614)**. That is within **0.9 cm** of the renderer position (0.1917, 0.7978, 0.6656): the whole baked mesh sat inside a ~1.5 cm cloud around the renderer, the size of an unscaled hand.
3. Reach along wrist -> middle end bone: the cloud's farthest point projects 0.72 m on the bone direction (the renderer is 0.72 m from the wrist along it), hence "0.512 x the bone scale" with a bone reach of 1.42 m. No scale convention of BakeMesh is involved after that: it is simply "vertices not scaled by 100".
4. The bones were right: `wrist (0.0000, 0.1733, 0.0579)`, `index knuckle (0.0415, 0.8016, 0.3340)`, `pinky knuckle (0.0977, 0.7164, -0.1272)`, `middle end bone (0.1534, 1.5687, 0.2501)` equal the FBX bind pose x 0.01 with x mirrored (my Python prediction was (-0.1534 ...) in file space: Unity space = (-x, y, z), exactly the "mirrorX" emulation of run 2). So `RestoreBindPose` works, the frame maths would have worked on a correct tip, and the fit bounds of the speck came from the garbage vertices only.
5. Other findings in the same prefab: 75 transforms = PH_Hand + RiggedHand + `handRig_02` + Armature + Hand + 68 bones + **an empty `Camera` and an empty `Light` GameObject** (Unity keeps the FBX nodes as empty objects when importCameras / importLights are off; the `GetComponentsInChildren<Camera>` removal found nothing). The self-test passed because it only moves bones.
6. Pose before the restore: the log says 42 of 48 bones rotated, moved up to 22.3 cm. That is less than the 40-60 cm the Properties70 fist would need, so what Unity instantiates is not the Properties70 fist (my run-2 inference [I] was wrong on that point; the restore is still needed and harmless).

## 2. What was wrong in my run-2 design
I replaced `BakeMesh(baked)` + `TransformPoint` by `BakeMesh(baked, true)` + `position + rotation * v` in the last revision "to avoid the scale ambiguity", and added a silent rescale that hid the symptom (it printed a WARNING but kept going). Both are removed.

## 3. Changes (worktree copies; the main tree still has run-2 code)
| file | change |
|---|---|
| `Runtime/Presentation/PhHandFit.cs` | `SkinVertices(vertices, boneWeights, bone.localToWorld * bindpose matrices)` (pure CPU skinning, world space); `BoundsPlausible(min, max, expectedLen, out why)` (fingertip at 0.19 +-1 cm, wrist end in -6..+1 cm, 9-20 cm wide, 3.5-9 cm thick, thumb side -x wider); `DefaultHandLenM = 0.19` |
| `Runtime/Presentation/PhModelInfo.cs` | `boundsMin`, `boundsMax` (written by the importer; zero = never recorded) |
| `Runtime/Presentation/VirtualArmRig.cs` | `HandModelUsable()` in `Build`: a rigged hand wrapper without plausible recorded bounds (including the degenerate wrapper currently on disk) is ignored and the whole arm stays procedural; a glove wrapper without recorded bounds is taken as is |
| `Editor/PhantomModelImporter.RiggedHand.cs` | `SkinnedWorldVertices` (CPU skinning) replaces `BakedWorldVertices`; logs the skinned mesh bounds and the skin-vs-end-bone reach (expected ratio 1.001) and **throws** when they differ by more than 3 % (the glove fallback then takes over) instead of rescaling; removes the empty `Camera` / `Light` GameObjects (log: expected 2); `BoundsPlausible` gate before saving; records bounds in `PhModelInfo`; the self-test also CPU-skins the saved wrapper and logs plausible / WARNING |
| `Editor/PhantomModelImporter.cs` | glove path: records its bounds and refuses to save an implausible glove (then no wrapper is written and the arm keeps its procedural hand) |
| `Tests/Editor/PhHandFitTests.cs` | +3 pure tests: `SkinVertices_BlendsTheBoneMatrices_AndRenormalisesTheWeights`, `SkinVertices_OfTheRunTwoSetup_GivesAWorldSizedHand_NotASpeckAtTheRenderer` (scale 100 renderer, 1.4 cm mesh units -> 1.4375 m; the old formula gives a < 2 cm speck), `BoundsPlausible_RejectsTheSpeckOfRunTwo_AndAcceptsTheFittedHand` (also accepts glove-like bounds) |

Expected `[hand]` lines after the fix (Python numbers of run 2, unchanged): `skinned mesh bounds (import space) size about 0.417 x 1.481 x 1.019`, `skin reach ... ratio 1.001`, `empty Camera / Light placeholder objects removed: 2`, `frame ... wrist -> fingertip 1.4215 ..., scale 0.13366`, `final bounds min (-0.0757,-0.0354,-0.0120) max (0.0627,0.0223,0.1900)` (after dx -0.0007, dy +0.0009), `thumb on -x OK`, `self-test: saved wrapper skinned bounds ... plausible`.

## 4. Verification, honestly
Standalone compile of all five assemblies from the worktree sources with the same harness as run 2 (csc 10.0.401, Bee rsp of the main checkout, Unity DLLs): Presentation (19 sources), Editor (3), Shell.Editor (8), Tests.Editor (9), Tests.PlayMode (1): **no errors, no new warnings**.
Pure tests, real NUnit under .NET 10:
```
> dotnet bin\run.dll Opus.Games.PhantomHand.Tests.PhHandFitTests
  ... PASS (30 cases, incl. SkinVertices_Blends..., SkinVertices_OfTheRunTwoSetup..., BoundsPlausible_Rejects...)
DONE passed=30 failed=0 total=30
```
Written, never run: everything that touches Unity objects (`SkinnedWorldVertices`, the importer path, `HandModelUsable`, the self-test), and every picture. I do not know yet that the hand looks right.

## 5. What blocked the rest, and what to do next
- First action of the driver session: `python tools/unity_mcp.py --help` -> *"Permission for this action was denied by the Claude Code auto mode classifier. Reason: [Modify Shared Resources]"*. Instructions in the tool result: do the rest, then stop and ask the user. I did not retry, and I did not reach the bridge another way (curl, python import): that would bypass the denial.
- Second: `Edit` on `H:\Chenta\phantomhand\...` -> *"This agent is isolated in the worktree ... Edit the worktree copy of this file instead of the shared-checkout path."* (the coordinator's "work only in the main tree" cannot be followed with the Edit tool). The six files above are therefore in the worktree, not in the main tree.
- The lock: written at 11:57 (`MODELS Sonnet-driver ...`) before the first denial, removed at the end of this session.
- **To finish, someone who is allowed to drive Unity** (the user adding a PowerShell permission rule for `python tools/unity_mcp.py`, or the coordinator running it) should: (1) copy the six files over the main tree (worktree path `H:\Chenta\phantomhand\.claude\worktrees\agent-aeec594e4e861c042\game\Assets\...`; line endings differ, harmless); (2) `python tools/unity_mcp.py recompile`; (3) `RunBatch()`, read the `[hand]` lines against section 3; (4) `BuildScene()` (must end `models=True`), `CaptureModelShots()`, open the PNGs; (5) `test EditMode` and `test PlayMode "PhantomHandPresentationTests"`. If the rigged bake throws, the glove fallback runs for the first time (also never run): look at its `[hand] bounds` line and the arm's `[VirtualArmRig]` warning; an implausible hand now means a procedural arm, never a speck.
- Until the six files are in the main tree the default arm still uses the degenerate `PH_Hand.prefab` on disk (the run-2 code has no gate). Deleting that prefab (or applying the delta) fixes it; I did not delete it (hard deletion of a file needs the user's decision).

## 6. Open items that are not fixed by this
Same as run 2 section 13 (thumb 1.5-2.5 cm below the table plane at bind, fingers floating 0.6-1.2 cm, curl digging into the table, normal-map green channel, table fit), plus: the table / forearm / sleeve / brush / stone wrappers written by the coordinator's run were not looked at by me (no pictures).

## 7. Finish in the open editor (Opus, 8 Oct 12:15-12:40)

The six files of section 4 were copied into the main tree (line endings kept) and driven through `tools/unity_mcp.py` by the main session.

- Bake 1 (CPU skinning): `[hand] skin reach ... ratio 1.003`, scale 0.13366, `final bounds (wrapper m): min (-0.0758, -0.0353, -0.0120) max (0.0626, 0.0224, 0.1900) (hand length 0.1900, width 0.1384, thickness 0.0576)`, `thumb on -x OK`, self-test `plausible`, 15 of 15 joints bound.
- Pictures showed two more defects, both from one cause: `LoadGeo` multiplied by `root.worldToLocalMatrix`, which cancelled the -90 X / x100 that the Meta prefabs keep on the ROOT object (the mesh sits on the root). Everything was measured in raw FBX space (Z up, 2 cm): the table lay on its side, the brush came out 59 cm long with its width as the long axis, the sleeve texture was torn. Fix: keep the root's rotation and scale (`PhantomModelImporter.cs`, `LoadGeo`). After it: `[997491_L] ... world bounds size (0.590, 2.006, 0.667)`, `[brush] ... bounds min (-0.037, 0.000, -0.031) max (0.036, 0.220, 0.034)`, table upright.
- `CaptureModelShots`: the three curl shots were byte-identical (edit mode never re-skins between renders). `forceMatrixRecalculationPerRender` on the arm's skinned renderers fixes the shots; the dust particles are cleared before the table and room shots.
- `BuildScene` deleted and re-copied the scene, which gave `PhantomHand.unity` a new GUID on every rebuild (build settings kept the old one). It now puts the scene's `.meta` back; GUID stays `66d18b6d...`.
- Tests after the last change: `DONE EditMode passed=514 failed=0`, `DONE PlayMode passed=20 failed=0` (filter `PhantomHandPresentationTests`). The full-run test (the only one that loads the scene) was not re-run here.
- Pictures looked at: `logs/sessions/screens/ph/models/` (11 PNG): rigged hand with its skin texture on the sleeve and forearm, table upright, 22 cm paint brush at motor A, stone above and on the hand, fingers closed at curl 1.

Open (cosmetic, not fixed): the forearm stump is about 15-25 % lighter than the back of the hand (the tone is the average over the whole hand texture, palm included); the brush handle leans toward the fingers and hides part of the hand at the start of a stroke; closed fingers dip below the table plane; the hand's wrist cut meets the sleeve cuff with no overlap.
