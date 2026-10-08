# Session: 2026-09-14 Track S run2 (OPUS Live Protocol simulation tools -- fix + prove)

**Agent:** Sonnet 5
**Task:** Opus review found the run1 (Haiku) deliverables for `sim/live/*` were never actually exercised end to end. Make them genuinely work and prove it with real command output.
**Status:** COMPLETE

---

## CHECKPOINT 0: Read brief, contracts, and run1 state

- Read `docs/agent-briefs/_COMMON.md`, `contracts/LIVE_PROTOCOL.md`, `contracts/schemas/live-message.schema.json` + `event.schema.json` + `program.schema.json`, `logs/sessions/2026-09-14-S-live-tools.md`, and all of `sim/live/*.py`.
- Confirmed baseline: `sim/live/.venv/Scripts/python.exe -m pytest test_protocol.py -q` -> **24 passed** (unchanged throughout this session).
- Confirmed `analytics/.venv/Scripts/python.exe` exists and `contracts/validate.py --session <dir>` treats `metrics.json` as optional (`[WARN]`, not `[FAIL]`).
- Read the fixture session at `contracts/fixtures/sessions/healthy/` (46 events, 3 kin chunks, ~13.1s of session-clock time).

## Defects confirmed and fixed

1. **`e2e.py` never ran standalone.** `import pytest` at module scope made `"pytest" in sys.modules` always true, so `main()` under `if __name__ == "__main__"` hit the `else` branch's dead code path and exited 0 having done nothing. Fixed: removed the module-level `import pytest`, removed the `sys.modules` guard entirely, and made `__main__` always call `asyncio.run(main())` and `sys.exit()` the real result.

2. **Wrong contracts path.** `Path(__file__).parent.parent / "contracts"` from `sim/live/e2e.py` resolves to `sim/contracts` (doesn't exist). Fixed to `Path(__file__).resolve().parents[2] / "contracts"` (`sim/live/e2e.py` -> parents[2] = repo root). Also fixed the equivalent bug already latent in `fake_hub.py`/`fake_headset.py` (they used `parent.parent`, which happened to accidentally work only because they live one level deeper than the original e2e.py assumed -- verified and left `parents[2]`/`parents[1]` correct for their actual location `sim/live/`).

3. **`results["success"] = True` was hardcoded**, and the "latency" computed in run1 was actually meaningless: it diffed `headset.state.sent_ts` against `headset.state.recv_ts`, but those two dicts share almost no keys in common (a `trial_event` a headset sends is never individually replied to with the same id; only `hello`/`command`/`ping` respond by *new* message id). Rewrote `e2e.py` (`sim/live/e2e.py`) to actually assert, using the real `FakeHub`/`FakeHeadset` classes:
   - hub sees **zero invalid inbound messages** (`hub.state.invalid_count == 0`)
   - every `trial_event` in `events.ndjson` is delivered to the hub **exactly once**, payload-for-payload identical (checked by `seq`), even across a real network drop + reconnect
   - every `command` sent by the hub is **ack'd within 1s** (`hub.state.command_ack_over_1s` must be empty)
   - **command→ack** and **event send→receive** latencies reported as real p50/p95/max (headset send ts vs hub recv ts, matched by message id -- both processes share one clock since this is in-process)
   - every uploaded file is **sha256-identical** to the source file, and the uploaded session directory **passes `analytics/.venv/Scripts/python.exe contracts/validate.py --session <dir>`** (exit code 0; `metrics.json` correctly optional)
   - the drop scenario: real network drop (actually closes the WebSocket), buffered `trial_event`s replayed after reconnect via the headset's outbox, hub de-dups by message id -> **0 duplicates counted**, `headset.state.resends > 0` proven

4. **Missing brief requirements**, now implemented:
   - `fake_hub.py`: validates every inbound message against the schema and now actually **counts invalid ones** (`HubState.invalid_count`/`invalid_messages`); tracks trial_events received (de-duped by message id, `HubState.received_trial_events`); tracks command→ack latency via `HubState.pending_command_acks` / `command_ack_latencies_ms` / `command_ack_over_1s`.
   - `fake_headset.py`: **pause now genuinely freezes the replay clock** (see bug #6 below) instead of just pausing the event-send loop while `_elapsed_ms()` kept ticking. **stop** cleanly breaks both the event loop and the status loop.
   - **Ports are fully configurable** everywhere and `e2e.py` picks free ephemeral TCP/UDP ports per run (`get_free_tcp_port()`/`get_free_udp_port()`, bind-to-0-then-close) instead of the fixed `8787`/`8788`, so it never collides with a real Flutter hub someone else is running on 8787.
   - Added a **real UDP-beacon discovery e2e variant** (`run_e2e_discovery` / `test_e2e_discovery`) using `--host auto` instead of a fixed `--host`. It passed for real on this machine (see run output below); if it can't find the hub within the timeout in some other environment it reports that explicitly and is skipped (not silently green), with a note in `docs/MANUAL_TODO.md`.

## Additional real defects found while proving this end-to-end (not on the original list)

5. **`UDPDiscovery.discover()` blocked the whole asyncio event loop.** It was an `async def` that called the blocking `socket.recvfrom()` directly. Harmless when hub and headset are separate OS processes, but when both run in the *same* process/event loop (exactly what the discovery e2e test does, and exactly the kind of thing a test harness should do), the blocking call starved the hub's own beacon-sending task, so no beacon was ever sent while discovery was "listening" -- 0% discovery success in-process. Fixed by moving the blocking recv loop into `loop.run_in_executor(...)`. Verified with a minimal repro before and after the fix (see below).

6. **Pause didn't freeze the replay clock.** `_elapsed_ms()` used raw wall-clock time with no accounting for time spent paused, so on resume the session clock had silently jumped forward by however long the pause lasted -- trial timing would have been wrong in any real pause/resume cycle. Fixed by tracking `total_paused_ms` (+ `paused_at`) and subtracting it from elapsed time; verified with a standalone script showing the clock reading identically before and immediately after a 1s pause, then correctly resuming from where it left off (see checkpoint 2 below).

7. **sha256 upload mismatch.** The headset uploaded `json.dumps(session_data)`/`"\n".join(json.dumps(e) ...)` -- a re-serialization of the parsed JSON, which is never byte-identical to the source file (whitespace, float repr, key order) even though the *content* is equivalent. The brief explicitly requires sha256 equality to the source, and the hub's own idempotency check (`200`/`201`/`409` by sha256) depends on this. Fixed by reading and uploading the exact source bytes (`_session_raw_bytes`, `_events_raw_bytes`) instead of re-encoding.

8. **Hub's scripted "basic" scenario timing was wall-clock, not session-clock-aware.** The hardcoded `pause after 5s -> resume after 2s -> stop after 5s` (12s total) only happened to work because run1 only ever tested at `speed=10` (1.31s of real replay time needed). At `speed=1` (~13.1s of real replay time needed), the hub sent `stop` at t=12s real -- **before** the headset finished replaying, clipping the session at 38/46 events. This is a real protocol-adjacent bug: a hub with a fixed scenario timer can truncate a real session. Fixed by adding `FakeHub(scenario_timing=...)` (assign_wait/pre_pause/pause_hold/post_resume, all in real seconds) and having `e2e.py` size `pre_pause`/`post_resume` from the actual fixture length divided by `speed`, with margin. Confirmed fixed: speed=1 run now delivers 46/46 both with and without a drop.

9. **Outbox was imported but never used** in either `fake_hub.py` or `fake_headset.py` (dead import in run1). Now genuinely used in `fake_headset.py`: every `trial_event` goes into `self.trial_outbox` before attempting to send; on reconnect (after a fresh `hello`/`hello_ack`), `flush_trial_outbox()` resends everything in the outbox (same message ids) so the hub's id-based de-dup gives exactly-once delivery. This directly implements LIVE_PROTOCOL.md's "each side keeps an outbox of ... all trial_events since the last hello_ack" / "peer replays anything after it" / "receivers de-dup by id" rules.

10. **`reconnect()` used the pre-resolution `self.host`** (which could literally be the string `"auto"`) and a hardcoded port, so a real drop+reconnect against a discovered (non-127.0.0.1) hub would have tried to connect to the literal host `"auto"`. Fixed: `connect()` now stores `resolved_host`/`resolved_port`, and `reconnect()` uses those.

## CHECKPOINT 1: fake_hub.py and fake_headset.py fixed

Files touched:
- `sim/live/fake_hub.py` -- invalid-message counting, trial_event de-dup tracking, command-ack latency tracking, configurable `beacon_port`, configurable `contracts_dir`/`out_dir` (for test isolation), fixed UDP beacon to also try computed local subnet broadcasts + explicit loopback (was hardcoded to `192.168.1.255`), `scenario_timing` parameterization, `stop()` now cancels the scenario task and cleans up the runner.
- `sim/live/fake_headset.py` -- `--port`/`--beacon-port` CLI flags, `--no-scenario` mode (waits for a real `start` command, up to 30s, before replaying; raises with a clear error if none arrives), pause/resume clock accounting fix, trial_event outbox + resend-on-reconnect, fixed blocking `UDPDiscovery.discover()`, `discover_hub()`/`connect()`/`reconnect()` now consistently track and reuse the resolved host+port, `print_report()`/`summary()` give a clean machine- and human-readable exit summary.

Verified how (real output, not claimed):
```
$ .venv/Scripts/python.exe -m pytest test_protocol.py -q
24 passed in 1.43s
```
Standalone repro of bug #5 (blocking discovery starves the beacon task), before the fix: 0/1 discoveries succeeded in-process. After the fix (`run_in_executor`): confirmed working, see checkpoint 3.

Standalone repro of bug #6 (pause clock), via a small script driving `FakeHeadset._elapsed_ms()` directly:
```
elapsed before pause 308.40ms
elapsed while paused (should be ~same) 308.40ms   <- clock genuinely frozen
elapsed after resume (~0.5s real minus 1s paused) 521.81ms
```

## CHECKPOINT 2: e2e.py rewritten with real assertions

`sim/live/e2e.py` rewritten from scratch: ephemeral ports, real `Checks` collector (reports every failing assertion, not just the first), the 6 assertion groups listed under defect #3/#4 above, a `run_e2e_discovery()` variant, `test_e2e_basic`/`test_e2e_with_drop`/`test_e2e_discovery` pytest functions (plain `def` + `asyncio.run(...)`, no pytest-asyncio dependency needed for these three, though pytest-asyncio stays pinned in requirements.txt since `test_protocol.py` uses it), and a `main()`/`sys.exit()` standalone path with `--speed`.

Negative-control check that the harness actually fails on real failure (not another rubber stamp): monkey-patched `ANALYTICS_PY` to a nonexistent path and re-ran `run_e2e_scenario` -- got `success: False` with the specific failing check named, confirming the assertions are load-bearing.

## CHECKPOINT 3: Real runs, both scenarios, both speeds, plus discovery -- full evidence

### `python e2e.py --speed 10` (standalone), exit code 0

```
=== E2E SCENARIO: basic (drop_at=None, speed=10.0) ===
[E2E] Hub started on 127.0.0.1:57922
[E2E] Headset connected
[E2E] Hello sent
[E2E] Replay complete
--- checks: basic drop_at=None ---
  [PASS] hub saw 0 invalid messages (got 0: [])
  [PASS] no duplicate seqs counted at hub (dup seqs: [])
  [PASS] hub received 46 unique trial_events, expected 46
  [PASS] received payloads match source events (mismatched seqs: [])
  [PASS] all 3 command acks arrived within 1s (over_1s=[])
  [PASS] measured event send->receive latency for 46 events
  [PASS] all expected files uploaded (expected ['session.json', 'events.ndjson', 'kin_000.json', 'kin_001.json', 'kin_002.json'], got [...])
  [PASS] uploaded files sha256-match source (mismatches: [])
  [PASS] contracts/validate.py --session exits 0 (got 0); stdout=[PASS] .../session.json
[PASS] .../events.ndjson: 46 events validated
[PASS] .../kin_000.json
[PASS] .../kin_001.json
[PASS] .../kin_002.json
[WARN] .../metrics.json not found

[PASS] All validations passed

Test 1 Results (excerpt): command_ack_latency_ms {p50: 4.13, p95: 7.03, max: 7.03, count: 3}
                          event_latency_ms       {p50: 2.32, p95: 5.08, max: 7.10, count: 46}
                          hub_invalid_count: 0, success: true

=== E2E SCENARIO: basic (drop_at=2.0, speed=10.0) ===
--- checks: basic drop_at=2.0 ---
  [PASS] hub saw 0 invalid messages (got 0: [])
  [PASS] no duplicate seqs counted at hub (dup seqs: [])
  [PASS] hub received 46 unique trial_events, expected 46
  [PASS] received payloads match source events (mismatched seqs: [])
  [PASS] all 1 command acks arrived within 1s (over_1s=[])
  [PASS] measured event send->receive latency for 46 events
  [PASS] all expected files uploaded [...]
  [PASS] uploaded files sha256-match source (mismatches: [])
  [PASS] contracts/validate.py --session exits 0 [...] All validations passed
  [PASS] headset resent >=1 trial_event from its outbox after reconnect (resent 13)

Test 2 Results (excerpt): resends: 13, hub_invalid_count: 0, success: true
                          command_ack_latency_ms {p50: 1.0, p95: 1.0, max: 1.0, count: 1}
                          event_latency_ms       {p50: 46.2, p95: 47.9, max: 48.1, count: 46}

=== E2E SCENARIO: discovery (UDP beacon, --host auto) ===
--- checks: discovery ---
  [PASS] beacon advertised the correct port (expected 57933, got 57933)
  [PASS] headset.discover_hub() resolved a host (172.17.99.2)
  [PASS] headset completed hello/hello_ack handshake over the discovered address

=== E2E TEST SUMMARY ===
Test 1 (no drop):        PASS
Test 2 (with drop):      PASS
Test 3 (UDP discovery):  PASS

Latency (no drop): event send->recv p50=2.3ms p95=5.1ms max=7.1ms | command->ack p50=4.1ms p95=7.0ms max=7.0ms
Latency (with drop): event send->recv p50=46.2ms p95=47.9ms max=48.1ms | command->ack p50=1.0ms p95=1.0ms max=1.0ms
```
Real elapsed wall time for the whole run: **18.7s**. `echo $?` -> **0**.

### `python e2e.py --speed 1` (standalone), a real ~13s session, exit code 0

(First attempt at speed=1 caught bug #8 above: Test 1 genuinely FAILED with `hub received 38 unique trial_events, expected 46` because the hub's fixed-timing scenario sent `stop` before the headset finished replaying. Fixed scenario_timing scaling, re-ran:)

```
=== E2E TEST SUMMARY ===
Test 1 (no drop):        PASS
Test 2 (with drop):      PASS
Test 3 (UDP discovery):  PASS

Latency (no drop): event send->recv p50=2.0ms p95=4.0ms max=11.6ms | command->ack p50=3.0ms p95=4.1ms max=4.1ms
Latency (with drop): event send->recv p50=11.7ms p95=16.1ms max=17.1ms | command->ack p50=2.5ms p95=2.5ms max=2.5ms
```
Real elapsed wall time: **56.1s** (both scenarios well over the "≥20s session" requirement; the drop scenario alone is ~30s of real time at speed=1 given the 3s drop + scaled scenario timing). `echo $?` -> **0**.

### `pytest test_protocol.py e2e.py -v`

```
============================= test session starts =============================
collecting ... collected 27 items

test_protocol.py::TestSchemaRegistry::test_load_schemas PASSED           [  3%]
... (all 24 protocol tests) ...
test_protocol.py::TestOutbox::test_clear PASSED                          [ 88%]
e2e.py::test_e2e_basic PASSED                                            [ 92%]
e2e.py::test_e2e_with_drop PASSED                                        [ 96%]
e2e.py::test_e2e_discovery PASSED                                        [100%]

============================= 27 passed in 16.05s =============================
```

## CHECKPOINT 4: `fake_headset.py` against a "real" (non-scripted) hub, and CLI-level checks

Ran `fake_hub.py` and `fake_headset.py` as separate CLI processes (`python -m fake_hub --port 18800 --beacon-port 18801`, `python -m fake_headset --host 127.0.0.1 --port 18800 --no-scenario`) to prove the CLI flags work, not just the in-process classes:

```
[WS] Connecting to ws://127.0.0.1:18800/opus/v1/live
[WS] Connected
[MSG TX] type=hello ...
[MSG RX] type=hello_ack ...
[PROTOCOL] Pairing acknowledged
[REPLAY] Starting session replay
[REPLAY] --no-scenario: waiting for a real 'start' command from the hub
```
Confirmed: with no scripted scenario running on the hub, `--no-scenario` correctly blocks on a real `start` (does not self-drive replay), rather than the old behavior (starts replaying unconditionally regardless of what the hub actually said). `GET /opus/v1/health` on the CLI hub also returned `{"status": "healthy"}`.

Separately (in-process, to exercise a *manually driven* "real-hub-like" flow end to end since there's no real Flutter hub yet): started a hub with `scenario=None`, connected a `FakeHeadset(no_scenario=True)`, manually sent a `command("start")` through the hub object (simulating a real hub's UI action), then a `command("stop")` 1s later:

```
=== HEADSET REPORT ===
Device: headset-000  Session: no-scenario-test
Events replayed: 46  Resent after reconnect: 0
Stopped cleanly: True
hub received trial_events: 36
hub invalid: 0
```
36/46 events is *correct* here (not a bug): `stop` was deliberately sent only 1s after `start` at real-time speed, so the session was legitimately cut short by an explicit stop command, which the headset honored immediately and uploaded whatever had been captured so far -- exactly the intended "stop is immediate" semantics from LIVE_PROTOCOL.md.

## Remaining limitations / manual gaps (see docs/MANUAL_TODO.md)

- UDP broadcast discovery is proven working on **this machine, loopback-only** (both `255.255.255.255` and the machine's own subnet broadcasts were delivered locally). Cross-machine broadcast on a real LAN (two physical/different Windows boxes, real Wi-Fi/Ethernet) is **not** tested here and may be blocked by the Windows Defender Firewall profile (Public networks block inbound UDP by default) -- needs a human with two machines on the same LAN to verify, or to open the firewall rule.
- `fake_headset.py --no-scenario` was verified against a hand-driven "real-hub-like" flow (see checkpoint 4) and against a real CLI hub process waiting correctly for `start`; it has **not** been run against the actual Flutter hub app yet since that hasn't been built/started by the Flutter-track agent as of this session. When it is (on port 8787 per the brief), run: `python -m fake_headset --session contracts/fixtures/sessions/healthy --host <flutter-hub-ip> --port 8787 --no-scenario --speed 1`.
- `FakeHub`'s scripted `"basic"` scenario is a **test fixture**, not a stand-in for real clinician behavior -- its pause/resume/stop timing is still wall-clock and scaled from the *known* fixture length in `e2e.py`; a real hub obviously doesn't need this since a human decides when to pause/stop.
- Kinematics-chunk files, `metrics_tick`, and `file_available` messages are not deeply exercised by this harness beyond upload/sha256 -- LIVE_PROTOCOL.md's `file_available` message type (headset notifying the hub via the live channel that a chunk was uploaded) is not sent at all by `fake_headset.py`; only the HTTP PUT itself happens. Flagged here rather than silently left out.

## Files touched this session

- `sim/live/fake_hub.py` -- rewritten (see defects 4, 8, 9 above)
- `sim/live/fake_headset.py` -- rewritten (see defects 4, 5, 6, 7, 9, 10 above)
- `sim/live/e2e.py` -- rewritten (see defects 1, 2, 3, 4 above)
- `sim/live/README.md` -- added Status section and real latency table
- `logs/sessions/2026-09-14-S-live-tools-run2.md` -- this file

No changes to `sim/live/protocol.py` or `sim/live/test_protocol.py` were needed; all 24 existing unit tests still pass unmodified.

## Proposed CHANGELOG lines (for Opus)

- Fix: `sim/live/e2e.py` was a no-op when run standalone (`import pytest` made a guard always true) and asserted nothing real; rewritten with genuine protocol assertions (exactly-once delivery, ack timing, sha256 file integrity, contracts/validate.py pass, resume-after-drop with zero duplicates) and ephemeral ports.
- Fix: `sim/live/fake_headset.py` pause no longer lets the replay clock drift; UDP hub discovery no longer blocks the event loop; trial_events now genuinely resume via an outbox after a reconnect; uploaded files are now byte-identical (sha256-correct) to source.
- Fix: `sim/live/fake_hub.py` now counts invalid inbound messages and tracks command-ack latency; its scripted test scenario's timing scales with session length/replay speed instead of a hardcoded 12s that clipped real-time (speed=1) sessions early.
- Add: `fake_headset.py --no-scenario` flag for driving the real Flutter hub (waits for an actual `start` command instead of assuming the sim's scripted scenario).

## Next step

- Point `fake_headset.py --no-scenario` at the real Flutter hub once it's running on port 8787, per the brief.
- Get a second machine to verify cross-machine UDP beacon discovery on a real LAN (see MANUAL_TODO.md).
- Consider implementing the `file_available` live-channel notification in `fake_headset.py` for full LIVE_PROTOCOL.md flow fidelity.
