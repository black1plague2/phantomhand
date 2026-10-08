using System.Collections.Generic;
using System.Globalization;

namespace Opus.Games.PhantomHand
{
    public enum WitnessEntryKind { Arrow, Number, Word, Bar }

    /// <summary>One line on a witness card, fully formatted for the chosen language.</summary>
    public sealed class WitnessEntry
    {
        public string Key, Label, ValueText, Detail;
        public WitnessEntryKind Kind;
        public bool HasData;
        /// <summary>Arrow: +1 toward the virtual hand, -1 away, 0 none. Word: +1 strong, 0 weak, -1 none.</summary>
        public int Direction;
        /// <summary>Bar fill 0..1 (the -3..+3 answer mapped onto the bar).</summary>
        public double Fraction;
        /// <summary>q4: shown as a pointer, not proof.</summary>
        public bool Pointer;
        public string PointerText;
        /// <summary>Row group ("body", "mind", "observer") and its header in the chosen language; the panel draws the header above the first row of each group.</summary>
        public string Group, GroupHeader;
    }

    public sealed class WitnessCardView
    {
        public PhCondition Condition;
        public string Title;
        public List<WitnessEntry> Entries = new List<WitnessEntry>();
        public WitnessEntry Entry(string key) { foreach (var e in Entries) if (e.Key == key) return e; return null; }
    }

    /// <summary>
    /// U4 item 4: the witness panel's content as plain data. Cards are built from the summary's ROW LIST (so the q4 row
    /// appears exactly when the summary has it, labelled as a pointer, not proof). The closing line fades in 3 s after the
    /// panel is shown. No Unity dependency.
    /// </summary>
    public sealed class WitnessView
    {
        public const double ClosingDelayS = 3.0, ClosingFadeS = 1.0;

        public string Lang = PhStrings.En;
        public string Title, PreliminaryLabel, ClosingLine;
        /// <summary>"You closed it 3 times. It closed by itself 2 times." when the agency phase ran, else null.</summary>
        public string AgencyLine;
        public WitnessCardView Sync, Async;

        public static double ClosingAlpha(double secondsSinceShown)
        {
            double a = (secondsSinceShown - ClosingDelayS) / ClosingFadeS;
            return a < 0 ? 0 : (a > 1 ? 1 : a);
        }

        public static WitnessView Build(WitnessSummary s, string lang)
        {
            var v = new WitnessView
            {
                Lang = lang,
                Title = PhStrings.Get("w_title", lang),
                PreliminaryLabel = PhStrings.Get("w_preliminary", lang),
                ClosingLine = s.ClosingLine(lang),
                AgencyLine = s.AgencyRan ? PhStrings.Format("w_agency_facts", lang, s.DrivenCloses, s.AutonomousCloses) : null,
                Sync = new WitnessCardView { Condition = PhCondition.Sync, Title = PhStrings.Get("w_sync", lang) },
                Async = new WitnessCardView { Condition = PhCondition.Async, Title = PhStrings.Get("w_async", lang) },
            };
            foreach (var row in s.Rows)
            {
                v.Sync.Entries.Add(Entry(row, row.SyncValue, lang));
                v.Async.Entries.Add(Entry(row, row.AsyncValue, lang));
            }
            return v;
        }

        private static WitnessEntry Entry(WitnessRow row, object val, string lang)
        {
            bool hi = lang == PhStrings.Hi;
            var e = new WitnessEntry { Key = row.Key, Label = hi ? row.LabelHi : row.LabelEn, Pointer = row.Pointer, HasData = val != null, Group = row.Group };
            if (row.Group != null) e.GroupHeader = PhStrings.Get("w_group_" + row.Group, lang);
            if (row.Pointer) e.PointerText = PhStrings.Get("w_pointer", lang);
            string nodata = PhStrings.Get("w_nodata", lang);
            switch (row.Key)
            {
                case "drift_change_cm":
                    e.Kind = WitnessEntryKind.Arrow;
                    if (val is double)
                    {
                        double d = (double)val;
                        e.Direction = d > 0.25 ? 1 : (d < -0.25 ? -1 : 0);
                        e.ValueText = Signed(d, 1) + " cm";
                        e.Detail = PhStrings.Get(e.Direction > 0 ? "w_toward" : e.Direction < 0 ? "w_away" : "w_none_shift", lang);
                    }
                    else e.ValueText = nodata;
                    break;
                case "flinch_latency_ms":
                    e.Kind = WitnessEntryKind.Number;
                    e.ValueText = val is double ? ((double)val).ToString("F0", CultureInfo.InvariantCulture) + " ms" : nodata;
                    break;
                case "flinch_strength":
                    e.Kind = WitnessEntryKind.Word;
                    string str = val as string;
                    e.Direction = str == "strong" ? 1 : (str == "weak" ? 0 : -1);
                    e.ValueText = PhStrings.Get(str == "strong" ? "w_strong" : str == "weak" ? "w_weak" : "w_none", lang);
                    e.HasData = str != null;
                    break;
                default:   // ownership, q4 and any future row on the -3..+3 scale
                    e.Kind = WitnessEntryKind.Bar;
                    if (val is double)
                    {
                        double q = (double)val;
                        e.Fraction = (q + 3.0) / 6.0; if (e.Fraction < 0) e.Fraction = 0; if (e.Fraction > 1) e.Fraction = 1;
                        e.ValueText = Signed(q, 1);
                    }
                    else e.ValueText = nodata;
                    break;
            }
            return e;
        }

        private static string Signed(double v, int dp)
        {
            return (v > 0 ? "+" : "") + v.ToString("F" + dp, CultureInfo.InvariantCulture);
        }
    }
}
