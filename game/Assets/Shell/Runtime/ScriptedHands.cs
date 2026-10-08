using System;
using Opus.Sdk;

namespace Opus.Shell
{
    /// <summary>
    /// A deterministic, scriptable <see cref="IHandSource"/> for Phantom Hand when no headset is present (batch PlayMode, the editor with
    /// no Link): the right forearm rests at a set wrist pose, the left index fingertip sits wherever the script puts it. Positions are
    /// metres in the scene (calibration) space, +x right, +y up, +z forward, like the Meta adapter. Every <see cref="Step"/> counts as
    /// a new tracking sample (DataVersion), so the threat collector and the tracking-rate meter see a live hand. Plain C#.
    /// </summary>
    public sealed class ScriptedHands : IHandSource
    {
        public const double ForearmToIndexTipM = 0.18;
        public const double ForearmToPalmM = 0.09;
        public const double ForearmToThumbTipM = 0.14;

        public double[] RightWrist = { 0.18, 0.771, 0.40 };
        /// <summary>Horizontal unit vector from the elbow toward the fingers.</summary>
        public double[] RightAxis = { 0.0, 0.0, 1.0 };
        public double[] LeftIndexTip = { -0.25, 0.90, 0.30 };
        public double[] HeadPos = { 0.0, 1.18, 0.02 };
        public bool RightTracked = true, LeftTracked = true;
        public float LeftPinch, RightPinch;

        private int _version;

        public int Version { get { return _version; } }

        /// <summary>Advance one tracking sample.</summary>
        public void Step() { _version++; }

        public double[] RightPalm { get { return Along(RightWrist, RightAxis, ForearmToPalmM); } }
        public double[] RightIndexTip { get { return Along(RightWrist, RightAxis, ForearmToIndexTipM); } }

        private static double[] Along(double[] p, double[] axis, double d)
        {
            return new[] { p[0] + axis[0] * d, p[1] + axis[1] * d, p[2] + axis[2] * d };
        }

        private static readonly double[] Identity = { 0, 0, 0, 1 };

        public bool TryGetJointPose(string joint, out double[] posMeters, out double[] rotQuatXyzw)
        {
            posMeters = null; rotQuatXyzw = null;
            double[] p;
            switch (joint)
            {
                case OpusJoints.Head: p = HeadPos; break;
                case OpusJoints.RWrist: if (!RightTracked) return false; p = RightWrist; break;
                case OpusJoints.RPalm: if (!RightTracked) return false; p = RightPalm; break;
                case OpusJoints.RIndexTip: if (!RightTracked) return false; p = RightIndexTip; break;
                case OpusJoints.RThumbTip: if (!RightTracked) return false; p = Along(RightWrist, RightAxis, ForearmToThumbTipM); break;
                case OpusJoints.LIndexTip: if (!LeftTracked) return false; p = LeftIndexTip; break;
                case OpusJoints.LWrist: if (!LeftTracked) return false; p = new[] { LeftIndexTip[0], LeftIndexTip[1], LeftIndexTip[2] - ForearmToIndexTipM }; break;
                case OpusJoints.LPalm: if (!LeftTracked) return false; p = new[] { LeftIndexTip[0], LeftIndexTip[1], LeftIndexTip[2] - ForearmToIndexTipM + ForearmToPalmM }; break;
                case OpusJoints.LThumbTip: if (!LeftTracked) return false; p = new[] { LeftIndexTip[0] + 0.03, LeftIndexTip[1] - 0.02, LeftIndexTip[2] - 0.04 }; break;
                default: return false;
            }
            posMeters = (double[])p.Clone();
            rotQuatXyzw = (double[])Identity.Clone();
            return true;
        }

        public TrackingConfidence GetConfidence(HandSide side)
        {
            return IsTracked(side) ? TrackingConfidence.High : TrackingConfidence.None;
        }

        public bool IsTracked(HandSide side) { return side == HandSide.Left ? LeftTracked : RightTracked; }

        public float GetPinchStrength(HandSide side) { return side == HandSide.Left ? LeftPinch : RightPinch; }

        public int GetDataVersion(HandSide side) { return _version; }
    }
}
