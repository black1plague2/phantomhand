using System;
using Newtonsoft.Json.Linq;
using Opus.Games.PhantomHand;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Opus.Shell
{
    /// <summary>Test / tooling hooks read once by <see cref="PhantomHandSceneController"/>.Awake. Static so a PlayMode test can set them before the scene loads.</summary>
    public static class PhantomHandOverrides
    {
        public static JObject ParamOverrides;        // merged over the program's params (tests: demo_mode)
        public static int? Seed;
        public static bool? ForceDemoHands;          // true = scripted hands even with a headset
        public static bool? ScriptedParticipant;     // null = settings
        public static bool? DriveSession;            // true = the session runner runs without a headset
        public static bool? AutoStart;               // true = start a run by itself
        public static Func<string, string> Env;     // environment reader; null = Environment.GetEnvironmentVariable

        public static void Reset()
        {
            ParamOverrides = null; Seed = null; ForceDemoHands = null; ScriptedParticipant = null;
            DriveSession = null; AutoStart = null; Env = null;
        }
    }

    /// <summary>
    /// Composition root of Phantom Hand (PH U5). Wires one run end to end: SessionClock, the hand source (MetaHandSource in the headset,
    /// ScriptedHands without one), the game module (GameRegistry.Create("phantom_hand") + manifest + params), the haptic client and the
    /// two wearable-node clients (Node A haptic/IMU, Node B bio/EMG), the U3 and U4 presenters, and the recorders (trial events,
    /// kinematics of both hands, sensors). Implements <see cref="ISessionHost"/> so <see cref="OpusSessionRunner"/> drives the hub link.
    ///
    /// Frame order: clients Pump -> (scripted participant) -> module Tick -> presenters -> recorders.
    /// Fail-safe (PRD 11): no Node A = strokes are logged delivered=false (HUD "Sleeve offline"); no Node B = EMG null; no hub = recorded
    /// locally and uploaded later (the runner). Stop-safe: on disable, pause, quit and session end the sleeve gets Stop and the OLED "IDLE".
    /// One scene instance plays one run: the presenters are bound once, so the next person gets a scene reload (<see cref="PrepareNextSession"/>).
    /// </summary>
    public sealed class PhantomHandSceneController : MonoBehaviour, ISessionHost
    {
        public enum HandMode { Auto, Demo, RealHands }

        [SerializeField] private PhantomHandSettings settings;
        [SerializeField] private TextAsset manifestJson;
        [SerializeField] private PhantomAnchors anchors;
        [SerializeField] private ArmThreatPresenter armPresenter;
        [SerializeField] private PhantomHandUiPresenter uiPresenter;
        [SerializeField] private HandMode handMode = HandMode.Auto;
        [Tooltip("Let the session runner talk to the hub even without a headset (editor verification).")]
        [SerializeField] private bool driveSessionWithoutHeadset = false;

        private const double KinIntervalMs = 1000.0 / 72.0;
        private const string GameIdConst = "phantom_hand";

        private Func<string, string> _env;
        private PhantomEndpoints _ep;
        private GameManifest _manifest;
        private bool _useDemo;
        private SessionClock _clock;
        private UdpHapticTransport _transportA, _transportB;
        private HapticClient _haptic;
        private SleeveSensorClient _nodeA, _nodeB;
        private IHandSource _hands;
        private ScriptedHands _scripted;
        private PhantomAutoParticipant _auto;
        private PhantomHandModule _module;
        private HapticCueEventAdapter _cues;
        private KinematicsRecorder _kinRecorder;
        private KinematicsSampler _kinSampler;
        private SensorRecorder _sensRecorder;
        private TrackingRateMeter _rateMeter;
        private readonly TraceDownsampler _trace = new TraceDownsampler();
        private double _kinLastMs = -1;
        private bool _bound, _sessionUsed;
        private string _pendingOrder;
        private string _autoStartReason;
        private float _headSec;
        private PhPhase _prevPhase = PhPhase.Idle;
        private int? _prevCond;
        private int _kinChunks;
        private string _boundGeometry;
        private OpusSessionRunner _runner;
        private PhantomStandbyCard _standby;
        private float _standbyT;
        private string _standbyIp;
        private string _defaultArm = "right";   // the manifest's stimulated_side, for the Ready card (a program may still choose the other)
        private Func<string> _diag;

        private sealed class Ctx : ISessionContext
        {
            public SessionClock Clock { get; private set; }
            public int BlockIndex { get { return 0; } }
            public Ctx(SessionClock c) { Clock = c; }
        }

        // ---- public surface -----------------------------------------------------------------------------------------
        public PhantomHandModule Module { get { return _module; } }
        public HapticClient Haptic { get { return _haptic; } }
        public SleeveSensorClient NodeA { get { return _nodeA; } }
        public SleeveSensorClient NodeB { get { return _nodeB; } }
        public PhantomEndpoints Endpoints { get { return _ep; } }
        public PhantomAutoParticipant AutoParticipant { get { return _auto; } }
        public PhantomStandbyCard StandbyCard { get { return _standby; } }
        public bool IsBound { get { return _bound; } }
        public int SensorChunksWritten { get; private set; }
        public int HapticEventsScheduled { get { return _cues != null ? _cues.Scheduled : 0; } }
        public int HapticEventsDelivered { get { return _cues != null ? _cues.Delivered : 0; } }

        // ---- ISessionHost -------------------------------------------------------------------------------------------
        public string GameId { get { return GameIdConst; } }
        public string GameVersion { get { return _manifest != null && !string.IsNullOrEmpty(_manifest.Version) ? _manifest.Version : "0.1.0"; } }
        public string ContractsVersion { get { return "0.2"; } }
        public string AffectedSide { get { return "none"; } }
        public double? MeasuredTrackingRateHz
        {
            get { return !_useDemo && _rateMeter != null && _rateMeter.LastMeasuredHz > 0 ? _rateMeter.LastMeasuredHz : (double?)null; }
        }
        public string HubHostHint { get { return _ep != null ? _ep.HubHost : null; } }
        public bool UsingRealHands { get { return !_useDemo; } }
        public IHandSource Hands { get { return _hands; } }
        public SessionClock Clock { get { return _clock; } }
        public float HeadTrackedSeconds { get { return _headSec; } }
        public bool IsModuleRunning { get { return _module != null && _module.IsRunning; } }
        public bool IsSessionComplete { get { return _module != null && _module.Machine != null && _module.Machine.IsDone; } }
        public bool NeedsFreshScene { get { return _sessionUsed; } }
        public bool AutoStartRequested
        {
            get { return _autoStartReason != null && !_sessionUsed && (_useDemo || _headSec >= 2.5f); }
        }
        public string AutoStartReason { get { return _autoStartReason; } }
        public event Action<TrialEvent> OnTrialEvent;
        public event Action<int, JObject> OnMetricsTick;

        // ---- lifecycle ----------------------------------------------------------------------------------------------

        // The scene is saved with a right arm lying beside its outline, and both stayed on show until the first run was bound: a
        // wearer read "Sleeve arm: left" and saw a right arm with its outline on the right. Until then the scene now looks like
        // every idle after a run: no arm, no outline, and the layout of the arm the Ready card names.
        private void Start()
        {
            if (_bound) return;
            if (anchors != null)
            {
                anchors.LayOutFor(_defaultArm == "left" ? HandSide.Left : HandSide.Right);
                if (anchors.armRestOutline != null) anchors.armRestOutline.gameObject.SetActive(false);
            }
            if (armPresenter != null && armPresenter.arm != null) armPresenter.arm.Visible = false;
        }

        private void Awake()
        {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _env = PhantomHandOverrides.Env ?? Environment.GetEnvironmentVariable;
            if (settings == null) settings = Resources.Load<PhantomHandSettings>(PhantomHandSettings.ResourcePath);
            if (settings == null) settings = ScriptableObject.CreateInstance<PhantomHandSettings>(); // defaults: discovery everywhere
            _ep = PhantomEndpoints.Resolve(_env, settings);
            if (_ep.HubPort != 0 && _ep.HubPort != PhantomEndpoints.DefaultHubPort)
                Debug.LogWarning("[PhantomHand] hub port " + _ep.HubPort + " requested but LiveClient always uses " + PhantomEndpoints.DefaultHubPort + " (CROSS-TRACK request to the SDK owner); using host only.");
            LoadManifest();
            try
            {
                if (_manifest != null) _defaultArm = PhantomHandParams.From(ParamBinder.Bind(_manifest.ParamSchema, new JObject()).Params).StimulatedSide;
            }
            catch (Exception) { /* the card then says "right", the code's own default */ }

            if (anchors == null) anchors = GetComponent<PhantomAnchors>();
            if (anchors == null) anchors = FindFirstObjectByType<PhantomAnchors>();
            if (armPresenter == null) armPresenter = GetComponentInChildren<ArmThreatPresenter>(true);
            if (armPresenter == null) armPresenter = FindFirstObjectByType<ArmThreatPresenter>();
            if (uiPresenter == null) uiPresenter = GetComponentInChildren<PhantomHandUiPresenter>(true);
            if (uiPresenter == null) uiPresenter = FindFirstObjectByType<PhantomHandUiPresenter>();

            bool real = handMode == HandMode.RealHands || (handMode == HandMode.Auto && HeadsetActive());
            if (PhantomHandOverrides.ForceDemoHands == true) real = false;
            _useDemo = !real;

            AndroidMulticastLock.Acquire();   // Quest: without it Android drops the node discovery beacons (UDP broadcast)
            _clock = new SessionClock();
            _transportA = new UdpHapticTransport(_ep.NodeAHost, DiscoveryFilter.Haptic, _ep.NodeAPort, _ep.DiscoveryPort, _ep.TelemetryPort);
            _haptic = new HapticClient(_transportA) { Enabled = false, MaxIntensity = 1.0 };
            _haptic.Clock = () => _clock.NowMs;
            _haptic.OnCueRecorded += OnHapticCue;
            _haptic.Start();
            _transportB = new UdpHapticTransport(_ep.NodeBHost, DiscoveryFilter.Bio, _ep.NodeBPort, _ep.DiscoveryPort, _ep.TelemetryPort);
            _transportB.Start();

            if (_useDemo)
            {
                _scripted = new ScriptedHands();
                if (anchors != null && anchors.armRestOutline != null)
                {
                    var f = anchors.armRestOutline.forward; f.y = 0;
                    if (f.sqrMagnitude > 1e-6f) { f.Normalize(); _scripted.RightAxis = new double[] { f.x, 0, f.z }; }
                }
                _hands = _scripted;
                bool scripted = PhantomHandOverrides.ScriptedParticipant ?? settings.scriptedParticipantWithoutHeadset;
                if (scripted)
                {
                    _auto = new PhantomAutoParticipant(_scripted) { CalibrationTarget = CalibrationTargetArray };
                    _auto.SubmitCalibrationItself = uiPresenter == null;
                    _auto.AnswerPoint = v =>
                    {
                        Vector3? p = uiPresenter != null && _bound ? uiPresenter.AnswerPoint(v) : null;
                        return p.HasValue ? new double[] { p.Value.x, p.Value.y, p.Value.z } : null;
                    };
                }
            }
            else _hands = BuildLiveHandSource();

            _autoStartReason = ProgramHandoff.TakeAutoStart();
            if (_autoStartReason == null && PhantomHandOverrides.AutoStart == true) _autoStartReason = "test auto-start";
            if (_autoStartReason == null && settings.autoStartOnHeadset && !_useDemo) _autoStartReason = "auto-start on headset";

            if (Application.isPlaying)
            {
                var runner = GetComponent<OpusSessionRunner>();
                if (runner == null) runner = gameObject.AddComponent<OpusSessionRunner>();
                runner.RunWithDemoDriver = _ep.FromEnvironment || driveSessionWithoutHeadset || PhantomHandOverrides.DriveSession == true;
                _runner = runner;
            }
            _diag = DiagnosticsLine;
            DeviceDiagnostics.Status = _diag;
            Debug.Log("[PhantomHand] controller up: hands=" + (_useDemo ? "scripted (no headset)" : "REAL tracked hands") +
                      ", hub=" + (_ep.HubHost ?? "discover") + ", nodeA=" + (_ep.NodeAHost ?? "discover") + ", nodeB=" + (_ep.NodeBHost ?? "discover") +
                      ", discoveryPort=" + _ep.DiscoveryPort + ", ui=" + (uiPresenter != null) + ", arm=" + (armPresenter != null));
        }

        private void LoadManifest()
        {
            string text = ManifestText();
            if (string.IsNullOrEmpty(text)) { Debug.LogError("[PhantomHand] no manifest text (assign it on the controller or in PhantomHandSettings)"); return; }
            try { _manifest = GameManifest.FromJson(text); }
            catch (Exception e) { Debug.LogError("[PhantomHand] manifest failed to parse: " + e.Message); }
        }

        private string ManifestText()
        {
            if (manifestJson != null) return manifestJson.text;
            if (settings != null && settings.manifest != null) return settings.manifest.text;
#if UNITY_EDITOR
            var ta = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Games/PhantomHand/manifest.json");
            if (ta != null) return ta.text;
#endif
            return null;
        }

        private static bool HeadsetActive()
        {
            if (!Application.isEditor) return true;
            if (Application.isBatchMode) return false;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
            try
            {
                var t = Type.GetType("UnityEngine.XR.Management.XRGeneralSettings, Unity.XR.Management");
                var inst = t != null ? t.GetProperty("Instance").GetValue(null) : null;
                var mgr = inst != null ? inst.GetType().GetProperty("Manager").GetValue(inst) : null;
                var loader = mgr != null ? mgr.GetType().GetProperty("activeLoader").GetValue(mgr) : null;
                if (loader != null) return true;
            }
            catch (Exception) { /* fall through */ }
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.Count > 0;
        }

        private IHandSource BuildLiveHandSource()
        {
#if OPUS_META_HANDS
            var leftGo = GameObject.Find("OVRHandDataSourceLeft");
            var rightGo = GameObject.Find("OVRHandDataSourceRight");
            // The Hmd component sits on "OVRHmd" (OVRHmdDataSource only feeds it).
            var hmdGo = GameObject.Find("OVRHmd");
            if (hmdGo == null) hmdGo = GameObject.Find("OVRHmdDataSource");
            var leftHand = leftGo != null ? leftGo.GetComponent<Oculus.Interaction.Input.IHand>() : null;
            var rightHand = rightGo != null ? rightGo.GetComponent<Oculus.Interaction.Input.IHand>() : null;
            var hmd = hmdGo != null ? hmdGo.GetComponent<Oculus.Interaction.Input.IHmd>() : null;
            if (leftHand == null || rightHand == null)
                Debug.LogWarning("[PhantomHand] could not find the raw OVR hand sources by name; hand tracking will report untracked.");
            return new MetaHandSource(leftHand, rightHand, hmd);
#else
            return new MetaHandSource(null, null, null);
#endif
        }

        private double[] CalibrationTargetArray()
        {
            if (uiPresenter != null && _bound)
            {
                var v = uiPresenter.CalibrationTarget();
                return new double[] { v.x, v.y, v.z };
            }
            if (anchors != null && anchors.armRestOutline != null && _module != null)
            {
                var o = anchors.armRestOutline;
                var p = o.position + o.forward * (float)(_module.Params.ForearmLengthCm / 100.0) + Vector3.up * 0.021f;
                return new double[] { p.x, p.y, p.z };
            }
            return new double[] { PhArm.X(_module != null ? _module.Params.Arm : HandSide.Right, 0.18), 0.771, 0.40 };
        }

        // ---- per frame ----------------------------------------------------------------------------------------------

        private void Update()
        {
            if (_clock == null) return;
            double now = _clock.NowMs;
            TrackHead(Time.deltaTime);
            if (!_useDemo) KeepSeated();

            // 1. clients Pump
            _haptic.Pump(now);
            DrainCueAcks();
            if (_nodeA != null) _nodeA.Pump();
            if (_nodeB != null) _nodeB.Pump();

            if (_module == null)
            {
                if (_scripted != null) _scripted.Step();
                TickStandby();
                return;
            }
            // 2. module Tick (the scripted participant is an input device: it acts just before)
            if (_auto != null) _auto.Tick(now, _module);
            _module.Tick(now);
            ObservePhase();
            // 3. presenters
            if (_bound)
            {
                if (armPresenter != null) armPresenter.Tick();
                if (uiPresenter != null) uiPresenter.Tick();
            }
            // 4. recorders
            RecordFrame(now);
            if (_cues != null) _cues.Tick(now);
        }

        /// <summary>Before the run: the three links and how to start (the both-hands pinch is the runner's), refreshed twice a second.</summary>
        private void TickStandby()
        {
            if (_sessionUsed || _autoStartReason != null || !Application.isPlaying) return;
            float hold = _runner != null ? _runner.PinchHoldProgress : 0f;
            _standbyT -= Time.unscaledDeltaTime;
            if (_standbyT > 0f && hold <= 0f) return;
            if (_standbyT <= 0f) { _standbyT = 0.5f; _standbyIp = DeviceDiagnostics.LocalIPv4(); }
            if (_standby == null)
            {
                Camera cam = anchors != null && anchors.cameraRig != null ? anchors.cameraRig.GetComponentInChildren<Camera>(true) : null;
                if (cam == null) cam = Camera.main;
                var at = anchors != null ? anchors.witnessPanel : null;
                _standby = new PhantomStandbyCard(at != null ? at.position : new Vector3(0f, 1.30f, 0.95f),
                                                  at != null ? at.rotation : Quaternion.Euler(8f, 0f, 0f), cam);
            }
            bool hub = HubConnected();
            // An address given by hand (phantom_endpoints.json, the settings asset) switches the search off: say so, or a stale one looks like a dead app.
            string fixedHub = !hub && !string.IsNullOrEmpty(_ep.HubHost) ? "  (only " + _ep.HubHost + " is tried: it was set by hand)" : "";
            string body = "Operator app:   " + (hub ? "connected" : "not found yet") + fixedHub + "\n" +
                          "Sleeve:   " + (_transportA.HasDevice ? "found" : "not found yet") + "\n" +
                          "Muscle sensor:   " + (_transportB.HasDevice ? "found" : "not found yet") + "\n" +
                          "Sleeve arm:   " + _defaultArm + "  (the other hand points and answers)\n\n" +
                          (_useDemo ? "No headset: the hands are scripted."
                                    : hub ? "The operator starts the run, or pinch both hands for 2 seconds."
                                          : "To start without the app: pinch both hands for 2 seconds.") + "\n" +
                          "The run also works without the sleeve and the sensor.";
            string foot = (DeviceDiagnostics.DeviceId ?? "") + "    " + (_standbyIp ?? "no network") +
                          (DeviceDiagnostics.BoundPort > 0 ? "    logs: port " + DeviceDiagnostics.BoundPort : "");
            _standby.Show("Ready", body, foot, hold);
        }

        private bool HubConnected() { return _runner != null && _runner.Client != null && _runner.Client.IsConnected; }

        /// <summary>The scene in one line, for the health line of the device log (<see cref="DeviceDiagnostics.Status"/>): what an operator
        /// app would show, for a run that has none.</summary>
        private string DiagnosticsLine()
        {
            return "phase " + (_module != null ? PhNames.Of(_module.CurrentPhase) : "idle") +
                   ", operator app " + (HubConnected() ? "connected" : "not connected") +
                   ", arm " + (_module != null ? _module.Params.StimulatedSide : _defaultArm) +
                   ", sleeve " + NodeWord(_transportA, _nodeA) + ", muscle sensor " + NodeWord(_transportB, _nodeB) +
                   ", strokes acked " + HapticEventsDelivered + " of " + HapticEventsScheduled +
                   ", hands " + (_useDemo ? "scripted" : HandWord(HandSide.Left) + "/" + HandWord(HandSide.Right));
        }

        private string NodeWord(UdpHapticTransport t, SleeveSensorClient c)
        {
            if (t == null || !t.HasDevice) return "not found";
            return t.DeviceEndpoint + (c == null || !_asking ? "" : c.Connected ? " streaming" : " silent");
        }

        private string HandWord(HandSide s) { return _hands != null && _hands.IsTracked(s) ? "tracked" : "lost"; }

        private void TrackHead(float dt)
        {
            double[] p, r;
            bool ok = _hands != null && _hands.TryGetJointPose(OpusJoints.Head, out p, out r) && p != null &&
                      (p[0] * p[0] + p[1] * p[1] + p[2] * p[2]) > 1e-6;
            _headSec = ok ? _headSec + dt : 0f;
        }

        private void RecordFrame(double now)
        {
            if (_rateMeter != null && _hands != null) _rateMeter.Sample(now, _hands);
            if (_kinSampler != null && (_kinLastMs < 0 || now - _kinLastMs >= KinIntervalMs))
            {
                _kinLastMs = now;
                _kinSampler.SampleFrame(now);
            }
            if (_sensRecorder != null) _sensRecorder.Tick();
        }

        private void ObservePhase()
        {
            var ph = _module.CurrentPhase;
            if (ph == _prevPhase) return;
            if (_prevPhase == PhPhase.Questionnaire) TickMetrics(_prevCond);
            _prevPhase = ph;
            _prevCond = _module.CurrentConditionIndex;
        }

        private void TickMetrics(int? cond)
        {
            if (!cond.HasValue || _module == null || cond.Value < 0 || cond.Value > 1) return;
            var m = PhantomLiveStatus.MetricsFor(_module.Results[cond.Value]);
            if (m == null) return;
            var h = OnMetricsTick;
            if (h != null) h(cond.Value + 1, m);
        }

        // ---- events -------------------------------------------------------------------------------------------------

        private void HandleModuleEvent(TrialEvent e)
        {
            if (e.Type == "drift_probe" || e.Type == "threat_response") TickMetrics(e.Trial);
            RaiseEvent(e);
        }

        private void RaiseEvent(TrialEvent e)
        {
            var h = OnTrialEvent;
            if (h != null) h(e);
        }

        // Acks reach OnHapticCue on the transport's receive thread (HapticClient.HandleMessage). The adapter, the event file and the
        // live link are main-thread only, so those records wait here until Update, or until the session ends.
        private readonly System.Collections.Concurrent.ConcurrentQueue<HapticCueRecord> _cueAcks = new System.Collections.Concurrent.ConcurrentQueue<HapticCueRecord>();
        private int _mainThreadId;

        private void OnHapticCue(HapticCueRecord r)
        {
            if (_mainThreadId != 0 && System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThreadId) { _cueAcks.Enqueue(r); return; }
            if (_cues != null) _cues.OnRecord(r, _clock.NowMs);
        }

        private void DrainCueAcks()
        {
            HapticCueRecord r;
            while (_cueAcks.TryDequeue(out r))
                if (_cues != null && _clock != null) _cues.OnRecord(r, _clock.NowMs);
        }

        // ---- session ------------------------------------------------------------------------------------------------

        public void StartNewSession(JObject overrides)
        {
            if (_sessionUsed) Debug.LogWarning("[PhantomHand] StartNewSession on a scene that already ran: presenters are not rebound; reload instead.");
            _sessionUsed = true;
            if (_standby != null) _standby.Hide();
            _clock = new SessionClock();
            _trace.Reset();
            _kinChunks = 0; SensorChunksWritten = 0;

            var module = (PhantomHandModule)GameRegistry.Create(GameIdConst);
            module.LoadManifest(ManifestText());
            module.AdditionsEnabled = settings.additionsEnabled;
            int seed = PhantomHandOverrides.Seed ?? (settings.seed != 0 ? settings.seed : (Guid.NewGuid().GetHashCode() & 0x7fffffff));
            module.SetSeed(seed);

            var merged = new JObject();
            if (overrides != null) merged.Merge(overrides.DeepClone());
            if (_pendingOrder != null) merged["condition_order"] = _pendingOrder;
            if (PhantomHandOverrides.ParamOverrides != null) merged.Merge(PhantomHandOverrides.ParamOverrides.DeepClone());
            var bind = ParamBinder.Bind(module.Manifest.ParamSchema, merged);
            if (!bind.IsValid) Debug.LogWarning("[PhantomHand] param bind warnings: " + string.Join("; ", bind.Errors));
            module.Configure(bind.Params, new Ctx(_clock));
            module.OnTrialEvent += HandleModuleEvent;
            _module = module;
            if (_scripted != null) _scripted.Arm = module.Params.Arm;
            _blockParams = (JObject)bind.Params.Raw.DeepClone();
            _blockParams["seed"] = seed;

            _haptic.Enabled = module.Params.HapticsEnabled;
            _haptic.MaxIntensity = module.Params.HapticMaxIntensity;

            _nodeA = new SleeveSensorClient(_transportA, _clock); _nodeA.Start();
            _nodeB = new SleeveSensorClient(_transportB, _clock); _nodeB.Start();
            // Node B emg_burst -> events.ndjson (PRD 9.4, 03-SPEC 7); raised from SleeveSensorClient.Pump on the main thread
            _nodeB.OnEmgBurst += b => module.SubmitEmgBurst(b.Peak, b.BaselineRms, b.DeviceMs);
            // Sensor samples go to the CURRENT recorder only. (SensorRecorder.Attach subscribes for good: after the session ended the recorder
            // kept writing a sens_###.json every 5 s into the closed session folder, after the uploader had listed the files.)
            ForwardSamplesToRecorder(_nodeA); ForwardSamplesToRecorder(_nodeB);
            _haptic.StartKeepalive(_clock.NowMs);
            _asking = true;

            _cues = new HapticCueEventAdapter { Trial = () => _module != null ? _module.CurrentConditionIndex : null };
            _cues.OnEvent += RaiseEvent;

            if (!_bound) BindPresenters();
            else RefreshGeometryIfChanged();
            if (_scripted != null && anchors != null && anchors.armRestOutline != null)
            {
                var f = anchors.armRestOutline.forward; f.y = 0;   // the outline as laid out for this run's arm
                if (f.sqrMagnitude > 1e-6f) { f.Normalize(); _scripted.RightAxis = new double[] { f.x, 0, f.z }; }
            }
            _prevPhase = PhPhase.Idle; _prevCond = null;

            Debug.Log("[PhantomHand] session start: seed=" + seed + " order=" + module.Params.ConditionOrder + " demo_mode=" + module.Params.DemoMode +
                      " arm=" + module.Params.StimulatedSide);
            module.Begin(); // emits block_start + phase_start(calibrate) synchronously; the runner replays them
        }

        private JObject _blockParams;

        private void ForwardSamplesToRecorder(SleeveSensorClient client)
        {
            client.OnImuSample += x => { if (_sensRecorder != null) _sensRecorder.AddImu(x); };
            client.OnEmgChunk += x => { if (_sensRecorder != null) _sensRecorder.AddEmgChunk(x); };
        }

        private void BindPresenters()
        {
            if (armPresenter != null)
            {
                if (armPresenter.anchors == null) armPresenter.anchors = anchors;
                armPresenter.Bind(_module, _haptic, _nodeA, _nodeB, _clock, _hands);
            }
            if (uiPresenter != null)
            {
                if (uiPresenter.anchors == null) uiPresenter.anchors = anchors;
                uiPresenter.Bind(_module, _haptic, _nodeA, _nodeB, _clock, _hands);
            }
            _boundGeometry = GeometryKey(_module.Params);
            _bound = true;
        }

        private static string GeometryKey(PhantomHandParams p)
        {
            return p.ForearmLengthCm + "/" + p.MotorAFromWristCm + "/" + p.MotorSpacingCm;
        }

        private void RefreshGeometryIfChanged()
        {
            if (!_bound || armPresenter == null || armPresenter.arm == null) return;
            string k = GeometryKey(_module.Params);
            if (k == _boundGeometry) return;
            armPresenter.arm.Build(_module.Params);
            _boundGeometry = k;
        }

        public void PauseSession()
        {
            if (_module != null) _module.Pause();
            if (_haptic != null) { try { _haptic.Stop(); } catch (Exception e) { Debug.LogWarning("[PhantomHand] pause stop: " + e.Message); } }
        }

        public void ResumeSession()
        {
            if (_module != null) _module.Resume();
        }

        public void EndSession()
        {
            if (_module != null) _module.End();
            DrainCueAcks();
            if (_cues != null) _cues.FlushAll();
            StopSleeveSafely("end");
            if (_haptic != null) _haptic.StopKeepalive();
            if (_nodeA != null) _nodeA.Stop();
            if (_nodeB != null) _nodeB.Stop();
            _asking = false;
        }

        // The boards stream only while a run asks them to (the keepalive of a session). Between runs a board that is found is
        // ready, not lost: on the headset (9 Oct) the phone showed both sensors as gone after every run, until the next Start.
        private bool _asking;

        /// <summary>The operator's "recenter": the wearer back onto the scene's seat. Refused once a run has taken its calibration (the
        /// arm's position was measured in the room as it stands).</summary>
        public bool Recenter() { return FreeToSeat() && Seat(); }

        // ---- the seat ------------------------------------------------------------------------------------------------

        private Transform _head;
        private bool _seated, _headLocalKnown;
        private Vector3 _headLocalLast;
        private float _headYawLast;

        private Transform Head()
        {
            if (_head == null && anchors != null && anchors.cameraRig != null)
            {
                var cam = anchors.cameraRig.GetComponentInChildren<Camera>(true);
                _head = cam != null ? cam.transform : null;
            }
            return _head;
        }

        private bool Seat()
        {
            var head = Head();
            Vector3 eye = anchors != null && anchors.seatedEyePose != null ? anchors.seatedEyePose.position : new Vector3(0f, 1.18f, 0.02f);
            if (!PhSeat.Align(anchors != null ? anchors.cameraRig : null, head, eye)) return false;
            Debug.Log("[PhantomHand] seated: the head is at the scene's eye point " + eye.ToString("F2") + ", camera rig at " + anchors.cameraRig.position.ToString("F2"));
            return true;
        }

        /// <summary>Before a run, during its calibration until the arm is taken, and after it: the room may still be moved under the wearer.</summary>
        private bool FreeToSeat()
        {
            if (_module == null) return true;
            var ph = _module.CurrentPhase;
            if (ph == PhPhase.Idle || ph == PhPhase.Done) return true;
            return ph == PhPhase.Calibrate && (uiPresenter == null || uiPresenter.Calibration == null || uiPresenter.Calibration.State != CalibState.Confirmed);
        }

        /// <summary>Seats the wearer once the head has been tracked for a moment, and again whenever the headset's own origin jumps
        /// (the system's recentre), as long as no run depends on where the room stands.</summary>
        private void KeepSeated()
        {
            var head = Head();
            if (head == null) return;
            Vector3 local = head.localPosition; float yaw = head.localEulerAngles.y;
            bool jumped = _headLocalKnown && PhSeat.Jumped(_headLocalLast, _headYawLast, local, yaw);
            _headLocalLast = local; _headYawLast = yaw; _headLocalKnown = true;
            if (!FreeToSeat()) return;
            if (!_seated) { if (_headSec >= 0.75f) _seated = Seat(); }
            else if (jumped) Seat();
        }

        public void StartKinematicsRecording(string sessionId, string sessionDir, double rateHz = 72.0)
        {
            _kinRecorder = new KinematicsRecorder(sessionId, _clock, sessionDir, rateHz, _useDemo ? "synthetic" : "xr_hands") { BackgroundWrites = true };
            _kinSampler = new KinematicsSampler(_kinRecorder, _hands);
            _kinSampler.OnTrackingChanged += (side, tracked) => RaiseEvent(new TrialEvent
            {
                Block = 0,
                Trial = _module != null ? _module.CurrentConditionIndex : null,
                Type = tracked ? "tracking_regained" : "tracking_lost",
                Hand = side == HandSide.Left ? "left" : "right",
            });
            _kinLastMs = -1;
            _rateMeter = new TrackingRateMeter(_module != null ? _module.Params.Arm : HandSide.Right);
            _sensRecorder = new SensorRecorder(sessionId, _clock, sessionDir, "udp") { BackgroundWrites = true };
            if (_cues != null) _sensRecorder.MotorExclusion = _cues.InMotorWindow;
        }

        public int StopKinematicsRecording()
        {
            DrainCueAcks();
            if (_cues != null) _cues.FlushAll();
            int n = 0;
            if (_kinRecorder != null)
            {
                _kinRecorder.Flush();
                n = _kinRecorder.ChunksWritten;
                _kinRecorder = null; _kinSampler = null;
            }
            if (_sensRecorder != null)
            {
                _sensRecorder.Flush();
                SensorChunksWritten = _sensRecorder.ChunksWritten;
                _sensRecorder = null;
            }
            _kinChunks = n;
            return n;
        }

        public void PrepareNextSession(string why)
        {
            StopSleeveSafely("next_person");
            ProgramHandoff.PendingAutoStartReason = why ?? "next person";
            if (_pendingOrder != null && ProgramHandoff.PendingProgram != null)
            {
                var blocks = ProgramHandoff.PendingProgram.SelectToken("program.blocks") as JArray;
                if (blocks != null && blocks.Count > 0)
                {
                    var b = (JObject)blocks[0];
                    var prm = b["params"] as JObject ?? new JObject();
                    prm["condition_order"] = _pendingOrder;
                    b["params"] = prm;
                }
            }
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().path
                : SceneManager.GetActiveScene().name);
        }

        // ---- hooks --------------------------------------------------------------------------------------------------

        public bool TryHandleCommand(string command, JObject parameters, out string error)
        {
            error = null;
            var r = PhantomCommandRouter.Handle(command, parameters, _module, _sessionUsed);
            if (!r.Handled) return false;
            error = r.Error;
            if (r.PendingOrder != null) _pendingOrder = r.PendingOrder;
            if (r.StopMotors && _haptic != null) { try { _haptic.Stop(); } catch (Exception e) { Debug.LogWarning("[PhantomHand] abort stop: " + e.Message); } }
            if (_module != null && _sessionUsed)
            {
                RaiseEvent(new TrialEvent
                {
                    Block = 0,
                    Trial = _module.CurrentConditionIndex,
                    Type = "command_received",
                    Data = new JObject { ["command"] = command, ["phase"] = r.LogPhase, ["ok"] = r.Error == null },
                });
            }
            return true;
        }

        public void FillStatus(JObject status, double nowMs)
        {
            var m = _module;
            string phase = m != null ? PhNames.Of(m.CurrentPhase) : "idle";
            string cond = m != null && m.CurrentCondition.HasValue ? PhNames.Of(m.CurrentCondition.Value) : null;
            double? rem = m != null ? PhantomLiveStatus.RemainingFor(m.CurrentPhase, m.RemainingS(nowMs)) : (double?)null;
            bool hap = _nodeA != null && _asking ? _nodeA.Connected : (_transportA != null && _transportA.HasDevice);
            bool bio = _nodeB != null && _asking ? _nodeB.Connected : (_transportB != null && _transportB.HasDevice);
            double? emg = _nodeB != null && bio && _nodeB.HasEmgCalibration ? _nodeB.EmgLevel01 : (double?)null;
            status["game_state"] = PhantomLiveStatus.GameState(phase, cond, rem, hap, bio, emg);
            if (_nodeA != null || _nodeB != null)
            {
                var tr = _trace.Build(_nodeB != null ? _nodeB.Emg : null, _nodeA != null ? _nodeA.Imu : null, nowMs);
                if (tr != null) status["trace"] = tr;
            }
            status["elapsed_s"] = Math.Round(nowMs / 1000.0, 1);
            if (m != null && m.CurrentConditionIndex.HasValue) status["trial"] = m.CurrentConditionIndex.Value;
            status["trials_total"] = 2;
        }

        public void DescribeBlocks(SessionEnvelope envelope, bool ended, bool completed)
        {
            if (!ended)
            {
                envelope.Blocks.Add(new BlockInfo
                {
                    Index = 0, GameId = GameIdConst, GameVersion = GameVersion,
                    Params = _blockParams != null ? (object)_blockParams : new JObject(), StartedTMs = 0,
                });
                return;
            }
            BlockInfo b = envelope.Blocks.Count > 0 ? envelope.Blocks[0] : null;
            if (b == null)
            {
                b = new BlockInfo { Index = 0, GameId = GameIdConst, GameVersion = GameVersion, Params = _blockParams != null ? (object)_blockParams : new JObject(), StartedTMs = 0 };
                envelope.Blocks.Add(b);
            }
            b.EndedTMs = _clock.NowMs;
            b.Completed = completed;
        }

        // ---- stop-safety --------------------------------------------------------------------------------------------

        private void StopSleeveSafely(string why)
        {
            if (_haptic == null) return;
            try
            {
                _haptic.Stop();
                _haptic.SendDisplay("IDLE", _clock != null ? _clock.NowMs : 0);
            }
            catch (Exception e) { Debug.LogWarning("[PhantomHand] StopSleeveSafely(" + why + "): " + e.Message); }
        }

        private void OnDisable() { StopSleeveSafely("disable"); }
        private void OnApplicationQuit() { StopSleeveSafely("quit"); }
        private void OnApplicationPause(bool paused)
        {
            if (paused) { StopSleeveSafely("pause"); return; }
            // back on a head: it may be another head, or the same one on another chair
            if (FreeToSeat()) { _seated = false; _headSec = 0f; _headLocalKnown = false; }
        }

        private void OnDestroy()
        {
            if (DeviceDiagnostics.Status == _diag) DeviceDiagnostics.Status = null;
            StopSleeveSafely("destroy");
            if (_module != null) _module.OnTrialEvent -= HandleModuleEvent;
            // Disposing a client disposes its (shared) transport; Stop() on a stopped transport is a no-op, so the order is free.
            try { if (_nodeA != null) _nodeA.Dispose(); } catch (Exception) { }
            try { if (_nodeB != null) _nodeB.Dispose(); } catch (Exception) { }
            try { if (_haptic != null) { _haptic.OnCueRecorded -= OnHapticCue; _haptic.Dispose(); } } catch (Exception) { }
            try { if (_transportB != null) _transportB.Dispose(); } catch (Exception) { }
        }
    }
}
