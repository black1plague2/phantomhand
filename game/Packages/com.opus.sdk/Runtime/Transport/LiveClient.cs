using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Opus.Sdk
{
    /// <summary>
    /// Headset-side implementation of OPUS Live Protocol v1 (contracts/LIVE_PROTOCOL.md,
    /// contracts/schemas/live-message.schema.json). Owns: UDP beacon discovery (port 8788) with a manual-host
    /// fallback, the `ws://host:8787/opus/v1/live` WebSocket connection, hello/hello_ack + pair_token
    /// persistence, periodic status/ping, trial_event/metrics_tick send, requires_ack + 1s-timeout resend,
    /// reconnect with exponential backoff (0.5s -> 8s), resume via `resume_from_seq` + an outbox, inbound
    /// de-dup by id, and `HTTP PUT` file upload (idempotent by sha256) via UnityWebRequest.
    ///
    /// Threading: the WebSocket/UDP/HTTP work runs on background <see cref="Task"/>s. Everything that must run
    /// on Unity's main thread (starting a UnityWebRequest, invoking <see cref="OnCommand"/> so game/shell code
    /// can safely touch scene objects) is queued into <see cref="_mainThreadQueue"/> and drained by
    /// <see cref="Pump"/>, which the shell must call once per frame (e.g. from a MonoBehaviour Update()).
    ///
    /// This class never talks to a specific game module directly — it raises <see cref="OnCommand"/> for the
    /// shell to dispatch (start/pause/resume/stop/recenter/skip_block/adjust_params/show_message), keeping
    /// com.opus.sdk's only Unity-specific dependency confined to UnityEngine.Networking + PlayerPrefs.
    /// </summary>
    public sealed class LiveClient : IDisposable
    {
        public const int BeaconPort = 8788;
        public const int HubPort = 8787;
        public const string WsPath = "/opus/v1/live";
        private const string PairTokenPrefKey = "opus_pair_token";

        private readonly string _deviceId;
        private readonly Func<JObject> _versionsProvider;
        private readonly Func<JArray> _gamesProvider;
        private readonly string _manualHost;
        private readonly int _hubPort;

        private readonly LiveMessageFactory _factory = new LiveMessageFactory("headset");
        private readonly LiveMessageOutbox _outbox = new LiveMessageOutbox();
        private readonly LiveMessageDedup _dedup = new LiveMessageDedup();
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly SessionClock _clock = new SessionClock();

        private ClientWebSocket _ws;
        private CancellationTokenSource _lifetimeCts;
        private Task _connectionLoopTask;
        private volatile bool _connected;
        private volatile bool _running;
        private int? _lastRecvSeqFromHub;
        private string _pairToken;
        private string _sessionId;
        private double _lastStatusSentMs = -1;
        private double _lastPingSentMs = -1;
        private double _lastPingSentAtMs;
        private double _lastPongRttMs = -1;
        private int _reconnectAttempt;

        /// <summary>(command, params, ackId) — the shell acks by calling <see cref="AckCommand"/> with the same ackId.</summary>
        public event Action<string, JObject, string> OnCommand;
        public event Action<ConnectionState> OnStateChanged;

        public enum ConnectionState { Disconnected, Discovering, Connecting, Connected, Reconnecting }

        public bool IsConnected => _connected;
        /// <summary>Messages kept for replay on the next hello_ack: unacked requires_ack messages and every trial_event.</summary>
        public int OutboxCount => _outbox.Count;
        public double LastRttMs => _lastPongRttMs;
        public string ActiveSessionId => _sessionId;
        /// <summary>The hub port the WebSocket and the HTTP uploads go to (<see cref="HubPort"/> unless the constructor was given another).</summary>
        public int ActiveHubPort => _hubPort;

        /// <param name="deviceId">Stable per-install id (e.g. SystemInfo.deviceUniqueIdentifier).</param>
        /// <param name="versionsProvider">Returns {"shell": "...", "sdk": "...", "games": {...}} for hello.payload.versions.</param>
        /// <param name="gamesProvider">Returns [{"id":..,"version":..}, ...] for hello.payload.games.</param>
        /// <param name="manualHost">If set, skip UDP discovery and connect directly (pairing-code / manual-IP fallback).</param>
        /// <param name="hubPort">TCP port of the hub's WebSocket + HTTP server (default 8787); out of range falls back to 8787.</param>
        public LiveClient(string deviceId, Func<JObject> versionsProvider, Func<JArray> gamesProvider = null, string manualHost = null, int hubPort = HubPort)
        {
            _deviceId = deviceId;
            _versionsProvider = versionsProvider;
            _gamesProvider = gamesProvider;
            _manualHost = manualHost;
            _hubPort = hubPort >= 1 && hubPort <= 65535 ? hubPort : HubPort;
            _pairToken = PlayerPrefs.GetString(PairTokenPrefKey, null);
        }

        /// <summary>`ws://host:port/opus/v1/live`.</summary>
        public static Uri BuildWsUri(string host, int port) => new Uri($"ws://{host}:{port}{WsPath}");

        /// <summary>`http://host:port/opus/v1/sessions/{session_id}/files/{name}`.</summary>
        public static string BuildUploadUrl(string host, int port, string sessionId, string name) =>
            $"http://{host}:{port}/opus/v1/sessions/{Uri.EscapeDataString(sessionId)}/files/{Uri.EscapeDataString(name)}";

        public void Start()
        {
            if (_running) return;
            _running = true;
            _lifetimeCts = new CancellationTokenSource();
            _connectionLoopTask = Task.Run(() => ConnectionLoopAsync(_lifetimeCts.Token));
        }

        public void Stop()
        {
            _running = false;
            _lifetimeCts?.Cancel();
            try { _ws?.Abort(); } catch { /* best-effort */ }
        }

        public void Dispose() => Stop();

        /// <summary>Drops the socket; the connection loop reconnects and replays (the outbox is kept). For a link that slept through the
        /// hub's pings (a headset that was taken off): it still works from this side while the hub has written the headset off.</summary>
        public void Reconnect()
        {
            try { _ws?.Abort(); } catch { /* best-effort */ }
        }

        /// <summary>Call once per frame (e.g. MonoBehaviour.Update). Drains main-thread work and sends
        /// periodic status/ping. All timing uses <paramref name="nowMs"/> so tests can drive it deterministically.</summary>
        public void Pump(double nowMs)
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); } catch (Exception e) { Debug.LogException(e); }
            }

            if (!_connected) return;

            if (_lastStatusSentMs < 0 || nowMs - _lastStatusSentMs >= 200) // <=5Hz per LIVE_PROTOCOL.md
            {
                // Shell drives actual status payload via SendStatus(); this just guards the rate for callers
                // that don't self-throttle. No-op if the shell hasn't called SendStatus recently.
            }

            if (_lastPingSentMs < 0 || nowMs - _lastPingSentMs >= 1000)
            {
                _lastPingSentMs = nowMs;
                _lastPingSentAtMs = _clock.NowMs; // same clock the pong handler reads, whatever time base the caller uses
                _ = SendAsync(_factory.Build("ping", new JObject { ["echo_ts_ms"] = nowMs }));
            }

            // Same time base as Track (SendAsync uses _clock.NowMs), not the caller's nowMs: with two bases every entry looked 1 s old at once.
            double outboxNow = _clock.NowMs;
            foreach (var timedOut in _outbox.TimedOut(outboxNow))
            {
                _outbox.MarkResent(timedOut.Id, outboxNow);
                _ = SendRawAsync(timedOut);
            }
        }

        public void SendStatus(JObject statusPayload, double nowMs)
        {
            if (!_connected) return;
            _lastStatusSentMs = nowMs;
            _ = SendAsync(_factory.Build("status", statusPayload, _sessionId));
        }

        public void SendTrialEvent(TrialEvent evt)
        {
            var payload = JObject.FromObject(evt);
            var msg = _factory.Build("trial_event", payload, _sessionId);
            // Kept for resume whether the link is up or not (LIVE_PROTOCOL.md, Resume: "all trial_events since the last hello_ack"),
            // although trial_events don't require_ack: an event raised during a hub outage is replayed on the next hello_ack.
            // With an early return here, a 30 s outage at the end of a run lost block_end for the live view (8 Oct 2026: 203 of 204).
            _outbox.Track(msg, _clock.NowMs);
            if (_connected) _ = SendAsync(msg);
        }

        public void SendMetricsTick(int windowTrials, JObject metrics)
        {
            if (!_connected) return;
            var payload = new JObject { ["window_trials"] = windowTrials, ["metrics"] = metrics };
            _ = SendAsync(_factory.Build("metrics_tick", payload, _sessionId));
        }

        public void NotifySessionStarted(string sessionId)
        {
            _sessionId = sessionId;
            if (!_connected) return;
            _ = SendAsync(_factory.Build("session_started", null, sessionId));
        }

        public void NotifySessionEnded(string endReason)
        {
            if (_connected)
                _ = SendAsync(_factory.Build("session_ended", new JObject { ["end_reason"] = endReason }, _sessionId));
        }

        public void NotifyFileAvailable(string name, long bytes, string sha256)
        {
            if (!_connected) return;
            var payload = new JObject { ["name"] = name, ["bytes"] = bytes, ["sha256"] = sha256 };
            _ = SendAsync(_factory.Build("file_available", payload, _sessionId, requiresAck: false));
        }

        /// <summary>Shell calls this after handling a command from <see cref="OnCommand"/>.</summary>
        public void AckCommand(string ackId, bool ok, string error = null)
        {
            var payload = new JObject { ["ack_id"] = ackId, ["ok"] = ok, ["recv_ts_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            if (error != null) payload["error"] = error;
            _ = SendAsync(_factory.Build("ack", payload, _sessionId));
        }

        /// <summary>HTTP PUT /opus/v1/sessions/{session_id}/files/{name}. Idempotent on the hub by
        /// (session_id, name) + sha256 (201 new / 200 identical / 409 mismatch, per LIVE_PROTOCOL.md).</summary>
        public Task<bool> UploadFileAsync(string sessionId, string name, string absolutePath)
        {
            var tcs = new TaskCompletionSource<bool>();
            _mainThreadQueue.Enqueue(() => StartUploadCoroutineless(sessionId, name, absolutePath, tcs));
            return tcs.Task;
        }

        private async void StartUploadCoroutineless(string sessionId, string name, string absolutePath, TaskCompletionSource<bool> tcs)
        {
            try
            {
                if (string.IsNullOrEmpty(_hostForHttp))
                {
                    tcs.TrySetResult(false);
                    return;
                }
                byte[] bytes = System.IO.File.ReadAllBytes(absolutePath);
                string sha256 = Sha256Hex(bytes);
                string url = BuildUploadUrl(_hostForHttp, _hubPort, sessionId, name);

                using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
                req.uploadHandler = new UploadHandlerRaw(bytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/octet-stream");
                req.SetRequestHeader("X-Sha256", sha256);
                req.timeout = 15;   // seconds; without it a hub that vanished (phone off, hotspot gone) holds every remaining file for the OS connect timeout

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                bool ok = req.responseCode == 200 || req.responseCode == 201;
                if (req.responseCode == 409)
                    Debug.LogWarning($"[LiveClient] file_available sha256 mismatch for {name} (409) — hub already has a different version");
                else if (!ok && !_uploadFailureLogged)
                {
                    // once per outage, with Unity's own reason: "Insecure connection not allowed" here means the player was built without
                    // Player > Allow downloads over HTTP = Always allowed, and no file will ever reach a hub on the LAN
                    _uploadFailureLogged = true;
                    Debug.LogWarning($"[LiveClient] upload of {name} to {_hostForHttp}:{_hubPort} failed: {req.result}, {req.error} (HTTP {req.responseCode}); further failures are not logged until one succeeds");
                }
                if (ok) _uploadFailureLogged = false;
                if (ok) NotifyFileAvailable(name, bytes.LongLength, sha256);
                tcs.TrySetResult(ok);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                tcs.TrySetResult(false);
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // ---- connection lifecycle -----------------------------------------------------------------------

        private string _hostForHttp;
        private bool _uploadFailureLogged;   // main thread only (the upload path runs from the main-thread queue)

        private async Task ConnectionLoopAsync(CancellationToken ct)
        {
            double backoffMs = 500;
            while (_running && !ct.IsCancellationRequested)
            {
                string host = _manualHost;
                RaiseState(ConnectionState.Discovering);
                if (string.IsNullOrEmpty(host))
                {
                    host = await DiscoverHubViaUdpAsync(ct, timeoutMs: 5000);
                }
                if (string.IsNullOrEmpty(host))
                {
                    await DelaySafe(backoffMs, ct);
                    backoffMs = Math.Min(backoffMs * 2, 8000);
                    continue;
                }

                _hostForHttp = host;
                RaiseState(ConnectionState.Connecting);
                bool ok = await ConnectAndRunAsync(host, ct);
                _connected = false;
                RaiseState(_running ? ConnectionState.Reconnecting : ConnectionState.Disconnected);

                if (!_running) break;
                backoffMs = ok ? 500 : Math.Min(backoffMs * 2, 8000); // reset backoff after a session that connected cleanly
                await DelaySafe(backoffMs, ct);
            }
        }

        private static async Task DelaySafe(double ms, CancellationToken ct)
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(ms), ct); } catch (TaskCanceledException) { }
        }

        /// <summary>Listens for the hub's 1s UDP beacon on port 8788: {"opus_hub":1,"hub_id":..,"port":8787,"name":..}.</summary>
        private async Task<string> DiscoverHubViaUdpAsync(CancellationToken ct, int timeoutMs)
        {
            try
            {
                using var udp = new UdpClient(BeaconPort) { EnableBroadcast = true };
                var receiveTask = udp.ReceiveAsync();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs, ct));
                if (completed != receiveTask) return null;
                var result = receiveTask.Result;
                var json = Encoding.UTF8.GetString(result.Buffer);
                var obj = JObject.Parse(json);
                if (obj["opus_hub"]?.Value<int>() == 1)
                    return result.RemoteEndPoint.Address.ToString();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveClient] UDP discovery failed: {e.Message}");
            }
            return null;
        }

        private async Task<bool> ConnectAndRunAsync(string host, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            _ws = ws;
            try
            {
                var uri = BuildWsUri(host, _hubPort);
                await ws.ConnectAsync(uri, ct);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveClient] connect to {host} failed: {e.Message}");
                return false;
            }

            _connected = true;
            _reconnectAttempt = 0;
            RaiseState(ConnectionState.Connected);

            // hello
            var helloPayload = new JObject
            {
                ["device_id"] = _deviceId,
                ["role"] = "headset",
                ["versions"] = _versionsProvider?.Invoke() ?? new JObject(),
                ["games"] = _gamesProvider?.Invoke() ?? new JArray(),
                ["resume_from_seq"] = _lastRecvSeqFromHub,
                ["active_session_id"] = _sessionId,
            };
            var hello = _factory.Build("hello", helloPayload);
            await SendRawAsync(hello);

            var buffer = new byte[16 * 1024];
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close) return true;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    HandleIncoming(sb.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveClient] receive loop ended: {e.Message}");
            }
            return true;
        }

        private void HandleIncoming(string json)
        {
            LiveMessage msg;
            try { msg = LiveMessage.FromJson(json); }
            catch (Exception e) { Debug.LogWarning($"[LiveClient] bad message: {e.Message}"); return; }
            if (msg == null || string.IsNullOrEmpty(msg.Id)) return;
            if (_dedup.IsDuplicate(msg.Id)) return;

            _lastRecvSeqFromHub = msg.Seq;

            switch (msg.Type)
            {
                case "hello_ack":
                    string token = msg.Payload?["pair_token"]?.Value<string>();
                    if (!string.IsNullOrEmpty(token))
                    {
                        _pairToken = token;
                        _mainThreadQueue.Enqueue(() => PlayerPrefs.SetString(PairTokenPrefKey, token));
                    }
                    // Replay anything the hub says it hasn't seen yet.
                    int? hubResumeFrom = msg.Payload?["resume_from_seq"]?.Value<int?>();
                    foreach (var replay in _outbox.ReplayFrom(hubResumeFrom))
                        _ = SendRawAsync(replay);
                    break;

                case "ack":
                    string ackId = msg.Payload?["ack_id"]?.Value<string>();
                    if (ackId != null) _outbox.Ack(ackId);
                    break;

                case "pong":
                    _lastPongRttMs = _clock.NowMs - _lastPingSentAtMs;
                    break;

                case "ping":
                    _ = SendAsync(_factory.Build("pong", new JObject { ["echo_ts_ms"] = msg.Payload?["echo_ts_ms"] }, _sessionId));
                    break;

                case "assign_program":
                    _mainThreadQueue.Enqueue(() =>
                    {
                        OnCommand?.Invoke("assign_program", msg.Payload, msg.Id);
                    });
                    break;

                case "command":
                    string command = msg.Payload?["command"]?.Value<string>();
                    var cmdParams = msg.Payload?["params"] as JObject;
                    _mainThreadQueue.Enqueue(() => OnCommand?.Invoke(command, cmdParams, msg.Id));
                    break;

                case "error":
                    Debug.LogWarning($"[LiveClient] hub error: {msg.Payload}");
                    break;
            }
        }

        private Task SendAsync(LiveMessage msg)
        {
            if (msg.RequiresAck == true) _outbox.Track(msg, _clock.NowMs);
            return SendRawAsync(msg);
        }

        // ClientWebSocket allows only ONE outstanding SendAsync at a time; a second concurrent call throws
        // ("There is already one outstanding 'SendAsync' call") and that message is lost. Every send here is
        // fire-and-forget (status, ping, trial_event can all land in the same frame), so they are serialized.
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private async Task SendRawAsync(LiveMessage msg)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ws.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(msg.ToJson());
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveClient] send failed for {msg.Type}: {e.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void RaiseState(ConnectionState state) => _mainThreadQueue.Enqueue(() => OnStateChanged?.Invoke(state));
    }
}
