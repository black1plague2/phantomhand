using Opus.Sdk;

namespace Opus.Games.OrchardReach
{
    /// <summary>
    /// Game-specific grasp thresholds on top of the SDK's raw <see cref="IHandSource"/> signal (pinch strength
    /// from the Meta Interaction SDK's HandGrab/pinch pose). Tuned per graspType param; kept out of the SDK
    /// because threshold tuning is Orchard Reach's business, not the platform's (ARCHITECTURE.md §2).
    /// </summary>
    public sealed class GraspDetector
    {
        public const float PinchThreshold = 0.7f;
        public const float WholeHandThreshold = 0.5f; // Interaction SDK doesn't expose a separate power-grasp
                                                        // signal we can rely on cross-version; approximate whole-hand
                                                        // grasp as a lower pinch-strength bar (thumb+fingers curled)
                                                        // until a dedicated HandGrab pose asset is wired in the scene.

        public static bool IsPinching(IHandSource hands, HandSide side) => hands.GetPinchStrength(side) >= PinchThreshold;
        public static bool IsWholeHandGrasping(IHandSource hands, HandSide side) => hands.GetPinchStrength(side) >= WholeHandThreshold;
    }
}
