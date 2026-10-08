using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// Witness screen copy and grouping: the closing text says what changed and that the participant noticed it, nothing
    /// stronger; rows sit in Body / Mind / The one who noticed; the witness_summary event follows the contract fixture.
    /// </summary>
    public class WitnessCopyTests
    {
        private static readonly string[] ExpectedEn =
        {
            "The body changed. The touch changed. The feeling of \"mine\" changed.",
            "You noticed every change.",
            "Tattva 5: consciousness is beyond the body and mind.",
        };

        private static readonly string[] ExpectedHi =
        {
            "शरीर बदला। स्पर्श बदला। \"मेरा\" होने का एहसास बदला।",
            "हर बदलाव को आपने देखा।",
            "तत्त्व 5: चेतना शरीर और मन से परे है।",
        };

        private const string AgencyEn = "Even \"I did that\" changed.";
        private const string AgencyHi = "\"यह मैंने किया\" का एहसास भी बदला।";

        // ---- closing copy ---------------------------------------------------------------------------------------------

        [Test]
        public void ClosingLines_AreInTheAgreedOrder_BothLanguages()
        {
            var w = UiModelTests.Scripted(false);
            CollectionAssert.AreEqual(ExpectedEn, w.ClosingLines("en"));
            CollectionAssert.AreEqual(ExpectedHi, w.ClosingLines("hi"));
        }

        [Test]
        public void AgencyLine_IsSecond_AndOnlyWhenTheAgencyPhaseRan()
        {
            var w = UiModelTests.Scripted(false);
            Assert.IsFalse(w.AgencyRan, "no agency phase, no agency line");
            Assert.IsFalse(w.ClosingLines("en").Any(l => l.Contains("I did that")));
            Assert.IsFalse(w.ClosingLines("hi").Any(l => l.Contains("यह मैंने किया")));

            w.AgencyRan = true;
            CollectionAssert.AreEqual(new[] { ExpectedEn[0], AgencyEn, ExpectedEn[1], ExpectedEn[2] }, w.ClosingLines("en"));
            CollectionAssert.AreEqual(new[] { ExpectedHi[0], AgencyHi, ExpectedHi[1], ExpectedHi[2] }, w.ClosingLines("hi"));
        }

        [Test]
        public void ClosingLine_IsTheLinesJoinedByNewline()
        {
            var w = UiModelTests.Scripted(false);
            foreach (var lang in new[] { "en", "hi" })
                Assert.AreEqual(string.Join("\n", w.ClosingLines(lang)), w.ClosingLine(lang));
            Assert.AreEqual(WitnessSummary.ClosingEn, w.ClosingLine("en"), "the constants are the text without the agency line");
            Assert.AreEqual(WitnessSummary.ClosingHi, w.ClosingLine("hi"));
            w.AgencyRan = true;
            Assert.AreNotEqual(WitnessSummary.ClosingEn, w.ClosingLine("en"));
            Assert.AreEqual(4, w.ClosingLine("en").Split('\n').Length);
            Assert.AreEqual(w.ClosingLine("en"), WitnessView.Build(w, "en").ClosingLine);
            Assert.AreEqual(w.ClosingLine("hi"), WitnessView.Build(w, "hi").ClosingLine);
        }

        [Test]
        public void Lines_StillEndWithTheClosingText()
        {
            var w = UiModelTests.Scripted(false);
            Assert.AreEqual(w.ClosingLine("en"), w.Lines("en").Last());
            Assert.AreEqual(w.ClosingLine("hi"), w.Lines("hi").Last());
        }

        [Test]
        public void ClosingText_DoesNotClaimWhatTheExperimentCannotShow()
        {
            var w = UiModelTests.Scripted(false); w.AgencyRan = true;
            foreach (var bad in new[] { "did not change", "manufactured", "dissolved", "appearance", "not the Self" })
                StringAssert.DoesNotContain(bad, w.ClosingLine("en"));
            foreach (var bad in new[] { "वह नहीं बदला", "आभास है", "स्वयं नहीं" })
                StringAssert.DoesNotContain(bad, w.ClosingLine("hi"));
        }

        [Test]
        public void EventData_ClosingFields_AreTheJoinedText()
        {
            var w = UiModelTests.Scripted(false);
            var j = JObject.FromObject(w.ToEventData());
            Assert.AreEqual(ExpectedEn[0] + "\n" + ExpectedEn[1] + "\n" + ExpectedEn[2], j["closing_en"].Value<string>());
            Assert.AreEqual(ExpectedHi[0] + "\n" + ExpectedHi[1] + "\n" + ExpectedHi[2], j["closing_hi"].Value<string>());
            w.AgencyRan = true;
            j = JObject.FromObject(w.ToEventData());
            Assert.AreEqual(w.ClosingLine("en"), j["closing_en"].Value<string>());
            StringAssert.Contains(AgencyEn, j["closing_en"].Value<string>());
            StringAssert.Contains(AgencyHi, j["closing_hi"].Value<string>());
        }

        // ---- strings --------------------------------------------------------------------------------------------------

        [Test]
        public void Title_IsWhatChanged_InBothLanguages()
        {
            foreach (var key in new[] { "w_title", "ph_witness" })
            {
                Assert.AreEqual("What changed?", PhStrings.Get(key, "en"), key);
                Assert.AreEqual("क्या बदला?", PhStrings.Get(key, "hi"), key);
            }
            Assert.AreEqual("What changed?", WitnessView.Build(UiModelTests.Scripted(false), "en").Title);
            Assert.AreEqual("क्या बदला?", WitnessView.Build(UiModelTests.Scripted(false), "hi").Title);
            Assert.AreEqual("What changed?", PhStrings.PhaseWord(PhPhase.Witness, "en"));
        }

        [Test]
        public void GroupHeaders_HaveEnglishAndHindi()
        {
            var expect = new[]
            {
                new[] { "w_group_body", "Body", "शरीर" },
                new[] { "w_group_mind", "Mind", "मन" },
                new[] { "w_group_observer", "The one who noticed", "देखने वाला" },
            };
            foreach (var e in expect)
            {
                Assert.IsTrue(PhStrings.Has(e[0]), e[0]);
                Assert.AreEqual(e[1], PhStrings.Get(e[0], "en"));
                Assert.AreEqual(e[2], PhStrings.Get(e[0], "hi"));
            }
        }

        // ---- groups ---------------------------------------------------------------------------------------------------

        [Test]
        public void EveryRow_HasAGroup()
        {
            var w = UiModelTests.Scripted(true);
            var expect = new Dictionary<string, string>
            {
                { "drift_change_cm", "body" }, { "flinch_latency_ms", "body" }, { "flinch_strength", "body" },
                { "ownership", "mind" }, { "q4", "observer" },
            };
            Assert.AreEqual(expect.Count, w.Rows.Count);
            foreach (var r in w.Rows) Assert.AreEqual(expect[r.Key], r.Group, r.Key);
        }

        [Test]
        public void Groups_AreContiguous_InTheOrderBodyMindObserver()
        {
            var order = new List<string>();
            foreach (var r in UiModelTests.Scripted(true).Rows) if (order.Count == 0 || order.Last() != r.Group) order.Add(r.Group);
            CollectionAssert.AreEqual(new[] { "body", "mind", "observer" }, order);
        }

        [TestCase("en", "Body", "Mind", "The one who noticed")]
        [TestCase("hi", "शरीर", "मन", "देखने वाला")]
        public void View_CarriesEachEntrysGroup_AndTheLocalizedHeader(string lang, string body, string mind, string observer)
        {
            var v = WitnessView.Build(UiModelTests.Scripted(true), lang);
            foreach (var card in new[] { v.Sync, v.Async })
            {
                Assert.AreEqual("body", card.Entry("drift_change_cm").Group); Assert.AreEqual(body, card.Entry("drift_change_cm").GroupHeader);
                Assert.AreEqual("body", card.Entry("flinch_latency_ms").Group);
                Assert.AreEqual("body", card.Entry("flinch_strength").Group);
                Assert.AreEqual("mind", card.Entry("ownership").Group); Assert.AreEqual(mind, card.Entry("ownership").GroupHeader);
                Assert.AreEqual("observer", card.Entry("q4").Group); Assert.AreEqual(observer, card.Entry("q4").GroupHeader);
                foreach (var e in card.Entries) Assert.IsNotNull(e.Group, e.Key);
            }
        }

        [Test]
        public void Panel_HeightGrowsOnlyAsMuchAsTheRowsNeed()
        {
            const float oldFixedCardH = 420f;
            var plain = WitnessView.Build(UiModelTests.Scripted(false), "en");
            var withQ4 = WitnessView.Build(UiModelTests.Scripted(true), "en");
            float hPlain = PhWitnessPanel.CardHeight(plain.Sync), hQ4 = PhWitnessPanel.CardHeight(withQ4.Sync);
            Assert.AreEqual(hPlain, PhWitnessPanel.CardHeight(plain.Async), "both cards carry the same rows");
            Assert.LessOrEqual(hPlain, oldFixedCardH, "Body + Mind headers fit where the old card was");
            Assert.AreEqual(32f + 104f, hQ4 - hPlain, 1e-4, "the q4 row costs its group header plus one pointer row, nothing else");
            Assert.Less(PhWitnessPanel.CardHeight(withQ4.Sync), 520f);
            foreach (var lang in new[] { "en", "hi" })
                Assert.AreEqual(hQ4, PhWitnessPanel.CardHeight(WitnessView.Build(UiModelTests.Scripted(true), lang).Sync), "same layout in " + lang);
        }

        [Test]
        public void Q5Row_SitsInTheMindGroup_AndCostsExactlyOneRow()
        {
            var plain = UiModelTests.Scripted(true);
            var withQ5 = UiModelTests.Scripted(true);
            withQ5.Sync.Agency = 1.0;
            withQ5 = WitnessSummary.Build(withQ5.Sync, withQ5.Async);          // rebuild: the q5 row appears when either condition has the answer
            CollectionAssert.AreEqual(new[] { "drift_change_cm", "flinch_latency_ms", "flinch_strength", "ownership", "q5", "q4" }, withQ5.Rows.Select(r => r.Key).ToArray());
            Assert.AreEqual("mind", withQ5.Row("q5").Group);
            Assert.IsFalse(withQ5.Row("q5").Pointer, "q5 is data, not a pointer");
            var v = WitnessView.Build(withQ5, "en");
            Assert.AreEqual("mind", v.Sync.Entry("q5").Group); Assert.AreEqual("Mind", v.Sync.Entry("q5").GroupHeader);
            Assert.IsTrue(v.Sync.Entry("q5").HasData); Assert.IsFalse(v.Async.Entry("q5").HasData, "asked in the last condition only");
            Assert.AreEqual("I caused that movement", v.Sync.Entry("q5").Label);
            Assert.AreEqual("वह गति मैंने कराई", WitnessView.Build(withQ5, "hi").Sync.Entry("q5").Label);
            float rowH = PhWitnessPanel.CardHeight(v.Sync) - PhWitnessPanel.CardHeight(WitnessView.Build(plain, "en").Sync);
            Assert.AreEqual(76f, rowH, 1e-4, "one row inside the existing Mind group, no extra header");
        }

        // ---- witness_summary on the wire ------------------------------------------------------------------------------

        private static WitnessSummary WithQuestionnaires()
        {
            var sync = new ConditionResult { Condition = PhCondition.Sync, PreDriftCm = 0.5, PostDriftCm = 4.1, Ownership = 2.0, Control = -1.0, Awareness = 2.0,
                Threat = new ThreatResponse { EmgPeakX = 4.6, EmgLatencyMs = 180 } };
            var async = new ConditionResult { Condition = PhCondition.Async, PreDriftCm = 0.2, PostDriftCm = 0.6, Ownership = -1.5, Control = 0.5, Awareness = 2.0,
                Threat = new ThreatResponse { EmgPeakX = 1.8, EmgLatencyMs = 330 } };
            return WitnessSummary.Build(sync, async);
        }

        [Test]
        public void EventData_QuestionnaireMeans_GoOutOnTheContractScale1To7()
        {
            var j = JObject.FromObject(WithQuestionnaires().ToEventData());
            Assert.AreEqual(6.0, j["sync"]["ownership"].Value<double>(), 1e-9);      // +2 shown
            Assert.AreEqual(2.5, j["async"]["ownership"].Value<double>(), 1e-9);     // -1.5 shown
            Assert.AreEqual(3.0, j["sync"]["control"].Value<double>(), 1e-9);        // -1 shown
            Assert.AreEqual(4.5, j["async"]["control"].Value<double>(), 1e-9);       // +0.5 shown
            Assert.AreEqual(6.0, j["sync"]["witness_q4"].Value<double>(), 1e-9);
            Assert.AreEqual(6.0, j["async"]["witness_q4"].Value<double>(), 1e-9);
            foreach (var side in new[] { "sync", "async" })
            {
                Assert.IsNull(j[side]["q4"], "witness_q4 replaces q4 in " + side);
                Assert.IsNull(j[side]["agency_q5"], "no q5 answered, no key");
            }
        }

        [Test]
        public void EventData_Differences_AreUnchangedByTheShift_AndQ4IsRenamed()
        {
            var j = JObject.FromObject(WithQuestionnaires().ToEventData());
            var d = (JObject)j["sync_minus_async"];
            Assert.AreEqual(3.5, d["ownership"].Value<double>(), 1e-9);      // 6.0 - 2.5 = 2.0 - (-1.5)
            Assert.AreEqual(0.0, d["witness_q4"].Value<double>(), 1e-9);
            Assert.IsNull(d["q4"]);
            Assert.AreEqual(3.2, d["drift_change_cm"].Value<double>(), 1e-9);
        }

        [Test]
        public void EventData_OtherKeysAreKept()
        {
            var j = JObject.FromObject(WithQuestionnaires().ToEventData());
            foreach (var k in new[] { "drift_change_cm", "flinch_latency_ms", "flinch_strength", "flinch_emg_peak_x", "flinch_wrist_peak_mps", "ownership", "control", "witness_q4" })
                Assert.IsNotNull(j["sync"][k], k);
            Assert.AreEqual(3.6, j["sync"]["drift_change_cm"].Value<double>(), 1e-9);
            Assert.AreEqual("strong", j["sync"]["flinch_strength"].Value<string>());
            Assert.IsNotNull(j["closing_en"]); Assert.IsNotNull(j["closing_hi"]);
        }

        [Test]
        public void EventData_AgencyQ5_OnlyWhenAnswered_OnTheContractScale()
        {
            var w = WithQuestionnaires();
            w.Sync.Agency = 1.0;
            var j = JObject.FromObject(w.ToEventData());
            Assert.AreEqual(5.0, j["sync"]["agency_q5"].Value<double>(), 1e-9);
            Assert.IsNull(j["async"]["agency_q5"]);
        }

        [Test]
        public void EventData_ConditionOrder_IsWrittenInRunOrder_AndLeftOutWhenUnknown()
        {
            var w = WithQuestionnaires();
            Assert.IsNull(JObject.FromObject(w.ToEventData())["condition_order"], "unknown order: no key");
            w.ConditionOrder = new[] { PhCondition.Async, PhCondition.Sync };
            var order = (JArray)JObject.FromObject(w.ToEventData())["condition_order"];
            CollectionAssert.AreEqual(new[] { "async", "sync" }, order.Select(t => t.Value<string>()).ToArray());
        }

        [Test]
        public void EventData_FollowsTheSchemaShape_ForWitnessSummary()
        {
            // event.schema.json: data is an object, `sync` and/or `async` are objects, condition_order holds 1-2 of sync|async
            var w = WithQuestionnaires(); w.ConditionOrder = new[] { PhCondition.Sync, PhCondition.Async };
            var j = JObject.FromObject(w.ToEventData());
            Assert.AreEqual(JTokenType.Object, j["sync"].Type); Assert.AreEqual(JTokenType.Object, j["async"].Type);
            var order = ((JArray)j["condition_order"]).Select(t => t.Value<string>()).ToList();
            Assert.That(order.Count, Is.InRange(1, 2));
            foreach (var c in order) CollectionAssert.Contains(new[] { "sync", "async" }, c);
            // the contract fixture event.11.json carries these per-condition keys; each is present here
            foreach (var k in new[] { "drift_change_cm", "ownership", "control", "flinch_emg_peak_x", "witness_q4" })
            { Assert.IsNotNull(j["sync"][k], k); Assert.IsNotNull(j["async"][k], k); }
        }
    }
}
