using NUnit.Framework;
using Opus.Games.OrchardReach;

namespace Opus.Games.OrchardReach.Tests
{
    public class TargetSamplerTests
    {
        private sealed class FixedRandom : IRandomSource
        {
            private readonly double[] _values;
            private int _i;
            public FixedRandom(params double[] values) => _values = values;
            public double NextDouble() => _values[_i++ % _values.Length];
        }

        private static OrchardReachParams DefaultParams(string side = "right", double neglectBias = 0)
        {
            return new OrchardReachParams
            {
                Side = side, TrialCount = 10,
                ReachPercentMin = 50, ReachPercentMax = 85,
                AzimuthMinDeg = -45, AzimuthMaxDeg = 45,
                ElevationMinDeg = 0, ElevationMaxDeg = 40,
                NeglectBias = neglectBias,
            };
        }

        [Test]
        public void AlternateSide_TogglesEveryTrial()
        {
            var sampler = new TargetSampler(DefaultParams(side: "alternate"), new FixedRandom(0.5));
            Assert.AreEqual("right", sampler.ResolveSideForTrial(0));
            Assert.AreEqual("left", sampler.ResolveSideForTrial(1));
            Assert.AreEqual("right", sampler.ResolveSideForTrial(2));
        }

        [Test]
        public void FixedSide_AlwaysReturnsThatSide()
        {
            var sampler = new TargetSampler(DefaultParams(side: "left"), new FixedRandom(0.9));
            Assert.AreEqual("left", sampler.ResolveSideForTrial(0));
            Assert.AreEqual("left", sampler.ResolveSideForTrial(7));
        }

        [Test]
        public void ZeroBias_UniformSample_RespectsRange()
        {
            var sampler = new TargetSampler(DefaultParams(neglectBias: 0), new FixedRandom(0.5));
            double az = sampler.SampleAzimuthDeg(-45, 45);
            Assert.AreEqual(0, az, 1e-9); // u=0.5, exponent=1 -> midpoint
        }

        [Test]
        public void NegativeBias_SkewsTowardLowEndOfRange()
        {
            var biased = new TargetSampler(DefaultParams(neglectBias: -1), new FixedRandom(0.5));
            var neutral = new TargetSampler(DefaultParams(neglectBias: 0), new FixedRandom(0.5));
            double biasedAz = biased.SampleAzimuthDeg(-45, 45);
            double neutralAz = neutral.SampleAzimuthDeg(-45, 45);
            Assert.Less(biasedAz, neutralAz, "bias=-1 should pull samples toward the negative (left) end");
        }

        [Test]
        public void PositiveBias_SkewsTowardHighEndOfRange()
        {
            var biased = new TargetSampler(DefaultParams(neglectBias: 1), new FixedRandom(0.5));
            double biasedAz = biased.SampleAzimuthDeg(-45, 45);
            Assert.Greater(biasedAz, 0);
        }

        [Test]
        public void SampledValues_StayWithinDeclaredRanges()
        {
            var sampler = new TargetSampler(DefaultParams(), new FixedRandom(0.0, 0.25, 0.5, 0.75, 0.999));
            for (int i = 0; i < 20; i++)
            {
                var t = sampler.SampleRealTarget(i);
                Assert.GreaterOrEqual(t.AzimuthDeg, -45);
                Assert.LessOrEqual(t.AzimuthDeg, 45);
                Assert.GreaterOrEqual(t.ElevationDeg, 0);
                Assert.LessOrEqual(t.ElevationDeg, 40);
                Assert.GreaterOrEqual(t.ReachPercent, 50);
                Assert.LessOrEqual(t.ReachPercent, 85);
                Assert.IsFalse(t.IsDistractor);
            }
        }

        [Test]
        public void Distractors_AreFlaggedAndCountMatches()
        {
            var sampler = new TargetSampler(DefaultParams(), new FixedRandom(0.2, 0.4, 0.6, 0.8));
            var real = sampler.SampleRealTarget(0);
            var distractors = sampler.SampleDistractors(3, real);
            Assert.AreEqual(3, distractors.Length);
            foreach (var d in distractors) Assert.IsTrue(d.IsDistractor);
        }
    }
}
