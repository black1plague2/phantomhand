using System.Collections.Generic;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// Pure-number tests for PhHandPoseSolver (21 joint positions to 15 bend angles, the facing of the hand, smoothing) and for the bone-to-slot table of PhHandFit.
    /// No GameObjects and no Quaternion calls (a Rodrigues rotation is written out below), so they also run outside Unity.
    /// The hand is synthetic: a right hand in the rig's wrapper frame (wrist at the origin, +z toward the fingers, +y out of the back, palm down, thumb on -x).
    /// Flat, the four fingers run parallel to +z (their wrist-to-knuckle lines fan out, as in a real hand) and the thumb lies 40 degrees away from +z in the palm plane.
    /// </summary>
    public class PhHandPoseTests
    {
        // ---- the synthetic hand: base joints and bone lengths in metres (average adult hand) ----------------------------------------
        // order: thumb, index, middle, ring, little; the thumb's base joint is its CMC
        private static readonly Vector3[] Base =
        {
            new Vector3(-0.020f, 0f, 0.025f),
            new Vector3(-0.026f, 0f, 0.088f),
            new Vector3(-0.005f, 0f, 0.090f),
            new Vector3(0.016f, 0f, 0.085f),
            new Vector3(0.034f, 0f, 0.070f),
        };
        // base -> middle joint, middle -> end joint, end joint -> tip (the thumb: CMC -> MCP -> IP -> tip)
        private static readonly float[][] Len =
        {
            new[] { 0.045f, 0.032f, 0.027f },
            new[] { 0.043f, 0.024f, 0.022f },
            new[] { 0.047f, 0.027f, 0.023f },
            new[] { 0.044f, 0.026f, 0.022f },
            new[] { 0.034f, 0.020f, 0.020f },
        };
        private static readonly Vector3 ThumbDir = new Vector3(-Mathf.Sin(40f * Mathf.Deg2Rad), 0f, Mathf.Cos(40f * Mathf.Deg2Rad));

        /// <summary>Rodrigues: the numbers Quaternion.AngleAxis(deg, axis) * v gives, without the native call.</summary>
        private static Vector3 Turn(Vector3 axis, float deg, Vector3 v)
        {
            axis = axis.normalized;
            float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad);
            return v * c + Vector3.Cross(axis, v) * s + axis * (Vector3.Dot(axis, v) * (1f - c));
        }

        /// <summary>The 21 points of the right hand. bendDeg (15, thumb first) is the bend toward the palm at each joint, null = flat; fanDeg (5) leans a finger's
        /// bones toward the thumb in the palm plane (the thumb entry is ignored), null = parallel. Each bend turns the rest of the finger about its flexion axis.</summary>
        private static Vector3[] Hand(float[] bendDeg = null, float[] fanDeg = null)
        {
            var j = new Vector3[HandSkeleton.JointCount];
            for (int f = 0; f < 5; f++)
            {
                Vector3 dir = f == 0 ? ThumbDir : (fanDeg == null ? Vector3.forward : Turn(Vector3.up, -fanDeg[f], Vector3.forward));
                Vector3 axis = PhHandFit.FlexionAxis(dir, Vector3.down);
                Vector3 p = Base[f];
                j[HandSkeleton.Point(f, 0)] = p;
                for (int k = 0; k < 3; k++)
                {
                    if (bendDeg != null) dir = Turn(axis, bendDeg[f * 3 + k], dir);
                    p += dir * Len[f][k];
                    j[HandSkeleton.Point(f, k + 1)] = p;
                }
            }
            return j;
        }

        private static Vector3[] Mirror(Vector3[] j)
        {
            var m = new Vector3[j.Length];
            for (int i = 0; i < j.Length; i++) m[i] = new Vector3(-j[i].x, j[i].y, j[i].z);
            return m;
        }

        private static Vector3[] Move(Vector3[] j, Vector3 axis, float deg, Vector3 offset)
        {
            var m = new Vector3[j.Length];
            for (int i = 0; i < j.Length; i++) m[i] = Turn(axis, deg, j[i]) + offset;
            return m;
        }

        private static PhHandPose Solve(Vector3[] joints, HandSide side = HandSide.Right)
        {
            var pose = new PhHandPose();
            Assert.IsTrue(PhHandPoseSolver.TrySolve(joints, side, ref pose), "solve");
            return pose;
        }

        private static float[] Bends(params float[] values)
        {
            var b = new float[15];
            for (int i = 0; i < values.Length; i++) b[i] = values[i];
            return b;
        }

        private static void AssertAngles(float[] expected, PhHandPose pose, float tol, string what)
        {
            Assert.AreEqual(15, pose.FlexDeg.Length);
            for (int i = 0; i < 15; i++) Assert.AreEqual(expected[i], pose.FlexDeg[i], tol, what + ", joint " + i);
        }

        private static void AssertV(Vector3 expected, Vector3 actual, float tol, string what)
        {
            Assert.AreEqual(expected.x, actual.x, tol, what + " x"); Assert.AreEqual(expected.y, actual.y, tol, what + " y"); Assert.AreEqual(expected.z, actual.z, tol, what + " z");
        }

        // ---- the angles -------------------------------------------------------------------------------------------------------------

        [Test]
        public void FlatHand_GivesFifteenAnglesWithinOneDegreeOfZero_AndFacesLikeTheHand()
        {
            var pose = Solve(Hand());
            AssertAngles(new float[15], pose, 1f, "flat");
            Vector3 toMiddle = (Base[2]).normalized;
            AssertV(toMiddle, pose.Forward, 1e-5f, "Forward is wrist -> middle knuckle");
            AssertV(Vector3.up, pose.Dorsal, 1e-5f, "Dorsal is out of the back (palm down)");
            Assert.AreEqual(0f, Vector3.Dot(pose.Forward, pose.Dorsal), 1e-5f);
            Assert.AreEqual(0f, pose.SpreadDeg, 1f, "parallel fingers");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void BendingOneFingersMiddleJointNinetyDegrees_ReadsNinetyThere_AndZeroElsewhere(int finger)
        {
            var bend = new float[15];
            bend[finger * 3 + 1] = 90f;
            var expected = (float[])bend.Clone();
            AssertAngles(expected, Solve(Hand(bend)), 1f, "finger " + finger);
        }

        // A finger that runs at an angle to its wrist-to-knuckle line (here: held parallel to the others, so the little finger is 26 degrees off its line) reads a little
        // high at mid-range: the bending plane comes from that line, tan(read) = tan(true) / cos(angle between the two). Worst case below is the little finger's 30
        // degrees reading 32.7; 3 degrees is the budget.
        [TestCase(1, 0, 40f)]
        [TestCase(2, 0, 60f)]
        [TestCase(3, 0, 45f)]
        [TestCase(0, 0, 25f)]
        [TestCase(0, 1, 30f)]
        [TestCase(0, 2, 55f)]
        [TestCase(1, 1, 80f)]
        [TestCase(2, 2, 35f)]
        [TestCase(3, 1, 110f)]
        [TestCase(4, 1, 100f)]
        [TestCase(4, 2, 30f)]
        public void EachJoint_ReadsItsOwnBend_AndTheOthersStayAtZero(int finger, int joint, float deg)
        {
            var bend = new float[15];
            bend[finger * 3 + joint] = deg;
            AssertAngles((float[])bend.Clone(), Solve(Hand(bend)), 3f, "finger " + finger + " joint " + joint);
        }

        [Test]
        public void AFist_ReadsTheBendsItWasBuiltWith()
        {
            var fist = Bends(30f, 45f, 60f,      // thumb: CMC, MCP, IP
                             90f, 100f, 70f,     // index
                             90f, 100f, 70f,     // middle
                             90f, 100f, 70f,     // ring
                             90f, 100f, 70f);    // little
            var pose = Solve(Hand(fist));
            AssertAngles(fist, pose, 1.5f, "fist");
            Assert.AreEqual(0f, pose.SpreadDeg, 0.001f, "no fan to tell when the fingers are curled");
        }

        [Test]
        public void FanningTheFingers_IsNotBending_AndShowsAsSpread()
        {
            var fan = new[] { 0f, 15f, 5f, -5f, -15f };                 // index and middle lean toward the thumb, ring and little away
            var pose = Solve(Hand(null, fan));
            AssertAngles(new float[15], pose, 1f, "fanned but flat");
            Assert.AreEqual(30f, pose.SpreadDeg, 1f, "index 15 toward the thumb, little 15 away");
            var tight = Solve(Hand(null, new[] { 0f, -10f, 0f, 0f, 10f }));
            Assert.AreEqual(-20f, tight.SpreadDeg, 1f, "fingers crossing over: negative");
        }

        [Test]
        public void FanningFingers_StillReadsTheBend()
        {
            var bend = Bends(0f, 0f, 0f, 0f, 70f, 40f, 0f, 70f, 40f, 0f, 70f, 40f, 0f, 70f, 40f);   // PIP and DIP of the four fingers
            var pose = Solve(Hand(bend, new[] { 0f, 18f, 6f, -6f, -18f }));
            AssertAngles(bend, pose, 1.5f, "fanned and bent");
        }

        [Test]
        public void SpreadIsZero_WhenTheIndexOrLittleFingerPointsOutOfThePalmPlane()
        {
            var curledIndex = Bends(0f, 0f, 0f, 90f, 0f, 0f);
            var pose = Solve(Hand(curledIndex, new[] { 0f, 20f, 0f, 0f, -20f }));
            Assert.AreEqual(0f, pose.SpreadDeg);
            var curledLittle = new float[15]; curledLittle[12] = 80f;
            Assert.AreEqual(0f, Solve(Hand(curledLittle, new[] { 0f, 20f, 0f, 0f, -20f })).SpreadDeg);
        }

        [Test]
        public void Angles_AreClampedToTheirPlausibleRange()
        {
            var back = new float[15]; back[7] = -80f;        // middle finger bent backwards
            var fold = new float[15]; fold[7] = 170f;
            Assert.AreEqual(PhHandPoseSolver.MinFlexDeg, Solve(Hand(back)).FlexDeg[7], 1e-4f);
            Assert.AreEqual(PhHandPoseSolver.MaxFlexDeg, Solve(Hand(fold)).FlexDeg[7], 1e-4f);
            Assert.AreEqual(-30f, PhHandPoseSolver.MinFlexDeg); Assert.AreEqual(120f, PhHandPoseSolver.MaxFlexDeg);
            // a finger inside the range is left alone
            var ok = new float[15]; ok[7] = -20f;
            Assert.AreEqual(-20f, Solve(Hand(ok)).FlexDeg[7], 1f);
        }

        // ---- handedness and rigid motion ----------------------------------------------------------------------------------------------

        private static readonly float[] Mixed = Bends(20f, 35f, 15f, 10f, 40f, 20f, 60f, 70f, 30f, 45f, 80f, 25f, 15f, 55f, 35f);

        [Test]
        public void TheMirroredLeftHand_GivesTheSameAnglesAsTheRightHand()
        {
            var right = Move(Hand(Mixed, new[] { 0f, 12f, 4f, -4f, -12f }), new Vector3(1f, 2f, 3f), 70f, new Vector3(0.3f, -0.2f, 0.5f));
            var left = Mirror(right);
            var pr = Solve(right, HandSide.Right);
            var pl = Solve(left, HandSide.Left);
            AssertAngles(pr.FlexDeg, pl, 1e-3f, "left vs right");
            Assert.AreEqual(pr.SpreadDeg, pl.SpreadDeg, 1e-3f);
            AssertV(new Vector3(-pr.Forward.x, pr.Forward.y, pr.Forward.z), pl.Forward, 1e-5f, "Forward mirrored");
            AssertV(new Vector3(-pr.Dorsal.x, pr.Dorsal.y, pr.Dorsal.z), pl.Dorsal, 1e-5f, "Dorsal mirrored");
        }

        [Test]
        public void TheSideMatters_ARightHandReadAsLeftHasItsPalmTurnedInside_Out()
        {
            var fist = Bends(0f, 0f, 0f, 90f, 90f, 60f);
            var asRight = Solve(Hand(fist), HandSide.Right);
            var asLeft = Solve(Hand(fist), HandSide.Left);
            Assert.AreEqual(90f, asRight.FlexDeg[3], 1f);
            Assert.AreEqual(PhHandPoseSolver.MinFlexDeg, asLeft.FlexDeg[3], 1e-4f, "flexion reads as extension, clamped");
            AssertV(Vector3.up, asRight.Dorsal, 1e-5f, "right");
            AssertV(Vector3.down, asLeft.Dorsal, 1e-5f, "left");
        }

        [Test]
        public void RotatingAndMovingTheWholeHand_KeepsTheAngles_AndTurnsForwardAndDorsalWithIt()
        {
            var still = Hand(Mixed, new[] { 0f, 10f, 3f, -3f, -10f });
            var p0 = Solve(still);
            foreach (var axis in new[] { new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 3f), new Vector3(-0.4f, 0.1f, 1f) })
                foreach (var deg in new[] { 35f, 120f, 200f })
                {
                    var moved = Move(still, axis, deg, new Vector3(0.7f, -1.2f, 0.4f));
                    var p1 = Solve(moved);
                    AssertAngles(p0.FlexDeg, p1, 0.02f, "axis " + axis + ", " + deg + " degrees");
                    Assert.AreEqual(p0.SpreadDeg, p1.SpreadDeg, 0.05f);
                    AssertV(Turn(axis, deg, p0.Forward), p1.Forward, 1e-4f, "Forward turns with the hand");
                    AssertV(Turn(axis, deg, p0.Dorsal), p1.Dorsal, 1e-4f, "Dorsal turns with the hand");
                }
        }

        [Test]
        public void ForwardAndDorsal_AreUnitAndPerpendicular()
        {
            var pose = Solve(Move(Hand(Mixed), new Vector3(0.3f, 0.9f, -0.2f), 77f, Vector3.zero));
            Assert.AreEqual(1f, pose.Forward.magnitude, 1e-5f);
            Assert.AreEqual(1f, pose.Dorsal.magnitude, 1e-5f);
            Assert.AreEqual(0f, Vector3.Dot(pose.Forward, pose.Dorsal), 1e-5f);
        }

        [Test]
        public void AnArchedPalm_StillGivesAPerpendicularDorsal_AndForwardFollowsTheMiddleKnuckle()
        {
            var flat = Hand(Mixed);
            var arched = (Vector3[])flat.Clone();
            for (int k = 0; k < 4; k++) arched[HandSkeleton.Point(2, k)] += new Vector3(0f, 0.012f, 0f);     // the middle finger sits 12 mm above the plane of wrist, index and little knuckle
            var p0 = Solve(flat);
            var p1 = Solve(arched);
            Assert.AreEqual(1f, p1.Dorsal.magnitude, 1e-5f);
            Assert.AreEqual(0f, Vector3.Dot(p1.Forward, p1.Dorsal), 1e-5f, "perpendicular although the knuckles are not in one plane");
            Assert.Greater(p1.Dorsal.y, 0.98f, "still out of the back of the hand");
            Assert.Greater(Vector3.Angle(p0.Forward, p1.Forward), 5f, "Forward is the line to the middle knuckle");
        }

        // ---- bad frames ---------------------------------------------------------------------------------------------------------------

        private static IEnumerable<TestCaseData> BadFrames()
        {
            yield return new TestCaseData((Vector3[])null).SetName("Rejects_NullArray");
            yield return new TestCaseData(new Vector3[20]).SetName("Rejects_TooShortArray");
            var nan = Hand(Mixed); nan[7].y = float.NaN;
            yield return new TestCaseData(nan).SetName("Rejects_NaN");
            var inf = Hand(Mixed); inf[12].x = float.PositiveInfinity;
            yield return new TestCaseData(inf).SetName("Rejects_Infinity");
            var zeroBone = Hand(Mixed); zeroBone[HandSkeleton.Point(3, 2)] = zeroBone[HandSkeleton.Point(3, 1)];
            yield return new TestCaseData(zeroBone).SetName("Rejects_ABoneOfLengthZero");
            var halfMm = Hand(Mixed); halfMm[HandSkeleton.Point(2, 3)] = halfMm[HandSkeleton.Point(2, 2)] + new Vector3(0f, 0f, 0.0005f);
            yield return new TestCaseData(halfMm).SetName("Rejects_ABoneHalfAMillimetreLong");
            var wristOnKnuckle = Hand(Mixed); wristOnKnuckle[HandSkeleton.Wrist] = wristOnKnuckle[HandSkeleton.Point(2, 0)];
            yield return new TestCaseData(wristOnKnuckle).SetName("Rejects_WristOnTheMiddleKnuckle");
            var thumbOnWrist = Hand(Mixed); thumbOnWrist[HandSkeleton.Point(0, 0)] = thumbOnWrist[HandSkeleton.Wrist];
            yield return new TestCaseData(thumbOnWrist).SetName("Rejects_ThumbBaseOnTheWrist");
            var flatLine = Hand(Mixed);
            flatLine[HandSkeleton.Point(1, 0)] = new Vector3(0f, 0f, 0.09f); flatLine[HandSkeleton.Point(4, 0)] = new Vector3(0f, 0f, 0.07f);
            yield return new TestCaseData(flatLine).SetName("Rejects_PalmWithTheKnucklesInALineWithTheWrist");
            yield return new TestCaseData(new Vector3[HandSkeleton.JointCount]).SetName("Rejects_EverythingAtOnePoint");
        }

        [TestCaseSource(nameof(BadFrames))]
        public void ABadFrame_ReturnsFalse_AndLeavesTheLastGoodPoseAsItWas(Vector3[] bad)
        {
            var pose = new PhHandPose();
            Assert.IsTrue(PhHandPoseSolver.TrySolve(Hand(Mixed, new[] { 0f, 8f, 2f, -2f, -8f }), HandSide.Right, ref pose));
            var angles = (float[])pose.FlexDeg.Clone();
            Vector3 f = pose.Forward, d = pose.Dorsal; float s = pose.SpreadDeg;
            Assert.IsFalse(PhHandPoseSolver.TrySolve(bad, HandSide.Right, ref pose));
            CollectionAssert.AreEqual(angles, pose.FlexDeg);
            Assert.AreEqual(f, pose.Forward); Assert.AreEqual(d, pose.Dorsal); Assert.AreEqual(s, pose.SpreadDeg);
        }

        [Test]
        public void ABadFrame_NeverLeavesAFingerHalfWritten()
        {
            var pose = new PhHandPose();
            Assert.IsTrue(PhHandPoseSolver.TrySolve(Hand(), HandSide.Right, ref pose));
            var bad = Hand(Mixed);
            bad[HandSkeleton.Point(4, 3)] = bad[HandSkeleton.Point(4, 2)];       // only the last finger is bad; the first four would read fine
            Assert.IsFalse(PhHandPoseSolver.TrySolve(bad, HandSide.Right, ref pose));
            AssertAngles(new float[15], pose, 1f, "still the flat hand");
        }

        [Test]
        public void AFailedSolve_OnAFreshPose_StillLeavesAUsablePose()
        {
            var pose = new PhHandPose();
            Assert.IsFalse(PhHandPoseSolver.TrySolve(null, HandSide.Left, ref pose));
            Assert.IsNotNull(pose.FlexDeg);
            AssertAngles(new float[15], pose, 0f, "fresh");
            Assert.AreEqual(1f, pose.Forward.magnitude, 1e-6f); Assert.AreEqual(1f, pose.Dorsal.magnitude, 1e-6f);
        }

        [Test]
        public void TheAnglesArray_IsReused_WhenItHasFifteenEntries_AndMadeWhenItDoesNot()
        {
            var mine = new float[15];
            var pose = new PhHandPose { FlexDeg = mine };
            Assert.IsTrue(PhHandPoseSolver.TrySolve(Hand(Mixed), HandSide.Right, ref pose));
            Assert.AreSame(mine, pose.FlexDeg, "no allocation per frame");
            Assert.AreEqual(35f, mine[1], 1.5f);
            Assert.IsTrue(PhHandPoseSolver.TrySolve(Hand(), HandSide.Right, ref pose));
            Assert.AreSame(mine, pose.FlexDeg);
            Assert.AreEqual(0f, mine[1], 1f);

            var wrong = new PhHandPose { FlexDeg = new float[10] };
            Assert.IsTrue(PhHandPoseSolver.TrySolve(Hand(Mixed), HandSide.Right, ref wrong));
            Assert.AreEqual(15, wrong.FlexDeg.Length);
        }

        // ---- smoothing --------------------------------------------------------------------------------------------------------------------

        private static PhHandPose Pose(float value, Vector3 forward, Vector3 dorsal, float spread = 0f)
        {
            var p = new PhHandPose { FlexDeg = new float[15], Forward = forward, Dorsal = dorsal, SpreadDeg = spread };
            for (int i = 0; i < 15; i++) p.FlexDeg[i] = value + i;
            return p;
        }

        [Test]
        public void Smooth_StepsTowardTheTargetByTheExponential()
        {
            var cur = Pose(0f, Vector3.forward, Vector3.up, 0f);
            var tgt = Pose(50f, Vector3.forward, Vector3.up, 20f);
            PhHandPoseSolver.Smooth(ref cur, tgt, 0.1f, 0.1f);        // one time constant: 1 - 1/e of the way
            float k = 1f - Mathf.Exp(-1f);
            for (int i = 0; i < 15; i++) Assert.AreEqual((50f + i) * k + i * (1f - k), cur.FlexDeg[i], 1e-3f, "joint " + i);
            Assert.AreEqual(20f * k, cur.SpreadDeg, 1e-3f);
        }

        [Test]
        public void Smooth_ConvergesOnTheTarget_WithoutOvershoot()
        {
            var cur = Pose(0f, Vector3.forward, Vector3.up);
            var tgt = Pose(90f, Vector3.forward, Vector3.up);
            for (int n = 0; n < 120; n++)
            {
                PhHandPoseSolver.Smooth(ref cur, tgt, 1f / 60f, 0.1f);
                for (int i = 0; i < 15; i++) Assert.LessOrEqual(cur.FlexDeg[i], tgt.FlexDeg[i] + 1e-4f, "no overshoot");
            }
            for (int i = 0; i < 15; i++) Assert.AreEqual(tgt.FlexDeg[i], cur.FlexDeg[i], 1e-3f, "joint " + i);
        }

        [TestCase(0f, 0.1f)]
        [TestCase(-0.016f, 0.1f)]
        [TestCase(0.016f, 0f)]
        [TestCase(0.016f, -0.1f)]
        [TestCase(float.NaN, 0.1f)]
        [TestCase(0.016f, float.NaN)]
        public void Smooth_WithNoTimeStepOrTimeConstant_CopiesTheTarget(float dt, float tau)
        {
            var cur = Pose(3f, Vector3.right, Vector3.up, 5f);
            var tgt = Pose(70f, Vector3.forward, Vector3.up, 12f);
            PhHandPoseSolver.Smooth(ref cur, tgt, dt, tau);
            for (int i = 0; i < 15; i++) Assert.AreEqual(tgt.FlexDeg[i], cur.FlexDeg[i], 0f, "joint " + i);
            Assert.AreEqual(12f, cur.SpreadDeg); Assert.AreEqual(tgt.Forward, cur.Forward); Assert.AreEqual(tgt.Dorsal, cur.Dorsal);
        }

        [Test]
        public void Smooth_BlendsTheDirections_AndKeepsThemUnitAndPerpendicular()
        {
            var axis = new Vector3(1f, 1f, 0f);
            var cur = Pose(0f, Vector3.forward, Vector3.up);
            var tgt = Pose(0f, Turn(axis, 80f, Vector3.forward), Turn(axis, 80f, Vector3.up));
            float before = Vector3.Angle(cur.Forward, tgt.Forward);
            PhHandPoseSolver.Smooth(ref cur, tgt, 0.1f, 0.1f);
            Assert.AreEqual(1f, cur.Forward.magnitude, 1e-5f); Assert.AreEqual(1f, cur.Dorsal.magnitude, 1e-5f);
            Assert.AreEqual(0f, Vector3.Dot(cur.Forward, cur.Dorsal), 1e-5f);
            Assert.Less(Vector3.Angle(cur.Forward, tgt.Forward), before * 0.6f, "moved toward the target");
            Assert.Greater(Vector3.Angle(cur.Forward, tgt.Forward), 0.1f, "but not all the way");
        }

        [Test]
        public void Smooth_FromOppositeDirections_FollowsTheTargetInsteadOfCollapsing()
        {
            var cur = Pose(0f, Vector3.forward, Vector3.up);
            var tgt = Pose(0f, Vector3.back, Vector3.up);
            PhHandPoseSolver.Smooth(ref cur, tgt, 0.0693147f, 0.1f);          // exactly half way: the blend of z and -z is nothing
            Assert.AreEqual(1f, cur.Forward.magnitude, 1e-5f);
            Assert.AreEqual(Vector3.back, cur.Forward);
        }

        [Test]
        public void Smooth_IntoAFreshPose_CopiesTheTarget_WithItsOwnArray()
        {
            var cur = new PhHandPose();
            var tgt = Pose(40f, Vector3.forward, Vector3.up, 7f);
            PhHandPoseSolver.Smooth(ref cur, tgt, 0.016f, 0.2f);
            CollectionAssert.AreEqual(tgt.FlexDeg, cur.FlexDeg);
            Assert.AreNotSame(tgt.FlexDeg, cur.FlexDeg, "writing the smoothed pose must not touch the target");
            Assert.AreEqual(7f, cur.SpreadDeg); Assert.AreEqual(Vector3.forward, cur.Forward);
        }

        [Test]
        public void Smooth_ToATargetWithoutAngles_ChangesNothing()
        {
            var cur = Pose(10f, Vector3.right, Vector3.up, 3f);
            var snapshot = (float[])cur.FlexDeg.Clone();
            PhHandPoseSolver.Smooth(ref cur, new PhHandPose(), 0.016f, 0.1f);
            CollectionAssert.AreEqual(snapshot, cur.FlexDeg);
            Assert.AreEqual(Vector3.right, cur.Forward); Assert.AreEqual(3f, cur.SpreadDeg);
        }

        [Test]
        public void SolvedPoses_CanBeSmoothed_EndToEnd()
        {
            var open = Solve(Hand());
            var closed = Solve(Hand(Bends(30f, 45f, 60f, 90f, 100f, 70f, 90f, 100f, 70f, 90f, 100f, 70f, 90f, 100f, 70f)));
            var shown = open;
            shown.FlexDeg = (float[])open.FlexDeg.Clone();
            for (int n = 0; n < 90; n++) PhHandPoseSolver.Smooth(ref shown, closed, 1f / 60f, 0.1f);
            for (int i = 0; i < 15; i++) Assert.AreEqual(closed.FlexDeg[i], shown.FlexDeg[i], 0.01f);
        }

        // ---- bone names to pose slots -----------------------------------------------------------------------------------------------------

        [Test]
        public void FlexIndex_NumbersTheFifteenDrivenBones_ThumbFirst_EachOnce()
        {
            var seen = new HashSet<int>();
            foreach (var name in PhHandFit.DrivenBones())
            {
                int i = PhHandFit.FlexIndex(name);
                Assert.GreaterOrEqual(i, 0, name); Assert.Less(i, 15, name);
                Assert.IsTrue(seen.Add(i), name + " shares a slot");
                StringAssert.StartsWith(PhHandFit.DigitOf(i / 3), name, "the slot's finger is the bone's digit");
                Assert.AreEqual(PhHandFit.Bone(PhHandFit.DigitOf(i / 3), i % 3 + 1), name, "the slot's joint is the bone's segment");
            }
            Assert.AreEqual(15, seen.Count);
            Assert.AreEqual(0, PhHandFit.FlexIndex("thumb_01.R")); Assert.AreEqual(2, PhHandFit.FlexIndex("thumb_03.R"));
            Assert.AreEqual(3, PhHandFit.FlexIndex("index_01.R")); Assert.AreEqual(5, PhHandFit.FlexIndex("index_03.R"));
            Assert.AreEqual(7, PhHandFit.FlexIndex("middle_02.R")); Assert.AreEqual(11, PhHandFit.FlexIndex("ring_03.R"));
            Assert.AreEqual(12, PhHandFit.FlexIndex("pinky_01.R")); Assert.AreEqual(14, PhHandFit.FlexIndex("pinky_03.R"));
        }

        [Test]
        public void FlexIndex_IsMinusOne_ForEveryBoneThatIsNotDriven()
        {
            foreach (var n in new[] { "index_base.R", "thumb_base.R", "hand.R", "hand.R.001", "index_03.R_end", "index_Ctrl.R", "index_Ctrl_01.R", "pinky_tip.R", "Armature", "", null })
                Assert.AreEqual(-1, PhHandFit.FlexIndex(n), n ?? "null");
        }

        [Test]
        public void DigitOf_ListsTheThumbFirst()
        {
            CollectionAssert.AreEqual(new[] { "thumb", "index", "middle", "ring", "pinky" }, new[] { PhHandFit.DigitOf(0), PhHandFit.DigitOf(1), PhHandFit.DigitOf(2), PhHandFit.DigitOf(3), PhHandFit.DigitOf(4) });
        }
    }
}
