using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    public class TrackingRateMeterTests
    {
        private sealed class FakeHandSource : IHandSource
        {
            public int Version;
            public bool TryGetJointPose(string joint, out double[] pos, out double[] rot) { pos = null; rot = null; return false; }
            public TrackingConfidence GetConfidence(HandSide side) => TrackingConfidence.High;
            public bool IsTracked(HandSide side) => true;
            public float GetPinchStrength(HandSide side) => 0f;
            public int GetDataVersion(HandSide side) => Version;
        }

        [Test]
        public void MeasuresApproximatelyCorrectHz_ForHighFrequencySource()
        {
            var hand = new FakeHandSource();
            var meter = new TrackingRateMeter(HandSide.Right);
            double nowMs = 0;
            double? result = null;

            // Simulate ~60Hz for just over 1 second (16.67ms per tick).
            for (int i = 0; i < 62; i++)
            {
                nowMs += 16.6667;
                hand.Version++;
                var r = meter.Sample(nowMs, hand);
                if (r.HasValue) result = r;
            }

            Assert.IsTrue(result.HasValue);
            Assert.That(result.Value, Is.InRange(55, 65));
            Assert.IsFalse(meter.BelowThreshold);
        }

        [Test]
        public void LowFrequencySource_FlagsBelowThreshold()
        {
            var hand = new FakeHandSource();
            var meter = new TrackingRateMeter(HandSide.Right);
            double nowMs = 0;
            double? result = null;

            // ~30Hz for just over 1 second.
            for (int i = 0; i < 32; i++)
            {
                nowMs += 33.333;
                hand.Version++;
                var r = meter.Sample(nowMs, hand);
                if (r.HasValue) result = r;
            }

            Assert.IsTrue(result.HasValue);
            Assert.Less(result.Value, TrackingRateMeter.WarnBelowHz);
            Assert.IsTrue(meter.BelowThreshold);
        }
    }
}
