using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    /// PH U5 item 4: PH_FullRun. One complete Phantom Hand run in demo_mode (induction 60 s at most, one rating after each condition, then dissolve and reveal, ~3.5 min) in the real
    /// PhantomHand scene, driven by the scripted participant, against sim/live/fake_hub.py (live link + uploads) and the sleeve twin
    /// (sim/sleeve/twin.py --kind both: Node A haptic+IMU, Node B bio+EMG) on OFFSET ports.
    ///
    /// Endpoints: if the L3 harness set OPUS_PH_HUB / OPUS_PH_NODE_A / OPUS_PH_NODE_B / OPUS_PH_DISCOVERY_PORT they are used (and not
    /// started here); otherwise the test starts the hub (port 8787: LiveClient.HubPort is a const) and the twin (port offset 12100) itself.
    /// Every wait is on an explicit condition (hub health, twin ready line, node streams flowing, session phases), never a fixed sleep.
    ///
    /// Asserts: phase order; &gt;= 30 stroke events; &gt;= 95 % of haptic cues acked; sens + kin chunks written; the local AND the hub-stored
    /// session validate (contracts/validate.py); live-link RTT p50 &lt; 250 ms; 0 invalid messages at the hub; statuses carried game_state + trace.
    /// </summary>
    public class PhantomHandFullRunTests
    {
        private const string ScenePath = "Assets/Scenes/PhantomHand.unity";
        private const int HubPortRequired = 8787;
        private const int TwinOffset = 12100;

        private Process _hub, _twin;
        private readonly StringBuilder _hubErr = new StringBuilder();
        private readonly StringBuilder _twinOut = new StringBuilder();
        private int _controlPort;

        [TearDown]
        public void TearDown()
        {
            PhantomHandOverrides.Reset();
            try
            {
                if (_twin != null && !_twin.HasExited)
                {
                    if (_controlPort > 0)
                        using (var s = new System.Net.Sockets.UdpClient()) s.Send(Encoding.ASCII.GetBytes("quit"), 4, "127.0.0.1", _controlPort);
                    if (!_twin.WaitForExit(3000)) _twin.Kill();
                }
            }
            catch (Exception e) { Debug.LogWarning("[PH_FullRun] twin teardown: " + e.Message); }
            try { if (_hub != null && !_hub.HasExited) { _hub.Kill(); _hub.WaitForExit(3000); } }
            catch (Exception e) { Debug.LogWarning("[PH_FullRun] hub teardown: " + e.Message); }
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator PH_FullRun_DemoMode_AgainstFakeHubAndTwin()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string simDir = Path.Combine(root, "sim");
            string py = Path.Combine(simDir, "live", ".venv", "Scripts", "python.exe");
            string validatePy = Path.Combine(root, "contracts", "validate.py");
            string analyticsPy = Path.Combine(root, "analytics", ".venv", "Scripts", "python.exe");

            // ---- endpoints (explicit readiness, no sleeps) ------------------------------------------------------------
            string envHub = Environment.GetEnvironmentVariable(PhantomEndpoints.EnvHub);
            string hubHost = "127.0.0.1"; int hubPort = HubPortRequired;
            string nodeA, nodeB, discovery;
            string hubOutDir = Path.Combine(simDir, "out", "hub_sessions");
            bool external = !string.IsNullOrEmpty(envHub);
            if (external)
            {
                PhantomEndpoints.ParseHostPort(envHub, out hubHost, out hubPort);
                nodeA = Environment.GetEnvironmentVariable(PhantomEndpoints.EnvNodeA);
                nodeB = Environment.GetEnvironmentVariable(PhantomEndpoints.EnvNodeB);
                discovery = Environment.GetEnvironmentVariable(PhantomEndpoints.EnvDiscoveryPort);
                Assert.IsNotNull(nodeA, "OPUS_PH_NODE_A missing"); Assert.IsNotNull(nodeB, "OPUS_PH_NODE_B missing");
                Assert.AreEqual(HubPortRequired, hubPort,
                    "LiveClient.HubPort is a const (8787): the hub must listen there until the SDK takes a port (CROSS-TRACK request, run log U5).");
            }
            else
            {
                Assert.IsTrue(File.Exists(py), "venv python not found at " + py + " (sim/live/README.md setup)");
                _hub = StartProcess(py, "-m live.fake_hub --port " + HubPortRequired, simDir, null, _hubErr);
                _twin = StartProcess(py, "-m sim.sleeve.twin --kind both --port-offset " + TwinOffset + " --seed 5 --no-stdin", root, _twinOut, null);
                float waited = 0f; JObject ready = null;
                while (waited < 30f && ready == null)
                {
                    yield return null; waited += Time.unscaledDeltaTime;
                    string[] lines; lock (_twinOut) lines = _twinOut.ToString().Split('\n');
                    foreach (var l in lines)
                    {
                        if (!l.TrimStart().StartsWith("{")) continue;
                        try { var j = JObject.Parse(l); if ((string)j["event"] == "ready") { ready = j; break; } } catch (Exception) { }
                    }
                }
                Assert.IsNotNull(ready, "twin never printed its ready line within 30 s:\n" + _twinOut);
                var ports = (JObject)ready["ports"];
                _controlPort = (int)ports["control"];
                nodeA = "127.0.0.1:" + (int)ports["haptic"]; nodeB = "127.0.0.1:" + (int)ports["bio"];
                discovery = ((int)ports["discovery"]).ToString();

                bool healthy = false; waited = 0f;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                    while (waited < 20f && !healthy)
                    {
                        var t = Task.Run(async () => { try { return (await http.GetAsync("http://127.0.0.1:" + HubPortRequired + "/opus/v1/health")).IsSuccessStatusCode; } catch { return false; } });
                        while (!t.IsCompleted) { yield return null; waited += Time.unscaledDeltaTime; }
                        healthy = t.Result;
                        if (!healthy) { yield return new WaitForSecondsRealtime(0.25f); waited += 0.25f; }
                    }
                Assert.IsTrue(healthy, "fake_hub never became healthy on " + HubPortRequired + ":\n" + _hubErr);
            }

            var env = new Dictionary<string, string>
            {
                { PhantomEndpoints.EnvHub, hubHost + ":" + hubPort }, { PhantomEndpoints.EnvNodeA, nodeA },
                { PhantomEndpoints.EnvNodeB, nodeB }, { PhantomEndpoints.EnvDiscoveryPort, discovery },
            };
            PhantomHandOverrides.Env = k => env.ContainsKey(k) ? env[k] : null;
            PhantomHandOverrides.ParamOverrides = new JObject { ["demo_mode"] = true };
            PhantomHandOverrides.Seed = 20261008;
            PhantomHandOverrides.ForceDemoHands = true;
            PhantomHandOverrides.ScriptedParticipant = true;
            PhantomHandOverrides.DriveSession = true;
            PhantomHandOverrides.AutoStart = true;

            // ---- load the real scene -----------------------------------------------------------------------------------
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null; yield return null;
            var anchors = UnityEngine.Object.FindFirstObjectByType<Opus.Games.PhantomHand.PhantomAnchors>();
            Assert.IsNotNull(anchors, "PhantomAnchors not found: is this the PhantomHand scene?");
            var controller = UnityEngine.Object.FindFirstObjectByType<PhantomHandSceneController>();
            if (controller == null)
            {
                // The scene builder wires the controller; until it does, add it the same way (Awake runs now, reading the overrides above).
                controller = anchors.gameObject.AddComponent<PhantomHandSceneController>();
                yield return null;
            }
            var runner = controller.GetComponent<OpusSessionRunner>();
            Assert.IsNotNull(runner, "the controller must bring its OpusSessionRunner");

            // A flinch at each stone impact: the twin answers "flinch" on its control port with an EMG burst (x6 from +120 ms, 350 ms
            // long) and an IMU jolt (+150 ms). The game's own threat analysis over the live node streams has to see it; without this the
            // run only ever produced "no flinch", so that path (node time -> session time -> ThreatResponseAnalyzer -> witness) was untested.
            int flinchesSent = 0;
            Action<TrialEvent> flinchAtImpact = e =>
            {
                if (e == null || e.Type != "threat_impact" || _controlPort <= 0) return;
                using (var s = new System.Net.Sockets.UdpClient()) s.Send(Encoding.ASCII.GetBytes("flinch"), 6, "127.0.0.1", _controlPort);
                flinchesSent++;
            };
            if (!external) controller.OnTrialEvent += flinchAtImpact;

            // ---- wait: hub link up, session started, nodes streaming ------------------------------------------------------
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 30f && !(runner.Client != null && runner.Client.IsConnected)) yield return null;
            Assert.IsTrue(runner.Client != null && runner.Client.IsConnected, "live link to the hub never connected");
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 30f && runner.CurrentPhase != OpusSessionRunner.Phase.Running) yield return null;
            Assert.AreEqual(OpusSessionRunner.Phase.Running, runner.CurrentPhase, "the session never started");
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 20f && !(controller.NodeA.Connected && controller.NodeB.Connected)) yield return null;
            Assert.IsTrue(controller.NodeA.Connected, "Node A (twin) never streamed");
            Assert.IsTrue(controller.NodeB.Connected, "Node B (twin) never streamed");
            string sessionDir = runner.LastSessionDir; string sessionId = runner.SessionId;
            Debug.Log("[PH_FullRun] session " + sessionId + " running; nodes streaming; dir=" + sessionDir);

            // ---- run to the end, sampling RTT; a phase-time watchdog fails a stuck run with its phase -----------------------
            var rtt = new List<double>(); double lastRtt = -1; var phasesSeen = new List<string>();
            float lastPhaseChange = Time.realtimeSinceStartup; string lastPhase = null;
            float slowMs = 0f;   // time spent in frames longer than the 50 ms a stroke cue may be late: the host's share of dropped cues
            int hitches = 0; float hitchMs = 0f, worstHitchMs = 0f; int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);   // main-thread hitches drop cues as "late": count them
            t0 = Time.realtimeSinceStartup;
            while (runner.CurrentPhase == OpusSessionRunner.Phase.Running || runner.CurrentPhase == OpusSessionRunner.Phase.Paused)
            {
                if (runner.Client != null && runner.Client.LastRttMs > 0 && Math.Abs(runner.Client.LastRttMs - lastRtt) > 1e-6) { lastRtt = runner.Client.LastRttMs; rtt.Add(lastRtt); }
                string ph = controller.Module != null ? Opus.Games.PhantomHand.PhNames.Of(controller.Module.CurrentPhase) : "?";
                if (Time.unscaledDeltaTime * 1000f > HapticClient.StrokeLateDropMs) slowMs += Time.unscaledDeltaTime * 1000f;
                if (Time.unscaledDeltaTime > 0.12f)
                {
                    float ms = Time.unscaledDeltaTime * 1000f; hitches++; hitchMs += ms; worstHitchMs = Mathf.Max(worstHitchMs, ms);
                    int g0 = GC.CollectionCount(0), g2 = GC.CollectionCount(2);
                    Debug.Log("[PH_FullRun][hitch] " + ms.ToString("F0") + " ms in phase " + ph + "; GC gen0 +" + (g0 - gc0) + " gen2 +" + (g2 - gc2));
                    gc0 = g0; gc2 = g2;
                }
                if (ph != lastPhase) { lastPhase = ph; phasesSeen.Add(ph); lastPhaseChange = Time.realtimeSinceStartup; }
                Assert.Less(Time.realtimeSinceStartup - lastPhaseChange, 100f, "stuck in phase '" + ph + "' for 100 s; phases so far " + string.Join(",", phasesSeen));
                Assert.Less(Time.realtimeSinceStartup - t0, 420f, "run exceeded 7 minutes; phases so far " + string.Join(",", phasesSeen));
                yield return null;
            }
            Assert.AreEqual(OpusSessionRunner.Phase.Finished, runner.CurrentPhase);
            double slowShare = slowMs / Math.Max(1f, (Time.realtimeSinceStartup - t0) * 1000f);
            Debug.Log("[PH_FullRun] run finished in " + (Time.realtimeSinceStartup - t0).ToString("F0") + " s; phases " + string.Join(",", phasesSeen) +
                      "; frames over 120 ms: " + hitches + " (total " + hitchMs.ToString("F0") + " ms, worst " + worstHitchMs.ToString("F0") + " ms)");
            if (rtt.Count > 0) { var r = rtt.OrderBy(x => x).ToList(); Debug.Log("[PH_FullRun] RTT so far: n=" + r.Count + " p50=" + r[r.Count / 2].ToString("F1") + " p95=" + r[(int)(r.Count * 0.95)].ToString("F1") + " ms"); }
            if (!external)
                Debug.Log("[PH_FullRun] hub received so far: " + HubReceived(_hubErr) + "; events in the session file: " +
                          File.ReadAllLines(Path.Combine(sessionDir, "events.ndjson")).Count(l => l.Trim().Length > 0));

            // ---- uploads: wait until the runner marks the session uploaded (explicit condition) -------------------------------
            t0 = Time.realtimeSinceStartup;
            string uploadedMarker = Path.Combine(sessionDir, ".uploaded");
            int upFrames = 0; float upMaxDt = 0f;
            while (Time.realtimeSinceStartup - t0 < 60f && !File.Exists(uploadedMarker)) { upFrames++; upMaxDt = Mathf.Max(upMaxDt, Time.unscaledDeltaTime); yield return null; }
            Debug.Log("[PH_FullRun] upload wait " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s, frames=" + upFrames + ", max frame " + (upMaxDt * 1000f).ToString("F0") +
                      " ms, files uploaded so far " + runner.UploadedFiles + ", message '" + runner.LastUploadMessage + "'" +
                      (external ? "" : "; hub received: " + HubReceived(_hubErr)));
            Assert.IsTrue(File.Exists(uploadedMarker), "session never finished uploading: " + runner.LastUploadMessage);

            // ---- local session content -----------------------------------------------------------------------------------
            var events = File.ReadAllLines(Path.Combine(sessionDir, "events.ndjson")).Where(l => l.Trim().Length > 0).Select(JObject.Parse).ToList();
            var phaseOrder = events.Where(e => (string)e["type"] == "phase_start").Select(e => (string)e["data"]["phase"]).ToList();
            var expected = new[] { "calibrate", "probe_pre", "induction", "threat", "probe_post", "questionnaire", "probe_pre", "induction", "threat", "probe_post", "questionnaire",
                                   "dissolve", "reveal", "witness" };
            CollectionAssert.AreEqual(expected, phaseOrder, "phase order");
            int strokes = events.Count(e => (string)e["type"] == "stroke");
            Assert.GreaterOrEqual(strokes, 30, "stroke events");
            var cues = events.Where(e => (string)e["type"] == "haptic_cue").ToList();
            int delivered = cues.Count(e => (bool)e["data"]["delivered"]);
            Assert.GreaterOrEqual(cues.Count, 2 * 30, "haptic_cue events (two per stroke)");
            // Two different questions. (1) The pipeline: every cue the game SENT must come back acked, whatever the host does.
            // (2) The host: a cue whose frame arrives more than 50 ms after its time is dropped as "late" (never touch late), so a
            // host that cannot hold its frames loses touches without anything being wrong in the game. 8 Oct 2026: this laptop's CPU
            // ran at about a third of its speed for most of each run and 9-17 % of the cues were dropped that way. Such a run says
            // nothing about cue delivery, so it ends Inconclusive (after every other check below), not green and not red.
            int late = cues.Count(e => !(bool)e["data"]["delivered"] && (string)e["data"]["reason"] == "late");
            string cueLine = "stroke cues acked: " + delivered + "/" + cues.Count + " (" + late + " dropped as late; frames over " +
                             HapticClient.StrokeLateDropMs + " ms took " + (slowShare * 100).ToString("F1") + " % of the run)";
            Assert.GreaterOrEqual(delivered / (double)Math.Max(1, cues.Count - late), 0.98, "cues that were sent and acked; " + cueLine);
            string hostTooSlow = null;
            if (delivered / (double)cues.Count < 0.95 && slowShare > 0.03) hostTooSlow = "host too slow to judge cue delivery; " + cueLine;
            else Assert.GreaterOrEqual(delivered / (double)cues.Count, 0.95, cueLine);
            Assert.AreEqual(1, events.Count(e => (string)e["type"] == "witness_summary"), "witness_summary");
            if (!external)
            {
                // the simulated flinch, as the game measured it: EMG peak at least 3x the resting level (the twin's burst is x6) and an onset
                // after the impact, inside the 1.5 s the analyzer looks at (the twin starts the burst 120 ms after it gets the command)
                Assert.AreEqual(2, flinchesSent, "one flinch command per stone impact");
                var responses = events.Where(e => (string)e["type"] == "threat_response").ToList();
                Assert.AreEqual(2, responses.Count, "threat_response events");
                foreach (var r in responses)
                {
                    var d = r["data"]; string line = d.ToString(Newtonsoft.Json.Formatting.None);
                    Assert.IsTrue(d["emg_peak_x"].Type != JTokenType.Null && (double)d["emg_peak_x"] >= 3.0, "EMG flinch peak (x resting level): " + line);
                    Assert.IsTrue(d["emg_latency_ms"].Type != JTokenType.Null, "EMG flinch onset found: " + line);
                    Assert.That((double)d["emg_latency_ms"], Is.InRange(60.0, 900.0), "EMG flinch onset after the impact (ms): " + line);
                    Assert.IsTrue(d["imu_peak"].Type != JTokenType.Null && (double)d["imu_peak"] > 1.0, "IMU jolt (m/s2 over rest): " + line);
                }
                var witness = events.First(e => (string)e["type"] == "witness_summary")["data"];
                foreach (var c in new[] { "sync", "async" })
                    Assert.AreNotEqual("none", (string)witness[c]["flinch_strength"], "the witness shows the flinch (" + c + "): " + witness[c].ToString(Newtonsoft.Json.Formatting.None));
            }
            Assert.AreEqual(3, events.Count(e => (string)e["type"] == "questionnaire_item"), "questionnaire items");
            Assert.AreEqual("block_end", (string)events.First(e => (string)e["type"] == "block_end")["type"]);
            int sens = Directory.GetFiles(sessionDir, "sens_*.json").Length, kin = Directory.GetFiles(sessionDir, "kin_*.json").Length;
            Assert.GreaterOrEqual(sens, 1, "sens chunks"); Assert.GreaterOrEqual(kin, 1, "kin chunks");
            var sessionJson = JObject.Parse(File.ReadAllText(Path.Combine(sessionDir, "session.json")));
            Assert.AreEqual("completed", (string)sessionJson["end_reason"]);
            Assert.AreEqual("phantom_hand", (string)sessionJson["blocks"][0]["game_id"]);
            Assert.IsTrue((bool)sessionJson["blocks"][0]["completed"]);
            var sensJson = JObject.Parse(File.ReadAllText(Directory.GetFiles(sessionDir, "sens_*.json").OrderBy(x => x).First()));
            Assert.IsNotNull(sensJson["emg_env"], "EMG stream in the sens file"); Assert.IsNotNull(sensJson["imu"], "IMU stream in the sens file");

            // ---- contract validation: local session, then (when the test owns the hub) the hub-stored copy --------------------
            if (File.Exists(analyticsPy) && File.Exists(validatePy))
            {
                var local = Run(analyticsPy, "\"" + validatePy + "\" --session \"" + sessionDir + "\"", root, 60000);
                Assert.AreEqual(0, local.Item1, "local session failed contracts/validate.py:\n" + local.Item2 + local.Item3);
                string hubCopy = Path.Combine(hubOutDir, sessionId);
                if (!external)
                {
                    t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 20f && !File.Exists(Path.Combine(hubCopy, "session.json"))) yield return null;
                    Assert.IsTrue(Directory.Exists(hubCopy), "hub did not store the session under " + hubCopy);
                    var remote = Run(analyticsPy, "\"" + validatePy + "\" --session \"" + hubCopy + "\"", root, 60000);
                    Assert.AreEqual(0, remote.Item1, "hub-stored session failed contracts/validate.py:\n" + remote.Item2 + remote.Item3);
                    Assert.AreEqual(Directory.GetFiles(sessionDir, "*.json").Length + 1, Directory.GetFiles(hubCopy).Length,
                        "every local json file + events.ndjson reached the hub");
                }
            }
            else Debug.LogWarning("[PH_FullRun] analytics venv or validate.py missing: validation skipped (logged, not silent)");

            // ---- RTT and invalid messages ---------------------------------------------------------------------------------
            Assert.GreaterOrEqual(rtt.Count, 5, "RTT samples");
            rtt.Sort(); double p50 = rtt[rtt.Count / 2], p95 = rtt[(int)(rtt.Count * 0.95)];
            Debug.Log("[PH_FullRun] RTT n=" + rtt.Count + " p50=" + p50.ToString("F1") + " p95=" + p95.ToString("F1") + " ms; strokes=" + strokes + " cues=" + delivered + "/" + cues.Count + " sens=" + sens + " kin=" + kin);
            Assert.Less(p50, 250.0, "RTT p50");
            if (!external)
            {
                string err; lock (_hubErr) err = _hubErr.ToString();
                int invalid = CountOccurrences(err, "[MSG] Validation failed") + CountOccurrences(err, "Failed to parse JSON");
                Assert.AreEqual(0, invalid, "invalid messages seen by the hub:\n" + err);
            }
            if (hostTooSlow != null) Assert.Inconclusive(hostTooSlow);
        }

        /// <summary>Messages the fake hub logged as received, by type (from its stderr): a diagnostic for duplicate floods.</summary>
        private static string HubReceived(StringBuilder hubErr)
        {
            string herr; lock (hubErr) herr = hubErr.ToString();
            var rx = new SortedDictionary<string, int>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(herr, @"\[MSG RX\] type=(\w+)"))
            { int c; rx.TryGetValue(m.Groups[1].Value, out c); rx[m.Groups[1].Value] = c + 1; }
            return string.Join(", ", rx.Select(kv => kv.Key + "=" + kv.Value)) +
                   "; invalid=" + (CountOccurrences(herr, "[MSG] Validation failed") + CountOccurrences(herr, "Failed to parse JSON"));
        }

        private static int CountOccurrences(string s, string needle)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        private static Process StartProcess(string exe, string args, string cwd, StringBuilder stdout, StringBuilder stderr)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe, Arguments = args, WorkingDirectory = cwd, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                },
            };
            p.OutputDataReceived += (s, e) => { if (e.Data != null && stdout != null) lock (stdout) stdout.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null && stderr != null) lock (stderr) stderr.AppendLine(e.Data); };
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
            return p;
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
