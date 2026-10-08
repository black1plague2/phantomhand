using System;

namespace Opus.Sdk
{
    /// <summary>
    /// Drives one frame of sampling: reads every joint in <see cref="OpusJoints.All"/> from an
    /// <see cref="IHandSource"/> and feeds it into a <see cref="KinematicsRecorder"/>, and raises
    /// tracking_lost/tracking_regained callbacks on confidence transitions. Call <see cref="SampleFrame"/>
    /// once per input-update tick (OVRManager's input-update path in the Meta adapter, or once per
    /// replayed frame for SyntheticHandDriver) — not from a coroutine, so no frames are skipped or
    /// downsampled relative to the device's native hand-tracking rate.
    /// </summary>
    public sealed class KinematicsSampler
    {
        private readonly KinematicsRecorder _recorder;
        private readonly IHandSource _hands;
        private bool? _leftWasTracked;
        private bool? _rightWasTracked;

        /// <param name="onTrackingChanged">(side, nowTracked) — fire tracking_lost/tracking_regained TrialEvents from here.</param>
        public event Action<HandSide, bool> OnTrackingChanged;

        public KinematicsSampler(KinematicsRecorder recorder, IHandSource hands)
        {
            _recorder = recorder;
            _hands = hands;
        }

        public void SampleFrame(double nowMs)
        {
            foreach (var joint in OpusJoints.All)
            {
                double conf = ConfidenceForJoint(joint);
                if (_hands.TryGetJointPose(joint, out var pos, out var rot))
                    _recorder.Sample(nowMs, joint, pos, rot, conf);
                else
                    _recorder.Sample(nowMs, joint, new double[] { 0, 0, 0 }, null, 0.0);
            }

            CheckTrackingTransition(HandSide.Left, ref _leftWasTracked);
            CheckTrackingTransition(HandSide.Right, ref _rightWasTracked);
        }

        private double ConfidenceForJoint(string joint)
        {
            HandSide? side = joint.StartsWith("l_") ? HandSide.Left : joint.StartsWith("r_") ? HandSide.Right : (HandSide?)null;
            if (side == null) return 1.0; // head: HMD pose is always "tracked" while the app runs
            return _hands.GetConfidence(side.Value).ToConfValue();
        }

        private void CheckTrackingTransition(HandSide side, ref bool? wasTracked)
        {
            bool nowTracked = _hands.IsTracked(side);
            if (wasTracked.HasValue && wasTracked.Value != nowTracked)
                OnTrackingChanged?.Invoke(side, nowTracked);
            wasTracked = nowTracked;
        }
    }
}
