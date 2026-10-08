using System;

namespace Opus.Sdk
{
    /// <summary>One outbound device-level command, ready to serialize to JSON per
    /// contracts/schemas/haptic-device-command.schema.json (v1.1 wire format).</summary>
    public readonly struct HapticDeviceCommand
    {
        public readonly int Motor;
        public readonly int Intensity255;
        public readonly int DurationMs;
        public readonly string Pattern;
        public readonly string CueId;
        public readonly string Cue;
        /// <summary>v1.2 stroke only: session ms the stroke is meant to land (informational, the firmware ignores it).</summary>
        public readonly long? PlayAtMs;

        public HapticDeviceCommand(int motor, int intensity255, int durationMs, string pattern, string cueId, string cue)
            : this(motor, intensity255, durationMs, pattern, cueId, cue, null) { }

        public HapticDeviceCommand(int motor, int intensity255, int durationMs, string pattern, string cueId, string cue, long? playAtMs)
        {
            Motor = motor; Intensity255 = intensity255; DurationMs = durationMs; Pattern = pattern; CueId = cueId; Cue = cue;
            PlayAtMs = playAtMs;
        }

        public HapticDeviceCommand WithPlayAt(double playAtSessionMs) =>
            new HapticDeviceCommand(Motor, Intensity255, DurationMs, Pattern, CueId, Cue, (long)Math.Round(playAtSessionMs));
    }

    /// <summary>
    /// Pure mapping from a clinical cue (trunk_lean/low_confidence/success) to the Electronics team's
    /// device-level wire format, exactly per contracts/HAPTIC_PROTOCOL.md's v1.1 reconciliation table. Kept as
    /// static pure functions (no transport/timing/threading) so the mapping math + clamping is unit-testable in
    /// isolation, independent of <see cref="HapticClient"/>'s rate-limiting/watchdog/transport plumbing.
    /// </summary>
    public static class HapticCueMapper
    {
        public const int MotorUpperArm = 0; // trunk_lean, and the first half of success
        public const int MotorForearm = 1;  // low_confidence, and the second half of success

        public const int DeviceIntensityMin = 0, DeviceIntensityMax = 255;
        public const int DeviceDurationMinMs = 50, DeviceDurationMaxMs = 400;

        /// <summary>trunk_lean -> motor 0, pulse, intensity scaled 0.5..0.8 by excess lean, 300ms.
        /// intensity_frac = clamp(0.5 + excessLeanCm/10, 0.5, 0.8) * maxIntensity.</summary>
        public static HapticDeviceCommand TrunkLean(double excessLeanCm, double maxIntensity, string cueId)
        {
            double frac = Clamp(0.5 + excessLeanCm / 10.0, 0.5, 0.8) * Clamp01(maxIntensity);
            return new HapticDeviceCommand(MotorUpperArm, ToDeviceIntensity(frac), ClampDuration(300), "pulse", cueId, "trunk_lean");
        }

        /// <summary>low_confidence -> motor 1, pulse, intensity 0.4 * maxIntensity, 120ms, sent TWICE 120ms apart
        /// (the firmware has no double_tap pattern, so the double-pulse is emitted as two separate commands by
        /// the caller — this returns the single-pulse command shape used for both).</summary>
        public static HapticDeviceCommand LowConfidence(double maxIntensity, string cueId)
        {
            double frac = 0.4 * Clamp01(maxIntensity);
            return new HapticDeviceCommand(MotorForearm, ToDeviceIntensity(frac), ClampDuration(120), "pulse", cueId, "low_confidence");
        }

        /// <summary>success -> motor 0 then motor 1 (40ms apart), buzz, intensity 0.6 * maxIntensity, 200ms.
        /// Returns both commands; the caller is responsible for the 40ms gap between sending them.</summary>
        public static (HapticDeviceCommand first, HapticDeviceCommand second) Success(double maxIntensity, string cueId)
        {
            double frac = 0.6 * Clamp01(maxIntensity);
            int intensity = ToDeviceIntensity(frac);
            int duration = ClampDuration(200);
            var first = new HapticDeviceCommand(MotorUpperArm, intensity, duration, "buzz", cueId, "success");
            var second = new HapticDeviceCommand(MotorForearm, intensity, duration, "buzz", cueId, "success");
            return (first, second);
        }

        /// <summary>Phantom Hand brush pulse (HAPTIC_PROTOCOL v1.2): motor 0|1, pulse, 200 ms,
        /// intensity round(150 x maxIntensity) capped at 150 (3 V coin motors on a 5 V rail).</summary>
        public const int StrokeIntensityCap = 150, StrokeDurationMs = 200;

        public static HapticDeviceCommand Stroke(int motor, double maxIntensity, string cueId)
        {
            int m = motor <= 0 ? 0 : 1;
            int v = (int)Math.Round(StrokeIntensityCap * Clamp01(maxIntensity), MidpointRounding.AwayFromZero);
            if (v > StrokeIntensityCap) v = StrokeIntensityCap;
            return new HapticDeviceCommand(m, v, ClampDuration(StrokeDurationMs), "pulse", cueId, "stroke");
        }

        public static int ToDeviceIntensity(double frac0to1) =>
            (int)Math.Round(Clamp(frac0to1, 0.0, 1.0) * DeviceIntensityMax, MidpointRounding.AwayFromZero) is var v
                ? (int)Clamp(v, DeviceIntensityMin, DeviceIntensityMax) : 0;

        public static int ClampDuration(int ms) => (int)Clamp(ms, DeviceDurationMinMs, DeviceDurationMaxMs);

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        private static double Clamp01(double v) => Clamp(v, 0.0, 1.0);
    }
}
