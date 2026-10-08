using System;
using System.Collections.Generic;

namespace Opus.Games.PhantomHand
{
    public enum PhPhase
    {
        Idle, Calibrate, ProbePre, Induction, SelfTouch, Agency, Threat, ProbePost, Questionnaire,
        Dissolve, Reveal, Witness, Done
    }

    public enum PhCondition { Sync, Async }

    public static class PhNames
    {
        public static string Of(PhPhase p)
        {
            switch (p)
            {
                case PhPhase.Calibrate: return "calibrate";
                case PhPhase.ProbePre: return "probe_pre";
                case PhPhase.Induction: return "induction";
                case PhPhase.SelfTouch: return "self_touch";
                case PhPhase.Agency: return "agency";
                case PhPhase.Threat: return "threat";
                case PhPhase.ProbePost: return "probe_post";
                case PhPhase.Questionnaire: return "questionnaire";
                case PhPhase.Dissolve: return "dissolve";
                case PhPhase.Reveal: return "reveal";
                case PhPhase.Witness: return "witness";
                case PhPhase.Done: return "done";
                default: return "idle";
            }
        }

        public static string Of(PhCondition c) { return c == PhCondition.Sync ? "sync" : "async"; }
    }

    public struct PhaseChange
    {
        public PhPhase Phase;
        public PhCondition? Condition;
        /// <summary>Condition index (0 or 1) for per-condition phases, null for Calibrate/Dissolve/Reveal/Witness/Done.</summary>
        public int? TrialIndex;
        public double StartMs;
        /// <summary>False when the previous phase was ended by timeout, Advance or Abort instead of a completion call.</summary>
        public bool PreviousConfirmed;
        public PhPhase Previous;
    }

    /// <summary>
    /// Phase walk for one Phantom Hand run (03-SPEC section 5 + 12):
    /// Calibrate, then per condition ProbePre, Induction, [SelfTouch], [Agency, last condition only], [Threat], ProbePost, Questionnaire,
    /// then [Dissolve], [Reveal], Witness, Done. Timer phases end by Tick; Calibrate, probes and the
    /// questionnaire end by Complete or after 60 s (confirmed=false). Time is passed in (session ms) so
    /// tests drive it with a manual clock; Pause/Resume freeze every timer.
    /// </summary>
    public sealed class PhaseStateMachine
    {
        public const double CompletionTimeoutMs = 60000;
        public const double AgencyMs = 30000;
        public const double ThreatMs = 5000;
        public const double DissolveMs = 18000;
        public const double RevealMs = 10000;
        public const double WitnessMs = 30000;

        private readonly PhantomHandParams _p;
        private readonly bool _additions;
        private readonly PhCondition[] _order;
        private List<KeyValuePair<PhPhase, int>> _plan;
        private int _index = -1;
        private double _phaseStartMs;
        private bool _paused;
        private double _pausedAtMs;

        public event Action<PhaseChange> PhaseChanged;

        /// <param name="additions">When true, the optional A2/A3 phases (SelfTouch, Dissolve, Reveal) are inserted
        /// per their params. Default off until the additions start (after gate G2).</param>
        public PhaseStateMachine(PhantomHandParams p, bool additions = false, PhCondition[] orderOverride = null)
        {
            _p = p;
            _additions = additions;
            _order = orderOverride ?? (p.SyncFirst ? new[] { PhCondition.Sync, PhCondition.Async }
                                                   : new[] { PhCondition.Async, PhCondition.Sync });
            Build();
        }

        public PhPhase Phase { get; private set; } = PhPhase.Idle;
        public PhCondition? Condition { get; private set; }
        public int? ConditionIndex { get; private set; }
        public bool Aborted { get; private set; }
        public bool IsPaused { get { return _paused; } }
        public bool IsDone { get { return Phase == PhPhase.Done; } }
        public bool LastEndConfirmed { get; private set; }
        public double PhaseStartMs { get { return _phaseStartMs; } }
        public IReadOnlyList<PhCondition> Order { get { return _order; } }
        public bool AdditionsEnabled { get { return _additions; } }

        /// <summary>Length of the Induction phase (excluding the SelfTouch tail when that is in the plan).</summary>
        public double InductionPhaseMs { get { return (_p.EffectiveInductionS - (_additions ? _p.SelfTouchS : 0)) * 1000.0; } }
        public double SelfTouchMs { get { return (_additions ? _p.SelfTouchS : 0) * 1000.0; } }

        public void SetConditionOrder(PhCondition first)
        {
            if (Phase != PhPhase.Idle && Phase != PhPhase.Calibrate && Phase != PhPhase.ProbePre)
                throw new InvalidOperationException("condition order is locked once induction has started");
            _order[0] = first;
            _order[1] = first == PhCondition.Sync ? PhCondition.Async : PhCondition.Sync;
        }

        private void Build()
        {
            var plan = new List<KeyValuePair<PhPhase, int>>();
            Action<PhPhase, int> add = (ph, c) => plan.Add(new KeyValuePair<PhPhase, int>(ph, c));
            add(PhPhase.Calibrate, -1);
            for (int c = 0; c < 2; c++)
            {
                add(PhPhase.ProbePre, c);
                add(PhPhase.Induction, c);
                if (_additions && _p.SelfTouchS > 0) add(PhPhase.SelfTouch, c);
                if (_p.AgencyEnabled && c == 1) add(PhPhase.Agency, c);   // D14: once, in the LAST condition only (the hand that closes by itself comes at the end)
                if (_p.ThreatEnabled) add(PhPhase.Threat, c);
                add(PhPhase.ProbePost, c);
                // D18: a questionnaire follows every condition in every mode (in demo_mode its list is short, see PhantomHandModule.BuildItems)
                add(PhPhase.Questionnaire, c);
            }
            if (_additions && _p.DissolveEnabled) add(PhPhase.Dissolve, -1);
            if (_additions && _p.PassthroughReveal) add(PhPhase.Reveal, -1);
            add(PhPhase.Witness, -1);
            _plan = plan;
        }

        public IReadOnlyList<PhPhase> PlannedPhases()
        {
            var l = new List<PhPhase>();
            foreach (var e in _plan) l.Add(e.Key);
            l.Add(PhPhase.Done);
            return l;
        }

        public void Start(double nowMs)
        {
            if (Phase != PhPhase.Idle) return;
            Enter(0, nowMs, true);
        }

        public static bool IsTimerPhase(PhPhase p)
        {
            return p == PhPhase.Induction || p == PhPhase.SelfTouch || p == PhPhase.Agency || p == PhPhase.Threat ||
                   p == PhPhase.Dissolve || p == PhPhase.Reveal || p == PhPhase.Witness;
        }

        public static bool IsCompletionPhase(PhPhase p)
        {
            return p == PhPhase.Calibrate || p == PhPhase.ProbePre || p == PhPhase.ProbePost || p == PhPhase.Questionnaire;
        }

        public double DurationMs(PhPhase p)
        {
            switch (p)
            {
                case PhPhase.Induction: return InductionPhaseMs;
                case PhPhase.SelfTouch: return SelfTouchMs;
                case PhPhase.Agency: return AgencyMs;
                case PhPhase.Threat: return ThreatMs;
                case PhPhase.Dissolve: return DissolveMs;
                case PhPhase.Reveal: return RevealMs;
                case PhPhase.Witness: return WitnessMs;
                default: return CompletionTimeoutMs;
            }
        }

        public double RemainingMs(double nowMs)
        {
            if (Phase == PhPhase.Idle || Phase == PhPhase.Done) return 0;
            double now = _paused ? _pausedAtMs : nowMs;
            return Math.Max(0, _phaseStartMs + DurationMs(Phase) - now);
        }

        public double ElapsedMs(double nowMs)
        {
            if (Phase == PhPhase.Idle || Phase == PhPhase.Done) return 0;
            double now = _paused ? _pausedAtMs : nowMs;
            return Math.Max(0, now - _phaseStartMs);
        }

        /// <summary>Ends elapsed timer phases and timed-out completion phases. Catches up exactly: a late Tick starts
        /// each following phase at the scheduled end of the previous one, so the timeline does not drift.</summary>
        public void Tick(double nowMs)
        {
            if (_paused || Phase == PhPhase.Idle || Phase == PhPhase.Done) return;
            int guard = 0;
            while (Phase != PhPhase.Done && guard++ < 64)
            {
                double end = _phaseStartMs + DurationMs(Phase);
                if (nowMs + 1e-9 < end) break;
                bool confirmed = IsTimerPhase(Phase); // timers complete normally; completion phases timed out => false
                Enter(_index + 1, end, confirmed);
            }
        }

        /// <summary>Marks a completion phase (Calibrate/Probe/Questionnaire) done. Ignored on timer phases.</summary>
        public bool Complete(double nowMs, bool confirmed = true)
        {
            if (_paused || !IsCompletionPhase(Phase)) return false;
            Enter(_index + 1, nowMs, confirmed);
            return true;
        }

        /// <summary>Operator next phase: ends whatever is running (confirmed=false for completion phases).</summary>
        public bool Advance(double nowMs)
        {
            if (Phase == PhPhase.Idle || Phase == PhPhase.Done) return false;
            double at = _paused ? _pausedAtMs : nowMs;
            Enter(_index + 1, at, IsTimerPhase(Phase));
            return true;
        }

        public void Abort(double nowMs)
        {
            if (Phase == PhPhase.Done) return;
            Aborted = true;
            _paused = false;
            Enter(_plan.Count, nowMs, false);
        }

        public void Pause(double nowMs)
        {
            if (_paused || Phase == PhPhase.Idle || Phase == PhPhase.Done) return;
            _paused = true;
            _pausedAtMs = nowMs;
        }

        public void Resume(double nowMs)
        {
            if (!_paused) return;
            _phaseStartMs += Math.Max(0, nowMs - _pausedAtMs);
            _paused = false;
        }

        private void Enter(int index, double startMs, bool previousConfirmed)
        {
            var prev = Phase;
            _index = index;
            _phaseStartMs = startMs;
            LastEndConfirmed = previousConfirmed;
            if (index >= _plan.Count)
            {
                Phase = PhPhase.Done; Condition = null; ConditionIndex = null;
            }
            else
            {
                var e = _plan[index];
                Phase = e.Key;
                if (e.Value >= 0) { ConditionIndex = e.Value; Condition = _order[e.Value]; }
                else { ConditionIndex = null; Condition = null; }
            }
            // While paused, a manual Advance resets the paused clock to the new phase start.
            if (_paused) _pausedAtMs = startMs;
            var h = PhaseChanged;
            if (h != null)
                h(new PhaseChange
                {
                    Phase = Phase, Condition = Condition, TrialIndex = ConditionIndex, StartMs = startMs,
                    PreviousConfirmed = previousConfirmed, Previous = prev,
                });
        }
    }
}
