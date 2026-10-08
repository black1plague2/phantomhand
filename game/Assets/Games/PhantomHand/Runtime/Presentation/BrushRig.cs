using System;
using System.Collections.Generic;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>Brush pose along the forearm at one instant (distance from the wrist toward the elbow, height above the skin).</summary>
    public struct BrushSample
    {
        public float D;           // metres from the wrist reference point toward the elbow
        public float Lift;        // metres above the skin (0 = touching)
        public bool Contact;
        public int StrokeIndex;   // stroke being drawn / last stroke; -1 before the first
    }

    /// <summary>
    /// The visible brush (03-SPEC D1/D2): procedural handle + bristle head on a time-parameterised path, no physics. It touches
    /// down at the wrist at stroke.StartMs, passes motor A at PassAMs and B at PassBMs (speed = motor_spacing / motor_soa), lifts
    /// at the elbow and swings back above the arm. The path is pure maths over the geometry given in <see cref="SetGeometry"/>
    /// (ArmGeometry), so the brush keeps stroking when the arm is invisible (A2 dissolve). Bristles bend on contact; the swish
    /// sound follows the visual only. MEASURED pass times come from the animation itself: the moment the sampled path crosses
    /// the motor distance (interpolated inside the frame) is raised through <see cref="OnPass"/>.
    /// </summary>
    public sealed class BrushRig : MonoBehaviour
    {
        public Material handleMaterial, bristleMaterial;
        public AudioSource swishSource;

        public const float HoverM = 0.075f;
        public const double DescendMs = 120, LiftMs = 120;

        /// <summary>(stroke index, slot 0 = over motor A / 1 = over motor B, measured session ms).</summary>
        public event Action<int, int, double> OnPass;
        public event Action<int, double> OnTouchDown;

        public bool IsBuilt { get; private set; }
        public bool Active { get; set; }
        public Vector3 LastTip { get; private set; }

        private Vector3 _wrist = Vector3.zero, _elbowDir = Vector3.back;
        private float _len = 0.25f, _aM = 0.05f, _spacingM = 0.10f;
        private double _speedMPerMs = 0.10 / 100.0;
        private IReadOnlyList<StrokePlan> _plan = new List<StrokePlan>();
        private Transform _visual, _head, _handle;
        private Transform _model;      // PhModels.Brush instance (bristle tip at its origin, handle +y), null for the procedural brush
        private float _bend;
        // per-stroke measurement state
        private int _first;
        private double[] _lastT = new double[0], _lastD = new double[0];
        private bool[] _started = new bool[0], _fA = new bool[0], _fB = new bool[0];

        public void Build()
        {
            if (IsBuilt) return;
            _visual = new GameObject("BrushVisual").transform; _visual.SetParent(transform, false);
            var model = PhModels.Spawn(PhModels.Brush, _visual);
            if (model != null)
            {
                // 3D brush: 22 cm, bristle tip at the visual's origin (= the contact point), handle along +y
                _model = model.transform; _model.localPosition = Vector3.zero; _model.localRotation = Quaternion.identity;
                PhModels.StripShadows(model);
                _visual.gameObject.SetActive(false);
                IsBuilt = true;
                return;
            }
            var hm = handleMaterial != null ? handleMaterial : PhMaterials.Make("PH_BrushHandle", new Color(0.55f, 0.38f, 0.22f), 0.35f);
            var bm = bristleMaterial != null ? bristleMaterial : PhMaterials.Make("PH_Bristle", new Color(0.93f, 0.88f, 0.76f), 0.1f);
            // origin of _visual = brush tip contact point; local +y = handle direction
            var ferrule = Prim(PrimitiveType.Cylinder, "Ferrule", new Vector3(0, 0.030f, 0), new Vector3(0.017f, 0.014f, 0.007f), new Color(0.75f, 0.76f, 0.80f));
            ferrule.GetComponent<MeshRenderer>().sharedMaterial = hm;
            _handle = Prim(PrimitiveType.Cylinder, "Handle", new Vector3(0, 0.082f, 0), new Vector3(0.012f, 0.050f, 0.012f), Color.white);
            _handle.GetComponent<MeshRenderer>().sharedMaterial = hm;
            _head = Prim(PrimitiveType.Cube, "Bristles", new Vector3(0, 0.011f, 0), new Vector3(0.034f, 0.022f, 0.011f), Color.white);
            _head.GetComponent<MeshRenderer>().sharedMaterial = bm;
            _visual.gameObject.SetActive(false);
            IsBuilt = true;
        }

        private Transform Prim(PrimitiveType t, string name, Vector3 lp, Vector3 ls, Color c)
        {
            var go = GameObject.CreatePrimitive(t); go.name = name; PhMaterials.Strip(go);
            go.transform.SetParent(_visual, false); go.transform.localPosition = lp; go.transform.localScale = ls;
            return go.transform;
        }

        public bool Visible { get { return _visual != null && _visual.gameObject.activeSelf; } set { if (_visual != null) _visual.gameObject.SetActive(value); } }

        /// <summary>Independent of any renderer: wrist reference point, horizontal wrist-to-elbow direction, forearm length, motor positions.</summary>
        public void SetGeometry(Vector3 wristWorld, Vector3 elbowDirection, float forearmM, float motorAFromWristM, float motorSpacingM, double motorSoaMs)
        {
            _wrist = wristWorld; _elbowDir = new Vector3(elbowDirection.x, 0, elbowDirection.z).normalized;
            _len = forearmM; _aM = motorAFromWristM; _spacingM = motorSpacingM;
            _speedMPerMs = motorSpacingM / motorSoaMs;
        }

        public void SetPlan(IReadOnlyList<StrokePlan> plan)
        {
            _plan = plan ?? new List<StrokePlan>();
            int n = _plan.Count;
            _lastT = new double[n]; _lastD = new double[n]; _started = new bool[n]; _fA = new bool[n]; _fB = new bool[n];
            _first = 0;
        }

        // ---- path (pure) -----------------------------------------------------------------------------------------

        public BrushSample Evaluate(double t)
        {
            return Evaluate(_plan, t, _speedMPerMs, _len);
        }

        public static BrushSample Evaluate(IReadOnlyList<StrokePlan> plan, double t, double speedMPerMs, float len)
        {
            var s = new BrushSample { D = 0, Lift = HoverM, Contact = false, StrokeIndex = -1 };
            if (plan == null || plan.Count == 0) return s;
            // last stroke whose descent has begun
            int k = -1;
            for (int i = 0; i < plan.Count; i++) { if (t >= plan[i].StartMs - DescendMs) k = i; else break; }
            if (k < 0) return s;   // hover over the wrist before the first stroke
            var p = plan[k];
            s.StrokeIndex = k;
            if (t < p.StartMs)
            {
                s.D = 0; s.Lift = HoverM * (1f - Ease((float)((t - (p.StartMs - DescendMs)) / DescendMs)));
                return s;
            }
            if (t <= p.EndMs)
            {
                s.D = (float)Math.Min(len, speedMPerMs * (t - p.StartMs)); s.Lift = 0; s.Contact = true;
                return s;
            }
            s.D = len;
            if (t < p.EndMs + LiftMs) { s.Lift = HoverM * Ease((float)((t - p.EndMs) / LiftMs)); return s; }
            s.Lift = HoverM;
            if (k + 1 >= plan.Count) return s;   // last stroke: hover at the elbow
            double t0 = p.EndMs + LiftMs, t1 = plan[k + 1].StartMs - DescendMs;
            if (t1 > t0)
            {
                float u = (float)Math.Min(1.0, (t - t0) / (t1 - t0));
                s.D = Mathf.Lerp(len, 0f, Ease(u));
                s.Lift = HoverM + 0.03f * Mathf.Sin(Mathf.PI * u);
            }
            else s.D = 0;
            return s;
        }

        private static float Ease(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        // ---- per frame ---------------------------------------------------------------------------------------------

        public void Tick(double nowMs)
        {
            if (!IsBuilt || !Active) return;
            var s = Evaluate(nowMs);
            ApplyPose(s);
            MeasurePasses(nowMs);
        }

        /// <summary>Poses the brush at time t without raising any events (editor screenshots, previews).</summary>
        public void PoseAt(double t) { if (IsBuilt) ApplyPose(Evaluate(t)); }

        private void ApplyPose(BrushSample s)
        {
            Vector3 up = Vector3.up;
            Vector3 tip = _wrist + _elbowDir * s.D + up * (ArmGeometry.TopY(s.D, _len) + s.Lift);
            LastTip = tip;
            // handle trails behind the motion (toward the wrist)
            Vector3 handleDir = (up * Mathf.Cos(0.80f) - _elbowDir * Mathf.Sin(0.80f)).normalized;
            float target = s.Contact ? 1f : 0f;
            _bend = Mathf.MoveTowards(_bend, target, 0.34f);
            // bristle bend: head squashes and leans against the motion, wider on contact
            _visual.SetPositionAndRotation(tip, Quaternion.FromToRotation(Vector3.up, handleDir));
            if (_model != null)
            {
                // the model brush is one rigid mesh: a slight squash of the bristle end stands in for the bend
                _model.localScale = new Vector3(1f + 0.03f * _bend, 1f - 0.03f * _bend, 1f);
                return;
            }
            _head.localScale = new Vector3(0.034f + 0.006f * _bend, 0.022f - 0.007f * _bend, 0.011f + 0.004f * _bend);
            _head.localRotation = Quaternion.Euler(0, 0, 0) * Quaternion.AngleAxis(22f * _bend, _visual.InverseTransformDirection(Vector3.Cross(up, _elbowDir)));
            _head.localPosition = new Vector3(0, 0.011f - 0.003f * _bend, 0);
        }

        private void MeasurePasses(double now)
        {
            while (_first < _plan.Count && _started[_first] && _lastT[_first] >= _plan[_first].EndMs) _first++;
            for (int k = _first; k < _plan.Count; k++)
            {
                var p = _plan[k];
                if (p.StartMs > now) break;
                if (!_started[k])
                {
                    _started[k] = true; _lastT[k] = p.StartMs; _lastD[k] = 0;
                    var h = OnTouchDown; if (h != null) h(p.Index, p.StartMs);
                    if (swishSource != null) ProceduralSfx.Play(swishSource, ProceduralSfx.Swish(), 0.35f, 0.92f + 0.16f * (k % 3) / 2f);
                }
                double tt = Math.Min(now, p.EndMs);
                double d = Math.Min(_len, _speedMPerMs * (tt - p.StartMs));
                Cross(k, p, 0, _aM, tt, d, ref _fA[k]);
                Cross(k, p, 1, _aM + _spacingM, tt, d, ref _fB[k]);
                _lastT[k] = tt; _lastD[k] = d;
            }
        }

        private void Cross(int k, StrokePlan p, int slot, double thr, double tt, double d, ref bool fired)
        {
            if (fired) return;
            double ld = _lastD[k], lt = _lastT[k];
            if (ld < thr && d >= thr)
            {
                double m = d > ld ? lt + (thr - ld) / (d - ld) * (tt - lt) : tt;
                fired = true;
                var h = OnPass; if (h != null) h(p.Index, slot, m);
            }
        }
    }
}
