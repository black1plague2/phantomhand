using Opus.Games.PhantomHand.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Shell
{
    /// <summary>
    /// What the headset shows before a run: the three links and how to start. Without it a headset with no operator app next to it
    /// shows a silent room, and nothing says that a both-hands pinch starts the run. English only: it is read by whoever sets the
    /// headset up, before the run's language is known. Drawn where the witness panel appears at the end of the run.
    /// </summary>
    public sealed class PhantomStandbyCard
    {
        public const float WidthMm = 640f, HeightMm = 440f;

        private readonly GameObject _go;
        private readonly Text _title, _body, _foot;
        private readonly Image _hold;

        public PhantomStandbyCard(Vector3 position, Quaternion rotation, Camera worldCamera)
        {
            _go = new GameObject("StandbyCard", typeof(RectTransform));
            _go.transform.SetPositionAndRotation(position, rotation);
            var rt = PhUiKit.PreparePanel(_go.transform, WidthMm, HeightMm, worldCamera, attachPoke: false);
            PhUiKit.Box(rt, "Backdrop", 0, 0, WidthMm, HeightMm, PhUiKit.PanelBg);
            PhUiKit.Box(rt, "AccentBar", 0, 0, WidthMm, 5, PhUiKit.Oxblood);
            _title = PhUiKit.Label(rt, "Title", "", 34, PhUiKit.Ink, 28, 22, WidthMm - 56, 52, TextAnchor.MiddleLeft, FontStyle.Bold);
            _body = PhUiKit.Label(rt, "Body", "", 24, PhUiKit.Ink, 28, 92, WidthMm - 56, HeightMm - 172, TextAnchor.UpperLeft);
            _foot = PhUiKit.Label(rt, "Foot", "", 18, PhUiKit.InkDim, 28, HeightMm - 62, WidthMm - 56, 44, TextAnchor.MiddleLeft);
            PhUiKit.Box(rt, "HoldTrack", 0, HeightMm - 8, WidthMm, 8, PhUiKit.PanelLine);
            _hold = PhUiKit.Box(rt, "HoldFill", 0, HeightMm - 8, 0, 8, PhUiKit.Info);
        }

        public bool Shown { get { return _go != null && _go.activeSelf; } }
        public string BodyText { get { return _body.text; } }

        /// <param name="hold01">how far the both-hands pinch has been held (the bar along the bottom edge).</param>
        public void Show(string title, string body, string foot, float hold01)
        {
            if (_go == null) return;   // the scene went away
            if (!_go.activeSelf) _go.SetActive(true);
            PhUiKit.SetText(_title, title); PhUiKit.SetText(_body, body); PhUiKit.SetText(_foot, foot);
            PhUiKit.Place(_hold.rectTransform, 0, HeightMm - 8, WidthMm * Mathf.Clamp01(hold01), 8);
        }

        public void Hide() { if (_go != null && _go.activeSelf) _go.SetActive(false); }
    }
}
