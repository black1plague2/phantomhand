using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Opus.Shell.Tests
{
    /// <summary>
    /// The headset's diagnostics channel (<see cref="DeviceDiagnostics"/>) against a temp folder and a free port: what it hands out,
    /// what it refuses, and that the log file holds what Unity logged. Category "UnityEngine": Debug.Log and SystemInfo need the engine.
    /// </summary>
    [Category("UnityEngine")]
    public class DeviceDiagnosticsTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "opus_diag_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "opus_sessions", "s1"));
            File.WriteAllText(Path.Combine(_root, "opus_sessions", "s1", "events.ndjson"), "{\"n\":1}\n{\"n\":2}\n");
            File.WriteAllText(Path.Combine(_root, "secret.txt"), "not for the LAN");
            Directory.CreateDirectory(Path.Combine(_root, "Unity"));
            File.WriteAllText(Path.Combine(_root, "Unity", "state.txt"), "not for the LAN either");
            DeviceDiagnostics.Start(_root, port: 0, announce: false);
            Assert.Greater(DeviceDiagnostics.BoundPort, 0, "the server did not open a port");
        }

        [TearDown]
        public void TearDown()
        {
            DeviceDiagnostics.Stop();
            try { Directory.Delete(_root, true); } catch (Exception) { }
        }

        /// <summary>One GET over a plain socket: the body, and the status line in <paramref name="status"/>.</summary>
        private static string Get(string target, out string status)
        {
            using (var c = new TcpClient())
            {
                c.Connect("127.0.0.1", DeviceDiagnostics.BoundPort);
                c.ReceiveTimeout = 5000;
                var s = c.GetStream();
                byte[] req = Encoding.ASCII.GetBytes("GET " + target + " HTTP/1.1\r\nHost: test\r\n\r\n");
                s.Write(req, 0, req.Length);
                var all = new MemoryStream(); var buf = new byte[4096]; int r;
                while ((r = s.Read(buf, 0, buf.Length)) > 0) all.Write(buf, 0, r);
                string text = Encoding.UTF8.GetString(all.ToArray());
                int head = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                Assert.Greater(head, 0, "no HTTP head in the answer to " + target + ": " + text);
                status = text.Substring(0, text.IndexOf("\r\n", StringComparison.Ordinal));
                return text.Substring(head + 4);
            }
        }

        private static string LogName()
        {
            string status;
            return (string)JObject.Parse(Get("/", out status))["log"];
        }

        [Test]
        public void Index_ListsTheLogAndTheSessionFiles_AndNothingElse()
        {
            string status;
            var index = JObject.Parse(Get("/", out status));
            StringAssert.Contains("200", status);
            Assert.AreEqual(1, (int)index["opus_diag"]);
            var paths = index["files"].Select(f => (string)f["path"]).ToList();
            CollectionAssert.Contains(paths, (string)index["log"]);
            CollectionAssert.Contains(paths, "opus_sessions/s1/events.ndjson");
            Assert.AreEqual(2, paths.Count, "only the log and the session file: " + string.Join(", ", paths));
            Assert.AreEqual(16, (long)index["files"].First(f => (string)f["path"] == "opus_sessions/s1/events.ndjson")["size"]);

            var later = JObject.Parse(Get("/?since=" + ((long)index["now_ms"] + 60000), out status));
            CollectionAssert.AreEqual(new[] { (string)index["log"] }, later["files"].Select(f => (string)f["path"]).ToList(),
                "after a time in the future only the running log is listed (always, with its live size)");
            Debug.Log("diag-test growth");
            var grown = JObject.Parse(Get("/?since=" + ((long)index["now_ms"] + 60000), out status));
            Assert.Greater((long)grown["files"][0]["size"], (long)later["files"][0]["size"], "the listed size of the running log follows what was written");
        }

        [Test]
        public void Log_HoldsWhatUnityLogged_AndIsServedFromAnOffset()
        {
            Debug.Log("diag-test marker one");
            Debug.LogWarning("diag-test warning\nsecond line");
            LogAssert.Expect(LogType.Error, "diag-test error");
            Debug.LogError("diag-test error");
            DeviceDiagnostics.Line("[health] diag-test health");

            string status, rel = LogName();
            string log = Get("/f/" + rel, out status);
            StringAssert.Contains("200", status);
            StringAssert.StartsWith("# Chetna device log: device editor-", log);
            StringAssert.Contains(" I diag-test marker one\n", log);
            StringAssert.Contains(" W diag-test warning\n\tsecond line\n", log);
            StringAssert.Contains(" E diag-test error\n\t", log, "an error carries its stack, indented");
            StringAssert.Contains(" I [health] diag-test health\n", log);
            Assert.AreEqual(new FileInfo(DeviceDiagnostics.LogPath).Length, Encoding.UTF8.GetByteCount(log), "the served log is the whole file");

            int at = log.IndexOf("diag-test marker one", StringComparison.Ordinal);
            Assert.AreEqual(log.Substring(at), Get("/f/" + rel + "?offset=" + Encoding.UTF8.GetByteCount(log.Substring(0, at)), out status));
            Assert.AreEqual("", Get("/f/" + rel + "?offset=999999999", out status), "an offset past the end is an empty 200");
            StringAssert.Contains("200", status);
        }

        [Test]
        public void ALineThatRepeats_IsWrittenOnce_ThenCounted()
        {
            for (int i = 0; i < 6; i++) Debug.Log("diag-test same line");
            Debug.Log("diag-test another line");
            string status, log = Get("/f/" + LogName(), out status);
            Assert.AreEqual(1, log.Split('\n').Count(l => l.EndsWith(" I diag-test same line", StringComparison.Ordinal)), log);
            StringAssert.Contains(" I (the line above came 5 more times)\n", log);
            StringAssert.Contains(" I diag-test another line\n", log);
        }

        [TestCase("/f/secret.txt")]
        [TestCase("/f/logs/../secret.txt")]
        [TestCase("/f/logs/%2e%2e/secret.txt")]
        [TestCase("/f/..%2Fsecret.txt")]
        [TestCase("/f/Unity/state.txt")]
        [TestCase("/f/opus_sessions/../Unity/state.txt")]
        [TestCase("/f/opus_sessions/s1/missing.json")]
        [TestCase("/secret.txt")]
        [TestCase("/f/")]
        public void AnythingOutsideTheServedFolders_Is404(string target)
        {
            string status, body = Get(target, out status);
            StringAssert.Contains("404", status, target);
            StringAssert.DoesNotContain("not for the LAN", body);
        }

        [Test]
        public void AnAbsolutePath_Is404()
        {
            string status, body = Get("/f/" + Uri.EscapeDataString(Path.Combine(_root, "secret.txt")), out status);
            StringAssert.Contains("404", status);
            StringAssert.DoesNotContain("not for the LAN", body);
        }

        [Test]
        public void Resolve_KeepsToItsFolders()
        {
            Assert.IsNotNull(DeviceDiagnostics.Resolve(_root, "logs/a.log"));
            Assert.IsNotNull(DeviceDiagnostics.Resolve(_root, "opus_sessions/s1/events.ndjson"));
            Assert.IsNotNull(DeviceDiagnostics.Resolve(_root, PhantomEndpoints.FileName));
            Assert.IsNotNull(DeviceDiagnostics.Resolve(_root, "logs/../opus_sessions/s1/events.ndjson"), "a detour that ends inside a served folder is fine");
            foreach (var bad in new[] { "", "secret.txt", "../x", "logs/../../x", "logs/../secret.txt", "/etc/passwd", "C:\\Windows\\win.ini", "logs", "opus_sessions",
                                        "logsx/a.log", "opus_sessions_old/a.json", Path.Combine(_root, "secret.txt") })
                Assert.IsNull(DeviceDiagnostics.Resolve(_root, bad), "'" + bad + "' must not be served");
            Assert.IsNull(DeviceDiagnostics.Resolve(null, "logs/a.log"));
        }

        [Test]
        public void RequestLine_Parsing()
        {
            string path; long n;
            Assert.IsTrue(DeviceDiagnostics.TryParseRequest("GET /f/logs/a%20b.log?offset=120 HTTP/1.1", out path, out n));
            Assert.AreEqual("/f/logs/a b.log", path); Assert.AreEqual(120, n);
            Assert.IsTrue(DeviceDiagnostics.TryParseRequest("GET /?since=1760000000000 HTTP/1.1", out path, out n));
            Assert.AreEqual("/", path); Assert.AreEqual(1760000000000L, n);
            Assert.IsTrue(DeviceDiagnostics.TryParseRequest("GET /?offset=-5 HTTP/1.1", out path, out n));
            Assert.AreEqual(0, n, "a negative number is 0");
            Assert.IsTrue(DeviceDiagnostics.TryParseRequest("GET /f/x?offset=abc HTTP/1.1", out path, out n));
            Assert.AreEqual(0, n);
            Assert.IsFalse(DeviceDiagnostics.TryParseRequest("POST / HTTP/1.1", out path, out n), "GET only");
            Assert.IsFalse(DeviceDiagnostics.TryParseRequest("PUT /f/phantom_endpoints.json HTTP/1.1", out path, out n), "nothing can be written");
            Assert.IsFalse(DeviceDiagnostics.TryParseRequest("GET", out path, out n));
            Assert.IsFalse(DeviceDiagnostics.TryParseRequest("", out path, out n));
            Assert.IsFalse(DeviceDiagnostics.TryParseRequest(null, out path, out n));
        }
    }
}
