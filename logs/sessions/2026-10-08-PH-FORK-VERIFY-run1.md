# PH-FORK-VERIFY run 1 — independent checks by the second (forked) session (8 Oct 2026)

Who: the forked session [25d7be] of the main Phantom Hand session [dccb7d]. Not committed by me (the main session takes
this file by explicit path). After 84e26b6 I changed no file of the checkout: the two code changes I proposed went to the
main session as patches. The pitch film (`H:\Chenta\brag-output\`, outside the repo) is not part of this log.

## 1. Clean export of the pushed commit (16:23 to 16:57)

`git archive 44bd27a analytics contracts sim tools docs firmware app ... | tar -x` into a scratch folder outside the repo;
the suites ran there with the checkout's venv interpreters by absolute path. It holds for 12ea430 too (that commit only
changes `OpusBuildScript.cs`). Real output:

| Suite | Result |
|---|---|
| `contracts/validate.py` | `[PASS] All validations passed`, exit 0 |
| analytics `pytest` (`analytics/.venv`, cwd = the export, import resolved to the export) | `85 passed in 539.47s` (a Quest build was running) |
| `tools/demo/tests` (repo-root `.venv`) | `164 passed, 8 skipped in 190.47s` |
| `sim` (repo-root `.venv`) | `167 passed in 69.24s` (26 + 94 + 47) |
| L3 `run_pipeline.py --game phantom_hand --sim --no-unity` | 7/7, `PHANTOM HAND L3 GREEN`, exit 0 |
| the same with `--faults` | Fault A 5/5, Fault B 3/3, Fault C 4/4, `PHANTOM HAND L3 GREEN`, exit 0 |
| `flutter test --concurrency=2` (app + contracts + tools side by side) | `01:58 +481: All tests passed!` |

- `git status --short --ignored` outside `game/` and `logs/` lists only venvs, caches and build output: a fresh clone lacks
  no source file and no fixture.
- My mistake, not a defect: an app-only export fails 60 Flutter tests with PathNotFound, because the tests read
  `../contracts/fixtures` and `../tools/demo/tests/fixtures`. Export the three folders together.
- Three failures of `sim/live/test_phantom_replay.py` between 16:36 and 16:40 (WinError 10048 on the twin's bind;
  `{'bio': False}`) were a port clash: that file binds fixed UDP ports (8790 + 12100 and up) and the main session's
  `PH_FullRun` uses the same twin offset. Alone it passed 3 of 3. Two sessions must not run the sim suites and
  `PH_FullRun` at the same time.

## 2. What is inside the Quest APKs (16:58)

A script read the two values of `DevAgentSettings.asset` (keys `serverAddress`, `accessToken`) and searched every zip entry
of each APK for them; it prints key names and counts, never a value.

- `chetna-phantom-hand.apk` (16:31, 85 955 940 bytes): the token has 0 hits in 854 entries; the `DevAgentSettings` object
  is present with no text after its name (fields empty).
- `chetna-phantom-hand-dev.apk` (16:01): a 192.168.x.x address and a 32-character token sit in
  `assets/bin/Data/1b386b3b...`. It still must not be shared.

## 3. Faulted runs of the game in the open editor (17:09 to 17:18)

Tables and the defect are in `2026-10-08-PH-E2E-L3-run1.md`, "A faulted run of the game itself". How it was done, for
whoever repeats it:

- No C# change. `PH_FullRun` enters its "external" mode when `OPUS_PH_HUB` is set in the editor PROCESS; the bridge sets it
  (`IReflectionService`, `System.Environment.SetEnvironmentVariable`, arguments `{"variable": .., "value": ..}`). The
  script removes the variables at the end (value `""`) and reads them back empty.
- The script owns the hub (the harness's `RecordingHub`, port 8787) and the twin (port offset 33000), reads the phase
  from the live status and injects the fault: `off-a` on the twin's control port 27 s into the SYNC induction; or the hub
  stopped for 30 s from 20 s into the witness phase and restarted with its books kept.
- In external mode the test sends no flinch command: the script sends `flinch` at each `threat_impact` it sees at the hub.
- Normal count: the hub gets 204 trial events for 206 events in the file (`session_start` and `session_end` travel as
  `session_started` / `session_ended`).
- Script and evidence: `sim/out/unity_faults_2026-10-08/` (git-ignored): `unity_fault_run.py` (the improved version, with
  `node_b_absent`), the two patches, and per run `run.log`, `result.json`, `status_records.json`, `twin.jsonl`,
  `playmode.json`.

Patches handed over: `1-liveclient-keeps-trial-events-raised-offline.patch` (committed by the main session as a25ab93)
and `2-unity-fault-run-harness.patch` (`OPUS_PH_FAULT` in `PH_FullRun`, `tools/demo/unity_fault_run.py`; the main session
applied its test hunks by hand and will add the tool).

## 4. The phone app's hub and replays (read only, 18:30; nothing run)

- `app/lib/core/hub/hub_server.dart:250` puts into `hello_ack.resume_from_seq` the value the headset sent in its own
  `hello` (the last HUB seq the headset saw). `LiveClient.cs:393` reads that field as "the last HEADSET seq the hub has".
  The two numbers come from different counters, so after a reconnect the headset replays nearly every trial event.
- `hub_server.dart:217` builds a new `HubConnection` for the reconnect and carries only the sent history over
  (`seedHistory`), not the set of ids already seen (`hub_connection.dart:68`). The replays are therefore not
  de-duplicated and reach the listeners again.
- For Phantom Hand this is harmless by reading: `TraceBuffer.addMarker` drops a marker of the same kind within 1 ms, a
  `witness_summary` replaces the previous one, and `phantomLiveRepositoryProvider` rebuilds onto the new connection. It
  would show in the Orchard monitor and in `GET /opus/v1/live/last_status` ("events").
- WRONG as first written here ("the same replay is what refills the card after a reconnect"): see the correction in
  section 7. What I read correctly is the chain that makes the card lose its state:
  `phantom_live_providers.dart:52` keeps one repository per connection object, `phantom_live_screen.dart:68-72` starts a new
  `PhantomLiveModel` when the repository changes, and the witness and the markers only ever arrive as trial events
  (`hub_phantom_live_repository.dart:67-75`; the status replayed on attach has `game_state` and the trace, not the
  witness). So the card's state has to survive a reconnect of the same device, with tests for it (witness still shown;
  `threat_impact` and `emg_burst` markers still there, once each).
- No test in `app/test` has a headset that reconnects. Not changed by me; the main session has a subagent on it (18:40).

## 5. Not done / not verified

- (Closed in section 7: "204 of 204 after a hub outage" with the fix a25ab93, and Fault B with the game.)
- A fault run of the game on a quiet PC: all four fault runs so far ended with the test Inconclusive or failed on
  cue delivery because the host was slow, so none of them says anything about cue delivery.
- Unity in batch mode: not tried (the owner's rule is the open editor).
- Nothing here ran on a Quest or against the real boards.

## 6. How to repeat

- Clean export: section 1, first paragraph; then the commands of the table from the export's root.
- A faulted game run: take `game/.ph_unity.lock`, quiet CPU, then
  `.venv\Scripts\python.exe tools\demo\unity_fault_run.py hub_absent <out dir>` (about 5 minutes;
  `node_a_off`, `node_b_absent`, `none` likewise). Expect `204/204; missing: none` in the Fault C rows.

## 7. Two more faulted runs of the game, on the fixed build (20:07 to 20:16)

By the main session's hand-over ("editor yours", lock FREE): simulator only (the script's own twin at port offset 33000,
hub on 8787), nothing sent to the real boards, no recompile, no build. Checkout clean at 4e72ead. Lock taken 20:07:15,
written FREE 20:16:43; afterwards the editor was not playing, the five `OPUS_PH_*` variables read back empty, no process
of mine was left and nothing listened on 8787 or 41790 to 41793. Evidence: `sim/out/unity_faults_2026-10-08b/<fault>/`
(`run.log`, `result.json`, `status_records.json`, `twin.jsonl`, `playmode.json`), git-ignored.

Commands, from the repository root:

```
.venv\Scripts\python.exe tools\demo\unity_fault_run.py hub_absent sim\out\unity_faults_2026-10-08b\hub_absent
.venv\Scripts\python.exe tools\demo\unity_fault_run.py node_b_absent sim\out\unity_faults_2026-10-08b\node_b_absent
```

Both times the test itself ended Inconclusive ("host too slow to judge cue delivery"): this PC was also running the
Flutter suite of a subagent. Every undelivered cue has the reason `late`; none is `no_ack`; the twin executed every cue
it was sent and rejected none. So the row "Stroke cues acked by the twin" is red for the host's reason in both runs and
says nothing about the pipeline. Not repeated, as agreed.

**hub_absent** (20:07:26 to 20:12:09; session `324f210f-a8e6-4f5d-a115-9af2fa907512`; hub stopped at 200.3 s after the
test's start, back at 230.3 s; test: Inconclusive, 247.2 s; frames over 50 ms took 38.5 % of the run)

| Result | Check | Threshold | Observed |
|---|---|---|---|
| PASS | Fault C: hub absent 30 s: run completes | yes | run complete=True |
| PASS | Fault C: websocket re-established after the outage | a hello after the comeback | 2 hellos; after the comeback: 1 |
| PASS | Fault C: the session's files landed after the hub came back | all, written after the comeback | 83 of 83 files on the hub; 83 written after the comeback |
| PASS | Fault C: every trial_event arrived exactly once after resume | all | **204/204; missing: none** (203/204 before a25ab93) |
| PASS | Phases in order, both conditions | 100 % | 14 phases, 2 conditions (async/sync) |
| FAIL (host) | Stroke cues acked by the twin | >= 98 % | 77.2 % (88/114; 26 dropped as `late`, 0 `no_ack`; twin: 88 executed / 0 rejected) |
| PASS | SYNC visual-to-send timing error | mean <= 20 ms, p95 <= 40 ms | mean 12.5, p95 26.0 ms (n=16) |
| PASS | ASYNC delay | 500-700 ms on >= 95 % of strokes | 100 % of 22 strokes (min 511, max 712 ms; +/-40 ms send tolerance) |
| PASS | Live status RTT to hub | p50 < 250 ms; invalid 0 | p50 5.7 ms, p95 1168.6 ms (n=201; the outage is in the p95); 0 invalid; 364 statuses |
| PASS | Session valid and uploaded to the hub | yes | `validate.py --session` exit 0; 83/83 files; 204/204 trial events exactly once |
| PASS | Analytics writes embodiment with quality flags | yes | flags: degraded 5, missing 7, ok 20 |

**node_b_absent** (20:12:20 to 20:16:22; session `aa71c4b2-c4e7-4890-8bbf-f610ab935d33`; twin started with Node A only;
test: Inconclusive, 216.6 s; frames over 50 ms took 16.8 % of the run)

| Result | Check | Threshold | Observed |
|---|---|---|---|
| PASS | Fault B: Node B absent: run completes | yes | run complete=True |
| PASS | Fault B: status shows bio.connected=false throughout, no EMG recorded | yes | 392 statuses bio down, 0 up; emg in sens files=False; emg_burst events=0 |
| PASS | Fault B: flinch EMG flagged missing with a reason in analytics | missing + reason | `{"quality": "missing", "reasons": ["node_absent_bio"]}` |
| PASS | Phases in order, both conditions | 100 % | 14 phases, 2 conditions (async/sync) |
| FAIL (host) | Stroke cues acked by the twin | >= 98 % | 91.2 % (104/114; 10 dropped as `late`, 0 `no_ack`; twin: 104 executed / 0 rejected) |
| PASS | SYNC visual-to-send timing error | mean <= 20 ms, p95 <= 40 ms | mean 10.1, p95 26.1 ms (n=20) |
| PASS | ASYNC delay | 500-700 ms on >= 95 % of strokes | 100 % of 25 strokes (min 514, max 725 ms; +/-40 ms send tolerance) |
| PASS | Live status RTT to hub | p50 < 250 ms; invalid 0 | p50 5.0 ms, p95 35.4 ms (n=208); 0 invalid; 392 statuses |
| PASS | Session valid and uploaded to the hub | yes | `validate.py --session` exit 0; 82/82 files; 202/202 trial events exactly once (no `emg_burst` events in this run) |
| PASS | Analytics writes embodiment with quality flags | yes | flags: missing 13, ok 19 |

With section 3 this makes the three faults of the L3 table played by the game itself: Node A off mid-run, Node B absent,
hub absent 30 s. In each the run completed and the fault rows are green; cue delivery was never judged (slow host).

**Correction to section 4 (20:15, measured by the main session's reconnect tests, not by me).** I wrote that the headset's
replay refills the Phantom card after a reconnect. It does not: the replay is handled about 10 ms after `hello_ack`,
the card's new repository is built 250 to 300 ms later (the hub republishes its headset list every 250 ms), and
`trialEventStream` is a broadcast stream, so the replay has already gone by. A reconnect in the witness phase therefore
empties the audience mirror already today. The consumers are idempotent as read (the same replay sent once the card is
back makes both tests pass). The main session has a subagent on the smallest fix in `app/lib/features/live`.
