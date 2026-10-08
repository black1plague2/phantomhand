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
- The same replay is what refills the card after a reconnect, so fixing the two deviations alone would break it:
  `phantom_live_providers.dart:52` keeps one repository per connection object, `phantom_live_screen.dart:68-72` starts a new
  `PhantomLiveModel` when the repository changes, and the witness and the markers only ever arrive as trial events
  (`hub_phantom_live_repository.dart:67-75`; the status replayed on attach has `game_state` and the trace, not the
  witness). With replays de-duplicated or no longer sent, a reconnect during the witness phase would leave the audience
  mirror empty. A hub fix needs the card's state to survive a reconnect of the same device, with tests for it
  (witness still shown; `threat_impact` and `emg_burst` markers still there, once each).
- No test in `app/test` has a headset that reconnects. Not changed by me; the main session has a subagent on it (18:40).

## 5. Not done / not verified

- "204 of 204 after a hub outage" with the fix a25ab93: the `hub_absent` run has not been repeated (the editor and
  port 8787 are in use for the real boards).
- Fault B (Node B absent) with the game: needs `OPUS_PH_FAULT=node_b_absent` in the test, then
  `unity_fault_run.py node_b_absent <out dir>`.
- Unity in batch mode: not tried (the owner's rule is the open editor).
- Nothing here ran on a Quest or against the real boards.

## 6. How to repeat

- Clean export: section 1, first paragraph; then the commands of the table from the export's root.
- A faulted game run: take `game/.ph_unity.lock`, quiet CPU, then
  `.venv\Scripts\python.exe sim\out\unity_faults_2026-10-08\unity_fault_run.py hub_absent <out dir>` (about 5 minutes;
  `node_a_off`, `node_b_absent`, `none` likewise). Expect `204/204; missing: none` in the Fault C rows.
