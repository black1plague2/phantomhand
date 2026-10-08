# Spec v2 — Phantom Hand on the Chetna pipeline (implements PRD v2)

**Authority:** `PRD_v2.md` (in this folder) is the product truth. This spec is how we build it. If they
conflict, the PRD wins, except where §0 below records a decision that resolves a PRD gap. Opus changes this
file, with a dated line:
- 2026-10-07 v1 initial (Glyph C3, 10-day plan).
- 2026-10-07 v2 rewritten for PRD v2: 2× ESP-WROOM-32, power bank, Makeathon 7–9 Oct, MVP/stretch split.
- 2026-10-07 v3 (Opus): PRD v2 §5.1 additions A1–A5 approved by the user → §12 below; D9 (default order async_first), D10 (passthrough allowed for A2 only).
- 2026-10-07 v3.1 (Opus, O1 review): contracts v0.2 landed. Adopted as spec: all new event fields live in `data`, `trial` = condition index; phase ids calibrate|probe_pre|induction|self_touch|agency|threat|probe_post|questionnaire|dissolve|reveal|witness|done; condition `sync`|`async` (null outside); drift_cm + toward the virtual hand, positions in metres, calibration space; sensor_chunk.timestamp_ms = first sample, emg_burst.timestamp_ms = onset; embodiment = metric map per condition + sync_minus_async, stroke_timing_err_ms {mean,p95}; `set_condition_order` takes params.condition_order. Electronics/firmware are owned by the separate electronics team (user, 2026-10-07): F1/F2 code in firmware/ is an uncompiled reference only.

## 0. Decisions on PRD gaps (proposed by Opus — team confirms or overrides)

| # | Gap in PRD v2 | Decision |
|---|---|---|
| D1 | §9.2 "inter-motor delay 60–100 ms" vs a visible brush that must reach motor B at the moment B fires | Bench-tune `motor_soa_ms` (A→B onset gap, start 100, range 60–300). The brush's speed between the two motor positions is derived from it, so sight and touch stay in step: brush speed = 10 cm / motor_soa_ms. The rest of the stroke (wrist→A, B→elbow) runs at the same speed. |
| D2 | FR-VR-01 "virtual arm follows the tracked pose" vs "fingers frozen" and risk row "lock the pose during induction" | The virtual arm follows the real wrist (+ offset) only during calibration and agency. It freezes from induction start through threat and probe, so a flinch never moves the virtual hand and the stone always lands on it. Param `follow_during_induction` (default false). |
| D3 | Firmware v0.4.0 streams sensors to ONE peer (the last sender). A laptop plot (FR-AP-01 fallback) would steal the stream from the Quest | Firmware keeps up to 3 subscribers. Any device sends `{"type":"subscribe"}` every ≤ 2 s; sensor packets go to every live subscriber (expiry 5 s). Commands still come from anyone. |
| D4 | FR-AP-01 live traces in the Flutter app — no path defined from nodes to app | The Quest forwards a down-sampled trace (EMG env + |accel|, 20 Hz) inside the live status to the hub. The laptop fallback plot subscribes to the nodes directly (D3). |
| D5 | Two wire names: PRD `sensor_chunk` (Node B wire message) vs session sensor files | Wire message = `sensor_chunk` (PRD §9.3). Session file = `sens_###.json`, schema `sensor-file.schema.json`. |
| D6 | Stroke `intensity: 150` vs software max-intensity scaling | Sent intensity = round(150 × haptic_max_intensity), firmware caps at 150 (PWM cap for 3 V motors on 5 V). Default haptic_max_intensity 1.0. |
| D7 | Device ids | Node A keeps `SLEEVE_001` (existing tests), Node B `CHETNA_BIO_001`. Both firmware 0.5.0. |
| D8 | Phone hotspots sometimes block broadcast between clients | Manual host fields (hub host in the game, node IPs in the game and app) are always available; discovery is the default, not the only path. |

## 1. Purpose

Theme 5. A virtual arm, offset 15 cm, is stroked by a VR brush while the sleeve vibrates on the real forearm
in step: the participant feels the virtual hand as theirs. A stone drops on it and the real arm flinches.
Delaying the touch by 600 ms dissolves the feeling. The witness screen shows both states with their numbers.

## 2. System

```
Quest 3 standalone (Unity, com.opus.sdk + PhantomHand)
  ├─ WS :8787 / beacon :8788 ─► laptop hub (Flutter app) ─► session files ─► Python analytics ─► report
  ├─ UDP :8790 cmds / :8791 discovery ─► Node A "haptic" ESP-WROOM-32 #1: 2 ERM, MPU6050, OLED
  └─ UDP ◄─ sensor_chunk ─ Node B "bio" ESP-WROOM-32 #2: BioAmp EXG Pill
URBN 20,000 mAh bank ─► micro-USB 5 V to each board. One 2.4 GHz hotspot for all.
```
Both nodes optional at runtime: missing data → hand-tracking fallback + "Sleeve offline" indicator.

## 3. Hardware (detail: 05-ELECTRONICS-TEAM.md)

Node A: motors GPIO25 (A) / GPIO26 (B) via 1.5 kΩ → 2N2222A, 1N4007 flyback, 100 µF; motor + to the board 5 V
rail; PWM 2 kHz 8-bit capped at 150/255. I2C SDA 21 / SCL 22: MPU6050 0x68, SSD1306 0x3C. Battery `null`.
Node B: BioAmp at 3V3, OUT → GPIO34 (ADC1). 1 kHz sampling, band-pass 74.5–149.5 Hz, envelope 100 Hz.
Placement: motor A near the wrist (5 cm from the crease, dorsal), motor B 10 cm further toward the elbow.

## 4. Wire protocol (contracts own exact schemas)

| Message | Dir | Shape |
|---|---|---|
| discovery | A/B → all, 1 Hz, :8791 | PRD §9.1: `type:"device_discovery"`, device_id, `device_kind` haptic/bio, firmware_version, command_port 8790, status, `motor_count`, timestamp_ms. Node A also keeps the legacy `{"opus_haptic":1,...}` hello |
| stroke | game → A | PRD §9.2: `{cue_id, motor, intensity ≤150, duration_ms 200, pattern:"pulse", cue:"stroke", play_at_ms}` (play_at_ms informational; the game times the send) |
| display | game → A | `{"type":"display","text"}` ≤ 12 chars, ≤ 2/s → OLED line |
| subscribe | any → A/B | `{"type":"subscribe"}` every ≤ 2 s; expiry 5 s; max 3 subscribers (D3) |
| ping/keepalive | game → A | existing `ping`; game sends one every 1 s in a session (feeds the 2 s watchdog) |
| sensor_data (A) | A → subscribers | existing v1 form: `sensors{imu_accel_x/y/z, imu_gyro_x/y/z:{value,unit,status}}`, 100 Hz, one sample per packet |
| sensor_chunk (B) | B → subscribers | PRD §9.3: `{type:"sensor_chunk", device_id, device_kind:"bio", timestamp_ms, sample_rate_hz:100, emg_envelope:[…10 values], unit:"raw_adc", status}` every 100 ms |
| emg_burst | B → subscribers | `{type:"emg_burst", device_id, timestamp_ms, peak, baseline_rms}` when env > baseline + 3 SD ≥ 30 ms |
| status | A/B → subscribers | existing + device_kind, battery_pct null, subscribers count |
| live status | game → hub | existing + `game_state{phase, condition, remaining_s, nodes{haptic{connected}, bio{connected, emg_level}}}` + `trace{emg_env[], accel_mag[], t0_ms, fs_hz:20}` |
| operator cmds | hub → game | existing + `phase_next`, `set_condition_order`, `abort_phase`, `next_person` |

Software stroke limits: ≥ 250 ms per motor between stroke sends, ≤ 4 stroke sends/s. Firmware: 50–400 ms
pulse, 100 ms gap, 50 % duty per 10 s, 2 s watchdog, intensity cap 150.

## 5. Phases (PRD §5) — MVP unless marked

| # | Phase | Time | Behaviour |
|---|---|---|---|
| 0 | Calibrate | ≤ 30 s | Forearm outline; real wrist within 3 cm for 2 s; store wrist pose, forearm axis |
| 1 | ProbePre | 20 s | Dark room + ruler; dot on the left index tip only; point to where the right index feels; still < 1 cm for 1.5 s |
| 2 | Induction C1 | 90 s | Virtual right arm 15 cm to the LEFT (toward midline); frozen (D2); brush wrist→elbow at stroke_rate_hz (1); A then B per D1; OLED "SYNC"/"ASYNC" |
| 3 | Agency | 30 s | **Stretch.** Rest + MVC calibration (FR-VR-07); normalised EMG > emg_threshold closes the virtual hand; real hand still |
| 4 | Threat | 5 s | 0.6 s telegraph, stone (Rigidbody) falls on the virtual hand; record 1.5 s after impact: EMG burst, IMU jolt, real wrist speed |
| 5 | ProbePost + questions | 20 s + ~30 s | Probe again; 3 items on a 7-point scale, poked |
| 6 | Condition C2 | same as 2–5 | ASYNC: each motor fires async_delay_ms (600) ± 100 after the brush passes, A/B order random (50 %) |
| 7 | Witness | 30 s | SYNC vs ASYNC: drift change, flinch latency/size, ownership; closing line |

condition_order: sync_first default; counterbalancing = stretch (param exists, UI to alternate is stretch).

## 6. Params (manifest; PRD FR-VR-06 core + build params)

Core (PRD): offset_cm 5–30 (15) · stroke_rate_hz 0.5–1.5 (1.0) · induction_s 30–180 (90) ·
async_delay_ms 300–1000 (600) · condition_order sync_first|async_first (sync_first) · threat_enabled (true) ·
agency_enabled (false — stretch) · emg_threshold 0–1 (0.3) · tactile_lead_ms 0–150 (40).
Build: motor_soa_ms 60–300 (100) · stroke_jitter_ms 0–300 (150) · motor_a_from_wrist_cm 2–10 (5) ·
motor_spacing_cm 5–15 (10) · forearm_length_cm 20–30 (25) · haptics_enabled (true) ·
haptic_max_intensity 0–1 (1.0) · follow_during_induction (false) · demo_mode (false: when true, induction 45 s
and one questionnaire at the end) · stimulated_side right only for MVP (left = stretch).

## 7. Events (events.ndjson; trial = condition index)

PRD §9.4 set: phase_start, stroke, threat_impact, drift_probe, questionnaire_item, emg_burst. Plus, needed by
analytics and the witness: calibration, haptic_cue (cue "stroke", motor, delivered, ack_latency_ms),
threat_response (on-device preliminary), witness_summary. Field shapes:
phase_start {phase, condition} · calibration {wrist_pos, forearm_axis, ok} · stroke {index, brush_pass_a_ms,
brush_pass_b_ms, cue_a_send_ms, cue_b_send_ms, swapped, timing_err_ms} · drift_probe {when, perceived_x,
actual_x, drift_cm, confirmed} · threat_impact {impact_ms, impact_pos, ok} · threat_response {wrist_peak_mps,
wrist_latency_ms, imu_peak, emg_peak_x, emg_latency_ms, quality} · questionnaire_item {item, value} ·
emg_burst {peak, baseline_rms, device_ms} · witness_summary {per condition…}.
Files: session.json, events.ndjson, kin_###.json, sens_###.json (EMG env + IMU), metrics.json.

## 8. Analytics (FR-AN-01) — metrics.json `embodiment`, per condition + sync−async

drift_change_cm (+ toward virtual), ownership (mean q1,q2), control (q3), flinch_emg_peak_x (× baseline RMS),
flinch_emg_latency_ms, flinch_imu_peak, flinch_wrist_peak_mps, flinch_wrist_latency_ms,
stroke_timing_err_ms {mean,p95}, cue_delivery_rate, emg_windows_excluded. EMG samples within a motor pulse
+ 50 ms are excluded (FR-AN-01). Every value carries quality ok/degraded/missing + reasons.

## 9. App (FR-AP-01 MVP, FR-AP-02 stretch)

MVP: phantom_hand in the program builder from the manifest; live card with phase, condition, node status,
EMG envelope + |accel| traces (from `trace`), operator buttons (start, next phase, abort phase, pause, end).
Laptop fallback: `tools/demo/live_plot.py` subscribes to both nodes and plots EMG + accel.
Stretch: Embodiment report section, witness mirror, devices screen bio row.

## 10. KPIs (PRD §12) → acceptance

Full run twice in a row without reset · both nodes discovered and streaming wirelessly, no USB tether ·
touch-to-sight < 100 ms in SYNC (measured: stroke timing_err + ack path) · sync > async for drift and flinch
(direction) · EMG flinch clearly above baseline on the live trace · 5-teammate pilot.

## 11. Versions

Unity 6000.4.6f1 · URP 17.0.4 · Meta XR Core / Interaction 205.0.0 · Quest 3 · firmware A/B 0.5.0 ·
Arduino-ESP32 core 3.x, board "ESP32 Dev Module" · contracts v0.2.

## 12. Additions A1–A5 (PRD §5.1, user-approved 2026-10-07)

**Scope rule:** MVP+ items (A1–A3) start only after gate G2 is green; A4/A5 are stretch. Design the MVP code so
they slot in without rework: the phase machine takes optional phases, the questionnaire takes an item list, the
witness takes a row list, the brush/stroke path runs without the arm visible.

| Decision | |
|---|---|
| D9 | `condition_order` default becomes **async_first** (PRD revised run: the strongest illusion is kept for the finale). sync_first stays available. |
| D10 | Passthrough is allowed for A2 only: one `OVRPassthroughLayer` (underlay), disabled by default, enabled only in the Reveal phase. Standard passthrough layer, never the Passthrough Camera API. Rig validators allow this one layer. |

Revised phase order (per condition, then the finale, `demo_mode` timings in PRD §5.1):
Calibrate → ProbePre → [C1: Induction (+SelfTouch tail if self_touch_s > 0) → Agency? → Threat → ProbePost →
Questionnaire] → [C2: same] → Dissolve? → Reveal? → Witness → Done. Dissolve/Reveal run once, after the last
condition, only when `dissolve_enabled` / `passthrough_reveal`.

| # | Behaviour |
|---|---|
| A1 | Voice line (≤ 8 words, EN/HI clip by `voiceover_lang`) on each `phase_start` when `voiceover_enabled`; Voice mixer group; never during probes. Questionnaire adds **q4** "The awareness that noticed these sensations was the same as before." after every condition. Witness gets a q4 row per condition, labelled as a pointer, not proof. |
| A2 | Dissolve 18 s: arm alpha 1→0 over 3 s, brush + strokes continue ~15 s on empty space (same StrokeScheduler, same safety gates). Reveal 10 s: passthrough fades in (fallback when unavailable: ghost outline of the tracked real right hand). Voice "Not this." |
| A3 | SelfTouch = the last `self_touch_s` of each Induction: brush retires; left index dot; trigger bands at WorldPos(motor_a_from_wrist_cm) and +motor_spacing_cm; crossing a band fires that motor at once (ASYNC: + async_delay_ms); the same ≥ 250 ms/motor and ≤ 4/s gates. |
| A4 | Stretch. Mic RMS envelope low-passed 1 Hz → arm glow + 2–3 % swell; ASYNC replays the participant's own envelope from `breath_replay_lag_s` earlier; poor signal → 0.2 Hz guide breath, logged `source: "guide"`. Android RECORD_AUDIO. |
| A5 | Stretch, only with Node B + FR-VR-07 working. Extends Agency: EMG closes the hand, then the hand closes by itself while EMG is flat (`autonomous_close_enabled`); **q5** "I caused that movement." |

New params (manifest): voiceover_enabled (true) · voiceover_lang en|hi (en) · dissolve_enabled (true) ·
passthrough_reveal (true) · self_touch_s 0–30 (15; 0 = off) · breath_enabled (false) · breath_replay_lag_s
10–30 (20) · autonomous_close_enabled (false).

New events: `self_touch` {band a|b, motor, cue_send_ms, finger_speed_mps} · `dissolve_start` {condition} ·
`passthrough_on` {fallback: bool} · `breath_env` {t0_ms, fs_hz: 20, values[20], source mic|guide|replay} (one
event per second, not one per sample) · `autonomous_close` {emg_level}. questionnaire_item.item gains q4, q5.
Analytics: `witness_q4` per condition (expected flat; report the sync−async difference with no claim attached).

