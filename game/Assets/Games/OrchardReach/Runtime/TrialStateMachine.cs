using System;

namespace Opus.Games.OrchardReach
{
    public enum TrialState
    {
        Idle,
        TargetShown,
        MovementOnset,
        Grasped,
        Holding,
        TrialEnded,
    }

    public enum TrialOutcome
    {
        None,
        Success,
        Miss,
        Timeout,
        Dropped,
        // Run15 (stage D sorting): placed in a container that violates the block's sortRule (e.g. a green apple
        // dropped in the red-only basket). Never punished -- reward is withheld and a gentle corrective HUD
        // message shown; see OrchardReachModule.ConfirmPlacedInBasket and event.schema.json's outcome enum
        // (already includes "wrong_target"). Kept last in the enum so no existing ordinal/serialized value shifts.
        WrongTarget,
    }

    /// <summary>event.schema.json's `outcome` enum uses snake_case ("wrong_target"), which plain
    /// `enum.ToString().ToLowerInvariant()` cannot produce for multi-word names. Centralised here so every
    /// caller (OrchardReachModule's Emit/EmitSignal) converts outcomes the same, correct way.</summary>
    public static class TrialOutcomeExtensions
    {
        public static string ToSchemaString(this TrialOutcome outcome) => outcome switch
        {
            TrialOutcome.None => null,
            TrialOutcome.WrongTarget => "wrong_target",
            _ => outcome.ToString().ToLowerInvariant(),
        };
    }

    /// <summary>One state-transition side effect the caller (the Unity-facing OrchardReachModule) should record
    /// as a TrialEvent. Kept as a plain struct so the state machine itself never touches Opus.Sdk/Unity types.</summary>
    public readonly struct TrialSignal
    {
        public readonly string EventType; // matches event.schema.json's `type` enum
        public readonly TrialOutcome Outcome;
        public TrialSignal(string eventType, TrialOutcome outcome = TrialOutcome.None) { EventType = eventType; Outcome = outcome; }
    }

    /// <summary>
    /// One trial's lifecycle: target_shown -> movement_onset -> grasp -> (hold for holdMs) -> placed/trial_end,
    /// or timeout/miss along the way. Pure logic, driven by explicit calls + an injected clock reading — no
    /// Unity Update loop, no XR — so it's fully unit-testable (brief U4).
    /// </summary>
    public sealed class TrialStateMachine
    {
        public TrialState State { get; private set; } = TrialState.Idle;
        public TrialOutcome Outcome { get; private set; } = TrialOutcome.None;

        private readonly int _timeLimitMs;
        private readonly int _holdMs;
        private double _targetShownAtMs;
        private double _graspStartedAtMs;

        public TrialStateMachine(int timeLimitMs, int holdMs)
        {
            _timeLimitMs = timeLimitMs;
            _holdMs = holdMs;
        }

        public TrialSignal ShowTarget(double nowMs)
        {
            if (State != TrialState.Idle) throw new InvalidOperationException($"ShowTarget called in state {State}");
            State = TrialState.TargetShown;
            _targetShownAtMs = nowMs;
            return new TrialSignal("target_shown");
        }

        /// <summary>Hand has left its rest position and started moving toward the target.</summary>
        public TrialSignal? OnMovementOnset(double nowMs)
        {
            if (State != TrialState.TargetShown) return null; // ignore spurious/duplicate onset signals
            State = TrialState.MovementOnset;
            return new TrialSignal("movement_onset");
        }

        /// <summary>Pinch or whole-hand grasp detected while over the target.</summary>
        public TrialSignal? OnGrasp(double nowMs)
        {
            if (State != TrialState.MovementOnset && State != TrialState.TargetShown) return null;
            State = TrialState.Grasped;
            _graspStartedAtMs = nowMs;
            return new TrialSignal("grasp");
        }

        /// <summary>Grasp released before the hold requirement or the basket was reached — counts as dropped, not success.</summary>
        public TrialSignal? OnRelease(double nowMs, bool overBasket)
        {
            if (State != TrialState.Grasped && State != TrialState.Holding) return null;
            bool heldLongEnough = (nowMs - _graspStartedAtMs) >= _holdMs;
            if (overBasket && heldLongEnough)
            {
                State = TrialState.TrialEnded;
                Outcome = TrialOutcome.Success;
                return new TrialSignal("placed", TrialOutcome.Success);
            }
            State = TrialState.TrialEnded;
            Outcome = TrialOutcome.Dropped;
            return new TrialSignal("release", TrialOutcome.Dropped);
        }

        /// <summary>Call every frame (or every N ms) while Grasped/Holding to detect the hold requirement being met,
        /// and at any time to detect timeout. Returns a signal when a transition happens, else null.</summary>
        public TrialSignal? Tick(double nowMs)
        {
            if (State == TrialState.TrialEnded) return null;

            if (State == TrialState.Grasped && (nowMs - _graspStartedAtMs) >= _holdMs)
            {
                State = TrialState.Holding;
                return null; // internal transition only; "placed" fires on OnRelease/OnPlaced once basket confirms
            }

            if (nowMs - _targetShownAtMs >= _timeLimitMs && State != TrialState.Holding)
            {
                State = TrialState.TrialEnded;
                Outcome = TrialOutcome.Timeout;
                return new TrialSignal("trial_end", TrialOutcome.Timeout);
            }

            return null;
        }

        /// <summary>Explicit success: fruit held long enough and detected inside the basket collider.</summary>
        public TrialSignal Place(double nowMs)
        {
            if (State != TrialState.Holding && State != TrialState.Grasped)
                throw new InvalidOperationException($"Place called in state {State}");
            State = TrialState.TrialEnded;
            Outcome = TrialOutcome.Success;
            return new TrialSignal("placed", TrialOutcome.Success);
        }

        public TrialSignal EndTrial(double nowMs, TrialOutcome outcome)
        {
            State = TrialState.TrialEnded;
            Outcome = outcome;
            return new TrialSignal("trial_end", outcome);
        }
    }
}
