using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// The stimulated arm can be the left one (the sleeve is worn on the left forearm since 9 Oct 2026). A left arm is the right-arm
    /// layout mirrored in x: the virtual arm sits to the RIGHT of the real wrist, toward the body's midline; the drift is positive
    /// toward +x; the outline and the HUD change sides; the other hand points. Category "UnityEngine": GameObjects are built.
    /// </summary>
    [Category("UnityEngine")]
    public class LeftArmTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        private VirtualArmRig Arm(bool left)
        {
            _go = new GameObject("arm");
            var arm = _go.AddComponent<VirtualArmRig>();
            arm.LeftArm = left;
            arm.Build(0.25f, 0.05f, 0.10f);
            return arm;
        }

        [Test]
        public void TheVirtualArm_SitsTowardTheMidline_OfEitherArm()
        {
            var right = Arm(false);
            right.PlaceFromCalibration(new Vector3(0.18f, 0.771f, 0.40f), Vector3.forward, 15f);
            Assert.AreEqual(0.03f, right.WristWorld.x, 1e-4f, "right arm: 15 cm to the LEFT of the real wrist");
            Object.DestroyImmediate(_go);

            var left = Arm(true);
            left.PlaceFromCalibration(new Vector3(-0.18f, 0.771f, 0.40f), Vector3.forward, 15f);
            Assert.AreEqual(-0.03f, left.WristWorld.x, 1e-4f, "left arm: 15 cm to the RIGHT of the real wrist");
            Assert.AreEqual(0.771f, left.WristWorld.y, 1e-4f); Assert.AreEqual(0.40f, left.WristWorld.z, 1e-4f);
        }

        [Test]
        public void ALeftArm_IsTheRightOneMirrored()
        {
            var right = Arm(false);
            var rightThumb = ThumbBoxX(right); float rightPalmX = right.transform.InverseTransformPoint(right.PalmTopWorld).x;
            Object.DestroyImmediate(_go);

            var left = Arm(true);
            Assert.AreEqual(-1f, left.transform.Find("ArmVisual").localScale.x, 1e-6f, "the visual is mirrored in the arm's own x");
            Assert.AreEqual(-rightThumb, ThumbBoxX(left), 1e-5f, "the thumb's hit box is on the other side");
            Assert.Less(rightThumb, 0f, "a right hand has its thumb on -x");
            Assert.AreEqual(-rightPalmX, left.transform.InverseTransformPoint(left.PalmTopWorld).x, 1e-5f);
            // the mirror must not change how far the fingers close
            left.Curl = 1f; Assert.AreEqual(1f, left.Curl, 1e-6f);
        }

        private static float ThumbBoxX(VirtualArmRig arm)
        {
            // the hit proxy's boxes are palm, fingers, thumb, forearm in that order
            return ((BoxCollider)arm.HitColliders[2]).center.x;
        }

        [Test]
        public void TheLayout_IsMirroredForALeftArm_AndComesBack()
        {
            _go = new GameObject("anchors");
            var anchors = _go.AddComponent<PhantomAnchors>();
            anchors.armRestOutline = Child("outline", new Vector3(0.18f, 0.752f, 0.15f), Quaternion.Euler(0f, 12f, 0f));
            anchors.hudPanel = Child("hud", new Vector3(-0.34f, 1.36f, 0.80f), Quaternion.Euler(10f, 0f, 0f));
            Vector3 f0 = anchors.armRestOutline.forward;

            anchors.LayOutFor(HandSide.Left);
            Assert.AreEqual(HandSide.Left, anchors.LayoutArm);
            Assert.AreEqual(-0.18f, anchors.armRestOutline.position.x, 1e-5f);
            Assert.AreEqual(0.15f, anchors.armRestOutline.position.z, 1e-5f);
            Assert.AreEqual(-f0.x, anchors.armRestOutline.forward.x, 1e-5f, "the forearm direction is mirrored too");
            Assert.AreEqual(f0.z, anchors.armRestOutline.forward.z, 1e-5f);
            Assert.AreEqual(0.34f, anchors.hudPanel.position.x, 1e-5f, "the HUD changes sides");
            Assert.Greater(Vector3.Dot(anchors.hudPanel.up, Vector3.up), 0.9f, "and still stands upright (a rotation, not a reflection)");

            anchors.LayOutFor(HandSide.Left);
            Assert.AreEqual(-0.18f, anchors.armRestOutline.position.x, 1e-5f, "asking twice changes nothing");
            anchors.LayOutFor(HandSide.Right);
            Assert.AreEqual(0.18f, anchors.armRestOutline.position.x, 1e-5f);
            Assert.AreEqual(f0.x, anchors.armRestOutline.forward.x, 1e-5f);
        }

        private Transform Child(string name, Vector3 pos, Quaternion rot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_go.transform, false);
            t.SetPositionAndRotation(pos, rot);
            return t;
        }

        [Test]
        public void TheDrift_IsPositiveTowardTheVirtualHand_ForEitherArm()
        {
            // pointed 3 cm toward the virtual hand: toward -x for a right arm, +x for a left arm
            Assert.AreEqual(3.0, DriftProbe.DriftCmFor(0.20, 0.23, -1), 1e-9, "right arm");
            Assert.AreEqual(3.0, DriftProbe.DriftCmFor(-0.20, -0.23, 1), 1e-9, "left arm");

            var m = new PhantomHandModule();
            m.Configure(Ph.P("{\"stimulated_side\":\"left\"}"), null);
            Assert.AreEqual(1.0, m.Probe.TowardVirtualSignX, "a left arm's virtual hand lies toward +x");
            m = new PhantomHandModule();
            m.Configure(Ph.P("{\"stimulated_side\":\"right\"}"), null);
            Assert.AreEqual(-1.0, m.Probe.TowardVirtualSignX);
        }

        [Test]
        public void JointNames_FollowTheArm()
        {
            Assert.AreEqual(OpusJoints.LWrist, PhArm.Wrist(HandSide.Left)); Assert.AreEqual(OpusJoints.RPalm, PhArm.Palm(HandSide.Right));
            Assert.AreEqual(OpusJoints.RIndexTip, PhArm.IndexTip(PhArm.Other(HandSide.Left)));
            Assert.AreEqual(-0.18, PhArm.X(HandSide.Left, 0.18)); Assert.AreEqual(0.18, PhArm.X(HandSide.Right, 0.18));
        }
    }
}
