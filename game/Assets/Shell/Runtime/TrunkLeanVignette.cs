using System;
using Opus.Sdk;
using UnityEngine;
using UnityEngine.Rendering;

namespace Opus.Shell
{
    /// <summary>
    /// Run11: the trunk-lean vignette + audio cue did not exist before this run (confirmed by run10's own
    /// screenshot description: "No vignette or any other lean-warning visual overlay is present anywhere in the
    /// frame"). This is the PRIMARY feedback per the brief -- it must work standalone with haptics OFF and no
    /// sleeve, so it is built and driven directly off <see cref="FormFeedback"/>'s own polled state
    /// (LeanWarningActive/LastLeanCm/LowConfidenceWarningActive), never through HapticClient/GateCue, which can
    /// legitimately drop everything while haptics are disabled or no device is known.
    ///
    /// Implementation: a small quad parented to the eye anchor (CenterEyeAnchor, in both real play and the
    /// SyntheticHandDriver demo), sitting just past the near clip plane and sized to comfortably exceed any
    /// realistic eye-camera FOV/aspect. Being a normal scene-space quad (not a screen-space UI/RenderFeature), it
    /// renders for ANY camera positioned/oriented like the eye anchor -- including
    /// OrchardReachPlayModeAuditTests's own cloned capture camera, which only needs to be within 2cm of the
    /// anchor's pose, not literally the eye anchor's own Camera component. A URP ScriptableRendererFeature would
    /// be the "textbook" production answer, but that needs a ScriptableRendererData asset hand-edited outside of
    /// batch-mode script access (no interactive Editor in this project's workflow) -- this needs nothing but this
    /// component and is provably visible in the batch PlayMode screenshot capture path.
    ///
    /// Comfort-safe by construction: the vignette's alpha ramps in/out over ~150ms (no snap), only tints the
    /// outer half of the VISIBLE frame (an elliptical falloff normalized to the camera frustum, see <see cref="BuildVignetteTexture"/>), and never obscures the
    /// central reach/grasp field of view.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrunkLeanVignette : MonoBehaviour
    {
        private const float FadeSeconds = 0.15f; // "fading in over ~150 ms" per the brief; used for fade-out too
        private const float QuadDistanceM = 0.12f; // clears the audit capture camera's 0.01f near clip with margin
        private const float FallbackFovDeg = 90f;   // only if no camera is found; matches the capture camera's own fallback
        private const float FallbackAspect = 16f / 9f;
        private const double AudioMinGapMs = 800.0; // mirrors HAPTIC_PROTOCOL.md's trunk_lean min-gap -- a rate limit, not a nag
        private const float ToneFrequencyHz = 480f;
        private const float ToneDurationSeconds = 0.16f;

        private FormFeedback _formFeedback;

        private Material _vignetteMaterial;
        private MeshRenderer _vignetteRenderer;
        private Material _lowConfMaterial;
        private MeshRenderer _lowConfRenderer;

        private AudioSource _audioSource;
        private AudioClip _leanToneClip;

        private float _vignetteAlpha;
        private float _lowConfAlpha;
        private double _elapsedMs;
        private double _nextLeanAudioAllowedMs;

        /// <summary>True when the lean vignette is actually being drawn this frame (alpha above the renderer's
        /// own visibility cutoff). Exposed so a test/screenshot can prove the patient would really see it, rather
        /// than only that the component exists.</summary>
        public bool IsRendering => _vignetteRenderer != null && _vignetteRenderer.enabled && _vignetteAlpha > 0.001f;

        /// <summary>Current vignette opacity, 0..~0.85. Exposed because "is it rendering" is not the same
        /// question as "would the patient see it": the alpha ramps in over ~150 ms, so anything sampling on the
        /// first active frame sees ~0.002 and a screenshot taken there looks identical to no vignette at all.
        /// That is exactly what run11's first capture did.</summary>
        public float Alpha => _vignetteAlpha;

        /// <summary>Creates and wires the vignette as a new child of <paramref name="eyeAnchor"/>. Safe to call
        /// once per scene load; returns null (no-op) if either argument is missing so a scene/test without an eye
        /// anchor degrades silently instead of throwing.</summary>
        public static TrunkLeanVignette Attach(Transform eyeAnchor, FormFeedback formFeedback)
        {
            if (eyeAnchor == null || formFeedback == null) return null;
            var go = new GameObject("TrunkLeanVignette");
            var comp = go.AddComponent<TrunkLeanVignette>();
            comp.Initialize(eyeAnchor, formFeedback);
            return comp;
        }

        private void Initialize(Transform eyeAnchor, FormFeedback formFeedback)
        {
            _formFeedback = formFeedback;
            transform.SetParent(eyeAnchor, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            BuildVignetteQuad();
            BuildLowConfidenceIndicator();
            BuildAudio();
        }

        private void Update()
        {
            _elapsedMs += Time.deltaTime * 1000.0;

            // --- Trunk-lean vignette (primary feedback) ---
            bool leanActive = _formFeedback.LeanWarningActive;
            double excessCm = leanActive ? Math.Max(0.0, _formFeedback.LastLeanCm - _formFeedback.TrunkLeanWarningCm) : 0.0;
            // Strength scaled by excess lean -- same clamp shape as HapticCueMapper.TrunkLean's 0.5-0.8 intensity
            // scaling (contracts/HAPTIC_PROTOCOL.md), just in visual-alpha units so the two additive cues read as
            // one coherent signal even when only one of them is active.
            float targetAlpha = leanActive ? Mathf.Clamp(0.35f + (float)excessCm / 20f, 0.35f, 0.85f) : 0f;
            _vignetteAlpha = Mathf.MoveTowards(_vignetteAlpha, targetAlpha, Time.deltaTime / FadeSeconds);
            SetAlpha(_vignetteMaterial, _vignetteRenderer, _vignetteAlpha);

            if (leanActive)
            {
                if (_elapsedMs >= _nextLeanAudioAllowedMs)
                {
                    _audioSource.PlayOneShot(_leanToneClip);
                    _nextLeanAudioAllowedMs = _elapsedMs + AudioMinGapMs;
                }
            }
            else
            {
                // Reopen the gate so the NEXT lean episode's very first frame plays immediately instead of
                // waiting out a stale cadence left over from a previous episode.
                _nextLeanAudioAllowedMs = 0;
            }

            // --- Low-confidence ("hand out of view") indicator ---
            bool lowConfActive = _formFeedback.LowConfidenceWarningActive;
            float targetLowConfAlpha = lowConfActive ? 0.55f : 0f;
            _lowConfAlpha = Mathf.MoveTowards(_lowConfAlpha, targetLowConfAlpha, Time.deltaTime / FadeSeconds);
            SetAlpha(_lowConfMaterial, _lowConfRenderer, _lowConfAlpha);
        }

        private void OnDestroy()
        {
            SafeDestroy(_vignetteMaterial);
            SafeDestroy(_lowConfMaterial);
            SafeDestroy(_leanToneClip);
        }

        /// <summary>Run11 fix (EditMode regression): <see cref="OrchardReachSceneController"/>.Awake runs inside
        /// EditMode tests (SeatedLayoutTests drives it via reflection, with no Play mode), and UnityEngine
        /// Object.Destroy is an error there -- "Destroy may not be called from edit mode!". Route every teardown
        /// through here so the same component works in Play mode, in EditMode tests and in the batch PlayMode
        /// capture path without any caller having to care which one it is in.</summary>
        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void BuildVignetteQuad()
        {
            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "Vignette";
            SafeDestroy(quadGo.GetComponent<Collider>());
            quadGo.transform.SetParent(transform, worldPositionStays: false);
            quadGo.transform.localPosition = new Vector3(0f, 0f, QuadDistanceM);
            quadGo.transform.localRotation = Quaternion.identity;

            // Run11 fix (the vignette was invisible in every capture even at alpha 0.30): the quad used to be
            // sized for a deliberately generous 110 deg / 2.2 aspect "bigger than any realistic eye camera".
            // That is exactly backwards for a RADIAL falloff. The falloff is keyed to the quad's own extent, so
            // oversizing it means the camera's real ~90 deg / 1.78 frustum only ever sees the quad's inner
            // region -- which is the part the falloff keeps fully transparent. The warning border sat outside
            // the visible frame. Fit the quad to the camera that will actually look through it (the eye anchor's
            // own Camera, or the capture camera that copies its FOV), with a small margin so no untinted gap
            // shows at the very edge of frame.
            var eyeCam = GetComponentInParent<Camera>() ?? Camera.main;
            float fovDeg = eyeCam != null ? eyeCam.fieldOfView : FallbackFovDeg;
            // The eye camera can report a narrower aspect than the surface actually being rendered (in batch
            // capture it reported 4:3 while the render texture is 16:9), and a quad narrower than the frame
            // shows its own border as a hard vertical seam -- visible in run11's third capture. Never go below
            // 16:9, and overshoot the width, so the quad's edge always falls outside the frame. Overshooting
            // costs only a little falloff strength at the extreme sides; a visible rectangle edge would read as
            // a rendering bug to a clinician.
            float camAspect = eyeCam != null && eyeCam.aspect > 0.01f ? eyeCam.aspect : FallbackAspect;
            float aspect = Mathf.Max(camAspect, FallbackAspect);
            const float fitMargin = 1.08f;
            const float widthOvershoot = 1.25f; // also feeds the falloff below, so it stays frame-relative
            float halfHeight = QuadDistanceM * Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad) * fitMargin;
            float halfWidth = halfHeight * aspect * widthOvershoot;
            quadGo.transform.localScale = new Vector3(halfWidth * 2f, halfHeight * 2f, 1f);

            // The falloff has to be expressed in terms of the frame the patient actually SEES, not the quad,
            // because the quad is deliberately oversized to hide its own border. Pass in what fraction of the
            // quad is on-screen so the ramp reaches full strength exactly at the frame edge. Measuring against
            // the quad instead left the warning at ~5% opacity at the edge of frame -- present in the render,
            // useless as a safety cue.
            _vignetteMaterial = BuildTransparentUnlitMaterial(
                BuildVignetteTexture(1f / widthOvershoot, 1f / fitMargin), new Color(1f, 0.62f, 0.15f, 0f));
            _vignetteRenderer = ConfigureRenderer(quadGo, _vignetteMaterial);
        }

        /// <summary>A small soft-glow badge, lower-center of view, for the "bring your hand back into view" cue
        /// -- deliberately gentle (no text/iconography, which would need a font/canvas pipeline this batch-only
        /// project doesn't have wired up yet) but clearly a distinct color/shape/position from the lean vignette
        /// so the two cues are never confused for one another.</summary>
        private void BuildLowConfidenceIndicator()
        {
            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "LowConfidenceIndicator";
            SafeDestroy(quadGo.GetComponent<Collider>());
            quadGo.transform.SetParent(transform, worldPositionStays: false);
            // Slightly nearer than the vignette (so it draws on top of it if both are ever active at once) and
            // offset toward the bottom of the view, roughly where a dropped/out-of-frame hand would have been.
            quadGo.transform.localPosition = new Vector3(0f, -0.045f, QuadDistanceM - 0.01f);
            quadGo.transform.localRotation = Quaternion.identity;
            quadGo.transform.localScale = new Vector3(0.09f, 0.09f, 1f);

            _lowConfMaterial = BuildTransparentUnlitMaterial(BuildSoftCircleTexture(), new Color(0.55f, 0.85f, 1f, 0f));
            _lowConfRenderer = ConfigureRenderer(quadGo, _lowConfMaterial);
        }

        private static MeshRenderer ConfigureRenderer(GameObject quadGo, Material material)
        {
            var mr = quadGo.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.material = material;
            return mr;
        }

        private static Material BuildTransparentUnlitMaterial(Texture2D tex, Color baseColor)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            // Configure URP Unlit for alpha-blended transparency via script (no .shadergraph/.mat asset edit
            // needed, so this works headlessly in batch mode): Transparent surface, Alpha blend, no depth write,
            // no backface culling (the quad's exact winding relative to the eye anchor's forward axis doesn't
            // matter this way -- it must be visible regardless of which side faces the camera).
            mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
            mat.SetFloat("_Blend", 0f);   // 0 = Alpha
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_OFF");
            mat.renderQueue = (int)RenderQueue.Transparent + 100; // draw after ordinary transparents (overlay)
            mat.mainTexture = tex;
            mat.color = baseColor;
            return mat;
        }

        private static void SetAlpha(Material mat, MeshRenderer renderer, float alpha)
        {
            if (mat == null) return;
            var c = mat.color;
            c.a = alpha;
            mat.color = c;
            if (renderer != null) renderer.enabled = alpha > 0.001f;
        }

        /// <summary>Radial falloff: fully transparent through the central ~55% (where the patient reaches/looks)
        /// so the vignette can never obscure the task itself, ramping up to <paramref name="baseColor"/>'s tint
        /// toward the corners/edges -- a comfort-safe warning border, not a screen-covering wash.</summary>
        private static Texture2D BuildVignetteTexture(float visibleFracX, float visibleFracY)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Normalize to the VISIBLE frame: ex/ey reach 1.0 at the edge of what the camera sees, not
                    // at the edge of the (deliberately larger) quad. Elliptical, so the ramp is even on all
                    // four sides regardless of aspect.
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float ny = ((y + 0.5f) / size) * 2f - 1f;
                    float ex = nx / Mathf.Max(0.01f, visibleFracX);
                    float ey = ny / Mathf.Max(0.01f, visibleFracY);
                    float d = Mathf.Sqrt(ex * ex + ey * ey);
                    // Fully clear through the central ~60% of the frame (the reach/grasp field of view is never
                    // obscured), ramping to full tint by the frame edge. Tuned against the run11 capture: at 0.50
                    // the tint climbed far enough up the ground plane to be heavier than a peripheral warning
                    // should be.
                    float a = Mathf.Clamp01((d - 0.60f) / 0.40f);
                    a = a * a; // ease-in: soft at the inner edge, fuller toward the border
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Soft radial glow: bright center fading to fully transparent at the edge.</summary>
        private static Texture2D BuildSoftCircleTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / radius;
                    float a = 1f - Mathf.Clamp01(d);
                    a *= a;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private void BuildAudio()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D -- a safety cue must be audible regardless of head orientation
            _audioSource.volume = 0.5f;
            _leanToneClip = GenerateTone(ToneFrequencyHz, ToneDurationSeconds);
        }

        /// <summary>Procedurally generates a short, soft sine-wave tone (fast fade-in, slower fade-out envelope
        /// so there is no audible click at either end) -- used when no audio asset exists for the cue, per the
        /// brief.</summary>
        private static AudioClip GenerateTone(float frequencyHz, float durationSeconds)
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
            var samples = new float[sampleCount];
            const float attackSeconds = 0.015f;
            const float releaseSeconds = 0.05f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Min(1f, t / attackSeconds) * Mathf.Min(1f, (durationSeconds - t) / releaseSeconds);
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequencyHz * t) * 0.35f * envelope;
            }
            var clip = AudioClip.Create("OpusTrunkLeanTone", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
