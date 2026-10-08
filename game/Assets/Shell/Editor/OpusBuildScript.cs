using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// U-next-run R7: batch Android APK build. `-executeMethod Opus.Shell.Editor.OpusBuildScript.BuildAndroidApk`.
    /// Builds every enabled scene in Build Settings (falls back to just Assets/Scenes/OrchardReach.unity if none
    /// are set there yet — confirmed R2-R6 never touched Build Settings) to
    /// `releases/game/0.2.0/opus-game.apk`, ARM64 + IL2CPP per docs/UNITY_PRACTICES.md §2.
    /// </summary>
    public static class OpusBuildScript
    {
        private const string OutputRelativePath = "../releases/game/0.2.0/opus-game.apk";

        public static void BuildAndroidApk()
        {
            Debug.Log("[OPUS] OpusBuildScript: starting");
            try
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
                Debug.Log($"[OPUS] OpusBuildScript: target=Android, backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}, arch={PlayerSettings.Android.targetArchitectures}");

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0)
                {
                    Debug.LogWarning("[OPUS] OpusBuildScript: no scenes in Build Settings — falling back to Assets/Scenes/OrchardReach.unity only.");
                    scenes = new[] { "Assets/Scenes/OrchardReach.unity" };
                }
                Debug.Log($"[OPUS] OpusBuildScript: scenes = {string.Join(", ", scenes)}");

                string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, OutputRelativePath));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                Debug.Log($"[OPUS] OpusBuildScript: output = {outPath}");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                Debug.Log($"[OPUS] OpusBuildScript: result={summary.result} totalErrors={summary.totalErrors} totalWarnings={summary.totalWarnings} totalSize={summary.totalSize} outputPath={summary.outputPath}");

                if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    foreach (var step in report.steps)
                        foreach (var msg in step.messages)
                            if (msg.type == LogType.Error || msg.type == LogType.Exception)
                                Debug.LogError($"[OPUS] OpusBuildScript: BUILD ERROR [{step.name}]: {msg.content}");
                    throw new System.Exception($"Build did not succeed: {summary.result}");
                }

                Debug.Log("[OPUS] OpusBuildScript: DONE");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OPUS] OpusBuildScript: FAILED: {e}");
                throw;
            }
        }
    }
}
