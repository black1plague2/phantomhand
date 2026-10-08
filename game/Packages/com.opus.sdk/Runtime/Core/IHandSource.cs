namespace Opus.Sdk
{
    public enum HandSide { Left, Right }

    /// <summary>Maps directly to the `conf` column values the brief specifies: 0 = not tracked, 0.5 = low
    /// confidence, 1 = high confidence (from OVRHand.IsTracked / IsDataHighConfidence / HandConfidence on the
    /// concrete Meta adapter; a synthetic/replay source reports whatever the fixture recorded).</summary>
    public enum TrackingConfidence { None = 0, Low = 1, High = 2 }

    public static class TrackingConfidenceExtensions
    {
        public static double ToConfValue(this TrackingConfidence c) => c switch
        {
            TrackingConfidence.High => 1.0,
            TrackingConfidence.Low => 0.5,
            _ => 0.0,
        };
    }

    /// <summary>
    /// The SDK's only window onto hand tracking. Concrete implementations: a Meta XR adapter (OVRSkeleton /
    /// Interaction SDK IHand, in the Shell composition root — NOT in this package, so com.opus.sdk carries no
    /// Meta assembly dependency) in-headset/simulator, and <c>SyntheticHandDriver</c> (U5) replaying recorded
    /// kinematics for hands-free test sessions. <see cref="KinematicsSampler"/> and every game module talk to
    /// hands only through this interface, so swapping the source never touches recording or game logic.
    /// Positions/rotations are in the same calibration space as kinematics-chunk.schema.json (meters, +y up,
    /// +z forward, quaternion xyzw) and joint names are <see cref="OpusJoints"/> constants.
    /// </summary>
    public interface IHandSource
    {
        /// <summary>Raw pose for one schema joint (see <see cref="OpusJoints.All"/>), sampled every frame with no
        /// gameplay smoothing/filtering applied — filtering is fine for visuals downstream, never for what's recorded.</summary>
        bool TryGetJointPose(string joint, out double[] posMeters, out double[] rotQuatXyzw);

        TrackingConfidence GetConfidence(HandSide side);

        bool IsTracked(HandSide side);

        /// <summary>0..1 pinch strength (thumb-to-index), from the Meta Interaction SDK's pinch signal or the
        /// equivalent recorded/synthesized value on other sources.</summary>
        float GetPinchStrength(HandSide side);

        /// <summary>Monotonically increasing counter that ticks once per genuinely new tracking sample
        /// (ISDK's <c>IHand.CurrentDataVersion</c> on the Meta adapter). Used by <see cref="TrackingRateMeter"/>
        /// to measure the real hand-tracking rate at runtime — OpenXR gives no reliable way to confirm the
        /// configured frequency, so this is measured instead of trusted (docs/UNITY_PRACTICES.md §2).
        /// It also lets callers avoid re-recording a frame when the underlying data hasn't actually updated.</summary>
        int GetDataVersion(HandSide side);
    }
}
