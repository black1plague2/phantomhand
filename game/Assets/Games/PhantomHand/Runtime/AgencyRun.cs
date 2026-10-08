using System;

namespace Opus.Games.PhantomHand
{
    public enum AgencyStep { Rest, Squeeze, Driven, Watch }

    /// <summary>
    /// A5 agency phase (30 s), pure: Rest 0-4 s, Squeeze 4-8 s, Driven 8-20 s, Watch 20-30 s (without the autonomous close, Driven runs
    /// to 30 s). Ticked every frame with the session ms and a normalised muscle level 0..1 (null = no source at all).
    /// Driven: the hand closes when the level reaches emg_threshold and opens again at 0.6 x emg_threshold (hysteresis, no chatter);
    /// the curl eases to the target with a 0.12 s time constant; every closing is counted. A null level keeps the hand open.
    /// Watch: the level no longer controls the hand. It closes by itself at 22.0 s and 25.5 s (close 0.25 s, hold 0.9 s, open 0.35 s);
    /// before each it waits up to 1.5 s for the level to be at or below the open threshold ("EMG flat"), then fires anyway.
    /// Each fire raises <see cref="OnAutonomousClose"/> with the level (0 when unknown).
    /// </summary>
    public sealed class AgencyRun
    {
        public const double RestEndS = 4, SqueezeEndS = 8, DrivenEndS = 20, TotalS = 30;
        public const double OpenFraction = 0.6, CurlTauS = 0.12;
        public const double CloseS = 0.25, HoldS = 0.9, ReleaseS = 0.35, WaitS = 1.5;
        public static readonly double[] SelfCloseAtS = { 22.0, 25.5 };

        private readonly double _startMs, _threshold;
        private double _lastMs = double.NaN;
        private bool _closed;            // Driven: the hysteresis state
        private int _nextSelfClose;
        private double _selfCloseStartS = -1;
        private double _curl;

        public AgencyRun(double startMs, double emgThreshold, bool autonomousClose)
        {
            _startMs = startMs; _threshold = emgThreshold; AutonomousEnabled = autonomousClose;
        }

        public double StartMs { get { return _startMs; } }
        public bool AutonomousEnabled { get; private set; }
        public AgencyStep Step { get; private set; }
        /// <summary>Seconds into the phase at the last Tick.</summary>
        public double ElapsedS { get; private set; }
        /// <summary>0 = open hand, 1 = fist.</summary>
        public double Curl01 { get { return _curl; } }
        public int DrivenCloses { get; private set; }
        public int AutonomousCloses { get; private set; }
        public double OpenThreshold { get { return _threshold * OpenFraction; } }
        /// <summary>The hand closed by itself; the argument is the muscle level at that moment (0 when unknown).</summary>
        public event Action<double> OnAutonomousClose;

        public static AgencyStep StepAt(double elapsedS, bool autonomousClose)
        {
            if (elapsedS < RestEndS) return AgencyStep.Rest;
            if (elapsedS < SqueezeEndS) return AgencyStep.Squeeze;
            return elapsedS < DrivenEndS || !autonomousClose ? AgencyStep.Driven : AgencyStep.Watch;
        }

        /// <summary>Curl of a self-close u seconds after it started: ease up over 0.25 s, hold 0.9 s, ease down over 0.35 s.</summary>
        public static double SelfCloseCurl(double u)
        {
            if (u <= 0) return 0;
            if (u < CloseS) return Smooth(u / CloseS);
            if (u < CloseS + HoldS) return 1;
            return u < CloseS + HoldS + ReleaseS ? 1 - Smooth((u - CloseS - HoldS) / ReleaseS) : 0;
        }

        private static double Smooth(double x) { return x * x * (3 - 2 * x); }

        public void Tick(double nowMs, double? level)
        {
            double t = Math.Max(0, (nowMs - _startMs) / 1000.0);
            double dt = double.IsNaN(_lastMs) ? 0 : Math.Max(0, (nowMs - _lastMs) / 1000.0);
            _lastMs = nowMs; ElapsedS = t; Step = StepAt(t, AutonomousEnabled);
            if (Step == AgencyStep.Driven) Drive(level, dt);
            else if (Step == AgencyStep.Watch) Watch(t, level, dt);
            else Ease(0, dt);
        }

        private void Drive(double? level, double dt)
        {
            if (!level.HasValue) _closed = false;
            else if (!_closed && level.Value >= _threshold) { _closed = true; DrivenCloses++; }
            else if (_closed && level.Value <= OpenThreshold) _closed = false;
            Ease(_closed ? 1 : 0, dt);
        }

        private void Watch(double t, double? level, double dt)
        {
            _closed = false;
            if (_selfCloseStartS >= 0)
            {
                double u = t - _selfCloseStartS;
                _curl = SelfCloseCurl(u);
                if (u >= CloseS + HoldS + ReleaseS) _selfCloseStartS = -1;
                return;
            }
            Ease(0, dt);                                                  // the level never moves the hand here
            if (_nextSelfClose >= SelfCloseAtS.Length || t < SelfCloseAtS[_nextSelfClose]) return;
            bool flat = !level.HasValue || level.Value <= OpenThreshold;
            if (!flat && t < SelfCloseAtS[_nextSelfClose] + WaitS) return;   // still squeezing: wait, but not longer than 1.5 s
            _nextSelfClose++; _selfCloseStartS = t; AutonomousCloses++;
            var h = OnAutonomousClose; if (h != null) h(level ?? 0.0);
        }

        private void Ease(double target, double dt)
        {
            _curl += (target - _curl) * (1.0 - Math.Exp(-dt / CurlTauS));
        }
    }

    /// <summary>
    /// The level when there is no muscle sensor: real-hand flexion from hand tracking. Open (0) = the mean fingertip-to-palm distance of
    /// the Rest window, fist (1) = the smallest distance of the Squeeze window. A fist that moves the fingertip less than 1 cm is not a signal.
    /// </summary>
    public sealed class FlexionCalibration
    {
        public const double MinGapM = 0.01;
        private double _restSum, _squeezeMin = double.PositiveInfinity;
        private int _restN;

        public double RestMeanM { get; private set; }
        public double SqueezeMinM { get; private set; }
        public bool Ready { get; private set; }

        public void AddRest(double distM) { _restSum += distM; _restN++; }
        public void AddSqueeze(double distM) { if (distM < _squeezeMin) _squeezeMin = distM; }

        public bool Calibrate()
        {
            Ready = false;
            if (_restN == 0 || double.IsInfinity(_squeezeMin)) return false;
            RestMeanM = _restSum / _restN; SqueezeMinM = _squeezeMin;
            Ready = RestMeanM - SqueezeMinM >= MinGapM;
            return Ready;
        }

        public double Level(double distM)
        {
            double v = (RestMeanM - distM) / (RestMeanM - SqueezeMinM);
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }
}
