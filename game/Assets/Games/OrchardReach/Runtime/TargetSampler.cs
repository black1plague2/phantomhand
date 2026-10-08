using System;

namespace Opus.Games.OrchardReach
{
    /// <summary>Describes one sampled target before placement (side resolved, distractor flag set).</summary>
    public readonly struct TargetSpec
    {
        public readonly string Side;
        public readonly double AzimuthDeg, ElevationDeg, ReachPercent;
        public readonly bool IsDistractor;

        public TargetSpec(string side, double azimuthDeg, double elevationDeg, double reachPercent, bool isDistractor)
        {
            Side = side; AzimuthDeg = azimuthDeg; ElevationDeg = elevationDeg; ReachPercent = reachPercent; IsDistractor = isDistractor;
        }
    }

    /// <summary>Injectable RNG so target sampling is deterministic in tests.</summary>
    public interface IRandomSource
    {
        double NextDouble(); // [0,1)
    }

    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;
        public SystemRandomSource(int seed) => _random = new Random(seed);
        public double NextDouble() => _random.NextDouble();
    }

    /// <summary>
    /// Samples one real target (plus optional distractors) per trial from the clinician-set workspace ranges.
    /// `neglectBias` skews which half of the azimuth range gets sampled more often — used for left/right
    /// neglect training (ARCHITECTURE.md §4). Pure math + injected RNG: no Unity/XR dependency.
    /// </summary>
    public sealed class TargetSampler
    {
        private readonly OrchardReachParams _params;
        private readonly IRandomSource _random;
        private int _alternateIndex;

        public TargetSampler(OrchardReachParams parameters, IRandomSource random)
        {
            _params = parameters;
            _random = random;
        }

        public string ResolveSideForTrial(int trialIndex)
        {
            switch (_params.Side)
            {
                case "left": return "left";
                case "right": return "right";
                case "alternate": return (trialIndex % 2 == 0) ? "right" : "left";
                case "both": return _random.NextDouble() < 0.5 ? "left" : "right";
                default: return "right";
            }
        }

        /// <summary>Sample an azimuth within [min,max], skewed by neglectBias in [-1,1]
        /// (-1 = always low/left end of the range, +1 = always high/right end, 0 = uniform).</summary>
        public double SampleAzimuthDeg(double min, double max)
        {
            double u = _random.NextDouble(); // uniform [0,1)
            double bias = Math.Max(-1, Math.Min(1, _params.NeglectBias));
            // Warp u toward 0 or 1 depending on bias sign/magnitude without ever going fully degenerate.
            double exponent = 1.0 - Math.Abs(bias) * 0.9; // bias=1 -> exponent 0.1 (skewed), bias=0 -> exponent 1 (uniform)
            double warped = Math.Pow(u, exponent);
            if (bias < 0) warped = 1.0 - warped; // push toward the low (left/negative-azimuth) end
            return min + warped * (max - min);
        }

        public double SampleElevationDeg(double min, double max) => min + _random.NextDouble() * (max - min);

        public TargetSpec SampleRealTarget(int trialIndex, double reachPercentOverride = double.NaN)
        {
            string side = ResolveSideForTrial(trialIndex);
            double az = SampleAzimuthDeg(_params.AzimuthMinDeg, _params.AzimuthMaxDeg);
            double el = SampleElevationDeg(_params.ElevationMinDeg, _params.ElevationMaxDeg);
            double reach = double.IsNaN(reachPercentOverride)
                ? _params.ReachPercentMin + _random.NextDouble() * (_params.ReachPercentMax - _params.ReachPercentMin)
                : reachPercentOverride;
            return new TargetSpec(side, az, el, reach, isDistractor: false);
        }

        public TargetSpec[] SampleDistractors(int count, TargetSpec real)
        {
            var result = new TargetSpec[count];
            for (int i = 0; i < count; i++)
            {
                // Distractors share the workspace range but are placed away from the real target's azimuth.
                double az = SampleAzimuthDeg(_params.AzimuthMinDeg, _params.AzimuthMaxDeg);
                double el = SampleElevationDeg(_params.ElevationMinDeg, _params.ElevationMaxDeg);
                double reach = _params.ReachPercentMin + _random.NextDouble() * (_params.ReachPercentMax - _params.ReachPercentMin);
                string side = az >= 0 ? "right" : "left";
                result[i] = new TargetSpec(side, az, el, reach, isDistractor: true);
            }
            return result;
        }
    }
}
