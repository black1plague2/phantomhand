using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 item 4 (PRD section 5 row 7): two cards, SYNC and ASYNC, with the participant's own numbers (drift change with an
    /// arrow, flinch latency with strong/weak/none, ownership bar, and the q4 row labelled "a pointer, not proof" when the
    /// summary has it), a "Preliminary" label, and the closing line that fades in 3 s after the panel opens.
    /// Content comes from <see cref="WitnessView"/>; this class only draws it.
    /// </summary>
    public sealed class PhWitnessPanel : MonoBehaviour
    {
        public const float WidthMm = 1000f, HeightMm = 660f;
        private const float CardW = 470f, CardH = 420f, CardY = 84f;

        private Text _closing;
        private double _shownAtMs;
        private RectTransform _root;
        public WitnessView View { get; private set; }
        public Text ClosingText { get { return _closing; } }
        public float ClosingAlpha { get { return _closing == null ? 0f : _closing.color.a; } }

        public void Build(Camera worldCamera, bool attachPoke)
        {
            _root = PhUiKit.PreparePanel(transform, WidthMm, HeightMm, worldCamera, attachPoke);
        }

        public void Show(WitnessSummary summary, string lang, double nowMs)
        {
            Show(WitnessView.Build(summary, lang), nowMs);
        }

        public void Show(WitnessView view, double nowMs)
        {
            View = view; _shownAtMs = nowMs;
            PhUiKit.ClearChildren(_root);
            PhUiKit.Box(_root, "Backdrop", 0, 0, WidthMm, HeightMm, PhUiKit.PanelBg);
            PhUiKit.Box(_root, "AccentBar", 0, 0, WidthMm, 6, PhUiKit.Oxblood);
            PhUiKit.Label(_root, "Title", view.Title, 40, PhUiKit.Ink, 30, 18, 600, 56, TextAnchor.MiddleLeft, FontStyle.Bold);
            var chip = PhUiKit.Box(_root, "PreliminaryChip", WidthMm - 230, 28, 200, 40, PhUiKit.PanelLine);
            PhUiKit.Label(chip.transform, "PreliminaryLabel", view.PreliminaryLabel, 22, PhUiKit.Warn, 0, 0, 200, 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            BuildCard(view.Sync, 30, PhUiKit.Good);
            BuildCard(view.Async, 500, PhUiKit.Warn);
            _closing = PhUiKit.Label(_root, "ClosingLine", view.ClosingLine, 27, PhUiKit.Ink, 40, CardY + CardH + 14, WidthMm - 80, 130, TextAnchor.MiddleCenter, FontStyle.Italic);
            SetClosingAlpha(0f);
            gameObject.SetActive(true);
        }

        public void Hide() { gameObject.SetActive(false); }

        public void Tick(double nowMs)
        {
            if (View == null) return;
            SetClosingAlpha((float)WitnessView.ClosingAlpha((nowMs - _shownAtMs) / 1000.0));
        }

        private void SetClosingAlpha(float a)
        {
            if (_closing == null) return;
            var c = _closing.color; c.a = a; _closing.color = c;
        }

        private void BuildCard(WitnessCardView card, float x, Color accent)
        {
            var bg = PhUiKit.Box(_root, "Card_" + card.Condition, x, CardY, CardW, CardH, PhUiKit.PanelLine);
            var t = bg.transform;
            PhUiKit.Box(t, "CardBar", 0, 0, 8, CardH, accent);
            PhUiKit.Label(t, "CardTitle", card.Title, 32, accent, 26, 8, CardW - 40, 44, TextAnchor.MiddleLeft, FontStyle.Bold);
            float y = 60, prevY = 60; string prevKey = null;
            foreach (var e in card.Entries)
            {
                if (e.Key == "flinch_strength" && prevKey == "flinch_latency_ms")
                {
                    // strength shares the latency row: "420 ms   [Strong]"
                    BuildEntry(t, e, 26 + 230, prevY, CardW - 46 - 230, accent, withLabel: false);
                    prevKey = e.Key;
                    continue;
                }
                float h = e.Kind == WitnessEntryKind.Bar && e.Pointer ? 104 : 76;
                BuildEntry(t, e, 26, y, CardW - 46, accent, withLabel: true);
                prevY = y; prevKey = e.Key;
                y += h;
            }
        }

        private void BuildEntry(Transform card, WitnessEntry e, float x, float y, float w, Color accent, bool withLabel)
        {
            if (withLabel) PhUiKit.Label(card, "Label_" + e.Key, e.Label, 20, PhUiKit.InkDim, x, y, w, 26, TextAnchor.MiddleLeft);
            float vy = y + 28;
            switch (e.Kind)
            {
                case WitnessEntryKind.Arrow:
                {
                    var col = !e.HasData ? PhUiKit.InkDim : e.Direction > 0 ? PhUiKit.Info : e.Direction < 0 ? PhUiKit.Bad : PhUiKit.InkDim;
                    string arrow = !e.HasData ? "" : e.Direction > 0 ? "←" : e.Direction < 0 ? "→" : "•";
                    PhUiKit.Label(card, "Arrow_" + e.Key, arrow, 40, col, x, vy - 4, 56, 50, TextAnchor.MiddleCenter, FontStyle.Bold);
                    PhUiKit.Label(card, "Value_" + e.Key, e.ValueText, 36, e.HasData ? col : PhUiKit.InkDim, x + 60, vy, 150, 44, TextAnchor.MiddleLeft, FontStyle.Bold);
                    if (e.Detail != null) PhUiKit.Label(card, "Detail_" + e.Key, e.Detail, 19, PhUiKit.InkDim, x + 214, vy, w - 214, 44, TextAnchor.MiddleLeft);
                    break;
                }
                case WitnessEntryKind.Number:
                    PhUiKit.Label(card, "Value_" + e.Key, e.ValueText, 36, e.HasData ? PhUiKit.Ink : PhUiKit.InkDim, x, vy, w, 44, TextAnchor.MiddleLeft, FontStyle.Bold);
                    break;
                case WitnessEntryKind.Word:
                {
                    var col = !e.HasData ? PhUiKit.InkDim : e.Direction > 0 ? PhUiKit.Good : e.Direction == 0 ? PhUiKit.Warn : PhUiKit.Bad;
                    var chip = PhUiKit.Box(card, "WordChip_" + e.Key, x, vy + 2, 170, 40, PhUiKit.PanelBg);
                    PhUiKit.Label(chip.transform, "Value_" + e.Key, e.ValueText, 26, col, 0, 0, 170, 40, TextAnchor.MiddleCenter, FontStyle.Bold);
                    break;
                }
                default:   // Bar
                {
                    float barW = w - 110;
                    PhUiKit.Box(card, "BarBg_" + e.Key, x, vy + 12, barW, 22, PhUiKit.PanelBg);
                    if (e.HasData)
                    {
                        var fill = PhUiKit.Box(card, "BarFill_" + e.Key, x, vy + 12, Mathf.Max(4f, barW * (float)e.Fraction), 22, e.Pointer ? PhUiKit.Info : accent);
                        fill.name = "BarFill_" + e.Key;
                    }
                    PhUiKit.Box(card, "BarMid_" + e.Key, x + barW / 2f - 1, vy + 6, 2, 34, PhUiKit.InkDim);
                    PhUiKit.Label(card, "Value_" + e.Key, e.ValueText, 30, e.HasData ? PhUiKit.Ink : PhUiKit.InkDim, x + barW + 10, vy, 100, 44, TextAnchor.MiddleLeft, FontStyle.Bold);
                    if (e.Pointer) PhUiKit.Label(card, "Pointer_" + e.Key, e.PointerText, 20, PhUiKit.Info, x, vy + 46, w, 26, TextAnchor.MiddleLeft, FontStyle.Italic);
                    break;
                }
            }
        }
    }
}
