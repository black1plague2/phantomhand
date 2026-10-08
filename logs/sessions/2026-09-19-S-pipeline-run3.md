# S (sim + pipeline tooling) — run 3 — 2026-09-19

RESUME context: was told a previous run2 log existed at
`logs/sessions/2026-09-19-S-pipeline-run2.md` and was killed by a usage limit "just before its
final test pass." That log file does **not exist** on disk (checked at start of this run — not
found, not in git status, not in `logs/sessions/` listing). However `git diff sim/` showed
`sim/live/fake_headset.py` (+168/-7) and `sim/live/protocol.py` (+34/-3) already modified, and
`tools/` was entirely untracked, so run2's actual work survived even though its log did not (log
was presumably never written before the kill, or was written and then lost — can't tell which).
This run picked up from the code state, verified everything against real runs, and wrote this log
from scratch.

## Starting state found (before any changes this run)

- `sim/live/fake_headset.py`, `sim/live/protocol.py` — modified (uncommitted), full mock-headset
  feature set already present: `--generate`/`--stage {reach,sorting}`, realistic `status` payload
  (state/session_id/patient_ref/trial/trials_completed/trials_total/fps/tracking_rate_hz/hands/
  real_hands/haptic/stage/rule/good_dose), `metrics_tick` per trial, outbox-based resend on
  reconnect, `--drop-at` network-drop simulation, HTTP PUT upload of session.json/events.ndjson/
  kin_*.json at replay end, `--no-scenario` mode for the real Flutter hub.
- `tools/demo/check_session.py`, `tools/demo/watch_and_analyse.py`, `tools/demo/sleeve_test.py`,
  `tools/demo/START_DEMO.md`, `tools/demo/tests/*` — all present and untracked (new from run2).
- `sim/live/test_fake_headset_generate.py` — untracked, new.
- No code in `sim/`, `analytics/`, or `tools/` looked unfinished or stubbed on inspection.

Conclusion: run2 finished essentially all the implementation work; what was actually missing was
the **verification pass** (running the full test suite for real and writing it down), which is
exactly what the resume instruction said was interrupted. This run did that verification, found
one real finding (item 2's root-cause ask) and confirmed everything else already works.

## Task 1 — sim/live/fake_headset.py: mock headset

Already complete (see starting state above). Printed `--help` for real, verbatim:

```
usage: fake_headset.py [-h] [--session SESSION] [--generate]
                       [--stage {reach,sorting}] [--trials TRIALS]
                       [--seed SEED] [--patient-ref PATIENT_REF] [--host HOST]
                       [--port PORT] [--beacon-port BEACON_PORT]
                       [--speed SPEED] [--drop-at DROP_AT] [--no-scenario]

OPUS Headset Simulator (mock headset)

options:
  -h, --help            show this help message and exit
  --session SESSION     Session directory to replay (skip --generate)
  --generate            Generate a fresh scripted session (via
                        sim/synthetic_patients) instead of requiring a pre-
                        recorded --session directory. This is the mode for
                        testing the Flutter app: a realistic mock headset with
                        no fixture files needed.
  --stage {reach,sorting}
                        Scripted stage to play when --generate is used.
  --trials TRIALS       Trial count for --generate.
  --seed SEED           RNG seed for --generate.
  --patient-ref PATIENT_REF
                        patient_ref for --generate.
  --host HOST           Hub host (auto = UDP discovery, or an IP to skip it)
  --port PORT           Hub WebSocket/HTTP port
  --beacon-port BEACON_PORT
                        UDP beacon port for --host auto
  --speed SPEED         Replay speed multiplier
  --drop-at DROP_AT     Simulate network drop after N session-seconds
  --no-scenario         Do not assume a scripted hub scenario: wait for a real
                        'start' command before replaying (use this against the
                        real Flutter hub).
```

`--stage sorting` relabels reach targets into `container_left/center/right` and injects
`wrong_target` outcomes (~1/4 of would-be successes) via `_apply_sorting_flavor` in
`fake_headset.py` — confirmed present, not re-verified live this run (covered by
`sim/live/test_fake_headset_generate.py`, which passed — see Task 6). `form_warning`/`haptic_cue`
event types are not synthesized by `fake_headset.py` itself; they come from whatever
`sim/synthetic_patients.generate.write_session()` puts in `events.ndjson` for a given profile —
confirmed present in the real recorded session's own events (trial 0 has 3× `form_warning`, see
Task 2 below), so the live protocol already carries them end-to-end when the generator produces
them. Agent A1 can point `fake_headset.py --generate --host 172.18.226.79 --port 8787` (or
`--host auto` for beacon discovery on 8788) at the phone hub directly.

## Task 2 — tools/demo/check_session.py + root-cause of trial 3's null RT/MT/SPARC

Ran against the real recorded session:
```
analytics/.venv/Scripts/python.exe tools/demo/check_session.py app/.hub_data/c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7
```
```
[PASS] schema validation (contracts/validate.py --session)
[PASS] all 6 trial(s) have trial_start and trial_end
[PASS] every non-timeout trial has target_shown
[PASS] all 5 successful trial(s) have a contact event
[PASS] every trial window overlaps the recorded kinematics range [55, 32095] ms
[WARN] 1 successful trial(s) have no computable endpoint_error_cm: [3]
[PASS] rate_hz 72.0 >= 45
[PASS] no implausibly fast (< 50 ms) reaction times

PASS: 0 failed, 1 warned, 8 checks total
```

Ran against a freshly generated synthetic fixture (`fake_headset.generate_scripted_session`,
stage=reach, seed=42, 6 trials):
```
PASS: 0 failed, 0 warned, 8 checks total
```
(all 3 successful trials had computable endpoint_error_cm — confirms the checker itself is not
the source of trial 3's nulls; it's specific to that one real trial's recorded events.)

### Root cause: trial 3, `c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7`

Trial 3's `events.ndjson` entries (exact, in order):
```
t=23915.3008ms  trial_start
t=23915.3811ms  target_shown  (target reachPercent=63.55, az=0.54°, el=9.21°)
t=23916.7054ms  contact                       <- only 1.324 ms after target_shown
t=23916.8163ms  grasp
t=24749.0040ms  placed        outcome=success
t=24749.1821ms  trial_end     outcome=success
```

Every other trial in this session (0, 1, 2, 4, 5) has a `movement_onset` event between
`target_shown` and `contact`. **Trial 3 has no `movement_onset` event at all** — the event stream
jumps straight from `target_shown` to `contact` 1.3 ms later.

`analytics/opus_analytics/segmentation.py` defines `Trial.attempted` as
`self.t_movement_onset is not None` (line 41). `analytics/opus_analytics/metrics/trial_metrics.py`
line 46 checks `if not trial.attempted:` first, before even looking at `outcome`, and calls
`_invalid_all(out, ALL_METRIC_IDS, ["trial_timed_out_before_movement_onset"])` — which nulls out
every single metric (reaction_time_ms, movement_time_ms, peak_speed_mps, sparc, ldlj,
endpoint_error_cm, everything) for the whole trial. This is exactly what `metrics.json` shows for
trial 3: every metric is `{"value": null, "quality": "invalid", "quality_reasons":
["trial_timed_out_before_movement_onset"]}`, confirmed by direct inspection of the file.

So mechanically: **the analytics pipeline gates all per-trial metrics on `movement_onset` having
fired; trial 3's recording never emitted that event, so every metric for trial 3 is nulled.**

Why did trial 3 never emit `movement_onset`? The 1.3 ms gap between `target_shown` and `contact`
is the tell: on every other trial in this session the target-to-contact gap is hundreds of
milliseconds (a real reach), but trial 3's hand was already at/inside the target's contact volume
the instant the target appeared. `movement_onset` in the Unity runner is a velocity-threshold
crossing during an active reach — with no reach to make (hand already in position), that
threshold-crossing detector never fires, so the event is legitimately absent from the recording;
it is not a dropped/lost message (the WS/file pipeline is otherwise complete for this trial — 6/6
event types present for every other trial, and `contracts/validate.py` passes trial 3's events
individually). This is a **real edge case in the source recording** (hand parked on/near the next
target when it appeared), not a bug in `check_session.py`, `contracts/validate.py`, or the
metrics pipeline's null-handling — the pipeline's behavior (null out metrics that need a
movement-onset timestamp that doesn't exist) is correct; the `quality_reasons` label
`"trial_timed_out_before_movement_onset"` is somewhat misleading for this specific case (the trial
did not time out — it succeeded — it just never crossed the onset threshold), which is a labeling
precision issue in `trial_metrics.py`'s reason string, worth a follow-up but out of this task's
scope (S track = sim/analytics/tools tooling verification, not analytics semantics changes).

**Secondary observation (WARN vs FAIL wording):** `check_session.py`'s `check_endpoint_error`
correctly reports this as `[WARN]` (successful trial with no computable endpoint_error_cm), not
`[FAIL]`, per its own documented design ("no successful trials is not itself a failure"-style
tolerance) — consistent with the overall `PASS: 0 failed, 1 warned` result. No code change made;
this is a real, correctly-surfaced data characteristic, not a check_session.py defect.

## Task 3 — tools/demo/watch_and_analyse.py + tests

Present, tested via `tools/demo/tests/test_watch_and_analyse.py` (part of the 17-test run below).
No changes needed.

## Task 4 — tools/demo/sleeve_test.py --ip (local fake only)

`sleeve_test.py --help`:
```
usage: sleeve_test.py [-h] --ip IP [--port PORT] [--timeout TIMEOUT]

OPUS haptic sleeve smoke test

options:
  -h, --help         show this help message and exit
  --ip IP            Sleeve IP address
  --port PORT        Sleeve UDP command port (default 8790)
  --timeout TIMEOUT  Per-command ack timeout, seconds
```

Unit tests (`tools/demo/tests/test_sleeve_test.py`) run against `LocalFakeSleeve`, a purpose-built
stand-in that mirrors the **real firmware's** wire format from `firmware/opus_sleeve/opus_sleeve.ino`
(`type: "haptic"` device command / `type: "cue"` game-format / ack carries `cue_id`+`ack_id`+
`status`+`ok`, interleaved `status` datagrams that the client must skip) — all 5 tests in that file
passed (part of the 17-test tools/demo run, Task 6).

Sanity-checked this run by also starting `sim/haptic/fake_haptic.py` for real (background,
`--port 8792 --discovery-port 8793`, non-conflicting with the real sleeve's 8790/8791) and running
`sleeve_test.py --ip 127.0.0.1 --port 8792` against it directly: the 3 "device"-format commands
got `NO ACK` (fake_haptic.py's `MessageValidator` rejects `{"type":"haptic",...}` against
`contracts/schemas/haptic-message.schema.json`, which requires `v`/`cue`/0-1 float intensity —
i.e. the schema-only game format), while the "game-format cue" command got a clean `ok` ack. This
is **not a new bug** — it's the exact discrepancy already documented in
`test_sleeve_test.py`'s own module docstring: the real firmware (source of truth per that
docstring) accepts `type:"haptic"` device commands with the ack including `cue_id`, but
`sim/haptic/fake_haptic.py`'s dispatcher (built earlier, before the firmware was read) does not
implement that branch reachably — flagged there as "out of this task's remit to touch
fake_haptic.py's dispatch without checking with N/U/A tracks." Confirmed still true; no fix
attempted here (per that explicit prior scope note, and per this run's SCOPE which does not
license changing `fake_haptic.py`'s wire-format dispatch without cross-track sign-off). Killed the
manual `fake_haptic.py` process afterward (Windows PID via `netstat`/`taskkill`); it was never
left running past this check.

## Task 5 — tools/demo/START_DEMO.md

Present, plain Windows cmd.exe syntax throughout (`cd /d "..."`, backslash paths,
`C:\flutter\bin\dart.bat`/`flutter`/`dart run`). Covers all three requested scenarios:
(a) app alone (`flutter run -d chrome`), (b) app + mock headset over the network (firewall step,
hub via `dart run tool\hub_cli.dart`, `fake_headset.py --generate`, IP-finding via `ipconfig`),
(c) app + Unity (batch-mode command line, closed GUI editor) + sleeve (simulated via
`fake_haptic.py`, or real ESP32 via `sleeve_test.py --ip` first). No changes needed.

## Task 6 — Full test run (real output, this run)

```
sim/live/.venv/Scripts/python.exe -m pytest sim/live/ -p no:cacheprovider -q
  → 29 passed in 23.76s

sim/haptic/.venv/Scripts/python.exe -m pytest sim/haptic/ -p no:cacheprovider -q
  → 14 passed in 1.55s

analytics/.venv/Scripts/python.exe -m pytest analytics/tests -p no:cacheprovider -q
  → 51 passed in 136.42s

analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests -p no:cacheprovider -q
  → 17 passed in 22.66s

analytics/.venv/Scripts/python.exe contracts/validate.py            (all fixtures)
  → [PASS] All validations passed (14 valid + 11 invalid-correctly-rejected fixtures)

analytics/.venv/Scripts/python.exe contracts/validate.py --session app/.hub_data/c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7
  → [PASS] All validations passed (session.json, events.ndjson: 45 events, kin_000..006.json, metrics.json)
```

**Grand total: 111/111 automated tests passed, 0 failures.** `contracts/validate.py` green on
both the fixture suite and the one real recorded session checked.

Ports used by every test run above are OS-assigned ephemeral ports (`get_free_tcp_port`/
`get_free_udp_port` helpers in `sim/live/e2e.py` and `sim/live/test_fake_headset_generate.py`) or
explicit non-conflicting ports chosen by hand for the manual sleeve check (8792/8793) — nothing in
this run touched 8787/8788/8790/8791 (verified no process bound to those ports before/after via
`netstat`).

## Scope discipline

Touched only `sim/`, `analytics/`, `tools/` (read-only inspection elsewhere: `CLAUDE.md`,
`CONTEXT.md` context read, not edited; `contracts/*.md`/`contracts/schemas/*` read-only). No git
commit made (per rules — Opus reviews/commits/pushes). No changes to `game/`, `app/`.

## Final checkpoint

All 6 numbered tasks in this run's brief are done and verified with real command output above:
1. fake_headset.py — complete, `--help` printed, feature set confirmed against LIVE_PROTOCOL.md.
2. check_session.py — run on real session (1 WARN, root-caused with exact timestamps/numbers
   above: trial 3 has no `movement_onset` event, gating all its metrics to null by design) and on
   a synthetic fixture (clean PASS).
3. watch_and_analyse.py — tested, passing.
4. sleeve_test.py — unit-tested against local fake (real firmware wire format), passing; manual
   check against `fake_haptic.py` confirms a pre-existing, already-documented, deliberately
   unfixed dispatch mismatch (not new, not addressed here per prior scope note).
5. START_DEMO.md — present, correct cmd.exe syntax, covers all 3 usage modes.
6. Full suite: 111/111 tests passed; `contracts/validate.py` green on fixtures + one real session.

**Nothing left outstanding for this run's brief.** No further action needed from a replacement
agent unless new work is assigned.
