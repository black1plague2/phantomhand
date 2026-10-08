using System.Collections.Generic;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Collects the window impact-2 s .. impact+1.5 s for the threat response (U3 item 5): the raw right wrist (one sample per new
    /// hand-tracking DataVersion), Node A accel (SleeveSensorClient.Imu) and Node B EMG envelope (SleeveSensorClient.Emg), then
    /// runs ThreatResponseAnalyzer. Missing streams give nulls plus a quality flag; hand tracking alone is a valid fallback (PRD 11).
    /// Plain C#; the rings may be null (node not present).
    /// </summary>
    public sealed class ThreatResponseCollector
    {
        public const double WindowBeforeMs = ThreatResponseAnalyzer.BaselineMs;
        public const double WindowAfterMs = ThreatResponseAnalyzer.ResponseMs;

        private readonly System.Func<TimeSeriesRing<ImuSample>> _imu;
        private readonly System.Func<TimeSeriesRing<EmgSample>> _emg;
        private readonly List<Vec3Sample> _wrist = new List<Vec3Sample>();
        private int _lastVersion = int.MinValue;
        private double? _impactMs;

        public bool Armed { get { return _impactMs.HasValue; } }
        public int WristSamples { get { return _wrist.Count; } }
        public ThreatResponse LastResult { get; private set; }

        public ThreatResponseCollector(System.Func<TimeSeriesRing<ImuSample>> imu, System.Func<TimeSeriesRing<EmgSample>> emg)
        {
            _imu = imu; _emg = emg;
        }

        /// <summary>Record a raw wrist position (calibration space, metres) when the tracking DataVersion changed.</summary>
        public bool PushWrist(double tMs, Vector3 pos, int dataVersion)
        {
            if (dataVersion == _lastVersion) return false;
            _lastVersion = dataVersion;
            _wrist.Add(new Vec3Sample(tMs, pos.x, pos.y, pos.z));
            double cut = _impactMs.HasValue ? _impactMs.Value - WindowBeforeMs - 200 : tMs - 6000;
            int drop = 0;
            while (drop < _wrist.Count - 1 && _wrist[drop].TMs < cut) drop++;
            if (drop > 0) _wrist.RemoveRange(0, drop);
            return true;
        }

        public void Arm(double impactMs) { _impactMs = impactMs; LastResult = null; }
        public void Disarm() { _impactMs = null; }

        /// <summary>Returns the finished analysis once the 1.5 s after the impact have passed (or force = true), else null.</summary>
        public ThreatResponse Tick(double nowMs, bool force = false)
        {
            if (!_impactMs.HasValue) return null;
            double impact = _impactMs.Value;
            if (!force && nowMs < impact + WindowAfterMs) return null;
            double from = impact - WindowBeforeMs, to = impact + WindowAfterMs;
            var accel = new List<Vec3Sample>();
            var ring = _imu != null ? _imu() : null;
            if (ring != null)
                foreach (var s in ring.Range(from, to)) accel.Add(new Vec3Sample(s.TMs, s.Ax, s.Ay, s.Az));
            var emg = new List<ScalarSample>();
            var eRing = _emg != null ? _emg() : null;
            if (eRing != null)
                foreach (var s in eRing.Range(from, to)) emg.Add(new ScalarSample(s.TMs, s.Value));
            var wrist = new List<Vec3Sample>();
            foreach (var w in _wrist) if (w.TMs >= from - 50 && w.TMs <= to + 50) wrist.Add(w);
            LastResult = ThreatResponseAnalyzer.Analyze(impact, wrist, accel, emg);
            _impactMs = null;
            return LastResult;
        }
    }
}
