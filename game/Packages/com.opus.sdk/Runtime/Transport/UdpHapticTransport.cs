using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Opus.Sdk
{
    /// <summary>
    /// v1 haptic transport: UDP JSON, port 8790 (game->node, node replies on the same socket), discovery via the
    /// nodes' 1 s beacon on port 8791 (or a manual host from settings) — mirrors <see cref="LiveClient"/>'s
    /// discovery-then-connect shape, but UDP/connectionless throughout (a dropped node never blocks anything).
    ///
    /// v1.2: the beacon listener is the shared <see cref="DiscoveryHub"/> (one socket for any number of
    /// transports), both beacon dialects are understood, and an optional <see cref="DiscoveryFilter"/> picks which
    /// node this transport latches onto (default: the first `haptic` node; use <see cref="DiscoveryFilter.Bio"/>
    /// for Node B). Ports are constructor parameters so tests can run on offsets.
    ///
    /// Threading: one background <see cref="Task"/> drains an outgoing <see cref="BlockingCollection{T}"/> queue
    /// and does the actual <see cref="UdpClient.Send"/> call; a second reads inbound replies (ack/status/sensor
    /// data) on the command socket. Nothing here ever runs on Unity's main thread, and <see cref="Send"/> itself
    /// only does a lock-free enqueue.
    /// </summary>
    public sealed class UdpHapticTransport : IHapticTransport, IDiscoveryListener
    {
        public const int DefaultCommandPort = 8790;
        public const int DefaultDiscoveryPort = 8791;
        public const int CommandPort = DefaultCommandPort;       // kept for existing callers
        public const int DiscoveryPort = DefaultDiscoveryPort;   // kept for existing callers

        private readonly string _manualHost;
        private readonly int _manualPort;
        private readonly int _discoveryPort;
        private readonly DiscoveryFilter _filter;
        private UdpClient _cmdSocket;
        private CancellationTokenSource _cts;
        private volatile IPEndPoint _deviceEndpoint;
        private readonly BlockingCollection<string> _outQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private volatile bool _running;
        private bool _registeredWithHub;

        public bool HasDevice => _deviceEndpoint != null;
        public event Action<string> OnMessage;
        public event Action OnDeviceKnown;

        /// <summary>Endpoint commands go to once a device is known (null before).</summary>
        public IPEndPoint DeviceEndpoint => _deviceEndpoint;
        /// <summary>device_id from the latched beacon (null for a manual host).</summary>
        public string LatchedDeviceId { get; private set; }
        /// <summary>Local command-socket port (the node replies and streams to it). 0 before Start.</summary>
        public int LocalPort { get; private set; }

        /// <param name="manualHost">If set, skip discovery and send directly to this host (IP address).</param>
        /// <param name="filter">Which node to latch; null = first `haptic` node.</param>
        /// <param name="commandPort">Node command port for a manual host (beacons carry their own).</param>
        /// <param name="discoveryPort">UDP port the nodes broadcast to (8791 in production).</param>
        public UdpHapticTransport(string manualHost = null, DiscoveryFilter filter = null,
                                  int commandPort = DefaultCommandPort, int discoveryPort = DefaultDiscoveryPort)
        {
            _manualHost = manualHost;
            _filter = filter ?? DiscoveryFilter.Haptic;
            _manualPort = commandPort;
            _discoveryPort = discoveryPort;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();

            try
            {
                _cmdSocket = new UdpClient(0); // ephemeral local port; the node replies to whatever src port we send from
                LocalPort = ((IPEndPoint)_cmdSocket.Client.LocalEndPoint).Port;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UdpHapticTransport] failed to open command socket: {e.Message}");
                return;
            }

            if (!string.IsNullOrEmpty(_manualHost) && IPAddress.TryParse(_manualHost, out var ip))
            {
                _deviceEndpoint = new IPEndPoint(ip, _manualPort);
                OnDeviceKnown?.Invoke();
            }
            else
            {
                _registeredWithHub = true;
                DiscoveryHub.Register(_discoveryPort, this);
            }

            _ = Task.Run(() => SendLoopAsync(_cts.Token));
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            ReleaseHub();
            try { _cts?.Cancel(); } catch { /* best-effort */ }
            try { _cmdSocket?.Close(); } catch { /* best-effort */ }
        }

        public void Dispose() => Stop();

        private void ReleaseHub()
        {
            if (!_registeredWithHub) return;
            _registeredWithHub = false;
            DiscoveryHub.Unregister(_discoveryPort, this);
        }

        /// <summary>Hub callback (background thread). Latches the FIRST beacon that matches the filter, then
        /// leaves the hub so the shared socket can close when nobody else is waiting.</summary>
        public void OnDiscovered(DiscoveryInfo info)
        {
            if (!_running || _deviceEndpoint != null || !_filter.Matches(info)) return;
            lock (this)
            {
                if (_deviceEndpoint != null) return;
                _deviceEndpoint = new IPEndPoint(info.Host, info.CommandPort);
                LatchedDeviceId = info.DeviceId;
            }
            Debug.Log($"[UdpHapticTransport] node discovered at {_deviceEndpoint} (device_id={info.DeviceId}, kind={info.DeviceKind})");
            ReleaseHub();
            OnDeviceKnown?.Invoke();
        }

        public void Send(string json)
        {
            if (!_running) return;
            // Never blocks: BlockingCollection.Add only blocks if bounded-capacity is full, and this queue is
            // unbounded — the actual socket write happens on SendLoopAsync's background thread.
            try { _outQueue.Add(json); } catch (InvalidOperationException) { /* queue completed/disposed */ }
        }

        private async Task SendLoopAsync(CancellationToken ct)
        {
            try
            {
                foreach (var json in _outQueue.GetConsumingEnumerable(ct))
                {
                    var endpoint = _deviceEndpoint;
                    if (endpoint == null) continue; // no known device yet — drop silently, not an error
                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes(json);
                        await _cmdSocket.SendAsync(bytes, bytes.Length, endpoint);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[UdpHapticTransport] send failed: {e.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try { result = await _cmdSocket.ReceiveAsync(); }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    OnMessage?.Invoke(json);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
