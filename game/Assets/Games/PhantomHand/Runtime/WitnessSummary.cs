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
        public double? Ownership, Control, Awareness;
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
        public string Key;
        public string LabelEn, LabelHi, Unit;
        public object SyncValue, AsyncValue;
        /// <summary>A pointer, not proof (q4).</summary>
        public bool Pointer;
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
        public const string ClosingEn = "What can be manufactured and dissolved is an appearance, not the Self. The witness that noticed the change did not change.";
        public const string ClosingHi = "जो बनाया और मिटाया जा सकता है वह एक आभास है, स्वयं नहीं। जिसने इस बदलाव को देखा, वह नहीं बदला।";

        public List<WitnessRow> Rows = new List<WitnessRow>();
        public ConditionResult Sync, Async;

        public static WitnessSummary Build(ConditionResult sync, ConditionResult async)
        {
            var w = new WitnessSummary { Sync = sync, Async = async };
            w.Rows.Add(new WitnessRow { Key = "drift_change_cm", LabelEn = "Where your hand felt to be", LabelHi = "आपका हाथ कहाँ महसूस हुआ", Unit = "cm", SyncValue = sync.DriftChangeCm, AsyncValue = async.DriftChangeCm });
            w.Rows.Add(new WitnessRow { Key = "flinch_latency_ms", LabelEn = "Flinch after the stone", LabelHi = "पत्थर के बाद झटका", Unit = "ms", SyncValue = sync.FlinchLatencyMs, AsyncValue = async.FlinchLatencyMs });
            w.Rows.Add(new WitnessRow { Key = "flinch_strength", LabelEn = "Flinch strength", LabelHi = "झटके की तीव्रता", Unit = "", SyncValue = sync.FlinchStrength, AsyncValue = async.FlinchStrength });
            w.Rows.Add(new WitnessRow { Key = "ownership", LabelEn = "It felt like my hand", LabelHi = "यह मेरा हाथ लगा", Unit = "-3..+3", SyncValue = sync.Ownership, AsyncValue = async.Ownership });
            if (sync.Awareness.HasValue || async.Awareness.HasValue)
                w.Rows.Add(new WitnessRow { Key = "q4", LabelEn = "The awareness that noticed was the same", LabelHi = "देखने वाली जागरूकता वही रही", Unit = "-3..+3", SyncValue = sync.Awareness, AsyncValue = async.Awareness, Pointer = true });
            return w;
        }

        public WitnessRow Row(string key)
        {
            foreach (var r in Rows) if (r.Key == key) return r;
            return null;
        }

        public string ClosingLine(string lang) { return lang == "hi" ? ClosingHi : ClosingEn; }

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

        /// <summary>Event payload for witness_summary (snake_case keys, per condition + sync-minus-async).</summary>
        public Dictionary<string, object> ToEventData()
        {
            var d = new Dictionary<string, object>();
            d["sync"] = CondData(Sync);
            d["async"] = CondData(Async);
            var diff = new Dictionary<string, object>();
            foreach (var r in Rows) { var x = r.Difference; if (x.HasValue) diff[r.Key] = x.Value; }
            d["sync_minus_async"] = diff;
            d["closing_en"] = ClosingEn;
            d["closing_hi"] = ClosingHi;
            return d;
        }

        private static Dictionary<string, object> CondData(ConditionResult c)
        {
            var d = new Dictionary<string, object>();
            d["drift_change_cm"] = c.DriftChangeCm;
            d["flinch_latency_ms"] = c.FlinchLatencyMs;
            d["flinch_strength"] = c.FlinchStrength;
            d["flinch_emg_peak_x"] = c.Threat == null ? null : c.Threat.EmgPeakX;
            d["flinch_wrist_peak_mps"] = c.Threat == null ? null : c.Threat.WristPeakMps;
            d["ownership"] = c.Ownership;
            d["control"] = c.Control;
            d["q4"] = c.Awareness;
            return d;
        }
    }
}
