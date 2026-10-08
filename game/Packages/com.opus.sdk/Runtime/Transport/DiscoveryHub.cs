using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Opus.Sdk
{
    /// <summary>One discovery beacon, parsed from either dialect: PRD v2 section 9.1 (`type:"device_discovery"`) or
    /// the legacy `{"opus_haptic":1,"device_id","port","fw"}` hello (Node A sends both merged in one datagram).</summary>
    public sealed class DiscoveryInfo
    {
        public string DeviceId;
        public string DeviceKind = "haptic";   // a beacon without device_kind is a haptic node (legacy hello)
        public IPAddress Host;
        public int CommandPort = UdpHapticTransport.DefaultCommandPort;
        public string Firmware;
        public bool Legacy;                    // true when only the legacy hello fields were present
    }

    /// <summary>Which beacons a transport latches onto. A null kind accepts any kind; a null device id accepts any id.</summary>
    public sealed class DiscoveryFilter
    {
        public readonly string DeviceKind;
        public readonly string DeviceId;

        public DiscoveryFilter(string deviceKind = null, string deviceId = null)
        {
            DeviceKind = string.IsNullOrEmpty(deviceKind) ? null : deviceKind;
            DeviceId = string.IsNullOrEmpty(deviceId) ? null : deviceId;
        }

        public static DiscoveryFilter Haptic => new DiscoveryFilter("haptic");
        public static DiscoveryFilter Bio => new DiscoveryFilter("bio");
        public static DiscoveryFilter Any => new DiscoveryFilter();

        public bool Matches(DiscoveryInfo info)
        {
            if (info == null) return false;
            if (DeviceKind != null && !string.Equals(DeviceKind, info.DeviceKind, StringComparison.Ordinal)) return false;
            if (DeviceId != null && !string.Equals(DeviceId, info.DeviceId, StringComparison.Ordinal)) return false;
            return true;
        }
    }

    public interface IDiscoveryListener
    {
        /// <summary>Called on a background thread for every beacon received (1 Hz per node), already parsed.</summary>
        void OnDiscovered(DiscoveryInfo info);
    }

    /// <summary>
    /// Owns ONE UDP listener per discovery port (production: 8791) and fans each beacon out to every registered
    /// listener. Several transports (Node A haptic, Node B bio) therefore share a single socket, which matters on
    /// Android where SO_REUSEADDR is not honoured reliably. The socket opens on the first Register and closes on the
    /// last Unregister. Ports are parameters so tests can use an offset.
    /// </summary>
    public static class DiscoveryHub
    {
        private sealed class Entry
        {
            public readonly List<IDiscoveryListener> Listeners = new List<IDiscoveryListener>();
            public UdpClient Socket;
            public CancellationTokenSource Cts;
            public volatile bool Bound;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();

        /// <summary>Parse a beacon. Returns null when the datagram is not a discovery message.</summary>
        public static DiscoveryInfo Parse(string json, IPAddress remote)
        {
            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (Exception) { return null; }
            if (obj == null) return null;

            string type = obj["type"]?.Type == JTokenType.String ? obj["type"].Value<string>() : null;
            bool prd = type == "device_discovery";
            bool legacy = SafeInt(obj["opus_haptic"]) == 1;
            if (!prd && !legacy) return null;

            var info = new DiscoveryInfo
            {
                Host = remote,
                DeviceId = SafeString(obj["device_id"]),
                DeviceKind = SafeString(obj["device_kind"]) ?? "haptic",
                Firmware = SafeString(obj["firmware_version"]) ?? SafeString(obj["fw"]),
                Legacy = !prd,
            };
            int port = prd ? (SafeInt(obj["command_port"]) ?? SafeInt(obj["port"]) ?? UdpHapticTransport.DefaultCommandPort)
                           : (SafeInt(obj["port"]) ?? SafeInt(obj["command_port"]) ?? UdpHapticTransport.DefaultCommandPort);
            if (port < 1 || port > 65535) return null;
            info.CommandPort = port;
            return info;
        }

        private static string SafeString(JToken t) => t != null && t.Type == JTokenType.String ? t.Value<string>() : null;
        private static int? SafeInt(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (int?)t.Value<int>() : null;

        public static int ListenerCount(int port)
        {
            lock (Gate) return Entries.TryGetValue(port, out var e) ? e.Listeners.Count : 0;
        }

        /// <summary>True once the shared socket for this port is bound.</summary>
        public static bool IsListening(int port)
        {
            lock (Gate) return Entries.TryGetValue(port, out var e) && e.Bound;
        }

        public static void Register(int port, IDiscoveryListener listener)
        {
            if (listener == null) return;
            Entry entry;
            bool start = false;
            lock (Gate)
            {
                if (!Entries.TryGetValue(port, out entry))
                {
                    entry = new Entry { Cts = new CancellationTokenSource() };
                    Entries[port] = entry;
                    start = true;
                }
                if (!entry.Listeners.Contains(listener)) entry.Listeners.Add(listener);
            }
            if (start)
            {
                var e = entry;
                _ = Task.Run(() => ListenLoopAsync(port, e));
            }
        }

        public static void Unregister(int port, IDiscoveryListener listener)
        {
            Entry toClose = null;
            lock (Gate)
            {
                if (!Entries.TryGetValue(port, out var entry)) return;
                entry.Listeners.Remove(listener);
                if (entry.Listeners.Count == 0)
                {
                    Entries.Remove(port);
                    toClose = entry;
                }
            }
            if (toClose != null)
            {
                try { toClose.Cts.Cancel(); } catch { /* best-effort */ }
                try { toClose.Socket?.Close(); } catch { /* best-effort */ }
            }
        }

        /// <summary>Parse one datagram and hand it to every listener registered on <paramref name="port"/>.
        /// Returns the number of listeners notified (0 when it is not a discovery message). Public so tests can
        /// drive the fan-out without a socket.</summary>
        public static int Dispatch(int port, string json, IPAddress remote)
        {
            var info = Parse(json, remote);
            if (info == null) return 0;
            IDiscoveryListener[] snapshot;
            lock (Gate)
            {
                if (!Entries.TryGetValue(port, out var entry)) return 0;
                snapshot = entry.Listeners.ToArray();
            }
            foreach (var l in snapshot)
            {
                try { l.OnDiscovered(info); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return snapshot.Length;
        }

        private static async Task ListenLoopAsync(int port, Entry entry)
        {
            var ct = entry.Cts.Token;
            // Windows can hold a just-closed UDP port briefly (run10 root cause): set ReuseAddress before the bind
            // and retry a few times so one slow OS-level release never blackholes discovery for a whole scene.
            const int maxBindAttempts = 5;
            for (int attempt = 1; attempt <= maxBindAttempts; attempt++)
            {
                try
                {
                    var socket = new UdpClient();
                    UdpConnReset.Disable(socket.Client);
                    socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.ExclusiveAddressUse = false;
                    socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                    socket.EnableBroadcast = true;
                    lock (Gate)
                    {
                        if (ct.IsCancellationRequested) { try { socket.Close(); } catch { } return; }
                        entry.Socket = socket;
                        entry.Bound = true;
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (attempt >= maxBindAttempts)
                    {
                        Debug.LogWarning($"[DiscoveryHub] listener failed to bind {port} after {maxBindAttempts} attempts: {e.Message}");
                        return;
                    }
                    try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }
                }
            }

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try { result = await entry.Socket.ReceiveAsync(); }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException e) when (UdpConnReset.IsConnReset(e)) { continue; } // WSAECONNRESET: keep listening
                    catch (SocketException) { break; }
                    Dispatch(port, Encoding.UTF8.GetString(result.Buffer), result.RemoteEndPoint.Address);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
