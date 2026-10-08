using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Games.PhantomHand.Tests
{
    internal static class Ph
    {
        public static ParamSet P(string json = "{}") { return new ParamSet(JObject.Parse(json)); }
        public static PhantomHandParams Params(string json = "{}") { return PhantomHandParams.From(P(json)); }
    }

    public class PhantomHandParamsTests
    {
        [Test]
        public void Defaults_MatchSpecSection6AndD9()
        {
            var p = Ph.Params();
            Assert.AreEqual(15, p.OffsetCm); Assert.AreEqual(1.0, p.StrokeRateHz); Assert.AreEqual(90, p.InductionS);
            Assert.AreEqual(600, p.AsyncDelayMs); Assert.AreEqual("async_first", p.ConditionOrder);
            Assert.IsTrue(p.ThreatEnabled); Assert.IsFalse(p.AgencyEnabled); Assert.AreEqual(0.3, p.EmgThreshold);
            Assert.AreEqual(40, p.TactileLeadMs); Assert.AreEqual(833, p.MotorSoaMs, "D16: a 12 cm/s stroke"); Assert.AreEqual(150, p.StrokeJitterMs);
            Assert.AreEqual(5, p.MotorAFromWristCm); Assert.AreEqual(10, p.MotorSpacingCm); Assert.AreEqual(25, p.ForearmLengthCm);
            Assert.IsTrue(p.HapticsEnabled); Assert.AreEqual(1.0, p.HapticMaxIntensity);
            Assert.IsFalse(p.FollowDuringInduction); Assert.IsFalse(p.DemoMode); Assert.AreEqual("right", p.StimulatedSide);
        }

        [Test]
        public void Section12Defaults_Parsed()
        {
            var p = Ph.Params();
            Assert.IsTrue(p.VoiceoverEnabled); Assert.AreEqual("en", p.VoiceoverLang); Assert.IsTrue(p.DissolveEnabled);
            Assert.IsTrue(p.PassthroughReveal); Assert.AreEqual(0, p.SelfTouchS, "parsed (default 15) but ignored until A3 exists"); Assert.IsFalse(p.BreathEnabled);
            Assert.AreEqual(20, p.BreathReplayLagS); Assert.IsFalse(p.AutonomousCloseEnabled);
        }

        [Test]
        public void OutOfRangeValues_AreClamped()
        {
            var p = Ph.Params("{\"offset_cm\":99,\"stroke_rate_hz\":9,\"induction_s\":5,\"async_delay_ms\":10,\"tactile_lead_ms\":999,\"motor_soa_ms\":1,\"haptic_max_intensity\":3,\"emg_threshold\":-1}");
            Assert.AreEqual(30, p.OffsetCm); Assert.AreEqual(1.5, p.StrokeRateHz); Assert.AreEqual(30, p.InductionS);
            Assert.AreEqual(300, p.AsyncDelayMs); Assert.AreEqual(150, p.TactileLeadMs); Assert.AreEqual(60, p.MotorSoaMs);
            Assert.AreEqual(1.0, p.HapticMaxIntensity); Assert.AreEqual(0, p.EmgThreshold);
        }

        [Test]
        public void DemoMode_CapsTheInductionAt60_AndAShorterInductionStillShortensIt()
        {
            Assert.AreEqual(60, PhantomHandParams.DemoInductionS);
            Assert.AreEqual(60, Ph.Params("{\"demo_mode\":true,\"induction_s\":120}").EffectiveInductionS);
            Assert.AreEqual(60, Ph.Params("{\"demo_mode\":true}").EffectiveInductionS, "the default 90 gives 60");
            Assert.AreEqual(45, Ph.Params("{\"demo_mode\":true,\"induction_s\":45}").EffectiveInductionS);
            Assert.AreEqual(30, Ph.Params("{\"demo_mode\":true,\"induction_s\":30}").EffectiveInductionS);
            Assert.AreEqual(90, Ph.Params().EffectiveInductionS);
            Assert.AreEqual(120, Ph.Params("{\"induction_s\":120}").EffectiveInductionS, "no demo mode, no cap");
        }

        [Test]
        public void UnknownConditionOrder_FallsBackToAsyncFirst_AndSyncFirstParses()
        {
            Assert.AreEqual("async_first", Ph.Params("{\"condition_order\":\"banana\"}").ConditionOrder);
            Assert.IsTrue(Ph.Params("{\"condition_order\":\"sync_first\"}").SyncFirst);
        }

        [Test]
        public void ForearmLength_GrowsToCoverMotorB()
        {
            var p = Ph.Params("{\"forearm_length_cm\":20,\"motor_a_from_wrist_cm\":10,\"motor_spacing_cm\":15}");
            Assert.GreaterOrEqual(p.ForearmLengthCm, 27);
        }

        [Test]
        public void StimulatedSide_ChoosesTheArm_AndTheOtherHandPoints()
        {
            var left = Ph.Params("{\"stimulated_side\":\"left\"}");
            Assert.AreEqual("left", left.StimulatedSide);
            Assert.AreEqual(Opus.Sdk.HandSide.Left, left.Arm); Assert.AreEqual(Opus.Sdk.HandSide.Right, left.Pointer);
            var right = Ph.Params("{\"stimulated_side\":\"right\"}");
            Assert.AreEqual(Opus.Sdk.HandSide.Right, right.Arm); Assert.AreEqual(Opus.Sdk.HandSide.Left, right.Pointer);
            Assert.AreEqual("right", Ph.Params("{\"stimulated_side\":\"both\"}").StimulatedSide, "anything else is the right arm");
        }

        [Test]
        public void SelfTouch_IsParsedButIgnored_UntilA3Exists()
        {
            // A3 is not built: any self_touch_s would insert an empty SelfTouch phase and cut the brush part of the induction
            foreach (var json in new[] { "{}", "{\"self_touch_s\":15}", "{\"induction_s\":30,\"self_touch_s\":30}" })
                Assert.AreEqual(0, Ph.Params(json).SelfTouchS, json);
        }
    }

    public class PhaseStateMachineTests
    {
        [Test]
        public void FullWalk_DefaultOrder_ThreatOnly()
        {
            var m = new PhaseStateMachine(Ph.Params());
            var seen = new List<PhPhase>();
            m.PhaseChanged += c => seen.Add(c.Phase);
            double t = 0; m.Start(t);
            while (!m.IsDone) { t += 1000; m.Tick(t); if (PhaseStateMachine.IsCompletionPhase(m.Phase)) m.Complete(t); }
            var expect = new List<PhPhase> { PhPhase.Calibrate };
            for (int i = 0; i < 2; i++)
                expect.AddRange(new[] { PhPhase.ProbePre, PhPhase.Induction, PhPhase.Threat, PhPhase.ProbePost, PhPhase.Questionnaire });
            expect.AddRange(new[] { PhPhase.Witness, PhPhase.Done });
            CollectionAssert.AreEqual(expect, seen);
        }

        [Test]
        public void ConditionOrder_AsyncFirstAndSyncFirst()
        {
            var a = new PhaseStateMachine(Ph.Params());
            CollectionAssert.AreEqual(new[] { PhCondition.Async, PhCondition.Sync }, a.Order);
            var s = new PhaseStateMachine(Ph.Params("{\"condition_order\":\"sync_first\"}"));
            CollectionAssert.AreEqual(new[] { PhCondition.Sync, PhCondition.Async }, s.Order);
            var seenConds = new List<PhCondition?>();
            s.PhaseChanged += c => { if (c.Phase == PhPhase.Induction) seenConds.Add(c.Condition); };
            double t = 0; s.Start(t);
            while (!s.IsDone) { t += 1000; s.Tick(t); if (PhaseStateMachine.IsCompletionPhase(s.Phase)) s.Complete(t); }
            CollectionAssert.AreEqual(new PhCondition?[] { PhCondition.Sync, PhCondition.Async }, seenConds);
        }

        [Test]
        public void AgencyOn_AddsOneAgencyPhase_InTheLastConditionOnly_BeforeThreat()
        {
            // D12 (was: once per condition): the hand that closes by itself comes in the last condition
            var m = new PhaseStateMachine(Ph.Params("{\"agency_enabled\":true}"));
            var plan = m.PlannedPhases().ToList();
            Assert.AreEqual(1, plan.Count(p => p == PhPhase.Agency));
            int i = plan.IndexOf(PhPhase.Agency);
            Assert.AreEqual(PhPhase.Threat, plan[i + 1]);
            Assert.AreEqual(PhPhase.Induction, plan[i - 1]);
            Assert.Greater(i, plan.IndexOf(PhPhase.Questionnaire), "after the first condition's questionnaire");
            var seen = new List<KeyValuePair<PhPhase, int?>>();
            m.PhaseChanged += c => seen.Add(new KeyValuePair<PhPhase, int?>(c.Phase, c.TrialIndex));
            double t = 0; m.Start(t);
            while (!m.IsDone) { t += 1000; m.Tick(t); if (PhaseStateMachine.IsCompletionPhase(m.Phase)) m.Complete(t); }
            Assert.AreEqual(1, seen.Count(s => s.Key == PhPhase.Agency));
            Assert.AreEqual(1, seen.First(s => s.Key == PhPhase.Agency).Value, "condition index 1");
            Assert.AreEqual(30000, m.DurationMs(PhPhase.Agency));
        }

        [Test]
        public void AgencyOff_NoAgencyPhase_Anywhere()
        {
            Assert.AreEqual(0, new PhaseStateMachine(Ph.Params()).PlannedPhases().Count(p => p == PhPhase.Agency));
            Assert.AreEqual(0, new PhaseStateMachine(Ph.Params("{\"autonomous_close_enabled\":true}")).PlannedPhases().Count(p => p == PhPhase.Agency),
                "the autonomous close alone does not add the phase");
        }

        [Test]
        public void ThreatOff_SkipsThreat()
        {
            var m = new PhaseStateMachine(Ph.Params("{\"threat_enabled\":false}"));
            Assert.AreEqual(0, m.PlannedPhases().Count(p => p == PhPhase.Threat));
        }

        [Test]
        public void Induction_EndsExactlyAtInductionSeconds()
        {
            var m = new PhaseStateMachine(Ph.Params());
            m.Start(0); m.Complete(100); m.Complete(200); // calibrate, probe pre
            Assert.AreEqual(PhPhase.Induction, m.Phase);
            m.Tick(200 + 89999);
            Assert.AreEqual(PhPhase.Induction, m.Phase);
            m.Tick(200 + 90000);
            Assert.AreEqual(PhPhase.Threat, m.Phase);
            Assert.AreEqual(200 + 90000, m.PhaseStartMs, 1e-6);
        }

        [Test]
        public void LateTick_CatchesUp_WithoutDrift()
        {
            var m = new PhaseStateMachine(Ph.Params());
            m.Start(0); m.Complete(0); m.Complete(0);   // induction starts at 0
            m.Tick(90000 + 5000 + 123);                 // past induction + threat
            Assert.AreEqual(PhPhase.ProbePost, m.Phase);
            Assert.AreEqual(95000, m.PhaseStartMs, 1e-6);
        }

        [Test]
        public void Advance_SkipsCurrentPhase_AndMarksCompletionPhaseUnconfirmed()
        {
            var m = new PhaseStateMachine(Ph.Params());
            m.Start(0);
            m.Advance(500);
            Assert.AreEqual(PhPhase.ProbePre, m.Phase);
            Assert.IsFalse(m.LastEndConfirmed);
            m.Complete(600); m.Advance(700);
            Assert.AreEqual(PhPhase.Threat, m.Phase);
            Assert.IsTrue(m.LastEndConfirmed);
        }

        [Test]
        public void Abort_GoesToDone_AndIsRecorded()
        {
            var m = new PhaseStateMachine(Ph.Params());
            m.Start(0); m.Complete(10); m.Abort(20);
            Assert.AreEqual(PhPhase.Done, m.Phase);
            Assert.IsTrue(m.Aborted);
            m.Tick(1e9);
            Assert.AreEqual(PhPhase.Done, m.Phase);
        }

        [Test]
        public void PauseFreezesTimers_ResumeContinues()
        {
            var m = new PhaseStateMachine(Ph.Params());
            m.Start(0); m.Complete(0); m.Complete(0);    // induction at 0
            m.Pause(10000);
            m.Tick(500000);
            Assert.AreEqual(PhPhase.Induction, m.Phase);
            Assert.AreEqual(80000, m.RemainingMs(500000), 1e-6);
            m.Resume(60000 + 10000);                      // paused for 60 s
            m.Tick(70000 + 79999);
            Assert.AreEqual(PhPhase.Induction, m.Phase);
            m.Tick(70000 + 80000);
            Assert.AreEqual(PhPhase.Threat, m.Phase);
        }

        [Test]
        public void CompletionPhase_TimesOutAfter60s_Unconfirmed()
        {
            var m = new PhaseStateMachine(Ph.Params());
            PhaseChange last = default(PhaseChange);
            m.PhaseChanged += c => last = c;
            m.Start(0);
            m.Tick(59999);
            Assert.AreEqual(PhPhase.Calibrate, m.Phase);
            m.Tick(60000);
            Assert.AreEqual(PhPhase.ProbePre, m.Phase);
            Assert.IsFalse(last.PreviousConfirmed);
            Assert.AreEqual(PhPhase.Calibrate, last.Previous);
        }

        [Test]
        public void Complete_IgnoredOnTimerPhase()
        {
            var m = new PhaseStateMachine(Ph.Params());
            m.Start(0); m.Complete(0); m.Complete(0);
            Assert.AreEqual(PhPhase.Induction, m.Phase);
            Assert.IsFalse(m.Complete(10));
            Assert.AreEqual(PhPhase.Induction, m.Phase);
        }

        [Test]
        public void PhaseChanged_CarriesConditionAndTrialIndex()
        {
            var m = new PhaseStateMachine(Ph.Params());
            var log = new List<PhaseChange>();
            m.PhaseChanged += log.Add;
            m.Start(0); m.Complete(0); m.Complete(0);
            Assert.IsNull(log[0].TrialIndex); Assert.IsNull(log[0].Condition);       // calibrate
            Assert.AreEqual(0, log[2].TrialIndex); Assert.AreEqual(PhCondition.Async, log[2].Condition);
        }

        [Test]
        public void DemoMode_AQuestionnaireFollowsEveryCondition_AsInEveryMode()
        {
            // D18 (was: one questionnaire at the end of the demo, which left the witness with one empty side)
            foreach (var json in new[] { "{\"demo_mode\":true}", "{}" })
            {
                var m = new PhaseStateMachine(Ph.Params(json));
                var plan = m.PlannedPhases().ToList();
                Assert.AreEqual(2, plan.Count(p => p == PhPhase.Questionnaire), json);
                int first = plan.IndexOf(PhPhase.Questionnaire), last = plan.LastIndexOf(PhPhase.Questionnaire);
                Assert.AreEqual(PhPhase.ProbePost, plan[first - 1]); Assert.AreEqual(PhPhase.ProbePost, plan[last - 1]);
                Assert.AreEqual(PhPhase.ProbePre, plan[first + 1], "the second condition starts right after the first one's questionnaire");
            }
            Assert.AreEqual(60000, new PhaseStateMachine(Ph.Params("{\"demo_mode\":true}")).InductionPhaseMs);
        }

        [Test]
        public void Additions_Off_NoSelfTouchDissolveReveal_On_InsertsThem()
        {
            var off = new PhaseStateMachine(Ph.Params()).PlannedPhases();
            Assert.IsFalse(off.Contains(PhPhase.SelfTouch)); Assert.IsFalse(off.Contains(PhPhase.Dissolve)); Assert.IsFalse(off.Contains(PhPhase.Reveal));
            var m = new PhaseStateMachine(Ph.Params(), additions: true);
            var on = m.PlannedPhases();
            Assert.AreEqual(0, on.Count(p => p == PhPhase.SelfTouch), "A3 is not built: no SelfTouch phase from the params");
            Assert.AreEqual(1, on.Count(p => p == PhPhase.Dissolve));
            Assert.AreEqual(1, on.Count(p => p == PhPhase.Reveal));
            var l = on.ToList();
            Assert.Less(l.IndexOf(PhPhase.Reveal), l.IndexOf(PhPhase.Witness));
            Assert.Less(l.LastIndexOf(PhPhase.Questionnaire), l.IndexOf(PhPhase.Dissolve));
            Assert.Less(l.IndexOf(PhPhase.Dissolve), l.IndexOf(PhPhase.Reveal));
            Assert.AreEqual(90000, m.InductionPhaseMs, "the whole induction is brush");
            Assert.AreEqual(0, m.SelfTouchMs);
        }

        [Test]
        public void SelfTouchBranch_StillWorks_WhenTheFieldIsSetDirectly()
        {
            var p = Ph.Params(); p.SelfTouchS = 15;   // what A3 will do
            var m = new PhaseStateMachine(p, additions: true);
            Assert.AreEqual(2, m.PlannedPhases().Count(x => x == PhPhase.SelfTouch));
            Assert.AreEqual(75000, m.InductionPhaseMs);   // 90 s minus the 15 s self-touch tail
            Assert.AreEqual(15000, m.SelfTouchMs);
        }

        [Test]
        public void SetConditionOrder_AllowedBeforeInduction_ThrowsAfter()
        {
            var m = new PhaseStateMachine(Ph.Params());
            m.Start(0);
            m.SetConditionOrder(PhCondition.Sync);
            CollectionAssert.AreEqual(new[] { PhCondition.Sync, PhCondition.Async }, m.Order);
            m.Complete(0); m.Complete(0);
            Assert.Throws<System.InvalidOperationException>(() => m.SetConditionOrder(PhCondition.Async));
        }
    }
}
