using System.Collections.Generic;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// A tracked fingertip presses the questionnaire's buttons (PhFingerTouch): by touching one, or by staying over it. The panel
    /// stands where the scene puts it (in front of the seat, tilted), so the maths is exercised in a rotated frame.
    /// Category "UnityEngine": a real uGUI panel is built.
    /// </summary>
    [Category("UnityEngine")]
    public class FingerTouchTests
    {
        private GameObject _go;
        private PhQuestionnairePanel _panel;
        private Questionnaire _q;
        private PhFingerTouch _touch;
        private List<Button> _buttons;
        private readonly Dictionary<Button, int> _clicks = new Dictionary<Button, int>();

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("QuestionnairePanel", typeof(RectTransform));
            _go.transform.SetPositionAndRotation(new Vector3(0f, 1.06f, 0.47f), Quaternion.Euler(18f, 0f, 0f));
            _panel = _go.AddComponent<PhQuestionnairePanel>();
            _panel.Build(null, false);
            _q = new Questionnaire(new[] { Questionnaire.Q1, Questionnaire.Q2, Questionnaire.Q3 });
            _panel.Show(_q, v => _q.Answer(v) != null, () => _q.Back(), "en", 0);
            _touch = new PhFingerTouch();
            _buttons = new List<Button>(_panel.Buttons);
            _clicks.Clear();
            foreach (var b in _buttons) { var k = b; _clicks[k] = 0; k.onClick.AddListener(() => _clicks[k]++); }
        }

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        private Button Scale(int value) { return _panel.Buttons[value + 3]; }

        /// <summary>A point over the middle of a button, mm in front of the panel (negative = pushed through it).</summary>
        private static Vector3 Over(Button b, float mmInFront, float offsetXmm = 0f)
        {
            var rt = (RectTransform)b.transform;
            return rt.TransformPoint(new Vector3(rt.rect.center.x + offsetXmm, rt.rect.center.y, -mmInFront));
        }

        [Test]
        public void StayingOverAButton_PressesItAfterTheDwell_Once()
        {
            var b = Scale(2);
            Assert.IsNull(_touch.Tick(0, _buttons, Over(b, 25f)));
            Assert.AreSame(b, _touch.Hovered);
            Assert.IsNull(_touch.Tick(PhFingerTouch.DwellMs - 1, _buttons, Over(b, 25f)), "not before the dwell is over");
            Assert.That(_touch.Progress01, Is.InRange(0.9f, 1f));
            Assert.AreSame(b, _touch.Tick(PhFingerTouch.DwellMs, _buttons, Over(b, 25f)));
            Assert.AreEqual(1, _clicks[b]);
            Assert.IsTrue(_panel.HasPending, "the panel took the press");
            for (double t = 700; t < 4000; t += 50) Assert.IsNull(_touch.Tick(t, _buttons, Over(b, 25f)), "one press per visit");
            Assert.AreEqual(1, _clicks[b]);

            _panel.Tick(PhFingerTouch.DwellMs + PhQuestionnairePanel.AutoAdvanceMs);
            Assert.AreEqual(2, _q.ValueOf("q1"), "the answer reached the questionnaire");
        }

        [Test]
        public void TouchingAButton_PressesItAtOnce_ButNotASweepAlongTheRow()
        {
            var b = Scale(-1);
            Assert.IsNull(_touch.Tick(0, _buttons, Over(b, 3f)), "the finger has to be over it for a moment first");
            Assert.IsNull(_touch.Tick(PhFingerTouch.TouchAfterMs - 1, _buttons, Over(b, 3f)));
            Assert.AreSame(b, _touch.Tick(PhFingerTouch.TouchAfterMs, _buttons, Over(b, 3f)));
            Assert.AreEqual(1, _clicks[b]);

            // a hand that slides along the surface, 60 ms on each button, presses nothing
            _touch.Reset();
            double t = 1000;
            for (int v = -3; v <= 3; v++, t += 60) Assert.IsNull(_touch.Tick(t, _buttons, Over(Scale(v), 2f)), "sweep over " + v);
            foreach (var kv in _clicks) if (kv.Key != b) Assert.AreEqual(0, kv.Value);
        }

        [Test]
        public void LeavingAndComingBack_PressesAgain()
        {
            var b = Scale(0);
            _touch.Tick(0, _buttons, Over(b, 20f)); _touch.Tick(600, _buttons, Over(b, 20f));
            Assert.AreEqual(1, _clicks[b]);
            Assert.IsNull(_touch.Tick(700, _buttons, Over(b, 120f)), "finger pulled back: over nothing");
            Assert.IsNull(_touch.Hovered);
            _touch.Tick(800, _buttons, Over(b, 20f));
            Assert.AreSame(b, _touch.Tick(1400, _buttons, Over(b, 20f)));
            Assert.AreEqual(2, _clicks[b]);
        }

        [Test]
        public void TooFarAway_OrBesideTheButtons_IsNothing()
        {
            var b = Scale(3);
            for (double t = 0; t < 2000; t += 100)
            {
                Assert.IsNull(_touch.Tick(t, _buttons, Over(b, PhFingerTouch.HoverMm + 10f)), "too far in front");
                Assert.IsNull(_touch.Hovered);
            }
            for (double t = 2000; t < 4000; t += 100)
                Assert.IsNull(_touch.Tick(t, _buttons, Over(b, 10f, PhQuestionnairePanel.ButtonMm)), "beside the last button");
            for (double t = 4000; t < 6000; t += 100)
                Assert.IsNull(_touch.Tick(t, _buttons, Over(b, -(PhFingerTouch.BehindMm + 10f))), "far behind the panel");
            Assert.IsNull(_touch.Tick(6000, _buttons, (Vector3?)null, (Vector3?)null), "no hand tracked");
            foreach (var kv in _clicks) Assert.AreEqual(0, kv.Value);
        }

        [Test]
        public void TheFingerNearestThePanel_Wins_AndAMarginAroundAButtonCounts()
        {
            Button far = Scale(-3), near = Scale(3);
            _touch.Tick(0, _buttons, Over(far, 30f), Over(near, 8f));
            Assert.AreSame(near, _touch.Hovered);
            // a fingertip 3 mm outside the button's edge still counts as over it
            _touch.Reset();
            _touch.Tick(0, _buttons, Over(near, 8f, PhQuestionnairePanel.ButtonMm / 2f + 3f));
            Assert.AreSame(near, _touch.Hovered);
        }
    }
}
