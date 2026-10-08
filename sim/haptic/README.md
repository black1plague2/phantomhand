# Chetna Fake Haptic Sleeve (sim/haptic/)

Software-only fake for the haptic sleeve in `contracts/HAPTIC_PROTOCOL.md` /
`contracts/schemas/haptic-message.schema.json` (+ v1.1's
`contracts/schemas/haptic-device-command.schema.json`). No firmware, no BLE, no
hardware — a UDP/JSON sleeve simulator so Unity/Flutter can develop and test
against something without a physical sleeve present.

## Wire formats

As of **v1.1** (see `contracts/HAPTIC_PROTOCOL.md`), two formats are accepted on
UDP port 8790:

1. **Device command (primary, the Electronics team's firmware format)** — no `v1`
   envelope, just `{"motor":0|1,"intensity":0-255,"duration_ms":50-400,"pattern":"pulse"|"buzz"|"ramp","cue_id":"<optional>","cue":"<optional clinical label>"}`.
   The sleeve acks with `{"v":1,"type":"ack","id":"...","ts_ms":...,"ack_id":"<cue_id>","ok":true}`,
   echoing `cue_id` when the caller supplied one (or a generated id if not).
2. **Legacy/internal semantic envelope** — the original v1 `cue`/`stop`/`config`/`ping`
   messages (`{"v":1,"type":"cue",...}`), validated against `haptic-message.schema.json`.
   Kept for back-compat / internal use; still acked the same way.

The sleeve tells the two apart by the presence of `"type"` (envelope messages always
carry it; device commands never do).

## Setup

```bash
cd sim/haptic
python -m venv .venv
```

Windows:
```powershell
.\.venv\Scripts\activate
pip install -r requirements.txt
```

**Venv note:** this track uses its own `sim/haptic/.venv`, not `sim/live/.venv`.
The haptic sleeve only needs `jsonschema` + `referencing` + `pytest` (UDP is
stdlib `asyncio`/`socket` — no `aiohttp`/WebSocket/HTTP side here), so a
dedicated, smaller venv keeps this track's dependencies independent of the
Live Protocol track's.

## Running the sleeve

```bash
# Basic: listen on 8790, discovery hello broadcast on 8791, status at 1 Hz
.\.venv\Scripts\python fake_haptic.py

# With fake IMU (<=50 Hz) and a starting battery level
.\.venv\Scripts\python fake_haptic.py --imu --battery-start 87

# Log every received cue (device or legacy) as one JSON line, for later analysis
.\.venv\Scripts\python fake_haptic.py --json-log out\cue_log.ndjson

# Simulate a disconnect 10s in: the sleeve keeps counting inbound datagrams but
# stops acking / sending status / broadcasting hello
.\.venv\Scripts\python fake_haptic.py --drop-after 10

# Custom ports / device id
.\.venv\Scripts\python fake_haptic.py --port 8790 --discovery-port 8791 --device-id sleeve-01
```

Flags: `--port` (default 8790), `--discovery-port` (default 8791), `--imu`,
`--imu-rate` (Hz, capped at 50), `--drop-after <seconds>`, `--battery-start`
(default 100), `--json-log <path>`, `--device-id`, `--selftest`.

Stop with Ctrl+C; a report (cue/ping/ack counts, invalid datagrams, battery,
whether disconnect was simulated) prints on shutdown.

## Self-test (scripted sender, end-to-end cue -> ack)

No separate client script is needed — `--selftest` starts the sleeve in-process,
sends one device command and one legacy cue from a throwaway UDP client, waits
for each `ack`, and prints the measured send->ack latency:

```bash
.\.venv\Scripts\python fake_haptic.py --selftest
```

Example real output (loopback UDP, same machine):
```
[device] Sent cue_id=e483ffb9-... -> ack ok=True ack_id=e483ffb9-...
[device] Measured send->ack latency: 1.879 ms
[legacy] Sent cue id=0ac1e704-... -> ack ok=True ack_id=0ac1e704-...
[legacy] Measured send->ack latency: 0.922 ms
```

## Summarizing a --json-log (haptic_report.py)

```bash
.\.venv\Scripts\python haptic_report.py out\cue_log.ndjson

# machine-readable
.\.venv\Scripts\python haptic_report.py out\cue_log.ndjson --json-out
```

Reports: total records, counts by kind (`device`/`cue`) and by clinical `cue`
label, min gap since the previous cue, max intensity (normalized to 0-1 even
for device-format 0-255 records), and any violation of the contract's safety
caps:

- **cue-level caps** (legacy envelope, `contracts/HAPTIC_PROTOCOL.md` "Safety
  and privacy"): intensity > 1.0, duration_ms > 1000.
- **device-level caps** (v1.1 firmware limits): intensity outside 0-255,
  duration_ms outside [50, 400].
- **shared caps**: gap since the previous cue below the record's `min_gap_ms`
  (defaults to 800 ms, the contract's default), and a sustained rate of more
  than 2 cues/second in any 1-second window.

## Tests

```bash
.\.venv\Scripts\python -m pytest test_fake_haptic.py -v
```

Covers: schema validation of `ack`/`status` replies and of the device-command
format; a cue -> ack round trip (both wire formats, including the v1.1
`cue_id` echo); ping -> reply; the safety-cap checker flagging a synthetic
violating log (cue-level and device-level); and `--drop-after` disconnect mode
no longer replying after the cutoff.

Real output from the last run (14 tests):
```
test_fake_haptic.py::test_ack_message_validates_against_schema PASSED
test_fake_haptic.py::test_status_message_validates_against_schema PASSED
test_fake_haptic.py::test_device_command_validates_against_device_schema PASSED
test_fake_haptic.py::test_device_command_out_of_range_is_rejected PASSED
test_fake_haptic.py::test_cue_ack_round_trip PASSED
test_fake_haptic.py::test_device_command_ack_echoes_cue_id PASSED
test_fake_haptic.py::test_device_command_without_cue_id_still_acks PASSED
test_fake_haptic.py::test_ping_gets_a_reply PASSED
test_fake_haptic.py::test_invalid_datagram_is_counted_not_crashed PASSED
test_fake_haptic.py::test_drop_after_stops_replies PASSED
test_fake_haptic.py::test_report_flags_no_violations_on_clean_log PASSED
test_fake_haptic.py::test_report_flags_synthetic_violations PASSED
test_fake_haptic.py::test_report_flags_device_level_violations PASSED
test_fake_haptic.py::test_report_default_min_gap_used_when_missing PASSED

============================= 14 passed in 2.57s ==============================
```

## Files

- `fake_haptic.py` — the sleeve simulator (UDP listener, discovery beacon, status/imu loops, selftest).
- `haptic_report.py` — `--json-log` summarizer + safety-cap checker.
- `test_fake_haptic.py` — pytest suite.
- `requirements.txt` — `jsonschema`, `referencing`, `pytest`.
