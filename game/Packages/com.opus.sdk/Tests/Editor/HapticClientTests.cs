using System;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>A no-network fake transport so HapticClient's gating/rate-limit/watchdog logic is testable in
    /// EditMode without a real UDP socket. Records every JSON string handed to <see cref="Send"/> and lets the
    /// test inject inbound messages (acks/status) via <see cref="Receive"/>.</summary>
    internal sealed class FakeHapticTransport : IHapticTransport
    {
        public readonly List<string> Sent = new List<string>();
        public bool HasDevice { get; set; } = true;
        public event Action<string> OnMessage;
        public event Action OnDeviceKnown;

        public void Start() { }
        public void Stop() { }
        public void Send(string json) => Sent.Add(json);
        public void Receive(string json) => OnMessage?.Invoke(json);
        public void Dispose() { }
    }

    public class HapticClientTests
    {
        private static JObject Last(FakeHapticTransport t) => JObject.Parse(t.Sent[t.Sent.Count - 1]);

        [Test]
        public void Disabled_NeverSendsACue()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = false, MaxIntensity = 1.0 };

            bool sent = c.SendTrunkLean(10, nowMs: 0);

            Assert.IsFalse(sent);
            Assert.IsEmpty(t.Sent);
        }

        [Test]
        public void Enabled_NoDeviceKnown_DoesNotSend()
        {
            var t = new FakeHapticTransport { HasDevice = false };
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            bool sent = c.SendTrunkLean(10, nowMs: 0);

            Assert.IsFalse(sent);
            Assert.IsEmpty(t.Sent);
        }

        [Test]
        public void TrunkLean_SendsDeviceCommandShape()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            bool sent = c.SendTrunkLean(excessLeanCm: 3, nowMs: 0);

            Assert.IsTrue(sent);
            Assert.AreEqual(1, t.Sent.Count);
            var msg = Last(t);
            Assert.AreEqual(0, msg["motor"].Value<int>());
            Assert.AreEqual("pulse", msg["pattern"].Value<string>());
            Assert.AreEqual(300, msg["duration_ms"].Value<int>());
            Assert.IsFalse(msg.ContainsKey("type"), "device-level cue commands must NOT carry the v1 envelope 'type' field");
        }

        [Test]
        public void MinGapPerZone_800ms_SuppressesRepeatOnSameMotor()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            Assert.IsTrue(c.SendTrunkLean(3, nowMs: 0));
            Assert.IsFalse(c.SendTrunkLean(3, nowMs: 500), "must be suppressed: < 800ms since the last cue on motor 0");
            Assert.IsTrue(c.SendTrunkLean(3, nowMs: 900), "must be allowed: > 800ms since the last cue on motor 0");

            Assert.AreEqual(2, t.Sent.Count);
        }

        [Test]
        public void MinGapPerZone_IsPerMotor_NotGlobal()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            Assert.IsTrue(c.SendTrunkLean(3, nowMs: 0));       // motor 0
            Assert.IsTrue(c.SendLowConfidence(nowMs: 100));    // motor 1, different zone -> not gated by motor 0's timer
        }

        [Test]
        public void GlobalRateCap_2PerSecond_DropsExcessAcrossZones()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            // Two different zones so the per-zone gap never blocks these, only the global 2/s cap should.
            Assert.IsTrue(c.SendTrunkLean(3, nowMs: 0));        // 1st in window
            Assert.IsTrue(c.SendLowConfidence(nowMs: 10));      // 2nd in window
            Assert.IsFalse(c.SendTrunkLean(3, nowMs: 20), "a 3rd cue within the trailing 1000ms must be dropped by the global cap");

            Assert.IsTrue(c.SendTrunkLean(3, nowMs: 1050), "outside the trailing-1000ms window, sends resume");
        }

        [Test]
        public void Success_PreemptsThePerZoneMinGap()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            Assert.IsTrue(c.SendTrunkLean(3, nowMs: 0)); // uses motor 0's gap timer
            Assert.IsTrue(c.SendSuccess(nowMs: 50), "success must preempt trunk_lean's motor-0 min-gap");
        }

        [Test]
        public void LowConfidence_SendsTwoPulsesForDoubleTap()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            c.SendLowConfidence(nowMs: 0);
            Assert.AreEqual(1, t.Sent.Count, "first pulse sent immediately; second must be scheduled, not inline");

            c.Pump(119);
            Assert.AreEqual(1, t.Sent.Count, "second pulse must not fire before the documented 120ms gap");

            c.Pump(120);
            Assert.AreEqual(2, t.Sent.Count, "second pulse fires once nowMs reaches the 120ms gap (non-blocking, via Pump)");
            Assert.AreEqual(1, JObject.Parse(t.Sent[0])["motor"].Value<int>());
            Assert.AreEqual(1, JObject.Parse(t.Sent[1])["motor"].Value<int>());
        }

        [Test]
        public void Ack_MarksCueRecordDelivered()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };
            HapticCueRecord seen = null;
            c.OnCueRecorded += r => seen = r;

            c.SendTrunkLean(3, nowMs: 0);
            var sentMsg = Last(t);
            string cueId = sentMsg["cue_id"].Value<string>();
            Assert.IsFalse(seen.Delivered);

            t.Receive(new JObject { ["type"] = "ack", ["ack_id"] = cueId, ["ok"] = true }.ToString());

            Assert.IsTrue(seen.Delivered);
        }

        [Test]
        public void Watchdog_SendsStopAfter2sIdle()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            c.SendTrunkLean(3, nowMs: 0);
            t.Sent.Clear();

            c.Pump(1999);
            Assert.IsEmpty(t.Sent, "must not stop before 2000ms idle");

            c.Pump(2001);
            Assert.AreEqual(1, t.Sent.Count);
            Assert.AreEqual("stop", Last(t)["type"].Value<string>());
        }

        [Test]
        public void Watchdog_DoesNotRepeatStopEveryFrame()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            c.SendTrunkLean(3, nowMs: 0);
            c.Pump(2001);
            c.Pump(3000);
            c.Pump(4000);

            int stopCount = t.Sent.FindAll(s => JObject.Parse(s)["type"]?.Value<string>() == "stop").Count;
            Assert.AreEqual(1, stopCount);
        }

        [Test]
        public void StatusMessage_UpdatesExposedState()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = true, MaxIntensity = 1.0 };

            t.Receive(new JObject
            {
                ["type"] = "status", ["device_id"] = "sleeve-01",
                ["battery_pct"] = 77.0, ["motors_ok"] = true, ["imu_ok"] = false, ["fw"] = "0.1.0",
            }.ToString());

            Assert.AreEqual("sleeve-01", c.DeviceId);
            Assert.AreEqual(77.0, c.BatteryPct);
            Assert.IsTrue(c.MotorsOk);
            Assert.IsFalse(c.ImuOk);
        }

        [Test]
        public void Stop_IsAllowedEvenWhenDisabled()
        {
            var t = new FakeHapticTransport();
            var c = new HapticClient(t) { Enabled = false };

            c.Stop();

            Assert.AreEqual(1, t.Sent.Count);
            Assert.AreEqual("stop", Last(t)["type"].Value<string>());
        }
    }
}
