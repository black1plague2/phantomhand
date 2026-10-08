using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Opus.Sdk
{
    public sealed class OutboxEntry
    {
        [JsonProperty("file_name")] public string FileName;
        [JsonProperty("uploaded")] public bool Uploaded;
        [JsonProperty("attempts")] public int Attempts;
    }

    public sealed class OutboxManifest
    {
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("entries")] public List<OutboxEntry> Entries = new List<OutboxEntry>();
    }

    /// <summary>
    /// Local-first, resumable upload queue: session files (session.json, events.ndjson, kin_###.json) live
    /// on disk under the session directory regardless of network state. `outbox.json` records which files
    /// have been acked so a crash or app kill mid-session resumes without re-uploading or losing track
    /// (ARCHITECTURE.md §3: "Outbox: local-first, chunked, resumable").
    /// </summary>
    public sealed class Outbox
    {
        public string SessionDir { get; }
        public string SessionId { get; }
        private readonly string _manifestPath;
        private OutboxManifest _manifest;

        public Outbox(string sessionDir, string sessionId)
        {
            SessionDir = sessionDir;
            SessionId = sessionId;
            _manifestPath = Path.Combine(sessionDir, "outbox.json");
            _manifest = LoadOrCreate();
        }

        private OutboxManifest LoadOrCreate()
        {
            if (File.Exists(_manifestPath))
            {
                var loaded = JsonConvert.DeserializeObject<OutboxManifest>(File.ReadAllText(_manifestPath));
                if (loaded != null) return loaded;
            }
            return new OutboxManifest { SessionId = SessionId };
        }

        public void Save()
        {
            Directory.CreateDirectory(SessionDir);
            File.WriteAllText(_manifestPath, JsonConvert.SerializeObject(_manifest, Formatting.Indented));
        }

        /// <summary>Register a file as pending upload (idempotent — calling twice for the same name is a no-op).</summary>
        public void Enqueue(string fileName)
        {
            if (_manifest.Entries.Any(e => e.FileName == fileName)) return;
            _manifest.Entries.Add(new OutboxEntry { FileName = fileName, Uploaded = false });
            Save();
        }

        public IReadOnlyList<OutboxEntry> PendingEntries => _manifest.Entries.Where(e => !e.Uploaded).ToList();
        public IReadOnlyList<OutboxEntry> AllEntries => _manifest.Entries;

        /// <summary>Uploads every pending file via `transport`. Marks each acked on success; leaves it pending
        /// (bumping attempts) on failure so a later resume retries it. Never re-uploads an already-acked file.</summary>
        public async Task<int> FlushAsync(ITransport transport)
        {
            int uploaded = 0;
            foreach (var entry in _manifest.Entries.Where(e => !e.Uploaded).ToList())
            {
                var path = Path.Combine(SessionDir, entry.FileName);
                if (!File.Exists(path)) continue; // not written yet; will be enqueued+flushed later
                entry.Attempts++;
                bool ok = await transport.UploadFileAsync(SessionId, entry.FileName, path);
                if (ok)
                {
                    entry.Uploaded = true;
                    uploaded++;
                }
            }
            Save();
            return uploaded;
        }

        /// <summary>Re-derive an Outbox from whatever is already on disk (crash-resume): every session file
        /// present in the directory but missing from a stale/missing manifest gets enqueued as pending.</summary>
        public static Outbox Resume(string sessionDir, string sessionId)
        {
            var outbox = new Outbox(sessionDir, sessionId);
            if (!Directory.Exists(sessionDir)) return outbox;

            foreach (var file in Directory.GetFiles(sessionDir))
            {
                var name = Path.GetFileName(file);
                if (name == "outbox.json") continue;
                outbox.Enqueue(name); // no-op if already tracked (and already-acked entries are left alone)
            }
            return outbox;
        }
    }
}
