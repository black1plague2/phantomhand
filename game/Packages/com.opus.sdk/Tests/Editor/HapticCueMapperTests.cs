using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>Run9 step 3: pure mapping-table tests for HapticCueMapper, per
    /// contracts/HAPTIC_PROTOCOL.md's v1.1 reconciliation table (exact intensities/durations/motors).</summary>
    public class HapticCueMapperTests
    {
        [Test]
        public void TrunkLean_ZeroExcess_MapsToMinIntensity()
        {
            var cmd = HapticCueMapper.TrunkLean(excessLeanCm: 0, maxIntensity: 1.0, cueId: "x");
            Assert.AreEqual(0, cmd.Motor);
            Assert.AreEqual("pulse", cmd.Pattern);
            Assert.AreEqual(300, cmd.DurationMs);
            // frac = clamp(0.5 + 0/10, 0.5, 0.8) * 1.0 = 0.5 -> round(0.5*255) = 128
            Assert.AreEqual(128, cmd.Intensity255);
        }

        [Test]
        public void TrunkLean_LargeExcess_ClampsAt0Point8Fraction()
        {
            var cmd = HapticCueMapper.TrunkLean(excessLeanCm: 100, maxIntensity: 1.0, cueId: "x");
            // frac = clamp(0.5 + 10, 0.5, 0.8) = 0.8 -> round(0.8*255)=204
            Assert.AreEqual(204, cmd.Intensity255);
        }

        [Test]
        public void TrunkLean_MidExcess_ScalesLinearly()
        {
            var cmd = HapticCueMapper.TrunkLean(excessLeanCm: 3, maxIntensity: 1.0, cueId: "x");
            // frac = 0.5 + 3/10 = 0.8 -> round(0.8*255) = 204
            Assert.AreEqual(204, cmd.Intensity255);
        }

        [Test]
        public void TrunkLean_MaxIntensityScalesDown()
        {
            var cmd = HapticCueMapper.TrunkLean(excessLeanCm: 0, maxIntensity: 0.5, cueId: "x");
            // frac = 0.5 * 0.5 = 0.25 -> round(0.25*255) = 64 (63.75 rounds to 64)
            Assert.AreEqual(64, cmd.Intensity255);
        }

        [Test]
        public void LowConfidence_MapsToMotor1Pulse120ms()
        {
            var cmd = HapticCueMapper.LowConfidence(maxIntensity: 1.0, cueId: "x");
            Assert.AreEqual(1, cmd.Motor);
            Assert.AreEqual("pulse", cmd.Pattern);
            Assert.AreEqual(120, cmd.DurationMs);
            // 0.4 * 1.0 * 255 = 102
            Assert.AreEqual(102, cmd.Intensity255);
        }

        [Test]
        public void Success_MapsBothMotorsBuzz200ms()
        {
            var (first, second) = HapticCueMapper.Success(maxIntensity: 1.0, cueId: "x");
            Assert.AreEqual(0, first.Motor);
            Assert.AreEqual(1, second.Motor);
            Assert.AreEqual("buzz", first.Pattern);
            Assert.AreEqual("buzz", second.Pattern);
            Assert.AreEqual(200, first.DurationMs);
            Assert.AreEqual(200, second.DurationMs);
            // 0.6 * 1.0 * 255 = 153
            Assert.AreEqual(153, first.Intensity255);
            Assert.AreEqual(153, second.Intensity255);
        }

        [Test]
        public void Intensity_NeverExceedsDeviceRange()
        {
            var cmd = HapticCueMapper.TrunkLean(excessLeanCm: 9999, maxIntensity: 2.0 /* out of range input, still clamped */, cueId: "x");
            Assert.LessOrEqual(cmd.Intensity255, HapticCueMapper.DeviceIntensityMax);
            Assert.GreaterOrEqual(cmd.Intensity255, HapticCueMapper.DeviceIntensityMin);
        }

        [Test]
        public void Duration_NeverOutsideDeviceRange()
        {
            Assert.AreEqual(HapticCueMapper.DeviceDurationMinMs, HapticCueMapper.ClampDuration(10));
            Assert.AreEqual(HapticCueMapper.DeviceDurationMaxMs, HapticCueMapper.ClampDuration(10000));
            Assert.AreEqual(200, HapticCueMapper.ClampDuration(200));
        }

        [Test]
        public void CueIdAndLabel_AreCarriedThrough()
        {
            var cmd = HapticCueMapper.TrunkLean(0, 1.0, "abc-123");
            Assert.AreEqual("abc-123", cmd.CueId);
            Assert.AreEqual("trunk_lean", cmd.Cue);
        }
    }
}
