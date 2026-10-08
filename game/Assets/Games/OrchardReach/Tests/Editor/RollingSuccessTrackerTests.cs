using NUnit.Framework;

namespace Opus.Games.OrchardReach.Tests
{
    public class RollingSuccessTrackerTests
    {
        [Test]
        public void Rate_IsZero_WhenEmpty()
        {
            var t = new RollingSuccessTracker(5);
            Assert.AreEqual(0.0, t.Rate);
            Assert.IsFalse(t.IsFull(5));
        }

        [Test]
        public void Rate_ComputesOverWindow_AndDropsOldest()
        {
            var t = new RollingSuccessTracker(5);
            // 4 success, 1 fail -> 80%, matches the brief's auto-advance threshold exactly.
            t.RegisterOutcome(true);
            t.RegisterOutcome(true);
            t.RegisterOutcome(true);
            t.RegisterOutcome(true);
            t.RegisterOutcome(false);
            Assert.IsTrue(t.IsFull(5));
            Assert.AreEqual(0.8, t.Rate, 1e-9);

            // Push one more success in: oldest (a success) falls out of the 5-window, net rate unchanged.
            t.RegisterOutcome(true);
            Assert.AreEqual(5, t.Count);
            Assert.AreEqual(0.8, t.Rate, 1e-9);

            // Push two failures: both earlier successes fall out, rate drops.
            t.RegisterOutcome(false);
            t.RegisterOutcome(false);
            Assert.AreEqual(5, t.Count);
            Assert.AreEqual(0.4, t.Rate, 1e-9); // window now: [F,F,T,F,F] i.e. 2/5... verify exact sequence below
        }

        [Test]
        public void IsFull_FalseUntilWindowSizeReached()
        {
            var t = new RollingSuccessTracker(3);
            t.RegisterOutcome(true);
            Assert.IsFalse(t.IsFull(3));
            t.RegisterOutcome(true);
            Assert.IsFalse(t.IsFull(3));
            t.RegisterOutcome(true);
            Assert.IsTrue(t.IsFull(3));
        }

        [Test]
        public void Reset_ClearsWindow()
        {
            var t = new RollingSuccessTracker(3);
            t.RegisterOutcome(true);
            t.RegisterOutcome(true);
            t.Reset();
            Assert.AreEqual(0, t.Count);
            Assert.AreEqual(0.0, t.Rate);
        }
    }

    public class SortingRuleEvaluatorTests
    {
        [Test]
        public void NoRule_AlwaysCorrect()
        {
            Assert.IsTrue(SortingRuleEvaluator.IsCorrectContainer("none", "compost", "red", true));
        }

        [Test]
        public void RedOnly_RedInRedBasket_Correct()
        {
            Assert.IsTrue(SortingRuleEvaluator.IsCorrectContainer("red_only", "red_basket", "red", true));
        }

        [Test]
        public void RedOnly_GreenInRedBasket_WrongTarget()
        {
            Assert.IsFalse(SortingRuleEvaluator.IsCorrectContainer("red_only", "red_basket", "green", true));
        }

        [Test]
        public void RedOnly_GreenInGreenBasket_Correct()
        {
            // "Only RED apples in the red basket" implies non-red apples belong elsewhere -- placing a green
            // apple somewhere that ISN'T the red basket is not a rule violation.
            Assert.IsTrue(SortingRuleEvaluator.IsCorrectContainer("red_only", "green_basket", "green", true));
        }

        [Test]
        public void RipeOnly_UnripeInCompost_Correct()
        {
            Assert.IsTrue(SortingRuleEvaluator.IsCorrectContainer("ripe_only", "compost", "red", false));
        }

        [Test]
        public void RipeOnly_RipeInCompost_WrongTarget()
        {
            Assert.IsFalse(SortingRuleEvaluator.IsCorrectContainer("ripe_only", "compost", "red", true));
        }

        [Test]
        public void ColorMatch_RedInGreenBasket_WrongTarget()
        {
            Assert.IsFalse(SortingRuleEvaluator.IsCorrectContainer("color_match", "green_basket", "red", true));
        }

        [Test]
        public void ColorMatch_NeitherColorGoesToCompost_Correct()
        {
            Assert.IsTrue(SortingRuleEvaluator.IsCorrectContainer("color_match", "compost", "yellow", true));
        }
    }
}
