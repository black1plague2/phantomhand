using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Opus.Shell.Tests
{
    /// <summary>
    /// EditMode tests for the Quest runtime override <c>phantom_endpoints.json</c> (precedence env &gt; file &gt; asset &gt; discovery).
    /// The parsing is pure (the file text is passed in), so nothing here touches the disk or the device. Tests in category
    /// "UnityEngine" create a ScriptableObject or read Application, which needs the real engine.
    /// </summary>
    public class PhantomEndpointsFileTests
    {
        private const string Full =
            "{ \"hubHost\": \"192.168.1.20\", \"hubPort\": 8788, \"nodeAHost\": \"192.168.1.31\", \"nodeAPort\": 9001, " +
            "\"nodeBHost\": \"192.168.1.32\", \"nodeBPort\": 9002, \"discoveryPort\": 9791 }";

        private static PhantomEndpoints.FileValues Parse(string text)
        {
            PhantomEndpoints.FileValues v; string problem;
            Assert.IsTrue(PhantomEndpoints.TryParseFile(text, out v, out problem), "should parse: " + problem);
            Assert.IsNull(problem);
            return v;
        }

        // ---- the pure parser ----------------------------------------------------------------------------------------------

        [Test]
        public void TryParseFile_AllSevenKeys()
        {
            var v = Parse(Full);
            Assert.AreEqual("192.168.1.20", v.HubHost); Assert.AreEqual(8788, v.HubPort);
            Assert.AreEqual("192.168.1.31", v.NodeAHost); Assert.AreEqual(9001, v.NodeAPort);
            Assert.AreEqual("192.168.1.32", v.NodeBHost); Assert.AreEqual(9002, v.NodeBPort);
            Assert.AreEqual(9791, v.DiscoveryPort);
            CollectionAssert.AreEqual(new[] { "hubHost", "hubPort", "nodeAHost", "nodeAPort", "nodeBHost", "nodeBPort", "discoveryPort" }, v.Keys);
        }

        [Test]
        public void TryParseFile_PartialFile_SetsOnlyTheGivenKeys()
        {
            var v = Parse("{ \"nodeAHost\": \"10.0.0.7\" }");
            Assert.AreEqual("10.0.0.7", v.NodeAHost);
            Assert.IsNull(v.HubHost); Assert.IsFalse(v.HubPort.HasValue); Assert.IsFalse(v.NodeAPort.HasValue);
            Assert.IsNull(v.NodeBHost); Assert.IsFalse(v.DiscoveryPort.HasValue);
            CollectionAssert.AreEqual(new[] { "nodeAHost" }, v.Keys);
            Assert.AreEqual(0, Parse("{}").Keys.Count, "an empty object is a valid file that sets nothing");
        }

        [TestCase("")]
        [TestCase("   \r\n ")]
        [TestCase("{")]
        [TestCase("[]")]
        [TestCase("42")]
        [TestCase("null")]
        [TestCase("\"hubHost\"")]
        [TestCase("not json at all")]
        [TestCase("{ \"hubHost\": }")]
        [TestCase("{} trailing")]
        [TestCase("}{")]
        public void TryParseFile_NotAJsonObject_IsRejected_NeverThrows(string text)
        {
            PhantomEndpoints.FileValues v = null; string problem = null; bool ok = true;
            Assert.DoesNotThrow(() => ok = PhantomEndpoints.TryParseFile(text, out v, out problem));
            Assert.IsFalse(ok); Assert.IsNull(v);
            Assert.IsFalse(string.IsNullOrEmpty(problem), "a rejected file says why");
        }

        [Test]
        public void TryParseFile_Null_IsRejected()
        {
            PhantomEndpoints.FileValues v; string problem;
            Assert.IsFalse(PhantomEndpoints.TryParseFile(null, out v, out problem));
            Assert.IsNotNull(problem);
        }

        [TestCase("{ \"hubPort\": 0 }", "hubPort")]
        [TestCase("{ \"hubPort\": 65536 }", "hubPort")]
        [TestCase("{ \"hubPort\": -1 }", "hubPort")]
        [TestCase("{ \"hubPort\": \"abc\" }", "hubPort")]
        [TestCase("{ \"nodeAPort\": 1.5 }", "nodeAPort")]
        [TestCase("{ \"nodeBPort\": true }", "nodeBPort")]
        [TestCase("{ \"nodeBPort\": 99999999999999999999 }", "nodeBPort")]
        [TestCase("{ \"discoveryPort\": [8791] }", "discoveryPort")]
        [TestCase("{ \"hubHost\": 5 }", "hubHost")]
        [TestCase("{ \"nodeAHost\": \"a:b:c\" }", "nodeAHost")]
        [TestCase("{ \"nodeBHost\": \"1.2.3.4:99999\" }", "nodeBHost")]
        [TestCase("{ \"nodeAHost\": { \"ip\": \"1.2.3.4\" } }", "nodeAHost")]
        public void TryParseFile_WrongValueInAKnownKey_RejectsTheWholeFile(string text, string key)
        {
            PhantomEndpoints.FileValues v; string problem;
            Assert.IsFalse(PhantomEndpoints.TryParseFile(text, out v, out problem));
            Assert.IsNull(v);
            StringAssert.Contains(key, problem, "the reason names the key");
        }

        [Test]
        public void TryParseFile_WrongValueMakesTheValidKeysUnusableToo()
        {
            PhantomEndpoints.FileValues v; string problem;
            Assert.IsFalse(PhantomEndpoints.TryParseFile("{ \"hubHost\": \"10.0.0.2\", \"hubPort\": \"oops\" }", out v, out problem));
            Assert.IsNull(v, "all or nothing: no half-applied file");
        }

        [Test]
        public void TryParseFile_Bom_Whitespace_CaseInsensitiveKeys_UnknownKeysIgnored()
        {
            string bom = ((char)0xFEFF).ToString();
            var v = Parse(bom + "\r\n  { \"HUBHOST\": \" 10.0.0.2 \", \"NodeAport\": 9000, \"somethingElse\": [1, 2], \"// note\": \"x\" }  \r\n");
            Assert.AreEqual("10.0.0.2", v.HubHost);
            Assert.AreEqual(9000, v.NodeAPort);
            Assert.IsNull(v.NodeAHost);
        }

        [Test]
        public void TryParseFile_EmptyStringAndNull_MeanNotSet()
        {
            var v = Parse("{ \"hubHost\": \"\", \"hubPort\": null, \"nodeAHost\": \"   \", \"nodeAPort\": \"\", \"discoveryPort\": null }");
            Assert.IsNull(v.HubHost); Assert.IsFalse(v.HubPort.HasValue); Assert.IsNull(v.NodeAHost);
            Assert.IsFalse(v.NodeAPort.HasValue); Assert.IsFalse(v.DiscoveryPort.HasValue);
            Assert.AreEqual(0, v.Keys.Count);
        }

        [Test]
        public void TryParseFile_PortAsNumericString_IsAccepted()
        {
            var v = Parse("{ \"hubPort\": \" 8787 \", \"discoveryPort\": \"8791\" }");
            Assert.AreEqual(8787, v.HubPort); Assert.AreEqual(8791, v.DiscoveryPort);
        }

        [Test]
        public void TryParseFile_HostWithPort_ExplicitPortKeyWins()
        {
            var embedded = Parse("{ \"hubHost\": \"10.0.0.2:9000\", \"nodeAHost\": \"[::1]:9100\" }");
            Assert.AreEqual("10.0.0.2", embedded.HubHost); Assert.AreEqual(9000, embedded.HubPort);
            Assert.AreEqual("::1", embedded.NodeAHost); Assert.AreEqual(9100, embedded.NodeAPort);
            var both = Parse("{ \"hubHost\": \"10.0.0.2:9000\", \"hubPort\": 8787 }");
            Assert.AreEqual("10.0.0.2", both.HubHost); Assert.AreEqual(8787, both.HubPort);
        }

        // ---- precedence through Resolve -----------------------------------------------------------------------------------

        private static Dictionary<string, string> Env(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        [Test]
        public void Resolve_FileOverridesTheDefaults_AndCarriesTheHubPort()
        {
            var warnings = new List<string>();
            var e = PhantomEndpoints.Resolve(n => null, null, () => Full, warnings.Add);
            Assert.AreEqual("192.168.1.20", e.HubHost); Assert.AreEqual(8788, e.HubPort, "hubPort travels in the resolved endpoints");
            Assert.AreEqual("192.168.1.31", e.NodeAHost); Assert.AreEqual(9001, e.NodeAPort);
            Assert.AreEqual("192.168.1.32", e.NodeBHost); Assert.AreEqual(9002, e.NodeBPort);
            Assert.AreEqual(9791, e.DiscoveryPort);
            Assert.IsFalse(e.FromEnvironment, "a file is not the harness environment");
            StringAssert.StartsWith("applied hubHost", e.FileStatus);
            Assert.AreEqual(0, warnings.Count);
        }

        [Test]
        public void Resolve_EnvironmentBeatsTheFile_FieldByField()
        {
            var env = Env(PhantomEndpoints.EnvHub, "127.0.0.1:40123", PhantomEndpoints.EnvDiscoveryPort, "39791");
            var e = PhantomEndpoints.Resolve(k => env.ContainsKey(k) ? env[k] : null, null, () => Full, null);
            Assert.AreEqual("127.0.0.1", e.HubHost); Assert.AreEqual(40123, e.HubPort);      // env
            Assert.AreEqual(39791, e.DiscoveryPort);                                         // env
            Assert.AreEqual("192.168.1.31", e.NodeAHost); Assert.AreEqual(9001, e.NodeAPort); // file (env has no node A)
            Assert.AreEqual("192.168.1.32", e.NodeBHost); Assert.AreEqual(9002, e.NodeBPort); // file
            Assert.IsTrue(e.FromEnvironment);
        }

        [Test]
        public void Resolve_NoFile_ChangesNothing_AndWarnsNothing()
        {
            var warnings = new List<string>();
            var e = PhantomEndpoints.Resolve(null, null, () => null, warnings.Add);
            Assert.IsNull(e.HubHost); Assert.IsNull(e.NodeAHost); Assert.IsNull(e.NodeBHost);
            Assert.AreEqual(8791, e.DiscoveryPort); Assert.AreEqual(8790, e.NodeAPort); Assert.AreEqual(0, e.HubPort);
            Assert.IsNull(e.FileStatus); Assert.AreEqual(0, warnings.Count, "an absent file is the normal case, not a warning");
            Assert.DoesNotThrow(() => PhantomEndpoints.Resolve(null, null, null, null), "no reader = no file layer");
        }

        [TestCase("{")]
        [TestCase("[1,2]")]
        [TestCase("{ \"hubPort\": 70000 }")]
        [TestCase("")]
        public void Resolve_MalformedFile_IsIgnored_WithExactlyOneWarning(string text)
        {
            var warnings = new List<string>();
            var e = PhantomEndpoints.Resolve(null, null, () => text, warnings.Add);
            Assert.IsNull(e.HubHost); Assert.IsNull(e.NodeAHost); Assert.AreEqual(8791, e.DiscoveryPort);
            Assert.IsNull(e.FileStatus);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(PhantomEndpoints.FileName, warnings[0]);
        }

        [Test]
        public void Resolve_UnreadableFile_IsIgnored_WithExactlyOneWarning()
        {
            var warnings = new List<string>();
            PhantomEndpoints e = null;
            Assert.DoesNotThrow(() => e = PhantomEndpoints.Resolve(null, null, () => { throw new IOException("sharing violation"); }, warnings.Add));
            Assert.IsNull(e.HubHost);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("sharing violation", warnings[0]);
        }

        [Test]
        public void Resolve_MalformedFile_DoesNotDisturbTheEnvironmentLayer()
        {
            var env = Env(PhantomEndpoints.EnvNodeA, "127.0.0.1:39790");
            var e = PhantomEndpoints.Resolve(k => env.ContainsKey(k) ? env[k] : null, null, () => "{", null);
            Assert.AreEqual("127.0.0.1", e.NodeAHost); Assert.AreEqual(39790, e.NodeAPort); Assert.IsTrue(e.FromEnvironment);
        }

        [Test]
        public void Names_MatchTheDocumentedAdbPath()
        {
            Assert.AreEqual("phantom_endpoints.json", PhantomEndpoints.FileName);
            Assert.AreEqual(64 * 1024, PhantomEndpoints.MaxFileBytes);
        }

        // ---- needs the engine (ScriptableObject, Application) ---------------------------------------------------------------

        [Test, Category("UnityEngine")]
        public void Resolve_FileSitsBetweenTheSettingsAssetAndTheEnvironment()
        {
            var s = UnityEngine.ScriptableObject.CreateInstance<PhantomHandSettings>();
            s.hubHost = "10.0.0.2"; s.nodeAHost = "10.0.0.3"; s.nodeBHost = "10.0.0.4"; s.discoveryPort = 8791;
            var env = Env(PhantomEndpoints.EnvNodeB, "127.0.0.1:39792");
            var e = PhantomEndpoints.Resolve(k => env.ContainsKey(k) ? env[k] : null, s,
                () => "{ \"hubHost\": \"10.0.0.9\", \"nodeBHost\": \"10.0.0.8\", \"discoveryPort\": 9791 }", null);
            Assert.AreEqual("10.0.0.9", e.HubHost, "file beats the asset");
            Assert.AreEqual("10.0.0.3", e.NodeAHost, "a key the file does not set keeps the asset value");
            Assert.AreEqual("127.0.0.1", e.NodeBHost, "env beats the file"); Assert.AreEqual(39792, e.NodeBPort);
            Assert.AreEqual(9791, e.DiscoveryPort);
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test, Category("UnityEngine")]
        public void Resolve_TwoArgOverload_NeverReadsAFileOutsideAnAndroidPlayer()
        {
            var e = PhantomEndpoints.Resolve(n => null, null);
            Assert.IsNull(e.FileStatus);
            Assert.IsNull(e.HubHost);
        }
    }
}
