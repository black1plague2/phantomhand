using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 item 5: the small head-locked-ish HUD: two chips ("Sleeve offline", "EMG offline"; amber, non-disruptive, only
    /// shown while something is missing), the phase word with the time left, and a spectator block that stays hidden unless
    /// the both-hands pinch (3 s) switched it on. Draws a <see cref="HudModel"/>.
    /// </summary>
    public sealed class PhHudPanel : MonoBehaviour
    {
        // U4 fix (UiPanelTests.Hud_TextFits): the phase word and the time left are stacked instead of side by side ("Watch the hand" /
        // "A few questions" and the HI words wrapped in the old 190 mm column), and the spectator block (3 lines, HI ~82 mm) gets a taller box.
        public const float WidthMm = 360f, HeightMm = 260f, MainHeightMm = 144f;

        private GameObject _chipSleeve, _chipEmg, _spectatorBox, _pinchBar;
        private Text _chipSleeveText, _chipEmgText, _phase, _time, _spectator;
        private Image _pinchFill;

        public Text PhaseText { get { return _phase; } }
        public Text TimeText { get { return _time; } }
        public Text SpectatorText { get { return _spectator; } }
        public bool SleeveChipShown { get { return _chipSleeve != null && _chipSleeve.activeSelf; } }
        public bool EmgChipShown { get { return _chipEmg != null && _chipEmg.activeSelf; } }
        public bool SpectatorShown { get { return _spectatorBox != null && _spectatorBox.activeSelf; } }

        public void Build(Camera worldCamera)
        {
            var rt = PhUiKit.PreparePanel(transform, WidthMm, HeightMm, worldCamera, attachPoke: false);
            PhUiKit.Box(rt, "Backdrop", 0, 0, WidthMm, MainHeightMm, PhUiKit.PanelBg);
            PhUiKit.Box(rt, "AccentBar", 0, 0, WidthMm, 5, PhUiKit.Oxblood);
            _chipSleeve = Chip(rt, "ChipSleeve", 12, 14, 164, out _chipSleeveText);
            _chipEmg = Chip(rt, "ChipEmg", 184, 14, 164, out _chipEmgText);
            _phase = PhUiKit.Label(rt, "Phase", "", 32, PhUiKit.Ink, 16, 54, WidthMm - 32, 48, TextAnchor.MiddleLeft, FontStyle.Bold);
            _time = PhUiKit.Label(rt, "Time", "", 26, PhUiKit.InkDim, 16, 102, WidthMm - 32, 38, TextAnchor.MiddleLeft);
            var box = PhUiKit.Box(rt, "SpectatorBox", 0, MainHeightMm + 4, WidthMm, HeightMm - MainHeightMm - 4, PhUiKit.PanelBg);
            _spectatorBox = box.gameObject;
            _spectator = PhUiKit.Label(box.transform, "Spectator", "", 22, PhUiKit.Ink, 16, 8, WidthMm - 32, HeightMm - MainHeightMm - 20, TextAnchor.UpperLeft);
            _spectatorBox.SetActive(false);
            var bar = PhUiKit.Box(rt, "PinchBar", 0, MainHeightMm - 6, WidthMm, 6, PhUiKit.PanelLine);
            _pinchBar = bar.gameObject; _pinchFill = PhUiKit.Box(bar.transform, "PinchFill", 0, 0, 0, 6, PhUiKit.Info);
            _pinchBar.SetActive(false);
        }

        private static GameObject Chip(RectTransform parent, string name, float x, float y, float w, out Text label)
        {
            var img = PhUiKit.Box(parent, name, x, y, w, 36, new Color(0.20f, 0.14f, 0.05f, 1f));
            label = PhUiKit.Label(img.transform, "Label", "", 20, PhUiKit.Warn, 0, 0, w, 36, TextAnchor.MiddleCenter, FontStyle.Bold);
            return img.gameObject;
        }

        public void Apply(HudModel m, float pinchProgress01)
        {
            if (_phase == null) return;
            _chipSleeve.SetActive(m.SleeveOffline); _chipEmg.SetActive(m.EmgOffline);
            PhUiKit.SetText(_chipSleeveText, PhStrings.Get("hud_sleeve_offline", m.Lang));
            PhUiKit.SetText(_chipEmgText, PhStrings.Get("hud_emg_offline", m.Lang));
            PhUiKit.SetText(_phase, m.PhaseText); PhUiKit.SetText(_time, m.TimeLeftText);
            _spectatorBox.SetActive(m.SpectatorVisible);
            PhUiKit.SetText(_spectator, m.SpectatorText);
            bool pinching = pinchProgress01 > 0.02f;
            _pinchBar.SetActive(pinching);
            if (pinching) PhUiKit.Place(_pinchFill.rectTransform, 0, 0, WidthMm * Mathf.Clamp01(pinchProgress01), 6);
        }
    }
}
