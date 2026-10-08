# Electronics team handoff (received 2026-10-08) and consistency check

Source: the electronics team's handoff, pasted by Rudra in the project chat on 2026-10-08. Their text is kept
verbatim in §A; Opus's check against `docs/PH_ELECTRONICS_INTERFACE.md` and contracts v0.2 is §B; the firmware author's
answers of the same evening are §C.

## C. Answers from the firmware author (2026-10-08 18:09, link board lines 2-10)

Read from the firmware source (v0.5.0) by the session that wrote it. "Seen on the wire" is noted where this PC has
checked it itself; everything else is their statement.

| Request (§B) | Answer | What it meant for us |
|---|---|---|
| Where do replies go? (new question) | acks to the sender's source IP **and source port**; `sensor_data` / `sensor_chunk` to the last sender's IP on the **fixed port 8790** | **A real gap on our side**: the game and the Python tools listened only on the port they sent from, so they would have had acks and no IMU, no EMG. Fixed in software (`TelemetryHub` in the SDK, the tools send from port 8790); contracts HAPTIC_PROTOCOL v1.3 row "Telemetry port". The twin's team dialect did not model this, which is why every simulated run was green |
| 4. One listener or several? | one: the last sender. Node A switches on any datagram that parses as JSON, Node B on any datagram; a keepalive counts. Node A stops its motors after 2 s without a packet, Node B forgets its peer after 5 s | as assumed; the game's 1 Hz keepalive to both nodes covers both limits |
| 1. `sensor_chunk.timestamp_ms` | device `millis()` when the 4th value was stored: the **last** value, about 30 ms after the first | no change needed in the game: it places a chunk by its arrival and counts back from the last sample, so both conventions give the same session times. Documented in the contract and the schema |
| 2. Unknown extra fields | ignored on `stop` and `display`. A packet with a `motor` key is played as a cue whatever its `type`; the v1 cue form without `motor` is ignored without an ack; a float intensity (0.6) is cast to 0 and rejected | the Phantom Hand scene only sends the device form (integer intensity, `motor` key), `stop`, `display`, `keepalive`, `subscribe`, `ping`: none of them carries a `motor` key by accident |
| 3. `status`, `emg_burst` | neither node sends them. Only `device_discovery`, `sensor_data` (A), `sensor_chunk` (B) and `ack` | as assumed: "connected" from packet age, the flinch from the envelope itself |
| (their note) EMG baseline | the envelope's resting level moves with the supply and the electrode cable: about 395 on laptop USB with the cable out, about 695 on the bank, about 12 with the cable in and the pads loose | calibrate rest and MVC per person with the pads on (the game's calibration step does); never compare raw levels across power sources |
| (their test) motors next to the EMG | both boards on one bank, motors stroking, no electrodes: envelope mean 699 sd 27.6 idle, 694 sd 14.1 with motors | no motor artefact seen without a person; on a person not checked yet |

## B. Consistency check (Opus, 2026-10-08)

Overall: **compatible in substance** (ports 8790/8791, discovery form, stroke command shape, cap 150, 50–400 ms,
100 ms gap, 50 % duty/10 s, 2 s watchdog, Unity owns all timing incl. the 600 ms async delay, raw-ADC envelope).
Nothing has been proven over Wi-Fi yet (their table: not tested). Differences and who changes:

| # | Topic | Contract / our side | Their firmware | Who changes |
|---|---|---|---|---|
| 1 | Node A device_id | `SLEEVE_001` (03-SPEC D7) | `CHETNA_HAPTIC_001` | **Us**: match by `device_kind`, accept any id; twin default → `CHETNA_HAPTIC_001` |
| 2 | Ack | v1 `{ack_id, ok}` or `{cue_id, status}` | `{type:"ack", device_id, cue_id, accepted: bool}` | **Us**: HapticClient + schema accept `accepted` |
| 3 | Keepalive | `ping` + `subscribe` each second | `{"type":"keepalive"}` | **Us**: also send `keepalive` each second (harmless to both) |
| 4 | Telemetry fan-out | up to 3 subscribers (D3) | only the last sender | **Us**: the laptop live plot reads via the hub (`--hub`, D4), never directly; optional **request** to them for D3 |
| 5 | Display | `{type:"display", text}` | `{type:"display", mode}` | **Us**: send both `text` and `mode` |
| 6 | Node B chunk | 10 values every 100 ms | 4 values, 25 packets/s | none: schema allows 1–10; **request**: confirm `timestamp_ms` = first sample |
| 7 | IMU | accel + gyro | accel only | none (gyro optional) |
| 8 | Status / emg_burst | status 1 Hz; emg_burst from Node B | not mentioned | **Us**: "connected" from packet age (already), bursts computed on our side (analytics already recomputes); **request**: say if they exist |
| 9 | Legacy hello | `{opus_haptic:1}` merged in discovery | absent | none (DiscoveryHub parses the PRD form) |
| 10 | Stop | v1 `{v,type:"stop",id,ts_ms}` | `{"type":"stop"}` | **request**: confirm extra fields are ignored; else we send the bare form |
| 11 | A→B gap | motor_soa_ms 100 | their tool uses 80 | tune on the bench (manifest param) |
| 12 | OLED wiring | I2C 0x3C (PRD §8.1) | SPI (18/23/27/5/4) | none for software; PRD pin map is out of date |
| 13 | Power | one bank for both nodes | each board own supply, grounds not joined | none for software (safer for EMG) |

### Requests back to the electronics team
1. Confirm `sensor_chunk.timestamp_ms` is the device time of the **first** value in `emg_envelope`.
2. Confirm Node A ignores unknown extra fields (e.g. `v`, `id`, `ts_ms` on `stop`, `text` next to `mode` on `display`).
3. Do the nodes send any `status` message or `emg_burst`? If yes, paste one example of each.
4. Optional, nice to have: stream to up to 3 recent senders (03-SPEC D3) so a laptop can watch without stealing the stream from the Quest. Not required: our laptop plot can read through the hub.
5. After the Wi-Fi test: run `python tools/demo/node_probe.py <ip> 8790` against each node and send the output, plus the §5 measurement table of PH_ELECTRONICS_INTERFACE.md.

## A. Their handoff (verbatim)

> Chetna "Phantom Hand": electronics handoff for the Unity / Quest side. Team Kela, Vedanta Makeathon (7-9 Oct 2026). Updated 8 Oct 2026.

**What is built and verified**

| Item | Status |
|---|---|
| Node A (haptics + IMU + OLED) wired, flashed with `node_a_haptic` v0.5.0 | Verified on the bench over USB: both motors spin on a serial command, MPU6050 found at I2C 0x68, OLED shows status |
| Node B (BioAmp EMG) wired, flashed with `node_b_bio` v0.5.0 | Partly verified: the signal chain reacts to touching the electrode leads. Not yet tested with electrodes on a person |
| Wi-Fi / UDP between the nodes and another device | Not tested yet. The Wi-Fi credentials are still placeholders in both sketches |
| Everything powered from the power bank together | Not tested yet |

Treat the protocol below as what the firmware is written to do, not as something already proven over the network.

**Hardware.** Two separate nodes, each on its own ESP32 dev board (micro-USB, 5 V), each with its own supply. Their grounds are not joined.

Node A: haptics, IMU, OLED (`device_id` `CHETNA_HAPTIC_001`): Motor A GPIO25, Motor B GPIO26 (via 1.5 kΩ to 2N2222 base); I2C SDA/SCL 21/22 (MPU6050 only); OLED 7-pin SPI: SCK 18, MOSI 23, DC 27, CS 5, RES 4. Two ERM motors on the 5 V rail, each switched by a 2N2222 with a 1N4007 flyback diode (stripe to 5 V) and a 100 µF capacitor. MPU6050 at 0x68 from 3V3. Motor PWM capped at 150/255 in firmware.

Node B: EMG (`device_id` `CHETNA_BIO_001`): BioAmp EXG Pill from 3V3 (not 5 V), OUT to GPIO34 (ADC1, input only), 100 µF across 3V3/GND. Samples at 1 kHz, band-pass 74.5-149.5 Hz, rectifies, ~50 ms envelope, streams at 100 Hz. Output is a raw ADC envelope (0-4095, 12-bit), not mV; noise floor a few hundred counts with nothing attached, so Unity should calibrate a per-person baseline and max contraction.

Boards: Node A MAC a0:b7:65:63:83:5c, USB serial 567E037716; Node B MAC 8c:94:df:aa:57:f4, USB serial 5C3D082364.

**Safety rules (keep in the demo).** Node B never on a laptop USB port or charger while electrodes are on a person; in the demo it runs from the power bank only. Never power a board from the bank and USB at once. Bank stays on the table, not on the forearm. Disposable electrodes, with consent. Firmware motor limits: pulses 50-400 ms, min gap 100 ms, 50 % duty over any 10 s, 2 s watchdog; do not bypass from Unity.

**Network (UDP, JSON, 2.4 GHz only; a phone hotspot works).** 8790 = commands to a node and the node's replies + sensor stream back; 8791 = discovery broadcast from each node once per second.

Discovery: `{"type":"device_discovery","device_id":"CHETNA_HAPTIC_001","device_kind":"haptic","firmware_version":"0.5.0","command_port":8790,"status":"available","motor_count":2,"timestamp_ms":123456}`. `device_kind` is `haptic` (Node A) or `bio` (Node B, `motor_count` 0). The sender's IP address is the node's address.

Telemetry: a node streams only to the IP of whoever last sent it a packet; send a keepalive to each node about once a second. Telemetry comes back on UDP 8790.
Node A, 100 Hz: `{"type":"sensor_data","device_id":"CHETNA_HAPTIC_001","timestamp_ms":123456,"sensors":{"imu_accel_x":{"value":0.12,"unit":"m/s2","status":"ok"},"imu_accel_y":{"value":-0.05,"unit":"m/s2","status":"ok"},"imu_accel_z":{"value":9.81,"unit":"m/s2","status":"ok"}}}`
Node B, 25 packets/s with 4 values each: `{"type":"sensor_chunk","device_id":"CHETNA_BIO_001","device_kind":"bio","timestamp_ms":123456,"sample_rate_hz":100,"emg_envelope":[301.2,303.5,299.8,310.1],"unit":"raw_adc","status":"ok"}`

Commands to Node A (8790): keepalive `{"type":"keepalive"}`, stop `{"type":"stop"}`, cue `{"cue_id":"stroke_17","motor":0,"intensity":150,"duration_ms":200,"pattern":"pulse"}` (motor 0/1 = brush positions 1/2, A then B about once per second; intensity capped at 150; duration 50-400; pattern `pulse` or `buzz`). Ack to the sender: `{"type":"ack","device_id":"CHETNA_HAPTIC_001","cue_id":"stroke_17","accepted":true,"timestamp_ms":123500}`; `accepted` false if the motor is still running a previous pulse, the 100 ms gap has not passed, the 10 s duty budget is used up, or the motor/intensity is invalid. OLED text: `{"type":"display","mode":"SYNC"}`.

Timing: `play_at_ms` is ignored; a cue starts on arrival, so Unity controls timing. Expect Wi-Fi latency (a few to tens of ms, occasional spikes). The async 600 ms delay is produced on the Unity side by delaying the cue.

Unity side must: 1) listen on 8791 and keep device_id -> IP; 2) keepalive each node ~1/s; 3) per stroke send motor 0 then motor 1, once per second (their test tool uses an 80 ms gap, to tune against brush speed); 4) async: delay cues by 600 ms; 5) read Node B's envelope with a per-person baseline. Laptop test tool: `chetna_udp_tool.py` (discovery, single pulses, strokes, live watch).

Open items: Wi-Fi credentials are placeholders; EMG on a person untested; both nodes from one bank + motor noise on EMG untested; `play_at_ms` not implemented.
