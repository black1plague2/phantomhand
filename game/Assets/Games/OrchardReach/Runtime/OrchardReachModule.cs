using System;
using Opus.Sdk;

namespace Opus.Games.OrchardReach
{
    /// <summary>
    /// IGameModule for Orchard Reach. Owns the per-trial orchestration (sample target -> place -> wait for
    /// grasp -> hold -> basket) by delegating math to <see cref="TargetPlacement"/>/<see cref="TargetSampler"/>
    /// and lifecycle to <see cref="TrialStateMachine"/>. The scene (Assets/Games/OrchardReach/Scenes) owns the
    /// MonoBehaviour that calls <see cref="Tick"/> every frame with the shared SessionClock and an
    /// <see cref="Opus.Sdk.IHandSource"/> (Meta XR SDK/OVRSkeleton in-headset and in the Meta XR Simulator,
    /// SyntheticHandDriver in U5) — this class has no MonoBehaviour/OVR dependency itself so the orchestration
    /// logic is unit-testable.
    /// </summary>
    [GameModule("orchard_reach")]
    public sealed class OrchardReachModule : IGameModule
    {
        public GameManifest Manifest { get; private set; }
        public event Action<TrialEvent> OnTrialEvent;

        /// <summary>Run9: exposed so the Shell composition root (OrchardReachSceneController) can read
        /// trunkLeanWarningCm/hapticsEnabled/hapticMaxIntensity without re-parsing the manifest itself.</summary>
        public OrchardReachParams Params => _params;

        /// <summary>The shell loads manifest.json (Assets/Games/OrchardReach/manifest.json) and calls this before
        /// Configure(), so ParamBinder validates against the same schema the app used to build its prescription form.</summary>
        public void LoadManifest(string manifestJson) => Manifest = GameManifest.FromJson(manifestJson);

        private OrchardReachParams _params;
        private ISessionContext _session;
        private IRandomSource _random;
        private TargetSampler _sampler;
        private AdaptiveStaircase _staircase;
        private double[] _chestReference = { 0, 0, 0 };
        private double _armLengthM = 0.6;

        private int _trialIndex = -1;
        private TrialStateMachine _trial;
        private TargetSpec _currentTarget;
        private PlacedTarget _currentPlacement;
        private bool _running;
        private int _successCount;
        private int _trialsCompletedCount;
        private RollingSuccessTracker _rolling;

        // Distinct from _trialIndex (which is StartNextTrial's "which slot are we on" counter, including the
        // final sentinel call that decides to End()): TrialsCompleted counts trials that have actually
        // FINISHED (success/timeout/dropped), incremented once per OnTrialFinished call. Using _trialIndex + 1
        // here previously overcounted by one at End() (it included the trial that never started) and, worse,
        // undercounted mid-run for any caller polling TrialsCompleted as a "done yet" signal (it flipped to the
        // next trial's count the instant that trial STARTED, not when it finished).
        public int TrialsCompleted => _trialsCompletedCount;
        public int SuccessCount => _successCount;
        public TrialState CurrentTrialState => _trial?.State ?? TrialState.Idle;

        public void Configure(ParamSet parameters, ISessionContext session)
        {
            _params = OrchardReachParams.From(parameters);
            _session = session;
            _random = new SystemRandomSource(seed: 12345);
            _sampler = new TargetSampler(_params, _random);
            if (_params.Adaptive)
                _staircase = new AdaptiveStaircase(_params.ReachPercentMin, _params.ReachPercentMax,
                    (_params.ReachPercentMin + _params.ReachPercentMax) / 2.0);
            // Run15: shared rolling window backs both auto-advance (>=80% over the last 5) and the
            // adaptive-difficulty band (70-85%) -- see RollingSuccessTracker's class doc.
            _rolling = new RollingSuccessTracker(_params.AdaptiveWindow);
            _trialIndex = -1;
            _successCount = 0;
            _trialsCompletedCount = 0;
        }

        /// <summary>Rolling success rate over the last <c>adaptiveWindow</c> trials (0 if no trials yet).
        /// The Shell/OpusSessionRunner reads this for status/metrics_tick's `good_dose`-adjacent fields and for
        /// deciding stage auto-advance; the module itself does not switch stages (a clinician param, not a
        /// runtime side effect the game should apply to itself silently).</summary>
        public double RollingSuccessRate => _rolling?.Rate ?? 0.0;

        /// <summary>True once the rolling window has enough trials to be a stable estimate AND the rate has
        /// reached the brief's auto-advance bar. The Shell checks this after each trial_end when
        /// <see cref="OrchardReachParams.AutoAdvance"/> is on and requests the next stage from the manifest
        /// (this module does not know the stage sequence itself -- that ordering lives with the caller).</summary>
        public bool ReadyToAutoAdvance => _rolling != null && _rolling.IsFull(_params.AdaptiveWindow) && _rolling.Rate >= 0.80;

        /// <summary>Quality-gated "good dose" count (task 2/3): a successful, correct-container placement that
        /// also passed any active quality check (stage C's upright-tilt threshold). Clinician-facing only --
        /// reported in status/metrics_tick, never surfaced on the patient HUD (per the brief, so a patient
        /// never sees a number that reads as being graded). Distinct from SuccessCount: a stage-C placement
        /// that lands in the basket but tips over on release is still a Success outcome (it WAS placed) but
        /// does not add to GoodDoseCount.</summary>
        public int GoodDoseCount { get; private set; }

        /// <summary>Calibration data from the Shell; must be called after Configure and before Begin.</summary>
        public void SetCalibration(double[] chestReference, double armLengthM)
        {
            _chestReference = chestReference;
            _armLengthM = armLengthM;
        }

        public void Begin()
        {
            _running = true;
            Emit("block_start");
            StartNextTrial(_session.Clock.NowMs);
        }

        public void Pause()
        {
            _running = false;
            Emit("pause");
        }

        public void Resume()
        {
            _running = true;
            Emit("resume");
        }

        public void End()
        {
            if (_running) Emit("block_end");
            _running = false;
        }

        private void StartNextTrial(double nowMs)
        {
            _trialIndex++;
            if (_trialIndex >= _params.TrialCount)
            {
                End();
                return;
            }

            double reachOverride = _staircase != null ? _staircase.Current : double.NaN;
            _currentTarget = _sampler.SampleRealTarget(_trialIndex, reachOverride);
            _currentPlacement = TargetPlacement.Place(_chestReference, _armLengthM, _currentTarget.AzimuthDeg, _currentTarget.ElevationDeg, _currentTarget.ReachPercent);
            _trial = new TrialStateMachine(_params.TimeLimitMs, _params.HoldMs);

            Emit("trial_start", hand: _currentTarget.Side);
            var signal = _trial.ShowTarget(nowMs);
            EmitSignal(signal, _currentTarget.Side, target: TargetToTrialTarget());
        }

        private TrialTarget TargetToTrialTarget() => new TrialTarget
        {
            Id = $"trial_{_trialIndex}",
            // Run12: record the target CHEST-RELATIVE, matching the frame the kinematics are recorded in
            // (contracts/fixtures/sessions/healthy uses chest-relative joint positions, e.g. target y ~0.26 for a
            // seated shoulder-height reach). TargetPlacement.Place returns a world point -- writing that straight
            // into the event mixed two frames, and analytics' endpoint_error_cm, which measures hand-vs-target
            // distance, came out at 79-133 cm for reaches that visibly hit the apple. Subtracting the chest
            // reference puts both sides of that subtraction in one frame.
            Pos = SubtractChest(_currentPlacement.ToArray()),
            AzimuthDeg = _currentTarget.AzimuthDeg,
            ElevationDeg = _currentTarget.ElevationDeg,
            ReachPercent = _currentTarget.ReachPercent,
            IsDistractor = false,
        };

        /// <summary>World point -> chest-relative, the frame the contracts record positions in.</summary>
        private double[] SubtractChest(double[] world) => new[]
        {
            world[0] - _chestReference[0],
            world[1] - _chestReference[1],
            world[2] - _chestReference[2],
        };

        /// <summary>Drive the current trial forward one frame. Call from the Shell's per-frame input-update
        /// path (see KinematicsSampler) with the same <see cref="IHandSource"/> used for recording — the game
        /// never talks to OVRSkeleton/Interaction SDK directly.</summary>
        public void Tick(double nowMs, IHandSource hands)
        {
            if (!_running || _trial == null || _trial.State == TrialState.TrialEnded) return;

            string sideStr = _currentTarget.Side;
            var side = sideStr == "left" ? HandSide.Left : HandSide.Right;

            if (_trial.State == TrialState.TargetShown && hands.IsTracked(side) && (MovementOnsetGate?.Invoke(side) ?? true))
            {
                EmitSignal(_trial.OnMovementOnset(nowMs), sideStr);
            }

            if ((_trial.State == TrialState.MovementOnset || _trial.State == TrialState.TargetShown) && IsGraspOverTarget(hands, side)
                && (GraspProximityGate?.Invoke(side) ?? true))
            {
                // Run15 fix (Opus, from real Link session c93ae4fa trial 3): the hand can already be AT the
                // fruit the instant the target appears (a fast/short reach, or the proximity gate satisfied on
                // frame 1) -- in that case the block above never fires (MovementOnsetGate legitimately says "no
                // movement yet", or hands.IsTracked was false for a frame) and this branch reached grasp/contact
                // straight from TargetShown, so movement_onset was NEVER emitted for that trial. Analytics gates
                // every one of its 12 trial metrics on t_movement_onset (the start of the reach window), so that
                // trial's own contact/placed events were real and successful but every metric came back null.
                // Contact/grasp is definitionally "the hand has arrived", i.e. movement already happened, so if
                // we're about to detect it while still in TargetShown, force the onset transition first (same
                // nowMs -- an onset-to-contact time of 0ms is honest here, not synthesized) so movement_onset
                // always precedes contact/grasp in event order, unconditionally, regardless of the gate's answer.
                if (_trial.State == TrialState.TargetShown)
                    EmitSignal(_trial.OnMovementOnset(nowMs), sideStr);
                // Run12: emit "contact" immediately before "grasp". The contracts define contact as the hand
                // reaching the target and grasp as closing on it; this game only ever emitted the latter, and
                // analytics uses t_contact as the end of the reach window -- with it absent, every movement
                // metric (movement time, peak speed, SPARC, LDLJ, submovements, path ratio, endpoint error,
                // trunk lean) returned null with reason "required_event_missing", for every trial of every
                // session this game has ever produced. IsGraspOverTarget is precisely the "hand is at the
                // target" condition, so this is the correct moment for it, not a synthesized approximation.
                if (_trial.State != TrialState.Grasped) Emit("contact", sideStr);
                EmitSignal(_trial.OnGrasp(nowMs), sideStr);

                // Run16 (stage A, reach-to-touch): the brief's whole point for this stage is "touch = success" --
                // no pinch/hold/basket-placement requirement at all, so a patient who can only extend their arm
                // and touch the (large) target still completes a trial. Contact/grasp firing IS the touch here;
                // succeed immediately instead of falling through to the hold-then-basket path every other stage
                // uses. Gated strictly behind Stage=="A_touch" so B/C/D/E are byte-for-byte unaffected.
                if (_params.Stage == "A_touch" && _trial.State == TrialState.Grasped)
                {
                    var placeSignal = _trial.Place(nowMs);
                    if (placeSignal.Outcome == TrialOutcome.Success) GoodDoseCount++; // no quality gate on stage A
                    EmitSignal(placeSignal, sideStr);
                    Emit("trial_end", sideStr, outcome: placeSignal.Outcome.ToSchemaString());
                    OnTrialFinished(placeSignal.Outcome, nowMs);
                    return;
                }
            }

            if (!SceneOwnsRelease && _trial.State == TrialState.Holding && hands.GetPinchStrength(side) < GraspDetector.WholeHandThreshold * 0.5f)
            {
                // Grip relaxed before the scene confirmed a basket placement -> dropped.
                ReleaseGrasp(nowMs, overBasket: false);
                return;
            }

            var tick = _trial.Tick(nowMs);
            if (tick.HasValue)
            {
                EmitSignal(tick, sideStr);
                if (tick.Value.EventType == "trial_end") OnTrialFinished(tick.Value.Outcome, nowMs);
            }
        }

        /// <summary>Real-hands play: extra condition for movement onset (e.g. the hand has actually moved away from
        /// where it was when the target appeared). Null = onset as soon as the hand is tracked (demo/tests).</summary>
        public Func<HandSide, bool> MovementOnsetGate { get; set; }

        /// <summary>Real-hands play: a pinch only counts as a grasp when this says the hand is AT the fruit.
        /// Null = any pinch counts (the synthetic demo driver, whose pinch is already timed to the reach).</summary>
        public Func<HandSide, bool> GraspProximityGate { get; set; }

        /// <summary>When true the scene decides when a held fruit is let go (via <see cref="ReleaseGrasp"/>),
        /// so it can tell "released over the basket" from "dropped". Default false keeps the module's own
        /// grip-relax drop detection.</summary>
        public bool SceneOwnsRelease { get; set; }

        public bool IsRunning => _running;
        public int TrialIndex => _trialIndex;
        public int TrialCount => _params?.TrialCount ?? 0;
        public string CurrentSide => _trial == null ? null : _currentTarget.Side;

        /// <summary>The held fruit was let go. Over the basket = success; anywhere else = dropped. Either way
        /// the trial is closed with a trial_end so analytics can segment it.</summary>
        public void ReleaseGrasp(double nowMs, bool overBasket)
        {
            if (_trial == null || (_trial.State != TrialState.Grasped && _trial.State != TrialState.Holding)) return;
            if (overBasket)
            {
                ConfirmPlacedInBasket(nowMs);
                return;
            }
            string sideStr = _currentTarget.Side;
            EmitSignal(_trial.OnRelease(nowMs, overBasket: false), sideStr);
            // Same gap as run12's success path: the dropped path emitted "release" but never "trial_end", so
            // the trial stayed open to analytics.
            Emit("trial_end", sideStr, outcome: "dropped");
            OnTrialFinished(_trial.Outcome, nowMs);
        }

        /// <summary>Scene calls this when the placed-item collider confirms the target landed in the (single,
        /// non-sorting) basket. Stages A-C and stage D with no rule chosen yet all go through here.</summary>
        public void ConfirmPlacedInBasket(double nowMs) =>
            ConfirmPlacedInContainer(nowMs, containerId: null, fruitColor: null, fruitRipe: true, tiltDeg: null);

        /// <summary>Run15: sorting-aware / precision-aware placement. Scene calls this from whichever
        /// container's trigger fired, with the fruit's own color/ripe tags and (stage C) its measured upright
        /// tilt at the moment of placement. <paramref name="containerId"/>/<paramref name="fruitColor"/> are
        /// null for the plain single-basket case (stages A/B, or D before a rule is picked) -- sorting logic is
        /// then skipped entirely (identical behaviour to the pre-run15 <see cref="ConfirmPlacedInBasket"/>).</summary>
        public void ConfirmPlacedInContainer(double nowMs, string containerId, string fruitColor, bool fruitRipe, double? tiltDeg)
        {
            if (_trial == null) return;

            bool sortingActive = _params.Stage == "D_sorting" && _params.SortRule != "none" && containerId != null;
            bool correct = !sortingActive || SortingRuleEvaluator.IsCorrectContainer(_params.SortRule, containerId, fruitColor, fruitRipe);
            bool qualityCheck = _params.Stage == "C_precision" && tiltDeg.HasValue;
            // "Upright" threshold for stage C's tilt-at-placement check. Not clinically tuned (no reference
            // given in the brief beyond "must end upright"); documented as a first guess, same as run14's
            // bounce constants, for a clinician/Opus pass later.
            const double UprightTiltThresholdDeg = 20.0;
            bool qualityOk = !qualityCheck || Math.Abs(tiltDeg.Value) <= UprightTiltThresholdDeg;

            var data = new System.Collections.Generic.Dictionary<string, object>();
            if (containerId != null) data["container"] = containerId;
            if (fruitColor != null) data["fruit_color"] = fruitColor;
            if (sortingActive) data["correct"] = correct;
            if (tiltDeg.HasValue) data["tilt_deg"] = tiltDeg.Value;
            if (qualityCheck) data["quality_ok"] = qualityOk;
            object dataObj = data.Count > 0 ? data : null;

            if (!correct)
            {
                // Wrong container: never punish. Reward withheld (no "placed" event -- the scene's bloom/chime/
                // haptic success feedback is wired to "placed", not "trial_end"), gentle corrective text is the
                // Shell/HUD's job (task 3/4 of this run), trial_end still closes the trial cleanly for analytics.
                EmitSignal(_trial.EndTrial(nowMs, TrialOutcome.WrongTarget), _currentTarget.Side, data: dataObj);
                OnTrialFinished(TrialOutcome.WrongTarget, nowMs);
                return;
            }

            var signal = _trial.Place(nowMs);
            if (signal.Outcome == TrialOutcome.Success && qualityOk) GoodDoseCount++;
            // Run15 (task 3): variable-ratio bonus. A fixed ~1-in-4 (manifest default) chance a genuine success
            // ALSO gets the bonus sparkle/chime variant -- unpredictable reinforcement schedule, not tied to
            // any particular trial number, using the same seeded IRandomSource the target sampler already uses
            // (so a session is fully reproducible from its seed, same guarantee as target sampling). Never
            // applies to a wrong_target (no reward there at all, see above) or to Miss/Timeout/Dropped.
            if (signal.Outcome == TrialOutcome.Success && _random.NextDouble() < _params.VariableRewardRate)
            {
                data["bonus"] = true;
                dataObj = data;
            }
            EmitSignal(signal, _currentTarget.Side, data: dataObj);
            // Run12: emit trial_end on THIS path too. The timeout and dropped-grip paths both end a trial with a
            // "trial_end" event, but a successful basket placement -- the normal, happy path -- emitted only
            // "placed" and then finished the trial internally. Analytics segments a trial as target_shown ->
            // trial_end, so every successful trial was left unclosed and EVERY metric came back null/invalid
            // (found by running the whole pipeline end to end; the session still passed schema validation, which
            // is why no earlier test caught it). Outcome mirrors the Place signal so success/failure is unchanged.
            Emit("trial_end", _currentTarget.Side, outcome: signal.Outcome.ToSchemaString(), data: dataObj);
            OnTrialFinished(signal.Outcome, nowMs);
        }

        private void OnTrialFinished(TrialOutcome outcome, double nowMs)
        {
            bool success = outcome == TrialOutcome.Success;
            if (success) _successCount++;
            _trialsCompletedCount++;
            _staircase?.RegisterOutcome(success);
            // wrong_target is deliberately NOT counted as a rolling "success", but it also should not sink
            // the adaptive-difficulty/auto-advance rate as hard as a genuine miss/timeout/drop -- the patient
            // DID complete a correct reach-grasp-place motion, they just chose the wrong container. Treated as
            // a plain failure for now (simplest correct behaviour); a softer weighting is a product decision
            // for Opus/clinicians to make, not one this run should invent unilaterally.
            _rolling?.RegisterOutcome(success);
            StartNextTrial(nowMs);
        }

        private bool IsGraspOverTarget(IHandSource hands, HandSide side)
        {
            if (_params.GraspType == "pinch" || _params.GraspType == "any")
                if (GraspDetector.IsPinching(hands, side)) return true;
            if (_params.GraspType == "whole_hand" || _params.GraspType == "any")
                if (GraspDetector.IsWholeHandGrasping(hands, side)) return true;
            return false;
        }

        private void Emit(string type, string hand = null, TrialTarget target = null, string outcome = null, object data = null)
        {
            OnTrialEvent?.Invoke(new TrialEvent
            {
                TMs = _session.Clock.NowMs,
                Block = _session.BlockIndex,
                // Block-level events belong to no trial. Stamping them with _trialIndex wrote trial -1 for
                // block_start (schema minimum is 0) and trial N for block_end, and analytics then counted both
                // as extra "trials" (7 for a 5-trial session, found on the first real runner session).
                Trial = type.StartsWith("block_") || _trialIndex < 0 ? (int?)null : _trialIndex,
                Type = type,
                Hand = hand,
                Target = target,
                Outcome = outcome,
                Data = data,
            });
        }

        private void EmitSignal(TrialSignal? signal, string hand, TrialTarget target = null, object data = null)
        {
            if (!signal.HasValue) return;
            Emit(signal.Value.EventType, hand, target, signal.Value.Outcome.ToSchemaString(), data);
        }
    }
}
