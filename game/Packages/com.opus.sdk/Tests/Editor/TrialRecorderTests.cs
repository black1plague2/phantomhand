using System.IO;
using NUnit.Framework;
using Newtonsoft.Json;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    public class TrialRecorderTests
    {
        [Test]
        public void SeqIsMonotonicAndOrdered()
        {
            var clock = SessionClock.StartedAt(0);
            var recorder = new TrialRecorder(clock);
            recorder.Record(0, null, "session_start");
            recorder.Record(0, null, "block_start");
            recorder.Record(0, 0, "trial_start");
            recorder.Record(0, 0, "trial_end", outcome: "success");

            Assert.AreEqual(4, recorder.Log.Count);
            for (int i = 0; i < recorder.Log.Count; i++)
                Assert.AreEqual(i, recorder.Log[i].Seq);

            // t_ms must be non-decreasing in emission order (event ordering guarantee).
            for (int i = 1; i < recorder.Log.Count; i++)
                Assert.GreaterOrEqual(recorder.Log[i].TMs, recorder.Log[i - 1].TMs);
        }

        [Test]
        public void WritesValidNdjsonLinesToDisk()
        {
            var dir = Path.Combine(Path.GetTempPath(), "opus_trial_test_" + System.Guid.NewGuid());
            var eventsPath = Path.Combine(dir, "events.ndjson");
            try
            {
                var clock = SessionClock.StartedAt(0);
                var recorder = new TrialRecorder(clock, eventsPath);
                recorder.Record(0, null, "session_start");
                recorder.Record(0, 0, "trial_start");
                recorder.Close();

                var lines = File.ReadAllLines(eventsPath);
                Assert.AreEqual(2, lines.Length);
                foreach (var line in lines)
                {
                    var parsed = JsonConvert.DeserializeObject<TrialEvent>(line);
                    Assert.IsNotNull(parsed.Type);
                }
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ResumeFromExistingSeq_ContinuesNumbering()
        {
            var clock = SessionClock.StartedAt(0);
            var recorder = new TrialRecorder(clock, path: null, startSeq: 5);
            var evt = recorder.Record(0, null, "resume");
            Assert.AreEqual(5, evt.Seq);
            Assert.AreEqual(6, recorder.NextSeq);
        }
    }
}
