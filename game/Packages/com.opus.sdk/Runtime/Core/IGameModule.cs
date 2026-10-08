using System;

namespace Opus.Sdk
{
    /// <summary>
    /// Contract every plug-in game implements. The shell discovers implementations
    /// through <see cref="GameRegistry"/> (via <see cref="GameModuleAttribute"/>) — never by hand-wiring.
    /// </summary>
    public interface IGameModule
    {
        /// <summary>Static manifest describing this game (id, version, param schema, events, metrics).</summary>
        GameManifest Manifest { get; }

        /// <summary>Bound + validated parameters for the current block. Call before Begin().</summary>
        void Configure(ParamSet parameters, ISessionContext session);

        void Begin();
        void Pause();
        void Resume();

        /// <summary>Ends the block. Must be safe to call even if Begin() was never reached (e.g. abort).</summary>
        void End();

        /// <summary>Raised for every trial/session event the game wants recorded. The shell subscribes exactly once, at Configure time.</summary>
        event Action<TrialEvent> OnTrialEvent;
    }

    /// <summary>What a game module needs from the shell/SDK without depending on it directly (keeps games testable without Unity/XR).</summary>
    public interface ISessionContext
    {
        SessionClock Clock { get; }
        int BlockIndex { get; }
    }

    /// <summary>Marks an <see cref="IGameModule"/> implementation for automatic discovery by <see cref="GameRegistry"/>.
    /// No per-game edits anywhere in the SDK or shell are required beyond adding this attribute.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GameModuleAttribute : Attribute
    {
        public string GameId { get; }
        public GameModuleAttribute(string gameId) => GameId = gameId;
    }
}
