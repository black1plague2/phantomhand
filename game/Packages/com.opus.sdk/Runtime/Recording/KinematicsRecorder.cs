using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Opus.Sdk
{
    /// <summary>
    /// Buffers per-joint pose samples and flushes them as 5 s columnar chunks (kin_###.json) matching
    /// kinematics-chunk.schema.json. Sampling itself (reading joints from an <see cref="IHandSource"/> each
    /// frame — see <see cref="KinematicsSampler"/>) is the caller's job, kept out of this class so it stays
    /// testable without Meta XR/OVR — call <see cref="Sample"/> per joint per frame.
    /// </summary>
    public sealed class KinematicsRecorder
    {
        public const double ChunkDurationMs = 5000.0;

        private readonly string _sessionId;
        private readonly SessionClock _clock;
        private readonly string _sessionDir;
        private readonly double _rateHz;
        private readonly string _source;

        private KinematicsChunk _current;
        private double _chunkStartMs;
        private int _nextChunkSeq;
        /// <summary>Mid-run chunk rollovers serialise and write the finished chunk on a background thread, in order. A 5 s chunk is about 150 KB of JSON and cost
        /// 150-400 ms of main-thread time in the editor: frames froze every 5 s and stroke cues due in that window went out late. <see cref="Flush"/> still
        /// returns only when every file is on disk. Off by default, so tests and older callers see each file at once.</summary>
        public bool BackgroundWrites;
        private Task _pendingWrite = Task.CompletedTask;
        private volatile string _writeError;
        public int ChunksWritten { get; private set; }
        public IReadOnlyList<KinematicsChunk> WrittenChunks => _writtenInMemory;
        private readonly List<KinematicsChunk> _writtenInMemory = new List<KinematicsChunk>();

        public KinematicsRecorder(string sessionId, SessionClock clock, string sessionDir, double rateHz, string source = "xr_hands", int startChunkSeq = 0)
        {
            _sessionId = sessionId;
            _clock = clock;
            _sessionDir = sessionDir;
            _rateHz = rateHz;
            _source = source;
            _nextChunkSeq = startChunkSeq;
            StartNewChunk();
        }

        private void StartNewChunk()
        {
            _current = new KinematicsChunk
            {
                SessionId = _sessionId,
                Seq = _nextChunkSeq,
                RateHz = _rateHz,
                Source = _source,
                Joints = new List<string>(),
            };
            _chunkStartMs = _clock.NowMs;
        }

        /// <summary>Record one joint's pose for the current frame. Call once per tracked joint per sampled frame.
        /// `frameTMs` should be identical for every joint captured in the same frame so t_ms stays a single shared timeline.
        ///
        /// Run16 (found by running real analytics over a real recorded session, not by schema validation): the
        /// chunk format is COLUMNAR and kinematics-chunk.schema.json states the invariant explicitly --
        /// "each array length == t_ms length. conf 0 = not tracked." The previous implementation appended to
        /// t_ms once per distinct timestamp but appended to a joint's pos/conf only on the frames where that
        /// joint was actually sampled. A caller that legitimately skips an untracked joint (which
        /// OrchardReachSceneController.TrySampleJoint does by design, to avoid writing a fake pose at the
        /// origin) therefore left that joint's arrays permanently SHORTER than, and misaligned with, t_ms --
        /// a joint first seen on frame 30 had its 1st sample attributed to t_ms[0]. Measured on a real
        /// session (cb77b474): t_ms=157 while r_wrist had 28 entries. Consequences: analytics crashed with
        /// IndexError, and where it did not crash every velocity/SPARC value after the first dropout was
        /// computed against the wrong timestamps. JSON Schema cannot express a cross-array length equality,
        /// so this passed contracts/validate.py every time -- the same class of "shape, not meaning" defect
        /// as the five recorded in CONTEXT.md.
        ///
        /// Fixed at the source: t_ms is the authoritative frame axis, and EVERY joint series is kept exactly
        /// parallel to it. A frame a joint missed is padded with conf 0 (the schema's own encoding for "not
        /// tracked"), carrying the last known position forward purely as a placeholder -- conf 0 is what
        /// analytics gates on, so a padded sample is never read as a real pose.</summary>
        public void Sample(double frameTMs, string joint, double[] pos, double[] rot, double conf)
        {
            // A new timestamp opens a new frame: close out the previous one by padding every joint that did
            // not report during it, so all series are exactly t_ms-length before the new frame is appended.
            bool newFrame = _current.TMs.Count == 0 || _current.TMs[_current.TMs.Count - 1] != frameTMs;
            if (newFrame)
            {
                PadAllSeriesTo(_current.TMs.Count);
                _current.TMs.Add(frameTMs);
            }

            if (!_current.Frames.TryGetValue(joint, out var series))
            {
                series = new JointSeries();
                if (rot != null) series.Rot = new List<double[]>();
                _current.Frames[joint] = series;
                _current.Joints.Add(joint);
                // A joint discovered mid-chunk (hand came into view late) must be back-filled for every frame
                // before this one, or its first real sample silently lands at t_ms[0].
                PadSeriesTo(series, _current.TMs.Count - 1);
            }

            int frameIndex = _current.TMs.Count - 1;
            PadSeriesTo(series, frameIndex); // frames this joint missed since it last reported
            if (series.Pos.Count == frameIndex + 1)
            {
                // Same joint sampled twice within one frame: last write wins rather than desynchronising.
                series.Pos[frameIndex] = pos;
                series.Conf[frameIndex] = conf;
                if (series.Rot != null && rot != null) series.Rot[frameIndex] = rot;
            }
            else
            {
                series.Pos.Add(pos);
                series.Conf.Add(conf);
                if (series.Rot != null) series.Rot.Add(rot ?? LastOr(series.Rot, IdentityRot));
            }

            if (_clock.NowMs - _chunkStartMs >= ChunkDurationMs)
                Roll();
        }

        private static readonly double[] IdentityRot = { 0, 0, 0, 1 };

        private static double[] LastOr(List<double[]> list, double[] fallback)
            => list != null && list.Count > 0 ? list[list.Count - 1] : fallback;

        /// <summary>Pad one series up to <paramref name="length"/> frames with conf 0 ("not tracked") entries,
        /// repeating the last known pose as an inert placeholder.</summary>
        private static void PadSeriesTo(JointSeries series, int length)
        {
            while (series.Pos.Count < length)
            {
                series.Pos.Add(LastOr(series.Pos, new double[] { 0, 0, 0 }));
                series.Conf.Add(0.0);
                if (series.Rot != null) series.Rot.Add(LastOr(series.Rot, IdentityRot));
            }
        }

        private void PadAllSeriesTo(int length)
        {
            foreach (var series in _current.Frames.Values) PadSeriesTo(series, length);
        }

        /// <summary>Force-write the current chunk (call at block/session end even if under 5 s).</summary>
        public void Flush() { Roll(); Drain(); }

        /// <summary>Waits for the background writes and reports a failed one (a lost chunk must not pass silently).</summary>
        private void Drain()
        {
            _pendingWrite.Wait(10000);
            if (_writeError == null) return;
            string e = _writeError; _writeError = null;
            throw new IOException(e);
        }

        /// <summary>Closes the current chunk and starts the next one; the file is written here, or queued when <see cref="BackgroundWrites"/> is on.</summary>
        private void Roll()
        {
            if (_current.TMs.Count == 0) { return; } // nothing sampled this chunk; don't emit an empty file

            // The final frame's stragglers: any joint that did not report on the last sampled frame.
            PadAllSeriesTo(_current.TMs.Count);

            if (!string.IsNullOrEmpty(_sessionDir))
            {
                Directory.CreateDirectory(_sessionDir);
                var path = Path.Combine(_sessionDir, $"kin_{_current.Seq:000}.json");
                var chunk = _current;      // complete: StartNewChunk replaces it below and nothing mutates it again
                if (BackgroundWrites) _pendingWrite = _pendingWrite.ContinueWith(_ => WriteFile(path, () => JsonConvert.SerializeObject(chunk, Formatting.None)), TaskScheduler.Default);
                else File.WriteAllText(path, JsonConvert.SerializeObject(chunk, Formatting.None));
            }
            _writtenInMemory.Add(_current);
            ChunksWritten++;
            _nextChunkSeq++;
            StartNewChunk();
        }

        private void WriteFile(string path, Func<string> json)
        {
            try { File.WriteAllText(path, json()); }
            catch (Exception e) { _writeError = path + ": " + e.Message; }
        }
    }
}
