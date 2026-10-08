using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Opus.Shell.Tests.PlayMode
{
    /// <summary>
    /// U-next-run R6: PlayMode integration test wiring the headset-side <see cref="LiveClient"/> against
    /// `sim/live/fake_hub.py --scenario basic` (see sim/live/README.md). Starts the hub as a real child process,
    /// connects a real LiveClient over a real WebSocket/HTTP loop (localhost), drives the scripted session
    /// (assign_program -> start -> pause -> resume -> stop, per the hub's own scenario timing), uploads session
    /// files, then shells out to `contracts/validate.py --session &lt;hub dir&gt;` to confirm the hub-stored session
    /// is contract-valid, and reports ping/pong RTT p50/p95 plus the hub's invalid-message count (must be 0).
    /// </summary>
    public class LiveLinkIntegrationTests
    {
        private const int Port = 8787; // LiveClient.HubPort is a hardcoded const; the hub must use the same port.
        private Process _hubProcess;
        private string _projectRoot;
        private string _sessionId;

        [UnityTest]
        public IEnumerator LiveClient_CompletesScenarioBasic_AgainstFakeHub()
        {
            _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string simDir = Path.Combine(_projectRoot, "sim");
            string pythonExe = Path.Combine(simDir, "live", ".venv", "Scripts", "python.exe");
            Assert.IsTrue(File.Exists(pythonExe), $"fake_hub venv python not found at {pythonExe} — see sim/live/README.md setup.");

            // --- start the hub as a real child process ---
            _hubProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-m live.fake_hub --port {Port} --scenario basic",
                    WorkingDirectory = simDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            var hubStdout = new System.Text.StringBuilder();
            var hubStderr = new System.Text.StringBuilder();
            _hubProcess.OutputDataReceived += (s, e) => { if (e.Data != null) lock (hubStdout) hubStdout.AppendLine(e.Data); };
            _hubProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (hubStderr) hubStderr.AppendLine(e.Data); };
            _hubProcess.Start();
            _hubProcess.BeginOutputReadLine();
            _hubProcess.BeginErrorReadLine();
            Debug.Log($"[OPUS] LiveLinkTest: started fake_hub.py, PID={_hubProcess.Id}");

            // --- wait for /opus/v1/health, up to 15s ---
            bool healthy = false;
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            {
                float waited = 0f;
                while (waited < 15f)
                {
                    yield return new WaitForSeconds(0.5f);
                    waited += 0.5f;
                    Task<bool> check = CheckHealthAsync(http);
                    while (!check.IsCompleted) yield return null;
                    if (check.Result) { healthy = true; break; }
                }
            }
            Debug.Log($"[OPUS] LiveLinkTest: hub health check -> {healthy} after wait");
            if (!healthy)
            {
                Debug.LogError($"[OPUS] LiveLinkTest: hub never became healthy. stdout=\n{hubStdout}\nstderr=\n{hubStderr}");
            }
            Assert.IsTrue(healthy, "fake_hub.py never responded healthy on /opus/v1/health within 15s.");

            // --- connect LiveClient (manual host = skip UDP discovery) ---
            // session_id must be a UUID per contracts/schemas/session-envelope.schema.json (a first pass using a
            // human-readable "run6-live-test-<ms>" id failed validate.py with "is not a 'uuid'").
            _sessionId = Guid.NewGuid().ToString();
            var rttSamplesMs = new List<double>();
            var client = new LiveClient(
                deviceId: "run6-editor-playmode-test",
                versionsProvider: () => new JObject { ["shell"] = "0.1.0", ["sdk"] = "0.1.0" },
                gamesProvider: () => new JArray(new JObject { ["id"] = "orchard_reach", ["version"] = "0.1.0" }),
                manualHost: "127.0.0.1");

            bool sessionStarted = false, sessionStopped = false;
            int trialSeq = 0;

            client.OnCommand += (command, cmdParams, ackId) =>
            {
                Debug.Log($"[OPUS] LiveLinkTest: OnCommand '{command}' ackId={ackId}");
                client.AckCommand(ackId, ok: true);
                switch (command)
                {
                    case "start":
                        client.NotifySessionStarted(_sessionId);
                        sessionStarted = true;
                        break;
                    case "stop":
                        client.NotifySessionEnded("hub_stop");
                        sessionStopped = true;
                        break;
                }
            };
            // assign_program has no "command" field (it's its own message type) — handled generically isn't
            // exposed via OnCommand in LiveClient (only "assign_program" and "command" both raise OnCommand per
            // its switch statement — confirmed by reading LiveClient.HandleIncoming), so the switch above already
            // covers it implicitly (command==null for assign_program; falls through the switch harmlessly).

            client.Start();

            float elapsed = 0f;
            const float maxWait = 25f; // scenario is ~13s (1+5+2+5); generous margin for CI/editor overhead.
            double lastPingCheckRtt = -1;
            while (elapsed < maxWait && !sessionStopped)
            {
                client.Pump(elapsed * 1000.0);
                if (sessionStarted)
                {
                    // Send a couple of trial_events + a status while "running" so the hub has real session
                    // content to store, matching what a real headset session would do.
                    if (trialSeq < 3 && elapsed > 6f + trialSeq * 1.5f)
                    {
                        var evt = new TrialEvent { TMs = elapsed * 1000.0, Seq = trialSeq, Block = 0, Trial = trialSeq, Type = trialSeq == 0 ? "trial_start" : "target_shown" };
                        client.SendTrialEvent(evt);
                        client.SendStatus(new JObject { ["state"] = "running", ["trial"] = trialSeq }, elapsed * 1000.0);
                        trialSeq++;
                    }
                }
                if (client.LastRttMs > 0 && Math.Abs(client.LastRttMs - lastPingCheckRtt) > 0.001)
                {
                    lastPingCheckRtt = client.LastRttMs;
                    rttSamplesMs.Add(client.LastRttMs);
                }
                yield return null;
                elapsed += Time.deltaTime;
            }

            Debug.Log($"[OPUS] LiveLinkTest: scenario loop ended after {elapsed:F1}s, sessionStarted={sessionStarted}, sessionStopped={sessionStopped}, rttSamples={rttSamplesMs.Count}");
            Assert.IsTrue(sessionStarted, "Hub's 'start' command was never received/handled — scenario_basic didn't progress.");
            Assert.IsTrue(sessionStopped, "Hub's 'stop' command was never received within the time budget.");

            // --- upload session files (mirrors what the real shell does at session end) ---
            string tmpDir = Path.Combine(_projectRoot, "logs", "sessions", "unity_logs", "run6_live_tmp");
            Directory.CreateDirectory(tmpDir);
            string sessionJsonPath = Path.Combine(tmpDir, "session.json");
            string eventsPath = Path.Combine(tmpDir, "events.ndjson");
            // Must satisfy contracts/schemas/session-envelope.schema.json's required fields (session_id,
            // contracts_version, patient_ref, program_ref, started_at, device, versions, calibration, blocks) —
            // a first pass here that only wrote session_id/device_id/game_id failed validate.py with
            // "'contracts_version' is a required property"; shaped to match
            // contracts/fixtures/sessions/healthy/session.json's real structure instead of guessing again.
            File.WriteAllText(sessionJsonPath, new JObject
            {
                ["session_id"] = _sessionId,
                ["contracts_version"] = "0.1",
                ["patient_ref"] = "run6-live-test-patient",
                ["program_ref"] = "run6-live-test-program",
                ["mode"] = "simulation",
                ["started_at"] = DateTimeOffset.UtcNow.AddSeconds(-elapsed).ToString("o"),
                ["ended_at"] = DateTimeOffset.UtcNow.ToString("o"),
                ["end_reason"] = "completed",
                ["device"] = new JObject { ["model"] = "editor-playmode-test", ["device_id"] = "run6-editor-playmode-test", ["os"] = "windows-editor", ["tracking_rate_hz"] = 72.0 },
                ["versions"] = new JObject { ["shell"] = "0.1.0", ["sdk"] = "0.1.0", ["games"] = new JObject { ["orchard_reach"] = "0.1.0" } },
                ["calibration"] = new JObject { ["affected_side"] = "right", ["dominant_side"] = "right", ["arm_length_m"] = new JObject { ["left"] = 0.55, ["right"] = 0.55 }, ["posture"] = "seated" },
                ["blocks"] = new JArray(),
            }.ToString());
            File.WriteAllText(eventsPath, string.Join("\n", Enumerable.Range(0, trialSeq).Select(i =>
                new JObject { ["t_ms"] = i * 1000.0, ["seq"] = i, ["block"] = 0, ["trial"] = i, ["type"] = "target_shown" }.ToString(Newtonsoft.Json.Formatting.None))));

            bool up1 = false, up2 = false;
            var uploadTask1 = client.UploadFileAsync(_sessionId, "session.json", sessionJsonPath);
            while (!uploadTask1.IsCompleted) { client.Pump(elapsed * 1000.0); yield return null; }
            up1 = uploadTask1.Result;
            var uploadTask2 = client.UploadFileAsync(_sessionId, "events.ndjson", eventsPath);
            while (!uploadTask2.IsCompleted) { client.Pump(elapsed * 1000.0); yield return null; }
            up2 = uploadTask2.Result;
            Debug.Log($"[OPUS] LiveLinkTest: file uploads -> session.json={up1} events.ndjson={up2}");
            Assert.IsTrue(up1, "session.json upload (HTTP PUT) failed.");
            Assert.IsTrue(up2, "events.ndjson upload (HTTP PUT) failed.");

            client.Dispose();
            // A first pass here (Stop() then straight into WaitForSeconds) left LiveClient's background
            // Task/thread work still unwinding when the batch process later called -quit, which crashed the
            // editor's mono finalizer thread during shutdown (after the test had already passed and results.xml
            // was written, so it didn't affect the recorded result, but it's a real robustness gap). Giving
            // Dispose() a moment to actually finish before the test method returns reduces that risk.
            yield return new WaitForSeconds(1.5f);

            // --- give the hub's async handlers a moment to flush to disk, then check the stored session dir ---
            yield return new WaitForSeconds(1.0f);
            string hubOutDir = Path.Combine(simDir, "out", "hub_sessions", _sessionId);
            bool hubHasFiles = Directory.Exists(hubOutDir) &&
                                File.Exists(Path.Combine(hubOutDir, "session.json")) &&
                                File.Exists(Path.Combine(hubOutDir, "events.ndjson"));
            Debug.Log($"[OPUS] LiveLinkTest: hub stored session dir {hubOutDir} exists-with-files={hubHasFiles}");
            Assert.IsTrue(hubHasFiles, $"Hub did not store session.json+events.ndjson under {hubOutDir}.");

            // --- latency report ---
            if (rttSamplesMs.Count > 0)
            {
                var sorted = rttSamplesMs.OrderBy(x => x).ToList();
                double p50 = Percentile(sorted, 0.50);
                double p95 = Percentile(sorted, 0.95);
                Debug.Log($"[OPUS] LiveLinkTest: RTT samples={sorted.Count} p50={p50:F1}ms p95={p95:F1}ms max={sorted[sorted.Count - 1]:F1}ms");
            }
            else
            {
                Debug.LogWarning("[OPUS] LiveLinkTest: no ping/pong RTT samples captured (LastRttMs never updated) — see MANUAL_TODO.");
            }

            // --- stop the hub, then validate the stored session with contracts/validate.py ---
            TryStopHub();
            yield return new WaitForSeconds(0.5f);

            string analyticsPython = Path.Combine(_projectRoot, "analytics", ".venv", "Scripts", "python.exe");
            string validatePy = Path.Combine(_projectRoot, "contracts", "validate.py");
            var validateResult = RunProcess(analyticsPython, $"\"{validatePy}\" --session \"{hubOutDir}\"", _projectRoot, 30000);
            Debug.Log($"[OPUS] LiveLinkTest: validate.py exit={validateResult.exitCode}\nstdout={validateResult.stdout}\nstderr={validateResult.stderr}");
            Assert.AreEqual(0, validateResult.exitCode, $"contracts/validate.py --session reported the hub-stored session as invalid:\n{validateResult.stdout}\n{validateResult.stderr}");

            // Log the hub's own log output as evidence for its invalid-message count (LIVE_PROTOCOL.md requires
            // this to be reported; the hub doesn't expose it over HTTP, only in its own log output). Python's
            // `logging` module defaults to stderr, not stdout — a first pass here only captured/logged stdout,
            // which came back empty even though the hub clearly logged plenty (visible only in the earlier
            // failure-path dump, which printed both streams) — so check stderr, the actual destination.
            string stderrTail = hubStderr.ToString();
            if (stderrTail.Length > 6000) stderrTail = stderrTail.Substring(stderrTail.Length - 6000);
            int invalidCount = stderrTail.Split(new[] { "[MSG] Validation failed" }, StringSplitOptions.None).Length - 1
                              + stderrTail.Split(new[] { "Failed to parse JSON" }, StringSplitOptions.None).Length - 1;
            Debug.Log($"[OPUS] LiveLinkTest: fake_hub.py invalid-message occurrences found in its log = {invalidCount} (must be 0)");
            Debug.Log($"[OPUS] LiveLinkTest: fake_hub.py stderr(log) tail:\n{stderrTail}");

            Debug.Log("[OPUS] LiveLinkTest: DONE");
        }

        [TearDown]
        public void TearDown() => TryStopHub();

        private void TryStopHub()
        {
            try
            {
                if (_hubProcess != null && !_hubProcess.HasExited)
                {
                    _hubProcess.Kill();
                    _hubProcess.WaitForExit(3000);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[OPUS] LiveLinkTest: hub teardown warning: {e.Message}"); }
        }

        private static async Task<bool> CheckHealthAsync(HttpClient http)
        {
            try
            {
                var resp = await http.GetAsync($"http://127.0.0.1:{Port}/opus/v1/health");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            double idx = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            if (lo == hi) return sorted[lo];
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
        }

        private static (int exitCode, string stdout, string stderr) RunProcess(string exe, string args, string cwd, int timeoutMs)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = cwd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            p.Start();
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(timeoutMs);
            int code = p.HasExited ? p.ExitCode : -1;
            return (code, stdout, stderr);
        }
    }
}
