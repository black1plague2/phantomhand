using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Opus.Sdk
{
    /// <summary>
    /// Writes the wearable-node streams as sens_###.json (contracts/schemas/sensor-file.schema.json): 5 s columnar
    /// chunks, t_ms in session ms on the same <see cref="SessionClock"/> as events and kinematics. Node B EMG goes
    /// to `emg_env`, Node A IMU to `imu`. Main-thread only: feed it from <see cref="SleeveSensorClient"/> events
    /// (<see cref="Attach"/>) or call <see cref="AddImu"/> / <see cref="AddEmg"/> directly.
    ///
    /// A chunk covers 5 s from its first sample; it is written when a sample arrives 5 s or more after that first
    /// sample, when <see cref="Tick"/> sees the clock 5 s past it, or on <see cref="Flush"/>. Each stream holds ONE
    /// device id per chunk file (the first it sees); samples from another device id on the same stream are counted
    /// in <see cref="DroppedForeign"/> and not written. Non-finite values are skipped.
    /// </summary>
    public sealed class SensorRecorder
    {
        public const double ChunkDurationMs = 5000.0;

        private readonly string _sessionId;
        private readonly SessionClock _clock;
        private readonly string _sessionDir;
        private readonly string _source;

        private int _nextSeq;
        private double _chunkStartMs = double.NaN;

        private string _emgDevice;
        private double _emgRate;
        private readonly List<double> _emgT = new List<double>(), _emgV = new List<double>();
        private readonly List<bool> _emgExcl = new List<bool>();

        private string _imuDevice;
        private readonly List<double> _imuT = new List<double>();
        private readonly List<double> _ax = new List<double>(), _ay = new List<double>(), _az = new List<double>(),
                                      _gx = new List<double>(), _gy = new List<double>(), _gz = new List<double>();

        /// <summary>Mid-run chunk rollovers serialise and write the finished chunk on a background thread, in order. A 5 s chunk is about 150 KB of JSON and cost
        /// 150-400 ms of main-thread time in the editor: frames froze every 5 s and stroke cues due in that window went out late. <see cref="Flush"/> still
        /// returns only when every file is on disk. Off by default, so tests and older callers see each file at once.</summary>
        public bool BackgroundWrites;
        private Task _pendingWrite = Task.CompletedTask;
        private volatile string _writeError;
        public int ChunksWritten { get; private set; }
        public string LastPath { get; private set; }
        public int DroppedForeign { get; private set; }

        /// <summary>Optional: returns true when session time t is inside a motor pulse + 50 ms (FR-AN-01). When set,
        /// each EMG chunk also carries the optional `motor_excl` array.</summary>
        public Func<double, bool> MotorExclusion { get; set; }

        public SensorRecorder(string sessionId, SessionClock clock, string sessionDir, string source = "udp", int startChunkSeq = 0)
        {
            _sessionId = sessionId; _clock = clock; _sessionDir = sessionDir; _source = source; _nextSeq = startChunkSeq;
        }

        /// <summary>Subscribe to a client's main-thread events so every received sample is recorded.</summary>
        public void Attach(SleeveSensorClient client)
        {
            client.OnImuSample += s => AddImu(s);
            client.OnEmgChunk += c => AddEmgChunk(c);
        }

        public void AddImu(ImuSample s) => AddImu(s.DeviceId ?? "SLEEVE_001", s);

        public void AddImu(string deviceId, ImuSample s)
        {
            if (!Finite(s.TMs, s.Ax, s.Ay, s.Az, s.Gx, s.Gy, s.Gz)) return;
            RollIfDue(s.TMs);
            if (_imuDevice == null) _imuDevice = deviceId;
            else if (_imuDevice != deviceId) { DroppedForeign++; return; }
            OpenIfNeeded(s.TMs);
            _imuT.Add(s.TMs);
            _ax.Add(s.Ax); _ay.Add(s.Ay); _az.Add(s.Az); _gx.Add(s.Gx); _gy.Add(s.Gy); _gz.Add(s.Gz);
        }

        public void AddEmgChunk(EmgChunk c)
        {
            foreach (var s in c.Samples) AddEmg(c.DeviceId, c.RateHz, s);
        }

        public void AddEmg(string deviceId, double rateHz, EmgSample s)
        {
            if (!Finite(s.TMs, s.Value)) return;
            RollIfDue(s.TMs);
            if (_emgDevice == null) { _emgDevice = deviceId; _emgRate = rateHz; }
            else if (_emgDevice != deviceId) { DroppedForeign++; return; }
            OpenIfNeeded(s.TMs);
            _emgT.Add(s.TMs); _emgV.Add(s.Value);
            if (MotorExclusion != null) _emgExcl.Add(MotorExclusion(s.TMs));
        }

        /// <summary>Call each frame so a quiet stream still gets flushed on time.</summary>
        public void Tick()
        {
            if (!double.IsNaN(_chunkStartMs) && _clock.NowMs - _chunkStartMs >= ChunkDurationMs) Roll();
        }

        private static bool Finite(params double[] v)
        {
            foreach (var d in v) if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            return true;
        }

        private void OpenIfNeeded(double t)
        {
            if (double.IsNaN(_chunkStartMs)) _chunkStartMs = t;
            else if (t < _chunkStartMs) _chunkStartMs = t;   // slight reordering across streams
        }

        private void RollIfDue(double t)
        {
            if (!double.IsNaN(_chunkStartMs) && t - _chunkStartMs >= ChunkDurationMs) Roll();
        }

        /// <summary>Write the current chunk (also call at session end). No file when nothing was recorded.</summary>
        public void Flush() { Roll(); Drain(); }

        /// <summary>Waits for the background writes and reports a failed one (a lost chunk must not pass silently).</summary>
        private void Drain()
        {
            _pendingWrite.Wait(10000);
            if (_writeError == null) return;
            string e = _writeError; _writeError = null;
            throw new IOException(e);
        }

        /// <summary>Closes the current chunk; the file is written here, or queued when <see cref="BackgroundWrites"/> is on.</summary>
        private void Roll()
        {
            bool hasEmg = _emgT.Count > 0, hasImu = _imuT.Count > 0;
            if (!hasEmg && !hasImu) { Reset(); return; }

            double t0 = double.PositiveInfinity;
            if (hasEmg) t0 = Math.Min(t0, _emgT[0]);
            if (hasImu) t0 = Math.Min(t0, _imuT[0]);

            var root = new JObject
            {
                ["session_id"] = _sessionId,
                ["chunk_seq"] = _nextSeq,
                ["t0_ms"] = Math.Max(0, t0),
                ["source"] = _source,
            };
            if (hasEmg)
            {
                var emg = new JObject
                {
                    ["device_id"] = _emgDevice,
                    ["rate_hz"] = _emgRate,
                    ["unit"] = "raw_adc",
                    ["t_ms"] = new JArray(_emgT),
                    ["value"] = new JArray(_emgV),
                };
                if (_emgExcl.Count == _emgT.Count) emg["motor_excl"] = new JArray(_emgExcl);
                root["emg_env"] = emg;
            }
            if (hasImu)
            {
                root["imu"] = new JObject
                {
                    ["device_id"] = _imuDevice,
                    ["rate_hz"] = EstimateRate(_imuT),
                    ["t_ms"] = new JArray(_imuT),
                    ["ax"] = new JArray(_ax), ["ay"] = new JArray(_ay), ["az"] = new JArray(_az),
                    ["gx"] = new JArray(_gx), ["gy"] = new JArray(_gy), ["gz"] = new JArray(_gz),
                };
            }

            if (!string.IsNullOrEmpty(_sessionDir))
            {
                Directory.CreateDirectory(_sessionDir);
                LastPath = Path.Combine(_sessionDir, $"sens_{_nextSeq:000}.json");
                string path = LastPath;    // root is a private copy of the samples: nothing touches it after this point
                if (BackgroundWrites) _pendingWrite = _pendingWrite.ContinueWith(_ => WriteFile(path, () => root.ToString(Formatting.None)), TaskScheduler.Default);
                else File.WriteAllText(path, root.ToString(Formatting.None));
            }
            ChunksWritten++;
            _nextSeq++;
            Reset();
        }

        private void WriteFile(string path, Func<string> json)
        {
            try { File.WriteAllText(path, json()); }
            catch (Exception e) { _writeError = path + ": " + e.Message; }
        }

        private void Reset()
        {
            _chunkStartMs = double.NaN;
            _emgT.Clear(); _emgV.Clear(); _emgExcl.Clear();
            _imuT.Clear(); _ax.Clear(); _ay.Clear(); _az.Clear(); _gx.Clear(); _gy.Clear(); _gz.Clear();
            // the device ids stay: they do not change inside one session
        }

        private static double EstimateRate(List<double> t)
        {
            if (t.Count < 3) return 100.0;
            var d = new List<double>();
            for (int i = 1; i < t.Count; i++) if (t[i] > t[i - 1]) d.Add(t[i] - t[i - 1]);
            if (d.Count == 0) return 100.0;
            d.Sort();
            double med = d[d.Count / 2];
            return med > 0 ? Math.Round(1000.0 / med, 1) : 100.0;
        }
    }
}
