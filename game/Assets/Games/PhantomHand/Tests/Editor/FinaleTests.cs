using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// The finale (03-SPEC section 12 A2) at module level: the Dissolve phase (brush and strokes run for 18 s, same scheduler,
    /// same safety gates, trial null) and the Reveal phase (passthrough_on, fallback version). Pure: a manual clock, no engine.
    /// </summary>
    public class FinaleTests
    {
        private sealed class FakeSession : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex { get { return 0; } }
        }

        private sealed class FakeTransport : IHapticTransport
        {
            public readonly List<string> Sent = new List<string>();
            public bool HasDevice { get; set; } = true;
            public event Action<string> OnMessage { add { } remove { } }
            public event Action OnDeviceKnown { add { } remove { } }
            public void Start() { }
            public void Stop() { }
            public void Send(string json) { Sent.Add(json); }
            public void Dispose() { }
        }

        /// <summary>A module on a manual clock with the additions on (dissolve and reveal are part of the plan).</summary>
        private sealed class Run
        {
            public readonly FakeSession S = new FakeSession();
            public readonly PhantomHandModule M = new PhantomHandModule();
            public readonly List<TrialEvent> Ev = new List<TrialEvent>();

            public Run(string json = "{}", bool additions = true)
            {
                M.AdditionsEnabled = additions;
                M.OnTrialEvent += Ev.Add;
                M.Configure(Ph.P(json), S);
            }

            public double Now { get { return S.Clock.NowMs; } }
            public void Adv(double ms) { S.Clock.Advance(ms); M.Tick(Now); }

            /// <summary>Begins the run and skips phases the way the operator's phase_next does, until <paramref name="phase"/> is current.</summary>
            public void SkipTo(PhPhase phase)
            {
                M.Begin();
                int guard = 0;
                while (M.CurrentPhase != phase && guard++ < 40) Assert.IsTrue(M.AdvancePhase(), "stuck in " + M.CurrentPhase);
                Assert.AreEqual(phase, M.CurrentPhase);
            }

            public List<TrialEvent> Of(string type) { return Ev.Where(e => e.Type == type).ToList(); }
            public static JObject Data(TrialEvent e) { return JObject.FromObject(e.Data); }
            public PhCondition Last { get { return M.Machine.Order[M.Machine.Order.Count - 1]; } }
        }

        // ---- dissolve_start -------------------------------------------------------------------------------------------

        [TestCase("async_first", "sync")]
        [TestCase("sync_first", "async")]
        public void Dissolve_EmitsDissolveStartOnce_NamingTheLastCondition(string order, string last)
        {
            var r = new Run("{\"condition_order\":\"" + order + "\"}");
            r.SkipTo(PhPhase.Dissolve);
            var ev = r.Of("dissolve_start");
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(last, Run.Data(ev[0])["condition"].Value<string>());
            Assert.IsNull(ev[0].Trial, "a finale event has no condition index");
            int i = r.Ev.IndexOf(ev[0]);
            Assert.AreEqual("phase_start", r.Ev[i - 1].Type, "it follows the phase_start of the dissolve");
            Assert.AreEqual("dissolve", Run.Data(r.Ev[i - 1])["phase"].Value<string>());

            r.Adv(PhaseStateMachine.DissolveMs + 1);                   // on into the reveal
            r.Adv(PhaseStateMachine.RevealMs + 1);
            Assert.AreEqual(1, r.Of("dissolve_start").Count, "once per run");
        }

        // ---- the strokes ----------------------------------------------------------------------------------------------

        [TestCase("async_first")]
        [TestCase("sync_first")]
        public void Dissolve_PlansStrokes_ForTheLastCondition_InsideTheWindow(string order)
        {
            var r = new Run("{\"condition_order\":\"" + order + "\"}");
            r.SkipTo(PhPhase.Dissolve);
            double start = r.M.Machine.PhaseStartMs;
            var plan = r.M.CurrentStrokes;
            Assert.Greater(plan.Count, 0);
            var expect = new StrokeScheduler(r.M.Params, r.M.Seed).Plan(r.Last, start, PhaseStateMachine.DissolveMs);
            Assert.AreEqual(expect.Count, plan.Count, "the same scheduler, the same seed, the last condition's timing");
            for (int i = 0; i < plan.Count; i++)
            {
                Assert.AreEqual(expect[i].StartMs, plan[i].StartMs, 1e-9);
                Assert.AreEqual(expect[i].CueMotor0Ms, plan[i].CueMotor0Ms, 1e-9);
                Assert.AreEqual(expect[i].CueMotor1Ms, plan[i].CueMotor1Ms, 1e-9);
            }
            Assert.AreEqual(start + StrokeScheduler.LeadInMs, plan[0].StartMs, 1e-9);
            Assert.Less(plan.Max(s => Math.Max(s.EndMs, s.LastCueMs)), start + PhaseStateMachine.DissolveMs, "the last stroke and its cues end inside the 18 s");
            if (r.Last == PhCondition.Sync) Assert.AreEqual(plan[0].PassAMs, plan[0].CueMotor0Ms, 1e-9, "SYNC cue = pass");
            else Assert.GreaterOrEqual(plan[0].SlotACueMs - plan[0].PassAMs, 500.0, "ASYNC cue = pass + delay");
        }

        [Test]
        public void Dissolve_StrokesFollowTheSameSafetyGates()
        {
            foreach (var order in new[] { "async_first", "sync_first" })
            {
                var r = new Run("{\"condition_order\":\"" + order + "\"}");
                r.SkipTo(PhPhase.Dissolve);
                foreach (int m in new[] { 0, 1 })
                {
                    var t = r.M.CurrentStrokes.Select(s => m == 0 ? s.CueMotor0Ms : s.CueMotor1Ms).OrderBy(x => x).ToList();
                    for (int i = 1; i < t.Count; i++) Assert.GreaterOrEqual(t[i] - t[i - 1], HapticClient.StrokeMinGapPerMotorMs - 1e-6, order + " motor " + m);
                }
                var all = r.M.CurrentStrokes.SelectMany(s => new[] { s.CueMotor0Ms, s.CueMotor1Ms }).OrderBy(x => x).ToList();
                for (int i = HapticClient.StrokeMaxPerSecond; i < all.Count; i++)
                    Assert.GreaterOrEqual(all[i] - all[i - HapticClient.StrokeMaxPerSecond], 1000 - 1e-6, order + ": too many sends in one second");
            }
        }

        [TestCase("async_first", true)]
        [TestCase("sync_first", false)]
        public void StrokeSubmittedInDissolve_IsAccepted_AndItsEventHasNoTrial(string order, bool lastIsSync)
        {
            var r = new Run("{\"condition_order\":\"" + order + "\"}");
            r.SkipTo(PhPhase.Dissolve);
            Assert.AreEqual(lastIsSync, r.Last == PhCondition.Sync);
            Assert.IsNull(r.M.CurrentCondition, "no current condition in the dissolve");
            Assert.AreEqual(r.Last, r.M.StrokeCondition);
            var s = r.M.CurrentStrokes[1];
            Assert.IsTrue(r.M.SubmitStroke(new StrokeRecord { Index = 1, PassAMs = s.PassAMs, PassBMs = s.PassBMs, CueASendMs = s.CueMotor0Ms - 40, CueBSendMs = s.CueMotor1Ms - 40, Swapped = s.Swapped }));
            var e = r.Ev.Last();
            Assert.AreEqual("stroke", e.Type);
            Assert.IsNull(e.Trial);
            var d = Run.Data(e);
            Assert.AreEqual(1, d["index"].Value<int>());
            if (lastIsSync) Assert.AreEqual(0.0, d["timing_err_ms"].Value<double>(), 1e-6, "SYNC timing error is computed as in the induction");
            else Assert.AreEqual(JTokenType.Null, d["timing_err_ms"].Type, "ASYNC has no timing error");
        }

        [Test]
        public void StrokeAfterTheDissolve_IsRefused()
        {
            var r = new Run();
            r.SkipTo(PhPhase.Dissolve);
            var s = r.M.CurrentStrokes[0];
            r.Adv(PhaseStateMachine.DissolveMs + 1);
            Assert.AreEqual(PhPhase.Reveal, r.M.CurrentPhase);
            Assert.IsFalse(r.M.SubmitStroke(new StrokeRecord { Index = 0, PassAMs = s.PassAMs, PassBMs = s.PassBMs }));
            Assert.IsNull(r.M.StrokeCondition);
        }

        // ---- the driver over the whole dissolve -----------------------------------------------------------------------

        [TestCase("async_first", "SYNC")]
        [TestCase("sync_first", "ASYNC")]
        public void Driver_RunsTheWholeDissolve_RecordsEveryStroke_AndShowsTheLastCondition(string order, string oled)
        {
            var r = new Run("{\"condition_order\":\"" + order + "\"}");
            var t = new FakeTransport();
            var h = new HapticClient(t);
            var d = new StrokeDriver(r.M, h, null);
            r.SkipTo(PhPhase.Dissolve);
            d.Begin(r.Now);                                              // what the presenter does on the phase change
            var plan = r.M.CurrentStrokes;
            var passed = new HashSet<string>();
            while (r.M.CurrentPhase == PhPhase.Dissolve)
            {
                r.S.Clock.Advance(10);
                h.Pump(r.Now);
                r.M.Tick(r.Now);
                if (r.M.CurrentPhase != PhPhase.Dissolve) break;
                foreach (var s in plan)
                {
                    if (s.PassAMs <= r.Now && passed.Add(s.Index + "a")) d.OnPass(s.Index, 0, s.PassAMs);
                    if (s.PassBMs <= r.Now && passed.Add(s.Index + "b")) d.OnPass(s.Index, 1, s.PassBMs);
                }
                d.Tick(r.Now);
            }
            d.End(r.Now);                                                // what the presenter does when the phase is left

            var strokes = r.Of("stroke");
            Assert.AreEqual(plan.Count, strokes.Count, "one stroke event per planned stroke");
            Assert.IsTrue(strokes.All(e => e.Trial == null), "all of them without a trial");
            var cues = t.Sent.Select(JObject.Parse).Where(j => (string)j["cue"] == "stroke").ToList();
            Assert.AreEqual(plan.Count * 2, cues.Count, "both motors for every stroke");
            var texts = t.Sent.Select(JObject.Parse).Where(j => (string)j["type"] == "display").Select(j => (string)j["text"]).ToList();
            CollectionAssert.AreEqual(new[] { oled, "IDLE" }, texts, "the sleeve display names the condition whose timing runs, then IDLE");
        }

        // ---- nothing changes when it is off ---------------------------------------------------------------------------

        private static List<string> PhaseStarts(Run r) { return r.Of("phase_start").Select(e => Run.Data(e)["phase"].Value<string>()).ToList(); }

        private static readonly string[] PlainRun =
        {
            "calibrate", "probe_pre", "induction", "threat", "probe_post", "questionnaire",
            "probe_pre", "induction", "threat", "probe_post", "questionnaire", "witness",
        };

        [Test]
        public void DissolveDisabled_NoDissolve_NoStrokes_NoEvent()
        {
            var r = new Run("{\"dissolve_enabled\":false,\"passthrough_reveal\":false}");
            r.M.Begin();
            while (r.M.CurrentPhase != PhPhase.Witness) Assert.IsTrue(r.M.AdvancePhase());
            CollectionAssert.AreEqual(PlainRun, PhaseStarts(r));
            Assert.AreEqual(0, r.Of("dissolve_start").Count);
            Assert.IsNull(r.M.StrokeCondition);
        }

        [Test]
        public void AdditionsOff_NoDissolveNoReveal_EvenWithTheParamsOn()
        {
            var r = new Run("{\"dissolve_enabled\":true,\"passthrough_reveal\":true}", additions: false);
            r.M.Begin();
            while (r.M.CurrentPhase != PhPhase.Witness) Assert.IsTrue(r.M.AdvancePhase());
            CollectionAssert.AreEqual(PlainRun, PhaseStarts(r));
            Assert.AreEqual(0, r.Of("dissolve_start").Count);
            Assert.AreEqual(0, r.Of("passthrough_on").Count);
            Assert.IsFalse(r.M.SubmitReveal(true), "no reveal phase, no event");
        }

        // ---- reveal (fallback) ----------------------------------------------------------------------------------------

        [Test]
        public void Reveal_PassthroughOn_FallbackTrue_ExactlyOnce_AndNoTrial()
        {
            var r = new Run();
            r.SkipTo(PhPhase.Reveal);
            Assert.AreEqual(0, r.Of("passthrough_on").Count, "nothing until the presenter reports it");
            Assert.IsTrue(r.M.SubmitReveal(true));
            Assert.IsFalse(r.M.SubmitReveal(true), "once per run");
            Assert.IsFalse(r.M.SubmitReveal(false));
            var ev = r.Of("passthrough_on");
            Assert.AreEqual(1, ev.Count);
            Assert.IsTrue(Run.Data(ev[0])["fallback"].Value<bool>());
            Assert.IsNull(ev[0].Trial);
            r.Adv(PhaseStateMachine.RevealMs + 1);
            Assert.AreEqual(PhPhase.Witness, r.M.CurrentPhase);
            Assert.AreEqual(1, r.Of("passthrough_on").Count);
        }

        [Test]
        public void Reveal_RecordsTheFlagAsGiven()
        {
            var r = new Run();
            r.SkipTo(PhPhase.Reveal);
            Assert.IsTrue(r.M.SubmitReveal(false));
            Assert.IsFalse(Run.Data(r.Of("passthrough_on")[0])["fallback"].Value<bool>());
        }

        [Test]
        public void Reveal_RefusedOutsideTheRevealPhase()
        {
            var r = new Run();
            Assert.IsFalse(r.M.SubmitReveal(true), "before the run");
            r.SkipTo(PhPhase.Dissolve);
            Assert.IsFalse(r.M.SubmitReveal(true), "in the dissolve");
            r.Adv(PhaseStateMachine.DissolveMs + 1);
            Assert.AreEqual(PhPhase.Reveal, r.M.CurrentPhase);
            r.Adv(PhaseStateMachine.RevealMs + 1);
            Assert.AreEqual(PhPhase.Witness, r.M.CurrentPhase);
            Assert.IsFalse(r.M.SubmitReveal(true), "in the witness");
            Assert.AreEqual(0, r.Of("passthrough_on").Count);
        }

        [Test]
        public void Reveal_IsReportedOncePerRun_NotOncePerModule()
        {
            var r = new Run();
            r.SkipTo(PhPhase.Reveal);
            Assert.IsTrue(r.M.SubmitReveal(true));
            r.M.End();
            r.M.Configure(Ph.P("{}"), r.S);                              // the next person: same module, fresh run
            r.SkipTo(PhPhase.Reveal);
            Assert.IsTrue(r.M.SubmitReveal(true));
            Assert.AreEqual(2, r.Of("passthrough_on").Count);
        }

        [Test]
        public void RevealCurve_AlphaFadesInOver1s_OffsetSlidesToZeroOver3s()
        {
            const double off = 15;
            Assert.AreEqual(0f, ArmThreatPresenter.RevealAlpha(0), 1e-6);
            Assert.AreEqual(0.5f, ArmThreatPresenter.RevealAlpha(0.5), 1e-6);
            Assert.AreEqual(1f, ArmThreatPresenter.RevealAlpha(1.5), 1e-6);
            Assert.AreEqual(1f, ArmThreatPresenter.RevealAlpha(3), 1e-6);
            Assert.AreEqual(1f, ArmThreatPresenter.RevealAlpha(9), 1e-6);

            Assert.AreEqual(off, ArmThreatPresenter.RevealOffsetCm(0, off), 1e-9, "starts at offset_cm");
            Assert.AreEqual(off / 2, ArmThreatPresenter.RevealOffsetCm(1.5, off), 1e-9, "smoothstep is halfway at the middle");
            Assert.AreEqual(0.0, ArmThreatPresenter.RevealOffsetCm(3, off), 1e-9, "on the real hand after 3 s");
            Assert.AreEqual(0.0, ArmThreatPresenter.RevealOffsetCm(9, off), 1e-9, "and it stays there");
            Assert.AreEqual(22.5, ArmThreatPresenter.RevealOffsetCm(0, 22.5), 1e-9, "the offset is the param, not a constant");

            double prev = off;
            for (double t = 0; t <= 10; t += 0.05) { double o = ArmThreatPresenter.RevealOffsetCm(t, off); Assert.LessOrEqual(o, prev + 1e-9); Assert.GreaterOrEqual(o, 0.0); prev = o; }
            Assert.Less(ArmThreatPresenter.RevealOffsetCm(0.3, off), off); Assert.Greater(ArmThreatPresenter.RevealOffsetCm(0.3, off), off * 0.9, "eases out of the start");
        }

        [Test]
        public void PhaseWord_Reveal_InBothLanguages()
        {
            Assert.AreEqual("Your hand was here all along", PhStrings.PhaseWord(PhPhase.Reveal, "en"));
            Assert.AreEqual("आपका हाथ शुरू से यहीं था", PhStrings.PhaseWord(PhPhase.Reveal, "hi"));
        }

        [Test]
        public void AdditionsOn_RunIs_BothConditions_ThenDissolve_Reveal_Witness()
        {
            var r = new Run();
            r.M.Begin();
            while (r.M.CurrentPhase != PhPhase.Witness) Assert.IsTrue(r.M.AdvancePhase());
            var expect = PlainRun.Take(11).Concat(new[] { "dissolve", "reveal", "witness" });
            CollectionAssert.AreEqual(expect, PhaseStarts(r));
            foreach (var p in r.Of("phase_start").Where(e => new[] { "dissolve", "reveal", "witness" }.Contains(Run.Data(e)["phase"].Value<string>())))
            {
                Assert.IsNull(p.Trial, "finale phases have no trial");
                Assert.AreEqual(JTokenType.Null, Run.Data(p)["condition"].Type);
            }
        }

        [TestCase("async_first", "async", "sync")]
        [TestCase("sync_first", "sync", "async")]
        public void WitnessSummaryEvent_CarriesTheConditionOrder_OnTheContractScale(string order, string first, string second)
        {
            var r = new Run("{\"condition_order\":\"" + order + "\"}");
            r.M.Begin();
            while (r.M.CurrentPhase != PhPhase.Witness) Assert.IsTrue(r.M.AdvancePhase());
            var d = Run.Data(r.Of("witness_summary").Single());
            CollectionAssert.AreEqual(new[] { first, second }, ((JArray)d["condition_order"]).Select(t => t.Value<string>()).ToArray());
            Assert.AreEqual(JTokenType.Object, d["sync"].Type); Assert.AreEqual(JTokenType.Object, d["async"].Type);
            Assert.IsNull(d["sync"]["q4"]);
        }

        // ---- the arm's fade (pure) and the words ----------------------------------------------------------------------

        [Test]
        public void DissolveAlpha_Holds4s_ThenFades1To0Over3s()
        {
            Assert.AreEqual(1f, ArmThreatPresenter.DissolveAlpha(0), 1e-6);
            Assert.AreEqual(1f, ArmThreatPresenter.DissolveAlpha(3.9), 1e-6);
            Assert.AreEqual(1f, ArmThreatPresenter.DissolveAlpha(4.0), 1e-6);
            Assert.AreEqual(2f / 3f, ArmThreatPresenter.DissolveAlpha(5.0), 1e-6);
            Assert.AreEqual(0.5f, ArmThreatPresenter.DissolveAlpha(5.5), 1e-6);
            Assert.AreEqual(0f, ArmThreatPresenter.DissolveAlpha(7.0), 1e-6);
            Assert.AreEqual(0f, ArmThreatPresenter.DissolveAlpha(7.1), 1e-6);
            Assert.AreEqual(0f, ArmThreatPresenter.DissolveAlpha(18.0), 1e-6);
            float prev = 1f;
            for (double t = 0; t <= 18; t += 0.05) { float a = ArmThreatPresenter.DissolveAlpha(t); Assert.LessOrEqual(a, prev + 1e-6f); prev = a; }
            Assert.AreEqual(4.0, ArmThreatPresenter.DissolveHoldS); Assert.AreEqual(3.0, ArmThreatPresenter.DissolveFadeS);
        }

        [Test]
        public void PhaseWord_Dissolve_InBothLanguages()
        {
            Assert.AreEqual("The touch is still here", PhStrings.PhaseWord(PhPhase.Dissolve, "en"));
            Assert.AreEqual("स्पर्श अब भी यहीं है", PhStrings.PhaseWord(PhPhase.Dissolve, "hi"));
        }
    }
}
