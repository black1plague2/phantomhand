using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Pure forearm shape maths shared by the arm mesh (VirtualArmRig) and the brush path (BrushRig), so the brush never
    /// depends on the arm's renderers or visibility (A2 keeps stroking on an invisible arm). d = distance from the
    /// wrist reference point toward the elbow, in metres. All offsets are relative to the wrist reference point P,
    /// which sits at the arm's axis height at the wrist.
    /// </summary>
    public static class ArmGeometry
    {
        public const float WristHalfWidth = 0.028f, ElbowHalfWidth = 0.046f;
        public const float WristHalfHeight = 0.021f, ElbowHalfHeight = 0.038f;

        public static float HalfWidth(float d, float len) { float t = Mathf.Clamp01(d / len); return Mathf.Lerp(WristHalfWidth, ElbowHalfWidth, Mathf.SmoothStep(0f, 1f, t) * 0.7f + t * 0.3f); }
        public static float HalfHeight(float d, float len) { float t = Mathf.Clamp01(d / len); return Mathf.Lerp(WristHalfHeight, ElbowHalfHeight, Mathf.SmoothStep(0f, 1f, t) * 0.7f + t * 0.3f); }

        /// <summary>Axis height relative to P: the arm lies on the table, so the bottom stays level.</summary>
        public static float AxisY(float d, float len) { return HalfHeight(d, len) - WristHalfHeight; }

        /// <summary>Height of the dorsal (top) surface relative to P at distance d.</summary>
        public static float TopY(float d, float len) { return AxisY(d, len) + HalfHeight(d, len); }
    }
}
