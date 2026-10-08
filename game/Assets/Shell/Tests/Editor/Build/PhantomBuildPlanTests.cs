using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using Opus.Shell.Editor;

namespace Opus.Shell.Tests.Build
{
    /// <summary>
    /// EditMode tests for the decisions behind OpusBuildScript.BuildAndroidApkPhantomHand (PH U6). The Unity build itself (BuildPipeline,
    /// Gradle, IL2CPP) is not run here; everything it decides before and after is: which scenes, what name, which flavour, what the
    /// failure says, and whether the file that came out is a plausible IL2CPP ARM64 APK.
    /// </summary>
    public class PhantomBuildPlanTests
    {
        private readonly List<string> _temp = new List<string>();

        [TearDown]
        public void DeleteTempFiles()
        {
            foreach (var p in _temp) { try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { /* temp file, best effort */ } }
            _temp.Clear();
        }

        // ---- scenes -------------------------------------------------------------------------------------------------------

        [Test]
        public void Scenes_BootstrapPresent_BootstrapFirstThenPhantomHand()
        {
            var scenes = PhantomBuildPlan.ResolveScenes(p => p == PhantomBuildPlan.BootstrapScene || p == PhantomBuildPlan.PhantomHandScene);
            CollectionAssert.AreEqual(new[] { "Assets/Scenes/Bootstrap.unity", "Assets/Scenes/PhantomHand.unity" }, scenes);
        }

        [Test]
        public void Scenes_BootstrapAbsent_OnlyPhantomHand()
        {
            var scenes = PhantomBuildPlan.ResolveScenes(p => p == PhantomBuildPlan.PhantomHandScene);
            CollectionAssert.AreEqual(new[] { "Assets/Scenes/PhantomHand.unity" }, scenes);
        }

        [Test]
        public void Scenes_NeverContainOrchard_NoMatterWhatExists()
        {
            var scenes = PhantomBuildPlan.ResolveScenes(p => true);
            CollectionAssert.AreEqual(new[] { "Assets/Scenes/Bootstrap.unity", "Assets/Scenes/PhantomHand.unity" }, scenes);
            foreach (var s in scenes) StringAssert.DoesNotContain("Orchard", s);
        }

        [Test]
        public void Scenes_PhantomHandMissing_Throws_InsteadOfFallingBackToAnotherScene()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PhantomBuildPlan.ResolveScenes(p => p == "Assets/Scenes/OrchardReach.unity" || p == PhantomBuildPlan.BootstrapScene));
            StringAssert.Contains("PhantomHand.unity", ex.Message);
            Assert.Throws<ArgumentNullException>(() => PhantomBuildPlan.ResolveScenes(null));
        }

        // ---- development switch -------------------------------------------------------------------------------------------

        private static Func<string, string> Env(string value)
        {
            return name => name == PhantomBuildPlan.EnvDevelopment ? value : null;
        }

        [TestCase("1")]
        [TestCase("true")]
        [TestCase("TRUE")]
        [TestCase("Yes")]
        [TestCase(" on ")]
        public void Development_EnvironmentSwitchOn(string value)
        {
            Assert.IsTrue(PhantomBuildPlan.IsDevelopmentRequested(Env(value), new string[0]));
        }

        [TestCase("0")]
        [TestCase("")]
        [TestCase("no")]
        [TestCase("false")]
        [TestCase(null)]
        public void Development_EnvironmentSwitchOffOrAbsent(string value)
        {
            Assert.IsFalse(PhantomBuildPlan.IsDevelopmentRequested(Env(value), new[] { "Unity.exe", "-batchmode", "-quit" }));
        }

        [Test]
        public void Development_CommandLineFlag()
        {
            Assert.IsTrue(PhantomBuildPlan.IsDevelopmentRequested(Env(null), new[] { "Unity.exe", "-executeMethod", "X.Y", "-phDevelopment" }));
            Assert.IsTrue(PhantomBuildPlan.IsDevelopmentRequested(Env(null), new[] { "-PHDEVELOPMENT" }));
            Assert.IsFalse(PhantomBuildPlan.IsDevelopmentRequested(Env(null), new[] { "-phDevelopmentX", "development" }));
            Assert.IsFalse(PhantomBuildPlan.IsDevelopmentRequested(null, null), "no environment reader and no arguments is a release build");
        }

        // ---- names --------------------------------------------------------------------------------------------------------

        [Test]
        public void Output_ReleaseAndDevelopmentNames_AreDifferentFiles()
        {
            Assert.AreEqual("releases/game/0.1.0/chetna-phantom-hand.apk", PhantomBuildPlan.RelativeOutputPath("0.1.0", false));
            Assert.AreEqual("releases/game/0.1.0/chetna-phantom-hand-dev.apk", PhantomBuildPlan.RelativeOutputPath("0.1.0", true));
            StringAssert.Contains("phantom-hand", PhantomBuildPlan.ApkFileName(false));
            Assert.AreNotEqual(PhantomBuildPlan.ApkFileName(false), PhantomBuildPlan.ApkFileName(true));
            StringAssert.EndsWith(".apk", PhantomBuildPlan.ApkFileName(true));
        }

        [Test]
        public void Version_ComesFromTheManifest_ElseTheFallback_ElseZero()
        {
            Assert.AreEqual("0.1.0", PhantomBuildPlan.VersionFromManifest("{ \"id\": \"phantom_hand\", \"version\": \"0.1.0\" }", "1.0"));
            Assert.AreEqual("1.0", PhantomBuildPlan.VersionFromManifest("{ \"id\": \"phantom_hand\" }", "1.0"), "no version key");
            Assert.AreEqual("1.0", PhantomBuildPlan.VersionFromManifest("{ \"version\": 3 }", "1.0"), "version is not a string");
            Assert.AreEqual("1.0", PhantomBuildPlan.VersionFromManifest("not json", "1.0"));
            Assert.AreEqual("1.0", PhantomBuildPlan.VersionFromManifest(null, "1.0"));
            Assert.AreEqual("0.0.0", PhantomBuildPlan.VersionFromManifest(null, null));
            Assert.AreEqual("0.0.0", PhantomBuildPlan.VersionFromManifest("{ \"version\": \"  \" }", ""));
        }

        [Test]
        public void Version_CannotEscapeTheReleasesFolder()
        {
            Assert.AreEqual("0.0.0", PhantomBuildPlan.SafeVersion(".."));
            Assert.AreEqual("0.0.0", PhantomBuildPlan.SafeVersion("."));
            Assert.AreEqual(".._x", PhantomBuildPlan.SafeVersion("..\\x"));
            Assert.AreEqual(".._.._x", PhantomBuildPlan.SafeVersion("../../x"));
            Assert.AreEqual("1.0_beta_2", PhantomBuildPlan.SafeVersion(" 1.0 beta/2 "));
            StringAssert.DoesNotContain("/..", PhantomBuildPlan.RelativeOutputPath("../../etc", false).Substring("releases/game/".Length));
        }

        [Test]
        public void AdbPushCommand_NamesTheSameFileTheGameReads()
        {
            string cmd = PhantomBuildPlan.AdbPushCommand("com.DefaultCompany.OPUS");
            Assert.AreEqual("adb push phantom_endpoints.json /sdcard/Android/data/com.DefaultCompany.OPUS/files/phantom_endpoints.json", cmd);
            StringAssert.Contains(PhantomEndpoints.FileName, cmd);
        }

        // ---- the scene must carry the composition root ---------------------------------------------------------------------

        [Test]
        public void SceneUsesScript_FindsTheScriptGuidInUnityYaml()
        {
            const string guid = "e1f5cf6a0a056904dbf5d454c11d138b";
            string yaml = "--- !u!114 &123\nMonoBehaviour:\n  m_GameObject: {fileID: 7}\n  m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}\n  m_Name: \n";
            Assert.IsTrue(PhantomBuildPlan.SceneUsesScript(yaml, guid));
            Assert.IsTrue(PhantomBuildPlan.SceneUsesScript(yaml, guid.ToUpperInvariant()));
            Assert.IsFalse(PhantomBuildPlan.SceneUsesScript(yaml, "0123456789abcdef0123456789abcdef"), "another script's guid");
            Assert.IsFalse(PhantomBuildPlan.SceneUsesScript("", guid));
            Assert.IsFalse(PhantomBuildPlan.SceneUsesScript(null, guid));
            Assert.IsFalse(PhantomBuildPlan.SceneUsesScript(yaml, ""));
            Assert.IsFalse(PhantomBuildPlan.SceneUsesScript(yaml, null));
            StringAssert.EndsWith("PhantomHandSceneController.cs", PhantomBuildPlan.SceneControllerScript);
        }

        // ---- failure text -------------------------------------------------------------------------------------------------

        [Test]
        public void Summary_CarriesResultCountsOutputAndTheFirstErrors()
        {
            var errors = new List<string> { "[Gradle] Unable to establish loopback connection", "[IL2CPP] boom" };
            string s = PhantomBuildPlan.FormatSummary("Phantom Hand APK build did not succeed", "Failed", 3, 12, 812.46, "C:/r/releases/game/0.1.0/x.apk", true, errors);
            StringAssert.StartsWith("Phantom Hand APK build did not succeed", s);
            StringAssert.Contains("result=Failed", s);
            StringAssert.Contains("errors=3", s);
            StringAssert.Contains("warnings=12", s);
            StringAssert.Contains("time=812.5s", s);
            StringAssert.Contains("mode=development", s);
            StringAssert.Contains("output=C:/r/releases/game/0.1.0/x.apk", s);
            StringAssert.Contains("Unable to establish loopback connection", s);
            StringAssert.Contains("[IL2CPP] boom", s);
            StringAssert.Contains("mode=release", PhantomBuildPlan.FormatSummary("h", "Failed", 0, 0, 1, "p", false, null));
        }

        [Test]
        public void Summary_CapsALongErrorList()
        {
            var errors = new List<string>();
            for (int i = 0; i < 20; i++) errors.Add("error " + i);
            string s = PhantomBuildPlan.FormatSummary("h", "Failed", 20, 0, 1, "p", false, errors, 5);
            StringAssert.Contains("error 4", s);
            StringAssert.DoesNotContain("error 5", s);
            StringAssert.Contains("and 15 more", s);
        }

        // ---- is the file a plausible Quest APK ----------------------------------------------------------------------------

        private string MakeZip(params string[] entries)
        {
            string path = Path.Combine(Path.GetTempPath(), "ph_apk_test_" + Guid.NewGuid().ToString("N") + ".apk");
            _temp.Add(path);
            using (var fs = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                foreach (var name in entries)
                    using (var s = zip.CreateEntry(name).Open()) s.WriteByte(1);
            return path;
        }

        [Test]
        public void Apk_MissingFile_IsAProblem()
        {
            StringAssert.Contains("no file", PhantomBuildPlan.ApkProblem(Path.Combine(Path.GetTempPath(), "ph_does_not_exist_" + Guid.NewGuid().ToString("N") + ".apk")));
            StringAssert.Contains("no file", PhantomBuildPlan.ApkProblem(null));
        }

        [Test]
        public void Apk_TooSmall_IsAProblem_ByDefault()
        {
            string good = MakeZip("AndroidManifest.xml", "lib/arm64-v8a/libil2cpp.so");
            StringAssert.Contains("bytes", PhantomBuildPlan.ApkProblem(good), "a valid zip of a few hundred bytes is still not a Quest APK");
            Assert.IsNull(PhantomBuildPlan.ApkProblem(good, 1), "the same file passes once the minimum is lowered: only the size rule rejected it");
        }

        [Test]
        public void Apk_NotAZip_IsAProblem()
        {
            string path = Path.Combine(Path.GetTempPath(), "ph_apk_test_" + Guid.NewGuid().ToString("N") + ".apk");
            _temp.Add(path);
            File.WriteAllBytes(path, new byte[4096]);
            StringAssert.Contains("not a readable zip", PhantomBuildPlan.ApkProblem(path, 1));
        }

        [Test]
        public void Apk_WithoutManifest_IsAProblem()
        {
            StringAssert.Contains("AndroidManifest.xml", PhantomBuildPlan.ApkProblem(MakeZip("lib/arm64-v8a/libil2cpp.so"), 1));
        }

        [Test]
        public void Apk_MonoBuild_OrWrongArchitecture_IsAProblem()
        {
            StringAssert.Contains("libil2cpp.so", PhantomBuildPlan.ApkProblem(MakeZip("AndroidManifest.xml", "lib/arm64-v8a/libmono.so"), 1));
            StringAssert.Contains("libil2cpp.so", PhantomBuildPlan.ApkProblem(MakeZip("AndroidManifest.xml", "lib/armeabi-v7a/libil2cpp.so"), 1));
        }

        [Test]
        public void Apk_Il2cppArm64WithManifest_Passes()
        {
            Assert.IsNull(PhantomBuildPlan.ApkProblem(MakeZip("AndroidManifest.xml", "classes.dex", "lib/arm64-v8a/libunity.so", "lib/arm64-v8a/libil2cpp.so"), 1));
        }
    }
}
