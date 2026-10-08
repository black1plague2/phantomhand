using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U3 presenter: owns the virtual arm, brush, stroke driver, threat drop and response collector and drives them from the
    /// module's phase. The composition root (U5) calls <see cref="Bind"/> once and <see cref="Tick"/> every frame AFTER
    /// haptic Pump and module.Tick (frame order: clients Pump -> module Tick -> presenters -> recorders).
    /// Arm visibility: Calibrate (follows the real wrist + offset), Induction/SelfTouch/Agency/Threat/Dissolve (frozen
    /// except Calibrate/Agency or follow_during_induction); hidden for probes, questionnaire, reveal and witness.
    /// </summary>
    public sealed class ArmThreatPresenter : MonoBehaviour
    {
        public VirtualArmRig arm;
        public BrushRig brush;
        public ThreatDrop threat;
        public PhantomAnchors anchors;
        [Tooltip("Seconds after the Threat phase starts before the 0.6 s telegraph begins.")]
        public float threatLeadInS = 0.5f;

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
        private Vector3 _calibWrist, _calibAxis = Vector3.forward;
        private double _threatAtMs = -1;

        public void Bind(PhantomHandModule module, HapticClient haptic, SleeveSensorClient nodeA, SleeveSensorClient nodeB, SessionClock clock, IHandSource hands)
        {
            _module = module; _haptic = haptic; _clock = clock; _hands = hands;
            var p = module.Params;
            if (arm == null) { arm = new GameObject("VirtualArm").AddComponent<VirtualArmRig>(); arm.transform.SetParent(transform, false); }
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

        /// <summary>Calibrated real right wrist and forearm axis (elbow toward fingers), from the calibration phase (U4).</summary>
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

        private Vector3? RealPalm()
        {
            double[] pos, rot;
            if (_hands != null && _hands.IsTracked(HandSide.Right) && _hands.TryGetJointPose(OpusJoints.RPalm, out pos, out rot))
                return new Vector3((float)pos[0], (float)pos[1], (float)pos[2]);
            return null;
        }

        private bool RealWrist(out Vector3 p)
        {
            double[] pos, rot;
            if (_hands != null && _hands.IsTracked(HandSide.Right) && _hands.TryGetJointPose(OpusJoints.RWrist, out pos, out rot))
            { p = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]); return true; }
            p = default(Vector3); return false;
        }

        // ---- per frame -----------------------------------------------------------------------------------------------

        public void Tick()
        {
            if (!_bound) return;
            double now = _clock.NowMs;
            Vector3 w;
            if (RealWrist(out w)) Collector.PushWrist(now, w, _hands.GetDataVersion(HandSide.Right));

            var phase = _module.CurrentPhase;
            if (phase != _phase) { var prev = _phase; _phase = phase; OnPhase(prev, phase, now); }

            switch (phase)
            {
                case PhPhase.Calibrate:
                case PhPhase.Agency:
                    if (RealWrist(out w)) arm.Follow(w, _hasCalib ? _calibAxis : Vector3.forward, (float)_module.Params.OffsetCm);
                    break;
                case PhPhase.Induction:
                case PhPhase.SelfTouch:
                case PhPhase.Dissolve:
                    if (_module.Params.FollowDuringInduction && RealWrist(out w)) arm.Follow(w, _calibAxis, (float)_module.Params.OffsetCm);
                    break;
                case PhPhase.Threat:
                    TickThreat(now);
                    break;
            }
            if (Driver.Active) { brush.Tick(now); Driver.Tick(now); }
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
                    BeginInduction(nowMs);
                    break;
                case PhPhase.Agency:
                    arm.Unfreeze(); arm.Visible = true;
                    break;
                case PhPhase.SelfTouch:
                case PhPhase.Dissolve:
                    arm.Visible = true;
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
            if (RealWrist(out w)) arm.PlaceFromCalibration(w, _hasCalib ? _calibAxis : Vector3.forward, (float)_module.Params.OffsetCm);
            else
            {
                var a = DefaultCalibration();
                arm.PlaceFromCalibration(a.Key, a.Value, (float)_module.Params.OffsetCm);
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
            return new System.Collections.Generic.KeyValuePair<Vector3, Vector3>(new Vector3(0.18f, 0.771f, 0.40f), Vector3.forward);
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
