using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Shell
{
    /// <summary>
    /// Turns the haptic client's raw cue records into the ONE final-state <c>haptic_cue</c> event per stroke cue that analytics counts
    /// (03-SPEC section 7: cue "stroke", motor, delivered, ack_latency_ms; analytics: delivery = delivered / scheduled per condition).
    ///
    /// <see cref="HapticClient"/> raises a record when a stroke is SENT (Delivered=false, SentMs set), again when its ack lands
    /// (Delivered=true, AckLatencyMs) or is rejected (Reason set), and once for a stroke that was dropped before sending (SentMs null,
    /// Reason late | disabled | no_device | motor_gap | rate_cap | cancelled). events.ndjson is append-only, so a cue is written when its
    /// outcome is known: at the ack, at the rejection, at the drop, or after <see cref="AckTimeoutMs"/> with no ack (delivered=false,
    /// reason "no_ack"). The condition index (event.trial) is the one current when the cue was SENT.
    /// Also keeps the motor-on windows (send to send + 250 ms) for the sensor files' <c>motor_excl</c> flags (FR-AN-01).
    /// Plain C#, main thread only.
    /// </summary>
    public sealed class HapticCueEventAdapter
    {
        public const double AckTimeoutMs = 1500;
        public const double MotorWindowMs = 250;

        private sealed class Pending { public int Motor; public double SentMs; public int? Trial; public string CueId; }

        private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        private readonly List<double[]> _windows = new List<double[]>(); // [from, to]

        /// <summary>Current condition index (0/1) or null; read when a cue is sent or dropped.</summary>
        public Func<int?> Trial { get; set; }

        /// <summary>Raised (main thread) with the finished haptic_cue event (TMs is the caller's to restamp).</summary>
        public event Action<TrialEvent> OnEvent;

        public int Scheduled { get; private set; }
        public int Delivered { get; private set; }
        public int PendingCount { get { return _pending.Count; } }
        public double DeliveryRate { get { return Scheduled == 0 ? 1.0 : (double)Delivered / Scheduled; } }

        public void OnRecord(HapticCueRecord r, double nowMs)
        {
            if (r == null || r.Cue != "stroke") return;
            string id = r.CueId ?? ("motor" + r.Motor + "@" + r.SentAtMs);
            if (!r.SentMs.HasValue)
            {
                // dropped before sending: scheduled, never delivered
                Scheduled++;
                Raise(r.Motor, false, null, r.Reason ?? "dropped", CurrentTrial(), id, null);
                return;
            }
            Pending p;
            if (!_pending.TryGetValue(id, out p))
            {
                if (r.Delivered || r.Reason != null) return; // an ack/rejection for a cue we never saw sent: nothing to complete
                Scheduled++;
                _pending[id] = new Pending { Motor = r.Motor, SentMs = r.SentMs.Value, Trial = CurrentTrial(), CueId = id };
                AddWindow(r.SentMs.Value);
                return;
            }
            _pending.Remove(id);
            if (r.Delivered)
            {
                Delivered++;
                Raise(p.Motor, true, r.AckLatencyMs, null, p.Trial, id, p.SentMs);
            }
            else
            {
                Raise(p.Motor, false, null, r.Reason ?? "rejected", p.Trial, id, p.SentMs);
            }
        }

        /// <summary>Close out cues whose ack never came.</summary>
        public void Tick(double nowMs)
        {
            if (_pending.Count == 0) { Prune(nowMs); return; }
            List<string> expired = null;
            foreach (var kv in _pending)
                if (nowMs - kv.Value.SentMs > AckTimeoutMs) (expired ?? (expired = new List<string>())).Add(kv.Key);
            if (expired != null)
                foreach (var id in expired)
                {
                    var p = _pending[id]; _pending.Remove(id);
                    Raise(p.Motor, false, null, "no_ack", p.Trial, id, p.SentMs);
                }
            Prune(nowMs);
        }

        /// <summary>Session end: every cue still waiting is written as not delivered.</summary>
        public void FlushAll()
        {
            if (_pending.Count == 0) return;
            var copy = new List<Pending>(_pending.Values);
            _pending.Clear();
            foreach (var p in copy) Raise(p.Motor, false, null, "no_ack", p.Trial, p.CueId, p.SentMs);
        }

        /// <summary>True when session time t_ms lies within a motor pulse plus the 50 ms ring-down (the sensor files' motor_excl flag).</summary>
        public bool InMotorWindow(double tMs)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var w = _windows[i];
                if (tMs >= w[0] && tMs <= w[1]) return true;
                if (w[1] < tMs - 5000) break; // windows are appended in time order
            }
            return false;
        }

        private void AddWindow(double sentMs) { _windows.Add(new[] { sentMs, sentMs + MotorWindowMs }); }

        private void Prune(double nowMs)
        {
            int drop = 0;
            while (drop < _windows.Count && _windows[drop][1] < nowMs - 60000) drop++;
            if (drop > 0) _windows.RemoveRange(0, drop);
        }

        private int? CurrentTrial() { return Trial == null ? null : Trial(); }

        private void Raise(int motor, bool delivered, double? ackLatencyMs, string reason, int? trial, string cueId, double? sentMs)
        {
            var d = new JObject { ["cue"] = "stroke", ["motor"] = motor, ["delivered"] = delivered, ["cue_id"] = cueId };
            if (delivered && ackLatencyMs.HasValue) d["ack_latency_ms"] = Math.Round(ackLatencyMs.Value, 1);
            if (sentMs.HasValue) d["sent_ms"] = Math.Round(sentMs.Value, 1);
            if (reason != null) d["reason"] = reason;
            var h = OnEvent;
            if (h != null) h(new TrialEvent { Block = 0, Trial = trial, Type = "haptic_cue", Data = d });
        }
    }
}
