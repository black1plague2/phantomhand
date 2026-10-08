using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
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

        // ---- PH U6: Phantom Hand APK for the Quest -------------------------------------------------------------------------

        /// <summary>
        /// PH U6: Quest APK of Phantom Hand, from the menu or `-executeMethod Opus.Shell.Editor.OpusBuildScript.BuildAndroidApkPhantomHand`
        /// (no arguments; `-phDevelopment` on the command line or PH_BUILD_DEVELOPMENT=1 makes it a development build). Scenes: Bootstrap (only if
        /// Assets/Scenes/Bootstrap.unity exists) then PhantomHand, never Orchard. Output: &lt;repo&gt;/releases/game/&lt;manifest version&gt;/chetna-phantom-hand[-dev].apk.
        /// Throws (batch exit code 1) with the build report summary when the build fails or the file is not a plausible IL2CPP ARM64 APK; prints path and size.
        /// </summary>
        [MenuItem("Tools/OPUS/Build Phantom Hand APK")]
        public static void BuildAndroidApkPhantomHand()
        {
            BuildPhantomHandApk(PhantomBuildPlan.IsDevelopmentRequested(System.Environment.GetEnvironmentVariable, System.Environment.GetCommandLineArgs()));
        }

        /// <summary>The same build, always a development build (profiler-connectable, Meta XR Operator needs one): chetna-phantom-hand-dev.apk.</summary>
        [MenuItem("Tools/OPUS/Build Phantom Hand APK (development)")]
        public static void BuildAndroidApkPhantomHandDevelopment()
        {
            BuildPhantomHandApk(true);
        }

        /// <summary>The build itself; returns the APK path. Never leaves an old or half-written APK looking like the result of this run.</summary>
        public static string BuildPhantomHandApk(bool development)
        {
            string mode = development ? "development" : "release";
            Debug.Log($"[OPUS] OpusBuildScript(PH): starting a {mode} Phantom Hand APK build");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new BuildFailedException("Leave Play mode before building the APK.");

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string repoRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
                string[] scenes = PhantomBuildPlan.ResolveScenes(p => File.Exists(Path.Combine(projectRoot, p)));
                if (scenes[0] != PhantomBuildPlan.BootstrapScene)
                    Debug.LogWarning("[OPUS] OpusBuildScript(PH): Assets/Scenes/Bootstrap.unity does not exist, so the APK opens PhantomHand directly " +
                                     "(no game choice by the hub program). Menu Tools/OPUS/Build Bootstrap Scene creates it.");
                Debug.Log("[OPUS] OpusBuildScript(PH): scenes = " + string.Join(", ", scenes));
                RequireSceneController(projectRoot);

                string manifestPath = Path.Combine(projectRoot, PhantomBuildPlan.ManifestPath);
                string version = PhantomBuildPlan.VersionFromManifest(File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null, PlayerSettings.bundleVersion);
                string outPath = Path.GetFullPath(Path.Combine(repoRoot, PhantomBuildPlan.RelativeOutputPath(version, development)));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                SetAsideStaleApk(outPath);
                Debug.Log($"[OPUS] OpusBuildScript(PH): version = {version}, output = {outPath}");

                ApplyQuestBuildSettings();

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outPath,
                    target = BuildTarget.Android,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                var errors = new System.Collections.Generic.List<string>();
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception) errors.Add($"[{step.name}] {msg.content}");
                Debug.Log($"[OPUS] OpusBuildScript(PH): result={summary.result} totalErrors={summary.totalErrors} totalWarnings={summary.totalWarnings} totalSize={summary.totalSize} outputPath={summary.outputPath}");

                if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    DeleteQuietly(outPath);
                    throw new BuildFailedException(PhantomBuildPlan.FormatSummary("Phantom Hand APK build did not succeed", summary.result.ToString(),
                        summary.totalErrors, summary.totalWarnings, summary.totalTime.TotalSeconds, outPath, development, errors));
                }
                string problem = PhantomBuildPlan.ApkProblem(outPath);
                if (problem != null)
                {
                    string rejected = outPath + ".rejected";
                    DeleteQuietly(rejected);
                    try { if (File.Exists(outPath)) File.Move(outPath, rejected); }
                    catch (System.Exception) { /* the exception below is the news */ }
                    throw new BuildFailedException(PhantomBuildPlan.FormatSummary(
                        "Phantom Hand APK build reported success but the file is not a usable APK (" + problem + "; kept as " + rejected + ")",
                        summary.result.ToString(), summary.totalErrors, summary.totalWarnings, summary.totalTime.TotalSeconds, outPath, development, errors));
                }

                long bytes = new FileInfo(outPath).Length;
                string package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                Debug.Log($"[OPUS] OpusBuildScript(PH): DONE {mode} APK = {outPath} ({bytes} bytes, {bytes / 1048576.0:F1} MB), package {package}");
                Debug.Log($"[OPUS] OpusBuildScript(PH): install: adb install -r \"{outPath}\"");
                Debug.Log("[OPUS] OpusBuildScript(PH): hub/node addresses without a rebuild: " + PhantomBuildPlan.AdbPushCommand(package) + "   (then restart the app)");
                return outPath;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OPUS] OpusBuildScript(PH): FAILED: {e}");
                throw;
            }
        }

        /// <summary>
        /// The Quest settings of 02-RULES / U6 that a build can guarantee. Each is read first and only written when it differs, so the normal run
        /// leaves ProjectSettings untouched, and every line says what it found. Not here: 72 Hz (RigRuntimeSettings sets it at run time),
        /// MSAA 4x / HDR off (the URP asset), Quest 3 only (OVRProjectConfig) - those are verified by reading, see the run log.
        /// </summary>
        private static void ApplyQuestBuildSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log($"[OPUS] OpusBuildScript(PH): active build target is {EditorUserBuildSettings.activeBuildTarget}, switching to Android (assets re-import, several minutes)");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new BuildFailedException("Could not switch the active build target to Android (is the Android module installed?).");
            }
            Ensure("scripting backend", PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android), ScriptingImplementation.IL2CPP,
                   v => PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, v));
            Ensure("CPU architectures", PlayerSettings.Android.targetArchitectures, AndroidArchitecture.ARM64,
                   v => PlayerSettings.Android.targetArchitectures = v);
            Ensure("build system", EditorUserBuildSettings.androidBuildSystem, AndroidBuildSystem.Gradle,
                   v => EditorUserBuildSettings.androidBuildSystem = v);
            Ensure("texture compression", EditorUserBuildSettings.androidBuildSubtarget, MobileTextureSubtarget.ASTC,
                   v => EditorUserBuildSettings.androidBuildSubtarget = v);
            Ensure("build App Bundle (.aab) instead of .apk", EditorUserBuildSettings.buildAppBundle, false,
                   v => EditorUserBuildSettings.buildAppBundle = v);
            Ensure("export as Gradle project instead of an APK", EditorUserBuildSettings.exportAsGoogleAndroidProject, false,
                   v => EditorUserBuildSettings.exportAsGoogleAndroidProject = v);
            // The hub is plain http:// on the LAN and the session files go up with UnityWebRequest (LiveClient.UploadFileAsync). With Unity's default,
            // NotAllowed, a player may use plain HTTP only to localhost / 127.0.0.1: every editor run passes (its hub is on 127.0.0.1) and a headset,
            // whose hub is a LAN address, would upload nothing. Found on 8 Oct 2026 by reading the settings behind the first APKs, before any ran.
            Ensure("plain HTTP to the LAN hub (Player > Allow downloads over HTTP)", PlayerSettings.insecureHttpOption, InsecureHttpOption.AlwaysAllowed,
                   v => PlayerSettings.insecureHttpOption = v);

            // Vulkan only: automatic API selection off and the list exactly [Vulkan].
            bool automatic = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (!automatic && apis != null && apis.Length == 1 && apis[0] == UnityEngine.Rendering.GraphicsDeviceType.Vulkan)
                Debug.Log("[OPUS] OpusBuildScript(PH): graphics API: ok Vulkan only");
            else
            {
                Debug.LogWarning("[OPUS] OpusBuildScript(PH): graphics API: was " + (automatic || apis == null ? "automatic" : string.Join("+", apis)) + ", setting Vulkan only");
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            }
        }

        /// <summary>Fail before a long build, not after it: an APK whose PhantomHand scene has no PhantomHandSceneController would start and do nothing.</summary>
        private static void RequireSceneController(string projectRoot)
        {
            string guid = AssetDatabase.AssetPathToGUID(PhantomBuildPlan.SceneControllerScript);
            if (string.IsNullOrEmpty(guid))
                throw new BuildFailedException("Cannot find " + PhantomBuildPlan.SceneControllerScript + " in the project.");
            string scene = File.ReadAllText(Path.Combine(projectRoot, PhantomBuildPlan.PhantomHandScene));
            if (!PhantomBuildPlan.SceneUsesScript(scene, guid))
                throw new BuildFailedException(PhantomBuildPlan.PhantomHandScene + " has no PhantomHandSceneController component, so an APK built from it would start and do nothing. " +
                                               "Wire the controller into PhantomHandSceneBuilder (U5 builder hook) and rebuild the scene (Tools/OPUS/Build PhantomHand Scene), then build again.");
            Debug.Log("[OPUS] OpusBuildScript(PH): PhantomHand.unity carries PhantomHandSceneController (ok)");
        }

        private static void Ensure<T>(string what, T current, T wanted, System.Action<T> set)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(current, wanted))
            {
                Debug.Log($"[OPUS] OpusBuildScript(PH): {what}: ok ({current})");
                return;
            }
            Debug.LogWarning($"[OPUS] OpusBuildScript(PH): {what}: was {current}, setting {wanted}");
            set(wanted);
        }

        /// <summary>A failed build (a Gradle error is the known R7 case) must not leave an old APK that looks like this run's result.</summary>
        private static void SetAsideStaleApk(string outPath)
        {
            if (!File.Exists(outPath)) return;
            string prev = outPath + ".prev";
            DeleteQuietly(prev);
            File.Move(outPath, prev);
            Debug.Log($"[OPUS] OpusBuildScript(PH): the previous APK was moved to {prev}");
        }

        private static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception e) { Debug.LogWarning($"[OPUS] OpusBuildScript(PH): could not delete {path}: {e.Message}"); }
        }
    }

    /// <summary>
    /// The Meta SDK writes this PC's LAN address and the editor bridge's access token into Assets/Resources/DevAgentSettings.asset at the start of EVERY
    /// build (its DevAgentBuildProcessor, order 1) and puts the old values back afterwards. A development APK wants them (the on-device agent connects back
    /// to the editor). A release APK must not carry a credential for this PC's editor: the bridge listens on every network interface. This step runs after
    /// the SDK's and empties both fields in a build that is not a development build. Found on 8 Oct 2026 by looking inside the release APK.
    /// </summary>
    internal sealed class ReleaseBuildBridgeSettings : IPreprocessBuildWithReport
    {
        public const string AssetPath = "Assets/Resources/DevAgentSettings.asset";

        public int callbackOrder { get { return 1000; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) != 0) return;
            var asset = AssetDatabase.LoadMainAssetAtPath(AssetPath);
            if (asset == null) return;
            var so = new SerializedObject(asset);
            var token = so.FindProperty("accessToken");
            var address = so.FindProperty("serverAddress");
            if (token == null || address == null)
            {
                Debug.LogWarning("[OPUS] OpusBuildScript: DevAgentSettings has no accessToken / serverAddress field any more: check that this release build does not carry the editor bridge token");
                return;
            }
            token.stringValue = ""; address.stringValue = "";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            Debug.Log("[OPUS] OpusBuildScript: release build, the editor bridge's address and token are left out of DevAgentSettings");
        }
    }
}
