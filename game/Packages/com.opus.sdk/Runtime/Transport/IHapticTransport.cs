using System;

namespace Opus.Sdk
{
    /// <summary>
    /// Transport abstraction for the haptic sleeve link (contracts/HAPTIC_PROTOCOL.md). v1 implementation is
    /// <see cref="UdpHapticTransport"/>; a future BLE transport (v2, Quest has no built-in BLE central API — an
    /// Android plugin bridge would be needed) implements this same interface so <see cref="HapticClient"/>'s
    /// cue/mapping/safety code never changes when the transport does.
    /// All methods are non-blocking / fire-and-forget: a missing or unresponsive sleeve must never stall the
    /// caller (the game loop).
    /// </summary>
    public interface IHapticTransport : IDisposable
    {
        /// <summary>Begin background send/receive/discovery work. Safe to call once.</summary>
        void Start();

        /// <summary>Stop all background work. Safe to call multiple times.</summary>
        void Stop();

        /// <summary>Enqueue a raw JSON datagram to the currently known device (no-op, silently, if no device has
        /// been discovered/configured yet — a missing sleeve is not an error). Returns immediately; the actual
        /// socket write happens on a background queue.</summary>
        void Send(string json);

        /// <summary>True once a device endpoint is known (via discovery or manual host) and sends are possible.</summary>
        bool HasDevice { get; }

        /// <summary>Raised (on a background thread — callers marshal to main thread themselves) for every
        /// inbound datagram's raw JSON text (status/ack/imu/hello).</summary>
        event Action<string> OnMessage;

        /// <summary>Raised the moment a device is discovered or configured.</summary>
        event Action OnDeviceKnown;
    }
}
