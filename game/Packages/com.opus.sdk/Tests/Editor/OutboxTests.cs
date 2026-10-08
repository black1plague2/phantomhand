using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    public class OutboxTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "opus_outbox_test_" + System.Guid.NewGuid());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public async Task FlushUploadsPendingAndMarksAcked()
        {
            File.WriteAllText(Path.Combine(_dir, "session.json"), "{}");
            var outbox = new Outbox(_dir, "sess-1");
            outbox.Enqueue("session.json");

            var transport = new MockTransport();
            int uploaded = await outbox.FlushAsync(transport);

            Assert.AreEqual(1, uploaded);
            Assert.AreEqual(1, transport.Sent.Count);
            Assert.AreEqual(0, outbox.PendingEntries.Count);
        }

        [Test]
        public async Task FailedUpload_StaysPendingForRetry()
        {
            File.WriteAllText(Path.Combine(_dir, "events.ndjson"), "{}");
            var outbox = new Outbox(_dir, "sess-1");
            outbox.Enqueue("events.ndjson");

            var transport = new MockTransport { FailNext = true };
            int uploaded = await outbox.FlushAsync(transport);

            Assert.AreEqual(0, uploaded);
            Assert.AreEqual(1, outbox.PendingEntries.Count);
            Assert.AreEqual(1, outbox.PendingEntries[0].Attempts);
        }

        [Test]
        public async Task Resume_AfterCrash_PicksUpUnackedFilesAndSkipsAcked()
        {
            // Simulate a prior run: session.json got acked, kin_000.json was written but never acked before "crash".
            File.WriteAllText(Path.Combine(_dir, "session.json"), "{}");
            File.WriteAllText(Path.Combine(_dir, "kin_000.json"), "{}");
            var first = new Outbox(_dir, "sess-1");
            first.Enqueue("session.json");
            first.Enqueue("kin_000.json");
            var transport = new MockTransport();
            await first.FlushAsync(transport); // both upload successfully once

            // New kin chunk appears after the "crash" (written by the recorder before the process died).
            File.WriteAllText(Path.Combine(_dir, "kin_001.json"), "{}");

            var resumed = Outbox.Resume(_dir, "sess-1");
            Assert.AreEqual(1, resumed.PendingEntries.Count, "only the new, never-acked chunk should be pending");
            Assert.AreEqual("kin_001.json", resumed.PendingEntries[0].FileName);

            var transport2 = new MockTransport();
            int uploaded = await resumed.FlushAsync(transport2);
            Assert.AreEqual(1, uploaded);
            CollectionAssert.DoesNotContain(transport2.Sent.ConvertAll(t => t.fileName), "session.json");
        }
    }
}
