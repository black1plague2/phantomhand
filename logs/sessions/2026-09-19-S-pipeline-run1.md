# Track S — demo pipeline tooling, run 1 (2026-09-19)

Scope: `sim/`, `analytics/`, `tools/demo/` (new), this log only. Did not touch `game/`, `app/`,
`contracts/schemas/*`, `CONTEXT.md`, `logs/CHANGELOG.md`. No `git commit`. No Unity. No binding of
8787/8788/8790/8791 (Opus's live Unity session is using the hub/beacon on those ports right now) —
all new tests use OS-assigned or explicitly-chosen alternate ports (8797/8798/8792/8793 style, or
`free_udp_port()`/ephemeral TCP where the existing test helpers already do that).

Context read first: `CLAUDE.md`, `CONTEXT.md` star-start-here box, `contracts/LIVE_PROTOCOL.md`,
`docs/TESTING_RUNBOOK.md`, `logs/sessions/2026-09-19-N-analytics-run3.md`, `analytics/VALIDATION.md`
sections 10-12, `contracts/validate.py`, `analytics/opus_analytics/*` (analyze.py, io_session.py,
segmentation.py, quality.py, metrics/trial_metrics.py), `sim/live/*.py`, `sim/haptic/fake_haptic.py`,
`app/tool/hub_cli.dart`, `docs/MANUAL_TODO.md`.

## Plan

1. `tools/demo/watch_and_analyse.py` — poll a hub sessions dir, validate + analyse finished sessions,
   print a plain-English summary.
2. `tools/demo/check_session.py` — meaning checker (schema + 6 sanity checks) for a real session dir.
3. `tools/demo/start_pc_demo.ps1` + `.cmd` + `tools/demo/open_firewall.ps1`.
4. `sim/live/fake_headset.py` compatibility check/fix (new event types, metrics_tick).
5. Run every existing + new test suite, paste real output.
6. Final checkpoint.

(CHECKPOINTs below, one per task, appended as each is finished and verified.)

## CHECKPOINT 1 -- Tasks 1 & 2: `watch_and_analyse.py` + `check_session.py`

Wrote `tools/demo/watch_and_analyse.py` (poll loop, default `app/.hub_data`, 2s interval, `--once`
for tests) and `tools/demo/check_session.py` (schema check via `contracts/validate.py --session`
subprocess, then 6 meaning checks built on `opus_analytics.io_session.load_session` /
`segmentation.segment_trials` / `metrics.trial_metrics.compute_trial_metrics`, imported directly --
these scripts must run under `analytics/.venv/Scripts/python.exe`, documented in each file's
docstring). Both use `pathlib.Path` throughout and list-form `subprocess.run(...)` args (never
`shell=True`), so the space in `...\GARV BANSAL\...` is never at risk.

Tests: `tools/demo/tests/{conftest.py, test_watch_and_analyse.py, test_check_session.py}` (11 tests):
copy `contracts/fixtures/sessions/healthy` into `tmp_path`, then for `check_session`: assert PASS/exit
0 on the untouched copy, and FAIL/exit 1 (specifically "no contact event", schema staying `[PASS]`)
on a copy with every `contact` line stripped from `events.ndjson` -- the same defect class as
`analytics/tests/test_analysability.py`'s regression test. For `watch_and_analyse`: `is_session_ready`
true/false cases (ended vs not, kin present vs not, metrics.json already present), `watch_once`
actually running validate+analytics and writing `metrics.json`, and two idempotency cases (same
`processed` set across polls, and a *fresh* `processed` set across a simulated process restart --
both must skip re-analysis once `metrics.json` exists).

```
$ ./analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests -v
11 passed in 33.05s
```

Also ran `check_session.py` directly against the real (uncopied) fixture as an evidence check:
```
$ ./analytics/.venv/Scripts/python.exe tools/demo/check_session.py contracts/fixtures/sessions/healthy
[PASS] schema validation (contracts/validate.py --session)
[PASS] all 6 trial(s) have trial_start and trial_end
[PASS] every non-timeout trial has target_shown
[PASS] all 6 successful trial(s) have a contact event
[PASS] every trial window overlaps the recorded kinematics range [0, 15611] ms
[PASS] all 6 successful trial(s) have endpoint_error_cm <= 25 cm
[PASS] rate_hz 72.0 >= 45
[PASS] no implausibly fast (< 50 ms) reaction times

PASS: 0 failed, 0 warned, 8 checks total
EXIT=0
```
No port binding involved in either tool or its tests (pure filesystem + subprocess), so no conflict
with Opus's live 8787/8788 session.

## CHECKPOINT 2 -- Task 3: `start_pc_demo.ps1`/`.cmd` + `open_firewall.ps1`

Wrote `tools/demo/start_pc_demo.ps1` (opens 3 separate `powershell.exe -NoExit` windows: hub via
`dart run tool/hub_cli.dart --port 8787` with `-AutoDrive`/`-WithHaptics`/`-SkipWatcher` switches
mirroring `hub_cli.dart`'s real flags and `docs/TESTING_RUNBOOK.md` section 6's exact commands;
haptic sim via `sim/haptic/.venv/Scripts/python.exe -m haptic.fake_haptic` run from `sim/`, gated
behind `-WithHaptics` since haptics are additive/off-by-default per CONTEXT.md; the watcher from
CHECKPOINT 1). `tools/demo/start_pc_demo.cmd` is a 5-line forwarding wrapper for double-click/cmd.exe
use. `tools/demo/open_firewall.ps1` prints (never runs) the exact `New-NetFirewallRule` commands for
hub TCP 8787 + UDP 8788, plus sleeve UDP 8790/8791 behind `-Haptics` (same shape as the existing
`docs/MANUAL_TODO.md` Track A firewall entry, extended with the haptic ports and verify/remove
commands).

**Did not execute** `start_pc_demo.ps1` (it binds 8787/8788/8790/8791, which the task brief
explicitly forbids me from doing right now -- Opus's live Unity session is on those ports).
Verified both `.ps1` files parse as valid PowerShell without executing them
(`[scriptblock]::Create((Get-Content -Raw <file>))` on each -- parses, never invokes):
```
OK: tools\demo\start_pc_demo.ps1
OK: tools\demo\open_firewall.ps1
```
`open_firewall.ps1` binds nothing and only prints, so it was safe to actually run for real evidence:
```
$ powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\demo\open_firewall.ps1 -Haptics
This script does NOT change anything on this machine.
Copy the block below into an elevated (Administrator) PowerShell window and run it there:

# OPUS hub: WebSocket/HTTP (contracts/LIVE_PROTOCOL.md)
New-NetFirewallRule -DisplayName "OPUS Hub TCP 8787" -Direction Inbound -Protocol TCP -LocalPort 8787 -Action Allow
# OPUS hub: UDP discovery beacon (contracts/LIVE_PROTOCOL.md)
New-NetFirewallRule -DisplayName "OPUS Hub UDP 8788" -Direction Inbound -Protocol UDP -LocalPort 8788 -Action Allow
# OPUS haptic sleeve simulator: UDP cue/status (contracts/HAPTIC_PROTOCOL.md)
New-NetFirewallRule -DisplayName "OPUS Haptic UDP 8790" -Direction Inbound -Protocol UDP -LocalPort 8790 -Action Allow
# OPUS haptic sleeve simulator: UDP discovery
New-NetFirewallRule -DisplayName "OPUS Haptic Discovery UDP 8791" -Direction Inbound -Protocol UDP -LocalPort 8791 -Action Allow
...
```
**Not verified this run** (needs a human at the machine, per the task's own port ban): actually
launching `start_pc_demo.ps1` and confirming three real windows come up and a headset can join.
Flagged as a `docs/MANUAL_TODO.md` follow-up below.

## CHECKPOINT 3 -- Task 4: `sim/live/fake_headset.py` compatibility

Investigated what "compatibility with new event types" actually needed: `event.schema.json` already
lists every new type (`session_start`, `block_start`, `trial_start`, `target_shown`,
`movement_onset`, `contact`, `grasp`, `release`/`placed`, `trial_end`, `block_end`, `session_end`,
`form_warning`, `haptic_cue`), and `fake_headset.py` replays `events.ndjson` verbatim as
`trial_event` payloads -- it never switches on event type, so every new type already passes through
and validates with zero code changes. **The actual gap was `metrics_tick`**: `contracts/
LIVE_PROTOCOL.md` requires it ("metrics_tick per trial") and the Flutter app already consumes it
(`app/lib/core/hub/hub_connection.dart`'s `metricsStream`, `app/tool/hub_cli.dart` already prints
it), but neither `sim/live/protocol.py`'s `MessageBuilder` nor `fake_headset.py` could ever produce
one -- confirmed by `grep -rn metrics_tick sim/` finding it only in a docstring, never a builder
method or a send call.

**Fix** (2 files):
- `sim/live/protocol.py`: added `MessageBuilder.metrics_tick(window_trials, metrics)`.
- `sim/live/fake_headset.py`: tracks `target_shown`/`movement_onset`/`contact` timestamps per trial
  (reset on `trial_start`) and sends one `metrics_tick` (on-device-style `reaction_time_ms`/
  `movement_time_ms` computed from that trial's own timestamps, explicitly documented in the code
  as an approximation, not the authoritative analytics) right after each `trial_end` -- matching
  `LIVE_PROTOCOL.md`'s "metrics_tick per trial" cadence. A miss/timeout trial with missing
  timestamps just sends fewer (possibly zero) metrics rather than inventing values.

Existing `sim/live` test suite (26 tests, unchanged) still green:
```
$ ./.venv/Scripts/python.exe -m pytest -q          (sim/live/)
26 passed in 1.03s
```
Ran `e2e.py` directly (not just under pytest) to see the new message actually flow hub-side:
```
$ ./.venv/Scripts/python.exe e2e.py --speed 10
...
"hub_message_counts": {
  "hello": 2, "hello_ack": 2, "assign_program": 1, "ack": 1, "trial_event": 67,
  "status": 5, "metrics_tick": 6, "command": 3, "ping": 5
},
"hub_invalid_count": 0,
"success": true, "error": null
...
Test 1 (no drop):        PASS
Test 2 (with drop):      PASS
Test 3 (UDP discovery):  PASS
```
`metrics_tick: 6` (one per trial in the healthy fixture) with `hub_invalid_count: 0` confirms the
new message type is schema-valid and actually reaches the hub, without breaking any existing
assertion (hub's `handle_message` already tolerated unknown-but-valid types via its `else` branch
before this change; now it's not unknown, just still uncounted beyond `message_counts`).

`fake_headset.py --host --port` (against any hub, any host/port) was already parameterized before
this run (`--host`, `--port`, `--beacon-port`, `--no-scenario`) -- confirmed by reading
`main()`'s argparse block; no change needed there.
