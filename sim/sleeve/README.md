# Sleeve twin-lite (sim/sleeve/) - fake Node A (haptic) + Node B (bio)

Software twin of the two Phantom Hand ESP32 nodes (PRD v2 section 9, 03-SPEC section 4). One process, no
hardware. Stdlib only (asyncio UDP); tests add `jsonschema` + `pytest` (use `sim/haptic/.venv`).

## Invocation (stable - tools/demo drives it exactly like this)

```bash
# from the repo root
python -m sim.sleeve.twin --kind both --port-offset 20000 --seed 1 --log out/twin.jsonl
python sim/sleeve/twin.py --kind haptic|bio|both [--port-offset N] [--seed S] [--log PATH|-]
                          [--ack-latency-ms 8] [--ack-jitter-ms 4] [--spinup-ms 30]
                          [--watchdog-s 2.0] [--no-stdin] [--duration SECONDS] [--host 127.0.0.1]
```

## Ports (base + `--port-offset N`)

| What | Port | Notes |
|---|---|---|
| Node A command/stream (UDP) | 8790 + N | stroke / display / ping / subscribe |
| Discovery (UDP, broadcast + 127.0.0.1) | 8791 + N | both nodes announce here, 1 Hz |
| Node B command/stream (UDP) | 8792 + N | only for `--kind bio|both`; its discovery `command_port` says so. (Real nodes both use 8790 on different IPs; one process needs two ports.) With `--kind bio` alone it still uses 8792 + N. |
| **Control (UDP)** | **8793 + N** | text commands, one per datagram, JSON reply |

Examples: offset 20000 -> A 28790, discovery 28791, B 28792, control **28793**.

## Stdout: JSON lines

First line when everything is bound (use it as the readiness signal, never sleep):
`{"event":"ready","kinds":["haptic","bio"],"ports":{"haptic":28790,"bio":28792,"discovery":28791,"control":28793},"seed":1,"pid":...}`
Later lines: `{"event":"oled","text":"SYNC"}`, `{"event":"control","cmd":"flinch","ok":true}`, `{"event":"exit"}`.
Human log text goes to stderr.

## Control commands (stdin lines AND UDP control port; UDP replies `{"ok":true,"cmd":...}`)

| Command | Effect |
|---|---|
| `flinch` | Node B EMG burst at +120 ms, Node A IMU jolt at +150 ms (each if that kind runs) |
| `squeeze` | sustained EMG burst, ~1 s (forearm squeeze) |
| `off-a` / `on-a` | Node A power off/on: silent (no discovery, acks, stream), still logs what it receives |
| `off-b` / `on-b` | same for Node B |
| `jitter <ms>` | ack latency jitter (+/- ms) |
| `latency <ms>` | ack latency mean |
| `loss <pct>` | drop pct % of inbound commands and outbound acks/stream packets (not discovery) |
| `status` | reply with current state (also on stdout) |
| `quit` | clean exit |

## Behaviour (firmware 0.5.0 parity)

- **Discovery** (1 Hz): `{"type":"device_discovery","device_id","device_kind","firmware_version":"0.5.0","command_port","status":"available","motor_count","timestamp_ms"}`;
  Node A = `SLEEVE_001`, haptic, motor_count 2, plus the legacy hello fields (`opus_haptic`, `port`, `fw`) in the same datagram.
  Node B = `CHETNA_BIO_001`, bio, motor_count 0.
- **Node A commands** (any of: bare device command, `{"type":"haptic",...}`, legacy v1 `cue` envelope): motor 0|1 (2/3 -> `MOTOR_UNAVAILABLE`),
  intensity clamped to 150, duration clamped to 50-400 ms, per-motor start-to-start gap < 100 ms -> `CUE_GAP`, duty > 50 % in any 10 s -> `DUTY_CYCLE_LIMIT` (the firmware's code).
  Ack carries both dialects: `ack_id`+`ok` and `cue_id`+`status` (`executed`/`rejected`), after a configurable latency (default 8 ms +/- 4).
  Spin-up model: motor on at receive + 30 ms (logged as `motor_on_ms`; the IMU shows vibration from then).
- **Watchdog**: after the first command, if nothing (command, `ping`, or `subscribe`) arrives for 2 s, motors are forced off and a `watchdog` event is logged; re-arms on the next command.
- **display** `{"type":"display","text"}` <= 12 chars, <= 2/s -> `oled` event/stdout line; otherwise rejected (counted).
- **Subscribers** (A and B, max 3 each): `{"type":"subscribe"}` adds/refreshes; `ping` from a subscriber refreshes; 5 s expiry. Streams go only to live subscribers.
- **Node A stream**: `sensor_data` v1, 100 Hz, one sample per packet (`imu_accel_x/y/z`, `imu_gyro_x/y/z`, `imu_temperature`; m/s2, rad/s). Quiet arm (+9.81 on z) + noise; motor vibration while a pulse is on; jolt on `flinch`.
- **Node B stream**: `sensor_chunk` every 100 ms, 10 envelope values (100 Hz), baseline ~420 raw_adc, SD ~6; bursts on `flinch`/`squeeze`;
  motor artefact (raised mean + variance) whenever Node A (same process, `--kind both`) pulses. `emg_burst` event when env > baseline + 3 SD for >= 30 ms
  (twin-only convenience: suppressed while a pulse + 50 ms is active, analytics excludes those windows anyway).
- **Log** (`--log`): one JSON object per line: `{"ev":"rx"|"tx"|"stroke"|"event", "node", "t_ms" (wall clock ms), ...}`.
  `stroke` records carry `recv_ts_ms`, `ack_send_ts_ms`, `ack_latency_ms`, `spinup_ms`, `motor_on_ms`, `motor_off_ms`, `intensity_in/applied`, `status`.
  Streams are not logged per packet (counts appear in `status`).

## Tests

```bash
cd sim/haptic && .venv/Scripts/python.exe -m pytest ../sleeve -q      # twin tests (offset 10000+)
cd sim/haptic && .venv/Scripts/python.exe -m pytest -q                 # fake_haptic tests (26)
```

## Implementation notes (S1 run1)

- Files: `twin.py` (core `Twin` with an injected clock + `TwinServer` sockets/threads), `test_twin.py` (38 tests).
  Threads, not asyncio: asyncio.sleep is quantised to ~15 ms on Windows, which would smear 8 ms acks and the 100 Hz stream.
- Extra control commands (fault injection): `stick-a [motor]` / `unstick-a [motor]` latch a motor on after its pulse so the
  2 s watchdog has something to force off (real pulses self-end at <= 400 ms).
- Rejection code for the duty limit is `DUTY_CYCLE_LIMIT` (as in opus_sleeve.ino), not `DUTY_LIMIT`.
- Timestamps: messages carry device milliseconds since the node booted (`on-a`/`on-b` reboot the node); log `t_ms`/`recv_ts_ms` are wall-clock ms.
  `sensor_chunk.timestamp_ms` is the device time of the chunk's LAST sample; `emg_burst.timestamp_ms` is the burst ONSET; `baseline_rms` = the baseline envelope (420).
- Ack timing: `ack_latency_ms` in the stroke record is the scheduled delay (receive -> send); `vibration_start_ms` in the ack = command time (PWM starts now, as in the firmware); the modelled motor spin-up (+30 ms) is in the log's `motor_on_ms` and visible in the IMU.
- Subscriber streams include `status` (1 Hz) and `emg_burst`; commanding a node does NOT subscribe the sender (spec D3).
