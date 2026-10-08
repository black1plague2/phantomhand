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

## Interruption 09:56 and recovery (times from the system clock; earlier clock times in this log run ~20 min fast)
- 09:56:48 the user restarted the PC (System log event 1074, user-initiated; not a crash). It closed the editor and killed the four
  running agents (Unity driver in step 7, Sim in step 1, Flutter mid-download at 1.54 of 1.93 GB, U6 code before any write).
- After the restart: the user's dashboard came back on 127.0.0.1:8787 (scheduled task `MomentumDashboard`, runs at logon) → stopped
  again (same-day approval; not disabled: the task is the user's). The user reopened the editor at 10:05 (bridge up). The bridge kept
  a stale "test run in progress" flag from the interrupted PlayMode run → cleared with `TestRunnerTools.CancelTestRun`.
  A crash-recovery scene `game/Assets/_Recovery/0 (1).unity` written at startup was deleted (untracked junk).
- Phone `164cd676` is now authorised (`adb devices` → `device`); it carries an older `com.opus.opus_app` 1.0.0 (installed 01:39).
  The user approved using and testing it.
- Agents were resumed from their transcripts with SendMessage (driver, sim, Flutter, U6 code); nothing committed was lost.

## Work since the restart
| What | Result | Evidence |
|---|---|---|
| SDK change set (worktree) reviewed and merged | 7/7 items; one review change by Opus: `keepalive` keeps going to a silent node (a power-cycled node only streams to a peer it has heard from) | `logs/sessions/2026-10-08-PH-U-SDKB-run1.md`; commit `f9b7ae8` |
| Unity EditMode after the merge | **378 passed, 0 failed** (343 + 35 new) | `python tools/unity_mcp.py test EditMode` (run `7204271b`, 20.4 s) |
| Unity driver steps 1–6 | EditMode 343/343 before the merge; scene rebuilt with U4 UI + controller; Bootstrap scene; build order Bootstrap, PhantomHand, OrchardReach | `logs/sessions/2026-10-08-PH-U-U45FIX-run1.md`; commit `f9b7ae8` |
| Contracts v0.2.1 | ack `accepted` (bool), `keepalive`, display `mode`; HAPTIC_PROTOCOL v1.3 | commit `8c88d0d`; `validate.py` PASS |
| Fixture drift (PH_STATUS Flag 1) | fixtures' witness numbers were raw x in cm; now post − pre like Unity and analytics (2.0 / 0.0) | commit `4a1c93e`; `test_embodiment` 32 passed, fixture freshness 9 passed |
| Docs (scribe, reviewed) | CHANGELOG, PH_STATUS counts, MANUAL_TODO new-machine steps | commit `b61dd73` |

## Agents — wave 3 (10:20–10:45)
| Agent | Model | Scope | Output |
|---|---|---|---|
| Unity driver (resumed; holds the editor) | Sonnet | step 7 PlayMode all + PH_FullRun, step 8 U4 shots, step 9 proof, step 10 hubPort through ISessionHost, step 11 app-pause → Pause/ResumeSession | `2026-10-08-PH-U-U45FIX-run1.md` |
| Sim (resumed) | Sonnet | twin on LAN, `--lan` harness, firewall script, old-PC paths, **`--dialect team`** twin (real firmware behaviour) | `2026-10-08-PH-S-XMACHINE-run1.md` |
| App (resumed) | Sonnet | Flutter 3.47.4 at `H:\flutter`, tests, Windows build, APK → install on the phone, hub check through `adb forward tcp:8797 tcp:8787`, replay → live card screenshots | `2026-10-08-PH-A-SETUP-run1.md` |
| U6 code (resumed, worktree) | Sonnet | PH APK build method, demo mode, Quest endpoint file, multicast lock | `2026-10-08-PH-U-U6CODE-run1.md` |
| Models (worktree; user: "incorporate the 3D models") | Sonnet | rigged skin hand replaces the procedural hand, wrappers baked from the builder, model table, roles for the other Meta models, capture method | `2026-10-08-PH-U-MODELS-run2.md` |
| Research R1 illusion levers · R2 two-motor stroke rendering · R3 app/operator UX · R4 demo, pitch, alternatives (user: "alternative research agents", "keep adding features from the research") | Sonnet ×4 | one report each, ranked change lists | `docs/agent-briefs/ph/research/R1…R4-*.md` |

Queue for the editor (serial), after the driver's current run: merge + bake MODELS → merge U6 code → Android target switch + APK →
L3 with Unity (`run_pipeline.py --game phantom_hand --sim`, both twin dialects) → research-driven features → Orchard removal.

## CHECKPOINT
10:50 — commits `018682b`, `b61dd73`, `8c88d0d`, `f9b7ae8`, `4a1c93e` pushed. Nine agents running (table above). The driver holds
`game/.ph_unity.lock`. PC port 8787 is free; the phone forward uses PC port 8797.
Resume: read this file, then each agent's log above; worktrees are under `.claude/worktrees/` (`git worktree list`); merge a worktree
with `git -C <worktree> diff --binary > patch` + `git apply --3way patch` on main, then recompile and `python tools/unity_mcp.py test EditMode`.
Opus still owes: O2 of every agent diff; merging MODELS and U6CODE; choosing and assigning features from R1–R4; L3 with Unity; APK;
Orchard removal; CONTEXT/PH_STATUS refresh; the firewall commands for the user.

## WAVE 4 (10:58-11:31 IST): Theme 5 audit applied, Orchard stopped, research R2/R3 acted on

User input in this window: (1) a Theme 5 audit pasted with "choose what makes sense and apply it"; (2) "why is orchard reach being played ... kindly stop that and ignore that project"; (3) "switch the 3d models, the hand is from sketchfab i downloaded it free"; (4) "you can test phantom hand, just stay away from orchard reach".

### What was taken from the audit (decisions D11-D15 in 03-SPEC)
| Audit item | Decision | Where |
|---|---|---|
| Do not claim the demo proves or measures consciousness; q4 is a pointer | APPLIED | closing copy (builder), docs/PH_JUDGE_SHEET.md, playbook pitch line 3, D11 |
| Witness screen: what changed / the observer | APPLIED, regrouped as Body (measured) / Mind (reported) / The one who noticed; the audit's "flinch under Mind" was not followed (a reflex is body) | builder step 1, D12 |
| Invisible-hand finale as the climax | APPLIED: Dissolve made real; Reveal ships as the fallback first (arm glides back onto the tracked real hand); real passthrough only after a headset check | builder steps 2-3, D13 |
| Agency (EMG closes the hand, then it closes by itself, q5) ahead of the breathing arm | APPLIED as opt-in, once per run in the last condition, hand-tracking fallback without EMG | builder step 4, D14 |
| Opening pitch, judge questions, mapping table, self vs Self | APPLIED | docs/PH_JUDGE_SHEET.md |
| Third block sync -> async -> sync | REJECTED: 4-minute budget; machine, events, analytics and witness compare exactly two conditions; D9 already gives unconvinced -> built -> removed | D15 |
| Audit's run order (sync first) | NOT followed: D9 (async first, user-approved 7 Oct) keeps the strongest illusion right before the finale; `condition_order` still switches it | judge sheet section 4 |
| Breathing arm, voice-over | stay unbuilt | D14 |
Audit claim not verified by me: "the theme guide lists a phantom hand in VR among its examples" (I have not seen the PDF); the judge sheet says to check before quoting.

### Orchard Reach
Cause: the Unity driver's brief said "PlayMode all"; its runner used the filter `.*`, which plays the old Orchard tests. Action 11:05: killed that runner, `TestRunnerTools.CancelTestRun` (only resets state), `EditorApplication.ExitPlaymode` (verified isPlaying false twice), killed the orphan fake_hub and twin it left on 8787 / offset 12100, told the driver: PlayMode filter `PhantomHand|PH_` only, never load or play Orchard. Rule now in 03-SPEC D16 and in memory. Orchard code is not being removed (the PH scene builder still copies the camera rig from OrchardReach.unity); the PH APK plan never includes the Orchard scene.

### Unity driver unblocked
Its CROSS-TRACK REQUEST S1 confirmed by reading LiveMessage.cs:86-87 and LiveClient.cs:142-146,161,430: every trial_event sat in the live outbox and was re-sent each second (84x duplicates, main-thread hitches, stroke cues dropped as late: 69-92 % acked vs the 95 % bar). The driver was authorised to apply the two-line SDK fix itself plus three tests.

### Research acted on
- R2 (two-motor stroke): the brush ran at 1 m/s. D16: default `motor_soa_ms` 833 (12 cm/s), one tap per motor as the brush passes, strokes never overlap (+300 ms); flick stays reachable; bench protocol and four firmware questions in MANUAL_TODO.
- R3 (app UX): D1 (card unreachable before Start and after a run on a real Quest) is the one app defect that can block the demo. D17: questionnaire values 1..7 on the wire, blue/amber for the two conditions.

### Agents (plan lines)
Constraint: the single open Unity editor (one driver); everything else is parallel and file-disjoint. Failure modes: a builder edits a file the driver holds -> forbidden lists + "integration lines owed by Opus" + `git apply --check` before any merge; code that never ran in Unity -> merged only through the editor queue with EditMode all + PlayMode `PhantomHand|PH_`; plausible-but-wrong UI -> screenshots read by Opus before commit; CPU load from Flutter/pytest spoiling the timing-sensitive full run -> concurrency caps in the briefs, heavy suites not started by Opus while the driver runs.
Ledger: driver S1 fix = delegate (owns the editor). Finale/witness/slow brush/agency = one Sonnet builder, worktree, steps 1, 2, 3, 3b, 4 (shared files, so one agent, not four). App live card (B1, B2 without a plugin, B3, B7, B8, B12) = Sonnet, worktree. Witness mirror (+ report if time) = Sonnet, worktree, new files only. Judge sheet, spec decisions, MANUAL_TODO, sim review + commit = Opus direct (claims discipline and contracts are not delegated).
Commits: 9666dd8 (sim: LAN twin, team dialect, firewall report script; re-run by review: sleeve 94, lan/tool_paths 15, phantom_replay 18), ab533e2 (judge sheet, D11-D15, R3), c57e529 (R2, D16-D17, human steps). All pushed; remote main verified at c57e529.
Open flake: sim/live test_phantom_replay.py failed once (1 of 18) in five full runs while Unity was compiling; not reproduced, test name not captured.

### Queue for the editor (serial), after the driver releases the lock
1. O2 + commit of the driver's change set. 2. Models merge + bake (user asked: switch to the 3D models). 3. U6 patch + integration lines (multicast lock, Bootstrap advertises only loadable games). 4. Finale/witness/slow-brush/agency patch + owed lines (ModuleTests.cs:130, `additionsEnabled` true in PhantomHandSettings.cs and the asset, manifest copies, full-run phase list). 5. Android target + APK. 6. L3 with Unity, both twin dialects.

## WAVE 5 (11:30-13:10 IST): research R4 and R1 applied, first full run, U6 code, models, runbook, the audit in the game, operator app
What landed on `main` (tests as each commit message states them):
- 98b5cde, 808b4d3: rigged-hand credit in `CREDITS.md`, research R4; judge sheet leads with the flinch and the rating, fallback steps.
- 15cca8d (11:49): first real full run in the editor; fixed: the live outbox re-sent every trial_event, contract values, sensor recorder. EditMode 383/383, PlayMode Phantom Hand 20/21.
- 9fd4e18 (11:55): U6 code: Phantom Hand APK method, `phantom_endpoints.json`, multicast lock. EditMode 511/511.
- 17ba2ee (12:01): research R1; D18-D20 (rating after both conditions, demo induction min(`induction_s`, 60), swap guard, stone volume).
- 7f64e29 (12:31): 3D models in the game, rigged hand with CPU skinning. EditMode 514/514, PlayMode presentation tests 20/20.
- a667def (12:33): `docs/PH_ON_DEVICE_RUNBOOK.md`; `tools/demo/sleeve_station.py` (38 tests); operator app on the team phone as the side-by-side build `com.opus.opus_app.pc`.
- 29b0c1d (13:01): the audit in the game: witness regroup, Dissolve + Reveal (fallback), slow brush (`motor_soa_ms` 833), opt-in agency, rating after both conditions, background chunk writes. EditMode 618/618, PlayMode Phantom Hand 25/26.
- 9a011a3 (13:02): seven prop GLB models, not wired. aec2aed (13:06): operator app live card, audience results mirror, embodiment report; flutter test +455.
Open:
- `PH_FullRun` is red on one bar: cues acked 88-91 % against 95 %. The editor main thread stalls 150-400 ms about 80 times per run. The test tool's polling and the chunk writes do not explain it. Cause not found.
- Real passthrough for the Reveal needs a headset (only the fallback exists). The Quest APK was never built. L3 with Unity was not run. Nothing ran on a headset or on the real boards.
- The seven props are not wired. Four Unity Asset Store packs named in `CREDITS.md` are not imported and must stay out of git. Meta asset terms are unchecked.
- Cosmetic (7f64e29): the forearm tone is lighter than the hand; the brush hides part of the hand at stroke start.
