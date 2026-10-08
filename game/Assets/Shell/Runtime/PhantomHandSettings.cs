using UnityEngine;

namespace Opus.Shell
{
    /// <summary>
    /// Phantom Hand deployment settings (03-SPEC D8: "manual host fields are always available; discovery is the default, not the
    /// only path"). One asset, <c>Assets/Shell/Resources/PhantomHandSettings.asset</c>, loaded with Resources.Load so it ships in a
    /// player build without scene wiring. Created (if missing) by the editor when the project loads
    /// (<c>BootstrapSceneBuilder</c>) or from the menu "Tools/OPUS/Create Phantom Hand Settings".
    ///
    /// Precedence for every endpoint: environment variable (OPUS_PH_HUB, OPUS_PH_NODE_A, OPUS_PH_NODE_B, OPUS_PH_DISCOVERY_PORT: used
    /// by the L3 harness and the PlayMode PH_FullRun) &gt; this asset &gt; discovery (UDP beacons) / PlayerPrefs "opus_hub_host".
    /// Empty host = discover. Hosts are IPv4 addresses (or "ip:port"); the hotspot's DHCP table shows them.
    /// </summary>
    [CreateAssetMenu(fileName = "PhantomHandSettings", menuName = "OPUS/Phantom Hand Settings")]
    public sealed class PhantomHandSettings : ScriptableObject
    {
        public const string ResourcePath = "PhantomHandSettings";

        [Header("Manual endpoints (empty = discover on the LAN)")]
        [Tooltip("Clinician app / laptop hub IP, no port (the live link always uses 8787). Empty = UDP beacon discovery.")]
        public string hubHost = "";
        [Tooltip("Node A (haptic sleeve) IP. Empty = discovery.")]
        public string nodeAHost = "";
        [Tooltip("Node A command port (firmware default 8790).")]
        public int nodeACommandPort = 8790;
        [Tooltip("Node B (bio / EMG) IP. Empty = discovery.")]
        public string nodeBHost = "";
        [Tooltip("Node B command port (firmware default 8790).")]
        public int nodeBCommandPort = 8790;
        [Tooltip("UDP port the nodes broadcast their discovery beacon to (8791).")]
        public int discoveryPort = 8791;

        [Header("Game")]
        [Tooltip("Manifest of the game (game/Assets/Games/PhantomHand/manifest.json). Filled in by the editor script.")]
        public TextAsset manifest;
        [Tooltip("Bootstrap scene: the game scene to open when the program names no game.")]
        public string defaultGameId = "phantom_hand";
        [Tooltip("Bootstrap scene: seconds to wait for the hub's assign_program (which names the game) before opening the default game. 0 = do not wait.")]
        public float bootstrapProgramWaitSec = 3f;

        [Header("Behaviour")]
        [Tooltip("Start a run as soon as the scene is up and the headset tracks (no clinician Start needed). Off for the clinic; on for a stand-alone kiosk.")]
        public bool autoStartOnHeadset = false;
        [Tooltip("A2/A3 additions (self-touch, dissolve, reveal). Off until gate G2 is green and a teammate test passed.")]
        public bool additionsEnabled = true;
        [Tooltip("Editor / batch (no headset): a scripted participant plays the run so the whole pipeline can be exercised.")]
        public bool scriptedParticipantWithoutHeadset = true;
        [Tooltip("0 = a new random seed per run (logged); otherwise the fixed seed (tests).")]
        public int seed = 0;
    }
}
