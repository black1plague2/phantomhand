using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Opus.Art.Editor
{
    /// <summary>
    /// R2 (Track U next-run brief): creates/opens the empty scene the Building Blocks rig gets applied into,
    /// per the brief's exact path <c>Assets/Scenes/OrchardReach.unity</c>. Kept separate from
    /// ImportMetaAssetsTool.cs since scene lifecycle (create/open/save) is a distinct concern from asset import.
    /// </summary>
    public static class SceneSetupTool
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";

        [MenuItem("Tools/OPUS/Create Or Open Orchard Reach Scene")]
        public static string CreateOrOpenOrchardReachScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();

            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return $"opened existing scene at {ScenePath}";
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            return $"created new empty scene at {ScenePath}";
        }

        /// <summary>
        /// R5 (visual audit) prep: places the R3 wrapper prefabs into the currently open scene at plausible
        /// real-world positions so a screenshot can confirm materials/scale/pivots before the full R4 shell/flow
        /// exists. Idempotent: destroys and rebuilds its own "OrchardArt" root each call. Table root uses
        /// TopCenter pivot (its Y *is* the tabletop height, 0.6m), so the basket (BottomCenter pivot) is placed at
        /// the same Y to rest exactly on that surface; the apple (Center pivot) is placed half its own height
        /// above the table surface.
        /// </summary>
        [MenuItem("Tools/OPUS/Place Orchard Art For Audit")]
        public static string PlaceOrchardArtForAudit()
        {
            var existing = GameObject.Find("OrchardArt");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("OrchardArt");

            const float tableTopY = 0.6f;
            const float appleRadiusM = 0.035f; // 0.07m diameter / 2

            Place("Assets/Art/Prefabs/orchard_table/orchard_table.prefab", new Vector3(0, tableTopY, 1.5f), root.transform);
            Place("Assets/Art/Prefabs/basket/basket.prefab", new Vector3(0.15f, tableTopY, 1.5f), root.transform);
            Place("Assets/Art/Prefabs/fruit_apple/fruit_apple.prefab", new Vector3(-0.15f, tableTopY + appleRadiusM, 1.5f), root.transform);
            Place("Assets/Art/Prefabs/orchard_tree/orchard_tree.prefab", new Vector3(1.2f, 0, 3.0f), root.transform);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return "placed orchard_table, basket, fruit_apple, orchard_tree under 'OrchardArt' for visual audit";
        }

        /// <summary>R5 prep: adds a single warm directional light (no lighting existed in the empty scene) and
        /// frames the Scene view camera on the placed art so a screenshot actually shows something. Idempotent.</summary>
        [MenuItem("Tools/OPUS/Add Audit Lighting And Frame View")]
        public static string AddAuditLightingAndFrameView()
        {
            var existing = GameObject.Find("AuditSun");
            if (existing != null) Object.DestroyImmediate(existing);

            var sunGo = new GameObject("AuditSun");
            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.intensity = 1.2f;
            sunGo.transform.rotation = Quaternion.Euler(50, -30, 0);

            var art = GameObject.Find("OrchardArt");
            if (art != null && SceneView.lastActiveSceneView != null)
            {
                var bounds = new Bounds(art.transform.position, Vector3.one * 0.1f);
                foreach (var r in art.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                SceneView.lastActiveSceneView.Frame(bounds, false);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return "added AuditSun directional light and framed Scene view on OrchardArt";
        }

        private static void Place(string prefabPath, Vector3 position, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[SceneSetupTool] prefab not found at {prefabPath}");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
        }
    }
}
