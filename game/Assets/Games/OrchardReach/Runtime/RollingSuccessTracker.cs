using System.Collections.Generic;

namespace Opus.Games.OrchardReach
{
    /// <summary>Fixed-size rolling window of trial outcomes (success/not), used by two brief-U-next-run
    /// features that share the same math: (1) stage auto-advance ("rolling success >= 80% over the last 5
    /// trials") and (2) adaptive difficulty ("keep success in a 70-85% band"). Plain C#, no Unity/XR
    /// dependency -- unit-testable like every other piece of orchestration logic in this game (see
    /// OrchardReachModule's own class doc).</summary>
    public sealed class RollingSuccessTracker
    {
        private readonly Queue<bool> _window;
        private readonly int _capacity;
        private int _successCount;

        public RollingSuccessTracker(int windowSize)
        {
            _capacity = System.Math.Max(1, windowSize);
            _window = new Queue<bool>(_capacity);
        }

        public int Count => _window.Count;

        public void RegisterOutcome(bool success)
        {
            if (_window.Count >= _capacity)
            {
                if (_window.Dequeue()) _successCount--;
            }
            _window.Enqueue(success);
            if (success) _successCount++;
        }

        /// <summary>Successes / trials currently in the window. 0 when the window is empty (no trials yet) --
        /// callers that need "at least N trials before deciding anything" should check <see cref="Count"/>
        /// against the required window size first (e.g. auto-advance shouldn't fire on trial 2 of a 5-window).</summary>
        public double Rate => _window.Count == 0 ? 0.0 : (double)_successCount / _window.Count;

        /// <summary>True once the window is full (has seen at least <paramref name="requiredCount"/> trials) --
        /// the auto-advance/adaptive-difficulty gate that stops a rate computed from 1-2 trials from being acted
        /// on as if it were a stable estimate.</summary>
        public bool IsFull(int requiredCount) => _window.Count >= requiredCount;

        public void Reset()
        {
            _window.Clear();
            _successCount = 0;
        }
    }

    /// <summary>Stage D (sorting): decides whether a placement is correct for the block's <c>sortRule</c>.
    /// Pure function of the rule + the fruit's own color/ripeness + which container it landed in -- no Unity
    /// dependency, so the rule table itself is unit-testable without a scene, multiple basket prefabs, or the
    /// bridge being up. The scene is only responsible for reporting WHICH container triggered and the fruit's
    /// color/ripe tags (both already exist as FruitMarker-style data per the U-next-run brief).</summary>
    public static class SortingRuleEvaluator
    {
        /// <summary>containerId is a scene-defined label: "red_basket" | "green_basket" | "compost" (brief's
        /// 3-container stage D layout). fruitColor: "red" | "green" (etc.); fruitRipe: true = ripe.
        /// Returns true (correct container) or false (wrong_target).</summary>
        public static bool IsCorrectContainer(string sortRule, string containerId, string fruitColor, bool fruitRipe)
        {
            switch (sortRule)
            {
                case "none":
                    // No sorting rule active (stages A-C, or D before a rule is picked): any basket is correct,
                    // same as the single-basket behaviour every earlier stage already has.
                    return true;
                case "red_only":
                    return containerId == "red_basket" ? fruitColor == "red" : fruitColor != "red";
                case "green_only":
                    return containerId == "green_basket" ? fruitColor == "green" : fruitColor != "green";
                case "ripe_only":
                    return containerId == "compost" ? !fruitRipe : fruitRipe;
                case "unripe_only":
                    return containerId == "compost" ? fruitRipe : !fruitRipe;
                case "color_match":
                    // Red apples -> red basket, green apples -> green basket, everything else -> compost.
                    if (containerId == "red_basket") return fruitColor == "red";
                    if (containerId == "green_basket") return fruitColor == "green";
                    if (containerId == "compost") return fruitColor != "red" && fruitColor != "green";
                    return false;
                default:
                    return true; // unknown rule: never punish for a rule we don't recognise
            }
        }
    }
}
