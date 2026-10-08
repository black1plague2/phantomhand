using System;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 item 3 (FR-VR-05): one questionnaire item at a time, seven poke buttons (-3..+3, each 6.4 cm wide), the two scale
    /// anchors, a Back button and a progress count. The panel takes the <see cref="Questionnaire"/> (so the item list is the
    /// caller's: q1-q3 now, q4 after each condition, a q5 slot) and two callbacks. A press highlights the button and the
    /// answer is handed over 0.4 s later (auto-advance); presses while an answer is pending are ignored.
    /// Clock-driven: call <see cref="Tick"/> with session ms every frame.
    /// </summary>
    public sealed class PhQuestionnairePanel : MonoBehaviour
    {
        public const float WidthMm = 560f, HeightMm = 300f;
        public const float ButtonMm = 64f, ButtonGapMm = 12f;
        public const double AutoAdvanceMs = 400;

        public string Lang = PhStrings.En;

        private Questionnaire _q;
        private Func<int, bool> _onAnswer;
        private Func<bool> _onBack;
        private Text _item, _progress, _low, _high;
        private Button _back;
        private readonly Button[] _buttons = new Button[7];
        private readonly Image[] _faces = new Image[7];
        private int? _pendingValue;
        private double _pendingAtMs, _nowMs;
        private string _shownId;

        public Button[] Buttons { get { return _buttons; } }
        public Button BackButton { get { return _back; } }
        public Text ItemText { get { return _item; } }
        public Text ProgressText { get { return _progress; } }
        public string ShownItemId { get { return _shownId; } }
        public bool HasPending { get { return _pendingValue.HasValue; } }

        public void Build(Camera worldCamera, bool attachPoke)
        {
            var rt = PhUiKit.PreparePanel(transform, WidthMm, HeightMm, worldCamera, attachPoke);
            PhUiKit.Box(rt, "Backdrop", 0, 0, WidthMm, HeightMm, PhUiKit.PanelBg);
            PhUiKit.Box(rt, "AccentBar", 0, 0, WidthMm, 6, PhUiKit.Oxblood);
            _back = PhUiKit.PokeButton(rt, "Back", "", 24, 20, 20, 90, 44, PhUiKit.ButtonBg, PhUiKit.Ink);
            _back.onClick.AddListener(() => PressBack());
            _progress = PhUiKit.Label(rt, "Progress", "", 22, PhUiKit.InkDim, WidthMm - 160, 28, 140, 30, TextAnchor.MiddleRight);
            _item = PhUiKit.Label(rt, "Item", "", 27, PhUiKit.Ink, 30, 76, WidthMm - 60, 92, TextAnchor.MiddleCenter, FontStyle.Bold);
            float rowW = 7 * ButtonMm + 6 * ButtonGapMm, x0 = (WidthMm - rowW) / 2f;
            for (int i = 0; i < 7; i++)
            {
                int value = i - 3;
                var b = PhUiKit.PokeButton(rt, "Scale_" + value, Signed(value), 28, x0 + i * (ButtonMm + ButtonGapMm), 176, ButtonMm, ButtonMm, PhUiKit.ButtonBg, PhUiKit.Ink);
                int captured = value;
                b.onClick.AddListener(() => Press(captured));
                _buttons[i] = b; _faces[i] = b.GetComponent<Image>();
            }
            _low = PhUiKit.Label(rt, "AnchorLow", "", 20, PhUiKit.InkDim, x0 - 6, 248, 250, 40, TextAnchor.UpperLeft);
            _high = PhUiKit.Label(rt, "AnchorHigh", "", 20, PhUiKit.InkDim, WidthMm - x0 - 244, 248, 250, 40, TextAnchor.UpperRight);
            ApplyStaticText();
        }

        public static string Signed(int v) { return v > 0 ? "+" + v : v.ToString(); }

        private void ApplyStaticText()
        {
            if (_back != null) PhUiKit.SetText(_back.GetComponentInChildren<Text>(), PhStrings.Get("q_back", Lang));
            if (_low != null) PhUiKit.SetText(_low, Questionnaire.AnchorLow(Lang));
            if (_high != null) PhUiKit.SetText(_high, Questionnaire.AnchorHigh(Lang));
        }

        /// <summary>Show a questionnaire. onAnswer(value) returns true when accepted; onBack() returns true when it stepped back.</summary>
        public void Show(Questionnaire q, Func<int, bool> onAnswer, Func<bool> onBack, string lang, double nowMs)
        {
            _q = q; _onAnswer = onAnswer; _onBack = onBack; Lang = lang; _nowMs = nowMs; _pendingValue = null; _shownId = null;
            ApplyStaticText();
            gameObject.SetActive(true);
            Refresh();
        }

        public void Hide() { _q = null; _pendingValue = null; gameObject.SetActive(false); }

        /// <summary>A poke on a scale button (also what the uGUI click calls). Ignored while an answer is pending or nothing is shown.</summary>
        public bool Press(int value)
        {
            if (_q == null || _q.IsComplete || _pendingValue.HasValue || value < Questionnaire.Min || value > Questionnaire.Max) return false;
            _pendingValue = value; _pendingAtMs = _nowMs;
            Highlight(value);
            return true;
        }

        public bool PressBack()
        {
            if (_q == null || _pendingValue.HasValue || _q.CurrentIndex == 0 && !_q.IsComplete) return false;
            bool ok = _onBack != null && _onBack();
            if (ok) Refresh();
            return ok;
        }

        public void Tick(double nowMs)
        {
            _nowMs = nowMs;
            if (_q == null) return;
            if (_pendingValue.HasValue && nowMs - _pendingAtMs >= AutoAdvanceMs)
            {
                int v = _pendingValue.Value; _pendingValue = null;
                if (_onAnswer != null) _onAnswer(v);
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_q == null) return;
            var cur = _q.Current;
            if (cur == null) return;   // complete: the phase is about to change
            if (cur.Id != _shownId || !_pendingValue.HasValue)
            {
                if (cur.Id != _shownId) { _shownId = cur.Id; PhUiKit.SetText(_item, cur.Text(Lang)); }
                PhUiKit.SetText(_progress, PhStrings.Format("q_progress", Lang, _q.CurrentIndex + 1, _q.Items.Count));
                _back.gameObject.SetActive(_q.CurrentIndex > 0);
                if (!_pendingValue.HasValue) Highlight(_q.ValueOf(cur.Id));
            }
        }

        private void Highlight(int? value)
        {
            for (int i = 0; i < 7; i++)
                _faces[i].color = (value.HasValue && value.Value == i - 3) ? PhUiKit.Oxblood : PhUiKit.ButtonBg;
        }
    }
}
