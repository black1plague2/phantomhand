using System;

namespace Opus.Sdk
{
    /// <summary>Fired when a form-feedback condition transitions active/inactive.</summary>
    public readonly struct FormWarningSignal
    {
        public readonly string Kind; // "trunk_lean" | "low_confidence"
        public readonly bool Active; // true = just started, false = just cleared
        public readonly double LeanCm; // meaningful for "trunk_lean" only
        public FormWarningSignal(string kind, bool active, double leanCm)
        {
            Kind = kind; Active = active; LeanCm = leanCm;
        }
    }

    /// <summary>
    /// Run9 step 2: trunk-lean + low-tracking-confidence form feedback. Plain C#, no XR/Unity dependency, so the
    /// threshold/hysteresis logic is unit-testable without a headset (per the brief: "EditMode tests for the
    /// threshold/hysteresis logic (plain C#, no XR)").
    ///
    /// Trunk lean: head displacement TOWARD the target, measured relative to the trial-start head pose (the
    /// caller supplies this distance in cm every frame — this class only owns the >threshold-for-N-ms debounce
    /// and the active/clear edge detection, not the geometry itself, so it has no Vector3/Transform dependency).
    /// Fires once on the rising edge (threshold exceeded continuously for &gt; <see cref="WarningDurationMs"/>),
    /// clears once the signal drops back at/under the threshold (edge-triggered, not re-fired every frame while
    /// active — callers needing "still active" state read <see cref="LeanWarningActive"/> directly).
    ///
    /// Tracking: fires the SAME event kind ("low_confidence") from either low tracking confidence OR an explicit
    /// tracking-lost flag, debounced by <see cref="TrackingLostDurationMs"/> — the two calling conditions from the
    /// brief ("low tracking confidence / tracking_lost &gt; 500ms") collapse into one boolean the caller computes
    /// (confidence-low OR lost), since both call for the identical clinical cue and both share one debounce window
    /// per HAPTIC_PROTOCOL.md's `low_confidence` cue definition.
    /// </summary>
    public sealed class FormFeedback
    {
        public double TrunkLeanWarningCm;
        public double WarningDurationMs = 300;
        public double TrackingLostDurationMs = 500;

        public bool LeanWarningActive { get; private set; }
        public bool LowConfidenceWarningActive { get; private set; }
        public double LastLeanCm { get; private set; }

        private double _leanExceedSinceMs = -1;
        private double _trackingBadSinceMs = -1;

        /// <summary>Raised exactly once per active/clear transition.</summary>
        public event Action<FormWarningSignal> OnSignal;

        public FormFeedback(double trunkLeanWarningCm) => TrunkLeanWarningCm = trunkLeanWarningCm;

        /// <param name="nowMs">Session clock time.</param>
        /// <param name="headLeanTowardTargetCm">Head displacement toward the target vs. the trial-start head
        /// pose, in cm (>=0; caller does the geometry/projection).</param>
        /// <param name="trackingConfidenceLow">True if the active hand's tracking confidence is Low/None.</param>
        /// <param name="trackingLost">True if tracking has been fully lost (no pose at all) this frame.</param>
        public void Tick(double nowMs, double headLeanTowardTargetCm, bool trackingConfidenceLow, bool trackingLost)
        {
            TickLean(nowMs, headLeanTowardTargetCm);
            TickTracking(nowMs, trackingConfidenceLow || trackingLost);
        }

        private void TickLean(double nowMs, double leanCm)
        {
            LastLeanCm = leanCm;
            bool exceeding = leanCm > TrunkLeanWarningCm;
            if (exceeding)
            {
                if (_leanExceedSinceMs < 0) _leanExceedSinceMs = nowMs;
                if (!LeanWarningActive && nowMs - _leanExceedSinceMs > WarningDurationMs)
                {
                    LeanWarningActive = true;
                    OnSignal?.Invoke(new FormWarningSignal("trunk_lean", true, leanCm));
                }
            }
            else
            {
                _leanExceedSinceMs = -1;
                if (LeanWarningActive)
                {
                    LeanWarningActive = false;
                    OnSignal?.Invoke(new FormWarningSignal("trunk_lean", false, leanCm));
                }
            }
        }

        private void TickTracking(double nowMs, bool bad)
        {
            if (bad)
            {
                if (_trackingBadSinceMs < 0) _trackingBadSinceMs = nowMs;
                if (!LowConfidenceWarningActive && nowMs - _trackingBadSinceMs > TrackingLostDurationMs)
                {
                    LowConfidenceWarningActive = true;
                    OnSignal?.Invoke(new FormWarningSignal("low_confidence", true, 0));
                }
            }
            else
            {
                _trackingBadSinceMs = -1;
                if (LowConfidenceWarningActive)
                {
                    LowConfidenceWarningActive = false;
                    OnSignal?.Invoke(new FormWarningSignal("low_confidence", false, 0));
                }
            }
        }

        /// <summary>Resets all debounce/active state — call at trial_start so a new trial's head-pose baseline
        /// doesn't inherit a stale exceed timer from the previous trial.</summary>
        public void Reset()
        {
            _leanExceedSinceMs = -1;
            _trackingBadSinceMs = -1;
            LeanWarningActive = false;
            LowConfidenceWarningActive = false;
            LastLeanCm = 0;
        }
    }
}
