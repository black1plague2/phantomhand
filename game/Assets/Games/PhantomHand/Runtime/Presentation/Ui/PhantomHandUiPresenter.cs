using System;
using System.Collections.Generic;
using Opus.Sdk;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 presenter: calibration outline + hold, drift probe (dark room, ruler, fingertip dot), questionnaire, witness and HUD,
    /// driven by the module's phase. Same frame contract as <see cref="ArmThreatPresenter"/>: the composition root (U5) calls
    /// <see cref="Bind"/> once and <see cref="Tick"/> every frame after haptic Pump and module.Tick. Everything the participant
    /// reads comes from <see cref="PhStrings"/> (EN/HI by <see cref="Lang"/>, default the voiceover_lang param).
    /// No OVRInput: pokes arrive as uGUI clicks through the ISDK PointableCanvas; hands come from <see cref="IHandSource"/>.
    /// </summary>
    public sealed class PhantomHandUiPresenter : MonoBehaviour
    {
        public const double ConfirmDwellMs = 700;

        public PhantomAnchors anchors;
        [Tooltip("Optional: receives SetCalibration(wrist, forearmAxis) when the calibration is confirmed.")]
        public ArmThreatPresenter armPresenter;
        public bool attachPoke = true;
        public string Lang = PhStrings.En;

        public PhInstructionPanel Instruction { get; private set; }
        public PhQuestionnairePanel Questionnaire { get; private set; }
        public PhWitnessPanel Witness { get; private set; }
        public PhHudPanel Hud { get; private set; }
        public HudModel HudData { get; } = new HudModel();
        public PinchToggle Pinch { get; } = new PinchToggle();
        /// <summary>The fingertip press on the questionnaire's buttons (the hands are tracked but not shown, and nothing else presses them).</summary>
        public PhFingerTouch Touch { get; } = new PhFingerTouch();
        public CalibrationTracker Calibration { get; private set; }
        public ProbeState LastProbeState { get; private set; } = ProbeState.Waiting;
        public bool CalibrationCommitted { get; private set; }

        private PhantomHandModule _module;
        private HapticClient _haptic;
        private SleeveSensorClient _nodeA, _nodeB;
        private SessionClock _clock;
        private IHandSource _hands;
        private PhPhase _phase = PhPhase.Idle;
        private bool _bound;
        private HandSide _arm = HandSide.Right, _pointer = HandSide.Left;   // the stimulated arm, and the free hand that points and answers
        private double _confirmAtMs = -1;
        private bool _probeTicked;
        private AudioSource _sfx;
        private LineRenderer _outline;
        private MaterialPropertyBlock _mpb;
        private static readonly Color OutlineTeal = new Color(0.30f, 0.95f, 0.75f, 1f);

        public PhPhase LastPhase { get { return _phase; } }

        public void Bind(PhantomHandModule module, HapticClient haptic, SleeveSensorClient nodeA, SleeveSensorClient nodeB,
                         SessionClock clock, IHandSource hands)
        {
            _module = module; _haptic = haptic; _nodeA = nodeA; _nodeB = nodeB; _clock = clock; _hands = hands;
            if (module.Params != null && (module.Params.VoiceoverLang == PhStrings.Hi || module.Params.VoiceoverLang == PhStrings.En))
                Lang = module.Params.VoiceoverLang;
            if (anchors == null) anchors = GetComponentInParent<PhantomAnchors>();
            if (anchors == null) anchors = FindFirstObjectByType<PhantomAnchors>();
            if (module.Params != null) { _arm = module.Params.Arm; _pointer = module.Params.Pointer; }
            if (anchors != null) anchors.LayOutFor(_arm);
            Camera cam = null;
            if (anchors != null && anchors.cameraRig != null) cam = anchors.cameraRig.GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;

            Instruction = Ensure<PhInstructionPanel>(anchors != null ? anchors.instructionPanel : null, "InstructionPanel", new Vector3(0f, 1.00f, 0.60f), new Vector3(26f, 0f, 0f));
            Questionnaire = Ensure<PhQuestionnairePanel>(anchors != null ? anchors.questionnairePanel : null, "QuestionnairePanel", new Vector3(0f, 1.06f, 0.47f), new Vector3(18f, 0f, 0f));
            Witness = Ensure<PhWitnessPanel>(anchors != null ? anchors.witnessPanel : null, "WitnessPanel", new Vector3(0f, 1.30f, 0.95f), new Vector3(8f, 0f, 0f));
            Hud = Ensure<PhHudPanel>(anchors != null ? anchors.hudPanel : null, "HudPanel", new Vector3((float)PhArm.X(_arm, -0.30), 1.38f, 0.80f), new Vector3(10f, 0f, 0f));
            Instruction.Build(cam);
            Questionnaire.Build(cam, attachPoke);
            Witness.Build(cam, attachPoke);
            Hud.Build(cam);
            Instruction.Hide(); Questionnaire.Hide(); Witness.Hide(); Hud.gameObject.SetActive(false);

            if (anchors != null && anchors.armRestOutline != null) _outline = anchors.armRestOutline.GetComponent<LineRenderer>();
            _mpb = new MaterialPropertyBlock();
            BuildSfx();
            _phase = PhPhase.Idle; _bound = true;
        }

        private T Ensure<T>(Transform existing, string name, Vector3 pos, Vector3 euler) where T : Component
        {
            Transform t = existing;
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
                go.SetActive(false);
                t = go.transform;
            }
            var c = t.GetComponent<T>();
            if (c == null) c = t.gameObject.AddComponent<T>();
            return c;
        }

        private void BuildSfx()
        {
            var parent = anchors != null && anchors.audioRoot != null ? anchors.audioRoot : transform;
            var go = new GameObject("Sfx_Ui");
            go.transform.SetParent(parent, false);
            _sfx = go.AddComponent<AudioSource>();
            _sfx.playOnAwake = false; _sfx.spatialBlend = 0f;
            var sibling = anchors != null && anchors.audioRoot != null ? anchors.audioRoot.Find("Sfx_Thud") : null;
            var sibSrc = sibling != null ? sibling.GetComponent<AudioSource>() : null;
            if (sibSrc != null) _sfx.outputAudioMixerGroup = sibSrc.outputAudioMixerGroup;
        }

        private void PlayTick() { ProceduralSfx.Play(_sfx, ProceduralSfx.Tick(), 0.6f); }

        // ---- per frame -----------------------------------------------------------------------------------------------

        public void Tick()
        {
            if (!_bound) return;
            double now = _clock.NowMs;
            var phase = _module.CurrentPhase;
            if (phase != _phase) { var prev = _phase; _phase = phase; OnPhase(prev, phase, now); }

            switch (phase)
            {
                case PhPhase.Calibrate: TickCalibrate(now); break;
                case PhPhase.ProbePre:
                case PhPhase.ProbePost: TickProbe(now); break;
                case PhPhase.Questionnaire: Questionnaire.Tick(now); TickTouch(now); break;
                case PhPhase.Witness: Witness.Tick(now); break;
            }
            TickHud(now);
        }

        private void OnPhase(PhPhase prev, PhPhase now, double nowMs)
        {
            // leave
            if (prev == PhPhase.Calibrate) { Instruction.Hide(); SetOutline(false, OutlineTeal); }
            if (prev == PhPhase.ProbePre || prev == PhPhase.ProbePost) EndProbeLook();
            if (prev == PhPhase.Questionnaire) { Questionnaire.Hide(); EndTouch(); }
            if (prev == PhPhase.Witness) Witness.Hide();

            // enter
            switch (now)
            {
                case PhPhase.Calibrate:
                    CalibrationCommitted = false; _confirmAtMs = -1;
                    Calibration = new CalibrationTracker(ToArr(CalibrationTarget()));
                    SetOutline(true, OutlineTeal);
                    break;
                case PhPhase.ProbePre:
                case PhPhase.ProbePost:
                    BeginProbeLook();
                    break;
                case PhPhase.Questionnaire:
                    if (_module.CurrentQuestionnaire != null)
                        Questionnaire.Show(_module.CurrentQuestionnaire, v => _module.SubmitQuestionnaireAnswer(v), () => _module.QuestionnaireBack(), Lang, nowMs);
                    break;
                case PhPhase.Witness:
                    if (_module.Witness != null) Witness.Show(_module.Witness, Lang, nowMs);
                    break;
            }
        }

        // ---- calibration (item 1) --------------------------------------------------------------------------------------

        /// <summary>World position where the real wrist of the stimulated arm should rest: the outline's wrist end, wrist height above the table.</summary>
        public Vector3 CalibrationTarget()
        {
            float forearm = (float)(_module.Params.ForearmLengthCm / 100.0);
            if (anchors != null && anchors.armRestOutline != null)
            {
                var o = anchors.armRestOutline;
                return o.position + o.forward * forearm + Vector3.up * 0.021f;
            }
            return new Vector3((float)PhArm.X(_arm, 0.18), 0.771f, 0.40f);
        }

        private void TickCalibrate(double now)
        {
            if (CalibrationCommitted || Calibration == null) return;
            double[] wrist = null, palm = null;
            bool tracked = _hands != null && _hands.IsTracked(_arm) &&
                           _hands.TryGetJointPose(PhArm.Wrist(_arm), out wrist, out _);
            if (tracked) _hands.TryGetJointPose(PhArm.Palm(_arm), out palm, out _);
            var fallback = anchors != null && anchors.armRestOutline != null ? ToArr(anchors.armRestOutline.forward) : null;
            var st = Calibration.Update(now, tracked, wrist, palm, fallback);

            string title = PhStrings.Get("calib_title", Lang);
            if (st == CalibState.Confirmed)
            {
                if (_confirmAtMs < 0) { _confirmAtMs = now; PlayTick(); }
                SetOutline(true, PhUiKit.Good);
                Instruction.Show(title, PhStrings.Get("calib_done", Lang), 1f, PhUiKit.Good, true);
                if (now - _confirmAtMs >= ConfirmDwellMs) CommitCalibration();
                return;
            }
            float pulse = 0.65f + 0.35f * Mathf.Sin((float)(now / 1000.0) * 4f);
            SetOutline(true, OutlineTeal * pulse);
            if (st == CalibState.Holding)
                Instruction.Show(title, PhStrings.Get("calib_holding", Lang), (float)Calibration.Progress01, PhUiKit.Info, true);
            else
                Instruction.Show(title, tracked ? "" : PhStrings.Get("calib_lost", Lang), 0f, tracked ? PhUiKit.Info : PhUiKit.Warn, true);
        }

        private void CommitCalibration()
        {
            var w = Calibration.Wrist; var a = Calibration.Axis;
            var wv = new Vector3((float)w[0], (float)w[1], (float)w[2]);
            var av = new Vector3((float)a[0], (float)a[1], (float)a[2]);
            if (armPresenter != null) armPresenter.SetCalibration(wv, av);
            CalibrationCommitted = true;
            _module.SubmitCalibration(w, a, true);
        }

        private void SetOutline(bool on, Color c)
        {
            if (anchors != null && anchors.armRestOutline != null) anchors.armRestOutline.gameObject.SetActive(on);
            if (!on || _outline == null) return;
            _mpb.SetColor("_BaseColor", c); _mpb.SetColor("_Color", c);
            _outline.SetPropertyBlock(_mpb);
        }

        // ---- drift probe (item 2) ---------------------------------------------------------------------------------------

        private void BeginProbeLook()
        {
            LastProbeState = ProbeState.Waiting; _probeTicked = false;
            if (anchors != null)
            {
                if (anchors.darken != null) anchors.darken.SetDark(true);
                if (anchors.probeRuler != null) anchors.probeRuler.gameObject.SetActive(true);
                var tip = anchors.leftIndexDot != null ? anchors.leftIndexDot.GetComponent<TipDot>() : null;
                if (tip != null) { tip.target = null; tip.SetVisible(false); }
            }
            SetOutline(false, OutlineTeal);
            Instruction.Show(PhStrings.ProbeInstruction(Lang), "", 0f, PhUiKit.Info, false);
        }

        private void EndProbeLook()
        {
            if (anchors != null)
            {
                if (anchors.darken != null) anchors.darken.SetDark(false);
                if (anchors.probeRuler != null) anchors.probeRuler.gameObject.SetActive(false);
                var tip = anchors.leftIndexDot != null ? anchors.leftIndexDot.GetComponent<TipDot>() : null;
                if (tip != null) tip.SetVisible(false);
            }
            SetOutline(false, OutlineTeal);
            Instruction.Hide();
        }

        private void TickProbe(double now)
        {
            // "left" is the pointing hand and "right" the stimulated arm here, as in the right-arm layout these names come from
            double[] leftTip = null, rightWrist = null, rightIdx = null;
            bool leftTracked = _hands != null && _hands.IsTracked(_pointer) && _hands.TryGetJointPose(PhArm.IndexTip(_pointer), out leftTip, out _);
            bool rightTracked = _hands != null && _hands.IsTracked(_arm);
            if (rightTracked) { _hands.TryGetJointPose(PhArm.Wrist(_arm), out rightWrist, out _); _hands.TryGetJointPose(PhArm.IndexTip(_arm), out rightIdx, out _); }

            // the dot rides the tracked index tip of the pointing hand (the hand mesh itself stays invisible)
            var dotT = anchors != null ? anchors.leftIndexDot : null;
            var tip = dotT != null ? dotT.GetComponent<TipDot>() : null;
            if (tip != null)
            {
                tip.SetVisible(leftTracked);
                if (leftTracked) dotT.position = new Vector3((float)leftTip[0], (float)leftTip[1], (float)leftTip[2]);
            }

            var st = _module.FeedProbe(now, leftTracked, leftTip, rightWrist, rightIdx != null ? (double?)rightIdx[0] : null);
            LastProbeState = st;
            string title = PhStrings.ProbeInstruction(Lang);
            switch (st)
            {
                case ProbeState.Confirmed:
                    if (!_probeTicked) { _probeTicked = true; PlayTick(); }
                    Instruction.Show(title, PhStrings.Get("probe_done", Lang), 1f, PhUiKit.Good, false);
                    break;
                case ProbeState.Holding:
                    SetOutline(false, OutlineTeal);
                    Instruction.Show(title, PhStrings.Get("probe_holding", Lang), (float)_module.Probe.Progress01, PhUiKit.Info, false);
                    break;
                case ProbeState.ArmMoved:
                    // the room stays dark; the outline is shown again so the participant can put the arm back (probe paused)
                    SetOutline(true, PhUiKit.Warn);
                    Instruction.Show(PhStrings.Get("probe_arm_moved", Lang), "", 0f, PhUiKit.Warn, false);
                    break;
                default:
                    SetOutline(false, OutlineTeal);
                    Instruction.Show(title, "", 0f, PhUiKit.Info, false);
                    break;
            }
        }

        // ---- fingertip press on the questionnaire ----------------------------------------------------------------------------

        private readonly List<Button> _touchButtons = new List<Button>();
        private Button _touchShown;

        private Vector3? IndexTip(HandSide side)
        {
            double[] p;
            if (_hands == null || !_hands.IsTracked(side) || !_hands.TryGetJointPose(PhArm.IndexTip(side), out p, out _)) return null;
            return new Vector3((float)p[0], (float)p[1], (float)p[2]);
        }

        /// <summary>The index finger of either hand presses the scale buttons and Back; the dot of the probes shows where the
        /// fingertip is (on the free hand when it is tracked, else on the other), because no hand is drawn.</summary>
        private void TickTouch(double now)
        {
            _touchButtons.Clear();
            _touchButtons.AddRange(Questionnaire.Buttons);
            if (Questionnaire.BackButton != null) _touchButtons.Add(Questionnaire.BackButton);
            Vector3? free = IndexTip(_pointer), other = IndexTip(_arm);
            if (Touch.Tick(now, _touchButtons, free, other) != null) PlayTick();

            if (_touchShown != Touch.Hovered)
            {
                if (_touchShown != null) _touchShown.transform.localScale = Vector3.one;
                _touchShown = Touch.Hovered;
            }
            if (_touchShown != null) _touchShown.transform.localScale = Vector3.one * (1.06f + 0.10f * Touch.Progress01);   // grows while the finger dwells

            var dotT = anchors != null ? anchors.leftIndexDot : null;
            var dot = dotT != null ? dotT.GetComponent<TipDot>() : null;
            if (dot != null)
            {
                Vector3? tip = free ?? other;
                dot.SetVisible(tip.HasValue);
                if (tip.HasValue) dotT.position = tip.Value;
            }
        }

        private void EndTouch()
        {
            if (_touchShown != null) _touchShown.transform.localScale = Vector3.one;
            _touchShown = null; Touch.Reset();
            var dotT = anchors != null ? anchors.leftIndexDot : null;
            var dot = dotT != null ? dotT.GetComponent<TipDot>() : null;
            if (dot != null) dot.SetVisible(false);
        }

        // ---- HUD (item 5) ----------------------------------------------------------------------------------------------

        private void TickHud(double now)
        {
            float ls = _hands != null ? _hands.GetPinchStrength(HandSide.Left) : 0f;
            float rs = _hands != null ? _hands.GetPinchStrength(HandSide.Right) : 0f;
            Pinch.Update(now, ls, rs);

            var m = HudData;
            m.Lang = Lang; m.Phase = _phase; m.Condition = _module.CurrentCondition; m.RemainingS = _module.RemainingS(now);
            m.AgencyNow = _phase == PhPhase.Agency && _module.Agency != null ? (AgencyStep?)_module.Agency.Step : null;   // the caption names the step
            m.HapticConnected = _haptic != null && _haptic.Connected;
            m.BioConnected = _nodeB != null && _nodeB.Connected;
            m.EmgLevel01 = _nodeB != null ? _nodeB.EmgLevel01 : 0.0;
            m.StrokeCount = _module.CurrentStrokes.Count;
            m.SpectatorVisible = Pinch.On;

            bool dark = _phase == PhPhase.ProbePre || _phase == PhPhase.ProbePost;
            bool show = _phase != PhPhase.Idle && _phase != PhPhase.Done && _phase != PhPhase.Witness && !dark;
            if (Hud.gameObject.activeSelf != show) Hud.gameObject.SetActive(show);
            if (show) Hud.Apply(m, (float)Pinch.Progress01);
        }

        private static double[] ToArr(Vector3 v) { return new double[] { v.x, v.y, v.z }; }
    }
}
