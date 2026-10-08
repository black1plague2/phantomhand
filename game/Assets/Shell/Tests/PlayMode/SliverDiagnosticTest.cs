using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Opus.Shell.Tests.PlayMode
{
    /// <summary>
    /// Run13 one-shot diagnostic (Opus review of run12/01_mid_reach.png's stretched dark-red sliver between
    /// the two orchard trees, with its own ground shadow -- real geometry, not a post-effect). A static
    /// edit-mode LOD-forcing pass (Assets/Shell/Editor/LodSliverDiagnosticTool.cs) ruled out the two visible
    /// trees' own LODGroups -- none of their forced LOD0/1/2 renders reproduced the sliver. That tool could not
    /// reach `DemoHandProxy_R_GhostHand` (the ghost_hand_static asset OrchardReachSceneController poses every
    /// frame during a reach -- see DriveGhostHandVisual) because that object's whole ancestor chain only
    /// becomes active once the building-block rig initializes in Play mode, so it was invisible to
    /// FindObjectsByType in a static edit-mode capture. This test runs in actual PlayMode, during
    /// TrialState.MovementOnset (the exact state 01_mid_reach.png captures), and forces the ghost hand's own
    /// LODGroup through each level to see whether the sliver tracks it.
    /// Delete this file (and the Editor-mode tool) once the investigation is written up in the session log.
    /// </summary>
    public class SliverDiagnosticTest
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";

        [UnityTest]
        public IEnumerator CaptureDuringMovementOnsetAcrossForcedLods()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            var go = GameObject.Find("OrchardReachSceneController");
            Assert.IsNotNull(go, "OrchardReachSceneController not found — dressing tool must run first.");
            var controller = go.GetComponent<OrchardReachSceneController>();

            float elapsed = 0f;
            while (elapsed < 10f && controller.CurrentTrialState != Opus.Games.OrchardReach.TrialState.MovementOnset)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            Assert.AreEqual(Opus.Games.OrchardReach.TrialState.MovementOnset, controller.CurrentTrialState,
                $"never reached MovementOnset within 10s (state={controller.CurrentTrialState}) — cannot reproduce the shot under test.");

            string outDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")), "logs", "sessions", "screens", "unity", "run13_lod_diag");
            Directory.CreateDirectory(outDir);

            // Now that we're in Play mode with the rig initialized, every LODGroup — including the ghost
            // hand's, invisible to the earlier edit-mode pass — should be reachable.
            var groups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None);
            var names = new List<string>();
            LODGroup ghostGroup = null;
            foreach (var g in groups)
            {
                names.Add($"{g.gameObject.name}(root={g.transform.root.name},active={g.gameObject.activeInHierarchy})");
                if (g.transform.root.name.Contains("GhostHand") || g.gameObject.name.Contains("GhostHand") ||
                    g.transform.parent != null && g.transform.parent.name.Contains("GhostHand"))
                    ghostGroup = g;
                // Also check ancestors up to root for "GhostHand" in case naming differs.
                var t = g.transform;
                while (t != null) { if (t.name.Contains("GhostHand")) { ghostGroup = g; break; } t = t.parent; }
            }
            Debug.Log($"[OPUS] SliverDiag: {groups.Length} active LODGroup(s) during MovementOnset: {string.Join(" | ", names)}");
            Debug.Log($"[OPUS] SliverDiag: ghost hand LODGroup found = {(ghostGroup != null)}" +
                      (ghostGroup != null ? $" name={ghostGroup.gameObject.name} enabled renderers={CountEnabledRenderers(ghostGroup)}" : ""));

            // Baseline capture (auto LOD, whatever this frame would normally show).
            CaptureShot(outDir, "playmode_mid_reach_auto");

            if (ghostGroup != null)
            {
                ghostGroup.ForceLOD(0);
                yield return null;
                CaptureShot(outDir, "playmode_mid_reach_ghost_lod0");

                ghostGroup.ForceLOD(2);
                yield return null;
                CaptureShot(outDir, "playmode_mid_reach_ghost_lod2");

                ghostGroup.ForceLOD(-1);
            }
            else
            {
                Debug.LogWarning("[OPUS] SliverDiag: could not locate the ghost hand's LODGroup by name search — see the full group list above.");
            }
        }

        private static int CountEnabledRenderers(LODGroup g)
        {
            int n = 0;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) if (r.enabled) n++;
            return n;
        }

        private static void CaptureShot(string outDir, string name)
        {
            var centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye == null) return;

            var camGo = new GameObject($"__SliverDiagCamera_{name}");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(centerEye.transform.position, centerEye.transform.rotation);
                var existing = centerEye.GetComponent<Camera>();
                cam.fieldOfView = existing != null ? existing.fieldOfView : 90f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.Skybox;

                const int w = 1920, h = 1080;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var prev = RenderTexture.active;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;

                var path = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"[OPUS] SliverDiag: wrote {path}");
                Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
            finally { Object.Destroy(camGo); }
        }
    }
}
