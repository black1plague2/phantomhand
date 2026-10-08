# Testing runbook — what you can run yourself

Every part of Chetna, in the order it's easiest to try. Each section says what you should see when it works,
and what is known to be broken so you don't chase it.

Nothing here needs a headset, a phone, or the electronics sleeve. All of it runs in simulation on this machine.

---

## 0. One-time: nothing to install

Everything already has its environment:

| Component | Interpreter / SDK | Where |
|---|---|---|
| Flutter app | Flutter 3.47 / Dart 3.13 | `C:\flutter\bin` |
| Unity game | Unity 6000.4.6f1 | `C:\Program Files\Unity\Hub\Editor\6000.4.6f1` |
| Analytics | Python venv | `analytics\.venv` |
| Live sim | Python venv | `sim\live\.venv` |
| Haptic sim | Python venv | `sim\haptic\.venv` |

---

## 1. Contracts (5 seconds)

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" && ./analytics/.venv/Scripts/python.exe contracts/validate.py
```

**Expect:** a list of `[PASS]` lines ending in `[PASS] All validations passed`. This checks all 10 schemas
against valid fixtures *and* confirms the deliberately-invalid fixtures are still rejected.

---

## 2. Analytics (about 90 seconds)

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/analytics" && ./.venv/Scripts/python.exe -m pytest -q
```

**Expect:** `49 passed`.

To see it analyse a real session and print biomarkers:

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" && ./analytics/.venv/Scripts/python.exe -m opus_analytics contracts/fixtures/sessions/healthy
```

**Expect:** `wrote .../metrics.json` then a session-metrics table. Metrics marked `[invalid]` are analytics
refusing to compute from insufficient or low-quality data — that is correct behaviour, not a failure.

---

## 3. Simulators (2 seconds each)

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/sim/live" && ./.venv/Scripts/python.exe -m pytest -q
```
**Expect:** `26 passed`.

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/sim/haptic" && ./.venv/Scripts/python.exe -m pytest -q
```
**Expect:** `14 passed`.

---

## 4. The Flutter app

### Tests (about 20 seconds)

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/app" && /c/flutter/bin/flutter test
```
**Expect:** `All tests passed!` — 157 tests, including 38 golden (screenshot) tests.

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/app" && /c/flutter/bin/flutter analyze
```
**Expect:** 0 errors, 0 warnings. It prints ~118 `info` style lints, all pre-existing and all in test files.

### See the app in a browser

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/app" && /c/flutter/bin/flutter run -d chrome
```

**What to look at:** the patient profile's recovery charts, the session report (including its haptic cue
analysis), the program builder's dynamic form, the devices screen with the haptic sleeve tile, and the
progress screen's dose adherence.

**Known good:** trend labels now state direction honestly ("Improved beyond normal variation" vs "Within
normal variation"), and lower-is-better metrics like trunk lean are no longer labelled backwards.

---

## 5. The Unity game

The GUI editor must be **closed** — the project can only be driven in batch mode, and an open editor locks it.

### All logic tests (about 4 minutes)

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" && "/c/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Unity.exe" -batchmode -projectPath "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/game" -logFile logs/sessions/my_editmode.log -runTests -testPlatform EditMode -testResults logs/sessions/my_editmode.xml
```

**Expect:** `total="107" passed="107" failed="0"` in the results XML. Note there is **no `-quit`** — adding it
kills the runner before the tests run.

### Play tests, which also produce screenshots (about 2 minutes)

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" && "/c/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Unity.exe" -batchmode -projectPath "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/game" -logFile logs/sessions/my_playmode.log -runTests -testPlatform PlayMode -testResults logs/sessions/my_playmode.xml
```

**Expect:** 6 of 7 pass and **1 skipped** — `FullPipeline_...` skips on purpose unless you have the hub
running (section 6). Screenshots land in `logs/sessions/screens/unity/`.

**Worth opening:** `logs/sessions/screens/unity/run11/during_trunk_lean.png` — the amber lean-warning vignette
with the reach field left clear. That is the patient-facing safety cue.

---

## 6. The whole pipeline together

### 6a. One command (do this first)

`tools/demo/run_pipeline.py` is the whole demo in a single process: it generates a realistic
session, starts the haptic sleeve simulator, starts a hub (or aims at one you already have),
streams the session over the live protocol, fires a real haptic cue at every completed trial,
measures each leg's latency, then validates the result and prints the biomarkers. It exits
**non-zero** on any failed assertion, so it works in CI.

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" && ./sim/live/.venv/Scripts/python.exe tools/demo/run_pipeline.py
```

Run it with the **sim/live** venv (it needs aiohttp + numpy); it shells out to the analytics venv
for `contracts/validate.py` and `opus_analytics` by itself. Takes about 45 s.

Every port it binds is OS-assigned and ephemeral — it never touches 8787/8788/8790/8791/8797
unless you ask it to, so it is safe to run while the app's real hub is up.

To aim it at a **real hub** — the Flutter hub running on the phone — give it the address the app's
Devices screen shows, and slow the replay to real time:

```bash
./sim/live/.venv/Scripts/python.exe tools/demo/run_pipeline.py --hub 172.17.221.245:8787 --speed 1
```

It preflights `GET /opus/v1/health` and aborts with a plain-English diagnosis if the hub is not
reachable. Other flags: `--sleeve-host <ip>` for the real ESP32, `--no-sleeve` to skip the haptic
leg, `--session <dir>` to replay a recorded session instead of generating one,
`--stage sorting`, `--wait-for-start` (wait for a human to press Start in the app),
`--json-report <path>`.

**What "it worked" looks like** — the last three lines are the ones that matter:

```
  LEG game->hub (one-way, in-process): p50=3.6 ms  p95=14.6 ms  max=32.7 ms  n=196
  LEG cue trigger->sleeve datagram: p50=0.00 ms  max=16.00 ms  n=20
  26/26 checks passed in 43.1s

  PIPELINE OK
```

The checks assert **meaning, not shape**: every trial closed, every success has a `contact`,
kinematics exist and share one clock with the events, status actually arrived, one
`metrics_tick` per trial, every file uploaded 200/201, and every biomarker physiologically
plausible. That list is exactly the five defects the 2026-09-19 pipeline run found — all of which
produced files that passed `contracts/validate.py`. `tools/demo/tests/test_run_pipeline.py`
re-breaks a real session in each of those five ways and proves each check fails.

> A local-hub run measures true one-way latency (both clocks are this process's). A `--hub` run
> measures ping RTT / 2, and falls back to the hello/hello_ack round trip if that hub sends no
> pongs — the output always says which number you are looking at.
> On Windows `time.monotonic()` has ~15.6 ms granularity, so a sub-millisecond loopback haptic leg
> prints as `p50=0.00 ms / max=16.00 ms`. That is the clock, not the network.

### 6b. The manual version, with the real Unity game

Three processes at once. Use three terminals, in this order.

**Terminal 1 — the clinician's hub:**
```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/app" && /c/flutter/bin/dart run tool/hub_cli.dart --port 8787 --auto-drive
```

**Terminal 2 — the haptic sleeve simulator:**
```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/sim" && ./haptic/.venv/Scripts/python.exe -m haptic.fake_haptic
```

**Terminal 3 — the headset session:**
```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" && "/c/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Unity.exe" -batchmode -projectPath "C:/Users/GARV BANSAL/Documents/VR_games/OPUS/game" -logFile logs/sessions/my_pipeline.log -runTests -testPlatform PlayMode -testFilter "Opus.Shell.Tests.PlayMode.FullPipelineIntegrationTests" -testResults logs/sessions/my_pipeline.xml
```

**Expect** in terminal 3's log (`grep "FullPipeline:" logs/sessions/my_pipeline.log`):
- the hub found on 8787, the sleeve connected
- 6 trials completed, trial events, haptic cues, and **kin chunks > 0**
- 4 files uploaded to the hub
- live-link RTT p50 (was 165 ms; the target is < 250 ms)
- `contracts/validate.py --session exit=0`

Then analyse what the clinician actually received:

```bash
cd "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" && ./analytics/.venv/Scripts/python.exe -m opus_analytics app/.hub_data/<session-id>
```

**Important:** run the two simulators only when you want this pipeline test. If the hub is still holding port
8787 (or the sleeve 8790) when you run the *ordinary* PlayMode suite, the single-leg tests cannot bind their
own ports and will fail for that reason alone.

---

## 7. USB-forward live demo — real phone, real game (or a stand-in), cmd.exe

Everything above runs entirely on this PC. This section is the one place that reaches an actual
device over USB: the app's Devices screen listens on TCP 8787 on the **phone**; `adb forward`
makes `127.0.0.1:8787` **on the PC** the same socket. Written in `cmd.exe` syntax on purpose
(this is the one workflow that leans on `adb.exe`, which behaves identically from either shell,
but the rest of this section stays consistent with itself if you paste it into a plain Command
Prompt window, not Git Bash).

Set the adb path once per Command Prompt session (adjust if your SDK lives elsewhere):

```bat
set ADB="C:\Users\GARV BANSAL\AppData\Local\Android\sdk\platform-tools\adb.exe"
```

### 7a. Confirm the phone is visible and forward the port

```bat
%ADB% devices
%ADB% forward tcp:8787 tcp:8787
%ADB% forward --list
```

**Expect:** one device listed as `device` (not `unauthorized`/`offline`), and
`forward --list` showing a line containing `tcp:8787`. If `devices` lists nothing, the phone needs
USB debugging re-enabled (Settings > Developer options) and a fresh "Allow this computer?" prompt
accepted on the phone screen — this is a human step, not something a script can do.

### 7b. Prove the harness sees the phone (dry run — the phone hub may be OFF)

```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
analytics\.venv\Scripts\python.exe tools\demo\prove_live_to_phone.py
```

**Expect (hub off, the common case):** `(a)` reports the forward is registered, `(b)` reports
`UNREACHABLE` (nothing is listening on the phone's end of that forward yet — that is not a bug in
the tool, the app just isn't running its hub), `(c)` still succeeds over `adb ... run-as` (which
does not need the TCP hub at all) and prints every session already on the phone with its
`device.model`, flagging `SYNTHETIC` vs a real, non-synthetic model. The tool always exits 0 — it
is a diagnostic report, not a pass/fail gate; read the printed `SUMMARY` and the final
`real-game -> phone leg joined: True/False` line.

### 7c. With the app actually running on the phone (hub up)

Launch the OPUS app on the phone (from its launcher, or `%ADB% shell am start -n com.opus.opus_app/.MainActivity`
if you know the activity name), open the Devices screen so its hub starts listening, then re-run:

```bat
analytics\.venv\Scripts\python.exe tools\demo\prove_live_to_phone.py
```

**Expect:** `(b)` now reports `OK: TCP connect succeeded`. If the Unity game (real hardware, or the
batch-mode `FullPipelineIntegrationTests` run from section 6b pointed at the phone's IP instead of
`localhost`) has also just run a session, `(c)` should list a **new** session whose
`device.model` is **not** `synthetic` — that is the specific, previously-unproven claim
CONTEXT.md's ★★★ RUN 18 box calls out ("the two legs have never been joined").

### 7d. Pull and fully re-check one phone session

```bat
analytics\.venv\Scripts\python.exe tools\demo\prove_live_to_phone.py --pull session-000
```

**Expect:** the session is copied file-by-file (via `adb ... run-as cat`, since `adb pull` cannot
read an app-private path directly) into a temp folder under `%TEMP%`, then
`contracts\validate.py --session`, `python -m opus_analytics`, and `tools\demo\check_session.py`
all run against that local copy and print their real output, ending in
`PASS: 0 failed, 0 warned, 8 checks total` (or an honest list of what's wrong, if anything is).

---

## 8. Haptic sleeve end-to-end — firmware v0.4.0 (4 motors), cmd.exe

The real ESP32 sleeve may or may not be reachable (it depends on being on the same Wi-Fi/hotspot
as this PC — see `logs/sessions/2026-09-19-OPUS-sleeve-check.md` for the last time it was checked
and `docs/MANUAL_TODO.md` for the current network state). Everything in 8a runs against the
**software emulator** and needs no hardware at all; 8b is the same tool pointed at a real board's
IP when one is reachable.

### 8a. Emulator (no hardware needed)

Start the emulator in one Command Prompt window (leave it running):

```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
sim\haptic\.venv\Scripts\python.exe sim\haptic\fake_haptic.py --port 8792 --discovery-port 8793
```

**Expect:** `[HAPTIC] Listening on UDP 0.0.0.0:8792`. Ports 8792/8793 are deliberately **not**
8790/8791 (the protocol's documented defaults) so this never collides with a real sleeve or
another simulator instance already using the standard ports — see `contracts/HAPTIC_PROTOCOL.md`.
Add `--motor-count 4` to emulate a fully-fitted sleeve (default `1`, matching the real board
today), or `--no-route-unfitted` to make an unfitted channel reject `MOTOR_UNAVAILABLE` instead of
falling back to motor 0.

In a **second** Command Prompt window:

```bat
cd /d "C:\Users\GARV BANSAL\Documents\VR_games\OPUS"
analytics\.venv\Scripts\python.exe tools\demo\sleeve_test.py --ip 127.0.0.1 --port 8792
```

**Expect:** a 10-row PASS/FAIL table — 3 device-format patterns + 1 game-format cue + a 4-motor
sweep (`sweep motor 0..3`) + a deliberate too-fast pair whose **second** command is expected to
come back `rejected`/`CUE_GAP` (and is reported `PASS` for being correctly rejected, not for
executing) — ending in `10/10 PASS`. **Do not** pass `--gap-ms` below `100`: the firmware's (and
the emulator's) `MIN_CUE_GAP_MS` is 100 ms per motor, and spacing commands closer than that will
produce real, correct `CUE_GAP` rejections that this tool will then (correctly) mark as failures
for any command that wasn't the deliberate pair.

Stop the emulator with Ctrl+C in its window when done (or `%ADB%`-style `taskkill /PID <pid> /F`
if it was started detached — check with `netstat -ano | findstr :8792` for the PID first).

### 8b. Real hardware

```bat
analytics\.venv\Scripts\python.exe tools\demo\sleeve_test.py --ip 10.179.145.125
```

(replace the IP with whatever the board's serial monitor printed at boot — see
`firmware/opus_sleeve/opus_sleeve.ino`'s `printHandoff()`, which logs `DEVICE_IP=...` — the PC and
the board must be on the same Wi-Fi/hotspot; `docs/ELECTRONICS_HANDOFF.md` and the sleeve-check
log above have the current reachability state). **Expect:** the same 10-row table; real-hardware
latency runs 47-172 ms per the ack round trip over Wi-Fi (see the sleeve-check log), not the
near-0 ms loopback numbers the emulator prints.

---

## Known broken — don't chase these

| Thing | Status |
|---|---|
| **Android APK (app and game)** | Blocked by Gradle `Unable to establish loopback connection` — a machine-level security/firewall issue, not our code. This is why a phone or Quest cannot be tested yet. |
| **Windows desktop build** | Needs the Visual Studio 2022 C++ workload (a UAC click). |
| `endpoint_error_cm` | Was measuring a coordinate-frame mismatch (79–133 cm for reaches that visibly hit). Fixed 2026-09-19; re-verify before trusting it. |
| Reaction times < 1 ms | An artifact of the demo driver grasping almost immediately. Not a measurement of anything. |
| `neglect_index`, `fatigue_slope`, `reach_envelope_area_m2` | Stay `invalid` on a 9-second demo — they need more clean trials. |
| `02_grasp.png` in run12 | Captured a frame with neither hand nor apple visible (shot fires too early). The mid-reach and placed shots are good. |

---

## If something fails

1. Unity: `grep "error CS" <your log>` first — a compile error explains everything downstream.
2. A PlayMode test that depends on a network peer can fail purely on timing or a port already in use. Re-run
   once before investigating.
3. Anything that looks like garbled characters (`â€”`) in a doc is an encoding problem — never rewrite these
   files with PowerShell `Set-Content` without `-Encoding UTF8`.
