using System;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Shell
{
    /// <summary>
    /// A deterministic, scriptable <see cref="IHandSource"/> for Phantom Hand when no headset is present (batch PlayMode, the editor with
    /// no Link): the right forearm rests at a set wrist pose, the left index fingertip sits wherever the script puts it. Positions are
    /// metres in the scene (calibration) space, +x right, +y up, +z forward, like the Meta adapter. Every <see cref="Step"/> counts as
    /// a new tracking sample (DataVersion), so the threat collector and the tracking-rate meter see a live hand.
    /// The stimulated arm's hand also has a whole skeleton (<see cref="IHandSkeletonSource"/>): a flat hand, palm down, that closes
    /// with <see cref="ArmCurl01"/>, so the path from tracked joints to the virtual hand's bones runs in the editor too.
    /// </summary>
    public sealed class ScriptedHands : IHandSource, IHandSkeletonSource
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
        /// <summary>The stimulated arm. The fields above keep their names from the right-arm layout: "Right..." is the stimulated
        /// arm, "Left..." the pointing hand. With a left arm the same poses answer to the other side's joint names.</summary>
        public HandSide Arm = HandSide.Right;

        /// <summary>How far the stimulated arm's hand is closed in its skeleton: 0 = flat, 1 = fist.</summary>
        public float ArmCurl01;

        // A right hand, palm down, in its own frame (x toward the little finger, z toward the fingers), metres: the knuckle of the
        // thumb, index, middle, ring and little finger from the wrist, and the three bone lengths of each. A left hand mirrors x.
        private static readonly float[] KnuckleX = { -0.031f, -0.026f, -0.005f, 0.016f, 0.034f };
        private static readonly float[] KnuckleY = { -0.012f, 0f, 0f, 0f, 0f };
        private static readonly float[] KnuckleZ = { 0.035f, 0.088f, 0.090f, 0.085f, 0.070f };
        private static readonly float[][] BoneM =
        {
            new[] { 0.040f, 0.030f, 0.027f }, new[] { 0.043f, 0.024f, 0.022f }, new[] { 0.047f, 0.027f, 0.023f },
            new[] { 0.044f, 0.026f, 0.022f }, new[] { 0.034f, 0.020f, 0.020f },
        };
        private static readonly float[] FistDeg = { 80f, 95f, 60f }, ThumbFistDeg = { 20f, 35f, 45f };

        public bool TryGetSkeleton(HandSide side, Vector3[] joints)
        {
            if (side != Arm || !RightTracked || joints == null || joints.Length < HandSkeleton.JointCount) return false;
            var wrist = new Vector3((float)RightWrist[0], (float)RightWrist[1], (float)RightWrist[2]);
            var fwd = new Vector3((float)RightAxis[0], 0f, (float)RightAxis[2]);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float mirror = Arm == HandSide.Left ? -1f : 1f;
            joints[HandSkeleton.Wrist] = wrist;
            for (int f = 0; f < HandSkeleton.FingerCount; f++)
            {
                Vector3 p = wrist + right * (KnuckleX[f] * mirror) + Vector3.up * KnuckleY[f] + fwd * KnuckleZ[f];
                // the thumb leaves the hand 40 degrees outward; the fingers point straight ahead
                Vector3 dir = f == 0 ? (fwd * 0.766f - right * (0.643f * mirror)).normalized : fwd;
                Vector3 toPalm = Vector3.Cross(dir, Vector3.down).normalized;   // turning about it bends the finger toward the table
                float bent = 0f;
                for (int k = 0; k < HandSkeleton.PointsPerFinger; k++)
                {
                    joints[HandSkeleton.Point(f, k)] = p;
                    if (k == 3) break;
                    bent += (f == 0 ? ThumbFistDeg[k] : FistDeg[k]) * Mathf.Clamp01(ArmCurl01);
                    p += Quaternion.AngleAxis(bent, toPalm) * dir * BoneM[f][k];
                }
            }
            return true;
        }

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
            if (Arm == HandSide.Left && joint != null && joint.Length > 2 && joint[1] == '_' && (joint[0] == 'l' || joint[0] == 'r'))
                joint = (joint[0] == 'l' ? "r" : "l") + joint.Substring(1);
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

        public bool IsTracked(HandSide side) { return side == Arm ? RightTracked : LeftTracked; }

        public float GetPinchStrength(HandSide side) { return side == Arm ? RightPinch : LeftPinch; }

        public int GetDataVersion(HandSide side) { return _version; }
    }
}
