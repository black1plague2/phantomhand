namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// U4 item 5: both hands pinching for 3 s flips the spectator-details switch (hidden by default). The pinch must be
    /// released before it can flip again. No Unity dependency.
    /// </summary>
    public sealed class PinchToggle
    {
        public const float PinchThreshold = 0.8f;
        public const double HoldMs = 3000;

        private double _since = -1;
        private bool _armed = true;

        public bool On { get; private set; }
        public double Progress01 { get; private set; }

        public bool Update(double nowMs, bool leftPinching, bool rightPinching)
        {
            bool both = leftPinching && rightPinching;
            if (!both) { _since = -1; _armed = true; Progress01 = 0; return On; }
            if (!_armed) return On;
            if (_since < 0) _since = nowMs;
            Progress01 = System.Math.Min(1.0, (nowMs - _since) / HoldMs);
            if (nowMs - _since >= HoldMs) { On = !On; _armed = false; Progress01 = 0; }
            return On;
        }

        public bool Update(double nowMs, float leftStrength, float rightStrength)
        {
            return Update(nowMs, leftStrength >= PinchThreshold, rightStrength >= PinchThreshold);
        }

        public void Reset() { On = false; _since = -1; _armed = true; Progress01 = 0; }
    }
}
