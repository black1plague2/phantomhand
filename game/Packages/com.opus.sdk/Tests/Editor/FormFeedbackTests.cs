using System.Collections.Generic;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>Run9 step 2: threshold/hysteresis logic for FormFeedback. Plain C#, no XR — verifies the
    /// >300ms lean debounce + clear-on-return, and the >500ms tracking debounce, independent of any Unity
    /// Transform/head-pose geometry (the caller supplies the already-computed lean distance).</summary>
    public class FormFeedbackTests
    {
        [Test]
        public void LeanBelowThreshold_NeverFires()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;

            for (double t = 0; t <= 1000; t += 50) ff.Tick(t, headLeanTowardTargetCm: 5, trackingConfidenceLow: false, trackingLost: false);

            Assert.IsEmpty(signals);
            Assert.IsFalse(ff.LeanWarningActive);
        }

        [Test]
        public void LeanAboveThreshold_FiresOnlyAfter300ms()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;

            ff.Tick(0, 10, false, false);
            Assert.IsEmpty(signals, "must not fire immediately on the first exceeding frame");

            ff.Tick(299, 10, false, false);
            Assert.IsEmpty(signals, "must not fire before the 300ms debounce elapses");

            ff.Tick(301, 10, false, false);
            Assert.AreEqual(1, signals.Count);
            Assert.AreEqual("trunk_lean", signals[0].Kind);
            Assert.IsTrue(signals[0].Active);
            Assert.AreEqual(10, signals[0].LeanCm);
            Assert.IsTrue(ff.LeanWarningActive);
        }

        [Test]
        public void LeanWarning_DoesNotRefireWhileStillActive()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;

            ff.Tick(0, 10, false, false);
            ff.Tick(301, 10, false, false);
            ff.Tick(500, 12, false, false);
            ff.Tick(1000, 15, false, false);

            Assert.AreEqual(1, signals.Count, "should fire once on the rising edge, not every frame while active");
        }

        [Test]
        public void LeanWarning_ClearsWhenLeanReturnsAtOrBelowThreshold()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;

            ff.Tick(0, 10, false, false);
            ff.Tick(301, 10, false, false); // active
            ff.Tick(400, 8, false, false);  // <= threshold -> clears immediately (no debounce on clear)

            Assert.AreEqual(2, signals.Count);
            Assert.IsFalse(signals[1].Active);
            Assert.IsFalse(ff.LeanWarningActive);
        }

        [Test]
        public void LeanExceedanceInterrupted_ResetsDebounceTimer()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;

            ff.Tick(0, 10, false, false);   // start exceeding
            ff.Tick(250, 5, false, false);  // dips back under threshold before 300ms — must reset the timer
            ff.Tick(260, 10, false, false); // exceeding again, this is a NEW debounce window
            ff.Tick(500, 10, false, false); // only 240ms since the new window started

            Assert.IsEmpty(signals, "an interrupted exceedance must restart the 300ms window, not resume it");
        }

        [Test]
        public void TrackingLost_FiresAfter500msAndClearsOnRecovery()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;

            ff.Tick(0, 0, false, trackingLost: true);
            ff.Tick(499, 0, false, trackingLost: true);
            Assert.IsEmpty(signals);

            ff.Tick(501, 0, false, trackingLost: true);
            Assert.AreEqual(1, signals.Count);
            Assert.AreEqual("low_confidence", signals[0].Kind);
            Assert.IsTrue(signals[0].Active);

            ff.Tick(600, 0, false, trackingLost: false);
            Assert.AreEqual(2, signals.Count);
            Assert.IsFalse(signals[1].Active);
        }

        [Test]
        public void LowConfidence_AloneAlsoTriggersTheSameCue()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;

            ff.Tick(0, 0, trackingConfidenceLow: true, trackingLost: false);
            ff.Tick(501, 0, trackingConfidenceLow: true, trackingLost: false);

            Assert.AreEqual(1, signals.Count);
            Assert.AreEqual("low_confidence", signals[0].Kind);
        }

        [Test]
        public void Reset_ClearsAllDebounceAndActiveState()
        {
            var ff = new FormFeedback(trunkLeanWarningCm: 8);
            ff.Tick(0, 10, false, false);
            ff.Tick(301, 10, false, false);
            Assert.IsTrue(ff.LeanWarningActive);

            ff.Reset();
            Assert.IsFalse(ff.LeanWarningActive);
            Assert.IsFalse(ff.LowConfidenceWarningActive);

            // After reset, a fresh exceedance needs its own full 300ms window again.
            var signals = new List<FormWarningSignal>();
            ff.OnSignal += signals.Add;
            ff.Tick(1000, 10, false, false);
            ff.Tick(1200, 10, false, false); // only 200ms since the post-reset window started
            Assert.IsEmpty(signals);
        }
    }
}
