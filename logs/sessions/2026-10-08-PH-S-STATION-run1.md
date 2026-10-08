# PH S STATION run1 - "brush your own arm" sleeve station (python builder)

## Goal
`tools/demo/sleeve_station.py`: a no-headset demo for the haptic sleeve (fallback when the Quest path is down; bystander demo; R4 table B
row 3, tier T3). The visitor strokes a drawn forearm with the mouse; the pointer passing band A taps motor A, band B taps motor B; one key
adds a 600 ms delay. It shows touch-sight timing and nothing else. Plus one safety warning in `tools/demo/live_plot.py`.

Files I may touch: `tools/demo/sleeve_station.py` (new), `tools/demo/tests/test_sleeve_station.py` (new), `tools/demo/live_plot.py`
(docstring + one printed line), this log. Nothing else. No git commit/tag/push, no firewall/PATH/system changes. Unity, Flutter, adb, the
phone and Orchard Reach were not touched. `git status --short` for my paths at the end: ` M tools/demo/live_plot.py`,
`?? tools/demo/sleeve_station.py`, `?? tools/demo/tests/test_sleeve_station.py`, `?? logs/sessions/2026-10-08-PH-S-STATION-run1.md`.

## Plan (written before coding)
1. Read contracts/handoff/rules/R2/R4, `sleeve_test.py`, `node_probe.py`, `live_plot.py`, the twin CLI and both dialects.
2. One file, stdlib only, pieces separated so each is testable: `StrokeEngine` (PURE, the 5 limits live here), `Link` (PURE ack watchdog),
   `ack_ok`, `Sleeve` (UDP to Node A only), `Station` (engine -> offline gate -> UDP -> acks -> counters), `StationApp` (tkinter),
   `script_samples/run_script` (headless), `discover` (live_plot's own beacon listener), `main`.
3. Tests: pure logic (+ a randomised "no limit can be broken" property test), fake-socket wire tests, twin-backed integration (subprocess
   twin, Node A only, offsets 15200-15290 only: `--script` in both dialects, offline/recovery, dead port, discovery).
4. Run the new tests + `test_lan_mode.py` + `test_tool_paths.py` (+ `test_live_plot.py`, because I edited live_plot.py). No big suites.
5. Open the real window against a twin (synthetic Tk events) to check start/close without a traceback. Cannot judge the look.
6. Kill every process I start; list the ports.

## Facts read (verified this session, from the files)
- Wire (team handoff section A, HAPTIC_PROTOCOL v1.3): cue `{"cue_id","motor","intensity","duration_ms","pattern"}` (no `type`), `{"type":"keepalive"}`,
  `{"type":"stop"}`; ack `{"type":"ack","device_id","cue_id","accepted","timestamp_ms"}`; telemetry goes to the LAST sender only.
  The reference twin takes the same three forms (twin.py `NodeA.on_message`: `typ is None` -> cue; `stop` with no `motor` -> stop all; keepalive known).
  So one sender serves both dialects and the station has **no `--dialect` switch**.
- Ack precedence: `HapticClient.AckSucceeded` (game/Packages/com.opus.sdk/Runtime/Transport/HapticClient.cs:500-507): `ok`, then boolean `accepted`,
  then `status` (accepted/executed = success); an ack with none of them is a plain receipt = delivered. `ack_ok()` copies exactly that.
- Limits (02-RULES 4.2, HAPTIC_PROTOCOL v1.2): >= 250 ms per motor, <= 4 sends/s, intensity <= 150, 200 ms pulse, <= 50 % duty / 10 s per motor.
- Geometry (R2 section B, P1): xA = 5 cm, xB = 15 cm from the wrist, forearm L = 25 cm, brush 12 cm/s -> A-to-B gap 833 ms.
- `node_probe.py` runs on import (module-level code reading `sys.argv`): not importable. `sleeve_test.py` cannot be reused for the wire:
  ```
  $ python -c "...sleeve_test.CommandResult(ack={'type':'ack','cue_id':'stroke_17','accepted':True,...}).ok / ._device_command(0,150,200,'pulse')"
  sleeve_test.CommandResult.ok for {accepted:true} -> False | passed -> False
  sleeve_test._device_command has type: haptic
  ```
  i.e. against the team firmware that tool reads every ack as a failure and sends `"type":"haptic"`, which the team firmware does not document
  (twin team dialect ignores it, no ack). Reused by import instead: `live_plot.parse_hostport`, `live_plot.Feeds._discovery_loop` (listen only) + `LiveData`.
- The reference firmware (`firmware/opus_sleeve/opus_sleeve.ino:669-677`) lists no `keepalive` among its known types: a bare keepalive is "ignored and
  counted; not a keepalive" there. The real boards run the team's sketches, where it feeds the watchdog (handoff), and the twin treats it as known.
- Clocks on this PC (Python 3.12.8, Windows):
  ```
  monotonic info : namespace(implementation='GetTickCount64()', monotonic=True, adjustable=False, resolution=0.015625)
  perf_counter   : namespace(implementation='QueryPerformanceCounter()', monotonic=True, adjustable=False, resolution=1e-07)
  monotonic smallest steps (ms): [15.0, 16.0, 16.0, 15.0, 16.0]
  sleep(1ms) -> min/mean/max ms: 1.13 1.60 2.47
  ```
  => the station times everything with `time.perf_counter()` (a 15.6 ms clock would hide 8 ms ack latencies) and polls with `time.sleep(0.001)`.

## Decisions
- D1 One sender, no `--dialect`: the three bare forms above. Cue carries only the five documented fields (no `cue`, no `play_at_ms`): the team has not
  yet confirmed that their firmware ignores unknown fields (request #2 in PH_ELECTRONICS_HANDOFF_FROM_TEAM.md), so the station sends nothing they did not document.
  `stop` is the bare form for the same reason, sent twice (one datagram can be lost; the node's 2 s watchdog is the backstop).
- D2 Band logic: a band fires when the brush PATH (segment from the previous sample) touches its 1.5 cm zone, either direction, so a fast flick over a band
  is not missed; it is then disarmed until the brush is > 1.25 cm from the band centre (hysteresis: hovering/jitter cannot re-fire). A lifted brush
  (`move(None)`) forgets its path and re-arms; touching down inside a zone taps it. Two bands touched in one sample fire in path order.
- D3 Limits are applied when the tap is SENT (after the 600 ms delay), in `StrokeEngine.poll`, not at crossing time; a refused tap is counted by
  reason (`gap`, `rate`, `duty`) and never queued. The 4/s rule counts a CLOSED 1 s window (sends at 0, 250, 500, 750 -> the next is allowed at 1001, not 1000).
  Duty: this motor's on-time in [t - 10 s, t + 200 ms] plus the new pulse must stay <= 5 s; that is slightly stricter than "50 % of any 10 s" and
  guarantees the firmware's own duty check (previous 10 s < 50 %) never has to refuse. Intensity: `min(150, x)` in the constructor, pulse fixed at 200 ms.
- D4 Switching the condition (Space) cancels taps still waiting, so a tap scheduled as "late" never lands in the "in sync" condition.
- D5 Offline gate (the one place I interpreted the brief): "stops sending cues until acks return" cannot be literal, because an ack only ever answers a cue
  (the team firmware does not answer keepalive; `ping` is ignored there). So: offline = a tap has waited 2 s for ANY ack (any ack clears it, accepted or
  not); while offline, taps are held (counted, shown) except ONE trial tap per 2 s - the visitor's own stroke - and the first ack back makes it online again.
  Nothing is sent that the visitor did not stroke. Consequences: with a dead sleeve and an idle visitor the red strip does not appear (the status line then
  reads "last ack N s ago"); one lost datagram followed by a pause > 2 s shows the red strip until the next stroke, whose first tap goes out as the trial.
- D6 Window: single-threaded Tk, non-blocking socket, the engine is called straight from `<Motion>`; the screen redraws at ~30 fps; a band lights for 250 ms
  when its tap is actually sent (the only way a bystander can SEE the 600 ms). `<Leave>` lifts the brush (otherwise re-entering elsewhere would sweep the bands).
  Any exception in a Tk callback: traceback, `stop`, window closed, exit code 1.
- D7 Wording: nothing but touch-sight timing (a test scans the source for ownership/consciousness/therapy-type words).
- D8 Added to the station (not asked, one line): a start-up stderr note with the same "LAST sender" warning as live_plot.

## What was built
- `tools/demo/sleeve_station.py` - 475 physical lines (26 docstring, 70 blank, 9 comment-only, 378 code). Target was ~350: I am over by about a third; the offline gate
  (~35), the headless runner (~30) and the window (~100) are what the brief asked for, I found nothing left to cut without dropping a requirement.
- `tools/demo/tests/test_sleeve_station.py` - 38 tests.
- `tools/demo/live_plot.py` - module docstring: removed the clause "(the firmware keeps up to 3 subscribers, so this never steals the stream from the Quest)"
  (false for the team firmware) and added a WARNING paragraph; `main()` prints one stderr line before `feeds.start()`. Behaviour otherwise unchanged:
  ```
  live_plot: WARNING - subscribing to the nodes directly; the team firmware streams only to the LAST sender, so with the Quest in a session this takes the sensor stream away from the headset (use it headset-off, or rely on the app's live card)
  ```
  diff: 8 insertions, 2 deletions (`git diff --stat`).

## Evidence (real output)
### Tests
```
$ H:\Chenta\phantomhand\sim\live\.venv\Scripts\python.exe -m pytest tools/demo/tests/test_sleeve_station.py -q -p no:cacheprovider -s
..................................reference -> {"cues_sent": 12, "acks_accepted": 12, "acks_refused": 0, "acks_missing": 0, "dropped_by_limiter": 0, "held_offline": 0, "mean_ack_latency_ms": 12.7, "pass": true}
.team -> {"cues_sent": 12, "acks_accepted": 12, "acks_refused": 0, "acks_missing": 0, "dropped_by_limiter": 0, "held_offline": 0, "mean_ack_latency_ms": 11.8, "pass": true}
...
38 passed in 47.49s
$ pytest tools/demo/tests/test_lan_mode.py tools/demo/tests/test_tool_paths.py -q -p no:cacheprovider
15 passed in 2.60s
$ pytest tools/demo/tests/test_live_plot.py -q -p no:cacheprovider        (edited file, so I ran its tests too)
18 passed in 10.73s
```
Do the tests have teeth? 20 mutations of the source (scratch copy, `scratchpad/mutate.py`): gap 250->100, rate 4->5, open 1 s window, duty 5000->6000, duty
ignoring the new pulse, intensity cap 150->200, delay 600->500, hysteresis 1.25->0.5, toggle keeps pending, unstable poll order, no nearest-first, lift does not
re-arm, link never offline, link without trial, ack accepted-before-ok, ack status-first, no stop on close, no keepalive, cue with `type`, `time.monotonic`
instead of `perf_counter`: **20 of 20 killed** (the clock one only after I added `test_the_clock_resolves_well_below_a_millisecond`; first pass had 1 survivor).
The randomised test drives 12000 random pointer samples (jumps, lifts, condition flips) and checks every limit on what went out (and that the limiter dropped > 50).

### Twin rehearsal, read back from the twin's own log (`scratchpad/rehearse.py`; twin `--kind haptic`, Node A only)
```
=== reference: twin ready {'haptic': 24030, 'bio': 24032, 'discovery': 24031, 'control': 24033}
$ python tools/demo/sleeve_station.py --discovery-port 24031 --script        (no --a-ip: found from the beacon)
exit code 0 after 16.6 s
stdout: {"cues_sent": 12, "acks_accepted": 12, "acks_refused": 0, "acks_missing": 0, "dropped_by_limiter": 0, "held_offline": 0, "mean_ack_latency_ms": 11.5, "pass": true}
stderr: sleeve_station: listening for Node A on UDP 24031 (up to 12 s) ... / sleeve_station: Node A at 127.0.0.1:24030
twin saw rx: {'keepalive': 16, 'cue': 12, 'stop': 2} | distinct senders: 1
twin stroke records: Counter({'executed': 12}) | motors: [0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1] | intensity_applied: [150] | first cue wire: {'cue_id': 'st_0001', 'motor': 0, 'intensity': 150, 'duration_ms': 200, 'pattern': 'pulse'}
last two rx: [{'type': 'stop'}, {'type': 'stop'}]
=== team: twin ready {'haptic': 24035, 'bio': 24037, 'discovery': 24036, 'control': 24038} dialect=team
$ python tools/demo/sleeve_station.py --a-ip 127.0.0.1:24035 --script
exit code 0 after 15.8 s
stdout: {"cues_sent": 12, "acks_accepted": 12, "acks_refused": 0, "acks_missing": 0, "dropped_by_limiter": 0, "held_offline": 0, "mean_ack_latency_ms": 13.0, "pass": true}
twin saw rx: {'keepalive': 16, 'cue': 12, 'stop': 2} | distinct senders: 1
twin stroke records: Counter({'executed': 12}) | ... same motors/intensity ... | last two rx: [{'type': 'stop'}, {'type': 'stop'}]
```
The 600 ms is on the wire: the integration test reads the A-to-A spacing of the cues as the twin received them and expects the stroke period 2690 ms
(sync-sync, delayed-delayed) and 3290 ms where the first delayed stroke begins (tolerance 250 ms).

### Failure paths (CLI)
```
$ sleeve_station.py --a-ip 127.0.0.1:24040 --script        (nothing listens on 24040)
{"cues_sent": 7, "acks_accepted": 0, "acks_refused": 0, "acks_missing": 7, "dropped_by_limiter": 0, "held_offline": 5, "mean_ack_latency_ms": null, "pass": false}
exit code: 1   (18.0 s)
$ sleeve_station.py --discovery-port 24071                  (no beacon)
sleeve_station: listening for Node A on UDP 24071 (up to 12 s) ... / sleeve_station: Node A not found; pass --a-ip host[:port]
exit code: 2   (12.5 s)
```
7 of 12 taps went out blind: the first 2 s, then one trial per 2 s; 5 were held.

### The real window (`scratchpad/gui_smoke.py`, synthetic Tk events, twin offset 15260, canvas texts read back with `itemcget`)
Look NOT judged. What the window said, in order (run 3 of 3; Space was delivered, Esc was not - see open issue 3):
```
[sync, after one stroke]   IN SYNC | Sleeve: last ack 1.3 s ago | A | motor A, 5 cm | B | motor B, 15 cm | wrist | elbow | taps sent 2 | acked 2 | dropped by limiter 0 | Stroke the arm slowly ...
[Space]                    DELAYED 600 ms | ... | taps sent 4 | acked 4 | dropped by limiter 0
[control off-a, stroke]    DELAYED 600 ms | Sleeve offline - no ack for 2 s, taps held | ... | taps sent 6 | acked 4
[control on-a, stroke]     DELAYED 600 ms | Sleeve: last ack 0.7 s ago | ... | taps sent 9 | acked 6 | dropped by limiter 0 | held (offline) 1
main() returned 0 after 16.7 s ; twin rx counts: {'cue': 9, 'keepalive': 17, 'stop': 2}   (harness then closed it with app.quit())
run 1 (Esc and Space both delivered): main() returned 0 after 16.4 s ; twin rx counts: {'cue': 9, 'keepalive': 16, 'stop': 2}
```
Close paths, each a short run, `main()` return code and what the twin received: Esc key -> 0, stop x2; window close button (WM_DELETE_WINDOW) -> 0, stop x2;
exception inside a Tk callback (injected `1/0`) -> traceback printed, window closed, **1**, stop x2; `--fullscreen` + Esc -> 0, stop x2.
Unit test `test_stop_goes_out_on_any_exception...` covers the non-window path (exception in `run_script` -> `stop` + socket closed).

## Open issues / not verified
1. The LOOK of the window is not judged (no screenshot reviewed): sizes, colours, 2 m readability, the position of the bands against the printed 5 / 15 cm marks.
2. The REAL sleeve is not tested: nothing here ran against a board. The team's own handoff says Wi-Fi/UDP is untested on their side. Unknown until then: whether their firmware
   accepts the exact five-field cue and a `stop` with no extras (it is the documented form), whether a 200 ms pulse leaves "motor still running" clear by the 250 ms gap
   (50 ms margin), cue-id length limits (ours are 7 chars, `st_0001`).
3. Keyboard keys need OS focus. Synthetic Space and Esc were delivered in full run 1; in run 2 neither was; in run 3 Space was (the harness called focus_force just
   before it) and Esc was not (no focus_force before it); with focus_force just before Esc, the `esc` and `fullscreen` runs closed by key. So a harness/focus
   effect (the window was not the foreground app), not an app fault - the close button, the exception path and quit() do not depend on focus. In the room: click the window once.
4. The first window run showed "dropped by limiter 22" after the first synthetic stroke; an instrumented re-run (212 pointer samples, 0 jumps > 0.5 cm) and two more
   full runs showed 0. I attribute the 22 to the real mouse moving over the window during that first run; not proven. The limiter absorbed it (2 taps sent).
5. I opened the window 9 times (3 full scenarios of ~16 s, 1 diagnostic of 3 s, 5 runs of ~1 s for Esc / close / exception x2 / fullscreen), not once as asked, to
   check each close path and to chase item 4. Each run called `focus_force()`, so the foreground changed briefly each time.
6. Offline rule corner cases are in D5 (idle dead sleeve; one lost datagram + pause).
7. `tools/demo/sleeve_test.py` (not mine, not touched): `CommandResult.ok` ignores `accepted` and `_device_command` sends `"type":"haptic"` - against the team
   firmware every command reads as failed. Needs an owner (suggest: send the bare form, read `accepted`).
8. Reference firmware ignores a bare keepalive (see Facts); only relevant if someone points the station at `firmware/opus_sleeve` instead of the team boards.
9. Discovery against a LOOPBACK twin in the team dialect: its beacon has no `ip` field, so the host is the datagram source, which may be the PC's LAN address
   (the broadcast copy) that the loopback-bound twin does not serve. Use `--a-ip` for twin rehearsals; real boards are fine (their source IS their address).
   The discovery test therefore asserts the host only for the reference dialect.
10. Telemetry hazard (contract v1.3): station and live_plot both make the laptop the "last sender"; warned in both docstrings and on stderr at start.
11. File size: 475 lines (378 code) against a ~350 target (see "What was built"). Test file: 549 lines.

## Ports and processes (all released)
Twin subprocesses, `--kind haptic`, loopback only: offsets 15210 (24000/24001/24003), 15220 (24010/24011/24013), 15230 (24020/24021/24023) in the tests;
15240 (24030-24033), 15245 (24035-24038), 15260 (24050-24053) by hand. Sending to nothing: 24040 (dead Node A), 24060 and 24062 (live_plot warning test).
Listening for beacons only: the twin's beacon ports above and the quiet 24071. Never the default 8790-8793, never 8787, never offsets 12100 / 14010.
After the last run: no `sim.sleeve.twin` / `sleeve_station` python processes and no UDP endpoint in 24000-24099 (`Get-NetUDPEndpoint` empty).
Scratch files (mutation copy, rehearsal logs) are in the session scratchpad, not in the repo.

## Commands for a human
Twin rehearsal on this PC (two consoles, repo root):
```
sim\live\.venv\Scripts\python.exe -m sim.sleeve.twin --kind haptic --port-offset 15210 --no-stdin [--dialect team]
sim\live\.venv\Scripts\python.exe tools\demo\sleeve_station.py --a-ip 127.0.0.1:24000            [--fullscreen]
sim\live\.venv\Scripts\python.exe tools\demo\sleeve_station.py --a-ip 127.0.0.1:24000 --script   # no window, one JSON line, exit 0 = ok
```
Real sleeve (Node A powered, laptop on the same 2.4 GHz network as the board; the Quest NOT in a session):
```
sim\live\.venv\Scripts\python.exe tools\demo\sleeve_station.py                       # finds Node A from its UDP 8791 beacon (12 s)
sim\live\.venv\Scripts\python.exe tools\demo\sleeve_station.py --a-ip <Node A ip>    # when discovery finds nothing (hotspot drops broadcasts, or the firewall blocks 8791 in)
sim\live\.venv\Scripts\python.exe tools\demo\sleeve_station.py --a-ip <Node A ip> --script    # 20-second check without a screen
```
No firewall rule was changed by me; acks come back to the station's own UDP port, discovery needs UDP 8791 inbound for python.exe.

## Checkpoint
Done. Nothing pending on my side. A replacement agent can resume from this log: the code is in the three files above, the scratch drivers are in the session scratchpad
(`mutate.py`, `rehearse.py`, `gui_smoke.py`) and are not needed for anything in the repo.
