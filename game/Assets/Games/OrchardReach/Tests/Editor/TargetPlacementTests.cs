using NUnit.Framework;
using Opus.Games.OrchardReach;

namespace Opus.Games.OrchardReach.Tests
{
    public class TargetPlacementTests
    {
        [Test]
        public void StraightAheadZeroElevation_LiesOnForwardAxis()
        {
            var chest = new double[] { 0, 0, 0 };
            var p = TargetPlacement.Place(chest, armLengthM: 0.6, azimuthDeg: 0, elevationDeg: 0, reachPercent: 100);
            Assert.AreEqual(0, p.X, 1e-9);
            Assert.AreEqual(0, p.Y, 1e-9);
            Assert.AreEqual(0.6, p.Z, 1e-9);
        }

        [Test]
        public void ReachPercentScalesDistanceLinearly()
        {
            var chest = new double[] { 0, 0, 0 };
            var half = TargetPlacement.Place(chest, 0.6, 0, 0, 50);
            var full = TargetPlacement.Place(chest, 0.6, 0, 0, 100);
            Assert.AreEqual(full.Z / 2.0, half.Z, 1e-9);
        }

        [Test]
        public void PositiveAzimuth_GoesToRightPositiveX()
        {
            var chest = new double[] { 0, 0, 0 };
            var right = TargetPlacement.Place(chest, 0.6, azimuthDeg: 45, elevationDeg: 0, reachPercent: 100);
            Assert.Greater(right.X, 0);
        }

        [Test]
        public void NegativeAzimuth_GoesToLeftNegativeX()
        {
            var chest = new double[] { 0, 0, 0 };
            var left = TargetPlacement.Place(chest, 0.6, azimuthDeg: -45, elevationDeg: 0, reachPercent: 100);
            Assert.Less(left.X, 0);
        }

        [Test]
        public void PositiveElevation_GoesUp()
        {
            var chest = new double[] { 0, 0, 0 };
            var up = TargetPlacement.Place(chest, 0.6, azimuthDeg: 0, elevationDeg: 30, reachPercent: 100);
            Assert.Greater(up.Y, 0);
        }

        [Test]
        public void PlacementDistanceNeverExceedsArmLength()
        {
            var chest = new double[] { 0, 0, 0 };
            for (double reach = 20; reach <= 110; reach += 10)
            {
                var p = TargetPlacement.Place(chest, 0.6, azimuthDeg: 45, elevationDeg: 40, reachPercent: reach);
                double dist = TargetPlacement.DistanceFromChest(chest, p);
                Assert.LessOrEqual(dist, 0.6 * 1.10 + 1e-9); // reachPercent can exceed 100 up to schema max 110
            }
        }

        [Test]
        public void OffsetChestReference_TranslatesResult()
        {
            var chest = new double[] { 0.1, 0.2, 0.3 };
            var p = TargetPlacement.Place(chest, 0.6, 0, 0, 100);
            Assert.AreEqual(0.1, p.X, 1e-9);
            Assert.AreEqual(0.2, p.Y, 1e-9);
            Assert.AreEqual(0.3 + 0.6, p.Z, 1e-9);
        }

        [Test]
        public void ZeroOrNegativeArmLength_Throws()
        {
            var chest = new double[] { 0, 0, 0 };
            Assert.Throws<System.ArgumentOutOfRangeException>(() => TargetPlacement.Place(chest, 0, 0, 0, 50));
        }
    }
}
