# Firmware prompts (ph-builder, Sonnet) — code only; humans flash (05-ELECTRONICS-TEAM.md)

Every prompt starts with: "Read CLAUDE.md, docs/agent-briefs/ph/PRD_v2.md (§8–§11), 02-RULES.md (§3 Firmware,
§4 Safety), 03-SPEC.md (§0, §3, §4), contracts/HAPTIC_PROTOCOL.md v1.2, and firmware/opus_sleeve/opus_sleeve.ino
v0.4.0 completely, including its header (two dialects on purpose)." Owns firmware/**. No credentials in files.
Both prompts run tonight (Oct 7) in parallel; they share no files.

---

## F1 — Node A v0.5.0 (extend v0.4.0)  · Oct 7 · branch ph/f1-node-a · timebox 2 h

```text
Edit firmware/opus_sleeve/opus_sleeve.ino in place (it is already ESP-WROOM-32). Keep every v0.4.0 safety
behaviour and both dialects. Changes:
1) MOTOR_COUNT 2 on GPIO25 (A) / GPIO26 (B); MOTOR_CHANNELS stays 4 for addressing; routing of 2/3 → 0 kept.
   MAX_INTENSITY 150 (3 V motors on the 5 V rail, PRD §8.2). FIRMWARE_VERSION "0.5.0".
2) Watchdog (PRD FR-FW-04): if no command OR keepalive (ping/subscribe/display also count) for 2000 ms while
   any motor is active, all outputs → 0. Keep the existing per-motor 400 ms ceiling.
3) Subscribers (03-SPEC D3): up to 3 entries {ip, port, lastSeenMs}; `{"type":"subscribe"}` adds/refreshes;
   expiry 5 s; any command/ping also refreshes its sender. sensor_data/status/acks go to every live subscriber
   (acks only to the sender). Replace sendPeer accordingly; status reports `subscribers`.
4) MPU6050 at 100 Hz (SENSOR_PERIOD_MS 10) in the existing sensor_data v1 form; skip the temperature field to
   keep packets small; one sample per packet; timestamp_ms per packet.
5) OLED SSD1306 (0x3C, shared I2C 21/22): line 1 device id + fw; line 2 IP or "NO WIFI"; line 3 large text from
   the last `{"type":"display","text"}` (≤ 12 chars, default "IDLE"); line 4 "LINK" + subscriber count, or
   "NO LINK". Refresh ≤ 5 Hz, never blocking the motor loop (render with Adafruit_SSD1306, display() only when
   changed). OLED absent → firmware runs without it and says so at boot.
6) Discovery 1 Hz: new PRD §9.1 form (type device_discovery, device_kind "haptic", firmware_version, command_port,
   status, motor_count 2, timestamp_ms) AND the legacy {"opus_haptic":1,...} hello. battery_pct null in status.
7) Bench self-test (hold BOOT at reset, or Serial command `selftest`): pulses A then B 3× at intensity 150/200 ms,
   prints IMU/OLED found. Serial command `soa <ms>`: plays 10 strokes (A then B after <ms>) once per second so the
   wearer can choose motor_soa_ms. Serial command `scan`: I2C scan.
8) Accept `cue:"stroke"` and ignore `play_at_ms` (the game times sends). Unknown types ignored with a counter.
Verify: arduino-cli core install esp32:esp32; arduino-cli lib install "ArduinoJson" "Adafruit MPU6050"
"Adafruit Unified Sensor" "Adafruit SSD1306" "Adafruit GFX Library"; arduino-cli compile --fqbn esp32:esp32:esp32
firmware/opus_sleeve → paste output (0 warnings in our file). Also run python sim/haptic tests that target the
firmware contract if any. Update the header comment (v0.5.0 changes) and firmware/README.md (flash steps from
05-ELECTRONICS-TEAM.md §6, self-test and soa commands).
```

---

## F2 — Node B bio_node v0.5.0  · Oct 7 · branch ph/f2-node-b · timebox 2.5 h

```text
New firmware/bio_node/bio_node.ino for ESP-WROOM-32:
- Boot banner on Serial: "SAFETY: power bank only. Never connect to a laptop or charger while electrodes are
  on a person." Also sent once in the first status message.
- ADC on GPIO34 (ADC1) at exactly 1 kHz via a hardware timer ISR that only stores samples in a ring buffer
  (analogRead outside the ISR is fine if you use a task; never block Wi-Fi). 12-bit, attenuation for 0–3.3 V.
- Processing at 1 kHz: DC removal; EMG band-pass 74.5–149.5 Hz (port Upside Down Labs' published EMG filter for
  1 kHz sampling if its licence allows, otherwise design an equivalent 4th-order Butterworth band-pass and put the
  coefficients and the Python that generated them in firmware/bio_node/filter_design.py); full-wave rectify;
  10 Hz low-pass envelope; decimate to 100 Hz.
- Every 100 ms send PRD §9.3 sensor_chunk to all subscribers: {type:"sensor_chunk", device_id:"CHETNA_BIO_001",
  device_kind:"bio", timestamp_ms (time of the first sample), sample_rate_hz:100, emg_envelope:[10 values],
  unit:"raw_adc", status:"ok"|"saturated"|"flat"}.
- emg_burst: baseline = running median + MAD over quiet 2 s windows; burst when env > baseline + 3 SD for ≥ 30 ms
  (refractory 300 ms) → {type:"emg_burst", device_id, timestamp_ms, peak, baseline_rms}.
- Subscribers exactly like F1 (max 3, subscribe/ping refresh, 5 s expiry). Discovery 1 Hz PRD §9.1 (device_kind
  "bio", motor_count 0, firmware_version "0.5.0"). Status 1 Hz (battery_pct null, subscribers, samples dropped).
- Never drives any output pin from a network message. Ignores motor commands (ack rejected NOT_A_HAPTIC_NODE).
- Serial command `raw` toggles a 1 kHz raw print for Serial Plotter (bench only); `stats` prints rate/drops.
Host test (firmware/bio_node/test/, g++ or Python reimplementation of the same coefficients): gain at 20, 75, 100,
150, 300 Hz; envelope reaches 90 % of a step burst in < 40 ms; burst detector fires on a synthetic flinch and not
on noise. arduino-cli compile --fqbn esp32:esp32:esp32 firmware/bio_node → paste output. firmware/README.md:
flashing, electrode placement, safety warning, how to check with tools/demo/live_plot.py.
```
