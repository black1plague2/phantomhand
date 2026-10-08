using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Opus.Sdk
{
    /// <summary>
    /// Sensor side of the two-node sleeve (HAPTIC_PROTOCOL v1.2, PRD section 9). Wraps ONE transport (build one
    /// client per node: Node A with a `haptic` filter for the IMU, Node B with a `bio` filter for the EMG; the
    /// parser handles both message families on either).
    ///
    /// Thread model: the transport raises OnMessage on its receive thread. Parsing, time mapping and the 10 s ring
    /// buffers happen there under short locks; user-facing events are queued and raised from <see cref="Pump"/> on
    /// the main thread. Malformed packets are counted (<see cref="MalformedCount"/>) and ignored.
    ///
    /// Time: each node's millis() is mapped to session ms with a median offset (<see cref="ClockOffsetEstimator"/>),
    /// one estimator per device id. sensor_chunk.timestamp_ms is the FIRST sample (sample i is at
    /// timestamp_ms + i x 1000 / sample_rate_hz); emg_burst.timestamp_ms is the onset. For the offset a chunk counts
    /// as sent at its LAST sample.
    /// </summary>
    public sealed class SleeveSensorClient : IDisposable
    {
        public const double SilenceTimeoutMs = 3000;
        public const double KeepaliveIntervalMs = 1000;
        public const int EmgSmoothSamples = 5;

        private readonly IHapticTransport _transport;
        private readonly SessionClock _clock;
        private readonly object _gate = new object();
        private readonly Dictionary<string, ClockOffsetEstimator> _offsets = new Dictionary<string, ClockOffsetEstimator>();
        private readonly ConcurrentQueue<Action> _events = new ConcurrentQueue<Action>();
        private NodeStatus _status;
        private double _lastRxMs = double.NegativeInfinity;
        private double _nextKeepaliveMs;
        private bool _keepalive;
        private bool _lastConnectedReported;
        private long _malformed;
        private long _accepted;

        public TimeSeriesRing<ImuSample> Imu { get; } = new TimeSeriesRing<ImuSample>(s => s.TMs);
        public TimeSeriesRing<EmgSample> Emg { get; } = new TimeSeriesRing<EmgSample>(s => s.TMs);

        /// <summary>Raised from <see cref="Pump"/> (main thread). Only queued while somebody is subscribed.</summary>
        public event Action<ImuSample> OnImuSample;
        public event Action<EmgChunk> OnEmgChunk;
        public event Action<EmgBurst> OnEmgBurst;
        public event Action<NodeStatus> OnStatus;
        public event Action<bool> OnConnectionChanged;

        public long MalformedCount => Interlocked.Read(ref _malformed);
        public long AcceptedCount => Interlocked.Read(ref _accepted);

        public SleeveSensorClient(IHapticTransport transport, SessionClock clock)
        {
            _transport = transport;
            _clock = clock;
            _transport.OnMessage += HandleMessage;
        }

        /// <summary>Starts the transport (idempotent) and the 1 Hz subscribe + ping keepalive.</summary>
        public void Start()
        {
            _transport.Start();
            _keepalive = true;
            _nextKeepaliveMs = _clock.NowMs;
        }

        /// <summary>Stops the keepalive; the transport keeps running until Dispose.</summary>
        public void Stop() => _keepalive = false;

        public bool Connected
        {
            get
            {
                double last;
                lock (_gate) last = _lastRxMs;
                return _clock.NowMs - last <= SilenceTimeoutMs;
            }
        }

        public NodeStatus Status { get { lock (_gate) return _status; } }

        // ---------------------------------------------------------------- derived values

        public bool HasAccel => Imu.TryGetLatest(out _);

        /// <summary>|a| of the latest IMU sample in m/s2; 0 before the first sample.</summary>
        public double AccelMagnitude => Imu.TryGetLatest(out var s) ? s.AccelMagnitude : 0.0;

        /// <summary>Mean of the last <see cref="EmgSmoothSamples"/> envelope samples (raw ADC), NaN before the first.</summary>
        public double EmgCurrent
        {
            get
            {
                var snap = Emg.Snapshot();
                if (snap.Length == 0) return double.NaN;
                int n = Math.Min(EmgSmoothSamples, snap.Length);
                double sum = 0;
                for (int i = snap.Length - n; i < snap.Length; i++) sum += snap[i].Value;
                return sum / n;
            }
        }

        public double? EmgRest { get; private set; }
        public double? EmgMvc { get; private set; }
        public bool HasEmgCalibration => EmgRest.HasValue && EmgMvc.HasValue && EmgMvc.Value > EmgRest.Value;

        public void SetEmgCalibration(double rest, double mvc)
        {
            EmgRest = rest; EmgMvc = mvc;
        }

        /// <summary>Rest baseline = mean envelope over the session-ms window. Needs >= 10 samples.</summary>
        public bool CalibrateRest(double fromMs, double toMs)
        {
            var w = Emg.Range(fromMs, toMs);
            if (w.Length < 10) return false;
            double sum = 0;
            foreach (var s in w) sum += s.Value;
            EmgRest = sum / w.Length;
            return true;
        }

        /// <summary>Maximum voluntary contraction = highest <see cref="EmgSmoothSamples"/>-sample moving mean in the
        /// window (robust to a single spike). Needs >= 10 samples and a value above the rest baseline.</summary>
        public bool CalibrateMvc(double fromMs, double toMs)
        {
            var w = Emg.Range(fromMs, toMs);
            if (w.Length < 10) return false;
            int k = EmgSmoothSamples;
            double best = double.NegativeInfinity, run = 0;
            for (int i = 0; i < w.Length; i++)
            {
                run += w[i].Value;
                if (i >= k) run -= w[i - k].Value;
                if (i >= k - 1) best = Math.Max(best, run / k);
            }
            if (EmgRest.HasValue && best <= EmgRest.Value) return false;
            EmgMvc = best;
            return true;
        }

        /// <summary>FR-VR-07: (current - rest) / (mvc - rest), clamped to 0..1. 0 without a calibration, without
        /// data, or while the node is silent.</summary>
        public double EmgLevel01
        {
            get
            {
                if (!HasEmgCalibration || !Connected) return 0.0;
                double cur = EmgCurrent;
                if (double.IsNaN(cur)) return 0.0;
                double v = (cur - EmgRest.Value) / (EmgMvc.Value - EmgRest.Value);
                return v < 0 ? 0 : v > 1 ? 1 : v;
            }
        }

        /// <summary>Current device-to-session offset for a node (null before its first packet).</summary>
        public double? ClockOffsetMs(string deviceId)
        {
            lock (_gate) return _offsets.TryGetValue(deviceId ?? "", out var e) ? e.OffsetMs : null;
        }

        public IReadOnlyDictionary<string, double> ClockOffsets()
        {
            var d = new Dictionary<string, double>();
            lock (_gate) foreach (var kv in _offsets) if (kv.Value.OffsetMs.HasValue) d[kv.Key] = kv.Value.OffsetMs.Value;
            return d;
        }

        // ---------------------------------------------------------------- main thread

        /// <summary>Call every frame: sends the 1 Hz keepalive and raises queued events.</summary>
        public void Pump()
        {
            double now = _clock.NowMs;
            if (_keepalive && now >= _nextKeepaliveMs)
            {
                _nextKeepaliveMs += KeepaliveIntervalMs;
                if (_nextKeepaliveMs <= now) _nextKeepaliveMs = now + KeepaliveIntervalMs;
                if (_transport.HasDevice)
                {
                    _transport.Send(new JObject { ["type"] = "subscribe" }.ToString(Newtonsoft.Json.Formatting.None));
                    _transport.Send(new JObject
                    {
                        ["v"] = 1, ["type"] = "ping", ["id"] = Guid.NewGuid().ToString(),
                        ["ts_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    }.ToString(Newtonsoft.Json.Formatting.None));
                }
            }

            while (_events.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }

            bool connected = Connected;
            if (connected != _lastConnectedReported)
            {
                _lastConnectedReported = connected;
                OnConnectionChanged?.Invoke(connected);
            }
        }

        // ---------------------------------------------------------------- receive thread

        private void HandleMessage(string json)
        {
            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (Exception) { Bad(); return; }
            if (obj == null) { Bad(); return; }

            string type = obj["type"]?.Type == JTokenType.String ? obj["type"].Value<string>() : null;
            try
            {
                switch (type)
                {
                    case "sensor_data": ParseSensorData(obj); break;
                    case "sensor_chunk": ParseSensorChunk(obj); break;
                    case "emg_burst": ParseBurst(obj); break;
                    case "status": ParseStatus(obj); break;
                    default: break; // ack, discovery, imu, unknown: not ours
                }
            }
            catch (FormatException) { Bad(); }
            catch (InvalidCastException) { Bad(); }
            catch (ArgumentException) { Bad(); }
        }

        private void Bad() => Interlocked.Increment(ref _malformed);

        private void Touch()
        {
            double now = _clock.NowMs;
            lock (_gate) _lastRxMs = now;
            Interlocked.Increment(ref _accepted);
        }

        private static bool Num(JToken t, out double v)
        {
            v = 0;
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) return false;
            v = t.Value<double>();
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static bool Sensor(JObject sensors, string name, out double v)
        {
            v = 0;
            var s = sensors[name] as JObject;
            if (s == null || !Num(s["value"], out v)) return false;
            string st = s["status"]?.Type == JTokenType.String ? s["status"].Value<string>() : "ok";
            return st == "ok";
        }

        /// <summary>Observe a packet for the device's clock offset (under _gate) and return the mapper to use.</summary>
        private double MapToSession(string deviceId, double deviceMs, double deviceSendMs, double receiveSessionMs)
        {
            lock (_gate)
            {
                if (!_offsets.TryGetValue(deviceId, out var est)) _offsets[deviceId] = est = new ClockOffsetEstimator();
                est.Observe(deviceSendMs, receiveSessionMs);
                return est.ToSessionMs(deviceMs);
            }
        }

        private void ParseSensorData(JObject o)
        {
            string id = o["device_id"]?.Type == JTokenType.String ? o["device_id"].Value<string>() : null;
            var sensors = o["sensors"] as JObject;
            if (id == null || sensors == null || !Num(o["timestamp_ms"], out double ts) || ts < 0) { Bad(); return; }
            if (!Sensor(sensors, "imu_accel_x", out double ax) || !Sensor(sensors, "imu_accel_y", out double ay)
                || !Sensor(sensors, "imu_accel_z", out double az)) { Bad(); return; }
            // gyro is optional per sample; extras (imu_temperature, future names) are ignored
            Sensor(sensors, "imu_gyro_x", out double gx);
            Sensor(sensors, "imu_gyro_y", out double gy);
            Sensor(sensors, "imu_gyro_z", out double gz);

            double rx = _clock.NowMs;
            double t = MapToSession(id, ts, ts, rx);
            var s = new ImuSample { DeviceId = id, TMs = t, Ax = ax, Ay = ay, Az = az, Gx = gx, Gy = gy, Gz = gz };
            Imu.Add(s);
            Touch();
            if (OnImuSample != null) _events.Enqueue(() => OnImuSample?.Invoke(s));
        }

        private void ParseSensorChunk(JObject o)
        {
            string id = o["device_id"]?.Type == JTokenType.String ? o["device_id"].Value<string>() : null;
            var env = o["emg_envelope"] as JArray;
            if (id == null || env == null || env.Count == 0
                || !Num(o["timestamp_ms"], out double ts) || ts < 0
                || !Num(o["sample_rate_hz"], out double rate) || rate <= 0) { Bad(); return; }

            var vals = new double[env.Count];
            for (int i = 0; i < vals.Length; i++)
                if (!Num(env[i], out vals[i])) { Bad(); return; }

            double dt = 1000.0 / rate;
            double rx = _clock.NowMs;
            double first = MapToSession(id, ts, ts + (vals.Length - 1) * dt, rx);
            var samples = new EmgSample[vals.Length];
            for (int i = 0; i < vals.Length; i++)
            {
                samples[i] = new EmgSample { TMs = first + i * dt, Value = vals[i] };
                Emg.Add(samples[i]);
            }
            Touch();
            if (OnEmgChunk != null)
            {
                var chunk = new EmgChunk { DeviceId = id, RateHz = rate, Samples = samples };
                _events.Enqueue(() => OnEmgChunk?.Invoke(chunk));
            }
        }

        private void ParseBurst(JObject o)
        {
            string id = o["device_id"]?.Type == JTokenType.String ? o["device_id"].Value<string>() : null;
            if (id == null || !Num(o["timestamp_ms"], out double ts) || ts < 0
                || !Num(o["peak"], out double peak) || !Num(o["baseline_rms"], out double bl)) { Bad(); return; }

            double onset;
            lock (_gate)
            {
                // A burst is sent after its onset, so it never feeds the offset estimate; before the first
                // sensor packet there is no mapping yet and the device time is used as is.
                onset = _offsets.TryGetValue(id, out var est) ? est.ToSessionMs(ts) : ts;
            }
            var b = new EmgBurst { DeviceId = id, TMs = onset, DeviceMs = ts, Peak = peak, BaselineRms = bl };
            Touch();
            if (OnEmgBurst != null) _events.Enqueue(() => OnEmgBurst?.Invoke(b));
        }

        private void ParseStatus(JObject o)
        {
            string id = o["device_id"]?.Type == JTokenType.String ? o["device_id"].Value<string>() : null;
            if (id == null) { Bad(); return; }
            var st = new NodeStatus
            {
                DeviceId = id,
                DeviceKind = o["device_kind"]?.Type == JTokenType.String ? o["device_kind"].Value<string>() : null,
                Firmware = (o["firmware_version"] ?? o["fw"])?.Type == JTokenType.String ? (o["firmware_version"] ?? o["fw"]).Value<string>() : null,
            };
            if (Num(o["battery_pct"], out double bp)) st.BatteryPct = bp;
            if (Num(o["subscribers"], out double sub)) st.Subscribers = (int)sub;
            if (Num(o["unknown_msgs"], out double um)) st.UnknownMsgs = (int)um;
            if (o["oled_available"]?.Type == JTokenType.Boolean) st.OledAvailable = o["oled_available"].Value<bool>();
            lock (_gate) _status = st;
            Touch();
            if (OnStatus != null) _events.Enqueue(() => OnStatus?.Invoke(st));
        }

        public void Dispose()
        {
            _transport.OnMessage -= HandleMessage;
            _transport.Dispose();
        }
    }
}
