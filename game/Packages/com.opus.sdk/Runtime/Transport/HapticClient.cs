using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Opus.Sdk
{
    /// <summary>One record of a cue actually sent (or attempted), for the `haptic_cue` events.ndjson line and
    /// for latency measurement (matches the ack's echoed cue_id back to the send time).</summary>
    public sealed class HapticCueRecord
    {
        public string Cue;         // trunk_lean | low_confidence | success | stroke
        public double IntensityFrac; // 0..1, our internal (pre-255-scaling) intensity
        public bool Delivered;      // true once an ack for this cue_id is observed; false if never acked
        public string CueId;
        public double SentAtMs;
        public double? AckLatencyMs;

        // ---- additive (HAPTIC_PROTOCOL v1.2, stroke cues; all optional for the three clinical cues) ----
        public int Motor = -1;           // 0 | 1; -1 when not recorded
        public double? ScheduledMs;      // intended send time (playAt - lead), session ms
        public double? PlayAtMs;         // session ms the stroke was meant to land
        public double? SentMs;           // actual send time, session ms; null when the cue was dropped
        public string Reason;            // why Delivered is false: late | disabled | no_device | motor_gap | rate_cap | cancelled | <error_code>
    }

    /// <summary>
    /// Run9 step 3: headset-side haptic sleeve client, mirroring <see cref="LiveClient"/>'s style (own transport,
    /// non-blocking sends, status tracking, event-driven). Maps the three clinical cues (trunk_lean,
    /// low_confidence, success — contracts/HAPTIC_PROTOCOL.md) to the Electronics team's device-level wire
    /// format via <see cref="HapticCueMapper"/>, enforces every safety rule in software (this class, not the
    /// firmware): max_intensity ceiling, minimum 800ms gap per zone/motor, duration cap (delegated to the
    /// mapper), a 2s-idle watchdog stop, and stop-on-pause/end/quit (callers invoke <see cref="Stop"/> for those).
    /// Every cue attempted is raised via <see cref="OnCueRecorded"/> so the composition root can write it to
    /// events.ndjson as a `haptic_cue` event, whether or not the sleeve ever acks it (a missing sleeve must not
    /// silently vanish from the record).
    ///
    /// v1.2 (Phantom Hand): <see cref="ScheduleStroke"/> (brush pulses with their own safety gate),
    /// <see cref="SendDisplay"/> (OLED text) and <see cref="StartKeepalive"/> (ping + subscribe each second).
    /// </summary>
    public sealed class HapticClient : IDisposable
    {
        private const int WatchdogIdleMs = 2000;
        private const int MinGapPerZoneMs = 800;
        private const int MaxCuesPerSecond = 2;
        private const int LowConfidenceDoubleTapGapMs = 120;
        private const int SuccessMotorGapMs = 40;

        // ---- v1.2 stroke / display / keepalive limits (HAPTIC_PROTOCOL.md v1.2) ----
        public const int StrokeMinGapPerMotorMs = 250;
        public const int StrokeMaxPerSecond = 4;
        public const int StrokeLateDropMs = 50;
        public const int DisplayMaxChars = 12;
        public const int DisplayMaxPerSecond = 2;
        public const int KeepaliveIntervalMs = 1000;

        private sealed class PendingStroke
        {
            public string CueId;
            public int Motor;
            public double PlayAtMs, LeadMs, SendAtMs;
        }

        private readonly IHapticTransport _transport;
        private readonly Queue<double> _recentSendTimesMs = new Queue<double>();
        private readonly Dictionary<int, double> _lastSentPerMotorMs = new Dictionary<int, double>();
        private readonly Dictionary<string, HapticCueRecord> _pendingByCueId = new Dictionary<string, HapticCueRecord>();

        private readonly List<PendingStroke> _strokes = new List<PendingStroke>();
        private readonly Queue<double> _recentStrokeSendsMs = new Queue<double>();
        private readonly Dictionary<int, double> _lastStrokePerMotorMs = new Dictionary<int, double>();
        private readonly Queue<double> _recentDisplayMs = new Queue<double>();
        private int _strokeCounter;
        private bool _keepaliveActive;
        private double _nextKeepaliveMs;
        private double _lastPumpMs;

        private double _lastAnySendMs = -1;
        private bool _watchdogStopSent = true; // no cue sent yet, nothing to watchdog
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        // Non-blocking delayed sends (the low_confidence double pulse's second tap) -- fired from Pump() once
        // the caller's own session clock (nowMs) reaches FireAtMs. Deliberately NOT Thread.Sleep: Pump already
        // runs once per frame off nowMs, so a scheduled-time check costs nothing and never blocks the caller.
        private readonly List<(double FireAtMs, Action Action)> _scheduled = new List<(double, Action)>();

        /// <summary>Optional session-clock reader (for example () =&gt; clock.NowMs). When set, the ack latency is
        /// measured on receipt (acks arrive on a background thread); otherwise AckLatencyMs stays null.</summary>
        public Func<double> Clock { get; set; }

        public bool KeepaliveActive => _keepaliveActive;
        public int PendingStrokeCount => _strokes.Count;

        public bool Enabled { get; set; } // hapticsEnabled manifest param, default false
        public double MaxIntensity { get; set; } = 0.8; // hapticMaxIntensity manifest param

        public bool Connected => _transport.HasDevice;
        public bool MotorsOk { get; private set; } = true;
        public bool ImuOk { get; private set; } = true;
        public double? BatteryPct { get; private set; }
        public string DeviceId { get; private set; }

        /// <summary>Raised for every cue attempted (sent to the transport), and again when its ack lands
        /// (Delivered flips true, AckLatencyMs filled in) — the composition root writes both as `haptic_cue`
        /// events (an update-in-place in its own log is fine; events.ndjson itself is append-only, so callers
        /// typically only persist the delivered/undelivered final state, see HapticClientTests for the exact
        /// sequence expected). Strokes that were dropped (late, gated, cancelled) are raised once with
        /// Delivered=false and a Reason.</summary>
        public event Action<HapticCueRecord> OnCueRecorded;

        public HapticClient(IHapticTransport transport)
        {
            _transport = transport;
            _transport.OnMessage += HandleMessage;
        }

        public void Start() => _transport.Start();

        /// <summary>Stop-on-pause/end/quit: tells the sleeve to stop all motors and cancels queued strokes.
        /// Always allowed to send even if <see cref="Enabled"/> is false (a "stop" is a safety message,
        /// not a clinical cue) — but only if a device is actually known, otherwise it's a silent no-op.</summary>
        public void Stop(string zone = "all")
        {
            CancelPendingStrokes();
            SendStop(zone);
        }

        private void SendStop(string zone)
        {
            // The device-level wire format (v1.1) has no "stop" command shape of its own (the Electronics team's
            // firmware only defines motor/intensity/duration/pattern) — per HAPTIC_PROTOCOL.md's v1.1 mapping
            // table note, all non-cue message types (stop/config/ping) keep the original v1 envelope; only cues
            // moved to the device-level format.
            SendEnvelope(new JObject
            {
                ["v"] = 1,
                ["type"] = "stop",
                ["id"] = Guid.NewGuid().ToString(),
                ["ts_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["zone"] = zone,
            });
            _watchdogStopSent = true;
            _lastAnySendMs = -1;
        }

        public void SendConfig(bool enabled, double maxIntensity, int minGapMs = MinGapPerZoneMs)
        {
            Enabled = enabled;
            MaxIntensity = maxIntensity;
            SendEnvelope(new JObject
            {
                ["v"] = 1,
                ["type"] = "config",
                ["id"] = Guid.NewGuid().ToString(),
                ["ts_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["enabled"] = enabled,
                ["max_intensity"] = maxIntensity,
                ["min_gap_ms"] = minGapMs,
            });
        }

        /// <param name="excessLeanCm">Lean beyond the trunkLeanWarningCm threshold (>=0); scales intensity
        /// 0.5..0.8 per the mapping table.</param>
        public bool SendTrunkLean(double excessLeanCm, double nowMs)
        {
            if (!GateCue(HapticCueMapper.MotorUpperArm, nowMs)) return false;
            var cueId = Guid.NewGuid().ToString();
            var cmd = HapticCueMapper.TrunkLean(excessLeanCm, MaxIntensity, cueId);
            SendDeviceCommand(cmd, "trunk_lean", nowMs);
            return true;
        }

        /// <summary>Sent as two pulses <see cref="LowConfidenceDoubleTapGapMs"/> (120ms, per HAPTIC_PROTOCOL.md)
        /// apart (the firmware has no double_tap pattern) — both share one cue_id/record for latency purposes
        /// (the first pulse's ack is what's measured). The second pulse is scheduled via <see cref="Pump"/>
        /// (non-blocking — no Thread.Sleep), not sent inline, so the caller's frame is never held up.</summary>
        public bool SendLowConfidence(double nowMs)
        {
            if (!GateCue(HapticCueMapper.MotorForearm, nowMs)) return false;
            var cueId = Guid.NewGuid().ToString();
            var cmd = HapticCueMapper.LowConfidence(MaxIntensity, cueId);
            SendDeviceCommand(cmd, "low_confidence", nowMs);
            // Second pulse: best-effort, not individually gated/recorded (it's part of the same logical cue) --
            // scheduled LowConfidenceDoubleTapGapMs later instead of sent immediately, per the protocol doc.
            string secondPulseJson = ToJson(new HapticDeviceCommand(cmd.Motor, cmd.Intensity255, cmd.DurationMs, cmd.Pattern, cueId, cmd.Cue));
            _scheduled.Add((nowMs + LowConfidenceDoubleTapGapMs, () => _transport.Send(secondPulseJson)));
            return true;
        }

        /// <summary>Success preempts the per-zone min-gap (a reward must always land) but still respects the
        /// global sustained-rate cap and duration/intensity clamps.</summary>
        public bool SendSuccess(double nowMs)
        {
            var cueId = Guid.NewGuid().ToString();
            var (first, second) = HapticCueMapper.Success(MaxIntensity, cueId);
            if (!GateCue(first.Motor, nowMs, bypassZoneGap: true)) return false;
            SendDeviceCommand(first, "success", nowMs, bypassZoneGap: true);
            _lastSentPerMotorMs[second.Motor] = nowMs; // preempt that zone's own gap too
            _transport.Send(ToJson(second));
            return true;
        }

        // ------------------------------------------------------------------ v1.2: stroke cues

        /// <summary>Queue one brush pulse. It is sent from <see cref="Pump"/> at playAtSessionMs - leadMs (no
        /// Thread.Sleep). Returns the cue id (stroke_&lt;n&gt;, echoed in the ack) unless one is supplied.
        /// Own safety gate, separate from the clinical cues': at least 250 ms per motor and at most 4 sends per
        /// rolling second; a send more than 50 ms past its time is dropped. Every outcome is raised through
        /// <see cref="OnCueRecorded"/> (dropped ones with Delivered=false and a Reason, "late" for the 50 ms rule).</summary>
        public string ScheduleStroke(int motor, double playAtSessionMs, double leadMs, string cueId = null)
        {
            if (motor != 0 && motor != 1) throw new ArgumentOutOfRangeException(nameof(motor), "motor must be 0 or 1");
            if (string.IsNullOrEmpty(cueId)) cueId = "stroke_" + (_strokeCounter++);
            _strokes.Add(new PendingStroke
            {
                CueId = cueId, Motor = motor, PlayAtMs = playAtSessionMs, LeadMs = leadMs, SendAtMs = playAtSessionMs - leadMs,
            });
            return cueId;
        }

        private void PumpStrokes(double nowMs)
        {
            if (_strokes.Count == 0) return;
            List<PendingStroke> due = null;
            foreach (var st in _strokes)
                if (nowMs >= st.SendAtMs) (due ?? (due = new List<PendingStroke>())).Add(st);
            if (due == null) return;
            due.Sort((a, b) => a.SendAtMs.CompareTo(b.SendAtMs));

            foreach (var st in due)
            {
                _strokes.Remove(st);
                string reason = null;
                if (nowMs - st.SendAtMs > StrokeLateDropMs) reason = "late";
                else if (!Enabled) reason = "disabled";
                else if (!_transport.HasDevice) reason = "no_device";
                else
                {
                    while (_recentStrokeSendsMs.Count > 0 && nowMs - _recentStrokeSendsMs.Peek() >= 1000) _recentStrokeSendsMs.Dequeue();
                    if (_lastStrokePerMotorMs.TryGetValue(st.Motor, out var last) && nowMs - last < StrokeMinGapPerMotorMs) reason = "motor_gap";
                    else if (_recentStrokeSendsMs.Count >= StrokeMaxPerSecond) reason = "rate_cap";
                }

                var cmd = HapticCueMapper.Stroke(st.Motor, MaxIntensity, st.CueId).WithPlayAt(st.PlayAtMs);
                var record = new HapticCueRecord
                {
                    Cue = "stroke",
                    IntensityFrac = cmd.Intensity255 / (double)HapticCueMapper.StrokeIntensityCap,
                    CueId = st.CueId,
                    SentAtMs = nowMs,
                    Delivered = false,
                    Motor = st.Motor,
                    ScheduledMs = st.SendAtMs,
                    PlayAtMs = st.PlayAtMs,
                };
                if (reason != null)
                {
                    record.Reason = reason;
                    OnCueRecorded?.Invoke(record);
                    continue;
                }

                _recentStrokeSendsMs.Enqueue(nowMs);
                _lastStrokePerMotorMs[st.Motor] = nowMs;
                _lastAnySendMs = nowMs;           // strokes refresh the watchdog
                _watchdogStopSent = false;
                record.SentMs = nowMs;
                lock (_pendingByCueId) _pendingByCueId[st.CueId] = record;
                _transport.Send(ToJson(cmd));
                OnCueRecorded?.Invoke(record);
            }
        }

        /// <summary>Drops every queued (unsent) stroke, raising each as delivered=false, reason "cancelled".</summary>
        public void CancelPendingStrokes()
        {
            if (_strokes.Count == 0) return;
            var copy = _strokes.ToArray();
            _strokes.Clear();
            foreach (var st in copy)
            {
                OnCueRecorded?.Invoke(new HapticCueRecord
                {
                    Cue = "stroke", CueId = st.CueId, Motor = st.Motor, ScheduledMs = st.SendAtMs, PlayAtMs = st.PlayAtMs,
                    SentAtMs = _lastPumpMs, Delivered = false, Reason = "cancelled",
                });
            }
        }

        // ------------------------------------------------------------------ v1.2: OLED text + keepalive

        private double NowFallback => Clock != null ? Clock() : _lastPumpMs;

        /// <summary>OLED line on Node A. Printable ASCII only, truncated to 12 chars, at most 2 per rolling second
        /// (extra calls return false). Empty text shows IDLE on the device. Sent even when haptics are disabled.</summary>
        public bool SendDisplay(string text) => SendDisplay(text, NowFallback);

        public bool SendDisplay(string text, double nowMs)
        {
            if (!_transport.HasDevice) return false;
            while (_recentDisplayMs.Count > 0 && nowMs - _recentDisplayMs.Peek() >= 1000) _recentDisplayMs.Dequeue();
            if (_recentDisplayMs.Count >= DisplayMaxPerSecond) return false;
            var sb = new System.Text.StringBuilder();
            foreach (char ch in text ?? "")
            {
                if (ch < 0x20 || ch > 0x7E) continue;
                if (sb.Length >= DisplayMaxChars) break;
                sb.Append(ch);
            }
            _recentDisplayMs.Enqueue(nowMs);
            // The contract fake reads `text`, the real firmware reads `mode`: send the same line in both.
            string line = sb.ToString();
            SendEnvelope(new JObject { ["type"] = "display", ["text"] = line, ["mode"] = line });
            return true;
        }

        /// <summary>Start the once-per-second ping + subscribe (firmware watchdog FR-FW-04 and the subscriber
        /// list, 03-SPEC D3) plus the bare `{"type":"keepalive"}` the real firmware's 2 s watchdog is fed by. The first
        /// set goes out on the next Pump and goes on for a silent node too: a power-cycled node only streams to a peer
        /// it has heard from. Keepalives do not touch the cue watchdog.</summary>
        public void StartKeepalive(double nowMs)
        {
            _keepaliveActive = true;
            _nextKeepaliveMs = nowMs;
        }

        public void StopKeepalive() => _keepaliveActive = false;

        private void PumpKeepalive(double nowMs)
        {
            if (!_keepaliveActive || nowMs < _nextKeepaliveMs) return;
            _nextKeepaliveMs += KeepaliveIntervalMs;
            if (_nextKeepaliveMs <= nowMs) _nextKeepaliveMs = nowMs + KeepaliveIntervalMs;
            if (!_transport.HasDevice) return;
            SendEnvelope(new JObject
            {
                ["v"] = 1,
                ["type"] = "ping",
                ["id"] = Guid.NewGuid().ToString(),
                ["ts_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            SendEnvelope(new JObject { ["type"] = "subscribe" });
            SendEnvelope(new JObject { ["type"] = "keepalive" });
        }

        /// <summary>Call once per frame: drains the watchdog. If no cue has been sent in
        /// <see cref="WatchdogIdleMs"/> since the last one (and a stop hasn't already been sent for that idle
        /// period), sends a safety `stop` — mirrors the sleeve's own documented watchdog, done in software per
        /// the brief so a stuck/frozen game loop can't leave a motor buzzing indefinitely even if the firmware's
        /// own watchdog were absent. Also sends due strokes and the keepalive.</summary>
        public void Pump(double nowMs)
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); } catch (Exception e) { Debug.LogException(e); }
            }

            // Fire any delayed sends (e.g. the low_confidence second pulse) whose time has come. Iterate
            // backwards so removing a fired entry doesn't skip the next one.
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                if (nowMs >= _scheduled[i].FireAtMs)
                {
                    var action = _scheduled[i].Action;
                    _scheduled.RemoveAt(i);
                    try { action(); } catch (Exception e) { Debug.LogException(e); }
                }
            }

            _lastPumpMs = nowMs;
            PumpStrokes(nowMs);
            PumpKeepalive(nowMs);

            if (_lastAnySendMs >= 0 && !_watchdogStopSent && nowMs - _lastAnySendMs > WatchdogIdleMs)
            {
                SendStop("all"); // queued strokes stay queued: "idle for 2 s" says nothing about future ones
            }
        }

        private bool GateCue(int motor, double nowMs, bool bypassZoneGap = false)
        {
            if (!Enabled)
            {
                Debug.Log($"[HapticClient] cue dropped for motor {motor}: Enabled=false.");
                return false;
            }
            if (!_transport.HasDevice)
            {
                // Run10 diagnostic (kept — cheap, only fires on the drop path, useful for future "why didn't my
                // cue send" debugging without a fake sleeve running): no device means nothing to gate/send
                // meaningfully, this is not an error, but it's exactly the condition run9/run10 needed visibility
                // into (a cue was requested but silently swallowed because discovery hadn't latched yet).
                Debug.Log($"[HapticClient] cue dropped for motor {motor}: no haptic device known yet (HasDevice=false).");
                return false;
            }

            // Global sustained-rate cap: at most MaxCuesPerSecond within the trailing 1000ms window.
            while (_recentSendTimesMs.Count > 0 && nowMs - _recentSendTimesMs.Peek() > 1000) _recentSendTimesMs.Dequeue();
            if (_recentSendTimesMs.Count >= MaxCuesPerSecond)
            {
                Debug.Log($"[HapticClient] cue dropped for motor {motor}: sustained-rate cap ({MaxCuesPerSecond}/s) hit.");
                return false;
            }

            if (!bypassZoneGap && _lastSentPerMotorMs.TryGetValue(motor, out var last) && nowMs - last < MinGapPerZoneMs)
            {
                Debug.Log($"[HapticClient] cue dropped for motor {motor}: per-zone min gap ({MinGapPerZoneMs}ms) not elapsed (last={last:F0} now={nowMs:F0}).");
                return false;
            }

            return true;
        }

        private void SendDeviceCommand(HapticDeviceCommand cmd, string cueLabel, double nowMs, bool bypassZoneGap = false)
        {
            Debug.Log($"[HapticClient] sending cue={cueLabel} motor={cmd.Motor} cueId={cmd.CueId} nowMs={nowMs:F0}");
            _recentSendTimesMs.Enqueue(nowMs);
            _lastSentPerMotorMs[cmd.Motor] = nowMs;
            _lastAnySendMs = nowMs;
            _watchdogStopSent = false;

            var record = new HapticCueRecord
            {
                Cue = cueLabel,
                IntensityFrac = cmd.Intensity255 / (double)HapticCueMapper.DeviceIntensityMax,
                Delivered = false,
                CueId = cmd.CueId,
                SentAtMs = nowMs,
                SentMs = nowMs,
                Motor = cmd.Motor,
            };
            lock (_pendingByCueId) _pendingByCueId[cmd.CueId] = record;
            _transport.Send(ToJson(cmd));
            OnCueRecorded?.Invoke(record);
        }

        private void SendEnvelope(JObject msg) => _transport.Send(msg.ToString(Newtonsoft.Json.Formatting.None));

        private static string ToJson(HapticDeviceCommand cmd)
        {
            var o = new JObject
            {
                ["motor"] = cmd.Motor,
                ["intensity"] = cmd.Intensity255,
                ["duration_ms"] = cmd.DurationMs,
                ["pattern"] = cmd.Pattern,
            };
            if (cmd.CueId != null) o["cue_id"] = cmd.CueId;
            if (cmd.Cue != null) o["cue"] = cmd.Cue;
            if (cmd.PlayAtMs.HasValue) o["play_at_ms"] = cmd.PlayAtMs.Value;
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        private void HandleMessage(string json)
        {
            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (Exception e) { Debug.LogWarning($"[HapticClient] bad message: {e.Message}"); return; }

            string type = obj["type"]?.Value<string>();
            switch (type)
            {
                case "status":
                    DeviceId = obj["device_id"]?.Value<string>() ?? DeviceId;
                    BatteryPct = obj["battery_pct"]?.Value<double?>();
                    MotorsOk = obj["motors_ok"]?.Value<bool?>() ?? obj["motor_0"]?["available"]?.Value<bool?>() ?? MotorsOk;
                    ImuOk = obj["imu_ok"]?.Value<bool?>() ?? ImuOk;
                    break;

                case "ack":
                    // v1 ack: {ack_id, ok}. Electronics-team contract ack: {cue_id, status: accepted|executed|
                    // rejected|error}. Real firmware ack: {cue_id, accepted: bool}. "accepted" is a success exactly
                    // like "executed". Accept all three so the physical sleeve and the simulator are interchangeable.
                    string ackId = obj["ack_id"]?.Value<string>() ?? obj["cue_id"]?.Value<string>();
                    HapticCueRecord record = null;
                    lock (_pendingByCueId)
                    {
                        if (ackId != null && _pendingByCueId.TryGetValue(ackId, out record)) _pendingByCueId.Remove(ackId);
                        else record = null;
                    }
                    if (record != null)
                    {
                        string st = obj["status"]?.Type == JTokenType.String ? obj["status"].Value<string>() : null;
                        record.Delivered = AckSucceeded(obj, st);
                        // HandleMessage runs on a background thread (per IHapticTransport's OnMessage contract)
                        // with no access to the main-thread SessionClock unless the owner supplied Clock; without
                        // it only delivery is flagged here and latency is computed where the ack is consumed.
                        var clock = Clock;
                        if (clock != null) record.AckLatencyMs = clock() - record.SentAtMs;
                        if (!record.Delivered) record.Reason = obj["error_code"]?.Value<string>() ?? st ?? "rejected";
                        OnCueRecorded?.Invoke(record);
                    }
                    break;
            }
        }

        /// <summary>Did the node take the cue? `ok` (v1) wins, then the boolean `accepted` (real firmware), then the
        /// `status` text (accepted and executed are both successes; rejected, error and anything else are not). An ack
        /// that carries none of them is a plain receipt and counts as delivered.</summary>
        private static bool AckSucceeded(JObject ack, string status)
        {
            var ok = ack["ok"];
            if (ok != null && ok.Type == JTokenType.Boolean) return ok.Value<bool>();
            var accepted = ack["accepted"];
            if (accepted != null && accepted.Type == JTokenType.Boolean) return accepted.Value<bool>();
            return status == null || status == "accepted" || status == "executed";
        }

        public void Dispose()
        {
            _transport.OnMessage -= HandleMessage;
            _transport.Dispose();
        }
    }
}
