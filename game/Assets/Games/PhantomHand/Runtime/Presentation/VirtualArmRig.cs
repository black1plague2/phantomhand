using System;
using System.Collections.Generic;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>Marks the virtual hand/forearm hit proxy so the stone only counts collisions with it.</summary>
    public sealed class VirtualHandHit : MonoBehaviour { }

    /// <summary>Materials for the arm; the scene builder assigns assets, null falls back to runtime-made ones.</summary>
    [Serializable]
    public sealed class ArmMaterials
    {
        public Material skin, sleeve, band;
    }

    /// <summary>
    /// The virtual right arm (03-SPEC D2): procedural forearm (tapered, forearm_length_cm), a posable hand (palm + 3-segment
    /// fingers + 2-segment thumb on a joint hierarchy, so fingers can curl for A5/agency), a dark virtual sleeve with two
    /// accent bands at motor A / motor B. Local frame: origin = wrist reference point P, +z toward the fingers, +y up
    /// (palm down), +x to the right of the forearm axis (thumb side is -x for a right hand).
    /// Placement: <see cref="PlaceFromCalibration"/> puts the wrist offset_cm to the LEFT of the calibrated real wrist on the
    /// table plane with the same yaw. <see cref="Freeze"/> stops <see cref="Follow"/> from moving it (induction .. threat).
    /// Exposes <see cref="Alpha"/> (A2 dissolve) and <see cref="WorldPos"/> (A3 self-touch bands, cue positions).
    /// Design note: the hand is a procedural joint hierarchy rather than a duplicated ISDK skinned hand, so it needs no IHand
    /// driver and can be posed (Curl) deterministically. When PhantomModelImporter has baked PH_Hand / PH_Forearm / PH_Sleeve the arm uses those
    /// instead: the rigged hand keeps its own skin texture and Curl drives its finger bones (PhRiggedHand), the glove fallback is squashed.
    /// </summary>
    public sealed class VirtualArmRig : MonoBehaviour
    {
        public ArmMaterials materials = new ArmMaterials();

        public float ForearmLengthM { get; private set; } = 0.25f;
        public float MotorAFromWristM { get; private set; } = 0.05f;
        public float MotorSpacingM { get; private set; } = 0.10f;
        public bool IsBuilt { get; private set; }
        public bool Frozen { get; private set; }
        /// <summary>A left arm: the same arm mirrored in its own x (thumb on +x), placed to the RIGHT of the real wrist. Set before <see cref="Build(PhantomHandParams)"/>.</summary>
        public bool LeftArm { get; set; }
        public Collider[] HitColliders { get { return _hit.ToArray(); } }

        private const float PalmLen = 0.098f, PalmW = 0.092f, PalmT = 0.022f;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Material> _opaque = new List<Material>();
        private readonly List<Material> _fade = new List<Material>();
        private readonly List<Collider> _hit = new List<Collider>();
        private readonly List<FingerJoint> _joints = new List<FingerJoint>();
        private float _alpha = 1f, _curl;
        private Transform _handRoot, _visualRoot;
        private Transform _handModel;      // model hand (PhModels.Hand), null when the procedural hand is used
        private PhModelInfo _handInfo;
        private PhRiggedHand _rig;         // bone driver of the rigged hand model; null for the glove mesh (squash fallback) and the procedural hand

        private struct FingerJoint { public Transform T; public float RelaxedDeg, ClosedDeg; }

        // ---- build -----------------------------------------------------------------------------------------------

        public void Build(PhantomHandParams p)
        {
            Build(p != null ? (float)p.ForearmLengthCm / 100f : 0.25f, p != null ? (float)p.MotorAFromWristCm / 100f : 0.05f, p != null ? (float)p.MotorSpacingCm / 100f : 0.10f);
        }

        public void Build(float forearmM, float motorAM, float motorSpacingM)
        {
            if (IsBuilt) Teardown();
            ForearmLengthM = forearmM; MotorAFromWristM = motorAM; MotorSpacingM = motorSpacingM;
            var skin = materials.skin != null ? materials.skin : PhMaterials.Make("PH_Skin", new Color(0.74f, 0.58f, 0.48f), 0.25f);
            var sleeve = materials.sleeve != null ? materials.sleeve : PhMaterials.Make("PH_Sleeve", new Color(0.06f, 0.06f, 0.08f), 0.1f);
            var band = materials.band != null ? materials.band : PhMaterials.Make("PH_Band", new Color(0.25f, 0.85f, 0.70f), 0.2f);

            _visualRoot = new GameObject("ArmVisual").transform;
            _visualRoot.SetParent(transform, false);

            float sleeveFrom = 0.012f, sleeveTo = Mathf.Min(forearmM - 0.02f, motorAM + motorSpacingM + 0.045f);
            // 3D models (PhModels, built by PhantomModelImporter) when all three arm wrappers exist; otherwise the procedural arm.
            bool models = HandModelUsable() && PhModels.Load(PhModels.Forearm) != null && PhModels.Load(PhModels.Sleeve) != null;
            if (models)
            {
                var fi = PhModels.Load(PhModels.Forearm).GetComponent<PhModelInfo>();
                var si = PhModels.Load(PhModels.Sleeve).GetComponent<PhModelInfo>();
                float fRef = fi != null ? fi.referenceForearmM : 0.25f, sRef = si != null && si.rangeToM > 0.01f ? si.rangeToM : 0.195f;
                // a textured hand keeps its own material, and the forearm then uses its wrapper's tone-matched skin instead of the flat override
                var hi = PhModels.Load(PhModels.Hand).GetComponent<PhModelInfo>();
                AddModel(PhModels.Forearm, _visualRoot, new Vector3(1f, 1f, forearmM / fRef), hi != null && hi.texturedSkin ? null : materials.skin);
                AddModel(PhModels.Sleeve, _visualRoot, new Vector3(1f, 1f, Mathf.Max(0.05f, sleeveTo) / sRef), null);
            }
            else
            {
                AddMesh("Forearm", _visualRoot, TubeMesh("ph_forearm", 0f, forearmM, 1f, 1f, true), skin);
                AddMesh("Sleeve", _visualRoot, TubeMesh("ph_sleeve", sleeveFrom, sleeveTo, 1.06f, 1.09f), sleeve);
            }
            AddMesh("BandA", _visualRoot, TubeMesh("ph_bandA", motorAM - 0.009f, motorAM + 0.009f, 1.10f, 1.14f), band);
            AddMesh("BandB", _visualRoot, TubeMesh("ph_bandB", motorAM + motorSpacingM - 0.009f, motorAM + motorSpacingM + 0.009f, 1.10f, 1.14f), band);

            if (models) BuildHandModel();
            else BuildHand(skin);
            // After the hand is bound: the bones are driven by local rotations found in the unmirrored pose, and a mirror above them
            // turns a closing right hand into a closing left hand without touching any of that.
            _visualRoot.localScale = new Vector3(LeftArm ? -1f : 1f, 1f, 1f);
            BuildHitProxy();
            var rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            IsBuilt = true;
            ApplyCurl();
            Alpha = _alpha;
        }

        /// <summary>The hand wrapper exists and its recorded bounds are plausible (a rigged hand must have recorded them; a glove wrapper baked before bounds were recorded is taken as is):
        /// otherwise the whole arm stays procedural, a degenerate hand is never the default.</summary>
        private static bool HandModelUsable()
        {
            var proto = PhModels.Load(PhModels.Hand);
            if (proto == null) return false;
            var info = proto.GetComponent<PhModelInfo>();
            if (info == null) return true;
            bool recorded = info.boundsMin != Vector3.zero || info.boundsMax != Vector3.zero;
            if (!info.rigged && !recorded) return true;
            string why;
            if (PhHandFit.BoundsPlausible(info.boundsMin, info.boundsMax, PhHandFit.DefaultHandLenM, out why)) return true;
            Debug.LogWarning("[VirtualArmRig] the baked hand is not usable (" + why + "): the arm stays procedural. Re-run PhantomModelImporter.RunBatch().");
            return false;
        }

        private void Teardown()
        {
            foreach (var m in _fade) if (m != null) DestroyImmediate(m);
            _renderers.Clear(); _opaque.Clear(); _fade.Clear(); _hit.Clear(); _joints.Clear();
            _handModel = null; _handInfo = null; _rig = null;
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            IsBuilt = false;
        }

        /// <summary>Tube along -z (wrist toward elbow) from distance d0 to d1, ellipse cross-sections following ArmGeometry.</summary>
        private Mesh TubeMesh(string name, float d0, float d1, float inflate0, float inflate1, bool dome = false)
        {
            const int rings = 16, seg = 20;
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float d = Mathf.Lerp(d0, d1, r / (float)rings);
                float k = Mathf.Lerp(inflate0, inflate1, r / (float)rings);
                float fr = r / (float)rings;
                if (dome && fr > 0.8f) k *= Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow((fr - 0.8f) / 0.2f, 2f) * 0.9f));
                float hw = ArmGeometry.HalfWidth(d, ForearmLengthM) * k, hh = ArmGeometry.HalfHeight(d, ForearmLengthM) * k, cy = ArmGeometry.AxisY(d, ForearmLengthM);
                for (int s = 0; s < seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * hw, cy + Mathf.Sin(a) * hh, -d));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * seg + s, b = r * seg + (s + 1) % seg, c = (r + 1) * seg + s, e = (r + 1) * seg + (s + 1) % seg;
                    // winding: facing outward for rings going toward -z
                    tri.AddRange(new[] { a, c, b, b, c, e });
                }
            int c0 = v.Count; v.Add(new Vector3(0, ArmGeometry.AxisY(d0, ForearmLengthM), -d0));
            int c1 = v.Count; v.Add(new Vector3(0, ArmGeometry.AxisY(d1, ForearmLengthM), -d1));
            for (int s = 0; s < seg; s++)
            {
                int n = (s + 1) % seg;
                tri.AddRange(new[] { c0, s, n });
                tri.AddRange(new[] { c1, rings * seg + n, rings * seg + s });
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private void AddMesh(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            Register(mr, mat);
        }

        private void Register(Renderer r, Material opaque)
        {
            _renderers.Add(r); _opaque.Add(opaque); _fade.Add(PhMaterials.FadeCopy(opaque, depthWrite: true));
        }

        /// <summary>Instantiates a model wrapper under parent; its renderers use overrideMat (when given) or the wrapper's own material.</summary>
        private GameObject AddModel(string wrapper, Transform parent, Vector3 scale, Material overrideMat)
        {
            var go = PhModels.Spawn(wrapper, parent);
            if (go == null) return null;
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = scale;
            RegisterModel(go, overrideMat);
            return go;
        }

        /// <summary>Registers every Renderer below an instantiated model (MeshRenderer and SkinnedMeshRenderer alike) for the material override (overrideMat, or each
        /// renderer's own material when null), shadows off and the alpha / A2 fade swap. Public so EditMode tests can adopt a hand-built model.</summary>
        public void RegisterModel(GameObject go, Material overrideMat)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var m = overrideMat != null ? overrideMat : r.sharedMaterial;
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                Register(r, m);
            }
        }

        private void BuildHandModel()
        {
            // the rigged hand carries its own skin texture: no flat colour override (the glove mesh is black, so it still gets the skin colour)
            var proto = PhModels.Load(PhModels.Hand).GetComponent<PhModelInfo>();
            var go = AddModel(PhModels.Hand, _visualRoot, Vector3.one, proto != null && proto.texturedSkin ? null : materials.skin);
            SetHandModel(go);
        }

        /// <summary>Makes an instantiated hand model the arm's hand: reads its measured info (hit proxy, palm top) and binds its bone driver when it has one;
        /// without a usable rig <see cref="Curl"/> squashes the model instead. Public so EditMode tests can adopt a hand-built model.</summary>
        public void SetHandModel(GameObject go)
        {
            _handRoot = go.transform; _handModel = go.transform;
            _handInfo = go.GetComponent<PhModelInfo>();
            _rig = go.GetComponent<PhRiggedHand>();
            // bind in the unmirrored pose (see Build): a hand swapped in later finds the mirror already there
            Vector3 mirror = _visualRoot != null ? _visualRoot.localScale : Vector3.one;
            if (_visualRoot != null) _visualRoot.localScale = Vector3.one;
            if (_rig != null && !_rig.Bind()) _rig = null;
            if (_visualRoot != null) _visualRoot.localScale = mirror;
            if (IsBuilt) RebuildHitProxy();     // a hand swapped in after Build: the proxy follows its measurements
        }

        private void RebuildHitProxy()
        {
            var old = transform.Find("HitProxy");
            if (old != null) DestroyImmediate(old.gameObject);
            _hit.Clear();
            BuildHitProxy();
        }

        private void BuildHand(Material skin)
        {
            _handRoot = new GameObject("Hand").transform;
            _handRoot.SetParent(_visualRoot, false);
            float baseY = -ArmGeometry.WristHalfHeight + PalmT * 0.5f;

            var palm = GameObject.CreatePrimitive(PrimitiveType.Sphere); palm.name = "Palm";
            PhMaterials.Strip(palm); palm.transform.SetParent(_handRoot, false);
            palm.transform.localPosition = new Vector3(0, baseY + 0.001f, PalmLen * 0.5f - 0.004f);
            palm.transform.localScale = new Vector3(PalmW, PalmT, PalmLen + 0.016f);
            palm.GetComponent<MeshRenderer>().sharedMaterial = skin; Register(palm.GetComponent<MeshRenderer>(), skin);

            float knuckleZ = PalmLen - 0.004f;
            AddFinger("Index", new Vector3(-0.0285f, baseY, knuckleZ), new[] { 0.040f, 0.025f, 0.021f }, 0.0145f, -3f, skin);
            AddFinger("Middle", new Vector3(-0.0095f, baseY, knuckleZ + 0.003f), new[] { 0.044f, 0.028f, 0.022f }, 0.0150f, -0.5f, skin);
            AddFinger("Ring", new Vector3(0.0095f, baseY, knuckleZ), new[] { 0.041f, 0.026f, 0.021f }, 0.0140f, 2f, skin);
            AddFinger("Pinky", new Vector3(0.0285f, baseY, knuckleZ - 0.006f), new[] { 0.032f, 0.020f, 0.018f }, 0.0125f, 6f, skin);
            AddFinger("Thumb", new Vector3(-0.040f, baseY, 0.030f), new[] { 0.040f, 0.032f }, 0.0170f, -42f, skin);
        }

        private void AddFinger(string name, Vector3 root, float[] segs, float width, float splayDeg, Material skin)
        {
            var parent = new GameObject(name).transform;
            parent.SetParent(_handRoot, false);
            parent.localPosition = root;
            parent.localRotation = Quaternion.Euler(0, splayDeg, 0);
            Transform t = parent;
            bool thumb = name == "Thumb";
            for (int i = 0; i < segs.Length; i++)
            {
                var joint = new GameObject(name + "_j" + i).transform;
                joint.SetParent(t, false);
                joint.localPosition = i == 0 ? Vector3.zero : new Vector3(0, 0, segs[i - 1]);
                float relaxed = thumb ? (i == 0 ? 6f : 10f) : (i == 0 ? 10f : i == 1 ? 16f : 9f);
                float closed = thumb ? (i == 0 ? 25f : 55f) : (i == 0 ? 82f : i == 1 ? 95f : 60f);
                _joints.Add(new FingerJoint { T = joint, RelaxedDeg = relaxed, ClosedDeg = closed });
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule); cap.name = "Seg";
                PhMaterials.Strip(cap);
                cap.transform.SetParent(joint, false);
                // the capsule primitive is 2 tall (radius 0.5) along y; rotate it onto +z and scale to the segment
                cap.transform.localRotation = Quaternion.Euler(90, 0, 0);
                float w = width * 1.22f * (1f - 0.10f * i);
                cap.transform.localScale = new Vector3(w, (segs[i] + w * 0.35f) * 0.5f, w * 0.88f);
                cap.transform.localPosition = new Vector3(0, 0, segs[i] * 0.5f);
                cap.GetComponent<MeshRenderer>().sharedMaterial = skin; Register(cap.GetComponent<MeshRenderer>(), skin);
                t = joint;
            }
        }

        private void BuildHitProxy()
        {
            var proxy = new GameObject("HitProxy");
            proxy.transform.SetParent(transform, false);
            proxy.AddComponent<VirtualHandHit>();
            float baseY = -ArmGeometry.WristHalfHeight;
            var palm = proxy.AddComponent<BoxCollider>();
            palm.center = new Vector3(0, baseY + 0.014f, PalmLen * 0.5f); palm.size = new Vector3(PalmW, 0.032f, PalmLen);
            var fing = proxy.AddComponent<BoxCollider>();
            fing.center = new Vector3(0, baseY + 0.011f, PalmLen + 0.04f); fing.size = new Vector3(PalmW - 0.004f, 0.022f, 0.08f);
            if (_handInfo != null)
            {
                // keep the boxes flush with the visible model hand (top = dorsal surface); palm length / width / centre, finger length and the thumb box are
                // the importer's measurements (for the glove they equal the constants above, so its proxy is unchanged)
                float ph = Mathf.Max(0.02f, _handInfo.palmTopY - baseY), fh = Mathf.Max(0.015f, _handInfo.fingerTopY - baseY);
                float pl = _handInfo.palmLenM, pw = _handInfo.palmWidthM, cx = _handInfo.palmCenterX, fl = _handInfo.fingerLenM;
                palm.center = new Vector3(cx, baseY + ph * 0.5f, pl * 0.5f); palm.size = new Vector3(pw, ph, pl);
                fing.center = new Vector3(cx, baseY + fh * 0.5f, pl + fl * 0.5f); fing.size = new Vector3(pw - 0.004f, fh, fl);
            }
            var thumb = proxy.AddComponent<BoxCollider>();
            thumb.center = new Vector3(-0.055f, baseY + 0.010f, 0.05f); thumb.size = new Vector3(0.04f, 0.02f, 0.07f);
            if (_handInfo != null)
            {
                thumb.center = new Vector3(_handInfo.thumbCenterXZ.x, baseY + 0.010f, _handInfo.thumbCenterXZ.y);
                thumb.size = new Vector3(_handInfo.thumbSizeXZ.x, 0.02f, _handInfo.thumbSizeXZ.y);
            }
            var arm = proxy.AddComponent<BoxCollider>();
            float mid = ForearmLengthM * 0.5f;
            arm.center = new Vector3(0, ArmGeometry.AxisY(mid, ForearmLengthM), -mid);
            arm.size = new Vector3(ArmGeometry.HalfWidth(mid, ForearmLengthM) * 2f, ArmGeometry.HalfHeight(mid, ForearmLengthM) * 2f, ForearmLengthM);
            if (LeftArm)   // the proxy is not under the mirrored visual: mirror its boxes by hand
                foreach (var b in new[] { palm, fing, thumb, arm }) b.center = new Vector3(-b.center.x, b.center.y, b.center.z);
            _hit.AddRange(new Collider[] { palm, fing, thumb, arm });
        }

        // ---- pose / placement --------------------------------------------------------------------------------------

        /// <summary>Wrist reference point in world space.</summary>
        public Vector3 WristWorld { get { return transform.position; } }
        /// <summary>Unit vector wrist-to-elbow, horizontal.</summary>
        public Vector3 ElbowDirection { get { return -transform.forward; } }

        /// <summary>World point on the dorsal surface of the forearm fromWristCm toward the elbow (motor positions, A3 bands).</summary>
        public Vector3 WorldPos(float fromWristCm)
        {
            float d = fromWristCm / 100f;
            return transform.TransformPoint(new Vector3(0, ArmGeometry.TopY(d, ForearmLengthM), -d));
        }

        /// <summary>World point on the arm axis (inside the arm).</summary>
        public Vector3 AxisWorldPos(float fromWristCm)
        {
            float d = fromWristCm / 100f;
            return transform.TransformPoint(new Vector3(0, ArmGeometry.AxisY(d, ForearmLengthM), -d));
        }

        public Vector3 PalmCenterWorld { get { return transform.TransformPoint(new Vector3(0, -ArmGeometry.WristHalfHeight + PalmT * 0.5f, PalmLen * 0.5f)); } }
        public Vector3 PalmTopWorld
        {
            get
            {
                float top = _handInfo != null ? _handInfo.palmTopY : -ArmGeometry.WristHalfHeight + PalmT;
                float cx = _handInfo != null ? _handInfo.palmCenterX : 0f, pl = _handInfo != null ? _handInfo.palmLenM : PalmLen;
                return transform.TransformPoint(new Vector3(LeftArm ? -cx : cx, top, pl * 0.5f));
            }
        }

        /// <summary>Wrist position = calibrated real wrist, offset_cm toward the body's midline on the table plane (to the LEFT of a
        /// right arm, to the RIGHT of a left arm), palm down, same yaw.</summary>
        public void PlaceFromCalibration(Vector3 realWrist, Vector3 forearmAxis, float offsetCm)
        {
            var f = new Vector3(forearmAxis.x, 0, forearmAxis.z);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();
            var inward = Vector3.Cross(f, Vector3.up) * (LeftArm ? -1f : 1f);   // f=+z: -x for a right arm, +x for a left arm
            SetPose(realWrist + inward * (offsetCm / 100f), Quaternion.LookRotation(f, Vector3.up));
        }

        /// <summary>Follows the real wrist (+offset) unless frozen. Returns whether the pose changed.</summary>
        public bool Follow(Vector3 realWrist, Vector3 forearmAxis, float offsetCm)
        {
            if (Frozen) return false;
            PlaceFromCalibration(realWrist, forearmAxis, offsetCm);
            return true;
        }

        private void SetPose(Vector3 pos, Quaternion rot)
        {
            transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
        }

        public void Freeze() { Frozen = true; }
        public void Unfreeze() { Frozen = false; }

        public bool Visible
        {
            get { return _visualRoot != null && _visualRoot.gameObject.activeSelf; }
            set { if (_visualRoot != null) _visualRoot.gameObject.SetActive(value); }
        }

        /// <summary>The most the hand turns at the wrist away from lying flat along the forearm.</summary>
        public const float MaxWristDeg = 75f;

        /// <summary>Turns the hand at the wrist so that it points along <paramref name="forward"/> with its back toward
        /// <paramref name="dorsal"/> (world directions, as the real hand is held); the forearm stays where it lies. Limited to
        /// <see cref="MaxWristDeg"/>, so one bad frame of tracking cannot turn the hand over.</summary>
        public void SetHandOrientation(Vector3 forward, Vector3 dorsal)
        {
            if (_handRoot == null || forward.sqrMagnitude < 1e-6f || dorsal.sqrMagnitude < 1e-6f) return;
            Vector3 f = transform.InverseTransformDirection(forward), d = transform.InverseTransformDirection(dorsal);
            if (LeftArm) { f.x = -f.x; d.x = -d.x; }   // the hand lives under the mirrored visual
            _handRoot.localRotation = Quaternion.RotateTowards(Quaternion.identity, Quaternion.LookRotation(f, d), MaxWristDeg);
        }

        /// <summary>The hand flat along the forearm again (the pose of the induction and of the stone).</summary>
        public void ResetHandOrientation() { if (_handRoot != null) _handRoot.localRotation = Quaternion.identity; }

        /// <summary>World direction the hand points in (wrist toward the fingers).</summary>
        public Vector3 HandForwardWorld
        {
            get
            {
                if (_handRoot == null) return transform.forward;
                Vector3 l = _handRoot.localRotation * Vector3.forward;
                if (LeftArm) l.x = -l.x;
                return transform.TransformDirection(l);
            }
        }

        /// <summary>0 = relaxed static pose, 1 = fist (agency / A5).</summary>
        public float Curl { get { return _curl; } set { _curl = Mathf.Clamp01(value); ApplyCurl(); } }

        private void ApplyCurl()
        {
            if (_rig != null) { _rig.SetCurl(_curl); return; }   // rigged hand: the finger bones close toward the palm
            if (_handModel != null)
            {
                // the glove mesh has no bones: approximate a clench by compressing the hand along the fingers and thickening it,
                // keeping its underside on the table plane
                float sy = 1f + 0.30f * _curl;
                _handModel.localScale = new Vector3(1f + 0.08f * _curl, sy, 1f - 0.22f * _curl);
                _handModel.localPosition = new Vector3(0f, 0.0205f * (sy - 1f), 0f);
                return;
            }
            foreach (var j in _joints)
                if (j.T != null) j.T.localRotation = Quaternion.Euler(Mathf.Lerp(j.RelaxedDeg, j.ClosedDeg, _curl), 0, 0);
        }

        /// <summary>Hands the 15 finger angles of a tracked hand to the rigged hand's bones. False without a rigged hand (the procedural hand and the glove mesh keep
        /// using <see cref="Curl"/>); the last of Curl and ApplyHandPose wins.</summary>
        public bool ApplyHandPose(in PhHandPose pose)
        {
            if (_rig == null || pose.FlexDeg == null || pose.FlexDeg.Length < PhHandPose.FlexCount) return false;
            _rig.SetFlexion(pose.FlexDeg);
            return true;
        }

        /// <summary>1 = opaque, 0 = invisible (A2 dissolve). Below 1 the arm renders with transparent twins of its materials.</summary>
        public float Alpha
        {
            get { return _alpha; }
            set
            {
                _alpha = Mathf.Clamp01(value);
                bool fade = _alpha < 0.999f;
                for (int i = 0; i < _renderers.Count; i++)
                {
                    if (_renderers[i] == null) continue;
                    if (fade) PhMaterials.SetAlpha(_fade[i], _alpha);
                    _renderers[i].sharedMaterial = fade ? _fade[i] : _opaque[i];
                    _renderers[i].enabled = _alpha > 0.001f;
                }
            }
        }

        /// <summary>Hash of the world pose of the wrist and every finger joint (rounded to 0.1 mm / 0.01 deg): constant while frozen.</summary>
        public int PoseHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Hash(transform.position);
                h = h * 31 + Hash(transform.rotation.eulerAngles * 0.01f);
                foreach (var j in _joints) if (j.T != null) h = h * 31 + Hash(j.T.position) * 7 + Hash(j.T.rotation.eulerAngles * 0.01f);
                if (_rig != null) h = h * 31 + _rig.PoseHash();
                if (_handModel != null) h = h * 31 + Mathf.RoundToInt(_curl * 1000f);
                return h;
            }
        }

        private static int Hash(Vector3 v)
        {
            unchecked { return (Mathf.RoundToInt(v.x * 10000f) * 73856093) ^ (Mathf.RoundToInt(v.y * 10000f) * 19349663) ^ (Mathf.RoundToInt(v.z * 10000f) * 83492791); }
        }

        private void OnDestroy()
        {
            foreach (var m in _fade) if (m != null) Destroy(m);
        }

        private void Awake() { if (!IsBuilt && Application.isPlaying) Build(0.25f, 0.05f, 0.10f); }
    }
}
