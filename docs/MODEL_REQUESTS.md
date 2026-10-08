# Model Requests — Orchard Reach / Shell (Track U)

Everything below is currently a **ProBuilder/primitive placeholder** (or, where noted, not yet built) under
`Assets/Art/Placeholders/`. Each placeholder prefab carries a `ModelSlot` component documenting the pivot,
scale, and forward axis a real model must match so it can be dropped in without touching gameplay code (the
grasp/placement math reads collider bounds and named child transforms, not mesh geometry).

**How to drop in a real model:** import the FBX/GLB under `Assets/Art/Models/<id>/`, drag it into the matching
placeholder prefab as a child of the `ModelSlot` root (replacing/hiding the placeholder mesh renderer), keep the
existing collider/anchor child objects at the same local positions, and re-bake lighting/reflection probes if the
new mesh is emissive-lit. No C# changes should be needed — `IGameModule`/`TargetPlacement` only look at the
`ModelSlot` root transform and named anchors, never the renderer.

| id | description | where used | real-world size (m) | pivot | forward axis (+Z) | max tris (Quest) | textures | format | animation | count/variants |
|---|---|---|---|---|---|---|---|---|---|---|
| `fruit_apple` | Round reach/grasp target, mid-size red apple | `OrchardReach` target spawn point (one instantiated per trial at the sampled azimuth/elevation/reach%) | 0.07 (diameter) | center of mass (geometric center) | none (rotationally near-symmetric; forward = stem-up local +Y) | 500 | Base Color + Normal + Roughness/Metallic (ORM packed), 512×512 | GLB | none (static); a subtle idle wobble/highlight is done in shader, not animation | 1 base mesh + 2 color variants (red, green) for future distractor contrast |
| `basket` | Placement target the player releases fruit into | `OrchardReach` basket at home position (fixed, within reach envelope origin) | 0.30 (rim diameter) × 0.22 (height) | bottom-center (floor contact point) | opening faces the player, local +Z = out of the basket toward the user | 1500 | Base Color + Normal, 1024×1024, woven-basket look | GLB | none | 1 |
| `orchard_table` | Home surface the basket sits on, also the seated calibration reference height | `Shell`/`OrchardReach` scene, under the basket | 0.60 (top height) × 0.8×0.5 (top footprint) | top-center of the tabletop (so placement code can anchor "on the table" directly) | local +Z = long edge facing the player | 2000 | Base Color + Normal + Roughness, 1024×1024, light wood | GLB | none | 1 |
| `orchard_tree` | Background dressing only (branches optionally hold non-grasped decorative fruit, not the trial targets) | Orchard Reach environment background, 1.5-2m from the player, non-interactive | 2.5 (height) | base of trunk at ground contact | none (radially symmetric enough) | 6000 (LOD0), can drop to a billboard/impostor for Quest if over budget | Base Color + Normal, trunk 1024×1024 + a shared leaf-card atlas 512×512 | GLB | none | 1, can reuse the same mesh mirrored/rescaled for a second tree if the orchard reads too empty |
| `ghost_hand` | Semi-transparent guide hand shown when `ghostGuide` param is true and the player is idle past the timeout | `OrchardReach` guidance layer, follows the ideal reach path to the current target | matches average adult hand, ~0.19 (wrist-to-fingertip) | wrist joint (rigged to match the same joint convention as `OpusJoints`: `*_wrist`, `*_index_tip`, `*_thumb_tip`, `*_palm`) | local +Z = palm-forward when hand is open flat | 3000, rigged | Base Color (flat translucent cyan, no texture needed — a simple unlit/transparent material is fine) | GLB with a simple 5-finger skeleton (no need for a hand-pose library — the ghost only needs open/closed/pinch poses, which can be procedural bone rotations if the rig has standard finger joints) | 1, left/right mirrored at runtime |
| `pairing_hub_icon` | Small 3D "hub found" marker rendered above a discovered hub's card in the world-space pairing UI (optional polish; the flat UI icon works without this) | `Shell` pairing screen | 0.03 | center | n/a (billboards to camera) | 200 | Base Color only, 128×128 | GLB | none | 1 |

## Imported asset review (Opus, 2026-09-15): user-supplied models in `Assets/MetaAssets/`

The user added 6 models, each with 3 LODs (L ≈ 15k tris, M ≈ 4k, S ≈ 1k), one 2048² PNG texture, and one material. Issues verified from the FBX data and .mat/.meta files:
- Meshes are **normalized to ~2 units on the longest axis and centered at the origin**: no real-world scale, and pivots are at the middle.
- Materials use the **built-in Standard shader** (fileID 46, `_MainTex`), so they **render pink in URP**. Convert them to URP Lit (`_BaseMap`).
- Textures import at 2048 with **no Android override**. Set ASTC 6×6; 1024 for table/basket/tree/hand, 512 for fruit.
- No rigs on any model.

| Asset id | Is | Slot | Native bbox (x·y·z) | Fix |
|---|---|---|---|---|
| `12913` | red apple with stem + 2 leaves | `fruit_apple` | 2.00·1.71·1.88 | scale ≈ **0.040** (apple *body* ≈ 7 cm, verify visually); S LOD in trials |
| `632623` | green/pale apple | green distractor variant | 1.67·1.66·1.99 | scale ≈ 0.042; **texture shows AI artifacts (rainbow sheen, holes)**. OK for distractors if the visual audit agrees |
| `43333` | wicker basket **with arched handle** | `basket` | 1.45·1.99·1.04 | scale ≈ **0.208** (rim 0.30 m, height incl. handle ≈ 0.41 m); pivot → bottom center; check the handle doesn't block placement |
| `808342` | wooden trestle table | `orchard_table` | 2.01·1.52·1.25 | scale ≈ **0.394** (top 0.60 m, footprint ≈ 0.79 × 0.49 m); pivot → top center |
| `1777303` | tree, brown trunk, **purple/orange foliage** | `orchard_tree` | 0.78·0.87·**2.01** (**Z-up export**) | rotate **−90° X**, scale ≈ **1.24** (2.5 m); pivot → trunk base; background only. Foliage reads plum/autumn; a greener tree is optional |
| `286115` | open hand | `ghost_hand` | 1.46·0.68·1.99 | scale ≈ **0.095** (0.19 m); **not rigged**, so static open-palm hint only. The animated ghost guide uses the ISDK rigged hand with a translucent material |

Scales are computed from native bounds. The Unity agent confirms final sizes from renderer bounds and audit screenshots.

## Not yet built (needs the models above before it makes sense to author)
- **Distractor fruit variants** (`distractors` param, 0-4 per trial): reuses `fruit_apple`'s green-color variant plus
  one additional shape (e.g. a small pear) once modeling bandwidth allows — not blocking M4/M5, which can ship with
  the single apple mesh recolored per distractor.
- **Trunk-lean warning visual**: currently planned as a simple screen-space vignette/tint (no 3D asset needed) per
  `docs/UNITY_PRACTICES.md`; flag here only if the visual audit (M5) shows a 3D indicator reads better than a
  vignette.

## Notes for whoever models these
- Coordinate convention: Unity left-handed, +Y up, +Z forward, meters. Matches `kinematics-chunk.schema.json`'s
  position convention so no re-mapping is needed between recorded hand joints and prop placement.
- Keep total scene draw calls under Quest's ~100-call budget (M4 goal): batch the tree's leaf cards into one atlas
  and static-batch the table/basket (they never move) so only the fruit and ghost hand cost a draw call each during
  a trial.
- All meshes should be authored so the "up" direction is already correct at import (no manual rotation offsets
  baked into the prefab, which would fight the `ModelSlot` forward-axis contract above).
