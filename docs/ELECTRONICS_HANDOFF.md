> **Current for the Phantom Hand build (Oct 2026): `docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md`.** This file is the older single-sleeve handoff; where they differ, 05 wins.

# Chetna haptic sleeve — Electronics handoff

Hardware, firmware, wiring, mounting and the physical build: **Electronics team**.
Unity, Flutter, the command protocol, session recording and analytics: **software team** (Garv's).

**Contract of record:** `contracts/HAPTIC_PROTOCOL.md` (v1.1) + `contracts/schemas/haptic-device-command.schema.json`.
Where this file and the contract disagree, **the contract wins** and the disagreement is a bug in one of them —
§3 lists the ones open right now. Schema changes are made by Opus only, never unilaterally on either side.

Restructured 2026-09-19 from the Electronics team's build-pipeline draft, reconciled line by line against the
firmware actually in this repo (`firmware/opus_sleeve/opus_sleeve.ino` v0.3.0) and the schemas.

---

## 1. Status at a glance

| Area | State |
|---|---|
| UDP transport, ports 8790 / 8791 | ✅ Settled, implemented both sides |
| Command / ack / status / discovery wire format | ✅ Settled — but see §3.1, the two sides speak **two dialects** and the firmware bridges them |
| Motor 0 (upper arm) | ✅ Built, verified from the PC: 4/4 cues `executed`, ack 25–91 ms |
| Motor 1 (forearm) | ⚠️ **Claimed built in the draft; the firmware in this repo says otherwise** — see §3.3 |
| Power | ✅ USB-only for this prototype, by decision (§7). Battery path deferred to §10 |
| Motors 2 + 3 (scale-up to 4) | ⛔ **Blocked on schema sign-off** — see §9 |
| IMU (MPU6050) | ◐ Wired and streaming raw `sensor_data`; no detection logic, and software does not depend on it |
| `battery_pct`, `temperature_c` | ◐ Hardcoded placeholders — no battery, no thermistors. `TODO(v2)` in firmware |

### Power decision (2026-09-19)
This prototype runs **entirely on USB power**. No battery, charger or regulator is in the current build; the
ESP32 and every motor channel are fed from a USB cable. This is deliberate for the protocol/firmware/driver
stage. The consequence is stated plainly so it is not forgotten: **a USB-tethered sleeve is not yet something a
patient can wear and move freely in**, and it must be revisited before the sleeve is worn during a real VR
session. The battery path is preserved in §10.

---

## 2. The protocol (build against this)

### 2.1 Command — software → sleeve, UDP 8790, one JSON object per datagram
```json
{"motor":0,"intensity":180,"duration_ms":300,"pattern":"pulse","cue_id":"a1b2","cue":"trunk_lean"}
```
| Field | Range | Notes |
|---|---|---|
| `motor` | `0` \| `1` | **0 = upper arm, 1 = forearm** (confirmed). Extension to `0–3` is proposed, not approved — §9 |
| `intensity` | 0–255 | Already scaled by the clinician's ceiling before it leaves the app |
| `duration_ms` | 50–400 | |
| `pattern` | `pulse` \| `buzz` \| `ramp` | **`buzz` is required** — see §3.2 |
| `cue_id` | string, optional | Echo it in the ack; this is how end-to-end latency is measured |
| `cue` | `trunk_lean` \| `low_confidence` \| `success`, optional | Clinical label, for your logging only. Ignore it if you like |

Software guarantees on the wire: never more than **2 commands/second**, never shorter than 50 ms or longer
than 400 ms, intensity always pre-clamped.

### 2.2 Replies — sleeve → software, to the sender's address/port
```json
{"type":"ack","cue_id":"a1b2","ack_id":"a1b2","status":"executed","ok":true,"timestamp_ms":0}
{"type":"status","device_id":"SLEEVE_001","battery_pct":100,"connected":true,
 "motors_ok":true,"imu_ok":true,"fw":"0.3.0",
 "motor_0":{"available":true,"temperature_c":0},
 "motor_1":{"available":true,"temperature_c":0},"timestamp_ms":0}
```
`status` values: `accepted` | `rejected` | `executed` | `error`.
`status` message once per second, or on `{"type":"ping"}`.

### 2.3 Discovery — sleeve → broadcast, UDP 8791, once per second
```json
{"type":"device_discovery","opus_haptic":1,"device_id":"SLEEVE_001","port":8790,"fw":"0.3.0"}
```

### 2.4 Limits the firmware already enforces (these answer questions the draft still listed as TODO)
Read straight out of `opus_sleeve.ino` v0.3.0 — **the Electronics team has already answered these in code**, so
they should be promoted from "TODO" to "confirmed, pending bench re-measurement":

| Limit | Value in firmware |
|---|---|
| Minimum gap between commands | `MIN_CUE_GAP_MS` = **100 ms**, per motor, start-to-start |
| Max continuous on-time | `MAX_CONTINUOUS_ON_MS` = **400 ms** |
| Max intensity | `MAX_INTENSITY` = **230** (~90 % duty; coin motors run hot and loud at 100 % on 5 V) |
| Duty cycle | `DUTY_CYCLE_LIMIT_PCT` = **50 %** over a rolling 10 s window — placeholder, unverified |
| Watchdog | `WATCHDOG_MS` = **2000 ms** — no command for 2 s while vibrating forces a stop |
| Chip temperature | `TEMPERATURE_LIMIT_C` = 70 °C, but `ENFORCE_CHIP_TEMPERATURE` = **0** — the ESP32 die sensor is uncalibrated and reads ≥ 70 °C at idle, so it is reported, not enforced |

---

## 3. Open conflicts — resolve these before the pilot

These are real disagreements between the draft, the contract and the shipped firmware. None of them block the
current 2-motor demo; all of them will bite later.

### 3.1 Two dialects, bridged by the firmware (working as intended, but know it exists)
The Electronics spec and the software contract were written independently. The firmware **deliberately speaks
both**: its ack carries `cue_id` *and* `ack_id`, `status` *and* `ok`; its status carries nested
`motor_0`/`motor_1` *and* flat `motors_ok`/`imu_ok`. Commands are accepted with or without a `"type":"haptic"`
envelope. Keep that dual emission — several software components read one dialect and several read the other.
*(This is also the most likely explanation for the `sleeve_test.py` ↔ `fake_haptic.py` dispatch mismatch the
pipeline track is currently root-causing; that investigation may produce a correction to this section.)*

### 3.2 ⚠️ `pattern` list in the draft is wrong — it drops `buzz` and adds `continuous`
The draft states `pulse | continuous | ramp`. The contract and every implementation say **`pulse | buzz | ramp`**.

- **`buzz` must not be dropped.** It is the waveform of the `success` cue. It is implemented in the firmware
  (25 ms on/off, `opus_sleeve.ino:121`), enumerated in both schemas, and used across the simulator, the demo
  tools and their tests. Removing it silently breaks the reward cue.
- **`continuous` is not in the schema.** The firmware accepts it and maps it to `pulse`
  (`opus_sleeve.ino:238`), so it works on hardware but **fails `contracts/validate.py`**. Either drop it from
  the spec or propose it to Opus as a schema addition — do not leave it in a spec that validation rejects.
- `double_tap` is a third asymmetry: the firmware and `haptic-message.schema.json` support it, the
  device-command schema does not. Software currently sends two separate pulses instead, so this is latent, not
  live.

### 3.3 ⚠️ Motor 1: "done" in the draft, "not fitted" in the firmware
The draft's build pipeline marks step 7 (second motor channel) as **[Done — current build]**. The firmware in
this repo says the opposite:
```c
#define MOTOR_COUNT            1   // fitted motors (motor 1 reserved, not fitted)
#define ROUTE_MOTOR1_TO_MOTOR0 1   // while motor 1 is not fitted, play its cues on motor 0
```
So every forearm cue is currently being felt on the upper arm. One of these is stale. **Before the joint test,
confirm which** — and if the second channel really is wired on the bench, push the firmware update that sets
`MOTOR_COUNT 2` and `ROUTE_MOTOR1_TO_MOTOR0 0`, otherwise the software will believe it is driving two motors
while the patient feels one.

### 3.4 Still genuinely open
- **Motor 2 / motor 3 physical placement** — undecided. Needed before §9 can proceed.
- **Bench-measured** minimum gap and max on-time for the real driver circuit (the §2.4 values are the firmware's
  assumptions, not measurements).
- **A real duty-cycle / thermal limit** before any patient testing. 50 % is a placeholder.
- **`battery_pct` meaning** — deferred with the battery (§10).

---

## 4. What the software guarantees to you

- Cues are **fire-and-forget**. A missing, slow or disconnected sleeve never affects the game or the session —
  the patient just gets the on-screen vignette and audio instead. You cannot break the game by being absent.
- A **clinician-set intensity ceiling** scales everything sent, plus a minimum gap and an automatic `stop` if
  cues stop refreshing. Values you receive are already inside your stated limits.
- **No patient data ever reaches the sleeve.** Commands carry no identifiers.
- Every cue sent is logged to the session file as a `haptic_cue` event and surfaced in the clinician's report,
  including whether it was delivered.

---

## 5. Parts

| # | Part | Spec | In hand | Notes |
|---|---|---|---|---|
| 1 | MCU | ESP32 WROOM | ✅ | USB-powered throughout this build |
| 2 | IMU | MPU6050 | ✅ | I2C: VCC→3.3 V, GND→GND, SCL→GPIO22, SDA→GPIO21 |
| 3 | Vibration motors | 3–5 V ERM, up to ×4 in use | ✅ ×5 | 4-motor scale-up needs **no new motors**; 1 spare left |
| 4 | Motor driver, **per channel** | 1× 2N2222A + 1× 1N4007 + 1× ~1 kΩ | ✅ ×5 each | 4-motor scale-up needs **no new driver parts**; 1 spare set left |
| 5 | Power | USB cable (data-capable) | ✅ | Feeds ESP32 + all motor channels. Check headroom before 4 simultaneous channels — §9 |

---

## 6. Circuit

**One complete, identical driver circuit per motor channel. Channels cannot share a driver.**

```
ESP32 GPIO (PWM) → ~1 kΩ → 2N2222A base
2N2222A emitter  → common ground (shared with the ESP32 and every other channel)
2N2222A collector → motor(−)
motor(+)          → power rail
1N4007 across the motor: cathode → motor(+)/rail, anode → motor(−)/collector
100 µF across the rail, local to each motor/transistor pair
```

- **The flyback diode must be local to its own motor.** The inductive spike collapses in that specific
  winding; a diode on one channel does not protect another channel's transistor.
- **The 100 µF buffers the ~60–100 mA switching transient locally**, so channels switching independently don't
  sag the rail the ESP32 runs from.
- **Why one driver per motor:** the protocol addresses motors individually, each with its own intensity and
  timing. A transistor is a single PWM switch and can represent exactly one motor's state. This is true at 2
  channels and stays true at 4 — the scale-up is a full copy of this circuit per channel, not a bigger version
  of one circuit.

**Power path (this build):** USB → ESP32 onboard regulator → 3.3 V / 5 V pins → breadboard rail → every motor
driver rail. No external regulator needed; USB already supplies clean, stable power.

**GPIO map:** motor 0 = GPIO25, motor 1 = GPIO26, proposed motor 2 = GPIO27, motor 3 = GPIO14. All PWM-capable,
none colliding with the I2C pins (21/22) or boot-strapping pins. LEDC PWM, 2 kHz, 8-bit.

**IMU:** MPU6050 on I2C, independent of the motor circuits, sharing the same ground.

---

## 7. Build pipeline

| # | Step | State |
|---|---|---|
| 1 | ESP32 up on the Arduino toolchain, basic sketch over serial | ✅ |
| 2 | Motor channel 0 (§6) on USB power, proven with a hardcoded pulse | ✅ |
| 3 | UDP listener on 8790: parse command JSON, drive PWM on motor 0 | ✅ |
| 4 | Discovery broadcast on 8791 + ack replies | ✅ |
| 5 | `status` replies with placeholder `battery_pct` / `temperature_c` | ✅ |
| 6 | MPU6050 wired, raw IMU streaming (no detection logic) | ✅ |
| 7 | Motor channel 1 (forearm), both channels driven independently | ⚠️ **disputed — see §3.3** |
| 8 | Full test plan (§8) against the real headset/hub, both channels live | ☐ blocked on 7 |
| 9 | Motor channel 2 driver + firmware array to 3 channels | ⛔ blocked on §3.4 placement |
| 10 | Motor channel 3 driver + firmware to 4 channels | ⛔ blocked on §9 schema sign-off |
| 11 | Re-run the full test plan at 4 channels, **including a current-draw check** with all 4 firing near-simultaneously | ⛔ |

---

## 8. Test plan

1. **Self-test, no software needed:** send one datagram with `netcat` or a Python one-liner; confirm the right
   motor runs for the stated duration.
2. **Against our simulator:** `sim/haptic/fake_haptic.py` speaks this exact protocol, so each side can be proven
   before the other is present.
3. **Joint test (~30 min):**
   1. discovery — the headset finds the sleeve with no IP typing;
   2. one cue of each type per motor — confirm **which** motor fires and how it feels;
   3. `status` / battery read;
   4. disconnect mid-session — confirm the game carries on unaffected;
   5. latency check via `cue_id` echoes.

**Network note:** the sleeve currently sits at 10.179.145.125 on the phone's hotspot, while the PC is on a
different subnet — so a discovery broadcast cannot cross between them. The game has a `hapticManualHost`
setting for exactly this case. Put the sleeve and the headset on the same network for the joint test.

---

## 9. Scale-up to 4 motors — verdict

The draft's own reasoning is sound and is kept here; the decision on the schema is added below.

**Electronically: implementable now, no purchases.** Enough motors, transistors, diodes and resistors are in
hand for 4 channels plus one spare of each, and four free PWM-capable GPIOs exist that don't collide with I2C or
boot-strapping pins (§6).

**Motive for the change:** move from 2 motors chosen by *anatomical location* to 4 chosen by *semantic event*
(success, posture error, …). That is a meaningful design shift and worth doing.

**Power budget: verify, don't assume.** Four ERM motors switching near-simultaneously could draw roughly
250–400 mA on top of the ESP32's ~150–260 mA Wi-Fi-active draw. Likely fine on a 1 A+ USB source, tight on a
bare 500 mA port. Take a multimeter reading once channels 2/3 are wired.

### Proposed schema change
```
motor: integer, 0-3   (was: 0-1)
  0 = upper arm   (existing)
  1 = forearm     (existing)
  2 = TBD         (placement undecided)
  3 = TBD         (placement undecided)
```
Knock-on effects: `INVALID_MOTOR` rejection triggers outside 0–3 rather than 0–1; `status` gains `motor_2` /
`motor_3` objects of the same shape; and any software-side validation, motor pickers or clinician controls that
assume a 2-motor sleeve must be updated.

### Opus decision (2026-09-19) — approved in direction, **deferred in implementation**
The extension is sensible and the widening itself is backward-compatible (no currently valid message becomes
invalid). It is **not being implemented now**, for one reason: the project is in a feature freeze until the
end-to-end prototype demo is running, and this change touches validation, UI and session logging across Unity,
Flutter and the analytics path — exactly the kind of cross-cutting edit that destabilises a demo.

**So:**
- Firmware may implement 0–3 **defensively** (accept and drive motors 2/3 if fitted) so bench work is not
  blocked. That is explicitly sanctioned.
- **Software will not send `motor` > 1**, and the schema stays `enum: [0, 1]`, until this is lifted.
- Lifting it requires: the §3.4 placement decision, then a single Opus change set — `haptic-device-command.schema.json`,
  the `HapticClient` cue→motor mapping, the Flutter devices UI, and `fake_haptic.py` — landed together with
  `contracts/validate.py` green.

Treat 0–3 as **bench-only** until that lands.

---

## 10. Deferred: battery-powered revision

Not part of this build. Kept so the reasoning isn't lost when the sleeve needs to come off the tether.

| Need | Choice | In hand |
|---|---|---|
| Cell + charger | 18650 3000 mAh + TP4056 (C-type, protection IC present) | ✅ both, unused |
| Regulation | **The one missing part.** A boost module (MT3608) into VIN, or a Li-ion-rated LDO (HT7333) into the 3.3 V pin | ❌ |
| Battery sensing | 100 kΩ/100 kΩ divider into an ADC pin, replacing the placeholder `battery_pct` | ✅ resistors, not wired |
| Assembly | Thin flexible wire, heat-shrink, fabric pocket or printed holder | — |

**Why regulation is not optional:** the cell swings 4.2 → 3.0 V. Feeding `VIN`/`5V` through the board's LDO
browns out below ~4.4 V, i.e. across most of the discharge curve. This is the most common failure in this build.

**Mounting:** put the battery on the **upper arm, never the forearm** — a hard cylinder on the forearm shifts
the sleeve and disturbs hand tracking.

**Expected life:** ESP32 on Wi-Fi averages ~80–120 mA and motors only pulse briefly, so a 2500 mAh 18650 is
roughly 20 h — far beyond a session.

---

## 11. Answers owed, both directions

**Electronics → software**
1. §3.3 — is motor 1 actually fitted? If yes, ship the firmware update.
2. §3.2 — confirm `buzz` stays and decide whether `continuous` is dropped or proposed.
3. §3.4 — motor 2 / 3 placement.
4. Bench-measured minimum gap, max on-time, and a real duty-cycle/thermal limit.

**Software → electronics**
1. Motor→location mapping — ✅ confirmed: 0 = upper arm, 1 = forearm.
2. UDP 8790 / discovery 8791 — ✅ confirmed, unchanged.
3. Motor range 0–3 — ⛔ deferred, see §9. Bench-only until the freeze lifts.
4. `battery_pct` — placeholder 100 accepted for this build; real mapping deferred with §10.
