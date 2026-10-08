namespace Opus.Games.OrchardReach
{
    /// <summary>Classic 2-down/1-up staircase on reach % (converges near the ~70.7% success threshold).
    /// Two consecutive successes push the target further away (harder); any single failure pulls it back in (easier).</summary>
    public sealed class AdaptiveStaircase
    {
        public double Current { get; private set; }
        public readonly double Min, Max, StepPercent;
        private int _consecutiveSuccesses;
        public int Reversals { get; private set; }
        private int _lastDirection; // -1 = last moved easier, +1 = last moved harder, 0 = none yet

        public AdaptiveStaircase(double min, double max, double startingPercent, double stepPercent = 5.0)
        {
            Min = min; Max = max; StepPercent = stepPercent;
            Current = Clamp(startingPercent);
        }

        public void RegisterOutcome(bool success)
        {
            if (success)
            {
                _consecutiveSuccesses++;
                if (_consecutiveSuccesses >= 2)
                {
                    Move(+1);
                    _consecutiveSuccesses = 0;
                }
            }
            else
            {
                _consecutiveSuccesses = 0;
                Move(-1);
            }
        }

        private void Move(int direction)
        {
            if (_lastDirection != 0 && direction != _lastDirection) Reversals++;
            _lastDirection = direction;
            Current = Clamp(Current + direction * StepPercent);
        }

        private double Clamp(double value) => value < Min ? Min : (value > Max ? Max : value);
    }
}
