using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Opus.Shell.Editor
{
    /// <summary>
    /// PH U6: the decisions behind <c>OpusBuildScript.BuildAndroidApkPhantomHand</c> that need no Unity API, so EditMode can test them:
    /// which scenes go into the APK, where it is written and what it is called, whether the run asked for a development build,
    /// what the failure message says, and whether the file that came out is a plausible Quest APK. No UnityEditor dependency on purpose.
    /// </summary>
    public static class PhantomBuildPlan
    {
        public const string BootstrapScene = "Assets/Scenes/Bootstrap.unity";
        public const string PhantomHandScene = "Assets/Scenes/PhantomHand.unity";
        public const string ManifestPath = "Assets/Games/PhantomHand/manifest.json";
        /// <summary>The composition root: a PhantomHand scene without a component of this script builds fine and then does nothing on the headset.</summary>
        public const string SceneControllerScript = "Assets/Shell/Runtime/PhantomHandSceneController.cs";
        public const string ApkBaseName = "chetna-phantom-hand";
        /// <summary>Environment switch for a development build (batch runs): 1 / true / yes / on.</summary>
        public const string EnvDevelopment = "PH_BUILD_DEVELOPMENT";
        /// <summary>Command-line switch for a development build: <c>-executeMethod ...BuildAndroidApkPhantomHand -phDevelopment</c>.</summary>
        public const string ArgDevelopment = "-phDevelopment";
        /// <summary>An IL2CPP / ARM64 Unity APK with the engine, Meta and OpenXR libraries is tens of MB; anything under this is broken.</summary>
        public const long MinPlausibleApkBytes = 5L * 1024 * 1024;

        /// <summary>
        /// The scenes of the APK, in build order: Bootstrap (only if the file exists) then PhantomHand. PhantomHand missing is an error: this
        /// build never substitutes another scene (the old BuildAndroidApk fell back to OrchardReach), and Orchard is never added.
        /// </summary>
        /// <param name="exists">project-relative scene path ("Assets/Scenes/X.unity") to "the file exists".</param>
        public static string[] ResolveScenes(Func<string, bool> exists)
        {
            if (exists == null) throw new ArgumentNullException("exists");
            if (!exists(PhantomHandScene))
                throw new InvalidOperationException(PhantomHandScene + " does not exist. Build it first (menu Tools/OPUS/Build PhantomHand Scene); " +
                                                    "this build never falls back to another scene.");
            var scenes = new List<string>();
            if (exists(BootstrapScene)) scenes.Add(BootstrapScene);
            scenes.Add(PhantomHandScene);
            return scenes.ToArray();
        }

        /// <summary>True when the scene file (Unity YAML text) holds a component of the script with this GUID (<c>m_Script: {fileID: 11500000, guid: ..., type: 3}</c>).</summary>
        public static bool SceneUsesScript(string sceneYaml, string scriptGuid)
        {
            if (string.IsNullOrEmpty(sceneYaml) || string.IsNullOrEmpty(scriptGuid)) return false;
            return sceneYaml.IndexOf("guid: " + scriptGuid, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>True when the run asked for a development build: env PH_BUILD_DEVELOPMENT is 1/true/yes/on, or the argument -phDevelopment is present.</summary>
        public static bool IsDevelopmentRequested(Func<string, string> env, string[] args)
        {
            string v = env != null ? env(EnvDevelopment) : null;
            if (!string.IsNullOrEmpty(v))
            {
                v = v.Trim();
                if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (args != null)
                foreach (var a in args)
                    if (a != null && a.Trim().Equals(ArgDevelopment, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The "version" of the Phantom Hand manifest, made safe for a folder name; the fallback (the player's bundle version) when the
        /// manifest is missing, not JSON or has no version; "0.0.0" when both are unusable.</summary>
        public static string VersionFromManifest(string manifestJson, string fallback)
        {
            string v = null;
            if (!string.IsNullOrWhiteSpace(manifestJson))
            {
                try
                {
                    var o = JToken.Parse(manifestJson) as JObject;
                    var t = o != null ? o["version"] : null;
                    if (t != null && t.Type == JTokenType.String) v = (string)t;
                }
                catch (Exception) { /* not JSON: use the fallback */ }
            }
            return SafeVersion(string.IsNullOrWhiteSpace(v) ? fallback : v);
        }

        /// <summary>Letters, digits, dot, dash and underscore only; anything else (separators, spaces) becomes '_'; never empty or all dots.</summary>
        public static string SafeVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
            var sb = new StringBuilder();
            foreach (char c in version.Trim())
                sb.Append(((char.IsLetterOrDigit(c) && c < 128) || c == '.' || c == '-' || c == '_') ? c : '_');
            string s = sb.ToString();
            return s.Trim('.').Length == 0 ? "0.0.0" : s;
        }

        public static string ApkFileName(bool development)
        {
            return ApkBaseName + (development ? "-dev" : "") + ".apk";
        }

        /// <summary>Output path relative to the REPOSITORY root (forward slashes): releases/game/&lt;version&gt;/chetna-phantom-hand[-dev].apk.</summary>
        public static string RelativeOutputPath(string version, bool development)
        {
            return "releases/game/" + SafeVersion(version) + "/" + ApkFileName(development);
        }

        /// <summary>The operator command that sets the hub / node addresses on the Quest without a rebuild (see PhantomEndpoints).</summary>
        public static string AdbPushCommand(string packageId)
        {
            return "adb push " + PhantomEndpoints.FileName + " /sdcard/Android/data/" + packageId + "/files/" + PhantomEndpoints.FileName;
        }

        /// <summary>The text of the exception (and the log) for a build that did not produce a usable APK: result, counts, time, output and the
        /// first <paramref name="maxLines"/> error messages of the build report.</summary>
        public static string FormatSummary(string headline, string result, int totalErrors, int totalWarnings, double totalSeconds,
                                           string outputPath, bool development, IList<string> errorLines, int maxLines = 15)
        {
            var sb = new StringBuilder();
            sb.Append(headline).Append(": result=").Append(result)
              .Append(" errors=").Append(totalErrors).Append(" warnings=").Append(totalWarnings)
              .Append(" time=").Append(totalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append("s")
              .Append(" mode=").Append(development ? "development" : "release")
              .Append(" output=").Append(outputPath);
            if (errorLines != null)
            {
                int n = Math.Min(maxLines, errorLines.Count);
                for (int i = 0; i < n; i++) sb.Append("\n  ").Append(errorLines[i]);
                if (errorLines.Count > n) sb.Append("\n  ... and ").Append(errorLines.Count - n).Append(" more error message(s) in the build log");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Null when <paramref name="path"/> is a plausible Quest APK; otherwise the reason it is not. Checked on the file itself, not on the
        /// build report: it must exist, be at least <paramref name="minBytes"/>, open as a zip, and contain AndroidManifest.xml and
        /// lib/arm64-v8a/libil2cpp.so (which also proves the IL2CPP + ARM64 settings took effect).
        /// </summary>
        public static string ApkProblem(string path, long minBytes = MinPlausibleApkBytes)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "no file at " + path;
            long size = new FileInfo(path).Length;
            if (size < minBytes) return "only " + size + " bytes (a Quest APK is at least " + minBytes + ")";
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    bool manifest = false, il2cpp = false;
                    foreach (var e in zip.Entries)
                    {
                        string n = e.FullName;
                        if (n == "AndroidManifest.xml") manifest = true;
                        else if (n.StartsWith("lib/arm64-v8a/", StringComparison.Ordinal) && n.EndsWith("libil2cpp.so", StringComparison.Ordinal)) il2cpp = true;
                    }
                    if (!manifest) return "the archive has no AndroidManifest.xml";
                    if (!il2cpp) return "the archive has no lib/arm64-v8a/libil2cpp.so (not an IL2CPP ARM64 build)";
                }
            }
            catch (Exception e)
            {
                return "not a readable zip archive (" + e.GetType().Name + ": " + e.Message + ")";
            }
            return null;
        }
    }
}
