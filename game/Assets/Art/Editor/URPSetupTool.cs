using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Opus.Art.Editor
{
    /// <summary>
    /// R1 (Track U next-run brief): "URP asset for Quest (MSAA 4x, no HDR/post)". Discovered missing while
    /// visually auditing R3's converted materials — without an assigned URP Render Pipeline Asset in Graphics
    /// Settings, the project falls back to the Built-in pipeline, which renders any Shader Graph-based
    /// "Universal Render Pipeline/Lit" material as solid magenta (Unity's standard shader-error color). This was
    /// mistaken for a material-conversion bug at first; the materials are fine (see ImportMetaAssetsTool.cs) —
    /// this missing asset is the actual root cause.
    /// </summary>
    public static class URPSetupTool
    {
        private const string SettingsDir = "Assets/Settings";
        private const string RendererPath = SettingsDir + "/OpusURPRenderer.asset";
        private const string PipelineAssetPath = SettingsDir + "/OpusURPAsset.asset";

        [MenuItem("Tools/OPUS/Create And Assign Quest URP Asset")]
        public static string CreateAndAssignQuestUrpAsset()
        {
            Directory.CreateDirectory(SettingsDir);
            AssetDatabase.Refresh();

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            bool created = pipelineAsset == null;
            if (created)
            {
                pipelineAsset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
            }

            // Renderer list is not publicly settable in every URP version; go through SerializedObject so this
            // compiles regardless of exactly which URP 17.x point release is installed.
            var so = new SerializedObject(pipelineAsset);
            var rendererList = so.FindProperty("m_RendererDataList");
            rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Quest baseline per docs/UNITY_PRACTICES.md §2: MSAA 4x, no HDR, no post-processing extras.
            pipelineAsset.msaaSampleCount = 4;
            pipelineAsset.supportsHDR = false;

            EditorUtility.SetDirty(pipelineAsset);
            EditorUtility.SetDirty(rendererData);

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;

            AssetDatabase.SaveAssets();
            return created
                ? $"created {PipelineAssetPath} (+ {RendererPath}), MSAA 4x, HDR off, assigned as default pipeline"
                : $"reassigned existing {PipelineAssetPath} as default pipeline (MSAA 4x, HDR off)";
        }
    }
}
