using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// Run13 diagnostic (Opus review of run12/01_mid_reach.png: a stretched dark-red sliver floating between
    /// the two orchard trees, with its own ground shadow, so it is real rendered geometry, not a post-effect).
    /// One-shot investigation tool, not shipped game logic: renders the same seated-eye view once per forced
    /// LOD level (auto, 0, 1, 2) on both orchard_tree instances so the sliver can be attributed to a specific
    /// LOD's mesh (or ruled out) by comparing the four PNGs. Delete after the investigation is written up.
    /// Batch entry: `-executeMethod Opus.Shell.Editor.LodSliverDiagnosticTool.Run`.
    /// </summary>
    public static class LodSliverDiagnosticTool
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";
        private const int Width = 1920;
        private const int Height = 1080;

        public static void Run()
        {
            Debug.Log("[OPUS] LodSliverDiagnosticTool: starting");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log($"[OPUS] LodSliverDiagnosticTool: opened scene {scene.path}, valid={scene.IsValid()}");

            // Same fix as OpusScreenshotTool.BatchCaptureStaticShots: with no real headset/controllers connected
            // in batch mode, the rig's default hand/controller hint models render directly in front of the
            // camera and dominate the frame — not what we're investigating here. Hide them for these shots too.
            var rigGo = GameObject.Find("[BuildingBlock] Camera Rig");
            var hiddenRenderers = new System.Collections.Generic.List<Renderer>();
            if (rigGo != null)
            {
                foreach (var r in rigGo.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.gameObject.name.Contains("DemoHandProxy")) continue;
                    if (r.enabled) { hiddenRenderers.Add(r); r.enabled = false; }
                }
                Debug.Log($"[OPUS] LodSliverDiagnosticTool: hid {hiddenRenderers.Count} rig hand/controller-hint renderers");
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string outDir = Path.Combine(projectRoot, "logs", "sessions", "screens", "unity", "run13_lod_diag");
            Directory.CreateDirectory(outDir);

            var groups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None);
            Debug.Log($"[OPUS] LodSliverDiagnosticTool: found {groups.Length} LODGroup(s) in the dressed scene");
            foreach (var g in groups)
            {
                var lods = g.GetLODs();
                string dist = "";
                for (int i = 0; i < lods.Length; i++) dist += $" LOD{i}:screenRelHeight={lods[i].screenRelativeTransitionHeight}";
                Debug.Log($"[OPUS] LodSliverDiagnosticTool: {g.gameObject.name} (root {g.transform.root.name}) size={g.size} refPoint={g.localReferencePoint}{dist}");
            }

            var eyePos = new Vector3(0f, 1.15f, -0.05f);
            var eyeForward = Quaternion.Euler(30f, 0f, 0f) * Vector3.forward;

            // auto first (whatever Unity would actually pick), then force each level in turn.
            CaptureForced(groups, -1, outDir, "lod_auto", eyePos, eyeForward);
            CaptureForced(groups, 0, outDir, "lod0_forced", eyePos, eyeForward);
            CaptureForced(groups, 1, outDir, "lod1_forced", eyePos, eyeForward);
            CaptureForced(groups, 2, outDir, "lod2_forced", eyePos, eyeForward);

            foreach (var g in groups) g.ForceLOD(-1);
            foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
            Debug.Log("[OPUS] LodSliverDiagnosticTool: DONE");
        }

        private static void CaptureForced(LODGroup[] groups, int force, string outDir, string name, Vector3 pos, Vector3 fwd)
        {
            foreach (var g in groups) g.ForceLOD(force);
            CaptureShot(outDir, name, pos, pos + fwd, 90f);
        }

        private static void CaptureShot(string outDir, string name, Vector3 position, Vector3 lookAt, float fov)
        {
            var camGo = new GameObject($"__LodDiagCamera_{name}");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = position;
                cam.transform.LookAt(lookAt);
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.Skybox;

                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var prevActive = RenderTexture.active;

                cam.Render(); // warmup, matches OpusScreenshotTool's shader-binding fix
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                string path = Path.Combine(outDir, $"{name}.png");
                File.WriteAllBytes(path, png);
                Debug.Log($"[OPUS] LodSliverDiagnosticTool: wrote {path} ({png.Length} bytes)");

                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
            }
        }
    }
}
