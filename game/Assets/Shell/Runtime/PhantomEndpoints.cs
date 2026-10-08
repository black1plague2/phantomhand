using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Opus.Shell
{
    /// <summary>
    /// Where the Phantom Hand scene finds its peers: hub, Node A (haptic), Node B (bio) and the discovery port. Plain C#
    /// (EditMode-testable). Precedence per field: environment (OPUS_PH_HUB / OPUS_PH_NODE_A / OPUS_PH_NODE_B /
    /// OPUS_PH_DISCOVERY_PORT, "host:port") &gt; <c>phantom_endpoints.json</c> in <c>Application.persistentDataPath</c> (Android player
    /// only) &gt; <see cref="PhantomHandSettings"/> &gt; discovery (null host).
    ///
    /// Quest runtime override, no rebuild: write <c>phantom_endpoints.json</c>, every key optional, names case-insensitive, an empty or
    /// null value = not set (that field falls through to the asset / discovery):
    /// <code>{ "hubHost": "192.168.1.20", "hubPort": 8787, "nodeAHost": "192.168.1.31", "nodeAPort": 8790,
    ///   "nodeBHost": "192.168.1.32", "nodeBPort": 8790, "discoveryPort": 8791 }</code>
    /// and push it, then restart the app:
    /// <c>adb push phantom_endpoints.json /sdcard/Android/data/&lt;package&gt;/files/phantom_endpoints.json</c>
    /// (&lt;package&gt; is printed by the APK build; the Unity default is com.DefaultCompany.OPUS). A missing file is the normal case (info
    /// log); an unreadable or malformed file is ignored with ONE warning per process, never an exception. <c>hubPort</c> is carried in
    /// <see cref="HubPort"/> even where the live link cannot use it yet.
    /// </summary>
    public sealed class PhantomEndpoints
    {
        public const string EnvHub = "OPUS_PH_HUB";
        public const string EnvNodeA = "OPUS_PH_NODE_A";
        public const string EnvNodeB = "OPUS_PH_NODE_B";
        public const string EnvDiscoveryPort = "OPUS_PH_DISCOVERY_PORT";
        /// <summary>Test knob only: a simulator on the same PC cannot stream to 8790, that port is the simulated node's own.</summary>
        public const string EnvTelemetryPort = "OPUS_PH_TELEMETRY_PORT";
        public const int DefaultCommandPort = 8790;
        public const int DefaultDiscoveryPort = 8791;
        public const int DefaultHubPort = 8787;
        public const string FileName = "phantom_endpoints.json";
        /// <summary>A bigger file is not an endpoints file (a wrong push): it is ignored with a warning, not read.</summary>
        public const int MaxFileBytes = 64 * 1024;

        public string HubHost; public int HubPort;
        public string NodeAHost; public int NodeAPort = DefaultCommandPort;
        public string NodeBHost; public int NodeBPort = DefaultCommandPort;
        public int DiscoveryPort = DefaultDiscoveryPort;
        /// <summary>Local UDP port both node transports also listen on. The real firmware (0.5.0) acks to the port a command came
        /// from but streams IMU and EMG to port 8790 of the last sender (HAPTIC_PROTOCOL v1.3).</summary>
        public int TelemetryPort = DefaultCommandPort;
        /// <summary>True when any value came from the environment (the harness is driving: the session runner then runs even without a headset).</summary>
        public bool FromEnvironment;
        /// <summary>"applied hubHost, nodeAHost" when an endpoints file was applied, else null (a rejected file goes to the warn callback).</summary>
        public string FileStatus;

        /// <summary>"1.2.3.4:8790", "1.2.3.4" or "[::1]:8790" to (host, port); (null, 0) when empty or malformed. Port 0 = not given.</summary>
        public static void ParseHostPort(string s, out string host, out int port)
        {
            host = null; port = 0;
            if (string.IsNullOrWhiteSpace(s)) return;
            s = s.Trim();
            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                int close = s.IndexOf(']');
                if (close < 2) return;
                host = s.Substring(1, close - 1);
                if (close + 1 < s.Length)
                {
                    if (s[close + 1] != ':' || !TryPort(s.Substring(close + 2), out port)) { host = null; port = 0; }
                }
                return;
            }
            int colon = s.LastIndexOf(':');
            if (colon < 0) { host = s; return; }
            if (s.IndexOf(':') != colon) return; // bare IPv6 without brackets is ambiguous: reject
            string h = s.Substring(0, colon);
            if (h.Length == 0 || !TryPort(s.Substring(colon + 1), out port)) { port = 0; return; }
            host = h;
        }

        private static bool TryPort(string t, out int port)
        {
            port = 0;
            return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536;
        }

        /// <param name="env">environment reader (name to value or null); tests pass a dictionary lookup.</param>
        /// <param name="settings">may be null.</param>
        public static PhantomEndpoints Resolve(Func<string, string> env, PhantomHandSettings settings)
        {
            // The production path: the endpoints file is read only by an Android player (hermetic in the editor and in tests).
            var e = Resolve(env, settings, ReadDeviceFile, WarnOnce);
            LogFileStatusOnce(e.FileStatus);
            return e;
        }

        /// <param name="readEndpointsFile">returns the text of phantom_endpoints.json, null when there is no such file; may throw (= unreadable).
        /// null = no file layer.</param>
        /// <param name="warn">receives at most ONE message per call (unreadable or malformed file); may be null.</param>
        public static PhantomEndpoints Resolve(Func<string, string> env, PhantomHandSettings settings, Func<string> readEndpointsFile, Action<string> warn)
        {
            var e = new PhantomEndpoints();
            if (settings != null)
            {
                string h; int p;
                ParseHostPort(settings.hubHost, out h, out p); e.HubHost = h; e.HubPort = p;
                ParseHostPort(settings.nodeAHost, out h, out p); e.NodeAHost = h; e.NodeAPort = p > 0 ? p : OrDefault(settings.nodeACommandPort, DefaultCommandPort);
                ParseHostPort(settings.nodeBHost, out h, out p); e.NodeBHost = h; e.NodeBPort = p > 0 ? p : OrDefault(settings.nodeBCommandPort, DefaultCommandPort);
                e.DiscoveryPort = OrDefault(settings.discoveryPort, DefaultDiscoveryPort);
            }
            if (readEndpointsFile != null) e.ApplyFileLayer(readEndpointsFile, warn);
            if (env != null)
            {
                string h; int p;
                ParseHostPort(env(EnvHub), out h, out p);
                if (h != null) { e.HubHost = h; e.HubPort = p; e.FromEnvironment = true; }
                ParseHostPort(env(EnvNodeA), out h, out p);
                if (h != null) { e.NodeAHost = h; e.NodeAPort = p > 0 ? p : DefaultCommandPort; e.FromEnvironment = true; }
                ParseHostPort(env(EnvNodeB), out h, out p);
                if (h != null) { e.NodeBHost = h; e.NodeBPort = p > 0 ? p : DefaultCommandPort; e.FromEnvironment = true; }
                int dp;
                if (TryPort(env(EnvDiscoveryPort) ?? "", out dp)) { e.DiscoveryPort = dp; e.FromEnvironment = true; }
                if (TryPort(env(EnvTelemetryPort) ?? "", out dp)) e.TelemetryPort = dp;
            }
            return e;
        }

        // ---- phantom_endpoints.json ---------------------------------------------------------------------------------------

        /// <summary>What phantom_endpoints.json said. A null field = the key was absent, null or empty (the lower layer wins).</summary>
        public sealed class FileValues
        {
            public string HubHost; public int? HubPort;
            public string NodeAHost; public int? NodeAPort;
            public string NodeBHost; public int? NodeBPort;
            public int? DiscoveryPort;
            /// <summary>The keys that carried a value, in the spelling of this class's documentation.</summary>
            public readonly List<string> Keys = new List<string>();
        }

        private sealed class BadFileValue : Exception { public BadFileValue(string message) : base(message) { } }

        /// <summary>Pure: the text of phantom_endpoints.json to its values. False (and <paramref name="problem"/>) for text that is not a
        /// JSON object or that has a wrong value in one of the seven known keys: the file is then ignored as a whole. Never throws.</summary>
        public static bool TryParseFile(string text, out FileValues values, out string problem)
        {
            values = null; problem = null;
            try
            {
                if (text != null) text = text.TrimStart((char)0xFEFF); // a UTF-8 BOM survives Encoding.GetString and PowerShell 5 "-Encoding UTF8"
                if (string.IsNullOrWhiteSpace(text)) { problem = "the file is empty"; return false; }
                var root = JToken.Parse(text) as JObject;
                if (root == null) { problem = "the top level must be a JSON object { ... }"; return false; }
                var v = new FileValues();
                ReadHostAndPort(root, "hubHost", "hubPort", v, out v.HubHost, out v.HubPort);
                ReadHostAndPort(root, "nodeAHost", "nodeAPort", v, out v.NodeAHost, out v.NodeAPort);
                ReadHostAndPort(root, "nodeBHost", "nodeBPort", v, out v.NodeBHost, out v.NodeBPort);
                v.DiscoveryPort = ReadPort(root, "discoveryPort");
                if (v.DiscoveryPort.HasValue) v.Keys.Add("discoveryPort");
                values = v;
                return true;
            }
            catch (BadFileValue e) { problem = e.Message; return false; }
            catch (Exception e) { problem = "not valid JSON (" + FirstLine(e.Message) + ")"; return false; }
        }

        /// <summary>A host key (a string, "host" or "host:port"; empty = not set) and its port key (an explicit port wins over one inside the host).</summary>
        private static void ReadHostAndPort(JObject o, string hostKey, string portKey, FileValues v, out string host, out int? port)
        {
            host = null; int embedded = 0;
            var t = o.GetValue(hostKey, StringComparison.OrdinalIgnoreCase);
            if (t != null && t.Type != JTokenType.Null)
            {
                if (t.Type != JTokenType.String) throw new BadFileValue(hostKey + " must be a string such as \"192.168.1.20\"");
                string s = ((string)t).Trim();
                if (s.Length > 0)
                {
                    ParseHostPort(s, out host, out embedded);
                    if (host == null) throw new BadFileValue(hostKey + " \"" + s + "\" is not a host or host:port");
                    v.Keys.Add(hostKey);
                }
            }
            port = ReadPort(o, portKey);
            if (!port.HasValue && host != null && embedded > 0) port = embedded;
            if (port.HasValue) v.Keys.Add(portKey);
        }

        /// <summary>A port key: a JSON integer 1-65535 or the same in a string; null / absent / "" = not set.</summary>
        private static int? ReadPort(JObject o, string key)
        {
            var t = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
            if (t == null || t.Type == JTokenType.Null) return null;
            long n;
            if (t.Type == JTokenType.String && ((string)t).Trim().Length == 0) return null;
            if (t.Type == JTokenType.Integer && ((JValue)t).Value is long l) n = l;
            else if (t.Type == JTokenType.String && long.TryParse(((string)t).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { }
            else throw new BadFileValue(key + " must be a port number 1-65535");
            if (n < 1 || n > 65535) throw new BadFileValue(key + " " + n + " is not a port number 1-65535");
            return (int)n;
        }

        private void ApplyFileLayer(Func<string> read, Action<string> warn)
        {
            string text = null, problem = null;
            try { text = read(); }
            catch (Exception ex) { problem = "could not read " + FileName + ": " + FirstLine(ex.Message); }
            if (problem == null)
            {
                if (text == null) return; // no file: the normal case
                FileValues f; string why;
                if (TryParseFile(text, out f, out why))
                {
                    if (f.HubHost != null) HubHost = f.HubHost;
                    if (f.HubPort.HasValue) HubPort = f.HubPort.Value;
                    if (f.NodeAHost != null) NodeAHost = f.NodeAHost;
                    if (f.NodeAPort.HasValue) NodeAPort = f.NodeAPort.Value;
                    if (f.NodeBHost != null) NodeBHost = f.NodeBHost;
                    if (f.NodeBPort.HasValue) NodeBPort = f.NodeBPort.Value;
                    if (f.DiscoveryPort.HasValue) DiscoveryPort = f.DiscoveryPort.Value;
                    FileStatus = f.Keys.Count == 0 ? "found, but it sets no known key" : "applied " + string.Join(", ", f.Keys);
                    return;
                }
                problem = FileName + " ignored: " + why;
            }
            if (warn != null) warn("[PhantomHand] " + problem);
        }

        private static bool _warned, _missingLogged, _statusLogged;

        /// <summary>The file on a real Android player (Quest): persistentDataPath/phantom_endpoints.json. Everywhere else null, so the editor,
        /// the EditMode tests and the L3 harness never pick up a stray file.</summary>
        private static string ReadDeviceFile()
        {
            if (Application.platform != RuntimePlatform.Android) return null;
            string path = Path.Combine(Application.persistentDataPath, FileName);
            if (!File.Exists(path))
            {
                if (!_missingLogged)
                {
                    _missingLogged = true;
                    Debug.Log("[PhantomHand] no " + path + " (optional: adb push a " + FileName + " there to set the hub / node addresses without a rebuild)");
                }
                return null;
            }
            long size = new FileInfo(path).Length;
            if (size > MaxFileBytes) throw new IOException(FileName + " is " + size + " bytes, the limit is " + MaxFileBytes);
            return File.ReadAllText(path);
        }

        private static void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning(message);
        }

        private static void LogFileStatusOnce(string status)
        {
            if (status == null || _statusLogged) return;
            _statusLogged = true;
            Debug.Log("[PhantomHand] " + FileName + " " + status);
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "no detail";
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            if (nl >= 0) s = s.Substring(0, nl);
            return s.Length > 160 ? s.Substring(0, 160) + "..." : s;
        }

        private static int OrDefault(int v, int d) { return v > 0 && v < 65536 ? v : d; }
    }
}
