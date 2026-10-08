using System.Threading.Tasks;

namespace Opus.Sdk
{
    /// <summary>Abstraction over how session artifacts leave the device. Same surface for Mock (Phase 1, local
    /// loopback), Lan (clinic relay) and Cloud (Phase 3) transports — see ARCHITECTURE.md §3/§7.</summary>
    public interface ITransport
    {
        Task<bool> UploadFileAsync(string sessionId, string relativeFileName, string absolutePath);
    }

    /// <summary>Phase 1 transport: "uploading" a file just means it's already durably on disk under
    /// Outbox's session directory. Records what was "sent" for test assertions and loopback debugging.</summary>
    public sealed class MockTransport : ITransport
    {
        public readonly System.Collections.Generic.List<(string sessionId, string fileName)> Sent = new System.Collections.Generic.List<(string, string)>();
        public bool FailNext;

        public Task<bool> UploadFileAsync(string sessionId, string relativeFileName, string absolutePath)
        {
            if (FailNext)
            {
                FailNext = false;
                return Task.FromResult(false);
            }
            Sent.Add((sessionId, relativeFileName));
            return Task.FromResult(true);
        }
    }
}
