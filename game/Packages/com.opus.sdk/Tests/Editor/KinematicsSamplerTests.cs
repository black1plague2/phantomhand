using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    public class KinematicsSamplerTests
    {
        private sealed class ScriptedHandSource : IHandSource
        {
            public bool Tracked = true;
            public TrackingConfidence Confidence = TrackingConfidence.High;
            public float Pinch = 0f;

            public bool TryGetJointPose(string joint, out double[] pos, out double[] rot)
            {
                pos = new double[] { 1, 2, 3 };
                rot = new double[] { 0, 0, 0, 1 };
                return Tracked;
            }
            public TrackingConfidence GetConfidence(HandSide side) => Confidence;
            public bool IsTracked(HandSide side) => Tracked;
            public float GetPinchStrength(HandSide side) => Pinch;
            private int _version;
            public int GetDataVersion(HandSide side) => Tracked ? ++_version : _version;
        }

        [Test]
        public void SamplesEveryJointEachFrame()
        {
            var clock = SessionClock.Manual();
            var recorder = new KinematicsRecorder("s1", clock, sessionDir: null, rateHz: 90);
            var hand = new ScriptedHandSource();
            var sampler = new KinematicsSampler(recorder, hand);

            clock.Advance(11.1);
            sampler.SampleFrame(clock.NowMs);
            recorder.Flush();

            Assert.AreEqual(OpusJoints.All.Length, recorder.WrittenChunks[0].Joints.Count);
            foreach (var joint in OpusJoints.All)
                Assert.AreEqual(1, recorder.WrittenChunks[0].Frames[joint].Conf.Count);
        }

        [Test]
        public void UntrackedJoint_RecordsZeroConfidence()
        {
            var clock = SessionClock.Manual();
            var recorder = new KinematicsRecorder("s1", clock, sessionDir: null, rateHz: 90);
            var hand = new ScriptedHandSource { Tracked = false, Confidence = TrackingConfidence.None };
            var sampler = new KinematicsSampler(recorder, hand);

            clock.Advance(11.1);
            sampler.SampleFrame(clock.NowMs);
            recorder.Flush();

            Assert.AreEqual(0.0, recorder.WrittenChunks[0].Frames[OpusJoints.LWrist].Conf[0]);
        }

        [Test]
        public void TrackingLost_And_Regained_RaiseTransitionOnce()
        {
            // ScriptedHandSource.Tracked is a single flag shared by both hands (IsTracked ignores `side`), so a
            // real transition here flips BOTH HandSide.Left and HandSide.Right at once -> one event per side per
            // state change. This test asserts the "once per actual state change" guarantee (KinematicsSampler
            // must not re-raise while the state is unchanged across frames) by looking at one side's events only.
            var clock = SessionClock.Manual();
            var recorder = new KinematicsRecorder("s1", clock, sessionDir: null, rateHz: 90);
            var hand = new ScriptedHandSource { Tracked = true };
            var sampler = new KinematicsSampler(recorder, hand);

            var transitions = new System.Collections.Generic.List<(HandSide, bool)>();
            sampler.OnTrackingChanged += (side, tracked) => transitions.Add((side, tracked));

            clock.Advance(11.1); sampler.SampleFrame(clock.NowMs); // establishes baseline, no transition
            clock.Advance(11.1); sampler.SampleFrame(clock.NowMs); // still tracked, no transition
            Assert.AreEqual(0, transitions.Count);

            hand.Tracked = false;
            clock.Advance(11.1); sampler.SampleFrame(clock.NowMs); // lost
            clock.Advance(11.1); sampler.SampleFrame(clock.NowMs); // still lost -> must NOT re-raise
            var rightTransitions = transitions.FindAll(t => t.Item1 == HandSide.Right);
            Assert.AreEqual(1, rightTransitions.Count, "must raise exactly once per side on loss, not once per frame");
            Assert.IsFalse(rightTransitions[0].Item2);
            Assert.AreEqual(2, transitions.Count, "both sides transition together with this fixture's shared Tracked flag");

            hand.Tracked = true;
            clock.Advance(11.1); sampler.SampleFrame(clock.NowMs); // regained
            rightTransitions = transitions.FindAll(t => t.Item1 == HandSide.Right);
            Assert.AreEqual(2, rightTransitions.Count);
            Assert.IsTrue(rightTransitions[1].Item2);
            Assert.AreEqual(4, transitions.Count);
        }
    }
}
