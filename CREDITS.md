# Credits and third-party assets

This repository is public. Every third-party asset kept in it is listed here with its licence.

## Rigged hand (the virtual hand in Phantom Hand)

- **Title:** "Rigged hand"
- **Author:** Elena FF ([sketchfab.com/elenaferfor](https://sketchfab.com/elenaferfor))
- **Source:** <https://sketchfab.com/3d-models/rigged-hand-eae97cc2a742413cb5338ab942b12c1e> (published 26 Sep 2020, free download)
- **Licence:** [Creative Commons Attribution-ShareAlike 4.0](https://creativecommons.org/licenses/by-sa/4.0/) (CC BY-SA 4.0): the author must be credited; modified versions must be shared under the same licence; commercial use is allowed.
- **Files:** `game/Assets/Art/PhantomHand/Models/RiggedHand/` (`handRig_02.fbx`, `hand_Co.jpg`, `hand_No.png`, `hand_Ro.jpg`, `hand_Sp.jpg`), as added to the repository by the team.
- **Changes made:** the project's tooling does not alter the files above. At edit time the Unity importer builds a wrapper prefab from them (scaled to a 19 cm hand, bind pose restored, finger bones driven for the closing hand, material tone-matched to the forearm). That wrapper and any other modified version of this model are under CC BY-SA 4.0 as well.

Credit line for slides, the demo table and an About screen:
"Rigged hand" by Elena FF, CC BY-SA 4.0, sketchfab.com/elenaferfor

## Room and table props (made for this project)

- **Files:** `game/Assets/Art/PhantomHand/Models/` `Brush/PH_Brush.glb`, `Lamp/PH_PendantLamp.glb`, `Plant/PH_Plant.glb`, `Window/PH_Window.glb`, `Props/PH_SingingBowl.glb`, `Props/PH_TeaCup.glb`, `Props/PH_FramedPicture.glb`.
- **Origin:** added by the team on 8 Oct 2026. The files carry no author or licence field. Their generator tag is `THREE.GLTFExporter r184` and their material names use this project's `PH_` prefix, so they are taken to be the team's own procedural models, not third-party downloads. Confirm this before a public release build.
- **In the game since 8 Oct 2026:** the importer reads each `.glb` and writes a wrapper prefab, one mesh, URP materials and the embedded textures under `game/Assets/Art/PhantomHand/Models/` (`Resources/PhantomModels/PH_*.prefab`, `Meshes/PHP_*`, `Materials/PHP_*`, `Textures/PHP_*`). Those files are derived from the `.glb` files above and carry the same origin.

## Unity Asset Store packages (not in this repository)

The Unity Asset Store licence lets a team use a package in its built game but not publish the package's files. These packages must stay out of this public repository (keep them in a git-ignored folder on the build PC):

- Pack Gesta Furniture #1: <https://assetstore.unity.com/packages/3d/props/furniture/pack-gesta-furniture-1-28237>
- Stones: <https://assetstore.unity.com/packages/3d/props/exterior/stones-40329>
- Dark Wave Paint Table 01: <https://assetstore.unity.com/packages/3d/props/dark-wave-paint-table-01-306300>
- Mobile Books: <https://assetstore.unity.com/packages/3d/props/interior/mobile-books-3356>

Imported on the build PC on 8 Oct 2026 (Package Manager, My Assets) into git-ignored folders. What the built game uses from
them, through wrappers baked on that PC (`Tools/OPUS/Bake local Asset Store props`, also git-ignored): the table
(Dark Wave Paint Table 01), the falling stone (Stones, `Stone_3`), a stack of four books (Mobile Books) and a chest of
drawers against the right wall (Pack Gesta Furniture #1, `tumba_fur`). A clone without the packs shows the repository's own
table and stone and no books or chest; the committed scene holds only the two empty slots.

## Meta asset-library models

`game/Assets/MetaAssets/` holds models taken from Meta's asset library inside the Unity editor (table, stone, brush, sleeve, glove and others). Their licence terms have not been checked yet: confirm them before a public release build (open item in `docs/MANUAL_TODO.md`).
