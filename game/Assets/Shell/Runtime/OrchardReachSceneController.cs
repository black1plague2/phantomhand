using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Opus.Games.OrchardReach;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Shell
{
    /// <summary>
    /// Unity-facing composition root for Orchard Reach (U-next-run priority #1): loads the manifest, configures
    /// <see cref="OrchardReachModule"/>, drives it every frame with an <see cref="IHandSource"/>, and reflects
    /// its trial events onto the actual scene objects (moves the fruit instance to each sampled target, detects
    /// basket placement via <see cref="BasketTrigger"/>).
    ///
    /// Two hand sources are supported:
    ///  - Real play: <see cref="MetaHandSource"/> over the OVRHandDataSourceLeft/Right already installed by the
    ///    Building Blocks rig (R2). The fruit's own HandGrabInteractable/Grabbable move it physically; this
    ///    controller only repositions it between trials and reads pinch/tracking state for the recorder.
    ///  - Editor/no-headset demo (R5 visual audit): <see cref="SyntheticHandDriver"/> replaying a recorded
    ///    kinematics fixture (see docs/UNITY_PRACTICES.md §6 — a full ISDK DataModifier injection is deferred,
    ///    so there is no physical OVR hand to grab with in this mode). To still show a believable reach/grasp/
    ///    place sequence, this controller directly carries the fruit to the driven hand's index-tip position
    ///    while the module reports Grasped/Holding, driven by the SAME synthetic data used for grasp detection
    ///    (pinch strength) — so the demo's visuals and the module's recorded trial state are consistent with
    ///    each other, even though no physical grab occurred.
    /// </summary>
    public sealed class OrchardReachSceneController : MonoBehaviour, ISessionHost
    {
        [Header("Manifest + params")]
        [SerializeField] private TextAsset manifestJson;
        [SerializeField] private int demoTrialCount = 6;

        [Header("Scene refs")]
        [SerializeField] private GameObject fruitPrefab;
        [SerializeField] private Transform chestReference;
        [SerializeField] private BasketTrigger basketTrigger;
        [SerializeField] private double armLengthM = 0.55;

        /// <summary>Which hands drive the game. Auto = your real tracked hands whenever a headset is active
        /// (Quest standalone, or Quest Link in the editor), otherwise the recorded demo driver (no headset:
        /// batch tests, the editor with no Link).</summary>
        public enum HandMode { Auto, Demo, RealHands }

        [Header("Hands")]
        [SerializeField] private HandMode handMode = HandMode.Auto;
        [Tooltip("Absolute localScale of the spawned FruitInstance.")]
        [SerializeField] private float fruitScale = 2f;
        [Tooltip("Real hands: how close (m) the pinch point must be to the fruit's centre for a pinch to count as a grab.")]
        [SerializeField] private float grabRadiusM = 0.10f;
        [Tooltip("Real hands: how far (m) the wrist must move after the fruit appears before movement onset is recorded.")]
        [SerializeField] private float movementOnsetM = 0.03f;
        [Tooltip("Demo driver only: start trials automatically on load (the PlayMode tests rely on this). Real-hands " +
                 "sessions are always started by OpusSessionRunner.")]
        [SerializeField] private bool autoBeginDemo = true;

        [Header("Demo driver (no headset)")]
        [SerializeField] private bool useDemoDriver = true;
        [SerializeField] private TextAsset[] demoFixtureChunks;
        [SerializeField] private float demoPlaybackSpeed = 1f;
        [SerializeField] private Transform demoHandProxyR; // the index-tip marker (kept as the historical "hand proxy" object PlayMode assertions read)

        // Run9 (Opus task #3, refined): a real hand mesh — the project's own `ghost_hand_static` prefab (asset
        // 286115, MODEL_REQUESTS.md: "static hint only") — replacing the lone sphere. Positioned at the wrist and
        // headed toward the index tip each frame; see DriveGhostHandVisual's doc comment for why this can't be a
        // true per-finger-posed grasp shape or use a "recorded wrist rotation" (the fixture schema/data has none
        // — verified: contracts/fixtures/sessions/healthy/kin_000.json's r_wrist has `"rot": null`).
        [Header("Demo hand visual (Run9)")]
        [SerializeField] private Transform demoHandGhostVisual;

        // Run9 steps 2-4: form feedback (trunk lean / low tracking confidence) + haptics.
        [Header("Form feedback + haptics (Run9)")]
        [SerializeField] private Transform headReference; // CenterEyeAnchor in real play; unused in demo (see below)
        [SerializeField] private string hapticManualHost; // optional manual sleeve IP, bypasses UDP discovery
        // Hands-free demo cannot produce a real head-lean or a real tracking dropout (no headset/hand tracker is
        // present at all in this mode), so it SIMULATES both, once each, early in the demo run, purely so the
        // three haptic cues (trunk_lean, low_confidence, success) all have a chance to fire without a headset —
        // per the brief's step 4. `success` needs no simulation: it already fires from a real successful trial.
        [SerializeField] private bool demoTriggerFormEvents = true;
        private const double DemoLeanSpikeCm = 20.0; // comfortably over any realistic trunkLeanWarningCm (max 30 per manifest)
        // Run12: these were each sized barely above FormFeedback's debounce (600 vs 300 ms, 700 vs 500 ms).
        // That leaves almost no margin: a handful of slow frames, or the scripted windows shifting relative to
        // trial pacing, and a window closes before its debounce has elapsed, so the cue silently never fires.
        // That is exactly the low_confidence flake seen across run11/run12 (cueOrder came back as
        // [trunk_lean,low_confidence], [low_confidence], [trunk_lean,success] and [success] on otherwise
        // identical runs). Widened to ~4x their debounce so a dropped frame or two cannot decide the outcome.
        // These windows only exist for the no-headset demo; real sessions use live head/hand geometry.
        private const double DemoLeanWindowMs = 1200.0;         // FormFeedback.WarningDurationMs = 300
        private const double DemoTrackingLossGapMs = 600.0;      // keeps the two triggers clearly separated
        private const double DemoTrackingLossWindowMs = 2000.0;  // FormFeedback.TrackingLostDurationMs = 500

        // Run9 bugfix: these windows are keyed off SESSION-wide elapsed demo time (since Awake), never reset by
        // a trial boundary — a first pass keyed them off _currentTrialIndex + a per-trial elapsed timer that was
        // reset to 0 on every "trial_start" (including via FormFeedback.Reset()), which silently broke the whole
        // mechanism whenever a demo trial finished in under ~300ms (very possible for a short/instant grasp): the
        // lean debounce's in-progress timer got wiped by the very next trial_start before it could ever reach the
        // 300ms threshold, so trunk_lean never fired. A session-wide clock has no such boundary to race against.
        private double _demoSessionElapsedMs;
        // Run11: see the arming block in TickFormFeedbackAndHaptics.
        private const double DemoHapticArmGraceMs = 3000.0; // hard ceiling: the demo never waits on hardware
        private bool _demoCueWindowsArmed;
        private double _demoArmWaitMs;

        // Run16 (stage D sorting): the 3 physical sorting containers, discovered by well-known scene name
        // (same "GameObject.Find fallback" pattern this class already uses for CenterEyeAnchor) rather than
        // requiring every existing/future scene to wire 3 new Inspector references -- a scene with none of
        // these objects (every pre-run16 scene) behaves identically to before: the array is just empty and only
        // the original single `basketTrigger` is ever checked.
        private static readonly (string SceneName, string ContainerId)[] SortingContainerNames =
        {
            ("Basket_Red", "red_basket"),
            ("Basket_Green", "green_basket"),
            ("Container_Compost", "compost"),
        };
        private readonly System.Collections.Generic.List<BasketTrigger> _sortingContainers = new();
        private AudioSource _successAudio;
        private int _successStreak;

        private FormFeedback _formFeedback;
        private HapticClient _haptic;
        private int _currentTrialIndex = -1;
        private double _currentTrialElapsedMs;
        private Vector3 _trialStartHeadPos;
        private bool _trialStartHeadPosCaptured;
        public event Action<FormWarningSignal> OnFormSignal;
        public event Action<HapticCueRecord> OnHapticCueRecorded;
        public HapticClient Haptic => _haptic;
        public FormFeedback FormFeedback => _formFeedback;

        private OrchardReachModule _module;
        private SessionClock _clock;
        // Run12: kinematics recording. GOAL.md's pipeline is "the headset records raw kinematics + trial
        // events, not just scores" (G-north-star step 3) and analytics consumes kin_###.json chunks -- but
        // nothing in this scene ever created a KinematicsRecorder, so every session this game produced was
        // events-only and could not be analysed at all. Opt-in via StartKinematicsRecording so ordinary
        // PlayMode tests that don't want files on disk are unaffected.
        private KinematicsRecorder _kinRecorder;
        private double _kinLastSampleMs = -1;
        private const double KinSampleIntervalMs = 1000.0 / 72.0; // record at the 72 Hz the device reports
        private SyntheticHandDriver _demoDriver;
        private IHandSource _liveHands;
        private GameObject _fruitInstance;
        private Rigidbody _fruitRb;
        private double _lastNowMs;
        private double _demoFrameAccumMs;
        private const double DemoFrameDtMs = 1000.0 / 72.0;

        // Run8 fix: Opus's review of run7's screenshots found the held apple landing on the ground next to a
        // table leg with a visibly wrong-looking texture (traced to the object overlapping/z-fighting with the
        // table-leg mesh + the demo hand proxy sphere at nearly the same point — not a material bug; the fruit's
        // material/shader were independently confirmed correct by SeatedLayoutTests). Root cause (already
        // written up in docs/MANUAL_TODO.md by run7 as a known, deferred product decision): the raw fixture's
        // recorded RIndexTip trajectory is a real human's reach-grasp-place motion captured against THEIR OWN
        // table/basket layout, not ours — adding our chest origin to it correctly fixes the coordinate FRAME
        // (run7's fix) but the resulting path has no reason to pass anywhere near THIS scene's specific basket
        // position, so it can walk anywhere, including through a table leg. Rather than leave the demo visibly
        // broken, this glides the held fruit from wherever it was at the moment of grasp toward the basket over
        // a fixed short duration instead of following the raw fixture path verbatim — still driven by the same
        // Grasped/Holding state from the module (so trial-state and visuals stay consistent, per this class's own
        // design doc above), just aimed at a destination that actually exists in this scene. This also gives the
        // "placed" event (BasketTrigger) an actual chance to fire, which it never could while chasing an
        // uncorrelated path.
        private double _holdStartMs = -1;
        private Vector3 _holdStartPos;
        private const double HoldGlideDurationMs = 900.0;

        /// <summary>Run13 (Opus review of run12's 02_grasp.png: hand hovering over the basket, apple nowhere
        /// visible): at the instant Grasped begins, `_holdStartMs` is just-set and the hand snaps EXACTLY onto
        /// the apple's position (see the Grasped/Holding branch below) — so on that first frame the apple is
        /// concentric with the hand mesh and rendered behind/inside it, not visibly "carried". Callers that want
        /// to capture a screenshot of the carry (not the ungraspable snap instant) should wait until this is
        /// comfortably > 0 (a few hundred ms) before shooting, while still well under HoldGlideDurationMs so the
        /// fruit hasn't reached/detached at the basket yet. -1 outside the Grasped/Holding carry phase.</summary>
        public double HoldGlideElapsedMs => _holdStartMs >= 0 ? _lastNowMs - _holdStartMs : -1.0;

        // Run9 fix (Opus review of run8's 03_grasp/04_placed_in_basket screenshots): the hand proxy sphere was
        // near a table leg while the apple sat elsewhere at the moment the module reported "grasp" — the module's
        // grasp DETECTION only ever looked at pinch-strength timing (GraspDetector/IsGraspOverTarget), never hand
        // position, so run8's fix (gliding the FRUIT to the basket once grasped) made the fruit's path sensible
        // but never reconciled the HAND's visual position with either the fruit or the module's grasp event —
        // two independently scripted motions, exactly Opus's "not contact" finding. Fixed at the root: the demo
        // hand's visual position is now retargeted every frame from its own reach-onset position toward the
        // CURRENT apple position, driven by the SAME raw pinch-strength signal the module already uses to decide
        // when "grasp" fires (GraspDetector.PinchThreshold) — so progress==1 (hand exactly at the apple) is
        // reached at essentially the same instant the module's Grasped state begins, without touching the
        // module's own detection logic (out of scope / a platform concern, not this game's).
        private Vector3 _reachStartWorld;
        private bool _reachStartCaptured;

        // Run9: once Grasped/Holding begins, the fruit is parented to the hand (attach) instead of gliding on an
        // independent path — the hand now genuinely carries it. `_releaseWorldPos` records the exact world
        // position at the moment of release (end of the carry glide, fruit detached) so PlayMode tests can assert
        // it lands above the basket rim before the separate settle phase drops it inside the basket bounds.
        public Vector3? LastReleaseWorldPos { get; private set; }
        private double _settleStartMs = -1;
        private Vector3 _settleStartPos;
        // Run14 (user's #1 priority): was a plain 250 ms linear Lerp with no bounce, and real-hands releases
        // never used this path at all (they teleported straight to InsideBasketPoint on release -- see
        // TickRealHands). Widened to a visible ~0.4 s eased-gravity fall with a small settle bounce, shared by
        // both demo and real-hands so the drop looks and behaves the same regardless of hand source.
        private const double SettleDurationMs = 400.0;
        private bool _settling; // true while a drop is in flight, for both demo and real-hand releases

        /// <summary>Starts a physically-plausible drop from <paramref name="fromWorld"/> down into the basket:
        /// eased fall (ease-in, like gravity accelerating) then a small bounce/settle at the bottom, landing
        /// exactly on <see cref="InsideBasketPoint"/>. BasketTrigger's real OnTriggerEnter still decides the
        /// authoritative "placed" moment (see OnFruitEnteredBasket) -- this only controls what the eye sees
        /// during the fall, so "placed" cannot fire before the fruit visibly reaches the basket.</summary>
        private void StartBasketDrop(Vector3 fromWorld)
        {
            if (_fruitInstance.transform.parent != null)
                _fruitInstance.transform.SetParent(null, worldPositionStays: true);
            _settleStartPos = fromWorld;
            _settleStartMs = _lastNowMs;
            _settling = true;
        }

        /// <summary>Ease-out-bounce: falls fast, slightly overshoots past the resting point (a visible little
        /// bounce), then settles. Standard robert-penner-style bounce-out, kept small (single bounce, ~8% of the
        /// fall height) so it reads as "settled into a basket", not a bouncy ball.</summary>
        private static float BounceEaseOut(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        /// <summary>Advances any drop in progress. Runs every frame for BOTH demo and real hands (moved out of
        /// the demo-only switch so real-hand releases get the same visible fall instead of a teleport).</summary>
        private void TickBasketDrop()
        {
            if (!_settling || _fruitInstance == null) return;
            float t = (float)Math.Clamp((_lastNowMs - _settleStartMs) / SettleDurationMs, 0.0, 1.0);
            Vector3 target = InsideBasketPoint();
            // Horizontal: simple ease so the apple doesn't drift once it's basically over the basket.
            float horizT = Mathf.Clamp01(t * 1.4f); // horizontal settles slightly before the vertical bounce finishes
            Vector3 pos = new Vector3(
                Mathf.Lerp(_settleStartPos.x, target.x, horizT),
                0f,
                Mathf.Lerp(_settleStartPos.z, target.z, horizT));
            // Vertical: gravity-like fall (ease-in) down to the target, then the small bounce settle on top.
            float fallY = Mathf.Lerp(_settleStartPos.y, target.y, t * t); // ease-in = accelerating fall
            float bounceLift = (1f - BounceEaseOut(t)) * 0.03f * Mathf.Max(1f, fruitScale); // dies out to 0 by t=1
            pos.y = fallY + bounceLift;
            _fruitInstance.transform.position = pos;
            if (t >= 1f) { _settling = false; _fruitInstance.transform.position = target; }
        }

        // Root fix (2026-09-17, run7, per Opus review of run6's screenshots): the recorded fixture's joint
        // positions (contracts/fixtures/sessions/healthy/kin_*.json) are chest-RELATIVE — confirmed by dumping
        // kin_000.json's "head" joint, which sits at y≈0.55 (a head-above-chest offset, not a world height) —
        // not absolute world/scene coordinates. TargetPlacement.Place already adds chestReference correctly for
        // sampled targets, but the demo hand proxy / held-fruit position was being set directly from the raw
        // (chest-relative) fixture coordinates with NO such offset, so it always ended up near world-origin
        // (on the ground, by the camera) regardless of where the table/basket were placed — the same coordinate-
        // frame bug in two places. Fixing it once, here, at the point where fixture data enters world space,
        // is what keeps target placement, the basket, and the demo replay all sharing one calibration origin
        // (chestReference), per Opus's explicit instruction not to special-case this per-symptom.
        private Vector3 _chestOriginWorld;

        /// <summary>The session's single timeline. Everything recorded for one session -- trial events,
        /// kinematics chunks, haptic cues -- must be stamped from THIS clock, or the streams cannot be
        /// correlated. Run12 found exactly that failure: a recorder built with its own `new SessionClock()`
        /// put events on a different origin from the kinematics, so analytics found no samples inside any
        /// trial window and returned null/invalid for every single metric while every file still passed
        /// schema validation.</summary>
        public SessionClock Clock => _clock;

        public TrialState CurrentTrialState => _module?.CurrentTrialState ?? TrialState.Idle;
        public int TrialsCompleted => _module?.TrialsCompleted ?? 0;
        public int SuccessCount => _module?.SuccessCount ?? 0;
        public event Action<TrialEvent> OnTrialEvent;

        private sealed class SessionContext : ISessionContext
        {
            public SessionClock Clock { get; }
            public int BlockIndex => 0;
            public SessionContext(SessionClock clock) => Clock = clock;
        }

        /// <summary>True when the patient's own tracked hands drive the game (not the recorded demo driver).</summary>
        public bool UsingRealHands => !useDemoDriver;
        public IHandSource Hands => useDemoDriver ? (IHandSource)_demoDriver : _liveHands;
        public OrchardReachModule Module => _module;

        // ---- ISessionHost (PH U5): what OpusSessionRunner needs, with Orchard's existing behaviour behind it ----
        string ISessionHost.GameId => "orchard_reach";
        string ISessionHost.GameVersion => "0.2.0";
        string ISessionHost.ContractsVersion => "0.1";
        string ISessionHost.AffectedSide => _module?.CurrentSide;
        double? ISessionHost.MeasuredTrackingRateHz => null;      // Orchard records the nominal 72 Hz, as before
        string ISessionHost.HubHostHint => null;                  // PlayerPrefs / the runner's serialized field decide
        bool ISessionHost.IsModuleRunning => _module != null && _module.IsRunning;
        bool ISessionHost.IsSessionComplete => _module != null && !_module.IsRunning && _module.TrialsCompleted >= _module.TrialCount;
        bool ISessionHost.NeedsFreshScene => false;
        bool ISessionHost.AutoStartRequested => false;
        string ISessionHost.AutoStartReason => null;
        event Action<int, JObject> ISessionHost.OnMetricsTick { add { } remove { } } // Orchard's live metrics are computed in the runner
        void ISessionHost.PrepareNextSession(string why) { }
        bool ISessionHost.TryHandleCommand(string command, JObject parameters, out string error) { error = null; return false; }
        void ISessionHost.FillStatus(JObject status, double nowMs) { }
        void ISessionHost.DescribeBlocks(SessionEnvelope envelope, bool ended, bool completed) { }
        public Transform HeadReference => headReference;
        public GameObject FruitInstance => _fruitInstance;

        /// <summary>Headset present? XRSettings.isDeviceActive is still false during the first scene's Awake over
        /// Quest Link (the XR session starts a moment later), which silently put Link play on the demo driver and
        /// switched off the whole session runner. So: a player build is always the Quest; batch mode (CI tests)
        /// never has one; in the interactive editor, an initialized XR loader (Play Mode OpenXR/Link) counts.</summary>
        /// <summary>The Building Blocks hand meshes ("[BuildingBlock] Hand Tracking left/right") were saved into the
        /// scene with their SkinnedMeshRenderer DISABLED — the headless screenshot tooling hides the rig's hand
        /// meshes and that state got saved — so with real hands the patient could grab but never saw their own
        /// hands (user report over Quest Link, 2026-09-19). Force them on at runtime so no future scene save can
        /// bring the bug back.</summary>
        private static void ShowTrackedHandMeshes()
        {
            foreach (var name in new[] { "[BuildingBlock] Hand Tracking left", "[BuildingBlock] Hand Tracking right" })
            {
                var go = GameObject.Find(name);
                if (go == null) { Debug.LogWarning($"[OPUS] hand mesh object '{name}' not found"); continue; }
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            }
        }

        private static bool HeadsetActive()
        {
            if (!Application.isEditor) return true;
            if (Application.isBatchMode) return false;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
            try
            {
                var t = Type.GetType("UnityEngine.XR.Management.XRGeneralSettings, Unity.XR.Management");
                var inst = t?.GetProperty("Instance")?.GetValue(null);
                var mgr = inst?.GetType().GetProperty("Manager")?.GetValue(inst);
                var loader = mgr?.GetType().GetProperty("activeLoader")?.GetValue(mgr);
                if (loader != null) return true;
            }
            catch { /* fall through */ }
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.Count > 0;
        }

        private void Awake()
        {
            if (handMode == HandMode.Demo) useDemoDriver = true;
            else if (handMode == HandMode.RealHands) useDemoDriver = false;
            else if (Application.isPlaying) useDemoDriver = !HeadsetActive();
            // (EditMode tests invoke Awake via reflection with Application.isPlaying=false: keep the serialized value.)

            // The static ghost-hand mesh stood in for a hand in the no-headset demo; the patient uses their own
            // tracked hands, so it is gone for good (user request, 2026-09-19). The small index-tip marker below
            // remains the demo's fallback visual; in real-hands play it is hidden too.
            if (demoHandGhostVisual != null)
            {
                demoHandGhostVisual.gameObject.SetActive(false);
                demoHandGhostVisual = null;
            }
            if (!useDemoDriver && demoHandProxyR != null) demoHandProxyR.gameObject.SetActive(false);
            if (!useDemoDriver) ShowTrackedHandMeshes();

            _clock = new SessionClock();
            BuildModule(new JObject { ["trialCount"] = demoTrialCount });

            _formFeedback = new FormFeedback(_module.Params.TrunkLeanWarningCm);
            _formFeedback.OnSignal += HandleFormSignal;

            // Run11: trunk-lean vignette + audio (the PRIMARY feedback -- haptics are additive per
            // HAPTIC_PROTOCOL.md, so this must exist and work with haptics off/no sleeve). Built directly off
            // _formFeedback's own polled state (see TrunkLeanVignette), never through HapticClient, so it's fully
            // independent of the haptics pipeline. `headReference` is the CenterEyeAnchor in real play; the
            // dressing tool doesn't currently wire it (a pre-existing gap -- live-mode trunk-lean geometry in
            // TickFormFeedbackAndHaptics also silently no-ops without it), so fall back to a scene lookup by the
            // rig's well-known anchor name rather than leaving both that and the vignette dark in every scene
            // that predates this fix.
            if (headReference == null)
            {
                var eyeGo = GameObject.Find("CenterEyeAnchor");
                if (eyeGo != null) headReference = eyeGo.transform;
            }
            if (headReference != null)
            {
                TrunkLeanVignette.Attach(headReference, _formFeedback);
            }
            else
            {
                Debug.LogWarning("OrchardReachSceneController: no CenterEyeAnchor found -- trunk-lean vignette/audio not attached.");
            }

            // Run9 step 3: haptics always constructed (so hapticMaxIntensity/status are inspectable), gated by
            // the manifest's hapticsEnabled param — HapticClient itself no-ops SendXxx calls while Enabled=false,
            // and a Stop() is always allowed (safety must not depend on the clinical toggle).
            BuildHaptic(hapticManualHost);

            var chest = chestReference != null ? chestReference.position : Vector3.zero;
            _chestOriginWorld = chest;
            _module.SetCalibration(new double[] { chest.x, chest.y, chest.z }, armLengthM);

            if (fruitPrefab != null)
            {
                _fruitInstance = Instantiate(fruitPrefab);
                _fruitInstance.name = "FruitInstance";
                _fruitInstance.transform.localScale = Vector3.one * fruitScale; // absolute (user: "FruitInstance scale 2")
                _fruitBaseScale = _fruitInstance.transform.localScale;
                if (_fruitInstance.GetComponentInChildren<FruitMarker>() == null)
                    _fruitInstance.AddComponent<FruitMarker>();
                _fruitRb = _fruitInstance.GetComponentInChildren<Rigidbody>();
                // Run11 fix (basket near-miss, 10.28cm vs the 10cm audit limit): the whole demo carry/release/
                // settle path is 100% script-driven (Vector3.Lerp in DriveDemoHandAndFruit, both while parented
                // to the hand and again during the post-release settle) -- physics was never meant to move this
                // rigidbody at all here, only to let BasketTrigger's OnTriggerEnter detect it (a trigger still
                // fires for a kinematic body). A prior comment at the release site even said "the apple's own
                // (kinematic) collider settles independently" -- but isKinematic was never actually set, so
                // gravity kept accelerating the non-kinematic body every FixedUpdate while it was parented to the
                // moving hand; Unity resolves that as silent local-position drift under the parent transform,
                // which accumulated over the ~whole carry glide into the measured horizontal miss. Kinematic in
                // demo mode only -- the real (non-demo) HandGrabInteractable/Grabbable path manages its own
                // rigidbody kinematic state and must not be touched here.
                if (useDemoDriver && _fruitRb != null) _fruitRb.isKinematic = true;

                // Real hands: this controller carries the fruit itself (grab = pinch AT the fruit, see
                // TickRealHands), so the fruit is kinematic and any ISDK grab components on the prefab are
                // switched off -- two systems moving one object is how you get a fruit that jitters or flies off.
                if (!useDemoDriver)
                {
                    if (_fruitRb != null) { _fruitRb.isKinematic = true; _fruitRb.useGravity = false; }
                    foreach (var mb in _fruitInstance.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null || mb is FruitMarker) continue;
                        var n = mb.GetType().Name;
                        if (n.Contains("Grab") || n.Contains("Grabbable") || n.Contains("Interactable")) mb.enabled = false;
                    }
                }
            }

            if (basketTrigger != null) basketTrigger.OnFruitEntered += _ => OnFruitEnteredBasket();

            // Run16 (stage D): find and wire the 3 sorting containers, if this scene has them.
            _sortingContainers.Clear();
            foreach (var (sceneName, containerId) in SortingContainerNames)
            {
                var go = GameObject.Find(sceneName);
                if (go == null) continue;
                var trigger = go.GetComponent<BasketTrigger>();
                if (trigger == null) continue;
                if (string.IsNullOrEmpty(trigger.ContainerId)) trigger.ContainerId = containerId;
                _sortingContainers.Add(trigger);
                string capturedId = trigger.ContainerId;
                trigger.OnFruitEntered += _ => OnFruitEnteredContainer(trigger, capturedId);
            }

            // Run16 (task 3): procedural success chime needs an AudioSource to play through -- built here in
            // code rather than requiring one on a scene prefab, same "no scene asset to forget" reasoning as
            // OpusHud.Create().
            _successAudio = gameObject.AddComponent<AudioSource>();
            _successAudio.spatialBlend = 1f; // 3D -- reads as coming from the basket, not the player's head
            _successAudio.playOnAwake = false;

            if (useDemoDriver)
            {
                _demoDriver = BuildDemoDriver();
                // Run8 (SeatedLayoutTests): force frame 0 of the fixture to be current, and position the hand
                // visual from it, immediately here in Awake() rather than waiting for the first Update() tick.
                // Previously the hand proxy stayed inactive/unpositioned until enough Time.deltaTime had
                // accumulated to cross one DemoFrameDtMs step in Update() — timing-dependent, and SyntheticHandDriver
                // itself starts at _frameCursor=-1 (TryGetJointPose always returns false before the first
                // AdvanceFrame()), so "the demo replay's first frame" needs an explicit AdvanceFrame() call here
                // to be well-defined and testable without relying on Play mode frame timing.
                _demoDriver?.AdvanceFrame();
                // Demo mode is meant to run unattended for the R5 visual audit (no shell UI to press "start"
                // yet) — auto-begin so Play mode immediately samples a target and moves the fruit there.
                // Run9: Begin() moved BEFORE the hand-visual positioning call (was after) — DriveDemoHandAndFruit
                // now reads _module.CurrentTrialState to decide reach-vs-carry-vs-settle behaviour, so the module
                // must already be in TargetShown (which Begin() sets synchronously, along with the fruit's first
                // target position via HandleTrialEvent) before the first positioning call, or frame 0 would fall
                // through to the "no trial running yet" branch and leave the hand unpositioned.
                if (autoBeginDemo)
                {
                    _module.Begin();
                    DriveDemoHandAndFruit();
                }
            }
            else
            {
                _liveHands = BuildLiveHandSource();
                // Real play begins when OpusSessionRunner calls StartNewSession() (clinician presses Start in the
                // app, or the patient pinches both hands). Until then the fruit waits, visible, on the table.
                if (_fruitInstance != null) _fruitInstance.SetActive(false);
            }
            // The session runner (hub link + recording + HUD) always travels with the controller, so no scene
            // wiring can be forgotten. It disables itself on the demo driver unless told otherwise.
            if (Application.isPlaying && GetComponent<OpusSessionRunner>() == null) gameObject.AddComponent<OpusSessionRunner>();
            Debug.Log($"[OPUS] OrchardReachSceneController: hands={(useDemoDriver ? "demo driver (no headset)" : "REAL tracked hands")}, fruitScale={fruitScale}");
        }

        private void BuildModule(JObject overrides)
        {
            if (_module != null) _module.OnTrialEvent -= HandleTrialEvent;
            _module = new OrchardReachModule();
            _module.LoadManifest(manifestJson != null ? manifestJson.text : DefaultManifestJson());
            _module.OnTrialEvent += HandleTrialEvent;

            var bind = ParamBinder.Bind(_module.Manifest.ParamSchema, overrides ?? new JObject());
            if (!bind.IsValid)
                Debug.LogWarning($"OrchardReachSceneController: param bind warnings: {string.Join("; ", bind.Errors)}");
            _module.Configure(bind.Params, new SessionContext(_clock));

            if (!useDemoDriver)
            {
                _module.SceneOwnsRelease = true;
                _module.GraspProximityGate = side => PinchPointNearFruit(side);
                _module.MovementOnsetGate = side => WristMovedSinceTargetShown(side);
            }
        }

        /// <summary>Start a fresh session: new clock, new trial sequence, current calibration. Called by
        /// <see cref="OpusSessionRunner"/>; <paramref name="overrides"/> are prescription params from the
        /// clinician's program (validated against the manifest; invalid values fall back to defaults).</summary>
        public void StartNewSession(JObject overrides)
        {
            _clock = new SessionClock();
            BuildModule(overrides);
            _formFeedback?.Reset();
            if (_haptic != null)
            {
                _haptic.Enabled = _module.Params.HapticsEnabled;
                _haptic.MaxIntensity = _module.Params.HapticMaxIntensity;
            }
            var chest = chestReference != null ? chestReference.position : Vector3.zero;
            _chestOriginWorld = chest;
            _module.SetCalibration(new double[] { chest.x, chest.y, chest.z }, armLengthM);
            _holding = false;
            _releaseFrames = 0;
            if (_fruitInstance != null)
            {
                _fruitInstance.transform.SetParent(null, worldPositionStays: true);
                _fruitInstance.SetActive(true);
            }
            _module.Begin();
        }

        public void PauseSession() { if (_module != null && _module.IsRunning) _module.Pause(); }
        public void ResumeSession() { if (_module != null && !_module.IsRunning) _module.Resume(); }
        public void EndSession()
        {
            _module?.End();
            _holding = false;
            if (!useDemoDriver && _fruitInstance != null) _fruitInstance.SetActive(false);
        }

        /// <summary>Move the camera rig so the player's real head sits at the designed seated eye point, facing
        /// the table. Everything the scene places (table, basket, trees, fruit targets) was laid out for that eye
        /// point, so this is what makes targets reachable whether the player sits, stands, or started the app
        /// facing a wall. The chest reference is a child of the rig, so it is moved back to where it was.</summary>
        public bool Recenter()
        {
            if (headReference == null) return false;
            var trackingSpace = headReference.parent;
            var rig = trackingSpace != null ? trackingSpace.parent : null;
            if (rig == null) return false;

            Vector3 chestWorldBefore = chestReference != null ? chestReference.position : Vector3.zero;

            // Yaw: turn the rig about the head so the head's flat forward matches the scene's +Z.
            Vector3 fwd = headReference.forward; fwd.y = 0;
            if (fwd.sqrMagnitude > 1e-4f)
            {
                float yaw = Vector3.SignedAngle(fwd.normalized, Vector3.forward, Vector3.up);
                rig.RotateAround(headReference.position, Vector3.up, yaw);
            }
            // Position: head onto the designed eye point.
            rig.position += DesignedEyeWorld - headReference.position;

            if (chestReference != null)
            {
                chestReference.position = chestWorldBefore;
                chestReference.rotation = Quaternion.identity;
            }
            Debug.Log($"[OPUS] Recenter: head now at {headReference.position} (designed {DesignedEyeWorld})");
            return true;
        }

        /// <summary>Seated eye point the scene was designed around (SeatedLayoutTests: eye 1.15 m, 5 cm behind
        /// the chest reference).</summary>
        [SerializeField] private Vector3 designedEyeWorld = new Vector3(0f, 1.15f, -0.05f);
        public Vector3 DesignedEyeWorld => designedEyeWorld;

        /// <summary>Editor/demo path: replays contracts/fixtures/sessions/healthy's kin_*.json (a real
        /// reach-grasp recording from the synthetic-patient generator) so Play mode has believable, non-trivial
        /// hand motion without a headset. Falls back to a null driver (module still runs, no visible motion)
        /// if the fixtures aren't found, e.g. in a build that doesn't ship contracts/.</summary>
        private SyntheticHandDriver BuildDemoDriver()
        {
            try
            {
                string root = Application.dataPath.Replace("/Assets", "").Replace("\\Assets", "");
                string fixtureDir = Path.Combine(root, "..", "contracts", "fixtures", "sessions", "healthy");
                fixtureDir = Path.GetFullPath(fixtureDir);
                if (!Directory.Exists(fixtureDir))
                {
                    Debug.LogWarning($"OrchardReachSceneController: demo fixture dir not found at {fixtureDir}; demo driver disabled.");
                    return null;
                }
                var files = Directory.GetFiles(fixtureDir, "kin_*.json");
                Array.Sort(files);
                return new SyntheticHandDriver(files);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"OrchardReachSceneController: failed to build demo driver: {e.Message}");
                return null;
            }
        }

        private IHandSource BuildLiveHandSource()
        {
#if OPUS_META_HANDS
            // The Building Blocks Interactions Rig (R2) puts the raw ISDK Hand components on GameObjects named
            // "OVRHandDataSourceLeft"/"Right" under "OVRHands", and the Hmd component under "OVRHmd" (confirmed
            // via SceneObjectsTools.GetSceneHierarchy in run4's checkpoint 3). Both `Hand` and `Hmd` are
            // MonoBehaviours that implement IHand/IHmd directly, so a plain GetComponent finds them — no need to
            // reach into the DataSource feeding them (per docs/UNITY_PRACTICES.md §4, MetaHandSource must read
            // the RAW hand, which these ARE — filtering/smoothing modifiers would sit downstream of this, not here).
            var leftGo = GameObject.Find("OVRHandDataSourceLeft");
            var rightGo = GameObject.Find("OVRHandDataSourceRight");
            // PH U5 fix: the Hmd component sits on "OVRHmd" (OVRHmdDataSource only feeds it), so look there first.
            var hmdGo = GameObject.Find("OVRHmd") ?? GameObject.Find("OVRHmdDataSource");
            var leftHand = leftGo != null ? leftGo.GetComponent<Oculus.Interaction.Input.IHand>() : null;
            var rightHand = rightGo != null ? rightGo.GetComponent<Oculus.Interaction.Input.IHand>() : null;
            var hmd = hmdGo != null ? hmdGo.GetComponent<Oculus.Interaction.Input.IHmd>() : null;
            if (leftHand == null || rightHand == null)
                Debug.LogWarning("OrchardReachSceneController: could not find raw OVR hand sources in the scene by name; live hand tracking will report untracked.");
            return new MetaHandSource(leftHand, rightHand, hmd);
#else
            return new MetaHandSource(null, null, null);
#endif
        }

        public void BeginSession() => _module.Begin();

        /// <summary>Test/tooling hook: Awake() already constructed <see cref="_haptic"/> from the manifest's
        /// hapticsEnabled/hapticMaxIntensity params (which default to false/0.8 and aren't overridden by the
        /// demoTrialCount-only overrides block) — PlayMode tests that need haptics on against a specific fake
        /// sleeve call this after the scene has loaded instead of fighting Unity's Awake timing.
        ///
        /// Run10 root-cause fix: this used to only flip `_haptic.Enabled`, leaving `_demoSessionElapsedMs`
        /// (started back in Awake()) running unaffected. Callers always call this a couple of frames AFTER the
        /// scene load (`LoadSceneAndGetController(); yield return null; yield return null;
        /// ConfigureHapticsForTest(...)`, per HapticIntegrationTests) — and the frame(s) immediately after a
        /// scene load routinely report a large `Time.deltaTime` (the load itself, plus Awake()/Start() work,
        /// collapsed into one timestep). Confirmed via run10_playmode3.log: the sleeve WAS discovered
        /// (`UdpHapticTransport` logged it) and the trigger windows' math is otherwise correct, yet not one
        /// "[HapticClient] cue dropped" diagnostic ever printed — meaning `HandleFormSignal` was never even
        /// invoked, i.e. `FormFeedback`'s ONE-SHOT edge-triggered trunk_lean/low_confidence signal (it fires
        /// once per rising edge, see FormFeedback's class doc) had already fired-and-been-discarded (dropped
        /// silently by `GateCue`'s `if (!Enabled) return false` branch, which — deliberately — logs nothing,
        /// since a disabled client dropping cues is the normal/expected case, not a diagnostic-worthy one)
        /// during those first couple of large-deltaTime frames, BEFORE this method ever ran to turn haptics on.
        /// By the time `Enabled` flips true, the demo's single scripted lean/loss windows have already elapsed
        /// with no one listening, and they never repeat. Resetting the demo clock HERE — the exact moment the
        /// test/caller declares "haptics are now configured and ready" — makes the scripted windows start
        /// strictly after that point, eliminating the race at its source instead of trying to out-guess frame
        /// timing. No effect on non-demo (live) callers, who never call this and always enable haptics
        /// synchronously from the manifest in Awake(), before Update() (and therefore before any lean/tracking
        /// computation) ever runs — there is no equivalent race for them.</summary>
        /// <summary>Begin writing kin_###.json chunks for this session into <paramref name="sessionDir"/>.
        /// Call once, before/at session start; call <see cref="StopKinematicsRecording"/> at session end to
        /// flush the final partial chunk. Sampling runs off the same IHandSource the game itself uses, so a demo
        /// (SyntheticHandDriver) and a live session record through exactly the same path.</summary>
        public void StartKinematicsRecording(string sessionId, string sessionDir, double rateHz = 72.0)
        {
            _kinRecorder = new KinematicsRecorder(sessionId, _clock, sessionDir, rateHz,
                source: useDemoDriver ? "synthetic" : "xr_hands");
            _kinLastSampleMs = -1;
        }

        /// <summary>Flush the in-progress chunk. Returns how many chunks were written in total.</summary>
        public int StopKinematicsRecording()
        {
            if (_kinRecorder == null) return 0;
            _kinRecorder.Flush();
            int n = _kinRecorder.ChunksWritten;
            _kinRecorder = null;
            return n;
        }

        public int KinematicsChunksWritten => _kinRecorder?.ChunksWritten ?? 0;

        /// <summary>One frame of every joint the contracts define, from whichever hand source is driving this
        /// session. Joints the source can't provide this frame are skipped rather than written as zeros, so a
        /// gap reads as a gap to analytics instead of as a real pose at the origin.</summary>
        private void SampleKinematics(IHandSource hands)
        {
            if (_kinRecorder == null || hands == null) return;
            if (_kinLastSampleMs >= 0 && _lastNowMs - _kinLastSampleMs < KinSampleIntervalMs) return;
            _kinLastSampleMs = _lastNowMs;

            foreach (var joint in OpusJoints.All)
            {
                if (!TrySampleJoint(hands, joint, out var pos, out var rot, out var conf)) continue;
                _kinRecorder.Sample(_lastNowMs, joint, pos, rot, conf);
            }
        }

        private bool TrySampleJoint(IHandSource hands, string joint, out double[] pos, out double[] rot, out double conf)
        {
            pos = null; rot = null; conf = 0;
            if (_demoDriver != null && useDemoDriver)
            {
                if (!_demoDriver.TryGetJointPose(joint, out pos, out rot)) return false;
                // The synthetic driver replays recorded fixture poses; confidence comes from the hand source so
                // a fixture's own tracking-loss spans stay visible to analytics' quality gating.
                var side = joint.StartsWith("l_") ? HandSide.Left : HandSide.Right;
                var c = hands.GetConfidence(side);
                conf = c == TrackingConfidence.High ? 1.0 : c == TrackingConfidence.Low ? 0.5 : 0.0;
                return true;
            }
            if (!useDemoDriver && hands != null)
            {
                // Real hands: ISDK poses are world-space; the contracts record chest-relative positions (the
                // same frame the trial targets are written in -- see OrchardReachModule.SubtractChest).
                if (!hands.TryGetJointPose(joint, out var world, out rot) || world == null) return false;
                pos = new[] { world[0] - _chestOriginWorld.x, world[1] - _chestOriginWorld.y, world[2] - _chestOriginWorld.z };
                if (joint == OpusJoints.Head) { conf = 1.0; return true; }
                var side = joint.StartsWith("l_") ? HandSide.Left : HandSide.Right;
                var c = hands.GetConfidence(side);
                conf = c == TrackingConfidence.High ? 1.0 : c == TrackingConfidence.Low ? 0.5 : 0.0;
                return true;
            }
            return false;
        }

        public void ConfigureHapticsForTest(bool enabled, double maxIntensity)
        {
            _haptic.Enabled = enabled;
            _haptic.MaxIntensity = maxIntensity;
            if (useDemoDriver)
            {
                _demoSessionElapsedMs = 0;
                _demoCueWindowsArmed = false; // Run11: re-arm with the reset clock, same reasoning as the line above
                _demoArmWaitMs = 0;
                // Resetting the elapsed-time clock alone isn't enough: FormFeedback's own debounce/active state
                // (LeanWarningActive/_leanExceedSinceMs) is edge-triggered and persists independently of the
                // caller's clock. If the scripted lean window's rising edge already fired during the couple of
                // (large-deltaTime, post-scene-load) frames BEFORE this method ran — while haptics were still
                // disabled, so HandleFormSignal's SendTrunkLean call was silently dropped by GateCue's
                // `!Enabled` branch — LeanWarningActive was already left `true`. Reopening the window afterward
                // (by zeroing _demoSessionElapsedMs above) then does nothing: TickLean's rising-edge check is
                // gated on `!LeanWarningActive`, and with no intervening "not exceeding" frame to clear it, the
                // stale `true` from the missed, disabled-haptics edge blocks the signal from ever firing again —
                // exactly the "sleeve discovered but cueOrder=[] forever" behaviour seen in run10_playmode3/4's
                // logs. FormFeedback.Reset() clears that debounce state so the freshly-reopened window gets a
                // genuine rising edge to fire on.
                _formFeedback?.Reset();
            }
        }

        private void BuildHaptic(string manualHost)
        {
            var transport = new UdpHapticTransport(string.IsNullOrEmpty(manualHost) ? null : manualHost);
            _haptic = new HapticClient(transport)
            {
                Enabled = _module.Params.HapticsEnabled,
                MaxIntensity = _module.Params.HapticMaxIntensity,
            };
            _haptic.OnCueRecorded += r => OnHapticCueRecorded?.Invoke(r);
            _haptic.Start();
        }

        /// <summary>Test hook: replace the haptic transport with one that uses UDP discovery (or the given manual
        /// host). The committed OrchardReach.unity carries a real sleeve IP in hapticManualHost (real-hands session),
        /// which bypasses discovery, so PlayMode tests that talk to sim/haptic/fake_haptic.py on this machine must
        /// call this before sending cues. Pass null/empty for pure discovery.</summary>
        public void RebuildHapticTransportForTest(string manualHost)
        {
            if (_haptic == null) return;
            var enabled = _haptic.Enabled; var max = _haptic.MaxIntensity;
            try { _haptic.Dispose(); } catch (Exception) { /* best-effort */ }
            BuildHaptic(manualHost);
            _haptic.Enabled = enabled; _haptic.MaxIntensity = max;
        }

        /// <summary>Seconds the headset has been continuously tracking the head (0 when not tracked).</summary>
        public float HeadTrackedSeconds { get; private set; }
        private bool _autoRecentered;

        /// <summary>Real hands: until the head is tracked, the CenterEyeAnchor sits exactly at the tracking-space
        /// origin with identity rotation. Any non-trivial local pose means the headset is delivering real data.</summary>
        private bool HeadIsTracked()
        {
            if (headReference == null) return false;
            return headReference.localPosition.sqrMagnitude > 1e-6f || Quaternion.Angle(headReference.localRotation, Quaternion.identity) > 0.01f;
        }

        private void Update()
        {
            if (!useDemoDriver)
            {
                HeadTrackedSeconds = HeadIsTracked() ? HeadTrackedSeconds + Time.deltaTime : 0f;
                // The rig uses floor-level tracking and sits ~1 m up in the scene, so before a recenter the patient
                // sees the orchard from ~2 m high. Recenter once, as soon as the pose has been stable for a moment.
                if (!_autoRecentered && HeadTrackedSeconds >= 0.75f) _autoRecentered = Recenter();
            }
            _lastNowMs = _clock.NowMs;
            IHandSource hands = useDemoDriver ? (IHandSource)_demoDriver : _liveHands;
            if (hands == null) return;

            if (useDemoDriver && _demoDriver != null)
            {
                _demoFrameAccumMs += Time.deltaTime * 1000.0 * demoPlaybackSpeed;
                while (_demoFrameAccumMs >= DemoFrameDtMs && !_demoDriver.Finished)
                {
                    _demoDriver.AdvanceFrame();
                    _demoFrameAccumMs -= DemoFrameDtMs;
                }
                // Run10 fix: the recorded fixture is only ~20s of unique motion (4 chunks x 360 frames @ 72Hz,
                // contracts/fixtures/sessions/healthy/kin_*.json); SyntheticHandDriver freezes at its last frame
                // once Finished (by design — the class is also used by the one-shot recording pipeline, where
                // "replay every frame exactly once" is the contract, see SyntheticHandDriverTests). A demo asked
                // to sustain demoTrialCount=6 full reach/grasp/place cycles needs more than 20s of live motion,
                // so once frozen, later trials could only complete by the frozen pose accidentally satisfying
                // that trial's randomly-sampled target's grasp condition (confirmed: run9/run10 without a
                // haptic sleeve stalled at 2-3/6 trials against the 40s test timeout, while a lucky sampling let
                // one run reach 6/6). Rebuilding a fresh driver instance (same fixture files, restarting at
                // frame 0) when exhausted — scoped to THIS demo composition root only, not a change to
                // SyntheticHandDriver's own one-shot contract — loops the recorded motion so the demo can drive
                // an arbitrary trial count without ever freezing.
                if (_demoDriver.Finished)
                {
                    _demoDriver = BuildDemoDriver() ?? _demoDriver;
                    _demoDriver.AdvanceFrame();
                }
            }

            SampleKinematics(hands);

            // Run9: positioning moved to AFTER Tick() (was before) so the retarget/attach/carry/settle logic
            // below can react to the trial state Tick() just produced in the SAME frame — e.g. the very frame
            // the module transitions into Grasped, the hand is snapped exactly onto the apple that same frame,
            // not one frame late.
            _module.Tick(_lastNowMs, hands);

            if (useDemoDriver) DriveDemoHandAndFruit();
            else TickRealHands(hands);
            TickBasketDrop();

            TickFormFeedbackAndHaptics(hands);
        }

        // ---- real tracked hands -------------------------------------------------------------------------

        private Vector3 _fruitBaseScale = Vector3.one;
        private bool _holding;
        private int _releaseFrames;
        private Vector3 _holdOffset;
        private Vector3? _onsetRefWrist;
        private const float ReleasePinch = 0.30f;   // below this for a few frames = let go
        private const int ReleaseFramesNeeded = 4;  // ~55 ms at 72 Hz; ignores single-frame pinch dropouts

        /// <summary>World-space pinch point (thumb/index midpoint), falling back to the index tip or palm.</summary>
        public bool TryGetPinchPoint(HandSide side, out Vector3 point)
        {
            point = default;
            if (_liveHands == null) return false;
            string p = side == HandSide.Left ? "l_" : "r_";
            bool haveIdx = _liveHands.TryGetJointPose(p + "index_tip", out var idx, out _);
            bool haveThb = _liveHands.TryGetJointPose(p + "thumb_tip", out var thb, out _);
            if (haveIdx && haveThb) { point = (V(idx) + V(thb)) * 0.5f; return true; }
            if (haveIdx) { point = V(idx); return true; }
            if (_liveHands.TryGetJointPose(p + "palm", out var palm, out _)) { point = V(palm); return true; }
            return false;
        }

        private static Vector3 V(double[] a) => new Vector3((float)a[0], (float)a[1], (float)a[2]);

        private bool PinchPointNearFruit(HandSide side)
        {
            if (_fruitInstance == null || !TryGetPinchPoint(side, out var pt)) return false;
            return Vector3.Distance(pt, _fruitInstance.transform.position) <= grabRadiusM;
        }

        /// <summary>Distance from the pinch point to the fruit (m), or -1 if the hand isn't tracked. For the HUD.</summary>
        public float DistanceToFruit(HandSide side)
        {
            if (_fruitInstance == null || !TryGetPinchPoint(side, out var pt)) return -1f;
            return Vector3.Distance(pt, _fruitInstance.transform.position);
        }

        private bool WristMovedSinceTargetShown(HandSide side)
        {
            string j = side == HandSide.Left ? OpusJoints.LWrist : OpusJoints.RWrist;
            if (_liveHands == null || !_liveHands.TryGetJointPose(j, out var w, out _)) return false;
            var wrist = V(w);
            if (!_onsetRefWrist.HasValue) { _onsetRefWrist = wrist; return false; } // hand first seen now
            return Vector3.Distance(wrist, _onsetRefWrist.Value) >= movementOnsetM;
        }

        private void TickRealHands(IHandSource hands)
        {
            if (_fruitInstance == null || _module == null) return;
            var state = _module.CurrentTrialState;
            var side = _module.CurrentSide == "left" ? HandSide.Left : HandSide.Right;

            if (state == TrialState.Grasped || state == TrialState.Holding)
            {
                if (!TryGetPinchPoint(side, out var pt)) return; // tracking blip: fruit stays put, still "held"
                if (!_holding)
                {
                    _holding = true;
                    _releaseFrames = 0;
                    _holdOffset = _fruitInstance.transform.position - pt; // keep the grab where it happened
                }
                _fruitInstance.transform.position = pt + _holdOffset;

                if (hands.GetPinchStrength(side) < ReleasePinch) _releaseFrames++;
                else _releaseFrames = 0;

                if (_releaseFrames >= ReleaseFramesNeeded)
                {
                    var overTrigger = FindContainerFruitIsOver();
                    _holding = false;
                    // Run14 fix: this used to teleport straight to InsideBasketPoint on release -- the user's
                    // #1 complaint (no visible fall for real hands at all). Now it starts the same eased
                    // gravity+bounce drop the demo uses, from wherever the fruit actually was at release.
                    if (overTrigger != null) StartBasketDrop(_fruitInstance.transform.position);
                    if (overTrigger != null && overTrigger != basketTrigger)
                    {
                        // Run16 (stage D real hands): resolve the sorting outcome for THIS container directly,
                        // rather than routing through ReleaseGrasp's single-basket ConfirmPlacedInBasket path.
                        // The later physics OnTriggerEnter on this same container is a no-op once this ends the
                        // trial -- OnFruitEnteredContainer/OnFruitEnteredBasket both guard on State==Holding.
                        var marker = _fruitInstance.GetComponentInChildren<FruitMarker>();
                        string color = marker != null ? marker.Color : null;
                        bool ripe = marker == null || marker.Ripe;
                        double? tiltDeg = _module.Params.Stage == "C_precision" ? FruitTiltDegAtPlacement() : (double?)null;
                        _module.ConfirmPlacedInContainer(_lastNowMs, overTrigger.ContainerId, color, ripe, tiltDeg);
                    }
                    else
                    {
                        _module.ReleaseGrasp(_lastNowMs, overTrigger != null);
                    }
                }
            }
            else
            {
                _holding = false;
            }
        }

        /// <summary>Run16: which container (the default single basket, or one of the 3 stage-D sorting
        /// containers) the fruit's current position is over, if any -- generous margin (fruit-sized) so
        /// patients don't fail a trial by a centimetre. Checked in a fixed order; the default basket first so
        /// existing single-basket scenes (no sorting containers at all) are completely unaffected.</summary>
        private BasketTrigger FindContainerFruitIsOver()
        {
            if (_fruitInstance == null) return null;
            if (basketTrigger != null && IsOverContainer(basketTrigger)) return basketTrigger;
            foreach (var c in _sortingContainers)
                if (IsOverContainer(c)) return c;
            return null;
        }

        private bool IsOverContainer(BasketTrigger trigger)
        {
            var col = trigger.GetComponent<Collider>();
            if (col == null) return false;
            var b = col.bounds;
            var p = _fruitInstance.transform.position;
            float margin = 0.06f * Mathf.Max(1f, fruitScale);
            return p.x >= b.min.x - margin && p.x <= b.max.x + margin
                && p.z >= b.min.z - margin && p.z <= b.max.z + margin
                && p.y >= b.min.y - 0.02f && p.y <= b.max.y + 0.35f;
        }

        /// <summary>Run9 steps 2+4: computes this frame's trunk-lean/tracking inputs and feeds them to
        /// <see cref="_formFeedback"/>, then pumps the haptic client (watchdog + queued main-thread work).
        /// Demo mode has no real headset/hand tracker at all, so it substitutes the scripted trigger windows
        /// described on <see cref="demoTriggerFormEvents"/>'s field comment instead of real geometry/confidence.
        /// </summary>
        private void TickFormFeedbackAndHaptics(IHandSource hands)
        {
            _currentTrialElapsedMs += Time.deltaTime * 1000.0 * (useDemoDriver ? demoPlaybackSpeed : 1f);

            double leanCm;
            bool trackingLow, trackingLost;

            if (useDemoDriver)
            {
                // Run11 fix (PlayMode race, cueOrder=[] with the sleeve present): the scripted demo cue windows
                // used to start counting the instant the scene loaded, while UdpHapticTransport's discovery only
                // learns the sleeve's address from its once-per-second broadcast. When discovery landed after the
                // 600 ms lean window had already closed, every cue was dropped with "no haptic device known yet"
                // and the demo showed no haptics at all -- a demo-harness timing bug, not a pipeline bug (run10
                // passed on luck). So hold the windows until the haptic path is actually ready. The grace timeout
                // keeps this from ever becoming a dependency on hardware: with haptics off, or with no sleeve
                // that ever answers, the windows open anyway and the session behaves exactly as before (G10 --
                // the game is never harmed by the sleeve). Real (non-demo) sessions don't use these windows at
                // all; a genuine lean before discovery still gets vignette + audio, which are the primary cues.
                if (!_demoCueWindowsArmed)
                {
                    _demoArmWaitMs += Time.deltaTime * 1000.0 * demoPlaybackSpeed;
                    if (!_haptic.Enabled || _haptic.Connected || _demoArmWaitMs >= DemoHapticArmGraceMs)
                        _demoCueWindowsArmed = true;
                }
                if (_demoCueWindowsArmed) _demoSessionElapsedMs += Time.deltaTime * 1000.0 * demoPlaybackSpeed;
                // Session-wide windows (see _demoSessionElapsedMs's field comment) — NOT gated by trial index, so
                // a fast-completing trial can't race the debounce.
                bool leanWindow = demoTriggerFormEvents && _demoSessionElapsedMs < DemoLeanWindowMs;
                bool lossWindow = demoTriggerFormEvents
                    && _demoSessionElapsedMs >= DemoLeanWindowMs + DemoTrackingLossGapMs
                    && _demoSessionElapsedMs < DemoLeanWindowMs + DemoTrackingLossGapMs + DemoTrackingLossWindowMs;
                leanCm = leanWindow ? DemoLeanSpikeCm : 0.0;
                trackingLow = false;
                trackingLost = lossWindow;
            }
            else
            {
                var side = HandSide.Right; // OrchardReachParams.Side isn't exposed per-trial here; Right covers the
                                            // common single-hand programs, same simplification MetaHandSource/live
                                            // callers already make elsewhere pending full side-profile support
                                            // (tracked as deferred scope in the run8 log).
                var confidence = hands?.GetConfidence(side) ?? TrackingConfidence.None;
                trackingLow = confidence == TrackingConfidence.Low;
                trackingLost = confidence == TrackingConfidence.None;

                if (headReference != null && _trialStartHeadPosCaptured)
                {
                    Vector3 towardTarget = (_fruitInstance != null ? _fruitInstance.transform.position : headReference.position) - _trialStartHeadPos;
                    Vector3 displacement = headReference.position - _trialStartHeadPos;
                    float leanM = towardTarget.sqrMagnitude > 1e-6f ? Vector3.Dot(displacement, towardTarget.normalized) : 0f;
                    leanCm = Math.Max(0.0, leanM * 100.0);
                }
                else
                {
                    leanCm = 0.0;
                }
            }

            _formFeedback.Tick(_lastNowMs, leanCm, trackingLow, trackingLost);
            RetryGatedLowConfidence();
            _haptic.Pump(_lastNowMs);
        }

        private void HandleFormSignal(FormWarningSignal sig)
        {
            OnFormSignal?.Invoke(sig);
            if (!sig.Active) return;

            if (sig.Kind == "trunk_lean")
            {
                double excess = Math.Max(0.0, sig.LeanCm - _module.Params.TrunkLeanWarningCm);
                _haptic.SendTrunkLean(excess, _lastNowMs);
            }
            else if (sig.Kind == "low_confidence")
            {
                _lowConfidencePending = !_haptic.SendLowConfidence(_lastNowMs);
                _lastLowConfidenceTryMs = _lastNowMs;
            }
        }

        // U4 follow-up (PH bisect): FormFeedback raises low_confidence ONCE per episode. The per-zone gap
        // (800 ms on the forearm motor) can swallow that single edge when a `success` pulse (whose second pulse
        // preempts the forearm zone) landed just before it, so a persistent tracking loss was never buzzed at all
        // (the demo's trial cadence of ~1 s made this near-deterministic). While the warning is still active and the
        // cue was gated, retry every 100 ms until it goes out or the episode ends.
        private bool _lowConfidencePending;
        private double _lastLowConfidenceTryMs;
        private void RetryGatedLowConfidence()
        {
            if (!_lowConfidencePending) return;
            if (!_formFeedback.LowConfidenceWarningActive || !_haptic.Enabled) { _lowConfidencePending = false; return; }
            if (!_haptic.Connected || _lastNowMs - _lastLowConfidenceTryMs < 100.0) return;
            _lastLowConfidenceTryMs = _lastNowMs;
            _lowConfidencePending = !_haptic.SendLowConfidence(_lastNowMs);
        }

        private void OnDisable() => StopHapticsSafely();
        // Run10 root-cause fix: OnDestroy previously only called HapticClient.Stop() (sends a device "stop"
        // envelope), never HapticClient.Dispose()/IHapticTransport.Dispose(). UdpHapticTransport's discovery
        // listener binds a FIXED port (8791, not ephemeral) in a background Task that Stop() never cancelled —
        // so on every scene reload (every PlayMode test, and any real in-game scene transition) the OLD
        // transport's discovery socket stayed bound forever, and the NEW controller's Awake()-constructed
        // transport's own `new UdpClient(DiscoveryPort)` bind then threw (caught, logged, discovery loop exited
        // early) — permanently HasDevice=false for that scene instance, so no cue could ever be gated/sent even
        // with a real fake_haptic.py broadcasting hello. Confirmed via run9_playmode3 logs: 3 PlayMode tests in
        // one Test Runner session (no domain reload between them) share one process's UDP port space, so only
        // the FIRST test's discovery bind could ever succeed. Disposing on OnDestroy (scene teardown / object
        // destruction) — not on OnDisable/OnApplicationPause, which must remain resumable — releases the port
        // before the next scene's Awake() tries to rebind it.
        private void OnDestroy()
        {
            StopHapticsSafely();
            try { _haptic?.Dispose(); } catch (Exception e) { Debug.LogWarning($"OrchardReachSceneController: haptic dispose warning: {e.Message}"); }
        }
        private void OnApplicationQuit() => StopHapticsSafely();
        private void OnApplicationPause(bool paused) { if (paused) StopHapticsSafely(); }

        /// <summary>Stop-on-pause/end/quit, per contracts/HAPTIC_PROTOCOL.md's safety section. Defensive against
        /// being called before Awake() has run (e.g. object destroyed mid-initialization) or more than once.</summary>
        private void StopHapticsSafely()
        {
            try { _haptic?.Stop(); } catch (Exception e) { Debug.LogWarning($"OrchardReachSceneController: StopHapticsSafely: {e.Message}"); }
        }

        /// <summary>
        /// Run9 (Opus review of run8: hand proxy near a table leg while the apple sat elsewhere at "grasp" —
        /// events were scripted, not contact). Root fix, replacing run8's two-independent-paths approach:
        ///  - Reach (TargetShown/MovementOnset): retarget the hand's visual position every frame from its own
        ///    reach-onset position toward the CURRENT apple position, using the fixture's own pinch-strength
        ///    ramp (0..GraspDetector.PinchThreshold) as the blend factor — guarantees the index tip is AT the
        ///    apple by the time the module's pinch-threshold-driven Grasped state fires (see field comment).
        ///  - Grasp (state becomes Grasped/Holding): snap the hand exactly onto the apple and attach (reparent)
        ///    the apple to the hand — one scripted path drives both from here on, not two.
        ///  - Carry: hand (with the attached apple) glides to a point above the basket's own measured collider
        ///    bounds (not a hand-typed constant — same "measure, don't guess" discipline as run8's pivot fix).
        ///  - Release: detach the apple at that above-basket point.
        ///  - Settle: the apple (now unparented) glides down, alone, into a point inside the basket's measured
        ///    bounds — this is what lets BasketTrigger's real physics OnTriggerEnter fire "placed" once its
        ///    collider actually crosses into the basket volume, same detection path as before, just aimed at a
        ///    real in-bounds destination instead of a hand-typed guess.
        /// </summary>
        private void DriveDemoHandAndFruit()
        {
            if (demoHandProxyR == null || _demoDriver == null || _fruitInstance == null) return;

            bool haveRaw = _demoDriver.TryGetJointPose(OpusJoints.RIndexTip, out var rawPos, out _);
            // + _chestOriginWorld: the fixture's joint position is chest-relative (see field comment above); add
            // our world chest reference so the raw reach-onset sample appears near the table/basket, not at
            // world origin.
            Vector3 rawWorld = haveRaw
                ? _chestOriginWorld + new Vector3((float)rawPos[0], (float)rawPos[1], (float)rawPos[2])
                : demoHandProxyR.position;

            demoHandProxyR.gameObject.SetActive(true);
            // Run8: the dressing tool hides this proxy via its Renderer (not GameObject.SetActive), so
            // GameObject.Find keeps finding it across re-runs (see OrchardSceneDressingTool's
            // FindInSceneIncludingInactive comment) — make it visible here explicitly.
            // Run11 (Opus review of run10's screenshot: "a debug cyan sphere is still visible behind the ghost
            // hand"): this proxy is a 3 cm cyan index-tip MARKER from before there was any hand mesh. It is still
            // the authoritative transform — DriveGhostHandVisual offsets the whole ghost hand off
            // demoHandProxyR.position, and the audit/seated-layout tests assert on it — so it must keep moving and
            // must NOT be deactivated. Only its *renderer* is suppressed, and only when the real hand mesh is
            // actually present and about to be drawn; with no ghost prefab in the scene the marker stays visible
            // as the fallback hand visual (exactly the fallback OrchardSceneDressingTool's warning describes).
            var rend = demoHandProxyR.GetComponent<Renderer>();
            if (rend != null) rend.enabled = demoHandGhostVisual == null;

            var state = _module.CurrentTrialState;

            switch (state)
            {
                case TrialState.TargetShown:
                case TrialState.MovementOnset:
                {
                    if (!_reachStartCaptured && haveRaw)
                    {
                        _reachStartWorld = rawWorld;
                        _reachStartCaptured = true;
                    }
                    float pinch = _demoDriver.GetPinchStrength(HandSide.Right);
                    float t = GraspDetector.PinchThreshold > 0f
                        ? Mathf.Clamp01(pinch / GraspDetector.PinchThreshold)
                        : 1f;
                    Vector3 start = _reachStartCaptured ? _reachStartWorld : rawWorld;
                    demoHandProxyR.position = Vector3.Lerp(start, _fruitInstance.transform.position, t);
                    break;
                }
                case TrialState.Grasped:
                case TrialState.Holding:
                {
                    if (_holdStartMs < 0)
                    {
                        // Moment of grasp: snap the hand exactly onto the apple (index tip == apple center,
                        // trivially satisfying "within 5cm at grasp" with margin to spare) and attach the apple
                        // to the hand so one motion carries both, instead of two independently scripted paths.
                        demoHandProxyR.position = _fruitInstance.transform.position;
                        if (_fruitInstance.transform.parent != demoHandProxyR)
                            _fruitInstance.transform.SetParent(demoHandProxyR, worldPositionStays: true);
                        if (_fruitRb != null) _fruitRb.linearVelocity = Vector3.zero;
                        _holdStartMs = _lastNowMs;
                        _holdStartPos = demoHandProxyR.position;
                    }
                    float glideT = (float)Math.Clamp((_lastNowMs - _holdStartMs) / HoldGlideDurationMs, 0.0, 1.0);
                    Vector3 releaseTarget = AboveBasketPoint();
                    demoHandProxyR.position = Vector3.Lerp(_holdStartPos, releaseTarget, glideT);
                    // The apple is parented to the hand -> follows automatically.

                    if (glideT >= 1f && _fruitInstance.transform.parent == demoHandProxyR)
                    {
                        // Release: detach so the apple's own (kinematic) collider settles independently of
                        // further hand motion, per the brief's "the hand carries it and releases over the
                        // basket" — this is the moment PlayMode asserts against (above rim, near basket center).
                        _fruitInstance.transform.SetParent(null, worldPositionStays: true);
                        // Run11 (flaky release assertion: 0.019 m, 0.000 m and 0.218 m across three otherwise
                        // identical runs): the glide drives the HAND to AboveBasketPoint, and the apple rides
                        // along as a child — so the apple lands at the release target plus whatever hand->apple
                        // offset the grasp happened to capture that run, which depends on where the fixture's
                        // recorded hand was when contact was detected. The demo release is scripted, not
                        // physical, and the thing that must be over the basket is the APPLE, so place the apple
                        // itself on the release target. (Real grab-interactable play is unaffected: this whole
                        // branch is behind useDemoDriver.)
                        _fruitInstance.transform.position = releaseTarget;
                        LastReleaseWorldPos = _fruitInstance.transform.position;
                        // Run14: shared eased-gravity + bounce drop (was a plain linear Lerp with no bounce).
                        StartBasketDrop(releaseTarget);
                    }
                    break;
                }
                default:
                {
                    _reachStartCaptured = false;
                    _holdStartMs = -1;
                    break;
                }
            }

            // TickBasketDrop() (called from Update, after this method) advances any drop in progress -- shared
            // with the real-hands release path, so both look and behave the same.

            DriveGhostHandVisual();
        }

        private Vector3 JointWorld(string joint)
        {
            if (_demoDriver != null && _demoDriver.TryGetJointPose(joint, out var p, out _))
                return _chestOriginWorld + new Vector3((float)p[0], (float)p[1], (float)p[2]);
            return demoHandProxyR.position;
        }

        /// <summary>
        /// Run9 (Opus task #3, refined per the orchestrator's follow-up): positions the project's real
        /// `ghost_hand_static` prefab instance (asset 286115) at the wrist each frame, replacing the sphere
        /// proxy with an actual hand mesh — "no sphere" for the visual, though `demoHandProxyR` (the small
        /// index-tip marker) is kept enabled alongside it because SeatedLayoutTests and this file's own PlayMode
        /// assertions read its exact position for the reach/grasp/carry/release/settle geometry.
        ///
        /// Run9 update (analytics v0.2 regenerated the fixtures with real wrist+palm `rot` quaternions —
        /// verified directly: contracts/fixtures/sessions/healthy/kin_000.json's r_wrist now carries
        /// `"rot": [x,y,z,w]` per frame, no longer null): the hand mesh is now oriented by that RECORDED wrist
        /// rotation when present, exactly per the brief. The wrist->index heading used previously is kept only
        /// as the fallback for frames/fixtures where `rot` is genuinely absent (SyntheticHandDriver.TryGetJointPose
        /// returns `rotQuatXyzw = null` in that case) — a documented approximation, not the default path anymore.
        /// Still a single static mesh (no per-finger posing) — a true grasp-shaped hand needs the ISDK/OVR
        /// synthetic-hand data pipeline this project has already deferred (docs/UNITY_PRACTICES.md §6).
        /// </summary>
        private void DriveGhostHandVisual()
        {
            if (demoHandGhostVisual == null) return;
            foreach (var r in demoHandGhostVisual.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

            Vector3 wrist = JointWorld(OpusJoints.RWrist);
            // Same rigid retarget offset as the index-tip marker (see DriveDemoHandAndFruit's caller comment) so
            // the whole hand visual moves together with wherever the index tip has been retargeted/carried to,
            // not the raw (scene-uncorrelated) fixture path.
            Vector3 offset = demoHandProxyR.position - JointWorld(OpusJoints.RIndexTip);
            Vector3 wristWorld = wrist + offset;
            demoHandGhostVisual.position = wristWorld;

            double[] rotXyzw = null;
            bool haveRot = _demoDriver != null && _demoDriver.TryGetJointPose(OpusJoints.RWrist, out _, out rotXyzw) && rotXyzw != null && rotXyzw.Length == 4;
            if (haveRot)
            {
                // Recorded joint rotations are xyzw, chest/calibration-relative like position — no translation
                // component to re-origin, so the raw quaternion applies directly in world space (the fixture's
                // calibration frame and this scene's world frame share the same orientation convention, per
                // TargetPlacement's existing chest-relative handling elsewhere in this class).
                demoHandGhostVisual.rotation = new Quaternion((float)rotXyzw[0], (float)rotXyzw[1], (float)rotXyzw[2], (float)rotXyzw[3]);
            }
            else
            {
                // Fallback: wrist -> index-tip heading, for any fixture/frame with no recorded rotation.
                Vector3 heading = demoHandProxyR.position - wristWorld;
                if (heading.sqrMagnitude > 1e-6f)
                    demoHandGhostVisual.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
            }
        }

        /// <summary>A point comfortably above the basket's own measured collider bounds (not a hand-typed
        /// constant — same "measure the real asset, don't guess" discipline as ImportMetaAssetsTool's pivot
        /// fix) so "release" is provably above the rim regardless of the basket's actual imported scale.</summary>
        private Vector3 AboveBasketPoint()
        {
            if (basketTrigger == null) return demoHandProxyR.position;
            var b = BasketBounds();
            return new Vector3(b.center.x, b.max.y + 0.08f, b.center.z);
        }

        /// <summary>A point inside the basket's own measured collider bounds, near its floor, for the apple to
        /// settle at after release.</summary>
        private Vector3 InsideBasketPoint()
        {
            if (basketTrigger == null) return LastReleaseWorldPos ?? _fruitInstance.transform.position;
            var b = BasketBounds();
            float y = Mathf.Min(b.min.y + 0.03f, b.max.y);
            return new Vector3(b.center.x, y, b.center.z);
        }

        private Bounds BasketBounds()
        {
            var col = basketTrigger.GetComponent<Collider>();
            return col != null ? col.bounds : new Bounds(basketTrigger.transform.position, Vector3.one * 0.1f);
        }

        private void OnFruitEnteredBasket()
        {
            var state = _module.CurrentTrialState;
            if (state == TrialState.Holding)
            {
                // Run9 bugfix: the real physics OnTriggerEnter (this callback) fires as soon as the fruit's
                // collider first overlaps the basket's trigger volume — which happens partway through the
                // separate cosmetic "settle" glide from AboveBasketPoint down into InsideBasketPoint (confirmed
                // via a debug capture: the module's "placed" event fired while the apple was still at y=0.83,
                // above the basket's own max.y=0.80 — physics detection and the scripted settle animation are
                // decoupled, so "placed" can be confirmed before the settle animation visually finishes). Since
                // "placed" is the authoritative confirmation, snap straight to the real inside-basket resting
                // point HERE rather than trusting the in-flight glide to have arrived yet — the apple must
                // actually BE inside the basket's bounds at the moment placement is confirmed, not just
                // eventually get there a quarter-second later.
                _fruitInstance.transform.position = InsideBasketPoint();
                _settleStartMs = -1;
                _settling = false;
                int trialsBefore = _module.TrialsCompleted;
                _module.ConfirmPlacedInBasket(_lastNowMs);
                // ConfirmPlacedInBasket (containerId=null) never withholds the reward -- it always reaches the
                // Place() branch -- so this is unconditionally a success worth celebrating.
                OnSuccessfulPlacement(_fruitInstance.transform.position);
                _ = trialsBefore; // (kept only for readability of the "always succeeds here" comment above)
            }
        }

        /// <summary>Run16 (stage D): mirror of <see cref="OnFruitEnteredBasket"/> for one of the 3 sorting
        /// containers. Reports the fruit's own color/ripe tags and (stage C) its measured upright tilt; the
        /// module decides correct-vs-wrong_target and whether any reward is due (see
        /// OrchardReachModule.ConfirmPlacedInContainer's doc comment) -- this method never re-derives that
        /// judgement itself, only supplies what only the scene can know (which physical container, the fruit's
        /// visual tags, its measured tilt).</summary>
        private void OnFruitEnteredContainer(BasketTrigger trigger, string containerId)
        {
            var state = _module.CurrentTrialState;
            if (state != TrialState.Holding) return;

            var marker = _fruitInstance != null ? _fruitInstance.GetComponentInChildren<FruitMarker>() : null;
            string color = marker != null ? marker.Color : null;
            bool ripe = marker == null || marker.Ripe;
            double? tiltDeg = _module.Params.Stage == "C_precision" ? FruitTiltDegAtPlacement() : (double?)null;

            var col = trigger.GetComponent<Collider>();
            Vector3 resting = col != null
                ? new Vector3(col.bounds.center.x, Mathf.Min(col.bounds.min.y + 0.03f, col.bounds.max.y), col.bounds.center.z)
                : trigger.transform.position;
            _fruitInstance.transform.position = resting;
            _settleStartMs = -1;
            _settling = false;

            bool wasSuccessOutcomePossible = true; // module decides; we just react to whether "placed" actually fired
            int successBefore = _module.SuccessCount;
            _module.ConfirmPlacedInContainer(_lastNowMs, containerId, color, ripe, tiltDeg);
            bool succeeded = _module.SuccessCount > successBefore;
            _ = wasSuccessOutcomePossible;
            if (succeeded) OnSuccessfulPlacement(resting);
            else _successStreak = 0; // wrong_target: no reward, streak resets (same "gentle, not punitive" spirit -- just no escalation)
        }

        /// <summary>Chime (pitch rises with streak) + a small success particle bloom, shared by every container's
        /// success path. Haptic success is unchanged -- it already fires from HandleTrialEvent's
        /// trial_end/success branch, independent of this.</summary>
        private void OnSuccessfulPlacement(Vector3 atWorldPos)
        {
            _successStreak++;
            bool bonus = _lastPlacementBonus;
            ProceduralChime.PlaySuccess(_successAudio, _successStreak, bonus);
            SpawnSuccessBloom(atWorldPos);
        }

        /// <summary>Set from the "placed"/trial_end trial event's data.bonus (module-side variable-ratio reward) so
        /// the chime/particle can react to it without this class re-deriving the same random draw.</summary>
        private bool _lastPlacementBonus;

        /// <summary>A brief particle burst at the placement point -- no imported VFX asset, a plain code-built
        /// ParticleSystem (small burst of bright dots, short lifetime, self-destructs once finished) so this
        /// works in any scene without a prefab reference to forget wiring.</summary>
        private static void SpawnSuccessBloom(Vector3 atWorldPos)
        {
            var go = new GameObject("SuccessBloom");
            go.transform.position = atWorldPos;
            var ps = go.AddComponent<ParticleSystem>();
            // Run16: AddComponent<ParticleSystem>() returns a system that is ALREADY PLAYING (playOnAwake
            // defaults to true), and Unity refuses to let `main.duration` be set on a playing system --
            // it logs "[Assert] Setting the duration while system is still playing is not supported".
            // That assert fired on EVERY successful placement and, because Unity's test framework fails a
            // test on any unexpected log, it turned 6 of the 8 PlayMode tests red (the whole suite except
            // the two that never complete a placement). Stop and clear first, then configure, then Play.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.2f;
            main.startSize = 0.03f;
            main.startColor = new Color(1f, 0.92f, 0.45f, 1f); // warm gold -- calm "reward", not an alarm color
            main.maxParticles = 24;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
            ps.Play();
            global::UnityEngine.Object.Destroy(go, 1.2f);
        }

        /// <summary>Stage C (precision): angle between the held fruit's current up axis and world up, measured at
        /// the moment of placement -- "must end upright" per the brief. Not clinically tuned (documented on the
        /// module's UprightTiltThresholdDeg constant); this only supplies the raw measurement.</summary>
        private double FruitTiltDegAtPlacement() =>
            _fruitInstance != null ? Vector3.Angle(_fruitInstance.transform.up, Vector3.up) : 0.0;

        private void HandleTrialEvent(TrialEvent e)
        {
            OnTrialEvent?.Invoke(e);

            // Run16 (task 3): capture the module's variable-ratio bonus flag off the SAME "placed" event
            // OnFruitEnteredBasket/Container is about to react to (ConfirmPlacedInContainer emits "placed"
            // synchronously, before returning to that caller -- see OrchardReachModule.EmitSignal), so by the
            // time OnSuccessfulPlacement reads _lastPlacementBonus it reflects THIS placement, not a stale one.
            if (e.Type == "placed")
            {
                _lastPlacementBonus = e.Data is System.Collections.Generic.Dictionary<string, object> d
                    && d.TryGetValue("bonus", out var bonusVal) && bonusVal is bool isBonus && isBonus;
            }

            if (e.Type == "trial_start")
            {
                // Run9 step 2/4: new trial -> new lean baseline + a clean debounce state (a stale exceed timer
                // or "already active" flag from the previous trial must not leak into this one).
                _currentTrialIndex = e.Trial ?? _currentTrialIndex;
                _currentTrialElapsedMs = 0;
                _lastPlacementBonus = false;
                // Run9 bugfix: do NOT reset FormFeedback here in demo mode — its lean-spike debounce window is
                // keyed off session-wide elapsed time (_demoSessionElapsedMs), not per-trial state, precisely so
                // a trial boundary can never race/clobber it (see _demoSessionElapsedMs's field comment). In
                // live mode a new trial DOES mean a new head-pose baseline, so the debounce state is reset there.
                if (!useDemoDriver) _formFeedback?.Reset();
                if (headReference != null)
                {
                    _trialStartHeadPos = headReference.position;
                    _trialStartHeadPosCaptured = true;
                }
            }

            if (e.Type == "trial_end" && e.Outcome == "success")
            {
                // Run9 step 3: success cue, per HAPTIC_PROTOCOL.md's cue table ("trial_end with outcome success").
                _haptic?.SendSuccess(_lastNowMs);
            }

            if ((e.Type == "target_shown" || e.Type == "trial_start") && e.Target != null && _fruitInstance != null)
            {
                // Run9 safety net: a new trial can start (e.g. the module's own drop-on-grip-relax path in
                // OrchardReachModule.Tick) while the PREVIOUS trial's fruit is still parented to the hand
                // mid-carry (glideT < 1) — without this, setting the fruit's world position below would leave it
                // still attached to the hand transform, so the next reach's hand motion would drag it along
                // instead of leaving it at the new target. Always detach first.
                // worldPositionStays MUST be true: with false, the fruit kept its LOCAL scale relative to the tiny
                // hand marker it was parented to (world 0.2 / marker 0.03 = 6.7x) and ballooned after trial 1.
                if (_fruitInstance.transform.parent != null)
                    _fruitInstance.transform.SetParent(null, worldPositionStays: true);
                // Run16 (stages A/C difficulty): targetSizeScale multiplies the base fruit scale -- stage A
                // ("large target") and stage C ("smaller/rotated fruit") both drive it from the SAME manifest
                // param, just in opposite directions; every other stage's default (1.0) leaves this a no-op,
                // byte-for-byte the pre-run16 scale.
                _fruitInstance.transform.localScale = _fruitBaseScale * (float)_module.Params.TargetSizeScale;

                // Run16 (stage D sorting): tag this trial's fruit with a color/ripeness so the container it
                // lands in can be checked against the block's sortRule. Cosmetic-only (material tint, no new
                // mesh) -- see FruitMarker.ApplyTint. Every other stage leaves the marker at its default
                // red/ripe, matching every pre-run16 scene's implicit (untagged) fruit exactly.
                var fruitMarker = _fruitInstance.GetComponentInChildren<FruitMarker>();
                if (fruitMarker != null)
                {
                    if (_module.Params.Stage == "D_sorting")
                    {
                        fruitMarker.Color = UnityEngine.Random.value < 0.5f ? "red" : "green";
                        fruitMarker.Ripe = UnityEngine.Random.value < 0.7f; // mostly ripe, some unripe to sort out
                    }
                    else
                    {
                        fruitMarker.Color = "red";
                        fruitMarker.Ripe = true;
                    }
                    fruitMarker.ApplyTint();
                }
                // Reset the carry/release/settle state machine for the new trial.
                _holdStartMs = -1;
                _settleStartMs = -1;
                _settling = false;
                LastReleaseWorldPos = null;
                _reachStartCaptured = false;
                _holding = false;
                _releaseFrames = 0;
                if (!useDemoDriver)
                {
                    // Movement onset is measured from where the hand is NOW, when the fruit appears.
                    var s = _module.CurrentSide == "left" ? HandSide.Left : HandSide.Right;
                    string j = s == HandSide.Left ? OpusJoints.LWrist : OpusJoints.RWrist;
                    _onsetRefWrist = _liveHands != null && _liveHands.TryGetJointPose(j, out var w, out _) ? V(w) : (Vector3?)null;
                }

                var p = e.Target.Pos;
                if (p != null && p.Length == 3)
                {
                    // Run12: the event's target is now chest-relative (same frame as the recorded kinematics --
                    // see OrchardReachModule.SubtractChest and the endpoint_error_cm bug it fixes), so add the
                    // world chest origin back to place the apple, exactly as JointWorld already does for
                    // recorded joint positions.
                    _fruitInstance.transform.position = _chestOriginWorld + new Vector3((float)p[0], (float)p[1], (float)p[2]);
                    // Run16 (stage C precision): "smaller, rotated fruit" -- a random starting tilt so upright
                    // placement is an actual skill to demonstrate, not automatically true. Every other stage
                    // keeps the identity rotation (unchanged pre-run16 behaviour).
                    _fruitInstance.transform.rotation = _module.Params.Stage == "C_precision"
                        ? Quaternion.Euler(UnityEngine.Random.Range(-35f, 35f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-35f, 35f))
                        : Quaternion.identity;
                    if (_fruitRb != null) _fruitRb.linearVelocity = Vector3.zero;
                }
            }
        }

        private static string DefaultManifestJson() => "{\"id\":\"orchard_reach\",\"version\":\"0.1.0\",\"sdkVersion\":\"^0.1.0\",\"paramSchema\":{\"type\":\"object\",\"properties\":{}}}";
    }
}
