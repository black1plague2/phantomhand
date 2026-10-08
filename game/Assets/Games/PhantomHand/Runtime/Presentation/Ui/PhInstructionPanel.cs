using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 items 1-2: the instruction text for calibration and the drift probe, with a status line and a progress ring
    /// (2 s for the calibration hold, 1.5 s for the probe hold). In the dark probe the backdrop is dropped so only text and
    /// ring float in the dark (uGUI is unlit, so the text stays readable while the room is black).
    /// </summary>
    public sealed class PhInstructionPanel : MonoBehaviour
    {
        public const float WidthMm = 640f, HeightMm = 260f;

        private Image _backdrop, _accent, _ring, _ringTrack;
        private Text _title, _status;

        public Text TitleText { get { return _title; } }
        public Text StatusText { get { return _status; } }
        public float RingFill { get { return _ring == null ? 0f : _ring.fillAmount; } }
        public Color RingColor { get { return _ring == null ? Color.clear : _ring.color; } }

        public void Build(Camera worldCamera)
        {
            var rt = PhUiKit.PreparePanel(transform, WidthMm, HeightMm, worldCamera, attachPoke: false);
            _backdrop = PhUiKit.Box(rt, "Backdrop", 0, 0, WidthMm, HeightMm, PhUiKit.PanelBg);
            _accent = PhUiKit.Box(rt, "AccentBar", 0, 0, WidthMm, 6, PhUiKit.Oxblood);
            _title = PhUiKit.Label(rt, "Title", "", 27, PhUiKit.Ink, 30, 20, WidthMm - 60, 160, TextAnchor.UpperLeft, FontStyle.Bold);
            _ringTrack = PhUiKit.Box(rt, "RingTrack", 30, 190, 60, 60, PhUiKit.PanelLine);
            _ringTrack.sprite = PhUiKit.RingSprite();
            _ring = PhUiKit.Ring(rt, "Ring", 30, 190, 60, PhUiKit.Info, 0f);
            _status = PhUiKit.Label(rt, "Status", "", 30, PhUiKit.Ink, 106, 192, WidthMm - 136, 56, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        public void Show(string title, string status, float progress01, Color ringColor, bool backdrop)
        {
            if (_title == null) return;
            PhUiKit.SetText(_title, title); PhUiKit.SetText(_status, status); _status.color = ringColor;
            _ring.fillAmount = Mathf.Clamp01(progress01); _ring.color = ringColor;
            _backdrop.enabled = backdrop; _accent.enabled = backdrop; _ringTrack.enabled = backdrop;
            gameObject.SetActive(true);
        }

        public void Hide() { gameObject.SetActive(false); }
    }
}
