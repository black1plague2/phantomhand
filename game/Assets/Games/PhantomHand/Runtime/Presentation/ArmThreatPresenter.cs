using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U3 presenter: owns the virtual arm, brush, stroke driver, threat drop and response collector and drives them from the
    /// module's phase. The composition root (U5) calls <see cref="Bind"/> once and <see cref="Tick"/> every frame AFTER
    /// haptic Pump and module.Tick (frame order: clients Pump -> module Tick -> presenters -> recorders).
    /// Arm visibility: Calibrate (lies where the real arm is, no offset, so the participant sees the arm they are placing in the
    /// outline; at table height whatever the height of the real surface), Induction/SelfTouch/Agency/Threat/Dissolve (offset, frozen
    /// except Calibrate/Agency or follow_during_induction; the Dissolve arm fades out after 4 s while the brush goes on);
    /// Reveal (fades in and slides onto the real wrist, no passthrough in this build); hidden for probes, questionnaire and witness.
    /// </summary>
    public sealed class ArmThreatPresenter : MonoBehaviour
    {
        public VirtualArmRig arm;
        public BrushRig brush;
        public ThreatDrop threat;
        public PhantomAnchors anchors;
        [Tooltip("Seconds after the Threat phase starts before the 0.6 s telegraph begins.")]
        public float threatLeadInS = 0.5f;

        /// <summary>A2 dissolve: the arm stays fully visible for DissolveHoldS seconds, then fades 1 to 0 over DissolveFadeS; the brush keeps stroking.</summary>
        public const double DissolveHoldS = 4, DissolveFadeS = 3;
        /// <summary>A2 reveal (fallback, no passthrough): the arm fades in over RevealFadeInS and slides onto the tracked real hand over RevealSlideS.</summary>
        public const double RevealFadeInS = 1, RevealSlideS = 3;

        public StrokeDriver Driver { get; private set; }
        public ThreatResponseCollector Collector { get; private set; }
        public PhPhase LastPhase { get { return _phase; } }
        public bool HasCalibration { get { return _hasCalib; } }

        private PhantomHandModule _module;
        private HapticClient _haptic;
        private SessionClock _clock;
        private IHandSource _hands;
        private PhPhase _phase = PhPhase.Idle;
        private bool _hasCalib, _bound;
        private HandSide _side = HandSide.Right;   // the stimulated arm
        private Vector3 _calibWrist, _calibAxis = Vector3.forward;
        private double _threatAtMs = -1;
        // A5 agency: where the muscle level comes from
        private SleeveSensorClient _nodeB;
        private AgencyStep _agencyStep = AgencyStep.Rest;
        private bool _restOk, _emgReady;
        private FlexionCalibration _flexion = new FlexionCalibration();
        private const double RestSettleS = 1.0, RestWindowMs = 3000, SqueezeWindowMs = 4000;

        public void Bind(PhantomHandModule module, HapticClient haptic, SleeveSensorClient nodeA, SleeveSensorClient nodeB, SessionClock clock, IHandSource hands)
        {
            _module = module; _haptic = haptic; _clock = clock; _hands = hands; _nodeB = nodeB;
            var p = module.Params;
            _side = p.Arm;
            if (anchors != null) anchors.LayOutFor(_side);
            if (arm == null) { arm = new GameObject("VirtualArm").AddComponent<VirtualArmRig>(); arm.transform.SetParent(transform, false); }
            arm.LeftArm = _side == HandSide.Left;
            if (brush == null) { brush = new GameObject("BrushRig").AddComponent<BrushRig>(); brush.transform.SetParent(transform, false); }
            if (threat == null) { threat = new GameObject("ThreatDrop").AddComponent<ThreatDrop>(); threat.transform.SetParent(transform, false); }
            arm.Build(p);
            brush.Build();
            threat.Build();
            arm.Visible = false; brush.Visible = false; brush.Active = false;
            WireSfx();
            Driver = new StrokeDriver(module, haptic, brush);
            Collector = new ThreatResponseCollector(
                () => nodeA != null ? nodeA.Imu : null,
                () => nodeB != null ? nodeB.Emg : null);
            threat.RealHandPosition = RealPalm;
            threat.OnImpact -= OnImpact;
            threat.OnImpact += OnImpact;
            _phase = PhPhase.Idle; _bound = true;
        }

        /// <summary>Calibrated real wrist of the stimulated arm and its forearm axis (elbow toward fingers), from the calibration phase (U4).</summary>
        public void SetCalibration(Vector3 wrist, Vector3 forearmAxis)
        {
            _calibWrist = wrist; _calibAxis = forearmAxis; _hasCalib = true;
        }

        private void WireSfx()
        {
            if (anchors == null || anchors.audioRoot == null) return;
            brush.swishSource = SourceOf("Sfx_Swish");
            threat.thudSource = SourceOf("Sfx_Thud");
            threat.creakSource = SourceOf("Sfx_Creak");
        }

        private AudioSource SourceOf(string n)
        {
            var t = anchors.audioRoot.Find(n);
            return t != null ? t.GetComponent<AudioSource>() : null;
        }

        /// <summary>Arm opacity tSec seconds into the dissolve. Pure.</summary>
        public static float DissolveAlpha(double tSec)
        {
            double a = 1.0 - (tSec - DissolveHoldS) / DissolveFadeS;
            return (float)(a < 0 ? 0 : a > 1 ? 1 : a);
        }

        /// <summary>Arm opacity tSec seconds into the reveal: 0 to 1 over RevealFadeInS. Pure.</summary>
        public static float RevealAlpha(double tSec)
        {
            double a = tSec / RevealFadeInS;
            return (float)(a < 0 ? 0 : a > 1 ? 1 : a);
        }

        /// <summary>Lateral offset (cm) tSec seconds into the reveal: offset_cm eased to 0 (smoothstep) over RevealSlideS, 0 after. Pure.</summary>
        public static double RevealOffsetCm(double tSec, double offsetCm)
        {
            double u = tSec / RevealSlideS;
            u = u < 0 ? 0 : u > 1 ? 1 : u;
            return offsetCm * (1.0 - u * u * (3.0 - 2.0 * u));
        }

        private Vector3? RealPalm()
        {
            double[] pos, rot;
            if (_hands != null && _hands.IsTracked(_side) && _hands.TryGetJointPose(PhArm.Palm(_side), out pos, out rot))
                return new Vector3((float)pos[0], (float)pos[1], (float)pos[2]);
            return null;
        }

        private bool RealWrist(out Vector3 p)
        {
            double[] pos, rot;
            if (_hands != null && _hands.IsTracked(_side) && _hands.TryGetJointPose(PhArm.Wrist(_side), out pos, out rot))
            { p = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]); return true; }
            p = default(Vector3); return false;
        }

        /// <summary>The height of a wrist that rests on the virtual table.</summary>
        private float TableWristY()
        {
            return anchors != null && anchors.armRestOutline != null ? anchors.armRestOutline.position.y + 0.021f : 0.771f;
        }

        /// <summary>The real forearm's direction on the table plane (wrist toward palm), or the outline's when the palm is not tracked.</summary>
        private Vector3 RealAxis(Vector3 wrist)
        {
            Vector3? palm = RealPalm();
            if (palm.HasValue)
            {
                Vector3 a = palm.Value - wrist; a.y = 0f;
                if (a.sqrMagnitude > 0.03f * 0.03f) return a.normalized;
            }
            return anchors != null && anchors.armRestOutline != null ? anchors.armRestOutline.forward : Vector3.forward;
        }

        // ---- per frame -----------------------------------------------------------------------------------------------

        public void Tick()
        {
            if (!_bound) return;
            double now = _clock.NowMs;
            Vector3 w;
            if (RealWrist(out w)) Collector.PushWrist(now, w, _hands.GetDataVersion(_side));

            var phase = _module.CurrentPhase;
            if (phase != _phase) { var prev = _phase; _phase = phase; OnPhase(prev, phase, now); }

            switch (phase)
            {
                case PhPhase.Calibrate:
                    if (RealWrist(out w)) arm.Follow(new Vector3(w.x, TableWristY(), w.z), RealAxis(w), 0f);
                    break;
                case PhPhase.Agency:
                    if (RealWrist(out w)) arm.Follow(w, _hasCalib ? _calibAxis : Vector3.forward, (float)_module.Params.OffsetCm);
                    TickAgency(now);
                    break;
                case PhPhase.Induction:
                case PhPhase.SelfTouch:
                case PhPhase.Dissolve:
                    if (_module.Params.FollowDuringInduction && RealWrist(out w)) arm.Follow(w, _calibAxis, (float)_module.Params.OffsetCm);
                    if (phase == PhPhase.Dissolve)
                    {
                        float a = DissolveAlpha(_module.Machine.ElapsedMs(now) / 1000.0);   // the machine's elapsed time stops with a pause
                        if (a != arm.Alpha) arm.Alpha = a;
                    }
                    break;
                case PhPhase.Reveal:
                {
                    double t = _module.Machine.ElapsedMs(now) / 1000.0;
                    float ra = RevealAlpha(t);
                    if (ra != arm.Alpha) arm.Alpha = ra;
                    if (!RealWrist(out w)) w = DefaultCalibration().Key;   // the calibrated wrist when the hand is not tracked
                    arm.Follow(w, _calibAxis, (float)RevealOffsetCm(t, _module.Params.OffsetCm));
                    break;
                }
                case PhPhase.Threat:
                    TickThreat(now);
                    break;
            }
            if (Driver.Active) { brush.Tick(now); Driver.Tick(now); }
        }

        // ---- A5 agency: the muscle (or, without a sensor, the real hand) closes the virtual hand, then it closes by itself ----------

        /// <summary>Fingertip-to-palm distance of the real hand of the stimulated arm in metres (the flexion signal), null when it is not tracked.
        /// Only the index fingertip: OpusJoints has no middle or ring fingertip.</summary>
        private double? IndexToPalmM()
        {
            double[] tip, palm, rot;
            if (_hands == null || !_hands.IsTracked(_side) ||
                !_hands.TryGetJointPose(PhArm.IndexTip(_side), out tip, out rot) || !_hands.TryGetJointPose(PhArm.Palm(_side), out palm, out rot)) return null;
            return Vector3.Distance(new Vector3((float)tip[0], (float)tip[1], (float)tip[2]), new Vector3((float)palm[0], (float)palm[1], (float)palm[2]));
        }

        private void TickAgency(double now)
        {
            var run = _module.Agency;
            if (run == null) return;
            double? d = IndexToPalmM();
            var step = run.Step;                                       // the step of the previous frame
            if (step == AgencyStep.Rest && run.ElapsedS >= RestSettleS && d.HasValue) _flexion.AddRest(d.Value);
            if (step == AgencyStep.Squeeze && d.HasValue) _flexion.AddSqueeze(d.Value);
            double? level = null;                                      // no source at all: the hand stays open
            if (step == AgencyStep.Driven || step == AgencyStep.Watch)
                level = _emgReady ? _nodeB.EmgLevel01 : _flexion.Ready && d.HasValue ? (double?)_flexion.Level(d.Value) : null;
            run.Tick(run.StartMs + _module.Machine.ElapsedMs(now), level);   // the machine's elapsed time stops with a pause
            if (run.Step != _agencyStep)
            {
                // each calibration runs once, when its step is over: Node B rest, then Node B MVC; the hand-tracking fallback at the same time
                if (_agencyStep <= AgencyStep.Rest && run.Step > AgencyStep.Rest)
                    _restOk = _nodeB != null && _nodeB.Connected && _nodeB.CalibrateRest(now - RestWindowMs, now);
                if (_agencyStep <= AgencyStep.Squeeze && run.Step > AgencyStep.Squeeze)
                {
                    _emgReady = _restOk && _nodeB.CalibrateMvc(now - SqueezeWindowMs, now);
                    _flexion.Calibrate();
                }
                _agencyStep = run.Step;
            }
            arm.Curl = (float)run.Curl01;
        }

        private void TickThreat(double now)
        {
            if (_threatAtMs >= 0 && now >= _threatAtMs)
            {
                _threatAtMs = -1;
                Vector3 surface = arm.PalmTopWorld;
                Vector3 drop = surface + Vector3.up * PhantomAnchors.DropHeightM;
                if (anchors != null && anchors.threatDropPoint != null) anchors.threatDropPoint.position = drop;
                threat.Begin(drop, surface, now);
            }
            threat.Tick(now);
            var r = Collector.Tick(now);
            if (r != null) _module.SubmitThreatResponse(r);
        }

        private void OnImpact(double impactMs, Vector3? pos, bool ok)
        {
            _module.SubmitThreatImpact(impactMs, pos.HasValue ? new double[] { pos.Value.x, pos.Value.y, pos.Value.z } : null, ok);
            if (ok) Collector.Arm(impactMs);
            else Debug.Log("[PhantomHand] threat_impact ok:false (no response window recorded)");
        }

        private void OnPhase(PhPhase prev, PhPhase now, double nowMs)
        {
            bool wasStroking = prev == PhPhase.Induction || prev == PhPhase.SelfTouch || prev == PhPhase.Dissolve;
            bool isStroking = now == PhPhase.Induction || now == PhPhase.SelfTouch || now == PhPhase.Dissolve;
            if (wasStroking && !isStroking)
            {
                Driver.End(nowMs);
                brush.Active = false; brush.Visible = false;
            }
            if (prev == PhPhase.Agency) arm.Curl = 0f;   // the stone falls on an open hand
            if (prev == PhPhase.Threat && now != PhPhase.Threat)
            {
                if (Collector.Armed) Debug.Log("[PhantomHand] threat phase ended before the 1.5 s response window closed");
                Collector.Disarm();
                threat.Cancel();
                _threatAtMs = -1;
            }

            switch (now)
            {
                case PhPhase.Calibrate:
                    arm.Unfreeze(); arm.Alpha = 1f; arm.Curl = 0f; arm.Visible = true;
                    PlaceDefault();
                    break;
                case PhPhase.Induction:
                case PhPhase.Dissolve:   // the dissolve starts like an induction: arm placed and frozen, brush and strokes running (the arm then fades)
                    BeginInduction(nowMs);
                    break;
                case PhPhase.Agency:
                    arm.Unfreeze(); arm.Visible = true;
                    _agencyStep = AgencyStep.Rest; _restOk = _emgReady = false;
                    _flexion = new FlexionCalibration();
                    break;
                case PhPhase.SelfTouch:
                    arm.Visible = true;
                    break;
                case PhPhase.Reveal:
                    // fallback reveal: the arm fades in and slides onto the real hand (no passthrough layer in this build)
                    arm.Unfreeze(); arm.Curl = 0f; arm.Alpha = 0f; arm.Visible = true;
                    _module.SubmitReveal(true);
                    break;
                case PhPhase.Threat:
                    arm.Visible = true;
                    _threatAtMs = nowMs + threatLeadInS * 1000.0;
                    break;
                case PhPhase.Done:
                case PhPhase.Idle:
                    arm.Visible = false; threat.Cancel();
                    if (_haptic != null && prev != PhPhase.Idle) _haptic.SendDisplay("IDLE", nowMs);
                    break;
                default:
                    arm.Visible = false;
                    break;
            }
        }

        private void PlaceDefault()
        {
            Vector3 w;
            // the start of the calibration: where the real arm is, or in the outline until it is tracked
            if (RealWrist(out w)) arm.PlaceFromCalibration(new Vector3(w.x, TableWristY(), w.z), RealAxis(w), 0f);
            else
            {
                var a = DefaultCalibration();
                arm.PlaceFromCalibration(a.Key, a.Value, 0f);
            }
        }

        private System.Collections.Generic.KeyValuePair<Vector3, Vector3> DefaultCalibration()
        {
            if (_hasCalib) return new System.Collections.Generic.KeyValuePair<Vector3, Vector3>(_calibWrist, _calibAxis);
            if (anchors != null && anchors.armRestOutline != null)
            {
                var o = anchors.armRestOutline; var axis = o.forward;
                return new System.Collections.Generic.KeyValuePair<Vector3, Vector3>(o.position + axis * (float)(_module.Params.ForearmLengthCm / 100.0) + Vector3.up * 0.021f, axis);
            }
            return new System.Collections.Generic.KeyValuePair<Vector3, Vector3>(new Vector3((float)PhArm.X(_side, 0.18), 0.771f, 0.40f), Vector3.forward);
        }

        private void BeginInduction(double nowMs)
        {
            var p = _module.Params;
            Vector3 w;
            if (!_hasCalib)
            {
                var d = DefaultCalibration();
                _calibWrist = RealWrist(out w) ? w : d.Key; _calibAxis = d.Value; _hasCalib = true;
            }
            arm.Unfreeze();
            arm.PlaceFromCalibration(_calibWrist, _calibAxis, (float)p.OffsetCm);
            if (!p.FollowDuringInduction) arm.Freeze();
            arm.Curl = 0f; arm.Alpha = 1f; arm.Visible = true;
            brush.SetGeometry(arm.WristWorld, arm.ElbowDirection, arm.ForearmLengthM, arm.MotorAFromWristM, arm.MotorSpacingM, p.MotorSoaMs);
            brush.Visible = true;
            Driver.Begin(nowMs);
        }
    }
}
