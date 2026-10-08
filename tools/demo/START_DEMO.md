# Starting the OPUS demo (plain language, Windows cmd.exe)

This is for someone who just wants to run the demo, not read code. Every command below is
cmd.exe syntax (`cd /d`, backslash paths) — paste each block into its own `cmd.exe` window.
If you prefer PowerShell or Git Bash, the same commands work with forward slashes and `cd`
instead of `cd /d`.

---

## (0) The one-command proof — start here

Before any multi-terminal ritual, run this. It is the entire game → UDP/WS → hub → analytics path
in one process, with real measured latency and real assertions, and it tells you plainly whether
the pipeline is healthy on this machine right now.

```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
sim\live\.venv\Scripts\python.exe tools\demo\run_pipeline.py
```

Takes about 45 seconds. It binds **only ephemeral ports**, so it is safe to run while the real hub
is up on 8787. The last line is either `PIPELINE OK` (exit code 0) or `PIPELINE FAILED` with every
failed check named (exit code 1).

To point it at **the app's real hub on a phone** instead of a simulated one — use the IP the app's
Devices screen shows, and `--speed 1` so it plays at real time and you can watch the Live Monitor
screen fill in:

```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
sim\live\.venv\Scripts\python.exe tools\demo\run_pipeline.py --hub 172.17.221.245:8787 --speed 1
```

Useful flags: `--sleeve-host <esp32 ip>` (real sleeve instead of the simulator), `--no-sleeve`,
`--stage sorting`, `--session <recorded dir>`, `--wait-for-start` (waits for a human to press
Start in the app), `--json-report out.json`.

If it fails at the hub step, it prints what to check (wrong IP / hub not started in the app /
firewall) — fix that before trying anything below.

---

Three ways to test the app **by hand**, from simplest to most complete:

- **(a) App alone** — just the Flutter app, no headset, no sleeve. Good for UI/report work.
- **(b) App + mock headset over the network** — a real phone (or another PC) opens the app,
  a script on this PC pretends to be the Quest headset and streams a fake session to it.
  This is the one to use for testing the Flutter app "for real" without a Quest.
- **(c) App + Unity + sleeve** — the real thing: Unity game, haptic sleeve (simulated or real
  ESP32), Flutter hub, all three talking over the live protocol.

Ports used below: hub 8787 (TCP, WebSocket/HTTP) + 8788 (UDP discovery beacon), haptic sleeve
8790 (UDP commands) + 8791 (UDP discovery). If you already have a real hub or sleeve running on
those ports, don't start a second one on the same ports — pick different ones with `--port`.

---

## (a) App alone

```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\app"
C:\flutter\bin\flutter run -d chrome
```

Opens the app in Chrome. There is no headset and no sleeve, so the live monitor screen will show
"waiting for headset" and the devices screen will show the sleeve as disconnected — that's normal.
Use this to look at the patient profile, program builder, and any already-recorded session's report
(the report screens work off files already sitting in `app\.hub_data\<session-id>\`).

---

## (b) App + mock headset over the network (recommended for phone testing)

This is the setup for handing a phone to someone and having them watch a session arrive live,
without needing a Quest or Unity running.

**Step 1 — find this PC's IP address** (so the phone can reach it):
```bat
ipconfig
```
Look for the "IPv4 Address" under your active Wi-Fi/Ethernet adapter, e.g. `192.168.1.42`.
The phone and this PC must be on the **same Wi-Fi network**.

**Step 2 — open the firewall ports** (one-time, needs an elevated/Administrator window):
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\tools\demo"
powershell -NoProfile -ExecutionPolicy Bypass -File open_firewall.ps1 -Haptics
```
This only **prints** the exact commands to run — it changes nothing by itself, for safety. Copy
the printed block into an Administrator PowerShell window and run it there, once.

**Step 3 — start the hub** (Terminal 1 — this is what the phone's app connects to):
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\app"
C:\flutter\bin\dart run tool\hub_cli.dart --port 8787
```
Leave this window open. It prints every message it receives.

**Step 4 — open the app on the phone:**
- If you have a built APK on the phone already, open it and it should discover the hub
  automatically on the local network (UDP beacon on 8788).
- Otherwise, on this PC, run `flutter run -d chrome` as in (a) and note the phone can't reach
  a Chrome tab on this PC directly — for a real phone you need the APK (see
  `docs/MANUAL_TODO.md` for the current Gradle blocker) or `flutter run -d web-server
  --web-hostname 0.0.0.0 --web-port 5000` and browse to `http://<this PC's IP>:5000` from the
  phone's browser.

**Step 5 — start the mock headset** (Terminal 2 — this is the script that pretends to be Unity):
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\sim\live"
.venv\Scripts\python.exe fake_headset.py --generate --stage reach --host 127.0.0.1 --port 8787
```
Replace `--host 127.0.0.1` with this PC's IP (from Step 1) if the phone/app is a separate device
reaching over Wi-Fi; use `127.0.0.1` only when the app and this script are on the same machine.
Add `--stage sorting` instead of `reach` to script a sorting-style session (containers,
occasional `wrong_target` trials) instead of a reach-and-place one.

**What you should see:** the hub window logs `hello`, a stream of `status`/`trial_event`/
`metrics_tick` messages, and finally 4 files uploaded (`session.json`, `events.ndjson`, one or
more `kin_*.json`). The app's live monitor screen updates in real time, and once the session ends
its report becomes available with real (synthetic) numbers, because the mock headset PUTs a
contract-valid session at the end — same generator analytics' own fixtures use
(`sim/synthetic_patients`), so `contracts/validate.py --session app\.hub_data\<session-id>`
passes and `tools\demo\check_session.py` will say PASS.

---

## (c) App + Unity + sleeve (the full pipeline)

Needs the Unity editor closed (batch mode only) and three terminals. This is
`docs/TESTING_RUNBOOK.md` section 6's setup; the short version:

**Terminal 1 — hub:**
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\app"
C:\flutter\bin\dart run tool\hub_cli.dart --port 8787 --auto-drive
```

**Terminal 2 — haptic sleeve** (simulated; skip this and point at the real ESP32's IP instead if
you have hardware — see `tools\demo\sleeve_test.py` to check it answers first):
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\sim"
haptic\.venv\Scripts\python.exe -m haptic.fake_haptic
```

**Terminal 3 — the Unity headset session** (batch mode, GUI editor must be closed):
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
"C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\game" -logFile logs\sessions\my_pipeline.log -runTests -testPlatform PlayMode -testFilter "Opus.Shell.Tests.PlayMode.FullPipelineIntegrationTests" -testResults logs\sessions\my_pipeline.xml
```

**Then check the result for real** (don't just trust the log):
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
analytics\.venv\Scripts\python.exe tools\demo\check_session.py app\.hub_data\<session-id>
```
`<session-id>` is printed by the hub terminal when the session ends (also visible as a new folder
under `app\.hub_data\`). A clean run prints `PASS: 0 failed, 0 warned` (see
`docs/TESTING_RUNBOOK.md` section 6 for what "good" looks like in each terminal's own log).

**If you have a real ESP32 sleeve** instead of the simulator, check it answers before trusting a
full pipeline run:
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
analytics\.venv\Scripts\python.exe tools\demo\sleeve_test.py --ip <sleeve's IP>
```
Prints ack status + round-trip latency for a pulse, a buzz, a ramp, and one game-format cue. Do
not point Unity at a sleeve IP you have not first checked this way.

---

## Watching sessions land automatically (any of the above)

Instead of manually re-checking `app\.hub_data\`, leave this running in its own window:
```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
analytics\.venv\Scripts\python.exe tools\demo\watch_and_analyse.py
```
It polls `app\.hub_data`, and the moment a session looks finished, runs `contracts/validate.py`
and the analytics pipeline on it and prints a plain-English summary (trial count, success rate,
reaction/movement time, any quality warnings) without you having to do anything.

## What "good" looks like, in one line per tool

| Tool | Good result |
|---|---|
| `run_pipeline.py` | ends with `N/N checks passed` then `PIPELINE OK`; exit code 0 |
| `fake_headset.py --generate` | hub receives `hello`→`status`(≥5/s)→`trial_event`s→`metrics_tick`s→4 uploaded files; app report renders |
| `check_session.py <dir>` | `PASS: 0 failed, 0 warned` |
| `watch_and_analyse.py` | prints a summary within a couple of seconds of a session ending |
| `sleeve_test.py --ip <ip>` | 4/4 commands accepted/executed, latency a few ms to a few hundred ms |
