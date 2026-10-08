using UnityEngine;

namespace Opus.Sdk
{
    /// <summary>
    /// The 21 points of one hand that a hand source can give beyond <see cref="IHandSource"/>'s wrist, palm and two fingertips:
    /// the wrist, and for each of thumb, index, middle, ring and little finger its three joints and its tip, from the hand outward.
    /// Positions only (metres, the same space as <see cref="IHandSource"/>): everything a consumer needs (how far each joint is
    /// bent, which way the hand faces) follows from positions, and does not depend on any runtime's joint-orientation convention.
    /// </summary>
    public static class HandSkeleton
    {
        public const int JointCount = 21;
        public const int Wrist = 0;
        /// <summary>First point of each finger; a finger is four consecutive points: base joint, middle joint, end joint, tip.
        /// Thumb: carpometacarpal, metacarpophalangeal, interphalangeal, tip. Others: knuckle (MCP), PIP, DIP, tip.</summary>
        public const int Thumb = 1, Index = 5, Middle = 9, Ring = 13, Little = 17;
        public const int FingerCount = 5, PointsPerFinger = 4;

        /// <summary>The index of point <paramref name="k"/> (0 = base joint .. 3 = tip) of finger <paramref name="finger"/> (0 = thumb .. 4 = little).</summary>
        public static int Point(int finger, int k) { return 1 + finger * PointsPerFinger + k; }
    }

    /// <summary>
    /// Optional second face of a hand source: the whole hand. A source that cannot give it (the scripted hands of the editor
    /// tests, a replay of recorded kinematics) simply does not implement it, and consumers keep to <see cref="IHandSource"/>.
    /// </summary>
    public interface IHandSkeletonSource
    {
        /// <summary>Fills <paramref name="joints"/> (length at least <see cref="HandSkeleton.JointCount"/>) in the order of
        /// <see cref="HandSkeleton"/>. False when the hand is not tracked or a joint is missing; the array is then undefined.</summary>
        bool TryGetSkeleton(HandSide side, Vector3[] joints);
    }
}
