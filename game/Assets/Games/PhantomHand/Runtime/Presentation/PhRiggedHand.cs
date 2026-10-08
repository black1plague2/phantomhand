using System.Collections.Generic;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Finger curl for the rigged hand wrapper (PH_Hand baked from handRig_02.fbx by PhantomModelImporter). <see cref="Bind"/> finds the driven
    /// bones (PhHandFit.DrivenBones) by name below this transform, snapshots their rest rotation and derives every flexion axis from the rest
    /// pose: rotating a bone about cross(boneDirection, palmNormal) by a positive angle turns it toward the palm. The importer fixes the
    /// wrapper frame (+z toward the fingers, +y dorsal), so the palm normal is the wrapper's local down. <see cref="SetCurl"/> then sets
    /// localRotation = AngleAxis(maxDeg * curl, axisInParent) * rest on every driven bone. When fewer than <see cref="MinJoints"/> bones are
    /// found the rig does not allow bone driving and VirtualArmRig falls back to its squash approximation.
    /// <see cref="SetFlexion"/> copies a tracked hand instead: each bone turns about the same axis by the measured bend minus its own bend in the rest pose
    /// (the rest pose is not a flat hand), both measured by PhHandPoseSolver; the last of SetCurl and SetFlexion wins.
    /// </summary>
    public sealed class PhRiggedHand : MonoBehaviour
    {
        public const int MinJoints = 12;     // 4 fingers x 3 bones

        /// <summary>Largest turn away from the rest pose that <see cref="SetFlexion"/> applies to one bone: the solver's range (-30..120 degrees) plus the rest bends
        /// of handRig_02 (about -37 for the thumb's first bone up to +28), rounded.</summary>
        public const float MinTurnDeg = -60f, MaxTurnDeg = 160f;

        // Flex = slot in PhHandPose.FlexDeg (-1: not mapped, only SetCurl moves the bone); RestBendDeg = the bone's bend in the bind pose
        private struct Joint { public Transform Bone; public Quaternion Rest; public Vector3 AxisInParent; public float MaxDeg; public int Flex; public float RestBendDeg; }

        private readonly List<Joint> _joints = new List<Joint>();
        private float _curl;

        public int JointCount { get { return _joints.Count; } }
        public bool IsBound { get { return _joints.Count >= MinJoints; } }
        public float Curl { get { return _curl; } }

        /// <summary>Captures the rest pose of the driven bones; returns whether the rig can be driven.</summary>
        public bool Bind()
        {
            _joints.Clear(); _curl = 0f;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            Vector3 palmWorld = transform.TransformDirection(Vector3.down).normalized;
            Transform wrist;
            byName.TryGetValue(PhHandFit.WristBone, out wrist);
            foreach (var name in PhHandFit.DrivenBones())
            {
                Transform bone, child;
                if (!byName.TryGetValue(name, out bone) || !byName.TryGetValue(PhHandFit.ChildBone(name), out child)) continue;
                Vector3 axisWorld = PhHandFit.FlexionAxis(child.position - bone.position, palmWorld);
                if (axisWorld == Vector3.zero) continue;
                Transform parent = bone.parent;
                Vector3 axis = parent != null ? Quaternion.Inverse(parent.rotation) * axisWorld : axisWorld;
                int flex = PhHandFit.FlexIndex(name);
                float restBend;
                if (!TryRestBend(flex, bone, child, wrist, byName, palmWorld, out restBend)) flex = -1;
                _joints.Add(new Joint { Bone = bone, Rest = bone.localRotation, AxisInParent = axis, MaxDeg = PhHandFit.CurlDegrees(name), Flex = flex, RestBendDeg = restBend });
            }
            return IsBound;
        }

        /// <summary>Bend of a driven bone in the bind pose, measured the way PhHandPoseSolver measures a tracked hand: in the bending plane of its digit (wrist to the digit's
        /// first bone, and the palm normal), against wrist to bone for the digit's first bone and against the previous bone for the others. False when the wrist bone
        /// is missing or a bone is shorter than 1 mm.</summary>
        private static bool TryRestBend(int flex, Transform bone, Transform child, Transform wrist, Dictionary<string, Transform> byName, Vector3 palmWorld, out float deg)
        {
            deg = 0f;
            if (flex < 0 || wrist == null) return false;
            int joint = flex % 3;
            string digit = PhHandFit.DigitOf(flex / 3);
            Transform first, previous = wrist;
            if (!byName.TryGetValue(PhHandFit.Bone(digit, 1), out first)) return false;
            if (joint > 0 && !byName.TryGetValue(PhHandFit.Bone(digit, joint), out previous)) return false;
            Vector3 e1, e2;
            if (!PhHandPoseSolver.BendFrame(first.position - wrist.position, palmWorld, out e1, out e2)) return false;
            Vector3 reference = bone.position - previous.position, direction = child.position - bone.position;
            if (reference.magnitude < PhHandPoseSolver.MinBoneM || direction.magnitude < PhHandPoseSolver.MinBoneM) return false;
            deg = PhHandPoseSolver.BendDeg(reference, direction, e1, e2);
            return true;
        }

        /// <summary>0 = rest pose (the bind pose baked into the wrapper), 1 = closed hand.</summary>
        public void SetCurl(float curl01)
        {
            _curl = Mathf.Clamp01(curl01);
            for (int i = 0; i < _joints.Count; i++)
            {
                var j = _joints[i];
                if (j.Bone != null) j.Bone.localRotation = Quaternion.AngleAxis(j.MaxDeg * _curl, j.AxisInParent) * j.Rest;
            }
        }

        /// <summary>
        /// Turns every mapped bone to the measured bend (<paramref name="flexDeg15"/> = PhHandPose.FlexDeg, degrees toward the palm): a rotation about the bone's flexion axis
        /// by measured minus the bone's rest bend, limited to <see cref="MinTurnDeg"/>..<see cref="MaxTurnDeg"/>, so a measured bend equal to the rest bend leaves the bone at
        /// rest. A bone without a mapping (no wrist bone found at bind, or a degenerate bone) stays under <see cref="SetCurl"/>. Short arrays and NaN entries are ignored.
        /// <see cref="Curl"/> keeps the last SetCurl value.
        /// </summary>
        public void SetFlexion(float[] flexDeg15)
        {
            if (flexDeg15 == null || flexDeg15.Length < PhHandPose.FlexCount) return;
            for (int i = 0; i < _joints.Count; i++)
            {
                var j = _joints[i];
                if (j.Bone == null || j.Flex < 0) continue;
                float measured = flexDeg15[j.Flex];
                if (float.IsNaN(measured) || float.IsInfinity(measured)) continue;
                float turn = Mathf.Clamp(measured - j.RestBendDeg, MinTurnDeg, MaxTurnDeg);
                j.Bone.localRotation = Quaternion.AngleAxis(turn, j.AxisInParent) * j.Rest;
            }
        }

        /// <summary>Hash of the world pose of the driven bones (0.1 mm / 0.01 deg): constant while the hand is frozen and neither curling nor following a tracked pose.</summary>
        public int PoseHash()
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < _joints.Count; i++)
                {
                    var b = _joints[i].Bone;
                    if (b != null) h = h * 31 + Hash(b.position) * 7 + Hash(b.rotation.eulerAngles * 0.01f);
                }
                return h;
            }
        }

        private static int Hash(Vector3 v)
        {
            unchecked { return (Mathf.RoundToInt(v.x * 10000f) * 73856093) ^ (Mathf.RoundToInt(v.y * 10000f) * 19349663) ^ (Mathf.RoundToInt(v.z * 10000f) * 83492791); }
        }
    }
}
