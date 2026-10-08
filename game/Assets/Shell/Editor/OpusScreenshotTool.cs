using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// U-next-run R5 (headless visual audit, editor-mode static shots): opens Assets/Scenes/OrchardReach.unity
    /// (already dressed by OrchardSceneDressingTool) and renders 1920x1080 PNGs from two camera setups via
    /// RenderTexture, without needing Play mode or a headset:
    ///   - "seated_eye": camera at the seated player's eye height (~1.15 m), positioned at the chair, looking at
    ///     the table — approximates what the user will actually see.
    ///   - "wide_establishing": a pulled-back 3/4 view of the whole dressed corner (table/basket/trees/lighting)
    ///     for judging overall composition, scale, and draw-call budget context.
    /// Batch entry: `-executeMethod Opus.Shell.Editor.OpusScreenshotTool.BatchCaptureStaticShots`.
    /// </summary>
    public static class OpusScreenshotTool
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";
        private const int Width = 1920;
        private const int Height = 1080;

        public static void BatchCaptureStaticShots()
        {
            Debug.Log("[OPUS] OpusScreenshotTool: starting");
            try
            {
                ResetRenderingDebugger();

                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log($"[OPUS] OpusScreenshotTool: opened scene {scene.path}, valid={scene.IsValid()}");

                string outDir = ResolveOutDir();
                Directory.CreateDirectory(outDir);
                Debug.Log($"[OPUS] OpusScreenshotTool: output dir = {outDir}");

                // Fix (2026-09-17, run7): moving the rig closer to the table (per the seated-layout fix) put the
                // rig's default hand/controller hint models — rendered because no real headset/controllers are
                // connected in batch mode, confirmed by name: they live under "[BuildingBlock] Camera Rig" —
                // directly overlapping the basket in-frame. These are a no-tracking-connected rendering
                // artifact, not part of the dressed scene (table/basket/tree/fruit are separate top-level
                // objects placed by OrchardSceneDressingTool, not children of the rig), so hide every renderer
                // under the rig for these static composition shots only, then restore afterward.
                var rigGo = GameObject.Find("[BuildingBlock] Camera Rig");
                var hiddenRenderers = new System.Collections.Generic.List<Renderer>();
                if (rigGo != null)
                {
                    foreach (var r in rigGo.GetComponentsInChildren<Renderer>(true))
                    {
                        // Run8: don't hide DemoHandProxy_R (now parented under the rig for SeatedLayoutTests'
                        // "under the rig" check) — it's the demo hand visual, not a no-tracking hint mesh. It's
                        // r.enabled==false by default anyway in edit mode (only Awake()/Update() turn it on), so
                        // this exclusion is a no-op for the static tool today but keeps the two capture tools
                        // (this one and OrchardReachPlayModeAuditTests) consistent.
                        if (r.gameObject.name.Contains("DemoHandProxy")) continue;
                        if (r.enabled) { hiddenRenderers.Add(r); r.enabled = false; }
                    }
                    Debug.Log($"[OPUS] OpusScreenshotTool: hid {hiddenRenderers.Count} rig hand/controller-hint renderers for static shots");
                }

                // Run8, per Opus's explicit rejection of run7's "seated_eye" shot ("still ~1.5m back and
                // elevated: the whole table including legs is visible" — that was run7's deliberate
                // pulled-back "over the shoulder" compromise, which Opus correctly identified as not actually
                // representing the seated patient's view). Fixed at the root: this shot now uses the SAME
                // world position and orientation as the real calibrated rig/CenterEyeAnchor (world (0,1.15,-0.05),
                // pitched 30 deg down per OrchardSceneDressingTool's CenterEyeAnchor fix) — literally the
                // patient's own POV, not a review vantage. `lookAt` is computed as a point 1m along that exact
                // forward ray so CaptureShot's LookAt-based API reproduces the identical rotation.
                var eyePos = new Vector3(0f, 1.15f, -0.05f);
                var eyeForward = Quaternion.Euler(30f, 0f, 0f) * Vector3.forward;
                CaptureShot(outDir, "seated_eye",
                    position: eyePos,
                    lookAt: eyePos + eyeForward,
                    fov: 90f); // Quest-like FOV; this is the real headset POV, not a flattering review crop

                // Pulled-back 3/4 establishing view of the whole dressed corner.
                CaptureShot(outDir, "wide_establishing",
                    position: new Vector3(2.0f, 1.8f, -1.4f),
                    lookAt: new Vector3(0f, 0.6f, 0.8f),
                    fov: 60f);

                LogSceneStats();

                foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;

                Debug.Log("[OPUS] OpusScreenshotTool: DONE");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OPUS] OpusScreenshotTool: FAILED: {e}");
                throw;
            }
        }

        /// <summary>
        /// Fix (2026-09-17, run7, per Opus review of the PlayMode screenshots): 01_target_shown.png etc. came out
        /// in a garish red/black/green camo pattern on every object, not the real textures. That exact
        /// red(too dark)/black(too bright)/green(in range) color coding is URP's Rendering Debugger "Material
        /// Validation Mode = Albedo" overlay (`DebugDisplaySettingsMaterial.materialValidationMode`) — some
        /// earlier batch run (or the Editor's persisted Rendering Debugger window state, which lives outside the
        /// project's own asset files and survives across `-batchmode` invocations) left it switched on. It only
        /// showed up in the PlayMode captures, not `BatchCaptureStaticShots`' own shots, which is consistent with
        /// this being a global renderer-debug toggle, not a per-scene/per-material issue. Force it back to `None`
        /// before rendering.
        /// </summary>
        private static void ResetRenderingDebugger()
        {
            // Done via reflection, not a direct type reference: `DebugMaterialValidationMode` triggered a
            // CS0012 "defined in an assembly that is not referenced" pointing at a nonexistent
            // "Unity.RenderPipeline.Universal.ShaderLibrary" (singular "Pipeline" — not a real asmdef name in
            // this project; every reference tried, including the real "...RenderPipelines..." ShaderLibrary
            // asmdef, left the same error), so referencing the enum by name isn't resolving cleanly in this
            // project's assembly graph. Reflection sidesteps the whole problem — this is a "set an int-backed
            // enum field to its zero/None value" op, not something that benefits from compile-time type safety.
            // Wrapped in try/catch: `DebugDisplaySettings<T>.Instance`'s lazy-init threw a NullReferenceException
            // the first time this ran this early in a fresh batch process (before whatever it depends on -
            // likely DebugManager/the render pipeline - has finished initializing); this whole reset is a
            // nice-to-have for screenshot readability, not something that should abort the capture if it can't
            // reach the debug settings object in a particular batch run.
            try
            {
                var settingsType = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineDebugDisplaySettings, Unity.RenderPipelines.Universal.Runtime");
                var instanceProp = settingsType?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var settings = instanceProp?.GetValue(null);
                var materialSettingsProp = settingsType?.GetProperty("materialSettings");
                var materialSettings = materialSettingsProp?.GetValue(settings);
                if (materialSettings == null)
                {
                    Debug.LogWarning("[OPUS] ResetRenderingDebugger: could not reach UniversalRenderPipelineDebugDisplaySettings.Instance.materialSettings via reflection — skipping.");
                    return;
                }
                var validationModeProp = materialSettings.GetType().GetProperty("materialValidationMode");
                var before = validationModeProp.GetValue(materialSettings);
                validationModeProp.SetValue(materialSettings, System.Enum.ToObject(validationModeProp.PropertyType, 0)); // 0 == None
                Debug.Log($"[OPUS] ResetRenderingDebugger: materialValidationMode {before} -> None");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[OPUS] ResetRenderingDebugger: failed ({e.GetType().Name}: {e.Message}) — continuing without resetting it; if screenshots still show the red/black/green camo pattern, this is why.");
            }
        }

        private static string ResolveOutDir()
        {
            // Application.dataPath = .../OPUS/game/Assets
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            // Run9: output dir bumped to match this run's naming (per the brief's "seated_eye/wide to run9/") —
            // the capture logic itself is UNCHANGED (Opus reviewed run8's seated_eye.png as correct).
            return Path.Combine(projectRoot, "logs", "sessions", "screens", "unity", "run9");
        }

        private static void CaptureShot(string outDir, string name, Vector3 position, Vector3 lookAt, float fov)
        {
            var camGo = new GameObject($"__AuditCamera_{name}");
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

                // Fix (2026-09-17, run7, per Opus review): seated_eye.png came out with untextured/white
                // table+basket while the PlayMode shots (captured a couple of frames into a running scene) were
                // correctly textured. Root cause: this tool opens the scene and renders in the SAME call, before
                // URP has actually compiled/bound the real shader variants for freshly-loaded materials (a known
                // Unity quirk — the very first Camera.Render() after a scene load can rasterize before texture
                // bindings are fully resolved). A throwaway warmup render (discarded) forces that binding to
                // happen before the real, saved capture.
                cam.Render();

                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                string path = Path.Combine(outDir, $"{name}.png");
                File.WriteAllBytes(path, png);
                Debug.Log($"[OPUS] OpusScreenshotTool: wrote {path} ({png.Length} bytes)");

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

        private static void LogSceneStats()
        {
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int pinkCount = 0;
            foreach (var r in renderers)
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    // Built-in Standard shader (URP can't render it -> renders magenta/pink).
                    if (mat.shader != null && mat.shader.name.Contains("Standard") && !mat.shader.name.Contains("URP"))
                    {
                        pinkCount++;
                    }
                }
            }
            Debug.Log($"[OPUS] OpusScreenshotTool: scene has {renderers.Length} renderers, {pinkCount} using non-URP Standard-shader materials (likely pink)");
        }
    }
}
