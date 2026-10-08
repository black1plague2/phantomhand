using Opus.Sdk;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// Which arm is which. The stimulated arm (<see cref="PhantomHandParams.Arm"/>) wears the sleeve and is replaced by the virtual
    /// one; the other hand points in the probes and answers the questions. The scene is laid out for a right arm (the real arm rests
    /// at +x, the virtual one toward the body's midline); a left arm is that layout mirrored in x. Plain C#.
    /// </summary>
    public static class PhArm
    {
        public static HandSide Other(HandSide s) { return s == HandSide.Left ? HandSide.Right : HandSide.Left; }

        public static string Wrist(HandSide s) { return s == HandSide.Left ? OpusJoints.LWrist : OpusJoints.RWrist; }
        public static string Palm(HandSide s) { return s == HandSide.Left ? OpusJoints.LPalm : OpusJoints.RPalm; }
        public static string IndexTip(HandSide s) { return s == HandSide.Left ? OpusJoints.LIndexTip : OpusJoints.RIndexTip; }
        public static string ThumbTip(HandSide s) { return s == HandSide.Left ? OpusJoints.LThumbTip : OpusJoints.RThumbTip; }

        /// <summary>An x of the right-arm layout in the layout of this arm: unchanged for the right arm, mirrored for the left.</summary>
        public static double X(HandSide arm, double xOfRightLayout) { return arm == HandSide.Left ? -xOfRightLayout : xOfRightLayout; }
    }
}
