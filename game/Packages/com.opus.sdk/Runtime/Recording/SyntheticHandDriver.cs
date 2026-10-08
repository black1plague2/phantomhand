using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Opus.Sdk
{
    /// <summary>
    /// Replays previously-recorded (or synthetically generated, see `sim/`) kinematics-chunk-shaped frames as
    /// an <see cref="IHandSource"/>, so the full pipeline (KinematicsSampler -> KinematicsRecorder -> game
    /// module) runs completely hands-free in Editor/CI. This is the "same IHand abstraction" the brief calls
    /// for: because the whole SDK only ever talks to <see cref="IHandSource"/>, swapping this in for a live
    /// Meta adapter is invisible to every recorder and game.
    ///
    /// Note (docs/UNITY_PRACTICES.md §6): a from-scratch drop-in ISDK <c>DataModifier&lt;HandDataAsset&gt;</c>
    /// that injects into the live Interaction SDK hand pipeline (so real HandGrabInteractors also react to the
    /// replay) needs a connected Interaction SDK to compile/verify against and is deferred — this driver
    /// covers the recording-and-game-logic replay path (KinematicsSampler -> games), which is what U5's
    /// acceptance test (schema-valid session dir from a replayed fixture) actually requires.
    /// </summary>
    public sealed class SyntheticHandDriver : IHandSource
    {
        private readonly List<KinematicsChunk> _chunks;
        private int _frameCursor = -1;
        private double[] _lastT;
        private readonly Dictionary<string, JointSeries> _joined = new Dictionary<string, JointSeries>();
        private int _leftVersion, _rightVersion;

        public SyntheticHandDriver(IEnumerable<string> chunkJsonFiles)
        {
            _chunks = chunkJsonFiles
                .Select(f => JsonConvert.DeserializeObject<KinematicsChunk>(System.IO.File.ReadAllText(f)))
                .OrderBy(c => c.Seq)
                .ToList();

            var allT = new List<double>();
            foreach (var chunk in _chunks)
            {
                foreach (var joint in chunk.Joints)
                {
                    if (!_joined.TryGetValue(joint, out var series))
                    {
                        series = new JointSeries { Rot = chunk.Frames[joint].Rot != null ? new List<double[]>() : null };
                        _joined[joint] = series;
                    }
                    var src = chunk.Frames[joint];
                    series.Pos.AddRange(src.Pos);
                    series.Conf.AddRange(src.Conf);
                    if (series.Rot != null && src.Rot != null) series.Rot.AddRange(src.Rot);
                }
                allT.AddRange(chunk.TMs);
            }
            _lastT = allT.ToArray();
        }

        public int FrameCount => _lastT.Length;
        public bool Finished => _frameCursor >= FrameCount - 1;

        /// <summary>Advance to the next recorded frame. Call once per KinematicsSampler.SampleFrame tick.</summary>
        public bool AdvanceFrame()
        {
            if (_frameCursor >= FrameCount - 1) return false;
            _frameCursor++;
            _leftVersion++;
            _rightVersion++;
            return true;
        }

        public bool TryGetJointPose(string joint, out double[] posMeters, out double[] rotQuatXyzw)
        {
            posMeters = null; rotQuatXyzw = null;
            if (_frameCursor < 0 || !_joined.TryGetValue(joint, out var series)) return false;
            if (_frameCursor >= series.Pos.Count) return false;
            double conf = series.Conf[_frameCursor];
            if (conf <= 0) return false;
            posMeters = series.Pos[_frameCursor];
            rotQuatXyzw = series.Rot != null && _frameCursor < series.Rot.Count ? series.Rot[_frameCursor] : null;
            return true;
        }

        public TrackingConfidence GetConfidence(HandSide side)
        {
            string joint = side == HandSide.Left ? OpusJoints.LWrist : OpusJoints.RWrist;
            if (!_joined.TryGetValue(joint, out var series) || _frameCursor < 0 || _frameCursor >= series.Conf.Count)
                return TrackingConfidence.None;
            double c = series.Conf[_frameCursor];
            return c >= 0.99 ? TrackingConfidence.High : c > 0 ? TrackingConfidence.Low : TrackingConfidence.None;
        }

        public bool IsTracked(HandSide side) => GetConfidence(side) != TrackingConfidence.None;

        public float GetPinchStrength(HandSide side)
        {
            // Fixtures don't carry a pinch signal; approximate from index-tip/thumb-tip distance if both tracked.
            string prefix = side == HandSide.Left ? "l_" : "r_";
            if (!TryGetJointPose(prefix + "index_tip", out var idx, out _) || !TryGetJointPose(prefix + "thumb_tip", out var thumb, out _))
                return 0f;
            double dx = idx[0] - thumb[0], dy = idx[1] - thumb[1], dz = idx[2] - thumb[2];
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            const double pinchDistM = 0.02, openDistM = 0.10;
            double t = 1.0 - (dist - pinchDistM) / (openDistM - pinchDistM);
            return (float)Math.Clamp(t, 0.0, 1.0);
        }

        public int GetDataVersion(HandSide side) => side == HandSide.Left ? _leftVersion : _rightVersion;

        public double CurrentTMs => _frameCursor >= 0 && _frameCursor < _lastT.Length ? _lastT[_frameCursor] : 0;
    }
}
