# PH-U-DIAG run 1 (2026-10-08, 22:20 to 23:00 IST, orchestrating session on the build PC): a log channel out of the headset, a "Ready" card, the headset on its own

Why: the owner wanted every log and every file of a headset run to reach the build PC from the standalone APK, with or without a
USB cable, and the app to be testable with no boards and no Mac. Until now the only window into a Quest run was `adb logcat`.

Tags: **[V]** verified this session with the command and output named, **[N]** never run (said so).

## 1. What was built

| Piece | File | State |
|---|---|---|
| Log file on the device, read-only HTTP server on TCP 8796, UDP "here I am" every 2 s, `[health]` line every 5 s | `game/Assets/Shell/Runtime/DeviceDiagnostics.cs` | [V] in the editor (tests below); on a headset: section 4 |
| Switch | `PhantomHandSettings.diagnosticsServer` (default on) | [V] compiles; the off path [N] |
| "Ready" card before a run (three links, how to start, id, address, log port) | `PhantomStandbyCard.cs`, `PhantomHandSceneController.TickStandby` | [V] text by test, picture looked at (`sim/out/quest_diag/ready_card.png`, git-ignored) |
| The scene's one-line view of itself for the health line | `PhantomHandSceneController.DiagnosticsLine` | [V] in the served log of the run below |
| PC side: find the headset, mirror logs and sessions, capture `adb logcat` | `tools/demo/quest_collect.py` | [V] 11 tests against a fake headset; against a real one: section 4 |

Routes of the server: `GET /` or `/?since=<ms>` gives a JSON index (`device`, `boot`, `app`, `now_ms`, `log`, `files` with `path`,
`size`, `mtime_ms`; with `since` only what changed after that time of the headset's clock, the running log always);
`GET /f/<path>?offset=<n>` gives a file's bytes from n. Served: `logs/`, `opus_sessions/`, `phantom_endpoints.json`, nothing else.
Not a GET is 400, anything outside those folders 404, a file the game is rewriting 503. Nothing can be written through it.

Decisions worth knowing:
- The PC connects to the headset, not the other way round: Windows asks for no firewall rule for an outgoing connection, and on this
  PC the Python that would have had to listen is allowed on Public networks only.
- The log is a file first (flushed per line), the network second: what was logged before the Wi-Fi came up, or while nobody
  listened, is fetched later from byte 0. A line that repeats without a break is written once and then counted.
- The PC side was first given to a Sonnet subagent with the protocol as its contract; it produced nothing in 24 minutes and was
  stopped. The orchestrating session wrote the tool itself.

## 2. Tests, with their real output

```
python tools/unity_mcp.py test EditMode
DONE EditMode passed=766 failed=0 skipped=0 total=766 queued=766 duration=28.6s          (751 before + 15 new, DeviceDiagnosticsTests)

python tools/unity_mcp.py test PlayMode "PH_Standalone"        (first run, 22:38 to 22:43, one test at that time)
DONE PlayMode passed=1 failed=0 skipped=0 total=1 queued=1 duration=287.2s

.venv/Scripts/python.exe -m pytest tools/demo/tests/test_quest_collect.py -q
11 passed in 8.51s
```

`DeviceDiagnosticsTests` (EditMode, a temp folder and a free port, no datagrams): the index lists the log and the session file
and nothing else; the log holds an info line, a two-line warning, an error with its stack and a health line; a file is served
from an offset; a line logged six times is written once and counted; nine paths outside the served folders, among them
`logs/../secret.txt`, `..%2Fsecret.txt` and an absolute path, are 404; `POST` and `PUT` do not parse as a request.

`PH_Standalone_NoHubNoNodes_RunsToTheEndAndServesItsFiles` (PlayMode, the real scene, scripted participant, a hub address where
nothing listens, discovery on a port nothing beacons on, so the real boards on the LAN were never found): the card is up and
nothing starts by itself; after the start the card is gone; 12 s into the run the index, the log and the running session's
event file are read over HTTP; the run ends `completed`. From the device log of that run:

```
[PH_Standalone] run finished in 199 s; phases calibrate,probe_pre,induction,threat,probe_post,questionnaire,probe_pre,induction,threat,probe_post,questionnaire,dissolve,reveal,witness
[PH_Standalone] strokes=57 cues=114 (none delivered), reasons: no_device=112, late=2; sens files 0, kin files 40
[health] 115 fps, worst frame 16 ms, 0 of 577 frames slow, ...; phase witness, operator app not connected, sleeve not found, muscle sensor not found, strokes acked 0 of 114, hands scripted
```

The session passed `contracts/validate.py`, and every file of it read through the channel was equal to the file on the disk.
The device log of that run: 405 656 bytes, 3585 lines in 4 minutes; 865 of them one OVRPlugin warning that only the editor
without a headset produces, in turn with other lines, which is why it was not collapsed.

Second run of both PlayMode tests on the final code (after the three small changes of section 3): see section 4.

## 3. Found on the way
1. In the Windows editor the folder's own size and time of a file that is still open lag behind (until it is closed). The index
   therefore always lists the running log with the size the writer itself has; an open `events.ndjson` shows late in the index
   in the editor only (its bytes are current when fetched). On Android the folder's values are current. [V the cause: the index
   at 7 s into a run listed `events.ndjson` with size 0 while its bytes were served]
2. The first health line counted the app's start-up as a frame ("worst frame 51372 ms"); the first frame is now skipped.
3. Unity's `File.ReadAllBytes` on the running log is a sharing violation (the writer keeps it open): read it through the server
   or with `FileShare.ReadWrite`.

## 4. On the headset
Release APK built 22:51 to 22:54 from this tree: `releases/game/0.1.0/chetna-phantom-hand.apk`, 91 849 502 bytes, sha256
`28f5fbf71ca32ff94f0c483ee56556af127b6be570a5d51d43f0d3920394afc9`, `result=Succeeded totalErrors=0 totalWarnings=5`, scenes
Bootstrap + PhantomHand; the bridge token has 0 hits in its 875 entries, this PC's address 0 hits; `DeviceDiagnostics` in 3
entries, `opus_diag` in 1, `StandbyCard` in 2. [V]

State of the things around it at 22:52: adb saw the Quest 3 (`2G97C5ZH4T02Q7`, Android 14, battery 17 %, the app not installed)
on a Wi-Fi with addresses 10.54.112.x, not the hotspot of the boards (192.168.242.x); both boards beaconing on 8791
(`CHETNA_HAPTIC_001` .254, `CHETNA_BIO_001` .244); the phone app's hub answering with 0 headsets.

Second run of both PlayMode tests on the final code, 22:58 to 23:03:
`DONE PlayMode passed=2 failed=0 skipped=0 total=2 queued=2 duration=299.8s`. [V]

## 5. First start of the app on a headset (23:01), read over the new channel

The owner moved the Quest to the hotspot of the boards (192.168.242.248) and plugged it in; `adb install -r` returned `Success`
at 23:00:49; the app was started with `am start` at 23:01:16. Everything below is from the headset's own log, mirrored by
`quest_collect.py` into `sim/out/quest_logs/run1/quest-f43ab9d2/` (git-ignored), device id `quest-f43ab9d2`, the headset's
clock 1.5 s ahead of the PC's. Times are UTC as in the log (local time is 5 h 30 min later). [V]

```
17:31:30.220 I [OPUS] diagnostics: quest-f43ab9d2 serves its log and session files on http://192.168.242.248:8796/ ; ...
17:31:30.249 I [PhantomHand] Wi-Fi multicast lock acquired (phantomhand)
17:31:30.259 I [OPUS] Bootstrap: opening 'PhantomHand' for game 'phantom_hand' (wait elapsed)
17:31:30.600 I Current display frequency 90, available frequencies [72, 80, 90, 120]
17:31:30.653 I [PhantomHand] controller up: hands=REAL tracked hands, hub=discover, nodeA=discover, nodeB=discover, discoveryPort=8791, ui=True, arm=True
17:31:30.679 I [PhLocalProps] PH_Table, PH_Books, PH_Sideboard (the scene's table is switched off)
17:31:31.165 I [UdpHapticTransport] node discovered at 192.168.242.244:8790 (device_id=CHETNA_BIO_001, kind=bio)
17:31:31.412 I [UdpHapticTransport] node discovered at 192.168.242.254:8790 (device_id=CHETNA_HAPTIC_001, kind=haptic)
17:31:40.222 I [health] 72 fps, worst frame 18 ms, 0 of 361 frames slow, memory 224 MB engine + 7 MB scripts, battery 18 % Charging; phase idle, operator app not connected, sleeve 192.168.242.254:8790, muscle sensor 192.168.242.244:8790, strokes acked 0 of 0, hands lost/lost
```

What this shows, each for the first time on a real headset: the app starts and renders at a steady 72 frames per second with no
slow frame in any 5 s window after the first; both hands are tracked; the multicast lock is taken; **both boards are found by
their beacons within one second**; the Asset Store props are in the build; the log channel works from the headset over Wi-Fi.
No warning or error from the game's own code in the first four minutes (one line of the Meta runtime, "Local Dimming feature
is not supported").

### The defect it found: the link to the operator app connected and dropped every 2 seconds
The phone was dozing at first (no hub, `live link: Discovering`). When the owner woke the app, at 17:33:17, the log read
`Connecting`, `Connected`, then about 1.2 s later `Reconnecting`, `Discovering`, `Connecting`, `Connected`, and so on: 57
reconnects in 2 minutes, with no warning in between. The owner saw the headset flicker on the phone.

Cause, read in the code on both sides: `BootstrapLoader.Load` handed its `LiveClient` to a coroutine that disposes it 0.3 s
later, set its own field to null, and loaded the game scene. A coroutine dies with its MonoBehaviour, the scene change destroys
that MonoBehaviour before 0.3 s have passed, and `OnDestroy` saw a null field: the launch scene's client was never stopped. Its
connection loop runs on a background task and reconnects for ever. So the headset held two hub clients with the same device id,
and the hub closes the old connection of a device when a new `hello` arrives (`hub_server.dart`, `_onHello`): each one threw
the other out every second. It can only happen in a player build, which starts in the launch scene; every editor test starts in
the game scene. Until tonight no build had run on a headset.

Fix: the client is disposed by `Task.Delay(300).ContinueWith(...)`, which does not depend on the scene (`LiveClient.Stop` only
cancels a token and aborts a socket). `BootstrapLoader` also reads its endpoints through `PhantomHandOverrides.Env` now, like
the game scene, so a test can start in the launch scene without finding the real hub. Test
`PH_Bootstrap_StopsItsHubClientOnceTheGameSceneIsUp` (PlayMode): starts in the launch scene, takes its client, waits for the game
scene, then requires that client to be stopped. `DONE PlayMode passed=1 failed=0 ... duration=75.8s` with the fix. [V with the
fix; the test was not run against the old code]

Second defect, in the PC tool: the first `quest_collect.py` run copied nothing from the Quest for a minute, because the Unity
editor on the same PC, in play mode for the tests, answers like a headset and its data folder holds every session the editor
ever recorded (898 files, 68 MB copied before it was stopped). The tool now leaves `editor-...` devices alone unless `--editor`
is given. [V: 11 tests, and the second start mirrored the Quest within 5 s]

## 6. The fixed build on the headset (23:12 on): all three systems in one run

Second release APK, built 23:08 to 23:12 with the launch-scene fix: 91 847 798 bytes, `result=Succeeded totalErrors=0
totalWarnings=5`; `adb install -r` `Success` at 23:12:36. [V]

1. **The link no longer flaps.** Boot `6357d813` (17:44:14 UTC): both boards found in 1 s again, `reconnects 0` in the whole log.
   [V: `grep -c 'live link: Reconnecting'` on the mirrored log = 0]
2. **The headset did not find the operator app by its beacon this time.** `live link: Discovering` five times in 30 s, and a
   listener on this PC heard no datagram on UDP 8788 from the phone (4 s, three times between 23:02 and 23:20), while the hub's
   HTTP answered. At 23:03 the old build had found the hub by its beacon (57 times), so the beacon was on the air then; in
   between the phone had dozed. The app starts its beacon once with the hub (`hub_providers.dart`), has no app-lifecycle handling
   and swallows every send error (`udp_beacon.dart`). Reason not proven; a self-healing beacon is being written (see 7).
   Workaround used: `phantom_endpoints.json` with `{"hubHost":"192.168.242.162"}` pushed with adb, app restarted. Boot
   `af004c5b` (17:45:53): `phantom_endpoints.json applied hubHost`, `Connecting`, `Connected` 43 ms later. [V]
   **Trap:** a hub address set by hand switches the search off. If the phone's hotspot gives itself another address tomorrow,
   the headset will not find the app until the file is removed (`adb shell rm /sdcard/Android/data/com.DefaultCompany.OPUS/files/phantom_endpoints.json`)
   or corrected. The "Ready" card now says so ("only <address> is tried: it was set by hand") in builds after 23:27.
3. **Two operator apps were on the phone and the owner had the old one open.** `com.opus.opus_app` (updated 01:39 on 8 Oct, no
   `/opus/v1/live/last_status`, hub id 6ed33bb2) and `com.opus.opus_app.pc` (the build of 21:52 with today's fixes); same name
   and icon. Whichever is opened takes port 8787. At 23:17 the old one was closed and the current one brought to the front with
   adb; at 23:18, on the owner's request to keep only the working one, the old one was switched off with
   `pm disable-user --user 0 com.opus.opus_app` (nothing deleted; `pm enable com.opus.opus_app` brings it back; the owner can
   uninstall it on the phone). [V: `pm list packages -d` lists it; its process is gone]
4. **First run through all three systems** (a dry run, said the owner afterwards: nothing was worn). Session
   `76c3ba72-4b9a-42c5-86c5-140c54f78ef0`, started by the operator's Start on the phone (`started (clinician pressed Start)`),
   14 phases in 65 s because the operator sent `phase_next` nine times, ended `stopped_by_clinician`. From the headset's own
   files, mirrored over Wi-Fi: [V]
   - what the phone's hub said about the headset while it ran (`hub_view.log`): `headsets 1, state running`, each phase in
     turn, `sleeve True, sensor True`;
   - both boards streamed to the headset: 15 sens files, EMG 7084 samples and IMU 7092 samples in 71 s (100 Hz each);
   - 162 stroke cues were scheduled, 154 cancelled by the skipped phases, 8 sent, **8 acked** (median 40 ms, worst 129 ms);
   - `uploaded session 76c3ba72-...: 32 files` to the phone, the first upload from a headset (the plain-HTTP fix of f6242c2
     holds); the same 32 files on this PC through the log channel;
   - frame rate 72 with 0 to 2 slow frames per 5 s window; `threat_response` `emg_peak_x` 1.08 (nobody wore the pads).
5. **A contract defect that only a skipped calibration shows.** `contracts/validate.py` failed that session at
   `events.ndjson:88`: `threat_impact` with `"impact_pos": null, "ok": false`. With no calibration the game has no arm position
   and says so with null, as the `calibration` event does; the schema demanded an array there. Nothing reads `impact_pos`
   (analytics, app, tools: no use outside two test files). `impact_pos` is `vec3OrNull` now; the session passes, and so does the
   whole fixture check (`[PASS] All validations passed`). [V]

## 7. Open at 23:30
- The real run with the sleeve and the pads worn: the owner will say when. The collector is running into
  `sim/out/quest_logs/run2/` with `--hub 192.168.242.162:8787`.
- The beacon of the phone app after a doze (a Sonnet builder in a worktree: self-healing socket and tests); then a phone APK,
  then the hand-set hub address can leave the headset again.
- Two read-only helpers are looking at the code paths that only a headset exercises (scene reload, pause and resume, uploads)
  and at the headset's logcat of the first run; their findings go below.
- The Quest APK on the GitHub release `ph-handoff-2026-10-08` is the one of 21:22: it has the flapping link and no log channel.
- EMG on a person still shows no muscle activity (hardware side, see the PH-E2E-HW log).
