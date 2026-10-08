using System.IO;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    public class KinematicsRecorderTests
    {
        /// <summary>Run16 regression. kinematics-chunk.schema.json states "each array length == t_ms length.
        /// conf 0 = not tracked", but JSON Schema cannot express a cross-array length equality, so a recorder
        /// that dropped frames for an untracked joint passed contracts/validate.py while producing files whose
        /// positions were attributed to the wrong timestamps (real session cb77b474: t_ms=157, r_wrist=28).
        /// This reproduces the exact shape that broke it: a joint present from frame 0, a joint that drops out
        /// mid-chunk, and a joint that only appears late. Every series must come out exactly t_ms-length, with
        /// the gaps marked conf 0 and the real samples still sitting on their own frame index.</summary>
        [Test]
        public void EveryJointSeriesStaysParallelToTMs_EvenWhenJointsDropOutOrStartLate()
        {
            var clock = SessionClock.Manual();
            var recorder = new KinematicsRecorder("sess-gap", clock, sessionDir: null, rateHz: 72);

            const int frames = 30;
            for (int i = 0; i < frames; i++)
            {
                clock.Advance(13.889);
                // Always present.
                recorder.Sample(clock.NowMs, OpusJoints.Head, new[] { 0.0, (double)i, 0.0 }, null, 1.0);
                // Drops out for frames 10..19 (hand left the tracking volume).
                if (i < 10 || i >= 20)
                    recorder.Sample(clock.NowMs, OpusJoints.RWrist, new[] { 1.0, (double)i, 0.0 }, null, 1.0);
                // Only ever seen from frame 25 on.
                if (i >= 25)
                    recorder.Sample(clock.NowMs, OpusJoints.LWrist, new[] { 2.0, (double)i, 0.0 }, null, 1.0);
            }
            recorder.Flush();

            var chunk = recorder.WrittenChunks[0];
            Assert.AreEqual(frames, chunk.TMs.Count, "t_ms is the authoritative frame axis");
            foreach (var kv in chunk.Frames)
            {
                Assert.AreEqual(chunk.TMs.Count, kv.Value.Pos.Count, $"{kv.Key}.pos must be t_ms-length");
                Assert.AreEqual(chunk.TMs.Count, kv.Value.Conf.Count, $"{kv.Key}.conf must be t_ms-length");
            }

            // Gaps are marked not-tracked, and real samples stay on their own frame index.
            var wrist = chunk.Frames[OpusJoints.RWrist];
            Assert.AreEqual(1.0, wrist.Conf[9], "frame 9 was really tracked");
            Assert.AreEqual(0.0, wrist.Conf[15], "frames 10..19 were a dropout and must be conf 0");
            Assert.AreEqual(1.0, wrist.Conf[25], "tracking resumed at frame 20");
            Assert.AreEqual(25.0, wrist.Pos[25][1], "the sample recorded on frame 25 must still sit at index 25");

            var late = chunk.Frames[OpusJoints.LWrist];
            Assert.AreEqual(0.0, late.Conf[0], "a joint first seen at frame 25 must be back-filled as conf 0");
            Assert.AreEqual(0.0, late.Conf[24]);
            Assert.AreEqual(1.0, late.Conf[25]);
            Assert.AreEqual(25.0, late.Pos[25][1], "its first real sample belongs at frame 25, not frame 0");
        }

        [Test]
        public void ChunkRollsOverAtFiveSeconds()
        {
            var clock = SessionClock.Manual();
            var recorder = new KinematicsRecorder("sess-1", clock, sessionDir: null, rateHz: 90);

            // 90Hz for 6s = 540 frames, ~11.11ms apart. Chunk boundary is checked after each Sample call.
            double dt = 1000.0 / 90.0;
            for (int i = 0; i < 540; i++)
            {
                clock.Advance(dt);
                recorder.Sample(clock.NowMs, OpusJoints.Head, new[] { 0.0, 0.0, 0.0 }, null, 1.0);
            }
            recorder.Flush();

            Assert.GreaterOrEqual(recorder.ChunksWritten, 2, "6s of samples at a 5s chunk size must roll over into a 2nd chunk");
            // seq must be sequential starting at 0
            for (int i = 0; i < recorder.WrittenChunks.Count; i++)
                Assert.AreEqual(i, recorder.WrittenChunks[i].Seq);
        }

        [Test]
        public void WritesChunkFileMatchingSchemaShape()
        {
            var dir = Path.Combine(Path.GetTempPath(), "opus_kin_test_" + System.Guid.NewGuid());
            try
            {
                var clock = SessionClock.Manual();
                var recorder = new KinematicsRecorder("sess-2", clock, dir, rateHz: 72, source: "synthetic");
                for (int i = 0; i < 10; i++)
                {
                    clock.Advance(13.889);
                    recorder.Sample(clock.NowMs, OpusJoints.Head, new[] { 0.1, 0.2, 0.3 }, new[] { 0.0, 0.0, 0.0, 1.0 }, 1.0);
                    recorder.Sample(clock.NowMs, OpusJoints.RWrist, new[] { 0.4, 0.5, 0.6 }, null, 0.9);
                }
                recorder.Flush();

                var file = Path.Combine(dir, "kin_000.json");
                Assert.IsTrue(File.Exists(file));
                var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                Assert.AreEqual("sess-2", (string)json["session_id"]);
                Assert.AreEqual(0, (int)json["seq"]);
                Assert.AreEqual("synthetic", (string)json["source"]);
                Assert.AreEqual(10, ((Newtonsoft.Json.Linq.JArray)json["t_ms"]).Count);
                Assert.IsNotNull(json["frames"]["head"]);
                Assert.AreEqual(10, ((Newtonsoft.Json.Linq.JArray)json["frames"]["head"]["conf"]).Count);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
