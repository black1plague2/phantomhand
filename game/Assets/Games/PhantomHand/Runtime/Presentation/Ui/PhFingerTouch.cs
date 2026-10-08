using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Presses the buttons of a world-space panel with a tracked fingertip. The panels are wired for the Interaction SDK's poke
    /// (PhUiKit.AttachPoke), but the scene holds no poke interactor and shows no hands, so on the headset nothing ever pressed them
    /// (first headset night, 8 Oct 2026: no questionnaire could be answered). This needs only what the hand source already gives:
    /// the position of an index fingertip. A button is pressed when the fingertip touches it (reaches its surface after hovering
    /// it for a moment, so a hand sweeping along the row presses nothing), or when it stays over it within a few centimetres for
    /// <see cref="DwellMs"/> (tracked depth is the least reliable axis; holding still always works). One press per visit: the
    /// finger has to leave the button before it can press it again. No Unity events system, no raycasts.
    /// </summary>
    public sealed class PhFingerTouch
    {
        /// <summary>How far in front of the panel a fingertip counts as over a button, and how far behind (canvas units = mm).</summary>
        public const float HoverMm = 45f, BehindMm = 35f;
        /// <summary>Closer to the surface than this is a touch.</summary>
        public const float TouchMm = 6f;
        /// <summary>The button's rectangle is taken this much larger on every side: a fingertip is wider than a point.</summary>
        public const float MarginMm = 5f;
        public const double DwellMs = 600, TouchAfterMs = 120;

        /// <summary>The button the fingertip is over, or null.</summary>
        public Button Hovered { get; private set; }
        /// <summary>How far the dwell on <see cref="Hovered"/> has got, 0 to 1 (1 once it was pressed).</summary>
        public float Progress01 { get; private set; }

        private double _sinceMs;
        private bool _pressed;

        public void Reset() { Hovered = null; Progress01 = 0f; _pressed = false; }

        /// <summary>One frame. <paramref name="tips"/> are fingertip positions in world space (null entries = not tracked).
        /// Returns the button that was pressed in this frame (its onClick has been invoked), or null.</summary>
        public Button Tick(double nowMs, IList<Button> buttons, params Vector3?[] tips)
        {
            Button over = null; float depth = 0f, best = float.MaxValue;
            for (int b = 0; buttons != null && b < buttons.Count; b++)
            {
                var button = buttons[b];
                if (button == null || !button.isActiveAndEnabled || !button.interactable) continue;
                var rt = (RectTransform)button.transform;
                Rect r = rt.rect;
                for (int t = 0; tips != null && t < tips.Length; t++)
                {
                    if (!tips[t].HasValue) continue;
                    Vector3 l = rt.InverseTransformPoint(tips[t].Value);   // mm in the button's own frame; z < 0 is in front of the panel
                    if (l.x < r.xMin - MarginMm || l.x > r.xMax + MarginMm || l.y < r.yMin - MarginMm || l.y > r.yMax + MarginMm) continue;
                    if (l.z < -HoverMm || l.z > BehindMm) continue;
                    float d = Mathf.Abs(l.z);
                    if (d < best) { best = d; over = button; depth = l.z; }
                }
            }

            if (over == null) { Reset(); return null; }
            if (over != Hovered) { Hovered = over; _sinceMs = nowMs; _pressed = false; }
            double held = nowMs - _sinceMs;
            if (_pressed) { Progress01 = 1f; return null; }
            Progress01 = Mathf.Clamp01((float)(held / DwellMs));
            bool touching = depth >= -TouchMm && held >= TouchAfterMs;
            if (!touching && held < DwellMs) return null;
            _pressed = true; Progress01 = 1f;
            over.onClick.Invoke();
            return over;
        }
    }
}
