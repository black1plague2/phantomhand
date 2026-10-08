using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Opus.Games.PhantomHand;
using Opus.Sdk;

namespace Opus.Shell
{
    /// <summary>
    /// Builds the Phantom Hand parts of the live status (contracts/LIVE_PROTOCOL.md v0.2): <c>game_state</c> and <c>trace</c>, and the
    /// live <c>metrics_tick</c> payload. Pure functions over plain values (EditMode-testable); the scene controller supplies the values.
    /// </summary>
    public static class PhantomLiveStatus
    {
        /// <summary>status.payload.game_state. All four keys are always present (schema). remaining_s and emg_level may be null.</summary>
        public static JObject GameState(string phase, string condition, double? remainingS, bool hapticConnected, bool bioConnected, double? emgLevel)
        {
            var bio = new JObject { ["connected"] = bioConnected, ["emg_level"] = emgLevel.HasValue && bioConnected ? (JToken)Math.Round(Clamp01(emgLevel.Value), 3) : JValue.CreateNull() };
            return new JObject
            {
                ["phase"] = string.IsNullOrEmpty(phase) ? "idle" : phase,
                ["condition"] = condition == "sync" || condition == "async" ? (JToken)condition : JValue.CreateNull(),
                ["remaining_s"] = remainingS.HasValue && !double.IsNaN(remainingS.Value) ? (JToken)Math.Round(Math.Max(0, remainingS.Value), 1) : JValue.CreateNull(),
                ["nodes"] = new JObject
                {
                    ["haptic"] = new JObject { ["connected"] = hapticConnected },
                    ["bio"] = bio,
                },
            };
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        /// <summary>Remaining seconds worth showing: null in Idle/Done/Witness-less states and for non-finite values.</summary>
        public static double? RemainingFor(PhPhase phase, double remainingS)
        {
            if (phase == PhPhase.Idle || phase == PhPhase.Done) return null;
            if (double.IsNaN(remainingS) || double.IsInfinity(remainingS)) return null;
            return Math.Max(0, remainingS);
        }

        // ---- live metrics_tick ---------------------------------------------------------------------------------

        /// <summary>
        /// metrics_tick payload for one condition from what is known so far (null when nothing is known yet). Shape per the schema
        /// note: metric id to {value, unit, quality}; plus a "condition" entry naming sync/async. Drift, ownership and flinch
        /// appear as they become known; quality is "degraded" when the threat stream behind a value was degraded.
        /// </summary>
        public static JObject MetricsFor(ConditionResult r)
        {
            if (r == null) return null;
            var m = new JObject();
            if (r.DriftChangeCm.HasValue) Add(m, "drift_change_cm", r.DriftChangeCm.Value, "cm", "ok");
            if (r.Ownership.HasValue) Add(m, "ownership", r.Ownership.Value, "score", "ok");
            if (r.Control.HasValue) Add(m, "control", r.Control.Value, "score", "ok");
            var t = r.Threat;
            if (t != null)
            {
                if (t.EmgPeakX.HasValue) Add(m, "flinch_emg_peak_x", t.EmgPeakX.Value, "x_baseline", QualityOf(t, "emg"));
                if (t.EmgLatencyMs.HasValue) Add(m, "flinch_emg_latency_ms", t.EmgLatencyMs.Value, "ms", QualityOf(t, "emg"));
                if (t.ImuPeak.HasValue) Add(m, "flinch_imu_peak", t.ImuPeak.Value, "m/s2", QualityOf(t, "imu"));
                if (t.WristPeakMps.HasValue) Add(m, "flinch_wrist_peak_mps", t.WristPeakMps.Value, "m/s", QualityOf(t, "wrist"));
                if (t.WristLatencyMs.HasValue) Add(m, "flinch_wrist_latency_ms", t.WristLatencyMs.Value, "ms", QualityOf(t, "wrist"));
            }
            if (m.Count == 0) return null;
            m["condition"] = new JObject { ["value"] = PhNames.Of(r.Condition), ["unit"] = "label", ["quality"] = "ok" };
            return m;
        }

        private static void Add(JObject m, string id, double v, string unit, string quality)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return;
            m[id] = new JObject { ["value"] = Math.Round(v, 3), ["unit"] = unit, ["quality"] = quality };
        }

        private static string QualityOf(ThreatResponse t, string stream)
        {
            string q;
            if (t.Quality != null && t.Quality.TryGetValue(stream, out q) && !string.IsNullOrEmpty(q) && q != "missing") return q;
            return "ok";
        }
    }

    /// <summary>
    /// 20 Hz live trace (03-SPEC D4, contracts v0.2 <c>status.payload.trace</c>): the EMG envelope (raw ADC) and |accel| (m/s2) averaged
    /// into 50 ms bins, newest bins since the previous call, at most 100 per array. Bins are emitted only once they are 100 ms old so
    /// jittery packets land in the right bin; a bin with no sample repeats the stream's last value (leading gaps use the first value of
    /// the call) so index i is always t0_ms + 50 ms * i. A stream with no sample at all in the call gives an empty array.
    /// Plain C# over arrays (EditMode-testable); <see cref="Build(TimeSeriesRing{EmgSample},TimeSeriesRing{ImuSample},double)"/> adapts the SDK rings.
    /// </summary>
    public sealed class TraceDownsampler
    {
        public const double BinMs = 50.0;
        public const double SettleMs = 100.0;
        public const int MaxPerArray = 100;
        public const int FsHz = 20;

        private double _cursorMs = double.NaN;
        private double _lastEmg = double.NaN, _lastAcc = double.NaN;

        /// <summary>Forget the cursor (call at session start: the clock restarts at 0).</summary>
        public void Reset() { _cursorMs = double.NaN; _lastEmg = _lastAcc = double.NaN; }

        public JObject Build(TimeSeriesRing<EmgSample> emg, TimeSeriesRing<ImuSample> imu, double nowMs)
        {
            double end = EndFor(nowMs);
            if (double.IsNaN(_cursorMs)) _cursorMs = Math.Max(0, end - 500.0);
            double from = _cursorMs;
            if (end - from < BinMs) return null;
            // Pull the window once per ring.
            var e = emg == null ? new List<KeyValuePair<double, double>>() : ToPairs(emg.Range(from, end), s => s.TMs, s => s.Value);
            var a = imu == null ? new List<KeyValuePair<double, double>>() : ToPairs(imu.Range(from, end), s => s.TMs, s => s.AccelMagnitude);
            return Build(e, a, nowMs);
        }

        /// <summary>Pure core: (t_ms, value) pairs of the new window for each stream. Advances the cursor. Null when less than one bin is ready.</summary>
        public JObject Build(IList<KeyValuePair<double, double>> emg, IList<KeyValuePair<double, double>> accel, double nowMs)
        {
            double end = EndFor(nowMs);
            if (double.IsNaN(_cursorMs)) _cursorMs = Math.Max(0, end - 500.0);
            int bins = (int)Math.Floor((end - _cursorMs) / BinMs);
            if (bins < 1) return null;
            if (bins > MaxPerArray)
            {
                _cursorMs += (bins - MaxPerArray) * BinMs; // skip the oldest: only the newest 100 bins are sent
                bins = MaxPerArray;
            }
            double t0 = _cursorMs;
            var emgBins = Bin(emg, t0, bins, ref _lastEmg);
            var accBins = Bin(accel, t0, bins, ref _lastAcc);
            _cursorMs = t0 + bins * BinMs;
            return new JObject
            {
                ["emg_env"] = new JArray(Round(emgBins, 1)),
                ["accel_mag"] = new JArray(Round(accBins, 3)),
                ["t0_ms"] = Math.Round(t0, 1),
                ["fs_hz"] = FsHz,
            };
        }

        private static double EndFor(double nowMs) { return Math.Floor((nowMs - SettleMs) / BinMs) * BinMs; }

        private static List<KeyValuePair<double, double>> ToPairs<T>(T[] src, Func<T, double> t, Func<T, double> v)
        {
            var l = new List<KeyValuePair<double, double>>(src.Length);
            foreach (var s in src) l.Add(new KeyValuePair<double, double>(t(s), v(s)));
            return l;
        }

        private static double[] Bin(IList<KeyValuePair<double, double>> samples, double t0, int bins, ref double last)
        {
            var sum = new double[bins]; var n = new int[bins];
            if (samples != null)
                foreach (var kv in samples)
                {
                    if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value)) continue;
                    int i = (int)Math.Floor((kv.Key - t0) / BinMs);
                    if (i < 0 || i >= bins) continue;
                    sum[i] += kv.Value; n[i]++;
                }
            int first = -1;
            for (int i = 0; i < bins; i++) if (n[i] > 0) { first = i; break; }
            if (first < 0) { return new double[0]; } // nothing in this call: an empty array (the node is silent)
            var res = new double[bins];
            double carry = double.IsNaN(last) ? sum[first] / n[first] : last;
            for (int i = 0; i < bins; i++)
            {
                if (n[i] > 0) carry = sum[i] / n[i];
                res[i] = carry;
            }
            last = res[bins - 1];
            return res;
        }

        private static List<double> Round(double[] v, int digits)
        {
            var l = new List<double>(v.Length);
            foreach (var d in v) l.Add(Math.Round(d, digits));
            return l;
        }
    }
}
