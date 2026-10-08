using System.Collections.Generic;
using UnityEngine;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// Drives the "scene goes dark except the ruler" look of the drift probe (FR-VR-04). The probe elements (ruler,
    /// fingertip dot) use unlit materials, so scaling every light, the ambient trilight and the fog to zero leaves
    /// only them visible. Level01: 1 = fully lit room, 0 = dark. Fades over fadeSeconds.
    /// Things of the room that shine by themselves (the pendant lamp's unlit shade and emissive bulb, the unlit window glow) would stay visible, so they are
    /// listed in <see cref="renderers"/>: their _BaseColor and _EmissionColor follow the same level through a MaterialPropertyBlock (the shared materials are
    /// never touched, and at level 1 the block is removed again).
    /// </summary>
    public sealed class DarkenController : MonoBehaviour
    {
        public Light[] lights = new Light[0];
        public Renderer[] renderers = new Renderer[0];
        public float fadeSeconds = 0.6f;
        public Camera cameraToTint;

        private struct Glow { public Renderer R; public int Slot; public bool HasBase, HasEmission; public Color Base, Emission; }

        private float[] _baseIntensity;
        private Glow[] _glow = new Glow[0];
        private MaterialPropertyBlock _block;
        private Color _ambSky, _ambEq, _ambGround, _fog, _camBg;
        private float _ambIntensity = 1f, _reflection = 1f;
        private bool _captured;
        private float _target = 1f;

        public float Level01 { get; private set; } = 1f;
        public bool IsDark { get { return _target < 0.5f; } }

        private void Awake() { Capture(); }

        /// <summary>Remember the lit look. Safe to call again after the lighting changes.</summary>
        public void Capture()
        {
            _baseIntensity = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) _baseIntensity[i] = lights[i] != null ? lights[i].intensity : 0f;
            _ambSky = RenderSettings.ambientSkyColor; _ambEq = RenderSettings.ambientEquatorColor; _ambGround = RenderSettings.ambientGroundColor;
            _ambIntensity = RenderSettings.ambientIntensity; _reflection = RenderSettings.reflectionIntensity;
            _fog = RenderSettings.fogColor;
            _camBg = cameraToTint != null ? cameraToTint.backgroundColor : Color.black;
            var glow = new List<Glow>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var g = new Glow { R = r, Slot = i, HasBase = m.HasProperty("_BaseColor"), HasEmission = m.HasProperty("_EmissionColor") };
                    if (g.HasBase) g.Base = m.GetColor("_BaseColor");
                    if (g.HasEmission) g.Emission = m.GetColor("_EmissionColor");
                    if (g.HasBase || g.HasEmission) glow.Add(g);
                }
            }
            _glow = glow.ToArray();
            _captured = true;
            Level01 = 1f; _target = 1f;
        }

        public void SetDark(bool dark, bool immediate = false)
        {
            if (!_captured) Capture();
            _target = dark ? 0f : 1f;
            if (immediate || fadeSeconds <= 0f) { Level01 = _target; Apply(); }
        }

        private void Update()
        {
            if (!_captured || Mathf.Approximately(Level01, _target)) return;
            float step = fadeSeconds <= 0f ? 1f : Time.unscaledDeltaTime / fadeSeconds;
            Level01 = Mathf.MoveTowards(Level01, _target, step);
            Apply();
        }

        /// <summary>RGB times the level, alpha kept: the same scaling the ambient colours and the fog get in <see cref="Apply"/>.</summary>
        public static Color Scale(Color c, float level)
        {
            return new Color(c.r * level, c.g * level, c.b * level, c.a);
        }

        private void Apply()
        {
            float l = Level01;
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = _baseIntensity[i] * l;
            RenderSettings.ambientSkyColor = _ambSky * l; RenderSettings.ambientEquatorColor = _ambEq * l; RenderSettings.ambientGroundColor = _ambGround * l;
            RenderSettings.ambientIntensity = _ambIntensity * l; RenderSettings.reflectionIntensity = _reflection * l;
            RenderSettings.fogColor = _fog * l;
            if (cameraToTint != null) cameraToTint.backgroundColor = _camBg * l;
            ApplyGlow(l);
        }

        private void ApplyGlow(float l)
        {
            if (_glow.Length == 0) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (var g in _glow)
            {
                if (g.R == null) continue;
                if (l >= 1f) { g.R.SetPropertyBlock(null, g.Slot); continue; }          // lit again: back to the plain material values
                _block.Clear();
                if (g.HasBase) _block.SetColor("_BaseColor", Scale(g.Base, l));
                if (g.HasEmission) _block.SetColor("_EmissionColor", Scale(g.Emission, l));
                g.R.SetPropertyBlock(_block, g.Slot);
            }
        }

        private void OnDisable()
        {
            // never leave the room dark when the component goes away
            if (_captured) { Level01 = 1f; _target = 1f; Apply(); }
        }
    }
}
