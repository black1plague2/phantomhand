using Opus.Sdk;

namespace Opus.Games.OrchardReach
{
    /// <summary>Typed view over the bound <see cref="ParamSet"/> for Orchard Reach. Plain C#, no Unity/XR
    /// dependency, so placement math and the trial state machine are unit-testable in isolation.</summary>
    public sealed class OrchardReachParams
    {
        public string Side;
        public int TrialCount;
        public double ReachPercentMin, ReachPercentMax;
        public double AzimuthMinDeg, AzimuthMaxDeg;
        public double ElevationMinDeg, ElevationMaxDeg;
        public string GraspType;
        public int HoldMs;
        public int TimeLimitMs;
        public int Distractors;
        public double NeglectBias;
        public bool Adaptive;
        public double TrunkLeanWarningCm;
        public bool GhostGuide;
        public bool HapticsEnabled;
        public double HapticMaxIntensity;

        // Run15 (stages A-E + adaptive difficulty + sorting + feedback additions). Kept as plain typed fields
        // on this Unity-free class, same as every param above, so stage/sorting/adaptive logic stays unit
        // testable without a scene.
        public string Stage;              // "A_touch" | "B_grasp_place" | "C_precision" | "D_sorting" | "E_sequencing"
        public bool AutoAdvance;
        public string SortRule;           // "red_only" | "green_only" | "ripe_only" | "unripe_only" | "color_match" | "none"
        public int ContainerCount;
        public double TargetSizeScale;
        public double SuccessBandMin;
        public double SuccessBandMax;
        public int AdaptiveWindow;
        public bool GhostTrajectory;
        public double VariableRewardRate;
        public bool CompensationShoulder;

        public static OrchardReachParams From(ParamSet p)
        {
            var reach = p.GetDoubleArray("reachPercent");
            var az = p.GetDoubleArray("azimuthRangeDeg");
            var el = p.GetDoubleArray("elevationRangeDeg");
            return new OrchardReachParams
            {
                Side = p.GetString("side", "right"),
                TrialCount = p.GetInt("trialCount", 20),
                ReachPercentMin = reach.Length > 0 ? reach[0] : 50,
                ReachPercentMax = reach.Length > 1 ? reach[1] : 85,
                AzimuthMinDeg = az.Length > 0 ? az[0] : -45,
                AzimuthMaxDeg = az.Length > 1 ? az[1] : 45,
                ElevationMinDeg = el.Length > 0 ? el[0] : 0,
                ElevationMaxDeg = el.Length > 1 ? el[1] : 40,
                GraspType = p.GetString("graspType", "any"),
                HoldMs = p.GetInt("holdMs", 300),
                TimeLimitMs = p.GetInt("timeLimitMs", 15000),
                Distractors = p.GetInt("distractors", 0),
                NeglectBias = p.GetDouble("neglectBias", 0),
                Adaptive = p.GetBool("adaptive", false),
                TrunkLeanWarningCm = p.GetDouble("trunkLeanWarningCm", 8),
                GhostGuide = p.GetBool("ghostGuide", true),
                HapticsEnabled = p.GetBool("hapticsEnabled", false),
                HapticMaxIntensity = p.GetDouble("hapticMaxIntensity", 0.8),
                Stage = p.GetString("stage", "B_grasp_place"),
                AutoAdvance = p.GetBool("autoAdvance", false),
                SortRule = p.GetString("sortRule", "none"),
                ContainerCount = p.GetInt("containerCount", 1),
                TargetSizeScale = p.GetDouble("targetSizeScale", 1.0),
                SuccessBandMin = p.GetDouble("successBandMin", 0.70),
                SuccessBandMax = p.GetDouble("successBandMax", 0.85),
                AdaptiveWindow = p.GetInt("adaptiveWindow", 5),
                GhostTrajectory = p.GetBool("ghostTrajectory", false),
                VariableRewardRate = p.GetDouble("variableRewardRate", 0.25),
                CompensationShoulder = p.GetBool("compensationShoulder", false),
            };
        }
    }
}
