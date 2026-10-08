using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
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
    /// Run9 step 5: end-to-end verification of the haptic pipeline (FormFeedback -> HapticClient ->
    /// UdpHapticTransport -> sim/haptic/fake_haptic.py), mirroring LiveLinkIntegrationTests' shape (real child
    /// process, real UDP loop, then contracts/validate.py on the produced session dir).
    ///
    /// Loads the dressed OrchardReach.unity scene, whose OrchardReachSceneController auto-begins a demo session
    /// (SyntheticHandDriver, no headset) that scripts an exaggerated trunk-lean window during trial 0 and a
    /// tracking-loss window during trial 1 (see OrchardReachSceneController's demoTriggerFormEvents field) so
    /// trunk_lean and low_confidence cues are both guaranteed without a headset; `success` fires naturally from
    /// any trial that actually completes with outcome=success.
    /// </summary>
    public class HapticIntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";
        private const float MaxWaitSeconds = 40f;
        private Process _sleeveProcess;
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        }

        [TearDown]
        public void TearDown() => TryStopSleeve();

        /// <summary>Run10 root-cause fix: this used to call `GameObject.Find` for the controller on the SAME
        /// line as `EditorSceneManager.LoadSceneInPlayMode`, before any frame had passed. That scene load is
        /// NOT synchronous — it only actually swaps the loaded scene (destroying the old scene's objects,
        /// running the new scene's Awake()) on a later Editor update tick, mirroring how
        /// `SceneManager.LoadScene`'s Play-mode counterpart is documented to defer the actual load — so `Find`
        /// was reliably returning the PREVIOUS test's still-alive (soon to be destroyed) controller instead of
        /// the new one. Every caller already did `yield return null; yield return null;` right after this
        /// method returned, specifically to let the scene finish loading, but by then `controller` had already
        /// captured the stale reference. Confirmed via run10_playmode7.log's per-instance diagnostics
        /// (`controllerId`/`hapticHash` logged in `ConfigureHapticsForTest`/`HandleFormSignal`): each test's
        /// `ConfigureHapticsForTest(...)` call was silently configuring the PREVIOUS test's controller, while
        /// the actual live one for THIS test kept its manifest default (`Enabled=false`) for its entire
        /// lifetime — exactly matching "sleeve discovered, FormFeedback fires the edge correctly, but
        /// cueOrder=[] anyway" from run10_playmode3 through 7. Fixed by making this a coroutine that only calls
        /// `GameObject.Find` AFTER yielding twice past the load, guaranteeing the new scene has actually
        /// finished loading (Awake() already ran) first.</summary>
        private IEnumerator LoadSceneAndGetController(Action<OrchardReachSceneController> onReady)
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;
            var go = GameObject.Find("OrchardReachSceneController");
            Assert.IsNotNull(go, "OrchardReachSceneController not found — dressing tool must run first.");
            onReady(go.GetComponent<OrchardReachSceneController>());
        }

        private Process StartFakeSleeve(string jsonLogPath)
        {
            string pythonExe = Path.Combine(_projectRoot, "sim", "haptic", ".venv", "Scripts", "python.exe");
            if (!File.Exists(pythonExe))
            {
                // Fall back to the live-sim venv if haptic has none of its own yet (both venvs carry the same
                // `jsonschema` dependency fake_haptic.py needs).
                pythonExe = Path.Combine(_projectRoot, "sim", "live", ".venv", "Scripts", "python.exe");
            }
            Assert.IsTrue(File.Exists(pythonExe), $"fake_haptic venv python not found (checked sim/haptic/.venv and sim/live/.venv) at {pythonExe}.");

            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-m haptic.fake_haptic --json-log \"{jsonLogPath}\"",
                    WorkingDirectory = Path.Combine(_projectRoot, "sim"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) Debug.Log($"[fake_haptic] {e.Data}"); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Debug.Log($"[fake_haptic:err] {e.Data}"); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _sleeveProcess = p;
            return p;
        }

        private void TryStopSleeve()
        {
            try
            {
                if (_sleeveProcess != null && !_sleeveProcess.HasExited)
                {
                    _sleeveProcess.Kill();
                    _sleeveProcess.WaitForExit(3000);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[OPUS] HapticIntegrationTests: sleeve teardown warning: {e.Message}"); }
            _sleeveProcess = null;
        }

        /// <summary>Full happy path: sleeve present + hapticsEnabled=true. Verifies cue order, send-&gt;ack
        /// latency, every cue recorded as a `haptic_cue` event, and the resulting session dir passes
        /// contracts/validate.py --session.</summary>
        [UnityTest]
        public IEnumerator DemoSession_WithSleeve_ProducesOrderedCuesAndValidSession()
        {
            StartFakeSleeve(jsonLogPath: Path.Combine(_projectRoot, "logs", "sessions", "unity_logs", "run9_haptic_sleeve.ndjson"));
            yield return new WaitForSeconds(0.5f); // let the sleeve bind + start broadcasting hello

            OrchardReachSceneController controller = null;
            yield return LoadSceneAndGetController(c => controller = c);
            // U4 follow-up: the committed scene has a real sleeve IP in hapticManualHost (bypasses discovery), so the
            // cues went to that address and the fake on this machine never saw them (no acks). Use discovery.
            controller.RebuildHapticTransportForTest(null);
            controller.ConfigureHapticsForTest(enabled: true, maxIntensity: 1.0);

            // Run11: wait for UDP discovery to actually find the sleeve BEFORE the demo's scripted cue windows
            // matter, then re-arm so the lean window starts from a known-connected state. Without this the test
            // is a coin flip: discovery is driven by the sleeve's once-per-second broadcast and has taken
            // anywhere from well under a second to over three (it picked a link-local 169.254.x.x route on one
            // run), and any cue raised before it lands is correctly dropped with "no haptic device known yet".
            // That is right behaviour for the product (cues are fire-and-forget, G10) and useless as a test.
            float waitConn = 0f;
            while (waitConn < 20f && !controller.Haptic.Connected)
            {
                yield return null;
                waitConn += Time.deltaTime;
            }
            Assert.IsTrue(controller.Haptic.Connected,
                $"fake sleeve was never discovered within {waitConn:F1}s — cannot test cue delivery against it.");
            controller.ConfigureHapticsForTest(enabled: true, maxIntensity: 1.0); // resets the demo cue-window clock

            string sessionId = Guid.NewGuid().ToString();
            string sessionsRoot = Path.Combine(_projectRoot, "logs", "sessions", "unity_logs", "run9_haptic_sessions");
            var envelope = BuildEnvelope(sessionId);
            var writer = new SessionWriter(sessionsRoot, envelope);
            var clock = new System.Diagnostics.Stopwatch();
            clock.Start();
            var recorder = new TrialRecorder(new SessionClock(), writer.EventsPath);

            var cueOrder = new List<string>();
            var sendWallClockByCueId = new Dictionary<string, double>();
            var latenciesMs = new List<double>();
            int seenTrunkLean = 0, seenLowConfidence = 0, seenSuccess = 0;

            controller.OnTrialEvent += e =>
            {
                recorder.Record(e.Block, e.Trial, e.Type, e.Hand, e.Target, e.Outcome, e.Data);
            };
            controller.OnFormSignal += sig =>
            {
                recorder.Record(0, null, "form_warning", data: new JObject { ["kind"] = sig.Kind, ["active"] = sig.Active, ["leanCm"] = sig.LeanCm });
            };
            controller.OnHapticCueRecorded += rec =>
            {
                recorder.Record(0, null, "haptic_cue", data: new JObject
                {
                    ["cue"] = rec.Cue, ["intensity"] = rec.IntensityFrac, ["delivered"] = rec.Delivered, ["cue_id"] = rec.CueId,
                });

                if (!sendWallClockByCueId.ContainsKey(rec.CueId))
                {
                    sendWallClockByCueId[rec.CueId] = clock.Elapsed.TotalMilliseconds;
                    cueOrder.Add(rec.Cue);
                    if (rec.Cue == "trunk_lean") seenTrunkLean++;
                    else if (rec.Cue == "low_confidence") seenLowConfidence++;
                    else if (rec.Cue == "success") seenSuccess++;
                }
                else if (rec.Delivered)
                {
                    double latency = clock.Elapsed.TotalMilliseconds - sendWallClockByCueId[rec.CueId];
                    latenciesMs.Add(latency);
                }
            };

            float elapsed = 0f;
            while (elapsed < MaxWaitSeconds && !(controller.TrialsCompleted >= 6 && seenTrunkLean > 0 && seenLowConfidence > 0))
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            // Give any in-flight acks a moment to land.
            yield return new WaitForSeconds(0.5f);

            Debug.Log($"[OPUS] HapticIntegrationTest: elapsed={elapsed:F1}s trialsCompleted={controller.TrialsCompleted} " +
                      $"cueOrder=[{string.Join(",", cueOrder)}] trunk_lean={seenTrunkLean} low_confidence={seenLowConfidence} success={seenSuccess}");

            Assert.Greater(seenTrunkLean, 0, "trunk_lean cue never fired — demo lean-spike trigger did not produce a cue.");
            Assert.Greater(seenLowConfidence, 0, "low_confidence cue never fired — demo tracking-loss trigger did not produce a cue.");

            int firstTrunkLean = cueOrder.IndexOf("trunk_lean");
            int firstLowConfidence = cueOrder.IndexOf("low_confidence");
            Assert.Less(firstTrunkLean, firstLowConfidence,
                "trunk_lean (trial 0's lean window) must be sent before low_confidence (trial 1's tracking-loss window), since trial index only increases.");

            Assert.IsNotEmpty(latenciesMs, "no ack round-trip latency samples were captured — the fake sleeve never acked a cue.");
            double maxLatency = latenciesMs.Max();
            double avgLatency = latenciesMs.Average();
            Debug.Log($"[OPUS] HapticIntegrationTest: send->ack latency samples={latenciesMs.Count} avg={avgLatency:F2}ms max={maxLatency:F2}ms");
            Assert.Less(maxLatency, 100.0, $"trigger->send->ack latency exceeded 100ms (max={maxLatency:F2}ms).");

            recorder.Close();
            envelope.EndedAt = DateTimeOffset.UtcNow.ToString("o");
            writer.Save();

            var validateResult = RunValidate(writer.SessionDir);
            Debug.Log($"[OPUS] HapticIntegrationTest: validate.py exit={validateResult.exitCode}\n{validateResult.stdout}\n{validateResult.stderr}");
            Assert.AreEqual(0, validateResult.exitCode, "contracts/validate.py --session reported the produced session as invalid.");

            int haticEventLines = File.ReadAllLines(writer.EventsPath).Count(l => l.Contains("\"type\":\"haptic_cue\""));
            Assert.Greater(haticEventLines, 0, "no haptic_cue lines found in events.ndjson.");
        }

        /// <summary>hapticsEnabled=false must send nothing at all, even though the same demo lean/tracking-loss
        /// triggers fire (FormFeedback itself doesn't know about the haptics gate — HapticClient does).</summary>
        [UnityTest]
        public IEnumerator DemoSession_HapticsDisabled_SendsNoCues()
        {
            OrchardReachSceneController controller = null;
            yield return LoadSceneAndGetController(c => controller = c);
            // Explicitly disabled (also the manifest default — this makes the test's intent unambiguous).
            controller.ConfigureHapticsForTest(enabled: false, maxIntensity: 1.0);

            int cueCount = 0;
            controller.OnHapticCueRecorded += _ => cueCount++;

            float elapsed = 0f;
            while (elapsed < 3f) { yield return null; elapsed += Time.deltaTime; }

            Debug.Log($"[OPUS] HapticIntegrationTest: hapticsEnabled=false -> cueCount={cueCount} after {elapsed:F1}s (expect 0)");
            Assert.AreEqual(0, cueCount, "HapticClient must not send any cue while Enabled=false.");
        }

        /// <summary>No sleeve answers at all (never started) — the session must still run to completion; a
        /// missing sleeve must never stall the game loop (UDP is fire-and-forget, per HAPTIC_PROTOCOL.md).</summary>
        [UnityTest]
        public IEnumerator DemoSession_SleeveNeverAnswers_SessionStillCompletes()
        {
            // Deliberately do NOT start fake_haptic.py.
            OrchardReachSceneController controller = null;
            yield return LoadSceneAndGetController(c => controller = c);
            controller.ConfigureHapticsForTest(enabled: true, maxIntensity: 1.0);

            float elapsed = 0f;
            while (elapsed < MaxWaitSeconds && controller.TrialsCompleted < 6)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            Debug.Log($"[OPUS] HapticIntegrationTest: sleeve absent -> trialsCompleted={controller.TrialsCompleted} after {elapsed:F1}s");
            Assert.GreaterOrEqual(controller.TrialsCompleted, 6, "session did not complete its trials — a missing/unresponsive sleeve must not stall the game loop.");
        }

        private SessionEnvelope BuildEnvelope(string sessionId) => new SessionEnvelope
        {
            SessionId = sessionId,
            ContractsVersion = "0.1",
            PatientRef = "run9-haptic-test-patient",
            ProgramRef = "run9-haptic-test-program",
            Mode = "simulation",
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
            EndedAt = DateTimeOffset.UtcNow.ToString("o"),
            EndReason = "completed",
            Device = new DeviceInfo { Model = "editor-playmode-test", DeviceId = "run9-haptic-test", Os = "windows-editor", TrackingRateHz = 72.0 },
            Versions = new VersionsInfo { Shell = "0.1.0", Sdk = "0.1.0", Games = new Dictionary<string, string> { ["orchard_reach"] = "0.1.0" } },
            Calibration = new CalibrationInfo { AffectedSide = "right", DominantSide = "right", ArmLengthM = new ArmLength { Left = 0.55, Right = 0.55 }, Posture = "seated" },
            Blocks = new List<BlockInfo>(),
        };

        private (int exitCode, string stdout, string stderr) RunValidate(string sessionDir)
        {
            string analyticsPython = Path.Combine(_projectRoot, "analytics", ".venv", "Scripts", "python.exe");
            string validatePy = Path.Combine(_projectRoot, "contracts", "validate.py");
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = analyticsPython,
                    Arguments = $"\"{validatePy}\" --session \"{sessionDir}\"",
                    WorkingDirectory = _projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            p.Start();
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            return (p.HasExited ? p.ExitCode : -1, stdout, stderr);
        }
    }
}
