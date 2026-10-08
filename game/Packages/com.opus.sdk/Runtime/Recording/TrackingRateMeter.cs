namespace Opus.Sdk
{
    /// <summary>
    /// Measures the real hand-tracking rate by counting <see cref="IHandSource.GetDataVersion"/> increments
    /// per second, because OpenXR gives no reliable way to confirm the configured frequency (Hands Only /
    /// High) actually took effect on device (docs/UNITY_PRACTICES.md §2: "measure it"). Feed the result into
    /// session.json's device.tracking_rate_hz and warn below the 55 Hz floor (High should read ~60Hz+, Low ~30Hz).
    /// </summary>
    public sealed class TrackingRateMeter
    {
        public const double WarnBelowHz = 55.0;
        public const double WindowMs = 1000.0;

        private readonly HandSide _side;
        private int _lastVersion = -1;
        private int _countInWindow;
        private double _windowStartMs;
        public double LastMeasuredHz { get; private set; } = -1;
        public bool BelowThreshold => LastMeasuredHz >= 0 && LastMeasuredHz < WarnBelowHz;

        public TrackingRateMeter(HandSide side) => _side = side;

        /// <summary>Call every frame with the current session time and hand source. Returns a freshly closed
        /// measurement once per rolling window, else null.</summary>
        public double? Sample(double nowMs, IHandSource hands)
        {
            int version = hands.GetDataVersion(_side);
            if (_lastVersion == -1) { _lastVersion = version; _windowStartMs = nowMs; return null; }

            if (version != _lastVersion)
            {
                _countInWindow += (version - _lastVersion);
                _lastVersion = version;
            }

            double elapsed = nowMs - _windowStartMs;
            if (elapsed < WindowMs) return null;

            LastMeasuredHz = _countInWindow / (elapsed / 1000.0);
            _countInWindow = 0;
            _windowStartMs = nowMs;
            return LastMeasuredHz;
        }
    }
}
