using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Opus.Sdk;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Opus.Shell
{
    /// <summary>
    /// The only component of the Bootstrap scene (build index 0). Picks the game scene and loads it:
    ///   1. game id from env OPUS_GAME, else the program the hub sends (assign_program block game_id) within
    ///      <see cref="PhantomHandSettings.bootstrapProgramWaitSec"/> s, else PlayerPrefs "opus_game_id", else
    ///      <see cref="PhantomHandSettings.defaultGameId"/>;
    ///   2. the program that named the game is acked here and handed on (<see cref="ProgramHandoff.PendingProgram"/>) so the game's
    ///      session runner applies it, and this scene's hub client is disposed before the game scene opens its own.
    /// Scene names: phantom_hand = "PhantomHand", orchard_reach = "OrchardReach" (both in Build Settings; see BootstrapSceneBuilder).
    /// Without a hub (or with wait = 0) the default game opens at once, so a headset with no laptop still plays.
    /// </summary>
    public sealed class BootstrapLoader : MonoBehaviour
    {
        public static string SceneFor(string gameId)
        {
            switch (gameId)
            {
                case "orchard_reach": return "OrchardReach";
                case "phantom_hand": return "PhantomHand";
                default: return null;
            }
        }

        [SerializeField] private PhantomHandSettings settings;

        private LiveClient _client;
        private float _t;
        private bool _loading;
        private float _wait;
        private string _deviceId;

        private void Start()
        {
            AndroidMulticastLock.Acquire();   // Quest: without it Android drops the hub and node UDP beacons
            if (settings == null) settings = Resources.Load<PhantomHandSettings>(PhantomHandSettings.ResourcePath);
            Func<string, string> env = PhantomHandOverrides.Env ?? Environment.GetEnvironmentVariable;   // the game scene's reader (tests)
            string forced = env("OPUS_GAME");
            if (!string.IsNullOrEmpty(forced) && SceneFor(forced) != null) { Load(forced, "env OPUS_GAME"); return; }

            _wait = settings != null ? settings.bootstrapProgramWaitSec : 0f;
            var ep = PhantomEndpoints.Resolve(env, settings);
            if (_wait <= 0f) { Load(DefaultGame(), "no wait"); return; }

            _deviceId = (Application.isEditor ? "editor-" : "quest-") + SystemInfo.deviceUniqueIdentifier.Replace("-", "").Substring(0, 8).ToLowerInvariant();
            string host = ep.HubHost;
            if (string.IsNullOrWhiteSpace(host)) host = PlayerPrefs.GetString("opus_hub_host", "");
            var games = AdvertisedGames();   // asked here: the client calls its providers on a background thread, and this one uses a Unity API
            _client = new LiveClient(_deviceId,
                () => new JObject { ["shell"] = "0.2.0", ["sdk"] = "0.1.0" },
                () => games,       // only the games whose scene is in this build
                string.IsNullOrWhiteSpace(host) ? null : host.Trim());
            _client.OnCommand += OnCommand;
            _client.Start();
        }

        /// <summary>hello.games: a game is offered only when its scene can be loaded (the Phantom Hand APK holds Bootstrap + PhantomHand only).</summary>
        private static JArray AdvertisedGames()
        {
            var games = new JArray();
            if (Application.CanStreamedLevelBeLoaded(SceneFor("phantom_hand"))) games.Add(new JObject { ["id"] = "phantom_hand", ["version"] = "0.1.0" });
            if (Application.CanStreamedLevelBeLoaded(SceneFor("orchard_reach"))) games.Add(new JObject { ["id"] = "orchard_reach", ["version"] = "0.2.0" });
            return games;
        }

        private string DefaultGame()
        {
            string g = PlayerPrefs.GetString("opus_game_id", "");
            if (!string.IsNullOrEmpty(g) && SceneFor(g) != null) return g;
            g = settings != null ? settings.defaultGameId : null;
            return !string.IsNullOrEmpty(g) && SceneFor(g) != null ? g : "phantom_hand";
        }

        private void OnCommand(string command, JObject p, string ackId)
        {
            if (_loading || command != "assign_program") { if (command != "assign_program") _client?.AckCommand(ackId, false, "game not loaded yet"); return; }
            var block = ((p?["program"] as JObject)?["blocks"] as JArray)?.OfType<JObject>().FirstOrDefault(b => SceneFor(b["game_id"]?.Value<string>()) != null);
            if (block == null) { _client.AckCommand(ackId, false, "no known game in the program"); return; }
            _client.AckCommand(ackId, true);
            ProgramHandoff.PendingProgram = (JObject)p.DeepClone();
            Load(block["game_id"].Value<string>(), "program");
        }

        private void Update()
        {
            if (_loading || _client == null) return;
            _client.Pump(Time.realtimeSinceStartupAsDouble * 1000.0);
            _t += Time.unscaledDeltaTime;
            if (_t >= _wait) Load(DefaultGame(), "wait elapsed");
        }

        private void Load(string gameId, string why)
        {
            if (_loading) return;
            _loading = true;
            // Let the ack leave before the socket goes away (the game scene's runner reconnects within a second). Not a coroutine: it
            // dies with this scene before it has run, and a client that is never stopped keeps reconnecting under this headset's device
            // id, so the hub drops the game's own link every second (first run on a headset, 8 Oct 2026: "Connected" and
            // "Reconnecting" in turn, 2 s apart; the editor tests start in the game scene and never saw it).
            var c = _client; _client = null;
            if (c != null) System.Threading.Tasks.Task.Delay(300).ContinueWith(_ => c.Dispose());
            string scene = SceneFor(gameId);
            if (!Application.CanStreamedLevelBeLoaded(scene)) { why += ", '" + gameId + "' is not in this build"; gameId = "phantom_hand"; scene = SceneFor(gameId); }
            Debug.Log($"[OPUS] Bootstrap: opening '{scene}' for game '{gameId}' ({why})");
            SceneManager.LoadScene(scene);
        }

        private void OnDestroy() { if (_client != null) _client.Dispose(); }
    }
}
