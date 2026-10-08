using System.Linq;
using NUnit.Framework;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>U4 pure-logic tests: calibration hold, pinch toggle, HUD model, string table, witness content.</summary>
    public class UiModelTests
    {
        private static readonly double[] Target = { 0.20, 0.77, 0.40 };

        // ---- calibration ----------------------------------------------------------------------------------------------

        private static CalibState Hold(CalibrationTracker c, double fromMs, double toMs, double[] wrist, double[] palm = null)
        {
            var st = c.State;
            for (double t = fromMs; t <= toMs; t += 14) st = c.Update(t, true, wrist, palm);
            return st;
        }

        [Test]
        public void Calibration_ConfirmsAfter2s_WithinThreeCm_NotBefore()
        {
            var c = new CalibrationTracker(Target);
            var near = new[] { 0.21, 0.77, 0.41 };   // ~1.4 cm away
            Assert.AreEqual(CalibState.Holding, Hold(c, 0, 1900, near));
            Assert.Less(c.Progress01, 1.0);
            Assert.AreEqual(CalibState.Confirmed, Hold(c, 1910, 2100, near));
            Assert.AreEqual(0.21, c.Wrist[0], 1e-9);
        }

        [Test]
        public void Calibration_LeavingTheRadius_RestartsTheWindow()
        {
            var c = new CalibrationTracker(Target);
            Hold(c, 0, 1500, Target);
            c.Update(1520, true, new[] { 0.26, 0.77, 0.40 });   // 6 cm away
            Assert.AreEqual(CalibState.Waiting, c.State);
            Assert.AreEqual(CalibState.Holding, Hold(c, 1540, 3400, Target));   // only 1.9 s since return
            Assert.AreEqual(CalibState.Confirmed, Hold(c, 3410, 3700, Target));
        }

        [Test]
        public void Calibration_TrackingLoss_Resets()
        {
            var c = new CalibrationTracker(Target);
            Hold(c, 0, 1500, Target);
            c.Update(1520, false, null);
            Assert.AreEqual(CalibState.Waiting, c.State);
            Assert.AreEqual(0.0, c.Progress01, 1e-9);
        }

        [Test]
        public void Calibration_Axis_PointsFromWristToPalm_Horizontally()
        {
            var c = new CalibrationTracker(Target);
            Hold(c, 0, 2100, Target, new[] { 0.20, 0.80, 0.55 });
            Assert.AreEqual(CalibState.Confirmed, c.State);
            Assert.AreEqual(0.0, c.Axis[0], 1e-9);
            Assert.AreEqual(0.0, c.Axis[1], 1e-9);
            Assert.AreEqual(1.0, c.Axis[2], 1e-9);
        }

        [Test]
        public void Calibration_Axis_FallsBackWhenPalmMissing()
        {
            var a = CalibrationTracker.ForearmAxis(Target, null, null);
            Assert.AreEqual(1.0, a[2], 1e-9);
            var b = CalibrationTracker.ForearmAxis(Target, null, new[] { 1.0, 0.0, 0.0 });
            Assert.AreEqual(1.0, b[0], 1e-9);
        }

        // ---- pinch toggle ---------------------------------------------------------------------------------------------

        [Test]
        public void PinchToggle_NeedsBothHandsFor3s_AndReleaseToFlipAgain()
        {
            var p = new PinchToggle();
            for (double t = 0; t < 2900; t += 20) p.Update(t, true, true);
            Assert.IsFalse(p.On);
            Assert.IsTrue(p.Update(3000, true, true));
            for (double t = 3020; t < 7000; t += 20) p.Update(t, true, true);   // still held: no second flip
            Assert.IsTrue(p.On);
            p.Update(7020, false, true);                                         // release resets
            for (double t = 7040; t <= 10100; t += 20) p.Update(t, true, true);
            Assert.IsFalse(p.On);
        }

        [Test]
        public void PinchToggle_OneHandNeverFlips()
        {
            var p = new PinchToggle();
            for (double t = 0; t < 8000; t += 20) p.Update(t, true, false);
            Assert.IsFalse(p.On);
        }

        // ---- HUD ------------------------------------------------------------------------------------------------------

        [Test]
        public void Hud_ChipsFollowConnectivity_SpectatorHiddenByDefault()
        {
            var m = new HudModel { Phase = PhPhase.Induction, Condition = PhCondition.Async, RemainingS = 62.2, StrokeCount = 12 };
            Assert.IsTrue(m.SleeveOffline); Assert.IsTrue(m.EmgOffline);
            m.HapticConnected = true;
            Assert.IsFalse(m.SleeveOffline); Assert.IsTrue(m.EmgOffline);
            Assert.AreEqual("", m.SpectatorText, "the participant must not see the condition by default");
            Assert.AreEqual("1:03 left", m.TimeLeftText);
            m.SpectatorVisible = true;
            StringAssert.Contains("ASYNC", m.SpectatorText);
        }

        [Test]
        public void Hud_NoTimeShownWhenIdleOrWitness()
        {
            var m = new HudModel { Phase = PhPhase.Witness, RemainingS = 30 };
            Assert.AreEqual("", m.TimeLeftText);
        }

        // ---- strings --------------------------------------------------------------------------------------------------

        [Test]
        public void Strings_EveryKeyHasEnglishAndHindi()
        {
            foreach (var k in PhStrings.Keys)
            {
                string en = PhStrings.Get(k, PhStrings.En), hi = PhStrings.Get(k, PhStrings.Hi);
                Assert.IsNotEmpty(en, k); Assert.IsNotEmpty(hi, k);
                Assert.IsTrue(hi.Any(ch => ch >= 'ऀ' && ch <= 'ॿ'), "Hindi text for " + k + " has no Devanagari");
            }
            Assert.AreEqual("nope", PhStrings.Get("nope", "en"));
        }

        [Test]
        public void Strings_PhaseWordExistsForEveryPhase()
        {
            foreach (PhPhase p in System.Enum.GetValues(typeof(PhPhase)))
            {
                Assert.IsNotEmpty(PhStrings.PhaseWord(p, "en"), p.ToString());
                Assert.IsNotEmpty(PhStrings.PhaseWord(p, "hi"), p.ToString());
            }
        }

        [Test]
        public void Strings_ProbeInstructionIsThePrdWording()
        {
            Assert.AreEqual("Keep your right hand still. With your left index, point above the ruler to where you feel your right index is, then hold still.",
                PhStrings.ProbeInstruction("en"));
        }

        // ---- witness --------------------------------------------------------------------------------------------------

        public static WitnessSummary Scripted(bool q4)
        {
            var sync = new ConditionResult { Condition = PhCondition.Sync, PreDriftCm = 0.5, PostDriftCm = 4.1, Ownership = 2.0, Awareness = q4 ? (double?)2 : null,
                Threat = new ThreatResponse { EmgPeakX = 4.6, EmgLatencyMs = 180 } };
            var async = new ConditionResult { Condition = PhCondition.Async, PreDriftCm = 0.2, PostDriftCm = 0.6, Ownership = -1.5, Awareness = q4 ? (double?)2 : null,
                Threat = new ThreatResponse { EmgPeakX = 1.8, EmgLatencyMs = 330 } };
            return WitnessSummary.Build(sync, async);
        }

        [Test]
        public void Witness_ShowsTheScriptedRunsNumbers()
        {
            var v = WitnessView.Build(Scripted(false), "en");
            Assert.AreEqual("Preliminary", v.PreliminaryLabel);
            var d = v.Sync.Entry("drift_change_cm");
            Assert.AreEqual(WitnessEntryKind.Arrow, d.Kind);
            Assert.AreEqual("+3.6 cm", d.ValueText); Assert.AreEqual(1, d.Direction);
            Assert.AreEqual("+0.4 cm", v.Async.Entry("drift_change_cm").ValueText);
            Assert.AreEqual("180 ms", v.Sync.Entry("flinch_latency_ms").ValueText);
            Assert.AreEqual("330 ms", v.Async.Entry("flinch_latency_ms").ValueText);
            Assert.AreEqual("Strong", v.Sync.Entry("flinch_strength").ValueText);
            Assert.AreEqual("Weak", v.Async.Entry("flinch_strength").ValueText);
            Assert.AreEqual(5.0 / 6.0, v.Sync.Entry("ownership").Fraction, 1e-9);
            Assert.AreEqual(1.5 / 6.0, v.Async.Entry("ownership").Fraction, 1e-9);
            Assert.IsNull(v.Sync.Entry("q4"), "no q4 row unless the summary has one");
            Assert.AreEqual(WitnessSummary.ClosingEn, v.ClosingLine);
        }

        [Test]
        public void Witness_Q4RowIsAPointerNotProof()
        {
            var v = WitnessView.Build(Scripted(true), "en");
            var q4 = v.Sync.Entry("q4");
            Assert.IsNotNull(q4); Assert.IsTrue(q4.Pointer);
            Assert.AreEqual("A pointer, not proof", q4.PointerText);
            Assert.IsNotNull(v.Async.Entry("q4"));
        }

        [Test]
        public void Witness_MissingValues_SayNoData_AndHindiLabelsUsed()
        {
            var s = WitnessSummary.Build(new ConditionResult { Condition = PhCondition.Sync }, new ConditionResult { Condition = PhCondition.Async });
            var v = WitnessView.Build(s, "hi");
            Assert.IsFalse(v.Sync.Entry("drift_change_cm").HasData);
            Assert.AreEqual(PhStrings.Get("w_nodata", "hi"), v.Sync.Entry("drift_change_cm").ValueText);
            Assert.AreEqual(WitnessSummary.ClosingHi, v.ClosingLine);
            Assert.AreEqual(s.Row("ownership").LabelHi, v.Sync.Entry("ownership").Label);
        }

        [Test]
        public void Witness_ClosingLineFadesInAfter3s()
        {
            Assert.AreEqual(0.0, WitnessView.ClosingAlpha(0), 1e-9);
            Assert.AreEqual(0.0, WitnessView.ClosingAlpha(2.9), 1e-9);
            Assert.AreEqual(0.5, WitnessView.ClosingAlpha(3.5), 1e-9);
            Assert.AreEqual(1.0, WitnessView.ClosingAlpha(4.5), 1e-9);
        }
    }
}
