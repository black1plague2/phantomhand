using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// Pure-maths tests for the rigged hand (PhHandFit, PhModels naming / fallback): no GameObjects, so they also run outside Unity.
    /// The bone positions are the bind pose of handRig_02.fbx, read from the binary FBX with a Python reader (TransformLink of every skin cluster x 0.01 for
    /// metres; the mesh bind matrix is TransformLink * Transform = the mesh node's world matrix, which was checked equal for all 48 clusters). They are in the
    /// file's own right-handed Y-up world: fingers +y, thumb +z, palm side -x. Unity imports such a file into a left-handed space by a reflection whose exact
    /// form (x or z flipped, plus the importer's node rotations) is not assumed anywhere: the tests re-create several plausible import spaces from the file
    /// data and require the same wrapper-space result from all of them.
    /// </summary>
    public class PhHandFitTests
    {
        // ---- bind pose of handRig_02.fbx, FBX world, metres ---------------------------------------------------------------
        private static readonly Vector3 Wrist = new Vector3(0.000000f, 0.173320f, 0.057917f);                 // hand.R
        private static readonly Vector3 Index1 = new Vector3(-0.041528f, 0.801639f, 0.333992f);               // index_01.R (knuckle)
        private static readonly Vector3 Middle1 = new Vector3(-0.056546f, 0.828565f, 0.173779f);
        private static readonly Vector3 Pinky1 = new Vector3(-0.097657f, 0.716370f, -0.127196f);
        private static readonly Vector3 Thumb1 = new Vector3(-0.169704f, 0.492724f, 0.352995f);
        private static readonly Vector3 Thumb2 = new Vector3(-0.116958f, 0.612138f, 0.485264f);
        private static readonly Vector3 Thumb3 = new Vector3(-0.148237f, 0.719065f, 0.589155f);
        private static readonly Vector3 ThumbEnd = new Vector3(-0.226228f, 0.854385f, 0.720362f);
        private static readonly Vector3 MiddleEnd = new Vector3(-0.153358f, 1.568677f, 0.250131f);            // middle_03.R_end
        private static readonly Vector3 TipVertex = new Vector3(-0.137652f, 1.575256f, 0.248687f);            // farthest skin vertex along wrist -> middle end
        private const float HandLen = 0.19f;

        // expected wrapper space (wrist at the origin, +z fingers, +y dorsal, -x thumb), from the same data in Python
        private static readonly Vector3 ExpIndex1 = new Vector3(-0.025401f, -0.000943f, 0.088312f);
        private static readonly Vector3 ExpMiddle1 = new Vector3(-0.003661f, 0.000520f, 0.089181f);
        private static readonly Vector3 ExpPinky1 = new Vector3(0.034873f, -0.000943f, 0.069526f);
        private static readonly Vector3 ExpThumb1 = new Vector3(-0.030127f, -0.022924f, 0.049591f);
        private static readonly Vector3 ExpThumbEnd = new Vector3(-0.071403f, -0.031684f, 0.104585f);
        private static readonly Vector3 ExpMiddleEnd = new Vector3(0.000031f, -0.002193f, 0.189362f);
        private const float ExpScale = 0.133658f, ExpMeasuredLen = 1.421537f;

        /// <summary>The file data as it would look after Unity's import in four plausible import spaces: all of them are reflections of the file world
        /// (Unity is left-handed), the last two with an extra rotation.</summary>
        private static Vector3 Import(Vector3 p, int space)
        {
            switch (space)
            {
                case 0: return new Vector3(-p.x, p.y, p.z);
                case 1: return new Vector3(p.x, p.y, -p.z);
                case 2: return RotateAbout(new Vector3(0.3f, 1f, -0.4f), 137f, new Vector3(-p.x, p.y, p.z)) + new Vector3(2f, -1f, 0.5f);
                default: return RotateAbout(new Vector3(-1f, 0.2f, 0.7f), -61f, new Vector3(p.x, p.y, -p.z)) + new Vector3(-0.4f, 3f, 1f);
            }
        }

        private static Vector3 RotateAbout(Vector3 axis, float deg, Vector3 v)
        {
            axis = axis.normalized;
            float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad);
            return v * c + Vector3.Cross(axis, v) * s + axis * (Vector3.Dot(axis, v) * (1f - c));
        }

        private static HandFrame Frame(int space)
        {
            HandFrame fr;
            Assert.IsTrue(PhHandFit.TryFrame(Import(Wrist, space), Import(TipVertex, space), Import(Index1, space), Import(Pinky1, space), HandLen, out fr), "frame");
            return fr;
        }

        private static void AssertV(Vector3 expected, Vector3 actual, float tol, string what)
        {
            Assert.AreEqual(expected.x, actual.x, tol, what + " x"); Assert.AreEqual(expected.y, actual.y, tol, what + " y"); Assert.AreEqual(expected.z, actual.z, tol, what + " z");
        }

        // ---- frame: orientation and scale ------------------------------------------------------------------------------------

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void Frame_PutsTheFingersOnPlusZ_TheThumbOnMinusX_AndThePalmDown(int space)
        {
            var fr = Frame(space);
            AssertV(Vector3.zero, fr.ToLocal(Import(Wrist, space)), 1e-5f, "wrist at the pivot");
            AssertV(new Vector3(0f, 0f, HandLen), fr.ToLocal(Import(TipVertex, space)), 2e-4f, "fingertip");
            AssertV(new Vector3(0f, 0f, 1f), fr.DirToLocal(fr.Forward), 1e-5f, "forward");
            AssertV(new Vector3(0f, -1f, 0f), fr.DirToLocal(fr.Palm), 1e-5f, "palm normal (palm DOWN)");
            AssertV(new Vector3(-1f, 0f, 0f), fr.DirToLocal(fr.Thumb), 1e-5f, "thumb side");
            AssertV(ExpIndex1, fr.ToLocal(Import(Index1, space)), 2e-4f, "index knuckle");
            AssertV(ExpMiddle1, fr.ToLocal(Import(Middle1, space)), 2e-4f, "middle knuckle");
            AssertV(ExpPinky1, fr.ToLocal(Import(Pinky1, space)), 2e-4f, "pinky knuckle");
            AssertV(ExpMiddleEnd, fr.ToLocal(Import(MiddleEnd, space)), 3e-4f, "middle end bone");
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void Frame_MatchesTheRotationTheImporterApplies(int space)
        {
            // PhantomModelImporter rotates the container by Quaternion.Inverse(Quaternion.LookRotation(Forward, Dorsal)). LookRotation's axes in Unity are
            // (cross(up, forward), up, forward); its inverse maps thumb -> -x only if cross(Dorsal, Forward) == -Thumb. That algebra, without the quaternion:
            var fr = Frame(space);
            AssertV(-fr.Thumb, Vector3.Cross(fr.Dorsal, fr.Forward), 1e-5f, "x axis of the look rotation");
            Assert.AreEqual(0f, Vector3.Dot(fr.Dorsal, fr.Forward), 1e-5f, "dorsal is orthogonal to forward (LookRotation would not have to re-orthogonalise)");
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void Frame_ThumbBones_LieOnMinusX_AndTowardThePalmSide(int space)
        {
            var fr = Frame(space);
            foreach (var t in new[] { Thumb1, Thumb2, Thumb3, ThumbEnd })
            {
                var l = fr.ToLocal(Import(t, space));
                Assert.Less(l.x, -0.02f, "thumb bone on the thumb side (-x)");
                Assert.Less(l.y, -0.01f, "the rig's thumb is opposed toward the palm, i.e. below the knuckle plane when the palm faces down");
            }
            AssertV(ExpThumb1, fr.ToLocal(Import(Thumb1, space)), 2e-4f, "thumb_01");
            AssertV(ExpThumbEnd, fr.ToLocal(Import(ThumbEnd, space)), 3e-4f, "thumb end");
        }

        [TestCase(0)] [TestCase(2)]
        public void Frame_Scale_MakesWristToMiddleFingertipTheHandLength(int space)
        {
            var fr = Frame(space);
            Assert.AreEqual(ExpMeasuredLen, fr.MeasuredLength, 1e-3f, "the file is 1.42 m long (a hand modelled at x7.5)");
            Assert.AreEqual(ExpScale, fr.Scale, 1e-5f);
            Assert.AreEqual(HandLen, fr.MeasuredLength * fr.Scale, 1e-5f);
        }

        [Test]
        public void Frame_OfAMirroredHand_ComesOutPalmUp_AndThePalmSideWitnessSaysSo()
        {
            // a LEFT hand (the right-hand data reflected through a plane): the right-hand rule puts its palm up, the thumb sits on the dorsal side
            Vector3 M(Vector3 p) { var q = Import(p, 0); return new Vector3(q.x, q.y, -q.z); }
            HandFrame mirrored;
            Assert.IsTrue(PhHandFit.TryFrame(M(Wrist), M(TipVertex), M(Index1), M(Pinky1), HandLen, out mirrored));
            var thumbMid = (M(Thumb1) + M(Thumb2) + M(Thumb3)) / 3f;
            Assert.Less(PhHandFit.PalmSideWitness(mirrored, thumbMid, (M(Index1) + M(Pinky1)) * 0.5f), 0f, "mirrored input must be flagged");
            var right = Frame(0);
            var rThumbMid = (Import(Thumb1, 0) + Import(Thumb2, 0) + Import(Thumb3, 0)) / 3f;
            Assert.Greater(PhHandFit.PalmSideWitness(right, rThumbMid, (Import(Index1, 0) + Import(Pinky1, 0)) * 0.5f), 0.1f, "the right hand passes (thumb 2+ cm toward the palm in model units x scale)");
        }

        [Test]
        public void TryFrame_DegenerateInput_ReturnsFalse()
        {
            HandFrame f;
            Assert.IsFalse(PhHandFit.TryFrame(Vector3.zero, Vector3.zero, Vector3.right, Vector3.left, HandLen, out f), "fingertip on the wrist");
            Assert.IsFalse(PhHandFit.TryFrame(Vector3.zero, Vector3.forward, Vector3.right, Vector3.right, HandLen, out f), "knuckles coincide");
            Assert.IsFalse(PhHandFit.TryFrame(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 2f), HandLen, out f), "knuckle line parallel to the finger axis");
            Assert.IsFalse(PhHandFit.TryFrame(Vector3.zero, Vector3.forward, Vector3.right, Vector3.left, 0f, out f), "no hand length");
        }

        // ---- measurements of the posed mesh ------------------------------------------------------------------------------------

        [Test]
        public void FarthestAlong_FindsTheMiddleFingertipVertex()
        {
            var pts = new List<Vector3> { Import(Wrist, 0), Import(Index1, 0), Import(ThumbEnd, 0), Import(TipVertex, 0), Import(Pinky1, 0) };
            int idx;
            var dir = (Import(MiddleEnd, 0) - Import(Wrist, 0)).normalized;
            float s = PhHandFit.FarthestAlong(pts, Import(Wrist, 0), dir, out idx);
            Assert.AreEqual(3, idx);
            Assert.AreEqual(ExpMeasuredLen, s, 5e-3f);
        }

        [Test]
        public void Percentile_InterpolatesBetweenSortedValues()
        {
            var v = new List<float> { 4f, 0f, 2f, 1f, 3f };
            Assert.AreEqual(0f, PhHandFit.Percentile(v, 0f), 1e-6f);
            Assert.AreEqual(2f, PhHandFit.Percentile(v, 0.5f), 1e-6f);
            Assert.AreEqual(3.88f, PhHandFit.Percentile(v, 0.97f), 1e-5f);
            Assert.AreEqual(0f, PhHandFit.Percentile(new List<float>(), 0.5f));
        }

        [Test]
        public void WristStats_UseOnlyTheBandAtTheCutEnd()
        {
            var rng = new System.Random(5);
            var pts = new List<Vector3>();
            for (int i = 0; i < 200; i++)
                pts.Add(new Vector3(0.005f + (float)(rng.NextDouble() - 0.5) * 0.06f, -0.0219f + (float)rng.NextDouble() * 0.043f, -0.012f + (float)rng.NextDouble() * 0.028f));
            for (int i = 0; i < 50; i++) pts.Add(new Vector3(0.08f, 0.05f, 0.1f + 0.001f * i));      // fingers: outside the 3 cm band
            var w = PhHandFit.Wrist(pts, PhHandFit.WristBandM);
            Assert.AreEqual(200, w.Count);
            Assert.AreEqual(-0.012f, w.MinZ, 6e-4f);
            Assert.AreEqual(0.005f, w.CenterX, 0.004f);
            Assert.AreEqual(-0.0219f, w.Bottom, 0.004f);
            Assert.Greater(w.Top, w.Bottom + 0.03f);
            Assert.AreEqual(0, PhHandFit.Wrist(new List<Vector3>(), 0.03f).Count);
        }

        [Test]
        public void LevelAndCentreShifts_AreSmallForTheMeasuredHand_AndClampedWhenTheMeasurementFails()
        {
            Assert.AreEqual(0.0014f, PhHandFit.LevelShiftY(-0.0219f), 1e-4f, "the measured wrist underside is 1.4 mm below the table level: the hand needs almost no correction");
            Assert.AreEqual(-PhHandFit.MaxShiftM, PhHandFit.LevelShiftY(0.5f));
            Assert.AreEqual(PhHandFit.MaxShiftM, PhHandFit.LevelShiftY(-0.5f));
            Assert.AreEqual(-0.0052f, PhHandFit.CenterShiftX(0.0052f), 1e-6f);
            Assert.AreEqual(PhHandFit.MaxShiftM, PhHandFit.CenterShiftX(-1f));
        }

        [Test]
        public void TopY_ReadsTheDorsalHeightInsideTheWindow()
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < 100; i++) pts.Add(new Vector3(0.002f * (i % 10), 0.001f * (i / 10), 0.04f + 0.0004f * i));   // palm: top 0.009
            pts.Add(new Vector3(0.5f, 9f, 0.05f));            // outside the x window
            pts.Add(new Vector3(0.0f, 9f, 0.5f));             // outside the z window
            float top = PhHandFit.TopY(pts, 0.03f, 0.09f, 0.01f, 0.02f);
            Assert.AreEqual(0.0088f, top, 0.0005f);
        }

        // ---- skinning and the plausibility gate (run 2: the 2 mm speck) ----------------------------------------------------------

        private static Matrix4x4 Translate(float x, float y, float z) { var m = Matrix4x4.identity; m.m03 = x; m.m13 = y; m.m23 = z; return m; }

        [Test]
        public void SkinVertices_BlendsTheBoneMatrices_AndRenormalisesTheWeights()
        {
            var v = new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(2f, 2f, 2f) };
            var w = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
                new BoneWeight { boneIndex0 = 0, weight0 = 0.45f, boneIndex1 = 1, weight1 = 0.45f },    // sums to 0.9: renormalised
            };
            var m = new[] { Matrix4x4.identity, Translate(0f, 0f, 1f) };
            var o = PhHandFit.SkinVertices(v, w, m);
            AssertV(new Vector3(1f, 0f, 0f), o[0], 1e-6f, "bone 0 stays at its bind pose");
            AssertV(new Vector3(0f, 1f, 1f), o[1], 1e-6f, "bone 1 moved +1 in z");
            AssertV(new Vector3(0f, 0f, 1.5f), o[2], 1e-6f, "50/50 blend");
            AssertV(new Vector3(2f, 2f, 2.5f), o[3], 1e-6f, "weights that sum to 0.9 are renormalised");
            AssertV(new Vector3(3f, 4f, 5f), PhHandFit.SkinVertices(new[] { new Vector3(3f, 4f, 5f) }, new[] { new BoneWeight() }, m)[0], 1e-6f, "a vertex without weights is unchanged");
        }

        [Test]
        public void SkinVertices_OfTheRunTwoSetup_GivesAWorldSizedHand_NotASpeckAtTheRenderer()
        {
            // The file keeps node scale 100 and Unity bakes the 0.01 unit factor into the vertices: a 1.4 m hand is ~1.4 cm in mesh units. At the bind pose every bone matrix
            // (bone.localToWorld * bindpose) is the renderer's matrix: scale 100 and the renderer's position (0.1917, 0.7978, 0.6656) as measured in the saved wrapper.
            var renderer = Matrix4x4.identity;
            renderer.m00 = 100f; renderer.m11 = 100f; renderer.m22 = 100f; renderer.m03 = 0.1917f; renderer.m13 = 0.7978f; renderer.m23 = 0.6656f;
            var v = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.004f, 0.012f, 0.002f), new Vector3(-0.002f, 0.0142f, 0.001f) };    // wrist, knuckle, fingertip in mesh units
            var w = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
            var o = PhHandFit.SkinVertices(v, w, new[] { renderer });
            Assert.AreEqual(1.4375f, Vector3.Distance(o[0], o[2]), 1e-3f, "wrist -> fingertip in metres");
            AssertV(new Vector3(-0.0083f, 2.2178f, 0.7656f), o[2], 1e-3f, "fingertip in world space");
            // what BakeMesh(useScale: true) + 'position + rotation * v' did: the whole hand within 2 cm of the renderer
            var speck = new Vector3(0.1917f, 0.7978f, 0.6656f);
            Assert.Less(Vector3.Distance(speck + v[0], speck + v[2]), 0.02f);
        }

        [Test]
        public void BoundsPlausible_RejectsTheSpeckOfRunTwo_AndAcceptsTheFittedHand()
        {
            string why;
            Assert.IsTrue(PhHandFit.BoundsPlausible(new Vector3(-0.0757f, -0.0354f, -0.0120f), new Vector3(0.0627f, 0.0223f, 0.1900f), PhHandFit.DefaultHandLenM, out why), why);
            Assert.IsNull(why);
            Assert.IsTrue(PhHandFit.BoundsPlausible(new Vector3(-0.090f, -0.0205f, -0.045f), new Vector3(0.050f, 0.025f, 0.19f), PhHandFit.DefaultHandLenM, out why), "the glove fallback (cuff trimmed at -4.5 cm): " + why);
            Assert.IsFalse(PhHandFit.BoundsPlausible(new Vector3(-0.0013f, -0.0134f, 0.1871f), new Vector3(0.0011f, -0.0120f, 0.1902f), PhHandFit.DefaultHandLenM, out why), "run 2's speck");
            StringAssert.Contains("wrist end", why);
            Assert.IsFalse(PhHandFit.BoundsPlausible(Vector3.zero, Vector3.zero, PhHandFit.DefaultHandLenM, out why), "a bake that never recorded bounds");
            StringAssert.Contains("fingertip", why);
            Assert.IsFalse(PhHandFit.BoundsPlausible(new Vector3(-0.0076f, -0.0035f, -0.0012f), new Vector3(0.0063f, 0.0022f, 0.0190f), PhHandFit.DefaultHandLenM, out why), "a hand 10x too small");
            Assert.IsFalse(PhHandFit.BoundsPlausible(new Vector3(-0.15f, -0.035f, -0.012f), new Vector3(0.15f, 0.022f, 0.19f), PhHandFit.DefaultHandLenM, out why), "too wide");
            StringAssert.Contains("width", why);
            Assert.IsFalse(PhHandFit.BoundsPlausible(new Vector3(-0.0757f, -0.005f, -0.012f), new Vector3(0.0627f, 0.0223f, 0.19f), PhHandFit.DefaultHandLenM, out why), "too thin");
            StringAssert.Contains("thickness", why);
            Assert.IsFalse(PhHandFit.BoundsPlausible(new Vector3(-0.0627f, -0.0354f, -0.012f), new Vector3(0.0757f, 0.0223f, 0.19f), PhHandFit.DefaultHandLenM, out why), "thumb on the +x side (a mirrored or turned hand)");
            StringAssert.Contains("thumb", why);
            Assert.AreEqual(0.19f, PhHandFit.DefaultHandLenM);
        }

        // ---- the rig: bone names, curl schedule, flexion axes -------------------------------------------------------------------

        [Test]
        public void BoneNames_FollowTheFbxHierarchy()
        {
            Assert.AreEqual("hand.R", PhHandFit.WristBone);
            Assert.AreEqual("index_base.R", PhHandFit.Bone("index", 0));
            Assert.AreEqual("pinky_02.R", PhHandFit.Bone("pinky", 2));
            Assert.AreEqual("middle_03.R_end", PhHandFit.EndBone("middle"));
            Assert.AreEqual("thumb_03.R_end", PhHandFit.EndBone("thumb"));
            CollectionAssert.AreEqual(new[] { "index", "middle", "ring", "pinky" }, PhHandFit.FingerNames);
        }

        [Test]
        public void CurlSchedule_DrivesFifteenBones_AndNothingElse()
        {
            var driven = PhHandFit.DrivenBones().ToList();
            Assert.AreEqual(15, driven.Count);
            Assert.AreEqual(15, driven.Distinct().Count());
            foreach (var n in driven) Assert.Greater(PhHandFit.CurlDegrees(n), 0f, n);
            Assert.AreEqual(72f, PhHandFit.CurlDegrees("index_01.R")); Assert.AreEqual(79f, PhHandFit.CurlDegrees("ring_02.R")); Assert.AreEqual(51f, PhHandFit.CurlDegrees("pinky_03.R"));
            Assert.AreEqual(15f, PhHandFit.CurlDegrees("thumb_01.R")); Assert.AreEqual(35f, PhHandFit.CurlDegrees("thumb_02.R")); Assert.AreEqual(30f, PhHandFit.CurlDegrees("thumb_03.R"));
            // the metacarpal, the unweighted parallel chain, the IK leaves and the forearm bones stay put
            foreach (var n in new[] { "index_base.R", "index_Ctrl.R", "index_Ctrl_01.R", "index_tip.R", "index_03.R_end", "hand.R", "hand.R.001", "pulse.R", "thumb_base.R", "thumb_Ctrl_02.R", "Armature", "" })
                Assert.AreEqual(0f, PhHandFit.CurlDegrees(n), n);
        }

        [Test]
        public void ChildBone_FollowsTheDigit()
        {
            Assert.AreEqual("middle_02.R", PhHandFit.ChildBone("middle_01.R"));
            Assert.AreEqual("middle_03.R", PhHandFit.ChildBone("middle_02.R"));
            Assert.AreEqual("middle_03.R_end", PhHandFit.ChildBone("middle_03.R"));
            Assert.AreEqual("thumb_03.R_end", PhHandFit.ChildBone("thumb_03.R"));
            Assert.IsNull(PhHandFit.ChildBone("index_base.R"));
            Assert.IsNull(PhHandFit.ChildBone("hand.R"));
        }

        [Test]
        public void FlexionAxis_TurnsTheBoneTowardThePalm_AndIsZeroWhenDegenerate()
        {
            // fingers along +z, palm facing down: the axis is +x and a positive rotation takes +z toward -y (down, the palm side)
            var axis = PhHandFit.FlexionAxis(Vector3.forward, Vector3.down);
            AssertV(Vector3.right, axis, 1e-6f, "axis");
            var turned = RotateAbout(axis, 30f, Vector3.forward);
            Assert.Less(turned.y, -0.49f); Assert.Greater(turned.z, 0.86f);
            // a tilted thumb bone: any rotation about its axis still moves it toward the palm normal
            var thumb = new Vector3(-0.6f, -0.2f, 0.77f).normalized;
            var a2 = PhHandFit.FlexionAxis(thumb, Vector3.down);
            Assert.AreEqual(0f, Vector3.Dot(a2, thumb), 1e-5f); Assert.AreEqual(0f, Vector3.Dot(a2, Vector3.down), 1e-5f);
            Assert.Greater(Vector3.Dot(RotateAbout(a2, 20f, thumb), Vector3.down), Vector3.Dot(thumb, Vector3.down));
            AssertV(Vector3.zero, PhHandFit.FlexionAxis(Vector3.down, Vector3.down), 0f, "bone along the palm normal");
        }

        // ---- wrapper naming and the hand fallback ---------------------------------------------------------------------------------

        [Test]
        public void WrapperNames_AreTheFixedPhNames_InTheResourcesFolder()
        {
            CollectionAssert.AreEqual(new[] { "PH_Hand", "PH_Forearm", "PH_Sleeve", "PH_Brush", "PH_Stone", "PH_Table", "PH_PendantLamp", "PH_Plant", "PH_Window", "PH_SingingBowl", "PH_TeaCup", "PH_FramedPicture" }, PhModels.All);
            Assert.AreEqual(12, PhModels.All.Distinct().Count());
            foreach (var n in PhModels.All) StringAssert.StartsWith("PH_", n);
            Assert.AreEqual("PhantomModels/", PhModels.ResourceFolder);
            Assert.AreEqual("PH_Hand", PhModels.Hand);
            Assert.AreEqual("PH_Table", PhModels.Table);
        }

        [Test]
        public void HandSource_PrefersTheRiggedHand_FallsBackToTheGlove_ThenToTheProceduralHand()
        {
            Assert.AreEqual(PhModels.HandSource.Rigged, PhModels.ChooseHandSource(true, true));
            Assert.AreEqual(PhModels.HandSource.Rigged, PhModels.ChooseHandSource(true, false));
            Assert.AreEqual(PhModels.HandSource.Glove, PhModels.ChooseHandSource(false, true), "324213 is the automatic fallback when handRig_02.fbx is missing");
            Assert.AreEqual(PhModels.HandSource.None, PhModels.ChooseHandSource(false, false));
        }
    }
}
