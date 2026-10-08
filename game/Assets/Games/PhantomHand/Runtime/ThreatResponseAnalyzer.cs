using System;
using System.Collections.Generic;

namespace Opus.Games.PhantomHand
{
    public struct Vec3Sample
    {
        public double TMs, X, Y, Z;
        public Vec3Sample(double t, double x, double y, double z) { TMs = t; X = x; Y = y; Z = z; }
    }

    public struct ScalarSample
    {
        public double TMs, V;
        public ScalarSample(double t, double v) { TMs = t; V = v; }
    }

    /// <summary>On-device preliminary threat response (03-SPEC section 7 threat_response). Null = not measurable.</summary>
    public sealed class ThreatResponse
    {
        public double? WristPeakMps;
        public double? WristLatencyMs;
        public double? ImuPeak;
        public double? ImuLatencyMs;
        public double? EmgPeakX;
        public double? EmgLatencyMs;
        /// <summary>Per stream: "ok" | "degraded" | "missing" (keys: wrist, imu, emg).</summary>
        public Dictionary<string, string> Quality = new Dictionary<string, string>();
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Analyses the window 2 s before to 1.5 s after the stone impact. Baseline = [impact-2000, impact).
    /// EMG latency = first run of 3 consecutive envelope samples above baseline mean + 3 SD (>= 30 ms, like
    /// emg_burst); peak is expressed as a multiple of the baseline RMS. Wrist latency = first crossing of 0.15 m/s
    /// (linearly interpolated). IMU = peak deviation of |a| from its baseline mean; latency = first crossing of
    /// mean + 3 SD. Missing streams give nulls plus a quality flag; hand tracking alone is a valid fallback.
    /// </summary>
    public static class ThreatResponseAnalyzer
    {
        public const double BaselineMs = 2000;
        public const double ResponseMs = 1500;
        public const double WristSpeedThresholdMps = 0.15;
        public const int EmgRunSamples = 3;
        public const double SdMultiplier = 3.0;

        public static ThreatResponse Analyze(double impactMs, IList<Vec3Sample> wrist, IList<Vec3Sample> accel, IList<ScalarSample> emg)
        {
            var r = new ThreatResponse();
            AnalyzeWrist(r, impactMs, wrist);
            AnalyzeImu(r, impactMs, accel);
            AnalyzeEmg(r, impactMs, emg);
            return r;
        }

        // -- wrist ---------------------------------------------------------------------------------------------
        private static void AnalyzeWrist(ThreatResponse r, double impact, IList<Vec3Sample> s)
        {
            if (s == null || s.Count < 3) { r.Quality["wrist"] = "missing"; r.Notes.Add("wrist: no samples"); return; }
            var speeds = new List<ScalarSample>();
            for (int i = 1; i < s.Count; i++)
            {
                double dt = (s[i].TMs - s[i - 1].TMs) / 1000.0;
                if (dt <= 0) continue;
                double dx = s[i].X - s[i - 1].X, dy = s[i].Y - s[i - 1].Y, dz = s[i].Z - s[i - 1].Z;
                speeds.Add(new ScalarSample((s[i].TMs + s[i - 1].TMs) / 2.0, Math.Sqrt(dx * dx + dy * dy + dz * dz) / dt));
            }
            int baseN = 0, respN = 0; double maxGap = 0;
            for (int i = 0; i < s.Count; i++)
            {
                double t = s[i].TMs;
                if (t >= impact - BaselineMs && t < impact) baseN++;
                else if (t >= impact && t <= impact + ResponseMs) respN++;
                if (i > 0 && t >= impact - BaselineMs && t <= impact + ResponseMs) maxGap = Math.Max(maxGap, t - s[i - 1].TMs);
            }
            if (respN == 0) { r.Quality["wrist"] = "missing"; r.Notes.Add("wrist: no samples after impact"); return; }
            bool degraded = baseN < 10 || respN < 10 || maxGap > 200;
            r.Quality["wrist"] = degraded ? "degraded" : "ok";
            if (degraded) r.Notes.Add("wrist: sparse samples (base " + baseN + ", response " + respN + ", max gap " + (int)maxGap + " ms)");

            double peak = 0;
            double? latency = null;
            for (int i = 0; i < speeds.Count; i++)
            {
                var sp = speeds[i];
                if (sp.TMs < impact || sp.TMs > impact + ResponseMs) continue;
                if (sp.V > peak) peak = sp.V;
                if (latency == null && sp.V > WristSpeedThresholdMps)
                {
                    double tCross = sp.TMs;
                    if (i > 0 && speeds[i - 1].V < WristSpeedThresholdMps && speeds[i].V > speeds[i - 1].V)
                    {
                        double f = (WristSpeedThresholdMps - speeds[i - 1].V) / (speeds[i].V - speeds[i - 1].V);
                        tCross = speeds[i - 1].TMs + f * (speeds[i].TMs - speeds[i - 1].TMs);
                    }
                    latency = Math.Max(0, tCross - impact);
                }
            }
            r.WristPeakMps = peak;
            r.WristLatencyMs = latency;
        }

        // -- IMU -----------------------------------------------------------------------------------------------
        private static void AnalyzeImu(ThreatResponse r, double impact, IList<Vec3Sample> a)
        {
            if (a == null || a.Count == 0) { r.Quality["imu"] = "missing"; r.Notes.Add("imu: node A not streaming"); return; }
            var baseVals = new List<double>();
            var resp = new List<ScalarSample>();
            foreach (var v in a)
            {
                double mag = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
                if (v.TMs >= impact - BaselineMs && v.TMs < impact) baseVals.Add(mag);
                else if (v.TMs >= impact && v.TMs <= impact + ResponseMs) resp.Add(new ScalarSample(v.TMs, mag));
            }
            if (baseVals.Count < 5 || resp.Count < 5) { r.Quality["imu"] = resp.Count == 0 ? "missing" : "degraded"; r.Notes.Add("imu: too few samples"); if (resp.Count == 0 || baseVals.Count == 0) return; }
            else r.Quality["imu"] = "ok";

            double mean, sd;
            MeanSd(baseVals, out mean, out sd);
            sd = Math.Max(sd, Math.Max(1e-6, Math.Abs(mean) * 0.002));
            double peak = 0; double? lat = null; int run = 0;
            for (int i = 0; i < resp.Count; i++)
            {
                double dev = Math.Abs(resp[i].V - mean);
                if (dev > peak) peak = dev;
                if (resp[i].V > mean + SdMultiplier * sd)
                {
                    run++;
                    if (run == EmgRunSamples && lat == null) lat = resp[i - EmgRunSamples + 1].TMs - impact;
                }
                else run = 0;
            }
            r.ImuPeak = peak;
            r.ImuLatencyMs = lat;
        }

        // -- EMG -----------------------------------------------------------------------------------------------
        private static void AnalyzeEmg(ThreatResponse r, double impact, IList<ScalarSample> e)
        {
            if (e == null || e.Count == 0) { r.Quality["emg"] = "missing"; r.Notes.Add("emg: node B not streaming"); return; }
            var baseVals = new List<double>();
            var resp = new List<ScalarSample>();
            foreach (var v in e)
            {
                if (v.TMs >= impact - BaselineMs && v.TMs < impact) baseVals.Add(v.V);
                else if (v.TMs >= impact && v.TMs <= impact + ResponseMs) resp.Add(v);
            }
            if (resp.Count == 0 || baseVals.Count == 0) { r.Quality["emg"] = "missing"; r.Notes.Add("emg: window not covered"); return; }
            // Expect ~100 Hz: baseline 200 and response 150 samples. Under 50 % => degraded.
            bool degraded = baseVals.Count < 100 || resp.Count < 75;
            r.Quality["emg"] = degraded ? "degraded" : "ok";
            if (degraded) r.Notes.Add("emg: sparse envelope (base " + baseVals.Count + ", response " + resp.Count + ")");

            double mean, sd;
            MeanSd(baseVals, out mean, out sd);
            double sumSq = 0; foreach (var v in baseVals) sumSq += v * v;
            double rms = Math.Sqrt(sumSq / baseVals.Count);
            sd = Math.Max(sd, Math.Max(1e-6, Math.Abs(mean) * 0.01));
            double thr = mean + SdMultiplier * sd;

            double peak = double.NegativeInfinity; double? lat = null; int run = 0;
            for (int i = 0; i < resp.Count; i++)
            {
                if (resp[i].V > peak) peak = resp[i].V;
                if (resp[i].V > thr) { run++; if (run == EmgRunSamples && lat == null) lat = resp[i - EmgRunSamples + 1].TMs - impact; }
                else run = 0;
            }
            r.EmgPeakX = rms > 1e-9 ? peak / rms : (double?)null;
            r.EmgLatencyMs = lat;
        }

        private static void MeanSd(IList<double> v, out double mean, out double sd)
        {
            double sum = 0; foreach (var x in v) sum += x;
            mean = sum / v.Count;
            double sq = 0; foreach (var x in v) sq += (x - mean) * (x - mean);
            sd = Math.Sqrt(sq / v.Count);
        }
    }
}
