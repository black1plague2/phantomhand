# 2026-10-08 — PH-O RESUME run 1 (Opus, new machine)

**Goal (user):** configure everything from `black1plague2/phantomhand` on this machine, read all docs, pick up
where the other agents left off, build end to end. Electronics run on a different machine. Use Sonnet/Haiku
subagents; drive the open Unity editor through the Unity MCP; parallel agents for every task in the docs.

## Machine (measured 08:25–08:45 IST, this PC, user `DELL`)
| Item | State |
|---|---|
| Repo | `H:\Chenta\phantomhand`, `main` @ `40af01b`, clean except editor-written files (below) |
| Unity | 6000.4.6f1 editor OPEN on `game/` (PID 26052); modules AndroidPlayer + windowsstandalonesupport; `Editor.log`: 0 `error CS`; `CompilationTools.GetCompilationStatus` → `clean`, errorCount 0 |
| Unity MCP bridge | listening on 48735/48736; 16 tools; reachable (see "Unity MCP root cause") |
| Python | 3.12.8 (+3.11); venv for the repo at `.venv` (Python baseline agent) |
| Flutter | **NOT installed** (no `C:\flutter`, not on PATH) — needs a download, asked the user |
| Android SDK | `%LOCALAPPDATA%\Android\Sdk` (platforms 35/36, NDK 27) + Unity's own; adb only inside Unity's SDK, not on PATH |
| Visual Studio | 2022 + 2026 with the C++ desktop workload (Flutter Windows build possible) |
| Java | Microsoft OpenJDK 17.0.20 |
| Devices | none attached (`adb devices` empty): no phone, no Quest on this PC yet |
| Port 8787 | **held by an unrelated program of the user** ("Momentum Dashboard", `python scripts\dashboard.py`, PID 14036, 127.0.0.1 only). Not ours to kill; asked the user |
| Network | Wi-Fi 192.168.242.190/24; also VPN 10.11.11.6/24, Hyper-V 172.31.176.1/20, VirtualBox 192.168.56.1/24 (multi-homed: matters for UDP discovery) |
| Arduino toolchain | none (electronics are on another machine by user decision) |

Editor-written local changes (not ours):
- `game/Assets/Resources/DevAgentSettings.asset` — the editor wrote the bridge token back. **Never commit it** (public repo).
  Marked `git update-index --skip-worktree` on this clone so `git add -A` cannot pick it up.
- `game/ProjectSettings/ProjectSettings.asset` — editor added a `WindowsStandaloneSupport` graphics-API entry (D3D11). Kept, unreviewed.

## Unity MCP — root cause of "registered and connected, but the session has no tools" (FIXED)
- `claude mcp add` (what the editor's Register button runs, cwd = `game/`) stores a **local-scope** entry keyed by the
  **git root** (`H:/Chenta/phantomhand`). A session opened in any other folder (this one started in `H:\Chenta`) never loads it.
- The editor's status check (`com.meta.xr.sdk.core/Editor/MCPBridge/McpRegistration.cs`, `CheckClaudeCodeStatusSync`) reads
  `~/.claude.json` → `projects["<Unity project dir>"]` = `H:/Chenta/phantomhand/game`, which never exists, then falls back
  to the **user-scope** list. So it shows "Not Registered" and Register reports "Registration returned unsuccessful".
- Fix: register at **user scope** — `python tools/unity_mcp.py register` (reads the token from the local asset, never prints it).
  Verified: `claude mcp get meta-xr-unity-runtime` from `H:\Chenta` → "Scope: User config … ✔ Connected"; a replay of the
  editor's check logic → Registered. Re-run it after "Regenerate" in the editor's AI Tools settings.
- A running session still only gets MCP tools at start. So this session (and any agent) drives the editor with
  **`python tools/unity_mcp.py`** (plain MCP-over-HTTP client: `list`, `schema <tool>`, `call <tool> '<json>'`). This removes the
  "restart the session after every editor restart" trap in the older logs. Still ONE Unity driver at a time (`game/.ph_unity.lock`).

## Plan (step → verify)
1. Baselines on this machine → real counts per suite (Unity EditMode + PlayMode by Opus; Python suites by an agent).
2. Remove Orchard Reach (user decision) → clean compile, every remaining test that passed in the baseline still passes.
3. Fix Phantom Hand test failures (U4, U5 never ran) → EditMode + PlayMode green; `CaptureU4Shots` screenshots on disk.
4. Wire builder hooks, bake models with the rigged hand, bootstrap scene → scene opens, PlayMode PhantomHand group green, screenshot.
5. Merge the SDK change set (real firmware dialect + live-link fixes) → SDK EditMode tests green.
6. Flutter: install, `pub get`, `flutter test` (+303 expected), `analyze`, Windows desktop hub build → hub answers on its port.
7. L3 with Unity (`run_pipeline.py --game phantom_hand --sim`, then `--faults`) → 04-E2E.md G2 table filled with real numbers.
8. U6: Android build (APK), demo mode, performance; additions A1–A3; docs (H2, H4, H5), CONTEXT/PH_STATUS/CHANGELOG.
9. Cross-machine electronics: discovery + manual host work with nodes/twin on another PC → twin on a second address answers.

Constraint: the one open Unity editor is a serial resource and the critical path; parallel agents only help around it
(SDK code in a worktree, Python, Flutter, docs). Correctness is the second constraint: several pieces compile but never ran.
Failure modes: an agent reports green without output → Opus re-runs the decisive suite before any commit; an agent dies
(usage limit) → resume from its checkpoint log; two agents touch the editor → only one brief ever carries the MCP, lock file;
editor domain-reload hang → driver stops and reports, Opus restarts the GUI editor; port 8787 held → env failure, not a code defect.
Ledger: docs sweep → Haiku (pure reading); spec/log gap worklist, network audit, Python baseline, SDK change set, Unity driver
→ Sonnet (scoped work with checks); plan, contracts, review, commits → Opus.

## Agents dispatched (08:40–08:50)
| Agent | Model | Scope | Output |
|---|---|---|---|
| Docs sweep | Haiku | all non-PH docs → setup, ports, human steps, stale values | returned (summary folded in below) |
| Gap worklist | Sonnet | PRD/spec/logs/code → unfinished + non-conforming items | `logs/sessions/2026-10-08-PH-O-WORKLIST-run1.md` |
| Network audit | Sonnet | cross-machine readiness of game/hub/sleeve sockets | reply to Opus |
| Python baseline | Sonnet | `.venv`, all Python suites, L3 `--no-unity` smoke | `logs/sessions/2026-10-08-PH-S-BASELINE-run1.md` |
| SDK change set | Sonnet (worktree) | 7 items: `accepted` acks, keepalive, display `mode`+`text`, `device_kind`, `hubPort`, UDP 10054, stroke re-schedule | `logs/sessions/2026-10-08-PH-U-SDKB-run1.md` (in its worktree) |

Docs sweep findings that matter here: `docs/TESTING_RUNBOOK.md` is stale (old PC paths `C:\Users\GARV BANSAL\…`, old counts
107/157/49); sleeve IP 10.179.145.125 and phone hub IP in the docs are stale; firewall rules are in `tools/demo/open_firewall.ps1`
(needs an admin prompt: human step).

## Results

### Baselines on this machine (before any fix)
| Suite | Result | Evidence |
|---|---|---|
| Unity EditMode (all) | 343 tests: **329 passed, 14 failed** | `python tools/unity_mcp.py test EditMode` (run `b249c159`, 35.6 s) |
| Unity PlayMode (all) | 29 tests: **25 passed, 4 failed** (all 4 environment: `sim/live/.venv` python missing; hub upload) | run `a8648869`, 64.4 s |
| `contracts/validate.py` | All validations passed (105 PASS) | `logs/sessions/2026-10-08-PH-S-BASELINE-run1.md` |
| analytics / sim haptic / sleeve / live | 83 / 26 / 38 / 45 passed | same log |
| `tools/demo/tests` | 106 passed, 8 skipped (recorded Orchard session `app/.hub_data/c93ae4fa-…` was never committed) | same log |
| L3 `--game phantom_hand --sim --no-unity` | 7/7; `--faults` A/B/C all green | same log |

EditMode failures (all in code that had never run): 12 × `UiPanelTests` (`MissingComponentException: no 'Canvas' attached` in
`PhUiKit.PreparePanel` — `GetComponent<T>() ?? AddComponent<T>()` never adds in the Editor, Unity fake null);
`PhantomLiveStatusTests.Trace_20HzBins_AreAveragedAndAligned` (expected 422.5, was 420.0);
`PhantomHandRigValidatorTest.Scene_HasAnchorsComponent_AndEveryAnchorIsWired` (committed scene is still the U3 scene).
By namespace: Sdk 123, OrchardReach 45, PhantomHand 135, Shell 39, 1 Addressables stub.

### Second root cause found: test runs never start while the editor is unfocused
Meta's bridge (`com.meta.xr.sdk.core/Editor/MCPBridge/TestRunnerTools.cs`) hands `ListTests`/`RunFiltered`/`RunAll` to the main
thread with `EditorApplication.delayCall`, which does not fire while the editor window is unfocused (their own
`CompilationTools.cs` comment says so and uses `SynchronizationContext.Post` instead). Symptom: `RunAll` hangs for ever, `GetResults`
shows `runId: null`. Window messages (WM_NULL, repaint, mouse move) do not wake it. Fix in `tools/unity_mcp.py`: while waiting,
call `EditorApplication.Internal_CallDelayFunctions` through the reflection tool (`pumped()`, used by `call` and `test`).
Also: the bridge records every result twice (686 results for 343 tests); `test` de-duplicates by full name.
EditMode and PlayMode both run fine with the editor in the background this way.

### Findings that change the plan
- **Firewall (human, admin):** Wi-Fi "Chulbul" is a *Public* network and the inbound rule "Unity 6000.4.6f1 Editor" has
  Allow/Domain + **Block/Public** (verified with `Get-NetFirewallRule`). Node beacons (UDP 8791) and hub beacons (UDP 8788) cannot reach
  the editor from another machine until that Block rule is removed. Loopback runs are not affected.
- **Scene/boot path not wired:** committed `PhantomHand.unity` has no U4 UI, no `PhantomHandSceneController`, no Bootstrap scene;
  build settings boot OrchardReach. An APK built now would boot Orchard.
- **Orchard removal is deferred until the demo path is green:** `PhantomHandSceneBuilder.BuildScene` copies the Meta camera rig from
  `OrchardReach.unity` (lines 52–57), so deleting Orchard first breaks every scene rebuild. Order now: U4/U5 fix + scene → SDK merge
  → L3 with Unity → APK → models/rigged hand → Orchard removal.
- Cross-machine blockers (network audit, 13 items) → SDK change set, U6 code (Quest endpoint file, multicast lock), sim agent (twin on LAN).
- `emg_burst` is never written by Unity (`PhantomHandModule.SubmitEmgBurst` has no caller) → in the Unity driver's list (B6).

### User decisions today (asked 09:40)
Flutter → install to `H:\flutter`. Port 8787 → stop the user's dashboard (done 09:45, port free). Phone `164cd676` → plugged in,
`unauthorized`, user will tap Allow later. Commits → commit + push to `main` at verified gates.

### Commits
`018682b` ph(o): new-machine baseline, Unity MCP client, single-venv fallback (pushed).

## Agents — wave 2 (09:50)
| Agent | Model | Scope | Log |
|---|---|---|---|
| Unity driver (holds the editor) | Sonnet | `??` fix, live-status bin test, hooks B2/B6, BuildScene, BuildBootstrap, EditMode + PlayMode green, PH_FullRun, U4 shots | `2026-10-08-PH-U-U45FIX-run1.md` |
| Sim | Sonnet | per-folder venvs (unblocks PlayMode), twin on LAN, harness `--lan`, firewall script, old-PC paths | `2026-10-08-PH-S-XMACHINE-run1.md` |
| App | Sonnet | Flutter at `H:\flutter`, pub get, test, analyze, Windows build, debug APK | `2026-10-08-PH-A-SETUP-run1.md` |
| U6 code (worktree) | Sonnet | PH APK build method, demo mode, Quest endpoint file, multicast lock | `2026-10-08-PH-U-U6CODE-run1.md` |
| SDK change set (worktree, from wave 1) | Sonnet | 7 items | `2026-10-08-PH-U-SDKB-run1.md` |
| Scribe | Haiku | CHANGELOG, PH_STATUS, MANUAL_TODO | — |

## CHECKPOINT
09:55 — baseline committed and pushed (`018682b`); wave 2 running; the Unity driver holds `game/.ph_unity.lock`.
Resume: read this file, then `logs/sessions/2026-10-08-PH-O-WORKLIST-run1.md` and the wave-2 logs above. Opus still owes: O2 review
of each agent diff, merging the two worktree patches (SDKB, U6CODE), contract requests, L3 with Unity, APK build, models, Orchard removal.
