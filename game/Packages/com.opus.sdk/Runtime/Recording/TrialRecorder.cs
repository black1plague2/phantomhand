using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Opus.Sdk
{
    /// <summary>
    /// Appends one JSON line per <see cref="TrialEvent"/> to `events.ndjson` under the session directory.
    /// Assigns the monotonically increasing `seq` and stamps `t_ms` from the shared <see cref="SessionClock"/>
    /// (so callers only need to supply block/trial/type/hand/target/outcome/data).
    /// Also keeps an in-memory log for tests / non-filesystem callers.
    /// </summary>
    public sealed class TrialRecorder
    {
        private readonly SessionClock _clock;
        private readonly string _path;
        private readonly List<TrialEvent> _log = new List<TrialEvent>();
        private int _nextSeq;
        private StreamWriter _writer;

        public IReadOnlyList<TrialEvent> Log => _log;
        public int NextSeq => _nextSeq;

        /// <param name="path">Full path to events.ndjson. Null/empty = memory-only (useful in EditMode tests).</param>
        /// <param name="startSeq">Resume point after a crash (Outbox reads the last seq already on disk).</param>
        public TrialRecorder(SessionClock clock, string path = null, int startSeq = 0)
        {
            _clock = clock;
            _path = path;
            _nextSeq = startSeq;
            if (!string.IsNullOrEmpty(_path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                _writer = new StreamWriter(_path, append: startSeq > 0) { AutoFlush = true };
            }
        }

        public TrialEvent Record(int block, int? trial, string type, string hand = null, TrialTarget target = null, string outcome = null, object data = null)
        {
            var evt = new TrialEvent
            {
                TMs = _clock.NowMs,
                Seq = _nextSeq++,
                Block = block,
                Trial = trial,
                Type = type,
                Hand = hand,
                Target = target,
                Outcome = outcome,
                Data = data,
            };
            _log.Add(evt);
            if (_writer != null)
            {
                _writer.WriteLine(JsonConvert.SerializeObject(evt));
            }
            return evt;
        }

        public void Close()
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
