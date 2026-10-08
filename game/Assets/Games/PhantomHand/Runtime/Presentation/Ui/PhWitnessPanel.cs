using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 item 4 (PRD section 5 row 7): two cards, SYNC and ASYNC, with the participant's own numbers grouped as Body (drift
    /// change with an arrow, flinch latency with strong/weak/none), Mind (ownership bar) and The one who noticed (the q4 row,
    /// labelled "a pointer, not proof" when the summary has it), a "Preliminary" label, and the closing lines that fade in 3 s
    /// after the panel opens. Content comes from <see cref="WitnessView"/>; this class only draws it. The panel is
    /// <see cref="HeightMm"/> high and grows only when the rows need more room.
    /// </summary>
    public sealed class PhWitnessPanel : MonoBehaviour
    {
        public const float WidthMm = 1000f, HeightMm = 660f;
        private const float CardW = 470f, CardY = 84f;
        // inside a card, mm from its top-left corner
        private const float RowsTop = 60f, GroupHeaderH = 32f, GroupHeaderTextH = 26f, RowH = 76f, PointerRowH = 104f, CardPad = 14f;
        // closing lines: one text line is allowed ClosingLineH, plus one spare line in case a long line wraps
        private const float ClosingLineH = 40f, ClosingGap = 10f, PanelPad = 12f, FactH = 44f;

        private Text _closing;
        private double _shownAtMs;
        private RectTransform _root;
        public WitnessView View { get; private set; }
        public Text ClosingText { get { return _closing; } }
        public float ClosingAlpha { get { return _closing == null ? 0f : _closing.color.a; } }

        /// <summary>One drawn item of a card: a group header (Entry == null) or a row, placed in mm inside the card.</summary>
        private sealed class Slot
        {
            public WitnessEntry Entry;
            public string Group, Header;
            public float X, Y, W;
            public bool WithLabel;
        }

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
            float cardH = Mathf.Max(CardHeight(view.Sync), CardHeight(view.Async));
            float factY = CardY + cardH + ClosingGap;                                       // one factual line under the cards when the agency phase ran
            float closingY = view.AgencyLine != null ? factY + FactH + ClosingGap : factY;
            float closingH = ClosingLineH * (view.ClosingLine.Split('\n').Length + 1);
            float h = Mathf.Max(HeightMm, closingY + closingH + PanelPad);
            _root.sizeDelta = new Vector2(WidthMm, h);
            PhUiKit.ClearChildren(_root);
            PhUiKit.Box(_root, "Backdrop", 0, 0, WidthMm, h, PhUiKit.PanelBg);
            PhUiKit.Box(_root, "AccentBar", 0, 0, WidthMm, 6, PhUiKit.Oxblood);
            PhUiKit.Label(_root, "Title", view.Title, 40, PhUiKit.Ink, 30, 18, 600, 56, TextAnchor.MiddleLeft, FontStyle.Bold);
            var chip = PhUiKit.Box(_root, "PreliminaryChip", WidthMm - 230, 28, 200, 40, PhUiKit.PanelLine);
            PhUiKit.Label(chip.transform, "PreliminaryLabel", view.PreliminaryLabel, 22, PhUiKit.Warn, 0, 0, 200, 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            BuildCard(view.Sync, 30, PhUiKit.Info, cardH);     // blue and amber: green against amber has no lightness difference
            BuildCard(view.Async, 500, PhUiKit.Warn, cardH);
            if (view.AgencyLine != null) PhUiKit.Label(_root, "AgencyFacts", view.AgencyLine, 24, PhUiKit.Ink, 40, factY, WidthMm - 80, FactH, TextAnchor.MiddleCenter);
            _closing = PhUiKit.Label(_root, "ClosingLine", view.ClosingLine, 26, PhUiKit.Ink, 40, closingY, WidthMm - 80, closingH, TextAnchor.MiddleCenter, FontStyle.Italic);
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

        /// <summary>Height of a card in mm: title strip, one header per group, the rows. Pure, so the same pass that draws a card also sizes the panel.</summary>
        public static float CardHeight(WitnessCardView card) { return Layout(card, null); }

        private static float Layout(WitnessCardView card, List<Slot> slots)
        {
            float y = RowsTop, prevY = RowsTop; string prevKey = null, prevGroup = null;
            foreach (var e in card.Entries)
            {
                if (e.Key == "flinch_strength" && prevKey == "flinch_latency_ms")
                {
                    // strength shares the latency row: "420 ms   [Strong]"
                    if (slots != null) slots.Add(new Slot { Entry = e, X = 26 + 230, Y = prevY, W = CardW - 46 - 230, WithLabel = false });
                    prevKey = e.Key;
                    continue;
                }
                if (e.Group != null && e.Group != prevGroup)
                {
                    if (slots != null) slots.Add(new Slot { Group = e.Group, Header = e.GroupHeader, X = 26, Y = y, W = CardW - 46 });
                    y += GroupHeaderH; prevGroup = e.Group;
                }
                if (slots != null) slots.Add(new Slot { Entry = e, X = 26, Y = y, W = CardW - 46, WithLabel = true });
                prevY = y; prevKey = e.Key;
                y += e.Kind == WitnessEntryKind.Bar && e.Pointer ? PointerRowH : RowH;
            }
            return y + CardPad;
        }

        private void BuildCard(WitnessCardView card, float x, Color accent, float cardH)
        {
            var bg = PhUiKit.Box(_root, "Card_" + card.Condition, x, CardY, CardW, cardH, PhUiKit.PanelLine);
            var t = bg.transform;
            PhUiKit.Box(t, "CardBar", 0, 0, 8, cardH, accent);
            PhUiKit.Label(t, "CardTitle", card.Title, 32, accent, 26, 8, CardW - 40, 44, TextAnchor.MiddleLeft, FontStyle.Bold);
            var slots = new List<Slot>();
            Layout(card, slots);
            foreach (var s in slots)
            {
                if (s.Entry == null) PhUiKit.Label(t, "Group_" + s.Group, s.Header, 20, accent, s.X, s.Y, s.W, GroupHeaderTextH, TextAnchor.MiddleLeft, FontStyle.Bold);
                else BuildEntry(t, s.Entry, s.X, s.Y, s.W, accent, s.WithLabel);
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
                    // a rating that was not asked in this condition (q5 exists in one condition only) shows a dash: "no data" does not fit beside the bar
                    PhUiKit.Label(card, "Value_" + e.Key, e.HasData ? e.ValueText : "–", 30, e.HasData ? PhUiKit.Ink : PhUiKit.InkDim, x + barW + 10, vy, 100, 44, TextAnchor.MiddleLeft, FontStyle.Bold);
                    if (e.Pointer) PhUiKit.Label(card, "Pointer_" + e.Key, e.PointerText, 20, PhUiKit.Info, x, vy + 46, w, 26, TextAnchor.MiddleLeft, FontStyle.Italic);
                    break;
                }
            }
        }
    }
}
