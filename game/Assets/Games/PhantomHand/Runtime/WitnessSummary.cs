using System;
using System.Collections.Generic;
using System.Globalization;

namespace Opus.Games.PhantomHand
{
    /// <summary>Everything measured for one condition.</summary>
    public sealed class ConditionResult
    {
        public PhCondition Condition;
        public double? PreDriftCm, PostDriftCm;
        public ThreatResponse Threat;
        public double? Ownership, Control, Awareness, Agency;
        public double? DriftChangeCm
        {
            get { return (PreDriftCm.HasValue && PostDriftCm.HasValue) ? PostDriftCm - PreDriftCm : (double?)null; }
        }
        /// <summary>EMG latency when available, hand-tracking latency otherwise (PRD section 11 fallback).</summary>
        public double? FlinchLatencyMs
        {
            get { return Threat == null ? null : (Threat.EmgLatencyMs ?? Threat.WristLatencyMs); }
        }
        public string FlinchStrength
        {
            get
            {
                if (Threat == null) return "none";
                if (Threat.EmgPeakX.HasValue)
                {
                    if (Threat.EmgPeakX.Value >= 3.0 && Threat.EmgLatencyMs.HasValue) return "strong";
                    if (Threat.EmgLatencyMs.HasValue) return "weak";
                    return "none";
                }
                if (Threat.WristPeakMps.HasValue)
                {
                    if (Threat.WristPeakMps.Value >= 0.5 && Threat.WristLatencyMs.HasValue) return "strong";
                    if (Threat.WristLatencyMs.HasValue) return "weak";
                }
                return "none";
            }
        }
    }

    /// <summary>One line of the witness comparison (values are numbers or strings; null = no data).</summary>
    public sealed class WitnessRow
    {
        /// <summary>Row groups: what the body did (measured, involuntary), what the participant reported (mind), and the one who noticed (q4).</summary>
        public const string Body = "body", Mind = "mind", Observer = "observer";

        public string Key;
        public string LabelEn, LabelHi, Unit;
        public object SyncValue, AsyncValue;
        /// <summary>A pointer, not proof (q4).</summary>
        public bool Pointer;
        /// <summary><see cref="Body"/>, <see cref="Mind"/> or <see cref="Observer"/>.</summary>
        public string Group;
        public double? Difference
        {
            get
            {
                if (SyncValue is double && AsyncValue is double) return (double)SyncValue - (double)AsyncValue;
                return null;
            }
        }
    }

    /// <summary>
    /// Witness screen content (PRD section 5 row 7): a row list comparing the participant's SYNC and ASYNC numbers, EN/HI
    /// summary lines and the closing line. Rows are data, so A1 can add the q4 row without changing this class.
    /// </summary>
    public sealed class WitnessSummary
    {
        // The closing copy says what changed and that the participant noticed it, nothing stronger.
        private const string ChangedEn = "The body changed. The touch changed. The feeling of \"mine\" changed.";
        private const string ChangedHi = "शरीर बदला। स्पर्श बदला। \"मेरा\" होने का एहसास बदला।";
        private const string AgencyEn = "Even \"I did that\" changed.";
        private const string AgencyHi = "\"यह मैंने किया\" का एहसास भी बदला।";
        private const string NoticedEn = "You noticed every change.";
        private const string NoticedHi = "हर बदलाव को आपने देखा।";
        private const string TattvaEn = "Tattva 5: consciousness is beyond the body and mind.";
        private const string TattvaHi = "तत्त्व 5: चेतना शरीर और मन से परे है।";

        /// <summary>The closing text when the agency phase did not run: the lines joined by a newline.</summary>
        public const string ClosingEn = ChangedEn + "\n" + NoticedEn + "\n" + TattvaEn;
        public const string ClosingHi = ChangedHi + "\n" + NoticedHi + "\n" + TattvaHi;

        public List<WitnessRow> Rows = new List<WitnessRow>();
        public ConditionResult Sync, Async;
        /// <summary>The agency phase ran and the hand closed by itself (A5): adds the second closing line and the facts line.</summary>
        public bool AgencyRan;
        /// <summary>How often the participant closed the hand with the muscle, and how often it closed by itself.</summary>
        public int DrivenCloses, AutonomousCloses;
        /// <summary>Run order of the two conditions for the witness_summary event; null = unknown, key left out.</summary>
        public IReadOnlyList<PhCondition> ConditionOrder;

        public static WitnessSummary Build(ConditionResult sync, ConditionResult async)
        {
            var w = new WitnessSummary { Sync = sync, Async = async };
            w.Rows.Add(new WitnessRow { Key = "drift_change_cm", LabelEn = "Where your hand felt to be", LabelHi = "आपका हाथ कहाँ महसूस हुआ", Unit = "cm", SyncValue = sync.DriftChangeCm, AsyncValue = async.DriftChangeCm, Group = WitnessRow.Body });
            w.Rows.Add(new WitnessRow { Key = "flinch_latency_ms", LabelEn = "Flinch after the stone", LabelHi = "पत्थर के बाद झटका", Unit = "ms", SyncValue = sync.FlinchLatencyMs, AsyncValue = async.FlinchLatencyMs, Group = WitnessRow.Body });
            w.Rows.Add(new WitnessRow { Key = "flinch_strength", LabelEn = "Flinch strength", LabelHi = "झटके की तीव्रता", Unit = "", SyncValue = sync.FlinchStrength, AsyncValue = async.FlinchStrength, Group = WitnessRow.Body });
            w.Rows.Add(new WitnessRow { Key = "ownership", LabelEn = "It felt like my hand", LabelHi = "यह मेरा हाथ लगा", Unit = "-3..+3", SyncValue = sync.Ownership, AsyncValue = async.Ownership, Group = WitnessRow.Mind });
            if (sync.Agency.HasValue || async.Agency.HasValue)   // q5 is asked in the last condition only
                w.Rows.Add(new WitnessRow { Key = "q5", LabelEn = "I caused that movement", LabelHi = "वह गति मैंने कराई", Unit = "-3..+3", SyncValue = sync.Agency, AsyncValue = async.Agency, Group = WitnessRow.Mind });
            if (sync.Awareness.HasValue || async.Awareness.HasValue)
                w.Rows.Add(new WitnessRow { Key = "q4", LabelEn = "The awareness that noticed was the same", LabelHi = "देखने वाली जागरूकता वही रही", Unit = "-3..+3", SyncValue = sync.Awareness, AsyncValue = async.Awareness, Pointer = true, Group = WitnessRow.Observer });
            return w;
        }

        public WitnessRow Row(string key)
        {
            foreach (var r in Rows) if (r.Key == key) return r;
            return null;
        }

        /// <summary>The closing lines in order: what changed, [the agency line when A5 ran], that you noticed, the Tattva line.</summary>
        public List<string> ClosingLines(string lang)
        {
            bool hi = lang == "hi";
            var l = new List<string> { hi ? ChangedHi : ChangedEn };
            if (AgencyRan) l.Add(hi ? AgencyHi : AgencyEn);
            l.Add(hi ? NoticedHi : NoticedEn);
            l.Add(hi ? TattvaHi : TattvaEn);
            return l;
        }

        /// <summary>The closing lines joined by a newline.</summary>
        public string ClosingLine(string lang) { return string.Join("\n", ClosingLines(lang)); }

        /// <summary>Plain-language lines for the panel; the closing line is last.</summary>
        public List<string> Lines(string lang)
        {
            bool hi = lang == "hi";
            var lines = new List<string>();
            lines.Add(CondLine(Sync, hi));
            lines.Add(CondLine(Async, hi));
            lines.Add(ClosingLine(lang));
            return lines;
        }

        private static string CondLine(ConditionResult c, bool hi)
        {
            string name = c.Condition == PhCondition.Sync ? (hi ? "समकालिक" : "Synchronous") : (hi ? "विलंबित" : "Delayed");
            string drift = c.DriftChangeCm.HasValue ? F(c.DriftChangeCm.Value, 1) + " cm" : "-";
            string flinch = c.FlinchLatencyMs.HasValue ? F(c.FlinchLatencyMs.Value, 0) + " ms" : "-";
            string own = c.Ownership.HasValue ? c.Ownership.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) : "-";
            if (hi)
                return name + ": खिसकाव " + drift + ", झटका " + flinch + " (" + StrengthHi(c.FlinchStrength) + "), अपनापन " + own;
            return name + ": drift " + drift + ", flinch " + flinch + " (" + c.FlinchStrength + "), ownership " + own;
        }

        private static string StrengthHi(string s)
        {
            return s == "strong" ? "तेज़" : s == "weak" ? "हल्का" : "कोई नहीं";
        }

        private static string F(double v, int dp) { return v.ToString("F" + dp, CultureInfo.InvariantCulture); }

        /// <summary>
        /// Event payload for witness_summary (snake_case keys, per condition + sync-minus-async), shaped like the contract fixture
        /// event.11.json: questionnaire means go out on the contract scale 1..7 (the screen's -3..+3 plus 4), q4 as `witness_q4`,
        /// q5 as `agency_q5` (only when answered), and the condition order when known. The differences are unchanged by the shift.
        /// </summary>
        public Dictionary<string, object> ToEventData()
        {
            var d = new Dictionary<string, object>();
            if (ConditionOrder != null)
            {
                var order = new List<string>();
                foreach (var c in ConditionOrder) order.Add(PhNames.Of(c));
                d["condition_order"] = order;
            }
            d["sync"] = CondData(Sync);
            d["async"] = CondData(Async);
            var diff = new Dictionary<string, object>();
            foreach (var r in Rows) { var x = r.Difference; if (x.HasValue) diff[EventKey(r.Key)] = x.Value; }
            d["sync_minus_async"] = diff;
            if (AgencyRan) d["agency"] = new Dictionary<string, object> { { "driven_closes", DrivenCloses }, { "autonomous_closes", AutonomousCloses } };
            d["closing_en"] = ClosingLine("en");
            d["closing_hi"] = ClosingLine("hi");
            return d;
        }

        private static string EventKey(string rowKey) { return rowKey == "q4" ? "witness_q4" : rowKey == "q5" ? "agency_q5" : rowKey; }

        /// <summary>The screen shows -3..+3; the contract scale is 1..7.</summary>
        private static double? Likert7(double? shown) { return shown.HasValue ? shown.Value + 4.0 : (double?)null; }

        private static Dictionary<string, object> CondData(ConditionResult c)
        {
            var d = new Dictionary<string, object>();
            d["drift_change_cm"] = c.DriftChangeCm;
            d["flinch_latency_ms"] = c.FlinchLatencyMs;
            d["flinch_strength"] = c.FlinchStrength;
            d["flinch_emg_peak_x"] = c.Threat == null ? null : c.Threat.EmgPeakX;
            d["flinch_wrist_peak_mps"] = c.Threat == null ? null : c.Threat.WristPeakMps;
            d["ownership"] = Likert7(c.Ownership);
            d["control"] = Likert7(c.Control);
            d["witness_q4"] = Likert7(c.Awareness);
            if (c.Agency.HasValue) d["agency_q5"] = Likert7(c.Agency);
            return d;
        }
    }
}
