using System;

namespace Opus.Games.PhantomHand
{
    public enum CalibState { Waiting, Holding, Confirmed }

    /// <summary>
    /// U4 item 1 maths: the real right wrist must stay within 3 cm of the outline's wrist point for 2 s. Leaving the
    /// radius (or losing tracking) restarts the window. On confirmation <see cref="Wrist"/> is the mean of the held samples
    /// (metres, world/calibration space) and <see cref="Axis"/> the forearm direction (elbow toward fingers, horizontal).
    /// No Unity dependency.
    /// </summary>
    public sealed class CalibrationTracker
    {
        public const double RadiusM = 0.03;
        public const double HoldMs = 2000;

        private readonly double[] _target;
        private readonly double _radius, _holdMs, _vertical;
        private double _startMs;
        private bool _started;
        private double _sx, _sy, _sz;
        private int _n;

        public CalibState State { get; private set; } = CalibState.Waiting;
        public double Progress01 { get; private set; }
        public double[] Wrist { get; private set; }
        public double[] Axis { get; private set; }
        /// <summary>Distance from the target at the last sample, metres (NaN when untracked).</summary>
        public double LastDistanceM { get; private set; } = double.NaN;

        /// <param name="verticalToleranceM">0 = the wrist must be within the radius in all three directions (a sphere). Above 0 the
        /// radius counts on the table plane only and the wrist may rest this much higher or lower than the target: a real table is
        /// rarely as high as the virtual one, and the presenter then moves the room to the arm (see PhantomHandUiPresenter).</param>
        public CalibrationTracker(double[] targetWrist, double radiusM = RadiusM, double holdMs = HoldMs, double verticalToleranceM = 0)
        {
            if (targetWrist == null || targetWrist.Length < 3) throw new ArgumentException("target wrist needs x,y,z");
            _target = (double[])targetWrist.Clone(); _radius = radiusM; _holdMs = holdMs; _vertical = verticalToleranceM;
        }

        public double[] Target { get { return (double[])_target.Clone(); } }

        public void Reset()
        {
            _started = false; _n = 0; _sx = _sy = _sz = 0; Progress01 = 0;
            if (State != CalibState.Confirmed) State = CalibState.Waiting;
        }

        /// <param name="palm">optional palm position; with the wrist it gives the forearm axis (fingers direction, flattened to the table plane).</param>
        public CalibState Update(double nowMs, bool tracked, double[] wrist, double[] palm = null, double[] fallbackAxis = null)
        {
            if (State == CalibState.Confirmed) return State;
            if (!tracked || wrist == null)
            {
                LastDistanceM = double.NaN; Reset(); return State;
            }
            double dx = wrist[0] - _target[0], dy = wrist[1] - _target[1], dz = wrist[2] - _target[2];
            double d = _vertical > 0 ? Math.Sqrt(dx * dx + dz * dz) : Math.Sqrt(dx * dx + dy * dy + dz * dz);
            LastDistanceM = d;
            if (d > _radius || (_vertical > 0 && Math.Abs(dy) > _vertical)) { Reset(); return State; }

            if (!_started) { _started = true; _startMs = nowMs; _n = 0; _sx = _sy = _sz = 0; }
            _sx += wrist[0]; _sy += wrist[1]; _sz += wrist[2]; _n++;
            double held = nowMs - _startMs;
            Progress01 = Math.Min(1.0, held / _holdMs);
            State = CalibState.Holding;
            if (held >= _holdMs)
            {
                Wrist = new[] { _sx / _n, _sy / _n, _sz / _n };
                Axis = ForearmAxis(Wrist, palm, fallbackAxis);
                State = CalibState.Confirmed;
            }
            return State;
        }

        /// <summary>Horizontal unit vector from the wrist toward the palm; the fallback (or +z) when the palm is missing or too close.</summary>
        public static double[] ForearmAxis(double[] wrist, double[] palm, double[] fallback)
        {
            if (wrist != null && palm != null)
            {
                double ax = palm[0] - wrist[0], az = palm[2] - wrist[2];
                double len = Math.Sqrt(ax * ax + az * az);
                if (len > 0.03) return new[] { ax / len, 0.0, az / len };
            }
            if (fallback != null)
            {
                double ax = fallback[0], az = fallback[2];
                double len = Math.Sqrt(ax * ax + az * az);
                if (len > 1e-6) return new[] { ax / len, 0.0, az / len };
            }
            return new[] { 0.0, 0.0, 1.0 };
        }
    }
}
