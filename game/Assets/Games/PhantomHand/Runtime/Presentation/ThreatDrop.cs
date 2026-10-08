using System;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>Lives on the stone: forwards physics callbacks to the owner and counts fixed steps (physics time).</summary>
    public sealed class StoneBody : MonoBehaviour
    {
        public ThreatDrop owner;
        private void OnCollisionEnter(Collision c) { if (owner != null) owner.NotifyCollision(c); }
        private void FixedUpdate() { if (owner != null) owner.CountStep(Time.fixedDeltaTime); }
    }

    /// <summary>
    /// The threat (PRD phase 4): a 10-12 cm stone hangs 40 cm above the virtual hand, a 0.6 s telegraph (growing blob shadow,
    /// trembling stone, creak) precedes the release, then a Rigidbody (continuous, interpolated; Transform.position is never
    /// touched after release) falls onto the virtual hand's compound collider. The FIRST collision with the hand is the
    /// impact: its time is physics time mapped onto the session clock (release time + fixed steps * fixedDeltaTime), followed
    /// by thud + dust; the stone then rolls off and fades. No hand hit within 2 s of release gives ok:false. If the real hand
    /// moved under the drop point the drop is aborted with a log line (nothing falls) and reported as ok:false.
    /// </summary>
    public sealed class ThreatDrop : MonoBehaviour
    {
        public Material stoneMaterial, shadowMaterial, dustMaterial;
        public AudioSource thudSource, creakSource;

        public const float StoneDiameterM = 0.11f;
        public const double TelegraphMs = 600, MissTimeoutMs = 2000, FadeStartAfterImpactMs = 1000, FadeDurationMs = 800;
        public const float RealHandClearanceM = 0.09f;
        /// <summary>Half the old volume: a loud thud startles people whether or not the hand feels theirs, which blurs the in-step against delayed difference.</summary>
        public const float ThudVolume = 0.5f, CreakVolume = 0.5f;

        public enum State { Idle, Telegraph, Falling, Settled, Done }

        /// <summary>(impact session ms, impact position or null, ok)</summary>
        public event Action<double, Vector3?, bool> OnImpact;

        /// <summary>Horizontal clearance check: where the real hand (palm) is, or null when unknown.</summary>
        public Func<Vector3?> RealHandPosition;

        public State Phase { get; private set; } = State.Idle;
        public bool IsBuilt { get; private set; }
        public Rigidbody Body { get; private set; }
        public Vector3 StonePosition { get { return Body != null ? Body.position : Vector3.zero; } }
        public double ReleaseMs { get; private set; }
        public int Aborted { get; private set; }

        private Transform _stone, _shadow;
        private MeshRenderer _stoneRenderer, _shadowRenderer;
        private Material _stoneFade, _shadowFade, _stoneOpaque;
        private Vector3 _dropPoint, _surfacePoint;
        private double _startMs, _impactSessionMs;
        private int _steps;
        private float _fixedDt = 0.02f;
        private Vector3? _impactPos;
        private static Texture2D _disc;

        public void Build()
        {
            if (IsBuilt) return;
            var stoneMat = stoneMaterial != null ? stoneMaterial : PhMaterials.Make("PH_Stone", new Color(0.46f, 0.45f, 0.43f), 0.12f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "Stone";
            go.transform.SetParent(transform, false);
            // 3D stone (PhModels.Stone, unit mean diameter): swap mesh + textured material, keep the sphere collider / Rigidbody behaviour
            var stoneModel = PhModels.Load(PhModels.Stone);
            var modelMesh = stoneModel != null ? stoneModel.GetComponentInChildren<MeshFilter>(true) : null;
            var modelRend = stoneModel != null ? stoneModel.GetComponentInChildren<MeshRenderer>(true) : null;
            if (modelMesh != null && modelMesh.sharedMesh != null && modelRend != null && modelRend.sharedMaterial != null)
            {
                go.GetComponent<MeshFilter>().sharedMesh = modelMesh.sharedMesh;
                stoneMat = modelRend.sharedMaterial;
            }
            else go.GetComponent<MeshFilter>().sharedMesh = RockMesh(go.GetComponent<MeshFilter>().sharedMesh);
            var col = go.GetComponent<SphereCollider>();
            col.radius = 0.5f * 0.92f;
            col.material = new PhysicsMaterial("ph_stone") { bounciness = 0.08f, dynamicFriction = 0.7f, staticFriction = 0.8f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            go.transform.localScale = Vector3.one * StoneDiameterM;
            _stoneRenderer = go.GetComponent<MeshRenderer>(); _stoneRenderer.sharedMaterial = stoneMat;
            _stoneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _stoneRenderer.receiveShadows = false;
            Body = go.AddComponent<Rigidbody>();
            Body.mass = 0.5f; Body.linearDamping = 0.05f; Body.angularDamping = 0.6f;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.isKinematic = true;
            go.AddComponent<StoneBody>().owner = this;
            _stone = go.transform;
            _stoneOpaque = stoneMat;
            _stoneFade = PhMaterials.FadeCopy(stoneMat);

            var sh = GameObject.CreatePrimitive(PrimitiveType.Quad); sh.name = "BlobShadow"; PhMaterials.Strip(sh);
            sh.transform.SetParent(transform, false);
            sh.transform.rotation = Quaternion.Euler(90, 0, 0);
            var smat = shadowMaterial != null ? shadowMaterial : PhMaterials.Make("PH_Shadow", new Color(0, 0, 0, 0.6f), 0f, unlit: true);
            _shadowFade = PhMaterials.FadeCopy(smat);
            if (_shadowFade.HasProperty("_BaseMap")) _shadowFade.SetTexture("_BaseMap", SoftDisc());
            _shadowFade.mainTexture = SoftDisc();
            _shadowRenderer = sh.GetComponent<MeshRenderer>(); _shadowRenderer.sharedMaterial = _shadowFade;
            _shadow = sh.transform;
            _stone.gameObject.SetActive(false); _shadow.gameObject.SetActive(false);
            IsBuilt = true;
        }

        private static Mesh RockMesh(Mesh src)
        {
            var m = UnityEngine.Object.Instantiate(src); m.name = "ph_rock";
            var v = m.vertices; var n = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var d = v[i].normalized;
                float k = 1f + 0.16f * (Mathf.PerlinNoise(d.x * 2.1f + 3.7f, d.y * 2.1f + d.z * 1.3f + 1.1f) - 0.5f) * 2f
                             + 0.06f * (Mathf.PerlinNoise(d.z * 5.3f + 9.2f, d.x * 4.7f + d.y * 3.1f) - 0.5f) * 2f;
                if (d.y < -0.5f) k *= 0.93f;
                v[i] = d * 0.5f * k; n[i] = d;
            }
            m.vertices = v; m.normals = n; m.RecalculateBounds();
            return m;
        }

        private static Texture2D SoftDisc()
        {
            if (_disc != null) return _disc;
            const int N = 64;
            _disc = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ph_disc" };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f - 0.5f, N / 2f - 0.5f)) / (N / 2f);
                    float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                    _disc.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            _disc.Apply();
            return _disc;
        }

        // ---- lifecycle -----------------------------------------------------------------------------------------------

        /// <summary>Starts the telegraph. dropPoint = where the stone hangs (40 cm above the hand), surfacePoint = hand top below it.
        /// Returns false (and reports ok:false) when the real hand is under the drop point.</summary>
        public bool Begin(Vector3 dropPoint, Vector3 surfacePoint, double nowMs)
        {
            if (!IsBuilt) Build();
            Cancel();
            _dropPoint = dropPoint; _surfacePoint = surfacePoint; _startMs = nowMs; _steps = 0; _impactPos = null;
            if (RealHandUnderDrop())
            {
                Aborted++;
                Phase = State.Done;
                var h0 = OnImpact; if (h0 != null) h0(nowMs, null, false);
                return false;
            }
            Phase = State.Telegraph;
            Body.isKinematic = false; Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;   // velocities can only be set on a dynamic body
            Body.isKinematic = true;
            _stone.gameObject.SetActive(true); _shadow.gameObject.SetActive(true);
            ResetStoneMaterial();
            Body.position = dropPoint; _stone.position = dropPoint; _stone.rotation = Quaternion.identity;
            _shadow.position = surfacePoint + Vector3.up * 0.003f;
            UpdateShadow(0f);
            if (creakSource != null) ProceduralSfx.Play(creakSource, ProceduralSfx.Creak(), CreakVolume);
            return true;
        }

        private void ResetStoneMaterial()
        {
            _stoneRenderer.sharedMaterial = _stoneOpaque;
            _stone.localScale = Vector3.one * StoneDiameterM;
        }

        private bool RealHandUnderDrop()
        {
            if (RealHandPosition == null) return false;
            var p = RealHandPosition();
            if (!p.HasValue) return false;
            float dx = p.Value.x - _dropPoint.x, dz = p.Value.z - _dropPoint.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist >= RealHandClearanceM) return false;
            Debug.Log("[PhantomHand] threat aborted: real hand is " + (dist * 100f).ToString("F1") + " cm from the point under the drop (needs >= " + (RealHandClearanceM * 100f).ToString("F0") + " cm)");
            return true;
        }

        public void Cancel()
        {
            if (!IsBuilt) return;
            Phase = State.Idle;
            if (Body != null) Body.isKinematic = true;
            _stone.gameObject.SetActive(false); _shadow.gameObject.SetActive(false);
        }

        public void CountStep(float dt) { _steps++; _fixedDt = dt; }

        public void Tick(double nowMs)
        {
            if (!IsBuilt) return;
            switch (Phase)
            {
                case State.Telegraph:
                    {
                        float u = (float)Math.Min(1.0, (nowMs - _startMs) / TelegraphMs);
                        UpdateShadow(u);
                        // trembling, growing with u (pre-release the stone is kinematic, so moving it directly is fine)
                        float amp = 0.0025f * u;
                        var jit = new Vector3(Mathf.Sin((float)nowMs * 0.09f), Mathf.Sin((float)nowMs * 0.13f + 1f) * 0.4f, Mathf.Cos((float)nowMs * 0.11f)) * amp;
                        Body.position = _dropPoint + jit; _stone.position = _dropPoint + jit;
                        if (nowMs - _startMs >= TelegraphMs) Release(nowMs);
                        break;
                    }
                case State.Falling:
                    if (nowMs - ReleaseMs >= MissTimeoutMs)
                    {
                        Phase = State.Settled; _impactSessionMs = nowMs; _impactPos = null;
                        var h = OnImpact; if (h != null) h(nowMs, null, false);
                    }
                    break;
                case State.Settled:
                    {
                        double since = nowMs - _impactSessionMs;
                        if (since >= FadeStartAfterImpactMs)
                        {
                            float a = 1f - (float)Math.Min(1.0, (since - FadeStartAfterImpactMs) / FadeDurationMs);
                            _stoneRenderer.sharedMaterial = _stoneFade;
                            PhMaterials.SetAlpha(_stoneFade, a);
                            if (a <= 0f) { _stone.gameObject.SetActive(false); Phase = State.Done; }
                        }
                        break;
                    }
            }
        }

        private void Release(double nowMs)
        {
            if (RealHandUnderDrop())
            {
                Aborted++;
                Cancel(); Phase = State.Done;
                var h0 = OnImpact; if (h0 != null) h0(nowMs, null, false);
                return;
            }
            _shadowRenderer.enabled = true;
            Phase = State.Falling; ReleaseMs = nowMs; _steps = 0;
            Body.position = _dropPoint; _stone.position = _dropPoint;
            Body.isKinematic = false;
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = UnityEngine.Random.insideUnitSphere * 0.5f;
        }

        private void UpdateShadow(float u)
        {
            float d = Mathf.Lerp(0.06f, 0.17f, u);
            _shadow.localScale = new Vector3(d, d, 1f);
            PhMaterials.SetAlpha(_shadowFade, Mathf.Lerp(0.25f, 0.85f, u));
        }

        internal void NotifyCollision(Collision c)
        {
            if (Phase != State.Falling) return;
            if (c.collider == null || c.collider.GetComponentInParent<VirtualHandHit>() == null) return;
            Vector3 pos = c.contactCount > 0 ? c.GetContact(0).point : Body.position;
            double impact = ReleaseMs + _steps * _fixedDt * 1000.0;
            Phase = State.Settled; _impactSessionMs = impact; _impactPos = pos;
            _shadow.gameObject.SetActive(false);
            if (thudSource != null) ProceduralSfx.Play(thudSource, ProceduralSfx.Thud(), ThudVolume, 0.95f);
            EmitDust(pos);
            // roll off: nudge away from the hand centre
            var away = Body.position - c.collider.bounds.center; away.y = 0;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.left;
            Body.AddForce(away.normalized * 0.10f, ForceMode.Impulse);
            var h = OnImpact; if (h != null) h(impact, pos, true);
        }

        public void EmitDust(Vector3 pos)
        {
            var go = new GameObject("Dust"); go.transform.SetParent(transform, false); go.transform.position = pos + Vector3.up * 0.01f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // a new system autoplays; settings can only change while stopped
            var main = ps.main; main.loop = false; main.duration = 0.2f; main.startLifetime = 0.7f; main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.04f); main.startColor = new Color(0.78f, 0.72f, 0.62f, 0.6f);
            main.gravityModifier = -0.02f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.025f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0.7f, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
            var pr = go.GetComponent<ParticleSystemRenderer>();
            Material dm = dustMaterial;
            if (dm == null)
            {
                var shd = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
                dm = new Material(shd); dm.color = new Color(0.78f, 0.72f, 0.62f, 1f);
                dm = PhMaterials.FadeCopy(dm);
            }
            // the dust is a soft disc, so its material has to blend whatever was assigned: an opaque one draws squares (seen in the first frame sequence, 8 Oct 2026)
            dm = dm.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ? new Material(dm) : PhMaterials.FadeCopy(dm);
            if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", SoftDisc());
            pr.sharedMaterial = dm;
            ps.Play();
            if (Application.isPlaying) Destroy(go, 1.5f);
        }

        private void OnDestroy()
        {
            if (_stoneFade != null) Destroy(_stoneFade);
            if (_shadowFade != null) Destroy(_shadowFade);
        }
    }
}
