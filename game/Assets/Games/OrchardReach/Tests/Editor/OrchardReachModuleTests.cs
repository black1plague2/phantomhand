using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Games.OrchardReach.Tests
{
    public class OrchardReachModuleTests
    {
        private sealed class FakeSession : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex => 0;
        }

        // Always tracked + strongly pinching: drives every trial to grasp on the first Tick after target_shown.
        private sealed class AlwaysGraspingHand : IHandSource
        {
            public bool TryGetJointPose(string joint, out double[] pos, out double[] rot) { pos = new double[] { 0, 0, 0 }; rot = null; return true; }
            public TrackingConfidence GetConfidence(HandSide side) => TrackingConfidence.High;
            public bool IsTracked(HandSide side) => true;
            public float GetPinchStrength(HandSide side) => 1.0f;
            private int _version;
            public int GetDataVersion(HandSide side) => ++_version;
        }

        private sealed class NeverGraspingHand : IHandSource
        {
            public bool TryGetJointPose(string joint, out double[] pos, out double[] rot) { pos = new double[] { 0, 0, 0 }; rot = null; return false; }
            public TrackingConfidence GetConfidence(HandSide side) => TrackingConfidence.None;
            public bool IsTracked(HandSide side) => false;
            public float GetPinchStrength(HandSide side) => 0f;
            public int GetDataVersion(HandSide side) => 0;
        }

        private static JObject Schema()
        {
            var manifestJson = System.IO.File.ReadAllText(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "Games/OrchardReach/manifest.json"));
            return JObject.Parse(manifestJson)["paramSchema"] as JObject;
        }

        [Test]
        public void FullBlock_RunsAllTrialsAndEmitsWellFormedEvents()
        {
            var module = new OrchardReachModule();
            var bound = ParamBinder.Bind(Schema(), JObject.Parse("{'side':'right','trialCount':5,'holdMs':0,'timeLimitMs':15000}".Replace('\'', '"')));
            Assert.IsTrue(bound.IsValid, string.Join(";", bound.Errors));

            var session = new FakeSession();
            module.Configure(bound.Params, session);
            module.SetCalibration(new double[] { 0, 0, 0 }, 0.6);

            var events = new List<TrialEvent>();
            module.OnTrialEvent += events.Add;

            var hand = new AlwaysGraspingHand();
            module.Begin();
            for (int frame = 0; frame < 200 && events.FindAll(e => e.Type == "block_end").Count == 0; frame++)
            {
                session.Clock.Advance(20);
                module.Tick(session.Clock.NowMs, hand);
                // The scene's basket trigger collider confirms placement; simulate it once the trial is holding.
                if (module.CurrentTrialState == TrialState.Holding)
                    module.ConfirmPlacedInBasket(session.Clock.NowMs);
            }

            Assert.AreEqual(5, module.TrialsCompleted, "all 5 trials should have run");
            Assert.AreEqual(1, events.FindAll(e => e.Type == "block_start").Count);
            Assert.AreEqual(1, events.FindAll(e => e.Type == "block_end").Count);
            Assert.AreEqual(5, events.FindAll(e => e.Type == "trial_start").Count);
            Assert.AreEqual(5, events.FindAll(e => e.Type == "target_shown").Count);
            Assert.AreEqual(5, events.FindAll(e => e.Type == "placed").Count);
            Assert.AreEqual(5, module.SuccessCount);

            // t_ms must never go backwards across the whole recorded stream (event ordering contract).
            for (int i = 1; i < events.Count; i++)
                Assert.GreaterOrEqual(events[i].TMs, events[i - 1].TMs);

            // Every event must carry the fields event.schema.json requires.
            foreach (var e in events)
            {
                Assert.IsNotNull(e.Type);
                Assert.GreaterOrEqual(e.TMs, 0);
            }
        }

        [Test]
        public void Timeout_WhenHandNeverMoves_StillAdvancesTrials()
        {
            var module = new OrchardReachModule();
            var bound = ParamBinder.Bind(Schema(), JObject.Parse("{'side':'right','trialCount':2,'timeLimitMs':100}".Replace('\'', '"')));
            var session = new FakeSession();
            module.Configure(bound.Params, session);
            module.SetCalibration(new double[] { 0, 0, 0 }, 0.6);

            var events = new List<TrialEvent>();
            module.OnTrialEvent += events.Add;

            var neverGrasp = new NeverGraspingHand();
            module.Begin();
            for (int frame = 0; frame < 50 && module.TrialsCompleted < 2; frame++)
            {
                session.Clock.Advance(20);
                module.Tick(session.Clock.NowMs, neverGrasp);
            }

            Assert.AreEqual(2, module.TrialsCompleted);
            Assert.AreEqual(2, events.FindAll(e => e.Type == "trial_end" && e.Outcome == "timeout").Count);
        }

        /// <summary>Run15 (Opus, from real Link session c93ae4fa trial 3): the hand can already be at the fruit
        /// the instant the target appears -- MovementOnsetGate legitimately says "no movement yet" (the real
        /// gate is a wrist-displacement threshold; here forced false to reproduce it deterministically), but
        /// GraspProximityGate says "already at the fruit", so contact/grasp fires straight from TargetShown.
        /// Before the fix, movement_onset was never emitted for that trial and analytics (gated on
        /// t_movement_onset) nulled every metric despite a real, successful placement. movement_onset must
        /// appear before contact in the recorded event order regardless of the onset gate's answer.</summary>
        [Test]
        public void HandAlreadyOverTargetAtTargetShown_StillEmitsMovementOnsetBeforeContact()
        {
            var module = new OrchardReachModule();
            var bound = ParamBinder.Bind(Schema(), JObject.Parse("{'side':'right','trialCount':3,'holdMs':0,'timeLimitMs':15000}".Replace('\'', '"')));
            Assert.IsTrue(bound.IsValid, string.Join(";", bound.Errors));

            var session = new FakeSession();
            module.Configure(bound.Params, session);
            module.SetCalibration(new double[] { 0, 0, 0 }, 0.6);
            // Real-hands wiring: onset gate never satisfied (hand hasn't "moved" by the wrist-displacement
            // threshold), proximity gate always satisfied (hand is already sitting at the fruit).
            module.MovementOnsetGate = _ => false;
            module.GraspProximityGate = _ => true;

            var events = new List<TrialEvent>();
            module.OnTrialEvent += events.Add;

            var hand = new AlwaysGraspingHand();
            module.Begin();
            session.Clock.Advance(20);
            module.Tick(session.Clock.NowMs, hand);

            int onsetIdx = events.FindIndex(e => e.Type == "movement_onset");
            int contactIdx = events.FindIndex(e => e.Type == "contact");
            Assert.GreaterOrEqual(onsetIdx, 0, "movement_onset must still be emitted");
            Assert.GreaterOrEqual(contactIdx, 0, "contact must still be emitted");
            Assert.Less(onsetIdx, contactIdx, "movement_onset must precede contact in the event stream");
        }

        /// <summary>Run15 (stage D sorting): a wrong-container placement must never look like success --
        /// no "placed" event (so success feedback never fires for it), trial_end outcome exactly "wrong_target"
        /// (event.schema.json's enum spelling), and the sorting data (container/fruit_color/correct) attached.</summary>
        [Test]
        public void SortingRule_WrongContainer_EndsTrialAsWrongTarget_NoPlacedEvent()
        {
            var module = new OrchardReachModule();
            var bound = ParamBinder.Bind(Schema(), JObject.Parse(
                "{'side':'right','trialCount':3,'holdMs':0,'timeLimitMs':15000,'stage':'D_sorting','sortRule':'red_only','containerCount':2}"
                    .Replace('\'', '"')));
            Assert.IsTrue(bound.IsValid, string.Join(";", bound.Errors));

            var session = new FakeSession();
            module.Configure(bound.Params, session);
            module.SetCalibration(new double[] { 0, 0, 0 }, 0.6);

            var events = new List<TrialEvent>();
            module.OnTrialEvent += events.Add;

            var hand = new AlwaysGraspingHand();
            module.Begin();
            session.Clock.Advance(20);
            module.Tick(session.Clock.NowMs, hand);
            Assert.AreEqual(TrialState.Holding, module.CurrentTrialState);

            // A green apple placed in the red-only basket -- wrong container under "red_only".
            module.ConfirmPlacedInContainer(session.Clock.NowMs, "red_basket", "green", fruitRipe: true, tiltDeg: null);

            Assert.AreEqual(0, events.FindAll(e => e.Type == "placed").Count, "no 'placed' event on a wrong-container drop");
            var end = events.Find(e => e.Type == "trial_end");
            Assert.IsNotNull(end);
            Assert.AreEqual("wrong_target", end.Outcome);
            Assert.AreEqual(0, module.SuccessCount, "wrong-container placement must never count as a success");
        }

        [Test]
        public void SortingRule_CorrectContainer_StillSucceeds()
        {
            var module = new OrchardReachModule();
            var bound = ParamBinder.Bind(Schema(), JObject.Parse(
                "{'side':'right','trialCount':3,'holdMs':0,'timeLimitMs':15000,'stage':'D_sorting','sortRule':'red_only','containerCount':2}"
                    .Replace('\'', '"')));
            var session = new FakeSession();
            module.Configure(bound.Params, session);
            module.SetCalibration(new double[] { 0, 0, 0 }, 0.6);

            var events = new List<TrialEvent>();
            module.OnTrialEvent += events.Add;

            var hand = new AlwaysGraspingHand();
            module.Begin();
            session.Clock.Advance(20);
            module.Tick(session.Clock.NowMs, hand);

            module.ConfirmPlacedInContainer(session.Clock.NowMs, "red_basket", "red", fruitRipe: true, tiltDeg: null);

            Assert.AreEqual(1, events.FindAll(e => e.Type == "placed").Count);
            Assert.AreEqual(1, module.SuccessCount);
            var end = events.Find(e => e.Type == "trial_end");
            Assert.AreEqual("success", end.Outcome);
        }
    }
}
