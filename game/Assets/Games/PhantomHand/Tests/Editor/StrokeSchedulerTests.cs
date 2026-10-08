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
            foreach (var soa in new[] { 60, 100, 180, 300, 833, 1700 })
                foreach (var s in Plan(PhCondition.Sync, "{\"motor_soa_ms\":" + soa + "}"))
                    Assert.AreEqual(soa, s.PassBMs - s.PassAMs, 1e-9);
        }

        // ---- D16: the default stroke is slow (833 ms from motor A to motor B = 12 cm/s) --------------------------------

        private const string Fast = "{\"motor_soa_ms\":100}";   // the old behaviour, still reachable

        [Test]
        public void Defaults_PassBMinusPassA_Is833_AndTheStrokeTakes2083ms()
        {
            var p = Ph.Params();
            Assert.AreEqual(833, p.MotorSoaMs);
            foreach (var c in new[] { PhCondition.Sync, PhCondition.Async })
            {
                var s = Plan(c)[0];
                Assert.AreEqual(833, s.PassBMs - s.PassAMs, 1e-9);
                Assert.AreEqual(416.5, s.PassAMs - s.StartMs, 1e-9, "motor A is 5 cm from the wrist at 12 cm/s");
                Assert.AreEqual(2083, s.EndMs - s.StartMs, 0.5 + 1e-9, "25 cm at 12 cm/s");
            }
            Assert.AreEqual(12.0, new StrokeScheduler(p, 1).SpeedCmPerMs * 1000.0, 0.01, "cm per second");
        }

        [Test]
        public void ConsecutiveStrokeStarts_AreAtLeastTheDurationPlus300msApart()
        {
            foreach (var c in new[] { PhCondition.Sync, PhCondition.Async })
                foreach (var json in new[] { "{}", "{\"stroke_rate_hz\":1.5,\"stroke_jitter_ms\":300}", "{\"stroke_rate_hz\":0.5,\"stroke_jitter_ms\":0}" })
                {
                    var l = Plan(c, json, seed: 4, dur: 120000);
                    Assert.Greater(l.Count, 20, json);
                    for (int i = 1; i < l.Count; i++)
                    {
                        Assert.GreaterOrEqual(l[i].StartMs - l[i - 1].EndMs, StrokeScheduler.MinStrokeGapMs - 1e-6, c + " " + json + " stroke " + i);
                        Assert.GreaterOrEqual(l[i].StartMs - l[i - 1].StartMs, 2082.5 + 300 - 1e-6, c + " " + json + " stroke " + i);
                    }
                }
            Assert.AreEqual(300, StrokeScheduler.MinStrokeGapMs);
        }

        [Test]
        public void Default_StrokeSpacing_IsTheGapRule_NotTheRateOrJitter()
        {
            // 1/stroke_rate_hz +/- jitter (850-1150 ms) is shorter than 2083 + 300, so the gap rule decides and the spacing is exact
            var l = Plan(PhCondition.Sync);
            for (int i = 1; i < l.Count; i++) Assert.AreEqual(2382.5, l[i].StartMs - l[i - 1].StartMs, 1e-6);
        }

        [Test]
        public void NominalRate_StillApplies_WhenItGivesALongerInterval()
        {
            // a faster brush (soa 300 ms: the stroke takes 750 ms, +300 ms gap = 1050 ms) leaves the nominal 0.5 Hz period of 2000 ms as the longer interval
            var l = Plan(PhCondition.Sync, "{\"motor_soa_ms\":300,\"stroke_rate_hz\":0.5,\"stroke_jitter_ms\":0}");
            for (int i = 1; i < l.Count; i++) Assert.AreEqual(2000, l[i].StartMs - l[i - 1].StartMs, 1e-6);
        }

        [Test]
        public void An45sInduction_PlansBetween17And20Strokes()
        {
            foreach (var c in new[] { PhCondition.Sync, PhCondition.Async })
                foreach (int seed in new[] { 1, 2, 3, 11, 12345 })
                {
                    var l = new StrokeScheduler(Ph.Params("{\"demo_mode\":true}"), seed).Plan(c, 0, 45000);
                    Assert.That(l.Count, Is.InRange(17, 20), c + " seed " + seed);
                    Assert.Less(l.Max(s => Math.Max(s.EndMs, s.LastCueMs)), 45000);
                }
        }

        [Test]
        public void PulseTimePerMotor_InAny10sWindow_StaysUnderHalf_For200msPulses()
        {
            double pulse = HapticCueMapper.StrokeDurationMs;
            Assert.AreEqual(200, pulse);
            foreach (var json in new[] { "{}", Fast, "{\"stroke_rate_hz\":1.5,\"async_delay_ms\":300,\"stroke_jitter_ms\":300}", "{\"motor_soa_ms\":1700}" })
                foreach (var c in new[] { PhCondition.Sync, PhCondition.Async })
                {
                    var l = Plan(c, json, seed: 8, dur: 120000);
                    for (int m = 0; m < 2; m++)
                    {
                        var t = l.Select(s => m == 0 ? s.CueMotor0Ms : s.CueMotor1Ms).OrderBy(x => x).ToList();
                        double worst = 0;
                        for (int i = 0; i < t.Count; i++)
                        {
                            // window starting at a pulse start: every pulse that starts inside [t_i, t_i + 10 s) counts a full 200 ms
                            double inWindow = t.Count(x => x >= t[i] && x < t[i] + 10000) * pulse;
                            worst = Math.Max(worst, inWindow / 10000.0);
                        }
                        Assert.Less(worst, 0.5, json + " " + c + " motor " + m + " worst duty " + worst);
                        if (json == "{}") Assert.LessOrEqual(worst, 0.10 + 1e-9, "default: at most 5 pulses of 200 ms per 10 s = 10 %");
                    }
                }
        }

        [Test]
        public void FastBrush_StillReachable_WithMotorSoa100()
        {
            var p = Ph.Params(Fast);
            Assert.AreEqual(100, p.MotorSoaMs);
            var l = Plan(PhCondition.Sync, Fast);
            Assert.AreEqual(50, l[0].PassAMs - l[0].StartMs, 1e-9);
            Assert.AreEqual(250, l[0].EndMs - l[0].StartMs, 1e-9);
            Assert.Greater(l.Count, 80, "about one stroke per second again");
            for (int i = 1; i < l.Count; i++) Assert.GreaterOrEqual(l[i].StartMs - l[i - 1].EndMs, StrokeScheduler.MinStrokeGapMs - 1e-6);
        }

        // ---- swap guard: the delayed condition swaps the A/B slots only when the delay clears the brush's A-to-B time by 300 ms ----

        [Test]
        public void Async_NeverSwaps_AtTheDefaultSoa()
        {
            foreach (int seed in new[] { 1, 2, 3, 77 })
            {
                var l = Plan(PhCondition.Async, "{}", seed, dur: 600000);
                Assert.Greater(l.Count, 100);
                Assert.IsFalse(l.Any(s => s.Swapped), "seed " + seed + ": a swapped B tap would fire 233 ms BEFORE the brush reaches B");
            }
        }

        [TestCase(100, 600, true)]
        [TestCase(100, 400, true)]
        [TestCase(100, 399, false)]
        [TestCase(300, 600, true)]
        [TestCase(301, 600, false)]
        [TestCase(600, 1000, true)]
        [TestCase(833, 600, false)]
        [TestCase(833, 1000, false)]
        [TestCase(60, 300, false)]
        [TestCase(60, 360, true)]
        public void SwapGuard_SwapsOnlyWhenDelayMinusSoaIsAtLeast300(int soa, int delay, bool swaps)
        {
            Assert.AreEqual(300, StrokeScheduler.SwapMinMs);
            var l = Plan(PhCondition.Async, "{\"motor_soa_ms\":" + soa + ",\"async_delay_ms\":" + delay + "}", seed: 5, dur: 1000000);
            Assert.Greater(l.Count, 300);
            double frac = l.Count(s => s.Swapped) / (double)l.Count;
            if (swaps) Assert.That(frac, Is.InRange(0.4, 0.6), "soa " + soa + " delay " + delay);
            else Assert.AreEqual(0.0, frac, "soa " + soa + " delay " + delay);
        }

        [Test]
        public void SwapsStillHappen_AtSoa100()
        {
            var l = Plan(PhCondition.Async, Fast, seed: 12, dur: 200000);
            Assert.That(l.Count(s => s.Swapped) / (double)l.Count, Is.InRange(0.35, 0.65));
        }

        [Test]
        public void DelayedPlan_AtTheDefaults_EveryCueIsAtLeast400msAfterItsOwnBrushPass()
        {
            foreach (int seed in new[] { 1, 2, 3, 9, 77 })
                foreach (var s in Plan(PhCondition.Async, "{}", seed, dur: 300000))
                {
                    Assert.GreaterOrEqual(s.CueMotor0Ms - s.PassAMs, 400.0, "motor 0 sits at A: seed " + seed + " stroke " + s.Index);
                    Assert.GreaterOrEqual(s.CueMotor1Ms - s.PassBMs, 400.0, "motor 1 sits at B: seed " + seed + " stroke " + s.Index);
                }
        }

        [Test]
        public void MotorSoa_RangeIs60To1700_DefaultIs833()
        {
            Assert.AreEqual(833, Ph.Params().MotorSoaMs);
            Assert.AreEqual(60, Ph.Params("{\"motor_soa_ms\":1}").MotorSoaMs);
            Assert.AreEqual(1700, Ph.Params("{\"motor_soa_ms\":99999}").MotorSoaMs);
            Assert.AreEqual(1250, Ph.Params("{\"motor_soa_ms\":1250}").MotorSoaMs);
            // the game's own manifest says the same (the other two copies are Opus's to sync)
            var file = ManifestPath();
            if (file == null) Assert.Inconclusive("manifest.json not found next to the sources");
            var prop = JObject.Parse(System.IO.File.ReadAllText(file))["paramSchema"]["properties"]["motor_soa_ms"];
            Assert.AreEqual(833, prop["default"].Value<int>()); Assert.AreEqual(60, prop["minimum"].Value<int>()); Assert.AreEqual(1700, prop["maximum"].Value<int>());
            StringAssert.Contains("833 ms = 12 cm/s", prop["x-ui"]["help"].Value<string>());
        }

        private static string ManifestPath([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        {
            if (string.IsNullOrEmpty(thisFile)) return null;
            var dir = System.IO.Path.GetDirectoryName(thisFile);                       // .../PhantomHand/Tests/Editor
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, "..", "..", "manifest.json"));
            return System.IO.File.Exists(path) ? path : null;
        }

        // ---- the brush rig follows the slow plan, including the swing back between strokes ---------------------------------

        [Test]
        public void BrushPath_WithTheDefaultPlan_StrokesAtTheSetSpeed_AndSwingsBackBetweenStrokes()
        {
            var p = Ph.Params();
            var plan = Plan(PhCondition.Sync, dur: 30000);
            double speedMPerMs = p.MotorSpacingCm / 100.0 / p.MotorSoaMs;
            float len = (float)(p.ForearmLengthCm / 100.0);
            for (int k = 0; k + 1 < plan.Count && k < 4; k++)
            {
                var a = plan[k]; var b = plan[k + 1];
                // contact: from the wrist to the elbow at 12 cm/s
                var mid = BrushRig.Evaluate(plan, a.StartMs + 1000, speedMPerMs, len);
                Assert.IsTrue(mid.Contact); Assert.AreEqual(0.12f, mid.D, 1e-4f); Assert.AreEqual(0f, mid.Lift, 1e-6f); Assert.AreEqual(k, mid.StrokeIndex);
                var end = BrushRig.Evaluate(plan, a.EndMs, speedMPerMs, len);
                Assert.AreEqual(len, end.D, 1e-4f);
                // between the strokes: lifts at the elbow, swings back to the wrist, comes down by the next stroke's start
                double gap = b.StartMs - a.EndMs;
                Assert.AreEqual(StrokeScheduler.MinStrokeGapMs, gap, 1e-6);
                double swingMs = gap - BrushRig.LiftMs - BrushRig.DescendMs;
                Assert.Greater(swingMs, 0, "the 300 ms gap leaves a swing: lift 120 + swing + descent 120");
                Assert.AreEqual(60, swingMs, 1e-6);
                var lifted = BrushRig.Evaluate(plan, a.EndMs + BrushRig.LiftMs, speedMPerMs, len);
                Assert.AreEqual(len, lifted.D, 1e-4f); Assert.AreEqual(BrushRig.HoverM, lifted.Lift, 1e-4f); Assert.IsFalse(lifted.Contact);
                var back = BrushRig.Evaluate(plan, b.StartMs - BrushRig.DescendMs, speedMPerMs, len);
                Assert.AreEqual(0f, back.D, 1e-4f, "back over the wrist when the descent starts");
                var down = BrushRig.Evaluate(plan, b.StartMs, speedMPerMs, len);
                Assert.AreEqual(0f, down.D, 1e-4f); Assert.AreEqual(0f, down.Lift, 1e-4f); Assert.AreEqual(k + 1, down.StrokeIndex);
                // the whole path stays on the forearm and above the skin, every 5 ms
                for (double t = a.StartMs - 200; t <= b.StartMs + 200; t += 5)
                {
                    var s = BrushRig.Evaluate(plan, t, speedMPerMs, len);
                    Assert.GreaterOrEqual(s.D, -1e-6f); Assert.LessOrEqual(s.D, len + 1e-6f); Assert.GreaterOrEqual(s.Lift, -1e-6f);
                    Assert.IsFalse(float.IsNaN(s.D) || float.IsNaN(s.Lift));
                }
            }
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
        public void Interval_WithinRateAndJitter()
        {
            var l = Plan(PhCondition.Sync, Fast);   // the fast brush (250 ms stroke): rate and jitter decide, the 300 ms gap rule does not
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
            var l = Plan(PhCondition.Sync, "{\"stroke_jitter_ms\":0,\"stroke_rate_hz\":0.5,\"motor_soa_ms\":100}");
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
            var l = new StrokeScheduler(Ph.Params(Fast), 77).Plan(PhCondition.Async, 0, 520000);   // swaps need the fast brush now (see the guard tests below)
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
            // at the default speed the stroke starts are fixed by the 300 ms gap rule, so the seed shows in the ASYNC cues and the A/B swaps
            Assert.IsTrue(a.Zip(c, (x, y) => x.CueMotor0Ms != y.CueMotor0Ms || x.Swapped != y.Swapped).Any(d => d));
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
                Assert.Greater(l.Count, 30, "slow strokes: about 37 in 90 s");
                var fast = Plan(c, Fast, seed: 3);
                Assert.Less(fast.Max(s => Math.Max(s.EndMs, s.LastCueMs)), end);
                Assert.Greater(fast.Count, 80, "the fast brush: about one stroke per second");
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
            foreach (var json in new[] { "{}", "{\"stroke_rate_hz\":1.5,\"async_delay_ms\":300,\"stroke_jitter_ms\":300}",
                                         Fast, "{\"motor_soa_ms\":100,\"stroke_rate_hz\":1.5,\"async_delay_ms\":300,\"stroke_jitter_ms\":300}" })
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
            var p = Ph.Params(); p.SelfTouchS = 15;   // A3 is not built, so the params ignore self_touch_s; set the field as A3 will
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
            f.RunTo(f.M.InductionStartMs + 10000);                   // strokes come every 2.38 s now: 8 cues are out after 10 s
            int sentBefore = f.Strokes().Count;
            Assert.Greater(sentBefore, 5, "a few strokes went out before the pause");

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
            Assert.Greater(ahead.Count, 15, "most of the induction is still ahead");
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
            f.RunTo(k.PassBMs + 50);                       // both passes are done; the cue of the A slot (pass + about 600 ms, before the brush even reaches B) is out, the B slot's is still queued
            Assert.Greater(k.LastCueMs - f.Lead, f.Now);

            f.Pause();
            f.RunTo(f.Now + 150);
            Assert.AreEqual(0, f.StrokeEvents().Count(e => Fx.Data(e)["index"].Value<int>() == k.Index), "the paused module refuses stroke events");
            f.M.Resume();
            int ahead = f.M.CurrentStrokes.Sum(s => (s.CueMotor0Ms - f.Lead >= f.Now ? 1 : 0) + (s.CueMotor1Ms - f.Lead >= f.Now ? 1 : 0));
            Assert.AreEqual(ahead, f.H.PendingStrokeCount, "every cue still ahead is queued again, the one of the stroke the pause cut through included");
            f.RunTo(f.M.CurrentStrokes.Last().EndMs + 1500);

            var mine = f.StrokeEvents().Select(Fx.Data).Where(d => d["index"].Value<int>() == k.Index).ToList();
            Assert.AreEqual(1, mine.Count, "the stroke the pause cut through is recorded, once");
            Assert.AreNotEqual(JTokenType.Null, mine[0]["cue_a_send_ms"].Type, "both cues were sent: the first before the pause, the second after the resume");
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
