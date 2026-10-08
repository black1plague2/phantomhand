using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Games.OrchardReach;
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
    /// Run12: the whole pipeline in one run, as GOAL.md's north star describes it — clinician app hub, a
    /// headset session driven by hand tracking, raw kinematics AND trial events recorded, the session handed to
    /// the hub, and the result contract-valid and analysable.
    ///
    /// Deliberately different from <see cref="LiveLinkIntegrationTests"/> and <see cref="HapticIntegrationTests"/>:
    /// those each start their own Python peer as a child process, which proves one leg at a time. This test
    /// starts NOTHING. It attaches to peers already running outside Unity — the real Flutter hub
    /// (`dart run tool/hub_cli.dart`) on 8787 and the sleeve simulator on 8790 — so all three processes are
    /// genuinely talking to each other at the same time, which is the one thing every previous run had not done.
    /// If a peer is absent the test says so plainly and skips rather than silently proving less than it claims.
    ///
    /// What it records: a full session directory (session.json + events.ndjson + kin_###.json chunks) written
    /// by the same production code paths a real headset session uses, then uploaded to the hub over the live
    /// link, then validated with contracts/validate.py --session. Analytics runs separately over the hub's
    /// stored copy (see the run log) — this test's job is to produce a session worth analysing.
    /// </summary>
    public class FullPipelineIntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";
        private const int HubPort = 8787;   // LiveClient.HubPort is a hardcoded const; the hub must match.
        private const float MaxWaitSeconds = 60f;

        private string _projectRoot;

        [SetUp]
        public void SetUp() => _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [UnityTest]
        public IEnumerator FullPipeline_AppHub_Headset_Sleeve_ProducesAnalysableSession()
        {
            // --- peers must already be running; this test starts neither ---
            if (!IsPortOpen("127.0.0.1", HubPort))
            {
                Assert.Ignore($"No hub is listening on 127.0.0.1:{HubPort}. Start the Flutter hub first: " +
                              "`dart run tool/hub_cli.dart --port 8787 --auto-drive` from app/. " +
                              "This test deliberately does not start its own hub — its whole purpose is to run " +
                              "against the real app.");
            }
            Debug.Log($"[OPUS] FullPipeline: found a hub listening on 127.0.0.1:{HubPort}");

            OrchardReachSceneController controller = null;
            yield return LoadSceneAndGetController(c => controller = c);
            controller.ConfigureHapticsForTest(enabled: true, maxIntensity: 0.8);

            // Wait for the sleeve so haptic cues are actually delivered rather than correctly-but-uselessly
            // dropped as undeliverable (see run11: cue gating is fire-and-forget by design, G10).
            float waitConn = 0f;
            while (waitConn < 20f && !controller.Haptic.Connected)
            {
                yield return null;
                waitConn += Time.deltaTime;
            }
            bool sleevePresent = controller.Haptic.Connected;
            Debug.Log($"[OPUS] FullPipeline: haptic sleeve connected={sleevePresent} after {waitConn:F1}s " +
                      (sleevePresent ? "" : "(start it with `python -m haptic.fake_haptic` from sim/ to include the sleeve leg)"));
            if (sleevePresent) controller.ConfigureHapticsForTest(enabled: true, maxIntensity: 0.8); // reset the demo cue clock

            // --- session recording: the real SessionWriter/TrialRecorder/KinematicsRecorder paths ---
            string sessionId = Guid.NewGuid().ToString();
            string sessionsRoot = Path.Combine(_projectRoot, "logs", "sessions", "unity_logs", "run12_full_pipeline");
            string sessionDir = Path.Combine(sessionsRoot, sessionId);
            var writer = new SessionWriter(sessionsRoot, BuildEnvelope(sessionId));
            // One clock for the whole session (see OrchardReachSceneController.Clock). Using a fresh
            // SessionClock here put events and kinematics on different origins and made every analytics metric
            // null/invalid even though every file validated.
            var recorder = new TrialRecorder(controller.Clock, writer.EventsPath);
            controller.StartKinematicsRecording(sessionId, sessionDir, rateHz: 72.0);

            int trialEvents = 0, hapticCues = 0, formWarnings = 0;
            var cueOrder = new List<string>();

            controller.OnTrialEvent += e =>
            {
                recorder.Record(e.Block, e.Trial, e.Type, e.Hand, e.Target, e.Outcome, e.Data);
                trialEvents++;
            };
            controller.OnFormSignal += sig =>
            {
                recorder.Record(0, null, "form_warning", data: new JObject { ["kind"] = sig.Kind, ["active"] = sig.Active, ["leanCm"] = sig.LeanCm });
                formWarnings++;
            };
            controller.OnHapticCueRecorded += rec =>
            {
                recorder.Record(0, null, "haptic_cue", data: new JObject
                {
                    ["cue"] = rec.Cue, ["intensity"] = rec.IntensityFrac, ["delivered"] = rec.Delivered, ["cue_id"] = rec.CueId,
                });
                hapticCues++;
                if (!cueOrder.Contains(rec.Cue)) cueOrder.Add(rec.Cue);
            };

            // --- live link to the app's hub, in parallel with the session running ---
            var rttSamplesMs = new List<double>();
            var client = new LiveClient(
                deviceId: "run12-full-pipeline",
                versionsProvider: () => new JObject { ["shell"] = "0.1.0", ["sdk"] = "0.1.0" },
                gamesProvider: () => new JArray(new JObject { ["id"] = "orchard_reach", ["version"] = "0.2.0" }),
                manualHost: "127.0.0.1");

            bool hubSaidStart = false;
            client.OnCommand += (command, cmdParams, ackId) =>
            {
                Debug.Log($"[OPUS] FullPipeline: hub command '{command}' ackId={ackId}");
                client.AckCommand(ackId, ok: true);
                if (command == "start") { client.NotifySessionStarted(sessionId); hubSaidStart = true; }
            };
            client.Start();

            string outDir = Path.Combine(_projectRoot, "logs", "sessions", "screens", "unity", "run12");
            Directory.CreateDirectory(outDir);
            bool shotReach = false, shotGrasp = false, shotPlaced = false;

            float elapsed = 0f;
            double lastRtt = -1;
            int statusSeq = 0;
            while (elapsed < MaxWaitSeconds && controller.TrialsCompleted < 6)
            {
                client.Pump(elapsed * 1000.0);

                // Stream status to the clinician's hub about twice a second, as a real session does.
                if (elapsed * 2 >= statusSeq)
                {
                    client.SendStatus(new JObject
                    {
                        ["state"] = "running",
                        ["trials_completed"] = controller.TrialsCompleted,
                        ["haptic_cues"] = hapticCues,
                    }, elapsed * 1000.0);
                    statusSeq++;
                }
                if (client.LastRttMs > 0 && Math.Abs(client.LastRttMs - lastRtt) > 0.001)
                {
                    lastRtt = client.LastRttMs;
                    rttSamplesMs.Add(client.LastRttMs);
                }

                // Screenshots of the states a reviewer actually wants to see.
                var state = controller.CurrentTrialState;
                if (!shotReach && state == TrialState.MovementOnset) { CaptureShot(outDir, "01_mid_reach"); shotReach = true; }
                // Run13 (Opus review of run12's 02_grasp.png: hand hovering over the basket, no apple visible
                // anywhere): the ORIGINAL check fired on the very first frame state==Grasped, which is exactly the
                // frame DriveDemoHandAndFruit snaps the hand onto the apple's position before parenting it — the
                // apple sits concentric with the hand mesh, rendered behind/inside it, not visibly "grasped". Wait
                // for the carry glide to have visibly progressed (HoldGlideElapsedMs, comfortably inside the
                // Grasped/Holding window: HoldMs default 300 ms, HoldGlideDurationMs 900 ms) so hand+apple have
                // separated from the tree and are unmistakably both on screen, still well before release/detach.
                if (!shotGrasp && (state == TrialState.Grasped || state == TrialState.Holding) &&
                    controller.HoldGlideElapsedMs >= 350.0)
                {
                    CaptureShot(outDir, "02_grasp");
                    shotGrasp = true;
                }
                if (!shotPlaced && controller.TrialsCompleted >= 1) { CaptureShot(outDir, "03_placed"); shotPlaced = true; }

                yield return null;
                elapsed += Time.deltaTime;
            }

            int chunks = controller.StopKinematicsRecording();
            recorder.Close();
            writer.Save();

            Debug.Log($"[OPUS] FullPipeline: session {sessionId} finished after {elapsed:F1}s — " +
                      $"trialsCompleted={controller.TrialsCompleted} trialEvents={trialEvents} formWarnings={formWarnings} " +
                      $"hapticCues={hapticCues} cueOrder=[{string.Join(",", cueOrder)}] kinChunks={chunks} hubSaidStart={hubSaidStart}");

            Assert.GreaterOrEqual(controller.TrialsCompleted, 6, "the demo session did not complete its 6 trials.");
            Assert.Greater(trialEvents, 0, "no trial events were recorded.");
            Assert.Greater(chunks, 0, "no kinematics chunks were written — the session is not analysable, which is " +
                                      "the exact pipeline gap this test exists to catch.");

            // --- hand the finished session to the hub, the way the shell does at session end ---
            var uploads = new List<string> { "session.json", "events.ndjson" };
            uploads.AddRange(Directory.GetFiles(sessionDir, "kin_*.json").Select(Path.GetFileName));
            foreach (var name in uploads)
            {
                var task = client.UploadFileAsync(sessionId, name, Path.Combine(sessionDir, name));
                while (!task.IsCompleted) { client.Pump(elapsed * 1000.0); yield return null; }
                Assert.IsTrue(task.Result, $"upload of {name} to the hub failed.");
            }
            Debug.Log($"[OPUS] FullPipeline: uploaded {uploads.Count} files to the hub ({string.Join(", ", uploads)})");

            client.Dispose();
            yield return new WaitForSeconds(1.5f);

            if (rttSamplesMs.Count > 0)
            {
                rttSamplesMs.Sort();
                double p50 = rttSamplesMs[rttSamplesMs.Count / 2];
                double p95 = rttSamplesMs[Math.Min(rttSamplesMs.Count - 1, (int)(rttSamplesMs.Count * 0.95))];
                Debug.Log($"[OPUS] FullPipeline: live-link RTT to the app hub — samples={rttSamplesMs.Count} p50={p50:F1}ms p95={p95:F1}ms max={rttSamplesMs[^1]:F1}ms (G2 target < 250 ms on LAN)");
            }

            // --- the recorded session must be contract-valid ---
            var (code, stdout, stderr) = RunValidate(sessionDir);
            Debug.Log($"[OPUS] FullPipeline: contracts/validate.py --session exit={code}\n{stdout}\n{stderr}");
            Assert.AreEqual(0, code, $"the recorded session is not contract-valid:\n{stdout}\n{stderr}");

            Debug.Log($"[OPUS] FullPipeline: session dir for analytics -> {sessionDir}");
        }

        private static bool IsPortOpen(string host, int port)
        {
            try
            {
                using var c = new System.Net.Sockets.TcpClient();
                return c.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(2)) && c.Connected;
            }
            catch { return false; }
        }

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
            Assert.IsNotNull(go, "OrchardReachSceneController not found — the dressing tool must run first.");
            onReady(go.GetComponent<OrchardReachSceneController>());
        }

        /// <summary>Same seated-eye capture the audit test uses (CenterEyeAnchor pose, its FOV, 1920x1080).</summary>
        private static void CaptureShot(string outDir, string name)
        {
            var centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye == null) return;

            var camGo = new GameObject($"__FullPipelineCamera_{name}");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(centerEye.transform.position, centerEye.transform.rotation);
                var existing = centerEye.GetComponent<Camera>();
                cam.fieldOfView = existing != null ? existing.fieldOfView : 90f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.Skybox;

                const int w = 1920, h = 1080;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var prev = RenderTexture.active;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;

                var path = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"[OPUS] FullPipeline: wrote {path}");
                UnityEngine.Object.Destroy(tex);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            finally { UnityEngine.Object.Destroy(camGo); }
        }

        private SessionEnvelope BuildEnvelope(string sessionId) => new SessionEnvelope
        {
            SessionId = sessionId,
            ContractsVersion = "0.1",
            PatientRef = "run12-pipeline-patient",
            ProgramRef = "run12-pipeline-program",
            Mode = "simulation",
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
            EndedAt = DateTimeOffset.UtcNow.ToString("o"),
            EndReason = "completed",
            Device = new DeviceInfo { Model = "editor-playmode-test", DeviceId = "run12-full-pipeline", Os = "windows-editor", TrackingRateHz = 72.0 },
            Versions = new VersionsInfo { Shell = "0.1.0", Sdk = "0.1.0", Games = new Dictionary<string, string> { ["orchard_reach"] = "0.2.0" } },
            Calibration = new CalibrationInfo { AffectedSide = "right", DominantSide = "right", ArmLengthM = new ArmLength { Left = 0.55, Right = 0.55 }, Posture = "seated" },
            Blocks = new List<BlockInfo>(),
        };

        private (int exitCode, string stdout, string stderr) RunValidate(string sessionDir)
        {
            string python = Path.Combine(_projectRoot, "analytics", ".venv", "Scripts", "python.exe");
            string validatePy = Path.Combine(_projectRoot, "contracts", "validate.py");
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = $"\"{validatePy}\" --session \"{sessionDir}\"",
                    WorkingDirectory = _projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            p.Start();
            string so = p.StandardOutput.ReadToEnd();
            string se = p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            return (p.ExitCode, so, se);
        }
    }
}
