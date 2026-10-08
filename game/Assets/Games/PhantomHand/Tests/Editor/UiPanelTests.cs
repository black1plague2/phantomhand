using System.Collections.Generic;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>U4 uGUI tests (edit mode, no poke wiring): text fits its box in EN and HI, button sizes, questionnaire order and Back, witness numbers on screen.</summary>
    public class UiPanelTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _made) if (g != null) Object.DestroyImmediate(g);
            _made.Clear();
        }

        private T NewPanel<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            _made.Add(go);
            return go.AddComponent<T>();
        }

        /// <summary>Preferred height of a wrapped text at its own width, panel pixels (= millimetres).</summary>
        private static float NeededHeight(Text t)
        {
            var settings = t.GetGenerationSettings(new Vector2(t.rectTransform.rect.width, 0f));
            settings.scaleFactor = 1f;
            settings.generateOutOfBounds = true;
            var gen = new TextGenerator();
            return gen.GetPreferredHeight(t.text, settings);
        }

        private static void AssertTextsFit(Component panel, string what)
        {
            foreach (var t in panel.GetComponentsInChildren<Text>(true))
            {
                if (string.IsNullOrEmpty(t.text)) continue;
                float need = NeededHeight(t), have = t.rectTransform.rect.height;
                Assert.LessOrEqual(need, have * 1.05f, what + " / " + t.name + " \"" + t.text + "\" needs " + need + " mm of " + have);
            }
        }

        [TestCase("en")]
        [TestCase("hi")]
        public void Instruction_TextFits(string lang)
        {
            var p = NewPanel<PhInstructionPanel>(); p.Build(null);
            p.Show(PhStrings.Get("calib_title", lang), PhStrings.Get("calib_holding", lang), 0.5f, PhUiKit.Info, true);
            AssertTextsFit(p, "calibration " + lang);
            p.Show(PhStrings.ProbeInstruction(lang), PhStrings.Get("probe_holding", lang), 0.5f, PhUiKit.Info, false);
            AssertTextsFit(p, "probe " + lang);
            p.Show(PhStrings.Get("probe_arm_moved", lang), "", 0f, PhUiKit.Warn, false);
            AssertTextsFit(p, "arm moved " + lang);
        }

        [TestCase("en")]
        [TestCase("hi")]
        public void Questionnaire_TextFits_AndButtonsAreAtLeast4cm(string lang)
        {
            var p = NewPanel<PhQuestionnairePanel>(); p.Build(null, false);
            var q = new Questionnaire(new[] { Questionnaire.Q1, Questionnaire.Q2, Questionnaire.Q3, Questionnaire.Q4 });
            p.Show(q, v => q.Answer(v) != null, () => q.Back(), lang, 0);
            for (int i = 0; i < 4; i++)
            {
                AssertTextsFit(p, "questionnaire item " + (i + 1) + " " + lang);
                q.Answer(0); p.Show(q, v => q.Answer(v) != null, () => q.Back(), lang, 0);
            }
            Assert.AreEqual(7, p.Buttons.Length);
            foreach (var b in p.Buttons)
            {
                var cm = PhUiKit.SizeCm((RectTransform)b.transform);
                Assert.GreaterOrEqual(cm.x, 4f, b.name); Assert.GreaterOrEqual(cm.y, 4f, b.name);
            }
        }

        [TestCase("en")]
        [TestCase("hi")]
        public void Witness_TextFits(string lang)
        {
            var p = NewPanel<PhWitnessPanel>(); p.Build(null, false);
            p.Show(UiModelTests.Scripted(true), lang, 0);
            AssertTextsFit(p, "witness " + lang);
        }

        [TestCase("en")]
        [TestCase("hi")]
        public void Witness_EveryTextStaysInsideThePanel_WhenTheCardsGrow(string lang)
        {
            var p = NewPanel<PhWitnessPanel>(); p.Build(null, false);
            p.Show(UiModelTests.Scripted(true), lang, 0);       // the longest default screen: Body + Mind + The one who noticed
            AssertTextsInsidePanel(p);
        }

        private static void AssertTextsInsidePanel(PhWitnessPanel p)
        {
            var root = new Vector3[4]; ((RectTransform)p.transform).GetWorldCorners(root);
            Assert.GreaterOrEqual(((RectTransform)p.transform).sizeDelta.y, PhWitnessPanel.HeightMm, "the panel never gets smaller than the scene's");
            var c = new Vector3[4];
            foreach (var t in p.GetComponentsInChildren<Text>(true))
            {
                t.rectTransform.GetWorldCorners(c);
                Assert.GreaterOrEqual(c[0].y, root[0].y - 1e-4f, t.name + " hangs below the panel");
                Assert.LessOrEqual(c[2].y, root[2].y + 1e-4f, t.name + " pokes above the panel");
            }
        }

        /// <summary>The agency run: a q5 row in the Mind group, the facts line under the cards and four closing lines.</summary>
        private static WitnessSummary ScriptedAgency()
        {
            var sync = new ConditionResult { Condition = PhCondition.Sync, PreDriftCm = 0.5, PostDriftCm = 4.1, Ownership = 2.0, Awareness = 2, Agency = 1.0,
                Threat = new ThreatResponse { EmgPeakX = 4.6, EmgLatencyMs = 180 } };
            var async = new ConditionResult { Condition = PhCondition.Async, PreDriftCm = 0.2, PostDriftCm = 0.6, Ownership = -1.5, Awareness = 2,
                Threat = new ThreatResponse { EmgPeakX = 1.8, EmgLatencyMs = 330 } };
            var w = WitnessSummary.Build(sync, async);
            w.AgencyRan = true; w.DrivenCloses = 3; w.AutonomousCloses = 2;
            return w;
        }

        [TestCase("en")]
        [TestCase("hi")]
        public void Witness_WithTheAgencyRun_TextFits_AndStaysInsideThePanel(string lang)
        {
            var p = NewPanel<PhWitnessPanel>(); p.Build(null, false);
            p.Show(ScriptedAgency(), lang, 0);
            AssertTextsFit(p, "witness agency " + lang);
            AssertTextsInsidePanel(p);
            var texts = p.GetComponentsInChildren<Text>(true);
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "AgencyFacts" && t.text == PhStrings.Format("w_agency_facts", lang, 3, 2)));
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "Value_q5"), "the q5 row is drawn");
            Assert.AreEqual(4, p.ClosingText.text.Split('\n').Length);
        }

        [TestCase("en")]
        [TestCase("hi")]
        public void Hud_AgencyCaptions_Fit(string lang)
        {
            var p = NewPanel<PhHudPanel>(); p.Build(null);
            foreach (AgencyStep step in System.Enum.GetValues(typeof(AgencyStep)))
            {
                var m = new HudModel { Lang = lang, Phase = PhPhase.Agency, AgencyNow = step, RemainingS = 25, SpectatorVisible = true, StrokeCount = 12 };
                p.Apply(m, 0.5f);
                AssertTextsFit(p, "hud agency " + step + " " + lang);
                Assert.AreEqual(PhStrings.AgencyWord(step, lang), p.PhaseText.text);
            }
        }

        [TestCase("en")]
        [TestCase("hi")]
        public void Hud_TextFits_WithChipsAndSpectator(string lang)
        {
            var p = NewPanel<PhHudPanel>(); p.Build(null);
            var m = new HudModel { Lang = lang, Phase = PhPhase.Induction, RemainingS = 125, Condition = PhCondition.Sync, SpectatorVisible = true, StrokeCount = 120, BioConnected = false };
            foreach (PhPhase ph in System.Enum.GetValues(typeof(PhPhase)))
            {
                m.Phase = ph; p.Apply(m, 0.5f);
                AssertTextsFit(p, "hud " + ph + " " + lang);
            }
        }

        [Test]
        public void Questionnaire_OrderBackAndAutoAdvance()
        {
            var p = NewPanel<PhQuestionnairePanel>(); p.Build(null, false);
            var q = new Questionnaire(new[] { Questionnaire.Q1, Questionnaire.Q2, Questionnaire.Q3, Questionnaire.Q4 });
            p.Show(q, v => q.Answer(v) != null, () => q.Back(), "en", 0);
            Assert.AreEqual("q1", p.ShownItemId);
            Assert.IsFalse(p.BackButton.gameObject.activeSelf, "no Back on the first item");
            Assert.IsFalse(p.PressBack());

            Assert.IsTrue(p.Press(-2));
            Assert.IsFalse(p.Press(3), "second press while an answer is pending is ignored");
            p.Tick(399); Assert.AreEqual("q1", p.ShownItemId, "not before 0.4 s");
            p.Tick(400); Assert.AreEqual("q2", p.ShownItemId);
            Assert.IsTrue(p.BackButton.gameObject.activeSelf);

            p.Press(3); p.Tick(900);
            Assert.AreEqual("q3", p.ShownItemId);

            Assert.IsTrue(p.PressBack());
            Assert.AreEqual("q2", p.ShownItemId);
            Assert.AreEqual(3, q.ValueOf("q2"), "the earlier answer is kept for editing");
            Assert.IsTrue(p.PressBack());
            Assert.AreEqual("q1", p.ShownItemId);
            Assert.AreEqual(-2, q.ValueOf("q1"));

            p.Press(1); p.Tick(1400);   // pending timer starts at the last Tick time
            p.Tick(1800);
            Assert.AreEqual("q2", p.ShownItemId);
        }

        [Test]
        public void Questionnaire_LastAnswerCompletes_AndItemsAreThePrdWording()
        {
            var p = NewPanel<PhQuestionnairePanel>(); p.Build(null, false);
            var q = new Questionnaire();   // q1-q3
            p.Show(q, v => q.Answer(v) != null, () => q.Back(), "en", 0);
            Assert.AreEqual("It felt as if the virtual hand was my hand.", p.ItemText.text);
            double t = 0;
            foreach (var expect in new[] { "q1", "q2", "q3" })
            {
                Assert.AreEqual(expect, p.ShownItemId);
                p.Tick(t);                 // let the panel see the current time before the press
                p.Press(0); t += 500; p.Tick(t);
            }
            Assert.IsTrue(q.IsComplete);
            Assert.AreEqual("Strongly disagree", Questionnaire.AnchorLow("en"));
            Assert.AreEqual("Strongly agree", Questionnaire.AnchorHigh("en"));
        }

        [Test]
        public void Witness_PanelShowsTheScriptedNumbers_AndClosingLineFades()
        {
            var p = NewPanel<PhWitnessPanel>(); p.Build(null, false);
            p.Show(UiModelTests.Scripted(true), "en", 0);
            var texts = p.GetComponentsInChildren<Text>(true);
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "Value_drift_change_cm" && t.text == "+3.6 cm"));
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "Value_flinch_latency_ms" && t.text == "180 ms"));
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "Pointer_q4" && t.text == "A pointer, not proof"));
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "Group_body" && t.text == "Body"));
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "Group_mind" && t.text == "Mind"));
            Assert.IsTrue(System.Array.Exists(texts, t => t.name == "Group_observer" && t.text == "The one who noticed"));
            Assert.IsTrue(System.Array.Exists(texts, t => t.text == "Preliminary"));
            // SYNC is blue and ASYNC amber (green against amber has no lightness difference)
            var bars = p.GetComponentsInChildren<Image>(true);
            Assert.AreEqual(PhUiKit.Info, System.Array.Find(bars, i => i.name == "CardBar" && i.transform.parent.name == "Card_Sync").color);
            Assert.AreEqual(PhUiKit.Warn, System.Array.Find(bars, i => i.name == "CardBar" && i.transform.parent.name == "Card_Async").color);
            Assert.AreEqual(0f, p.ClosingAlpha, 1e-4);
            p.Tick(2900); Assert.AreEqual(0f, p.ClosingAlpha, 1e-4);
            p.Tick(4500); Assert.AreEqual(1f, p.ClosingAlpha, 1e-4);
            Assert.AreEqual(WitnessSummary.ClosingEn, p.ClosingText.text);
        }

        [Test]
        public void Hud_ChipsAndSpectatorVisibility()
        {
            var p = NewPanel<PhHudPanel>(); p.Build(null);
            var m = new HudModel { Phase = PhPhase.Induction, RemainingS = 40, HapticConnected = true, BioConnected = false };
            p.Apply(m, 0f);
            Assert.IsFalse(p.SleeveChipShown); Assert.IsTrue(p.EmgChipShown); Assert.IsFalse(p.SpectatorShown);
            m.HapticConnected = false; m.SpectatorVisible = true; p.Apply(m, 0f);
            Assert.IsTrue(p.SleeveChipShown); Assert.IsTrue(p.SpectatorShown);
        }
    }
}
