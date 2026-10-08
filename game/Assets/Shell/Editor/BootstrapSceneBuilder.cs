using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// PH U5: the Bootstrap scene (build index 0, one camera + <see cref="BootstrapLoader"/>) and the Phantom Hand settings asset.
    ///   Tools/OPUS/Build Bootstrap Scene          -> Assets/Scenes/Bootstrap.unity, and Build Settings = Bootstrap, PhantomHand, OrchardReach
    ///   Tools/OPUS/Create Phantom Hand Settings   -> Assets/Shell/Resources/PhantomHandSettings.asset (also created automatically once
    ///                                                when the editor loads and the asset is missing)
    /// Both are reproducible from code (no hand-edited assets). Batch: -executeMethod Opus.Shell.Editor.BootstrapSceneBuilder.BuildBootstrap
    /// </summary>
    public static class BootstrapSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        public const string SettingsPath = "Assets/Shell/Resources/PhantomHandSettings.asset";
        public const string ManifestPath = "Assets/Games/PhantomHand/manifest.json";
        private static readonly string[] GameScenes = { "Assets/Scenes/PhantomHand.unity", "Assets/Scenes/OrchardReach.unity" };

        [InitializeOnLoadMethod]
        private static void EnsureSettingsOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (AssetDatabase.LoadAssetAtPath<PhantomHandSettings>(SettingsPath) == null) CreateSettings();
            };
        }

        [MenuItem("Tools/OPUS/Create Phantom Hand Settings")]
        public static string CreateSettingsMenu() { return CreateSettings(); }

        public static string CreateSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<PhantomHandSettings>(SettingsPath);
            bool created = s == null;
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                s = ScriptableObject.CreateInstance<PhantomHandSettings>();
                AssetDatabase.CreateAsset(s, SettingsPath);
            }
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifest != null && s.manifest != manifest) { s.manifest = manifest; EditorUtility.SetDirty(s); }
            AssetDatabase.SaveAssets();
            return (created ? "created " : "kept ") + SettingsPath + " manifest=" + (s.manifest != null);
        }

        [MenuItem("Tools/OPUS/Build Bootstrap Scene")]
        public static string BuildBootstrap()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Leave Play mode first.");
            CreateSettings();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.03f, 0.03f);
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 10f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 1.2f, 0f);

            var go = new GameObject("Bootstrap");
            go.AddComponent<BootstrapLoader>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            string order = SetBuildSettings();
            return "built " + ScenePath + "; build settings: " + order;
        }

        /// <summary>Bootstrap first, then the game scenes (existing entries of other scenes are kept after them).</summary>
        public static string SetBuildSettings()
        {
            var want = new List<string> { ScenePath };
            want.AddRange(GameScenes.Where(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null));
            var rest = EditorBuildSettings.scenes.Where(s => !want.Contains(s.path)).ToList();
            var all = want.Select(p => new EditorBuildSettingsScene(p, true)).Concat(rest).ToArray();
            EditorBuildSettings.scenes = all;
            return string.Join(", ", all.Select(s => Path.GetFileNameWithoutExtension(s.path)));
        }
    }
}
