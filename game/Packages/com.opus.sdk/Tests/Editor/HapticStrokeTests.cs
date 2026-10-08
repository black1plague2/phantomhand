using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>HAPTIC_PROTOCOL v1.2: stroke cues, OLED display text and the keepalive, against the fake transport
    /// and a controllable clock (the nowMs passed to Pump).</summary>
    public class HapticStrokeTests
    {
        private static HapticClient Make(out FakeHapticTransport t, double maxIntensity = 1.0)
        {
            t = new FakeHapticTransport();
            return new HapticClient(t) { Enabled = true, MaxIntensity = maxIntensity };
        }

        private static List<JObject> Parsed(FakeHapticTransport t) => t.Sent.Select(s => JObject.Parse(s)).ToList();
        private static List<JObject> OfType(FakeHapticTransport t, string type) =>
            Parsed(t).Where(o => o["type"]?.Value<string>() == type).ToList();
        private static List<JObject> Strokes(FakeHapticTransport t) =>
            Parsed(t).Where(o => o["cue"]?.Value<string>() == "stroke").ToList();

        [Test]
        public void Mapper_Stroke_Is150TimesMaxIntensityCappedAt150()
        {
            Assert.AreEqual(150, HapticCueMapper.Stroke(0, 1.0, "s").Intensity255);
            Assert.AreEqual(75, HapticCueMapper.Stroke(0, 0.5, "s").Intensity255);
            Assert.AreEqual(150, HapticCueMapper.Stroke(1, 5.0, "s").Intensity255, "ceiling above 1.0 is clamped");
            Assert.AreEqual(0, HapticCueMapper.Stroke(1, -1, "s").Intensity255);
            var c = HapticCueMapper.Stroke(1, 1.0, "stroke_3");
            Assert.AreEqual(1, c.Motor);
            Assert.AreEqual(200, c.DurationMs);
            Assert.AreEqual("pulse", c.Pattern);
            Assert.AreEqual("stroke", c.Cue);
            Assert.AreEqual("stroke_3", c.CueId);
        }

        [Test]
        public void Stroke_IsSentAtPlayAtMinusLead_WithinOnePump_WithWireFields()
        {
            var c = Make(out var t);
            string id = c.ScheduleStroke(0, playAtSessionMs: 1000, leadMs: 40);

            c.Pump(959);
            Assert.IsEmpty(t.Sent, "nothing before playAt - lead");
            c.Pump(960);
            var s = Strokes(t);
            Assert.AreEqual(1, s.Count, "sent inside the Pump that reaches playAt - lead");
            Assert.AreEqual(id, s[0]["cue_id"].Value<string>());
            Assert.AreEqual(0, s[0]["motor"].Value<int>());
            Assert.AreEqual(150, s[0]["intensity"].Value<int>());
            Assert.AreEqual(200, s[0]["duration_ms"].Value<int>());
            Assert.AreEqual("pulse", s[0]["pattern"].Value<string>());
            Assert.AreEqual(1000, s[0]["play_at_ms"].Value<long>(), "play_at_ms carries the intended landing time");
            Assert.IsFalse(s[0].ContainsKey("type"), "device-level command: no v1 envelope type");
        }

        [Test]
        public void Stroke_CueIdsAreSequential_AndCustomIdIsKept()
        {
            var c = Make(out _);
            Assert.AreEqual("stroke_0", c.ScheduleStroke(0, 1000, 0));
            Assert.AreEqual("stroke_1", c.ScheduleStroke(1, 1100, 0));
            Assert.AreEqual("stroke_17", c.ScheduleStroke(0, 2000, 0, "stroke_17"));
        }

        [Test]
        public void Stroke_ScaledByMaxIntensity()
        {
            var c = Make(out var t, maxIntensity: 0.5);
            c.ScheduleStroke(1, 100, 0);
            c.Pump(100);
            Assert.AreEqual(75, Strokes(t)[0]["intensity"].Value<int>());
        }

        [Test]
        public void Stroke_InvalidMotor_Throws()
        {
            var c = Make(out _);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => c.ScheduleStroke(2, 100, 0));
        }

        [Test]
        public void Stroke_MoreThan50msLate_IsDroppedAsLate_ExactlyAt50IsSent()
        {
            var c = Make(out var t);
            var recs = new List<HapticCueRecord>();
            c.OnCueRecorded += recs.Add;

            c.ScheduleStroke(0, 1000, 40);   // send time 960
            c.Pump(1010);                    // 50 ms late: still sent
            Assert.AreEqual(1, Strokes(t).Count);

            c.ScheduleStroke(1, 2000, 40);   // send time 1960
            c.Pump(2011);                    // 51 ms late: dropped
            Assert.AreEqual(1, Strokes(t).Count, "no second send");
            var late = recs.Last();
            Assert.IsFalse(late.Delivered);
            Assert.AreEqual("late", late.Reason);
            Assert.IsNull(late.SentMs);
            Assert.AreEqual(1960, late.ScheduledMs);
            Assert.AreEqual(1, late.Motor);
        }

        [Test]
        public void Stroke_MinGap250PerMotor()
        {
            var c = Make(out var t);
            var recs = new List<HapticCueRecord>();
            c.OnCueRecorded += recs.Add;
            c.ScheduleStroke(0, 1000, 0);
            c.ScheduleStroke(0, 1200, 0);   // 200 ms after the previous on motor 0
            c.ScheduleStroke(1, 1210, 0);   // other motor: not gated by motor 0
            c.ScheduleStroke(0, 1250, 0);   // exactly 250 ms: allowed

            for (double now = 1000; now <= 1300; now += 10) c.Pump(now);

            Assert.AreEqual(3, Strokes(t).Count);
            var dropped = recs.Where(r => r.Reason == "motor_gap").ToList();
            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(1200, dropped[0].PlayAtMs);
        }

        [Test]
        public void Stroke_AtMost4PerRollingSecond()
        {
            var c = Make(out var t);
            var recs = new List<HapticCueRecord>();
            c.OnCueRecorded += recs.Add;
            // alternating motors keep every per-motor gap >= 250 ms, so only the 4/s cap can bite
            c.ScheduleStroke(0, 0, 0);
            c.ScheduleStroke(1, 100, 0);
            c.ScheduleStroke(0, 250, 0);
            c.ScheduleStroke(1, 350, 0);
            c.ScheduleStroke(0, 500, 0);    // 5th inside one second
            c.ScheduleStroke(0, 1000, 0);   // the first send (t=0) has left the 1 s window

            for (double now = 0; now <= 1000; now += 10) c.Pump(now);

            Assert.AreEqual(5, Strokes(t).Count);
            Assert.AreEqual(1, recs.Count(r => r.Reason == "rate_cap"));
            Assert.AreEqual(500, recs.First(r => r.Reason == "rate_cap").PlayAtMs);
        }

        [Test]
        public void Stroke_DoesNotConsumeTheClinicalCueBudget()
        {
            var c = Make(out var t);
            c.ScheduleStroke(0, 0, 0); c.ScheduleStroke(1, 100, 0); c.ScheduleStroke(0, 250, 0); c.ScheduleStroke(1, 350, 0);
            for (double now = 0; now <= 400; now += 10) c.Pump(now);
            Assert.AreEqual(4, Strokes(t).Count);

            Assert.IsTrue(c.SendTrunkLean(2, nowMs: 400), "trunk_lean keeps its own 2/s and 800 ms rules");
        }

        [Test]
        public void Stroke_WhenDisabledOrNoDevice_IsRecordedNotSent()
        {
            var c = Make(out var t);
            var recs = new List<HapticCueRecord>();
            c.OnCueRecorded += recs.Add;
            c.Enabled = false;
            c.ScheduleStroke(0, 100, 0);
            c.Pump(100);
            c.Enabled = true;
            t.HasDevice = false;
            c.ScheduleStroke(1, 200, 0);
            c.Pump(200);

            Assert.IsEmpty(t.Sent);
            Assert.AreEqual(new[] { "disabled", "no_device" }, recs.Select(r => r.Reason).ToArray());
            Assert.IsTrue(recs.All(r => !r.Delivered));
        }

        [Test]
        public void Stroke_Record_HasMotorScheduledSentAndAckLatency()
        {
            var clock = SessionClock.Manual(0);
            var c = Make(out var t);
            c.Clock = () => clock.NowMs;
            var recs = new List<HapticCueRecord>();
            c.OnCueRecorded += recs.Add;

            string id = c.ScheduleStroke(1, playAtSessionMs: 1000, leadMs: 40);
            clock.Advance(962);
            c.Pump(clock.NowMs);
            Assert.AreEqual(1, recs.Count);
            var r = recs[0];
            Assert.AreEqual("stroke", r.Cue);
            Assert.AreEqual(1, r.Motor);
            Assert.AreEqual(960, r.ScheduledMs);
            Assert.AreEqual(962, r.SentMs);
            Assert.AreEqual(1000, r.PlayAtMs);
            Assert.IsFalse(r.Delivered);

            clock.Advance(12);
            t.Receive(new JObject { ["type"] = "ack", ["cue_id"] = id, ["status"] = "accepted", ["ok"] = true }.ToString());

            Assert.AreEqual(2, recs.Count, "second raise when the ack lands");
            Assert.IsTrue(r.Delivered);
            Assert.AreEqual(12.0, r.AckLatencyMs.Value, 1e-9);
            Assert.IsNull(r.Reason);
        }

        [Test]
        public void Stroke_RejectedAck_KeepsErrorCodeAsReason()
        {
            var c = Make(out var t);
            HapticCueRecord seen = null;
            c.OnCueRecorded += r => seen = r;
            string id = c.ScheduleStroke(0, 100, 0);
            c.Pump(100);
            t.Receive(new JObject { ["type"] = "ack", ["cue_id"] = id, ["status"] = "rejected", ["ok"] = false, ["error_code"] = "DUTY_CYCLE_LIMIT" }.ToString());
            Assert.IsFalse(seen.Delivered);
            Assert.AreEqual("DUTY_CYCLE_LIMIT", seen.Reason);
        }

        [Test]
        public void Watchdog_StaysSilentDuringA90sStrokeTrain_AndFires2sAfterTheLastStroke()
        {
            var c = Make(out var t);
            for (int i = 0; i < 90; i++) c.ScheduleStroke(i % 2, 1000.0 * (i + 1), 40);   // last send at 89960
            double lastSend = 89960;

            double now = 0;
            for (; now <= lastSend + 1990; now += 20) c.Pump(now);
            Assert.AreEqual(90, Strokes(t).Count);
            Assert.IsEmpty(OfType(t, "stop"), "strokes refresh the watchdog, no stop during or within 2 s after the train");

            for (; now <= lastSend + 2100; now += 20) c.Pump(now);
            Assert.AreEqual(1, OfType(t, "stop").Count, "fires once, just after the 2 s idle");
        }

        [Test]
        public void Watchdog_DoesNotCancelStrokesStillQueued()
        {
            var c = Make(out var t);
            c.SendTrunkLean(2, nowMs: 0);
            c.ScheduleStroke(0, 5000, 0);
            c.Pump(2100);
            Assert.AreEqual(1, OfType(t, "stop").Count);
            Assert.AreEqual(1, c.PendingStrokeCount, "the idle watchdog is not an abort of upcoming strokes");
            c.Pump(5000);
            Assert.AreEqual(1, Strokes(t).Count);
        }

        [Test]
        public void Stop_CancelsPendingStrokes_AndRecordsThem()
        {
            var c = Make(out var t);
            var recs = new List<HapticCueRecord>();
            c.OnCueRecorded += recs.Add;
            c.ScheduleStroke(0, 1000, 0);
            c.ScheduleStroke(1, 2000, 0);
            c.Stop();
            c.Pump(3000);
            Assert.AreEqual(0, Strokes(t).Count);
            Assert.AreEqual(2, recs.Count(r => r.Reason == "cancelled"));
            Assert.AreEqual(1, OfType(t, "stop").Count);
        }

        [Test]
        public void Keepalive_SendsPingAndSubscribeOncePerSecond()
        {
            var c = Make(out var t);
            c.StartKeepalive(0);
            for (double now = 0; now < 10000; now += 50) c.Pump(now);

            Assert.AreEqual(10, OfType(t, "ping").Count);
            Assert.AreEqual(10, OfType(t, "subscribe").Count);
            Assert.AreEqual(1, OfType(t, "ping")[0]["v"].Value<int>());
            Assert.IsEmpty(OfType(t, "stop"), "keepalive alone never triggers the cue watchdog");

            c.StopKeepalive();
            int before = t.Sent.Count;
            for (double now = 10000; now < 12000; now += 50) c.Pump(now);
            Assert.AreEqual(before, t.Sent.Count);
        }

        [Test]
        public void Keepalive_DoesNotRefreshTheCueWatchdog()
        {
            var c = Make(out var t);
            c.StartKeepalive(0);
            c.SendTrunkLean(2, nowMs: 0);
            for (double now = 0; now <= 2100; now += 50) c.Pump(now);
            Assert.AreEqual(1, OfType(t, "stop").Count, "pings keep the firmware watchdog fed; the software cue watchdog still reports idle motors");
        }

        [Test]
        public void Keepalive_SkippedWhileNoDeviceIsKnown()
        {
            var c = Make(out var t);
            t.HasDevice = false;
            c.StartKeepalive(0);
            for (double now = 0; now < 3000; now += 100) c.Pump(now);
            Assert.IsEmpty(t.Sent);
        }

        [Test]
        public void Display_IsTruncatedTo12PrintableAsciiChars()
        {
            var c = Make(out var t);
            Assert.IsTrue(c.SendDisplay("ASYNC CONDITION LONG", 0));
            var o = OfType(t, "display")[0];
            Assert.AreEqual("ASYNC CONDIT", o["text"].Value<string>());
            Assert.AreEqual(12, o["text"].Value<string>().Length);

            Assert.IsTrue(c.SendDisplay("SéYN\tC\n", 600));
            Assert.AreEqual("SYNC", OfType(t, "display")[1]["text"].Value<string>(), "non-printable and non-ASCII characters are dropped");
        }

        [Test]
        public void Display_AtMostTwoPerRollingSecond_EmptyMeansIdle()
        {
            var c = Make(out var t);
            Assert.IsTrue(c.SendDisplay("SYNC", 0));
            Assert.IsTrue(c.SendDisplay("ASYNC", 100));
            Assert.IsFalse(c.SendDisplay("X", 200), "third inside one second is refused");
            Assert.IsTrue(c.SendDisplay("", 1000), "t=0 has left the window; empty text is passed through (device shows IDLE)");
            Assert.IsFalse(c.SendDisplay("X", 1050));
            Assert.AreEqual(3, OfType(t, "display").Count);
            Assert.AreEqual("", OfType(t, "display")[2]["text"].Value<string>());
        }

        [Test]
        public void Display_NotSentWithoutADevice_AndWorksWhenHapticsAreDisabled()
        {
            var c = Make(out var t);
            c.Enabled = false;
            Assert.IsTrue(c.SendDisplay("SYNC", 0));
            t.HasDevice = false;
            Assert.IsFalse(c.SendDisplay("SYNC", 5000));
            Assert.AreEqual(1, t.Sent.Count);
        }
    }
}
