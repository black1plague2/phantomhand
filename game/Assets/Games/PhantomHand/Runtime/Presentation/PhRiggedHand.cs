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
    /// </summary>
    public sealed class PhRiggedHand : MonoBehaviour
    {
        public const int MinJoints = 12;     // 4 fingers x 3 bones

        private struct Joint { public Transform Bone; public Quaternion Rest; public Vector3 AxisInParent; public float MaxDeg; }

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
            foreach (var name in PhHandFit.DrivenBones())
            {
                Transform bone, child;
                if (!byName.TryGetValue(name, out bone) || !byName.TryGetValue(PhHandFit.ChildBone(name), out child)) continue;
                Vector3 axisWorld = PhHandFit.FlexionAxis(child.position - bone.position, palmWorld);
                if (axisWorld == Vector3.zero) continue;
                Transform parent = bone.parent;
                Vector3 axis = parent != null ? Quaternion.Inverse(parent.rotation) * axisWorld : axisWorld;
                _joints.Add(new Joint { Bone = bone, Rest = bone.localRotation, AxisInParent = axis, MaxDeg = PhHandFit.CurlDegrees(name) });
            }
            return IsBound;
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

        /// <summary>Hash of the world pose of the driven bones (0.1 mm / 0.01 deg): constant while the hand is frozen and not curling.</summary>
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
