# Sim prompts (ph-builder, Sonnet)

Every prompt starts with: "Read CLAUDE.md, docs/agent-briefs/ph/PRD_v2.md §9, 01–04, sim/haptic/README.md +
fake_haptic.py, sim/live/README.md, tools/demo/** (run_pipeline.py, sleeve_test.py, watch_and_analyse.py)."
Owns sim/** and tools/demo/**.

---

## S1 — Twin-lite: fake Node A + Node B  · Oct 7 (MVP) · branch ph/s1-twin · timebox 2 h

```text
Extend sim/haptic/fake_haptic.py (keep its CLI and tests working) or add sim/sleeve/twin.py reusing it:
- --kind haptic (default) | bio | both; --port-offset N (cmd 8790+N, discovery 8791+N); --seed.
- Discovery in the PRD §9.1 form (+ legacy hello for haptic) with device_kind, ids SLEEVE_001 / CHETNA_BIO_001.
- Haptic: accepts stroke commands (cue "stroke", intensity cap 150, firmware limits identical to v0.5.0:
  50–400 ms, 100 ms gap, 50 % duty/10 s, 2 s watchdog incl. keepalive), acks in both dialects with a configurable
  latency (default 8 ms ± 4) + spin-up model (30 ms) recorded in its log; display messages printed as an OLED line;
  IMU sensor_data v1 at 100 Hz (quiet arm + noise; a flinch jolt on demand).
- Bio: sensor_chunk every 100 ms per PRD §9.3 (baseline noise + bursts on demand + motor artefact whenever the
  haptic twin is pulsing — same process when --kind both), emg_burst events.
- Subscribers (max 3, subscribe/ping refresh, 5 s expiry) like the firmware.
- Control: stdin or a small UDP control port: `flinch` (EMG burst +120 ms, IMU jolt +150 ms), `squeeze`, `off-a`,
  `on-a`, `off-b`, `on-b`, `jitter <ms>`, `loss <pct>`.
- JSON-lines message log with receive/send timestamps (used by S2 for timing checks).
pytest: discovery both kinds and dialects; stroke limits and acks; watchdog with/without keepalive; subscribers
fan-out and expiry; sensor_chunk shape vs contracts; artefact during pulses; every control command; port offset.
Paste output.
```

---

## S2 — E2E harness + laptop live plot  · Oct 8 (MVP) · branch ph/s2-e2e · timebox 3 h

```text
1) tools/demo/live_plot.py (PRD FR-AP-01 fallback): subscribes to both nodes (discovery or --a-ip/--b-ip), plots
   EMG envelope and |accel| (last 10 s, matplotlib or pyqtgraph), shows SYNC/ASYNC from the game's live status if
   --hub is given, marks emg_burst and threat_impact. Works against the twin and real nodes. Full-screen mode for
   the audience.
2) tools/demo/run_pipeline.py --game phantom_hand with modes:
   --sim: start the hub (app/tool/hub_cli.dart or sim/live/fake_hub.py; --hub flutter|fake), the twin (--kind both)
   on chosen ports, then the Unity batch PlayMode PH_FullRun test (or --no-unity: sim/live/fake_headset.py
   --game phantom_hand replaying a fixture session through the live protocol — add that mode). Readiness checks,
   never sleeps. After upload: validate.py --session, python -m opus_analytics, then compute the G2/L3 table from
   04-E2E.md (phases, ack rate, SYNC timing_err mean/p95, ASYNC delay range, RTT p50, invalid messages, upload,
   embodiment present) and print it + write JSON/markdown.
   --faults: the three fault runs from the L3 table.
   --hardware: same checks against real nodes (no twin) + `--spinup` measuring motor start delay via an IMU spike
   on Node A after a single pulse (report median of 10).
pytest for the check functions with recorded fixtures; one --no-unity --sim smoke run pasted in the log.
```

---

## S3 — Full twin + participant model  · STRETCH (after G2) · branch ph/s3-twin-full

```text
Add the synthetic participant (model: ownership τ_up 40 s under sync within 300 ms, τ_down 20 s,
drift = ownership × 3 cm, flinch 160–220 ms ∝ ownership, answers from ownership), synthetic phantom_hand session
generator with synthetic_truth.json, and optional Wokwi project for the Node A firmware (ESP32 + MPU6050 +
SSD1306) if the Wokwi CLI is available. Only start when Opus says G2 is met.
```
