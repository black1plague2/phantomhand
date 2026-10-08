using System;
using System.Collections.Generic;

namespace Opus.Games.PhantomHand
{
    public enum ProbeState { Waiting, Holding, Confirmed, ArmMoved }

    /// <summary>Outcome of one drift probe. Positions are metres in calibration space (+x right); drift is cm.</summary>
    public sealed class ProbeResult
    {
        public string When;            // "pre" | "post"
        public bool Confirmed;
        public double? PerceivedXm;    // where the participant pointed (left index tip, x)
        public double? ActualXm;       // real right index tip x
        public double? DriftCm;        // + = perceived position shifted toward the virtual hand
    }

    /// <summary>
    /// FR-VR-04 drift probe maths. The participant points with the left index finger to where the right index feels;
    /// the answer is accepted once the fingertip has been still (every sample within 1 cm of the window centroid)
    /// for 1.5 s. If the right hand moved more than 3 cm since calibration the probe pauses (ArmMoved) and the
    /// window restarts. Drift sign: positive when the pointed position lies toward the virtual hand (which sits
    /// to the LEFT of the real one, so toward -x).
    /// </summary>
    public sealed class DriftProbe
    {
        public const double StillRadiusM = 0.01;
        public const double HoldMs = 1500;
        public const double ArmMoveLimitM = 0.03;

        private readonly List<double[]> _samples = new List<double[]>(); // t, x, y, z
        private double _windowStartMs;
        private bool _started;
        private double[] _rightRef;

        /// <summary>+1 if the virtual hand lies toward +x of the real hand, -1 if toward -x (default: left of a right arm).</summary>
        public double TowardVirtualSignX = -1;

        public ProbeState State { get; private set; } = ProbeState.Waiting;
        public double Progress01 { get; private set; }
        public double? PerceivedXm { get; private set; }

        /// <summary>Right wrist position stored at calibration, used for the "arm moved" check.</summary>
        public void SetRightReference(double[] wristPos) { _rightRef = wristPos == null ? null : (double[])wristPos.Clone(); }

        public void Begin()
        {
            _samples.Clear();
            _started = false;
            State = ProbeState.Waiting;
            Progress01 = 0;
            PerceivedXm = null;
        }

        /// <summary>Feed one frame. leftTip null/untracked resets the window. rightWrist is optional.</summary>
        public ProbeState Update(double nowMs, bool leftTracked, double[] leftTip, double[] rightWrist = null)
        {
            if (State == ProbeState.Confirmed) return State;

            if (_rightRef != null && rightWrist != null && Dist(_rightRef, rightWrist) > ArmMoveLimitM)
            {
                Reset();
                State = ProbeState.ArmMoved;
                return State;
            }

            if (!leftTracked || leftTip == null)
            {
                Reset();
                State = ProbeState.Waiting;
                return State;
            }

            if (!_started) { _started = true; _windowStartMs = nowMs; }
            _samples.Add(new[] { nowMs, leftTip[0], leftTip[1], leftTip[2] });

            double[] c = Centroid();
            double maxDev = 0;
            foreach (var s in _samples)
            {
                double dx = s[1] - c[0], dy = s[2] - c[1], dz = s[3] - c[2];
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > maxDev) maxDev = d;
            }
            if (maxDev > StillRadiusM)
            {
                // moved: restart the window from this sample
                _samples.Clear();
                _samples.Add(new[] { nowMs, leftTip[0], leftTip[1], leftTip[2] });
                _windowStartMs = nowMs;
                Progress01 = 0;
                State = ProbeState.Waiting;
                return State;
            }

            double held = nowMs - _windowStartMs;
            Progress01 = Math.Min(1.0, held / HoldMs);
            if (held >= HoldMs)
            {
                PerceivedXm = c[0];
                State = ProbeState.Confirmed;
            }
            else State = ProbeState.Holding;
            return State;
        }

        private void Reset()
        {
            _samples.Clear();
            _started = false;
            Progress01 = 0;
        }

        private double[] Centroid()
        {
            double x = 0, y = 0, z = 0;
            foreach (var s in _samples) { x += s[1]; y += s[2]; z += s[3]; }
            int n = _samples.Count;
            return new[] { x / n, y / n, z / n };
        }

        private static double Dist(double[] a, double[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Builds the result once confirmed (or an unconfirmed one on timeout/skip).</summary>
        public ProbeResult Result(string when, double? actualXm)
        {
            var r = new ProbeResult { When = when, Confirmed = State == ProbeState.Confirmed };
            if (r.Confirmed)
            {
                r.PerceivedXm = PerceivedXm;
                r.ActualXm = actualXm;
                if (actualXm.HasValue && PerceivedXm.HasValue)
                    r.DriftCm = DriftCmFor(PerceivedXm.Value, actualXm.Value, TowardVirtualSignX);
            }
            return r;
        }

        /// <summary>Signed drift in cm, positive toward the virtual hand.</summary>
        public static double DriftCmFor(double perceivedXm, double actualXm, double towardVirtualSignX)
        {
            return (perceivedXm - actualXm) * towardVirtualSignX * 100.0;
        }

        public static bool RightHandMoved(double[] reference, double[] current)
        {
            return reference != null && current != null && Dist(reference, current) > ArmMoveLimitM;
        }
    }
}
