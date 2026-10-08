using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Opus.Games.OrchardReach;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Shell
{
    /// <summary>
    /// The in-headset session lifecycle for real play (Quest standalone, or Quest Link in the editor):
    ///
    ///   1. Finds the clinician app's hub on the LAN (UDP beacon :8788, contracts/LIVE_PROTOCOL.md) and stays
    ///      connected (auto-reconnect). A manual host can be set for networks that block broadcast.
    ///   2. Starts a session when the clinician presses "Start session" in the app, or when the patient pinches
    ///      BOTH hands for ~1.5 s (so the game is fully testable with no phone at all).
    ///   3. Records the session exactly as analytics expects it -- session.json + events.ndjson + kin_###.json
    ///      (+ sens_###.json for the wearable nodes), all on one clock -- under
    ///      Application.persistentDataPath/opus_sessions/&lt;id&gt;/.
    ///   4. Streams it live while it runs: status (2 Hz), every trial event, and metrics_tick so the app's live monitor
    ///      moves in real time.
    ///   5. At the end uploads every file to the hub over HTTP (retried on reconnect: a session recorded while
    ///      the hub was away is sent the next time it is found), then waits for the next start.
    ///
    /// PH U5: the runner talks to the game through <see cref="ISessionHost"/> (implemented by
    /// <see cref="OrchardReachSceneController"/> and <see cref="PhantomHandSceneController"/>). The Orchard-only parts (the
    /// world-space <see cref="OpusHud"/>, per-trial live metrics, peak speed, trial-count defaults, form/haptic-cue events)
    /// run only when the host is the Orchard controller, exactly as before.
    ///
    /// Disabled automatically when the scene runs on the recorded demo driver (batch PlayMode tests, the editor
    /// with no headset) unless <see cref="runWithDemoDriver"/> is set -- the tests own their own hub clients.
    /// </summary>
    [DefaultExecutionOrder(100)] // after OrchardReachSceneController.Awake
    public sealed class OpusSessionRunner : MonoBehaviour
    {
        [SerializeField] private OrchardReachSceneController controller;

        [Header("Hub (clinician app)")]
        [Tooltip("Leave empty to find the app automatically (UDP beacon). Set to the phone/PC IP only if discovery is blocked.")]
        [SerializeField] private string manualHubHost = "";
        [Tooltip("Editor only: if nothing is found by broadcast, try a hub on this same PC (hub_cli.dart / Windows app).")]
        [SerializeField] private bool editorTryLocalhost = true;

        [Header("Session defaults (overridden by the program the clinician sends)")]
        [SerializeField] private int defaultTrialCount = 6;
        [Tooltip("Sessions land on this patient in the app unless the clinician sends a program for someone else. " +
                 "The default is the app's 6-week demo patient, so a live session appears on top of real history.")]
        [SerializeField] private string defaultPatientRef = "synthetic-longitudinal-9000";
        [Tooltip("Drive the haptic sleeve (ESP32 or fake_haptic.py) unless the program says hapticsEnabled=false.")]
        [SerializeField] private bool hapticsOnByDefault = true;

        [Header("Starting without the app")]
        [SerializeField] private float bothHandsPinchHoldSec = 1.5f;
        [Tooltip("Demo driver only (no headset): start a session automatically after this many seconds.")]
        [SerializeField] private float demoAutoStartSec = 2f;
        [Tooltip("Run this runner even on the demo driver (editor verification without a headset).")]
        [SerializeField] private bool runWithDemoDriver = false;

        public enum Phase { WaitingToStart, Running, Paused, Finishing, Finished }
        public Phase CurrentPhase { get; private set; } = Phase.WaitingToStart;
        public LiveClient Client => _client;
        public string SessionId => _sessionId;
        public string LastSessionDir { get; private set; }
        public int UploadedFiles { get; private set; }
        public string LastUploadMessage { get; private set; } = "";
        /// <summary>The game this runner drives (Orchard controller or Phantom Hand controller).</summary>
        public ISessionHost Host => _host;

        /// <summary>Run the hub link / session lifecycle even on the demo (no-headset) hand driver. Set before the first frame
        /// (Phantom Hand's controller and the PlayMode tests do); Start() reads it.</summary>
        public bool RunWithDemoDriver { get => runWithDemoDriver; set => runWithDemoDriver = value; }

        private ISessionHost _host;
        private OrchardReachSceneController _orchard; // non-null only for Orchard: guards the Orchard-only code paths

        private LiveClient _client;
        private LiveClient.ConnectionState _linkState = LiveClient.ConnectionState.Disconnected;
        private string _deviceId;
        private string _sessionsRoot;

        private JObject _programParams = new JObject();
        private string _patientRef;
        private string _programRef = "headset-default";

        private string _sessionId;
        private SessionWriter _writer;
        private TrialRecorder _recorder;
        private double _statusAccumMs;
        private float _bothPinchSec;
        private float _waitingSec;
        private int _statusCount;

        // live per-trial metrics (computed on the headset from the same events it records) -- Orchard only
        private double _tShown = -1, _tOnset = -1, _tContact = -1;
        private double _peakSpeed;
        private Vector3? _lastWrist;
        private double _lastWristMs;
        private readonly List<double> _rts = new List<double>();
        private readonly List<double> _mts = new List<double>();
        private readonly List<double> _peaks = new List<double>();
        private int _trialsEnded, _trialsSucceeded;
        public string LastOutcome { get; private set; }
        public int HapticCuesSent { get; private set; }

        private readonly Queue<string> _uploadQueue = new Queue<string>();
        private bool _uploading;
        private const float UploadRetrySec = 10f;
        private float _uploadRetrySec;
        private OpusHud _hud;
        // Run17 (task 2, HUD bug fix): whether this runner drives the hub/session lifecycle itself. False for a
        // demo-driver scene with runWithDemoDriver left at its default (batch PlayMode tests, which own their own
        // hub clients and would double-drive sessions if this runner also started them) -- but the HUD is always
        // created regardless, so a demo-mode run in the editor (no headset, no batch harness) is never blank.
        private bool _driveSession;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<OrchardReachSceneController>();
            if (controller == null) controller = FindAnyObjectByType<OrchardReachSceneController>();
            _orchard = controller;
            if (controller != null) _host = controller;
            else
            {
                // Phantom Hand: the controller is a sibling component or lives elsewhere in the scene.
                var ph = GetComponent<PhantomHandSceneController>();
                if (ph == null) ph = FindAnyObjectByType<PhantomHandSceneController>();
                if (ph != null) _host = ph;
            }
        }

        private void Start()
        {
            if (_host == null)
            {
                Debug.LogWarning("[OPUS] OpusSessionRunner: no game scene controller (Orchard or Phantom Hand) in the scene; disabled.");
                enabled = false;
                return;
            }
            // Run17 (task 2, HUD bug fix): the HUD is created unconditionally -- it is just a status panel, not
            // part of the session-driving logic, so a demo-mode run in the editor (no headset, no batch harness)
            // always shows it. Only the hub connection / session lifecycle below is gated: a demo-driver scene
            // with runWithDemoDriver left false is a batch-test harness that drives its own sessions and hub
            // clients, and would double-drive a session if this runner also started one.
            // (Phantom Hand has its own in-headset HUD, PhantomHandUiPresenter; this world-space panel is Orchard's.)
            if (_orchard != null) _hud = OpusHud.Create(_orchard);
            _driveSession = _host.UsingRealHands || runWithDemoDriver;
            if (!_driveSession) return; // HUD stays up; nothing else to wire for a batch-test harness scene

            _patientRef = defaultPatientRef;
            _deviceId = (Application.isEditor ? "editor-" : "quest-") +
                        SystemInfo.deviceUniqueIdentifier.Replace("-", "").Substring(0, 8).ToLowerInvariant();
            _sessionsRoot = Path.Combine(Application.persistentDataPath, "opus_sessions");
            Directory.CreateDirectory(_sessionsRoot);

            // The game may carry a hub host (Phantom Hand: env / settings asset); Orchard returns null here, so for it
            // this is PlayerPrefs -> the serialized field, exactly as before.
            string host = _host.HubHostHint;
            if (string.IsNullOrWhiteSpace(host)) host = PlayerPrefs.GetString("opus_hub_host", manualHubHost);
            if (string.IsNullOrWhiteSpace(host) && Application.isEditor && editorTryLocalhost && IsPortOpen("127.0.0.1", LiveClient.HubPort))
                host = "127.0.0.1"; // hub running on this PC: skip broadcast, which some PCs don't loop back
            StartClient(string.IsNullOrWhiteSpace(host) ? null : host.Trim());

            QueuePendingUploads();

            _host.OnTrialEvent += OnTrialEvent;
            _host.OnMetricsTick += OnHostMetricsTick;
            if (_orchard != null)
            {
                _orchard.OnFormSignal += OnFormSignal;
                _orchard.OnHapticCueRecorded += OnHapticCue;
            }

            // A program the Bootstrap scene received (or the previous participant's, across a next_person reload).
            var pending = ProgramHandoff.TakeProgram();
            if (pending != null) ApplyProgram(pending, out _);

            Debug.Log($"[OPUS] OpusSessionRunner: game={_host.GameId} device={_deviceId} hub={(host ?? "auto-discover")} sessions={_sessionsRoot}");
        }

        private void StartClient(string host)
        {
            _client?.Dispose();
            _client = new LiveClient(
                deviceId: _deviceId,
                versionsProvider: () => new JObject { ["shell"] = "0.2.0", ["sdk"] = "0.1.0", ["games"] = new JObject { [_host.GameId] = _host.GameVersion } },
                gamesProvider: () => new JArray(new JObject { ["id"] = _host.GameId, ["version"] = _host.GameVersion }),
                manualHost: host);
            _client.OnCommand += OnCommand;
            _client.OnStateChanged += s =>
            {
                _linkState = s;
                Debug.Log($"[OPUS] live link: {s}");
                if (s == LiveClient.ConnectionState.Connected) PumpUploads();
            };
            _client.Start();
        }

        private void OnDestroy()
        {
            // Before the handlers go: the closing events of a run that is cut off here (block_end, the last cue records) are written by them.
            if (CurrentPhase == Phase.Running || CurrentPhase == Phase.Paused) FinishSession("stopped_by_patient", upload: false);
            if (_host != null)
            {
                _host.OnTrialEvent -= OnTrialEvent;
                _host.OnMetricsTick -= OnHostMetricsTick;
            }
            if (_orchard != null)
            {
                _orchard.OnFormSignal -= OnFormSignal;
                _orchard.OnHapticCueRecorded -= OnHapticCue;
            }
            _client?.Dispose();
        }

        private void OnApplicationQuit() => OnDestroy();

        private bool _slept, _pausedByHeadset;

        /// <summary>
        /// The headset was taken off (true) or put on again (false). Phantom Hand only.
        /// A run does not go on without its participant: the phases run on wall time, and on the first headset night (8 Oct 2026) a
        /// run that sat 7 minutes off the head ended as "completed" within a second of waking, every remaining phase expired at once.
        /// So the run is paused the way the operator's Pause does it, and resumed on waking unless the operator paused it.
        /// The link to the hub is rebuilt on waking: a hub that missed our pongs while we slept has written this headset off, although
        /// the socket still carries our messages (the operator's card showed "offline" for a headset that was running).
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (!_driveSession || _orchard != null) return;
            if (paused)
            {
                _slept = true;
                if (CurrentPhase == Phase.Running) { _host.PauseSession(); CurrentPhase = Phase.Paused; _pausedByHeadset = true; }
                return;
            }
            if (!_slept) return;   // Unity also reports "not paused" once at start
            _slept = false;
            _client?.Reconnect();
            if (_pausedByHeadset && CurrentPhase == Phase.Paused) { _host.ResumeSession(); CurrentPhase = Phase.Running; }
            _pausedByHeadset = false;
            Debug.Log("[OPUS] headset back on: link to the hub rebuilt" + (CurrentPhase == Phase.Running ? ", run resumed" : ""));
        }

        // ---- per frame ---------------------------------------------------------------------------------

        private void Update()
        {
            // Run17 (task 2, HUD bug fix): the HUD render at the bottom always runs. Everything above it --
            // hub pump, phase auto-advance, status upload -- only runs when this runner owns the session
            // lifecycle (see _driveSession in Start()); a batch-test harness scene drives all of that itself.
            if (_driveSession)
            {
                double now = Time.realtimeSinceStartupAsDouble * 1000.0;
                _client?.Pump(now);

                switch (CurrentPhase)
                {
                    case Phase.WaitingToStart:
                    case Phase.Finished:
                        _waitingSec += Time.deltaTime;
                        if (_orchard != null && !_host.UsingRealHands && _waitingSec >= demoAutoStartSec && CurrentPhase == Phase.WaitingToStart)
                            StartSession("demo auto-start");
                        // Real hands: the first session starts by itself once the headset has tracked the head for a
                        // couple of seconds (after the auto-recenter), so putting the headset on is all it takes.
                        // Later sessions: clinician's Start, or pinch both hands.
                        else if (_orchard != null && _host.UsingRealHands && CurrentPhase == Phase.WaitingToStart && _host.HeadTrackedSeconds >= 2.5f)
                            StartSession("auto-start on headset");
                        // Phantom Hand: only when the scene asks (a next_person reload, or a test); otherwise the clinician's
                        // Start or the both-hands pinch below.
                        else if (_orchard == null && CurrentPhase == Phase.WaitingToStart && _host.AutoStartRequested)
                            StartSession(_host.AutoStartReason ?? "auto-start");
                        else if (BothHandsPinching()) { _bothPinchSec += Time.deltaTime; if (_bothPinchSec >= bothHandsPinchHoldSec) StartSession("both-hands pinch"); }
                        else _bothPinchSec = 0f;
                        break;

                    case Phase.Running:
                        if (_orchard != null) TrackPeakSpeed();
                        if (_host.IsSessionComplete)
                            FinishSession("completed", upload: true);
                        break;
                }

                // Uploads start when the link comes up and when a run ends; a file that failed in between (the headset was taken off
                // two seconds after Stop, first headset night) would otherwise wait for the next of those.
                _uploadRetrySec += Time.unscaledDeltaTime;
                if (_uploadRetrySec >= UploadRetrySec)
                {
                    _uploadRetrySec = 0f;
                    if (_uploadQueue.Count > 0 && !_uploading && _client != null && _client.IsConnected) PumpUploads();
                }

                _statusAccumMs += Time.deltaTime * 1000.0;
                if (_client != null && _client.IsConnected && _statusAccumMs >= 500)
                {
                    _statusAccumMs = 0;
                    _client.SendStatus(BuildStatus(), now);
                    _statusCount++;
                }
            }

            _hud?.Render(this, _orchard, _linkState, _bothPinchSec / Mathf.Max(0.01f, bothHandsPinchHoldSec));
        }

        public float PinchHoldProgress => _bothPinchSec / Mathf.Max(0.01f, bothHandsPinchHoldSec);

        private bool BothHandsPinching()
        {
            var h = _host.Hands;
            if (h == null || !_host.UsingRealHands) return false;
            return h.IsTracked(HandSide.Left) && h.IsTracked(HandSide.Right)
                && h.GetPinchStrength(HandSide.Left) >= 0.85f && h.GetPinchStrength(HandSide.Right) >= 0.85f;
        }

        private void TrackPeakSpeed()
        {
            var m = _orchard.Module;
            if (m == null || m.CurrentTrialState != TrialState.MovementOnset) { _lastWrist = null; return; }
            var side = m.CurrentSide == "left" ? HandSide.Left : HandSide.Right;
            string j = side == HandSide.Left ? OpusJoints.LWrist : OpusJoints.RWrist;
            if (_orchard.Hands == null || !_orchard.Hands.TryGetJointPose(j, out var p, out _) || p == null) return;
            var pos = new Vector3((float)p[0], (float)p[1], (float)p[2]);
            double t = _orchard.Clock.NowMs;
            if (_lastWrist.HasValue && t - _lastWristMs > 5)
            {
                double v = Vector3.Distance(pos, _lastWrist.Value) / ((t - _lastWristMs) / 1000.0);
                if (v < 5.0) _peakSpeed = Math.Max(_peakSpeed, v); // >5 m/s is a tracking jump, not a reach
            }
            _lastWrist = pos;
            _lastWristMs = t;
        }

        // ---- session lifecycle -------------------------------------------------------------------------

        public void StartSession(string why)
        {
            if (CurrentPhase == Phase.Running || CurrentPhase == Phase.Paused) return;
            if (_host.NeedsFreshScene)
            {
                // Phantom Hand: its presenters are bound once per scene, so the next participant gets a fresh scene
                // (reload ~2 s) that starts by itself, with the same patient and prescription.
                SwitchToNextPerson(why);
                return;
            }
            _bothPinchSec = 0f;
            _waitingSec = 0f;

            if (_host.UsingRealHands) _host.Recenter();

            var overrides = new JObject(_programParams);
            if (_orchard != null)
            {
                if (overrides["trialCount"] == null) overrides["trialCount"] = defaultTrialCount;
                // The sleeve is additive and harmless when absent (cues are simply undelivered), so live sessions
                // drive it unless the clinician's program explicitly turns it off.
                if (overrides["hapticsEnabled"] == null) overrides["hapticsEnabled"] = hapticsOnByDefault;
            }
            _preStartEvents.Clear();
            _starting = true;
            _host.StartNewSession(overrides); // new clock: must precede the recorder, which stamps from it
            _starting = false;
            // StartNewSession already began the module and emitted block_start/trial_start/target_shown before our
            // recorder existed -- those are replayed below from the module's first trial so nothing is lost.

            _sessionId = Guid.NewGuid().ToString();
            var env = new SessionEnvelope
            {
                SessionId = _sessionId,
                ContractsVersion = _host.ContractsVersion,
                PatientRef = _patientRef,
                ProgramRef = _programRef,
                Mode = _host.UsingRealHands ? "clinic" : "simulation",
                StartedAt = DateTimeOffset.UtcNow.ToString("o"),
                EndedAt = null,
                EndReason = null,
                Device = new DeviceInfo
                {
                    Model = Application.isEditor ? "quest-link-editor" : SystemInfo.deviceModel,
                    DeviceId = _deviceId,
                    Os = SystemInfo.operatingSystem,
                    TrackingRateHz = 72.0,
                },
                Versions = new VersionsInfo { Shell = "0.2.0", Sdk = "0.1.0", Games = new Dictionary<string, string> { [_host.GameId] = _host.GameVersion } },
                Calibration = new CalibrationInfo
                {
                    AffectedSide = _host.AffectedSide ?? "right",
                    DominantSide = "right",
                    ArmLengthM = new ArmLength { Left = 0.55, Right = 0.55 },
                    Posture = "seated",
                },
                Blocks = new List<BlockInfo>(),
            };
            _host.DescribeBlocks(env, ended: false, completed: false);
            _writer = new SessionWriter(_sessionsRoot, env);
            _writer.Save(); // session.json exists from the first second (crash-safe)
            LastSessionDir = _writer.SessionDir;
            _recorder = new TrialRecorder(_host.Clock, _writer.EventsPath);
            _recorder.Record(0, null, "session_start", data: new JObject { ["started_by"] = why });
            _host.StartKinematicsRecording(_sessionId, _writer.SessionDir, 72.0);

            ResetLiveMetrics();
            _client?.NotifySessionStarted(_sessionId);
            CurrentPhase = Phase.Running;
            LastOutcome = null;

            // Replay the events StartNewSession emitted before the recorder existed (block_start, trial_start,
            // target_shown of trial 0) with their original timestamps' order preserved.
            foreach (var e in _preStartEvents) Forward(e);
            _preStartEvents.Clear();

            Debug.Log($"[OPUS] session {_sessionId} started ({why}); patient={_patientRef}; dir={_writer.SessionDir}");
        }

        private readonly List<TrialEvent> _preStartEvents = new List<TrialEvent>();
        private bool _starting;

        public void FinishSession(string endReason, bool upload)
        {
            if (CurrentPhase != Phase.Running && CurrentPhase != Phase.Paused) return;
            CurrentPhase = Phase.Finishing;
            bool completed = _host.IsSessionComplete;
            if (_host.IsModuleRunning) _host.EndSession();
            else if (_host.UsingRealHands) _host.EndSession();

            _recorder?.Record(0, null, "session_end", data: new JObject { ["end_reason"] = endReason });
            int chunks = _host.StopKinematicsRecording();
            _recorder?.Close();
            _recorder = null;
            _writer.Envelope.EndedAt = DateTimeOffset.UtcNow.ToString("o");
            _writer.Envelope.EndReason = endReason;
            _writer.Envelope.Chunks = chunks;
            var measured = _host.MeasuredTrackingRateHz; // measured, not trusted (docs/UNITY_PRACTICES.md section 2); Orchard: null -> nominal 72
            if (measured.HasValue && measured.Value > 0) _writer.Envelope.Device.TrackingRateHz = Math.Round(measured.Value, 1);
            _host.DescribeBlocks(_writer.Envelope, ended: true, completed: completed);
            _writer.Save();
            _client?.NotifySessionEnded(endReason);
            _client?.SendStatus(BuildStatus("finished"), Time.realtimeSinceStartupAsDouble * 1000.0);

            Debug.Log($"[OPUS] session {_sessionId} finished ({endReason}): trials={_trialsEnded} success={_trialsSucceeded} kinChunks={chunks} dir={_writer.SessionDir}");
            if (upload) { _uploadQueue.Enqueue(_writer.SessionDir); PumpUploads(); }
            CurrentPhase = Phase.Finished;
            _waitingSec = 0f;
        }

        /// <summary>Phantom Hand next_person / a start after a finished run: end the run if needed, then reload the scene with the
        /// same prescription so the new participant starts clean (calibration first).</summary>
        private void SwitchToNextPerson(string why)
        {
            if (CurrentPhase == Phase.Running || CurrentPhase == Phase.Paused) FinishSession("next_person", upload: true);
            ProgramHandoff.PendingProgram = BuildProgramPayload();
            Debug.Log($"[OPUS] next person ({why}): reloading the scene");
            _host.PrepareNextSession(why);
        }

        private JObject BuildProgramPayload()
        {
            return new JObject
            {
                ["patient_ref"] = _patientRef,
                ["program"] = new JObject
                {
                    ["program_id"] = _programRef,
                    ["blocks"] = new JArray(new JObject { ["game_id"] = _host.GameId, ["params"] = _programParams.DeepClone() }),
                },
            };
        }

        // ---- events ------------------------------------------------------------------------------------

        private void OnTrialEvent(TrialEvent e)
        {
            if (_recorder == null)
            {
                // StartNewSession fires the first trial's events synchronously, before StartSession has built
                // the recorder -- keep them and replay once it exists.
                if (_starting) _preStartEvents.Add(e);
                return; // e.g. block_end after a finished session: nothing is recording
            }
            Forward(e);
        }

        private void Forward(TrialEvent e)
        {
            // Same clock as the module (the host's Clock), stamped at record time: identical to e.TMs for live
            // events, a few ms later for the replayed start events.
            var rec = _recorder.Record(e.Block, e.Trial, e.Type, e.Hand, e.Target, e.Outcome, e.Data);
            _client?.SendTrialEvent(rec);
            if (_orchard != null) UpdateLiveMetrics(rec);
        }

        private void OnHostMetricsTick(int windowTrials, JObject metrics)
        {
            if (_recorder == null) return;
            _client?.SendMetricsTick(windowTrials, metrics);
        }

        private void OnFormSignal(FormWarningSignal sig)
        {
            if (_recorder == null) return;
            var rec = _recorder.Record(0, _orchard.Module?.TrialIndex, "form_warning",
                data: new JObject { ["kind"] = sig.Kind, ["active"] = sig.Active, ["leanCm"] = Math.Round(sig.LeanCm, 1) });
            _client?.SendTrialEvent(rec);
        }

        private void OnHapticCue(HapticCueRecord r)
        {
            HapticCuesSent++;
            if (_recorder == null) return;
            var rec = _recorder.Record(0, _orchard.Module?.TrialIndex, "haptic_cue", data: new JObject
            {
                ["cue"] = r.Cue, ["intensity"] = r.IntensityFrac, ["delivered"] = r.Delivered, ["cue_id"] = r.CueId,
            });
            _client?.SendTrialEvent(rec);
        }

        private void ResetLiveMetrics()
        {
            _tShown = _tOnset = _tContact = -1; _peakSpeed = 0; _lastWrist = null;
            _rts.Clear(); _mts.Clear(); _peaks.Clear();
            _trialsEnded = _trialsSucceeded = 0;
            HapticCuesSent = 0;
        }

        private void UpdateLiveMetrics(TrialEvent e)
        {
            switch (e.Type)
            {
                case "target_shown": _tShown = e.TMs; _tOnset = _tContact = -1; _peakSpeed = 0; _lastWrist = null; break;
                case "movement_onset": _tOnset = e.TMs; break;
                case "contact": _tContact = e.TMs; break;
                case "trial_end":
                    _trialsEnded++;
                    LastOutcome = e.Outcome;
                    if (e.Outcome == "success") _trialsSucceeded++;
                    if (_tShown >= 0 && _tOnset >= 0) _rts.Add(_tOnset - _tShown);
                    if (_tOnset >= 0 && _tContact >= 0) _mts.Add(_tContact - _tOnset);
                    if (_peakSpeed > 0) _peaks.Add(_peakSpeed);
                    var metrics = new JObject { ["success_rate"] = Math.Round(100.0 * _trialsSucceeded / _trialsEnded, 1) };
                    if (_rts.Count > 0) metrics["reaction_time_ms"] = Math.Round(_rts.Average(), 1);
                    if (_mts.Count > 0) metrics["movement_time_ms"] = Math.Round(_mts.Average(), 1);
                    if (_peaks.Count > 0) metrics["peak_speed_mps"] = Math.Round(_peaks.Average(), 2);
                    // Run15 (task 2/3): clinician-only quality-gated dose counter, same field on every tick.
                    metrics["good_dose"] = _orchard.Module?.GoodDoseCount ?? 0;
                    _client?.SendMetricsTick(_trialsEnded, metrics);
                    break;
            }
        }

        public double? MeanReactionMs => _rts.Count > 0 ? _rts.Average() : (double?)null;
        public int TrialsSucceeded => _trialsSucceeded;
        public int TrialsEnded => _trialsEnded;

        private JObject BuildStatus(string stateOverride = null)
        {
            string state = stateOverride ?? CurrentPhase switch
            {
                Phase.Running => "running",
                Phase.Paused => "paused",
                Phase.Finished => "finished",
                _ => "ready",
            };
            var h = _host.Hands;
            string Hand(HandSide s) => h == null ? "none" : !h.IsTracked(s) ? "lost" : h.GetConfidence(s) == TrackingConfidence.High ? "high" : "low";

            if (_orchard == null)
            {
                // Phantom Hand (and any later game): the generic fields plus the game's own (game_id, game_state, trace).
                var gs = new JObject
                {
                    ["state"] = state,
                    ["session_id"] = _sessionId,
                    ["patient_ref"] = _patientRef,
                    ["game_id"] = _host.GameId,
                    ["fps"] = Math.Round(1.0 / Math.Max(1e-3, Time.smoothDeltaTime)),
                    ["tracking_rate_hz"] = Math.Round(_host.MeasuredTrackingRateHz ?? 72.0),
                    ["hands"] = new JObject { ["left"] = Hand(HandSide.Left), ["right"] = Hand(HandSide.Right) },
                    ["real_hands"] = _host.UsingRealHands,
                };
                _host.FillStatus(gs, _host.Clock.NowMs);
                return gs;
            }

            var m = _orchard.Module;
            var st = new JObject
            {
                ["state"] = state,
                ["session_id"] = _sessionId,
                ["patient_ref"] = _patientRef,
                ["trial"] = m?.TrialIndex ?? 0,
                ["trials_completed"] = m?.TrialsCompleted ?? 0,
                ["trials_total"] = m?.TrialCount ?? defaultTrialCount,
                ["fps"] = Math.Round(1.0 / Math.Max(1e-3, Time.smoothDeltaTime)),
                ["tracking_rate_hz"] = 72,
                ["hands"] = new JObject { ["left"] = Hand(HandSide.Left), ["right"] = Hand(HandSide.Right) },
                ["real_hands"] = _orchard.UsingRealHands,
                // Run15 (task 2/3): stage/rule for the clinician's live monitor (never shown on the patient
                // HUD -- see OpusHud), good_dose is the quality-gated counter, clinician-only by design.
                ["stage"] = m?.Params?.Stage,
                ["rule"] = m?.Params?.SortRule,
                ["good_dose"] = m?.GoodDoseCount ?? 0,
            };
            var hc = _orchard.Haptic;
            if (hc != null && hc.Enabled)
            {
                st["haptic"] = new JObject
                {
                    ["connected"] = hc.Connected,
                    ["device_id"] = hc.DeviceId,
                    ["battery_pct"] = hc.BatteryPct,
                    ["motors_ok"] = hc.MotorsOk,
                    ["cues_sent"] = HapticCuesSent,
                };
            }
            return st;
        }

        // ---- commands from the app ---------------------------------------------------------------------

        private void OnCommand(string command, JObject p, string ackId)
        {
            Debug.Log($"[OPUS] hub command '{command}'");
            bool ok = true; string err = null;
            switch (command)
            {
                case "assign_program":
                    ok = ApplyProgram(p, out err);
                    break;
                case "start":
                    if (CurrentPhase == Phase.Running || CurrentPhase == Phase.Paused) { ok = true; break; } // idempotent
                    StartSession("clinician pressed Start");
                    break;
                case "pause":
                    if (CurrentPhase == Phase.Running) { _host.PauseSession(); CurrentPhase = Phase.Paused; }
                    break;
                case "resume":
                    if (CurrentPhase == Phase.Paused) { _host.ResumeSession(); CurrentPhase = Phase.Running; }
                    break;
                case "stop":
                    FinishSession("stopped_by_clinician", upload: true);
                    break;
                case "recenter":
                    ok = _host.Recenter();
                    if (!ok) err = _orchard != null ? "no head pose to recenter on" : "not now: a run has taken its calibration (recenter before a run or while calibrating)";
                    break;
                case "show_message":
                    _hud?.Flash(p?["text"]?.Value<string>() ?? "");
                    break;
                case "next_person" when _orchard == null:
                    // End the current run if needed, reset the scene and counters, open a new session for the next participant.
                    SwitchToNextPerson("operator next_person");
                    break;
                default:
                    // Game-specific operator commands (Phantom Hand: phase_next, set_condition_order, abort_phase).
                    if (_host.TryHandleCommand(command, p, out var hostError)) { ok = hostError == null; err = hostError; }
                    else { ok = false; err = $"unsupported command '{command}'"; }
                    break;
            }
            _client?.AckCommand(ackId, ok, err);
        }

        /// <summary>assign_program payload: {program:{program_id, blocks:[{game_id, params}]}, patient_ref}.</summary>
        private bool ApplyProgram(JObject payload, out string error)
        {
            error = null;
            if (payload == null) { error = "empty assign_program"; return false; }
            var patient = payload["patient_ref"]?.Value<string>();
            if (!string.IsNullOrEmpty(patient)) _patientRef = patient;
            var program = payload["program"] as JObject;
            _programRef = program?["program_id"]?.Value<string>() ?? _programRef;
            string gameId = _host.GameId;
            var block = (program?["blocks"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(b => (b["game_id"]?.Value<string>() ?? gameId) == gameId);
            _programParams = (block?["params"] as JObject)?.DeepClone() as JObject ?? new JObject();
            _hud?.Flash($"Program received for {_patientRef}");
            Debug.Log($"[OPUS] program {_programRef} for {_patientRef}: {_programParams.ToString(Newtonsoft.Json.Formatting.None)}");
            return true;
        }

        // ---- uploads -----------------------------------------------------------------------------------

        private void QueuePendingUploads()
        {
            foreach (var dir in Directory.GetDirectories(_sessionsRoot))
            {
                if (File.Exists(Path.Combine(dir, ".uploaded"))) continue;
                if (!File.Exists(Path.Combine(dir, "session.json"))) continue;
                var env = SafeLoad(dir);
                if (env == null || string.IsNullOrEmpty(env.EndedAt)) continue; // unfinished (crashed) session: leave it
                _uploadQueue.Enqueue(dir);
            }
        }

        private static SessionEnvelope SafeLoad(string dir)
        {
            try { return SessionWriter.Load(dir); } catch { return null; }
        }

        private async void PumpUploads()
        {
            if (_uploading || _client == null) return;
            _uploading = true;
            try
            {
                while (_uploadQueue.Count > 0 && _client.IsConnected)
                {
                    string dir = _uploadQueue.Peek();
                    string id = Path.GetFileName(dir);
                    var names = new List<string> { "session.json", "events.ndjson" };
                    names.AddRange(Directory.GetFiles(dir, "kin_*.json").Select(Path.GetFileName).OrderBy(n => n));
                    names.AddRange(Directory.GetFiles(dir, "sens_*.json").Select(Path.GetFileName).OrderBy(n => n));
                    int ok = 0;
                    foreach (var n in names)
                    {
                        if (_client == null || !_client.IsConnected) break;   // the hub went away: do not wait out a timeout per file
                        var path = Path.Combine(dir, n);
                        if (!File.Exists(path)) continue;
                        if (await _client.UploadFileAsync(id, n, path)) ok++;
                    }
                    if (ok == names.Count(n => File.Exists(Path.Combine(dir, n))))
                    {
                        File.WriteAllText(Path.Combine(dir, ".uploaded"), DateTimeOffset.UtcNow.ToString("o"));
                        _uploadQueue.Dequeue();
                        UploadedFiles += ok;
                        LastUploadMessage = $"Sent {ok} files to the app";
                        Debug.Log($"[OPUS] uploaded session {id}: {ok} files");
                    }
                    else
                    {
                        LastUploadMessage = $"Upload incomplete ({ok}/{names.Count}); will retry";
                        Debug.LogWarning($"[OPUS] upload of {id} incomplete ({ok}/{names.Count}); retrying in {UploadRetrySec:F0} s");
                        _uploadQueue.Enqueue(_uploadQueue.Dequeue());   // to the back: one session that keeps failing must not hold up the others
                        break;
                    }
                }
            }
            finally { _uploading = false; }
        }

        private static bool IsPortOpen(string host, int port)
        {
            try
            {
                using var c = new System.Net.Sockets.TcpClient();
                return c.ConnectAsync(host, port).Wait(TimeSpan.FromMilliseconds(300)) && c.Connected;
            }
            catch { return false; }
        }
    }
}
