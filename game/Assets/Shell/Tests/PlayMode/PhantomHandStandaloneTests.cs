using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Sdk;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Debug = UnityEngine.Debug;

namespace Opus.Shell.Tests.PlayMode
{
    /// <summary>
    /// PH_Standalone: the headset on its own. No operator app (the hub address is this PC, where no hub listens), no sleeve and no
    /// muscle sensor (discovery on a port nothing beacons on, so the real boards on the LAN are never found and never driven), the
    /// scripted participant. Before the run the standby card must be up and nothing may start by itself; the run must go through
    /// every phase and record itself, every stroke logged as not delivered; and the diagnostics channel (DeviceDiagnostics) must
    /// hand out the log and the session files over HTTP, which is how a PC gets them from a headset that has no cable.
    /// </summary>
    public class PhantomHandStandaloneTests
    {
        private const string ScenePath = "Assets/Scenes/PhantomHand.unity";

        [TearDown]
        public void TearDown() { PhantomHandOverrides.Reset(); }

        private PhantomHandSceneController _controller;
        private OpusSessionRunner _runner;

        /// <summary>The real scene with the scripted participant, a hub address where no hub listens and discovery on a quiet port; no AutoStart: the scene has to wait, as a headset does.</summary>
        private IEnumerator OpenScene()
        {
            var env = new Dictionary<string, string>
            {
                { PhantomEndpoints.EnvHub, "127.0.0.1" }, { PhantomEndpoints.EnvDiscoveryPort, "21791" }, { PhantomEndpoints.EnvTelemetryPort, "21794" },
            };
            PhantomHandOverrides.Env = k => env.ContainsKey(k) ? env[k] : null;
            PhantomHandOverrides.ParamOverrides = new JObject { ["demo_mode"] = true };
            PhantomHandOverrides.Seed = 20261009;
            PhantomHandOverrides.ForceDemoHands = true;
            PhantomHandOverrides.ScriptedParticipant = true;
            PhantomHandOverrides.DriveSession = true;
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null; yield return null;
            _controller = UnityEngine.Object.FindFirstObjectByType<PhantomHandSceneController>();
            Assert.IsNotNull(_controller, "PhantomHandSceneController not found: is this the PhantomHand scene?");
            _runner = _controller.GetComponent<OpusSessionRunner>();
            Assert.IsNotNull(_runner, "the controller must bring its OpusSessionRunner");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 5f && (_controller.StandbyCard == null || !_controller.StandbyCard.Shown)) yield return null;
            Assert.IsTrue(_controller.StandbyCard != null && _controller.StandbyCard.Shown, "no standby card before the run");
            while (Time.realtimeSinceStartup - t0 < 3f) yield return null;
        }

        /// <summary>A real APK starts in the launch scene (Bootstrap), which opens a hub client while it waits for a program. That client must
        /// be stopped once the game scene is up: on the first headset run it was not, and two clients with one device id threw each
        /// other off the operator app's hub every second.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator PH_Bootstrap_StopsItsHubClientOnceTheGameSceneIsUp()
        {
            var env = new Dictionary<string, string>
            {
                { PhantomEndpoints.EnvHub, "127.0.0.1" }, { PhantomEndpoints.EnvDiscoveryPort, "21791" }, { PhantomEndpoints.EnvTelemetryPort, "21794" },
            };
            PhantomHandOverrides.Env = k => env.ContainsKey(k) ? env[k] : null;
            PhantomHandOverrides.ForceDemoHands = true;
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Bootstrap.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
#endif
            yield return null; yield return null;
            var boot = UnityEngine.Object.FindFirstObjectByType<BootstrapLoader>();
            Assert.IsNotNull(boot, "BootstrapLoader not found in the launch scene");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var client = (LiveClient)typeof(BootstrapLoader).GetField("_client", flags).GetValue(boot);
            Assert.IsNotNull(client, "the launch scene opens a hub client while it waits for a program (bootstrapProgramWaitSec > 0)");
            var running = typeof(LiveClient).GetField("_running", flags);
            Assert.IsTrue((bool)running.GetValue(client), "that client is running while the launch scene waits");

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 30f && UnityEngine.Object.FindFirstObjectByType<PhantomHandSceneController>() == null) yield return null;
            Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<PhantomHandSceneController>(), "the launch scene never opened the game scene");
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.IsFalse((bool)running.GetValue(client), "the launch scene's hub client is still running after the game scene opened");
        }

        /// <summary>Taking the headset off pauses the run and putting it on again resumes it: the phase and its time left stand still in
        /// between. On the first headset night a run that sat off the head for 7 minutes ended as "completed" the moment it woke.</summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator PH_Standalone_TakingTheHeadsetOff_PausesTheRun()
        {
            yield return OpenScene();
            var controller = _controller; var runner = _runner;
            runner.StartSession("test: as the both-hands pinch does");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 90f && (controller.Module == null || Opus.Games.PhantomHand.PhNames.Of(controller.Module.CurrentPhase) != "induction"))
                yield return null;
            Assert.AreEqual("induction", Opus.Games.PhantomHand.PhNames.Of(controller.Module.CurrentPhase), "the run never reached the induction");
            yield return new WaitForSecondsRealtime(1.0f);

            runner.SendMessage("OnApplicationPause", true);
            Assert.AreEqual(OpusSessionRunner.Phase.Paused, runner.CurrentPhase, "headset off: the run is paused");
            double left = controller.Module.RemainingS(controller.Clock.NowMs);
            yield return new WaitForSecondsRealtime(4.0f);
            Assert.AreEqual("induction", Opus.Games.PhantomHand.PhNames.Of(controller.Module.CurrentPhase), "the phase must not move while the headset is off");
            Assert.AreEqual(left, controller.Module.RemainingS(controller.Clock.NowMs), 0.05, "the time left in the phase must stand still while the headset is off");

            runner.SendMessage("OnApplicationPause", false);
            Assert.AreEqual(OpusSessionRunner.Phase.Running, runner.CurrentPhase, "headset on again: the run goes on");
            yield return new WaitForSecondsRealtime(2.0f);
            Assert.That(controller.Module.RemainingS(controller.Clock.NowMs), Is.InRange(left - 3.5, left - 1.0),
                "2 s after waking 2 s of the phase have passed, not also the 4 s the headset was off");

            // A pause the operator asked for is not undone by taking the headset off and putting it on.
            controller.PauseSession();
            typeof(OpusSessionRunner).GetProperty("CurrentPhase").SetValue(runner, OpusSessionRunner.Phase.Paused);
            runner.SendMessage("OnApplicationPause", true);
            runner.SendMessage("OnApplicationPause", false);
            Assert.AreEqual(OpusSessionRunner.Phase.Paused, runner.CurrentPhase, "the operator's pause must survive the headset coming off and on");

            runner.FinishSession("stopped_by_patient", upload: false);
        }

        /// <summary>The card a person sees before a run, as text and as a picture (sim/out/quest_diag/ready_card.png) for a person to look at.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator PH_Standalone_ReadyCard_SaysWhatIsMissingAndHowToStart()
        {
            yield return OpenScene();
            string body = _controller.StandbyCard.BodyText;
            StringAssert.Contains("Operator app:   not found yet  (only 127.0.0.1 is tried: it was set by hand)", body);
            StringAssert.Contains("Sleeve:   not found yet", body);
            StringAssert.Contains("Muscle sensor:   not found yet", body);
            StringAssert.Contains("works without the sleeve and the sensor", body);
            Assert.AreEqual(OpusSessionRunner.Phase.WaitingToStart, _runner.CurrentPhase, "nothing may start the run by itself");
            Picture(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")), "sim", "out", "quest_diag"), "ready_card");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator PH_Standalone_NoHubNoNodes_RunsToTheEndAndServesItsFiles()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string validatePy = Path.Combine(root, "contracts", "validate.py");
            string analyticsPy = Path.Combine(root, "analytics", ".venv", "Scripts", "python.exe");
            yield return OpenScene();
            var controller = _controller; var runner = _runner;
            int port = DeviceDiagnostics.BoundPort;
            Assert.Greater(port, 0, "the diagnostics server is not listening (is port " + DeviceDiagnostics.Port + " taken on this PC?)");
            float t0;

            Assert.AreEqual(OpusSessionRunner.Phase.WaitingToStart, runner.CurrentPhase, "nothing may start the run by itself");
            runner.StartSession("test: as the both-hands pinch does");
            yield return null;
            Assert.AreEqual(OpusSessionRunner.Phase.Running, runner.CurrentPhase, "the session did not start");
            Assert.IsFalse(controller.StandbyCard.Shown, "the standby card must go when the run starts");
            string sessionDir = runner.LastSessionDir, sessionId = runner.SessionId;

            // ---- the run, with one look through the diagnostics channel while it is going -----------------------------------
            var phasesSeen = new List<string>(); string lastPhase = null; float lastPhaseChange = Time.realtimeSinceStartup;
            bool looked = false;
            t0 = Time.realtimeSinceStartup;
            while (runner.CurrentPhase == OpusSessionRunner.Phase.Running || runner.CurrentPhase == OpusSessionRunner.Phase.Paused)
            {
                string ph = controller.Module != null ? Opus.Games.PhantomHand.PhNames.Of(controller.Module.CurrentPhase) : "?";
                if (ph != lastPhase) { lastPhase = ph; phasesSeen.Add(ph); lastPhaseChange = Time.realtimeSinceStartup; }
                Assert.Less(Time.realtimeSinceStartup - lastPhaseChange, 100f, "stuck in phase '" + ph + "' for 100 s; phases so far " + string.Join(",", phasesSeen));
                Assert.Less(Time.realtimeSinceStartup - t0, 420f, "run exceeded 7 minutes; phases so far " + string.Join(",", phasesSeen));
                if (!looked && Time.realtimeSinceStartup - t0 > 12f)
                {
                    looked = true;
                    var task = Task.Run(() =>
                    {
                        string index = Http(port, "/");
                        return new[] { index, Http(port, "/f/" + (string)JObject.Parse(index)["log"]), Http(port, "/f/opus_sessions/" + sessionId + "/events.ndjson") };
                    });
                    while (!task.IsCompleted) yield return null;
                    var paths = JObject.Parse(task.Result[0])["files"].Select(f => (string)f["path"]).ToList();
                    CollectionAssert.Contains(paths, "opus_sessions/" + sessionId + "/events.ndjson", "the running session is not in the index");
                    StringAssert.Contains("session " + sessionId + " started", task.Result[1], "the served log does not hold what Unity logged");
                    StringAssert.Contains("[health] ", task.Result[1], "no health line after 12 s");
                    StringAssert.Contains("sleeve not found, muscle sensor not found", task.Result[1], "the health line says what the scene sees");
                    StringAssert.Contains("\"block_start\"", task.Result[2], "the event file of the running session, read over HTTP");
                }
                yield return null;
            }
            Assert.IsTrue(looked, "the run ended before the diagnostics channel was read");
            Assert.AreEqual(OpusSessionRunner.Phase.Finished, runner.CurrentPhase);
            Assert.IsFalse(runner.Client != null && runner.Client.IsConnected,
                "the live link connected: a hub is running on this PC's port " + LiveClient.HubPort + ", so this was not a run without one");
            Assert.IsFalse(controller.Haptic.Connected, "a sleeve was found: this was not a run without one");
            Debug.Log("[PH_Standalone] run finished in " + (Time.realtimeSinceStartup - t0).ToString("F0") + " s; phases " + string.Join(",", phasesSeen));

            // ---- what the run left on the device ------------------------------------------------------------------------------
            var events = File.ReadAllLines(Path.Combine(sessionDir, "events.ndjson")).Where(l => l.Trim().Length > 0).Select(JObject.Parse).ToList();
            var phaseOrder = events.Where(e => (string)e["type"] == "phase_start").Select(e => (string)e["data"]["phase"]).ToList();
            var expected = new[] { "calibrate", "probe_pre", "induction", "threat", "probe_post", "questionnaire", "probe_pre", "induction", "threat", "probe_post", "questionnaire",
                                   "dissolve", "reveal", "witness" };
            CollectionAssert.AreEqual(expected, phaseOrder, "phase order");
            int strokes = events.Count(e => (string)e["type"] == "stroke");
            Assert.GreaterOrEqual(strokes, 30, "stroke events");
            var cues = events.Where(e => (string)e["type"] == "haptic_cue").ToList();
            Assert.GreaterOrEqual(cues.Count, 30, "with no sleeve every stroke is still logged as a cue");
            Assert.AreEqual(0, cues.Count(e => (bool)e["data"]["delivered"]), "no sleeve: no cue can be delivered");
            Assert.AreEqual(1, events.Count(e => (string)e["type"] == "witness_summary"), "witness_summary");
            Assert.AreEqual(3, events.Count(e => (string)e["type"] == "questionnaire_item"), "questionnaire items");
            var sessionJson = JObject.Parse(File.ReadAllText(Path.Combine(sessionDir, "session.json")));
            Assert.AreEqual("completed", (string)sessionJson["end_reason"]);
            Assert.IsTrue((bool)sessionJson["blocks"][0]["completed"]);
            Assert.GreaterOrEqual(Directory.GetFiles(sessionDir, "kin_*.json").Length, 1, "kin chunks");
            Assert.IsFalse(File.Exists(Path.Combine(sessionDir, ".uploaded")), "no hub: the session waits on the device");
            Debug.Log("[PH_Standalone] strokes=" + strokes + " cues=" + cues.Count + " (none delivered), reasons: " +
                      string.Join(", ", cues.GroupBy(e => (string)e["data"]["reason"] ?? "null").Select(g => g.Key + "=" + g.Count())) +
                      "; sens files " + Directory.GetFiles(sessionDir, "sens_*.json").Length + ", kin files " + Directory.GetFiles(sessionDir, "kin_*.json").Length);

            if (File.Exists(analyticsPy) && File.Exists(validatePy))
            {
                var local = Run(analyticsPy, "\"" + validatePy + "\" --session \"" + sessionDir + "\"", root, 60000);
                Assert.AreEqual(0, local.Item1, "the session of a run with no sleeve and no sensor failed contracts/validate.py:\n" + local.Item2 + local.Item3);
            }
            else Debug.LogWarning("[PH_Standalone] analytics venv or validate.py missing: validation skipped (logged, not silent)");

            // ---- every file of the finished session, read through the diagnostics channel, is the file on the disk -----------
            yield return new WaitForSecondsRealtime(1.5f);   // the background writers of the last chunks
            var local2 = Directory.GetFiles(sessionDir).Select(Path.GetFileName).Where(n => !n.StartsWith(".", StringComparison.Ordinal)).OrderBy(n => n).ToList();
            var fetch = Task.Run(() =>
            {
                var index = JObject.Parse(Http(port, "/"));
                var bad = new List<string>();
                foreach (string name in local2)
                {
                    string rel = "opus_sessions/" + sessionId + "/" + name;
                    var entry = index["files"].FirstOrDefault(f => (string)f["path"] == rel);
                    byte[] disk = File.ReadAllBytes(Path.Combine(sessionDir, name));
                    if (entry == null) { bad.Add(name + " is not in the index"); continue; }
                    if (!HttpBytes(port, "/f/" + rel).SequenceEqual(disk)) bad.Add(name + " differs from the file on the disk");
                }
                return bad;
            });
            while (!fetch.IsCompleted) yield return null;
            Assert.IsEmpty(fetch.Result, "session files over HTTP (" + local2.Count + " files)");
            Assert.GreaterOrEqual(local2.Count, 3, "session.json, events.ndjson and at least one kin file");
        }

        /// <summary>The standby card as a seated person sees it, as a PNG (the eye position and the render of PhantomHandSceneBuilder's shots;
        /// without a headset the rig's own eye sits on the floor).</summary>
        private static void Picture(string outDir, string name)
        {
            var card = GameObject.Find("StandbyCard");
            if (card == null) { Debug.Log("[PH_Standalone] no StandbyCard object: no picture"); return; }
            Directory.CreateDirectory(outDir);
            Canvas.ForceUpdateCanvases(); Canvas.ForceUpdateCanvases();
            var camGo = new GameObject("__StandaloneScreenshotCamera");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 1.18f, 0.02f);
                cam.transform.LookAt(card.transform.position);
                cam.fieldOfView = 70f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
                const int w = 1600, h = 900;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var prev = RenderTexture.active;
                cam.Render(); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(rt);
            }
            finally { UnityEngine.Object.DestroyImmediate(camGo); }
        }

        private static string Http(int port, string target) { return Encoding.UTF8.GetString(HttpBytes(port, target)); }

        /// <summary>One GET over a plain socket; the body. Throws when the status is not 200.</summary>
        private static byte[] HttpBytes(int port, string target)
        {
            using (var c = new TcpClient())
            {
                c.Connect("127.0.0.1", port);
                c.ReceiveTimeout = 10000;
                var s = c.GetStream();
                byte[] req = Encoding.ASCII.GetBytes("GET " + target + " HTTP/1.1\r\nHost: test\r\n\r\n");
                s.Write(req, 0, req.Length);
                var all = new MemoryStream(); var buf = new byte[65536]; int r;
                while ((r = s.Read(buf, 0, buf.Length)) > 0) all.Write(buf, 0, r);
                byte[] bytes = all.ToArray();
                int head = -1;
                for (int i = 0; i + 3 < bytes.Length; i++)
                    if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n') { head = i; break; }
                if (head < 0) throw new IOException("no HTTP head in the answer to " + target);
                string status = Encoding.ASCII.GetString(bytes, 0, head).Split('\r')[0];
                if (!status.Contains(" 200 ")) throw new IOException(target + " answered " + status);
                return bytes.Skip(head + 4).ToArray();
            }
        }

        private static Tuple<int, string, string> Run(string exe, string args, string cwd, int timeoutMs)
        {
            using (var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe, Arguments = args, WorkingDirectory = cwd, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                },
            })
            {
                p.Start();
                string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
                p.WaitForExit(timeoutMs);
                return Tuple.Create(p.HasExited ? p.ExitCode : -1, o, e);
            }
        }
    }
}
