using UnityEngine;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// Drives the "scene goes dark except the ruler" look of the drift probe (FR-VR-04). The probe elements (ruler,
    /// fingertip dot) use unlit materials, so scaling every light, the ambient trilight and the fog to zero leaves
    /// only them visible. Level01: 1 = fully lit room, 0 = dark. Fades over fadeSeconds.
    /// </summary>
    public sealed class DarkenController : MonoBehaviour
    {
        public Light[] lights = new Light[0];
        public float fadeSeconds = 0.6f;
        public Camera cameraToTint;

        private float[] _baseIntensity;
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

        private void Apply()
        {
            float l = Level01;
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = _baseIntensity[i] * l;
            RenderSettings.ambientSkyColor = _ambSky * l; RenderSettings.ambientEquatorColor = _ambEq * l; RenderSettings.ambientGroundColor = _ambGround * l;
            RenderSettings.ambientIntensity = _ambIntensity * l; RenderSettings.reflectionIntensity = _reflection * l;
            RenderSettings.fogColor = _fog * l;
            if (cameraToTint != null) cameraToTint.backgroundColor = _camBg * l;
        }

        private void OnDisable()
        {
            // never leave the room dark when the component goes away
            if (_captured) { Level01 = 1f; _target = 1f; Apply(); }
        }
    }
}
