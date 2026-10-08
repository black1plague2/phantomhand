using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;

namespace Opus.Games.PhantomHand.Tests
{
    public class StrokeSchedulerTests
    {
        private static List<StrokePlan> Plan(PhCondition c, string json = "{}", int seed = 1, double t0 = 1000, double dur = 90000)
        {
            return new StrokeScheduler(Ph.Params(json), seed).Plan(c, t0, dur);
        }

        [Test]
        public void Sync_CueEqualsPass_ForEveryStroke()
        {
            foreach (var s in Plan(PhCondition.Sync))
            {
                Assert.AreEqual(s.PassAMs, s.CueMotor0Ms, 1e-9);
                Assert.AreEqual(s.PassBMs, s.CueMotor1Ms, 1e-9);
                Assert.IsFalse(s.Swapped);
            }
        }

        [Test]
        public void PassGap_EqualsMotorSoa()
        {
            foreach (var soa in new[] { 60, 100, 180, 300 })
                foreach (var s in Plan(PhCondition.Sync, "{\"motor_soa_ms\":" + soa + "}"))
                    Assert.AreEqual(soa, s.PassBMs - s.PassAMs, 1e-9);
        }

        [Test]
        public void BrushSpeed_ConstantWristToElbow()
        {
            var p = Ph.Params();
            var s = Plan(PhCondition.Sync)[3];
            double speed = p.MotorSpacingCm / p.MotorSoaMs;                       // cm per ms
            Assert.AreEqual(p.MotorAFromWristCm / speed, s.PassAMs - s.StartMs, 1e-9);
            Assert.AreEqual(p.ForearmLengthCm / speed, s.EndMs - s.StartMs, 1e-9);
            // distance covered per ms is the same on the wrist-to-A, A-to-B and B-to-elbow legs
            double legB = (s.EndMs - s.PassBMs) * speed;
            Assert.AreEqual(p.ForearmLengthCm - p.MotorAFromWristCm - p.MotorSpacingCm, legB, 1e-9);
        }

        [Test]
        public void Defaults_StrokeTakes250ms_PassAAt50ms()
        {
            var s = Plan(PhCondition.Sync)[0];
            Assert.AreEqual(50, s.PassAMs - s.StartMs, 1e-9);
            Assert.AreEqual(250, s.EndMs - s.StartMs, 1e-9);
        }

        [Test]
        public void Interval_WithinRateAndJitter()
        {
            var l = Plan(PhCondition.Sync);
            for (int i = 1; i < l.Count; i++)
            {
                double gap = l[i].StartMs - l[i - 1].StartMs;
                Assert.GreaterOrEqual(gap, 850 - 1e-6); Assert.LessOrEqual(gap, 1150 + 1e-6);
            }
            double mean = (l.Last().StartMs - l.First().StartMs) / (l.Count - 1);
            Assert.AreEqual(1000, mean, 40);
        }

        [Test]
        public void Jitter0_GivesExactPeriod()
        {
            var l = Plan(PhCondition.Sync, "{\"stroke_jitter_ms\":0,\"stroke_rate_hz\":0.5}");
            for (int i = 1; i < l.Count; i++) Assert.AreEqual(2000, l[i].StartMs - l[i - 1].StartMs, 1e-6);
        }

        [Test]
        public void Async_DelayPerSlot_Is500To700()
        {
            foreach (var s in Plan(PhCondition.Async, seed: 5, dur: 180000))
            {
                Assert.That(s.SlotACueMs - s.PassAMs, Is.InRange(500.0, 700.0));
                Assert.That(s.SlotBCueMs - s.PassBMs, Is.InRange(500.0, 700.0));
            }
        }

        [Test]
        public void Async_SwapsAboutHalf_Over500Strokes()
        {
            var l = new StrokeScheduler(Ph.Params(), 77).Plan(PhCondition.Async, 0, 520000);
            Assert.GreaterOrEqual(l.Count, 500);
            var first500 = l.Take(500).ToList();
            double frac = first500.Count(s => s.Swapped) / 500.0;
            Assert.That(frac, Is.InRange(0.40, 0.60));
            // swapped really changes which motor gets the earlier slot
            var sw = first500.First(s => s.Swapped);
            Assert.AreEqual(sw.CueMotor1Ms, sw.SlotACueMs);
        }

        [Test]
        public void Seeded_SameSeedSamePlan_DifferentSeedDiffers()
        {
            var a = Plan(PhCondition.Async, seed: 9); var b = Plan(PhCondition.Async, seed: 9); var c = Plan(PhCondition.Async, seed: 10);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].StartMs, b[i].StartMs); Assert.AreEqual(a[i].CueMotor0Ms, b[i].CueMotor0Ms); Assert.AreEqual(a[i].Swapped, b[i].Swapped);
            }
            Assert.IsTrue(a.Zip(c, (x, y) => x.StartMs != y.StartMs).Any(d => d));
        }

        [Test]
        public void Strokes_NeverOverlap_EvenWhenSlowBrush()
        {
            // soa 300 ms over 5 cm spacing and a 30 cm forearm: stroke lasts 1.8 s, longer than the 1 s period
            var l = Plan(PhCondition.Sync, "{\"motor_soa_ms\":300,\"motor_spacing_cm\":5,\"forearm_length_cm\":30,\"motor_a_from_wrist_cm\":2}");
            Assert.Greater(l.Count, 5);
            for (int i = 1; i < l.Count; i++) Assert.Greater(l[i].StartMs, l[i - 1].EndMs);
        }

        [Test]
        public void LastStroke_AndItsCues_EndBeforeInductionEnd()
        {
            foreach (var c in new[] { PhCondition.Sync, PhCondition.Async })
            {
                double end = 1000 + 90000;
                var l = Plan(c, seed: 3);
                Assert.Less(l.Max(s => Math.Max(s.EndMs, s.LastCueMs)), end);
                Assert.Greater(l.Count, 80);
            }
        }

        [Test]
        public void FirstStroke_StartsAfterLeadIn()
        {
            Assert.AreEqual(1500, Plan(PhCondition.Sync)[0].StartMs, 1e-9);
        }

        [Test]
        public void SafetyGates_Default_And_FastAsync_Respected()
        {
            foreach (var json in new[] { "{}", "{\"stroke_rate_hz\":1.5,\"async_delay_ms\":300,\"stroke_jitter_ms\":300}" })
                foreach (var c in new[] { PhCondition.Sync, PhCondition.Async })
                {
                    var l = Plan(c, json, seed: 21, dur: 120000);
                    for (int m = 0; m < 2; m++)
                    {
                        var times = l.Select(s => m == 0 ? s.CueMotor0Ms : s.CueMotor1Ms).OrderBy(t => t).ToList();
                        for (int i = 1; i < times.Count; i++) Assert.GreaterOrEqual(times[i] - times[i - 1], 250 - 1e-6, json + " " + c + " motor " + m);
                    }
                    var all = l.SelectMany(s => new[] { s.CueMotor0Ms, s.CueMotor1Ms }).OrderBy(t => t).ToList();
                    for (int i = 4; i < all.Count; i++) Assert.GreaterOrEqual(all[i] - all[i - 4], 1000 - 1e-6, "more than 4 sends in a second: " + json + " " + c);
                }
        }

        [Test]
        public void InductionWithSelfTouchTail_PlansOnlyTheBrushPart()
        {
            var p = Ph.Params();
            var m = new PhaseStateMachine(p, additions: true);
            var l = new StrokeScheduler(p, 1).Plan(PhCondition.Sync, 0, m.InductionPhaseMs);
            Assert.Less(l.Last().EndMs, 75000);
        }
    }

    /// <summary>
    /// StrokeDriver after pause + resume (U5 log, open issue 3). The operator's pause calls HapticClient.Stop, which cancels every
    /// queued stroke cue; nothing queued them again, so the touch ended with the pause. Here the driver, the module and a real
    /// HapticClient (over a recording transport) run frame by frame on a manual clock, in the composition root's order:
    /// haptic Pump, module Tick, brush, driver Tick. EditMode has no BrushRig, so the "brush" reports every pass exactly at its
    /// plan time (what the animation measures to within a frame) through StrokeDriver.OnPass.
    /// </summary>
    public class StrokeDriverTests
    {
        private const string Sync = "{\"condition_order\":\"sync_first\",\"induction_s\":40}";
        private const string Async = "{\"condition_order\":\"async_first\",\"induction_s\":40}";

        private sealed class DriverSession : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex { get { return 0; } }
        }

        private sealed class DriverTransport : IHapticTransport
        {
            public readonly List<string> Sent = new List<string>();
            public bool HasDevice { get; set; } = true;
            public event Action<string> OnMessage { add { } remove { } }
            public event Action OnDeviceKnown { add { } remove { } }
            public void Start() { }
            public void Stop() { }
            public void Send(string json) { Sent.Add(json); }
            public void Dispose() { }
        }

        private sealed class Fx
        {
            public const double FrameMs = 10;
            public readonly DriverSession S = new DriverSession();
            public readonly PhantomHandModule M = new PhantomHandModule();
            public readonly DriverTransport T = new DriverTransport();
            public readonly HapticClient H;
            public readonly StrokeDriver D;
            public readonly List<TrialEvent> Ev = new List<TrialEvent>();
            public readonly List<HapticCueRecord> Cues = new List<HapticCueRecord>();
            private readonly HashSet<string> _passed = new HashSet<string>();

            public Fx(string paramsJson)
            {
                M.OnTrialEvent += Ev.Add;
                M.Configure(Ph.P(paramsJson), S);
                H = new HapticClient(T);
                H.OnCueRecorded += Cues.Add;
                D = new StrokeDriver(M, H, null);
            }

            public double Now { get { return S.Clock.NowMs; } }
            public double Lead { get { return M.Params.TactileLeadMs; } }

            public void Step()
            {
                S.Clock.Advance(FrameMs);
                H.Pump(Now);
                M.Tick(Now);
                if (!D.Active) return;
                foreach (var s in M.CurrentStrokes)
                {
                    if (s.PassAMs <= Now && _passed.Add(s.Index + "a")) D.OnPass(s.Index, 0, s.PassAMs);
                    if (s.PassBMs <= Now && _passed.Add(s.Index + "b")) D.OnPass(s.Index, 1, s.PassBMs);
                }
                D.Tick(Now);
            }

            public void RunTo(double ms) { while (Now < ms) Step(); }

            public void ToInduction()
            {
                M.Begin();
                Step();
                Assert.IsTrue(M.SubmitCalibration(new[] { 0.0, 0.75, 0.3 }, new[] { 0.0, 0.0, 1.0 }, true));
                Step();
                Assert.AreEqual(PhPhase.ProbePre, M.CurrentPhase);
                Assert.IsTrue(M.SubmitProbe(new ProbeResult { When = "pre", Confirmed = true, PerceivedXm = -0.16, ActualXm = 0.0, DriftCm = 0 }));
                Assert.AreEqual(PhPhase.Induction, M.CurrentPhase);
                D.Begin(Now);                  // the presenter does this on the phase change
            }

            /// <summary>What PhantomHandSceneController.PauseSession does.</summary>
            public void Pause() { M.Pause(); H.Stop(); }

            /// <summary>Every stroke command that reached the wire, in order.</summary>
            public List<JObject> Strokes() { return T.Sent.Select(s => JObject.Parse(s)).Where(o => (string)o["cue"] == "stroke").ToList(); }

            public List<TrialEvent> StrokeEvents() { return Ev.Where(e => e.Type == "stroke").ToList(); }

            public static JObject Data(TrialEvent e) { return JObject.FromObject(e.Data); }
        }

        private static string Key(int motor, double playAtMs) { return motor + "@" + (long)Math.Round(playAtMs); }
        private static string Key(JObject sent) { return Key(sent["motor"].Value<int>(), sent["play_at_ms"].Value<double>()); }

        [Test]
        public void PauseThenResume_SchedulesTheRemainingCuesAgain_AtTheirOriginalPlanTimes()
        {
            var f = new Fx(Sync);
            f.ToInduction();
            var plan = f.M.CurrentStrokes;
            f.RunTo(f.M.InductionStartMs + 6000);
            int sentBefore = f.Strokes().Count;
            Assert.Greater(sentBefore, 8, "a few strokes went out before the pause");

            f.Pause();
            Assert.AreEqual(0, f.H.PendingStrokeCount, "HapticClient.Stop cancelled the whole queue");
            f.RunTo(f.Now + 3000);                         // the clock runs on while paused, and so does the brush
            Assert.AreEqual(sentBefore, f.Strokes().Count, "nothing is sent while paused");

            f.M.Resume();
            double resumedAt = f.Now;
            var ahead = new List<string>();
            foreach (var s in plan)
            {
                if (s.CueMotor0Ms - f.Lead >= resumedAt) ahead.Add(Key(0, s.CueMotor0Ms));
                if (s.CueMotor1Ms - f.Lead >= resumedAt) ahead.Add(Key(1, s.CueMotor1Ms));
            }
            Assert.Greater(ahead.Count, 30, "most of the induction is still ahead");
            Assert.AreEqual(ahead.Count, f.H.PendingStrokeCount, "every cue that is still ahead is queued again, and nothing else");

            f.RunTo(plan.Last().EndMs + 1000);
            var after = f.Strokes().Skip(sentBefore).ToList();
            CollectionAssert.AreEquivalent(ahead, after.Select(Key).ToList(),
                "the cues sent after the resume are exactly the ones still ahead, at the plan's own landing times, each once");

            // touch against sight: every one left within a frame of (plan time - lead), and in SYNC the plan time IS the brush pass
            var resent = f.Cues.Where(r => r.Cue == "stroke" && r.SentMs.HasValue && r.SentMs.Value >= resumedAt).ToList();
            Assert.AreEqual(ahead.Count, resent.Count);
            foreach (var r in resent)
            {
                double late = r.SentMs.Value - (r.PlayAtMs.Value - f.Lead);
                Assert.GreaterOrEqual(late, 0.0, "never early");
                Assert.Less(late, Fx.FrameMs + 1e-6, "within one frame of its plan time");
            }
            var events = f.StrokeEvents();
            var indices = events.Select(e => Fx.Data(e)["index"].Value<int>()).ToList();
            Assert.AreEqual(indices.Count, indices.Distinct().Count(), "one stroke event per stroke");
            int nullErr = 0;
            foreach (var e in events.Where(e => e.TMs >= resumedAt))
            {
                var d = Fx.Data(e);
                if (d["timing_err_ms"].Type == JTokenType.Null) { nullErr++; continue; }   // a cue due while paused is never sent late
                double err = d["timing_err_ms"].Value<double>();
                Assert.GreaterOrEqual(err, 0.0);
                Assert.Less(err, Fx.FrameMs + 1e-6, "SYNC touch lands with the visual pass, as before the pause");
            }
            Assert.LessOrEqual(nullErr, 1, "at most the one stroke the resume cut through has a missing send time");
        }

        [Test]
        public void ResumeWithoutAStop_QueuesNothingTwice()
        {
            var f = new Fx(Sync);
            f.ToInduction();
            f.RunTo(f.M.InductionStartMs + 3000);
            f.M.Pause();                                   // the module is paused but nobody cancelled the sleeve queue
            int pending = f.H.PendingStrokeCount;
            Assert.Greater(pending, 0);
            f.M.Resume();
            Assert.AreEqual(pending, f.H.PendingStrokeCount, "no cue was cancelled, so none is scheduled again");
            f.RunTo(f.M.CurrentStrokes.Last().EndMs + 1000);
            var keys = f.Strokes().Select(Key).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "every cue went out exactly once");
        }

        [Test]
        public void TwoPauses_EveryCueOutsideThePauseWindowsIsSentExactlyOnce_AndNoneInsideThem()
        {
            var f = new Fx(Sync);
            f.ToInduction();
            var windows = new List<double[]>();
            foreach (double after in new[] { 4000.0, 9000.0 })
            {
                f.RunTo(f.M.InductionStartMs + after);
                double from = f.Now;
                f.Pause();
                f.RunTo(f.Now + 2500);
                f.M.Resume();
                windows.Add(new[] { from, f.Now });
            }
            f.RunTo(f.M.CurrentStrokes.Last().EndMs + 1000);

            var expected = new List<string>();
            foreach (var s in f.M.CurrentStrokes)
            {
                if (!windows.Any(w => s.CueMotor0Ms - f.Lead > w[0] && s.CueMotor0Ms - f.Lead < w[1])) expected.Add(Key(0, s.CueMotor0Ms));
                if (!windows.Any(w => s.CueMotor1Ms - f.Lead > w[0] && s.CueMotor1Ms - f.Lead < w[1])) expected.Add(Key(1, s.CueMotor1Ms));
            }
            CollectionAssert.AreEquivalent(expected, f.Strokes().Select(Key).ToList(), "sent exactly once outside the windows");
            foreach (var w in windows)
                Assert.IsFalse(f.Cues.Any(r => r.SentMs.HasValue && r.SentMs.Value > w[0] && r.SentMs.Value < w[1]), "nothing is sent while paused");
        }

        [Test]
        public void AsyncStrokeCaughtByAShortPause_StillGetsItsStrokeEvent_OnceItsCuesGoOut()
        {
            var f = new Fx(Async);
            f.ToInduction();
            var k = f.M.CurrentStrokes[6];
            f.RunTo(k.PassBMs + 50);                       // both passes are done; the two delayed cues (about 600 ms after the passes) are still queued
            Assert.Greater(k.CueMotor0Ms - f.Lead, f.Now);
            Assert.Greater(k.CueMotor1Ms - f.Lead, f.Now);

            f.Pause();
            f.RunTo(f.Now + 150);
            Assert.AreEqual(0, f.StrokeEvents().Count(e => Fx.Data(e)["index"].Value<int>() == k.Index), "the paused module refuses stroke events");
            f.M.Resume();
            int ahead = f.M.CurrentStrokes.Sum(s => (s.CueMotor0Ms - f.Lead >= f.Now ? 1 : 0) + (s.CueMotor1Ms - f.Lead >= f.Now ? 1 : 0));
            Assert.AreEqual(ahead, f.H.PendingStrokeCount, "every cue still ahead is queued again, the two of the stroke the pause cut through included");
            f.RunTo(f.M.CurrentStrokes.Last().EndMs + 1500);

            var mine = f.StrokeEvents().Select(Fx.Data).Where(d => d["index"].Value<int>() == k.Index).ToList();
            Assert.AreEqual(1, mine.Count, "the stroke the pause cut through is recorded, once");
            Assert.AreNotEqual(JTokenType.Null, mine[0]["cue_a_send_ms"].Type, "both cues were sent after the resume");
            Assert.AreNotEqual(JTokenType.Null, mine[0]["cue_b_send_ms"].Type);
            Assert.AreEqual(k.Swapped, mine[0]["swapped"].Value<bool>());
            var all = f.StrokeEvents().Select(e => Fx.Data(e)["index"].Value<int>()).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), "one stroke event per stroke");
        }

        [Test]
        public void Begin_Twice_StillQueuesEachCueOnlyOnceOnResume()
        {
            var f = new Fx(Sync);
            f.ToInduction();
            f.D.Begin(f.Now);                              // the next induction / a restart: must not leave a second subscription behind
            f.RunTo(f.M.InductionStartMs + 3000);
            f.Pause();
            f.RunTo(f.Now + 1000);
            f.M.Resume();
            int expected = f.M.CurrentStrokes.Sum(s => (s.CueMotor0Ms - f.Lead >= f.Now ? 1 : 0) + (s.CueMotor1Ms - f.Lead >= f.Now ? 1 : 0));
            Assert.AreEqual(expected, f.H.PendingStrokeCount);
        }

        [Test]
        public void AfterTheInductionEnded_AResumeSchedulesNothing()
        {
            var f = new Fx(Sync);
            f.ToInduction();
            f.RunTo(f.M.InductionStartMs + 3000);
            f.Pause();
            f.D.End(f.Now);                                // the presenter ends the driver when the phase leaves the induction
            f.M.Resume();
            Assert.AreEqual(0, f.H.PendingStrokeCount);
        }
    }
}
