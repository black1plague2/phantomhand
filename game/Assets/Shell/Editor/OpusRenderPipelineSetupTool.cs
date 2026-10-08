using UnityEditor;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// U-next-run R1 gap found during R5's visual audit (2026-09-17, run6): the project had URP (17.0.4) and
    /// URP/Lit materials, but `ProjectSettings/GraphicsSettings.asset` had `m_CustomRenderPipeline: {fileID: 0}`
    /// — NO Render Pipeline Asset assigned anywhere, so the Built-in Render Pipeline was still active. Built-in RP
    /// cannot render URP-only shaders, which is exactly why every dressed object rendered magenta/pink in the R5
    /// screenshots even though `OpusMaterialDiagTool` confirmed each material's shader was correctly
    /// "Universal Render Pipeline/Lit". This was never caught before because nothing in R1-R4 rendered the scene.
    /// Creates `Assets/Settings/OpusQuestURP.asset` (MSAA 4x, no HDR, no post-processing asset attached — post is
    /// opt-in per-camera volume, not a pipeline default; the brief's "no post" is honored by not enabling any
    /// default Volume/Bloom setup) and assigns it as both the default RP asset (Graphics settings) and the
    /// Quality Settings' per-level RP asset (Quality settings) — Unity checks both; a Quality-level override with
    /// no RP asset set also falls back to Built-in even if Graphics has one assigned.
    /// </summary>
    public static class OpusRenderPipelineSetupTool
    {
        private const string AssetPath = "Assets/Settings/OpusQuestURP.asset";

        public static void BatchSetupUrp()
        {
            Debug.Log("[OPUS] OpusRenderPipelineSetupTool: starting");
            try
            {
                System.IO.Directory.CreateDirectory("Assets/Settings");

                var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
                if (urpAsset == null)
                {
                    // UniversalRenderPipelineAsset.CreateRendererAsset(path, type) is `internal` (used by Unity's
                    // own "Assets/Create/Rendering/URP Asset" menu action), so the renderer-data sub-asset must be
                    // saved as its own persisted asset file (NOT just held in memory) or the reference goes stale
                    // on the next domain reload. Reflect it in rather than duplicating its private logic.
                    var createRendererAsset = typeof(UniversalRenderPipelineAsset).GetMethod(
                        "CreateRendererAsset",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var rendererType = System.Type.GetType(
                        "UnityEngine.Rendering.Universal.RendererType, Unity.RenderPipelines.Universal.Runtime");
                    object universalRendererEnumValue = System.Enum.Parse(rendererType, "UniversalRenderer");
                    var rendererData = (ScriptableRendererData)createRendererAsset.Invoke(
                        null, new object[] { AssetPath, universalRendererEnumValue, true, "Renderer" });

                    urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
                    AssetDatabase.CreateAsset(urpAsset, AssetPath);
                    Debug.Log($"[OPUS] OpusRenderPipelineSetupTool: created {AssetPath} + renderer data sub-asset");
                }
                else
                {
                    Debug.Log($"[OPUS] OpusRenderPipelineSetupTool: found existing {AssetPath}");
                }

                // Quest baseline per docs/UNITY_PRACTICES.md §2: MSAA 4x, no HDR, no post stack default.
                urpAsset.msaaSampleCount = 4;
                urpAsset.supportsHDR = false;
                EditorUtility.SetDirty(urpAsset);

                GraphicsSettings.defaultRenderPipeline = urpAsset;
                QualitySettings.renderPipeline = urpAsset;
                Debug.Log($"[OPUS] OpusRenderPipelineSetupTool: assigned as GraphicsSettings.defaultRenderPipeline and QualitySettings.renderPipeline (level={QualitySettings.GetQualityLevel()})");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                bool ok = GraphicsSettings.currentRenderPipeline != null;
                Debug.Log($"[OPUS] OpusRenderPipelineSetupTool: verify GraphicsSettings.currentRenderPipeline != null -> {ok}");
                Debug.Log("[OPUS] OpusRenderPipelineSetupTool: DONE");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OPUS] OpusRenderPipelineSetupTool: FAILED: {e}");
                throw;
            }
        }
    }
}
