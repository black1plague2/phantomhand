using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// EditMode tests for PhRiggedHand.SetFlexion and VirtualArmRig.ApplyHandPose on a hand-built bone hierarchy with the FBX's bone names (same idea as PhRiggedHandTests).
    /// The test hand's rest pose is not flat, as handRig_02's is not: every joint has a bend built in (RestBend), so "measured bend minus rest bend" is really exercised.
    /// Palm DOWN, fingers +z, wrist (hand.R) at the origin, all bone rotations identity, so a bone's world position is just its local offsets added up.
    /// Needs the native engine (GameObjects, Quaternion), hence Category("UnityEngine").
    /// </summary>
    public class PhRiggedHandFlexionTests
    {
        private readonly List<Object> _made = new List<Object>();
        private bool _prevUse;

        [SetUp]
        public void SetUp() { _prevUse = PhModels.UseModels; PhModels.UseModels = false; }

        [TearDown]
        public void TearDown()
        {
            PhModels.UseModels = _prevUse;
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private T Track<T>(T o) where T : Object { _made.Add(o); return o; }

        // Bend of every joint in the bind pose, degrees toward the palm, in PhHandPose order (thumb first, then index .. little). Close to what handRig_02 measures:
        // the fingers slightly hyperextended at the knuckle and curved after it, the thumb kinked.
        private static readonly float[] RestBend = { -36f, 27f, 10f, -10f, 10f, 9f, -9f, 17f, 6f, -9f, 20f, 8f, -13f, 12f, 11f };
        // first bone of each digit (thumb_01, index_01 ..) = the knuckle, wrist at the origin; then the three bone lengths up to the tip
        private static readonly Vector3[] Knuckle =
        {
            new Vector3(-0.031f, -0.022f, 0.050f), new Vector3(-0.026f, 0f, 0.088f), new Vector3(-0.005f, 0f, 0.090f), new Vector3(0.016f, 0f, 0.085f), new Vector3(0.034f, 0f, 0.070f),
        };
        private static readonly float[][] Len =
        {
            new[] { 0.025f, 0.020f, 0.027f }, new[] { 0.043f, 0.024f, 0.022f }, new[] { 0.047f, 0.027f, 0.023f }, new[] { 0.044f, 0.026f, 0.022f }, new[] { 0.034f, 0.020f, 0.020f },
        };

        private static Vector3 Turn(Vector3 axis, float deg, Vector3 v)      // Rodrigues, written out so the test does not lean on Quaternion.AngleAxis like the rig does
        {
            axis = axis.normalized;
            float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad);
            return v * c + Vector3.Cross(axis, v) * s + axis * (Vector3.Dot(axis, v) * (1f - c));
        }

        private static Transform Child(Transform parent, string name, Vector3 worldPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            return go.transform;
        }

        private static Transform Find(GameObject root, string name) { return root.GetComponentsInChildren<Transform>(true).First(t => t.name == name); }

        /// <summary>The wrapper as the importer makes it: pulse.R, hand.R (the wrist), hand.R.001, each digit's _base.R and then _01.._03 and the _end leaf. Each digit lies in its
        /// bending plane (wrist to knuckle and the palm normal) with the RestBend built in, so the rest bends PhRiggedHand measures equal RestBend. withWrist = false leaves hand.R out.</summary>
        private GameObject MakeHand(bool withWrist = true)
        {
            var root = Track(new GameObject("PH_Hand_flex"));
            var armature = Child(root.transform, "Armature", Vector3.zero);
            var pulse = Child(armature, "pulse.R", new Vector3(0f, 0f, -0.02f));
            Transform hand = withWrist ? Child(pulse, PhHandFit.WristBone, Vector3.zero) : pulse;
            var palm = Child(hand, "hand.R.001", new Vector3(0f, 0f, 0.027f));
            for (int f = 0; f < 5; f++)
            {
                string d = PhHandFit.DigitOf(f);
                var metacarpal = Child(f == 0 ? hand : palm, PhHandFit.Bone(d, 0), f == 0 ? new Vector3(-0.001f, 0.002f, 0.027f) : new Vector3(0f, 0.003f, 0.054f));
                Vector3 p = Knuckle[f];
                Vector3 dir = p.normalized;                                           // wrist -> knuckle
                Vector3 axis = PhHandFit.FlexionAxis(dir, Vector3.down);
                var bone = Child(metacarpal, PhHandFit.Bone(d, 1), p);
                for (int s = 1; s <= 3; s++)
                {
                    dir = Turn(axis, RestBend[f * 3 + s - 1], dir);
                    p += dir * Len[f][s - 1];
                    bone = Child(bone, s < 3 ? PhHandFit.Bone(d, s + 1) : PhHandFit.EndBone(d), p);
                }
            }
            return root;
        }

        private PhRiggedHand BoundRig(out GameObject go, bool withWrist = true)
        {
            go = MakeHand(withWrist);
            var rig = go.AddComponent<PhRiggedHand>();
            Assert.IsTrue(rig.Bind(), "the 15 driven bones are found");
            return rig;
        }

        private static string BoneOfSlot(int slot) { return PhHandFit.Bone(PhHandFit.DigitOf(slot / 3), slot % 3 + 1); }

        private static Quaternion[] LocalRotations(GameObject go) { return PhHandFit.DrivenBones().Select(n => Find(go, n).localRotation).ToArray(); }

        /// <summary>The 15 bends of the posed rig, read the way PhHandPoseSolver reads a tracked hand: the bone positions go in as the 21 joints.</summary>
        private static float[] ReadRig(GameObject go)
        {
            var joints = new Vector3[HandSkeleton.JointCount];
            joints[HandSkeleton.Wrist] = Find(go, PhHandFit.WristBone).position;
            for (int f = 0; f < 5; f++)
            {
                string d = PhHandFit.DigitOf(f);
                for (int s = 1; s <= 3; s++) joints[HandSkeleton.Point(f, s - 1)] = Find(go, PhHandFit.Bone(d, s)).position;
                joints[HandSkeleton.Point(f, 3)] = Find(go, PhHandFit.EndBone(d)).position;
            }
            var pose = new PhHandPose();
            Assert.IsTrue(PhHandPoseSolver.TrySolve(joints, HandSide.Right, ref pose), "the rig reads as a hand");
            return pose.FlexDeg;
        }

        private static float[] RestPlus(int slot, float deg) { var f = (float[])RestBend.Clone(); f[slot] += deg; return f; }

        // ---- SetFlexion --------------------------------------------------------------------------------------------------------------------

        [Test, Category("UnityEngine")]
        public void SetFlexion_WithTheRestBends_LeavesEveryBoneAtRest()
        {
            GameObject go;
            var rig = BoundRig(out go);
            var rest = LocalRotations(go);
            var tips = new[] { "index", "middle", "thumb" }.Select(d => Find(go, PhHandFit.EndBone(d)).position).ToArray();
            rig.SetFlexion(RestBend);
            var now = LocalRotations(go);
            for (int i = 0; i < rest.Length; i++) Assert.Less(Quaternion.Angle(rest[i], now[i]), 0.01f, "bone " + i);
            var tipsNow = new[] { "index", "middle", "thumb" }.Select(d => Find(go, PhHandFit.EndBone(d)).position).ToArray();
            for (int i = 0; i < tips.Length; i++) Assert.AreEqual(0f, Vector3.Distance(tips[i], tipsNow[i]), 1e-5f);
        }

        [Category("UnityEngine")]
        [TestCase(0)]       // thumb, first bone
        [TestCase(4)]       // index, middle joint
        [TestCase(7)]       // middle finger, middle joint
        [TestCase(9)]       // ring, base joint
        [TestCase(14)]      // little finger, end joint
        public void SetFlexion_FortyDegreesAboveTheRestBend_TurnsThatBoneFortyDegreesTowardThePalm_AndNothingElse(int slot)
        {
            GameObject go;
            var rig = BoundRig(out go);
            var bone = Find(go, BoneOfSlot(slot));
            var child = Find(go, PhHandFit.ChildBone(BoneOfSlot(slot)));
            Vector3 pivot = bone.position, before = child.position - bone.position;
            var rest = LocalRotations(go);

            rig.SetFlexion(RestPlus(slot, 40f));

            Vector3 after = child.position - bone.position;
            Assert.AreEqual(40f, Vector3.Angle(before, after), 0.05f, "the bone turned 40 degrees");
            Assert.Greater(Vector3.Dot(after - before, Vector3.down), 0.005f, "toward the palm (the palm normal is down)");
            Assert.AreEqual(0f, Vector3.Distance(pivot, bone.position), 1e-6f, "about its own joint");
            var now = LocalRotations(go);
            int turned = PhHandFit.DrivenBones().ToList().IndexOf(BoneOfSlot(slot));
            for (int i = 0; i < rest.Length; i++)
            {
                if (i == turned) Assert.AreEqual(40f, Quaternion.Angle(rest[i], now[i]), 0.05f, "the asked-for bone");
                else Assert.Less(Quaternion.Angle(rest[i], now[i]), 0.01f, "bone " + i + " stays");
            }
        }

        [Test, Category("UnityEngine")]
        public void SetFlexion_AFlatTrackedHand_StraightensTheRigsCurvedFingers()
        {
            GameObject go;
            var rig = BoundRig(out go);
            rig.SetFlexion(new float[15]);
            var read = ReadRig(go);
            for (int i = 0; i < 15; i++) Assert.AreEqual(0f, read[i], 0.5f, "joint " + i);
        }

        [Test, Category("UnityEngine")]
        public void SetFlexion_ThenReadingTheRigWithTheSolver_GivesBackTheAnglesThatWereAsked()
        {
            GameObject go;
            var rig = BoundRig(out go);
            var asked = new[] { 20f, 45f, 30f, 35f, 70f, 40f, 15f, 90f, 55f, 45f, 80f, 30f, 25f, 60f, 20f };
            rig.SetFlexion(asked);
            var read = ReadRig(go);
            for (int i = 0; i < 15; i++) Assert.AreEqual(asked[i], read[i], 1f, "joint " + i);
        }

        [Test, Category("UnityEngine")]
        public void SetFlexion_AndSetCurl_TheLastCallWins()
        {
            GameObject go;
            var rig = BoundRig(out go);
            var rest = LocalRotations(go);
            var tip = Find(go, PhHandFit.EndBone("index"));
            Vector3 restTip = tip.position;

            rig.SetCurl(1f);
            Assert.Greater(Vector3.Distance(restTip, tip.position), 0.02f, "curled");
            rig.SetFlexion(RestBend);
            Assert.AreEqual(0f, Vector3.Distance(restTip, tip.position), 1e-5f, "SetFlexion after SetCurl: back at the rest bends");

            rig.SetFlexion(RestPlus(3, 50f));
            Assert.Greater(Vector3.Distance(restTip, tip.position), 0.01f, "posed");
            rig.SetCurl(0f);
            var now = LocalRotations(go);
            for (int i = 0; i < rest.Length; i++) Assert.Less(Quaternion.Angle(rest[i], now[i]), 0.001f, "SetCurl after SetFlexion: the rest pose, bone " + i);
        }

        [Test, Category("UnityEngine")]
        public void SetFlexion_LimitsTheTurn_AndIgnoresShortArraysNaNAndInfinity()
        {
            GameObject go;
            var rig = BoundRig(out go);
            var bone = Find(go, BoneOfSlot(3)); var child = Find(go, PhHandFit.ChildBone(BoneOfSlot(3)));
            Vector3 before = child.position - bone.position;

            rig.SetFlexion(RestPlus(3, 1000f));
            Assert.AreEqual(PhRiggedHand.MaxTurnDeg, Vector3.Angle(before, child.position - bone.position), 0.05f);
            rig.SetFlexion(RestPlus(3, -1000f));
            Assert.AreEqual(-PhRiggedHand.MinTurnDeg, Vector3.Angle(before, child.position - bone.position), 0.05f);

            rig.SetFlexion(RestBend);
            var rest = LocalRotations(go);
            rig.SetFlexion(null);
            rig.SetFlexion(new float[14]);
            var nan = RestPlus(3, 40f); nan[4] = float.NaN; nan[5] = float.PositiveInfinity;
            rig.SetFlexion(nan);
            var now = LocalRotations(go);
            int index1 = PhHandFit.DrivenBones().ToList().IndexOf(BoneOfSlot(3));
            for (int i = 0; i < rest.Length; i++)
            {
                if (i == index1) Assert.AreEqual(40f, Quaternion.Angle(rest[i], now[i]), 0.05f, "the good entry is applied");
                else Assert.Less(Quaternion.Angle(rest[i], now[i]), 0.01f, "bone " + i + " ignored");
            }
        }

        [Test, Category("UnityEngine")]
        public void WithoutTheWristBone_NoBoneIsMapped_SoSetFlexionDoesNothing_AndSetCurlStillWorks()
        {
            GameObject go;
            var rig = BoundRig(out go, withWrist: false);
            Assert.AreEqual(15, rig.JointCount);
            var rest = LocalRotations(go);
            rig.SetFlexion(RestPlus(3, 40f));
            var now = LocalRotations(go);
            for (int i = 0; i < rest.Length; i++) Assert.Less(Quaternion.Angle(rest[i], now[i]), 0.001f, "bone " + i);
            var tip = Find(go, PhHandFit.EndBone("index"));
            Vector3 restTip = tip.position;
            rig.SetCurl(1f);
            Assert.Greater(Vector3.Distance(restTip, tip.position), 0.02f);
        }

        [Test, Category("UnityEngine")]
        public void PoseHash_FollowsTheFlexion_AndComesBackAtRest()
        {
            GameObject go;
            var rig = BoundRig(out go);
            int h0 = rig.PoseHash();
            Assert.AreEqual(h0, rig.PoseHash());
            rig.SetFlexion(RestPlus(5, 30f));
            Assert.AreNotEqual(h0, rig.PoseHash());
            rig.SetCurl(0f);
            Assert.AreEqual(h0, rig.PoseHash());
        }

        // ---- VirtualArmRig.ApplyHandPose ------------------------------------------------------------------------------------------------------

        private VirtualArmRig MakeArm()
        {
            var go = Track(new GameObject("arm"));
            var arm = go.AddComponent<VirtualArmRig>();
            arm.Build(0.25f, 0.05f, 0.10f);
            return arm;
        }

        [Test, Category("UnityEngine")]
        public void Arm_ApplyHandPose_TurnsTheRiggedHandsBones_AndSaysSo()
        {
            var arm = MakeArm();
            var go = MakeHand();
            go.AddComponent<PhModelInfo>(); go.AddComponent<PhRiggedHand>();
            arm.SetHandModel(go);
            var tip = Find(go, PhHandFit.EndBone("index"));
            Vector3 restTip = tip.position;

            var pose = new PhHandPose { FlexDeg = (float[])RestBend.Clone() };
            Assert.IsTrue(arm.ApplyHandPose(pose));
            Assert.AreEqual(0f, Vector3.Distance(restTip, tip.position), 1e-5f, "the rig's own rest bends leave the hand at rest");

            pose.FlexDeg[3] += 50f; pose.FlexDeg[4] += 50f;
            Assert.IsTrue(arm.ApplyHandPose(in pose));
            Assert.Less(tip.position.y, restTip.y - 0.01f, "the fingertip went toward the palm");

            arm.Curl = 0f;
            Assert.AreEqual(restTip.y, tip.position.y, 1e-6f, "Curl after ApplyHandPose wins");
        }

        [Test, Category("UnityEngine")]
        public void Arm_ApplyHandPose_IsFalse_WithoutARiggedHand_AndWithoutEnoughAngles()
        {
            var arm = MakeArm();
            var pose = new PhHandPose { FlexDeg = new float[15] };
            Assert.IsFalse(arm.ApplyHandPose(pose), "the procedural hand has no rig");

            var glove = Track(new GameObject("glove")); glove.AddComponent<PhModelInfo>();
            arm.SetHandModel(glove);
            Assert.IsFalse(arm.ApplyHandPose(pose), "the glove mesh has no rig either");
            Assert.AreEqual(Vector3.one, glove.transform.localScale, "and ApplyHandPose does not squash it");

            var go = MakeHand();
            go.AddComponent<PhModelInfo>(); go.AddComponent<PhRiggedHand>();
            arm.SetHandModel(go);
            Assert.IsFalse(arm.ApplyHandPose(new PhHandPose()), "no angles");
            Assert.IsFalse(arm.ApplyHandPose(new PhHandPose { FlexDeg = new float[10] }), "too few angles");
            Assert.IsTrue(arm.ApplyHandPose(pose));
        }
    }
}
