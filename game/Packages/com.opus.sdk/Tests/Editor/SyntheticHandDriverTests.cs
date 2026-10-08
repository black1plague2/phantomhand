using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    public class SyntheticHandDriverTests
    {
        private static string FixtureDir()
        {
            // Repo layout: game/Packages/com.opus.sdk/Tests/Editor/<this file> -> ../../../../../../contracts/fixtures/sessions/healthy
            var dir = new DirectoryInfo(UnityEngine.Application.dataPath).Parent.Parent.FullName; // .../OPUS
            return Path.Combine(dir, "contracts", "fixtures", "sessions", "healthy");
        }

        [Test]
        public void ReplayMatchesRecordedInput_NoDroppedOrDuplicatedFrames()
        {
            var fixtureDir = FixtureDir();
            Assume.That(Directory.Exists(fixtureDir), "fixture dir must exist at " + fixtureDir);
            var chunkFiles = Directory.GetFiles(fixtureDir, "kin_*.json").OrderBy(f => f).ToArray();
            Assume.That(chunkFiles.Length, Is.GreaterThan(0));

            var driver = new SyntheticHandDriver(chunkFiles);
            var clock = SessionClock.Manual();
            var outDir = Path.Combine(Path.GetTempPath(), "opus_u5_replay_" + Guid.NewGuid());
            var recorder = new KinematicsRecorder("replay-session", clock, outDir, rateHz: 72, source: "replay");
            var sampler = new KinematicsSampler(recorder, driver);

            int framesSampled = 0;
            while (driver.AdvanceFrame())
            {
                clock.Advance(13.889);
                sampler.SampleFrame(clock.NowMs);
                framesSampled++;
            }
            recorder.Flush();

            Assert.AreEqual(driver.FrameCount, framesSampled, "every input frame must be replayed exactly once (no dropped/duplicated frames)");

            // Compare the re-recorded head trajectory against the original fixture's, position error < 1mm.
            var originalChunk = Newtonsoft.Json.JsonConvert.DeserializeObject<KinematicsChunk>(File.ReadAllText(chunkFiles[0]));
            var originalHeadPos = originalChunk.Frames[OpusJoints.Head].Pos;
            var rerecorded = recorder.WrittenChunks[0].Frames[OpusJoints.Head].Pos;

            int compareCount = Math.Min(originalHeadPos.Count, rerecorded.Count);
            Assert.Greater(compareCount, 0);
            for (int i = 0; i < compareCount; i++)
            {
                var orig = originalHeadPos[i];
                var re = rerecorded[i];
                double err = Math.Sqrt(Math.Pow(orig[0] - re[0], 2) + Math.Pow(orig[1] - re[1], 2) + Math.Pow(orig[2] - re[2], 2));
                Assert.Less(err, 0.001, $"frame {i}: replay position error {err * 1000:F3}mm exceeds 1mm");
            }

            Directory.Delete(outDir, true);
        }

        /// <summary>Builds a complete, schema-shaped session directory from a replayed fixture (SDK code only —
        /// TrialRecorder + KinematicsRecorder + SessionWriter) at a fixed, printed path, so the agent session can
        /// run `contracts/validate.py --session &lt;dir&gt;` against it outside Unity as U5's acceptance evidence.</summary>
        [Test]
        public void BuildsFullSessionDirectory_ForExternalSchemaValidation()
        {
            var fixtureDir = FixtureDir();
            Assume.That(Directory.Exists(fixtureDir));
            var chunkFiles = Directory.GetFiles(fixtureDir, "kin_*.json").OrderBy(f => f).ToArray();

            string sessionId = "11111111-1111-4111-8111-111111111111";
            var sessionsRoot = Path.Combine(Path.GetTempPath(), "opus_u5_sessions_root");
            var outDir = Path.Combine(sessionsRoot, sessionId); // matches what SessionWriter below will use
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var clock = SessionClock.Manual();
            var driver = new SyntheticHandDriver(chunkFiles);
            var recorder = new KinematicsRecorder(sessionId, clock, outDir, rateHz: 72, source: "replay");
            var sampler = new KinematicsSampler(recorder, driver);
            var trialRecorder = new TrialRecorder(clock, Path.Combine(outDir, "events.ndjson"));

            trialRecorder.Record(0, null, "session_start");
            trialRecorder.Record(0, null, "block_start");
            int trial = 0;
            trialRecorder.Record(0, trial, "trial_start");
            trialRecorder.Record(0, trial, "target_shown");

            while (driver.AdvanceFrame())
            {
                clock.Advance(13.889);
                sampler.SampleFrame(clock.NowMs);
            }
            recorder.Flush();

            trialRecorder.Record(0, trial, "trial_end", outcome: "success");
            trialRecorder.Record(0, null, "block_end");
            trialRecorder.Record(0, null, "session_end");
            trialRecorder.Close();

            var envelope = new SessionEnvelope
            {
                SessionId = sessionId,
                PatientRef = "u5-replay-test",
                ProgramRef = "u5-replay-test-program",
                Mode = "simulation",
                StartedAt = "2026-09-14T00:00:00Z",
                EndedAt = "2026-09-14T00:05:00Z",
                EndReason = "completed",
                Device = new DeviceInfo { Model = "synthetic-replay", TrackingRateHz = 72 },
                Versions = new VersionsInfo { Shell = "0.0.0-sim", Sdk = "0.1.0" },
                Calibration = new CalibrationInfo
                {
                    AffectedSide = "right",
                    DominantSide = "right",
                    ArmLengthM = new ArmLength { Left = 0.6, Right = 0.6 },
                    Posture = "seated",
                    ChestReference = new double[] { 0, 0, 0 },
                },
                Blocks = new System.Collections.Generic.List<BlockInfo>
                {
                    new BlockInfo { Index = 0, GameId = "orchard_reach", GameVersion = "0.1.0", Params = new { trialCount = 1 }, Completed = true }
                },
                Chunks = recorder.ChunksWritten,
            };
            var writer = new SessionWriter(sessionsRoot, envelope); // writes to sessionsRoot/<sessionId> == outDir
            writer.Save();

            Assert.AreEqual(outDir, writer.SessionDir);
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "session.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "events.ndjson")));
            Assert.IsTrue(Directory.GetFiles(outDir, "kin_*.json").Length > 0);

            UnityEngine.Debug.Log($"OPUS_U5_SESSION_DIR={outDir}");
        }
    }
}
