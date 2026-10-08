using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Opus.Sdk
{
    /// <summary>
    /// One JSON text frame of OPUS Live Protocol v1 (contracts/LIVE_PROTOCOL.md,
    /// contracts/schemas/live-message.schema.json). Mirrors the schema's top-level shape exactly;
    /// `Payload` is a raw JObject so every message `type`'s payload shape (defined per-type in the schema's
    /// `allOf`) can be built/read without a matching C# class for each one.
    /// </summary>
    public sealed class LiveMessage
    {
        [JsonProperty("v")] public int V = 1;
        [JsonProperty("type")] public string Type;
        [JsonProperty("id")] public string Id = Guid.NewGuid().ToString();
        [JsonProperty("seq")] public int Seq;
        [JsonProperty("ts_ms")] public double TsMs;
        [JsonProperty("from")] public string From;
        [JsonProperty("session_id", NullValueHandling = NullValueHandling.Ignore)] public string SessionId;
        [JsonProperty("requires_ack", NullValueHandling = NullValueHandling.Ignore)] public bool? RequiresAck;
        [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)] public JObject Payload;

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

        public static LiveMessage FromJson(string json) => JsonConvert.DeserializeObject<LiveMessage>(json);

        /// <summary>Builds a message with the connection-scoped fields (seq/ts_ms/from) filled in by
        /// <see cref="LiveMessageFactory"/>, so callers only ever specify type/session/payload.</summary>
        public static LiveMessage Of(string type, JObject payload = null, string sessionId = null, bool requiresAck = false)
            => new LiveMessage { Type = type, Payload = payload, SessionId = sessionId, RequiresAck = requiresAck ? (bool?)true : null };
    }

    /// <summary>Per-connection seq counter + wall-clock stamping, shared by every message a <see cref="LiveClient"/>
    /// sends (contracts/LIVE_PROTOCOL.md: "seq: per-sender monotonic counter for this connection-session").</summary>
    public sealed class LiveMessageFactory
    {
        private readonly string _from;
        private int _nextSeq;

        public LiveMessageFactory(string from, int startSeq = 0)
        {
            _from = from;
            _nextSeq = startSeq;
        }

        public LiveMessage Build(string type, JObject payload = null, string sessionId = null, bool requiresAck = false)
        {
            var msg = LiveMessage.Of(type, payload, sessionId, requiresAck);
            msg.From = _from;
            msg.Seq = _nextSeq++;
            msg.TsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return msg;
        }
    }

    /// <summary>
    /// In-memory outbox for the Live Protocol connection (distinct from <see cref="Outbox"/>, which is the
    /// file-upload queue): keeps every `requires_ack` message until it is acked, plus every `trial_event`
    /// sent since the last `hello_ack`, so a reconnect can replay everything the peer's `resume_from_seq`
    /// says it hasn't seen yet (contracts/LIVE_PROTOCOL.md "Resume"). De-dup on the receive side is by
    /// message `id`; this class is the send side's memory of what to replay.
    /// </summary>
    public sealed class LiveMessageOutbox
    {
        private readonly List<LiveMessage> _unacked = new List<LiveMessage>();
        private readonly Dictionary<string, double> _sentAtMs = new Dictionary<string, double>();

        public void Track(LiveMessage msg, double nowMs)
        {
            _unacked.Add(msg);
            _sentAtMs[msg.Id] = nowMs;
        }

        public void Ack(string messageId)
        {
            _unacked.RemoveAll(m => m.Id == messageId);
            _sentAtMs.Remove(messageId);
        }

        /// <summary>`requires_ack` messages still unacked after <paramref name="timeoutMs"/> (default 1000ms per protocol) —
        /// caller should resend these. Messages that do not require an ack (trial_events) are kept for
        /// <see cref="ReplayFrom"/> on a reconnect only: they are never returned here, however old, so a timer never re-sends them.
        /// <paramref name="nowMs"/> must be in the same time base as the <c>nowMs</c> given to <see cref="Track"/> and <see cref="MarkResent"/>.</summary>
        public IReadOnlyList<LiveMessage> TimedOut(double nowMs, double timeoutMs = 1000.0)
            => _unacked.Where(m => m.RequiresAck == true && _sentAtMs.TryGetValue(m.Id, out var t) && nowMs - t >= timeoutMs).ToList();

        public void MarkResent(string messageId, double nowMs) => _sentAtMs[messageId] = nowMs;

        /// <summary>Everything the peer hasn't confirmed with seq greater than <paramref name="resumeFromSeq"/>
        /// (null/absent = replay everything currently outstanding).</summary>
        public IReadOnlyList<LiveMessage> ReplayFrom(int? resumeFromSeq)
            => _unacked.Where(m => resumeFromSeq == null || m.Seq > resumeFromSeq.Value)
                       .OrderBy(m => m.Seq).ToList();

        public int Count => _unacked.Count;
        public void Clear() { _unacked.Clear(); _sentAtMs.Clear(); }
    }

    /// <summary>De-dups inbound messages by <see cref="LiveMessage.Id"/> (contracts/LIVE_PROTOCOL.md: "Receivers
    /// de-dup by id"). Bounded so a long session doesn't grow this unboundedly.</summary>
    public sealed class LiveMessageDedup
    {
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Queue<string> _order = new Queue<string>();
        private readonly int _capacity;

        public LiveMessageDedup(int capacity = 4096) => _capacity = capacity;

        /// <returns>true if this id was already seen (caller should drop the message).</returns>
        public bool IsDuplicate(string id)
        {
            if (_seen.Contains(id)) return true;
            _seen.Add(id);
            _order.Enqueue(id);
            while (_order.Count > _capacity) _seen.Remove(_order.Dequeue());
            return false;
        }
    }
}
