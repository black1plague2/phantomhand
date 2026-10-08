# Unity prompts (ph-builder, Sonnet) — the critical path

Every prompt starts with: "Read CLAUDE.md, docs/agent-briefs/ph/PRD_v2.md (§5, §9, §10.1, §11), 01-ORCHESTRATION.md
(§5 Unity lock), 02-RULES.md (§3 Unity), 03-SPEC.md (all, esp. §0 decisions), docs/UNITY_PRACTICES.md and the
root-cause section of docs/agent-briefs/U-next-run.md before touching anything." Each ends with the checkpoint
log (02-RULES.md §1.8). Target device: Quest 3. Take the Unity lock for every batch run.

MVP: U1–U6. Stretch (after G2): U7, U8.

---

## U1 — SDK: stroke cues, two-node transport, sensor client  · Oct 7 · branch ph/u1-sdk · timebox 3 h
Owns: game/Packages/com.opus.sdk/** only. Depends: O1.

```text
Read every file in game/Packages/com.opus.sdk/Runtime/Transport and Recording, and contracts/HAPTIC_PROTOCOL.md v1.2.
Do not change behaviour or tests of trunk_lean / low_confidence / success.
1) HapticCueMapper.Stroke(motor, maxIntensity, cueId) → (motor, round(150 × maxIntensity) capped 150, 200 ms,
   "pulse", cueId, "stroke"); the JSON also carries play_at_ms (session ms, informational).
2) HapticClient.ScheduleStroke(motor, playAtSessionMs, leadMs): queued; sent from Pump() at playAt − lead (the
   existing non-blocking _scheduled list, no Thread.Sleep). Own gate: ≥ 250 ms per motor, ≤ 4 sends per rolling
   second. Send time > 50 ms in the past → dropped, recorded delivered=false reason "late". OnCueRecorded for every
   stroke with Motor, ScheduledMs, SentMs, ack latency (extend HapticCueRecord additively). Strokes refresh the
   watchdog.
3) HapticClient.SendDisplay(text) (≤ 12 chars, ≤ 2/s) and a keepalive: while a session runs, send ping +
   subscribe once per second (firmware watchdog FR-FW-04 and subscriber list 03-SPEC D3).
4) Discovery: static DiscoveryHub owning ONE UDP 8791 listener (Android doesn't honour SO_REUSEADDR reliably),
   parsing the PRD §9.1 form and the legacy hello, fanning out to registered transports. UdpHapticTransport gets an
   optional filter (device_kind and/or device_id; missing kind = "haptic") and a manual host. Ports configurable
   (tests use offsets).
5) SleeveSensorClient wrapping one transport: sends subscribe/ping each second; parses Node A sensor_data v1
   (sensors.imu_accel_x/y/z, imu_gyro_*) and Node B sensor_chunk (emg_envelope[], sample_rate_hz, timestamp_ms =
   first sample) and emg_burst and status; 10 s thread-safe ring buffers; device ms → session ms via the median
   offset of the first 20 packets (re-estimated every 30 s); main-thread events from Pump(); Connected=false after
   3 s silence; EmgLevel01 with rest/MVC calibration per PRD FR-VR-07 (Normalized = (cur − rest)/(mvc − rest),
   clamped 0..1); AccelMagnitude latest; malformed packets counted and ignored.
6) SensorRecorder → sens_###.json (contracts/schemas/sensor-file.schema.json), 5 s chunks, same SessionClock.
7) No Meta assembly references in com.opus.sdk.
EditMode tests (FakeHapticTransport + controllable clock; ≥ 25 new): send = playAt − lead within one Pump; late
drop; 250 ms gate; 4/s cap; watchdog silent during a 90 s stroke train and fires 2 s after the last; keepalive
cadence; display truncation/rate; discovery filter both kinds and both dialects; DiscoveryHub fan-out to two
transports; parsing of both sensor forms + malformed; clock offset ±5 ms on jittered packets; EMG normalisation;
SensorRecorder output validates (run validate.py on it). Run all SDK + OrchardReach EditMode; paste totals.
```

---

## U2 — Game logic + scene  · Oct 8 morning · branch ph/u2-logic-scene · timebox 3.5 h
Owns: game/Assets/Games/PhantomHand/Runtime (logic), Tests, game/Assets/Shell/Editor/PhantomHandSceneBuilder.cs,
game/Assets/Scenes/PhantomHand.unity, EditorBuildSettings. (Logic code may be written while U1 runs; Unity runs wait for the lock.)

```text
PART A — pure C# (pattern: game/Assets/Games/OrchardReach/Runtime; no MonoBehaviour/OVR dependency).
PhantomHand.Runtime.asmdef (rootNamespace Opus.Games.PhantomHand; refs Opus.Sdk.Runtime; Newtonsoft precompiled)
+ PhantomHand.Tests.Editor.asmdef. Don't edit manifest.json.
- PhantomHandParams.From(ParamSet) (03-SPEC §6, defaults/clamps, demo_mode).
- PhaseStateMachine: Calibrate → ProbePre → Induction → [Agency if agency_enabled] → [Threat if enabled] →
  ProbePost → Questionnaire, for C1 then C2 (from condition_order), → Witness → Done. Timer phases end by Tick;
  Calibrate/Probe/Questionnaire end on completion calls or a 60 s timeout (confirmed=false). Advance(), Abort(),
  Pause/Resume freeze timers. PhaseChanged(phase, condition, trialIndex).
- StrokeScheduler (seeded, 03-SPEC D1): interval = 1000/stroke_rate_hz ± stroke_jitter_ms; brush speed =
  motor_spacing_cm / motor_soa_ms, constant over the whole forearm; per stroke: startMs (brush touches the wrist),
  passAMs (= start + motor_a_from_wrist_cm / speed), passBMs (= passA + motor_soa_ms), endMs (elbow); SYNC cue =
  pass; ASYNC cue = pass + async_delay_ms ± 100 and A/B swapped on 50 %; no overlap; last stroke ends before
  induction end.
- DriftProbe (still < 1 cm over 1.5 s; driftCm + = toward the virtual hand), ThreatResponseAnalyzer (2 s
  baseline; EMG latency = first sample > baseline + 3 SD; wrist speed > 0.15 m/s; IMU |a| peak over baseline;
  quality flags), Questionnaire (q1–q3, ownership = mean(q1,q2)), WitnessSummary (per-condition numbers + EN/HI
  lines incl. the PRD closing line).
- PhantomHandModule : IGameModule, [GameModule("phantom_hand")]: LoadManifest/Configure/Begin/Pause/Resume/End,
  CurrentPhase/Condition/Strokes, Submit* methods; emits every 03-SPEC §7 event (trial = condition index).
≥ 40 EditMode tests incl. the stroke timing math (SYNC cue == pass, B − A == motor_soa_ms, ASYNC 500–700 ms,
swap 40–60 % over 500 strokes, determinism), phase walks (timer, Advance, Abort, pause, both orders, agency off),
probe stillness with noise, analyzer latencies ±10 ms on synthetic curves, golden event list for a scripted run.

PART B — scene by editor script only (Tools/OPUS/Build PhantomHand Scene + static BuildScene()):
PhantomHand.unity with the Building Blocks rig like OrchardReach (Camera Rig floor 72 Hz, Hand Tracking,
Interaction rig with Poke; no controllers, no Main Camera, no passthrough), hand VISUAL renderers off (tracking on),
left-index dot toggle; calm room corner, 75 cm matte table, warm key + ambient, light fog, static batching;
DarkenController for probes; anchors TableTop, ArmRestOutline, VirtualArmAnchor, BrushRig, ThreatDropPoint,
ProbeRuler (1 m, cm ticks, 35 cm ahead at table height), QuestionnairePanel, WitnessPanel, HudPanel; audio mixer
(Sfx, Voice) with placeholders. Add to Build Settings after OrchardReach (OrchardReach.unity untouched).
EditMode PhantomHandRigValidatorTest (one camera, hand tracking only, frequency LOW, MetaHandSource on raw hands,
renderers off, poke both hands, anchors, URP). Screenshots (table, dark probe, outline) → read → fix → reshoot.
Run all EditMode; paste totals.
```

---

## U3 — Virtual arm, brush, strokes, threat  · Oct 8 afternoon · branch ph/u3-arm-threat · timebox 3.5 h
Owns: game/Assets/Games/PhantomHand/Runtime/Presentation/** (asmdef with ISDK refs), game/Assets/Art/PhantomHand/**.

```text
1) VirtualArmRig (03-SPEC D2): right hand (duplicate the ISDK hand visual so fingers can be posed) + forearm
   (tapered mesh, forearm_length_cm, neutral skin) in a virtual sleeve that matches the real one (dark fabric, two
   bands at motor A/B positions). At induction start: calibrated real wrist pose + forearm axis, offset_cm to the
   LEFT on the table plane, palm down, matching wrist yaw; FROZEN (relaxed static pose) from induction start through
   threat; follows the wrist (+ offset) only in calibration/agency or when follow_during_induction=true.
   WorldPos(fromWristCm) helper.
2) BrushRig: brush (procedural if no model) on a time-parameterised path (no physics): touch down at the wrist at
   stroke.startMs, pass A at passAMs and B at passBMs, lift at the elbow, return. Bristle bend on contact. Swish
   sound follows the visual only.
3) StrokeDriver: per stroke ScheduleStroke(0, cueA, tactile_lead_ms) and (1, cueB, lead); record stroke events
   with MEASURED pass times from the animation clock and the cue sends from OnCueRecorded; timing_err_ms =
   send + lead − measured pass (SYNC). SendDisplay("SYNC"/"ASYNC") at induction start, "IDLE" at the end.
4) ThreatDrop: 10–12 cm stone 40 cm above the virtual hand, 0.6 s telegraph (shadow + creak), Rigidbody fall
   (interpolate, continuous; never Transform.position after release) onto a compound collider on the virtual hand;
   first collision → threat_impact (physics time → SessionClock), thud + dust; stone rolls off and fades. No hit in
   2 s → threat_impact {ok:false}. Abort with a log line if the real hand moved under the drop point.
5) ThreatResponseCollector: 2 s before → 1.5 s after impact: raw wrist (each new DataVersion), Node A accel, Node B
   EMG envelope + emg_burst; ThreatResponseAnalyzer → threat_response (quality flags for missing streams; hand
   tracking alone is a valid fallback per PRD §11).
PlayMode tests (fake transport, synthetic calibrated pose): stroke count = plan; SYNC |timing_err| ≤ 20 ms; ASYNC
500–700 ms with ~50 % swaps; virtual pose hash constant during induction; offset = offset_cm ± 0.5 cm; 20 drops all
collide within 1 s; injected wrist jerk +180 ms / IMU +150 ms / EMG +120 ms → latencies ±15 ms; Node B absent →
EMG null + flag. Screenshots: contact A, contact B, telegraph, impact; read; fix clipping; reshoot.
```

---

## U4 — Calibration, probe, questionnaire, witness, HUD  · Oct 8 afternoon (after U3's lock) · branch ph/u4-ui · timebox 2.5 h

```text
uGUI world-space + ISDK PokeInteractable/PointableCanvas (existing rig), no OVRInput, OpusHud styling, EN + HI
string table.
1) Calibration: glowing forearm outline; "Rest your right forearm inside the outline, palm down"; green + confirm
   after 2 s within 3 cm.
2) Probe (FR-VR-04): DarkenController on, virtual arm hidden, ruler + dot on the left index tip; "Keep your right
   hand still. With your left index, point above the ruler to where you feel your right index is, then hold still.";
   ring fills over 1.5 s; tick on confirm; if the right hand moved > 3 cm since calibration → "Please put your arm
   back on the outline" and pause the probe.
3) Questionnaire (FR-VR-05): one item at a time, 7 poke buttons −3..+3 ≥ 4 cm, anchors "Strongly disagree" /
   "Strongly agree", 45 cm from the eyes slightly low, auto-advance 0.4 s, Back. Items: q1 "It felt as if the virtual
   hand was my hand." q2 "It felt as if the touch I felt was caused by the brush on the virtual hand." q3 (control)
   "It felt as if my real hand was turning virtual."
4) Witness (PRD §5 row 7): SYNC and ASYNC cards (drift change with arrow, flinch latency + strong/weak/none, ownership
   bar), "preliminary" label, closing line fades in after 3 s.
5) HUD: "Sleeve offline" / "EMG offline" chips (non-disruptive, PRD §11), phase + time left; spectator details
   hidden in the headset by default (both-hands pinch 3 s toggles).
Tests: text fits bounds EN + HI; button sizes; probe confirms still finger / refuses jittery; questionnaire order
incl. Back; witness shows a scripted run's numbers. Screenshot every panel; read; fix; reshoot.
```

---

## U5 — Composition root, session runner, live link, L3  · Oct 8 evening · branch ph/u5-composition · timebox 3 h

```text
Read OrchardReachSceneController.cs, OpusSessionRunner.cs, OpusHud.cs end to end.
1) PhantomHandSceneController (Assets/Shell/Runtime): SessionClock; IHandSource (MetaHandSource in headset,
   SyntheticHandDriver in tests); GameRegistry.Create("phantom_hand") + LoadManifest + Configure; TrialRecorder;
   KinematicsRecorder (both hands); SensorRecorder; haptic + bio transports → HapticClient + two SleeveSensorClients;
   U3/U4 presenters. Frame order: clients Pump → module Tick → presenters → recorders. Stop-safe on
   disable/pause/quit (Stop + display "IDLE"). Manual node IPs in a settings asset (03-SPEC D8).
2) OpusSessionRunner: depend on a small ISessionHost interface implemented by BOTH OrchardReachSceneController and
   PhantomHandSceneController; Orchard behaviour and tests unchanged (prove it). Live status 2 Hz with game_state +
   trace (EMG env + |accel| at 20 Hz, 03-SPEC D4); trial events forwarded; metrics_tick with drift/ownership/flinch
   when known; commands start/pause/resume/end/phase_next/set_condition_order (before first induction)/abort_phase,
   all acked. Game chosen by the program's game id via a minimal Bootstrap scene (document it).
3) Missing peers (PRD §11 fail-safe): no haptic → strokes delivered=false, "Sleeve offline"; no bio → EMG null; no
   hub → record locally, upload later.
4) PlayMode PH_FullRun (demo_mode) against sim/live/fake_hub.py and the twin (--kind both) on offset ports, explicit
   readiness waits: phase order; ≥ 30 stroke events; ≥ 95 % acked; sens chunks written; uploaded session validates;
   RTT p50 < 250 ms; 0 invalid messages. Then all OrchardReach EditMode + PlayMode. Paste everything.
Then hand over to ph-e2e for L3 (`run_pipeline.py --game phantom_hand --sim`) and L4 with real nodes.
```

---

## U6 — APK, demo mode, performance  · Oct 9 morning · branch ph/u6-release · timebox 2.5 h

```text
1) demo_mode: induction 45 s, one questionnaire at the end, ~2.5 min run; one-gesture reset to Calibrate < 10 s
   without app restart; operator "Next person" command from the app does the same.
2) Perf on PhantomHand.unity for Quest 3: draw calls/eye < 100 (target < 60), SetPass < 40, only the stone's blob
   shadow, ASTC; log numbers; fix the top three costs.
3) OpusBuildScript.BuildAndroidApkPhantomHand (Bootstrap + PhantomHand + OrchardReach as fallback; IL2CPP, ARM64,
   Vulkan, 72 Hz) → releases/game/<ver>/chetna-phantom-hand.apk. If the R7 Gradle loopback issue blocks it, log the
   exact error to MANUAL_TODO and tell Opus immediately (humans build from the editor menu).
4) List every human step for H4 (install, pair, nodes on the bank, sleeve placement, electrodes, calibrate,
   tactile_lead_ms tuning, reset, failure handling).
Final: all EditMode + PlayMode (Orchard + PhantomHand + SDK); paste totals.
```

---

## U7 — Agency phase (STRETCH, after G2) · branch ph/u7-agency

```text
PRD row 3 / FR-VR-07: 5 s rest + MVC calibration ("relax… now squeeze as hard as comfortable"), Normalized =
(EMG_current − EMG_rest)/(EMG_mvc − EMG_rest); above emg_threshold for 150 ms closes the virtual hand (hysteresis
threshold − 0.1); the real hand stays still (warn if it moves > 2 cm); ASYNC: closing delayed by async_delay_ms.
Without Node B: left-hand pinch drives it instead and the event says so. Set agency_enabled true only after a
teammate test. PlayMode tests with fake EMG packets.
```

## U8 — Synthetic participant sweep (STRETCH) · branch ph/u8-synthetic

```text
Port S3's participant model into a SyntheticHandDriver extension; PlayMode PH_FullRun_Sim for both orders × 5
seeds; sync ownership > async; witness direction correct; sessions validate; deck screenshot series.
```
