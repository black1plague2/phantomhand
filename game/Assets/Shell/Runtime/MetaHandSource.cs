using Opus.Sdk;

namespace Opus.Shell
{
#if OPUS_META_HANDS
    using UnityEngine;
    using Oculus.Interaction;
    using Oculus.Interaction.Input;

    /// <summary>
    /// Concrete <see cref="IHandSource"/> backed by the Interaction SDK's raw <c>IHand</c> (the hand fed by
    /// <c>FromOVRHandDataSource</c>, sampled BEFORE any <c>HandFilter</c>/smoothing modifier — gameplay visuals
    /// may use a filtered hand downstream, recorded data must not, per docs/UNITY_PRACTICES.md §4) plus an
    /// <c>IHmd</c> (<c>FromOVRHmdDataSource</c>) for the head. Lives in the Shell (not com.opus.sdk) so the SDK
    /// package carries zero Meta/ISDK assembly dependency.
    ///
    /// Joint mapping (HandJointId -> OpusJoints):
    ///   HandWristRoot -> l_wrist / r_wrist
    ///   HandIndexTip  -> l_index_tip / r_index_tip
    ///   HandThumbTip  -> l_thumb_tip / r_thumb_tip
    ///   HandPalm      -> l_palm / r_palm
    ///
    /// TODO(verify): written from the documented ISDK v74+ IHand/IHmd surface without a live compile check —
    /// confirm exact method names once com.meta.xr.sdk.interaction.ovr 205.0.0 actually resolves in the editor.
    /// </summary>
    public sealed class MetaHandSource : IHandSource, IHandSkeletonSource
    {
        private readonly IHand _leftHand, _rightHand;
        private readonly IHmd _hmd;
        private int _leftVersion, _rightVersion;

        public MetaHandSource(IHand leftHand, IHand rightHand, IHmd hmd)
        {
            _leftHand = leftHand;
            _rightHand = rightHand;
            _hmd = hmd;
            if (_leftHand != null) _leftHand.WhenHandUpdated += () => _leftVersion = _leftHand.CurrentDataVersion;
            if (_rightHand != null) _rightHand.WhenHandUpdated += () => _rightVersion = _rightHand.CurrentDataVersion;
        }

        public bool TryGetJointPose(string joint, out double[] posMeters, out double[] rotQuatXyzw)
        {
            posMeters = null; rotQuatXyzw = null;

            if (joint == OpusJoints.Head)
            {
                if (_hmd == null || !_hmd.TryGetRootPose(out Pose headPose)) return false;
                posMeters = ToArray(headPose.position);
                rotQuatXyzw = ToArray(headPose.rotation);
                return true;
            }

            bool isLeft = joint.StartsWith("l_");
            var hand = isLeft ? _leftHand : _rightHand;
            if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid) return false;

            HandJointId jointId = joint switch
            {
                OpusJoints.LWrist or OpusJoints.RWrist => HandJointId.HandWristRoot,
                OpusJoints.LIndexTip or OpusJoints.RIndexTip => HandJointId.HandIndexTip,
                OpusJoints.LThumbTip or OpusJoints.RThumbTip => HandJointId.HandThumbTip,
                OpusJoints.LPalm or OpusJoints.RPalm => HandJointId.HandPalm,
                _ => HandJointId.Invalid,
            };
            if (jointId == HandJointId.Invalid) return false;
            if (!hand.GetJointPose(jointId, out Pose pose)) return false;

            posMeters = ToArray(pose.position);
            rotQuatXyzw = ToArray(pose.rotation);
            return true;
        }

        public TrackingConfidence GetConfidence(HandSide side)
        {
            var hand = side == HandSide.Left ? _leftHand : _rightHand;
            if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid) return TrackingConfidence.None;
            return hand.IsHighConfidence ? TrackingConfidence.High : TrackingConfidence.Low;
        }

        public bool IsTracked(HandSide side)
        {
            var hand = side == HandSide.Left ? _leftHand : _rightHand;
            return hand != null && hand.IsConnected && hand.IsTrackedDataValid;
        }

        public float GetPinchStrength(HandSide side)
        {
            var hand = side == HandSide.Left ? _leftHand : _rightHand;
            if (hand == null) return 0f;
            return hand.GetFingerPinchStrength(HandFinger.Index);
        }

        public int GetDataVersion(HandSide side) => side == HandSide.Left ? _leftVersion : _rightVersion;

        /// <summary>The 21 points of <see cref="Opus.Sdk.HandSkeleton"/> as the OpenXR hand names them: a finger's base joint is its proximal
        /// joint (the knuckle), the thumb's is its metacarpal joint.</summary>
        private static readonly HandJointId[] SkeletonJoints =
        {
            HandJointId.HandWristRoot,
            HandJointId.HandThumb1, HandJointId.HandThumb2, HandJointId.HandThumb3, HandJointId.HandThumbTip,
            HandJointId.HandIndex1, HandJointId.HandIndex2, HandJointId.HandIndex3, HandJointId.HandIndexTip,
            HandJointId.HandMiddle1, HandJointId.HandMiddle2, HandJointId.HandMiddle3, HandJointId.HandMiddleTip,
            HandJointId.HandRing1, HandJointId.HandRing2, HandJointId.HandRing3, HandJointId.HandRingTip,
            HandJointId.HandPinky1, HandJointId.HandPinky2, HandJointId.HandPinky3, HandJointId.HandPinkyTip,
        };

        public bool TryGetSkeleton(HandSide side, Vector3[] joints)
        {
            var hand = side == HandSide.Left ? _leftHand : _rightHand;
            if (joints == null || joints.Length < Opus.Sdk.HandSkeleton.JointCount || hand == null || !hand.IsConnected || !hand.IsTrackedDataValid) return false;
            for (int i = 0; i < SkeletonJoints.Length; i++)
            {
                if (!hand.GetJointPose(SkeletonJoints[i], out Pose pose)) return false;
                joints[i] = pose.position;
            }
            return true;
        }

        private static double[] ToArray(Vector3 v) => new double[] { v.x, v.y, v.z };
        private static double[] ToArray(Quaternion q) => new double[] { q.x, q.y, q.z, q.w };
    }
#else
    /// <summary>Stub used until com.meta.xr.sdk.interaction.ovr resolves in this editor session (scoped
    /// registry https://npm.developer.oculus.com, see Packages/manifest.json). Once it resolves, the
    /// Shell.Runtime asmdef's versionDefine flips on OPUS_META_HANDS and the real adapter above compiles instead.</summary>
    public sealed class MetaHandSource : IHandSource
    {
        public bool TryGetJointPose(string joint, out double[] posMeters, out double[] rotQuatXyzw)
        { posMeters = null; rotQuatXyzw = null; return false; }
        public TrackingConfidence GetConfidence(HandSide side) => TrackingConfidence.None;
        public bool IsTracked(HandSide side) => false;
        public float GetPinchStrength(HandSide side) => 0f;
        public int GetDataVersion(HandSide side) => 0;
    }
#endif
}
