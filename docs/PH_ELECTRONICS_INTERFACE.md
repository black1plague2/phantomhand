# Phantom Hand — what the sleeve nodes must do (interface for the electronics team)

**Authority:** contracts v0.2, PRD v2 (PRD_v2.md), spec v3.1 (docs/agent-briefs/ph/03-SPEC.md). Firmware v0.5.0 on both nodes (F1 Node A, F2 Node B).

This document defines what the firmware team's Node A and Node B boards must send and accept over the network. The software side (Unity Quest app, Flutter hub, analytics) is owned by the Phantom Hand team and speaks these exact wire formats. The electronics team owns the boards, motors, IMU, OLED, EMG sensor and the ESP32 firmware.

---

## 1. Scope: who owns what

| Component | Owner | Notes |
|---|---|---|
| **Hardware** | Electronics team | 2× ESP-WROOM-32 dev boards, motors, IMU (MPU6050), OLED (SSD1306), BioAmp EXG Pill, power bank, all wiring |
| **Firmware** | Electronics team | Arduino-ESP32 core 3.x; Node A extends v0.4.0 to dual-motor v0.5.0; Node B is new bio_node firmware v0.5.0 |
| **Wire protocol** | Shared (contracts) | JSON over UDP, ports 8790 (commands), 8791 (discovery). Schemas in `contracts/schemas/haptic-message.schema.json` and `haptic-device-command.schema.json` |
| **Safety limits & enforcement** | Shared | Firmware implements limits in firmware; software also checks them before sending (defence in depth) |
| **Transport test doubles** | Software team | `sim/sleeve/twin.py` (full two-node simulator, tested against real firmware limits) and `sim/haptic/fake_haptic.py` (v1 only). Hardware is optional at runtime |

[contracts/HAPTIC_PROTOCOL.md §1; docs/agent-briefs/ph/03-SPEC.md §0, D7]

---

## 2. Network

One 2.4 GHz Wi-Fi hotspot (phone, laptop or router). All three devices (Quest, Node A, Node B) connect to the same SSID.

| Function | Port | Direction | Broadcast? | Notes |
|---|---|---|---|---|
| Haptic commands + acks | UDP **8790** | Quest → Node A (acks reply same socket) | No | Stroke, display, ping, subscribe, all commands |
| Discovery | UDP **8791** | Node A + Node B broadcast → all | Yes, or unicast fallback | Both nodes broadcast every **1000 ms** [HAPTIC_PROTOCOL.md v1.2 line 41; PRD_v2.md §9.1] |

**Manual IP fallback (D8):** When a phone hotspot blocks broadcast, the game and app accept static IP host entries (saved in settings). Each board prints its own IP on the serial console at boot.

[HAPTIC_PROTOCOL.md v1.2 lines 32–44; docs/agent-briefs/ph/03-SPEC.md §0 D8; PRD_v2.md §9]

---

## 3. Node A (haptic + IMU + OLED) — SLEEVE_001

**Firmware:** v0.5.0 (extends v0.4.0). Device kind: **haptic**. Motor count: **2**. Battery: **null** (powered from bank).

### 3.1 Hardware pinout

| Signal | Pin | Device | Notes |
|---|---|---|---|
| Motor A drive | **GPIO25** | 2N2222A (1.5 kΩ base) | via 1.5 kΩ to transistor base; see 8.2 motor driver circuit |
| Motor B drive | **GPIO26** | 2N2222A (1.5 kΩ base) | same as above |
| I2C SDA | **GPIO21** | MPU6050 (addr **0x68**) + OLED (addr **0x3C**) | shared bus, 3.3 V |
| I2C SCL | **GPIO22** | both I2C devices | 3.3 V |

Avoid GPIO 0, 2, 12, 15 (boot-strapping) and 6–11 (flash).

[PRD_v2.md §8.1; docs/agent-briefs/ph/03-SPEC.md §3]

### 3.2 Motor driver circuit (per channel)

```
GPIO (3.3 V) ──1.5 kΩ──► 2N2222A base
             emitter ──► GND
            collector ──► motor (−)
            motor (+) ──► board 5 V rail
     1N4007 flyback: stripe (cathode) ──► motor (+), other end ──► collector
  100 µF cap: (+) ──► 5 V, (−) ──► GND (right next to transistor)
```

**Base current:** ~1.7 mA (enough for a coin ERM). **If motor feels weak**, add a second 1.5 kΩ in parallel (~750 Ω, ~3.5 mA).

**PWM:** 2 kHz, 8-bit. **Firmware caps intensity at 150/255** to keep the 3 V motor's average voltage near 3 V on a 5 V rail. Software sends **round(150 × haptic_max_intensity)**, which never exceeds 150.

[PRD_v2.md §8.2, §8.4; docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md §3, §8.2]

### 3.3 Incoming messages (all on UDP 8790)

#### Stroke command
Sent by the game during each brush stroke. One example JSON object per datagram:

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

| Field | Rule | Example | Notes |
|---|---|---|---|
| `motor` | **0** or **1** (only) | 0 = motor A (wrist), 1 = motor B (elbow) | Reject with error code `INVALID_MOTOR` if 2 or 3 |
| `intensity` | **0–255** wire range; firmware **must cap at 150** | 150 (never higher) | Software sends round(150 × haptic_max_intensity), default 1.0 → 150 |
| `duration_ms` | **50–400 ms** (strict) | 200 | Stroke always 200 ms; reject if outside range with `INVALID_DURATION` |
| `pattern` | `"pulse"` for strokes | `"pulse"` | Allow `pulse`, `buzz`, `ramp`; log unknown patterns |
| `cue` | `"stroke"` (label only) | `"stroke"` | For logging only; firmware ignores it |
| `cue_id` | echo in the ack | `"stroke_017"` | **Echo this in the ack** so the game can measure ack latency |
| `play_at_ms` | optional, game-side timing info | 143250 | Firmware ignores it; the game times its own send |

**Safety gates (firmware MUST enforce):**
- Minimum gap between commands to the same motor: **100 ms** (start-to-start). Reject `CUE_GAP`.
- Duty cycle: **≤ 50 % over a rolling 10 s per motor**. Reject `DUTY_CYCLE_LIMIT`.
- Watchdog: if nothing (command, ping, subscribe, display) arrives for **2000 ms**, force all motor outputs to 0.

[contracts/HAPTIC_PROTOCOL.md v1.2 lines 47–61; contracts/schemas/haptic-device-command.schema.json; PRD_v2.md §9.2; docs/agent-briefs/ph/03-SPEC.md §4]

#### Display command
Send text to the OLED screen. Up to **2 per second**, max **12 characters** printable ASCII.

```json
{"type": "display", "text": "SYNC"}
```

Set the OLED to show the text or `IDLE` if empty. Use for status (SYNC/ASYNC, link state, or operator messages).

[contracts/HAPTIC_PROTOCOL.md v1.2 line 71]

#### Ping / keepalive
The game sends **once per second** during a session to feed the 2000 ms watchdog.

```json
{"type": "ping"}
```

or the legacy v1 form:

```json
{"v": 1, "type": "ping", "id": "...", "ts_ms": ...}
```

Accept both forms. Ping refreshes the sender's subscriber entry (if it exists) but does NOT auto-subscribe.

[contracts/HAPTIC_PROTOCOL.md v1.2 line 72]

#### Subscribe
Game or a plotting tool subscribes to sensor streams. Send repeatedly at least every **2 seconds** to stay subscribed; expiry is **5 seconds**.

```json
{"type": "subscribe"}
```

Max **3 simultaneous subscribers** per node (D3). Track each by (IP, port).

[contracts/HAPTIC_PROTOCOL.md v1.2 line 70; docs/agent-briefs/ph/03-SPEC.md §0 D3]

### 3.4 Outgoing messages from Node A

#### Discovery (broadcast every 1000 ms to UDP 8791)

```json
{
  "type": "device_discovery",
  "device_id": "SLEEVE_001",
  "device_kind": "haptic",
  "firmware_version": "0.5.0",
  "command_port": 8790,
  "status": "available",
  "motor_count": 2,
  "timestamp_ms": 142050
}
```

Also include the legacy hello fields in the same datagram so old clients work:

```json
{
  "opus_haptic": 1,
  "port": 8790,
  "fw": "0.5.0",
  ...
}
```

Broadcast to UDP 8791 (or unicast to 127.0.0.1:8791 if broadcast is blocked).

[contracts/HAPTIC_PROTOCOL.md v1.2 line 44–45; contracts/schemas/haptic-message.schema.json deviceDiscovery; PRD_v2.md §9.1]

#### Sensor data stream (100 Hz, one sample per packet to UDP 8790 subscribers)

```json
{
  "type": "sensor_data",
  "device_id": "SLEEVE_001",
  "timestamp_ms": 2842,
  "sensors": {
    "imu_accel_x": {"value": 0.05, "unit": "m/s2", "status": "ok"},
    "imu_accel_y": {"value": -0.10, "unit": "m/s2", "status": "ok"},
    "imu_accel_z": {"value": 9.81, "unit": "m/s2", "status": "ok"},
    "imu_gyro_x": {"value": 0.001, "unit": "rad/s", "status": "ok"},
    "imu_gyro_y": {"value": 0.002, "unit": "rad/s", "status": "ok"},
    "imu_gyro_z": {"value": 0.000, "unit": "rad/s", "status": "ok"},
    "imu_temperature": {"value": 31.2, "unit": "C", "status": "ok"}
  }
}
```

Sample the MPU6050 at **100 Hz** (10 ms apart). Include accel (x, y, z in m/s²) and gyro (x, y, z in rad/s). Optionally add temperature. Send only to active subscribers.

[contracts/HAPTIC_PROTOCOL.md v1.2 line 73; docs/agent-briefs/ph/03-SPEC.md §4]

#### Status message (1 Hz to UDP 8790 subscribers; also sent on socket reply to a command sender)

```json
{
  "v": 1,
  "type": "status",
  "id": "...",
  "ts_ms": ...,
  "device_id": "SLEEVE_001",
  "device_kind": "haptic",
  "battery_pct": null,
  "subscribers": 1,
  "motors_ok": true,
  "imu_ok": true,
  "oled_available": true,
  "unknown_msgs": 0,
  "fw": "0.5.0",
  "last_cue_id": "stroke_017",
  "temperature_c": 31.2
}
```

Send the status 1 Hz to all active subscribers **and** reply immediately on the socket to any command sender (to ensure they know the node is alive). Include:
- `device_kind`: `"haptic"`
- `battery_pct`: **null** (powered from the bank)
- `subscribers`: count of live subscribers (0–3)
- `oled_available`: true/false
- `unknown_msgs`: count of unrecognized message types since boot (incremented each time)

[contracts/HAPTIC_PROTOCOL.md v1.2 line 76; contracts/schemas/haptic-message.schema.json status]

#### Ack (reply on the same socket as the command, or to a subscriber after a brief delay)

Firmware v0.5.0 **deliberately sends both dialects merged** in one datagram so the game's two message parsers both work:

```json
{
  "v": 1,
  "type": "ack",
  "id": "...",
  "ts_ms": ...,
  "ack_id": "stroke_017",
  "ok": true,
  "cue_id": "stroke_017",
  "status": "executed",
  "timestamp_ms": 2842
}
```

| Field | When present | Value | Notes |
|---|---|---|---|
| `ack_id` + `ok` | always | echo the `cue_id` from the command; true if the command was accepted and started | v1 dialect |
| `cue_id` + `status` | always | echo the `cue_id`; one of: `"executed"` (command accepted), `"rejected"` (denied by firmware), `"error"` (malformed) | device dialect |
| `error_code` | if `status` is `"rejected"` or `"error"` | one of: `INVALID_MOTOR`, `INVALID_DURATION`, `CUE_GAP`, `DUTY_CYCLE_LIMIT`, `INVALID_JSON` | [sim/sleeve/twin.py line 54] |
| `error_message` | optionally if an error | human-readable string | `"minimum gap 100 ms"` |

Reply **after ~8 ms** (±4 ms jitter is OK; the software measures 100 ms end-to-end). Send to the command's sender (the game Quest).

[contracts/HAPTIC_PROTOCOL.md v1.2 line 77; contracts/schemas/haptic-message.schema.json]

---

## 4. Node B (EMG) — CHETNA_BIO_001

**Firmware:** v0.5.0 (new bio_node). Device kind: **bio**. Motor count: **0**. Battery: **null**.

### 4.1 Hardware pinout

| Signal | Pin | Device | Notes |
|---|---|---|---|
| BioAmp VCC | **3V3** | BioAmp EXG Pill | Run at 3.3 V so OUT cannot exceed the ADC range |
| BioAmp GND | **GND** | BioAmp EXG Pill | Common ground |
| BioAmp OUT | **GPIO34** | ADC1 (input-only) | Never use ADC2 pins (unreliable when Wi-Fi is on) |

**Electrodes:** IN+ and IN− approximately **3 cm apart over the forearm flexor belly** (inner forearm, the muscle that bulges when you squeeze, about a third of the way from elbow to wrist). REF on the bony elbow. Clean skin with an alcohol wipe first.

[PRD_v2.md §8.3; docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md §4, §8.3]

### 4.2 Sampling and processing

- **ADC sampling:** **1 kHz** on GPIO34
- **Band-pass filter:** **74.5–149.5 Hz** (EMG frequency range)
- **Envelope:** rectified and low-passed, streamed at **100 Hz** (one sample every 10 ms)

[PRD_v2.md §8.3; docs/agent-briefs/ph/03-SPEC.md §3]

### 4.3 Incoming messages (on UDP 8790)

#### Subscribe
Same as Node A. Max **3 subscribers**, expiry **5 seconds**.

```json
{"type": "subscribe"}
```

[contracts/HAPTIC_PROTOCOL.md v1.2 line 70]

#### Ping / keepalive
Same as Node A. Optional; Node B does not enforce a watchdog itself, but it respects the 2 s watchdog rule so the system acts as one.

```json
{"type": "ping"}
```

[contracts/HAPTIC_PROTOCOL.md v1.2 line 72]

**Node B MUST IGNORE motor commands** (stroke, display, etc.). Log them as unknown message types.

### 4.4 Outgoing messages from Node B

#### Discovery (broadcast every 1000 ms to UDP 8791)

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

Broadcast every **1000 ms** to UDP 8791.

[contracts/HAPTIC_PROTOCOL.md v1.2 line 41–45; PRD_v2.md §9.1; contracts/schemas/haptic-message.schema.json deviceDiscovery]

#### Sensor chunk (every 100 ms to subscribers)

```json
{
  "type": "sensor_chunk",
  "device_id": "CHETNA_BIO_001",
  "device_kind": "bio",
  "timestamp_ms": 2842,
  "sample_rate_hz": 100,
  "emg_envelope": [420.5, 421.2, 419.8, 422.1, 420.9, 421.4, 420.2, 421.8, 420.1, 421.5],
  "unit": "raw_adc",
  "status": "ok"
}
```

Send every **100 ms** with **10 envelope values** (one per 10 ms at 100 Hz). **`timestamp_ms` is the device clock at the FIRST sample**; sample *i* is at `timestamp_ms + i × 1000 / 100`.

Envelope unit: **raw_adc** (the digitized rectified band-pass signal, 0–4095 for 3.3 V input).

[contracts/HAPTIC_PROTOCOL.md v1.2 line 74; PRD_v2.md §9.3; docs/agent-briefs/ph/03-SPEC.md §5 D5]

#### EMG burst event (to subscribers when detected)

```json
{
  "type": "emg_burst",
  "device_id": "CHETNA_BIO_001",
  "timestamp_ms": 3100,
  "peak": 600.2,
  "baseline_rms": 420.0
}
```

Emit when the envelope exceeds **baseline + 3 × SD for ≥ 30 ms**. **`timestamp_ms` is the burst onset** (the first sample above the threshold).

`baseline_rms`: the envelope baseline (see the simulator: ~420 raw_adc in quiet forearm). `peak`: the maximum envelope value during the burst.

[contracts/HAPTIC_PROTOCOL.md v1.2 line 75]

#### Status message (1 Hz to subscribers; optional reply on command socket)

```json
{
  "v": 1,
  "type": "status",
  "id": "...",
  "ts_ms": ...,
  "device_id": "CHETNA_BIO_001",
  "device_kind": "bio",
  "battery_pct": null,
  "subscribers": 2,
  "motors_ok": false,
  "imu_ok": false,
  "unknown_msgs": 0,
  "fw": "0.5.0"
}
```

Send **1 Hz** to all active subscribers. Include:
- `device_kind`: `"bio"`
- `battery_pct`: **null**
- `subscribers`: count of live subscribers (0–3)
- `motors_ok`: false (Node B has no motors)
- `imu_ok`: false (Node B has no IMU)

[contracts/HAPTIC_PROTOCOL.md v1.2 line 76]

#### Ack (on command socket reply)

When a game or tool sends a `subscribe` or `ping` to Node B, send the same dual-dialect ack as Node A:

```json
{
  "v": 1,
  "type": "ack",
  "id": "...",
  "ts_ms": ...,
  "ack_id": "subscribe_001",
  "ok": true,
  "cue_id": "subscribe_001",
  "status": "executed",
  "timestamp_ms": 2842
}
```

[contracts/HAPTIC_PROTOCOL.md v1.2 line 77]

### 4.5 Safety rules (MUST be in firmware and the runbook)

1. **Node B runs from the power bank only. Never connect it to a laptop, charger or anything plugged into mains while electrodes are on a person.**
2. **Never charge the power bank while Node B is attached.**
3. **Flash Node B with the bank cable unplugged and electrodes off.**
4. Firmware must print a safety warning at boot.
5. Electrodes: disposable only, no broken skin, no pacemakers/implants.

[docs/agent-briefs/ph/02-RULES.md §4.1; PRD_v2.md §11]

---

## 5. Timing and measurement requirements

The game's timing model depends on exact motor and link behaviour. The electronics team MUST measure and report these values.

| Parameter | Default | Range | Who measures | Where to report | Notes |
|---|---|---|---|---|---|
| `motor_soa_ms` | **100** | **60–300** | **Electronics team** (on your test bench, using Node A's SOA mode or the sim) | manifest + lab notes | Motor A→B onset gap; brush speed = 10 cm / motor_soa_ms; both motors must feel like ONE moving stroke. Measured by wearer in SOA mode |
| Motor spin-up (ms) | — | 20–40 (target) | Electronics team | lab notes; fed to `--spinup` in integration | Time from PWM on to vibration visible on IMU. Measured with `tools/demo/run_pipeline.py --hardware --spinup` (being built by the software team; until then, read the IMU jolt after a single pulse with node_probe) |
| `tactile_lead_ms` | **40** | **0–150** | Electronics team (tuned in joint test) | manifest | Delay to send the command early so touch lands in sync with the brush. Tuned by wearer in joint test: 20 SYNC strokes at 0/20/40/60/80 ms; pick "most simultaneous" |
| Ack latency p95 | **< 100 ms** | — | Electronics team (measured on their lab bench) | lab notes; acceptance test | Send → ack round-trip (measured by the game via `cue_id` echo) |
| EMG artefact (motors on) | — | — | Electronics team | lab notes | Measure EMG envelope while Node A pulses next to it; describe any spikes (frequency, amplitude, decay time) |
| Power bank auto-off result | — | must stay on 30 min | Electronics team | sign-off sheet | Run both boards from the bank for 30 min; power bank must not switch off. Measure voltage/current if possible |
| Node A IP address | — | 192.168.x.x (example) | Electronics team | printed on serial console at boot; saved for D8 manual fallback | Needed if hotspot blocks discovery broadcast |
| Node B IP address | — | 192.168.x.x (example) | Electronics team | printed on serial console at boot; saved for D8 manual fallback | Needed if hotspot blocks discovery broadcast |

[docs/agent-briefs/ph/03-SPEC.md §0 D1, D6; docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md §6, §7, §10; PRD_v2.md §10.1, §10.2]

---

## 6. Self-test against the software simulator

The Phantom Hand team provides a full two-node simulator (`sim/sleeve/twin.py`) that implements firmware v0.5.0 behaviour without hardware. Use it to verify your message formats before bringing up real boards.

### 6.1 Start the simulator

```bash
# From the repo root
python sim/sleeve/twin.py --kind both --port-offset 10000 --seed 1 --log test.jsonl
```

The simulator will print to stdout:

```json
{"event":"ready","kinds":["haptic","bio"],"ports":{"haptic":18790,"bio":18792,"discovery":18791,"control":18793},"seed":1,"pid":12345}
```

Ports used (with `--port-offset 10000`):
- **18790**: Node A command/stream
- **18791**: Discovery (broadcast)
- **18792**: Node B stream (only in simulator; real boards both use 8790)
- **18793**: Control (fault injection)

[sim/sleeve/README.md Invocation, Ports]

### 6.2 Probe a node (simulator or real board)

`tools/demo/node_probe.py` subscribes, sends one stroke (`stroke_001`), listens 1.5 s and validates every reply
against `contracts/schemas/haptic-message.schema.json`. Needs Python 3 with `jsonschema` (`pip install -r contracts/requirements.txt`).

```bash
# against the simulator started in 6.1 (Node A on 18790)
python tools/demo/node_probe.py 127.0.0.1 18790
# against your real Node A or Node B (IP from its Serial/OLED)
python tools/demo/node_probe.py <board-ip> 8790
```

Real output against the simulator (2026-10-07):
```
[PASS] ack: {"type": "ack", "cue_id": "stroke_001", "status": "executed", ...}
[PASS] sensor_data: {"type": "sensor_data", "device_id": "SLEEVE_001", ...}
[PASS] status: {"type": "status", "device_id": "SLEEVE_001", "device_kind": "haptic", ...}
received: {'ack': 1, 'sensor_data': 151, 'status': 1}
```
Any `[FAIL]` line names the field that breaks the contract. Node A must show ~150 `sensor_data` in 1.5 s (100 Hz);
Node B must show ~15 `sensor_chunk` (real output against the simulator: `received: {'sensor_chunk': 16, 'status': 1}`) and must never drive a pin for a motor command (ignore it, or answer with a rejected ack `NOT_A_HAPTIC_NODE`).

## 7. Pre-integration checklist

Tick each item before the two-node integration run (Oct 8). Photo this into `logs/sessions/<date>-PH-HW-signoff.md`.

| Check | Node A | Node B | By / time | Notes |
|---|---|---|---|---|
| **Flashed v0.5.0; Serial OK** | ☐ | ☐ | — | Arduino IDE, board "ESP32 Dev Module", ArduinoJson 7, Adafruit libs; baud 115200 |
| **Wi-Fi discovered wirelessly** | ☐ | ☐ | — | Both nodes broadcast discovery on 8791; game/app see them in ~1 s |
| **IMU 0x68 found; OLED 0x3C found** | ☐ | n/a | — | Prints on serial; OLED shows device ID + IP at boot |
| **Both motors felt; parallel resistor needed?** | ☐ | n/a | — | Self-test (hold BOOT at reset) pulses A then B 3×; if weak, add parallel 1.5 kΩ |
| **motor_soa_ms chosen** | ☐ | n/a | — | Serial command `soa 60` ... `soa 300`; wearer picks delay where A→B feels like ONE stroke |
| **Spin-up (ms) measured** | ☐ | n/a | — | `python tools/demo/run_pipeline.py --game phantom_hand --hardware --spinup` |
| **EMG squeeze ≥ 3× baseline** | n/a | ☐ | — | Electrodes on wearer; Node B on bank (not laptop); `python tools/demo/live_plot.py` |
| **EMG with motors running (describe)** | ☐ | ☐ | — | Node A pulsing next to Node B; note any spikes, frequency, decay time |
| **Power bank stays on 30 min** | ☐ | ☐ | — | Both boards running; bank must not auto-off. Measure voltage if possible |
| **Placement: Motor A 5 cm / Motor B 15 cm from wrist crease** | ☐ | n/a | — | Dorsal surface, flat, held by sleeve (not glued). Wires toward elbow |
| **Joint test (1–7) passed** | ☐ | ☐ | — | See 5-item joint test below |
| **Safety briefing (§4.5, Node B only) done** | n/a | ☐ | — | Firmware warning printed; runbook read |

### 7.1 Five-item joint test with software team (~30 min)

1. App/laptop sees **SLEEVE_001** (haptic) and **CHETNA_BIO_001** (bio) in the discovery list within 2 s of boot
2. Phase change shows **SYNC** or **ASYNC** on the Node A OLED (game tells nodes via `display` message)
3. **20 SYNC strokes** feel like motor A then motor B, one smooth stroke toward the elbow, in time with the brush on the Quest screen
4. **Harness latency test:** send→ack p95 < **100 ms** (measured by the game via the `cue_id` echo)
5. **Threat test:** EMG burst + IMU jolt visible on the live Quest status trace after the rock falls

Additional checks:
6. Unplug Node A mid-run → game shows "Sleeve offline"; plug back in → rediscovered within 2 s
7. Laptop live plot and the Quest receive sensor data simultaneously (subscriber list working)

[docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md §8]

---

## 8. Contact and sign-off

**Software team point of contact:** Phantom Hand team (Garv Bansal, lead)

**After bring-up (§6), report to the software team:**
- Chosen `motor_soa_ms` and bench measurement method
- Measured spin-up (ms)
- Whether a parallel 1.5 kΩ resistor was needed
- BioAmp OK at 3.3 V or swapped to Grove EMG (and why)
- EMG noise with motors running (amplitude, frequency, decay time in seconds)
- Node A and Node B IP addresses (for D8 manual fallback if needed)
- Photos of wiring and the sleeve on an arm
- Any deviation from this guide

**All issued parts returned by Oct 9** per the CARF declaration (PRD §7, §14).

[docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md §10; docs/agent-briefs/ph/PRD_v2.md §14, §15]

---

## Appendix: Sources and authority

| Topic | Source | Lines/Section |
|---|---|---|
| Protocol v1.2 overview | contracts/HAPTIC_PROTOCOL.md | v1.2 section, lines 40–82 |
| Device command (stroke) | contracts/schemas/haptic-device-command.schema.json | full schema |
| Wire messages | contracts/schemas/haptic-message.schema.json | full schema; both families |
| Spec decisions (D1–D10) | docs/agent-briefs/ph/03-SPEC.md | §0 decisions table |
| PRD § 8 (hardware), § 9 (protocol), § 10 (requirements), § 11 (safety) | docs/agent-briefs/ph/PRD_v2.md | sections as cited |
| Node pinout, motor driver, EMG setup, bring-up checklist | docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md | sections as cited |
| Safety rules (§4.1–4.5) | docs/agent-briefs/ph/02-RULES.md | §4 safety |
| Simulator and contracts validator | sim/sleeve/README.md, contracts/validate.py | as cited |
| Firmware limits (twin parity) | sim/sleeve/twin.py | constants at top of file (lines 32–76) |

---

**Document date:** 2026-10-07 · **Contracts version:** v0.2 · **Firmware version:** 0.5.0 (both nodes) · **Spec:** v3.1
