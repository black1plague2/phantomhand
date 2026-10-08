using System.Collections;
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
    /// Run10 task 5: a single screenshot captured DURING the demo's scripted trunk-lean window (see
    /// OrchardReachSceneController's demoTriggerFormEvents/DemoLeanWindowMs), from the live eye camera
    /// (CenterEyeAnchor), for a human/Opus visual check that a lean warning is genuinely active at capture time.
    /// Mirrors OrchardReachPlayModeAuditTests.CaptureShot's camera setup (same anchor, same FOV) since that
    /// shot is the project's established "correct seated-eye" reference. Run11: the vignette + audio the run10
    /// version of this comment reported as missing now EXIST (Assets/Shell/Runtime/TrunkLeanVignette.cs), so this
    /// capture is the visual proof of them. It asserts, via FormFeedback.LeanWarningActive, that the lean is
    /// genuinely active at the moment of capture, and additionally that the vignette component is attached and
    /// actually rendering (alpha > 0) -- otherwise the screenshot would silently go back to proving nothing.
    /// </summary>
    public class LeanScreenshotTest
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";

        [UnityTest]
        public IEnumerator CaptureDuringTrunkLean()
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
            while (elapsed < 5f && !(controller.FormFeedback != null && controller.FormFeedback.LeanWarningActive))
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            Assert.IsTrue(controller.FormFeedback != null && controller.FormFeedback.LeanWarningActive,
                $"trunk-lean warning never became active within 5s (elapsed={elapsed:F2}s) — cannot capture a genuine during-lean screenshot.");

            string outDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")), "logs", "sessions", "screens", "unity", "run11");
            Directory.CreateDirectory(outDir);
            // Run11: prove the vignette is not just attached but actually drawing at capture time. Without this
            // the screenshot can look identical whether the feature works or was silently never wired.
            var vignette = Object.FindFirstObjectByType<TrunkLeanVignette>();
            Assert.IsNotNull(vignette, "TrunkLeanVignette was never attached — the primary lean feedback is missing.");

            // Let the ~150 ms fade-in actually reach a visible opacity before shooting. The first run11 capture
            // fired on the lean's first active frame, caught alpha=0.002, and produced a PNG indistinguishable
            // from "no vignette exists" — while still passing a naive IsRendering check.
            float fade = 0f;
            while (fade < 1.5f && vignette.Alpha < 0.30f)
            {
                yield return null;
                fade += Time.deltaTime;
            }
            Assert.GreaterOrEqual(vignette.Alpha, 0.30f,
                $"vignette never faded in to a visible opacity (alpha={vignette.Alpha:F3} after {fade:F2}s) — the patient would see nothing.");

            // Run11 diagnostic: the first capture asserted IsRendering==true yet showed no vignette in the PNG,
            // so log exactly where the quad is relative to the capture camera and what material it carries.
            {
                var vq = GameObject.Find("Vignette");
                var eye = GameObject.Find("CenterEyeAnchor");
                if (vq != null && eye != null)
                {
                    var mr = vq.GetComponent<MeshRenderer>();
                    var mat = mr != null ? mr.sharedMaterial : null;
                    Vector3 toQuad = vq.transform.position - eye.transform.position;
                    Debug.Log($"[OPUS] VignetteDiag: quadWorld={vq.transform.position} eyeWorld={eye.transform.position} " +
                              $"distance={toQuad.magnitude:F3} dotForward={Vector3.Dot(toQuad.normalized, eye.transform.forward):F3} " +
                              $"lossyScale={vq.transform.lossyScale} rendererEnabled={(mr != null && mr.enabled)} " +
                              $"isVisible={(mr != null && mr.isVisible)} shader={(mat != null ? mat.shader.name : "<null mat>")} " +
                              $"color={(mat != null ? mat.color.ToString() : "-")} " +
                              $"baseColor={(mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "<no _BaseColor>")} " +
                              $"renderQueue={(mat != null ? mat.renderQueue : -1)} layer={vq.layer}");
                }
                else Debug.Log($"[OPUS] VignetteDiag: quad={(vq != null)} eye={(eye != null)}");
            }

            CaptureShot(outDir, "during_trunk_lean");
            Debug.Log($"[OPUS] LeanScreenshotTest: captured during_trunk_lean at leanCm={controller.FormFeedback.LastLeanCm:F1} (active={controller.FormFeedback.LeanWarningActive})");
        }

        private static void CaptureShot(string outDir, string name)
        {
            var centerEye = GameObject.Find("CenterEyeAnchor");
            Assert.IsNotNull(centerEye, "CenterEyeAnchor not found in the running scene.");

            var camGo = new GameObject($"__LeanScreenshotCamera_{name}");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(centerEye.transform.position, centerEye.transform.rotation);

                var existingCam = centerEye.GetComponent<Camera>();
                cam.fieldOfView = existingCam != null ? existingCam.fieldOfView : 90f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.Skybox;

                const int w = 1920, h = 1080;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var prevActive = RenderTexture.active;

                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                var bytes = tex.EncodeToPNG();
                string path = Path.Combine(outDir, $"{name}.png");
                File.WriteAllBytes(path, bytes);
                Debug.Log($"[OPUS] LeanScreenshotTest: wrote {path} ({bytes.Length} bytes)");

                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(rt);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
            }
        }
    }
}
