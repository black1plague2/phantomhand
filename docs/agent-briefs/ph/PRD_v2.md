# Product Requirements Document (PRD) v2 — Project Chetna: Phantom Hand

**Team:** Kela · **College:** Vellore Institute of Technology, Vellore
**Theme:** Theme 5 — "Consciousness is beyond the body and mind. Show artificial sense of Self in body and mind."
**Team members:** Vaibhav Jangid (team leader and representative), Bhavya Dhoot, Garv Bansal
**Event:** Vedanta Makeathon, 7–9 Oct 2026 · **Document date:** 7 Oct 2026
**Supersedes:** PRD v1 (Orchard Cider Press rehabilitation build)

---

## 1. Executive Summary

Phantom Hand makes a person feel a virtual hand as their own within about two minutes, then breaks that feeling on command. It runs on a Meta Quest 3 and a two-node sensor sleeve built from the parts the team now holds.

* **What the judge sees:** the participant's real forearm rests still on a table while a virtual arm, offset 15 cm sideways, is stroked by a VR brush. The sleeve vibrates on the real forearm in step with the brush. A rock then drops on the virtual hand and the real arm flinches. A live dashboard shows the flinch on the EMG and accelerometer traces.
* **Theme 5 link:** the sense that "this body is me" is manufactured from two synchronised signals and dissolved by delaying one of them by 600 ms. What can be manufactured and dissolved is an appearance, not the Self. The witness that noticed the change did not change.
* **Why it is feasible:** most of the stack is Chetna already: the Unity SDK, the UDP haptic protocol, session recording, the Flutter hub and the analytics pipeline. The new work is one game module, a second sleeve node and an embodiment metrics module.
* **Hardware:** two ESP-WROOM-32 boards, one for haptics and IMU (Node A) and one for EMG (Node B), both powered from one URBN 20,000 mAh power bank.

---

## 2. What Changed from PRD v1

PRD v2 keeps the Chetna pipeline but changes the game, splits the sleeve into two nodes and powers both from a power bank.

| Area | PRD v1 | PRD v2 (this document) |
| --- | --- | --- |
| Flagship game | Orchard Cider Press: reach, then squeeze an apple | Phantom Hand: ownership illusion, then threat. Orchard stays as a fallback module |
| Sleeve | One ESP32 DevKit carrying every sensor | Two nodes: Node A (haptics, IMU, OLED) and Node B (EMG) |
| Boards | ESP32 DevKit | 2 × ESP-WROOM-32 dev boards with micro-USB |
| Power | 3.7 V Li-ion, TP4056 and 5 V boost | One URBN 20,000 mAh power bank, one USB-A port per node. The 18650 cells and XL6009 are returned |
| Motor base resistor | 1 kΩ | 1.5 kΩ, or two in parallel if a motor feels weak |
| IMU role | Trunk-lean warning above 15° | Checks the real arm stays still and measures the flinch jolt |
| OLED role | Live EMG graph | Condition status (SYNC or ASYNC) and link state |
| Haptic pattern | Ramp and buzz | Motor A then Motor B stroke; 600 ms delay in the asynchronous condition |
| Main metrics | Trunk compensation, peak EMG | Drift toward the virtual hand, flinch size and latency, ownership score, sync minus async contrast |
| Network | UDP 8790 commands, 8791 discovery | Unchanged. Node B announces itself as `device_kind: bio` |

---

## 3. Problem and Theme 5 Rationale

Theme 5 asks the team to show an artificial sense of Self in body and mind. Phantom Hand turns that sense into something a visitor can feel, measure and lose within four minutes.

* **Body:** a hand that is not theirs is felt as theirs when sight and touch agree in time. The build plan cites published work on vibrotactile virtual hand illusions reporting drift of about 2 cm and higher ownership for synchronous than asynchronous stimulation ([Virtual Reality, 2024](https://link.springer.com/article/10.1007/s10055-024-01052-6)).
* **Mind:** the optional agency phase lets a forearm EMG burst close the virtual hand while the real hand stays still, adding the sense that "I am the one moving it".
* **The argument:** a feeling that can be built from two signals and removed by delaying one of them is an appearance. The participant who notices both states is the constant, which is the Tattva link.
* **Rehab value:** body ownership is a lever in mirror therapy and ownership work, so the build extends Chetna's clinical story. The gaps named in PRD v1 still apply: no touch feedback in standard VR rehab, and intent that optical tracking cannot see.

---

## 4. Users

Three people touch the system at the demo: the participant, the observer and the team operator.

| Persona | Need | Interaction |
| --- | --- | --- |
| Participant (judge or visitor) | Feel and see the illusion quickly and safely | Sits with the right forearm on the table, wears the sleeve and Quest, answers three questions by poking a 7-point scale in VR |
| Observer (judge or spectator) | See what is happening without wearing anything | Watches the sleeve OLED (SYNC or ASYNC), the live dashboard and the witness screen |
| Operator (team member) | Run the demo reliably, twice in a row | Starts and sequences the run from the Flutter app, sets the condition order, checks both nodes are online |
| Physiotherapist (later) | Objective ownership and motor biomarkers | Reads the session report. Not part of the hackathon demo |

---

## 5. The 4-Minute Run

One run takes about four minutes, is started from the Flutter app, and ends on a witness screen that compares the participant's own synchronous and asynchronous numbers.

**Setup:** the participant sits with the right forearm resting on a table. The sleeve is on that forearm, with Motor A near the wrist and Motor B 10 cm toward the elbow. EMG electrodes sit over the forearm flexors with the reference on the elbow bone. Hand tracking stays on throughout and the real hand is never rendered.

| # | Phase | Time | What happens | Scope |
| --- | --- | --- | --- | --- |
| 1 | Baseline drift probe | 20 s | Scene goes dark except a ruler. The participant points with the left index finger to where the right index feels to be. Hand tracking records both hands | MVP |
| 2 | Synchronous induction | 90 s | A virtual right arm appears 15 cm to the left of the real one. A brush strokes it wrist to elbow about once a second. Motor A then Motor B fire in step. OLED shows SYNC | MVP |
| 3 | Agency | 30 s | An EMG burst above threshold closes the virtual hand while the real hand stays still | Stretch |
| 4 | Threat | 5 s | A rock falls on the virtual hand. The system records the EMG burst, IMU jolt and real-hand withdrawal speed for 1.5 s after impact | MVP |
| 5 | Drift probe and questions | 20 s + questions | Second drift probe, then three ownership questions answered by poking a 7-point scale in VR | MVP |
| 6 | Asynchronous condition | 90 s + threat + probe | Same scene, but the sleeve fires 600 ms after the brush, in random order. Ownership should collapse and the flinch shrink | MVP |
| 7 | Witness screen | 30 s | The participant sees their own numbers side by side for the two conditions, then the closing line that ties it to the Tattva | MVP |

Counterbalancing the order of phases 2 and 6 across participants is a program setting and a stretch goal. All timings and the 15 cm offset live in the game manifest, so they can be tuned without code changes.

### 5.1 Additions (7 Oct, from the research note)

These turn the run from one illusion built and broken into a staged peeling of identity: body, breath, mind, then the witness. They are software only, on parts already in hand, and start once the "one full run recorded end to end" gate is green. Full evidence and costs: `research/phantom-hand-additions.md`.

| # | Addition | What happens | Cost | Scope |
| --- | --- | --- | --- | --- |
| A1 | Self-inquiry voice-over and witness item | Short voice lines (under 8 words, EN or HI) at each phase start follow the koshas and "neti, neti". A fourth question, q4, asks "The awareness that noticed these sensations was the same as before". The witness screen shows every body and mind number moving between SYNC and ASYNC while q4 stays flat. q4 is a pointer, not proof | S, 2–3 h | MVP+ |
| A2 | Invisible-hand finale ("Neti") | After the last threat the virtual arm fades out over 3 s while the brush and motors keep stroking empty space for about 15 s. Then passthrough fades in and shows the real arm 15 cm from where it felt. Voice: "Not this." Fallback: a ghost outline of the tracked real hand instead of passthrough | S, 2–4 h | MVP+ |
| A3 | "You are the brush" (self-touch) | For the last 15–20 s of induction the brush retires and the participant strokes the virtual arm with their tracked left index finger. Motor A then Motor B fire as the fingertip crosses each band (async: +600 ms). Same per-motor safety gate | M, 3–4 h | MVP+ |
| A4 | Breathing arm | A soft glow and 2–3 % swell on the virtual arm follow the participant's breath (exhale or hum, Quest microphone). In ASYNC it replays their own breath from 20 s earlier. If the signal is poor, it follows a 0.2 Hz guide breath and is labelled paced breathing | M, 4–5 h | Stretch |
| A5 | Intent, then a hand that moves by itself | Extends phase 3. Normalised EMG closes the virtual hand during an isometric squeeze; then the hand closes on its own while EMG is flat. q5: "I caused that movement." Fallback: drive it from real finger flexion via hand tracking. Only if Node B and FR-VR-07 already work | M–L, 5–7 h | Stretch |

**Revised run (about 4:40):** baseline probe → ASYNC (induction, threat, probe, q1–q4) → SYNC induction with the breathing arm (A4) and self-touch at the end (A3) → agency (A5, optional) → threat → probe and q1–q4 (q5 if A5 ran) → invisible-hand dissolve and passthrough reveal (A2) → witness screen with the flat q4 line. Running ASYNC first keeps the strongest illusion for the finale; `condition_order` stays a manifest setting.

**Not added:** heartbeat sync (no pulse sensor; moving the BioAmp to ECG loses the flinch measure, and faking it is ruled out), very long arm (competes with A2), third arm (spoils the drift probe), knife-style threats (shrink the SYNC vs ASYNC contrast; keep the falling stone), skin temperature (no sensor).

---

## 6. System Architecture

```
          ┌────────────────────────────────────────────────┐
          │  Meta Quest 3 (standalone)                     │
          │  - Unity + Meta XR SDK, PhantomHand module     │
          │  - Hand tracking (drift probe, withdrawal)     │
          │  - UDP out: haptic commands · UDP in: sensors  │
          └──────▲────────────────────────┬───────────────┘
                 │ sensor_chunk (UDP)     │ haptic commands (UDP 8790)
                 │                        │ discovery (UDP 8791)
        ┌────────┴─────────┐     ┌────────▼─────────┐
        │ Node B (bio)     │     │ Node A (haptic)  │
        │ ESP-WROOM-32 #2  │     │ ESP-WROOM-32 #1  │
        │ BioAmp EXG Pill  │     │ MPU6050 (0x68)   │
        │ OUT -> GPIO34    │     │ OLED SSD1306     │
        │                  │     │ (0x3C)           │
        │                  │     │ 2 x ERM motors   │
        │                  │     │ via 2N2222A      │
        └────────▲─────────┘     └────────▲─────────┘
                 │ micro-USB 5 V          │ micro-USB 5 V
              ┌──┴────────────────────────┴──┐
              │  URBN 20,000 mAh power bank  │
              └──────────────────────────────┘

   Laptop hub (Flutter app + analytics) on the same 2.4 GHz Wi-Fi,
   linked to the Quest over the existing WebSocket (8787) for live view
   and session recording.
```

All devices join one 2.4 GHz network (a phone hotspot works). Splitting the sleeve into two nodes keeps motor switching noise away from the EMG front end and keeps the EMG node battery-only.

---

## 7. Bill of Materials: Have, Want, Return

| Item | Qty | Role | Status |
| --- | --- | --- | --- |
| Meta Quest 3 | 1 | VR headset (own) | In hand |
| ESP-WROOM-32 dev board, micro-USB | 2 | Node A and Node B | In hand |
| MPU6050 | 1 | Node A, arm motion, I2C 0x68 | In hand |
| 0.96-inch OLED SSD1306 | 1 | Node A status display, I2C 0x3C | In hand |
| BioAmp EXG Pill with cable and electrodes | 1 set | Node B muscle sensing | In hand |
| ERM coin motors, 3 V | 2 (3 held) | Motor A and Motor B | In hand, 1 spare |
| 2N2222A transistors | 2 (6 held) | One driver per motor | In hand, 4 spare |
| 1N4007 diodes | 2 (5 held) | Flyback diode per motor | In hand, 3 spare |
| 100 µF capacitors | 4 | Two motor channels, two 5 V rails | In hand |
| 1.5 kΩ resistors | 2 (5 held) | Transistor base resistors | In hand, 3 spare |
| Breadboards | 2 | One per node | In hand |
| Jumper wires, M-M and F-M | 1 set | Wiring | In hand |
| URBN 20,000 mAh power bank | 1 | Powers both nodes | In hand. Confirm 2 USB-A ports |
| USB-C to USB-A cable and 5 V adapter | 1 each | Charge the power bank | In hand |
| **USB-A to micro-USB cables** | **2** | Bank to each board. One must carry data for flashing | **Short by 2** (borrow phone cables) |
| Grove EMG kit | 1 | Backup EMG sensor | Return after the BioAmp is verified |
| CH9102 USB-to-TTL module | 1 | Flashing fallback | Return after the first flash works |

**Already returned:** 2 × 18650 cells, XL6009 boost module, ESP32-H2 Glyph (no Wi-Fi).
**Dropped from the earlier plan:** Glyph C3 boards, boost converter, TP4056 charger, cell holders. The power bank replaces the whole battery chain.
All issued parts go back on 9 Oct, as the CARF declaration states.

---

## 8. Hardware Design

### 8.1 Pin map (ESP-WROOM-32)

| Node | Signal | Pin | Note |
| --- | --- | --- | --- |
| A | Motor A drive | GPIO25 | Through 1.5 kΩ to the 2N2222A base. Same pin as firmware v0.4.0 |
| A | Motor B drive | GPIO26 | Same as above |
| A | I2C SDA | GPIO21 | MPU6050 (0x68) and OLED (0x3C) share the bus at 3.3 V |
| A | I2C SCL | GPIO22 | |
| B | BioAmp OUT | GPIO34 | ADC1, input-only. ADC2 pins are unreliable while Wi-Fi is on |
| B | BioAmp VCC and GND | 3V3 and GND | Run the BioAmp at 3.3 V so its output cannot exceed the ADC range |

Avoid GPIO 0, 2, 12 and 15 (boot-strapping) and 6 to 11 (flash) for anything else. GPIO25 and 26 are only used as digital outputs.

### 8.2 Motor driver (per channel)

* GPIO → 1.5 kΩ → 2N2222A base. Emitter to GND. Collector to the motor negative lead. Motor positive lead to the board's 5 V rail.
* 1N4007 across the motor, cathode on the motor positive lead (flyback clamp).
* 100 µF across the 5 V rail and GND, next to the transistor.
* **Base current:** about (3.3 V − 0.7 V) / 1.5 kΩ ≈ 1.7 mA, enough for a coin ERM. If a motor feels weak or its transistor warms, put two 1.5 kΩ resistors in parallel (about 750 Ω, roughly 3.5 mA). Five resistors are held, so this works on both channels.
* **Duty cap:** the motors are 3 V parts on a 5 V rail, so cap PWM at about 150/255 so the average stays near 3 V. PWM is 2 kHz, 8-bit.

### 8.3 EMG (Node B)

* BioAmp powered from the 3.3 V pin. OUT to GPIO34.
* Electrodes: IN+ and IN− about 3 cm apart over the forearm flexor belly, reference on the bony elbow. Clean the skin with an alcohol wipe first.
* Sampling 1 kHz; band-pass 74.5–149.5 Hz; envelope streamed at 100 Hz.
* Bench check required: a clean forearm burst at 3.3 V, then again with a motor buzzing nearby.
* The Grove EMG kit is the swap-in if the BioAmp is too noisy.

### 8.4 Power

* One URBN 20,000 mAh power bank feeds both boards through two USB-A to micro-USB cables, one port per node. The boards' own regulators make 3.3 V.
* Put a 100 µF capacitor across each board's 5 V rail near the USB input to stop Wi-Fi brownouts.
* The two nodes now share one source, so motor noise can reach the EMG signal. The BioAmp runs from the board's regulated 3.3 V, and the motor-noise bench check in 8.3 covers this.
* Some power banks switch off at low current. Test with both boards running; sharing one bank raises the combined load and helps.
* The bank is heavy: keep it on the table with cables run to the sleeve, never on the forearm where hand tracking looks.
* Flash each board with the bank cable unplugged. Do not feed a board from the bank and USB at the same time.

---

## 9. Communication Protocols

Commands stay on UDP 8790 and discovery on UDP 8791. Fields marked *(new)* are proposed for v2 and go through the contracts owner.

### 9.1 Discovery (Node B example, broadcast every 1000 ms)

```json
{
  "type": "device_discovery",
  "device_id": "CHETNA_BIO_001",
  "device_kind": "bio",
  "firmware_version": "0.5.0",
  "command_port": 8790,
  "status": "available",
  "motor_count": 0,
  "timestamp_ms": 142050
}
```

Node A announces `device_kind: "haptic"` with `motor_count: 2`.

### 9.2 Stroke command (Quest to Node A)

```json
{
  "cue_id": "stroke_017",
  "motor": 0,
  "intensity": 150,
  "duration_ms": 200,
  "pattern": "pulse",
  "cue": "stroke",
  "play_at_ms": 143250
}
```

`cue: "stroke"` and `play_at_ms` are *(new)*. Motor 1 follows motor 0 after an inter-motor delay tuned on the bench (start at 60–100 ms). `tactile_lead_ms` in the manifest sends the command early by the measured link delay plus motor spin-up so touch lands with the brush. In the asynchronous condition the command is delayed by 600 ms.

### 9.3 Sensor stream (Node B to Quest, new schema)

```json
{
  "type": "sensor_chunk",
  "device_id": "CHETNA_BIO_001",
  "device_kind": "bio",
  "timestamp_ms": 143100,
  "sample_rate_hz": 100,
  "emg_envelope": [412.5, 415.1, 418.9, 421.3],
  "unit": "raw_adc",
  "status": "ok"
}
```

Node A streams `imu_accel_x/y/z` at 100 Hz in the same `sensor_data` form used in v1.

### 9.4 New game events

`phase_start`, `stroke`, `threat_impact`, `drift_probe`, `questionnaire_item`, `emg_burst`.

---

## 10. Functional Requirements

### 10.1 VR experience (Unity)

* **FR-VR-01 Virtual arm:** the virtual arm follows the tracked pose with a fixed sideways offset (manifest `offset_cm`, default 15). Fingers stay frozen during induction. The real hand is never rendered.
* **FR-VR-02 Brush strokes:** a brush moves wrist to elbow at `stroke_rate_hz` (default 1). Each stroke triggers Motor A then Motor B.
* **FR-VR-03 Threat:** a rock (Rigidbody) falls on the virtual hand. Impact time is logged.
* **FR-VR-04 Drift probe:** the participant points with the left index finger to where the right index feels to be. Error in cm is computed from hand tracking.
* **FR-VR-05 Ownership questions:** three items on a 7-point scale, answered by poking buttons in VR. Add q4 (witness item, A1) after each condition, and q5 (agency, A5) when the agency phase runs.
* **FR-VR-06 Manifest:** `offset_cm`, `stroke_rate_hz`, `induction_s`, `async_delay_ms` (600), `condition_order`, `threat_enabled`, `agency_enabled`, `emg_threshold`, `tactile_lead_ms`, plus for section 5.1: `voiceover_enabled`, `voiceover_lang`, `dissolve_enabled`, `passthrough_reveal`, `self_touch_s`, `breath_enabled`, `breath_replay_lag_s`, `autonomous_close_enabled`. The app builds its form from the manifest.
* **FR-VR-08 Additions:** the new phases in section 5.1 log `self_touch`, `dissolve_start`, `passthrough_on`, `breath_env` (20 Hz) and `autonomous_close` events. Passthrough uses the standard passthrough layer, not the camera API.
* **FR-VR-07 Calibration (agency phase):** 5 s resting baseline and a maximum voluntary contraction, normalised as:

```
Normalized = (EMG_current - EMG_rest) / (EMG_mvc - EMG_rest)
```

### 10.2 Firmware

* **FR-FW-01 Node A:** extend firmware v0.4.0 (already ESP-WROOM-32) from `MOTOR_COUNT 1` to 2, on GPIO25 and 26. Add the OLED (SYNC or ASYNC, link state) and MPU6050 sampling at 100 Hz. No board port is needed. Battery percentage reports `null` because power comes from the bank.
* **FR-FW-02 Node B:** new `bio_node` firmware: 1 kHz ADC on GPIO34, band-pass 74.5–149.5 Hz, rectified envelope streamed at 100 Hz, discovery as `bio`.
* **FR-FW-03 Safety limits (Node A):** pulses 50–400 ms, 100 ms minimum gap, 50% duty cap over a rolling 10 s per motor, PWM cap near 150/255.
* **FR-FW-04 Watchdog:** if no command or keepalive arrives for 2000 ms, all motor outputs go to 0.

### 10.3 Analytics and app

* **FR-AN-01 Embodiment metrics:** drift toward the virtual hand (cm), EMG flinch peak (multiple of baseline RMS) and latency, IMU jolt, real-hand withdrawal speed, ownership score, and the sync minus async contrast for each. EMG windows that overlap a motor pulse are flagged and not used.
* **FR-AP-01 Live view (MVP):** EMG envelope and accelerometer traces visible to observers during the run. The Flutter live panel is the target; a simple laptop plot is the fallback.
* **FR-AP-02 Report (stretch):** an Embodiment section in the session report and the witness-screen data.

---

## 11. Non-Functional Requirements and Safety

### Performance

* **Touch and sight agreement:** felt vibration within 100 ms of brush contact in the synchronous condition. The build plan measured the cue path at 3–72 ms. Ownership has been reported to survive visual delays up to about 300 ms ([Shimada et al., 2009](https://ideas.repec.org/a/plo/pone00/0006185.html)), so the 600 ms asynchronous delay sits well outside it.
* **Runtime:** at least 2.5 hours of continuous operation. A 20,000 mAh bank gives a large margin for two Wi-Fi boards; the real limit is the bank's low-current auto-off, which must be tested.
* **Fail-safe:** the game loop never blocks on lost UDP packets. Missing sensor data falls back to hand tracking and shows a non-disruptive "Sleeve offline" indicator.

### Safety rules (go in firmware and the runbook)

1. **Node B never touches a laptop or a charger while electrodes are on a person.** It runs from the power bank only. Never charge the bank while a node is attached.
2. **Flash with the bank cable unplugged**, and flash Node B only with the electrodes off.
3. **Disposable electrodes only**, skin cleaned first, with a consent line for anyone who wears them.
4. **Haptic limits** from FR-FW-03 and the 2 s watchdog from FR-FW-04 are always on.
5. **Never short or reverse** any supply lead; the power bank can deliver high current.

---

## 12. KPIs and Success Criteria

* The full run completes **twice in a row without a reset** during the live demo.
* **Both nodes are discovered and exchange packets wirelessly** at the demo, with no laptop USB tether to either node.
* **Touch-to-sight delay under 100 ms** in the synchronous condition.
* **A visible contrast between conditions:** more drift toward the virtual hand and a larger flinch in the synchronous run than the asynchronous one. Published synchronous drift is about 2 cm; the demo target is the direction of the effect, not a fixed number.
* **EMG flinch clearly above baseline** on the live trace after the threat.
* **Pilot:** five teammates run it before the demo, with order counterbalanced if time allows.

---

## 13. Risks and Fallbacks

| Risk | Likelihood | Fallback |
| --- | --- | --- |
| Illusion is weak for some judges | Medium | Show the live numbers anyway; the sync vs async contrast still makes the point. Pre-tune offset and stroke rate on five teammates |
| BioAmp noisy, or motor noise couples in through the shared bank | Medium | Check the trace with motors running; swap to the Grove EMG; else drop EMG and keep IMU and hand-tracking flinch |
| Power bank switches off at low current | Medium | Run both boards from the same bank to raise the load; test early; use a second bank if one is available |
| Motor feels weak with 1.5 kΩ base resistors | Low to medium | Put two 1.5 kΩ in parallel on that base |
| Hand tracking flickers on a resting hand | Low to medium | Lock the virtual arm pose during induction; use tracking only for probes and threat |
| No spare ESP32 board | Medium | Keep a known-good flash image and the CH9102 as a flashing fallback; if a board dies, run Node A alone (haptics, IMU and hand-tracking flinch) and drop EMG |
| Cables not found (micro-USB) | Low | Borrow phone cables from teammates; at least one must carry data |
| Consent for electrodes on visitors | Low if rules kept | Disposable electrodes, consent line, organiser approval |

---

## 14. Roadmap (7–9 Oct 2026)

The ten-day plan from the build plan is compressed into the Makeathon window. Stretch items: agency phase, counterbalanced order, full Python sleeve twin, Wokwi simulation, Flutter Embodiment report panel.

```
Oct 7 (evening): Bench and wiring
├── Flash both boards over micro-USB (bank unplugged)
├── I2C scan on Node A shows 0x68 and 0x3C
├── Motor driver test per channel (1.5 kΩ base; parallel pair if weak), duty cap 150/255
├── BioAmp at 3.3 V: clean forearm burst, then again with a motor buzzing nearby
├── Two-motor apparent motion: tune inter-motor delay (start at 60–100 ms)
└── Quest hand tracking with the sleeve on
        GATE: both nodes discovered over Wi-Fi; EMG trace usable

Oct 8: Build and integrate
├── Node A firmware v0.5.0 (2 motors, OLED, MPU6050 at 100 Hz)
├── Node B firmware (1 kHz sampling, band-pass, 100 Hz envelope)
├── PhantomHand Unity module and manifest; stroke cue with play_at_ms
├── Sensor chunk and new events through the existing UDP and hub
└── End-to-end run on real nodes; tune tactile_lead_ms
        GATE: one full 4-minute run recorded end to end

Oct 9: Pilot, polish, return
├── Five-teammate pilot; tune offset and stroke rate
├── Witness-screen wording and 3-minute pitch tied to Theme 5
├── Demo runs twice in a row without a reset
└── Return all issued parts per the CARF declaration
        GATE: demo-ready
```

---

## 15. Open Items

- [ ] 2 × USB-A to micro-USB cables, at least one carrying data
- [ ] Confirm the power bank has 2 USB-A ports (else a splitter cable or a second bank)
- [ ] Bench-check 1.5 kΩ motor drive strength on both channels
- [ ] Bench-check BioAmp at 3.3 V, including with motors running
- [ ] Demo slot length, to decide whether the agency phase fits
- [ ] Organiser rules on electrodes for judges and visitors
- [ ] Name: Phantom Hand, Sakshi, or keep it under the Chetna brand

---

## Sources

* Team build plan "Chetna × Theme 5 — Phantom Hand build plan", 7 Oct 2026 (architecture, phases, manifest, risks)
* PRD v1, Project Chetna (original pipeline, protocols, safety limits)
* Team component inventory and return list, 7 Oct 2026
* [Vibrotactile virtual hand illusion — Virtual Reality, 2024](https://link.springer.com/article/10.1007/s10055-024-01052-6)
* [Rubber hand illusion under delayed visual feedback — Shimada et al., PLoS ONE 2009](https://ideas.repec.org/a/plo/pone00/0006185.html)
