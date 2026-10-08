# Chetna Haptic Protocol v1.2 (headset/hub ↔ haptic sleeve and bio node)

Schema: `schemas/haptic-message.schema.json`. Opus-owned contract. **Software side only** (Unity + Flutter + a software fake).
The physical sleeve (tennis arm sleeve + ESP32 + motors + IMU) is built by the Electronics team; this file is the interface they implement.

## Scope split
| Owner | Builds |
|---|---|
| This project (software) | Command protocol, Unity `HapticClient`, trigger logic, Flutter status UI, `sim/haptic/fake_haptic.py`, all tests |
| Electronics team | ESP32 firmware, BLE/Wi-Fi transport on the device, motor drivers, IMU, sleeve assembly |

Haptics are **additive**. The visual vignette and audio cue remain the primary feedback and must work with no sleeve present.

## v1.1 — reconciliation with the Electronics team's spec (2026-09-18)
Their firmware accepts a **device-level** command; ours is a **clinical cue**. Both stay, with a documented mapping. The software sends the device-level form on the wire, so their firmware needs no changes.

**Device command (what firmware receives, their format, unchanged):**
```json
{"motor": 0, "intensity": 0, "duration_ms": 200, "pattern": "pulse"}
```
`motor` 0|1 · `intensity` 0–255 · `duration_ms` 50–400 · `pattern` pulse|buzz|ramp.

**Mapping (implemented in `HapticClient`, tested):**
| Cue | motor | pattern | intensity 0–255 | duration_ms | Notes |
|---|---|---|---|---|---|
| `trunk_lean` | 0 (upper arm) | `pulse` | round(255 × clamp(0.5 + excess_lean_cm/10, 0.5, 0.8) × max_intensity) | 300 | repeated while the lean persists, ≥ 800 ms apart |
| `low_confidence` | 1 (forearm) | `pulse` | round(255 × 0.4 × max_intensity) | 120 | sent twice, 120 ms apart (their `pattern` has no double_tap, so we emit two pulses) |
| `success` | both (0 then 1, 40 ms apart) | `buzz` | round(255 × 0.6 × max_intensity) | 200 | |
Clamps applied before sending: intensity 0–255, duration 50–400 ms (our 20–1000 ms range is narrowed to theirs), ≤ 2 cues/s, watchdog stop.
Our richer cue message stays the **internal** representation and the `haptic_cue` event payload, so analytics and the report keep clinical meaning.

**Transport decision: UDP over Wi-Fi for the prototype, BLE later.**
The Electronics team prefer BLE. ⚠️ Unity on Quest has **no built-in BLE central API**: a BLE link needs an Android plugin (or an app-side bridge), which is a real cost and a known risk from the old Aastheen project. So:
- **v1 (prototype, the demo):** UDP/JSON on the local Wi-Fi network (port 8790, discovery `hello` on 8791). Firmware sends the same JSON over UDP.
- **v2 (optional):** BLE behind the same `IHapticTransport` interface, so no cue/mapping code changes. If the team ships BLE first, they must provide the service + characteristic UUIDs, and a write-without-response characteristic taking the exact JSON above (or a 4-byte binary packing: motor u8, intensity u8, duration_ms u16 LE — cheaper on BLE).
- The sleeve should also work **alone** if the link drops: their optional local IMU lean threshold is welcome and independent of us.

**What we still need from them (in `docs/MANUAL_TODO.md`):** UDP port + discovery payload confirmation (or BLE UUIDs), motor index → body location (which is motor 0 vs 1), a status message with battery %, and their minimum time between commands.

## v1.2 — Phantom Hand (2026-10-07, contracts v0.2)
Adds a two-node sleeve (Node A haptic + IMU + OLED, Node B bio/EMG), the brush "stroke" cue and the messages around it. Everything in v1/v1.1 keeps working. Authority: `docs/agent-briefs/ph/PRD_v2.md` §9 and `03-SPEC.md` §0, §4. Firmware 0.5.0 on both nodes.

**Two message families, one schema.** `schemas/haptic-message.schema.json` is `anyOf` two families. (1) *Internal v1* messages always carry `v:1`, `id`, `ts_ms` (cue, stop, config, ping, status, ack, imu). (2) *Wire* messages as the nodes emit them carry none of those: `device_discovery`, the legacy `{"opus_haptic":1,...}` hello, `sensor_data`, `sensor_chunk`, `emg_burst`, `display`, `subscribe`, `status`, `ack`. Firmware 0.5.0 sends `status` and `ack` with both dialects merged in one datagram, so they match both families. The device command (below) is validated by `haptic-device-command.schema.json`.

**Device ids and kinds.** Node A `SLEEVE_001` (`device_kind:"haptic"`, `motor_count` 2), Node B `CHETNA_BIO_001` (`device_kind:"bio"`, `motor_count` 0). Both broadcast `device_discovery` every 1000 ms to UDP 8791 with `command_port` 8790. Node A's datagram also carries the legacy `opus_haptic`/`port`/`fw` fields, so `HapticClient` works unchanged. A game that wants both nodes keeps one entry per `device_id` (one haptic, one bio). Manual host fields stay available when a hotspot blocks broadcast (D8). Two-node discovery: the game latches the first `haptic` node for commands and the first `bio` node for sensor input.

### Stroke command
Sent to Node A on UDP 8790 (device-level form, no `type`):
```json
{"cue_id":"stroke_017","motor":0,"intensity":150,"duration_ms":200,"pattern":"pulse","cue":"stroke","play_at_ms":143250}
```
| Field | Rule |
|---|---|
| `motor` | 0 = motor A (wrist side), 1 = motor B (10 cm toward the elbow) |
| `intensity` | wire range stays 0–255, but the game sends **round(150 × `haptic_max_intensity`)** (default 1.0 → 150). The firmware caps every value at **150** (3 V coin motors on a 5 V rail) |
| `duration_ms` / `pattern` | 200 ms `pulse` (firmware allows 50–400) |
| `cue` | `"stroke"` (label only; also the value of `haptic_cue.data.cue` in events) |
| `play_at_ms` | optional integer, game clock ms the stroke is meant to land. **Informational**: the firmware ignores it, the game times its own send |
| `cue_id` | `stroke_<index>`; echoed in the ack, so `ack_latency_ms` is measurable |

**Software limits:** at least 250 ms between stroke sends to the same motor and at most 4 stroke sends per second (both nodes' worth). Firmware limits (unchanged): 100 ms minimum gap per motor, 50 % duty over a rolling 10 s per motor, intensity cap 150, watchdog 2000 ms.

**Lead and delay semantics.** A stroke has two brush passes (`brush_pass_a_ms`, `brush_pass_b_ms`). The *intended* cue time for each motor is `brush_pass − tactile_lead_ms (+ async_delay_ms ± 100 ms in ASYNC)`; the game sends the command at that time (`cue_*_send_ms` in the `stroke` event) and `timing_err_ms = actual send − intended`. `tactile_lead_ms` compensates the measured link delay plus motor spin-up. In ASYNC the whole delay is applied to both motors, the A/B order is random (50 %, `swapped:true` when B fired first).

**D1 brush/SOA rule.** `motor_soa_ms` (default 833, range 60–1700; 03-SPEC D16) is the onset gap between motor A and motor B. The visible brush speed between the two motor positions is derived from it: **brush speed = `motor_spacing_cm` / `motor_soa_ms`** (10 cm / 833 ms = 12 cm/s; 100–130 ms is the fast flick, about 1 m/s), and the rest of the stroke (wrist → A, B → elbow) runs at the same speed, so sight and touch stay in step. `stroke_rate_hz` sets how often a stroke starts, `stroke_jitter_ms` adds random spread.

### Other messages
| Message | Direction | Shape and rules |
|---|---|---|
| `subscribe` | any → node A or B | `{"type":"subscribe"}`, resend at least every 2 s. A subscriber expires after 5 s; at most 3 per node (D3). `sensor_data`, `sensor_chunk`, `emg_burst` and `status` go to every live subscriber; acks go only to the sender of the command. Every command also refreshes its sender |
| `display` | game → Node A | `{"type":"display","text":"SYNC"}`; printable ASCII, **≤ 12 characters**, **≤ 2 per second**; empty shows `IDLE`. The game sends `SYNC`/`ASYNC` per condition |
| keepalive | game → Node A | the existing `ping`, **once per second during a session**; it feeds the 2000 ms watchdog (ping, subscribe, display, stop, config and status_request all do; unknown or unparseable messages do not) |
| `sensor_data` (A) | A → subscribers | 100 Hz, one sample per packet: `{"type":"sensor_data","device_id","timestamp_ms","sensors":{"imu_accel_x":{"value","unit":"m/s2","status":"ok"}, … "imu_gyro_x/y/z" (rad/s), optionally "imu_temperature"}}`. Consumers must tolerate extra sensor names |
| `sensor_chunk` (B) | B → subscribers | every 100 ms: `{"type":"sensor_chunk","device_id","device_kind":"bio","timestamp_ms","sample_rate_hz":100,"emg_envelope":[… ≤10 values],"unit":"raw_adc","status":"ok"}`. **`timestamp_ms` is the device clock at the FIRST sample**; sample *i* is at `timestamp_ms + i × 1000 / sample_rate_hz` |
| `emg_burst` (B) | B → subscribers | `{"type":"emg_burst","device_id","timestamp_ms","peak","baseline_rms"}` when the envelope exceeds baseline + 3 SD for ≥ 30 ms. **`timestamp_ms` is the burst onset**; `peak` and `baseline_rms` are in envelope units (`raw_adc`) |
| `status` | node → subscribers | existing fields plus `device_kind`, `subscribers` (0–3), `battery_pct` (**null**: powered from a bank), and on Node A `oled_available`, `unknown_msgs` (ignored message types since boot) |
| `ack` | node → sender | both dialects in one packet: `cue_id`+`status` (`accepted`/`executed`/`rejected`/`error`)+`timestamp_ms` and `ack_id`+`ok`. Rejections carry `error_code` (for example `DUTY_CYCLE_LIMIT`, `CUE_GAP`, `INVALID_MOTOR`) and `error_message` |

**Time mapping.** Device `timestamp_ms` is the node's own `millis()`. The game maps it to session time once at subscribe (and every few seconds) and writes session-clock `t_ms` into `sens_###.json`. EMG samples within a motor pulse plus 50 ms are excluded by analytics (FR-AN-01), using the stroke `cue_*_send_ms` and acks.

**Safety (also enforced in firmware):** pulse 50–400 ms, 100 ms gap, 50 % duty per 10 s, intensity ≤ 150, 2000 ms watchdog. Node B never touches a laptop or charger while electrodes are on a person (PRD §11).

**Session files.** Node data is recorded as `sens_###.json` (`schemas/sensor-file.schema.json`: columnar `emg_env` and `imu` streams). Events: see `schemas/event.schema.json`.

## v1.3 — the electronics team's firmware dialect (2026-10-08, contracts v0.2.1)
The boards run the electronics team's own sketches (`node_a_haptic`, `node_b_bio` v0.5.0), not `firmware/opus_sleeve`. Source: `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` §A (their text) and §B (the differences). The game speaks both dialects at once; all additions are backward compatible.

| Topic | Rule |
|---|---|
| Node ids | Node A is `CHETNA_HAPTIC_001` on the real boards (`SLEEVE_001` in the reference firmware and older fixtures). The game matches a node by **`device_kind`** (`haptic` / `bio`), never by id; a discovery datagram without a kind is a legacy haptic node |
| `ack` | `{"type":"ack","device_id","cue_id","accepted":true|false,"timestamp_ms"}`. **`accepted:false` = the cue was not played** (motor still running, 100 ms gap, duty budget used, invalid motor/intensity): record it as `delivered:false`. Precedence when several fields are present: `ok`, then `accepted`, then `status` |
| `keepalive` | `{"type":"keepalive"}` to **each** node once per second, in addition to `ping` and `subscribe`. It feeds the 2 s watchdog and tells the node where to stream |
| `display` | the game sends both fields with the same value: `{"type":"display","text":"SYNC","mode":"SYNC"}` (their firmware reads `mode`) |
| Telemetry target | their firmware streams to the **last sender only** (no 3-subscriber list). While the game runs, no other tool may send to the nodes (`chetna_udp_tool.py`, `node_probe.py`, `live_plot.py` without `--hub`): it would take the stream away from the headset. A laptop watches through the hub |
| `sensor_chunk` | 4 values per packet, 25 packets per second (schema allows 1–10 values) |
| `sensor_data` | accelerometer only (gyro optional) |
| `stop` | the game keeps sending the v1 form; the bare `{"type":"stop"}` is what their firmware documents. Open request to the team: confirm extra fields are ignored |

## Transport
- v1 transport: **UDP datagrams, JSON, port 8790**, headset → sleeve; the sleeve answers on the same socket. Chosen because it is connectionless (a dropped sleeve never stalls the game loop), tiny, and implementable on an ESP32 in a few hundred lines.
- Discovery: the sleeve broadcasts `hello` every 1 s to UDP 8791 (`{"opus_haptic":1,"device_id":…,"port":8790,"fw":"…"}`); the game listens and latches the first device, or takes a manual host from settings. A BLE transport may be added later behind the same `IHapticTransport` interface; no message changes.
- Everything is fire-and-forget except `status`. The game must never block on a haptic write, and a missing sleeve is not an error.

## Messages (game → sleeve)
```json
{"v":1,"type":"cue","id":"<uuid>","ts_ms":1737… ,"cue":"trunk_lean","intensity":0.6,"duration_ms":300,"pattern":"pulse","zone":"upper_arm","repeat":2,"gap_ms":120}
{"v":1,"type":"stop","id":"…","ts_ms":…,"zone":"all"}
{"v":1,"type":"config","id":"…","ts_ms":…,"max_intensity":0.8,"enabled":true}
{"v":1,"type":"ping","id":"…","ts_ms":…}
```
Sleeve → game:
```json
{"v":1,"type":"status","id":"…","ts_ms":…,"device_id":"sleeve-01","battery_pct":82,"motors_ok":true,"imu_ok":true,"fw":"0.1.0","last_cue_id":"…","temperature_c":31}
{"v":1,"type":"ack","id":"…","ts_ms":…,"ack_id":"<cue id>","ok":true}
{"v":1,"type":"imu","id":"…","ts_ms":…,"seq":12,"ax":…,"ay":…,"az":…,"gx":…,"gy":…,"gz":…}   // optional, ≤50 Hz, placeholder for the Electronics team
```

## Cues (the only three clinical triggers in v1)
| cue | Fires when | Pattern | Purpose |
|---|---|---|---|
| `trunk_lean` | Head displacement toward the target exceeds `trunkLeanWarningCm` for > 300 ms during a reach | `pulse`, intensity 0.5–0.8 scaled by excess lean, repeat while the lean persists (min gap 800 ms) | Tells the patient to sit back; mirrors the visual vignette |
| `low_confidence` | Hand tracking confidence low or `tracking_lost` for > 500 ms | `double_tap`, intensity 0.4 | "Bring your hand back into view" |
| `success` | `trial_end` with outcome `success` (fruit placed) | `buzz`, intensity 0.6, 200 ms | Reward |

Rules: at most one cue per 800 ms per zone; `success` preempts others; `stop` is sent on pause, session end, and app quit. Intensity is always clamped by the `config.max_intensity` the clinician set.

## Safety and privacy
- Hard caps in software: intensity ≤ 1.0, duration ≤ 1000 ms, ≤ 2 cues/second sustained; a watchdog `stop` if no cue is refreshed within 2 s.
- The sleeve never receives patient identifiers. Cue messages carry no PII.
- Clinician control: haptics can be switched off per program (`hapticsEnabled`), with an intensity ceiling; off is the default until the hardware exists.

## Session record
Every cue sent is also written to `events.ndjson` as a `haptic_cue` event (with `cue`, `intensity`, `delivered` true/false), so analytics and the clinician report can count cues, relate them to lean episodes, and show whether the sleeve was connected. No new session files.

## Testing without hardware
`sim/haptic/fake_haptic.py` implements the sleeve side: answers `ping`, sends `status` at 1 Hz, logs every cue with timing, optionally emits fake IMU, and can simulate disconnect/battery-low. All Unity and Flutter tests run against it.
