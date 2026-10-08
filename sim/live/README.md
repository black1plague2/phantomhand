# Chetna Live Protocol v1 Simulation Tools

Python tools for simulating the Chetna Live Protocol (headset ↔ hub communication).

## Status (2026-09-14, run2)

The first pass (Haiku, run1) had these tools written but never actually exercised
end-to-end: `e2e.py` silently no-op'd (the `"pytest" in sys.modules` guard was
always true), pointed at the wrong contracts path, and hardcoded `success: True`
with no real assertions. This pass fixed those defects, added real protocol
assertions, and ran everything for real (see the results table below and
`logs/sessions/2026-09-14-S-live-tools-run2.md` for full console output).

## Setup

```bash
# Create virtual environment
python -m venv .venv

# Activate (Windows)
.\.venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt
```

## S1: Protocol Builder & Validator (protocol.py)

Core message builder, validator, and outbox for the Chetna Live Protocol.

```bash
# Run unit tests
pytest test_protocol.py -v
```

**Features:**
- MessageBuilder: constructs valid messages with uuid, seq, ts_ms, from (including ping/pong)
- MessageValidator: validates against live-message.schema.json with full referencing Registry
- Outbox: de-duplicates by id, tracks acks, detects timeouts, supports resume-from-seq
- Ping/Pong: echo_ts_ms tracking for RTT measurements

**Test coverage:** 29 tests (26 existing + 3 ping/pong) covering builder, validator, outbox, ping/pong, edge cases

## S2: Fake Hub (fake_hub.py)

Hub-side simulator with UDP beacon, WebSocket server, and HTTP file upload.

```bash
# Run without beacon (localhost only)
python -m live.fake_hub --port 8787

# Run with UDP beacon (broadcasts on port 8788)
python -m live.fake_hub --port 8787 --beacon

# Run basic scenario (assign_program -> start -> pause after 5s -> resume -> stop)
python -m live.fake_hub --port 8787 --beacon --scenario basic
```

**Features:**
- UDP beacon on port 8788 (broadcast + subnet broadcast)
- WebSocket /opus/v1/live for live messages
- HTTP PUT /opus/v1/sessions/{session_id}/files/{filename} with idempotent sha256 handling
- Message logging and latency reports
- Scripted scenario support (--scenario basic)
- Ping/pong heartbeat: sends ping every 1s, receives pong from headset, measures RTT (p50/p95/max)

## S3: Fake Headset (fake_headset.py)

Headset-side simulator that discovers hub, connects, and replays sessions.

```bash
# Replay a session (auto-discover hub via UDP beacon)
python -m live.fake_headset \
  --session contracts/fixtures/sessions/healthy \
  --host auto \
  --speed 1.0

# With manual host
python -m live.fake_headset \
  --session contracts/fixtures/sessions/healthy \
  --host 127.0.0.1

# Speed up replay (10x faster)
python -m live.fake_headset \
  --session contracts/fixtures/sessions/healthy \
  --speed 10.0

# Simulate network drop after 5 seconds
python -m live.fake_headset \
  --session contracts/fixtures/sessions/healthy \
  --drop-at 5
```

**Features:**
- UDP discovery (or manual host entry)
- Session replay at configurable speed
- Status messages at 5 Hz
- Trial event playback synchronized to session timeline
- HTTP PUT file uploads (session.json, events.ndjson, kin_*.json)
- Command handling (pause, resume, stop)
- Network drop simulation with reconnect/resume
- Ping/pong heartbeat: responds to hub pings with pongs, sends own pings every 1s, measures RTT (p50/p95/max)
- Latency reporting (event send->recv, ping RTT)

## S4: End-to-End Integration Test (e2e.py)

Runs fake_hub and fake_headset in-process, verifies end-to-end flow and latencies.

```bash
# Run as standalone (outputs JSON results)
python e2e.py

# Run as pytest
pytest e2e.py -v
```

**Tests:**
- Basic scenario (no network drop)
- Scenario with network drop and reconnect
- File upload integrity (sha256 verification)
- Message ordering and de-duplication
- Command acknowledgment
- Latency tracking (p50, p95, max): event send->recv, command->ack, ping RTT

**Output:**
```
=== E2E TEST SUMMARY ===
Test 1 (no drop): PASS
Test 2 (with drop): PASS

Latency (no drop): event send->recv p50=1.5ms p95=3.2ms max=5.1ms | command->ack p50=2.0ms p95=3.5ms max=4.1ms
  hub ping RTT (n=10): p50=1.2ms p95=2.0ms max=2.5ms
  headset ping RTT (n=10): p50=1.3ms p95=2.1ms max=2.6ms

Latency (with drop): event send->recv p50=2.1ms p95=4.5ms max=8.3ms | command->ack p50=2.2ms p95=3.0ms max=3.8ms
  hub ping RTT (n=8): p50=1.1ms p95=1.9ms max=2.3ms
  headset ping RTT (n=8): p50=1.4ms p95=2.2ms max=2.7ms
```

## Fixtures

Added live-message fixtures to `contracts/fixtures/`:

- **valid/live-message.1.json**: Hello from headset
- **valid/live-message.2.json**: Command from hub (requires_ack)
- **invalid/live-message.1.json**: Missing required field (from)
- **invalid/live-message.2.json**: Invalid message type

All fixtures pass `contracts/validate.py`.

## File Locations

```
sim/live/
├── __init__.py
├── protocol.py           (S1: message builder, validator, outbox)
├── test_protocol.py      (S1: 24 unit tests)
├── fake_hub.py           (S2: hub simulator)
├── fake_headset.py       (S3: headset simulator)
├── e2e.py                (S4: end-to-end test)
├── requirements.txt      (dependencies)
├── README.md             (this file)
└── .venv/                (python environment)

sim/out/
└── hub_sessions/         (uploaded files from sessions)
```

## Real E2E Run Results (2026-09-14, run2)

These are actual measured numbers from `python e2e.py`, not estimates. See
`logs/sessions/2026-09-14-S-live-tools-run2.md` for full console output.

| Scenario | Speed | Events | Command→ack p50 / p95 / max (ms) | Event send→recv p50 / p95 / max (ms) | Result |
|---|---|---|---|---|---|
| basic, no drop | 10x | 46/46 | 4.1 / 7.0 / 7.0 | 2.3 / 5.1 / 7.1 | PASS |
| basic, drop@2s (resume) | 10x | 46/46 (13 resent, 0 dup) | 1.0 / 1.0 / 1.0 | 46.2 / 47.9 / 48.1 | PASS |
| basic, no drop | 1x (~19s session) | 46/46 | 3.0 / 4.1 / 4.1 | 2.0 / 4.0 / 11.6 | PASS |
| basic, drop@2s (resume) | 1x (~30s session) | 46/46 (13 resent, 0 dup) | 2.5 / 2.5 / 2.5 | 11.7 / 16.1 / 17.1 | PASS |
| UDP beacon discovery (`--host auto`) | 10x | n/a | n/a | n/a | PASS (loopback; see caveat below) |

All numbers are well inside the LIVE_PROTOCOL.md targets (command applied <
250ms p95, event visible < 250ms p95) and the "ack within 1s" rule. Note the
drop scenario's higher event latency is expected: it includes the buffered
events that were queued during the simulated network drop and only sent
(and timestamped) after reconnect.

**UDP discovery caveat:** on this Windows dev machine, broadcast to
`255.255.255.255`/subnet addresses is delivered fine on loopback in testing,
but real-network broadcast delivery across two separate Windows machines is
untested and may be blocked by Windows Defender Firewall depending on the
network profile (Public vs Private). See `docs/MANUAL_TODO.md`.

## Protocol Reference

See `contracts/LIVE_PROTOCOL.md` and `contracts/schemas/live-message.schema.json`.

## Development Notes

- All code is async (asyncio)
- Logging to console with timestamps
- Network operations resilient to drops/reconnects
- Validation at message boundaries (send/receive)
- Thread-safe outbox with asyncio.Lock


## Phantom Hand mode (`fake_headset.py --game phantom_hand`, S2 run1)

`phantom_replay.py` replays a Phantom Hand session (default `contracts/fixtures/sessions/phantom_hand_min`) through the
live protocol AND drives the sleeve twin (`sim/sleeve/twin.py`) or real Node A/B like the Unity game would: every
`haptic_cue` becomes a real stroke datagram (ack measured and written back into the event), `subscribe` + keepalive
`ping`, OLED `SYNC`/`ASYNC`, `flinch` on the twin's control port at `threat_impact`, `status` (2 Hz) with
`game_state` + `trace` built from the real node streams, `sens_###.json` recorded from those streams, reconnect +
upload retry through a hub outage, Node B absent => no EMG anywhere. Idle gaps are compressed (`--compress-gap-ms`,
the windows around each `threat_impact` stay 1x so analytics sees dense sensor data).

```bash
sim/live/.venv/Scripts/python.exe sim/live/fake_headset.py --game phantom_hand --host 127.0.0.1 --port <hub>     --discovery-port <8791+N> --control 127.0.0.1:<8793+N> [--node-a ip:port] [--node-b ip:port] [--off-a-at 0.5]
```

The L3 harness is `tools/demo/run_pipeline.py --game phantom_hand --sim --no-unity` (it drives this class in-process).
Tests: `cd sim/live && .venv/Scripts/python.exe -m pytest test_phantom_replay.py -q` (16, real twin on offsets 12100+).
