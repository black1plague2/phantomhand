using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Opus.Games.PhantomHand.Tests
{
    internal sealed class Noise
    {
        private readonly Random _r;
        public Noise(int seed) { _r = new Random(seed); }
        public double Gauss(double sd)
        {
            double u1 = 1.0 - _r.NextDouble(), u2 = _r.NextDouble();
            return sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }

    public class DriftProbeTests
    {
        private static ProbeState Run(DriftProbe p, double fromMs, double toMs, double hz, Noise n, double sdM, double x = -0.15, bool tracked = true, double[] right = null)
        {
            var st = p.State;
            double dt = 1000.0 / hz;
            for (double t = fromMs; t <= toMs; t += dt)
                st = p.Update(t, tracked, new[] { x + n.Gauss(sdM), 0.80 + n.Gauss(sdM), 0.30 + n.Gauss(sdM) }, right);
            return st;
        }

        [Test]
        public void StillFinger_ConfirmsAfter1500ms_NotBefore()
        {
            var p = new DriftProbe(); p.Begin();
            Assert.AreEqual(ProbeState.Holding, Run(p, 0, 1400, 72, new Noise(1), 0.0015));
            Assert.Less(p.Progress01, 1.0);
            Assert.AreEqual(ProbeState.Confirmed, Run(p, 1410, 1700, 72, new Noise(2), 0.0015));
            Assert.AreEqual(-0.15, p.PerceivedXm.Value, 0.003);
        }

        [Test]
        public void JitteryFinger_IsNeverConfirmed()
        {
            var p = new DriftProbe(); p.Begin();
            Assert.AreNotEqual(ProbeState.Confirmed, Run(p, 0, 6000, 72, new Noise(3), 0.010));
        }

        [Test]
        public void Untracked_ResetsTheWindow()
        {
            var p = new DriftProbe(); p.Begin();
            Run(p, 0, 1000, 72, new Noise(4), 0.001);
            Run(p, 1010, 1100, 72, new Noise(5), 0.001, tracked: false);
            Assert.AreEqual(ProbeState.Waiting, p.State);
            Assert.AreEqual(ProbeState.Holding, Run(p, 1110, 2400, 72, new Noise(6), 0.001));   // only 1.3 s since resume
            Assert.AreEqual(ProbeState.Confirmed, Run(p, 2410, 2800, 72, new Noise(7), 0.001));
        }

        [Test]
        public void RightHandMovedMoreThan3cm_PausesTheProbe()
        {
            var p = new DriftProbe(); p.Begin();
            p.SetRightReference(new[] { 0.0, 0.75, 0.3 });
            var st = Run(p, 0, 3000, 72, new Noise(8), 0.001, right: new[] { 0.05, 0.75, 0.3 });
            Assert.AreEqual(ProbeState.ArmMoved, st);
            Assert.AreEqual(ProbeState.Confirmed, Run(p, 3010, 4700, 72, new Noise(9), 0.001, right: new[] { 0.005, 0.75, 0.3 }));
        }

        [Test]
        public void Drift_PositiveTowardTheVirtualHandOnTheLeft()
        {
            Assert.AreEqual(2.0, DriftProbe.DriftCmFor(-0.17, -0.15, -1), 1e-9);
            Assert.AreEqual(-1.0, DriftProbe.DriftCmFor(-0.14, -0.15, -1), 1e-9);
        }

        [Test]
        public void Result_ComputesDriftWhenConfirmed_NullWhenNot()
        {
            var p = new DriftProbe(); p.Begin();
            Run(p, 0, 1800, 72, new Noise(10), 0.0005, x: -0.18);
            var r = p.Result("pre", -0.15);
            Assert.IsTrue(r.Confirmed);
            Assert.AreEqual(3.0, r.DriftCm.Value, 0.2);
            var q = new DriftProbe(); q.Begin();
            var u = q.Result("post", -0.15);
            Assert.IsFalse(u.Confirmed); Assert.IsNull(u.DriftCm); Assert.IsNull(u.PerceivedXm);
        }

        [Test]
        public void RightHandMoved_StaticHelper()
        {
            Assert.IsTrue(DriftProbe.RightHandMoved(new[] { 0.0, 0, 0 }, new[] { 0.031, 0, 0 }));
            Assert.IsFalse(DriftProbe.RightHandMoved(new[] { 0.0, 0, 0 }, new[] { 0.029, 0, 0 }));
        }
    }

    public class ThreatResponseAnalyzerTests
    {
        private const double Impact = 10000;

        private static List<Vec3Sample> Wrist(double hz, double motionOnsetAfterImpactMs, double accel, int seed, double noiseM = 0.00005)
        {
            var n = new Noise(seed); var l = new List<Vec3Sample>();
            double tOn = Impact + motionOnsetAfterImpactMs;
            for (double t = Impact - 2200; t <= Impact + 1700; t += 1000.0 / hz)
            {
                double dt = Math.Max(0, (t - tOn) / 1000.0);
                double x = motionOnsetAfterImpactMs < 0 ? 0 : 0.5 * accel * dt * dt;
                l.Add(new Vec3Sample(t, x + n.Gauss(noiseM), 0.75 + n.Gauss(noiseM), 0.3 + n.Gauss(noiseM)));
            }
            return l;
        }

        private static List<Vec3Sample> Accel(double joltAfterImpactMs, double jolt, int seed)
        {
            var n = new Noise(seed); var l = new List<Vec3Sample>();
            for (double t = Impact - 2200; t <= Impact + 1700; t += 10)
            {
                double mag = 9.81 + n.Gauss(0.02);
                if (jolt > 0 && t >= Impact + joltAfterImpactMs && t < Impact + joltAfterImpactMs + 100) mag += jolt;
                l.Add(new Vec3Sample(t, 0, 0, mag));
            }
            return l;
        }

        private static List<ScalarSample> Emg(double burstAfterImpactMs, double level, int seed)
        {
            var n = new Noise(seed); var l = new List<ScalarSample>();
            for (double t = Impact - 2200; t <= Impact + 1700; t += 10)
            {
                double v = 400 + n.Gauss(5);
                if (level > 0 && t >= Impact + burstAfterImpactMs && t < Impact + burstAfterImpactMs + 300) v = level + n.Gauss(5);
                l.Add(new ScalarSample(t, v));
            }
            return l;
        }

        [Test]
        public void WristLatency_Within10ms_At72Hz()
        {
            // speed = a * dt, crosses 0.15 m/s at onset + 0.15 / a
            double a = 3.0, onset = 180;
            var r = ThreatResponseAnalyzer.Analyze(Impact, Wrist(72, onset, a, 1), null, null);
            Assert.AreEqual(onset + 0.15 / a * 1000.0, r.WristLatencyMs.Value, 10);
            Assert.AreEqual("ok", r.Quality["wrist"]);
        }

        [Test]
        public void WristLatency_Within10ms_At30Hz()
        {
            var r = ThreatResponseAnalyzer.Analyze(Impact, Wrist(30, 220, 2.5, 2), null, null);
            Assert.AreEqual(220 + 0.15 / 2.5 * 1000.0, r.WristLatencyMs.Value, 10);
        }

        [Test]
        public void WristPeak_MatchesSyntheticSpeed()
        {
            var r = ThreatResponseAnalyzer.Analyze(Impact, Wrist(72, 100, 3.0, 3), null, null);
            // at the end of the window dt ~1.4 s => v = 4.2 m/s (finite differences, midpoint) -- check close to analytic
            double expected = 3.0 * 1.4;
            Assert.AreEqual(expected, r.WristPeakMps.Value, 0.25);
        }

        [Test]
        public void NoMovement_NoWristLatency_NoFalseTrigger()
        {
            var r = ThreatResponseAnalyzer.Analyze(Impact, Wrist(72, -1, 0, 4), null, null);
            Assert.IsNull(r.WristLatencyMs);
            Assert.Less(r.WristPeakMps.Value, 0.15);
        }

        [Test]
        public void EmgLatency_Within10ms_AndPeakIsMultipleOfBaselineRms()
        {
            var r = ThreatResponseAnalyzer.Analyze(Impact, null, null, Emg(120, 1200, 5));
            Assert.AreEqual(120, r.EmgLatencyMs.Value, 10);
            Assert.AreEqual(3.0, r.EmgPeakX.Value, 0.2);
            Assert.AreEqual("ok", r.Quality["emg"]);
        }

        [Test]
        public void EmgFlatSignal_NoLatency_NoFalseTrigger_AcrossSeeds()
        {
            for (int seed = 1; seed <= 25; seed++)
            {
                var r = ThreatResponseAnalyzer.Analyze(Impact, null, null, Emg(0, 0, seed));
                Assert.IsNull(r.EmgLatencyMs, "seed " + seed);
                Assert.AreEqual(1.0, r.EmgPeakX.Value, 0.15);
            }
        }

        [Test]
        public void SingleSampleSpike_IsNotAFlinch()
        {
            var e = Emg(0, 0, 6);
            e[e.FindIndex(s => s.TMs >= Impact + 300)] = new ScalarSample(Impact + 300, 5000);
            Assert.IsNull(ThreatResponseAnalyzer.Analyze(Impact, null, null, e).EmgLatencyMs);
        }

        [Test]
        public void ImuPeakAndLatency()
        {
            var r = ThreatResponseAnalyzer.Analyze(Impact, null, Accel(150, 3.0, 7), null);
            Assert.AreEqual(3.0, r.ImuPeak.Value, 0.15);
            Assert.AreEqual(150, r.ImuLatencyMs.Value, 10);
            Assert.AreEqual("ok", r.Quality["imu"]);
        }

        [Test]
        public void MissingStreams_GiveNullsAndMissingFlags()
        {
            var r = ThreatResponseAnalyzer.Analyze(Impact, Wrist(72, 150, 3, 8), null, null);
            Assert.IsNull(r.EmgPeakX); Assert.IsNull(r.EmgLatencyMs); Assert.IsNull(r.ImuPeak);
            Assert.AreEqual("missing", r.Quality["emg"]); Assert.AreEqual("missing", r.Quality["imu"]);
            Assert.IsNotNull(r.WristLatencyMs);   // hand tracking alone is a valid fallback
        }

        [Test]
        public void SparseWrist_IsDegraded()
        {
            var w = new List<Vec3Sample>();
            for (int i = 0; i < 8; i++) w.Add(new Vec3Sample(Impact - 1000 + i * 400, 0, 0.75, 0.3));
            Assert.AreEqual("degraded", ThreatResponseAnalyzer.Analyze(Impact, w, null, null).Quality["wrist"]);
        }

        [Test]
        public void SparseEmg_IsDegraded()
        {
            var e = new List<ScalarSample>();
            for (double t = Impact - 2000; t < Impact + 1500; t += 50) e.Add(new ScalarSample(t, 400));
            Assert.AreEqual("degraded", ThreatResponseAnalyzer.Analyze(Impact, null, null, e).Quality["emg"]);
        }
    }
}
