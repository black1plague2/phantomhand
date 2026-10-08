using System.Diagnostics;

namespace Opus.Sdk
{
    /// <summary>
    /// Monotonic milliseconds since session start. Same clock drives event t_ms and kinematics t_ms
    /// so the two streams line up exactly (session-envelope / event / kinematics-chunk schemas all key off this).
    /// Backed by <see cref="Stopwatch"/> rather than wall-clock time, so it is immune to clock adjustments.
    /// </summary>
    public sealed class SessionClock
    {
        private readonly Stopwatch _stopwatch;
        private readonly bool _manual;
        private double _manualMs;

        public SessionClock()
        {
            _stopwatch = Stopwatch.StartNew();
        }

        private SessionClock(bool manual)
        {
            _manual = manual;
        }

        /// <summary>For tests: construct a clock that has already elapsed a fixed startup offset (real time still advances).</summary>
        public static SessionClock StartedAt(double offsetMs)
        {
            var clock = new SessionClock();
            clock._offsetMs = offsetMs;
            return clock;
        }

        /// <summary>For tests: a clock that only advances when <see cref="Advance"/> is called — deterministic,
        /// used to test time-based logic (chunk rollover) without depending on wall-clock timing.</summary>
        public static SessionClock Manual(double startMs = 0)
        {
            return new SessionClock(manual: true) { _manualMs = startMs };
        }

        public void Advance(double deltaMs)
        {
            if (!_manual) throw new System.InvalidOperationException("Advance() is only valid on a Manual clock");
            _manualMs += deltaMs;
        }

        private double _offsetMs;

        public double NowMs => _manual ? _manualMs : _offsetMs + _stopwatch.Elapsed.TotalMilliseconds;

        public void Reset() => _stopwatch?.Restart();
    }
}
