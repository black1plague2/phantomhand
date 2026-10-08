using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand;
using Opus.Sdk;

namespace Opus.Shell.Tests
{
    /// <summary>
    /// PH U6 item 1, the parts that are logic. demo_mode itself (induction 45 s, one questionnaire at the end) was built in U2 and is covered by
    /// PhaseAndParamsTests and ScriptedParticipantTests; this adds what U6 states and nothing yet measured: how long a demo run is
    /// (the "~2.5 min"), that the questionnaire really comes once and last, that a second run from a fresh module (what the next-person scene
    /// reload builds) starts from nothing and repeats the first, and that demo_mode shortens the timeline without touching the stroke or safety
    /// parameters. The run is the same scripted participant on a manual clock as ScriptedParticipantTests.AutoParticipant_PlaysDemoRun_...
    /// What stays MANUAL: the reload and gesture timing (&lt; 10 s) on the headset, a human-paced run.
    /// </summary>
    public class PhantomHandDemoModeTests
    {
        private sealed class Ctx : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex => 0;
        }

        private sealed class RunLog
        {
            public PhantomHandModule Module;
            public readonly List<KeyValuePair<PhPhase, double>> Phases = new List<KeyValuePair<PhPhase, double>>(); // phase, start in session ms
            public readonly List<TrialEvent> Events = new List<TrialEvent>();
            public double TotalMs;
            public IEnumerable<PhPhase> Order { get { return Phases.Select(p => p.Key); } }
        }

        /// <summary>One whole run with the scripted participant (20 ms steps) from a brand-new module, like a freshly loaded scene.</summary>
        private static RunLog Play(string paramsJson, int seed = 7)
        {
            var ctx = new Ctx();
            var m = new PhantomHandModule();
            m.SetSeed(seed);
            m.Configure(new ParamSet(JObject.Parse(paramsJson)), ctx);
            var log = new RunLog { Module = m };
            m.OnTrialEvent += log.Events.Add;
            var hands = new ScriptedHands();
            var auto = new PhantomAutoParticipant(hands) { SubmitCalibrationItself = true, CalibrationTarget = () => new[] { 0.18, 0.771, 0.40 } };
            m.Begin();
            log.Phases.Add(new KeyValuePair<PhPhase, double>(m.CurrentPhase, 0));
            for (int i = 0; i < 400000 && !m.Machine.IsDone; i++)
            {
                ctx.Clock.Advance(20);
                double now = ctx.Clock.NowMs;
                auto.Tick(now, m);
                if (m.CurrentPhase == PhPhase.ProbePre || m.CurrentPhase == PhPhase.ProbePost)
                {
                    double[] tip, wrist, index, rot;
                    hands.TryGetJointPose(OpusJoints.LIndexTip, out tip, out rot);
                    hands.TryGetJointPose(OpusJoints.RWrist, out wrist, out rot);
                    hands.TryGetJointPose(OpusJoints.RIndexTip, out index, out rot);
                    m.FeedProbe(now, true, tip, wrist, index[0]);
                }
                m.Tick(now);
                if (log.Phases[log.Phases.Count - 1].Key != m.CurrentPhase) log.Phases.Add(new KeyValuePair<PhPhase, double>(m.CurrentPhase, now));
            }
            log.TotalMs = ctx.Clock.NowMs;
            return log;
        }

        private static double TimedBudgetS(string paramsJson)
        {
            var machine = new PhaseStateMachine(PhantomHandParams.From(new ParamSet(JObject.Parse(paramsJson))));
            return machine.PlannedPhases().Where(PhaseStateMachine.IsTimerPhase).Sum(p => machine.DurationMs(p)) / 1000.0;
        }

        [Test]
        public void TimerPhases_DemoIs130Seconds_FullRunIs220()
        {
            Assert.AreEqual(2 * (45 + 5) + 30, TimedBudgetS("{\"demo_mode\":true}"), 1e-9, "2 x (induction 45 + threat 5) + witness 30");
            Assert.AreEqual(2 * (90 + 5) + 30, TimedBudgetS("{}"), 1e-9, "2 x (induction 90 + threat 5) + witness 30");
            Assert.AreEqual(130.0, TimedBudgetS("{\"demo_mode\":true,\"induction_s\":180}"), 1e-9, "demo_mode wins over a long induction_s");
        }

        [Test]
        public void ScriptedDemoRun_TakesAboutTwoAndAHalfMinutes()
        {
            var run = Play("{\"demo_mode\":true}");
            Assert.IsTrue(run.Module.Machine.IsDone, "the run reached Done");
            double s = run.TotalMs / 1000.0;
            Assert.GreaterOrEqual(s, 130.0, "cannot be shorter than its timers");
            Assert.LessOrEqual(s, 165.0, "a scripted participant needs about 140 s (timers 130 s + about 10 s of calibrate, probes and questions); got " + s + " s");
        }

        [Test]
        public void ScriptedFullRun_IsAboutFourMinutes_AndDemoIsClearlyShorter()
        {
            var full = Play("{}");
            var demo = Play("{\"demo_mode\":true}");
            double fullS = full.TotalMs / 1000.0, demoS = demo.TotalMs / 1000.0;
            Assert.IsTrue(full.Module.Machine.IsDone);
            Assert.GreaterOrEqual(fullS, 215.0); Assert.LessOrEqual(fullS, 250.0, "the PRD's four-minute run; got " + fullS + " s");
            Assert.Less(demoS, 0.7 * fullS, "demo " + demoS + " s vs full " + fullS + " s");
        }

        [Test]
        public void Demo_AsksOneQuestionnaire_AtTheEnd_ForTheLastConditionOnly()
        {
            var run = Play("{\"demo_mode\":true}");
            var expected = new[] { PhPhase.Calibrate, PhPhase.ProbePre, PhPhase.Induction, PhPhase.Threat, PhPhase.ProbePost,
                                   PhPhase.ProbePre, PhPhase.Induction, PhPhase.Threat, PhPhase.ProbePost, PhPhase.Questionnaire,
                                   PhPhase.Witness, PhPhase.Done };
            CollectionAssert.AreEqual(expected, run.Order);
            Assert.AreEqual(3, run.Events.Count(e => e.Type == "questionnaire_item"), "one set of three items");
            Assert.AreEqual(1, run.Events.Count(e => e.Type == "witness_summary"));
            Assert.IsFalse(run.Module.Results[0].Ownership.HasValue, "the first condition was never asked");
            Assert.IsTrue(run.Module.Results[1].Ownership.HasValue, "the single set of answers describes the last condition");
            Assert.IsTrue(run.Module.Results[0].DriftChangeCm.HasValue && run.Module.Results[1].DriftChangeCm.HasValue, "both probes ran in both conditions");

            var full = Play("{}");
            Assert.AreEqual(2, full.Order.Count(p => p == PhPhase.Questionnaire), "a normal run asks after each condition");
        }

        [Test]
        public void TwoRunsInARow_FromFreshModules_StartFromNothingAndRepeat()
        {
            var first = Play("{\"demo_mode\":true}");
            var second = Play("{\"demo_mode\":true}");
            Assert.AreNotSame(first.Module, second.Module);
            Assert.AreNotSame(first.Module.Results[1], second.Module.Results[1], "no result object is carried over");
            Assert.AreEqual(PhPhase.Calibrate, second.Phases[0].Key, "the next person starts at Calibrate");
            CollectionAssert.AreEqual(first.Phases.Select(p => p.Key), second.Phases.Select(p => p.Key));
            CollectionAssert.AreEqual(first.Phases.Select(p => p.Value), second.Phases.Select(p => p.Value), "same timeline to the millisecond");
            CollectionAssert.AreEqual(first.Events.Select(e => e.Type), second.Events.Select(e => e.Type), "same events in the same order");
            Assert.AreEqual(first.Module.Results[0].DriftChangeCm.Value, second.Module.Results[0].DriftChangeCm.Value, 1e-9);
            Assert.AreEqual(first.Module.Results[1].Ownership.Value, second.Module.Results[1].Ownership.Value, 1e-9);
            Assert.AreEqual(first.TotalMs, second.TotalMs, 1e-9);
        }

        [Test]
        public void Demo_ShortensTheTimeline_ButNotTheStrokeOrSafetyParameters()
        {
            var normal = PhantomHandParams.From(new ParamSet(new JObject()));
            var demo = PhantomHandParams.From(new ParamSet(new JObject { ["demo_mode"] = true }));
            Assert.AreEqual(90, normal.EffectiveInductionS, 1e-9);
            Assert.AreEqual(45, demo.EffectiveInductionS, 1e-9);
            Assert.AreEqual(normal.StrokeRateHz, demo.StrokeRateHz, 1e-9);
            Assert.AreEqual(normal.StrokeJitterMs, demo.StrokeJitterMs, 1e-9);
            Assert.AreEqual(normal.MotorSoaMs, demo.MotorSoaMs, 1e-9);
            Assert.AreEqual(normal.AsyncDelayMs, demo.AsyncDelayMs, 1e-9);
            Assert.AreEqual(normal.TactileLeadMs, demo.TactileLeadMs, 1e-9);
            Assert.AreEqual(normal.HapticMaxIntensity, demo.HapticMaxIntensity, 1e-9);
            Assert.AreEqual(normal.HapticsEnabled, demo.HapticsEnabled);
            Assert.AreEqual(normal.ThreatEnabled, demo.ThreatEnabled);
            Assert.AreEqual(normal.ConditionOrder, demo.ConditionOrder);
        }
    }
}
