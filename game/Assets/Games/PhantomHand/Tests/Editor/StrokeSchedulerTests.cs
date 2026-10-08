using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

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
}
