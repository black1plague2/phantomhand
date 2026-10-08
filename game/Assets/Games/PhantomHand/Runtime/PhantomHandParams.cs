using System;
using Opus.Sdk;

namespace Opus.Games.PhantomHand
{
    /// <summary>Typed view over the bound <see cref="ParamSet"/> for Phantom Hand (03-SPEC section 6 + section 12).
    /// Plain C#, no Unity/XR dependency. Every value is clamped to the manifest range so a bad program can never
    /// reach the sleeve with an out-of-range stroke rate or delay.</summary>
    public sealed class PhantomHandParams
    {
        // Core (PRD FR-VR-06)
        public double OffsetCm = 15;
        public double StrokeRateHz = 1.0;
        public double InductionS = 90;
        public double AsyncDelayMs = 600;
        public string ConditionOrder = "async_first";   // D9
        public bool ThreatEnabled = true;
        public bool AgencyEnabled = false;
        public double EmgThreshold = 0.3;
        public double TactileLeadMs = 40;

        // Build params (03-SPEC section 6)
        public double MotorSoaMs = 100;
        public double StrokeJitterMs = 150;
        public double MotorAFromWristCm = 5;
        public double MotorSpacingCm = 10;
        public double ForearmLengthCm = 25;
        public bool HapticsEnabled = true;
        public double HapticMaxIntensity = 1.0;
        public bool FollowDuringInduction = false;
        public bool DemoMode = false;
        public string StimulatedSide = "right";

        // Additions A1-A5 (03-SPEC section 12). Parsed now; behaviour starts after gate G2.
        public bool VoiceoverEnabled = true;
        public string VoiceoverLang = "en";
        public bool DissolveEnabled = true;
        public bool PassthroughReveal = true;
        public double SelfTouchS = 15;
        public bool BreathEnabled = false;
        public double BreathReplayLagS = 20;
        public bool AutonomousCloseEnabled = false;

        public const double DemoInductionS = 45;

        /// <summary>Induction length actually used (demo_mode forces 45 s).</summary>
        public double EffectiveInductionS => DemoMode ? DemoInductionS : InductionS;

        public bool SyncFirst => ConditionOrder == "sync_first";

        public static PhantomHandParams From(ParamSet p)
        {
            var r = new PhantomHandParams
            {
                OffsetCm = Clamp(p.GetDouble("offset_cm", 15), 5, 30),
                StrokeRateHz = Clamp(p.GetDouble("stroke_rate_hz", 1.0), 0.5, 1.5),
                InductionS = Clamp(p.GetDouble("induction_s", 90), 30, 180),
                AsyncDelayMs = Clamp(p.GetDouble("async_delay_ms", 600), 300, 1000),
                ConditionOrder = ParseOrder(p.GetString("condition_order", "async_first")),
                ThreatEnabled = p.GetBool("threat_enabled", true),
                AgencyEnabled = p.GetBool("agency_enabled", false),
                EmgThreshold = Clamp(p.GetDouble("emg_threshold", 0.3), 0, 1),
                TactileLeadMs = Clamp(p.GetDouble("tactile_lead_ms", 40), 0, 150),
                MotorSoaMs = Clamp(p.GetDouble("motor_soa_ms", 100), 60, 300),
                StrokeJitterMs = Clamp(p.GetDouble("stroke_jitter_ms", 150), 0, 300),
                MotorAFromWristCm = Clamp(p.GetDouble("motor_a_from_wrist_cm", 5), 2, 10),
                MotorSpacingCm = Clamp(p.GetDouble("motor_spacing_cm", 10), 5, 15),
                ForearmLengthCm = Clamp(p.GetDouble("forearm_length_cm", 25), 20, 30),
                HapticsEnabled = p.GetBool("haptics_enabled", true),
                HapticMaxIntensity = Clamp(p.GetDouble("haptic_max_intensity", 1.0), 0, 1),
                FollowDuringInduction = p.GetBool("follow_during_induction", false),
                DemoMode = p.GetBool("demo_mode", false),
                StimulatedSide = "right", // MVP: right only (left = stretch)
                VoiceoverEnabled = p.GetBool("voiceover_enabled", true),
                VoiceoverLang = p.GetString("voiceover_lang", "en") == "hi" ? "hi" : "en",
                DissolveEnabled = p.GetBool("dissolve_enabled", true),
                PassthroughReveal = p.GetBool("passthrough_reveal", true),
                SelfTouchS = Clamp(p.GetDouble("self_touch_s", 15), 0, 30),
                BreathEnabled = p.GetBool("breath_enabled", false),
                BreathReplayLagS = Clamp(p.GetDouble("breath_replay_lag_s", 20), 10, 30),
                AutonomousCloseEnabled = p.GetBool("autonomous_close_enabled", false),
            };
            // Derived guard: brush pass over motor B must stay on the forearm (needs 2 cm to the elbow).
            double minForearm = r.MotorAFromWristCm + r.MotorSpacingCm + 2;
            if (r.ForearmLengthCm < minForearm) r.ForearmLengthCm = minForearm;
            // Self-touch is the tail of the induction; keep at least 20 s of brush before it.
            double maxSelfTouch = Math.Max(0, r.EffectiveInductionS - 20);
            if (r.SelfTouchS > maxSelfTouch) r.SelfTouchS = maxSelfTouch;
            return r;
        }

        private static string ParseOrder(string s) => s == "sync_first" ? "sync_first" : "async_first";

        private static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo;
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
