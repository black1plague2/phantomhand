using System;
using System.Collections.Generic;
using Opus.Sdk;

namespace Opus.Games.PhantomHand
{
    /// <summary>One brush stroke as actually performed, reported by the presenter (U3) with MEASURED pass times.</summary>
    public sealed class StrokeRecord
    {
        public int Index;
        public double PassAMs, PassBMs;
        public double? CueASendMs, CueBSendMs;
        public bool Swapped;
        /// <summary>Optional override; when null and the condition is SYNC it is computed from sends, lead and passes.</summary>
        public double? TimingErrMs;
    }

    /// <summary>
    /// IGameModule for Phantom Hand: owns the phase walk, the stroke plan, the probes, the questionnaire and the witness
    /// summary, and emits every 03-SPEC section 7 event (trial = condition index; block-level and finale events have a null
    /// trial). No MonoBehaviour/OVR dependency: the scene controller (U5) calls Tick every frame and the presenters
    /// (U3/U4) call the Submit* methods with what they measured. Submit* calls made in the wrong phase are ignored
    /// (return false), so a late packet can never corrupt the record.
    /// </summary>
    [GameModule("phantom_hand")]
    public sealed class PhantomHandModule : IGameModule
    {
        public GameManifest Manifest { get; private set; }
        public event Action<TrialEvent> OnTrialEvent;

        public PhantomHandParams Params { get; private set; }
        public PhaseStateMachine Machine { get; private set; }
        public DriftProbe Probe { get; private set; } = new DriftProbe();
        public Questionnaire CurrentQuestionnaire { get; private set; }
        public WitnessSummary Witness { get; private set; }
        public ConditionResult[] Results { get; private set; } = new ConditionResult[2];

        /// <summary>Strokes planned for the current induction (absolute session ms). Empty outside induction.</summary>
        public IReadOnlyList<StrokePlan> CurrentStrokes { get { return _strokes; } }
        public double InductionStartMs { get; private set; }

        /// <summary>A5: the agency phase of this run (set when the phase starts, in the last condition only). The presenter ticks it with the muscle level.</summary>
        public AgencyRun Agency { get; private set; }

        /// <summary>Set true when the A2/A3 additions start (after gate G2); inserts SelfTouch/Dissolve/Reveal phases.</summary>
        public bool AdditionsEnabled;

        public int Seed { get; private set; } = 12345;
        public void SetSeed(int seed) { Seed = seed; }

        public PhPhase CurrentPhase { get { return Machine == null ? PhPhase.Idle : Machine.Phase; } }
        public PhCondition? CurrentCondition { get { return Machine == null ? null : Machine.Condition; } }
        public int? CurrentConditionIndex { get { return Machine == null ? null : Machine.ConditionIndex; } }
        /// <summary>Condition whose stroke plan is running: the current one, or the last one while the arm dissolves (the current condition and trial are null there).</summary>
        public PhCondition? StrokeCondition { get { return CurrentCondition ?? (CurrentPhase == PhPhase.Dissolve ? LastCondition : (PhCondition?)null); } }
        public bool IsRunning { get { return _running; } }

        private ISessionContext _session;
        private bool _submitting;
        private List<StrokePlan> _strokes = new List<StrokePlan>();
        private bool _running;
        private bool _blockStarted;
        private bool _revealReported;
        private int? _trialForEvents;

        public void LoadManifest(string manifestJson) { Manifest = GameManifest.FromJson(manifestJson); }

        public void Configure(ParamSet parameters, ISessionContext session)
        {
            Params = PhantomHandParams.From(parameters);
            Probe.TowardVirtualSignX = Params.Arm == HandSide.Left ? 1 : -1;   // the virtual hand lies toward the body's midline
            _session = session;
            Machine = new PhaseStateMachine(Params, AdditionsEnabled);
            Machine.PhaseChanged += OnPhaseChanged;
            Results = new ConditionResult[2];
            _strokes = new List<StrokePlan>();
            Witness = null;
            CurrentQuestionnaire = null;
            _running = false;
            _blockStarted = false;
            _revealReported = false;
            Agency = null;
        }

        /// <summary>Operator "set_condition_order": only before the first induction. Returns false when locked or unknown.</summary>
        public bool SetConditionOrder(string order)
        {
            if (Machine == null || (order != "sync_first" && order != "async_first")) return false;
            try { Machine.SetConditionOrder(order == "sync_first" ? PhCondition.Sync : PhCondition.Async); }
            catch (InvalidOperationException) { return false; }
            return true;
        }

        public void Begin()
        {
            if (Machine == null) throw new InvalidOperationException("Configure() must be called before Begin()");
            _running = true;
            _blockStarted = true;
            Emit("block_start", null);
            Machine.Start(Now);
        }

        public void Pause()
        {
            if (!_running) return;
            Machine.Pause(Now);
            _running = false;
            Emit("pause", _trialForEvents);
        }

        public void Resume()
        {
            if (_running || Machine == null || !Machine.IsPaused) return;
            Machine.Resume(Now);
            _running = true;
            Emit("resume", _trialForEvents);
        }

        public void End()
        {
            if (Machine == null || !_blockStarted) { _running = false; return; }
            if (!Machine.IsDone) Machine.Abort(Now); // OnPhaseChanged(Done) emits block_end
            _running = false;
        }

        /// <summary>Call every frame with the session clock.</summary>
        public void Tick(double nowMs)
        {
            if (!_running || Machine == null) return;
            Machine.Tick(nowMs);
        }

        /// <summary>Operator next phase (phase_next command).</summary>
        public bool AdvancePhase() { return _running && Machine.Advance(Now); }

        /// <summary>Operator abort_phase: same as Advance but explicit about intent; the phase is recorded as not confirmed.</summary>
        public bool AbortPhase() { return AdvancePhase(); }

        public double RemainingS(double nowMs) { return Machine == null ? 0 : Machine.RemainingMs(nowMs) / 1000.0; }

        private double Now { get { return _session.Clock.NowMs; } }

        private PhCondition LastCondition { get { return Machine.Order[Machine.Order.Count - 1]; } }

        // ---- submissions ------------------------------------------------------------------------------------------

        public bool SubmitCalibration(double[] wristPos, double[] forearmAxis, bool ok)
        {
            if (!_running || CurrentPhase != PhPhase.Calibrate) return false;
            Emit("calibration", null, new { wrist_pos = wristPos, forearm_axis = forearmAxis, ok = ok });
            if (ok)
            {
                Probe.SetRightReference(wristPos);
                _submitting = true;
                try { Machine.Complete(Now, true); } finally { _submitting = false; }
            }
            return true;
        }

        /// <summary>Feeds the drift probe for the current frame; auto-submits the result the moment it is confirmed.</summary>
        public ProbeState FeedProbe(double nowMs, bool leftTracked, double[] leftTip, double[] rightWrist, double? rightIndexXm)
        {
            if (!_running || (CurrentPhase != PhPhase.ProbePre && CurrentPhase != PhPhase.ProbePost)) return ProbeState.Waiting;
            var st = Probe.Update(nowMs, leftTracked, leftTip, rightWrist);
            if (st == ProbeState.Confirmed)
                SubmitProbe(Probe.Result(CurrentPhase == PhPhase.ProbePre ? "pre" : "post", rightIndexXm));
            return st;
        }

        public bool SubmitProbe(ProbeResult r)
        {
            var phase = CurrentPhase;
            if (!_running || (phase != PhPhase.ProbePre && phase != PhPhase.ProbePost)) return false;
            if ((phase == PhPhase.ProbePre) != (r.When == "pre")) return false;
            EmitProbe(r);
            _submitting = true;
            try { Machine.Complete(Now, r.Confirmed); } finally { _submitting = false; }
            return true;
        }

        private void EmitProbe(ProbeResult r)
        {
            int ci = _trialForEvents ?? 0;
            var res = ResultFor(ci);
            double? drift = r.Confirmed ? r.DriftCm : null;
            if (r.When == "pre") res.PreDriftCm = drift; else res.PostDriftCm = drift;
            Emit("drift_probe", _trialForEvents, new
            {
                when = r.When,
                perceived_x = r.PerceivedXm,
                actual_x = r.ActualXm,
                drift_cm = r.DriftCm,
                confirmed = r.Confirmed,
            });
        }

        public bool SubmitStroke(StrokeRecord s)
        {
            var p = CurrentPhase;
            if (!_running || (p != PhPhase.Induction && p != PhPhase.SelfTouch && p != PhPhase.Dissolve)) return false;
            double? err = s.TimingErrMs;
            if (!err.HasValue && StrokeCondition == PhCondition.Sync)
                err = StrokeTimingErrMs(s.CueASendMs, s.CueBSendMs, s.PassAMs, s.PassBMs, Params.TactileLeadMs);
            Emit("stroke", _trialForEvents, new
            {
                index = s.Index,
                brush_pass_a_ms = s.PassAMs,
                brush_pass_b_ms = s.PassBMs,
                cue_a_send_ms = s.CueASendMs,
                cue_b_send_ms = s.CueBSendMs,
                swapped = s.Swapped,
                timing_err_ms = err,
            });
            return true;
        }

        /// <summary>SYNC timing error: mean of (send + lead - measured pass) over motors A and B; null if a send is missing.</summary>
        public static double? StrokeTimingErrMs(double? sendA, double? sendB, double passA, double passB, double leadMs)
        {
            if (!sendA.HasValue || !sendB.HasValue) return null;
            return ((sendA.Value + leadMs - passA) + (sendB.Value + leadMs - passB)) / 2.0;
        }

        public bool SubmitThreatImpact(double impactMs, double[] impactPos, bool ok)
        {
            if (!_running || CurrentPhase != PhPhase.Threat) return false;
            Emit("threat_impact", _trialForEvents, new { impact_ms = impactMs, impact_pos = impactPos, ok = ok });
            return true;
        }

        public bool SubmitThreatResponse(ThreatResponse r)
        {
            if (!_running || CurrentPhase != PhPhase.Threat) return false;
            ResultFor(_trialForEvents ?? 0).Threat = r;
            Emit("threat_response", _trialForEvents, new
            {
                wrist_peak_mps = r.WristPeakMps,
                wrist_latency_ms = r.WristLatencyMs,
                imu_peak = r.ImuPeak,
                emg_peak_x = r.EmgPeakX,
                emg_latency_ms = r.EmgLatencyMs,
                quality = r.OverallQuality(),   // one value, like the schema and the fixtures; per-stream flags stay on r.Quality
            });
            return true;
        }

        /// <summary>
        /// A2 reveal: the real hand is shown (`fallback` = the arm slides onto the tracked hand instead of passthrough). Emits
        /// passthrough_on {fallback}; once per run, only in the Reveal phase.
        /// </summary>
        public bool SubmitReveal(bool fallback)
        {
            if (!_running || CurrentPhase != PhPhase.Reveal || _revealReported) return false;
            _revealReported = true;
            Emit("passthrough_on", _trialForEvents, new { fallback = fallback });
            return true;
        }

        public bool SubmitEmgBurst(double peak, double baselineRms, double deviceMs)
        {
            if (!_running) return false;
            Emit("emg_burst", _trialForEvents, new { peak = peak, baseline_rms = baselineRms, device_ms = deviceMs });
            return true;
        }

        /// <summary>Answer the current questionnaire item (-3..+3 on screen). Emits questionnaire_item with the contract's 1..7 value
        /// (event.schema.json, likert_1_7 in analytics; see <see cref="Questionnaire.ContractValue"/>); completes the phase after the last item.</summary>
        public bool SubmitQuestionnaireAnswer(int value)
        {
            if (!_running || CurrentPhase != PhPhase.Questionnaire || CurrentQuestionnaire == null) return false;
            string id = CurrentQuestionnaire.Answer(value);
            if (id == null) return false;
            Emit("questionnaire_item", _trialForEvents, new { item = id, value = Questionnaire.ContractValue(value) });
            if (CurrentQuestionnaire.IsComplete) Machine.Complete(Now, true);
            return true;
        }

        public bool QuestionnaireBack()
        {
            return _running && CurrentPhase == PhPhase.Questionnaire && CurrentQuestionnaire != null && CurrentQuestionnaire.Back();
        }

        // ---- phase handling ---------------------------------------------------------------------------------------

        private void OnPhaseChanged(PhaseChange c)
        {
            // 1. close out the phase that just ended
            if (!c.PreviousConfirmed && !_submitting)
            {
                if (c.Previous == PhPhase.Calibrate)
                    Emit("calibration", null, new { wrist_pos = (double[])null, forearm_axis = (double[])null, ok = false });
                else if (c.Previous == PhPhase.ProbePre || c.Previous == PhPhase.ProbePost)
                    EmitProbe(new ProbeResult { When = c.Previous == PhPhase.ProbePre ? "pre" : "post", Confirmed = false });
            }
            if (c.Previous == PhPhase.Questionnaire) FinishQuestionnaire();

            // 2. enter the new phase
            _trialForEvents = c.TrialIndex;
            if (c.Phase == PhPhase.Done)
            {
                _strokes = new List<StrokePlan>();
                if (_blockStarted)
                    Emit("block_end", null, new { aborted = Machine.Aborted });
                _running = false;
                return;
            }

            Emit("phase_start", c.TrialIndex, new
            {
                phase = PhNames.Of(c.Phase),
                condition = c.Condition.HasValue ? PhNames.Of(c.Condition.Value) : null,
            });

            switch (c.Phase)
            {
                case PhPhase.Induction:
                    InductionStartMs = c.StartMs;
                    _strokes = new StrokeScheduler(Params, Seed).Plan(c.Condition.Value, c.StartMs, Machine.InductionPhaseMs);
                    break;
                case PhPhase.Agency:
                    Agency = new AgencyRun(c.StartMs, Params.EmgThreshold, Params.AutonomousCloseEnabled);
                    Agency.OnAutonomousClose += level => Emit("autonomous_close", _trialForEvents, new { emg_level = level });
                    break;
                case PhPhase.Dissolve:
                    // the brush and the motors go on for the whole phase, with the LAST condition's timing (the arm that dissolves)
                    Emit("dissolve_start", c.TrialIndex, new { condition = PhNames.Of(LastCondition) });
                    _strokes = new StrokeScheduler(Params, Seed).Plan(LastCondition, c.StartMs, PhaseStateMachine.DissolveMs);
                    break;
                case PhPhase.ProbePre:
                case PhPhase.ProbePost:
                    Probe.Begin();
                    break;
                case PhPhase.Questionnaire:
                    CurrentQuestionnaire = new Questionnaire(BuildItems());
                    break;
                case PhPhase.Witness:
                    BuildWitness();
                    break;
            }
        }

        private List<QItem> BuildItems()
        {
            bool last = Machine.ConditionIndex == Machine.Order.Count - 1;
            // demo_mode keeps it short: q1 after the first condition; q1 plus the pointers after the last one
            var items = Params.DemoMode ? new List<QItem> { Questionnaire.Q1 } : Questionnaire.DefaultItems();
            if (Machine.AdditionsEnabled && Params.VoiceoverEnabled && (last || !Params.DemoMode)) items.Add(Questionnaire.Q4);
            if (HandClosedByItself && last) items.Add(Questionnaire.Q5);   // q5 only after the hand really closed by itself, in the last condition
            return items;
        }

        /// <summary>A5 ran: the agency phase had the autonomous close on and the hand closed by itself at least once.</summary>
        private bool HandClosedByItself { get { return Agency != null && Agency.AutonomousEnabled && Agency.AutonomousCloses > 0; } }

        private void FinishQuestionnaire()
        {
            if (CurrentQuestionnaire == null) return;
            int ci = _trialForEvents ?? 0;
            var r = ResultFor(ci);
            r.Ownership = CurrentQuestionnaire.Ownership;
            r.Control = CurrentQuestionnaire.Control;
            r.Awareness = CurrentQuestionnaire.Awareness;
            r.Agency = CurrentQuestionnaire.RoleMean(QRole.Agency);
            // demo_mode asks q1 only after the first condition, so there Ownership is q1 and Control is null
        }

        private ConditionResult ResultFor(int conditionIndex)
        {
            if (Results[conditionIndex] == null)
                Results[conditionIndex] = new ConditionResult { Condition = Machine.Order[conditionIndex] };
            return Results[conditionIndex];
        }

        private void BuildWitness()
        {
            ConditionResult sync = null, async = null;
            for (int i = 0; i < 2; i++)
            {
                var r = ResultFor(i);
                if (r.Condition == PhCondition.Sync) sync = r; else async = r;
            }
            Witness = WitnessSummary.Build(sync, async);
            Witness.ConditionOrder = Machine.Order;
            if (HandClosedByItself)
            {
                Witness.AgencyRan = true;
                Witness.DrivenCloses = Agency.DrivenCloses;
                Witness.AutonomousCloses = Agency.AutonomousCloses;
            }
            Emit("witness_summary", null, Witness.ToEventData());
        }

        private void Emit(string type, int? trial, object data = null)
        {
            var h = OnTrialEvent;
            if (h == null) return;
            h(new TrialEvent
            {
                TMs = _session.Clock.NowMs,
                Block = _session.BlockIndex,
                Trial = trial,
                Type = type,
                Data = data,
            });
        }
    }
}
