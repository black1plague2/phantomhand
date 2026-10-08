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
(filled in as runs complete)

## CHECKPOINT
08:50 — baselines in flight. Unity EditMode all (343 tests listed) started through the bridge by Opus; nothing committed.
Resume: read this file, then `logs/sessions/2026-10-08-PH-O-WORKLIST-run1.md`; re-run `python tools/unity_mcp.py call CompilationTools '{"method":"GetCompilationStatus"}'`.
