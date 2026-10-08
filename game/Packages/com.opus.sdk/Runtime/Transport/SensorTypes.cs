using System;
using System.Collections.Generic;

namespace Opus.Sdk
{
    /// <summary>One Node A IMU sample (MPU6050), time already mapped to session ms. Accel m/s2, gyro rad/s.</summary>
    public struct ImuSample
    {
        public string DeviceId;
        public double TMs;
        public double Ax, Ay, Az, Gx, Gy, Gz;
        public double AccelMagnitude => Math.Sqrt(Ax * Ax + Ay * Ay + Az * Az);
    }

    /// <summary>One Node B EMG envelope sample (raw ADC units, 100 Hz), time mapped to session ms.</summary>
    public struct EmgSample
    {
        public double TMs;
        public double Value;
    }

    /// <summary>A whole sensor_chunk after time mapping: sample i is at <c>Samples[i].TMs</c>.</summary>
    public sealed class EmgChunk
    {
        public string DeviceId;
        public double RateHz;
        public EmgSample[] Samples;
    }

    /// <summary>An emg_burst message: TMs is the burst ONSET (session ms); Peak and BaselineRms are envelope units.</summary>
    public struct EmgBurst
    {
        public string DeviceId;
        public double TMs;
        public double DeviceMs;
        public double Peak;
        public double BaselineRms;
    }

    /// <summary>Latest `status` message of a node (both node kinds).</summary>
    public sealed class NodeStatus
    {
        public string DeviceId;
        public string DeviceKind;
        public double? BatteryPct;
        public int? Subscribers;
        public bool? OledAvailable;
        public int? UnknownMsgs;
        public string Firmware;
    }

    /// <summary>
    /// Thread-safe sample history of the last <c>windowMs</c> (default 10 s) by sample time. Writers are the UDP
    /// receive thread, readers the main thread; every access takes one short lock. A hard item cap protects
    /// against a stuck timestamp.
    /// </summary>
    public sealed class TimeSeriesRing<T> where T : struct
    {
        private readonly object _gate = new object();
        private readonly Queue<T> _items = new Queue<T>();
        private readonly Func<T, double> _timeOf;
        private readonly double _windowMs;
        private readonly int _maxItems;
        private double _maxT = double.NegativeInfinity;
        private T _latest;
        private bool _hasLatest;

        public TimeSeriesRing(Func<T, double> timeOf, double windowMs = 10000, int maxItems = 20000)
        {
            _timeOf = timeOf; _windowMs = windowMs; _maxItems = maxItems;
        }

        public int Count { get { lock (_gate) return _items.Count; } }

        public void Add(T item)
        {
            lock (_gate)
            {
                double t = _timeOf(item);
                _items.Enqueue(item);
                if (t >= _maxT) { _maxT = t; _latest = item; _hasLatest = true; }
                while (_items.Count > 0 && (_maxT - _timeOf(_items.Peek()) > _windowMs || _items.Count > _maxItems))
                    _items.Dequeue();
            }
        }

        public bool TryGetLatest(out T item)
        {
            lock (_gate) { item = _latest; return _hasLatest; }
        }

        public T[] Snapshot()
        {
            lock (_gate) return _items.ToArray();
        }

        /// <summary>Samples with time in [fromMs, toMs].</summary>
        public T[] Range(double fromMs, double toMs)
        {
            lock (_gate)
            {
                var list = new List<T>();
                foreach (var it in _items)
                {
                    double t = _timeOf(it);
                    if (t >= fromMs && t <= toMs) list.Add(it);
                }
                return list.ToArray();
            }
        }

        public void Clear()
        {
            lock (_gate) { _items.Clear(); _hasLatest = false; _maxT = double.NegativeInfinity; }
        }
    }

    /// <summary>
    /// Maps a node's own millis() clock to session ms. The offset is the median of (session receive time - device
    /// time) over the first 20 packets, then re-estimated from a fresh batch of 20 every 30 s. Until the first batch
    /// is full the median of what has arrived so far is used. Network delay is not measured separately: it is
    /// part of the offset (positive, and jittery by a few ms on a hotspot, which the median suppresses).
    /// Not thread-safe; the owner serialises access.
    /// </summary>
    public sealed class ClockOffsetEstimator
    {
        public const int DefaultBatch = 20;
        public const double DefaultReestimateMs = 30000;

        private readonly int _batch;
        private readonly double _reestimateMs;
        private readonly List<double> _collect = new List<double>();
        private bool _collecting = true;
        private double _nextReestimateSessionMs;

        public double? OffsetMs { get; private set; }
        public bool Settled { get; private set; }   // true once a full batch has been used
        public int Reestimates { get; private set; }

        public ClockOffsetEstimator(int batch = DefaultBatch, double reestimateMs = DefaultReestimateMs)
        {
            _batch = batch; _reestimateMs = reestimateMs;
        }

        public void Observe(double deviceMs, double sessionMs)
        {
            if (!_collecting && Settled && sessionMs >= _nextReestimateSessionMs)
            {
                _collecting = true;
                _collect.Clear();
            }
            if (!_collecting) return;

            _collect.Add(sessionMs - deviceMs);
            if (_collect.Count >= _batch)
            {
                OffsetMs = Median(_collect);
                if (Settled) Reestimates++;
                Settled = true;
                _collecting = false;
                _nextReestimateSessionMs = sessionMs + _reestimateMs;
            }
            else if (!Settled)
            {
                OffsetMs = Median(_collect);   // provisional
            }
        }

        public double ToSessionMs(double deviceMs) => deviceMs + (OffsetMs ?? 0);

        private static double Median(List<double> v)
        {
            var a = v.ToArray();
            Array.Sort(a);
            int n = a.Length;
            return n % 2 == 1 ? a[n / 2] : 0.5 * (a[n / 2 - 1] + a[n / 2]);
        }
    }
}
