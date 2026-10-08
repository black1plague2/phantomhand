using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// Illusion-strength changes from the R1 research (D18): a rating after BOTH conditions in every mode (short in demo_mode),
    /// a calmer stone. (The demo induction length is in PhaseAndParamsTests, the ASYNC swap guard in StrokeSchedulerTests.)
    /// </summary>
    public class IllusionStrengthTests
    {
        private sealed class FakeSession : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex { get { return 0; } }
        }

        private sealed class Mod
        {
            public readonly FakeSession S = new FakeSession();
            public readonly PhantomHandModule M = new PhantomHandModule();
            public readonly List<TrialEvent> Ev = new List<TrialEvent>();

            public Mod(string json, bool additions = false)
            {
                M.AdditionsEnabled = additions;
                M.OnTrialEvent += Ev.Add;
                M.Configure(Ph.P(json), S);
                M.Begin();
            }

            public double Now { get { return S.Clock.NowMs; } }
            public void Skip(PhPhase to) { int g = 0; while (M.CurrentPhase != to && g++ < 60) Assert.IsTrue(M.AdvancePhase(), "stuck in " + M.CurrentPhase); Assert.AreEqual(to, M.CurrentPhase); }

            /// <summary>Items of the next questionnaire.</summary>
            public string[] NextItems() { Skip(PhPhase.Questionnaire); return M.CurrentQuestionnaire.Items.Select(i => i.Id).ToArray(); }

            public void Answer(params int[] values) { foreach (var v in values) Assert.IsTrue(M.SubmitQuestionnaireAnswer(v)); }

            public void PlayAgencyWithSelfCloses()
            {
                Skip(PhPhase.Agency);
                for (double t = 0; t < 30; t += 0.02)
                {
                    S.Clock.Advance(20); M.Tick(Now);
                    if (M.CurrentPhase != PhPhase.Agency) break;
                    M.Agency.Tick(Now, 0.0);
                }
            }

            public List<TrialEvent> Of(string type) { return Ev.Where(e => e.Type == type).ToList(); }
            public static JObject Data(TrialEvent e) { return JObject.FromObject(e.Data); }
        }

        private const string Demo = "{\"demo_mode\":true}";
        private const string DemoA5 = "{\"demo_mode\":true,\"agency_enabled\":true,\"autonomous_close_enabled\":true}";

        // ---- the questionnaire after each condition -------------------------------------------------------------------

        [Test]
        public void Demo_FirstConditionAsksQ1Only_LastAsksQ1_AndTheQ4PointerWhenTheRuleSaysSo()
        {
            var plain = new Mod(Demo);                                        // additions off: no q4
            CollectionAssert.AreEqual(new[] { "q1" }, plain.NextItems());
            plain.Answer(1);
            CollectionAssert.AreEqual(new[] { "q1" }, plain.NextItems());

            var withQ4 = new Mod(Demo, additions: true);                      // the existing q4 rule: additions on and voiceover on
            CollectionAssert.AreEqual(new[] { "q1" }, withQ4.NextItems(), "no q4 after the first condition in demo_mode");
            withQ4.Answer(1);
            CollectionAssert.AreEqual(new[] { "q1", "q4" }, withQ4.NextItems());

            var noVoice = new Mod("{\"demo_mode\":true,\"voiceover_enabled\":false}", additions: true);
            noVoice.NextItems(); noVoice.Answer(1);
            CollectionAssert.AreEqual(new[] { "q1" }, noVoice.NextItems(), "q4 follows the voiceover_enabled rule");
        }

        [Test]
        public void Demo_LastConditionAddsQ5_OnlyWhenTheAgencyRan()
        {
            var a = new Mod(DemoA5);
            CollectionAssert.AreEqual(new[] { "q1" }, a.NextItems(), "the agency phase comes later, in the last condition");
            a.Answer(1);
            a.PlayAgencyWithSelfCloses();
            Assert.AreEqual(2, a.M.Agency.AutonomousCloses);
            CollectionAssert.AreEqual(new[] { "q1", "q5" }, a.NextItems());

            var all = new Mod(DemoA5, additions: true);
            all.NextItems(); all.Answer(1);
            all.PlayAgencyWithSelfCloses();
            CollectionAssert.AreEqual(new[] { "q1", "q4", "q5" }, all.NextItems(), "q1 plus q4 plus q5, in that order");

            var noAgency = new Mod(Demo);                                     // agency off: never q5
            noAgency.NextItems(); noAgency.Answer(1);
            CollectionAssert.AreEqual(new[] { "q1" }, noAgency.NextItems());
        }

        [Test]
        public void OutsideDemoMode_NothingChanges_Q1ToQ3AfterEachCondition_Q4Q5ByTheirRules()
        {
            var m = new Mod("{}");
            CollectionAssert.AreEqual(new[] { "q1", "q2", "q3" }, m.NextItems());
            m.Answer(1, 1, 1);
            CollectionAssert.AreEqual(new[] { "q1", "q2", "q3" }, m.NextItems());

            var full = new Mod("{\"agency_enabled\":true,\"autonomous_close_enabled\":true}", additions: true);
            CollectionAssert.AreEqual(new[] { "q1", "q2", "q3", "q4" }, full.NextItems(), "q4 after every condition");
            full.Answer(1, 1, 1, 1);
            full.PlayAgencyWithSelfCloses();
            CollectionAssert.AreEqual(new[] { "q1", "q2", "q3", "q4", "q5" }, full.NextItems(), "q5 after the last one only");
        }

        [Test]
        public void Demo_ScriptedRun_TwoQuestionnaireRounds_OwnershipOnBothCards()
        {
            var m = new Mod(Demo);                                            // async_first: cond 0 = ASYNC, cond 1 = SYNC
            m.NextItems(); m.Answer(-1);
            m.NextItems(); m.Answer(2);
            Assert.AreEqual(-1.0, m.M.Results[0].Ownership.Value, 1e-9, "only q1 answered: Ownership is q1");
            Assert.AreEqual(2.0, m.M.Results[1].Ownership.Value, 1e-9);
            Assert.IsNull(m.M.Results[0].Control); Assert.IsNull(m.M.Results[1].Control);

            m.Skip(PhPhase.Witness);
            var items = m.Of("questionnaire_item");
            Assert.AreEqual(2, items.Count);
            CollectionAssert.AreEqual(new int?[] { 0, 1 }, items.Select(e => e.Trial).ToArray());
            CollectionAssert.AreEqual(new[] { "q1", "q1" }, items.Select(e => Mod.Data(e)["item"].Value<string>()).ToArray());

            var w = m.M.Witness;
            var row = w.Row("ownership");
            Assert.AreEqual(2.0, (double)row.SyncValue, 1e-9); Assert.AreEqual(-1.0, (double)row.AsyncValue, 1e-9);
            Assert.AreEqual(3.0, row.Difference.Value, 1e-9);
            var v = WitnessView.Build(w, "en");
            Assert.IsTrue(v.Sync.Entry("ownership").HasData, "SYNC card has its ownership bar");
            Assert.IsTrue(v.Async.Entry("ownership").HasData, "ASYNC card has its ownership bar");
            var d = Mod.Data(m.Of("witness_summary").Single());
            Assert.AreEqual(6.0, d["sync"]["ownership"].Value<double>(), 1e-9); Assert.AreEqual(3.0, d["async"]["ownership"].Value<double>(), 1e-9);
            Assert.AreEqual(3.0, d["sync_minus_async"]["ownership"].Value<double>(), 1e-9);
        }

        [Test]
        public void Demo_WithAgency_Q5GoesIntoTheLastConditionsResult_AndTheWitness()
        {
            var m = new Mod(DemoA5);
            m.NextItems(); m.Answer(0);
            m.PlayAgencyWithSelfCloses();
            m.NextItems(); m.Answer(1, 3);                                    // q1 = +1, q5 = +3
            Assert.AreEqual(1.0, m.M.Results[1].Ownership.Value, 1e-9);
            Assert.AreEqual(3.0, m.M.Results[1].Agency.Value, 1e-9);
            m.Skip(PhPhase.Witness);
            Assert.IsTrue(m.M.Witness.AgencyRan);
            Assert.IsNotNull(m.M.Witness.Row("q5"));
        }

        // ---- the stone ------------------------------------------------------------------------------------------------

        [Test]
        public void StoneThudAndCreak_PlayAtHalfVolume()
        {
            Assert.AreEqual(0.5f, ThreatDrop.ThudVolume);
            Assert.AreEqual(0.5f, ThreatDrop.CreakVolume);
        }
    }
}
