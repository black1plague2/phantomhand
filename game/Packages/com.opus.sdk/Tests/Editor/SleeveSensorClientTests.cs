using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>SleeveSensorClient: both Node A / Node B message forms (shapes taken from sim/sleeve/twin.py, which
    /// validates against the contract schemas), time mapping, ring buffers, connection state, EMG normalisation.</summary>
    public class SleeveSensorClientTests
    {
        private static string SensorData(string dev, double ts, double ax, double ay, double az, double gx = 0, double gy = 0, double gz = 0, bool extras = true)
        {
            JObject S(double v, string u) => new JObject { ["value"] = v, ["unit"] = u, ["status"] = "ok" };
            var sensors = new JObject
            {
                ["imu_accel_x"] = S(ax, "m/s2"), ["imu_accel_y"] = S(ay, "m/s2"), ["imu_accel_z"] = S(az, "m/s2"),
                ["imu_gyro_x"] = S(gx, "rad/s"), ["imu_gyro_y"] = S(gy, "rad/s"), ["imu_gyro_z"] = S(gz, "rad/s"),
            };
            if (extras) sensors["imu_temperature"] = S(30.1, "degC");
            return new JObject { ["type"] = "sensor_data", ["device_id"] = dev, ["timestamp_ms"] = ts, ["sensors"] = sensors }.ToString();
        }

        private static string Chunk(string dev, double ts, IEnumerable<double> vals, double rate = 100) =>
            new JObject
            {
                ["type"] = "sensor_chunk", ["device_id"] = dev, ["device_kind"] = "bio", ["timestamp_ms"] = ts,
                ["sample_rate_hz"] = rate, ["emg_envelope"] = new JArray(vals), ["unit"] = "raw_adc", ["status"] = "ok",
            }.ToString();

        private static string Burst(string dev, double ts, double peak = 900, double baseline = 120) =>
            new JObject { ["type"] = "emg_burst", ["device_id"] = dev, ["timestamp_ms"] = ts, ["peak"] = peak, ["baseline_rms"] = baseline }.ToString();

        private static SleeveSensorClient Make(out FakeHapticTransport t, out SessionClock clock, double startMs = 0)
        {
            t = new FakeHapticTransport();
            clock = SessionClock.Manual(startMs);
            return new SleeveSensorClient(t, clock);
        }

        // ------------------------------------------------------------------ parsing

        [Test]
        public void SensorData_NodeA_ParsedWithExtrasTolerated()
        {
            var c = Make(out var t, out var clock);
            var got = new List<ImuSample>();
            c.OnImuSample += got.Add;
            clock.Advance(5000);
            t.Receive(SensorData("SLEEVE_001", 1000, 0.3, -0.2, 9.8, 0.01, 0.02, 0.03));
            Assert.IsEmpty(got, "events are raised from Pump (main thread), not on the receive thread");
            c.Pump();

            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(0.3, got[0].Ax, 1e-9);
            Assert.AreEqual(-0.2, got[0].Ay, 1e-9);
            Assert.AreEqual(9.8, got[0].Az, 1e-9);
            Assert.AreEqual(0.03, got[0].Gz, 1e-9);
            Assert.AreEqual("SLEEVE_001", got[0].DeviceId);
            Assert.AreEqual(0, c.MalformedCount);
            Assert.AreEqual(Math.Sqrt(0.09 + 0.04 + 96.04), c.AccelMagnitude, 1e-9);
            Assert.IsTrue(c.HasAccel);
        }

        [Test]
        public void SensorChunk_NodeB_TimestampIsTheFirstSample_SamplesSpacedByRate()
        {
            var c = Make(out var t, out var clock);
            var chunks = new List<EmgChunk>();
            c.OnEmgChunk += chunks.Add;
            clock.Advance(5000);                                  // received when the 10th sample has just been taken
            t.Receive(Chunk("CHETNA_BIO_001", 1000, Enumerable.Range(0, 10).Select(i => 100.0 + i)));
            c.Pump();

            var s = chunks.Single().Samples;
            Assert.AreEqual(10, s.Length);
            // offset estimate = receive (5000) - device time of the LAST sample (1090) = 3910
            Assert.AreEqual(1000 + 3910, s[0].TMs, 1e-9, "first sample sits at timestamp_ms");
            for (int i = 1; i < 10; i++) Assert.AreEqual(s[0].TMs + 10.0 * i, s[i].TMs, 1e-9, "10 ms apart at 100 Hz");
            Assert.AreEqual(5000, s[9].TMs, 1e-9, "last sample lands on the receive time");
            Assert.AreEqual(109, s[9].Value, 1e-9);
            Assert.AreEqual(10, c.Emg.Count);
        }

        [Test]
        public void SensorChunk_OtherRate_UsesSampleRateField()
        {
            var c = Make(out var t, out var clock);
            clock.Advance(2000);
            t.Receive(Chunk("B", 500, new[] { 1.0, 2.0, 3.0 }, rate: 50));
            var s = c.Emg.Snapshot();
            Assert.AreEqual(s[0].TMs + 20, s[1].TMs, 1e-9);
            Assert.AreEqual(s[0].TMs + 40, s[2].TMs, 1e-9);
        }

        [Test]
        public void EmgBurst_TimestampIsTheOnset_MappedWithTheNodeOffset()
        {
            var c = Make(out var t, out var clock);
            var bursts = new List<EmgBurst>();
            c.OnEmgBurst += bursts.Add;
            clock.Advance(4000);
            t.Receive(Chunk("CHETNA_BIO_001", 100, new double[10]));     // offset: 4000 - 190 = 3810
            clock.Advance(500);
            t.Receive(Burst("CHETNA_BIO_001", 300));
            c.Pump();

            Assert.AreEqual(1, bursts.Count);
            Assert.AreEqual(300 + 3810, bursts[0].TMs, 1e-9, "onset, not the arrival time");
            Assert.AreEqual(300, bursts[0].DeviceMs, 1e-9);
            Assert.AreEqual(900, bursts[0].Peak, 1e-9);
            Assert.AreEqual(120, bursts[0].BaselineRms, 1e-9);
        }

        [Test]
        public void Status_Parsed_BatteryNullOnPowerBank()
        {
            var c = Make(out var t, out _);
            NodeStatus seen = null;
            c.OnStatus += s => seen = s;
            t.Receive(new JObject
            {
                ["type"] = "status", ["device_id"] = "SLEEVE_001", ["device_kind"] = "haptic", ["battery_pct"] = null,
                ["subscribers"] = 2, ["oled_available"] = true, ["unknown_msgs"] = 3, ["firmware_version"] = "0.5.0",
            }.ToString());
            c.Pump();
            Assert.IsNotNull(seen);
            Assert.AreEqual("SLEEVE_001", seen.DeviceId);
            Assert.IsNull(seen.BatteryPct);
            Assert.AreEqual(2, seen.Subscribers);
            Assert.AreEqual(true, seen.OledAvailable);
            Assert.AreEqual(3, seen.UnknownMsgs);
            Assert.AreEqual("0.5.0", seen.Firmware);
            Assert.AreSame(seen, c.Status);
        }

        [Test]
        public void MalformedPackets_AreCountedAndIgnored_UnknownTypesAreNot()
        {
            var c = Make(out var t, out var clock);
            clock.Advance(1000);
            t.Receive("garbage {");
            t.Receive("[1,2,3]");
            t.Receive("{\"type\":\"sensor_data\",\"device_id\":\"A\",\"timestamp_ms\":5}");                                 // no sensors
            t.Receive("{\"type\":\"sensor_data\",\"device_id\":\"A\",\"timestamp_ms\":5,\"sensors\":{\"imu_accel_x\":{\"value\":1,\"status\":\"ok\"}}}");   // missing y,z
            t.Receive(Chunk("B", 5, new[] { 1.0 }, rate: 0));
            t.Receive("{\"type\":\"sensor_chunk\",\"device_id\":\"B\",\"timestamp_ms\":5,\"sample_rate_hz\":100,\"emg_envelope\":[1,\"x\"]}");
            t.Receive("{\"type\":\"sensor_chunk\",\"device_id\":\"B\",\"timestamp_ms\":5,\"sample_rate_hz\":100,\"emg_envelope\":[]}");
            t.Receive("{\"type\":\"emg_burst\",\"device_id\":\"B\",\"timestamp_ms\":\"soon\"}");
            Assert.AreEqual(8, c.MalformedCount);
            Assert.AreEqual(0, c.Imu.Count);
            Assert.AreEqual(0, c.Emg.Count);
            Assert.IsFalse(c.Connected, "malformed packets never count as signs of life");

            t.Receive("{\"type\":\"ack\",\"cue_id\":\"x\",\"status\":\"accepted\",\"ok\":true}");
            t.Receive("{\"type\":\"device_discovery\",\"device_id\":\"B\"}");
            Assert.AreEqual(8, c.MalformedCount, "ack / discovery / unknown types belong to other parsers");
        }

        [Test]
        public void SensorData_WithNonOkStatus_IsRejected()
        {
            var c = Make(out var t, out _);
            var j = JObject.Parse(SensorData("A", 10, 0, 0, 9.8));
            j["sensors"]["imu_accel_y"]["status"] = "error";
            t.Receive(j.ToString());
            Assert.AreEqual(0, c.Imu.Count);
            Assert.AreEqual(1, c.MalformedCount);
        }

        // ------------------------------------------------------------------ connection + keepalive

        [Test]
        public void Connected_FalseAfter3sOfSilence_WithChangeEventsFromPump()
        {
            var c = Make(out var t, out var clock);
            var states = new List<bool>();
            c.OnConnectionChanged += states.Add;
            Assert.IsFalse(c.Connected, "nothing heard yet");

            t.Receive(new JObject { ["type"] = "status", ["device_id"] = "B" }.ToString());
            c.Pump();
            Assert.IsTrue(c.Connected);
            clock.Advance(3000);
            Assert.IsTrue(c.Connected, "exactly 3 s of silence is still connected");
            clock.Advance(1);
            Assert.IsFalse(c.Connected);
            c.Pump();
            Assert.AreEqual(new[] { true, false }, states.ToArray());

            t.Receive(SensorData("A", 1, 0, 0, 9.8));
            Assert.IsTrue(c.Connected, "traffic resumes the link");
        }

        [Test]
        public void Keepalive_SubscribeAndPingOncePerSecond()
        {
            var c = Make(out var t, out var clock);
            c.Start();
            for (int i = 0; i < 100; i++) { c.Pump(); clock.Advance(100); }     // 10 s in 100 ms frames

            var types = t.Sent.Select(s => JObject.Parse(s)["type"].Value<string>()).ToList();
            Assert.AreEqual(10, types.Count(x => x == "subscribe"));
            Assert.AreEqual(10, types.Count(x => x == "ping"));

            c.Stop();
            int n = t.Sent.Count;
            for (int i = 0; i < 20; i++) { c.Pump(); clock.Advance(100); }
            Assert.AreEqual(n, t.Sent.Count);
        }

        // ------------------------------------------------------------------ time mapping

        [Test]
        public void ClockOffset_JitteredPackets_MapWithinPlusMinus5ms()
        {
            var c = Make(out var t, out var clock);
            const double offset = 123456.0;     // session ms minus device ms
            var rnd = new Random(7);
            double now = 0;
            var truth = new List<double>();
            for (int k = 0; k < 300; k++)
            {
                double dev = 50000 + 10.0 * k;
                double rx = dev + offset + (rnd.NextDouble() * 10 - 5);   // +-5 ms network jitter
                clock.Advance(rx - now); now = rx;
                t.Receive(SensorData("SLEEVE_001", dev, 0, 0, 9.8));
                truth.Add(dev + offset);
            }
            var s = c.Imu.Snapshot();
            Assert.AreEqual(300, s.Length);
            for (int i = 0; i < s.Length; i++)
                Assert.AreEqual(truth[i], s[i].TMs, 5.0, "sample " + i);
            Assert.AreEqual(offset, c.ClockOffsetMs("SLEEVE_001").Value, 5.0);
        }

        [Test]
        public void ClockOffset_EachNodeHasItsOwnClock()
        {
            var c = Make(out var t, out var clock);
            clock.Advance(10000);
            t.Receive(SensorData("SLEEVE_001", 2000, 0, 0, 9.8));                 // offset 8000
            t.Receive(Chunk("CHETNA_BIO_001", 700, new double[10]));               // last sample 790 -> offset 9210
            Assert.AreEqual(8000, c.ClockOffsetMs("SLEEVE_001").Value, 1e-9);
            Assert.AreEqual(9210, c.ClockOffsetMs("CHETNA_BIO_001").Value, 1e-9);
            Assert.IsNull(c.ClockOffsetMs("NOPE"));
            Assert.AreEqual(2, c.ClockOffsets().Count);
        }

        [Test]
        public void ClockOffsetEstimator_TakesTheMedianOfTheFirst20_ThenReestimatesAfter30s()
        {
            var e = new ClockOffsetEstimator();
            for (int i = 0; i < 20; i++) e.Observe(i * 10, i * 10 + 1000 + (i == 3 ? 500 : 0));   // one outlier
            Assert.IsTrue(e.Settled);
            Assert.AreEqual(1000, e.OffsetMs.Value, 1e-9, "median ignores the outlier");

            // device clock drifts +20 ms; before 30 s nothing changes
            for (int i = 0; i < 100; i++) e.Observe(200 + i * 10, 200 + i * 10 + 1020);
            Assert.AreEqual(1000, e.OffsetMs.Value, 1e-9);
            // at/after 30 s since the estimate a fresh batch of 20 replaces it
            double baseT = 40000;
            for (int i = 0; i < 19; i++) e.Observe(baseT + i * 10, baseT + i * 10 + 1020);
            Assert.AreEqual(1000, e.OffsetMs.Value, 1e-9, "batch not complete yet");
            e.Observe(baseT + 190, baseT + 190 + 1020);
            Assert.AreEqual(1020, e.OffsetMs.Value, 1e-9);
            Assert.AreEqual(1, e.Reestimates);
        }

        // ------------------------------------------------------------------ buffers

        [Test]
        public void RingBuffers_KeepTheLast10Seconds()
        {
            var c = Make(out var t, out var clock);
            clock.Advance(100000);
            for (int k = 0; k < 1500; k++) { clock.Advance(10); t.Receive(SensorData("A", 1000 + 10.0 * k, 0, 0, 9.8)); }   // 15 s at 100 Hz
            var s = c.Imu.Snapshot();
            Assert.AreEqual(1001, s.Length, "10 s at 100 Hz, both ends inclusive");
            Assert.AreEqual(10000.0, s[s.Length - 1].TMs - s[0].TMs, 1e-6);
            for (int i = 1; i < s.Length; i++) Assert.Greater(s[i].TMs, s[i - 1].TMs);
        }

        [Test]
        public void RingBuffers_AreSafeUnderConcurrentWritersAndReaders()
        {
            var c = Make(out var t, out var clock);
            clock.Advance(1000);
            var writer = Task.Run(() =>
            {
                for (int k = 0; k < 4000; k++) t.Receive(SensorData("A", 10.0 * k, 0, 0, 9.8));
            });
            var errors = new List<Exception>();
            var reader = Task.Run(() =>
            {
                try { while (!writer.IsCompleted) { var s = c.Imu.Snapshot(); var m = c.AccelMagnitude; var r = c.Imu.Range(0, 5000); } }
                catch (Exception e) { lock (errors) errors.Add(e); }
            });
            Task.WaitAll(new[] { writer, reader }, 10000);
            Assert.IsEmpty(errors);
            Assert.AreEqual(1001, c.Imu.Count);
            Assert.AreEqual(4000, c.AcceptedCount);
        }

        // ------------------------------------------------------------------ EMG level

        private static void FeedEmg(SleeveSensorClient c, FakeHapticTransport t, SessionClock clock, double value, int chunks = 2)
        {
            for (int i = 0; i < chunks; i++)
            {
                clock.Advance(100);
                t.Receive(Chunk("CHETNA_BIO_001", clock.NowMs - 90 + 3000, Enumerable.Repeat(value, 10)));
            }
        }

        [Test]
        public void EmgLevel01_IsRestMvcNormalisedAndClamped()
        {
            var c = Make(out var t, out var clock, startMs: 1000);
            c.SetEmgCalibration(rest: 100, mvc: 500);

            FeedEmg(c, t, clock, 300);
            Assert.AreEqual(0.5, c.EmgLevel01, 1e-9);
            FeedEmg(c, t, clock, 100);
            Assert.AreEqual(0.0, c.EmgLevel01, 1e-9);
            FeedEmg(c, t, clock, 50);
            Assert.AreEqual(0.0, c.EmgLevel01, 1e-9, "below rest clamps to 0");
            FeedEmg(c, t, clock, 900);
            Assert.AreEqual(1.0, c.EmgLevel01, 1e-9, "above MVC clamps to 1");
            FeedEmg(c, t, clock, 200);
            Assert.AreEqual(0.25, c.EmgLevel01, 1e-9);
        }

        [Test]
        public void EmgLevel01_ZeroWithoutCalibration_OrWhenTheNodeIsSilent()
        {
            var c = Make(out var t, out var clock, startMs: 1000);
            FeedEmg(c, t, clock, 400);
            Assert.AreEqual(0.0, c.EmgLevel01, "no calibration");
            c.SetEmgCalibration(100, 500);
            Assert.AreEqual(0.75, c.EmgLevel01, 1e-9);
            clock.Advance(3500);
            Assert.AreEqual(0.0, c.EmgLevel01, "stale data is not an input");
            c.SetEmgCalibration(500, 500);
            FeedEmg(c, t, clock, 400);
            Assert.IsFalse(c.HasEmgCalibration, "mvc must exceed rest");
            Assert.AreEqual(0.0, c.EmgLevel01);
        }

        [Test]
        public void EmgCalibration_FromTheBuffer_RestMeanAndMvcMovingMean()
        {
            var c = Make(out var t, out var clock, startMs: 1000);
            var rest = Enumerable.Repeat(100.0, 10);
            double t0 = 5000;
            // 1 s of rest (10 chunks), then 1 s of squeeze at 500 with one 2000 spike
            for (int i = 0; i < 10; i++) { clock.Advance(100); t.Receive(Chunk("B", t0 + i * 100, rest)); }
            var squeeze = Enumerable.Repeat(500.0, 10).ToArray();
            for (int i = 0; i < 10; i++)
            {
                var v = (double[])squeeze.Clone();
                if (i == 4) v[3] = 2000;
                clock.Advance(100); t.Receive(Chunk("B", t0 + 1000 + i * 100, v));
            }
            var all = c.Emg.Snapshot();
            double restEnd = all[99].TMs, squeezeStart = all[100].TMs;

            Assert.IsTrue(c.CalibrateRest(all[0].TMs, restEnd));
            Assert.AreEqual(100.0, c.EmgRest.Value, 1e-9);
            Assert.IsTrue(c.CalibrateMvc(squeezeStart, all[199].TMs));
            // moving mean over 5 samples: one spike of 2000 among 500s -> (4*500+2000)/5 = 800 is the highest window
            Assert.AreEqual(800.0, c.EmgMvc.Value, 1e-9);
            Assert.IsTrue(c.HasEmgCalibration);
            Assert.IsFalse(c.CalibrateRest(0, 1), "too few samples");
        }

        [Test]
        public void Dispose_UnsubscribesFromTheTransport()
        {
            var c = Make(out var t, out _);
            c.Dispose();
            t.Receive(SensorData("A", 1, 0, 0, 9.8));
            Assert.AreEqual(0, c.Imu.Count);
        }

        // ------------------------------------------------------------------ the electronics team's firmware as flashed (handoff 2026-10-08)

        private const string BareKeepalive = "{\"type\":\"keepalive\"}";

        [Test]
        public void Keepalive_AlsoSendsTheBareKeepaliveDatagramOncePerSecond_StopsWithStop()
        {
            var c = Make(out var t, out var clock);
            c.Start();
            for (int i = 0; i < 100; i++) { c.Pump(); clock.Advance(100); }     // 10 s in 100 ms frames; the node never answers

            Assert.AreEqual(10, t.Sent.Count(s => s == BareKeepalive), "a node never heard from still gets it");
            var types = t.Sent.Select(s => JObject.Parse(s)["type"].Value<string>()).ToList();
            Assert.AreEqual(10, types.Count(x => x == "subscribe"), "subscribe + ping are unchanged");
            Assert.AreEqual(10, types.Count(x => x == "ping"));

            c.Stop();
            int n = t.Sent.Count;
            for (int i = 0; i < 20; i++) { c.Pump(); clock.Advance(100); }
            Assert.AreEqual(n, t.Sent.Count);
        }

        [Test]
        public void Keepalive_GoesOnWhenTheNodeFallsSilent()
        {
            var c = Make(out var t, out var clock);
            c.Start();
            int Keepalives() => t.Sent.Count(s => s == BareKeepalive);
            for (int i = 0; i < 30; i++) { c.Pump(); clock.Advance(100); }       // t = 0..2.9 s, nothing heard yet
            Assert.AreEqual(3, Keepalives());

            t.Receive(SensorData("CHETNA_HAPTIC_001", 1, 0, 0, 9.8));             // heard at t = 3.0 s
            for (int i = 0; i < 60; i++) { c.Pump(); clock.Advance(100); }       // pumps at t = 3.0 .. 8.9 s
            Assert.AreEqual(9, Keepalives(), "t = 0..8 s: silent since 3 s, still one per second so a power-cycled node re-attaches");
            var types = t.Sent.Select(s => JObject.Parse(s)["type"].Value<string>()).ToList();
            Assert.AreEqual(9, types.Count(x => x == "subscribe"));
            Assert.AreEqual(9, types.Count(x => x == "ping"));
        }

        [Test]
        public void Keepalive_NotSentWhileNoDeviceIsKnown()
        {
            var c = Make(out var t, out var clock);
            t.HasDevice = false;
            c.Start();
            for (int i = 0; i < 30; i++) { c.Pump(); clock.Advance(100); }
            Assert.IsEmpty(t.Sent);
        }

        [Test]
        public void RealFirmware_NodeA_AccelOnlySensorData_UnderItsOwnId_IsAccepted()
        {
            var c = Make(out var t, out var clock);
            clock.Advance(5000);
            // verbatim from the handoff: accel only (no gyro), device_id CHETNA_HAPTIC_001, no device_kind
            t.Receive("{\"type\":\"sensor_data\",\"device_id\":\"CHETNA_HAPTIC_001\",\"timestamp_ms\":123456,\"sensors\":{" +
                      "\"imu_accel_x\":{\"value\":0.12,\"unit\":\"m/s2\",\"status\":\"ok\"}," +
                      "\"imu_accel_y\":{\"value\":-0.05,\"unit\":\"m/s2\",\"status\":\"ok\"}," +
                      "\"imu_accel_z\":{\"value\":9.81,\"unit\":\"m/s2\",\"status\":\"ok\"}}}");

            Assert.AreEqual(0, c.MalformedCount);
            Assert.IsTrue(c.Connected);
            var s = c.Imu.Snapshot().Single();
            Assert.AreEqual("CHETNA_HAPTIC_001", s.DeviceId);
            Assert.AreEqual(0.12, s.Ax, 1e-9);
            Assert.AreEqual(-0.05, s.Ay, 1e-9);
            Assert.AreEqual(9.81, s.Az, 1e-9);
            Assert.AreEqual(0.0, s.Gx + s.Gy + s.Gz, "gyro is optional");
        }

        [Test]
        public void RealFirmware_NodeB_FourValueChunks_AreSpacedBySampleRate()
        {
            var c = Make(out var t, out var clock);
            clock.Advance(5000);
            // verbatim from the handoff: 4 values per packet, 25 packets per second
            t.Receive("{\"type\":\"sensor_chunk\",\"device_id\":\"CHETNA_BIO_001\",\"device_kind\":\"bio\",\"timestamp_ms\":123456," +
                      "\"sample_rate_hz\":100,\"emg_envelope\":[301.2,303.5,299.8,310.1],\"unit\":\"raw_adc\",\"status\":\"ok\"}");

            Assert.AreEqual(0, c.MalformedCount);
            var s = c.Emg.Snapshot();
            Assert.AreEqual(4, s.Length);
            Assert.AreEqual(301.2, s[0].Value, 1e-9);
            for (int i = 1; i < 4; i++) Assert.AreEqual(s[0].TMs + 10.0 * i, s[i].TMs, 1e-9, "10 ms apart at 100 Hz");
            Assert.AreEqual(5000, s[3].TMs, 1e-9, "the last sample lands on the receive time");
        }
    }
}
