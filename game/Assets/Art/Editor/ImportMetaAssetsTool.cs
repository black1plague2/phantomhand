using System.Collections.Generic;
using System.IO;
using System.Linq;
using Opus.Art;
using UnityEditor;
using UnityEngine;

namespace Opus.Art.Editor
{
    /// <summary>
    /// R3 (Track U next-run brief): "Tools/OPUS/Import Meta Assets" menu item.
    ///
    /// Fixes the issues Opus catalogued in docs/MODEL_REQUESTS.md ("Imported asset review") for the 6 user-supplied
    /// models under Assets/MetaAssets/ (each with L/M/S LODs, imported at native scale, Standard-shader materials,
    /// un-overridden 2048px textures, no rig):
    ///   1. Copies each L/M/S material to a URP Lit material under Assets/Art/Materials/URP/&lt;id&gt;/, carrying the
    ///      base color texture from the Standard shader's _MainTex to URP Lit's _BaseMap (fixes the "renders pink in
    ///      URP" bug — Standard's fileID 46 vs URP's Shader Graph-generated shader are binary-incompatible, a texture
    ///      swap on the same material asset is not sufficient).
    ///   2. Sets each source texture's Android platform override to ASTC 6x6 at the size MODEL_REQUESTS.md specifies
    ///      (512 for fruit_apple + its green variant, 1024 for basket/table/tree/ghost_hand).
    ///   3. Builds (or updates) a ModelSlot wrapper prefab per slot at Assets/Art/Prefabs/&lt;slotId&gt;/&lt;slotId&gt;.prefab:
    ///      a root GameObject carrying the ModelSlot component (real-world size, pivot convention, axis-fix flag)
    ///      with the existing MetaAssets LODGroup prefab nested as a "Model" child, scaled/rotated/offset per the
    ///      MODEL_REQUESTS table, with the child's renderers repointed at the new URP materials.
    ///
    /// This is intentionally idempotent (re-running after a source model changes updates the existing assets rather
    /// than duplicating them) so it can be re-run safely as models are revised.
    ///
    /// NOT YET RUN this session (2026-09-15, U run4): the Unity Editor's AI Agent Bridge was down for the whole
    /// session (see logs/sessions/2026-09-15-U-unity-run4.md), so this file has not compiled or executed against
    /// the real project. Before trusting its output: run the menu item, then visually confirm in the Scene view
    /// that (a) materials are no longer pink, (b) each wrapper prefab's rendered bounds roughly match the
    /// real-world size in the table below (screenshot per docs/agent-briefs/U-next-run.md R5), and (c) pivot
    /// offsets look right (basket/table resting on the floor/at floor level, not floating or sunk).
    /// </summary>
    public static class ImportMetaAssetsTool
    {
        private const string MetaAssetsRoot = "Assets/MetaAssets";
        private const string UrpMaterialsRoot = "Assets/Art/Materials/URP";
        private const string PrefabsRoot = "Assets/Art/Prefabs";

        private static readonly string[] Lods = { "L", "M", "S" };

        /// <summary>One row of docs/MODEL_REQUESTS.md's "Imported asset review" table.</summary>
        private class SlotSpec
        {
            public string AssetId;      // MetaAssets folder / file prefix, e.g. "12913"
            public string SlotId;       // Assets/Art/Prefabs/<SlotId>, e.g. "fruit_apple"
            public float Scale;         // uniform scale applied to the native (~2-unit) mesh
            public PivotConvention Pivot;
            public float NativeHeightUnits; // the bbox axis (Y) used to compute the pivot offset, native (pre-scale) units
            public Vector3 EulerFix;    // extra rotation baked into the Model child (e.g. tree's Z-up correction)
            public int TextureMaxSize;  // Android ASTC 6x6 max size
            public float RealWorldSizeM;
        }

        private static readonly SlotSpec[] Specs =
        {
            new SlotSpec { AssetId = "12913",   SlotId = "fruit_apple",       Scale = 0.040f, Pivot = PivotConvention.Center,       NativeHeightUnits = 1.71f, EulerFix = Vector3.zero,            TextureMaxSize = 512,  RealWorldSizeM = 0.07f },
            new SlotSpec { AssetId = "632623",  SlotId = "fruit_apple_green", Scale = 0.042f, Pivot = PivotConvention.Center,       NativeHeightUnits = 1.66f, EulerFix = Vector3.zero,            TextureMaxSize = 512,  RealWorldSizeM = 0.07f },
            new SlotSpec { AssetId = "43333",   SlotId = "basket",            Scale = 0.208f, Pivot = PivotConvention.BottomCenter, NativeHeightUnits = 1.99f, EulerFix = Vector3.zero,            TextureMaxSize = 1024, RealWorldSizeM = 0.30f },
            new SlotSpec { AssetId = "808342",  SlotId = "orchard_table",     Scale = 0.394f, Pivot = PivotConvention.TopCenter,    NativeHeightUnits = 1.25f, EulerFix = Vector3.zero,            TextureMaxSize = 1024, RealWorldSizeM = 0.60f },
            // FIX (2026-09-17, run6 visual audit), part 1: EulerFix was double-rotating the tree. Unity's own
            // FBX importer already bakes a -90 X correction into the imported mesh's LOD renderer transforms for
            // this Z-up-exported asset (confirmed via a diagnostic dump: LOD0's localRotation is (270,0,0) ==
            // -90 X). Applying a second -90 X on the wrapper's "Model" child on top of that laid the tree on its
            // side (trunk horizontal) in the R5 screenshots. Fixed by zeroing EulerFix.
            // FIX part 2: after that importer-baked rotation, the mesh's WORLD bounds are already Y-up AND
            // already bottom-anchored at local Y=0 (world bounds center Y=1.01, extents.y=1.01 -> min Y = 0,
            // max Y = 2.02m tall, matching MODEL_REQUESTS's native Z=2.01 "height"). So, unlike the other slots
            // (whose meshes are Y-up and Y-centered at the origin, needing +halfHeight to move the pivot from
            // center to bottom), this mesh needs NO additional offset — the importer's own rotation already put
            // it at ground level. Adding the usual BottomCenter half-height offset on top of that made the tree
            // float ~1.24m above the ground in the R5 screenshots. NativeHeightUnits=0 disables that offset for
            // this slot only (real height for reference: ~2.01m, applied via Scale already).
            new SlotSpec { AssetId = "1777303", SlotId = "orchard_tree",      Scale = 1.24f,  Pivot = PivotConvention.BottomCenter, NativeHeightUnits = 0f,    EulerFix = Vector3.zero,            TextureMaxSize = 1024, RealWorldSizeM = 2.5f },
            new SlotSpec { AssetId = "286115",  SlotId = "ghost_hand_static", Scale = 0.095f, Pivot = PivotConvention.Joint,        NativeHeightUnits = 0.68f, EulerFix = Vector3.zero,            TextureMaxSize = 1024, RealWorldSizeM = 0.19f },
        };

        /// <summary>Run8 throwaway diagnostic (SeatedLayoutTests found the table's TopCenter pivot landing 0.246m
        /// above the intended top): instantiates the wrapper + the source LOD mesh in isolation at world origin
        /// and logs exact renderer bounds so the real native mesh height can be read off directly instead of
        /// trusting MODEL_REQUESTS.md's hand-typed bbox column. -executeMethod entry point, no menu item.</summary>
        public static void DiagnosePivots()
        {
            foreach (var spec in new[] { "basket", "orchard_table" })
            {
                var wrapperPath = $"{PrefabsRoot}/{spec}/{spec}.prefab";
                var wrapper = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath));
                wrapper.transform.position = Vector3.zero;
                var wrapperRenderers = wrapper.GetComponentsInChildren<Renderer>();
                var wb = wrapperRenderers[0].bounds;
                for (int i = 1; i < wrapperRenderers.Length; i++) wb.Encapsulate(wrapperRenderers[i].bounds);
                var model = wrapper.transform.Find("Model");
                Debug.Log($"[OPUS][DIAG] {spec}: wrapperWorldBounds min={wb.min} max={wb.max} size={wb.size}; " +
                          $"Model.localPosition={model.localPosition} Model.localScale={model.localScale}");

                string assetId = spec == "basket" ? "43333" : "808342";
                var lodGroupPath = $"{MetaAssetsRoot}/Prefabs/{assetId}/{assetId}_LODGroup.prefab";
                var lodInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(lodGroupPath));
                lodInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                lodInstance.transform.localScale = Vector3.one;
                var lodRenderers = lodInstance.GetComponentsInChildren<Renderer>();
                var lb = lodRenderers[0].bounds;
                for (int i = 1; i < lodRenderers.Length; i++) lb.Encapsulate(lodRenderers[i].bounds);
                Debug.Log($"[OPUS][DIAG] {spec}: NATIVE (unscaled) LODGroup bounds min={lb.min} max={lb.max} size={lb.size} center={lb.center}");

                Object.DestroyImmediate(wrapper);
                Object.DestroyImmediate(lodInstance);
            }
        }

        [MenuItem("Tools/OPUS/Import Meta Assets")]
        public static void Run()
        {
            var log = new List<string>();
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogError("[ImportMetaAssetsTool] Could not find shader 'Universal Render Pipeline/Lit'. " +
                                "Is com.unity.render-pipelines.universal installed and the project's Graphics " +
                                "Settings pointing at a URP asset? Aborting.");
                return;
            }

            Directory.CreateDirectory(UrpMaterialsRoot);
            AssetDatabase.Refresh();

            foreach (var spec in Specs)
            {
                try
                {
                    ProcessSlot(spec, urpLit, log);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ImportMetaAssetsTool] Failed on slot '{spec.SlotId}' (asset {spec.AssetId}): {e}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ImportMetaAssetsTool] Done:\n" + string.Join("\n", log));
        }

        private static void ProcessSlot(SlotSpec spec, Shader urpLit, List<string> log)
        {
            string materialsDir = $"{UrpMaterialsRoot}/{spec.AssetId}";
            Directory.CreateDirectory(materialsDir);

            // 1) Convert each LOD's material to URP Lit.
            var urpMaterialsByLod = new Dictionary<string, Material>();
            foreach (var lod in Lods)
            {
                string srcMatPath = $"{MetaAssetsRoot}/Materials/{spec.AssetId}/{spec.AssetId}_{lod}.mat";
                var srcMat = AssetDatabase.LoadAssetAtPath<Material>(srcMatPath);
                if (srcMat == null)
                {
                    log.Add($"[{spec.SlotId}] WARN: source material not found at {srcMatPath}, skipping LOD {lod}");
                    continue;
                }

                Texture mainTex = srcMat.HasProperty("_MainTex") ? srcMat.GetTexture("_MainTex") : null;

                string dstMatPath = $"{materialsDir}/{spec.AssetId}_{lod}_URP.mat";
                var dstMat = AssetDatabase.LoadAssetAtPath<Material>(dstMatPath);
                if (dstMat == null)
                {
                    dstMat = new Material(urpLit) { name = $"{spec.AssetId}_{lod}_URP" };
                    AssetDatabase.CreateAsset(dstMat, dstMatPath);
                }
                else
                {
                    dstMat.shader = urpLit;
                }

                if (mainTex != null && dstMat.HasProperty("_BaseMap"))
                {
                    dstMat.SetTexture("_BaseMap", mainTex);
                }
                EditorUtility.SetDirty(dstMat);
                urpMaterialsByLod[lod] = dstMat;

                // 2) Android texture override (ASTC 6x6, spec'd max size).
                if (mainTex != null)
                {
                    string texPath = AssetDatabase.GetAssetPath(mainTex);
                    var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    if (importer != null)
                    {
                        var platformSettings = importer.GetPlatformTextureSettings("Android");
                        platformSettings.overridden = true;
                        platformSettings.maxTextureSize = spec.TextureMaxSize;
                        platformSettings.format = TextureImporterFormat.ASTC_6x6;
                        platformSettings.compressionQuality = (int)TextureCompressionQuality.Normal;
                        importer.SetPlatformTextureSettings(platformSettings);
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                        log.Add($"[{spec.SlotId}] texture {Path.GetFileName(texPath)} -> Android ASTC_6x6 @ {spec.TextureMaxSize}px");
                    }
                }
            }

            // 3) Build/update the ModelSlot wrapper prefab.
            string lodGroupPrefabPath = $"{MetaAssetsRoot}/Prefabs/{spec.AssetId}/{spec.AssetId}_LODGroup.prefab";
            var lodGroupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(lodGroupPrefabPath);
            if (lodGroupPrefab == null)
            {
                log.Add($"[{spec.SlotId}] WARN: LODGroup prefab not found at {lodGroupPrefabPath}, cannot build wrapper");
                return;
            }

            string prefabDir = $"{PrefabsRoot}/{spec.SlotId}";
            Directory.CreateDirectory(prefabDir);
            AssetDatabase.Refresh();
            string wrapperPath = $"{prefabDir}/{spec.SlotId}.prefab";

            // Build the wrapper in a scratch scene object, then save/overwrite the prefab asset.
            var root = new GameObject(spec.SlotId);
            try
            {
                var slot = root.AddComponent<ModelSlot>();
                slot.slotId = spec.SlotId;
                slot.realWorldSizeM = spec.RealWorldSizeM;
                slot.pivot = spec.Pivot;
                slot.axisFixApplied = spec.EulerFix != Vector3.zero;

                var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(lodGroupPrefab);
                modelInstance.name = "Model";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localScale = Vector3.one * spec.Scale;
                modelInstance.transform.localRotation = Quaternion.Euler(spec.EulerFix);

                // Run8 fix (SeatedLayoutTests, via a throwaway DiagnosePivots dump of real renderer bounds):
                // the old code assumed every native mesh is Y-centered at its own origin, so it moved the pivot
                // to the bottom/top face by shifting +/- half of a hand-typed "NativeHeightUnits" constant. That
                // assumption was FALSE for both orchard_table and basket (like the tree, fixed in run6, but
                // never generalized past that one slot): DiagnosePivots showed both meshes are already
                // bottom-anchored natively (min.y ~ 0), and MODEL_REQUESTS.md's hand-typed bbox column had the
                // Y/Z axes swapped for both too (basket's real Y-extent is 1.05, not the 1.99 used; table's is
                // 1.25, not 1.52) — two independent wrong inputs compounding into a ~0.21m float (basket) and a
                // ~0.246m top-surface offset (table), both confirmed against the real scene via
                // SeatedLayoutTests. Fixed at the root instead of adding two more per-slot magic numbers to the
                // pile: measure the model's ACTUAL post-scale local bounds here (root has just been created and
                // sits at the scene origin, unparented, so Renderer.bounds IS local-to-root at this point) and
                // derive the offset from that directly. This is self-correcting for whatever the source mesh's
                // real native pivot turns out to be — including reproducing the tree's already-correct
                // NativeHeightUnits=0 behaviour (its mesh measures bottom-anchored too, so -bounds.min.y ~= 0)
                // without needing that hack anymore. NativeHeightUnits is no longer read for Bottom/TopCenter;
                // kept on SlotSpec only as a documentation artifact of the (unreliable) original estimate.
                var boundsRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                Bounds? measuredBounds = null;
                if (boundsRenderers.Length > 0)
                {
                    var b = boundsRenderers[0].bounds;
                    for (int i = 1; i < boundsRenderers.Length; i++) b.Encapsulate(boundsRenderers[i].bounds);
                    measuredBounds = b;
                }
                switch (spec.Pivot)
                {
                    case PivotConvention.BottomCenter:
                        modelInstance.transform.localPosition = measuredBounds.HasValue
                            ? new Vector3(0, -measuredBounds.Value.min.y, 0)
                            : new Vector3(0, (spec.NativeHeightUnits * 0.5f) * spec.Scale, 0); // fallback if no renderer found yet
                        break;
                    case PivotConvention.TopCenter:
                        modelInstance.transform.localPosition = measuredBounds.HasValue
                            ? new Vector3(0, -measuredBounds.Value.max.y, 0)
                            : new Vector3(0, -(spec.NativeHeightUnits * 0.5f) * spec.Scale, 0);
                        break;
                    default:
                        modelInstance.transform.localPosition = Vector3.zero;
                        break;
                }

                // Repoint every renderer's materials at the URP copies (index-matched: MetaAssets materials were
                // named/assigned per-submesh by the FBX importer in declaration order, so array-index swap is the
                // correct mapping here rather than name matching).
                RepointMaterials(modelInstance, urpMaterialsByLod);

                bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath) != null;
                PrefabUtility.SaveAsPrefabAsset(root, wrapperPath, out bool success);
                log.Add(success
                    ? $"[{spec.SlotId}] wrapper prefab {(existed ? "updated" : "created")} at {wrapperPath}"
                    : $"[{spec.SlotId}] WARN: SaveAsPrefabAsset reported failure for {wrapperPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void RepointMaterials(GameObject modelRoot, Dictionary<string, Material> urpMaterialsByLod)
        {
            // Run8 fix (found via SeatedLayoutTests/PlayMode audit: the apple, close up, showed jagged
            // black/green blocky patches — a classic UV-mismatch pattern, not a wrong-asset bug, since
            // SeatedLayoutTests independently confirmed the PREFAB's assigned material is the correct 12913
            // apple, not the 632623 distractor). Root cause: this used to match each renderer's LOD by looking
            // for "_L"/"_M"/"_S" in the renderer's GAMEOBJECT name — but the LODGroup prefab's actual per-LOD
            // GameObjects are named "LOD0"/"LOD1"/"LOD2" (confirmed by reading 12913_LODGroup.prefab directly),
            // which never contains that pattern. So EVERY LOD level silently fell through to the "single-LOD
            // fallback" branch and got the "L" (highest-detail) material forced onto it — including LOD1/LOD2,
            // whose meshes have their OWN, DIFFERENT UV layout baked for their own (M/S) texture, not L's. Unity
            // switches to a lower LOD at close range/large screen size (exactly what a 7cm apple viewed from
            // ~0.3-0.5m in these screenshots triggers), so the close-up shots were rendering an M or S mesh
            // wearing the L texture through the wrong UVs — the scrambled/blocky look. Fixed by reading the LOD
            // suffix off the renderer's CURRENT (pre-repoint) material name instead of the GameObject name — the
            // original MetaAssets materials ARE correctly named "<assetId>_L"/"_M"/"_S" (confirmed the same way),
            // so this is a reliable per-LOD signal that was sitting right there unused.
            foreach (var renderer in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                var originalMats = renderer.sharedMaterials;
                string originalName = originalMats.Length > 0 && originalMats[0] != null ? originalMats[0].name : null;
                string lod = originalName != null
                    ? Lods.FirstOrDefault(l => originalName.EndsWith("_" + l))
                    : null;
                // Fall back to the old (unreliable but harmless) gameobject-name check in case some other slot's
                // naming convention differs, then to "use L for everything" only as a last resort.
                lod ??= Lods.FirstOrDefault(l => renderer.gameObject.name.Contains($"_{l}"));

                Material chosen = null;
                if (lod != null) urpMaterialsByLod.TryGetValue(lod, out chosen);
                if (chosen == null && urpMaterialsByLod.Count > 0)
                {
                    chosen = urpMaterialsByLod.ContainsKey("L") ? urpMaterialsByLod["L"] : urpMaterialsByLod.Values.First();
                }
                if (chosen != null)
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = chosen;
                    renderer.sharedMaterials = mats;
                }
            }
        }
    }
}
