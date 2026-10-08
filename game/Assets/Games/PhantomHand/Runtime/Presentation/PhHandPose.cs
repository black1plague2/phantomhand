using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// A tracked hand reduced to what the virtual hand copies: how far each of the 15 finger joints is bent and which way the hand faces.
    /// <see cref="PhHandPoseSolver.TrySolve"/> makes it from the 21 positions of <see cref="HandSkeleton"/>. FlexDeg is an array, so copying the struct
    /// copies the reference: keep one pose per consumer and let the solver or Smooth fill it.
    /// </summary>
    public struct PhHandPose
    {
        public const int FlexCount = 15;       // 5 fingers x 3 joints

        /// <summary>Degrees, index = finger * 3 + joint. Finger 0 = thumb, 1 = index, 2 = middle, 3 = ring, 4 = little; joint 0 = base, 1 = middle, 2 = end
        /// (thumb: CMC, MCP, IP). Positive = bent toward the palm, 0 = the two bones of that joint in line.</summary>
        public float[] FlexDeg;
        /// <summary>Unit vector from the wrist toward the middle finger's knuckle.</summary>
        public Vector3 Forward;
        /// <summary>Unit vector out of the back of the hand, perpendicular to <see cref="Forward"/>.</summary>
        public Vector3 Dorsal;
        /// <summary>How far the index and little fingers fan apart, degrees: 0 = parallel, positive = apart, negative = crossing. 0 as well when either of
        /// the two is bent so far at its base that the fan cannot be told.</summary>
        public float SpreadDeg;
    }

    /// <summary>
    /// Pure maths from joint positions to <see cref="PhHandPose"/> (no Unity objects, so EditMode tests feed it numbers).
    /// Angle rule: every finger has a bending plane, spanned by its reference line (wrist to the finger's base joint) and the palm normal. A joint angle is the
    /// signed angle in that plane from the bone before the joint to the bone after it, positive toward the palm; the first bone of a finger is measured against
    /// the reference line. The sideways part of a bone (fanning the fingers) falls out of the plane, so spreading the fingers does not read as bending, and a
    /// finger bent 90 degrees at its knuckle still measures right at the next joint. The thumb follows the same rule with the CMC as its base joint: its first
    /// angle is the CMC to MCP bone against the line wrist to CMC (the other fingers: knuckle to PIP against wrist to knuckle), then MCP and IP.
    /// The palm normal comes from the wrist, index knuckle and little knuckle; the side flips it, so a left hand and its mirror image give the same angles.
    /// Known bias: the plane is cut by the wrist-to-knuckle line, so a finger that points away from that line (the little finger held parallel to the others is 26
    /// degrees off its line) reads high at mid-range, tan(read) = tan(true) / cos(that angle): about 3 degrees at 45 degrees. Tracking noise is larger.
    /// </summary>
    public static class PhHandPoseSolver
    {
        /// <summary>A bone (or reference line) shorter than this is a tracking glitch, not a hand.</summary>
        public const float MinBoneM = 0.001f;
        /// <summary>Plausible range of one joint angle; a single bad frame cannot fold a finger backwards.</summary>
        public const float MinFlexDeg = -30f, MaxFlexDeg = 120f;

        private const float MinPalmSin = 0.05f;      // wrist -> index and wrist -> little knuckle closer than 3 degrees: no palm
        private const float MinFrameSin = 0.1f;      // a reference line within 6 degrees of the palm normal has no bending plane
        private const float MinInPlane = 0.35f;      // spread needs the first bones to lie mostly in the palm plane (bent less than about 70 degrees)

        // ---- solve ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Fills <paramref name="pose"/> from the 21 joints. False when the array is null or shorter than 21, a value is NaN or infinite, a bone is shorter than
        /// 1 mm, or the palm is degenerate; the pose then keeps its previous values (a fresh pose becomes a flat hand), so the caller can drop the frame.
        /// Reuses pose.FlexDeg when it has 15 entries, so a steady stream of frames allocates nothing.
        /// </summary>
        public static bool TrySolve(Vector3[] joints, HandSide side, ref PhHandPose pose)
        {
            if (pose.FlexDeg == null || pose.FlexDeg.Length != PhHandPose.FlexCount) pose.FlexDeg = new float[PhHandPose.FlexCount];
            if (pose.Forward.sqrMagnitude < 0.5f || pose.Dorsal.sqrMagnitude < 0.5f) { pose.Forward = Vector3.forward; pose.Dorsal = Vector3.up; }
            if (joints == null || joints.Length < HandSkeleton.JointCount) return false;
            for (int i = 0; i < HandSkeleton.JointCount; i++)
            {
                float sum = joints[i].x + joints[i].y + joints[i].z;     // NaN or infinity in any component survives the sum
                if (float.IsNaN(sum) || float.IsInfinity(sum)) return false;
            }

            Vector3 wrist = joints[HandSkeleton.Wrist];
            Vector3 toMiddle = joints[HandSkeleton.Point(2, 0)] - wrist;
            Vector3 toIndex = joints[HandSkeleton.Point(1, 0)] - wrist;
            Vector3 toLittle = joints[HandSkeleton.Point(4, 0)] - wrist;
            float lm = toMiddle.magnitude, li = toIndex.magnitude, ll = toLittle.magnitude;
            if (lm < MinBoneM || li < MinBoneM || ll < MinBoneM) return false;

            // right hand, palm down, fingers +z, thumb -x: cross(toLittle, toIndex) points down, out of the palm; a left hand is the mirror image
            Vector3 across = Vector3.Cross(toLittle, toIndex);
            float am = across.magnitude;
            if (am < MinPalmSin * li * ll) return false;
            Vector3 palm = across / am;
            if (side == HandSide.Left) palm = -palm;

            Vector3 forward = toMiddle / lm;
            Vector3 dorsal = -palm - forward * Vector3.Dot(-palm, forward);       // perpendicular to Forward
            float dm = dorsal.magnitude;
            if (dm < MinFrameSin) return false;
            dorsal /= dm;

            // a dry run first, so a bad finger cannot leave the pose half written
            for (int f = 0; f < HandSkeleton.FingerCount; f++) if (!Measure(joints, f, wrist, palm, null)) return false;
            for (int f = 0; f < HandSkeleton.FingerCount; f++) Measure(joints, f, wrist, palm, pose.FlexDeg);

            pose.Forward = forward;
            pose.Dorsal = dorsal;
            pose.SpreadDeg = Spread(joints, forward, dorsal);
            return true;
        }

        /// <summary>Validates one finger and, when <paramref name="deg"/> is not null, writes its three angles.</summary>
        private static bool Measure(Vector3[] j, int finger, Vector3 wrist, Vector3 palm, float[] deg)
        {
            Vector3 p0 = j[HandSkeleton.Point(finger, 0)];
            Vector3 r = p0 - wrist;
            Vector3 b1 = j[HandSkeleton.Point(finger, 1)] - p0;
            Vector3 b2 = j[HandSkeleton.Point(finger, 2)] - j[HandSkeleton.Point(finger, 1)];
            Vector3 b3 = j[HandSkeleton.Point(finger, 3)] - j[HandSkeleton.Point(finger, 2)];
            if (r.magnitude < MinBoneM || b1.magnitude < MinBoneM || b2.magnitude < MinBoneM || b3.magnitude < MinBoneM) return false;
            Vector3 e1, e2;
            if (!BendFrame(r, palm, out e1, out e2)) return false;
            if (deg != null)
            {
                deg[finger * 3] = Mathf.Clamp(BendDeg(r, b1, e1, e2), MinFlexDeg, MaxFlexDeg);
                deg[finger * 3 + 1] = Mathf.Clamp(BendDeg(b1, b2, e1, e2), MinFlexDeg, MaxFlexDeg);
                deg[finger * 3 + 2] = Mathf.Clamp(BendDeg(b2, b3, e1, e2), MinFlexDeg, MaxFlexDeg);
            }
            return true;
        }

        /// <summary>The bending plane of a finger as two unit vectors: e1 along the reference line (wrist to base joint), e2 the palm normal made perpendicular to e1.
        /// False when the line is shorter than 1 mm or lies within about 6 degrees of the palm normal. Public because the rig measures its own rest pose with it.</summary>
        public static bool BendFrame(Vector3 reference, Vector3 palmNormal, out Vector3 e1, out Vector3 e2)
        {
            e1 = Vector3.zero; e2 = Vector3.zero;
            float len = reference.magnitude;
            if (!(len >= MinBoneM)) return false;
            Vector3 u = reference / len;
            Vector3 n = palmNormal - u * Vector3.Dot(palmNormal, u);
            float nm = n.magnitude;
            if (!(nm >= MinFrameSin)) return false;      // also false for NaN
            e1 = u; e2 = n / nm;
            return true;
        }

        /// <summary>Signed angle in degrees from direction a to direction b, measured in the bending plane (e1, e2) of <see cref="BendFrame"/>: positive when b
        /// lies further toward the palm. The lengths of a and b do not matter; the part of a or b along the plane's normal is dropped.</summary>
        public static float BendDeg(Vector3 a, Vector3 b, Vector3 e1, Vector3 e2)
        {
            float ax = Vector3.Dot(a, e1), ay = Vector3.Dot(a, e2), bx = Vector3.Dot(b, e1), by = Vector3.Dot(b, e2);
            return Mathf.Atan2(ax * by - ay * bx, ax * bx + ay * by) * Mathf.Rad2Deg;
        }

        // ---- spread ----------------------------------------------------------------------------------------------------

        /// <summary>Lean of the index finger's first bone from Forward minus the lean of the little finger's, in the palm plane. Parallel fingers give 0 whatever the
        /// hand does, because both lean alike. 0 when either bone points mostly out of the plane (no fan to see).</summary>
        private static float Spread(Vector3[] j, Vector3 forward, Vector3 dorsal)
        {
            // little knuckle -> index knuckle points toward the thumb on either hand (no handedness needed); keep its part in the palm plane
            Vector3 thumbSide = j[HandSkeleton.Point(1, 0)] - j[HandSkeleton.Point(4, 0)];
            thumbSide -= forward * Vector3.Dot(thumbSide, forward) + dorsal * Vector3.Dot(thumbSide, dorsal);
            float tm = thumbSide.magnitude;
            if (tm < MinBoneM) return 0f;
            thumbSide /= tm;
            float index, little;
            if (!Lean(j, 1, forward, thumbSide, out index) || !Lean(j, 4, forward, thumbSide, out little)) return 0f;
            return Mathf.Clamp(index - little, -30f, 90f);
        }

        private static bool Lean(Vector3[] j, int finger, Vector3 forward, Vector3 thumbSide, out float deg)
        {
            deg = 0f;
            Vector3 b = j[HandSkeleton.Point(finger, 1)] - j[HandSkeleton.Point(finger, 0)];
            float along = Vector3.Dot(b, forward), side = Vector3.Dot(b, thumbSide);
            if (Mathf.Sqrt(along * along + side * side) < MinInPlane * b.magnitude) return false;
            deg = Mathf.Atan2(side, along) * Mathf.Rad2Deg;
            return true;
        }

        // ---- smoothing -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Moves <paramref name="current"/> toward <paramref name="target"/> by the exponential step 1 - exp(-dt / timeConstant): the 15 angles, the spread, and
        /// the two directions (blended, then made unit length again and Dorsal perpendicular to Forward). A dt or time constant that is 0, negative or NaN copies
        /// the target, which is also how a first frame starts. A target without angles changes nothing.
        /// </summary>
        public static void Smooth(ref PhHandPose current, in PhHandPose target, float dtSeconds, float timeConstantSeconds)
        {
            if (target.FlexDeg == null || target.FlexDeg.Length < PhHandPose.FlexCount) return;
            bool fresh = current.FlexDeg == null || current.FlexDeg.Length != PhHandPose.FlexCount;
            if (fresh) current.FlexDeg = new float[PhHandPose.FlexCount];
            float a = 1f;
            if (!fresh && dtSeconds > 0f && timeConstantSeconds > 0f) a = 1f - Mathf.Exp(-dtSeconds / timeConstantSeconds);

            for (int i = 0; i < PhHandPose.FlexCount; i++)
                current.FlexDeg[i] = a >= 1f ? target.FlexDeg[i] : current.FlexDeg[i] + (target.FlexDeg[i] - current.FlexDeg[i]) * a;
            current.SpreadDeg = a >= 1f ? target.SpreadDeg : current.SpreadDeg + (target.SpreadDeg - current.SpreadDeg) * a;

            if (a >= 1f || current.Forward.sqrMagnitude < 0.5f || current.Dorsal.sqrMagnitude < 0.5f)
            {
                current.Forward = target.Forward; current.Dorsal = target.Dorsal;
                return;
            }
            Vector3 f = Vector3.Lerp(current.Forward, target.Forward, a);
            float fm = f.magnitude;
            if (fm < 1e-4f) f = target.Forward; else f /= fm;                  // opposite directions blend to nothing: follow the target
            Vector3 d = Vector3.Lerp(current.Dorsal, target.Dorsal, a);
            d -= f * Vector3.Dot(d, f);
            float dm = d.magnitude;
            if (dm < 1e-4f) d = target.Dorsal; else d /= dm;
            current.Forward = f; current.Dorsal = d;
        }
    }
}
