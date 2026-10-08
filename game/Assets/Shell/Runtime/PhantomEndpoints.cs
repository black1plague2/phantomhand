using System;
using System.Globalization;

namespace Opus.Shell
{
    /// <summary>
    /// Where the Phantom Hand scene finds its peers: hub, Node A (haptic), Node B (bio) and the discovery port. Plain C#
    /// (EditMode-testable). Precedence per field: environment (OPUS_PH_HUB / OPUS_PH_NODE_A / OPUS_PH_NODE_B /
    /// OPUS_PH_DISCOVERY_PORT, "host:port") &gt; <see cref="PhantomHandSettings"/> &gt; discovery (null host).
    /// </summary>
    public sealed class PhantomEndpoints
    {
        public const string EnvHub = "OPUS_PH_HUB";
        public const string EnvNodeA = "OPUS_PH_NODE_A";
        public const string EnvNodeB = "OPUS_PH_NODE_B";
        public const string EnvDiscoveryPort = "OPUS_PH_DISCOVERY_PORT";
        public const int DefaultCommandPort = 8790;
        public const int DefaultDiscoveryPort = 8791;
        public const int DefaultHubPort = 8787;

        public string HubHost; public int HubPort;
        public string NodeAHost; public int NodeAPort = DefaultCommandPort;
        public string NodeBHost; public int NodeBPort = DefaultCommandPort;
        public int DiscoveryPort = DefaultDiscoveryPort;
        /// <summary>True when any value came from the environment (the harness is driving: the session runner then runs even without a headset).</summary>
        public bool FromEnvironment;

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
            var e = new PhantomEndpoints();
            if (settings != null)
            {
                string h; int p;
                ParseHostPort(settings.hubHost, out h, out p); e.HubHost = h; e.HubPort = p;
                ParseHostPort(settings.nodeAHost, out h, out p); e.NodeAHost = h; e.NodeAPort = p > 0 ? p : OrDefault(settings.nodeACommandPort, DefaultCommandPort);
                ParseHostPort(settings.nodeBHost, out h, out p); e.NodeBHost = h; e.NodeBPort = p > 0 ? p : OrDefault(settings.nodeBCommandPort, DefaultCommandPort);
                e.DiscoveryPort = OrDefault(settings.discoveryPort, DefaultDiscoveryPort);
            }
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
            }
            return e;
        }

        private static int OrDefault(int v, int d) { return v > 0 && v < 65536 ? v : d; }
    }
}
