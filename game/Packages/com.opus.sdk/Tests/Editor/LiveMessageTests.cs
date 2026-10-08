using System.Net;
using System.Net.Sockets;
using System.Text;
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
        public void Outbox_TrialEvent_IsNeverTimedOut_HoweverOld()
        {
            // Regression (PH U45FIX, S1): LiveClient.Pump re-sent every tracked trial_event once per second for the whole session,
            // 84x duplicates at the hub. Trial_events are tracked for ReplayFrom only; no timer may return them.
            var outbox = new LiveMessageOutbox();
            var factory = new LiveMessageFactory("headset");
            var evt = factory.Build("trial_event", new JObject { ["type"] = "stroke" });
            Assert.IsNull(evt.RequiresAck);
            outbox.Track(evt, nowMs: 0);

            Assert.AreEqual(0, outbox.TimedOut(nowMs: 1000).Count);
            Assert.AreEqual(0, outbox.TimedOut(nowMs: 60000).Count);
            Assert.AreEqual(0, outbox.TimedOut(nowMs: 1e9).Count, "however old");
            outbox.MarkResent(evt.Id, nowMs: 5000);
            Assert.AreEqual(0, outbox.TimedOut(nowMs: 1e9).Count, "also after a caller marked it resent");
        }

        [Test]
        public void Outbox_RequiresAck_IsDueAfterOneSecond_AndAgainOnlyOneSecondAfterMarkResent()
        {
            var outbox = new LiveMessageOutbox();
            var factory = new LiveMessageFactory("headset");
            var cmd = factory.Build("command", requiresAck: true);
            outbox.Track(cmd, nowMs: 0);

            Assert.AreEqual(0, outbox.TimedOut(nowMs: 999).Count);
            Assert.AreEqual(1, outbox.TimedOut(nowMs: 1000).Count);
            outbox.MarkResent(cmd.Id, nowMs: 1000);
            Assert.AreEqual(0, outbox.TimedOut(nowMs: 1999).Count, "not again before 1000 ms after the resend");
            Assert.AreEqual(1, outbox.TimedOut(nowMs: 2000).Count);
            outbox.Ack(cmd.Id);
            Assert.AreEqual(0, outbox.TimedOut(nowMs: 99999).Count, "an acked message is never due");
        }

        [Test]
        public void Outbox_UnackedTrialEvent_IsStillReplayedOnReconnect()
        {
            var outbox = new LiveMessageOutbox();
            var factory = new LiveMessageFactory("headset");
            var cmd = factory.Build("command", requiresAck: true);
            var evt = factory.Build("trial_event");
            outbox.Track(cmd, nowMs: 0);
            outbox.Track(evt, nowMs: 0);

            Assert.AreEqual(1, outbox.TimedOut(nowMs: 5000).Count, "only the requires_ack message is ever due");
            var all = outbox.ReplayFrom(resumeFromSeq: null);
            Assert.AreEqual(2, all.Count, "a reconnect replays the trial_event as well as the unacked command");
            Assert.AreEqual(cmd.Id, all[0].Id);
            Assert.AreEqual(evt.Id, all[1].Id);
            var rest = outbox.ReplayFrom(resumeFromSeq: cmd.Seq);
            Assert.AreEqual(1, rest.Count);
            Assert.AreEqual(evt.Id, rest[0].Id);
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

    /// <summary>The optional hubPort argument (default 8787) reaches the sockets: the ws:// URI, the HTTP upload URL and the real
    /// TCP connect. The connect test runs against a local TcpListener on an ephemeral port, never on 8787.</summary>
    public class LiveClientHubPortTests
    {
        private static LiveClient Make(int? port = null) => port.HasValue
            ? new LiveClient("hubport-test", () => new JObject(), null, "127.0.0.1", port.Value)
            : new LiveClient("hubport-test", () => new JObject(), null, "127.0.0.1");

        [Test]
        public void DefaultHubPort_Is8787()
        {
            Assert.AreEqual(8787, LiveClient.HubPort);
            Assert.AreEqual(8787, Make().ActiveHubPort);
        }

        [Test]
        public void ExplicitHubPort_IsKept_AndOutOfRangeFallsBackToTheDefault()
        {
            Assert.AreEqual(40123, Make(40123).ActiveHubPort);
            Assert.AreEqual(65535, Make(65535).ActiveHubPort);
            foreach (int bad in new[] { 0, -1, 65536, 100000 }) Assert.AreEqual(8787, Make(bad).ActiveHubPort, "port " + bad);
        }

        [Test]
        public void Urls_CarryThePort()
        {
            Assert.AreEqual("ws://192.168.43.5:8787/opus/v1/live", LiveClient.BuildWsUri("192.168.43.5", LiveClient.HubPort).ToString());
            Assert.AreEqual("ws://127.0.0.1:40123/opus/v1/live", LiveClient.BuildWsUri("127.0.0.1", 40123).ToString());
            Assert.AreEqual("http://127.0.0.1:40123/opus/v1/sessions/s%201/files/sens_000.json",
                            LiveClient.BuildUploadUrl("127.0.0.1", 40123, "s 1", "sens_000.json"));
        }

        [Test]
        public void CustomHubPort_ReachesTheSocket()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var client = Make(port);
            try
            {
                client.Start();
                var accepted = listener.AcceptTcpClientAsync();
                Assert.IsTrue(accepted.Wait(10000), "the client never connected to the custom hub port " + port);
                using (var peer = accepted.Result)
                {
                    peer.ReceiveTimeout = 5000;
                    var buffer = new byte[4096];
                    int n = peer.GetStream().Read(buffer, 0, buffer.Length);
                    string request = Encoding.ASCII.GetString(buffer, 0, n);
                    StringAssert.StartsWith("GET /opus/v1/live", request);
                    StringAssert.Contains(":" + port, request, "the Host header names the custom port");
                }
            }
            finally
            {
                client.Dispose();
                listener.Stop();
            }
        }
    }
}
