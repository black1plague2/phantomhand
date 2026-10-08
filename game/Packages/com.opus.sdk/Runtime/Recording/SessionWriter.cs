using System.IO;
using Newtonsoft.Json;

namespace Opus.Sdk
{
    /// <summary>Writes/updates session.json at the root of a session directory. Safe to call repeatedly
    /// (e.g. after every block) so a crash mid-session still leaves a resumable, mostly-valid envelope on disk.</summary>
    public sealed class SessionWriter
    {
        public string SessionDir { get; }
        public SessionEnvelope Envelope { get; }

        public SessionWriter(string sessionsRootDir, SessionEnvelope envelope)
        {
            Envelope = envelope;
            SessionDir = Path.Combine(sessionsRootDir, envelope.SessionId);
            Directory.CreateDirectory(SessionDir);
        }

        public string SessionJsonPath => Path.Combine(SessionDir, "session.json");
        public string EventsPath => Path.Combine(SessionDir, "events.ndjson");

        public void Save()
        {
            File.WriteAllText(SessionJsonPath, JsonConvert.SerializeObject(Envelope, Formatting.Indented));
        }

        public static SessionEnvelope Load(string sessionDir)
        {
            var path = Path.Combine(sessionDir, "session.json");
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<SessionEnvelope>(json);
        }
    }
}
