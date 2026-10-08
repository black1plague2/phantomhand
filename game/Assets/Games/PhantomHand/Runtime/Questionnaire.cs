using System;
using System.Collections.Generic;

namespace Opus.Games.PhantomHand
{
    public enum QRole { Ownership, Control, Awareness, Agency }

    /// <summary>One questionnaire item (7-point scale, -3 strongly disagree .. +3 strongly agree).</summary>
    public sealed class QItem
    {
        public string Id;
        public string En, Hi;
        public QRole Role;
        public QItem(string id, QRole role, string en, string hi) { Id = id; Role = role; En = en; Hi = hi; }
        public string Text(string lang) { return lang == "hi" ? Hi : En; }
    }

    /// <summary>
    /// FR-VR-05 questionnaire: one item at a time on a 7-point scale. The item list is injected so the additions can
    /// extend it (q4 after every condition when A1 runs, q5 when the agency phase runs) without touching this class.
    /// Ownership = mean of the Ownership-role items (q1, q2). Back() returns to the previous item.
    /// </summary>
    public sealed class Questionnaire
    {
        public const int Min = -3, Max = 3;

        /// <summary>The scale the participant sees and the on-device witness uses is -3..+3; events.ndjson (event.schema.json) and the
        /// analytics carry the same answer as 1..7 (1 = strongly disagree, 7 = strongly agree).</summary>
        public static int ContractValue(int shown) { return shown - Min + 1; }
        public const string AnchorLowEn = "Strongly disagree", AnchorHighEn = "Strongly agree";
        public const string AnchorLowHi = "पूरी तरह असहमत", AnchorHighHi = "पूरी तरह सहमत";

        public static readonly QItem Q1 = new QItem("q1", QRole.Ownership,
            "It felt as if the virtual hand was my hand.",
            "ऐसा लगा जैसे वर्चुअल हाथ मेरा अपना हाथ था।");
        public static readonly QItem Q2 = new QItem("q2", QRole.Ownership,
            "It felt as if the touch I felt was caused by the brush on the virtual hand.",
            "ऐसा लगा जैसे मुझे जो स्पर्श महसूस हुआ वह वर्चुअल हाथ पर ब्रश से हुआ था।");
        public static readonly QItem Q3 = new QItem("q3", QRole.Control,
            "It felt as if my real hand was turning virtual.",
            "ऐसा लगा जैसे मेरा असली हाथ वर्चुअल बनता जा रहा था।");
        public static readonly QItem Q4 = new QItem("q4", QRole.Awareness,
            "The awareness that noticed these sensations was the same as before.",
            "जिस जागरूकता ने इन संवेदनाओं को देखा, वह पहले जैसी ही थी।");
        public static readonly QItem Q5 = new QItem("q5", QRole.Agency,
            "I caused that movement.",
            "वह गति मैंने ही कराई।");

        public static List<QItem> DefaultItems() { return new List<QItem> { Q1, Q2, Q3 }; }

        private readonly List<QItem> _items;
        private readonly int?[] _answers;

        public Questionnaire() : this(DefaultItems()) { }

        public Questionnaire(IList<QItem> items)
        {
            if (items == null || items.Count == 0) throw new ArgumentException("questionnaire needs at least one item");
            _items = new List<QItem>(items);
            _answers = new int?[_items.Count];
        }

        public IReadOnlyList<QItem> Items { get { return _items; } }
        public int CurrentIndex { get; private set; }
        public bool IsComplete { get; private set; }
        public QItem Current { get { return IsComplete ? null : _items[CurrentIndex]; } }

        public int? ValueOf(string id)
        {
            for (int i = 0; i < _items.Count; i++) if (_items[i].Id == id) return _answers[i];
            return null;
        }

        /// <summary>Records the answer for the current item and moves on. Returns the item id answered, or null if the value is out of range / already complete.</summary>
        public string Answer(int value)
        {
            if (IsComplete || value < Min || value > Max) return null;
            var item = _items[CurrentIndex];
            _answers[CurrentIndex] = value;
            if (CurrentIndex == _items.Count - 1) IsComplete = AllAnswered();
            if (CurrentIndex < _items.Count - 1) CurrentIndex++;
            return item.Id;
        }

        /// <summary>Back: step to the previous item (from the first item, or when complete, stays on/returns to the last valid one).</summary>
        public bool Back()
        {
            if (IsComplete) { IsComplete = false; return true; }
            if (CurrentIndex == 0) return false;
            CurrentIndex--;
            return true;
        }

        private bool AllAnswered()
        {
            foreach (var a in _answers) if (!a.HasValue) return false;
            return true;
        }

        /// <summary>Mean of the items with a given role; null when none answered.</summary>
        public double? RoleMean(QRole role)
        {
            double sum = 0; int n = 0;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Role == role && _answers[i].HasValue) { sum += _answers[i].Value; n++; }
            return n == 0 ? (double?)null : sum / n;
        }

        public double? Ownership { get { return RoleMean(QRole.Ownership); } }
        public double? Control { get { return RoleMean(QRole.Control); } }
        public double? Awareness { get { return RoleMean(QRole.Awareness); } }

        public static string AnchorLow(string lang) { return lang == "hi" ? AnchorLowHi : AnchorLowEn; }
        public static string AnchorHigh(string lang) { return lang == "hi" ? AnchorHighHi : AnchorHighEn; }
    }
}
