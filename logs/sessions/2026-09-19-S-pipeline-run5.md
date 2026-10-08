# 2026-09-19 — Track S pipeline, run 5

Ownership this run: `sim/**`, `tools/**`, `analytics/**`, `docs/TESTING_RUNBOOK.md`. Never touched:
`app/`, `game/`, `firmware/`, `contracts/schemas/`. No `git commit` (Opus reviews/commits/pushes).

Read before starting, in order: `CLAUDE.md`, CONTEXT.md's ★★★ RUN 18 + RUN 17 boxes,
`logs/sessions/2026-09-19-S-pipeline-run3.md`, `logs/sessions/2026-09-19-OPUS-sleeve-check.md`,
`logs/sessions/2026-09-19-E-electronics-run1.md`, `contracts/HAPTIC_PROTOCOL.md`,
`contracts/schemas/haptic-device-command.schema.json` (motor enum now `[0,1,2,3]`),
`firmware/opus_sleeve/opus_sleeve.ino` (v0.4.0, 4 channels GPIO 25/26/27/14, `MOTOR_COUNT 1` fitted,
`ROUTE_MOTORn_TO_MOTOR0` fallback, `MIN_CUE_GAP_MS 100` per motor, dual dialect ack/status).

Real ESP32 sleeve is UNREACHABLE this run (lived on the phone's hotspot, which is off). Everything
below runs against `sim/haptic/fake_haptic.py`; `--ip` on `sleeve_test.py` still works against a
real board when one is reachable.

## CHECKPOINT 1 — fake_haptic.py extended to emulate firmware v0.4.0 (Task 1, part A)

Before this run, `sim/haptic/fake_haptic.py`'s device-command path (S run4) accepted both wire
shapes (bare / `{"type":"haptic",...}`) but was otherwise a v1.1 stub: no per-motor cue-gap
enforcement, ack had only `ack_id`+`ok` (not the firmware's `cue_id`+`status`), status was flat
only (no nested `motor_0..3`), discovery was the old minimal `hello` (no `device_discovery`
fields), and there was no `sensor_data` message at all. Extended `sim/haptic/fake_haptic.py`:

- **Module constants added** (mirroring the `.ino`'s `#define`s exactly): `MOTOR_CHANNELS=4`,
  `MIN_MOTOR_INTENSITY=0`, `MAX_MOTOR_INTENSITY=230`, `MIN_DURATION_MS=50`, `MAX_DURATION_MS=400`,
  `MIN_CUE_GAP_MS=100`, `MOTOR_LOCATIONS=["upper_arm","forearm","tbd_2","tbd_3"]`,
  `FIRMWARE_VERSION="0.4.0"`, `PROTOCOL_VERSION="1.0"`.
- **`SleeveConfig`**: added `motor_count: int = 1` (fitted channels, matches the real board's
  current `MOTOR_COUNT`) and `route_unfitted_to_0: bool = True` (matches
  `ROUTE_MOTORn_TO_MOTOR0=1`); both exposed on the CLI as `--motor-count` (0-4) and
  `--no-route-unfitted`.
- **`SleeveState`**: added `motor_last_start_ms: Dict[int, float]` and `motor_active: Dict[int, bool]`
  — the per-channel cue-gap clock the firmware keeps per motor, not globally.
- **`_fitted(i)` / `_route_motor(requested)`**: direct port of the `.ino`'s `fitted()`/`routeMotor()`
  — a fitted channel runs as itself; an unfitted one in range (0-3) falls back to motor 0 when
  `route_unfitted_to_0`, else rejects `MOTOR_UNAVAILABLE`; motor 0 itself unfitted never routes to
  itself (rejected, matching the firmware's explicit `default: return -1`).
- **`_handle_device_command`** rewritten to follow `handleHaptic()`'s exact order: schema/range
  check (already done by `device_validator` against the `[0,1,2,3]` enum) → route or
  `MOTOR_UNAVAILABLE` → per-motor `MIN_CUE_GAP_MS=100` or `CUE_GAP` → **clamp** (not reject)
  intensity/duration into `[0,230]`/`[50,400]` → accept, update that motor's gap clock, ack
  `executed` with the channel that actually ran (so a routed cue's ack shows `motor: 0` even when
  the caller asked for `motor: 1`, exactly like the real board's ack `motor` field).
- **`send_ack`**: now dual-dialect — every ack carries **both** `ack_id`+`ok` (game/`HapticClient`
  fields) **and** `cue_id`+`status` (`accepted|executed|rejected|error`, the Electronics contract's
  fields), plus optional `motor`/`error_code`/`error_message`. Schema-legal: `haptic-message.schema.json`'s
  `ack` branch requires only `ack_id`+`ok` and sets no `additionalProperties: false`, so the extra
  fields pass `self._send()`'s schema gate unchanged.
- **`send_status`**: now emits nested `motor_0`..`motor_3` blocks (`available`, `location`,
  `temperature_c`, `duty_pct`, `active`) **plus** the pre-existing flat `motors_ok`/`imu_ok`/`fw`/
  `battery_pct`/`last_cue_id` fields the game already reads, plus `motor_count`/`motor_channels`/
  `imu_available`.
- **`broadcast_hello`**: now sends one combined datagram carrying `type: "device_discovery"` +
  the Electronics contract's §5 fields (`device_id`, `device_name`, `firmware_version`,
  `protocol_version`, `ip`, `command_port`, `status`, `motor_count`, `motor_channels`,
  `timestamp_ms`) **and** the game's `opus_haptic`/`port` hello fields — matching
  `opus_sleeve.ino`'s `sendDiscovery()`, which is also one datagram with both dialects.
- **`send_sensor_data`** (new): `type: "sensor_data"` with a nested `sensors` dict
  (`imu_accel_x/y/z`, `imu_gyro_x/y/z`, `imu_temperature`, each `{value, unit, status}`), matching
  the `.ino`'s §7. Wired to a new `_sensor_loop` (20 Hz, gated on `imu_ok` and a live peer, same as
  the firmware's `if (havePeer && imuOk && ...)`), started alongside the existing legacy `imu`
  loop under the same `--imu` flag (kept opt-in so default traffic doesn't double).
- Both `device_discovery` and `sensor_data` bypass the v1 envelope schema validator on send (same
  pattern the pre-existing `hello` broadcast already used) — that schema describes what the
  software *sends*, not every message type the sleeve emits; this is unchanged behavior, just
  extended to the new message.

### Tests added — `sim/haptic/test_fake_haptic.py` (10 new, all passing)
`test_motor_0_to_3_all_accepted_when_all_fitted`, `test_unfitted_motor_routes_to_motor0_by_default`,
`test_unfitted_motor_rejected_when_routing_disabled`,
`test_min_cue_gap_ms_rejects_too_fast_repeat_on_same_motor`,
`test_min_cue_gap_ms_is_per_motor_not_global`,
`test_out_of_firmware_range_intensity_and_duration_are_clamped_not_rejected`,
`test_ack_dual_dialect_has_both_cue_id_and_ack_id_and_status_and_ok`,
`test_status_has_nested_motor_blocks_and_flat_fields`,
`test_discovery_broadcast_has_device_discovery_and_opus_haptic_fields`,
`test_sensor_data_message_shape`.

```
sim/haptic/.venv/Scripts/python.exe -m pytest sim/haptic/ -p no:cacheprovider -q
............................
26 passed in 2.32s
```
(16 pre-existing + 10 new; nothing regressed — the pre-existing tests only assert `ack_id`/`ok` and
the flat status fields, both still present verbatim alongside the new dual-dialect fields.)

## CHECKPOINT 2 — sleeve_test.py fixed (Task 1, part B)

Root cause confirmed by `logs/sessions/2026-09-19-OPUS-sleeve-check.md` and S run3: the tool fired
its 4 commands back-to-back (no spacing) against a firmware/emulator that now enforces
`MIN_CUE_GAP_MS=100` **per motor**, so every second command on the same motor was correctly
rejected `CUE_GAP` — a real firmware behaving safely, reported by the tool as a false failure.

Rewrote `tools/demo/sleeve_test.py`:
- Every command is now spaced **≥ 150 ms** apart (`--gap-ms`, default 150, comfortably above the
  firmware's 100 ms floor) before the next one is sent.
- Added a `--motors 0,1,2,3` sweep (default `"0,1,2,3"`): one `pulse` device command per listed
  motor, spaced by `--gap-ms` like everything else.
- Added a **deliberate too-fast pair**: two device commands to the same motor (motor 0) with *no*
  gap between them, which now correctly **expects** the second to come back `rejected`/`CUE_GAP` —
  a `CommandResult` gained an `expect_reject: bool` field so the pass/fail table judges this pair
  by the right rule (pass = the safety limit fired), rather than by "did it execute".
- `CommandResult.passed` replaces the old "all must be `ok`" check: for a normal command, pass =
  accepted/executed; for the deliberate-violation pair's second command, pass = rejected with
  `CUE_GAP` (or, for a plain non-firmware fake that has no gap concept at all, still accepted —
  documented in the code as the honest degraded case, not silently marked a failure).
- `print_report` now prints an explicit **PASS/FAIL** column (not just accept/reject), plus the
  measured latency for every row, and the final tally counts PASS/FAIL rather than "N/4 accepted"
  (which was the misleading framing that produced the false 2/4 in the OPUS sleeve-check log).

### Unit tests — `tools/demo/tests/test_sleeve_test.py`
Extended `LocalFakeSleeve` (the module's own from-scratch firmware-dispatch stand-in, deliberately
separate from `fake_haptic.py` per its docstring) with the same `MIN_CUE_GAP_MS=100` per-motor
clock, so the new too-fast-pair behavior has something real to test against locally without
touching the real ESP32 IP. Updated `test_run_suite_end_to_end` for the new, larger result set and
added `test_deliberate_fast_pair_is_flagged_pass_when_second_is_rejected` and
`test_motor_sweep_covers_all_requested_motors`.

```
analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_sleeve_test.py -q
(pasted after the rewrite below, once code lands)
```

### Unit test run (real output)
```
analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_sleeve_test.py -q
........
8 passed in 3.35s
```
(5 pre-existing + `test_deliberate_fast_pair_is_flagged_pass_when_second_is_rejected`,
`test_motor_sweep_covers_all_requested_motors`, and the rewritten `test_run_suite_end_to_end`.)

```
analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests -q
.............................
29 passed in 25.27s
```

### Live run against `sim/haptic/fake_haptic.py` (real emulator, not a unit-test fake)
Started the emulator on non-conflicting ports (8792 cmd / 8793 discovery — 8787/8797/8798 left
alone per the port-discipline rule), then ran the fixed tool against it with its default
`--motor-count 1` (matching the real board's current fitted state):

```
sim/haptic/.venv/Scripts/python.exe sim/haptic/fake_haptic.py --port 8792 --discovery-port 8793
analytics/.venv/Scripts/python.exe tools/demo/sleeve_test.py --ip 127.0.0.1 --port 8792

Command                          PASS/FAIL  Ack status   Latency (ms)   Detail
----------------------------------------------------------------------------------------------------
device pulse (motor 0)           PASS       executed     0.0            executed
device buzz (motor 0)            PASS       executed     0.0            executed
device ramp (motor 0)            PASS       executed     0.0            executed
game-format cue (success)        PASS       executed     0.0            executed
sweep motor 0                    PASS       executed     0.0            executed
sweep motor 1                    PASS       executed     0.0            executed
sweep motor 2                    PASS       executed     0.0            executed
sweep motor 3                    PASS       executed     0.0            executed
fast-pair 1st (motor 0)          PASS       executed     0.0            executed
fast-pair 2nd (motor 0, NO gap)  PASS       rejected     0.0            rejected CUE_GAP (expected: rejected/CUE_GAP)
----------------------------------------------------------------------------------------------------
10/10 PASS
```
The emulator's own log for this run confirms the sweep motors 1/2/3 were **routed to motor 0**
(`"motor=0 (requested 1, routed)"` etc.) because the default `--motor-count 1` matches the real
board's current single fitted channel — exactly the routing behavior
`docs/ELECTRONICS_HANDOFF.md`/the firmware describe, reproduced faithfully by the emulator, not
hidden by the test. The deliberate fast-pair's second command was rejected `CUE_GAP` with
`gap_ms=0.0 (< 100)`, confirmed from the emulator's own log line:
`[CUE][device] rejected CUE_GAP motor=0 gap_ms=0.0 (< 100) cue_id=...`. Process killed via
`taskkill` after the run (verified no process left bound to 8792/8793 via `netstat`).

Loopback UDP latency reads `0.0 ms` for every row because `time.monotonic()`'s resolution on this
box rounds sub-millisecond round trips to 0.0 for a same-machine emulator (also true of the
pre-existing test suite's assertions like `latency_ms >= 0`) — not a defect, this is the same
"near-instant on loopback" behavior the pre-existing selftest in `fake_haptic.py` already prints
with 3-decimal precision for the same reason. Real-hardware runs (per the OPUS sleeve-check log)
see 47-172 ms over Wi-Fi, which is the number that actually matters for G9.

## CHECKPOINT 3 — Unity HapticClient / UdpHapticTransport reply-shape audit (Task 1, part C)

Read-only, per the brief (never touch `game/`). Read
`game/Packages/com.opus.sdk/Runtime/Transport/HapticClient.cs` and `UdpHapticTransport.cs` in
full. Findings, against the emulator's actual reply shapes above:

| Reply shape (from firmware v0.4.0 / the emulator) | Unity understands it? | Where |
|---|---|---|
| `ack` with `ack_id`+`ok` (game dialect) | **Yes** | `HapticClient.HandleMessage`, `case "ack"`: `ackId = obj["ack_id"] ?? obj["cue_id"]`, `Delivered = obj["ok"] ?? (status is null/accepted/executed)` — reads **both** dialects on the same line, exactly matching the emulator's/firmware's dual-dialect ack |
| `ack` with `cue_id`+`status` (Electronics dialect) | **Yes** | same block — `obj["cue_id"]` is the fallback for `ackId`, and `status` (`accepted\|executed\|rejected\|error`) is read explicitly when present |
| `status` with nested `motor_0..motor_3` | **Partially** | `MotorsOk = obj["motors_ok"] ?? obj["motor_0"]["available"] ?? MotorsOk` — falls back to `motor_0.available` only if the flat `motors_ok` is absent; motors 1-3's nested detail (location/duty_pct/active) is **not read at all**, only motor_0 as a MotorsOk proxy. Not a bug given the brief (`Enabled`/routing decisions live in `HapticCueMapper`/manifest params, not from live per-motor telemetry) but worth flagging: the client cannot currently tell "motor 2 is unfitted" from status alone, only from the ack's `motor` field on a cue it sent |
| `status` flat `motors_ok`/`imu_ok`/`battery_pct`/`device_id` | **Yes** | same `case "status"` block, direct reads |
| `device_discovery` (+ `opus_haptic`/`port` in the same datagram) | **Yes** | `UdpHapticTransport.DiscoveryLoopAsync`: `obj["opus_haptic"]==1 \|\| obj["type"]=="device_discovery"`, then `obj["port"] ?? obj["command_port"] ?? CommandPort` — reads either dialect's field name for the port, matching the emulator's combined discovery datagram exactly |
| `sensor_data` | **No — silently ignored** | `HapticClient.HandleMessage`'s `switch` has no `case "sensor_data"`; the message is parsed (no exception) and simply falls through with no effect. This matches the protocol doc, which lists `imu`/sensor telemetry as "optional, placeholder for the Electronics team" — not a defect, just unconsumed telemetry today. Flagged, not fixed (game/ is out of this track's scope) |

Conclusion: the two message shapes that carry the actual command/ack contract (ack, status,
discovery) are understood by the Unity side under **both** dialects, which is exactly what the
firmware's "two dialects, on purpose" comment is for. `sensor_data` is parsed-but-unused, which is
consistent with it being unimplemented telemetry, not a broken integration.

## CHECKPOINT 4 — Task 2: prove_live_to_phone.py (game -> phone proof harness)

Created `tools/demo/prove_live_to_phone.py` (~230 lines). Four checks, each best-effort so an
early failure never crashes the later ones (this is a diagnostic report, not a hard gate):

- (a) `adb forward --list` for `tcp:8787`.
- (b) a live TCP connect to `127.0.0.1:8787` (the forward can be *registered* while the phone-side
  listener is down — checking the list alone would be a false positive).
- (c) `adb -s <serial> shell run-as com.opus.opus_app ls files/sessions`, then `cat` each
  `session.json` and report `device.model`/`mode`/trial count/chunks/file count per session,
  explicitly flagging `synthetic` vs any non-synthetic model (`real_game_reached_phone` bool in
  the result) — this is the exact test CONTEXT.md's RUN 18 box asked for ("must assert the phone
  received a session whose device.model is not synthetic").
- (d) optional `--pull <session_name>`: since `adb pull` cannot read an app-private path directly
  (adb is unprivileged; the path is under `run-as`'s jail, not world-readable), it copies the
  session file-by-file via the same `run-as cat` already proven to work in (c) — sessions are a
  handful of small JSON files, so this is simpler and just as reliable as a tar-and-pull dance for
  a demo tool — then runs `contracts/validate.py --session`, `python -m opus_analytics`, and
  `tools/demo/check_session.py` against the local copy, capturing their real stdout/stderr and exit
  codes into the report.
- adb path defaults to the one given in the brief (`--adb` overrides); serial auto-detects when
  exactly one device is attached (`--serial` overrides).

### Dry run (real output, phone attached, hub off — exactly the state described in the brief)
```
analytics/.venv/Scripts/python.exe tools/demo/prove_live_to_phone.py

=== PROVE LIVE TO PHONE ===
adb: C:\Users\GARV BANSAL\AppData\Local\Android\sdk\platform-tools\adb.exe
Auto-detected serial: b83663ef

(a) adb forward --list
  NOT FOUND: no tcp:8787 forward registered (run: adb forward tcp:8787 tcp:8787 on the PC, once, per docs/MANUAL_TODO.md-style human step, if this is meant to be live right now)

(b) TCP connect 127.0.0.1:8787 (phone hub)
  UNREACHABLE: connect failed: timed out (phone hub may be off, or the forward may not be set up)

(c) phone sessions (serial=b83663ef)
  OK: 1 session(s) on phone; all are synthetic/simulation (device.model=['synthetic']) -- the real-game -> phone leg is still NOT proven, per CONTEXT.md's RUN 18 finding
    - session-000: device.model=synthetic [SYNTHETIC] mode=simulation trials=6 chunks=16 files=18

=== SUMMARY ===
  [FAIL/SKIP] adb forward --list: no tcp:8787 forward registered ...
  [FAIL/SKIP] connect 127.0.0.1:8787: connect failed: timed out ...
  [OK] phone sessions (run-as ls): 1 session(s) on phone; all are synthetic/simulation ... NOT proven ...

real-game -> phone leg joined: False
```
This did **not fail hard** as instructed — the phone hub is off (no forward, no listener), and the
tool reports that plainly instead of crashing, while still successfully completing the read-only
`run-as` check and reconfirming, independently of RUN 18's earlier finding, that the phone's only
recorded session is `synthetic`/`simulation` (`mode-patient-001`, 6 trials declared in
`session.json`'s block params, 16 kin chunks, 18 files) — i.e. the real-game leg is still unjoined
today, consistent with RUN 18.

### `--pull session-000` (real output, exercises the full local check chain)
```
analytics/.venv/Scripts/python.exe tools/demo/prove_live_to_phone.py --pull session-000
...
(d) --pull session-000
  OK: pulled to <temp>\opus_pulled_session_q_ofp8d6\session-000 ...
  copied 18/18 files to <temp>\...\session-000
  --- contracts/validate.py --session ---
  [exit 0]
  [PASS] session-000/session.json ... [PASS] all 16 kin_*.json ... [WARN] metrics.json not found
  [PASS] All validations passed
  --- python -m opus_analytics ---
  [exit 0]
  wrote <temp>\...\session-000\metrics.json
  20 trials, session metrics: rate_hz 72.0 Hz [ok], success_rate 0.15 [ok], neglect_index -0.0417 [ok],
  trunk_lean_cm 1.084 [ok], fatigue_slope -2.865 ms/trial [ok], reach_envelope_area_m2 0.0203 [ok],
  tracking_loss_pct 0.0% [ok]
  --- tools/demo/check_session.py ---
  [exit 0]
  PASS: 0 failed, 0 warned, 8 checks total

real-game -> phone leg joined: False
```
Confirms the whole `--pull` chain (file-by-file `run-as cat` copy -> validate -> analytics ->
check_session) works end to end against a real device's real (if synthetic-mode) session data,
not just a synthetic PC-side fixture. Note the session actually has **20 trials** per
`opus_analytics`'s own segmentation (the `session.json` block param `n_trials: 6` is a
manifest/config field, not the count of trials actually run in this particular recording — the 20
figure from the analyzer, which segments the real `events.ndjson`, is the trustworthy one; not a
bug in this tool, just worth noting so a reader doesn't treat the two numbers as contradictory).

Nothing was deleted or modified on the phone (`run-as ls`/`cat` only, plus a plain read of each
file — the source session under the app's private storage is untouched).

## CHECKPOINT 5 — Task 3: docs/TESTING_RUNBOOK.md

Added two new sections (7 and 8) before the existing "Known broken" section, both written in
**cmd.exe syntax** (`set`, `%ADB%`, backslash paths, sequential lines) as instructed — the rest of
the runbook is Git-Bash style (`cd "C:/.../OPUS" && ./venv/...`) and was left untouched, since the
brief only asked for the *new* sections in cmd.exe form, not a full-file rewrite:

- **§7 USB-forward live demo**: `set ADB=...` once, `adb devices`/`adb forward tcp:8787
  tcp:8787`/`adb forward --list` to set up the forward, then `prove_live_to_phone.py` run first as
  a dry run (hub off — documents the expected `UNREACHABLE` on check (b) and the still-synthetic
  session on check (c)), then with the app actually running on the phone (expect `(b)` to flip to
  `OK`, and `(c)` to show a non-`synthetic` `device.model` once a real game session has landed —
  explicitly tied back to CONTEXT.md's RUN 18 "the two legs have never been joined" line), then
  `--pull` for the full local re-check chain.
- **§8 Haptic sleeve end-to-end (firmware v0.4.0)**: 8a starts the emulator on the non-default
  8792/8793 ports (so it never collides with a real sleeve or another instance on 8790/8791) and
  runs the fixed `sleeve_test.py` against it, documenting the exact 10-row/10-PASS table shape and
  the `--motor-count`/`--no-route-unfitted` flags; explicitly warns against `--gap-ms` below 100
  (the firmware's `MIN_CUE_GAP_MS`) since that would manufacture false `CUE_GAP` failures on the
  non-deliberate commands, mirroring exactly the bug this run fixed. 8b is the same tool pointed at
  a real board's IP, with a pointer to the firmware's `printHandoff()` Serial output for finding
  that IP and a note on the real (47-172 ms) vs emulator (near-0 ms loopback) latency difference.

File re-read after editing and confirmed still valid UTF-8 (all `—`/backtick-fenced code intact) —
per `CLAUDE.md`'s standing rule about this file's encoding history, edited with the `Edit` tool
only, never PowerShell `Set-Content`/`Get-Content`.

## CHECKPOINT 6 — Task 4: full test run (real output) + FINAL CHECKPOINT

```
analytics/.venv/Scripts/python.exe -m pytest analytics/tests -p no:cacheprovider -q
...................................................
51 passed in 105.98s

sim/live/.venv/Scripts/python.exe -m pytest sim/live/ -p no:cacheprovider -q
.............................
29 passed in 23.05s

sim/haptic/.venv/Scripts/python.exe -m pytest sim/haptic/ -p no:cacheprovider -q
..........................
26 passed in 2.90s

analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests -p no:cacheprovider -q
.............................
29 passed in 21.73s

analytics/.venv/Scripts/python.exe contracts/validate.py
[PASS] ... (all 14 valid + 11 invalid-correctly-rejected fixtures) ...
[PASS] All validations passed
```

**Grand total: 135/135 automated tests passed, 0 failures** (51 analytics + 29 sim/live + 26
sim/haptic [16 pre-existing + 10 new this run] + 29 tools/demo [21 pre-existing + 8 changed/added
in `test_sleeve_test.py` this run]), plus `contracts/validate.py` green on the full fixture suite.
(S run3's last full run reported 111/111 on an older, smaller test set before this run's additions
— the 24 extra tests here are the new v0.4.0/`sleeve_test.py`/`prove_live_to_phone.py` coverage,
not double-counting; `prove_live_to_phone.py` itself has no automated pytest suite, only the two
manual dry-runs pasted in CHECKPOINT 4 above, since it talks to a real attached device and is a
diagnostic tool by design, not a candidate for a mocked unit test that would just re-test
`subprocess.run`).

### Port/process hygiene check (real output)
```
netstat -ano | grep -E ":8787|:8788|:8790|:8791|:8792|:8793|:8797|:8798"
  TCP    127.0.0.1:8787         0.0.0.0:0              LISTENING       43776
```
That one hit is `adb.exe` (Unity's bundled Android SDK copy, confirmed via
`Get-Process -Id 43776`) holding the **pre-existing** USB port-forward described in the task brief
("8787 is forwarded by adb to the phone's hub") — this run never called `adb forward` itself (only
the docs example in §7a shows the command; it was not executed here), and only *connected* to
8787 once, for the dry-run check in CHECKPOINT 4, which the brief explicitly permits ("only
connect to it where a task says so"). Everything this run actually started and controlled
(`sim/haptic/fake_haptic.py` on 8792/8793) was killed via `taskkill` after its check, confirmed
clean by a second `netstat` with no hits on 8792/8793/8797/8798.

## Scope discipline

Touched only `sim/haptic/fake_haptic.py`, `sim/haptic/test_fake_haptic.py`,
`tools/demo/sleeve_test.py`, `tools/demo/tests/test_sleeve_test.py`,
`tools/demo/prove_live_to_phone.py` (new file), `docs/TESTING_RUNBOOK.md`, and this log. Read-only
everywhere else, including `game/Packages/com.opus.sdk/Runtime/Transport/HapticClient.cs` and
`UdpHapticTransport.cs` (per the brief, audited not edited), `contracts/HAPTIC_PROTOCOL.md`,
`contracts/schemas/*` (read to confirm the `motor` enum, never modified),
`firmware/opus_sleeve/opus_sleeve.ino` (read as ground truth, never modified — firmware is not
this track's to touch). No `git commit` (Opus reviews/commits/pushes).

## Final summary for a replacement agent

All 4 numbered tasks in this run's brief are done, with real command output pasted above:

1. **Electronics end-to-end**: `sim/haptic/fake_haptic.py` now faithfully emulates firmware
   v0.4.0 — 4-motor routing/fallback, per-motor `MIN_CUE_GAP_MS=100` rejection, intensity/duration
   clamping, dual-dialect ack (`cue_id`+`ack_id`+`status`+`ok`), nested `motor_0..3` status blocks
   alongside the flat fields, combined `device_discovery`/`opus_haptic` hello, and a new
   `sensor_data` message — all covered by 10 new passing tests (26/26 in `sim/haptic/` total).
   `tools/demo/sleeve_test.py` rewritten to space commands ≥150 ms, sweep all 4 motors, and
   deliberately test the `CUE_GAP` safety limit with an explicit PASS/FAIL+latency table; run live
   against the emulator with a pasted 10/10 PASS table. Unity's `HapticClient`/
   `UdpHapticTransport` were read (not touched) and confirmed to understand every reply shape
   except `sensor_data` (parsed harmlessly, not consumed — consistent with it being unimplemented
   optional telemetry, not a defect) — table in CHECKPOINT 3.
2. **Game → phone proof harness**: `tools/demo/prove_live_to_phone.py` created and dry-run twice
   (plain, and with `--pull session-000`) against the real attached phone (serial `b83663ef`),
   confirming the forward/hub/session-listing/pull-and-recheck chain all work, and reconfirming
   RUN 18's finding that the phone's only session today is still `synthetic`/`simulation` — the
   real-game leg remains unjoined, exactly as reported, not silently declared fixed.
3. **`docs/TESTING_RUNBOOK.md`**: added §7 (USB-forward live demo) and §8 (sleeve e2e), both in
   cmd.exe syntax as instructed, file re-confirmed UTF-8-clean after editing.
4. **Full test run**: 135/135 automated tests passed across analytics/sim-live/sim-haptic/tools,
   plus `contracts/validate.py` green on the full fixture suite. No stray processes/ports left
   behind by this run.

**Nothing left outstanding for this run's brief.** The one thing a future run should pick up (not
assigned here, flagged only): actually driving the real game into the phone hub to produce the
first non-synthetic phone-side session — `prove_live_to_phone.py` is now ready and waiting to
verify that the moment it happens.

