using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Games.PhantomHand.Tests
{
    public class QuestionnaireTests
    {
        [Test]
        public void ItemsAreAskedInOrder_AndCompleteAfterThree()
        {
            var q = new Questionnaire();
            Assert.AreEqual("q1", q.Current.Id);
            Assert.AreEqual("q1", q.Answer(2));
            Assert.AreEqual("q2", q.Current.Id);
            q.Answer(-1);
            Assert.IsFalse(q.IsComplete);
            Assert.AreEqual("q3", q.Current.Id);
            q.Answer(0);
            Assert.IsTrue(q.IsComplete);
        }

        [Test]
        public void Back_ReturnsToPreviousItem_AndAnswerIsOverwritten()
        {
            var q = new Questionnaire();
            q.Answer(1); q.Answer(1);
            Assert.IsTrue(q.Back());
            Assert.AreEqual("q2", q.Current.Id);
            q.Answer(3);
            Assert.AreEqual(3, q.ValueOf("q2"));
            Assert.IsFalse(new Questionnaire().Back());
        }

        [Test]
        public void Back_AfterCompletion_ReopensLastItem()
        {
            var q = new Questionnaire();
            q.Answer(1); q.Answer(1); q.Answer(1);
            Assert.IsTrue(q.IsComplete);
            Assert.IsTrue(q.Back());
            Assert.IsFalse(q.IsComplete);
            Assert.AreEqual("q3", q.Current.Id);
        }

        [Test]
        public void Ownership_IsMeanOfQ1Q2_ControlIsQ3()
        {
            var q = new Questionnaire();
            q.Answer(3); q.Answer(0); q.Answer(-2);
            Assert.AreEqual(1.5, q.Ownership.Value, 1e-9);
            Assert.AreEqual(-2.0, q.Control.Value, 1e-9);
            Assert.IsNull(q.Awareness);
        }

        [Test]
        public void OutOfRangeAnswer_IsRejected()
        {
            var q = new Questionnaire();
            Assert.IsNull(q.Answer(4)); Assert.IsNull(q.Answer(-4));
            Assert.AreEqual("q1", q.Current.Id);
        }

        [Test]
        public void ItemList_CanBeExtendedWithQ4()
        {
            var items = Questionnaire.DefaultItems(); items.Add(Questionnaire.Q4);
            var q = new Questionnaire(items);
            q.Answer(1); q.Answer(1); q.Answer(1);
            Assert.IsFalse(q.IsComplete);
            q.Answer(2);
            Assert.IsTrue(q.IsComplete);
            Assert.AreEqual(2.0, q.Awareness.Value);
        }

        [Test]
        public void EnglishAndHindiTextsExist_AndAnchors()
        {
            foreach (var it in new[] { Questionnaire.Q1, Questionnaire.Q2, Questionnaire.Q3, Questionnaire.Q4, Questionnaire.Q5 })
            { Assert.IsNotEmpty(it.Text("en")); Assert.IsNotEmpty(it.Text("hi")); Assert.AreNotEqual(it.Text("en"), it.Text("hi")); }
            Assert.AreEqual("Strongly disagree", Questionnaire.AnchorLow("en"));
            Assert.AreEqual("Strongly agree", Questionnaire.AnchorHigh("en"));
            Assert.AreEqual("It felt as if the virtual hand was my hand.", Questionnaire.Q1.En);
        }
    }

    public class WitnessSummaryTests
    {
        private static ConditionResult Cond(PhCondition c, double pre, double post, double? emgPeak, double? emgLat, double own)
        {
            return new ConditionResult
            {
                Condition = c, PreDriftCm = pre, PostDriftCm = post, Ownership = own, Control = -1,
                Threat = new ThreatResponse { EmgPeakX = emgPeak, EmgLatencyMs = emgLat, WristPeakMps = 0.8, WristLatencyMs = 200 },
            };
        }

        [Test]
        public void Rows_ShowEachConditionsNumbers_AndTheDifference()
        {
            var w = WitnessSummary.Build(Cond(PhCondition.Sync, 10, 14, 4.0, 130, 2.0), Cond(PhCondition.Async, 10, 11, 1.2, null, -1.0));
            Assert.AreEqual(4.0, (double)w.Row("drift_change_cm").SyncValue, 1e-9);
            Assert.AreEqual(1.0, (double)w.Row("drift_change_cm").AsyncValue, 1e-9);
            Assert.AreEqual(3.0, w.Row("drift_change_cm").Difference.Value, 1e-9);
            Assert.AreEqual(3.0, w.Row("ownership").Difference.Value, 1e-9);
            Assert.AreEqual("strong", w.Row("flinch_strength").SyncValue);
            Assert.AreEqual("none", w.Row("flinch_strength").AsyncValue);
            Assert.IsNull(w.Row("q4"));
        }

        [Test]
        public void FlinchLatency_FallsBackToWristWhenNoEmg()
        {
            var c = new ConditionResult { Threat = new ThreatResponse { WristLatencyMs = 210, WristPeakMps = 0.9 } };
            Assert.AreEqual(210, c.FlinchLatencyMs.Value);
            Assert.AreEqual("strong", c.FlinchStrength);
        }

        [Test]
        public void Lines_EndWithTheClosingLine_InBothLanguages()
        {
            var w = WitnessSummary.Build(Cond(PhCondition.Sync, 10, 14, 4.0, 130, 2.0), Cond(PhCondition.Async, 10, 11, 1.2, null, -1.0));
            var en = w.Lines("en"); var hi = w.Lines("hi");
            StringAssert.Contains("4.0 cm", en[0]); StringAssert.Contains("130 ms", en[0]); StringAssert.Contains("+2.0", en[0]);
            Assert.AreEqual(WitnessSummary.ClosingEn, en.Last());
            Assert.AreEqual(WitnessSummary.ClosingHi, hi.Last());
            StringAssert.Contains("You noticed every change.", en.Last());
            StringAssert.Contains("4.0 cm", hi[0]);
        }

        [Test]
        public void Q4Row_AppearsOnlyWhenAnswered_AndIsAPointer()
        {
            var s = Cond(PhCondition.Sync, 0, 1, 2, 100, 1); s.Awareness = 2;
            var a = Cond(PhCondition.Async, 0, 1, 2, 100, 1); a.Awareness = 2;
            var row = WitnessSummary.Build(s, a).Row("q4");
            Assert.IsNotNull(row); Assert.IsTrue(row.Pointer); Assert.AreEqual(0.0, row.Difference.Value);
        }

        [Test]
        public void EventData_HasPerConditionAndContrast()
        {
            var w = WitnessSummary.Build(Cond(PhCondition.Sync, 10, 14, 4.0, 130, 2.0), Cond(PhCondition.Async, 10, 11, 1.2, null, -1.0));
            var j = JObject.FromObject(w.ToEventData());
            Assert.AreEqual(4.0, j["sync"]["drift_change_cm"].Value<double>(), 1e-9);
            Assert.AreEqual(3.0, j["sync_minus_async"]["drift_change_cm"].Value<double>(), 1e-9);
            Assert.AreEqual(200.0, j["async"]["flinch_latency_ms"].Value<double>(), 1e-9);   // wrist fallback when EMG shows no response
        }
    }

    public class ModuleTests
    {
        private sealed class FakeSession : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex { get { return 0; } }
        }

        private sealed class Harness
        {
            public readonly FakeSession S = new FakeSession();
            public readonly PhantomHandModule M = new PhantomHandModule();
            public readonly List<TrialEvent> Ev = new List<TrialEvent>();

            public Harness(string json = "{}", bool additions = false)
            {
                M.AdditionsEnabled = additions;
                M.OnTrialEvent += Ev.Add;
                M.Configure(Ph.P(json), S);
            }

            public double Now { get { return S.Clock.NowMs; } }
            public void Adv(double ms) { S.Clock.Advance(ms); M.Tick(Now); }

            public void Calibrate(bool ok = true) { M.SubmitCalibration(new[] { 0.0, 0.75, 0.3 }, new[] { 0.0, 0, 1.0 }, ok); }

            public ProbeState Probe(double perceivedX, double actualX = 0)
            {
                var st = ProbeState.Waiting;
                for (int i = 0; i < 200 && st != ProbeState.Confirmed; i++)
                {
                    Adv(13.9);
                    st = M.FeedProbe(Now, true, new[] { perceivedX, 0.8, 0.3 }, new[] { 0.0, 0.75, 0.3 }, actualX);
                }
                return st;
            }

            public void Condition(double preX, double postX, double? emgPeak, double? emgLat, int[] answers)
            {
                Probe(preX);
                var s = M.CurrentStrokes[0];
                M.SubmitStroke(new StrokeRecord { Index = 0, PassAMs = s.PassAMs, PassBMs = s.PassBMs, CueASendMs = s.CueMotor0Ms - 40, CueBSendMs = s.CueMotor1Ms - 40, Swapped = s.Swapped });
                Adv(90000);
                M.SubmitThreatImpact(Now + 800, new[] { 0.0, 0.8, 0.3 }, true);
                M.SubmitThreatResponse(new ThreatResponse { EmgPeakX = emgPeak, EmgLatencyMs = emgLat, WristPeakMps = 0.7, WristLatencyMs = 200, Quality = new Dictionary<string, string> { { "wrist", "ok" }, { "imu", "missing" }, { "emg", "ok" } } });
                Adv(5000);
                Probe(postX);
                foreach (var a in answers) M.SubmitQuestionnaireAnswer(a);
            }

            public string Fmt(TrialEvent e)
            {
                var d = e.Data == null ? null : JObject.FromObject(e.Data);
                string tr = e.Trial.HasValue ? e.Trial.Value.ToString() : "-";
                switch (e.Type)
                {
                    case "phase_start": return "phase_start|" + tr + "|" + d["phase"] + "|" + (d["condition"].Type == JTokenType.Null ? "-" : d["condition"].ToString());
                    case "drift_probe": return "drift_probe|" + tr + "|" + d["when"] + "|" + (d["confirmed"].Value<bool>() ? "confirmed" : "timeout");
                    case "questionnaire_item": return "questionnaire_item|" + tr + "|" + d["item"] + "=" + d["value"];
                    default: return e.Type + "|" + tr;
                }
            }
        }

        [Test]
        public void GoldenEventList_ForAScriptedRun()
        {
            var h = new Harness();
            h.M.Begin();
            h.Adv(1000); h.Calibrate();
            h.Condition(-0.16, -0.19, 1.2, null, new[] { 1, 0, -2 });         // async (default order)
            h.Condition(-0.16, -0.21, 3.8, 120, new[] { 3, 2, -3 });          // sync
            Assert.AreEqual(PhPhase.Witness, h.M.CurrentPhase);
            h.Adv(30000);

            var expect = new List<string> { "block_start|-", "phase_start|-|calibrate|-", "calibration|-" };
            string[] conds = { "async", "sync" };
            string[][] answers = { new[] { "q1=1", "q2=0", "q3=-2" }, new[] { "q1=3", "q2=2", "q3=-3" } };
            for (int c = 0; c < 2; c++)
            {
                expect.Add("phase_start|" + c + "|probe_pre|" + conds[c]);
                expect.Add("drift_probe|" + c + "|pre|confirmed");
                expect.Add("phase_start|" + c + "|induction|" + conds[c]);
                expect.Add("stroke|" + c);
                expect.Add("phase_start|" + c + "|threat|" + conds[c]);
                expect.Add("threat_impact|" + c);
                expect.Add("threat_response|" + c);
                expect.Add("phase_start|" + c + "|probe_post|" + conds[c]);
                expect.Add("drift_probe|" + c + "|post|confirmed");
                expect.Add("phase_start|" + c + "|questionnaire|" + conds[c]);
                // the event carries the contract's 1..7 value (the -3..+3 answer + 4), not the on-screen one
                foreach (var a in answers[c]) expect.Add("questionnaire_item|" + c + "|" + a.Split('=')[0] + "=" + Questionnaire.ContractValue(int.Parse(a.Split('=')[1])));
            }
            expect.AddRange(new[] { "phase_start|-|witness|-", "witness_summary|-", "block_end|-" });
            CollectionAssert.AreEqual(expect, h.Ev.Select(h.Fmt).ToList());
        }

        [Test]
        public void ScriptedRun_WitnessNumbers_AreCorrect_AndSyncBeatsAsync()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate();
            h.Condition(-0.16, -0.19, 1.2, null, new[] { 1, 0, -2 });
            h.Condition(-0.16, -0.21, 3.8, 120, new[] { 3, 2, -3 });
            var w = h.M.Witness;
            Assert.IsNotNull(w);
            Assert.AreEqual(3.0, (double)w.Row("drift_change_cm").AsyncValue, 0.01);
            Assert.AreEqual(5.0, (double)w.Row("drift_change_cm").SyncValue, 0.01);
            Assert.Greater(w.Row("drift_change_cm").Difference.Value, 0);
            Assert.AreEqual(2.5, (double)w.Row("ownership").SyncValue, 1e-9);
            Assert.AreEqual(0.5, (double)w.Row("ownership").AsyncValue, 1e-9);
            Assert.AreEqual("strong", w.Row("flinch_strength").SyncValue);
            var ev = h.Ev.Last(e => e.Type == "witness_summary");
            Assert.AreEqual(5.0, JObject.FromObject(ev.Data)["sync"]["drift_change_cm"].Value<double>(), 0.01);
        }

        [Test]
        public void StrokeEvent_SyncTimingErrorComputed_AsyncNull_AndFieldsPresent()
        {
            var h = new Harness("{\"condition_order\":\"sync_first\"}");
            h.M.Begin(); h.Calibrate(); h.Probe(-0.16);
            var s = h.M.CurrentStrokes[2];
            h.M.SubmitStroke(new StrokeRecord { Index = 2, PassAMs = s.PassAMs + 4, PassBMs = s.PassBMs + 6, CueASendMs = s.CueMotor0Ms - 40, CueBSendMs = s.CueMotor1Ms - 40 });
            var j = JObject.FromObject(h.Ev.Last().Data);
            foreach (var k in new[] { "index", "brush_pass_a_ms", "brush_pass_b_ms", "cue_a_send_ms", "cue_b_send_ms", "swapped", "timing_err_ms" })
                Assert.IsNotNull(j[k], k);
            Assert.AreEqual(-5.0, j["timing_err_ms"].Value<double>(), 1e-6);   // mean(-4, -6)
            Assert.AreEqual(2, j["index"].Value<int>());
        }

        [Test]
        public void StrokePlan_IsAbsolute_AndStartsAtInduction()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate(); h.Probe(-0.16);
            Assert.AreEqual(PhPhase.Induction, h.M.CurrentPhase);
            Assert.AreEqual(h.M.InductionStartMs + 500, h.M.CurrentStrokes[0].StartMs, 1e-6);
            Assert.That(h.M.CurrentStrokes.Count, Is.InRange(35, 39));   // D16 slow brush: 37 strokes in 90 s
            Assert.Less(h.M.CurrentStrokes.Last().EndMs, h.M.InductionStartMs + 90000);
        }

        [Test]
        public void SubmissionsInTheWrongPhase_AreIgnored()
        {
            var h = new Harness();
            h.M.Begin();
            Assert.IsFalse(h.M.SubmitThreatImpact(1, null, true));
            Assert.IsFalse(h.M.SubmitStroke(new StrokeRecord()));
            Assert.IsFalse(h.M.SubmitQuestionnaireAnswer(1));
            Assert.IsFalse(h.M.SubmitProbe(new ProbeResult { When = "pre", Confirmed = true }));
            Assert.AreEqual(2, h.Ev.Count);   // block_start + phase_start(calibrate)
        }

        [Test]
        public void Probe_WrongWhenIsIgnored()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate();
            Assert.IsFalse(h.M.SubmitProbe(new ProbeResult { When = "post", Confirmed = true }));
        }

        [Test]
        public void CalibrationFailure_StaysInCalibrate_ThenTimeoutRecordsOkFalse()
        {
            var h = new Harness();
            h.M.Begin();
            h.Calibrate(ok: false);
            Assert.AreEqual(PhPhase.Calibrate, h.M.CurrentPhase);
            h.Adv(60000);
            Assert.AreEqual(PhPhase.ProbePre, h.M.CurrentPhase);
            var cals = h.Ev.Where(e => e.Type == "calibration").Select(e => JObject.FromObject(e.Data)["ok"].Value<bool>()).ToList();
            CollectionAssert.AreEqual(new[] { false, false }, cals);
        }

        [Test]
        public void ProbeTimeout_EmitsUnconfirmedProbeOnce()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate();
            h.Adv(60000);
            var probes = h.Ev.Where(e => e.Type == "drift_probe").ToList();
            Assert.AreEqual(1, probes.Count);
            var d = JObject.FromObject(probes[0].Data);
            Assert.IsFalse(d["confirmed"].Value<bool>());
            Assert.AreEqual(JTokenType.Null, d["drift_cm"].Type);
            Assert.AreEqual(PhPhase.Induction, h.M.CurrentPhase);
        }

        [Test]
        public void ConfirmedProbe_IsNotRecordedTwice()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate(); h.Probe(-0.16);
            Assert.AreEqual(1, h.Ev.Count(e => e.Type == "drift_probe"));
        }

        [Test]
        public void SetConditionOrder_LockedAfterInduction()
        {
            var h = new Harness();
            h.M.Begin();
            Assert.IsTrue(h.M.SetConditionOrder("sync_first"));
            h.Calibrate();
            Assert.AreEqual(PhCondition.Sync, h.M.CurrentCondition);
            h.Probe(-0.16);
            Assert.IsFalse(h.M.SetConditionOrder("async_first"));
            Assert.IsFalse(h.M.SetConditionOrder("nonsense"));
        }

        [Test]
        public void PauseAndResume_EmitEvents_AndFreezeTime()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate(); h.Probe(-0.16);
            double rem = h.M.RemainingS(h.Now);
            h.M.Pause();
            h.S.Clock.Advance(20000); h.M.Tick(h.Now);
            Assert.AreEqual(PhPhase.Induction, h.M.CurrentPhase);
            h.M.Resume();
            Assert.AreEqual(rem, h.M.RemainingS(h.Now), 1e-6);
            Assert.AreEqual(1, h.Ev.Count(e => e.Type == "pause")); Assert.AreEqual(1, h.Ev.Count(e => e.Type == "resume"));
        }

        [Test]
        public void End_BeforeBegin_IsSafe_AndAfterBeginEmitsAbortedBlockEnd()
        {
            var cold = new Harness();
            Assert.DoesNotThrow(() => cold.M.End());
            Assert.AreEqual(0, cold.Ev.Count);

            var h = new Harness();
            h.M.Begin(); h.Calibrate();
            h.M.End();
            Assert.AreEqual(PhPhase.Done, h.M.CurrentPhase);
            var last = h.Ev.Last();
            Assert.AreEqual("block_end", last.Type);
            Assert.IsTrue(JObject.FromObject(last.Data)["aborted"].Value<bool>());
            Assert.IsFalse(h.M.IsRunning);
        }

        [Test]
        public void AdvancePhase_SkipsInduction()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate(); h.Probe(-0.16);
            Assert.IsTrue(h.M.AdvancePhase());
            Assert.AreEqual(PhPhase.Threat, h.M.CurrentPhase);
        }

        [Test]
        public void AdditionsOn_QuestionnaireGetsQ4_AndPhasesAppear()
        {
            var h = new Harness("{\"induction_s\":60}", additions: true);
            h.M.Begin(); h.Calibrate(); h.Probe(-0.16);
            h.Adv(45000);   // induction 60 - 15 self-touch
            Assert.AreEqual(PhPhase.Induction, h.M.CurrentPhase);   // D13: no self-touch phase
            Assert.Less(h.M.CurrentStrokes.Last().EndMs, h.M.InductionStartMs + 60000);
            h.Adv(15000);
            Assert.AreEqual(PhPhase.Threat, h.M.CurrentPhase);
            h.Adv(5000); h.Probe(-0.18);
            Assert.AreEqual(PhPhase.Questionnaire, h.M.CurrentPhase);
            Assert.AreEqual(4, h.M.CurrentQuestionnaire.Items.Count);
        }

        [Test]
        public void Events_SerializeToJson_WithSnakeCaseFields_AndOneQualityValue()
        {
            var h = new Harness();
            h.M.Begin(); h.Calibrate(); h.Probe(-0.16);
            h.Adv(90000);
            h.M.SubmitThreatResponse(new ThreatResponse { EmgPeakX = 3.2, Quality = new Dictionary<string, string> { { "emg", "ok" }, { "imu", "missing" } } });
            string line = JsonConvert.SerializeObject(h.Ev.Last());
            var j = JObject.Parse(line);
            Assert.AreEqual("threat_response", j["type"].Value<string>());
            Assert.AreEqual(0, j["trial"].Value<int>());
            // event.schema.json: quality is ONE of ok | degraded | missing (the per-stream map stays on the module result)
            Assert.AreEqual(JTokenType.String, j["data"]["quality"].Type);
            Assert.AreEqual("degraded", j["data"]["quality"].Value<string>(), "emg ok, imu missing, wrist never analysed");
            Assert.AreEqual(3.2, j["data"]["emg_peak_x"].Value<double>(), 1e-9);
            Assert.AreEqual("missing", h.M.Results[0].Threat.Quality["imu"], "per-stream flags are kept on the result");
        }

        [Test]
        public void ThreatResponse_OverallQuality_IsOkOnlyWhenEveryStreamIsOk_AndMissingOnlyWhenNoneHasData()
        {
            System.Func<string, string, string, ThreatResponse> make = (w, i, e) =>
            {
                var r = new ThreatResponse();
                if (w != null) r.Quality["wrist"] = w;
                if (i != null) r.Quality["imu"] = i;
                if (e != null) r.Quality["emg"] = e;
                return r;
            };
            Assert.AreEqual("ok", make("ok", "ok", "ok").OverallQuality());
            Assert.AreEqual("degraded", make("ok", "ok", "degraded").OverallQuality());
            Assert.AreEqual("degraded", make("ok", "missing", "missing").OverallQuality(), "hand tracking alone is a valid fallback: degraded, not missing");
            Assert.AreEqual("degraded", make("ok", null, null).OverallQuality(), "a stream that was never analysed counts as missing");
            Assert.AreEqual("missing", make("missing", "missing", "missing").OverallQuality());
            Assert.AreEqual("missing", make(null, null, null).OverallQuality());
        }

        [Test]
        public void QuestionnaireEvents_CarryTheContractScale_1To7_WhileTheScreenScaleStaysMinus3To3()
        {
            Assert.AreEqual(1, Questionnaire.ContractValue(Questionnaire.Min));
            Assert.AreEqual(4, Questionnaire.ContractValue(0));
            Assert.AreEqual(7, Questionnaire.ContractValue(Questionnaire.Max));
            var h = new Harness();
            h.M.Begin(); h.Calibrate();
            h.Condition(-0.16, -0.19, 1.2, null, new[] { -3, 0, 3 });
            var items = h.Ev.Where(e => e.Type == "questionnaire_item").Select(e => JObject.FromObject(e.Data)).Take(3).ToList();
            CollectionAssert.AreEqual(new[] { 1, 4, 7 }, items.Select(d => d["value"].Value<int>()).ToList());
            Assert.AreEqual(-1.5, h.M.Results[0].Ownership.Value, 1e-9, "the on-device results (witness) stay on the on-screen -3..+3 scale: mean(q1, q2) = mean(-3, 0)");
        }

        [Test]
        public void EmgBurst_EventShape()
        {
            var h = new Harness();
            h.M.Begin();
            Assert.IsTrue(h.M.SubmitEmgBurst(900, 400, 123456));
            var j = JObject.FromObject(h.Ev.Last().Data);
            Assert.AreEqual(900, j["peak"].Value<double>()); Assert.AreEqual(400, j["baseline_rms"].Value<double>()); Assert.AreEqual(123456, j["device_ms"].Value<double>());
        }

        [Test]
        public void GameRegistry_FindsPhantomHand()
        {
            GameRegistry.Rescan();
            Assert.IsTrue(GameRegistry.Games.ContainsKey("phantom_hand"));
            Assert.IsInstanceOf<PhantomHandModule>(GameRegistry.Create("phantom_hand"));
        }

        [Test]
        public void LoadManifest_AcceptsMinimalManifest()
        {
            var m = new PhantomHandModule();
            m.LoadManifest("{\"id\":\"phantom_hand\",\"version\":\"0.1.0\",\"paramSchema\":{\"type\":\"object\",\"properties\":{}}}");
            Assert.AreEqual("phantom_hand", m.Manifest.Id);
        }
    }
}
