# Phantom Hand: handoff of 8 Oct 2026, 21:55 IST (the work moves to another machine)

Written by the orchestrating session on the Windows build PC (Dell Precision 5560) for whoever continues, human or agent, on a
different machine. Everything stated as working carries the check that showed it; everything else is listed as broken or as never
run. Event: Vedanta Makeathon, Team Kela; the pitch calls the product MAYA (subtitle Phantom Hand); in code it is Chetna / OPUS.
**Freeze: 9 Oct 2026, 12:00.**

- Repository: `https://github.com/black1plague2/phantomhand` (public), branch `main`. The commit that carries this file is the state.
- Built apps (not in git, `*.apk` is ignored): GitHub release **`ph-handoff-2026-10-08`** of the same repository.
- The deepest record of today: `logs/sessions/2026-10-08-PH-E2E-HW-run1.md` (the real boards), then `docs/PH_STATUS.md`.

## 0. Thirty seconds

| | State at 21:55 |
|---|---|
| Game in the Unity editor against the simulator | green: EditMode 751/751 (21:20), PlayMode `"PhantomHand|PH_"` 26/26 (19:45; not re-run after the small change of f6242c2) |
| Game in the editor against the two real ESP32 boards | works: full session 7/7 without a wearer (19:28); with the owner wearing the sleeve all 14 phases, 111 of 114 strokes acked (20:58) |
| EMG (muscle sensor) on a person | **broken at the sensor**: samples arrive at 100 Hz, the value does not follow a contraction |
| Quest APK | built at 21:22 and inspected from the inside; **never started on a headset**. The Quest 3 on the build PC had Developer Mode off |
| Phone app | demo patient and one-button demo work on the phone; the live card now keeps its witness numbers across a headset reconnect (d050b4b, Flutter suite 494/494); rebuilt 21:52 and installed on the owner's phone, see 5 |
| Firmware of the boards | exists **only on the owner's Mac**, not in this repository |

## 1. Getting it onto the new machine

```bash
git clone https://github.com/black1plague2/phantomhand.git
cd phantomhand
```

The tree is about 400 MB (game art, pictures in the logs). A machine that only needs the tools and the documents (for example the
one that talks to the boards) can take 22 MB instead:

```bash
git clone --depth 1 --filter=blob:none --sparse https://github.com/black1plague2/phantomhand.git
cd phantomhand && git sparse-checkout set docs contracts tools firmware sim
```

The two built apps:

```bash
gh release download ph-handoff-2026-10-08 --repo black1plague2/phantomhand --dir releases/handoff
```

or, without the GitHub CLI:
`https://github.com/black1plague2/phantomhand/releases/download/ph-handoff-2026-10-08/chetna-phantom-hand.apk` (Quest, 91 815 702 bytes),
`.../chetna-operator-app.apk` (65 706 088 bytes) and `.../chetna-operator-app-pc.apk` (65 706 092 bytes) for the phone.

What each kind of work needs installed:

| Work | Needs |
|---|---|
| Install the game on a Quest, run it, read its log | Android platform-tools (`adb`) only |
| Talk to the boards, run the Python tools and suites | Python 3.12; `python -m venv .venv` then `pip install -r requirements-dev.txt` (the root environment of the build PC, 31 packages) |
| Analytics alone | `analytics/.venv` from `analytics/requirements.txt` |
| Change the game or rebuild its APK | Unity **6000.4.6f1** with Android Build Support; the Meta packages come through `game/Packages/manifest.json` |
| Change the phone app | Flutter **3.47.4** (stable); `cd app && flutter pub get` |

On the build PC the interpreters are `.venv\Scripts\python.exe` (Windows layout). The PlayMode test `PH_FullRun` starts its own
simulator with `sim/live/.venv/Scripts/python.exe`: on macOS or Linux that path is `bin/python`, so that one test needs a Windows
machine or a one-line change.

## 2. What works, what is broken, what has never been run

### Works, with the check that showed it

| Thing | Evidence |
|---|---|
| Unity EditMode suite | 751/751 at 21:20 (`python tools/unity_mcp.py test EditMode`, open editor) |
| Unity PlayMode, Phantom Hand only | 26/26 at 19:45, `PH_FullRun` against its own simulator speaking the real firmware's dialect: 114/114 strokes, 0 late |
| Python: simulator, replay, pipeline | `sim/sleeve` + `sim/live` 148 passed; `tools/demo/tests` 171 passed, 8 skipped (19:40) |
| Contracts | `python contracts/validate.py`: "All validations passed" |
| Pipeline gate L3 in simulation | 7/7 for the reference firmware and for the team's firmware dialect (`run_pipeline.py --game phantom_hand --sim --no-unity [--dialect team]`) |
| The three L3 faults played by the game itself | Node A off mid-run, Node B absent, hub away 30 s: fault rows green; after the outage 204/204 events exactly once (`logs/sessions/2026-10-08-PH-FORK-VERIFY-run1.md` section 7). Cue delivery was not judged in those runs (host too slow) |
| Both real boards on the Wi-Fi hotspot | discovery beacons from both; acks on the sender's port, sensor data on UDP 8790 (seen on the wire, 19:27) |
| Full game session, editor against the real boards, nobody wearing | 7/7 gate rows, 114/114 strokes acked, ack round trip median 13 ms / p95 50 ms, EMG 100.0 Hz, IMU 99.6 Hz, 82/82 files (19:28) |
| The same with the owner wearing the sleeve and the pads | 14 phases, EMG 99.9 Hz and IMU 99.4 Hz over 199 s, 83/83 files, timing error 3.6 ms, every stroke visible on the IMU; 111/114 acks (20:58). Picture: `logs/sessions/screens/ph/hw/wearing_run_traces_2026-10-08.png` |
| Phone app | Flutter suite 494 passed, 0 failed (21:46, after the reconnect fix; 481 before it); the demo patient with its report and the one-button demo were run on the phone at 16:10 |
| Quest release APK | builds ("result=Succeeded", 21:22); archive intact, signature verifies; arm64, IL2CPP, Vulkan; scenes Bootstrap + PhantomHand; hand-tracking and Wi-Fi permissions; no editor token inside |

### Broken or open

| # | Problem | What is known | Who can move it |
|---|---|---|---|
| 1 | **EMG shows no muscle activity** | Rest 221, fist squeezed 209 (ratio 0.94; the bench asks for 3x); 75 s of squeezing and pad adjusting never reached 2x; flat at both stone drops. Motors do not disturb it. Candidates: pads not on the muscle, leads swapped, the BioAmp EXG Pill not set for EMG (the firmware's band is 74.5-149.5 Hz; the Pill may ship low-passed near 40 Hz and need a solder pad bridged: unverified). **Until fixed every EMG number of a session is noise** | the electronics side, at the board |
| 2 | 3 of 114 strokes without an ack when worn | All three on motor 1; the IMU shows the motor fired, so the ack packet was lost. Link is slower on the arm: ack median 26 ms, p95 86 ms. The test's 98 % line fails at 97.4 % | decide: accept, or confirm a stroke by its IMU signature |
| 3 | Phone live card emptied after a headset reconnect: **fixed in d050b4b**, on loopback only | Measured by new tests: the card restarts 250-360 ms after the reconnect and the headset's replay has passed by then. The card of a new connection is now seeded from a per-device memory (section 5). Still true: the card is gone for about 0.3 s at a reconnect, and an event that arrives while no card is on screen is lost | try it once with a real headset: switch the headset's Wi-Fi off and on in the witness phase |
| 4 | The operator app's hub deviates from the resume rule | `hello_ack.resume_from_seq` echoes the headset's number; ids are not remembered across a reconnect. Written into `contracts/LIVE_PROTOCOL.md` ("Known deviation"), with what item 3 measured | after the event, together with item 3's proper form |
| 5 | Firmware sources only on the Mac | the brief asks the Mac session to copy them into `firmware/team/` (Wi-Fi name and password replaced); not done | the owner's OK on the Mac |
| 6 | Status documents carry "not recorded" in places | the facts are now in the hardware log ("Facts for the record") | a second documentation pass |

### Never run (do not claim otherwise)

- **Anything on a Quest**: the APK has never started on a headset. Hand tracking, the look, frame rate, finding the hub and the
  boards from the headset, and the upload of session files from a headset are all unproven.
- The phone app as the hub with the real boards (every hardware run used a recording hub on the PC).
- A real flinch: nobody has reacted to the stone while wearing working electrodes.
- The bench table of `docs/PH_ELECTRONICS_INTERFACE.md` section 5 by a wearer's judgement (stroke gap that feels like one stroke, tactile lead).
- 30 minutes on the power bank; a Wi-Fi drop during a session with the real boards.
- Unity batch mode; Hindi strings by a native reader.

## 3. The boards

| | Node A (haptic) | Node B (bio) |
|---|---|---|
| `device_id` | `CHETNA_HAPTIC_001` | `CHETNA_BIO_001` |
| On the hotspot on 8 Oct | 192.168.242.254 | 192.168.242.244 |
| Firmware | the electronics team's `node_a_haptic` 0.5.0 | `node_b_bio` 0.5.0 |
| Carries | 2 coin motors (GPIO25 = motor 0, GPIO26 = motor 1), MPU6050 IMU, SPI OLED | BioAmp EXG Pill on GPIO34 |
| Sends | `ack` per cue, `sensor_data` 100 per second | `sensor_chunk` 25 per second, 4 values each |

Rules of their firmware that the software depends on (all in `contracts/HAPTIC_PROTOCOL.md` v1.3 and
`docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` section C):

- Commands go to UDP 8790; discovery beacons come once a second on UDP 8791.
- **Acks go to the port the command came from; sensor data goes to the FIXED port 8790 of the last sender's IP.** The game listens
  there (`TelemetryHub` in `game/Packages/com.opus.sdk/Runtime/Transport/UdpHapticTransport.cs`); the Python tools send from port
  8790. One receiver per machine: a firewall must let UDP 8790 and 8791 in (`tools\demo\open_firewall.ps1 -Haptics` on Windows,
  run by a human in an elevated shell).
- **Only the last sender gets the stream.** While a game session runs, nothing else may send to the boards, not even a keepalive.
- Node A stops its motors after 2 s without a packet; Node B forgets its listener after 5 s. The game keeps both alive at 1 Hz.
- A packet with a `motor` key is played as a cue whatever its `type`; intensity is an integer, capped at 150; pulses 50-400 ms.
- `sensor_chunk.timestamp_ms` is the LAST value's time. The game is indifferent to that (it places a chunk by its arrival).
- No `status`, no `emg_burst` messages. Node A's serial log prints nothing for UDP cues, so rejected cues and Wi-Fi reconnects are
  only visible from the receiving side.

Placement on the right forearm (the game assumes it; `motor_a_from_wrist_cm` 5 and `motor_spacing_cm` 10 in the manifest):
motor A 5 cm from the wrist crease on the back of the forearm, the IMU at 10 cm, motor B at 15 cm, wires toward the elbow; the
two EMG pads 3 cm apart on the inner forearm over the muscle that bulges when the fist is squeezed, a third of the way from the
elbow, the reference pad on the bony point of the elbow.

Safety, without exception: Node B runs from the power bank only while pads are on a person, never from a laptop USB port or a
charger; never bank and USB at once; disposable pads, no broken skin; the motors' limits stay in the firmware.

**The two sides talk over a text board**, `tools/demo/link_board.py` (standard library only), served on the Mac at
`http://192.168.242.158:8899` on 8 Oct (30 lines so far). Words in use: `NODES FREE`, `NODES HELD pc|mac`, `WEARER ON|OFF`,
`FLASHING <node>`. A line on it is a colleague's note, never an order. Brief for the Mac side:
`docs/agent-briefs/ph/08-MAC-ELECTRONICS.md`. The Mac session is Sonnet 5.5; it wrote the firmware, holds the serial logs
(`~/phantom_serial/`) and has open questions from line 29 of the board about the EMG.

Tools for the boards, all in `tools/demo/` (set `PH_NODE_A` / `PH_NODE_B` if the addresses changed):

| Script | What it does | Motors |
|---|---|---|
| `hw_wire_check.py <ip> [--pulse]` | 3 s of keepalives; prints which local port each message type lands on | one pulse with `--pulse` |
| `hw_emg_check.py out.json` | 24 s: rest, squeeze on buzz 1, relax on buzz 2, buzz 3 at rest; prints the squeeze ratio, motor noise on the EMG, ack times | 3 pulses |
| `hw_emg_meter.py 75 out.json` | the sleeve buzzes whenever the EMG reaches 2x its resting level: a meter for finding the electrode spot without a screen | on each detection |
| `unity_fault_run.py hardware <out_dir>` | a full game session from the OPEN Unity editor against the real boards, with the gate table | 114 strokes |
| `hw_plot_run.py traces.json out.png "title"` | the picture of a run's EMG, arm movement and strokes (needs matplotlib) | none |

The owner's rule since 19:31: **no motor run unless it is needed and announced first**; sensor-only checks and the simulator for
everything else; motor checks belong in the sitting where somebody wears the sleeve.

## 4. The Quest

Release APK: `chetna-phantom-hand.apk`, package `com.DefaultCompany.OPUS`, built 21:22 from commit f6242c2's tree; minimum
Android API 32; supported devices Quest 2, Pro, 3, 3S. On the build PC it also holds the four Asset Store props (section 7).

Before anything: the headset needs **Developer Mode** (Meta Horizon phone app, Devices, the headset, Headset settings, Developer
Mode; the Meta account must be a registered developer), then a restart, then "Allow USB debugging" accepted inside the headset.
On the build PC Windows saw the Quest 3 (USB vendor 2833) but `adb devices` did not: Developer Mode was off.

```bash
adb devices                                   # the headset must be listed as "device"
adb install -r releases/handoff/chetna-phantom-hand.apk
adb shell am start -n com.DefaultCompany.OPUS/com.unity3d.player.UnityPlayerGameActivity
adb logcat -s Unity                           # the game's own log
```

The game finds the hub and the boards by their beacons (hub UDP 8788, boards UDP 8791) when headset, phone and boards are on one
Wi-Fi that passes broadcasts. If it does not, addresses can be given without a rebuild:

```bash
# phantom_endpoints.json, every key optional:
# { "hubHost": "<phone ip>", "hubPort": 8787, "nodeAHost": "192.168.242.254", "nodeBHost": "192.168.242.244", "discoveryPort": 8791 }
adb push phantom_endpoints.json /sdcard/Android/data/com.DefaultCompany.OPUS/files/phantom_endpoints.json
```

First run on a headset, in this order, each step is a result worth writing down:

1. It starts and shows the room (table, arm) with a steady picture.
2. Both hands are tracked; the calibration step passes.
3. The phone app shows the headset as connected (the hub was found).
4. `adb logcat -s Unity` shows two lines `[UdpHapticTransport] node discovered at ...` and no `[TelemetryHub] cannot listen`.
5. Strokes are felt in time with the brush in the SYNC block and late in the ASYNC block.
6. At the end the session's report appears on the phone. **This is the proof of today's last fix**: until f6242c2 a headset build
   was not allowed plain HTTP to anything but localhost and would have uploaded nothing. A line
   `[LiveClient] upload of ... failed: ... Insecure connection not allowed` in logcat means an APK older than 21:22 is installed.

Why the owner is moving machines: the Quest was choppy on the build PC. That PC drops its CPU to about a third of its speed under
Unity's play mode (measured, see the L3 log); it is a poor host for Quest Link or for timing. The APK runs on the headset itself
and does not need a strong PC.

## 5. The phone app

- Packages: `com.opus.opus_app` (the normal build, `cd app && flutter build apk --release`) and `com.opus.opus_app.pc` (the
  same code built side by side on the build PC; that is the one on the owner's phone). Both APKs are in the release, built at
  21:51 and 21:52 from d050b4b (65 706 088 and 65 706 092 bytes). Install one with `adb install -r <file>`.
- One-button demo: on the login screen; it signs in as the mock clinician and plays a whole recorded Phantom Hand run with its
  report, with no headset, sleeve or hub (commit 44bd27a).
- The app is the hub: it listens on TCP 8787 and announces itself on UDP 8788. Operating steps: `docs/PH_ON_DEVICE_RUNBOOK.md`
  sections 4 and 6.
- **Reconnect fix (d050b4b, merged at 21:48):** `app/lib/features/live/phantom_live_providers.dart` wraps the per-connection
  repository in a remembering one. A card on a new connection of the same device is seeded once with the run state, the markers
  still inside the 20 s window and the witness, if that memory is under a minute old (`phantomResumeWindowProvider`) and the
  headset is not in another session; at most 8 devices are remembered. The hub is untouched. 13 new tests in `app/test/hub/` and
  `app/test/phantom/` (a previous participant's witness is never handed on; a zero window gives no seed; two listeners; the cap);
  whole suite 494/494. Both APKs of the release were rebuilt with it (21:51 and 21:52) and the `.pc` one was installed on the
  owner's phone at 21:52. Proven on loopback with a test headset only.

## 6. Where things are

| Path | What |
|---|---|
| `CLAUDE.md` | hard rules for agents in this repository |
| `CONTEXT.md` | the box at the top is the current state in a dozen lines |
| `docs/PH_STATUS.md` | every piece of work with its status, commit and log; test counts; the Next list; flags |
| `docs/PH_HANDOFF_2026-10-08.md` | this file |
| `docs/PH_ON_DEVICE_RUNBOOK.md` | how to run the demo with people: roles, bring-up order, what to say when something fails |
| `docs/MANUAL_TODO.md` | what only a human can do |
| `docs/PH_ELECTRONICS_INTERFACE.md`, `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` | the boards: the contract we wrote, the team's own handoff, the firmware author's answers |
| `docs/agent-briefs/ph/` | PRD v2, rules, the spec with its dated decisions, the E2E gates, the brief for the Mac side |
| `contracts/` | message schemas, `HAPTIC_PROTOCOL.md`, `LIVE_PROTOCOL.md`, `validate.py`, fixtures |
| `game/` | the Unity project. `Assets/Games/PhantomHand` (the module), `Assets/Shell` (scene controller, builder, build script, tests), `Packages/com.opus.sdk` (transports, clients) |
| `app/` | the Flutter operator app (hub, live card, reports, demo) |
| `analytics/` | the Python analytics that write `metrics.json` and the embodiment report |
| `sim/` | `sleeve/twin.py` (simulator of both boards, both firmware dialects), `live/` (fake hub, replay headset) |
| `tools/demo/` | pipeline harness, L3 checks, board tools, link board, firewall script; `tools/unity_mcp.py` drives the open editor |
| `firmware/` | an older reference firmware that was never run on a board. The real firmware is not here yet |
| `logs/sessions/` | one log per run. Today: `2026-10-08-PH-E2E-HW-run1.md` (boards), `-PH-E2E-L3-run1.md` (pipeline gate), `-PH-U-LOCALPROPS-run1.md` (props), `-PH-FORK-VERIFY-run1.md` (second session's checks), `-PH-O-RESUME-run1.md` (orchestrator's waves) |

Today's commits, oldest first: `a25ab93` live events raised while the hub is away are kept; `db116f3` the UDP 8790 listener and
the first hardware run; `4e72ead` Asset Store props; `fdc7a06`, `055b3d8`, `fca91e5`, `4b4cc05`, `f1efe48` logs and documents;
`f6242c2` plain HTTP allowed in a headset build; `d050b4b` the phone card across a reconnect.

## 7. What exists only on the build PC, and how to have it elsewhere

| What | Where on the build PC | Needed to continue? | On another machine |
|---|---|---|---|
| The four Unity Asset Store packs | `game/Assets/Free`, `Books`, `Dark Wave Paint`, `Furniture_ges1` (git-ignored; the store licence forbids publishing them) | only to rebuild an APK with the same look | Package Manager, My Assets, with the owner's Unity account; then `Tools/OPUS/Bake local Asset Store props`; names in `CREDITS.md` |
| The wrappers baked from them | `game/Assets/Art/PhantomHand/Models/Local/` (git-ignored) | same | made by that bake. Without them the game shows the repository's own table and stone and no books or chest: it still works |
| The APKs | `releases/game/0.1.0/`, `releases/app/1.0.0/` | yes | the GitHub release (section 1) |
| The editor bridge's token | `game/Assets/Resources/DevAgentSettings.asset` (never committed) | no | each machine's Meta SDK writes its own. A development APK carries it: never share a `-dev.apk` |
| Evidence of the hardware and fault runs | `sim/out/hw_2026-10-08/`, `sim/out/unity_faults_2026-10-08*/` (git-ignored, about 150 MB) | no: the numbers are in the logs | not moved |
| Python environments | `.venv`, `analytics/.venv`, `sim/live/.venv` | yes | section 1 |
| The side-by-side phone build folder | `H:\pcapp` | no | a normal `flutter build apk` gives the normal package |
| The pitch film and its sources | `H:\Chenta\brag-output\` (417 MB: `brag.mp4`, 86 s, rendered 20:01; `work\maya\` sources) | pitch only | not in any repository. Its proof scene says nobody had worn the pads yet and "700+ Unity tests": true at 20:00, stale since the wearing session |
| The deck that names the product MAYA | `C:\Users\DELL\Downloads\Green and Orange Illustrated International Yoga Day Presentation.pdf` | pitch only | with the owner |
| On the Mac: the firmware, the serial logs, the link board's log | `electronics_handoff.md` and the two sketches; `~/phantom_serial/`; `~/phantom_link_board.log` | **yes, the firmware** | item 5 of the open list |

## 8. How to run each check

| Check | Command (repository root) | Expected |
|---|---|---|
| Contracts | `python contracts/validate.py` | "All validations passed" |
| Simulator and replay | `.venv/Scripts/python.exe -m pytest sim/sleeve sim/live -q` | 148 passed |
| Pipeline tools | `.venv/Scripts/python.exe -m pytest tools/demo/tests -q` | 171 passed, 8 skipped |
| Analytics | `analytics/.venv/Scripts/python.exe -m pytest analytics/tests -q` | 84 passed (last run in the afternoon) |
| L3 gate in simulation | `.venv/Scripts/python.exe tools/demo/run_pipeline.py --game phantom_hand --sim --no-unity --dialect team` | "7/7 checks passed" |
| Unity EditMode | `python tools/unity_mcp.py test EditMode` (editor open, bridge running) | 751 passed |
| Unity PlayMode | `python tools/unity_mcp.py test PlayMode "PhantomHand|PH_"` | 26 passed. Never run without the filter: Orchard Reach must not run |
| A faulted run of the game | `.venv/Scripts/python.exe tools/demo/unity_fault_run.py hub_absent|node_b_absent|node_a_off|none <out_dir>` | the fault's rows green; needs port 8787 and the editor for 5 minutes |
| Flutter | `cd app && flutter test` | 494 passed |
| Build the Quest APK | `python tools/unity_mcp.py call IReflectionService '{"method":"InvokeStaticMethodFromJson","typeName":"Opus.Shell.Editor.OpusBuildScript","methodName":"BuildAndroidApkPhantomHand","arguments":"{}"}'` | the call times out after 10 minutes while the build goes on; wait for `OpusBuildScript(PH): result=` in the editor log. 36 minutes the first time, 4 after that |

Sessions written by the editor land in `%USERPROFILE%\AppData\LocalLow\DefaultCompany\OPUS\opus_sessions\`.

## 9. What to do next, in order

1. **Quest**: Developer Mode on, install the 21:22 APK, go through the first-run list of section 4 and write down each step's
   result. This is the largest unknown left.
2. **EMG**: the electronics side checks the BioAmp board (is it set for EMG?), the lead order and the pad contact; then
   `hw_emg_meter.py` until a squeeze buzzes every time, then `hw_emg_check.py` for the number (3x or more). Only after that do
   flinch numbers mean anything.
3. **Phone card after a reconnect**: fixed, merged and on the owner's phone (section 5). Left to do: try a real reconnect once
   (headset Wi-Fi off and on in the witness phase): the card should come back with its numbers.
4. **Phone as the hub with the real boards**: one session with the phone's live card on screen (unlock the phone, open the app).
5. **Firmware into the repository** from the Mac (`firmware/team/`, credentials replaced).
6. Decide what a lost ack means for the 98 % line when worn (item 2 of the open table).
7. The wearer's judgement for the bench table: motor order (first buzz at the wrist), touch and brush together in SYNC, the
   stroke gap. Measured so far without judgement: send to first vibration on the IMU, median 52 ms (`tactile_lead_ms` is 40).
8. PlayMode once at the current commit; the second documentation pass (the "not recorded" cells).
9. Human items of `docs/MANUAL_TODO.md`: Hindi strings by a native reader, the origin of the GLB props, the build PC's CPU throttle.

## 10. Rules that bind whoever continues (the owner's decisions)

- Commit at each verified gate and push to `origin/main`. Stage by explicit path, never `git add -A`: several sessions have shared
  one checkout. **No mention of AI tools, assistants or co-authors in commits or pull requests.**
- Never commit `game/Assets/Resources/DevAgentSettings.asset`, an Asset Store pack, a Wi-Fi name or password, or a token.
- **Orchard Reach is out of scope**: never run, load or play its scenes or tests. PlayMode runs carry the filter `"PhantomHand|PH_"`.
- One driver of the Unity editor at a time; `game/.ph_unity.lock` names it (write your session and purpose, `FREE` when done).
  The owner wants the OPEN editor driven through `tools/unity_mcp.py`, not batch mode.
- Nothing is sent to the boards while the other side holds them; motors only when needed and announced.
- Never claim an APK works before a person has run it on a headset. A receipt (exit code 0, an ack, "accepted") is not a result:
  check the thing itself.
- Never state that a demo proves or measures consciousness. The fourth question is "a pointer, not proof".
- Cheaper models for bounded work (Sonnet builders in worktrees, with a written contract and a maximum report length); the
  orchestrator reviews, runs the editor and commits. `contracts/schemas/*` change only through the orchestrator.
- On the build PC a process called Momentum Dashboard (`python scripts\dashboard.py`) takes port 8787 again and again; the owner
  allowed stopping it whenever it blocks the Unity live tests.

## 11. Traps that cost time today

- **Tests with everything on 127.0.0.1 hide two classes of defect**: where a device sends its replies (the 8790 listener), and
  what a player build is allowed to do off-machine (plain HTTP). Both were found by reading, not by a test.
- The build PC's CPU throttles under play mode: late strokes and slow frames there are the host. `PH_FullRun` ends Inconclusive
  ("host too slow") rather than failing; do not chase such a result in code.
- `sim/live/test_phantom_replay.py` and `PH_FullRun`'s own simulator use the same fixed UDP ports (20890 and up): never at once.
- An orphan `sim.sleeve.twin` process keeps its ports after a dead test run: "twin did not print its ready line".
- A Quest APK build takes the editor for its whole duration; any bridge call during it corrupts the picture of what was built.
- Shell here-documents mangle backslashes on the build PC: edit files with an editor tool, not with `cat <<EOF`.
- A Sonnet weekly limit stopped three builders mid-work at 19:20; their worktrees kept the diffs. A stopped builder's worktree is
  the place to look before redoing its work.

## 12. Prompt for the session that continues

See the chat of the handoff; the same text is kept here so that it survives:

```text
This is my own instruction to you. I am continuing the Phantom Hand project on this machine; the previous work was done by another
Claude session on my Windows PC and by a second one on my Mac (the electronics). You take over as the orchestrator.

1. You have my OK to clone https://github.com/black1plague2/phantomhand (my public repo) into the home folder and to download the
   two APKs of its release ph-handoff-2026-10-08. Nothing else is downloaded or installed without asking me.
2. Read, in this order: CLAUDE.md, docs/PH_HANDOFF_2026-10-08.md (the handoff, complete), the box at the top of CONTEXT.md, then
   logs/sessions/2026-10-08-PH-E2E-HW-run1.md. Follow section 10 of the handoff as my standing rules.
3. Tell me in ten lines what you understood: what works, what is broken, what was never run, and what you will do first.
4. First job: the Quest. Check with adb whether the headset is visible; if it is not, tell me exactly what to switch on. When it
   is, install the APK and take me through the first-run list of section 4 of the handoff, writing down each step's result in a
   new log under logs/sessions/.
5. The boards hang on my Mac; the session there keeps a text board (tools/demo/link_board.py, see section 3). Before you send
   anything to a board, read the board's last lines and post that you hold the nodes. No motor run without telling me first.
6. Commit what you verified, by explicit path, and push to main. No mention of AI tools or co-authors in commit messages.
7. Ask me before: flashing a board, changing a setting of this computer, installing software, deleting anything.
```
