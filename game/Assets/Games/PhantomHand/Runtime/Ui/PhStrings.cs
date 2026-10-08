using System.Collections.Generic;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// EN + HI string table for every in-headset label (U4). Plain, layman words (docs/design/OPUS_DESIGN_V2.md v3
    /// amendment: titles only, no helper paragraphs). Questionnaire item texts and the two scale anchors stay in
    /// <see cref="Questionnaire"/>; the witness row labels and the closing line stay in <see cref="WitnessSummary"/>
    /// (they are already EN + HI there). Unknown key: the key itself comes back, so a missing string is visible, never a crash.
    /// </summary>
    public static class PhStrings
    {
        public const string En = "en", Hi = "hi";

        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // calibration
            { "calib_title",      new[] { "Rest your right forearm inside the outline, palm down", "अपनी दाहिनी बांह रेखा के अंदर रखें, हथेली नीचे" } },
            { "calib_holding",    new[] { "Hold still", "ऐसे ही रुकें" } },
            { "calib_done",       new[] { "Got it", "ठीक है" } },
            { "calib_lost",       new[] { "I can't see your right hand", "आपका दाहिना हाथ दिख नहीं रहा" } },

            // drift probe
            { "probe_title",      new[] { "Keep your right hand still. With your left index, point above the ruler to where you feel your right index is, then hold still.",
                                          "अपना दाहिना हाथ स्थिर रखें। बाएँ हाथ की तर्जनी से पैमाने के ऊपर वहाँ इशारा करें जहाँ आपको दाहिनी तर्जनी महसूस होती है, फिर रुकें।" } },
            { "probe_holding",    new[] { "Hold still", "ऐसे ही रुकें" } },
            { "probe_done",       new[] { "Recorded", "दर्ज हुआ" } },
            { "probe_arm_moved",  new[] { "Please put your arm back on the outline", "कृपया अपना हाथ वापस रेखा पर रखें" } },

            // questionnaire
            { "q_back",           new[] { "Back", "पीछे" } },
            { "q_progress",       new[] { "{0} of {1}", "{1} में से {0}" } },

            // witness
            { "w_title",          new[] { "What changed?", "क्या बदला?" } },
            { "w_group_body",     new[] { "Body", "शरीर" } },
            { "w_group_mind",     new[] { "Mind", "मन" } },
            { "w_group_observer", new[] { "The one who noticed", "देखने वाला" } },
            { "w_preliminary",    new[] { "Preliminary", "प्रारंभिक" } },
            { "w_sync",           new[] { "In sync", "साथ-साथ" } },
            { "w_async",          new[] { "Delayed", "देरी से" } },
            { "w_pointer",        new[] { "A pointer, not proof", "एक संकेत, प्रमाण नहीं" } },
            { "w_toward",         new[] { "toward the virtual hand", "वर्चुअल हाथ की ओर" } },
            { "w_away",           new[] { "away from the virtual hand", "वर्चुअल हाथ से दूर" } },
            { "w_none_shift",     new[] { "no shift", "कोई खिसकाव नहीं" } },
            { "w_strong",         new[] { "Strong", "तेज़" } },
            { "w_weak",           new[] { "Weak", "हल्का" } },
            { "w_none",           new[] { "None", "कोई नहीं" } },
            { "w_nodata",         new[] { "no data", "कोई डेटा नहीं" } },
            { "w_agency_facts",   new[] { "You closed it {0} times. It closed by itself {1} times.", "आपने इसे {0} बार बंद किया। यह अपने आप {1} बार बंद हुआ।" } },

            // hud
            { "hud_sleeve_offline", new[] { "Sleeve offline", "स्लीव बंद" } },
            { "hud_emg_offline",    new[] { "EMG offline", "EMG बंद" } },
            { "hud_time_left",      new[] { "{0} left", "{0} बाकी" } },
            { "hud_spectator",      new[] { "Spectator details", "दर्शक विवरण" } },
            { "hud_condition",      new[] { "Condition", "स्थिति" } },
            { "hud_emg",            new[] { "Muscle signal", "मांसपेशी संकेत" } },
            { "hud_strokes",        new[] { "Brush strokes", "ब्रश स्ट्रोक" } },

            // phase words (what the participant needs to know, nothing more)
            { "ph_idle",          new[] { "Get ready", "तैयार हो जाएँ" } },
            { "ph_calibrate",     new[] { "Get ready", "तैयार हो जाएँ" } },
            { "ph_probe",         new[] { "Point", "इशारा करें" } },
            { "ph_induction",     new[] { "Watch the hand", "हाथ को देखें" } },
            { "ph_self_touch",    new[] { "Your turn", "अब आपकी बारी" } },
            { "ph_agency",        new[] { "Squeeze", "दबाएँ" } },
            { "ph_agency_rest",    new[] { "Relax your hand", "हाथ ढीला छोड़ें" } },
            { "ph_agency_squeeze", new[] { "Squeeze hard, once", "एक बार ज़ोर से मुट्ठी कसें" } },
            { "ph_agency_driven",  new[] { "Squeeze to close the hand", "मुट्ठी कसें, हाथ बंद होगा" } },
            { "ph_agency_watch",   new[] { "Relax. Just watch", "ढीला छोड़ें। बस देखें" } },
            { "ph_threat",        new[] { "Stay still", "स्थिर रहें" } },
            { "ph_questionnaire", new[] { "A few questions", "कुछ सवाल" } },
            { "ph_dissolve",      new[] { "The touch is still here", "स्पर्श अब भी यहीं है" } },
            { "ph_reveal",        new[] { "Your hand was here all along", "आपका हाथ शुरू से यहीं था" } },
            { "ph_witness",       new[] { "What changed?", "क्या बदला?" } },
            { "ph_done",          new[] { "Finished", "समाप्त" } },
        };

        public static IEnumerable<string> Keys { get { return Table.Keys; } }

        public static bool Has(string key) { return Table.ContainsKey(key); }

        public static string Get(string key, string lang)
        {
            string[] v;
            if (!Table.TryGetValue(key, out v)) return key;
            return lang == Hi ? v[1] : v[0];
        }

        public static string Format(string key, string lang, params object[] args)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key, lang), args);
        }

        /// <summary>The short participant-facing word for a phase.</summary>
        public static string PhaseWord(PhPhase phase, string lang)
        {
            switch (phase)
            {
                case PhPhase.Calibrate: return Get("ph_calibrate", lang);
                case PhPhase.ProbePre:
                case PhPhase.ProbePost: return Get("ph_probe", lang);
                case PhPhase.Induction: return Get("ph_induction", lang);
                case PhPhase.SelfTouch: return Get("ph_self_touch", lang);
                case PhPhase.Agency: return Get("ph_agency", lang);
                case PhPhase.Threat: return Get("ph_threat", lang);
                case PhPhase.Questionnaire: return Get("ph_questionnaire", lang);
                case PhPhase.Dissolve: return Get("ph_dissolve", lang);
                case PhPhase.Reveal: return Get("ph_reveal", lang);
                case PhPhase.Witness: return Get("ph_witness", lang);
                case PhPhase.Done: return Get("ph_done", lang);
                default: return Get("ph_idle", lang);
            }
        }

        /// <summary>The caption for one step of the agency phase (what to do right now).</summary>
        public static string AgencyWord(AgencyStep step, string lang)
        {
            switch (step)
            {
                case AgencyStep.Rest: return Get("ph_agency_rest", lang);
                case AgencyStep.Squeeze: return Get("ph_agency_squeeze", lang);
                case AgencyStep.Driven: return Get("ph_agency_driven", lang);
                default: return Get("ph_agency_watch", lang);
            }
        }

        public static string ProbeInstruction(string lang) { return Get("probe_title", lang); }
    }
}
