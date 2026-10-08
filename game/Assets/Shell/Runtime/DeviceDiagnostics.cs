using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace Opus.Shell
{
    /// <summary>
    /// The headset's own diagnostics channel, for a run with no USB cable. From the first scene on:
    ///   1. every Unity log line (errors and exceptions with their stack) goes to persistentDataPath/logs/&lt;utc&gt;_&lt;boot&gt;.log,
    ///      flushed per line so a crash loses nothing that was logged, plus one health line every 5 s (frame times, memory, battery
    ///      and what the scene says about itself, <see cref="Status"/>);
    ///   2. a read-only HTTP server on TCP <see cref="Port"/> hands out that folder, the session folders (opus_sessions/) and
    ///      phantom_endpoints.json, so a PC on the same Wi-Fi mirrors them while the app runs (tools/demo/quest_collect.py; a
    ///      browser on http://&lt;headset&gt;:8796/ works too). The PC connects to the headset, so no firewall rule is needed on the PC;
    ///   3. one UDP datagram to port <see cref="Port"/> (broadcast) every 2 s says where the headset is.
    /// Routes: GET / or /?since=&lt;ms&gt; = JSON index (files changed after that time of the headset's clock); GET /f/&lt;path&gt;?offset=&lt;n&gt;
    /// = the file's bytes from n. Nothing is written or run on request and anything outside those folders is a 404. Same trust
    /// model as the hub link (LAN, no authentication; the files carry patient_ref, never a name).
    /// Off: PhantomHandSettings.diagnosticsServer. The game does not depend on it: a port that cannot be opened is one info line.
    /// </summary>
    public static class DeviceDiagnostics
    {
        public const int Port = 8796;
        public const string LogsDir = "logs", SessionsDir = "opus_sessions";
        private const int KeepLogs = 10;
        private const long MaxLogBytes = 32L * 1024 * 1024;

        /// <summary>One line about the running scene for the health line (set by the scene's controller; called on the main thread).</summary>
        public static Func<string> Status;

        public static string LogPath { get { return _root != null && _logRel != null ? Path.Combine(_root, _logRel) : null; } }
        public static string DeviceId { get { return _device; } }
        /// <summary>The TCP port the server listens on, 0 when it does not.</summary>
        public static int BoundPort { get; private set; }

        private static readonly object Gate = new object();
        private static readonly StringBuilder Sb = new StringBuilder(512);
        private static FileStream _file;
        private static StreamWriter _log;
        private static string _root, _logRel, _device, _boot, _app;
        private static string _lastMessage;
        private static char _lastLevel;
        private static int _repeats;
        private static bool _full;
        private static TcpListener _listener;
        private static UdpClient _udp;
        private static Thread _thread;
        private static volatile bool _running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            var settings = Resources.Load<PhantomHandSettings>(PhantomHandSettings.ResourcePath);
            if (settings != null && !settings.diagnosticsServer) return;
            Start(Application.persistentDataPath);
            var go = new GameObject("DeviceDiagnostics");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Pulse>();
        }

        /// <param name="port">0 = any free port (tests), see <see cref="BoundPort"/>.</param>
        /// <param name="announce">false = no UDP datagrams (tests).</param>
        public static void Start(string root, int port = Port, bool announce = true)
        {
            Stop();
            string id = SystemInfo.deviceUniqueIdentifier.Replace("-", "");
            _device = (Application.isEditor ? "editor-" : "quest-") + (id.Length > 8 ? id.Substring(0, 8) : id).ToLowerInvariant();   // the session runner's device id
            _boot = Guid.NewGuid().ToString("N").Substring(0, 8);
            _app = Application.version;
            try
            {
                _root = Path.GetFullPath(root);
                var dir = Directory.CreateDirectory(Path.Combine(_root, LogsDir));
                foreach (var old in dir.GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepLogs - 1))
                    try { old.Delete(); } catch (Exception) { }
                var now = DateTime.UtcNow;
                _logRel = LogsDir + "/" + now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "_" + _boot + ".log";
                lock (Gate)
                {
                    _file = new FileStream(Path.Combine(_root, _logRel), FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
                    _log = new StreamWriter(_file, new UTF8Encoding(false)) { AutoFlush = true };
                    _full = false; _lastMessage = null; _repeats = 0;
                    _log.Write("# Chetna device log: device " + _device + ", boot " + _boot + ", started " + now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
                               ". Each record starts with the UTC time and I (info), W (warning), E (error) or X (exception); its further lines start with a tab.\n" +
                               "# app " + _app + " " + Application.identifier + ", Unity " + Application.unityVersion + ", " + SystemInfo.deviceModel + ", " +
                               SystemInfo.operatingSystem + ", " + SystemInfo.systemMemorySize + " MB RAM, " + SystemInfo.graphicsDeviceName + "\n");
                }
                Application.logMessageReceivedThreaded += OnLog;
            }
            catch (Exception e) { Debug.Log("[OPUS] diagnostics: no log file (" + e.Message + ")"); }

            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start(8);
                _listener = l;
                BoundPort = ((IPEndPoint)l.LocalEndpoint).Port;
                if (announce) _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
                _running = true;
                _thread = new Thread(Serve) { IsBackground = true, Name = "opus-diag" };
                _thread.Start(l);
                Debug.Log("[OPUS] diagnostics: " + _device + " serves its log and session files on http://" + (LocalIPv4() ?? "<this device>") + ":" + BoundPort + "/ ; log file " + LogPath);
            }
            catch (Exception e)
            {
                _listener = null; BoundPort = 0;
                Debug.Log("[OPUS] diagnostics: port " + port + " not opened (" + e.Message + "); the log is still written to " + LogPath);
            }
        }

        public static void Stop()
        {
            _running = false;
            Application.logMessageReceivedThreaded -= OnLog;
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { }
            _listener = null; BoundPort = 0;
            try { if (_udp != null) _udp.Close(); } catch (Exception) { }
            _udp = null;
            if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(500);
            _thread = null;
            lock (Gate)
            {
                if (_log != null) { try { FlushRepeats(); _log.Dispose(); } catch (Exception) { } }
                _log = null; _file = null;
            }
        }

        // ---- the log file ---------------------------------------------------------------------------------------------------

        private static void OnLog(string message, string stack, LogType type)
        {
            char level = type == LogType.Log ? 'I' : type == LogType.Warning ? 'W' : type == LogType.Exception ? 'X' : 'E';
            lock (Gate)
            {
                if (_log == null || _full) return;
                // The same line again and again (an exception in every frame) is written once, then counted: it would fill the file in minutes.
                if (level == _lastLevel && message == _lastMessage) { _repeats++; return; }
                FlushRepeats();
                _lastLevel = level; _lastMessage = message;
                Write(level, message, level == 'E' || level == 'X' ? stack : null);
            }
        }

        /// <summary>A line of the diagnostics' own (health, markers): to the file only, not to the Unity console.</summary>
        public static void Line(string text)
        {
            lock (Gate)
            {
                if (_log == null || _full) return;
                FlushRepeats();
                _lastMessage = null;   // a line that keeps repeating is written again after each of these
                Write('I', text, null);
            }
        }

        private static void FlushRepeats()
        {
            if (_repeats > 0) Write('I', "(the line above came " + _repeats + " more times)", null);
            _repeats = 0;
        }

        private static void Write(char level, string message, string stack)
        {
            try
            {
                Sb.Length = 0;
                Sb.Append(message ?? "");
                if (!string.IsNullOrEmpty(stack)) Sb.Append('\n').Append(stack.TrimEnd());
                Sb.Replace("\r", "").Replace("\n", "\n\t");
                _log.Write(DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + level + " " + Sb + "\n");
                if (_file.Position > MaxLogBytes)
                {
                    _log.Write("# log full (" + MaxLogBytes + " bytes): nothing more is written in this run\n");
                    _full = true;
                }
            }
            catch (Exception) { /* a full disk must not take the game down */ }
        }

        // ---- the server -----------------------------------------------------------------------------------------------------

        private static void Serve(object state)
        {
            var listener = (TcpListener)state;
            var sinceHello = System.Diagnostics.Stopwatch.StartNew();
            bool first = true;
            while (_running)
            {
                try
                {
                    if (_udp != null && (first || sinceHello.ElapsedMilliseconds >= 2000)) { first = false; sinceHello.Restart(); Announce(); }
                    // Polled, not a blocking accept: closing a listener does not reliably wake a thread blocked in accept on Android.
                    if (!listener.Pending()) { Thread.Sleep(40); continue; }
                    var c = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => Handle(c));
                }
                catch (Exception) { if (_running) Thread.Sleep(200); }
            }
        }

        private static void Announce()
        {
            string ip = LocalIPv4();
            long bytes;
            lock (Gate) bytes = _file != null ? _file.Position : 0;
            byte[] d = Utf8(new JObject
            {
                ["opus_diag"] = 1, ["device"] = _device, ["boot"] = _boot, ["port"] = BoundPort, ["ip"] = ip, ["app"] = _app,
                ["log"] = _logRel, ["log_bytes"] = bytes,
            }.ToString(Formatting.None));
            SendTo(d, IPAddress.Broadcast);
            IPAddress a;
            if (ip != null && IPAddress.TryParse(ip, out a))
            {
                var b = a.GetAddressBytes(); b[3] = 255;   // the /24 broadcast as well: some access points drop 255.255.255.255
                SendTo(d, new IPAddress(b));
            }
        }

        private static void SendTo(byte[] d, IPAddress to)
        {
            try { var u = _udp; if (u != null) u.Send(d, d.Length, new IPEndPoint(to, Port)); } catch (Exception) { }
        }

        /// <summary>The address this device uses on the LAN, or null. No packet is sent: a UDP "connect" only picks the route.</summary>
        public static string LocalIPv4()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("192.0.2.1", 9);   // TEST-NET-1: never a real host
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception) { return null; }
        }

        private static void Handle(TcpClient c)
        {
            try
            {
                using (c)
                {
                    c.ReceiveTimeout = 3000; c.SendTimeout = 15000; c.NoDelay = true;
                    var s = c.GetStream();
                    string path; long number;
                    if (!TryParseRequest(ReadRequestLine(s), out path, out number)) { Reply(s, "400 Bad Request", "text/plain", "GET only\n"); return; }
                    if (path == "/") { Reply(s, "200 OK", "application/json", IndexJson(number)); return; }
                    string full = path.StartsWith("/f/", StringComparison.Ordinal) ? Resolve(_root, path.Substring(3)) : null;
                    if (full == null || !File.Exists(full)) { Reply(s, "404 Not Found", "text/plain", "not a served file\n"); return; }
                    SendFile(s, full, number);
                }
            }
            catch (Exception) { /* the client went away: it asks again */ }
        }

        /// <summary>Reads the whole request head (a GET has no body): closing with unread bytes would reset the connection and cut the answer.</summary>
        private static string ReadRequestLine(Stream s)
        {
            var buf = new byte[8192]; int n = 0;
            while (n < buf.Length)
            {
                int r = s.Read(buf, n, buf.Length - n);
                if (r <= 0) break;
                n += r;
                if (n >= 4 && buf[n - 4] == '\r' && buf[n - 3] == '\n' && buf[n - 2] == '\r' && buf[n - 1] == '\n') break;
            }
            string head = Encoding.ASCII.GetString(buf, 0, n);
            int eol = head.IndexOf('\n');
            return eol > 0 ? head.Substring(0, eol).TrimEnd('\r') : head;
        }

        /// <summary>"GET /f/logs/a.log?offset=120 HTTP/1.1" to ("/f/logs/a.log", 120): the path percent-decoded, the number the value of
        /// offset= or since= (0 when absent or negative). False for anything that is not a GET with a path.</summary>
        public static bool TryParseRequest(string line, out string path, out long number)
        {
            path = null; number = 0;
            if (line == null) return false;
            var parts = line.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET" || !parts[1].StartsWith("/", StringComparison.Ordinal)) return false;
            string target = parts[1];
            int q = target.IndexOf('?');
            try { path = Uri.UnescapeDataString(q >= 0 ? target.Substring(0, q) : target); }
            catch (Exception) { path = null; return false; }
            if (q >= 0)
                foreach (var kv in target.Substring(q + 1).Split('&'))
                    if (kv.StartsWith("offset=", StringComparison.Ordinal) || kv.StartsWith("since=", StringComparison.Ordinal))
                        long.TryParse(kv.Substring(kv.IndexOf('=') + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
            if (number < 0) number = 0;
            return true;
        }

        /// <summary>The absolute path of a file this server may hand out, or null: only what lies under logs/ or opus_sessions/ of the data
        /// folder (and phantom_endpoints.json) once ".." and the separators are resolved. The request path is untrusted input.</summary>
        public static string Resolve(string root, string rel)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(rel) || rel.IndexOf('\0') >= 0) return null;
            try
            {
                bool windows = Path.DirectorySeparatorChar == '\\';
                var cmp = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                string baseDir = BaseDir(root);
                string full = Path.GetFullPath(Path.Combine(baseDir, rel));   // an absolute rel replaces baseDir here and fails the next line
                if (!full.StartsWith(baseDir, cmp)) return null;
                string inside = full.Substring(baseDir.Length);
                if (windows) inside = inside.Replace('\\', '/');
                bool served = inside.StartsWith(LogsDir + "/", cmp) || inside.StartsWith(SessionsDir + "/", cmp) || string.Equals(inside, PhantomEndpoints.FileName, cmp);
                return served ? full : null;
            }
            catch (Exception) { return null; }
        }

        private static string BaseDir(string root)
        {
            return Path.GetFullPath(root).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        }

        private static string IndexJson(long sinceMs)
        {
            var files = new JArray();
            string baseDir = BaseDir(_root);
            long now = UnixMs(DateTime.UtcNow);
            // The log of this run is always listed, with the size we wrote: the folder's own size and time of a file that is still
            // open lag behind (on Windows until it is closed, so there an open events.ndjson shows late too; on the headset they are current).
            long logBytes = -1;
            lock (Gate) if (_file != null) logBytes = _file.Position;
            if (logBytes >= 0) files.Add(new JObject { ["path"] = _logRel, ["size"] = logBytes, ["mtime_ms"] = now });
            string logName = Path.GetFileName(_logRel);   // unique: it carries the boot id
            foreach (string top in new[] { LogsDir, SessionsDir })
            {
                var dir = new DirectoryInfo(Path.Combine(_root, top));
                if (!dir.Exists) continue;
                foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                    if (logBytes < 0 || f.Name != logName) AddFile(files, f, baseDir, sinceMs);
            }
            AddFile(files, new FileInfo(Path.Combine(_root, PhantomEndpoints.FileName)), baseDir, sinceMs);
            return new JObject
            {
                ["opus_diag"] = 1, ["device"] = _device, ["boot"] = _boot, ["app"] = _app,
                ["now_ms"] = now, ["log"] = _logRel, ["files"] = files,
            }.ToString(Formatting.None);
        }

        private static void AddFile(JArray files, FileInfo f, string baseDir, long sinceMs)
        {
            try
            {
                if (!f.Exists) return;
                long m = UnixMs(f.LastWriteTimeUtc);
                if (m <= sinceMs) return;
                files.Add(new JObject { ["path"] = f.FullName.Substring(baseDir.Length).Replace('\\', '/'), ["size"] = f.Length, ["mtime_ms"] = m });
            }
            catch (Exception) { /* removed while we were listing */ }
        }

        private static long UnixMs(DateTime utc)
        {
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private static void SendFile(Stream s, string full, long offset)
        {
            FileStream f;
            try { f = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch (IOException) { Reply(s, "503 Service Unavailable", "text/plain", "busy, ask again\n"); return; }   // the game is rewriting it right now
            using (f)
            {
                long left = Math.Max(0, f.Length - offset);
                WriteHead(s, "200 OK", "application/octet-stream", left);
                if (left > 0) f.Seek(offset, SeekOrigin.Begin);
                var buf = new byte[64 * 1024];
                while (left > 0)
                {
                    int r = f.Read(buf, 0, (int)Math.Min(buf.Length, left));
                    if (r <= 0) break;
                    s.Write(buf, 0, r);
                    left -= r;
                }
            }
        }

        private static void Reply(Stream s, string status, string type, string body)
        {
            byte[] b = Utf8(body);
            WriteHead(s, status, type, b.Length);
            s.Write(b, 0, b.Length);
        }

        private static void WriteHead(Stream s, string status, string type, long length)
        {
            byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " +
                                               length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
            s.Write(h, 0, h.Length);
        }

        private static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s); }

        // ---- the health line ------------------------------------------------------------------------------------------------

        private sealed class Pulse : MonoBehaviour
        {
            private const float Every = 5f;
            private float _t, _worst;
            private int _frames, _slow;
            private bool _started;

            private void Update()
            {
                if (!_started) { _started = true; return; }   // the first frame's time is the app's start-up, not a frame
                float dt = Time.unscaledDeltaTime;
                _t += dt; _frames++;
                if (dt > _worst) _worst = dt;
                double hz = Screen.currentResolution.refreshRateRatio.value;
                if (dt > 1.5 / (hz > 1 ? hz : 72.0)) _slow++;   // a frame that missed its slot by half a frame or more
                if (_t < Every) return;

                string status = null;
                try { var f = Status; if (f != null) status = f(); }
                catch (Exception e) { status = "status failed: " + e.Message; }
                float battery = SystemInfo.batteryLevel;
                Line(string.Format(CultureInfo.InvariantCulture, "[health] {0:F0} fps, worst frame {1:F0} ms, {2} of {3} frames slow, memory {4} MB engine + {5} MB scripts, battery {6}{7}",
                    _frames / _t, _worst * 1000f, _slow, _frames, Profiler.GetTotalAllocatedMemoryLong() >> 20, Profiler.GetMonoUsedSizeLong() >> 20,
                    battery < 0 ? "unknown" : Mathf.RoundToInt(battery * 100f) + " % " + SystemInfo.batteryStatus, status != null ? "; " + status : ""));
                _t = 0; _worst = 0; _frames = 0; _slow = 0;
            }

            private void OnApplicationPause(bool paused)
            {
                Line(paused ? "[health] app paused (headset taken off, or the system menu is open)" : "[health] app resumed");
            }

            private void OnDestroy() { Stop(); }
        }
    }
}
