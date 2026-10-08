using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// EditMode tests for the rigged hand on the virtual arm: the Renderer-generic model registration (a SkinnedMeshRenderer built here), curl driving the finger
    /// bones vs the squash fallback, the hit proxy and palm-top values coming from the wrapper's measured PhModelInfo. The hand is a hand-built hierarchy with the
    /// FBX's bone names (no wrapper is needed in Resources, so the tests run before and after the bake). PhModels.UseModels is switched off while an arm is built,
    /// so Build() makes the procedural arm whatever has been baked.
    /// </summary>
    public class PhRiggedHandTests
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

        private static Transform Child(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private static Transform Find(GameObject root, string name) { return root.GetComponentsInChildren<Transform>(true).First(t => t.name == name); }

        /// <summary>A hand wrapper with the FBX's bone names, identity bone rotations, fingers along +z at x offsets, palm DOWN (the wrapper frame the importer produces).
        /// Segment lengths 0.030 / 0.020 / 0.020 from the knuckle; the unweighted _Ctrl / _tip bones of the file are included to prove they are left alone.</summary>
        private GameObject MakeHand(bool fullRig = true)
        {
            var root = Track(new GameObject("PH_Hand_test"));
            var armature = Child(root.transform, "Armature", Vector3.zero);
            var pulse = Child(armature, "pulse.R", new Vector3(0f, 0f, -0.1f));
            var hand = Child(pulse, "hand.R", new Vector3(0f, 0f, 0.1f));
            var hand1 = Child(hand, "hand.R.001", new Vector3(0f, 0f, 0.027f));
            float[] xs = { -0.025f, -0.004f, 0.017f, 0.035f };
            for (int f = 0; f < (fullRig ? 4 : 1); f++)
            {
                string d = PhHandFit.FingerNames[f];
                var b = Child(hand1, PhHandFit.Bone(d, 0), new Vector3(xs[f], 0f, 0.026f));
                var s1 = Child(b, PhHandFit.Bone(d, 1), new Vector3(0f, 0f, 0.036f));
                var s2 = Child(s1, PhHandFit.Bone(d, 2), new Vector3(0f, 0f, 0.030f));
                var s3 = Child(s2, PhHandFit.Bone(d, 3), new Vector3(0f, 0f, 0.020f));
                Child(s3, PhHandFit.EndBone(d), new Vector3(0f, 0f, 0.020f));
                Child(b, d + "_Ctrl.R", new Vector3(0f, 0f, 0.036f));
                Child(armature, d + "_tip.R", new Vector3(xs[f], 0f, 0.18f));
            }
            if (fullRig)
            {
                var tb = Child(hand, "thumb_base.R", new Vector3(-0.01f, 0f, 0.03f));
                var t1 = Child(tb, "thumb_01.R", new Vector3(-0.02f, -0.02f, 0.02f));
                var t2 = Child(t1, "thumb_02.R", new Vector3(-0.017f, 0.006f, 0.0175f));
                var t3 = Child(t2, "thumb_03.R", new Vector3(-0.0113f, -0.0045f, 0.0164f));
                Child(t3, "thumb_03.R_end", new Vector3(-0.0134f, -0.0106f, 0.0212f));
            }
            return root;
        }

        private VirtualArmRig MakeArm()
        {
            var go = Track(new GameObject("arm"));
            var arm = go.AddComponent<VirtualArmRig>();
            arm.Build(0.25f, 0.05f, 0.10f);
            return arm;
        }

        private static Vector3 Dir(float deg) { float r = deg * Mathf.Deg2Rad; return new Vector3(0f, -Mathf.Sin(r), Mathf.Cos(r)); }

        // ---- the rig: bind, curl, rest ------------------------------------------------------------------------------------------

        [Test]
        public void Bind_FindsTheFifteenDrivenBones_ByName()
        {
            var rig = MakeHand().AddComponent<PhRiggedHand>();
            Assert.IsTrue(rig.Bind());
            Assert.AreEqual(15, rig.JointCount);
            Assert.IsTrue(rig.IsBound);
        }

        [Test]
        public void SetCurl_ClosesTheFingerChainTowardThePalm_WithTheCumulativeAnglesOfTheSchedule()
        {
            var go = MakeHand();
            var rig = go.AddComponent<PhRiggedHand>();
            Assert.IsTrue(rig.Bind());
            var tip = Find(go, "index_03.R_end"); var knuckle = Find(go, "index_01.R");
            Vector3 k = knuckle.position, rest = tip.position;
            rig.SetCurl(1f);
            // knuckle bone stays; every segment turns about +x (the wrapper's lateral axis) by its schedule angle on top of its parent's: 72, 72+79, 72+79+51 degrees
            Vector3 expected = k + 0.030f * Dir(72f) + 0.020f * Dir(151f) + 0.020f * Dir(202f);
            Assert.AreEqual(k.x, knuckle.position.x, 1e-6f); Assert.AreEqual(k.y, knuckle.position.y, 1e-6f); Assert.AreEqual(k.z, knuckle.position.z, 1e-6f);
            Assert.AreEqual(expected.x, tip.position.x, 1e-4f); Assert.AreEqual(expected.y, tip.position.y, 1e-4f); Assert.AreEqual(expected.z, tip.position.z, 1e-4f);
            Assert.Less(tip.position.y, rest.y - 0.02f, "the fingertip went down toward the palm side");
            Assert.Less(tip.position.z, rest.z - 0.03f, "and back toward the wrist");
            rig.SetCurl(0.5f);
            Assert.Less(tip.position.y, rest.y - 0.02f, "half closed");
            Assert.Greater(Vector3.Distance(tip.position, expected), 0.01f, "half curl is a different pose from the full curl (the tip swings back up past 180 degrees)");
            rig.SetCurl(0f);
            Assert.AreEqual(rest.y, tip.position.y, 1e-6f); Assert.AreEqual(rest.z, tip.position.z, 1e-6f);
        }

        [Test]
        public void SetCurl_LeavesTheMetacarpal_TheUnweightedChain_AndTheForearmBonesWhereTheyAre()
        {
            var go = MakeHand();
            var rig = go.AddComponent<PhRiggedHand>();
            rig.Bind();
            var still = new[] { "pulse.R", "hand.R", "hand.R.001", "index_base.R", "index_Ctrl.R", "index_tip.R", "thumb_base.R", "ring_base.R" };
            var before = still.Select(n => Find(go, n).position).ToArray();
            rig.SetCurl(1f);
            for (int i = 0; i < still.Length; i++) Assert.AreEqual(0f, Vector3.Distance(before[i], Find(go, still[i]).position), 1e-6f, still[i]);
        }

        [Test]
        public void SetCurl_ClosesTheThumbToo_TowardThePalmSide()
        {
            var go = MakeHand();
            var rig = go.AddComponent<PhRiggedHand>();
            rig.Bind();
            var tip = Find(go, "thumb_03.R_end");
            Vector3 rest = tip.position;
            rig.SetCurl(1f);
            Assert.Greater(Vector3.Dot(tip.position - rest, Vector3.down), 0.004f, "the thumb tip moved toward the palm normal (down)");
        }

        [Test]
        public void SetCurl_UsesTheWrapperFrame_NotWorldDown()
        {
            var go = MakeHand();
            go.transform.rotation = Quaternion.Euler(0f, 35f, 90f);      // lying on its side: the palm normal is the wrapper's local down, not the world's
            var rig = go.AddComponent<PhRiggedHand>();
            Assert.IsTrue(rig.Bind());
            var tip = Find(go, "middle_03.R_end");
            float y0 = go.transform.InverseTransformPoint(tip.position).y;
            rig.SetCurl(1f);
            Assert.Less(go.transform.InverseTransformPoint(tip.position).y, y0 - 0.02f);
        }

        [Test]
        public void TooFewBones_DoNotBind_SoTheArmWillSquashInstead()
        {
            var go = MakeHand(fullRig: false);
            var rig = go.AddComponent<PhRiggedHand>();
            Assert.IsFalse(rig.Bind());
            Assert.AreEqual(3, rig.JointCount);
            Assert.IsFalse(rig.IsBound);
        }

        [Test]
        public void PoseHash_ChangesWithTheFingerBones_AndRepeats()
        {
            var go = MakeHand();
            var rig = go.AddComponent<PhRiggedHand>();
            rig.Bind();
            int h0 = rig.PoseHash();
            Assert.AreEqual(h0, rig.PoseHash());
            rig.SetCurl(0.4f);
            Assert.AreNotEqual(h0, rig.PoseHash());
            rig.SetCurl(0f);
            Assert.AreEqual(h0, rig.PoseHash());
        }

        // ---- the arm: bones vs squash ---------------------------------------------------------------------------------------------

        [Test]
        public void Arm_Curl_DrivesTheBones_WhenTheHandHasARig_AndDoesNotSquash()
        {
            var arm = MakeArm();
            var go = MakeHand();
            go.AddComponent<PhModelInfo>(); go.AddComponent<PhRiggedHand>();
            arm.SetHandModel(go);
            var tip = Find(go, "index_03.R_end");
            float y0 = tip.position.y; int h0 = arm.PoseHash();
            arm.Curl = 1f;
            Assert.Less(tip.position.y, y0 - 0.02f, "bones closed");
            Assert.AreEqual(Vector3.one, go.transform.localScale, "no squash on a rigged hand");
            Assert.AreNotEqual(h0, arm.PoseHash());
            arm.Curl = 0f;
            Assert.AreEqual(y0, tip.position.y, 1e-6f);
            Assert.AreEqual(h0, arm.PoseHash());
        }

        [Test]
        public void Arm_Curl_SquashesTheGloveMesh_WhichHasNoRig()
        {
            var arm = MakeArm();
            var glove = Track(new GameObject("glove")); glove.AddComponent<PhModelInfo>();
            arm.SetHandModel(glove);
            arm.Curl = 1f;
            Assert.AreEqual(1.08f, glove.transform.localScale.x, 1e-5f); Assert.AreEqual(1.30f, glove.transform.localScale.y, 1e-5f); Assert.AreEqual(0.78f, glove.transform.localScale.z, 1e-5f);
            arm.Curl = 0f;
            Assert.AreEqual(Vector3.one, glove.transform.localScale);
        }

        [Test]
        public void Arm_Curl_FallsBackToTheSquash_WhenTheRigHasTooFewBones()
        {
            var arm = MakeArm();
            var go = MakeHand(fullRig: false);
            go.AddComponent<PhModelInfo>(); go.AddComponent<PhRiggedHand>();
            arm.SetHandModel(go);
            arm.Curl = 1f;
            Assert.AreEqual(Quaternion.identity, Find(go, "index_01.R").localRotation, "the unusable rig is not driven");
            Assert.AreNotEqual(Vector3.one, go.transform.localScale, "the model is squashed instead");
        }

        // ---- Renderer-generic registration ------------------------------------------------------------------------------------------

        private GameObject MakeSkinnedModel(string name, Material mat, out SkinnedMeshRenderer smr)
        {
            var go = Track(new GameObject(name));
            var bone = new GameObject("bone").transform;
            bone.SetParent(go.transform, false);
            var mesh = Track(new Mesh { name = name + "_mesh" });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var w = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            mesh.boneWeights = new[] { w, w, w };
            mesh.bindposes = new[] { bone.worldToLocalMatrix * go.transform.localToWorldMatrix };
            smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.bones = new[] { bone }; smr.rootBone = bone; smr.sharedMaterial = mat;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; smr.receiveShadows = true;
            return go;
        }

        [Test]
        public void RegisterModel_TakesASkinnedMeshRenderer_ForShadowsMaterialAndFade()
        {
            var arm = MakeArm();
            var own = Track(PhMaterials.Make("own_skin", new Color(0.6f, 0.4f, 0.3f)));
            SkinnedMeshRenderer smr;
            var model = MakeSkinnedModel("skinned", own, out smr);
            arm.RegisterModel(model, null);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, smr.shadowCastingMode, "no shadow casters in the room");
            Assert.IsFalse(smr.receiveShadows);
            Assert.AreSame(own, smr.sharedMaterial, "no override: the textured wrapper material is kept");
            arm.Alpha = 0.5f;
            Assert.AreNotSame(own, smr.sharedMaterial, "A2 fade swaps in the transparent twin");
            Assert.AreEqual(0.5f, smr.sharedMaterial.color.a, 1e-3f);
            arm.Alpha = 0f;
            Assert.IsFalse(smr.enabled, "invisible at alpha 0");
            arm.Alpha = 1f;
            Assert.IsTrue(smr.enabled); Assert.AreSame(own, smr.sharedMaterial);
        }

        [Test]
        public void RegisterModel_AppliesTheOverrideMaterial_ToSkinnedAndPlainMeshRenderers()
        {
            var arm = MakeArm();
            var own = Track(PhMaterials.Make("own", Color.white));
            var flat = Track(PhMaterials.Make("flat_skin", new Color(0.74f, 0.58f, 0.48f)));
            SkinnedMeshRenderer smr;
            var model = MakeSkinnedModel("mixed", own, out smr);
            var plain = new GameObject("plain"); plain.transform.SetParent(model.transform, false);
            plain.AddComponent<MeshFilter>().sharedMesh = smr.sharedMesh;
            var mr = plain.AddComponent<MeshRenderer>(); mr.sharedMaterial = own;
            arm.RegisterModel(model, flat);
            Assert.AreSame(flat, smr.sharedMaterial); Assert.AreSame(flat, mr.sharedMaterial);
            arm.Alpha = 0.25f;
            Assert.AreEqual(0.25f, smr.sharedMaterial.color.a, 1e-3f); Assert.AreEqual(0.25f, mr.sharedMaterial.color.a, 1e-3f);
            arm.Alpha = 1f;
            Assert.AreSame(flat, smr.sharedMaterial); Assert.AreSame(flat, mr.sharedMaterial);
        }

        // ---- hit proxy and palm top from the measured info ---------------------------------------------------------------------------

        private static BoxCollider Box(VirtualArmRig arm, int i) { return (BoxCollider)arm.HitColliders[i]; }     // 0 palm, 1 fingers, 2 thumb, 3 forearm

        [Test]
        public void HitProxy_AndPalmTop_FollowTheRiggedHandsMeasurements()
        {
            var arm = MakeArm();
            var go = MakeHand();
            var info = go.AddComponent<PhModelInfo>();
            info.palmLenM = 0.089f; info.palmWidthM = 0.078f; info.palmCenterX = 0.005f; info.palmTopY = 0.019f; info.fingerTopY = 0.011f; info.fingerLenM = 0.09f;
            info.thumbCenterXZ = new Vector2(-0.052f, 0.076f); info.thumbSizeXZ = new Vector2(0.061f, 0.075f);
            arm.SetHandModel(go);
            Assert.AreEqual(4, arm.HitColliders.Length);
            var palm = Box(arm, 0); var fing = Box(arm, 1); var thumb = Box(arm, 2);
            Assert.AreEqual(0.078f, palm.size.x, 1e-6f); Assert.AreEqual(0.089f, palm.size.z, 1e-6f); Assert.AreEqual(0.005f, palm.center.x, 1e-6f); Assert.AreEqual(0.0445f, palm.center.z, 1e-6f);
            Assert.AreEqual(0.019f, palm.center.y + palm.size.y * 0.5f, 1e-5f, "the palm box top is the measured dorsal surface");
            Assert.AreEqual(0.011f, fing.center.y + fing.size.y * 0.5f, 1e-5f);
            Assert.AreEqual(0.09f, fing.size.z, 1e-6f); Assert.AreEqual(0.089f + 0.045f, fing.center.z, 1e-6f);
            Assert.AreEqual(-0.052f, thumb.center.x, 1e-6f); Assert.AreEqual(0.076f, thumb.center.z, 1e-6f); Assert.AreEqual(0.061f, thumb.size.x, 1e-6f); Assert.AreEqual(0.075f, thumb.size.z, 1e-6f);
            var top = arm.PalmTopWorld;
            Assert.AreEqual(0.005f, top.x, 1e-5f); Assert.AreEqual(0.019f, top.y, 1e-5f); Assert.AreEqual(0.0445f, top.z, 1e-5f, "the stone is aimed at the measured palm centre");
            foreach (var c in arm.HitColliders) Assert.IsNotNull(c.GetComponentInParent<VirtualHandHit>(), "the stone only counts hits on the hand proxy");
        }

        [Test]
        public void HitProxy_OfAGloveInfo_KeepsTheOldConstants()
        {
            var arm = MakeArm();
            var glove = Track(new GameObject("glove"));
            var info = glove.AddComponent<PhModelInfo>();      // defaults = the glove's constants; the glove importer sets palmTopY / fingerTopY
            info.palmTopY = 0.025f; info.fingerTopY = 0.021f;
            arm.SetHandModel(glove);
            var palm = Box(arm, 0); var fing = Box(arm, 1); var thumb = Box(arm, 2);
            Assert.AreEqual(0.092f, palm.size.x, 1e-6f); Assert.AreEqual(0.098f, palm.size.z, 1e-6f); Assert.AreEqual(0f, palm.center.x); Assert.AreEqual(0.049f, palm.center.z, 1e-6f);
            Assert.AreEqual(0.088f, fing.size.x, 1e-6f); Assert.AreEqual(0.08f, fing.size.z, 1e-6f); Assert.AreEqual(0.098f + 0.04f, fing.center.z, 1e-6f);
            Assert.AreEqual(-0.055f, thumb.center.x, 1e-6f); Assert.AreEqual(0.05f, thumb.center.z, 1e-6f); Assert.AreEqual(0.04f, thumb.size.x, 1e-6f); Assert.AreEqual(0.07f, thumb.size.z, 1e-6f);
            Assert.AreEqual(0.049f, arm.PalmTopWorld.z, 1e-6f); Assert.AreEqual(0.025f, arm.PalmTopWorld.y, 1e-6f);
        }

        [Test]
        public void ProceduralHand_KeepsItsOwnHitProxy()
        {
            var arm = MakeArm();
            var palm = Box(arm, 0);
            Assert.AreEqual(0.092f, palm.size.x, 1e-6f); Assert.AreEqual(0.032f, palm.size.y, 1e-6f); Assert.AreEqual(0.098f, palm.size.z, 1e-6f);
            Assert.AreEqual(-ArmGeometry.WristHalfHeight + 0.022f, arm.PalmTopWorld.y, 1e-6f);
        }
    }
}
