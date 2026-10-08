using UnityEngine;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// Puts the wearer at the scene's seat. The room is laid out around one eye point (PhantomAnchors.seatedEyePose: the table in
    /// front, the panels at reading distance), but a headset reports the head wherever its own origin happens to be: on the first
    /// headset night the wearer started beside or above the table and reached for the system's recentre again and again, which
    /// with a floor-level origin never corrects the height. <see cref="Align"/> moves the camera rig, not the room: the head keeps
    /// its pitch and roll, its flat forward becomes the scene's +z and its position the eye point, in height too. What the real
    /// table then lacks or exceeds in height is taken up at the calibration (PhantomHandUiPresenter moves the rig to the arm).
    /// </summary>
    public static class PhSeat
    {
        /// <summary>A head that moved this far, or turned this much, in tracking space within one frame did not move: the headset's
        /// origin did (the system's recentre).</summary>
        public const float JumpM = 0.25f, JumpDeg = 25f;

        /// <summary>Moves <paramref name="rig"/> (an ancestor of <paramref name="head"/>) so that the head is at <paramref name="eye"/>
        /// and looks along +z on the floor plane. False when either is missing.</summary>
        public static bool Align(Transform rig, Transform head, Vector3 eye)
        {
            if (rig == null || head == null) return false;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f) rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(fwd.normalized, Vector3.forward, Vector3.up));
            rig.position += eye - head.position;
            return true;
        }

        /// <summary>Whether the head's pose in tracking space changed more between two frames than a head can.</summary>
        public static bool Jumped(Vector3 localBefore, float yawBefore, Vector3 localNow, float yawNow)
        {
            return (localNow - localBefore).sqrMagnitude > JumpM * JumpM || Mathf.Abs(Mathf.DeltaAngle(yawNow, yawBefore)) > JumpDeg;
        }
    }
}
