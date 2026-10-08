using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>
    /// Pure-C# checks that <see cref="LiveMessage"/>/<see cref="LiveMessageFactory"/> produce the shape
    /// contracts/schemas/live-message.schema.json requires (v/type/id/seq/ts_ms/from + per-type required
    /// payload fields), and that the resume/dedup bookkeeping (LIVE_PROTOCOL.md "Reliability rules") behaves.
    /// This does not open a socket — the schema-conformance + fake_hub.py end-to-end check is done separately
    /// (see docs/agent-briefs' M6 note: sim/live/fake_hub.py, verified by the S track, 27 pytest passed).
    /// </summary>
    public class LiveMessageTests
    {
        [Test]
        public void Factory_StampsRequiredTopLevelFields()
        {
            var factory = new LiveMessageFactory("headset");
            var msg = factory.Build("hello", new JObject
            {
                ["device_id"] = "dev-1",
                ["role"] = "headset",
                ["versions"] = new JObject(),
            });

            Assert.AreEqual(1, msg.V);
            Assert.AreEqual("hello", msg.Type);
            Assert.IsNotEmpty(msg.Id);
            Assert.AreEqual("headset", msg.From);
            Assert.Greater(msg.TsMs, 0);
            Assert.AreEqual(0, msg.Seq);
        }

        [Test]
        public void Factory_SeqIncrementsPerMessage()
        {
            var factory = new LiveMessageFactory("headset");
            var a = factory.Build("ping");
            var b = factory.Build("ping");
            Assert.AreEqual(a.Seq + 1, b.Seq);
        }

        [Test]
        public void RoundTrip_JsonSerialization_PreservesShape()
        {
            var factory = new LiveMessageFactory("hub");
            var original = factory.Build("status", new JObject { ["state"] = "running" }, sessionId: "sess-1");
            var json = original.ToJson();

            StringAssert.Contains("\"v\":1", json);
            StringAssert.Contains("\"type\":\"status\"", json);
            StringAssert.Contains("\"from\":\"hub\"", json);
            StringAssert.Contains("\"session_id\":\"sess-1\"", json);

            var roundTripped = LiveMessage.FromJson(json);
            Assert.AreEqual(original.Id, roundTripped.Id);
            Assert.AreEqual(original.Seq, roundTripped.Seq);
            Assert.AreEqual("running", roundTripped.Payload["state"].Value<string>());
        }

        [Test]
        public void RequiresAck_OmittedWhenFalse_PresentWhenTrue()
        {
            var factory = new LiveMessageFactory("hub");
            var noAck = factory.Build("status", new JObject());
            var withAck = factory.Build("command", new JObject { ["command"] = "start" }, requiresAck: true);

            StringAssert.DoesNotContain("requires_ack", noAck.ToJson());
            StringAssert.Contains("\"requires_ack\":true", withAck.ToJson());
        }

        [Test]
        public void Outbox_TracksUntilAcked()
        {
            var outbox = new LiveMessageOutbox();
            var factory = new LiveMessageFactory("headset");
            var msg = factory.Build("command", requiresAck: true);

            outbox.Track(msg, nowMs: 0);
            Assert.AreEqual(1, outbox.Count);

            outbox.Ack(msg.Id);
            Assert.AreEqual(0, outbox.Count);
        }

        [Test]
        public void Outbox_TimedOutAfterOneSecond()
        {
            var outbox = new LiveMessageOutbox();
            var factory = new LiveMessageFactory("headset");
            var msg = factory.Build("command", requiresAck: true);
            outbox.Track(msg, nowMs: 0);

            Assert.AreEqual(0, outbox.TimedOut(nowMs: 500).Count, "should not be timed out before 1000ms");
            Assert.AreEqual(1, outbox.TimedOut(nowMs: 1000).Count, "should be timed out at/after 1000ms");
        }

        [Test]
        public void Outbox_ReplayFrom_FiltersBySeq()
        {
            var outbox = new LiveMessageOutbox();
            var factory = new LiveMessageFactory("headset");
            var m0 = factory.Build("trial_event");
            var m1 = factory.Build("trial_event");
            var m2 = factory.Build("trial_event");
            outbox.Track(m0, 0); outbox.Track(m1, 0); outbox.Track(m2, 0);

            var replay = outbox.ReplayFrom(resumeFromSeq: m0.Seq);
            Assert.AreEqual(2, replay.Count);
            Assert.AreEqual(m1.Id, replay[0].Id);
            Assert.AreEqual(m2.Id, replay[1].Id);

            Assert.AreEqual(3, outbox.ReplayFrom(resumeFromSeq: null).Count);
        }

        [Test]
        public void Dedup_DropsRepeatedId()
        {
            var dedup = new LiveMessageDedup();
            Assert.IsFalse(dedup.IsDuplicate("a"));
            Assert.IsTrue(dedup.IsDuplicate("a"));
            Assert.IsFalse(dedup.IsDuplicate("b"));
        }
    }
}
