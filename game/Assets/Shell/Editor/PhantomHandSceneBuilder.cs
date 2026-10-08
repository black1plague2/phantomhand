using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oculus.Interaction;
using Opus.Games.PhantomHand;
using Opus.Games.PhantomHand.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// U2 Part B: builds Assets/Scenes/PhantomHand.unity entirely from code (Tools/OPUS/Build PhantomHand Scene,
    /// static BuildScene()). The Meta Building Blocks rig (Camera Rig + Hand Tracking + comprehensive interaction
    /// rig) is taken from a copy of OrchardReach.unity -- Building Blocks are editor-GUI only, so the proven rig is
    /// reused instead of re-installed -- and everything Orchard-specific is stripped. OrchardReach.unity itself is
    /// never modified. Rebuilding always starts from that copy, so the result is reproducible.
    /// Rig rules: one camera (CenterEyeAnchor), hand tracking only, hand and controller visuals OFF (tracking stays
    /// on), poke interactors on both hands, floor-level tracking origin, 72 Hz (RigRuntimeSettings).
    /// World frame: origin on the floor under the seat, +x right, +y up, +z forward.
    /// </summary>
    public static class PhantomHandSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/PhantomHand.unity";
        public const string OrchardScenePath = "Assets/Scenes/OrchardReach.unity";
        private const string AssetDir = "Assets/Games/PhantomHand/Materials";
        private const string RigName = "[BuildingBlock] Camera Rig";
        private const string OutDir = "logs/sessions/screens/ph/u2";

        // Layout (metres). The real right forearm rests at ArmX; the virtual one is offset_cm (15) to the left.
        public const float TableTopY = 0.75f;
        public const float TableCenterZ = 0.47f;
        public const float ArmX = 0.18f;
        public const float ElbowZ = 0.15f;
        public const float ForearmLen = 0.25f;
        public const float HandLen = 0.18f;
        public const float OffsetM = 0.15f;
        public static readonly Vector3 SeatedEye = new Vector3(0f, 1.18f, 0.02f);

        // GLB props (metres, world frame). Lamp: over the table's far half (z >= 0.47) but short of z 0.54, where the seated eye's line to the top of the results panel starts to hit the shade.
        // Window: on Wall_Left's inner face. Bowl and cup: the far right corner, >= 10 cm from the real hand's area (x .136-.224, z .15-.58) and clear of the virtual arm, the ruler and the table edges.
        private const float LampX = 0.10f, LampZ = 0.50f, LampShadeAboveTableM = 1.10f;   // 0.75 put the shade on the top edge of the results panel in the seated view (witness_en_t4_5s, 8 Oct)
        private const float WallLeftInnerX = -2.45f, WindowZ = 1.65f, WindowCenterY = 1.45f;
        private const float BowlX = 0.49f, BowlZ = 0.64f, CupX = 0.34f, CupZ = 0.71f;

        [MenuItem("Tools/OPUS/Build PhantomHand Scene")]
        public static string BuildMenu() { return BuildScene(); }

        public static string BuildScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Leave Play mode before building the scene.");
            EnsureDir(AssetDir);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OrchardScenePath) == null)
                throw new FileNotFoundException("Rig source scene missing: " + OrchardScenePath);

            // CopyAsset gives the copy a new GUID: the scene's .meta is put back, so its GUID (build settings, references) survives a rebuild
            string metaFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ScenePath + ".meta"));
            string keptMeta = File.Exists(metaFile) ? File.ReadAllText(metaFile) : null;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(OrchardScenePath, ScenePath)) throw new IOException("could not copy rig scene");
            if (keptMeta != null) { File.WriteAllText(metaFile, keptMeta); AssetDatabase.Refresh(); }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            // 1. strip everything that is not the Building Blocks rig
            var rig = FindRoot(scene, RigName);
            if (rig == null) throw new InvalidOperationException("rig root '" + RigName + "' not found in the copy");
            foreach (var go in scene.GetRootGameObjects()) if (go != rig) UnityEngine.Object.DestroyImmediate(go);
            foreach (var n in new[] { "ChestReference", "DemoHandProxy_R", "DemoHandProxy_R_GhostHand" })
            {
                var t = rig.transform.Find(n);
                if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 2. rig: hand visuals off (tracking on), controllers off, camera background
            int handOff = HideHandVisuals(rig);
            var centerEye = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            var cam = centerEye != null ? centerEye.GetComponent<Camera>() : null;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.10f, 0.11f);
                cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
            }
            var settings = rig.GetComponent<RigRuntimeSettings>();
            if (settings == null) settings = rig.AddComponent<RigRuntimeSettings>();   // not `??`: GetComponent returns a fake null in the Editor
            settings.rigRoot = rig.transform; settings.displayFrequencyHz = 72;

            // 3. materials
            var matWall = LitMat("PH_Wall", new Color(0.66f, 0.68f, 0.64f), 0.05f);
            var matFloor = LitMat("PH_Floor", new Color(0.34f, 0.31f, 0.28f), 0.08f);
            var matTable = LitMat("PH_Table", new Color(0.60f, 0.50f, 0.40f), 0.06f);   // matte
            var matRug = LitMat("PH_Rug", new Color(0.36f, 0.42f, 0.47f), 0.03f);
            var matPot = LitMat("PH_Pot", new Color(0.55f, 0.36f, 0.28f), 0.1f);
            var matLeaf = LitMat("PH_Leaf", new Color(0.26f, 0.45f, 0.30f), 0.05f);
            var matArt = LitMat("PH_Art", new Color(0.78f, 0.62f, 0.42f), 0.02f);
            var matOutline = UnlitMat("PH_Outline", new Color(0.30f, 0.95f, 0.75f));
            var matRulerBase = UnlitMat("PH_RulerBase", new Color(0.10f, 0.10f, 0.12f));
            var matRulerTick = UnlitMat("PH_RulerTick", new Color(0.92f, 0.92f, 0.88f));
            var matRulerMajor = UnlitMat("PH_RulerMajor", new Color(1.0f, 0.78f, 0.30f));
            var matDot = UnlitMat("PH_Dot", new Color(1.0f, 0.35f, 0.30f));
            var matPanel = UnlitMat("PH_Panel", new Color(0.07f, 0.07f, 0.09f));

            // models: bake any missing wrapper (rigged hand, table, brush, stone, sleeve, GLB props) before the room, the table and the presentation use them; the primitives stay the fallback
            bool modelsReady = Opus.Games.PhantomHand.EditorTools.PhantomModelImporter.EnsureWrappers();

            // 4. root object + environment
            var root = new GameObject("PhantomHand");
            var env = new GameObject("Environment").transform; env.SetParent(root.transform, false);
            var roomRoot = new GameObject("Room").transform; roomRoot.SetParent(env, false);
            Box("Floor", roomRoot, new Vector3(0f, -0.025f, 1.0f), new Vector3(7f, 0.05f, 7f), matFloor, collider: true);
            Box("Wall_Front", roomRoot, new Vector3(0f, 1.5f, 2.7f), new Vector3(7f, 3f, 0.1f), matWall, collider: false);
            Box("Wall_Left", roomRoot, new Vector3(-2.5f, 1.5f, 1.0f), new Vector3(0.1f, 3f, 7f), matWall, collider: false);
            Box("Baseboard_Front", roomRoot, new Vector3(0f, 0.06f, 2.64f), new Vector3(7f, 0.12f, 0.03f), matTable, collider: false);
            Box("Baseboard_Left", roomRoot, new Vector3(-2.44f, 0.06f, 1.0f), new Vector3(0.03f, 0.12f, 7f), matTable, collider: false);
            // the room is closed: a wearer who turns right or round must not look into the void (8 Oct, seen in room_eye once the ceiling was there)
            Box("Wall_Right", roomRoot, new Vector3(2.5f, 1.5f, 1.0f), new Vector3(0.1f, 3f, 7f), matWall, collider: false);
            Box("Wall_Back", roomRoot, new Vector3(0f, 1.5f, -1.6f), new Vector3(5.1f, 3f, 0.1f), matWall, collider: false);
            Box("Baseboard_Right", roomRoot, new Vector3(2.44f, 0.06f, 1.0f), new Vector3(0.03f, 0.12f, 7f), matTable, collider: false);
            Box("Baseboard_Back", roomRoot, new Vector3(0f, 0.06f, -1.54f), new Vector3(5.1f, 0.12f, 0.03f), matTable, collider: false);
            var placed = new List<string>();     // GLB prop wrappers that made it into the scene (reported at the end)
            // framed picture: pivot = middle of the back face on the front wall's inner face (z 2.65), front (+z) turned to the room; the two boxes stay the fallback
            if (Place(PhModels.Picture, roomRoot, new Vector3(0.9f, 1.55f, 2.65f), Quaternion.Euler(0f, 180f, 0f), placed) == null)
            {
                Box("Picture", roomRoot, new Vector3(0.9f, 1.55f, 2.64f), new Vector3(0.9f, 0.6f, 0.02f), matArt, collider: false);
                Box("PictureFrame", roomRoot, new Vector3(0.9f, 1.55f, 2.655f), new Vector3(0.98f, 0.68f, 0.01f), matTable, collider: false);
            }
            var rug = Prim(PrimitiveType.Cylinder, "Rug", roomRoot, new Vector3(0f, 0.004f, 0.55f), new Vector3(2.2f, 0.004f, 2.2f), matRug);
            UnityEngine.Object.DestroyImmediate(rug.GetComponent<Collider>());
            var plant = new GameObject("Plant").transform; plant.SetParent(roomRoot, false); plant.position = new Vector3(-1.8f, 0f, 2.2f);
            if (Place(PhModels.Plant, plant, plant.position, Quaternion.identity, placed) == null)      // pivot = bottom centre of the pot, on the floor
            {
                Prim(PrimitiveType.Cylinder, "Pot", plant, new Vector3(-1.8f, 0.22f, 2.2f), new Vector3(0.34f, 0.22f, 0.34f), matPot, removeCollider: true);
                Prim(PrimitiveType.Sphere, "Leaves_A", plant, new Vector3(-1.8f, 0.72f, 2.2f), new Vector3(0.55f, 0.6f, 0.55f), matLeaf, removeCollider: true);
                Prim(PrimitiveType.Sphere, "Leaves_B", plant, new Vector3(-1.62f, 0.98f, 2.12f), new Vector3(0.36f, 0.4f, 0.36f), matLeaf, removeCollider: true);
            }

            // window on the left wall (pivot = middle of the back face, flush with the wall, front turned to the room) and the pendant lamp; both carry something that shines by itself
            var glowRenderers = new List<Renderer>();     // registered with the DarkenController: they go black in the probe phases
            var window = Place(PhModels.Window, roomRoot, new Vector3(WallLeftInnerX, WindowCenterY, WindowZ), Quaternion.Euler(0f, 90f, 0f), placed);
            if (window != null) { var glow = BuildWindowGlow(window.transform); if (glow != null) glowRenderers.Add(glow); }
            var lamp = Place(PhModels.Lamp, roomRoot, new Vector3(LampX, 0f, LampZ), Quaternion.identity, placed);
            float shadeBottomY = TableTopY + LampShadeAboveTableM;
            if (lamp != null) glowRenderers.Add(HangLamp(lamp, roomRoot, matWall, shadeBottomY));

            var table = new GameObject("Table").transform; table.SetParent(env, false);
            if (PhModels.SpawnTable(table, new Vector3(0f, TableTopY, TableCenterZ)) == null)   // the table model carries its own top collider; no wrapper -> the box table
            {
                Box("TableTopSurface", table, new Vector3(0f, TableTopY - 0.02f, TableCenterZ), new Vector3(1.2f, 0.04f, 0.7f), matTable, collider: true);
                foreach (var sx in new[] { -0.55f, 0.55f })
                    foreach (var sz in new[] { -0.30f, 0.30f })
                        Box("Leg", table, new Vector3(sx, (TableTopY - 0.04f) / 2f, TableCenterZ + sz), new Vector3(0.06f, TableTopY - 0.04f, 0.06f), matTable, collider: false);
            }
            // the singing bowl (striker on the near side) and the tea cup (handle to the right) on the table's far right corner; bottom-centre pivots stand on the table top
            var tableProps = new GameObject("TableProps").transform; tableProps.SetParent(env, false);
            Place(PhModels.Bowl, tableProps, new Vector3(BowlX, TableTopY, BowlZ), Quaternion.Euler(0f, 180f, 0f), placed);
            Place(PhModels.Cup, tableProps, new Vector3(CupX, TableTopY, CupZ), Quaternion.Euler(0f, 205f, 0f), placed);
            foreach (var t in env.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);

            // 5. lighting: warm key + ambient + light fog (shadows off on Quest; the stone gets its own blob shadow in U6)
            var lighting = new GameObject("Lighting").transform; lighting.SetParent(root.transform, false);
            var keyGo = new GameObject("KeyLight"); keyGo.transform.SetParent(lighting, false);
            keyGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional; key.color = new Color(1f, 0.90f, 0.76f); key.intensity = lamp != null ? 0.80f : 1.05f; key.shadows = LightShadows.None;   // under the pendant the key drops a little: the table is the bright spot
            Light pendantLight = null;
            if (lamp != null)
            {
                var pendantGo = new GameObject("PendantLight"); pendantGo.transform.SetParent(lighting, false);
                pendantGo.transform.position = new Vector3(LampX, shadeBottomY - 0.06f, LampZ);       // just under the shade; realtime, no shadows
                pendantLight = pendantGo.AddComponent<Light>();
                pendantLight.type = LightType.Point; pendantLight.color = new Color(1f, 0.76f, 0.48f); pendantLight.intensity = 1.3f; pendantLight.range = 2.6f; pendantLight.shadows = LightShadows.None;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.57f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.40f, 0.37f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.16f);
            RenderSettings.ambientIntensity = 1f; RenderSettings.reflectionIntensity = 0.3f;
            RenderSettings.skybox = null;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.04f;
            RenderSettings.fogColor = new Color(0.62f, 0.60f, 0.58f);

            // 6. anchors
            var anchors = root.AddComponent<PhantomAnchors>();
            anchors.cameraRig = rig.transform;
            anchors.tableTop = Empty("TableTop", root.transform, new Vector3(0f, TableTopY, TableCenterZ));
            float wristZ = ElbowZ + ForearmLen;
            // ArmRestOutline: where the real right forearm + hand rest (palm down, fingers away from the body)
            var outline = Empty("ArmRestOutline", root.transform, new Vector3(ArmX, TableTopY + 0.003f, ElbowZ));
            BuildOutline(outline, matOutline);
            anchors.armRestOutline = outline;
            anchors.virtualArmAnchor = Empty("VirtualArmAnchor", root.transform, new Vector3(ArmX - OffsetM, TableTopY, wristZ));
            anchors.brushRig = Empty("BrushRig", root.transform, new Vector3(ArmX - OffsetM, TableTopY, wristZ));
            anchors.threatDropPoint = Empty("ThreatDropPoint", root.transform, new Vector3(ArmX - OffsetM, TableTopY + PhantomAnchors.DropHeightM, wristZ + 0.09f));
            var ruler = BuildRuler(root.transform, matRulerBase, matRulerTick, matRulerMajor);
            anchors.probeRuler = ruler;

            var dot = Prim(PrimitiveType.Sphere, "LeftIndexDot", root.transform, new Vector3(-0.15f, TableTopY + 0.12f, PhantomAnchors.RulerDistanceM), Vector3.one * 0.012f, matDot, removeCollider: true);
            var tip = dot.AddComponent<TipDot>(); tip.dotRenderer = dot.GetComponent<MeshRenderer>(); tip.visibleOnStart = false;
            tip.dotRenderer.enabled = false;
            anchors.leftIndexDot = dot.transform;

            // 7. panels (world-space uGUI; contents are filled by U4)
            anchors.instructionPanel = Panel("InstructionPanel", root.transform, new Vector3(0f, 1.00f, 0.60f), new Vector3(26f, 0f, 0f), 0.64f, 0.26f, matPanel);
            anchors.questionnairePanel = Panel("QuestionnairePanel", root.transform, new Vector3(0f, 1.06f, 0.47f), new Vector3(18f, 0f, 0f), 0.56f, 0.30f, matPanel);
            anchors.witnessPanel = Panel("WitnessPanel", root.transform, new Vector3(0f, 1.28f, 0.95f), new Vector3(8f, 0f, 0f), 1.0f, 0.66f, matPanel);
            anchors.hudPanel = Panel("HudPanel", root.transform, new Vector3(-0.34f, 1.36f, 0.80f), new Vector3(10f, 0f, 0f), 0.36f, 0.20f, matPanel);

            // 8. darken controller (probes), seated eye pose, audio
            var darkGo = new GameObject("DarkenController"); darkGo.transform.SetParent(root.transform, false);
            var dark = darkGo.AddComponent<DarkenController>();
            dark.lights = pendantLight != null ? new[] { key, pendantLight } : new[] { key };     // the key stays first (the rig test reads lights[0])
            dark.renderers = glowRenderers.ToArray(); dark.cameraToTint = cam; dark.fadeSeconds = 0.6f;
            dark.Capture();
            anchors.darken = dark;
            anchors.seatedEyePose = Empty("SeatedEyePose", root.transform, SeatedEye);
            anchors.seatedEyePose.rotation = Quaternion.LookRotation((new Vector3(0.05f, TableTopY, 0.38f) - SeatedEye).normalized);
            string audio = BuildAudio(root.transform, anchors);
            BuildPresentation(root.transform, anchors);   // U3: virtual arm, brush, stone, presenter
            AddUi(root.transform, anchors);               // U4: event system for poke, UI presenter
            AddController(root);                          // U5: composition root (finds anchors + presenters itself in Awake)

            // 9. build settings: OrchardReach first, PhantomHand after it
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == OrchardScenePath)) list.Add(new EditorBuildSettingsScene(OrchardScenePath, true));
            if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return "built " + ScenePath + ": handRenderersOff=" + handOff + ", camera=" + (cam != null) + ", audio=" + audio +
                   ", buildScenes=" + EditorBuildSettings.scenes.Length + ", models=" + modelsReady + ", props=" + string.Join("+", placed.ToArray());
        }

        // ---------------------------------------------------------------------------------------------------------
        // rig helpers

        private static int HideHandVisuals(GameObject rig)
        {
            int n = 0;
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                string p = PathOf(r.transform);
                if (p.Contains("HandVisual") || p.Contains("Hand Tracking") || p.Contains("Controller"))
                {
                    if (r.enabled) { r.enabled = false; n++; }
                }
            }
            foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == "HandVisual")
                {
                    var so = new SerializedObject(mb);
                    var prop = so.FindProperty("_forceOffVisibility");
                    if (prop != null) { prop.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                }
            }
            foreach (var name in new[] { "OVRControllerVisualLeft", "OVRControllerVisualRight" })
            {
                var t = FindDeep(rig.transform, name);
                if (t != null) t.gameObject.SetActive(false);
            }
            return n;
        }

        // ---------------------------------------------------------------------------------------------------------
        // geometry helpers

        private static void BuildOutline(Transform outline, Material mat)
        {
            // silhouette of forearm + hand, palm down, elbow at local z=0, fingers toward +z; x = lateral
            var half = new List<Vector2>
            {
                new Vector2(0.036f, 0f), new Vector2(0.040f, 0.06f), new Vector2(0.035f, 0.16f), new Vector2(0.028f, ForearmLen),
                new Vector2(0.040f, ForearmLen + 0.04f), new Vector2(0.044f, ForearmLen + 0.09f), new Vector2(0.043f, ForearmLen + HandLen - 0.02f),
                new Vector2(0.030f, ForearmLen + HandLen),
            };
            var pts = new List<Vector3>();
            foreach (var p in half) pts.Add(new Vector3(p.x, 0f, p.y));
            for (int i = half.Count - 1; i >= 0; i--) pts.Add(new Vector3(-half[i].x, 0f, half[i].y));
            var lr = outline.gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.loop = true; lr.positionCount = pts.Count; lr.SetPositions(pts.ToArray());
            lr.widthMultiplier = 0.006f; lr.numCornerVertices = 4; lr.alignment = LineAlignment.View;
            lr.sharedMaterial = mat; lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            outline.gameObject.SetActive(false);   // shown by the calibration presenter
        }

        private static Transform BuildRuler(Transform parent, Material baseMat, Material tickMat, Material majorMat)
        {
            var rulerGo = new GameObject("ProbeRuler"); rulerGo.transform.SetParent(parent, false);
            rulerGo.transform.position = new Vector3(0f, TableTopY + 0.002f, PhantomAnchors.RulerDistanceM);
            float len = PhantomAnchors.RulerLengthM;
            // base strip
            var baseMesh = QuadsMesh("PH_RulerBase", new List<Rect> { new Rect(-len / 2f, -0.025f, len, 0.05f) });
            AddMeshObject("Base", rulerGo.transform, baseMesh, baseMat, 0f);
            var minor = new List<Rect>(); var major = new List<Rect>();
            for (int cm = 0; cm <= 100; cm++)
            {
                float x = -len / 2f + cm * 0.01f;
                bool ten = cm % 10 == 0, five = cm % 5 == 0;
                float h = ten ? 0.040f : five ? 0.028f : 0.016f;
                float w = ten ? 0.003f : 0.0015f;
                var r = new Rect(x - w / 2f, -0.0205f, w, h);
                if (ten || cm == 50) major.Add(r); else minor.Add(r);
            }
            AddMeshObject("Ticks", rulerGo.transform, QuadsMesh("PH_RulerTicks", minor), tickMat, 0.0004f);
            AddMeshObject("TicksMajor", rulerGo.transform, QuadsMesh("PH_RulerMajor", major), majorMat, 0.0008f);
            rulerGo.SetActive(false);   // shown by the probe presenter
            return rulerGo.transform;
        }

        /// <summary>Flat quads lying on the table plane; Rect.x = lateral (x), Rect.y = depth (z).</summary>
        private static Mesh QuadsMesh(string name, List<Rect> rects)
        {
            var verts = new List<Vector3>(); var tris = new List<int>(); var norms = new List<Vector3>();
            foreach (var r in rects)
            {
                int i = verts.Count;
                verts.Add(new Vector3(r.xMin, 0f, r.yMin)); verts.Add(new Vector3(r.xMin, 0f, r.yMax));
                verts.Add(new Vector3(r.xMax, 0f, r.yMax)); verts.Add(new Vector3(r.xMax, 0f, r.yMin));
                for (int k = 0; k < 4; k++) norms.Add(Vector3.up);
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            string path = AssetDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void AddMeshObject(string name, Transform parent, Mesh mesh, Material mat, float lift)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0f, lift, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        private static Transform Panel(string name, Transform parent, Vector3 pos, Vector3 euler, float wM, float hM, Material bg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 2f;
            go.AddComponent<GraphicRaycaster>();
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(wM * 1000f, hM * 1000f); rt.localScale = Vector3.one * 0.001f;
            var imgGo = new GameObject("Background", typeof(RectTransform), typeof(Image)); imgGo.transform.SetParent(go.transform, false);
            var irt = imgGo.GetComponent<RectTransform>(); irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = irt.offsetMax = Vector2.zero;
            imgGo.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.09f, 0.92f);
            var txtGo = new GameObject("Label", typeof(RectTransform), typeof(Text)); txtGo.transform.SetParent(go.transform, false);
            var trt = txtGo.GetComponent<RectTransform>(); trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(20, 10); trt.offsetMax = new Vector2(-20, -10);
            var txt = txtGo.GetComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.text = name; txt.alignment = TextAnchor.MiddleCenter; txt.color = new Color(0.8f, 0.8f, 0.82f); txt.fontSize = Mathf.RoundToInt(hM * 1000f * 0.18f);
            go.SetActive(false);   // shown by U4 presenters
            return go.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, pos, size, mat, removeCollider: !collider);
            return go;
        }

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 worldPos, Vector3 scale, Material mat, bool removeCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false); go.transform.position = worldPos; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (removeCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static Transform Empty(string name, Transform parent, Vector3 worldPos)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = worldPos;
            return go.transform;
        }

        /// <summary>Spawns a baked prop wrapper (PhModels) at a world pose and notes it in `placed`; null, and nothing in the scene, when the wrapper does not exist.</summary>
        private static GameObject Place(string wrapper, Transform parent, Vector3 worldPos, Quaternion rot, List<string> placed)
        {
            var go = PhModels.Spawn(wrapper, parent);
            if (go == null) return null;
            go.transform.SetPositionAndRotation(worldPos, rot);
            placed.Add(wrapper);
            return go;
        }

        /// <summary>The unlit dusk-coloured quad behind the window glass (the scene has no sky): a little larger than the glass quad, which the frame hides, 4 cm behind it, facing the room.</summary>
        private static Renderer BuildWindowGlow(Transform window)
        {
            var mr = window.GetComponentInChildren<MeshRenderer>();
            var mats = mr.sharedMaterials; int glass = -1;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.Contains("Glass")) glass = i;
            if (glass < 0) return null;
            Bounds gb = mr.GetComponent<MeshFilter>().sharedMesh.GetSubMesh(glass).bounds;         // the glass in the wrapper's own space (the model child sits at the wrapper's origin)
            var quad = Prim(PrimitiveType.Quad, "WindowGlow", mr.transform, Vector3.zero, new Vector3(gb.size.x + 0.06f, gb.size.y + 0.06f, 1f), UnlitMat("PH_WindowDusk", new Color(0.74f, 0.47f, 0.33f)), removeCollider: true);
            quad.transform.localPosition = gb.center + new Vector3(0f, 0f, -0.04f);
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);                         // the Quad primitive faces -z, the room is on +z
            return quad.GetComponent<MeshRenderer>();
        }

        /// <summary>Hangs the pendant (pivot = top of the canopy) so that the lowest point of the shade is at shadeBottomY, closes the room with a ceiling slab at the canopy, and swaps the
        /// linen shade for an unlit warm one with the same weave (it has to glow without the lights; the probe phases darken it through the DarkenController). Returns the lamp's renderer.</summary>
        private static MeshRenderer HangLamp(GameObject lamp, Transform room, Material wallMat, float shadeBottomY)
        {
            var mr = lamp.GetComponentInChildren<MeshRenderer>();
            float topY = shadeBottomY + (lamp.transform.position.y - mr.bounds.min.y);              // the lamp was placed at y 0: bounds.min.y is how far the shade hangs below the canopy
            lamp.transform.position = new Vector3(LampX, topY, LampZ);
            Box("Ceiling", room, new Vector3(0f, topY - 0.002f + 0.025f, 1.0f), new Vector3(7f, 0.05f, 7f), wallMat, collider: false);
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name.Contains("Linen"))
                {
                    var shade = UnlitMat("PH_LampShade", new Color(1f, 0.88f, 0.68f));
                    shade.SetTexture("_BaseMap", mats[i].GetTexture("_BaseMap"));
                    EditorUtility.SetDirty(shade);
                    mats[i] = shade;
                }
            mr.sharedMaterials = mats;
            return mr;
        }

        // ---------------------------------------------------------------------------------------------------------
        // materials

        private static Material LitMat(string name, Color c, float smoothness)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m); return m;
        }

        private static Material UnlitMat(string name, Color c)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m); return m;
        }

        private static Material LoadOrCreate(string name, string shader)
        {
            string path = AssetDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (sh == null) throw new InvalidOperationException("shader not found: " + shader);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        // ---------------------------------------------------------------------------------------------------------
        // audio

        private static string BuildAudio(Transform parent, PhantomAnchors anchors)
        {
            string mixerPath = "Assets/Games/PhantomHand/PhantomHandMixer.mixer";
            string status;
            UnityEngine.Audio.AudioMixer mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(mixerPath);
            if (mixer == null)
            {
                try
                {
                    var ctrlType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
                    var create = ctrlType.GetMethod("CreateMixerControllerAtPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    var ctrl = create.Invoke(null, new object[] { mixerPath });
                    var master = ctrlType.GetProperty("masterGroup").GetValue(ctrl);
                    var groupType = Type.GetType("UnityEditor.Audio.AudioMixerGroupController, UnityEditor");
                    var createGroup = ctrlType.GetMethod("CreateNewGroup", new[] { typeof(string), typeof(bool) });
                    var addChild = ctrlType.GetMethod("AddChildToParent", new[] { groupType, groupType });
                    foreach (var g in new[] { "Sfx", "Voice" })
                    {
                        var grp = createGroup.Invoke(ctrl, new object[] { g, true });
                        addChild.Invoke(ctrl, new[] { grp, master });
                    }
                    EditorUtility.SetDirty((UnityEngine.Object)ctrl);
                    AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(mixerPath);
                    mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(mixerPath);
                }
                catch (Exception e) { Debug.LogWarning("[PhantomHand] audio mixer creation failed: " + e.Message); }
            }
            var audioRoot = new GameObject("Audio").transform; audioRoot.SetParent(parent, false);
            anchors.audioRoot = audioRoot;
            if (mixer == null) return "mixer-unavailable";
            var sfx = mixer.FindMatchingGroups("Sfx"); var voice = mixer.FindMatchingGroups("Voice");
            foreach (var n in new[] { "Sfx_Swish", "Sfx_Thud", "Sfx_Creak", "Sfx_Tick" }) AudioPlaceholder(n, audioRoot, sfx.Length > 0 ? sfx[0] : null);
            AudioPlaceholder("Voice", audioRoot, voice.Length > 0 ? voice[0] : null);
            status = "mixer(Sfx=" + (sfx.Length > 0) + ",Voice=" + (voice.Length > 0) + ")";
            return status;
        }

        private static void AudioPlaceholder(string name, Transform parent, UnityEngine.Audio.AudioMixerGroup group)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.loop = false; src.spatialBlend = 0f; src.outputAudioMixerGroup = group;
        }

        // ---------------------------------------------------------------------------------------------------------
        // U3 presentation (virtual arm, brush, stone) -- additive

        private const string ArtMatDir = "Assets/Art/PhantomHand/Materials";
        private const string U3OutDir = "logs/sessions/screens/ph/u3";

        private static Material ArtMat(string name, Color c, float smoothness, bool unlit = false, bool particles = false)
        {
            EnsureDir(ArtMatDir);
            string path = ArtMatDir + "/" + name + ".mat";
            string shader = particles ? "Universal Render Pipeline/Particles/Unlit" : unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit";
            var sh = Shader.Find(shader) ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (unlit || particles) { if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Adds Presentation/{VirtualArm, BrushRig, ThreatDrop} + ArmThreatPresenter. Geometry is generated at runtime
        /// (Awake/Bind); the scene only holds the components, materials and anchor handles.</summary>
        private static void BuildPresentation(Transform root, PhantomAnchors anchors)
        {
            var pres = new GameObject("Presentation"); pres.transform.SetParent(root, false);
            var armGo = new GameObject("VirtualArm"); armGo.transform.SetParent(pres.transform, false);
            armGo.transform.position = anchors.virtualArmAnchor.position;
            var arm = armGo.AddComponent<VirtualArmRig>();
            arm.materials.skin = ArtMat("PH_Skin", new Color(0.74f, 0.58f, 0.48f), 0.25f);
            arm.materials.sleeve = ArtMat("PH_Sleeve", new Color(0.06f, 0.06f, 0.08f), 0.1f);
            arm.materials.band = ArtMat("PH_Band", new Color(0.25f, 0.85f, 0.70f), 0.2f);
            var brushGo = new GameObject("BrushRig"); brushGo.transform.SetParent(pres.transform, false);
            var brush = brushGo.AddComponent<BrushRig>();
            brush.handleMaterial = ArtMat("PH_BrushHandle", new Color(0.55f, 0.38f, 0.22f), 0.35f);
            brush.bristleMaterial = ArtMat("PH_Bristle", new Color(0.93f, 0.88f, 0.76f), 0.1f);
            var dropGo = new GameObject("ThreatDrop"); dropGo.transform.SetParent(pres.transform, false);
            var drop = dropGo.AddComponent<ThreatDrop>();
            drop.stoneMaterial = ArtMat("PH_Stone", new Color(0.46f, 0.45f, 0.43f), 0.12f);
            drop.shadowMaterial = ArtMat("PH_Shadow", new Color(0f, 0f, 0f, 0.6f), 0f, unlit: true);
            drop.dustMaterial = ArtMat("PH_Dust", new Color(0.78f, 0.72f, 0.62f, 1f), 0f, unlit: true, particles: true);
            if (anchors.audioRoot != null)
            {
                brush.swishSource = SourceNamed(anchors.audioRoot, "Sfx_Swish");
                drop.thudSource = SourceNamed(anchors.audioRoot, "Sfx_Thud");
                drop.creakSource = SourceNamed(anchors.audioRoot, "Sfx_Creak");
            }
            var presenter = pres.AddComponent<ArmThreatPresenter>();
            presenter.arm = arm; presenter.brush = brush; presenter.threat = drop; presenter.anchors = anchors;
            anchors.brushRig = brushGo.transform;
        }

        /// <summary>U4: EventSystem + PointableCanvasModule (so ISDK pokes become uGUI clicks) and the PhantomHandUiPresenter that
        /// builds the panel contents at runtime. Idempotent.</summary>
        private static void AddUi(Transform root, PhantomAnchors anchors)
        {
            if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(root, false);
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<PointableCanvasModule>();
            }
            var pres = root.Find("Presentation");
            if (pres == null) throw new InvalidOperationException("Presentation root missing; run BuildPresentation first");
            var ui = pres.GetComponent<PhantomHandUiPresenter>();
            if (ui == null) ui = pres.gameObject.AddComponent<PhantomHandUiPresenter>();
            ui.anchors = anchors;
            ui.armPresenter = pres.GetComponent<ArmThreatPresenter>();
            ui.attachPoke = true;
            // all panels start hidden; the presenter shows them per phase
            foreach (var t in new[] { anchors.instructionPanel, anchors.questionnairePanel, anchors.witnessPanel, anchors.hudPanel })
                if (t != null) t.gameObject.SetActive(false);
        }

        /// <summary>U5: PhantomHandSceneController on the scene root (next to PhantomAnchors). Idempotent.</summary>
        private static void AddController(GameObject root)
        {
            if (root.GetComponent<PhantomHandSceneController>() == null) root.AddComponent<PhantomHandSceneController>();
        }

        /// <summary>U4 on an existing PhantomHand.unity without a full rebuild: adds the instruction panel anchor, resizes the panels,
        /// adds the event system, the UI presenter and (U5) the scene controller.</summary>
        public static string BuildUi()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Leave Play mode first.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var anchors = UnityEngine.Object.FindFirstObjectByType<PhantomAnchors>();
            var root = anchors.transform;
            var matPanel = UnlitMat("PH_Panel", new Color(0.07f, 0.07f, 0.09f));
            if (anchors.instructionPanel == null)
                anchors.instructionPanel = Panel("InstructionPanel", root, new Vector3(0f, 1.00f, 0.60f), new Vector3(26f, 0f, 0f), 0.64f, 0.26f, matPanel);
            Resize(anchors.questionnairePanel, new Vector3(0f, 1.06f, 0.47f), 0.56f, 0.30f);
            Resize(anchors.witnessPanel, new Vector3(0f, 1.28f, 0.95f), 1.0f, 0.66f);
            Resize(anchors.hudPanel, new Vector3(-0.34f, 1.36f, 0.80f), 0.36f, 0.20f);
            AddUi(root, anchors);
            AddController(anchors.gameObject);
            EditorUtility.SetDirty(anchors);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return "U4 ui added: instructionPanel=" + (anchors.instructionPanel != null) + ", eventSystem=" +
                   (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null);
        }

        private static void Resize(Transform panel, Vector3 pos, float wM, float hM)
        {
            panel.position = pos;
            ((RectTransform)panel).sizeDelta = new Vector2(wM * 1000f, hM * 1000f);
        }

        private static AudioSource SourceNamed(Transform audioRoot, string n)
        {
            var t = audioRoot.Find(n);
            return t != null ? t.GetComponent<AudioSource>() : null;
        }

        /// <summary>Renders contact A, contact B, telegraph and impact into logs/sessions/screens/ph/u3/ (edit mode, no physics: the impact shot
        /// puts the stone on the palm and simulates the dust burst). Restores the scene.</summary>
        public static string CaptureU3Shots()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var pres = UnityEngine.Object.FindFirstObjectByType<ArmThreatPresenter>();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", U3OutDir));
            Directory.CreateDirectory(dir);
            var p = new PhantomHandParams();
            pres.arm.Build(p); pres.brush.Build(); pres.threat.Build();
            var realWrist = new Vector3(ArmX, TableTopY + 0.021f, ElbowZ + ForearmLen);
            pres.arm.PlaceFromCalibration(realWrist, Vector3.forward, (float)p.OffsetCm);
            pres.arm.Visible = true; pres.brush.Visible = true;
            pres.brush.SetGeometry(pres.arm.WristWorld, pres.arm.ElbowDirection, pres.arm.ForearmLengthM, pres.arm.MotorAFromWristM, pres.arm.MotorSpacingM, p.MotorSoaMs);
            var plan = new StrokeScheduler(p, 1).Plan(PhCondition.Sync, 0, 20000);
            pres.brush.SetPlan(plan);
            var written = new List<string>();
            Vector3 eye = SeatedEye;
            Vector3 look = pres.arm.AxisWorldPos(0f) + pres.arm.transform.forward * 0.05f;
            var s1 = plan[1];
            pres.brush.PoseAt(s1.PassAMs);
            written.Add(Shot(dir, "contact_a", eye, look, 62f));
            written.Add(Shot(dir, "contact_a_close", pres.arm.WorldPos(5f) + new Vector3(0.12f, 0.22f, -0.20f), pres.arm.WorldPos(5f), 38f));
            pres.brush.PoseAt(s1.PassBMs);
            written.Add(Shot(dir, "contact_b", eye, look, 62f));
            written.Add(Shot(dir, "contact_b_close", pres.arm.WorldPos(15f) + new Vector3(0.12f, 0.22f, -0.20f), pres.arm.WorldPos(15f), 38f));
            // telegraph: stone hanging above the hand, shadow growing
            pres.brush.Visible = false;
            var palmTop = pres.arm.PalmTopWorld; var drop = palmTop + Vector3.up * PhantomAnchors.DropHeightM;
            pres.threat.Begin(drop, palmTop, 0);
            pres.threat.Tick(520);
            written.Add(Shot(dir, "telegraph", eye, palmTop + Vector3.up * 0.13f, 72f));
            // impact: stone on the palm, dust burst
            pres.threat.Body.transform.position = palmTop + Vector3.up * 0.052f;
            pres.threat.EmitDust(palmTop);
            foreach (var ps in pres.threat.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(0.16f, true, true);
            written.Add(Shot(dir, "impact", eye, palmTop + Vector3.up * 0.08f, 52f));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // discard transient changes
            return string.Join(";", written);
        }

        private const string ModelsOutDir = "logs/sessions/screens/ph/models";

        /// <summary>Renders the model bake into logs/sessions/screens/ph/models/ so it can be judged in one call: the arm with the hand from the participant's eye (arm_eye), the hand
        /// from above (hand_top) and from a low side view at curl 0 / 0.5 / 1 with the table hidden (hand_side_curl0/50/100: the finger bones), the brush at contact (brush_contact,
        /// brush_contact_close), the stone at telegraph and impact, the table and the whole room, the room from the seated eye (room_eye) and the props on the table's far right
        /// corner (table_props). Edit mode, no physics; restores the scene. Run
        /// PhantomModelImporter.RunBatch() and BuildScene() first so the wrappers exist and the scene holds the model table.</summary>
        public static string CaptureModelShots()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var pres = UnityEngine.Object.FindFirstObjectByType<ArmThreatPresenter>();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ModelsOutDir));
            Directory.CreateDirectory(dir);
            var p = new PhantomHandParams();
            pres.arm.Build(p); pres.brush.Build(); pres.threat.Build();
            var realWrist = new Vector3(ArmX, TableTopY + 0.021f, ElbowZ + ForearmLen);
            pres.arm.PlaceFromCalibration(realWrist, Vector3.forward, (float)p.OffsetCm);
            pres.arm.Visible = true; pres.brush.Visible = false;
            var written = new List<string>();
            Vector3 eye = SeatedEye;
            Vector3 look = pres.arm.AxisWorldPos(0f) + pres.arm.transform.forward * 0.05f;
            Vector3 palmTop = pres.arm.PalmTopWorld;

            // 1 the arm with the hand, from the participant's eye
            written.Add(Shot(dir, "arm_eye", eye, look, 62f));
            // 2 the hand from above, then the finger bones from a low side view (curl 0 / 0.5 / 1; the table is hidden because closing fingers dive into it)
            Vector3 above = palmTop + new Vector3(0f, 0.32f, -0.07f), aim = palmTop + pres.arm.transform.forward * 0.045f;
            written.Add(Shot(dir, "hand_top", above, aim, 45f));
            var tableGo = GameObject.Find("Table");
            if (tableGo != null) tableGo.SetActive(false);
            Vector3 side = palmTop + new Vector3(-0.26f, 0.09f, 0.14f), sideAim = palmTop + pres.arm.transform.forward * 0.07f;
            // edit mode has no player loop to re-skin the hand between shots: skin at every render
            foreach (var smr in pres.arm.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
            foreach (float c in new[] { 0f, 0.5f, 1f })
            {
                pres.arm.Curl = c;
                written.Add(Shot(dir, "hand_side_curl" + Mathf.RoundToInt(c * 100f), side, sideAim, 40f));
            }
            pres.arm.Curl = 0f;
            if (tableGo != null) tableGo.SetActive(true);
            // 3 the brush at contact (motor A)
            pres.brush.Visible = true;
            pres.brush.SetGeometry(pres.arm.WristWorld, pres.arm.ElbowDirection, pres.arm.ForearmLengthM, pres.arm.MotorAFromWristM, pres.arm.MotorSpacingM, p.MotorSoaMs);
            var plan = new StrokeScheduler(p, 1).Plan(PhCondition.Sync, 0, 20000);
            pres.brush.SetPlan(plan);
            pres.brush.PoseAt(plan[1].PassAMs);
            written.Add(Shot(dir, "brush_contact", eye, look, 62f));
            written.Add(Shot(dir, "brush_contact_close", pres.arm.WorldPos(5f) + new Vector3(0.12f, 0.22f, -0.20f), pres.arm.WorldPos(5f), 38f));
            pres.brush.Visible = false;
            // 4 the stone: telegraph, then on the palm with the dust burst
            Vector3 drop = palmTop + Vector3.up * PhantomAnchors.DropHeightM;
            pres.threat.Begin(drop, palmTop, 0);
            pres.threat.Tick(520);
            written.Add(Shot(dir, "stone_telegraph", eye, palmTop + Vector3.up * 0.13f, 72f));
            pres.threat.Body.transform.position = palmTop + Vector3.up * 0.052f;
            pres.threat.EmitDust(palmTop);
            foreach (var ps in pres.threat.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(0.16f, true, true);
            written.Add(Shot(dir, "stone_impact", eye, palmTop + Vector3.up * 0.08f, 52f));
            pres.threat.Cancel();
            foreach (var ps in pres.threat.GetComponentsInChildren<ParticleSystem>()) ps.Clear(true);   // no dust left in the table and room shots
            // 5 the table (model or box fallback) and the whole room
            written.Add(Shot(dir, "table", eye, new Vector3(0.05f, TableTopY, 0.40f), 95f));
            written.Add(Shot(dir, "room", new Vector3(1.5f, 1.7f, -1.3f), new Vector3(0f, 0.8f, 1.0f), 62f));
            // 6 the room as the seated participant sees it when looking straight ahead (the window at the left edge, the pendant above the table, the picture and the plant on the far wall),
            // and the bowl and the tea cup on the table's far right corner
            written.Add(Shot(dir, "room_eye", eye, new Vector3(eye.x, eye.y, 2.65f), 80f));
            written.Add(Shot(dir, "room_eye_right", eye, new Vector3(2.45f, eye.y, -0.6f), 80f));     // turned right and a little back: the right and back walls close the room
            written.Add(Shot(dir, "table_props", eye, new Vector3(0.42f, TableTopY + 0.04f, 0.68f), 40f));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // discard transient changes
            return string.Join(";", written);
        }

        // ---------------------------------------------------------------------------------------------------------
        // frame sequences for pitch material

        /// <summary>Frame sequences of the run, rendered in edit mode at 30 fps into outDir (PNG, 1600x900; large, so never a folder of this repository): one brush stroke along
        /// the arm (stroke_###), the Dissolve, where the arm fades while the brush goes on (dissolve_###), and the stone from telegraph to impact (stone_###).
        /// The poses come from the code the game runs (BrushRig.PoseAt, VirtualArmRig.Alpha, ThreatDrop.Tick); only the stone's fall is placed by hand, because edit mode has
        /// no physics. Returns the frame counts and the stroke's times, so a film can line graphics up with the brush.</summary>
        public static string CaptureDemoSequence(string outDir)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var pres = UnityEngine.Object.FindFirstObjectByType<ArmThreatPresenter>();
            Directory.CreateDirectory(outDir);
            var p = new PhantomHandParams();
            pres.arm.Build(p); pres.brush.Build(); pres.threat.Build();
            var realWrist = new Vector3(ArmX, TableTopY + 0.021f, ElbowZ + ForearmLen);
            pres.arm.PlaceFromCalibration(realWrist, Vector3.forward, (float)p.OffsetCm);
            pres.arm.Visible = true;
            foreach (var smr in pres.arm.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
            Vector3 eye = SeatedEye, look = pres.arm.AxisWorldPos(0f) + pres.arm.transform.forward * 0.05f, palmTop = pres.arm.PalmTopWorld;
            const double frameMs = 1000.0 / 30.0;

            // 1 one stroke, wrist to elbow, at the default brush speed
            pres.brush.Visible = true;
            pres.brush.SetGeometry(pres.arm.WristWorld, pres.arm.ElbowDirection, pres.arm.ForearmLengthM, pres.arm.MotorAFromWristM, pres.arm.MotorSpacingM, p.MotorSoaMs);
            var plan = new StrokeScheduler(p, 1).Plan(PhCondition.Sync, 0, 20000);
            pres.brush.SetPlan(plan);
            var s1 = plan[1];
            double a0 = s1.StartMs - 300;
            int nStroke = 0;
            for (double t = a0; t <= s1.EndMs + 300; t += frameMs, nStroke++) { pres.brush.PoseAt(t); Shot(outDir, "stroke_" + nStroke.ToString("000"), eye, look, 56f); }

            // 2 the Dissolve: two more strokes while the arm fades
            double d0 = plan[2].StartMs - 300, d1 = plan[3].EndMs + 300;
            int nDissolve = 0;
            for (double t = d0; t <= d1; t += frameMs, nDissolve++)
            {
                pres.brush.PoseAt(t);
                pres.arm.Alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.70f, (float)((t - d0) / (d1 - d0))));
                Shot(outDir, "dissolve_" + nDissolve.ToString("000"), eye, look, 56f);
            }
            pres.arm.Alpha = 1f; pres.brush.Visible = false;

            // 3 the stone: the 0.6 s telegraph, the fall, the dust
            Vector3 drop = palmTop + Vector3.up * PhantomAnchors.DropHeightM, rest = palmTop + Vector3.up * 0.052f, aim = palmTop + Vector3.up * 0.10f;
            pres.threat.Begin(drop, palmTop, 0);
            int nStone = 0;
            for (double t = 0; t < ThreatDrop.TelegraphMs; t += frameMs, nStone++) { pres.threat.Tick(t); Shot(outDir, "stone_" + nStone.ToString("000"), eye, aim, 62f); }
            Vector3 from = pres.threat.Body.transform.position;
            for (float s = (float)(frameMs / 1000.0); ; s += (float)(frameMs / 1000.0))
            {
                float y = from.y - 0.5f * 9.81f * s * s;
                if (y <= rest.y) break;
                pres.threat.Body.transform.position = new Vector3(from.x, y, from.z);
                Shot(outDir, "stone_" + nStone.ToString("000"), eye, aim, 62f); nStone++;
            }
            int impactFrame = nStone;
            pres.threat.Body.transform.position = rest;
            pres.threat.EmitDust(palmTop);
            var dust = pres.threat.GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < 40; i++, nStone++)
            {
                foreach (var ps in dust) ps.Simulate((float)(frameMs / 1000.0), true, i == 0);
                Shot(outDir, "stone_" + nStone.ToString("000"), eye, aim, 62f);
            }
            pres.threat.Cancel();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // discard transient changes

            string msg = "{\"fps\":30,\"stroke\":{\"n\":" + nStroke + ",\"t0\":" + a0.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                         ",\"start\":" + s1.StartMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ",\"passA\":" + s1.PassAMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                         ",\"passB\":" + s1.PassBMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ",\"end\":" + s1.EndMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                         "},\"dissolve\":{\"n\":" + nDissolve + "},\"stone\":{\"n\":" + nStone + ",\"impact\":" + impactFrame + "}}";
            File.WriteAllText(Path.Combine(outDir, "seq.json"), msg);
            Debug.Log("[PH-SEQ] " + msg + " -> " + outDir);
            return msg;
        }

        // ---------------------------------------------------------------------------------------------------------
        // budget

        /// <summary>What the saved scene and the model wrappers ask the GPU to draw, counted without batching: enabled renderers, draw entries (renderer x
        /// material: the upper bound of the draw calls, the SRP batcher only lowers it) and triangles. The asset guide's Quest budget is 100 draw calls and
        /// 300 k triangles. The arm, brush, stone and the UI panels are built at run time, so the wrappers that are not already in the scene are listed apart.</summary>
        [MenuItem("Tools/OPUS/PhantomHand Scene Stats")]
        public static string SceneStats()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int renderers = 0, entries = 0, lights = 0; long tris = 0;
            var inScene = new HashSet<Mesh>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var mesh = MeshOf(r);
                if (!r.enabled || mesh == null) continue;
                renderers++; entries += Mathf.Max(1, r.sharedMaterials.Length); tris += Triangles(mesh); inScene.Add(mesh);
            }
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (l.enabled) lights++;
            int wEntries = 0; long wTris = 0; var spawned = new List<string>();
            foreach (var name in PhModels.All)
            {
                var w = PhModels.Load(name);
                if (w == null) continue;
                bool counted = false;
                foreach (var r in w.GetComponentsInChildren<Renderer>(true))
                {
                    var mesh = MeshOf(r);
                    if (mesh == null || inScene.Contains(mesh)) continue;
                    wEntries += Mathf.Max(1, r.sharedMaterials.Length); wTris += Triangles(mesh); counted = true;
                }
                if (counted) spawned.Add(name);
            }
            string msg = "PhantomHand scene, no batching: renderers=" + renderers + ", draw entries=" + entries + ", triangles=" + tris + ", realtime lights=" + lights +
                         "; wrappers spawned at run time (" + string.Join("+", spawned.ToArray()) + "): draw entries=" + wEntries + ", triangles=" + wTris +
                         "; together draw entries=" + (entries + wEntries) + ", triangles=" + (tris + wTris) + " (budget 100 draw calls, 300000 triangles; UI panels not counted)";
            Debug.Log("[PH-SCENE] " + msg);
            return msg;
        }

        private static Mesh MeshOf(Renderer r)
        {
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static long Triangles(Mesh m)
        {
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
            return n;
        }

        // ---------------------------------------------------------------------------------------------------------
        // screenshots (render-to-texture, no Play mode)

        /// <summary>Renders the three U2 review shots plus an overview into logs/sessions/screens/ph/u2/ and restores the scene.</summary>
        public static string CaptureShots()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var anchors = UnityEngine.Object.FindFirstObjectByType<PhantomAnchors>();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", OutDir));
            Directory.CreateDirectory(dir);
            var written = new List<string>();
            Vector3 eye = SeatedEye; var look = new Vector3(0.05f, TableTopY, 0.40f);

            // 1. table, lit, nothing extra
            written.Add(Shot(dir, "table", eye, look, 95f));
            // 2. dark probe: ruler + fingertip dot only
            anchors.probeRuler.gameObject.SetActive(true);
            var dotRend = anchors.leftIndexDot.GetComponent<MeshRenderer>(); dotRend.enabled = true;
            anchors.leftIndexDot.position = new Vector3(-0.12f, TableTopY + 0.10f, PhantomAnchors.RulerDistanceM);
            anchors.darken.Capture(); anchors.darken.SetDark(true, immediate: true);
            written.Add(Shot(dir, "dark_probe", eye, new Vector3(0f, TableTopY, 0.38f), 95f));
            anchors.darken.SetDark(false, immediate: true);
            anchors.probeRuler.gameObject.SetActive(false); dotRend.enabled = false;
            // 3. arm-rest outline
            anchors.armRestOutline.gameObject.SetActive(true);
            written.Add(Shot(dir, "outline", eye, new Vector3(0.12f, TableTopY, 0.40f), 95f));
            anchors.armRestOutline.gameObject.SetActive(false);
            // 4. overview of the room
            written.Add(Shot(dir, "overview", new Vector3(1.5f, 1.7f, -1.3f), new Vector3(0f, 0.8f, 1.0f), 62f));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // discard transient changes
            return string.Join(";", written);
        }

        private const string U4OutDir = "logs/sessions/screens/ph/u4";

        /// <summary>Scripted result used for the witness screenshots and tests: SYNC shows a clear drift, a strong flinch and high
        /// ownership; ASYNC the opposite; q4 flat.</summary>
        public static WitnessSummary ScriptedWitness(bool withQ4)
        {
            var sync = new ConditionResult { Condition = PhCondition.Sync, PreDriftCm = 0.5, PostDriftCm = 4.1, Ownership = 2.0, Control = -1, Awareness = withQ4 ? (double?)2 : null,
                Threat = new ThreatResponse { EmgPeakX = 4.6, EmgLatencyMs = 180, WristPeakMps = 0.8, WristLatencyMs = 210 } };
            var async = new ConditionResult { Condition = PhCondition.Async, PreDriftCm = 0.2, PostDriftCm = 0.6, Ownership = -1.5, Control = -2, Awareness = withQ4 ? (double?)2 : null,
                Threat = new ThreatResponse { EmgPeakX = 1.8, EmgLatencyMs = 330, WristPeakMps = 0.3, WristLatencyMs = 380 } };
            return WitnessSummary.Build(sync, async);
        }

        /// <summary>Renders every U4 panel state (calibration, probe, questionnaire, witness, HUD; EN and HI) into logs/sessions/screens/ph/u4/.
        /// Edit mode, no poke wiring; restores the scene.</summary>
        public static string CaptureU4Shots()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var anchors = UnityEngine.Object.FindFirstObjectByType<PhantomAnchors>();
            var cam = anchors.cameraRig.GetComponentInChildren<Camera>(true);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", U4OutDir));
            Directory.CreateDirectory(dir);
            var written = new List<string>();
            Vector3 eye = SeatedEye;

            var instr = anchors.instructionPanel.GetComponent<PhInstructionPanel>();
            if (instr == null) instr = anchors.instructionPanel.gameObject.AddComponent<PhInstructionPanel>();
            var quest = anchors.questionnairePanel.GetComponent<PhQuestionnairePanel>();
            if (quest == null) quest = anchors.questionnairePanel.gameObject.AddComponent<PhQuestionnairePanel>();
            var wit = anchors.witnessPanel.GetComponent<PhWitnessPanel>();
            if (wit == null) wit = anchors.witnessPanel.gameObject.AddComponent<PhWitnessPanel>();
            var hud = anchors.hudPanel.GetComponent<PhHudPanel>();
            if (hud == null) hud = anchors.hudPanel.gameObject.AddComponent<PhHudPanel>();
            instr.Build(cam); quest.Build(cam, false); wit.Build(cam, false); hud.Build(cam);

            // 1-3 calibration (lit room, outline visible)
            anchors.armRestOutline.gameObject.SetActive(true);
            foreach (var lang in new[] { "en", "hi" })
            {
                string title = PhStrings.Get("calib_title", lang);
                instr.Show(title, PhStrings.Get("calib_lost", lang), 0f, PhUiKit.Warn, true);
                written.Add(PanelShot(dir, "calib_waiting_" + lang, eye, new Vector3(0.05f, 0.90f, 0.50f), 62f));
                instr.Show(title, PhStrings.Get("calib_holding", lang), 0.55f, PhUiKit.Info, true);
                written.Add(PanelShot(dir, "calib_holding_" + lang, eye, new Vector3(0.05f, 0.90f, 0.50f), 62f));
                instr.Show(title, PhStrings.Get("calib_done", lang), 1f, PhUiKit.Good, true);
                written.Add(PanelShot(dir, "calib_confirmed_" + lang, eye, new Vector3(0.05f, 0.90f, 0.50f), 62f));
            }
            instr.Hide(); anchors.armRestOutline.gameObject.SetActive(false);

            // 4-5 probe: dark room, ruler, dot, floating text
            anchors.probeRuler.gameObject.SetActive(true);
            var dotRend = anchors.leftIndexDot.GetComponent<MeshRenderer>(); dotRend.enabled = true;
            anchors.leftIndexDot.position = new Vector3(-0.10f, 0.86f, PhantomAnchors.RulerDistanceM);
            anchors.darken.Capture(); anchors.darken.SetDark(true, immediate: true);
            foreach (var lang in new[] { "en", "hi" })
            {
                instr.Show(PhStrings.ProbeInstruction(lang), PhStrings.Get("probe_holding", lang), 0.6f, PhUiKit.Info, false);
                written.Add(PanelShot(dir, "probe_holding_" + lang, eye, new Vector3(0f, 0.88f, 0.42f), 70f));
            }
            instr.Show(PhStrings.Get("probe_arm_moved", "en"), "", 0f, PhUiKit.Warn, false);
            anchors.armRestOutline.gameObject.SetActive(true);
            written.Add(PanelShot(dir, "probe_arm_moved_en", eye, new Vector3(0.05f, 0.86f, 0.42f), 70f));
            anchors.armRestOutline.gameObject.SetActive(false);
            anchors.darken.SetDark(false, immediate: true);
            anchors.probeRuler.gameObject.SetActive(false); dotRend.enabled = false; instr.Hide();

            // 6 questionnaire
            var q = new Questionnaire(new[] { Questionnaire.Q1, Questionnaire.Q2, Questionnaire.Q3, Questionnaire.Q4 });
            quest.Show(q, v => true, () => true, "en", 0);
            written.Add(PanelShot(dir, "questionnaire_q1_en", eye, anchors.questionnairePanel.position, 46f));
            q.Answer(2); quest.Show(q, v => true, () => true, "en", 0);
            quest.Press(1);
            written.Add(PanelShot(dir, "questionnaire_q2_selected_en", eye, anchors.questionnairePanel.position, 46f));
            var qh = new Questionnaire(new[] { Questionnaire.Q1, Questionnaire.Q2, Questionnaire.Q3, Questionnaire.Q4 });
            qh.Answer(-1); qh.Answer(3); qh.Answer(0);
            quest.Show(qh, v => true, () => true, "hi", 0);
            written.Add(PanelShot(dir, "questionnaire_q4_hi", eye, anchors.questionnairePanel.position, 46f));
            quest.Hide();

            // 7 witness (closing line before and after the fade)
            wit.Show(ScriptedWitness(true), "en", 0);
            wit.Tick(1000);
            written.Add(PanelShot(dir, "witness_en_t1s", eye, anchors.witnessPanel.position, 56f));
            wit.Tick(4500);
            written.Add(PanelShot(dir, "witness_en_t4_5s", eye, anchors.witnessPanel.position, 56f));
            wit.Show(ScriptedWitness(true), "hi", 0); wit.Tick(4500);
            written.Add(PanelShot(dir, "witness_hi", eye, anchors.witnessPanel.position, 56f));
            wit.Hide();

            // 8 hud: default, offline chips + spectator, Hindi
            var m = new HudModel { Phase = PhPhase.Induction, RemainingS = 63, HapticConnected = true, BioConnected = true, Condition = PhCondition.Async, EmgLevel01 = 0.42, StrokeCount = 90 };
            hud.Apply(m, 0f); hud.gameObject.SetActive(true);
            written.Add(PanelShot(dir, "hud_default", eye, anchors.hudPanel.position, 30f));
            m.HapticConnected = false; m.BioConnected = false; m.SpectatorVisible = true;
            hud.Apply(m, 0.4f);
            written.Add(PanelShot(dir, "hud_offline_spectator", eye, anchors.hudPanel.position, 30f));
            m.Lang = "hi"; m.SpectatorVisible = false; hud.Apply(m, 0f);
            written.Add(PanelShot(dir, "hud_offline_hi", eye, anchors.hudPanel.position, 30f));
            hud.gameObject.SetActive(false);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // discard transient changes
            return string.Join(";", written);
        }

        private static string PanelShot(string dir, string name, Vector3 eye, Vector3 lookAt, float fov)
        {
            Canvas.ForceUpdateCanvases(); Canvas.ForceUpdateCanvases();
            return Shot(dir, name, eye, lookAt, fov);
        }

        private static string Shot(string dir, string name, Vector3 pos, Vector3 lookAt, float fov)
        {
            var camGo = new GameObject("__ph_cam_" + name);
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = pos; cam.transform.LookAt(lookAt);
                cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.Lerp(Color.black, new Color(0.10f, 0.10f, 0.11f), UnityEngine.Object.FindFirstObjectByType<DarkenController>().Level01);
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var prev = RenderTexture.active;
                cam.Render(); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                string path = Path.Combine(dir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                RenderTexture.active = prev; cam.targetTexture = null; rt.Release();
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
                return path;
            }
            finally { UnityEngine.Object.DestroyImmediate(camGo); }
        }

        // ---------------------------------------------------------------------------------------------------------

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var g in scene.GetRootGameObjects()) if (g.name == name) return g;
            return null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static void EnsureDir(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }
    }
}
