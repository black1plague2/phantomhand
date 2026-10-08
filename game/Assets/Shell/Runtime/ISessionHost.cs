using System;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Shell
{
    /// <summary>
    /// What <see cref="OpusSessionRunner"/> needs from a game's scene controller (PH U5). Implemented by BOTH
    /// <see cref="OrchardReachSceneController"/> and <see cref="PhantomHandSceneController"/>, so the hub link, session
    /// recording and upload code exist once. Everything game-specific (status fields, extra operator commands, the
    /// session envelope's block list) is a hook on this interface; the runner never reads a game module directly except
    /// for the Orchard-only parts it guards with a type check (HUD, per-trial live metrics, peak speed).
    /// </summary>
    public interface ISessionHost
    {
        // ---- identity -----------------------------------------------------------------------------------------
        /// <summary>Game id as in the manifest and in program blocks (orchard_reach, phantom_hand).</summary>
        string GameId { get; }
        string GameVersion { get; }
        /// <summary>session.json contracts_version written for this game (Orchard 0.1, Phantom Hand 0.2).</summary>
        string ContractsVersion { get; }
        /// <summary>session.json calibration.affected_side (left|right|both|none).</summary>
        string AffectedSide { get; }
        /// <summary>Measured hand-tracking rate for session.json device.tracking_rate_hz, or null to keep the nominal 72.</summary>
        double? MeasuredTrackingRateHz { get; }
        /// <summary>Hub host (no port) the game wants to use before PlayerPrefs / discovery, or null.</summary>
        string HubHostHint { get; }

        // ---- state --------------------------------------------------------------------------------------------
        bool UsingRealHands { get; }
        IHandSource Hands { get; }
        SessionClock Clock { get; }
        float HeadTrackedSeconds { get; }
        /// <summary>The game module is currently running (not paused, not finished).</summary>
        bool IsModuleRunning { get; }
        /// <summary>The module has played its whole plan (the runner then finishes the session as "completed").</summary>
        bool IsSessionComplete { get; }
        /// <summary>True when this scene instance has already played a session and cannot start another in place
        /// (Phantom Hand: reload for the next person). The runner then calls <see cref="PrepareNextSession"/> instead.</summary>
        bool NeedsFreshScene { get; }
        /// <summary>The scene wants the runner to start a session now without waiting for a clinician or a pinch.</summary>
        bool AutoStartRequested { get; }
        string AutoStartReason { get; }

        // ---- events -------------------------------------------------------------------------------------------
        event Action<TrialEvent> OnTrialEvent;
        /// <summary>(window_trials, metrics) for a live metrics_tick, raised by games that compute them (Phantom Hand).</summary>
        event Action<int, JObject> OnMetricsTick;

        // ---- lifecycle ----------------------------------------------------------------------------------------
        void StartNewSession(JObject overrides);
        void PauseSession();
        void ResumeSession();
        void EndSession();
        bool Recenter();
        void StartKinematicsRecording(string sessionId, string sessionDir, double rateHz = 72.0);
        /// <summary>Flush every recorder (kinematics, sensors); returns the number of kin_###.json chunks written.</summary>
        int StopKinematicsRecording();
        /// <summary>End the current scene instance and arrange for a fresh one that auto-starts (Phantom Hand next_person).</summary>
        void PrepareNextSession(string why);

        // ---- hooks --------------------------------------------------------------------------------------------
        /// <summary>Handle a game-specific operator command. Returns true when the command was recognised; the ack is
        /// ok when <paramref name="error"/> is null.</summary>
        bool TryHandleCommand(string command, JObject parameters, out string error);
        /// <summary>Add the game's fields to a status payload (Phantom Hand: game_id, game_state, trace). Orchard: no-op.</summary>
        void FillStatus(JObject status, double nowMs);
        /// <summary>Fill session.json blocks[] (Phantom Hand). Called at session start (ended=false) and end (ended=true).</summary>
        void DescribeBlocks(SessionEnvelope envelope, bool ended, bool completed);
    }
}
