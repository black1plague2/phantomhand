using System;
using System.Collections.Generic;

namespace Opus.Games.PhantomHand
{
    /// <summary>Seeded RNG (System.Random) so every plan is reproducible (02-RULES 1.10).</summary>
    public sealed class PhRandom
    {
        private readonly Random _r;
        public PhRandom(int seed) { _r = new Random(seed); }
        public double NextDouble() { return _r.NextDouble(); }
        /// <summary>Uniform in [-half, +half].</summary>
        public double Jitter(double half) { return (NextDouble() * 2.0 - 1.0) * half; }
        public bool Chance(double p) { return NextDouble() < p; }
    }

    /// <summary>One planned brush stroke. All times are session ms (absolute) unless built with t0 = 0.</summary>
    public sealed class StrokePlan
    {
        public int Index;
        /// <summary>Brush touches the wrist.</summary>
        public double StartMs;
        /// <summary>Brush passes over motor A / motor B (what the eye sees).</summary>
        public double PassAMs, PassBMs;
        /// <summary>Brush reaches the elbow.</summary>
        public double EndMs;
        /// <summary>When motor 0 (A) / motor 1 (B) should START vibrating (before the tactile lead is subtracted).</summary>
        public double CueMotor0Ms, CueMotor1Ms;
        /// <summary>ASYNC only: motors fire in swapped order (B-slot cue goes to motor 0, A-slot cue to motor 1).</summary>
        public bool Swapped;

        /// <summary>Cue time belonging to the brush pass over motor A's position (delay measured against PassAMs).</summary>
        public double SlotACueMs { get { return Swapped ? CueMotor1Ms : CueMotor0Ms; } }
        /// <summary>Cue time belonging to the brush pass over motor B's position (delay measured against PassBMs).</summary>
        public double SlotBCueMs { get { return Swapped ? CueMotor0Ms : CueMotor1Ms; } }
        public double LastCueMs { get { return Math.Max(CueMotor0Ms, CueMotor1Ms); } }
    }

    /// <summary>
    /// Plans the brush strokes and the matching motor cues for one induction (03-SPEC D1).
    /// Brush speed = motor_spacing_cm / motor_soa_ms, constant wrist to elbow, so sight and touch stay in step:
    /// SYNC cue == the moment the brush passes the motor; ASYNC cue = pass + async_delay_ms (+/- 100 ms), and the A/B
    /// motor order is swapped on 50 % of strokes. Strokes never overlap and the last stroke (with its cues) ends before
    /// the induction ends. Pure maths, seeded, no Unity dependency.
    /// </summary>
    public sealed class StrokeScheduler
    {
        public const double AsyncJitterMs = 100;
        public const double MinStrokeGapMs = 50;
        public const double LeadInMs = 500;
        public const double TailMarginMs = 100;
        public const double MinMotorGapMs = 250;   // software limit per motor (02-RULES 4.2)
        public const int MaxSendsPerSecond = 4;

        private readonly PhantomHandParams _p;
        private readonly int _seed;

        public StrokeScheduler(PhantomHandParams p, int seed) { _p = p; _seed = seed; }

        /// <summary>Brush speed in cm per ms.</summary>
        public double SpeedCmPerMs { get { return _p.MotorSpacingCm / _p.MotorSoaMs; } }
        public double WristToPassAMs { get { return _p.MotorAFromWristCm / SpeedCmPerMs; } }
        public double StrokeDurationMs { get { return _p.ForearmLengthCm / SpeedCmPerMs; } }

        public List<StrokePlan> Plan(PhCondition condition, double t0Ms, double durationMs)
        {
            var rng = new PhRandom(unchecked(_seed * 31 + (condition == PhCondition.Async ? 7919 : 104729)));
            var list = new List<StrokePlan>();
            double nominal = 1000.0 / _p.StrokeRateHz;
            double dur = StrokeDurationMs;
            double endLimit = t0Ms + durationMs - TailMarginMs;
            double start = t0Ms + LeadInMs;
            double prevEnd = double.NegativeInfinity;
            int index = 0;
            double prevStart = double.NaN;

            while (true)
            {
                if (index > 0)
                {
                    double interval = Math.Max(100, nominal + rng.Jitter(_p.StrokeJitterMs));
                    start = Math.Max(prevStart + interval, prevEnd + MinStrokeGapMs);
                }
                var s = new StrokePlan { Index = index };
                s.StartMs = start;
                s.PassAMs = start + WristToPassAMs;
                s.PassBMs = s.PassAMs + _p.MotorSoaMs;
                s.EndMs = start + dur;
                if (condition == PhCondition.Sync)
                {
                    s.CueMotor0Ms = s.PassAMs;
                    s.CueMotor1Ms = s.PassBMs;
                }
                else
                {
                    s.Swapped = rng.Chance(0.5);
                    double slotA = s.PassAMs + _p.AsyncDelayMs + rng.Jitter(AsyncJitterMs);
                    double slotB = s.PassBMs + _p.AsyncDelayMs + rng.Jitter(AsyncJitterMs);
                    s.CueMotor0Ms = s.Swapped ? slotB : slotA;
                    s.CueMotor1Ms = s.Swapped ? slotA : slotB;
                }
                if (Math.Max(s.EndMs, s.LastCueMs) > endLimit) break;
                list.Add(s);
                prevStart = start;
                prevEnd = s.EndMs;
                index++;
                if (index > 10000) break;
            }
            EnforceCueLimits(list);
            return list;
        }

        /// <summary>Pushes cue times later where needed so each motor is >= 250 ms apart and no more than 4 cues fall in
        /// any rolling second. A no-op at default settings; only touches extreme ASYNC combinations.</summary>
        private static void EnforceCueLimits(List<StrokePlan> strokes)
        {
            var refs = new List<KeyValuePair<StrokePlan, int>>();
            foreach (var s in strokes) { refs.Add(new KeyValuePair<StrokePlan, int>(s, 0)); refs.Add(new KeyValuePair<StrokePlan, int>(s, 1)); }
            refs.Sort((a, b) => Get(a).CompareTo(Get(b)));
            var accepted = new List<double>();
            var lastByMotor = new double[] { double.NegativeInfinity, double.NegativeInfinity };
            foreach (var r in refs)
            {
                double t = Get(r);
                int m = MotorOf(r);
                t = Math.Max(t, lastByMotor[m] + MinMotorGapMs);
                if (accepted.Count >= MaxSendsPerSecond)
                {
                    double oldest = accepted[accepted.Count - MaxSendsPerSecond];
                    t = Math.Max(t, oldest + 1000.0);
                }
                Set(r, t);
                lastByMotor[m] = t;
                accepted.Add(t);
                accepted.Sort();
            }
        }

        // Motor index of the (stroke, slot-field) reference: field 0 = CueMotor0Ms, field 1 = CueMotor1Ms.
        private static int MotorOf(KeyValuePair<StrokePlan, int> r) { return r.Value; }
        private static double Get(KeyValuePair<StrokePlan, int> r) { return r.Value == 0 ? r.Key.CueMotor0Ms : r.Key.CueMotor1Ms; }
        private static void Set(KeyValuePair<StrokePlan, int> r, double t)
        {
            if (r.Value == 0) r.Key.CueMotor0Ms = t; else r.Key.CueMotor1Ms = t;
        }
    }
}
