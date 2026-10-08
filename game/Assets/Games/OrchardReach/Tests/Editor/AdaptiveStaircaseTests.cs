using NUnit.Framework;
using Opus.Games.OrchardReach;

namespace Opus.Games.OrchardReach.Tests
{
    public class AdaptiveStaircaseTests
    {
        [Test]
        public void TwoConsecutiveSuccesses_IncreasesDifficulty()
        {
            var s = new AdaptiveStaircase(min: 50, max: 85, startingPercent: 65, stepPercent: 5);
            s.RegisterOutcome(true);
            Assert.AreEqual(65, s.Current); // only 1 success so far, no move yet
            s.RegisterOutcome(true);
            Assert.AreEqual(70, s.Current);
        }

        [Test]
        public void SingleFailure_DecreasesDifficultyImmediately()
        {
            var s = new AdaptiveStaircase(50, 85, 65, 5);
            s.RegisterOutcome(false);
            Assert.AreEqual(60, s.Current);
        }

        [Test]
        public void ClampsAtMinAndMax()
        {
            var s = new AdaptiveStaircase(50, 85, 52, 5);
            for (int i = 0; i < 10; i++) s.RegisterOutcome(false);
            Assert.AreEqual(50, s.Current);

            var s2 = new AdaptiveStaircase(50, 85, 83, 5);
            for (int i = 0; i < 10; i++) { s2.RegisterOutcome(true); s2.RegisterOutcome(true); }
            Assert.AreEqual(85, s2.Current);
        }

        [Test]
        public void FailureAfterOneSuccess_ResetsStreakWithoutDoubleMove()
        {
            var s = new AdaptiveStaircase(50, 85, 65, 5);
            s.RegisterOutcome(true);  // streak=1, no move
            s.RegisterOutcome(false); // resets streak, moves -1
            Assert.AreEqual(60, s.Current);
        }

        [Test]
        public void ReversalsAreCounted()
        {
            var s = new AdaptiveStaircase(50, 85, 65, 5);
            s.RegisterOutcome(true);
            s.RegisterOutcome(true); // move +1 (harder), direction now +1, no reversal yet
            s.RegisterOutcome(false); // move -1 (easier), direction flips -> reversal
            Assert.AreEqual(1, s.Reversals);
        }
    }
}
