using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>SensorRecorder -> sens_###.json (sensor-file.schema.json). The shape is checked here; the exact
    /// schema + the "every array as long as its t_ms" rule are checked by contracts/validate.py --session on the file
    /// written by <see cref="Output_ForContractValidation"/> (see logs/sessions/2026-10-07-PH-U-U1-run1.md).</summary>
    public class SensorRecorderTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "opus_u1_sens_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static ImuSample Imu(double t, string dev = "SLEEVE_001") =>
            new ImuSample { DeviceId = dev, TMs = t, Ax = 0.1, Ay = 0.2, Az = 9.8, Gx = 0.01, Gy = 0.02, Gz = 0.03 };

        private static JObject Read(string path) => JObject.Parse(File.ReadAllText(path));

        [Test]
        public void Flush_WritesBothStreams_ColumnarAndParallel()
        {
            var clock = SessionClock.Manual(0);
            var r = new SensorRecorder("sess-1", clock, _dir);
            for (int i = 0; i < 100; i++) r.AddImu(Imu(1000 + 10.0 * i));
            for (int i = 0; i < 100; i++) r.AddEmg("CHETNA_BIO_001", 100, new EmgSample { TMs = 1005 + 10.0 * i, Value = 120 + i });
            r.Flush();

            var o = Read(Path.Combine(_dir, "sens_000.json"));
            Assert.AreEqual("sess-1", o["session_id"].Value<string>());
            Assert.AreEqual(0, o["chunk_seq"].Value<int>());
            Assert.AreEqual(1000, o["t0_ms"].Value<double>(), 1e-9, "first sample of the chunk");
            Assert.AreEqual("udp", o["source"].Value<string>());

            var emg = (JObject)o["emg_env"];
            Assert.AreEqual("CHETNA_BIO_001", emg["device_id"].Value<string>());
            Assert.AreEqual("raw_adc", emg["unit"].Value<string>());
            Assert.AreEqual(100, emg["t_ms"].Count());
            Assert.AreEqual(100, emg["value"].Count());
            Assert.IsNull(emg["motor_excl"], "optional, only when an exclusion predicate is set");

            var imu = (JObject)o["imu"];
            Assert.AreEqual("SLEEVE_001", imu["device_id"].Value<string>());
            foreach (var k in new[] { "t_ms", "ax", "ay", "az", "gx", "gy", "gz" })
                Assert.AreEqual(100, imu[k].Count(), k);
            Assert.AreEqual(100.0, imu["rate_hz"].Value<double>(), 0.01);
        }

        [Test]
        public void Chunks_RollEvery5Seconds_OnSampleTime()
        {
            var clock = SessionClock.Manual(0);
            var r = new SensorRecorder("s", clock, _dir);
            for (int i = 0; i < 1200; i++) r.AddImu(Imu(2000 + 10.0 * i));    // 12 s at 100 Hz
            Assert.AreEqual(2, r.ChunksWritten, "two full 5 s chunks written on the way");
            r.Flush();
            Assert.AreEqual(3, r.ChunksWritten);

            var c0 = Read(Path.Combine(_dir, "sens_000.json"));
            var c1 = Read(Path.Combine(_dir, "sens_001.json"));
            var c2 = Read(Path.Combine(_dir, "sens_002.json"));
            Assert.AreEqual(2000, c0["t0_ms"].Value<double>(), 1e-9);
            Assert.AreEqual(7000, c1["t0_ms"].Value<double>(), 1e-9);
            Assert.AreEqual(12000, c2["t0_ms"].Value<double>(), 1e-9);
            Assert.AreEqual(500, c0["imu"]["t_ms"].Count());
            Assert.AreEqual(500, c1["imu"]["t_ms"].Count());
            Assert.AreEqual(200, c2["imu"]["t_ms"].Count());
            Assert.AreEqual(1, c1["chunk_seq"].Value<int>());
            Assert.AreEqual(2, c2["chunk_seq"].Value<int>());
        }

        [Test]
        public void Tick_FlushesAQuietStreamOnTheClock()
        {
            var clock = SessionClock.Manual(0);
            var r = new SensorRecorder("s", clock, _dir);
            clock.Advance(1000);
            r.AddImu(Imu(1000));
            clock.Advance(4999);
            r.Tick();
            Assert.AreEqual(0, r.ChunksWritten);
            clock.Advance(1);
            r.Tick();
            Assert.AreEqual(1, r.ChunksWritten);
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "sens_000.json")));
        }

        [Test]
        public void EmptyChunk_WritesNothing()
        {
            var r = new SensorRecorder("s", SessionClock.Manual(0), _dir);
            r.Flush();
            Assert.AreEqual(0, r.ChunksWritten);
            Assert.IsFalse(Directory.Exists(_dir) && Directory.GetFiles(_dir).Any());
        }

        [Test]
        public void SingleStreamChunk_IsValidAlone()
        {
            var r = new SensorRecorder("s", SessionClock.Manual(0), _dir);
            r.AddEmg("CHETNA_BIO_001", 100, new EmgSample { TMs = 50, Value = 1 });
            r.Flush();
            var o = Read(Path.Combine(_dir, "sens_000.json"));
            Assert.IsNotNull(o["emg_env"]);
            Assert.IsNull(o["imu"]);
        }

        [Test]
        public void MotorExclusion_AddsAParallelFlagArray()
        {
            var r = new SensorRecorder("s", SessionClock.Manual(0), _dir) { MotorExclusion = t => t >= 1020 && t < 1060 };
            for (int i = 0; i < 10; i++) r.AddEmg("B", 100, new EmgSample { TMs = 1000 + 10.0 * i, Value = i });
            r.Flush();
            var excl = Read(Path.Combine(_dir, "sens_000.json"))["emg_env"]["motor_excl"].Values<bool>().ToArray();
            Assert.AreEqual(10, excl.Length);
            Assert.AreEqual(new[] { false, false, true, true, true, true, false, false, false, false }, excl);
        }

        [Test]
        public void ForeignDeviceOnAStream_IsDropped_AndNonFiniteValuesSkipped()
        {
            var r = new SensorRecorder("s", SessionClock.Manual(0), _dir);
            r.AddImu(Imu(0, "SLEEVE_001"));
            r.AddImu(Imu(10, "SLEEVE_002"));
            r.AddImu(new ImuSample { DeviceId = "SLEEVE_001", TMs = 20, Ax = double.NaN });
            r.AddEmg("B", 100, new EmgSample { TMs = 0, Value = double.PositiveInfinity });
            r.Flush();
            Assert.AreEqual(1, r.DroppedForeign);
            var o = Read(Path.Combine(_dir, "sens_000.json"));
            Assert.AreEqual(1, o["imu"]["t_ms"].Count());
            Assert.IsNull(o["emg_env"]);
        }

        [Test]
        public void Attach_RecordsClientEventsEndToEnd_OnTheSharedClock()
        {
            var t = new FakeHapticTransport();
            var clock = SessionClock.Manual(0);
            var client = new SleeveSensorClient(t, clock);
            var rec = new SensorRecorder("s-e2e", clock, _dir);
            rec.Attach(client);

            clock.Advance(5000);
            for (int i = 0; i < 20; i++)
            {
                t.Receive(new JObject
                {
                    ["type"] = "sensor_data", ["device_id"] = "SLEEVE_001", ["timestamp_ms"] = 1000 + 10.0 * i,
                    ["sensors"] = new JObject
                    {
                        ["imu_accel_x"] = new JObject { ["value"] = 0.0, ["unit"] = "m/s2", ["status"] = "ok" },
                        ["imu_accel_y"] = new JObject { ["value"] = 0.0, ["unit"] = "m/s2", ["status"] = "ok" },
                        ["imu_accel_z"] = new JObject { ["value"] = 9.81, ["unit"] = "m/s2", ["status"] = "ok" },
                    },
                }.ToString());
            }
            t.Receive(new JObject
            {
                ["type"] = "sensor_chunk", ["device_id"] = "CHETNA_BIO_001", ["timestamp_ms"] = 1000, ["sample_rate_hz"] = 100,
                ["emg_envelope"] = new JArray(Enumerable.Repeat(130.0, 10)), ["unit"] = "raw_adc", ["status"] = "ok",
            }.ToString());
            client.Pump();
            rec.Flush();

            var o = Read(Path.Combine(_dir, "sens_000.json"));
            Assert.AreEqual(20, o["imu"]["t_ms"].Count());
            Assert.AreEqual(10, o["emg_env"]["value"].Count());
            Assert.AreEqual("SLEEVE_001", o["imu"]["device_id"].Value<string>());
            Assert.AreEqual("CHETNA_BIO_001", o["emg_env"]["device_id"].Value<string>());
        }

        /// <summary>Writes a realistic two-stream session file to a fixed temp path so contracts/validate.py can be
        /// run against it (same clock for both streams, 12 s, three chunks).</summary>
        [Test]
        public void Output_ForContractValidation()
        {
            string dir = Path.Combine(Path.GetTempPath(), "opus_u1_validate", "phantom_hand_min");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var clock = SessionClock.Manual(0);
            var r = new SensorRecorder("ph-u1-validate", clock, dir) { MotorExclusion = t => (t % 1000) < 250 };
            for (int i = 0; i < 1200; i++)
            {
                double t = 500 + 10.0 * i;
                r.AddImu(new ImuSample { DeviceId = "SLEEVE_001", TMs = t, Ax = 0.02 * Math.Sin(t / 100), Ay = 0.01, Az = 9.81, Gx = 0.001, Gy = 0.002, Gz = 0.003 });
                r.AddEmg("CHETNA_BIO_001", 100, new EmgSample { TMs = t + 3, Value = 120 + 10 * Math.Sin(t / 50) });
            }
            r.Flush();
            Assert.AreEqual(3, r.ChunksWritten);
            Assert.AreEqual(3, Directory.GetFiles(dir, "sens_*.json").Length);
            foreach (var f in Directory.GetFiles(dir, "sens_*.json"))
            {
                var o = Read(f);
                foreach (var stream in new[] { "emg_env", "imu" })
                {
                    var s = (JObject)o[stream];
                    int n = s["t_ms"].Count();
                    foreach (var p in s.Properties().Where(p => p.Value is JArray)) Assert.AreEqual(n, p.Value.Count(), f + " " + stream + "." + p.Name);
                }
            }
        }
    }
}
