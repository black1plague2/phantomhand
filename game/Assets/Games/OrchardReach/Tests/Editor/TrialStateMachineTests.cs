using NUnit.Framework;
using Opus.Games.OrchardReach;

namespace Opus.Games.OrchardReach.Tests
{
    public class TrialStateMachineTests
    {
        [Test]
        public void HappyPath_ShowGraspHoldPlace_EndsInSuccess()
        {
            var sm = new TrialStateMachine(timeLimitMs: 15000, holdMs: 300);
            var t0 = sm.ShowTarget(0);
            Assert.AreEqual("target_shown", t0.EventType);
            Assert.AreEqual(TrialState.TargetShown, sm.State);

            var onset = sm.OnMovementOnset(500);
            Assert.IsTrue(onset.HasValue);
            Assert.AreEqual(TrialState.MovementOnset, sm.State);

            var grasp = sm.OnGrasp(1200);
            Assert.IsTrue(grasp.HasValue);
            Assert.AreEqual(TrialState.Grasped, sm.State);

            // Before hold requirement is met, ticking should not end the trial.
            Assert.IsNull(sm.Tick(1300));
            Assert.AreEqual(TrialState.Grasped, sm.State);

            // After holdMs elapses, ticking transitions internally to Holding (no event yet).
            Assert.IsNull(sm.Tick(1200 + 300));
            Assert.AreEqual(TrialState.Holding, sm.State);

            var placed = sm.Place(1600);
            Assert.AreEqual("placed", placed.EventType);
            Assert.AreEqual(TrialOutcome.Success, placed.Outcome);
            Assert.AreEqual(TrialState.TrialEnded, sm.State);
        }

        [Test]
        public void Timeout_WhenNoGraspBeforeTimeLimit()
        {
            var sm = new TrialStateMachine(timeLimitMs: 1000, holdMs: 300);
            sm.ShowTarget(0);
            Assert.IsNull(sm.Tick(500));
            var timeout = sm.Tick(1001);
            Assert.IsTrue(timeout.HasValue);
            Assert.AreEqual("trial_end", timeout.Value.EventType);
            Assert.AreEqual(TrialOutcome.Timeout, timeout.Value.Outcome);
            Assert.AreEqual(TrialState.TrialEnded, sm.State);
        }

        [Test]
        public void ReleaseBeforeHoldComplete_CountsAsDropped()
        {
            var sm = new TrialStateMachine(timeLimitMs: 15000, holdMs: 500);
            sm.ShowTarget(0);
            sm.OnGrasp(100);
            var release = sm.OnRelease(200, overBasket: true); // held only 100ms < 500ms required
            Assert.IsTrue(release.HasValue);
            Assert.AreEqual(TrialOutcome.Dropped, release.Value.Outcome);
        }

        [Test]
        public void ReleaseNotOverBasket_EvenAfterHold_CountsAsDropped()
        {
            var sm = new TrialStateMachine(timeLimitMs: 15000, holdMs: 100);
            sm.ShowTarget(0);
            sm.OnGrasp(0);
            var release = sm.OnRelease(500, overBasket: false);
            Assert.AreEqual(TrialOutcome.Dropped, release.Value.Outcome);
        }

        [Test]
        public void ReleaseAfterSufficientHoldOverBasket_Succeeds()
        {
            var sm = new TrialStateMachine(timeLimitMs: 15000, holdMs: 300);
            sm.ShowTarget(0);
            sm.OnGrasp(0);
            var release = sm.OnRelease(400, overBasket: true);
            Assert.AreEqual("placed", release.Value.EventType);
            Assert.AreEqual(TrialOutcome.Success, release.Value.Outcome);
        }

        [Test]
        public void DuplicateShowTarget_Throws()
        {
            var sm = new TrialStateMachine(15000, 300);
            sm.ShowTarget(0);
            Assert.Throws<System.InvalidOperationException>(() => sm.ShowTarget(1));
        }

        [Test]
        public void MovementOnset_IgnoredIfNotInTargetShownState()
        {
            var sm = new TrialStateMachine(15000, 300);
            // Never called ShowTarget -> still Idle
            var onset = sm.OnMovementOnset(0);
            Assert.IsNull(onset);
        }

        [Test]
        public void TickAfterTrialEnded_IsNoOp()
        {
            var sm = new TrialStateMachine(1000, 300);
            sm.ShowTarget(0);
            sm.Tick(1001); // times out
            Assert.AreEqual(TrialState.TrialEnded, sm.State);
            Assert.IsNull(sm.Tick(5000));
        }
    }
}
