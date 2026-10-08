using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// A5 agency phase, pure: the step schedule, the hysteresis, the self-closes with the "wait for a flat level" rule, a missing
    /// level source, and the wiring in the module (autonomous_close events, q5 only after the hand really closed by itself).
    /// </summary>
    public class AgencyTests
    {
        private const double Start = 5000, Thr = 0.3;

        private static AgencyRun NewRun(bool autonomous = true) { return new AgencyRun(Start, Thr, autonomous); }

        /// <summary>Ticks every 14 ms from fromS to toS (seconds into the phase), inclusive of the start.</summary>
        private static void Go(AgencyRun r, double fromS, double toS, Func<double, double?> level)
        {
            for (double t = fromS; t <= toS + 1e-9; t += 0.014) r.Tick(Start + t * 1000.0, level(t));
        }

        private static void At(AgencyRun r, double t, double? level) { r.Tick(Start + t * 1000.0, level); }

        // ---- steps ----------------------------------------------------------------------------------------------------

        [Test]
        public void Steps_Rest0to4_Squeeze4to8_Driven8to20_Watch20to30()
        {
            var r = NewRun();
            foreach (var c in new[] {
                new object[] { 0.0, AgencyStep.Rest }, new object[] { 3.999, AgencyStep.Rest }, new object[] { 4.0, AgencyStep.Squeeze },
                new object[] { 7.999, AgencyStep.Squeeze }, new object[] { 8.0, AgencyStep.Driven }, new object[] { 19.999, AgencyStep.Driven },
                new object[] { 20.0, AgencyStep.Watch }, new object[] { 29.999, AgencyStep.Watch } })
            {
                At(r, (double)c[0], 0.0);
                Assert.AreEqual((AgencyStep)c[1], r.Step, "t = " + c[0]);
                Assert.AreEqual((AgencyStep)c[1], AgencyRun.StepAt((double)c[0], true));
            }
            Assert.AreEqual(30.0, AgencyRun.TotalS);
        }

        [Test]
        public void WithoutTheAutonomousClose_DrivenRunsTo30s_AndNoWatchStep()
        {
            var r = NewRun(autonomous: false);
            foreach (double t in new[] { 8.0, 20.0, 25.0, 29.999 })
            {
                At(r, t, 0.0);
                Assert.AreEqual(AgencyStep.Driven, r.Step, "t = " + t);
            }
            Assert.AreEqual(AgencyStep.Rest, AgencyRun.StepAt(3.9, false));
            Assert.AreEqual(AgencyStep.Squeeze, AgencyRun.StepAt(4.0, false));
        }

        [Test]
        public void RestAndSqueeze_KeepTheHandOpen_EvenWhenTheLevelIsHigh()
        {
            var r = NewRun();
            Go(r, 0, 7.99, t => 1.0);
            Assert.AreEqual(0.0, r.Curl01, 1e-9);
            Assert.AreEqual(0, r.DrivenCloses, "squeezing for the calibration is not a close");
        }

        // ---- Driven: hysteresis, edges, easing ------------------------------------------------------------------------

        [Test]
        public void Hysteresis_ClosesAtTheThreshold_OpensAt60PercentOfIt_NoChatterBetween()
        {
            var r = NewRun();
            At(r, 8.0, 0.29); Assert.AreEqual(0, r.DrivenCloses, "below the threshold: stays open");
            At(r, 8.1, 0.31); Assert.AreEqual(1, r.DrivenCloses, "at or above: closes");
            foreach (double v in new[] { 0.29, 0.25, 0.21, 0.19 }) { At(r, 8.2 + v, v); Assert.AreEqual(1, r.DrivenCloses, "still closed at " + v); }
            Assert.AreEqual(0.18, r.OpenThreshold, 1e-12);
            At(r, 9.0, 0.17);                                      // at or below 0.18: opens
            At(r, 9.1, 0.29); Assert.AreEqual(1, r.DrivenCloses, "0.29 does not close again");
            At(r, 9.2, 0.30); Assert.AreEqual(2, r.DrivenCloses, "0.30 does");
        }

        [Test]
        public void NoisyLevelAroundTheThreshold_GivesOneClose_NotAFlutter()
        {
            var r = NewRun();
            var rng = new Random(3);
            Go(r, 8.0, 12.0, t => 0.30 + (rng.NextDouble() - 0.5) * 0.04);   // 0.28 .. 0.32 for 4 s
            Assert.AreEqual(1, r.DrivenCloses);
            Go(r, 12.0, 16.0, t => 0.20 + (rng.NextDouble() - 0.5) * 0.16);  // 0.12 .. 0.28: drops below 0.18 now and then, never reaches 0.3 afterwards
            Assert.AreEqual(1, r.DrivenCloses, "no new close without a rise to the threshold");
        }

        [Test]
        public void DrivenCloses_CountsRisingEdges_OnlyInDriven()
        {
            var r = NewRun();
            Go(r, 0, 7.99, t => 1.0);                                          // Rest + Squeeze: not counted
            double t0 = 8.0;
            for (int i = 0; i < 3; i++)
            {
                Go(r, t0, t0 + 0.6, t => 0.6); t0 += 0.62;                      // squeeze
                Go(r, t0, t0 + 0.6, t => 0.0); t0 += 0.62;                      // relax
            }
            Assert.AreEqual(3, r.DrivenCloses);
            Go(r, 20.0, 29.9, t => 1.0);                                        // Watch: the level is ignored
            Assert.AreEqual(3, r.DrivenCloses);
        }

        [Test]
        public void Curl_EasesToTheTarget_WithA120msTimeConstant()
        {
            var r = NewRun();
            At(r, 8.0, 0.6);                                                    // first Driven tick: target closed, nothing eased yet
            Go(r, 8.0, 8.12, t => 0.6);
            Assert.AreEqual(1 - Math.Exp(-1), r.Curl01, 0.03, "after one time constant (0.12 s)");
            Go(r, 8.12, 9.0, t => 0.6);
            Assert.Greater(r.Curl01, 0.99);
            double before = r.Curl01;
            Go(r, 9.0, 9.12, t => 0.0);
            Assert.AreEqual(before * Math.Exp(-1), r.Curl01, 0.03, "and back down the same way");
            Go(r, 9.12, 10.0, t => 0.0);
            Assert.Less(r.Curl01, 0.01);
            Assert.GreaterOrEqual(r.Curl01, 0.0); Assert.LessOrEqual(r.Curl01, 1.0);
        }

        // ---- Watch: the self-closes -----------------------------------------------------------------------------------

        private static List<double> CloseTimes(AgencyRun r)
        {
            var times = new List<double>();
            r.OnAutonomousClose += level => times.Add(r.ElapsedS);
            return times;
        }

        [Test]
        public void Watch_FlatLevel_FiresExactlyTwoCloses_At22_and25_5()
        {
            var r = NewRun(); var times = CloseTimes(r);
            Go(r, 0, 29.99, t => 0.0);
            Assert.AreEqual(2, r.AutonomousCloses); Assert.AreEqual(2, times.Count);
            Assert.AreEqual(22.0, times[0], 0.02); Assert.AreEqual(25.5, times[1], 0.02);
        }

        [Test]
        public void Watch_LevelIsIgnoredForControl_AndEachCloseWaitsAtMost1_5s_ForTheLevelToDrop()
        {
            var r = NewRun(); var levels = new List<double>(); var times = CloseTimes(r);
            r.OnAutonomousClose += l => levels.Add(l);
            double maxCurlOutsideCloses = 0;
            for (double t = 0; t <= 29.99; t += 0.014)
            {
                At(r, t, 1.0);                                                  // the participant squeezes hard the whole time
                bool inClose = times.Any(f => t >= f && t < f + 1.52);
                if (t > 21.0 && !inClose && r.Step == AgencyStep.Watch) maxCurlOutsideCloses = Math.Max(maxCurlOutsideCloses, r.Curl01);
            }
            Assert.AreEqual(2, r.AutonomousCloses, "exactly two, however hard the level pushes");
            Assert.AreEqual(23.5, times[0], 0.02, "22.0 + the 1.5 s wait");
            Assert.AreEqual(27.0, times[1], 0.02, "25.5 + the 1.5 s wait");
            CollectionAssert.AreEqual(new[] { 1.0, 1.0 }, levels, "the event carries the level at that moment");
            Assert.Less(maxCurlOutsideCloses, 0.01, "the hand does not follow the squeeze in Watch");
        }

        [Test]
        public void Watch_FiresAsSoonAsTheLevelDropsBelowTheOpenThreshold()
        {
            var r = NewRun(); var times = CloseTimes(r);
            Go(r, 0, 22.59, t => t < 22.6 ? 0.5 : 0.1);
            Assert.AreEqual(0, r.AutonomousCloses, "still squeezing at 22.59");
            Go(r, 22.6, 24.0, t => 0.1);
            Assert.AreEqual(1, r.AutonomousCloses);
            Assert.AreEqual(22.6, times[0], 0.02);
            Go(r, 24.0, 29.99, t => 0.1);
            Assert.AreEqual(2, r.AutonomousCloses);
            Assert.AreEqual(25.5, times[1], 0.02, "the second one is on its own schedule");
        }

        [Test]
        public void SelfClose_Curve_Closes025s_Holds09s_Opens035s()
        {
            Assert.AreEqual(0.0, AgencyRun.SelfCloseCurl(0), 1e-9);
            Assert.AreEqual(0.5, AgencyRun.SelfCloseCurl(0.125), 1e-9);
            Assert.AreEqual(1.0, AgencyRun.SelfCloseCurl(0.25), 1e-9);
            Assert.AreEqual(1.0, AgencyRun.SelfCloseCurl(0.7), 1e-9);
            Assert.AreEqual(1.0, AgencyRun.SelfCloseCurl(1.14), 1e-9);
            Assert.AreEqual(0.5, AgencyRun.SelfCloseCurl(1.15 + 0.175), 1e-9);
            Assert.AreEqual(0.0, AgencyRun.SelfCloseCurl(1.5), 1e-9);
            Assert.AreEqual(0.0, AgencyRun.SelfCloseCurl(3.0), 1e-9);

            var r = NewRun(); var times = CloseTimes(r);
            double maxIn = 0, atEnd = 1;
            for (double t = 0; t <= 29.99; t += 0.014)
            {
                At(r, t, 0.0);
                if (times.Count > 0 && t >= times[0] + 0.3 && t <= times[0] + 1.1) maxIn = Math.Max(maxIn, r.Curl01);
                if (times.Count > 0 && Math.Abs(t - (times[0] + 1.6)) < 0.007) atEnd = r.Curl01;
            }
            Assert.AreEqual(1.0, maxIn, 1e-9, "a full fist while the self-close holds");
            Assert.AreEqual(0.0, atEnd, 1e-9, "open again 1.5 s after it started");
        }

        [Test]
        public void NothingFires_WhenTheAutonomousCloseIsDisabled()
        {
            var r = NewRun(autonomous: false); var times = CloseTimes(r);
            Go(r, 0, 29.99, t => 0.0);
            Assert.AreEqual(0, r.AutonomousCloses); Assert.AreEqual(0, times.Count);
            Assert.AreEqual(AgencyStep.Driven, r.Step);
            // the muscle still moves the hand after 20 s: Driven control goes on
            Go(r, 25.0, 25.5, t => 0.7);
            Assert.Greater(r.Curl01, 0.9);
            Assert.AreEqual(1, r.DrivenCloses);
        }

        [Test]
        public void NullLevel_IsSafe_HandStaysOpen_AndWatchStillRuns()
        {
            var r = NewRun(); var levels = new List<double>(); var times = CloseTimes(r);
            r.OnAutonomousClose += l => levels.Add(l);
            double maxCurlDriven = 0;
            for (double t = 0; t <= 29.99; t += 0.014)
            {
                At(r, t, null);
                if (r.Step == AgencyStep.Driven) maxCurlDriven = Math.Max(maxCurlDriven, r.Curl01);
            }
            Assert.AreEqual(0.0, maxCurlDriven, 1e-12, "no source, no movement");
            Assert.AreEqual(0, r.DrivenCloses);
            Assert.AreEqual(2, r.AutonomousCloses);
            Assert.AreEqual(22.0, times[0], 0.02); Assert.AreEqual(25.5, times[1], 0.02);
            CollectionAssert.AreEqual(new[] { 0.0, 0.0 }, levels, "emg_level is 0 when there is no level");
        }

        [Test]
        public void SourceThatDisappearsInDriven_OpensTheHand()
        {
            var r = NewRun();
            Go(r, 8.0, 9.0, t => 0.8);
            Assert.Greater(r.Curl01, 0.99);
            Go(r, 9.0, 10.0, t => (double?)null);
            Assert.Less(r.Curl01, 0.01);
            Assert.AreEqual(1, r.DrivenCloses);
        }

        // ---- the hand-tracking fallback level -------------------------------------------------------------------------

        [Test]
        public void Flexion_OpenIsTheRestMean_FistIsTheSqueezeMinimum()
        {
            var f = new FlexionCalibration();
            foreach (double d in new[] { 0.098, 0.100, 0.102 }) f.AddRest(d);
            foreach (double d in new[] { 0.09, 0.05, 0.041, 0.06 }) f.AddSqueeze(d);
            Assert.IsTrue(f.Calibrate()); Assert.IsTrue(f.Ready);
            Assert.AreEqual(0.100, f.RestMeanM, 1e-9); Assert.AreEqual(0.041, f.SqueezeMinM, 1e-9);
            Assert.AreEqual(0.0, f.Level(0.100), 1e-9);
            Assert.AreEqual(1.0, f.Level(0.041), 1e-9);
            Assert.AreEqual(0.5, f.Level(0.0705), 1e-9);
            Assert.AreEqual(0.0, f.Level(0.12), 1e-9, "more open than at rest: clamped");
            Assert.AreEqual(1.0, f.Level(0.02), 1e-9, "tighter than the best squeeze: clamped");
        }

        [Test]
        public void Flexion_NotReady_WithoutSamples_OrWithATinyGap()
        {
            var none = new FlexionCalibration();
            Assert.IsFalse(none.Calibrate()); Assert.IsFalse(none.Ready);
            var noSqueeze = new FlexionCalibration(); noSqueeze.AddRest(0.1);
            Assert.IsFalse(noSqueeze.Calibrate());
            var noRest = new FlexionCalibration(); noRest.AddSqueeze(0.05);
            Assert.IsFalse(noRest.Calibrate());
            var tiny = new FlexionCalibration(); tiny.AddRest(0.100); tiny.AddSqueeze(0.095);
            Assert.IsFalse(tiny.Calibrate(), "a fist that moves the fingertip 5 mm is not a signal");
        }

        // ---- the module -----------------------------------------------------------------------------------------------

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

            public Mod(string json)
            {
                M.OnTrialEvent += Ev.Add;
                M.Configure(Ph.P(json), S);
            }

            public double Now { get { return S.Clock.NowMs; } }
            public void Skip(PhPhase to) { int g = 0; while (M.CurrentPhase != to && g++ < 40) Assert.IsTrue(M.AdvancePhase(), "stuck in " + M.CurrentPhase); Assert.AreEqual(to, M.CurrentPhase); }

            /// <summary>Plays the whole 30 s agency phase the way the presenter does: every frame the run is ticked with the level.</summary>
            public void PlayAgency(Func<double, double?> level)
            {
                var run = M.Agency;
                for (double t = 0; t < 30; t += 0.02)
                {
                    S.Clock.Advance(20); M.Tick(Now);
                    if (M.CurrentPhase != PhPhase.Agency) break;
                    run.Tick(Now, level(t));
                }
            }

            public List<TrialEvent> Of(string type) { return Ev.Where(e => e.Type == type).ToList(); }
            public static JObject Data(TrialEvent e) { return JObject.FromObject(e.Data); }

            public int QuestionnaireLength() { Skip(PhPhase.Questionnaire); return M.CurrentQuestionnaire.Items.Count; }
        }

        private const string A5 = "{\"agency_enabled\":true,\"autonomous_close_enabled\":true,\"emg_threshold\":0.3}";

        [Test]
        public void Module_CreatesTheRunWhenTheAgencyPhaseStarts_InTheLastConditionOnly()
        {
            var m = new Mod(A5);
            m.M.Begin();
            Assert.IsNull(m.M.Agency);
            m.Skip(PhPhase.Questionnaire);                                   // first condition: no agency phase
            Assert.IsNull(m.M.Agency);
            m.Skip(PhPhase.Agency);
            Assert.IsNotNull(m.M.Agency); Assert.AreEqual(m.M.Machine.PhaseStartMs, m.M.Agency.StartMs);
            Assert.AreEqual(1, m.M.CurrentConditionIndex);
            Assert.AreEqual(Params().EmgThreshold, m.M.Agency.OpenThreshold / AgencyRun.OpenFraction, 1e-12);
            Assert.IsTrue(m.M.Agency.AutonomousEnabled);
        }

        private static PhantomHandParams Params() { return Ph.Params(A5); }

        [Test]
        public void Module_TurnsEachSelfCloseIntoAnAutonomousCloseEvent_WithTheLevelOrZero()
        {
            var m = new Mod(A5);
            m.M.Begin(); m.Skip(PhPhase.Agency);
            m.PlayAgency(t => t < 21 ? 0.9 : (t > 23 && t < 28 ? 0.6 : 0.0));   // squeezing until 21 s (flat at 22.0), and again from 23 s on (still squeezing when the 1.5 s wait ends at 27.0)
            var ev = m.Of("autonomous_close");
            Assert.AreEqual(2, ev.Count);
            Assert.AreEqual(2, m.M.Agency.AutonomousCloses);
            foreach (var e in ev) { Assert.AreEqual(1, e.Trial, "trial = the condition index"); Assert.IsTrue(Mod.Data(e)["emg_level"].Value<double>() >= 0.0); }
            Assert.AreEqual(0.0, Mod.Data(ev[0])["emg_level"].Value<double>(), 1e-9, "first: the level was flat at 22.0 s");
            Assert.AreEqual(0.6, Mod.Data(ev[1])["emg_level"].Value<double>(), 1e-9, "second: still squeezing at 25.5 s, fired after the 1.5 s wait");
            Assert.AreEqual(PhPhase.Threat, m.M.CurrentPhase);
        }

        [Test]
        public void Module_NoLevelSource_StillFiresTwice_WithEmgLevelZero()
        {
            var m = new Mod(A5);
            m.M.Begin(); m.Skip(PhPhase.Agency);
            m.PlayAgency(t => (double?)null);
            var ev = m.Of("autonomous_close");
            Assert.AreEqual(2, ev.Count);
            foreach (var e in ev) Assert.AreEqual(0.0, Mod.Data(e)["emg_level"].Value<double>());
        }

        [Test]
        public void Module_NoAutonomousClose_NoEvent_NoQ5_NoWitnessAddition()
        {
            var m = new Mod("{\"agency_enabled\":true}");
            m.M.Begin(); m.Skip(PhPhase.Agency);
            Assert.IsFalse(m.M.Agency.AutonomousEnabled);
            m.PlayAgency(t => 0.8);
            Assert.AreEqual(0, m.Of("autonomous_close").Count);
            Assert.AreEqual(3, m.QuestionnaireLength(), "q1-q3 only");
            for (int i = 0; i < 3; i++) m.M.SubmitQuestionnaireAnswer(1);
            m.Skip(PhPhase.Witness);
            Assert.IsFalse(m.M.Witness.AgencyRan);
            Assert.IsNull(m.M.Witness.Row("q5"));
            Assert.AreEqual(WitnessSummary.ClosingEn, m.M.Witness.ClosingLine("en"));
        }

        [Test]
        public void Q5_OnlyInTheLastConditionsQuestionnaire_AndOnlyAfterTheHandClosedByItself()
        {
            var m = new Mod(A5);
            m.M.Begin();
            Assert.AreEqual(3, m.QuestionnaireLength(), "first condition: q1-q3");
            for (int i = 0; i < 3; i++) m.M.SubmitQuestionnaireAnswer(0);
            m.Skip(PhPhase.Agency);
            m.PlayAgency(t => 0.0);
            Assert.AreEqual(4, m.QuestionnaireLength(), "last condition after a self-close: q5 appended");
            Assert.AreEqual("q5", m.M.CurrentQuestionnaire.Items.Last().Id);
            Assert.AreEqual(QRole.Agency, m.M.CurrentQuestionnaire.Items.Last().Role);

            // the agency phase was skipped by the operator before the hand ever closed: nothing to ask about
            var skipped = new Mod(A5);
            skipped.M.Begin(); skipped.Skip(PhPhase.Agency); skipped.Skip(PhPhase.Questionnaire);
            Assert.AreEqual(0, skipped.M.Agency.AutonomousCloses);
            Assert.AreEqual(3, skipped.M.CurrentQuestionnaire.Items.Count);

            // agency off: no phase, no q5, whatever the other switch says
            var off = new Mod("{\"autonomous_close_enabled\":true}");
            off.M.Begin();
            Assert.AreEqual(3, off.QuestionnaireLength());
            for (int i = 0; i < 3; i++) off.M.SubmitQuestionnaireAnswer(0);
            Assert.AreEqual(3, off.QuestionnaireLength());
        }

        [Test]
        public void Q5_AnswerFlowsIntoTheWitness_RowFactsLineAndClosingLine()
        {
            var m = new Mod(A5);
            m.M.Begin();
            m.Skip(PhPhase.Questionnaire);
            for (int i = 0; i < 3; i++) m.M.SubmitQuestionnaireAnswer(-1);           // async_first: this is the ASYNC condition
            m.Skip(PhPhase.Agency);
            var run = m.M.Agency;
            // two squeezes in Driven, one more in Watch before the first self-close
            m.PlayAgency(t => (t >= 9 && t < 9.5) || (t >= 12 && t < 12.5) ? 0.8 : 0.0);
            Assert.AreEqual(2, run.DrivenCloses); Assert.AreEqual(2, run.AutonomousCloses);
            m.Skip(PhPhase.Questionnaire);
            Assert.AreEqual(4, m.M.CurrentQuestionnaire.Items.Count);
            m.M.SubmitQuestionnaireAnswer(1); m.M.SubmitQuestionnaireAnswer(2); m.M.SubmitQuestionnaireAnswer(3); m.M.SubmitQuestionnaireAnswer(2);   // q5 = +2
            Assert.AreEqual(2.0, m.M.Results[1].Agency.Value, 1e-9);
            Assert.IsFalse(m.M.Results[0].Agency.HasValue);

            m.Skip(PhPhase.Witness);
            var w = m.M.Witness;
            Assert.IsTrue(w.AgencyRan); Assert.AreEqual(2, w.DrivenCloses); Assert.AreEqual(2, w.AutonomousCloses);
            var row = w.Row("q5");
            Assert.IsNotNull(row);
            Assert.AreEqual("mind", row.Group);
            Assert.AreEqual("I caused that movement", row.LabelEn); Assert.AreEqual("वह गति मैंने कराई", row.LabelHi);
            Assert.AreEqual(2.0, (double)row.SyncValue, 1e-9, "async_first: the last condition is SYNC");
            Assert.IsNull(row.AsyncValue);
            CollectionAssert.AreEqual(new[] { "drift_change_cm", "flinch_latency_ms", "flinch_strength", "ownership", "q5" }, w.Rows.Select(r => r.Key).ToArray(), "q5 sits with the mind rows");

            var en = WitnessView.Build(w, "en"); var hi = WitnessView.Build(w, "hi");
            Assert.AreEqual("You closed it 2 times. It closed by itself 2 times.", en.AgencyLine);
            Assert.AreEqual("आपने इसे 2 बार बंद किया। यह अपने आप 2 बार बंद हुआ।", hi.AgencyLine);
            Assert.AreEqual(4, en.ClosingLine.Split('\n').Length);
            StringAssert.Contains("Even \"I did that\" changed.", en.ClosingLine);
            Assert.AreEqual("mind", en.Sync.Entry("q5").Group);
            Assert.IsFalse(en.Async.Entry("q5").HasData);

            var d = JObject.FromObject(m.Of("witness_summary").Single().Data);
            Assert.AreEqual(6.0, d["sync"]["agency_q5"].Value<double>(), 1e-9, "contract scale: +2 shown is 6");
            Assert.IsNull(d["async"]["agency_q5"]);
            Assert.AreEqual(2, d["agency"]["driven_closes"].Value<int>()); Assert.AreEqual(2, d["agency"]["autonomous_closes"].Value<int>());
            StringAssert.Contains("Even \"I did that\" changed.", d["closing_en"].Value<string>());
        }

        [Test]
        public void WitnessWithoutAgency_HasNoFactsLine_NoQ5Row_NoAgencyKey()
        {
            var w = UiModelTests.Scripted(true);
            var v = WitnessView.Build(w, "en");
            Assert.IsNull(v.AgencyLine);
            Assert.IsNull(w.Row("q5"));
            Assert.IsNull(JObject.FromObject(w.ToEventData())["agency"]);
        }

        // ---- the captions ---------------------------------------------------------------------------------------------

        [Test]
        public void Captions_EnglishAndHindi_ForEveryStep()
        {
            var expect = new[]
            {
                new object[] { AgencyStep.Rest, "Relax your hand", "हाथ ढीला छोड़ें" },
                new object[] { AgencyStep.Squeeze, "Squeeze hard, once", "एक बार ज़ोर से मुट्ठी कसें" },
                new object[] { AgencyStep.Driven, "Squeeze to close the hand", "मुट्ठी कसें, हाथ बंद होगा" },
                new object[] { AgencyStep.Watch, "Relax. Just watch", "ढीला छोड़ें। बस देखें" },
            };
            foreach (var e in expect)
            {
                Assert.AreEqual((string)e[1], PhStrings.AgencyWord((AgencyStep)e[0], "en"));
                Assert.AreEqual((string)e[2], PhStrings.AgencyWord((AgencyStep)e[0], "hi"));
                var hud = new HudModel { Phase = PhPhase.Agency, AgencyNow = (AgencyStep)e[0] };
                Assert.AreEqual((string)e[1], hud.PhaseText);
                hud.Lang = "hi"; Assert.AreEqual((string)e[2], hud.PhaseText);
            }
            Assert.AreEqual("Squeeze", new HudModel { Phase = PhPhase.Agency }.PhaseText, "without a step the generic word");
            Assert.AreEqual("The touch is still here", new HudModel { Phase = PhPhase.Dissolve, AgencyNow = AgencyStep.Watch }.PhaseText, "the step only matters in the agency phase");
        }
    }
}
