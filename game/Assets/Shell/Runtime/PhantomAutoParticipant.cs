using System;
using Opus.Games.PhantomHand;
using Opus.Sdk;

namespace Opus.Shell
{
    /// <summary>
    /// A scripted participant for runs without a person (editor / batch PlayMode, the L3 harness): it moves a <see cref="ScriptedHands"/> the
    /// way a cooperative person would and answers the questionnaire. It does NOT touch the module's results directly, only the hand poses
    /// and the same <c>Submit*</c> calls the UI makes, so the whole chain (probe maths, calibration, events, recorders, live link) is
    /// exercised. Deterministic: no randomness. A full participant model (S3 / U8) replaces this later.
    ///  - Calibrate: the right wrist rests at <see cref="CalibrationTarget"/> (the U4 presenter holds it for 2 s and commits); when no UI
    ///    presenter exists (<see cref="SubmitCalibrationItself"/>) it submits the calibration itself after 2.2 s.
    ///  - Probes: the left index tip points still at the right index tip, shifted toward the virtual hand by <see cref="SyncDriftCm"/> /
    ///    <see cref="AsyncDriftCm"/> in ProbePost (so sync drifts more than async, the expected direction).
    ///  - Questionnaire: ownership high after SYNC, low after ASYNC. With a panel (<see cref="AnswerPoint"/>) the pointing
    ///    fingertip goes to the button and stays until the panel has taken the answer, then leaves it before the next item, the way
    ///    a person does; without one, one item every <see cref="AnswerEveryMs"/> straight into the module.
    /// </summary>
    public sealed class PhantomAutoParticipant
    {
        public readonly ScriptedHands Hands;
        public Func<double[]> CalibrationTarget;
        public bool SubmitCalibrationItself;
        public double AnswerEveryMs = 700;
        public double SyncDriftCm = 3.0, AsyncDriftCm = 1.0;
        public int Answered { get; private set; }
        /// <summary>Where a fingertip answers <c>value</c> (-3..3) on the questionnaire panel, world metres; null (or a null result)
        /// when there is no panel to touch.</summary>
        public Func<int, double[]> AnswerPoint;
        /// <summary>How long the fingertip stays away from the panel between two items.</summary>
        public double LeaveMs = 350;

        private PhPhase _phase = PhPhase.Idle;
        private double _phaseStartMs;
        private double _nextAnswerMs;
        private bool _calibSent;
        private string _touchingItem;      // the item the fingertip is answering
        private double _awayUntilMs;

        public PhantomAutoParticipant(ScriptedHands hands) { Hands = hands; }

        public void Tick(double nowMs, PhantomHandModule m)
        {
            Hands.Step();
            if (m == null) return;
            var phase = m.CurrentPhase;
            if (phase != _phase)
            {
                if (_phase == PhPhase.Questionnaire && _touchingItem != null) Answered++;   // the last item's answer ended the phase
                _phase = phase; _phaseStartMs = nowMs; _nextAnswerMs = nowMs + AnswerEveryMs; _calibSent = false;
                _touchingItem = null; _awayUntilMs = 0;
            }

            var target = CalibrationTarget != null ? CalibrationTarget() : null;
            if (target != null) Hands.RightWrist = new[] { target[0], target[1], target[2] };
            var tip = Hands.RightIndexTip;

            switch (phase)
            {
                case PhPhase.ProbePre:
                case PhPhase.ProbePost:
                {
                    double driftCm = 0;
                    if (phase == PhPhase.ProbePost) driftCm = m.CurrentCondition == PhCondition.Sync ? SyncDriftCm : AsyncDriftCm;
                    // positive drift = perceived position toward the virtual hand: toward -x for a right arm (the virtual arm is to its left), +x for a left arm
                    Hands.LeftIndexTip = new[] { tip[0] + (Hands.Arm == HandSide.Left ? 1 : -1) * driftCm / 100.0, tip[1] + 0.10, 0.35 };
                    break;
                }
                case PhPhase.Calibrate:
                    if (SubmitCalibrationItself && !_calibSent && nowMs - _phaseStartMs >= 2200 && Hands.RightWrist != null)
                    {
                        _calibSent = true;
                        m.SubmitCalibration((double[])Hands.RightWrist.Clone(), (double[])Hands.RightAxis.Clone(), true);
                    }
                    Hands.LeftIndexTip = new[] { PhArm.X(Hands.Arm, -0.25), 0.90, 0.30 };
                    break;
                case PhPhase.Questionnaire:
                {
                    var q = m.CurrentQuestionnaire;
                    var item = q != null && !q.IsComplete ? q.Current : null;
                    int v = 0;
                    if (item != null && item.Role == QRole.Ownership) v = m.CurrentCondition == PhCondition.Sync ? 2 : -1;
                    double[] at = item != null && AnswerPoint != null ? AnswerPoint(v) : null;
                    if (at != null)
                    {
                        // by fingertip: a new item means the last one was taken, so leave the panel for a moment first
                        if (_touchingItem != item.Id)
                        {
                            if (_touchingItem != null) { Answered++; _awayUntilMs = nowMs + LeaveMs; }
                            _touchingItem = item.Id;
                        }
                        Hands.LeftIndexTip = nowMs < _awayUntilMs ? new[] { at[0], at[1] + 0.12, at[2] - 0.15 } : at;
                    }
                    else if (item != null && nowMs >= _nextAnswerMs)
                    {
                        _nextAnswerMs = nowMs + AnswerEveryMs;
                        if (m.SubmitQuestionnaireAnswer(v)) Answered++;
                    }
                    else if (item == null && _touchingItem != null) { Answered++; _touchingItem = null; }
                    break;
                }
                default:
                    Hands.LeftIndexTip = new[] { PhArm.X(Hands.Arm, -0.25), 0.90, 0.30 };
                    break;
            }
        }
    }
}
