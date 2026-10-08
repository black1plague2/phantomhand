# PH S XMACHINE run1 (2026-10-08): per-folder venvs, cross-machine twin, demo harness LAN mode, old-PC paths

## Goal
1. Make the Python side match the documented per-folder venv layout on this PC (`analytics/.venv`, `sim/live/.venv`, `sim/haptic/.venv`, Python 3.12) because the Unity PlayMode tests and the harness hardcode those paths.
2. Make the sleeve twin and the demo harness usable when the electronics (real ESP32 nodes or the twin) run on a DIFFERENT PC on the LAN.
3. Remove the remaining old-PC paths from `sim/` and `tools/demo/`.
Lane: `sim/**` and `tools/demo/**` only. No git commit/tag/push. No firewall/env changes. Unity not launched.

## Plan (step -> verify)
1. venvs -> verify: each folder's tests green with its own venv; sim/live venv can launch fake_hub, twin, fake_haptic.
2. cross-machine twin (a beacon per LAN interface from a bound socket, b control socket stays 127.0.0.1, c fake_haptic if small, d `--lan` in the harness, e `open_firewall.ps1` report/fix modes, f tests) -> verify: new unit tests + old tests unchanged + manual LAN smoke.
3. old-PC paths (adb, dart, Unity.exe) -> env var, then PATH, then known locations, old value last -> verify: unit tests with fake environments.
4. full re-run of every suite + `run_pipeline.py --game phantom_hand --sim --no-unity` + two-process LAN smoke; ports 8787-8793 checked at the end.

---

## STEP 1 - three venvs (Python 3.12.8, all `python -m venv` from `C:\Users\DELL\AppData\Local\Programs\Python\Python312\python.exe`; global Python never touched)

Start 2026-10-08 09:38 IST. Interpreters (verified `--version` = Python 3.12.8, `sys.prefix` inside each folder):
```
H:\Chenta\phantomhand\analytics\.venv\Scripts\python.exe
H:\Chenta\phantomhand\sim\live\.venv\Scripts\python.exe
H:\Chenta\phantomhand\sim\haptic\.venv\Scripts\python.exe
```
All three are gitignored (`.venv/` in the root `.gitignore`).

### Installs (each with its own `python.exe -m pip`, pip upgraded inside the venv only: 24.3.1 -> 26.2.1, as contracts.yml does)
| venv | commands | result |
|---|---|---|
| analytics | `pip install -r requirements.txt` (the lock), `pip install -e ".[dev]"`, `pip install -e ../sim` (analytics/README.md Setup) | lock honoured: pytest 9.1.1, jsonschema 4.26.0, referencing 0.37.0, numpy 2.5.3, scipy 1.18.1, pandas 3.0.5, hypothesis 6.168.0; opus-analytics 0.1.0 + synthetic-patients 0.1.0 editable; `pip check`: No broken requirements found. |
| sim/haptic | `pip install -r requirements.txt` | jsonschema 4.23.0, referencing 0.35.1, pytest 8.3.1; `pip check` clean. |
| sim/live | `pip install -r requirements.txt` **+ `pip install numpy==2.5.3`** | aiohttp 3.10.5, jsonschema 4.23.0, referencing 0.35.1, pytest 8.3.1, pytest-asyncio 0.24.0, numpy 2.5.3; `pip check` clean. |

Because each folder has its own venv, the pin conflict the baseline hit in the single venv (analytics jsonschema 4.26.0/pytest 9.1.1/referencing 0.37.0 vs sim 4.23.0/8.3.1/0.35.1) is gone: every suite now runs on its own folder's pins.

**Deviation / finding (sim/live numpy):** `sim/live/requirements.txt` does not list numpy, but `fake_headset.py --generate` -> `synthetic_patients` needs it. First run of the sim/live suite with the requirements only: `3 failed, 42 passed in 36.59s` (all three in `test_fake_headset_generate.py`, `ModuleNotFoundError: No module named 'numpy'` at `sim/synthetic_patients/generate.py:7`). Fixed by installing the same numpy as the analytics lock (2.5.3) into sim/live/.venv; the line is added to `sim/live/requirements.txt` so the README setup reproduces it (see Files changed). `pip install -e ../sim` is NOT needed in sim/live: `fake_headset.py` puts the sim dir on sys.path itself.

### Verification (each folder's own venv, real output)
```
cd sim/haptic ; .venv/Scripts/python.exe -m pytest -q                 ->  26 passed in 6.96s
cd sim/haptic ; .venv/Scripts/python.exe -m pytest ../sleeve -q       ->  38 passed in 9.44s     (sim venv named by sim/sleeve/README.md)
cd sim/live   ; .venv/Scripts/python.exe -m pytest -q                 ->  45 passed in 94.77s (0:01:34)   (after numpy; 3 failed/42 passed before it)
cd analytics  ; .venv/Scripts/python.exe -m pytest -q                 ->  83 passed in 203.38s (0:03:23)   (run 10:08:48-10:12:13; the first run, started 09:46, was cut off by the PC restart at 09:56)
```
(sim/live, sim/haptic and sim/sleeve results above are from before the restart; the venvs survived it - `pip check` clean again at 10:08 - and all suites are re-run in Step 4.)
sim/live venv launches the three entry points exactly as the PlayMode tests do (throwaway ports, never 8787):
```
cd <repo root> ; sim/live/.venv/Scripts/python.exe -m sim.sleeve.twin --kind both --port-offset 30000 --seed 5 --no-stdin --duration 2
{"event": "ready", "kinds": ["haptic", "bio"], "ports": {"haptic": 38790, "bio": 38792, "discovery": 38791, "control": 38793}, "seed": 5, "pid": 33672}
{"event": "exit"}                                                     (exit 0)
cd sim ; ../sim/live/.venv/Scripts/python.exe -m live.fake_hub --help  -> usage banner (aiohttp + jsonschema import fine)
cd sim ; ../sim/live/.venv/Scripts/python.exe -m haptic.fake_haptic --port 38790 --discovery-port 38791 --selftest
  -> [device]/[legacy] acks, "Cues received: 2  Pings: 0  Acks sent: 2", exit 0
```
analytics venv, what the Unity PlayMode tests use it for: `analytics/.venv/Scripts/python.exe contracts/validate.py --session contracts/fixtures/sessions/healthy` -> `[PASS] All validations passed`.

### Files changed in Step 1
`sim/live/requirements.txt` +2 lines (comment + `numpy==2.5.3`), LF kept. Verified: `pip install -r requirements.txt` in sim/live/.venv -> everything "already satisfied". Nothing else touched.

### STEP 1 COMPLETE: 2026-10-08 10:12 IST (analytics suite finished 10:12:13)
The venvs themselves had been usable since 09:50 (sim/live, sim/haptic) and 09:44 (analytics install); 10:12 is when the last of the four verifications (analytics 83 passed) was green.
Resume note: the user restarted the PC at 09:56 and cut this run off; on resume I re-checked `git status` (clean under sim/ and tools/), ports 8787-8793 (free), python processes (none) before continuing.

---

## PLAN for Steps 2-4 and the new Step 2g (written before coding, 10:25 IST)
Findings that shaped it (all verified this session by reading the code):
- Unity's `DiscoveryHub` (game/Packages/com.opus.sdk/Runtime/Transport/DiscoveryHub.cs:76-100,222) takes the node host from the datagram SOURCE address (`result.RemoteEndPoint.Address`), not from the beacon's `ip` field. So the source IP of each beacon must be the LAN address: one socket bound to each local interface address. `phantom_replay._DiscoveryProtocol` (the fake headset) does the opposite (prefers the `ip` field, then the source), so the beacon of each interface also carries that interface's own `ip` (only when the datagram has an `ip` key: the team-dialect discovery has none).
- `fake_haptic.py` already binds 0.0.0.0; only its beacon (unbound sockets, hardcoded ip 127.0.0.1) has the problem; fixing it needs the interface enumeration (~60 lines) duplicated or a cross-folder import, and `test_fake_haptic.py` asserts on that beacon. Decision (2c): leave it, say so. The twin (`--kind haptic`) is the supported cross-machine stand-in.
- `PhantomHandFullRunTests.cs:82-85` (external-endpoint branch, game/ - not my lane) asserts `OPUS_PH_NODE_A/B` are set whenever `OPUS_PH_HUB` is. `--lan` must NOT set them (real discovery), so the Unity PlayMode path of `--lan` needs a Unity-track change (CROSS-TRACK REQUEST in the open issues). Not run here (brief: no Unity).
- `test_phantom_pipeline.py:100` builds `argparse.Namespace` by hand without the new flags -> the harness reads them with `getattr(args, "lan", False)` / `getattr(args, "dialect", "reference")`, otherwise an existing test would break.
- `phantom_replay.py:605` decides `delivered` with `ack.get("ok", ack.get("status") in (...))`: a team ack `{accepted:true}` has neither -> every stroke would count as undelivered. `:391` needs gyro keys (KeyError -> no IMU recorded when the node sends accel only). Both are fixed in sim/live (my lane) so the L3 harness can run against `--dialect team`.
Constraint: reference dialect and loopback behaviour byte-identical (38 sleeve tests, 26 haptic, 45 live, 114 demo tests unchanged). Failure modes: interface enumeration fails (ipconfig/ip/ifconfig missing, localized, timeout) -> fall back to `lan_ip()` /24 guess + `--broadcast-addr`, never raise; a bound beacon socket cannot bind -> skip that interface with a stderr line; a send error -> ignored per target. Ledger: no delegation (single agent, no subagents).
Order: twin.py (LAN, control bind, keepalive, display mode, team dialect, CLI) -> its tests -> phantom_replay -> harness flags (`--lan`, `--dialect`) -> tool path resolver (adb/dart/Unity) -> open_firewall.ps1 (+ read-only report) -> README -> full re-run (Step 4).

---
## CHECKPOINT 10:55 IST (steps 2, 2g, 3 coded; step 4 runs pending)
Done so far (details and real output are added per step below as they are verified):
- twin.py: LAN beacons per interface from bound sockets, control socket pinned to 127.0.0.1, keepalive/display-mode in every dialect, `--dialect team`, `_num` rejects NaN/Infinity (it used to kill the receive thread with ValueError).
- phantom_replay.py: `ack_delivered` (ok > accepted > status), accel-only IMU recorded with gyro 0.0 (as the Unity client does), bare `keepalive` to each node.
- run_pipeline.py / phantom_pipeline.py: `--lan`, `--dialect {reference,team}`; tool_paths.py (adb / dart / Unity resolver); prove_live_to_phone.py and start_pc_demo.ps1 use it.
- open_firewall.ps1 rewritten: report by default, change switches `-Apply [-Twin] [-Haptics]`, `-RemoveUnityBlock`, elevation check, ShouldProcess (-WhatIf/-Confirm). Only its report mode was run.
- Tests: sim/sleeve 38 -> 94 passed, sim/live +2, tools/demo/tests +15 (new files test_lan_mode.py, test_tool_paths.py).
- Line endings: all files are LF in this working tree (git ls-files --eol: i/lf w/lf); two test files were briefly rewritten as CRLF by my own helper script and converted back to LF (checked: 0 CRLF in every edited file).
Tooling note for the next agent: this shell's Bash tool rewrites backslashes inside heredocs (\ -> \, \t -> TAB); content with backslashes was written with the Write/Edit tools instead.

---
## STEP 2 - cross-machine twin (each audit finding verified against the code before changing it)

Verified first: `twin.py` `--host` default 127.0.0.1 (was ~983, now 1276) and the beacon went only to 255.255.255.255 + 127.0.0.1 from one unbound socket (was ~872-896) - true. Control socket bound `self.host` - true (so `--host 0.0.0.0` would have exposed `quit`/`off-a`/`loss` to the LAN). Also found: Unity's `DiscoveryHub` (game/Packages/com.opus.sdk/Runtime/Transport/DiscoveryHub.cs:76-100, 222) takes the node's address from the datagram SOURCE, not from the beacon's `ip` field.

### 2a/2b - sim/sleeve/twin.py (stdlib only)
| change | where |
|---|---|
| `is_loopback_host`, `parse_ipv4_interfaces` (ipconfig / `ip -4 -o addr` / ifconfig text -> (address, mask); language independent; never raises), `local_ipv4_interfaces` (runs the OS tool, [] on any failure), `beacon_targets` (directed broadcast per usable interface, loopback/link-local//31-/32 skipped, only the bound address when `--host` is one address, plus `--broadcast-addr` extras), `_with_ip` | twin.py:112-220 |
| `TwinServer._open_lan_beacons` / `_beacon_socket` (one socket bound to each interface address; bind errors -> stderr line + skip; no list -> /24 guess around `lan_ip()`), `_broadcast` sends the extra beacons from those sockets with the beacon's `ip` field set to that interface | twin.py:1096-1160 |
| control socket pinned to `127.0.0.1` whatever `--host` says | twin.py:1184-1186 |
| `--broadcast-addr IP` (repeatable); `ready` line gets `"lan":{"host","control","beacons"}` only for a non-loopback host (default ready line unchanged) | twin.py:1198, 1276-1280 |
| `_num` now rejects NaN/Infinity (json.loads accepts them; `int(nan)` raised inside the receive thread and silently killed the node) | twin.py:91 |
Loopback (`--host 127.0.0.1`, the default) is byte-identical: `lan_beacons` stays empty, the two old sends are untouched, the 38 original tests pass unchanged (checked with the new code before adding any test: `38 passed in 8.28s`).

Real evidence, the two-process smoke on this PC (listener = `scratchpad/lan_listener.py` bound 0.0.0.0:8791 with SO_REUSEADDR, twin in the foreground; both stopped by themselves):
```
twin:  sim/live/.venv/Scripts/python.exe sim/sleeve/twin.py --kind both --host 0.0.0.0 --no-stdin --duration 7      (11:02:37-11:02:45, exit 0)
{"event": "ready", "kinds": ["haptic", "bio"], "ports": {"haptic": 8790, "bio": 8792, "discovery": 8791, "control": 8793}, "seed": 1, "pid": 30396, "lan": {"host": "0.0.0.0", "control": "127.0.0.1", "beacons": [["192.168.56.1", "192.168.56.255"], ["192.168.242.55", "192.168.242.255"], ["172.29.80.1", "172.29.95.255"]]}}
listener output:
  from 127.0.0.1        bio    x8    ip field in the beacon: 192.168.242.55
  from 127.0.0.1        haptic x8    ip field in the beacon: 192.168.242.55
  from 172.29.80.1      bio    x8    ip field in the beacon: 172.29.80.1
  from 172.29.80.1      haptic x8    ip field in the beacon: 172.29.80.1
  from 192.168.242.55   bio    x8    ip field in the beacon: 192.168.242.55
  from 192.168.242.55   haptic x8    ip field in the beacon: 192.168.242.55
  from 192.168.56.1     bio    x16   ...
  from 192.168.56.1     haptic x16   ...
  distinct source addresses: ['127.0.0.1', '172.29.80.1', '192.168.242.55', '192.168.56.1']
  control port 8793 probes: 127.0.0.1 -> ANSWERED {"ok": true, "cmd": "status", ...}
                            172.29.80.1 / 192.168.242.55 / 192.168.56.1 -> silent (ConnectionResetError)
```
- Beacons DO arrive from the Wi-Fi address (192.168.242.55). That is this PC's Wi-Fi address now: the brief said 192.168.242.190, DHCP gave a new one after the restart (`Get-NetIPAddress`: Wi-Fi 192.168.242.55/24).
- The "limited broadcast leaves through the wrong adapter" finding is confirmed on this PC. Second run (offset 1000, ports 9790-9793, listener recording the pair source/`ip` field) shows two groups from source 192.168.56.1 (VirtualBox host-only): `ip` field 192.168.56.1 (the new bound-socket directed broadcast) and `ip` field 192.168.242.55 (the OLD unbound 255.255.255.255 beacon, whose `ip` field is the LAN address but which Windows sent out of the VirtualBox adapter). Before this change the Wi-Fi network got no beacon at all.
- Control port: answers on 127.0.0.1, silent through every LAN address (also covered by `test_control_port_is_not_reachable_through_the_lan_address_when_nodes_bind_all_interfaces`).

### 2c - sim/haptic/fake_haptic.py: LEFT UNCHANGED (not small)
It already binds 0.0.0.0 (line 143), so it is reachable; only its beacon (unbound sockets, hardcoded `"ip":"127.0.0.1"`, line 329, `test_fake_haptic.py` asserts on that beacon) has the multi-homed problem. Fixing it needs the interface enumeration (~60 lines) duplicated or a cross-folder import from sim/sleeve. The supported cross-machine stand-in is the twin (`--kind haptic`).

### 2d - harness `--lan` (run_pipeline.py:742-749, phantom_pipeline.py)
`run_pipeline.py --game phantom_hand ... --lan` (default off): the twin is started with `--host 0.0.0.0` (`twin_command`/`twin_host`), the Unity PlayMode environment gets no `OPUS_PH_NODE_A/B` (`unity_env`), and `lan_banner` prints the exact commands once:
```
python sim\sleeve\twin.py --kind both --host 0.0.0.0 --port-offset 0 --seed <seed> [--dialect team]        <- on the second PC
python tools\demo\run_pipeline.py --game phantom_hand --hardware --discovery-port 8791 --lan [--dialect team]  <- on this PC, no twin started here
```
The new flags are read with `getattr(args, "lan", False)` / `getattr(args, "dialect", None)`: `test_phantom_pipeline.py:100` builds `argparse.Namespace` by hand without them. The Unity path was NOT run (brief), only its pure builders are tested. `--hardware` + Unity used to crash (`twin.ports` with twin None); it now passes `{"discovery": ...}` and real discovery.
Run: `run_pipeline.py --game phantom_hand --sim --no-unity --lan` -> banner printed, `discovered: {'haptic': ('172.29.80.1', 39790), 'bio': ('192.168.242.55', 39792)}` (several local interfaces answered; the headset latches the `ip` field of the last beacon; every address reaches the 0.0.0.0-bound nodes), `7/7 checks passed -> G2 L3 GREEN`, exit 0 (11:00:37-11:02:01).

### 2e - tools/demo/open_firewall.ps1 (rewritten; ONLY the report mode was run)
Default = read-only report: network profiles + firewall profile settings; every rule of Unity.exe (action, direction, profile, protocol, ports); the OPUS port rules (HUB 8787, 8788, twin 8790/8791/8792) as inbound-Allow rules that name the port and are not tied to a program/service (bulk cmdlets, about 3 s); an explanation of the Block rule; the exact commands the switches run. Change switches (all need Administrator, ask for confirmation, support `-WhatIf`, skip existing rules): `-RemoveUnityBlock`; `-Apply` (program-scoped inbound UDP Allow for the pinned Unity editor + TCP 8787) `[-Twin]` (UDP 8790 + 8792) `[-Haptics]` (old 8790 + 8791 pair, kept because START_DEMO.md uses it). New rules use `-RemoteAddress LocalSubnet` (parameter). Non-elevated + change switch -> message and `exit 1` before anything runs (guard at lines 140-146; `Test-Elevated` evaluated in this session = False). Parses without errors in PowerShell 7 and Windows PowerShell 5.1; ASCII only. I did NOT run it with any change switch.
Report mode, real output (excerpt, `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\demo\open_firewall.ps1`, exit 0, "Nothing was changed"):
```
Name      InterfaceAlias NetworkCategory IPv4Connectivity
Tailscale Tailscale              Private        NoTraffic
Chulbul   Wi-Fi                   Public         Internet
Domain/Private/Public firewall: Enabled True, DefaultInboundAction NotConfigured
DisplayName             Enabled Direction Action Profile Protocol LocalPort
Unity 6000.4.6f1 Editor True    Inbound   Allow  Domain  Any      Any
Unity 6000.4.6f1 Editor True    Inbound   Block  Public  Any      Any
   TCP 8787 / UDP 8788 / UDP 8790 / UDP 8791 / UDP 8792 : missing   (no rule named OPUS*, and no rule in ANY state mentions ports 8787-8793)
  - Unity.exe has 1 enabled inbound BLOCK rule(s): Unity 6000.4.6f1 Editor [Public]. ... This PC is on a Public network right now (Wi-Fi).
Remove-NetFirewallRule -Name '{6CBC1AC1-EF06-4EFD-AB37-58BCCE7EB745}'    # Unity 6000.4.6f1 Editor
```
A first draft of the report said "present (Dell SupportAssist for Home PCs)" for every port: that rule has Any ports and is service-scoped. Fixed (an Any-port or program/service rule is not a port rule) and re-run before reporting.

### 2f - tests added
- sim/sleeve/test_twin.py (appended, no existing line touched): 38 -> 94. LAN block: parser fed canned ipconfig (English + German labels), `ip -4 -o addr`, ifconfig (macOS + net-tools) text and 12 garbage inputs; `beacon_targets`; `_with_ip`; loopback sends exactly the two old beacons and never enumerates; per-interface fan-out through fake sockets (bound address, `ip` patched, extras routed by subnet); team beacons byte-identical; a failing interface does not stop the others; /24 fallback; bind failure skipped; control socket on 127.0.0.1 with `--host 127.0.0.2` and (real, 0.0.0.0) not reachable through the LAN address; `_beacon_socket("127.0.0.2")` source address; CLI flags.
- tools/demo/tests/test_lan_mode.py (new, 8 tests): flag defaults/parsing, default `twin_command` equals the old argv flag for flag, `--lan`/`--dialect` add exactly their flags, hand-built Namespaces still work, `unity_env` with/without node overrides, `second_pc_command` exact string, banner, `run_phantom` prints the banner only with `--lan`.
- tools/demo/tests/test_tool_paths.py (new, 7 tests, step 3).

---
## STEP 2g - the electronics team's real firmware dialect (contracts v0.2.1, HAPTIC_PROTOCOL v1.3, PH_ELECTRONICS_HANDOFF_FROM_TEAM.md section A)
1. **keepalive in EVERY mode** (twin.py:339, `Node.handle_common`): known on both nodes, never `rx_invalid`/`unknown_type`, no reply, feeds the watchdog (every datagram already does) and refreshes a live subscriber like `ping`; it does not subscribe a stranger (spec D3).
2. **display `mode` in EVERY mode** (twin.py:504, `handle_display`): `text` and/or `mode`, same 12-char and 2/s rules, any bad field rejects the message; if both differ the reference twin shows `text`, the team twin `mode`.
3. **`--dialect team`** (default `reference`, byte-identical; `TeamNodeA`/`TeamNodeB` twin.py:795-860, built by `Twin(dialect=...)`): ids CHETNA_HAPTIC_001 / CHETNA_BIO_001; discovery exactly their 8 keys (no `ip`, no `opus_haptic`/`port`/`fw`); ack exactly `{type, device_id, cue_id, accepted, timestamp_ms}`, `accepted:false` for motor still running (`MOTOR_BUSY`), gap < 100 ms, duty >= 50 %/10 s, bad motor, intensity outside 0-255 (log codes only), 151-255 is capped to 150 not rejected, duration clamped; telemetry only to the sender of the last packet (any packet, one target, expires after 5 s without one - my assumption, flagged below); no `status`, no `emg_burst` messages (burst still in the log); Node B 4 values per packet at 25/s (`timestamp_ms` = first sample); Node A `sensor_data` accel only; `play_at_ms` ignored; everything their firmware does not document (ping, subscribe, `type:cue`, `type:haptic`, config, status_request) and garbage is ignored, counted in `status` as `ignored`, never acked. The twin's own log still records `status`/`error_code` per stroke, so `l3_checks.check_acks` keeps its independent witness.
- Harness: `--dialect {reference,team}` on `run_pipeline.py` -> `phantom_pipeline.twin_command` (`--dialect team` only when not reference).
- sim/live/phantom_replay.py (the fake headset the harness drives): `ack_delivered()` (ok > accepted > status; the old line gave every `{accepted:true}` ack as undelivered), accel-only IMU recorded with gyro 0.0 (what `SleeveSensorClient.cs:281-284` does; the schema requires gx/gy/gz), a bare `keepalive` to each node each second like the Unity client (`SleeveSensorClient.cs:194`). Tests: `test_ack_delivered_follows_the_contract_precedence...`, `test_headset_against_the_team_dialect_twin_delivers_by_accepted_and_records_accel_only_imu` (full mini replay against the team twin).
- Tests in sim/sleeve (2g1-3): keepalive known/silent/watchdog/refresh in both dialects, display mode/text/both + rate limit in both dialects, ids and exact discovery, exact ack + `play_at_ms`, busy/gap/duty/invalid motor-intensity (incl. NaN/Infinity: the node keeps serving), cap/clamp, stop with v1 extra fields, last-sender-only telemetry + expiry, no status/emg_burst, 4-value chunks 25/s, accel-only, ignore list, messages validated against `contracts/schemas/haptic-message.schema.json`, CLI subprocess in team mode.

### `--no-unity` smoke in both dialects (single runs each, sim/live venv, same seed 42)
| | reference | team |
|---|---|---|
| table | 7/7, G2 L3 GREEN, exit 0 (100 s) | 7/7, G2 L3 GREEN, exit 0 (86 s) |
| stroke cues acked | 16/16 (16 executed / 0 rejected in the twin log) | 16/16 (16 executed / 0 rejected) |
| hub RTT p50 / p95 | 10.1 / 18.8 ms | 7.7 / 12.8 ms (hub link, not the nodes: noise) |
| statuses with game_state | 148 | 147 |
| files uploaded to the hub | 20/20 (sens files 17) | 21/21 (sens files 18; same sample totals within 0.1 %: emg 5070 vs 5064, imu 5073 vs 5069: a 5-s chunk boundary) |
| sensor_chunk packets seen by the headset | 736 (10 values / 100 ms) | 1835 (4 values / 40 ms) |
| sensor_data packets | 7362 | 7342 |
| `emg_burst` messages from Node B | 2 | 0 (analytics recomputes the burst: flinch EMG peak x6.02 / x6.04 in both) |
| IMU in the sens files | gyro real | gyro 0.0 (flinch IMU peak 14.1 / 14.1 vs 14.3 / 14.2 m/s2, latency 147 ms in both) |
| flinch EMG latency (sync / async) | 114.4 / 114.3 ms | 117.6 / 106.5 ms (single runs: within run-to-run noise) |
| twin log records | 514 (90 tx acks = 16 strokes + 74 ping acks) | 440 (16 tx acks; the same 74 pings, 150 subscribes, 148 keepalives were received and ignored) |
Reading the old phantom_replay line (`ack.get("ok", ack.get("status") in (...))`), every `{accepted:true}` ack would have counted as undelivered (not run, inferred from the code).

---
## STEP 3 - old-PC paths
- New `tools/demo/tool_paths.py` (`resolve`, `find_adb`, `find_dart`, `find_unity`): environment variable, then PATH, then this PC's known places, the old value last; never requires the tool to exist. adb: `OPUS_ADB`, `ANDROID_HOME`/`ANDROID_SDK_ROOT`, PATH, `%LOCALAPPDATA%\Android\Sdk\platform-tools`, any Unity Hub editor's bundled adb, old `C:\Users\GARV BANSAL\...`. dart: `OPUS_DART`, `FLUTTER_ROOT`, PATH, `H:\flutter\bin\dart.bat`, old `C:/flutter/bin/dart.bat`. Unity: `OPUS_UNITY_EXE`, the Hub editor of the version `game/ProjectSettings/ProjectVersion.txt` pins (6000.4.6f1), old path. **No PATH lookup for Unity**: on this PC `C:\Users\DELL\AppData\Local\Unity\bin\Unity.EXE` is on PATH and is the Unity CLI ("unity" 1.0.0.0, 27 MB), not the editor (editor: "Unity Editor" 6000.4.6.722204); the first version of the resolver picked it, a real-machine check caught it, fixed + test added.
- prove_live_to_phone.py:46 (`ADB_DEFAULT = str(find_adb())`), phantom_pipeline.py:63-64 (`DART`, `UNITY_EXE`), start_pc_demo.ps1:62-70 (`Resolve-Dart`, same order; exercised in isolation: no override -> `H:\flutter\bin\dart.bat`, `OPUS_DART` honoured).
- On this PC now: `find_adb` -> `C:\Users\DELL\AppData\Local\Android\Sdk\platform-tools\adb.exe`, `find_dart` -> `H:\flutter\bin\dart.bat` (exists), `find_unity` -> `C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe` (exists).
- Grep of sim/ and tools/demo/ for GARV / C:\flutter / Documents\VR_games after the change: only the last-fallback constants (tool_paths.py:24-25, start_pc_demo.ps1:68, test_tool_paths.py:53) and DOCS left alone by rule: tools/demo/START_DEMO.md (many), run_pipeline.py:39 docstring, watch_and_analyse.py:25 docstring, comment lines.

---
## STEP 4 - everything again (final code state; one suite at a time; real summary lines)
```
contracts/validate.py (cwd contracts, sim/haptic venv = jsonschema 4.23.0)   [PASS] All validations passed   (112 [PASS], 0 [FAIL], exit 0)
analytics        analytics/.venv   -m pytest -q                              83 passed in 284.28s (0:04:44)
sim/haptic       sim/haptic/.venv  -m pytest -q                              26 passed in 2.41s
sim/sleeve       sim/haptic/.venv  -m pytest ../sleeve -q                    94 passed in 10.29s
sim/live         sim/live/.venv    -m pytest -q                              47 passed in 55.05s
sim/live e2e     sim/live/.venv    -m pytest e2e.py -q                       3 passed in 14.43s
tools/demo/tests A  sim/live/.venv (all but the two files below)             110 passed, 8 skipped in 69.64s (0:01:09)
tools/demo/tests B  analytics/.venv: test_check_session.py + test_watch_and_analyse.py   11 passed in 23.28s
   A + B = 121 passed + 8 skipped = 129 collected (baseline 114 collected = 106 + 8; +15 new)
run_pipeline.py --game phantom_hand --sim --no-unity                         7/7 checks passed -> G2 L3 GREEN   exit 0
   ... --dialect team                                                        7/7 checks passed -> G2 L3 GREEN   exit 0
   ... --lan                                                                 7/7 checks passed -> G2 L3 GREEN   exit 0
```
The 8 skips are the baseline's (recording `app/.hub_data/c93ae4fa-...` is not in the clone). pytest-asyncio's PytestDeprecationWarning is the baseline's open issue 3.
Process / port hygiene: every process started by my runs has exited (the only leftover I ever caused was an orphan twin from my deliberate run of the whole demo suite in the analytics venv - finding 2 - killed by PID after checking its command line). My two-process LAN smoke released 8790-8793 at 11:02:45. After that the UNITY agent's runs (parent Unity PID 2460, using the venvs created here) took ports: at 11:03 UDP 0.0.0.0:8790 was held by PID 30312 (`haptic.fake_haptic`, its HapticIntegration run9); at 11:10 TCP 0.0.0.0:8787 by PID 28484 (`live.fake_hub --port 8787`) plus a twin on offset 12100 (20890-20893), i.e. its PH_FullRun. Not mine, left alone; the final port check is in the report.

## Findings that need a decision (not changed)
1. **tools/demo/tests cannot run in ONE per-folder venv.** `test_check_session.py` and two tests of `test_watch_and_analyse.py` need scipy (opus_analytics) = analytics venv; everything that starts the hub/headset needs aiohttp = sim/live venv. The docstrings of the test files all say `analytics/.venv/...`, which is wrong for `test_phantom_pipeline.py` (3 tests fail there: no aiohttp). Used: A (sim/live venv, ignoring those two files) + B (analytics venv).
2. **Test hygiene:** `test_twin_subprocess_is_ready_when_it_says_so_and_stops_on_quit` has no try/finally: when it fails before `tw.stop()` (it did in the analytics venv, no aiohttp) the twin on offset 14010 keeps running and every later run of that test fails with "twin did not print its ready line" (ports 22800-22803 busy). Seen and fixed by killing PID 3500 (command line checked).
3. **CROSS-TRACK REQUEST (Unity track, not my lane):** `game/Assets/Shell/Tests/PlayMode/PhantomHandFullRunTests.cs:82-85` (`external` branch, taken whenever `OPUS_PH_HUB` is set) asserts `OPUS_PH_NODE_A/B` are set. `--lan` must not set them (real discovery), and `PhantomEndpoints.Resolve` already supports absent node hosts. The PlayMode path of `--lan` therefore needs that assertion relaxed (use discovery on `OPUS_PH_DISCOVERY_PORT` when they are absent). Also `LiveClient.HubPort` is a const 8787.
4. **Question for the electronics team:** HANDOFF says nodes stream "only to the IP of whoever last sent a packet" and "telemetry comes back on UDP 8790". The twin streams to the sender's (IP, source port). If the real firmware sends to (IP, 8790), a headset using an ephemeral socket gets nothing. Also: does the stream stop when the keepalives stop? The twin expires the target after 5 s (assumption).
5. **HAPTIC_PROTOCOL v1.3 vs sim/sleeve:** unknown/unparseable messages must NOT feed the 2 s watchdog (v1.2 table); the twin feeds it on any datagram (pre-existing, unchanged in both dialects).
6. fake_haptic.py unchanged (2c). Docs with old-PC paths (START_DEMO.md etc.) unchanged.
7. docs/MANUAL_TODO.md already lists the Windows Firewall step; `open_firewall.ps1` is now the "report and fix" tool it mentions. The Block rule removal / Allow rules are a HUMAN step in an elevated PowerShell: `tools\demo\open_firewall.ps1 -RemoveUnityBlock -Apply -WhatIf` first, then without `-WhatIf`.
8. Heads-up: my heavy suites (analytics 11:03-11:08, tools/demo 11:09-11:10) ran while the Unity agent's timing-sensitive PH_FullRun (it counts frame hitches) was going; if its run shows hitches in that window, that is why.
9. Shell tooling: this session's Bash tool rewrites backslashes in heredocs (`\\` -> `\`, `\t` -> TAB); files with backslashes were written with the Write/Edit tools. A helper script that used `Path.write_text` on Windows turned two LF files into CRLF; both were converted back (0 CRLF in every edited file now).

## Next step
Opus: review the diff (git diff -- sim tools, 3 new files in tools/demo), decide findings 1-3 and 5; commit (agents do not commit). Human: the firewall step (finding 7) and, for the cross-machine run, `python sim\sleeve\twin.py --kind both --host 0.0.0.0 --port-offset 0 --seed 1 [--dialect team]` on the electronics PC.
