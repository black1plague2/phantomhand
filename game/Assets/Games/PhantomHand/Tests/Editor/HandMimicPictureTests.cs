using System.IO;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// The virtual hand in the poses a tracked hand asks of it, on either arm: numbers for what can be measured (where the hand
    /// points when it turns at the wrist), and one sheet of pictures per arm for what only eyes can judge (do the fingers close
    /// toward the palm, is the thumb a thumb, is the left hand a left hand). The sheets go to sim/out/quest_diag/ (git-ignored).
    /// Category "UnityEngine": the baked hand model is instantiated and rendered.
    /// </summary>
    [Category("UnityEngine")]
    public class HandMimicPictureTests
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
            arm.PlaceFromCalibration(new Vector3(left ? -0.18f : 0.18f, 0.771f, 0.40f), Vector3.forward, 0f);
            return arm;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TheHand_TurnsAtTheWrist_TheWayTheRealOneDoes(bool left)
        {
            var arm = Arm(left);
            Assert.Less(Vector3.Angle(arm.HandForwardWorld, Vector3.forward), 0.01f, "at rest the hand lies along the forearm");

            // the real hand lifted 30 degrees at the wrist: fingers up
            Vector3 up30 = Quaternion.AngleAxis(-30f, Vector3.right) * Vector3.forward;
            arm.SetHandOrientation(up30, Quaternion.AngleAxis(-30f, Vector3.right) * Vector3.up);
            Assert.Less(Vector3.Angle(arm.HandForwardWorld, up30), 0.5f, "fingers up");
            Assert.Greater(arm.HandForwardWorld.y, 0.4f);

            // turned 25 degrees to the wearer's right, flat on the table: the fingers must go to +x for either arm
            Vector3 right25 = Quaternion.AngleAxis(25f, Vector3.up) * Vector3.forward;
            arm.SetHandOrientation(right25, Vector3.up);
            Assert.Less(Vector3.Angle(arm.HandForwardWorld, right25), 0.5f, "toward the wearer's right");
            Assert.Greater(arm.HandForwardWorld.x, 0.3f);

            // palm up: the turn about the forearm's own axis has no limit
            arm.SetHandOrientation(Vector3.forward, Vector3.down);
            Assert.Less(Vector3.Angle(arm.HandDorsalWorld, Vector3.down), 0.5f, "palm up");
            Assert.Less(Vector3.Angle(arm.HandForwardWorld, Vector3.forward), 0.5f, "and still along the forearm");

            // the thumb up, as for a handshake: the back of the hand faces away from the body's middle
            Vector3 outward = left ? Vector3.left : Vector3.right;
            arm.SetHandOrientation(Vector3.forward, outward);
            Assert.Less(Vector3.Angle(arm.HandDorsalWorld, outward), 0.5f, "thumb up");

            // palm up and lifted 40 degrees: both are kept
            Quaternion lift = Quaternion.AngleAxis(-40f, Vector3.right);
            arm.SetHandOrientation(lift * Vector3.forward, lift * Vector3.down);
            Assert.Less(Vector3.Angle(arm.HandForwardWorld, lift * Vector3.forward), 0.5f, "palm up, fingers lifted");
            Assert.Less(Vector3.Angle(arm.HandDorsalWorld, lift * Vector3.down), 0.5f);

            // a wild frame cannot fold the hand back over the forearm
            arm.SetHandOrientation(Vector3.back, Vector3.down);
            Assert.LessOrEqual(Vector3.Angle(arm.HandForwardWorld, Vector3.forward), VirtualArmRig.MaxWristDeg + 0.5f);

            arm.ResetHandOrientation();
            Assert.Less(Vector3.Angle(arm.HandForwardWorld, Vector3.forward), 0.01f);
        }

        // finger * 3 + joint, fingers thumb, index, middle, ring, little; degrees toward the palm
        private static float[] Pose(float[] thumb, float[] index, float[] others)
        {
            var f = new float[PhHandPose.FlexCount];
            for (int j = 0; j < 3; j++)
            {
                f[j] = thumb[j]; f[3 + j] = index[j];
                f[6 + j] = others[j]; f[9 + j] = others[j]; f[12 + j] = others[j];
            }
            return f;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Pictures_OfTheHandInSevenPoses(bool left)
        {
            var arm = Arm(left);
            float[] flat = { 0f, 0f, 0f }, closed = { 80f, 95f, 60f }, thumbIn = { 25f, 40f, 45f };
            var probe = new PhHandPose { FlexDeg = Pose(flat, flat, flat), Forward = Vector3.forward, Dorsal = Vector3.up };
            if (!arm.ApplyHandPose(in probe)) Assert.Ignore("no rigged hand on this machine (the procedural hand has no finger pose): nothing to picture");
            foreach (var smr in arm.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;

            string[] names = { "flat", "fist", "point", "half", "wrist up", "palm up", "thumb up" };
            float[][] poses =
            {
                Pose(flat, flat, flat), Pose(thumbIn, closed, closed), Pose(thumbIn, flat, closed),
                Pose(new[] { 10f, 15f, 15f }, new[] { 35f, 45f, 25f }, new[] { 35f, 45f, 25f }), Pose(flat, flat, flat),
                Pose(flat, flat, flat), Pose(flat, flat, flat),
            };
            const int w = 420, h = 340;
            var sheet = new Texture2D(w * poses.Length, h, TextureFormat.RGB24, false);
            var camGo = new GameObject("__hand_cam");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 40f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 10f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.22f, 0.22f, 0.24f);
                var lightGo = new GameObject("__hand_light"); lightGo.transform.SetParent(camGo.transform, false);
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(55f, 20f, 0f);
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                for (int i = 0; i < poses.Length; i++)
                {
                    var pose = new PhHandPose { FlexDeg = poses[i], Forward = Vector3.forward, Dorsal = Vector3.up };
                    arm.ApplyHandPose(in pose);
                    if (names[i] == "wrist up") arm.SetHandOrientation(Quaternion.AngleAxis(-35f, Vector3.right) * Vector3.forward, Quaternion.AngleAxis(-35f, Vector3.right) * Vector3.up);
                    else if (names[i] == "palm up") arm.SetHandOrientation(Vector3.forward, Vector3.down);
                    else if (names[i] == "thumb up") arm.SetHandOrientation(Vector3.forward, left ? Vector3.left : Vector3.right);
                    else arm.ResetHandOrientation();
                    // from the wearer's side: behind and above the wrist, looking at the middle of the hand
                    Vector3 mid = arm.WristWorld + Vector3.forward * 0.09f;
                    cam.transform.position = mid + new Vector3(0f, 0.30f, -0.30f);
                    cam.transform.LookAt(mid);
                    cam.targetTexture = rt;
                    cam.Render(); cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, w, h), i * w, 0);
                    RenderTexture.active = prev;
                }
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
                sheet.Apply();
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "sim", "out", "quest_diag"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "hand_poses_" + (left ? "left" : "right") + ".png");
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Debug.Log("[HandMimic] " + string.Join(" | ", names) + " -> " + path);
            }
            finally
            {
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(sheet);
            }
        }
    }
}
