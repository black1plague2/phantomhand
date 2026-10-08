using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests.PlayMode
{
    /// <summary>
    /// The finale on the real presenter (WRITTEN, NOT RUN: needs the Unity engine). Reuses the <see cref="Fx"/> fixture of
    /// PhantomHandPresentationTests: one frame at a time on a manual clock, haptic Pump, module Tick, presenter, physics.
    /// </summary>
    public class PhantomHandFinaleTests
    {
        private const string Json = "{\"condition_order\":\"sync_first\",\"induction_s\":30}";

        /// <summary>Fx configures the module with the additions off; the finale phases exist only with them on, so configure again before the run starts.</summary>
        private static Fx NewFx(string json = Json)
        {
            var f = new Fx(json);
            f.M.AdditionsEnabled = true;
            f.M.Configure(new ParamSet(JObject.Parse(json)), f.S);
            return f;
        }

        /// <summary>Operator-style skip (phase_next) through both conditions, one frame per phase so the presenter sees each one.</summary>
        private static void SkipTo(Fx f, PhPhase phase)
        {
            f.M.Begin(); f.Step();
            int guard = 0;
            while (f.M.CurrentPhase != phase && guard++ < 40) { Assert.IsTrue(f.M.AdvancePhase()); f.Step(); }
            Assert.AreEqual(phase, f.M.CurrentPhase);
        }

        // -- dissolve ------------------------------------------------------------------------------------------

        [Test]
        public void Dissolve_ArmIsFullyVisibleFor4s_ThenFadesOver3s_ThenIsGone()
        {
            using (var f = NewFx())
            {
                SkipTo(f, PhPhase.Dissolve);
                double t0 = f.M.Machine.PhaseStartMs;
                Assert.IsTrue(f.P.arm.Visible); Assert.IsTrue(f.P.arm.Frozen); Assert.AreEqual(0f, f.P.arm.Curl, 1e-4f);

                f.StepUntil(() => f.Now >= t0 + 3900, 6000, "3.9 s into the dissolve");
                Assert.AreEqual(1f, f.P.arm.Alpha, 1e-4f, "still fully visible at 3.9 s");

                f.StepUntil(() => f.Now >= t0 + 5500, 4000, "5.5 s into the dissolve");
                Assert.Greater(f.P.arm.Alpha, 0.01f); Assert.Less(f.P.arm.Alpha, 0.99f);
                Assert.AreEqual(0.5f, f.P.arm.Alpha, 0.03f, "halfway through the 3 s fade");

                f.StepUntil(() => f.Now >= t0 + 7100, 4000, "7.1 s into the dissolve");
                Assert.AreEqual(0f, f.P.arm.Alpha, 1e-4f, "gone after 7 s");
            }
        }

        [Test]
        public void Dissolve_ArmIsPlacedLikeTheInduction_AtTheOffset()
        {
            using (var f = NewFx())
            {
                SkipTo(f, PhPhase.Dissolve);
                var d = f.P.arm.WristWorld - f.Hands.Wrist;
                Assert.AreEqual(-0.15f, d.x, 0.005f, "15 cm to the left");
                Assert.AreEqual(0f, d.y, 0.001f); Assert.AreEqual(0f, d.z, 0.001f);
                Assert.AreEqual(1f, f.P.arm.Alpha, 1e-4f);
            }
        }

        [Test]
        public void Dissolve_BrushKeepsStrokingTheEmptySpace_AndEveryStrokeIsRecordedWithoutATrial()
        {
            using (var f = NewFx())
            {
                SkipTo(f, PhPhase.Dissolve);
                int planned = f.M.CurrentStrokes.Count;
                Assert.Greater(planned, 0);
                Assert.IsTrue(f.P.brush.Visible); Assert.IsTrue(f.P.brush.Active);
                bool brushAlways = true; var tips = new System.Collections.Generic.HashSet<int>();
                f.OnFrame = x =>
                {
                    if (x.M.CurrentPhase != PhPhase.Dissolve) return;
                    if (!x.P.brush.Visible || !x.P.brush.Active) brushAlways = false;
                    if (x.P.arm.Alpha <= 0f) tips.Add(Mathf.RoundToInt(x.P.brush.LastTip.z * 1000f));   // moves along the (empty) forearm
                };
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Dissolve, 25000, "dissolve end");
                Assert.IsTrue(brushAlways, "the brush is shown and active for the whole phase");
                Assert.Greater(tips.Count, 20, "the brush kept moving after the arm was gone");
                Assert.AreEqual(1, f.Events("dissolve_start").Count);
                Assert.AreEqual(planned, f.Ev.Count(e => e.Type == "stroke" && e.Trial == null), "one trial-less stroke event per planned stroke");
                Assert.IsFalse(f.P.brush.Visible, "the brush is put away when the phase ends");
            }
        }

        // -- agency (A5): with no level source the hand stays open, and still closes by itself twice -------------------------

        [Test]
        public void Agency_WithoutAnySource_HandStaysOpenUntilWatch_ThenClosesByItselfTwice()
        {
            // FakeHands has a constant fingertip-to-palm distance and Fx has no Node B, so the level is null all through
            using (var f = NewFx("{\"condition_order\":\"sync_first\",\"induction_s\":30,\"agency_enabled\":true,\"autonomous_close_enabled\":true}"))
            {
                SkipTo(f, PhPhase.Agency);
                double t0 = f.M.Machine.PhaseStartMs;
                float maxBeforeWatch = 0f;
                f.OnFrame = x => { if (x.M.CurrentPhase == PhPhase.Agency && x.Now < t0 + 21500) maxBeforeWatch = Mathf.Max(maxBeforeWatch, x.P.arm.Curl); };
                f.StepUntil(() => f.Now >= t0 + 21500, 30000, "21.5 s into the agency phase");
                Assert.AreEqual(0f, maxBeforeWatch, 1e-4f, "no source: the hand never moves in Rest, Squeeze or Driven");
                f.StepUntil(() => f.Now >= t0 + 22600, 3000, "22.6 s");
                Assert.AreEqual(1f, f.P.arm.Curl, 0.02f, "closed by itself since 22.0 s");
                f.StepUntil(() => f.Now >= t0 + 24000, 3000, "24 s");
                Assert.AreEqual(0f, f.P.arm.Curl, 0.02f, "open again 1.5 s after it started");
                f.StepUntil(() => f.Now >= t0 + 26300, 3000, "26.3 s");
                Assert.AreEqual(1f, f.P.arm.Curl, 0.02f, "the second one, from 25.5 s");
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Agency, 6000, "agency end");
                var ev = f.Events("autonomous_close");
                Assert.AreEqual(2, ev.Count);
                Assert.AreEqual(0.0, ev[0]["emg_level"].Value<double>(), 1e-9);
                Assert.AreEqual(0f, f.P.arm.Curl, 1e-4f, "the stone falls on an open hand");
            }
        }

        // -- reveal (fallback: the arm fades in and slides onto the real hand) ----------------------------------------------

        [Test]
        public void Reveal_ArmFadesInOver1s_SlidesOntoTheRealHandOver3s_ThenFollowsIt()
        {
            using (var f = NewFx())
            {
                SkipTo(f, PhPhase.Reveal);
                double t0 = f.M.Machine.PhaseStartMs;
                Assert.IsTrue(f.P.arm.Visible); Assert.IsFalse(f.P.arm.Frozen); Assert.AreEqual(0f, f.P.arm.Curl, 1e-4f);
                Assert.AreEqual(1, f.Events("passthrough_on").Count);
                Assert.IsTrue(f.Events("passthrough_on")[0]["fallback"].Value<bool>());

                f.StepUntil(() => f.Now >= t0 + 500, 2000, "0.5 s into the reveal");
                Assert.AreEqual(0.5f, f.P.arm.Alpha, 0.03f, "half faded in after 0.5 s");
                f.StepUntil(() => f.Now >= t0 + 1200, 2000, "1.2 s into the reveal");
                Assert.AreEqual(1f, f.P.arm.Alpha, 1e-4f);

                f.StepUntil(() => f.Now >= t0 + 1500, 2000, "1.5 s into the reveal");
                Assert.AreEqual(-0.075f, (f.P.arm.WristWorld - f.Hands.Wrist).x, 0.01f, "half of the 15 cm left");
                f.StepUntil(() => f.Now >= t0 + 3200, 4000, "3.2 s into the reveal");
                Assert.AreEqual(0f, (f.P.arm.WristWorld - f.Hands.Wrist).magnitude, 0.003f, "on the real wrist");

                f.Hands.Wrist += new Vector3(0.05f, 0f, 0.03f);          // the hand moves: the arm keeps following it with offset 0
                f.Step();
                Assert.AreEqual(0f, (f.P.arm.WristWorld - f.Hands.Wrist).magnitude, 0.003f);
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Reveal, 12000, "reveal end");
                Assert.AreEqual(1, f.Events("passthrough_on").Count, "still once");
            }
        }
    }
}
