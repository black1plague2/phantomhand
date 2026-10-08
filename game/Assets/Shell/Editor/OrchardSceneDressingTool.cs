using System.IO;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using Opus.Shell;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// U-next-run priority #1: "Tools/OPUS/Dress Orchard Scene" menu item.
    ///   1. Makes fruit_apple (+ green variant) grabbable: SphereCollider + Rigidbody + OneGrabFreeTransformer +
    ///      Grabbable + HandGrabInteractable, wired exactly like Meta's own ISDK samples (Grabbable references
    ///      the transformer + rigidbody; HandGrabInteractable references the rigidbody + the Grabbable as its
    ///      IPointableElement). No authored hand-grab poses (MODEL_REQUESTS.md's "+ grab poses if feasible" —
    ///      not authored this run; ISDK falls back to its default generic grip without them).
    ///   2. Makes the basket a placement-detection trigger (BoxCollider isTrigger + BasketTrigger).
    ///   3. Places table/basket/tree + lighting/fog into Assets/Scenes/OrchardReach.unity (seated orchard corner).
    ///   4. Adds the OrchardReachSceneController GameObject wired to the fruit prefab, the basket trigger, and
    ///      the rig's CenterEyeAnchor as the chest-height calibration reference.
    /// Idempotent: re-running skips components/objects that already exist by name/type rather than duplicating.
    /// </summary>
    public static class OrchardSceneDressingTool
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";
        private const string FruitApplePath = "Assets/Art/Prefabs/fruit_apple/fruit_apple.prefab";
        private const string FruitAppleGreenPath = "Assets/Art/Prefabs/fruit_apple_green/fruit_apple_green.prefab";
        private const string BasketPath = "Assets/Art/Prefabs/basket/basket.prefab";
        private const string TablePath = "Assets/Art/Prefabs/orchard_table/orchard_table.prefab";
        private const string TreePath = "Assets/Art/Prefabs/orchard_tree/orchard_tree.prefab";
        private const string ManifestPath = "Assets/Games/OrchardReach/manifest.json";

        public static string Ping() => "pong";

        /// <summary>
        /// Batch-mode entry point for `-executeMethod`: runs DressOrchardScene(), saves scenes+assets, and logs
        /// [OPUS] lines to the batch logfile so a `grep "[OPUS]"` on the log gives verifiable evidence without
        /// needing the editor UI. Exits with an error (non-zero via Debug.LogError, caught by CI/log grep) if the
        /// dressing throws, rather than silently leaving a half-dressed scene.
        /// </summary>
        public static void BatchDressOrchardScene()
        {
            Debug.Log("[OPUS] BatchDressOrchardScene: starting");
            try
            {
                var result = DressOrchardScene();
                Debug.Log($"[OPUS] BatchDressOrchardScene: DressOrchardScene() -> {result}");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("[OPUS] BatchDressOrchardScene: assets + scenes saved");

                // On-disk verification the log can be grepped for.
                var apple = PrefabUtility.LoadPrefabContents(FruitApplePath);
                bool hasGrab = apple.GetComponent<HandGrabInteractable>() != null;
                bool hasGrabbable = apple.GetComponent<Grabbable>() != null;
                bool hasCollider = apple.GetComponent<SphereCollider>() != null;
                PrefabUtility.UnloadPrefabContents(apple);
                Debug.Log($"[OPUS] verify fruit_apple: HandGrabInteractable={hasGrab} Grabbable={hasGrabbable} SphereCollider={hasCollider}");

                var basket = PrefabUtility.LoadPrefabContents(BasketPath);
                bool basketTrigger = basket.GetComponent<BasketTrigger>() != null;
                bool basketCol = basket.GetComponent<BoxCollider>() != null && basket.GetComponent<BoxCollider>().isTrigger;
                PrefabUtility.UnloadPrefabContents(basket);
                Debug.Log($"[OPUS] verify basket: BasketTrigger={basketTrigger} TriggerCollider={basketCol}");

                var scene = SceneManager.GetSceneByPath(ScenePath);
                var roots = scene.GetRootGameObjects();
                bool hasTable = FindDeep(roots, "OrchardTable") != null;
                bool hasBasketInScene = FindDeep(roots, "Basket") != null;
                bool hasTree1 = FindDeep(roots, "OrchardTree_1") != null;
                bool hasController = FindDeep(roots, "OrchardReachSceneController") != null;
                bool hasSun = FindDeep(roots, "OrchardSun") != null;
                Debug.Log($"[OPUS] verify scene: table={hasTable} basket={hasBasketInScene} tree1={hasTree1} sun={hasSun} controller={hasController}");

                Debug.Log("[OPUS] BatchDressOrchardScene: DONE");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OPUS] BatchDressOrchardScene: FAILED: {e}");
                throw;
            }
        }

        [MenuItem("Tools/OPUS/Dress Orchard Scene")]
        public static string DressOrchardScene()
        {
            MakeFruitGrabbable(FruitApplePath, addMarker: true);
            if (File.Exists(FruitAppleGreenPath)) MakeFruitGrabbable(FruitAppleGreenPath, addMarker: true);
            MakeBasketTrigger(BasketPath);

            var scene = OpenOrGetScene();
            var root = scene.GetRootGameObjects();

            Transform centerEye = FindDeep(root, "CenterEyeAnchor");
            // Real name confirmed by grepping the scene file: run6 assumed "Camera Rig" (never matched, silently
            // leaving `cameraRig` null — the actual bug behind Opus's "seated layout is wrong" review: the whole
            // rig-move-and-ChestReference-parenting block below was silently skipped every run so far).
            Transform cameraRig = FindDeep(root, "[BuildingBlock] Camera Rig");

            // --- Seated layout fix (2026-09-17, run7, per Opus review of run6's screenshots) ---
            // run6 left the rig at world origin (all anchors' local offsets are 0 with no live headset tracking
            // to drive them in batch mode, confirmed by a diagnostic hierarchy dump), so CenterEyeAnchor sat at
            // world (0,0,0) — nowhere near a seated eye height, and the calibration origin wired to it put
            // reach targets near the ground. Fix at the root: move the RIG itself to a real seated eye height/
            // position, and add a dedicated ChestReference child at a realistic chest offset below/forward of
            // the eye (a fixed offset is a reasonable simplification pre-calibration-UI, same as a real
            // clinical calibration step would produce once per session). All three of target sampling
            // (TargetPlacement via this ChestReference), the basket, and the demo hand replay's offset (see
            // OrchardReachSceneController's _chestOriginWorld fix) now share this ONE origin.
            // --- Gaze pitch fix (run8, per Opus's rejection of run7's "seated_eye" screenshot: whole table incl.
            // legs was visible, i.e. the camera was looking level/ahead, not down at the tabletop) ---
            // Attempt 1 (reverted): set CenterEyeAnchor.localRotation directly. Saved correctly to the .unity
            // file (confirmed by grepping it: m_LocalRotation was the exact 30 deg quaternion) but a SEPARATE
            // batch process re-opening that same file read it back as level — something resets this anchor's
            // rotation to identity as soon as the scene's live OVRCameraRig/OVRManager initializes, independent
            // of Play mode (the same test's height assertion, reading position, was unaffected — this is
            // rotation-specific).
            // Attempt 2 (reverted): insert a wrapper GameObject between TrackingSpace and CenterEyeAnchor,
            // pitching the wrapper. Broke Rig_HasExactlyOneEnabledCamera (2 enabled cameras instead of 1) —
            // whatever validates the rig's anchors evidently falls back to a per-eye-camera mode when it doesn't
            // recognize the hierarchy under TrackingSpace.
            // Attempt 3 (reverted): pitch TrackingSpace's own local rotation instead of CenterEyeAnchor's (same
            // GameObject count/hierarchy, no reparenting). Rig_HasExactlyOneEnabledCamera passed, but the
            // rotation STILL read back as identity on the next batch process — confirming the reset isn't about
            // hierarchy shape at all, it's that anything in the TrackingSpace-and-below anchor subtree gets its
            // rotation forced back to identity (almost certainly OVRCameraRig.UpdateAnchors(), which very likely
            // runs even in batch/no-device Edit mode and resets every tracked anchor's local pose to a neutral
            // default absent live sensor data).
            // Fix (this one verified working via SeatedLayoutTests + RigValidatorTests, not just measured
            // in-process): pitch the RIG ROOT itself instead — "[BuildingBlock] Camera Rig" is the GameObject
            // OVRManager/OVRCameraRig's OWN scripts live on, not one of the anchors those scripts drive, and its
            // POSITION edits (above) have persisted correctly across every run so far; empirically its ROTATION
            // does too. This does reopen the real production caveat noted in earlier runs — a real headset's
            // live head tracking updates CenterEyeAnchor's local pose, not the rig root, so this static pitch
            // would compound with a real patient's own head tilt rather than being superseded by it — but is the
            // only level of this hierarchy that both (a) survives a scene reopen and (b) doesn't corrupt the
            // rig's own camera-count invariant, so it stays a documented, deliberate compromise for the
            // batch/no-headset demo this project has depended on since run7's IDENTICAL position workaround
            // (this project has no live-tracking, calibration-UI-driven way to set this yet — a real fix needs
            // R4's calibration step to set this per-session from an actual live head pose, not a scene default).
            if (cameraRig != null)
            {
                cameraRig.position = new Vector3(0f, 1.15f, -0.05f); // seated eye height, per docs' seated-eye spec
                cameraRig.rotation = Quaternion.Euler(30f, 0f, 0f); // 30 deg down, within the 20-40 deg spec
            }

            // Cleanup: an earlier (reverted) experiment this run left a "SeatedGazeTilt" wrapper + a reparented
            // CenterEyeAnchor baked into a since-discarded save of this scene. Not reachable anymore once the
            // scene was restored from git before this fix landed, but keep this idempotent-safety net in case a
            // stale copy of that state ever resurfaces (e.g. a merge) — do nothing if it's not present.
            var staleTiltGo = FindInSceneIncludingInactive(scene, "SeatedGazeTilt");
            if (staleTiltGo != null && centerEye != null && centerEye.IsChildOf(staleTiltGo.transform))
            {
                centerEye.SetParent(staleTiltGo.transform.parent, worldPositionStays: false);
                centerEye.localPosition = Vector3.zero;
                centerEye.localRotation = Quaternion.identity;
            }
            if (staleTiltGo != null) Object.DestroyImmediate(staleTiltGo);

            var chestReferenceGo = GameObject.Find("ChestReference");
            if (chestReferenceGo == null) chestReferenceGo = new GameObject("ChestReference");
            if (cameraRig != null) chestReferenceGo.transform.SetParent(cameraRig, worldPositionStays: false);
            // Run8: chest sits ~0.20m below and ~0.05m forward of the eye for a seated adult — this used to be
            // expressed as a local offset from the rig, which worked while the rig root stayed level (run7). Now
            // that the rig root also carries the 30 deg gaze pitch (see the fix above — that's the one place
            // proven to survive a scene reopen), a LOCAL offset under a rotated parent would itself get rotated,
            // shifting the chest reference off its intended horizontal position (measured: z drifted to -0.11
            // instead of 0.00 before this fix). The chest is a body-position reference, not a gaze direction —
            // it should NOT tilt with the head — so set it as an explicit WORLD position/rotation after parenting
            // instead of a local offset, decoupling it from the rig's pitch entirely.
            chestReferenceGo.transform.position = cameraRig != null
                ? cameraRig.position + new Vector3(0f, -0.20f, 0.05f)
                : new Vector3(0f, 0.95f, 0f);
            chestReferenceGo.transform.rotation = Quaternion.identity;
            Transform chestReference = chestReferenceGo.transform; // world ~ (0, 0.95, 0.00)

            // --- Environment dressing ---
            // Run8 correction: MODEL_REQUESTS.md's hand-typed native bbox for 808342 has Y/Z swapped from the
            // real mesh (same bug class as ImportMetaAssetsTool's pivot fix above, confirmed via the same
            // DiagnosePivots dump) — the table's real footprint DEPTH (Z) is 0.60m, not the assumed 0.49m; its
            // real HEIGHT (floor to top, Y) is ~0.49m, not 0.60m, but that doesn't matter for placement since the
            // TopCenter pivot always puts the tabletop exactly at whatever Y we place the root at, regardless of
            // leg length. Near edge target ~0.35m in front of chest (chest z=0) => top-center pivot z =
            // 0.35 + halfDepth(0.30) = 0.65. Basket ~0.45m forward of chest (still inside the table's [0.35,0.95]
            // depth span with margin), slightly to the unaffected side (x=-0.08, matching the default
            // affected_side=right in the fixture's own calibration block) so it doesn't block the dominant-hand
            // reach path.
            var table = PlaceOrFind("OrchardTable", TablePath, new Vector3(0f, 0.60f, 0.65f), Quaternion.identity);
            var basketInstance = PlaceOrFind("Basket", BasketPath, new Vector3(-0.08f, 0.60f, 0.45f), Quaternion.identity);
            PlaceOrFind("OrchardTree_1", TreePath, new Vector3(1.6f, 0f, 1.9f), Quaternion.Euler(0, -20f, 0));
            PlaceOrFind("OrchardTree_2", TreePath, new Vector3(-1.7f, 0f, 2.1f), Quaternion.Euler(0, 35f, 0), scale: 0.9f);

            PlaceGround();
            SetupLighting();

            var basketTrigger = basketInstance.GetComponentInChildren<BasketTrigger>();

            // --- Demo hand proxy (visual only, for the R5 no-headset audit) ---
            // Run8 real bug found (docs/agent-briefs review): the scene file had THREE stray, all-disabled
            // "DemoHandProxy_R" GameObjects (confirmed by grepping Assets/Scenes/OrchardReach.unity for
            // `m_Name: DemoHandProxy_R` — 3 separate `GameObject:` blocks). Root cause: `GameObject.Find` never
            // matches an INACTIVE GameObject, and the old code called `handProxy.SetActive(false)` right after
            // creating it — so every re-run of this idempotent-by-design tool failed its own "does it already
            // exist" check and created a brand new duplicate sphere. Fixed two ways: (1) look up by name across
            // ALL objects in this scene, active or not, via Resources.FindObjectsOfTypeAll, deleting any extra
            // duplicates found along the way; (2) stop disabling the GameObject at all — hide it by disabling
            // its Renderer instead (a component-level toggle GameObject.Find doesn't care about), so future
            // re-runs of this tool find it correctly.
            var handProxy = FindInSceneIncludingInactive(scene, "DemoHandProxy_R");
            if (handProxy == null)
            {
                handProxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handProxy.name = "DemoHandProxy_R";
                Object.DestroyImmediate(handProxy.GetComponent<Collider>());
                handProxy.transform.localScale = Vector3.one * 0.03f;
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.2f, 0.9f, 1f, 0.85f);
                handProxy.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            var handProxyRenderer = handProxy.GetComponent<MeshRenderer>();
            if (handProxyRenderer != null) handProxyRenderer.enabled = false; // hidden until the controller positions it (Awake/Update)
            // "under the rig" (SeatedLayoutTests): parented to the camera rig so the hand visual lives in the
            // same hierarchy as the real OVR hand sources would. World position is set explicitly every frame by
            // OrchardReachSceneController regardless of this parent's transform, so reparenting is safe.
            if (cameraRig != null) handProxy.transform.SetParent(cameraRig, worldPositionStays: true);

            // --- Run9: real hand visual, replacing the lone sphere (Opus's task #3; refined per the orchestrator's
            // follow-up: use the ghost_hand_static prefab (asset 286115), not improvised primitives). A
            // from-scratch ISDK/OVR synthetic-hand-MESH driven by the same HandDataAsset (a true rigged,
            // per-finger-posed hand) needs a connected Interaction SDK data pipeline to wire up and verify
            // (docs/UNITY_PRACTICES.md §6 already deferred this for the SAME reason for SyntheticHandDriver
            // itself) — infeasible in a batch/headless session with no interactive Editor to inspect the result
            // against. `ghost_hand_static` (MODEL_REQUESTS.md: "286115 = static hint only") is the one hand asset
            // this project actually has; instantiating and posing IT (position at the wrist, heading derived from
            // wrist->index direction) is a real hand mesh, not a primitive stand-in, at the cost of not being a
            // per-finger-posed grasp shape — documented, not silently glossed over, in
            // OrchardReachSceneController.DriveDemoHandAndFruit's comment on this.
            const string GhostHandPath = "Assets/Art/Prefabs/ghost_hand_static/ghost_hand_static.prefab";
            var handVisual = FindInSceneIncludingInactive(scene, "DemoHandProxy_R_GhostHand");
            if (handVisual == null)
            {
                var ghostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GhostHandPath);
                if (ghostPrefab != null)
                {
                    handVisual = (GameObject)PrefabUtility.InstantiatePrefab(ghostPrefab);
                    handVisual.name = "DemoHandProxy_R_GhostHand";
                }
                else
                {
                    Debug.LogWarning($"[OPUS] DressOrchardScene: {GhostHandPath} not found — hand visual falls back to the index-tip marker sphere only.");
                }
            }
            if (handVisual != null)
            {
                var ghostRenderers = handVisual.GetComponentsInChildren<Renderer>(true);
                foreach (var r in ghostRenderers) r.enabled = false; // shown by the controller once it starts posing it
                if (cameraRig != null) handVisual.transform.SetParent(cameraRig, worldPositionStays: true);
            }

            // --- Controller ---
            var controllerGo = GameObject.Find("OrchardReachSceneController");
            if (controllerGo == null) controllerGo = new GameObject("OrchardReachSceneController");
            var controller = controllerGo.GetComponent<OrchardReachSceneController>();
            if (controller == null) controller = controllerGo.AddComponent<OrchardReachSceneController>();

            var so = new SerializedObject(controller);
            so.FindProperty("manifestJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            so.FindProperty("fruitPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(FruitApplePath);
            so.FindProperty("chestReference").objectReferenceValue = chestReference;
            so.FindProperty("basketTrigger").objectReferenceValue = basketTrigger;
            so.FindProperty("demoHandProxyR").objectReferenceValue = handProxy.transform;
            so.FindProperty("demoHandGhostVisual").objectReferenceValue = handVisual != null ? handVisual.transform : null;
            so.FindProperty("useDemoDriver").boolValue = true;
            so.FindProperty("demoTrialCount").intValue = 6;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            return $"dressed scene: table={table != null}, basket={basketInstance != null}, cameraRigFound={cameraRig != null}, cameraRigWorldPos={(cameraRig != null ? cameraRig.position.ToString() : "N/A")}, chestReferenceWorldPos={(chestReference != null ? chestReference.position.ToString() : "MISSING")}, centerEyeWorldForward={(centerEye != null ? centerEye.forward.ToString() : "N/A")}, controller wired";
        }

        private static Scene OpenOrGetScene()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            return scene;
        }

        private static Transform FindDeep(GameObject[] roots, string name)
        {
            foreach (var root in roots)
            {
                var t = FindDeep(root.transform, name);
                if (t != null) return t;
            }
            return null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindDeep(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Finds a GameObject by exact name anywhere in `scene`, including inactive ones (unlike
        /// `GameObject.Find`), and destroys any extra duplicates it encounters so re-running this tool is
        /// actually idempotent. See the DemoHandProxy_R comment above for the bug this fixes.</summary>
        private static GameObject FindInSceneIncludingInactive(Scene scene, string name)
        {
            GameObject found = null;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene != scene || go.name != name) continue;
                if (found == null) found = go;
                else Object.DestroyImmediate(go);
            }
            return found;
        }

        private static GameObject PlaceOrFind(string instanceName, string prefabPath, Vector3 pos, Quaternion rot, float scale = 1f)
        {
            var existing = GameObject.Find(instanceName);
            if (existing != null)
            {
                // Run8: re-apply position/rotation/scale on every run, not just at creation — otherwise a tuning
                // change to the placement numbers here (like this run's table/basket Z corrections) silently
                // does nothing on a scene that already has these objects from a previous run, exactly the same
                // "idempotent tool that isn't" bug class already found and fixed for DemoHandProxy_R.
                existing.transform.SetPositionAndRotation(pos, rot);
                existing.transform.localScale = Vector3.one * scale;
                return existing;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"OrchardSceneDressingTool: prefab not found at {prefabPath}");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = instanceName;
            instance.transform.SetPositionAndRotation(pos, rot);
            instance.transform.localScale = Vector3.one * scale;
            return instance;
        }

        /// <summary>Adds a large flat grass/soil-colored ground plane so the scene reads as an orchard corner
        /// instead of floating props over the raw grey/fog background (run6 had no ground GameObject at all —
        /// what looked like "grey ground" in those screenshots was just the fog/skybox horizon showing through
        /// under the props). No texture (Quest draw-call budget: this is one extra static-batched quad, not a
        /// new draw call source worth a texture fetch) — a flat, slightly-varied green-brown URP/Lit material
        /// reads as grass/soil at the distances this scene is viewed from.</summary>
        private static void PlaceGround()
        {
            var groundGo = GameObject.Find("OrchardGround");
            if (groundGo == null)
            {
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
                groundGo.name = "OrchardGround";
                Object.DestroyImmediate(groundGo.GetComponent<Collider>()); // no gameplay collision needed here
            }
            groundGo.transform.position = new Vector3(0f, 0f, 0f); // centered under the player (rig sits near x=0,z=-0.05..0.6)
            groundGo.transform.rotation = Quaternion.identity;
            // Run8 (Opus review of run7: "ground plane is small, with a visible grey edge"): Unity's default
            // Plane is 10x10m; a 3x scale (run7's value) only gave 30x30m, so its edge was reachable inside the
            // ~15m fog/shadow distance already configured below. Bumped to 4.5x -> 45x45m, safely over the 40x40
            // minimum with margin.
            groundGo.transform.localScale = Vector3.one * 4.5f;

            const string groundMatPath = "Assets/Art/Materials/OrchardGround.mat";
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>(groundMatPath);
            if (groundMat == null)
            {
                groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                Directory.CreateDirectory("Assets/Art/Materials");
                AssetDatabase.CreateAsset(groundMat, groundMatPath);
            }
            // Run8: no grass texture asset exists anywhere in the project (checked Assets/ and MetaAssets/), so
            // this stays a flat-color stand-in — pushed greener/more saturated than run7's grey-brown so it
            // reads unambiguously as grass rather than bare soil. A textured grass material is a MANUAL_TODO
            // (needs an actual grass texture asset, which is outside what this run can generate).
            groundMat.color = new Color(0.22f, 0.42f, 0.15f);
            if (groundMat.HasProperty("_Smoothness")) groundMat.SetFloat("_Smoothness", 0.05f);
            var groundRend = groundGo.GetComponent<MeshRenderer>();
            groundRend.sharedMaterial = groundMat;
            groundRend.staticShadowCaster = true;
            GameObjectUtility.SetStaticEditorFlags(groundGo, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
        }

        private static void SetupLighting()
        {
            var sunGo = GameObject.Find("OrchardSun");
            Light sun;
            if (sunGo == null)
            {
                sunGo = new GameObject("OrchardSun");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            else
            {
                sun = sunGo.GetComponent<Light>();
            }
            sunGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            sun.color = new Color(1f, 0.93f, 0.78f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft; // grounding for table legs / basket per Opus review
            sun.shadowStrength = 0.8f;

            // Warmer ambient/fog (2026-09-17, run7): run6's cool blue-grey ambient/fog read as flat and grey
            // rather than a warm orchard corner. Shifted all three toward warm green/amber tones.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.68f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.48f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.19f, 0.12f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.010f;
            RenderSettings.fogColor = new Color(0.88f, 0.8f, 0.62f);

            // Main light (soft) shadows must also be enabled on the active URP asset, not just the Light
            // component, or Light.shadows is silently ignored at render time.
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null)
            {
                // supportsMainLightShadows/supportsSoftShadows have `internal` setters (not accessible from this
                // Editor assembly) — UniversalRenderPipelineAsset.Create() already defaults both to true, which
                // is what we want, so only the public-setter fields need touching here.
                urp.shadowDistance = 15f;
                urp.mainLightShadowmapResolution = (int)UnityEngine.Rendering.Universal.ShadowResolution._1024;
                // supportsSoftShadows defaults to false on a freshly-created asset (confirmed by logging it) and
                // its setter is `internal` — reflect it in, same pattern as OpusRenderPipelineSetupTool's
                // renderer-data creation, rather than leaving soft shadows off.
                var softShadowsProp = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetProperty("supportsSoftShadows");
                softShadowsProp?.SetValue(urp, true);
                Debug.Log($"[OPUS] SetupLighting: urp.supportsMainLightShadows={urp.supportsMainLightShadows} supportsSoftShadows(after fix)={urp.supportsSoftShadows}");
                EditorUtility.SetDirty(urp);
            }
            else
            {
                Debug.LogWarning("[OPUS] SetupLighting: no active UniversalRenderPipelineAsset found — run OpusRenderPipelineSetupTool.BatchSetupUrp first.");
            }
        }

        private static void MakeFruitGrabbable(string prefabPath, bool addMarker)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                // NOTE: deliberately NOT using `a ?? b` here. For UnityEngine.Object, `??`/`?.` compare against
                // the raw C# null via IL `brtrue`, bypassing Unity's overloaded `==`/`!=` (which is what
                // GetComponent's "not found" result relies on) — see Unity's own "custom == operator" docs. That
                // mismatch caused a real MissingComponentException here on this run (GetComponent<SphereCollider>()
                // returning a falsy-but-not-C#-null value short-circuited AddComponent). Use explicit `== null`
                // checks instead, which do go through Unity's overload.
                var collider = root.GetComponent<SphereCollider>();
                if (collider == null) collider = root.AddComponent<SphereCollider>();
                collider.radius = 0.035f;
                collider.center = Vector3.zero;

                var rb = root.GetComponent<Rigidbody>();
                if (rb == null) rb = root.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var transformer = root.GetComponent<OneGrabFreeTransformer>();
                if (transformer == null) transformer = root.AddComponent<OneGrabFreeTransformer>();

                var grabbable = root.GetComponent<Grabbable>();
                if (grabbable == null) grabbable = root.AddComponent<Grabbable>();
                var grabbableSo = new SerializedObject(grabbable);
                grabbableSo.FindProperty("_oneGrabTransformer").objectReferenceValue = transformer;
                grabbableSo.FindProperty("_rigidbody").objectReferenceValue = rb;
                grabbableSo.FindProperty("_kinematicWhileSelected").boolValue = true;
                grabbableSo.FindProperty("_throwWhenUnselected").boolValue = false;
                grabbableSo.ApplyModifiedPropertiesWithoutUndo();

                var interactable = root.GetComponent<HandGrabInteractable>();
                if (interactable == null) interactable = root.AddComponent<HandGrabInteractable>();
                var interactableSo = new SerializedObject(interactable);
                interactableSo.FindProperty("_rigidbody").objectReferenceValue = rb;
                interactableSo.FindProperty("_pointableElement").objectReferenceValue = grabbable;
                interactableSo.FindProperty("_supportedGrabTypes").intValue = (int)GrabTypeFlags.All;
                interactableSo.ApplyModifiedPropertiesWithoutUndo();

                if (addMarker && root.GetComponent<FruitMarker>() == null) root.AddComponent<FruitMarker>();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void MakeBasketTrigger(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var col = root.GetComponent<BoxCollider>();
                if (col == null) col = root.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.center = new Vector3(0f, 0.11f, 0f);
                col.size = new Vector3(0.26f, 0.18f, 0.26f);

                if (root.GetComponent<BasketTrigger>() == null) root.AddComponent<BasketTrigger>();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
