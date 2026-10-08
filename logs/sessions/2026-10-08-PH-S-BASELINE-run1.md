# PH S BASELINE run1 (2026-10-08): Python side set up and baselined on the new Windows PC

## Goal
Set up the Python side of the repo on this freshly cloned machine (one venv at `H:\Chenta\phantomhand\.venv`, Python 3.12),
run every Python test suite plus the simulated pipeline smoke test, and report real results with root causes for anything
that fails. Earlier agents ran these on a different PC; this is the baseline for this machine.

## Environment (verified this session)
- Windows 11 Pro 10.0.26300, user DELL. Python 3.12.8 (`C:\Users\DELL\AppData\Local\Programs\Python\Python312\python.exe`); 3.11.9 exists via `py -3.11` (not used). Git Bash and PowerShell 7 available.
- Flutter NOT installed -> everything needing flutter/dart is "needs Flutter" and was not run (app/ tests, `phantom_pipeline --hub flutter`, `start_pc_demo.ps1`).
- Unity editor is open on game/ -> not run, no Unity flag passed, nothing under game/ or app/ touched.
- TCP 127.0.0.1:8787 is held by PID 14036 (`python scripts\dashboard.py`, the user's Momentum Dashboard). Never touched; still alive with the same command line at the end.
- Repo: `core.autocrlf=true`, working-tree files are CRLF. Edits kept CRLF (CRLF count == line count in all 3 edited files).

## Install (all into `H:\Chenta\phantomhand\.venv`; global Python never touched)
```
C:\Users\DELL\AppData\Local\Programs\Python\Python312\python.exe -m venv H:\Chenta\phantomhand\.venv
.venv\Scripts\python.exe -m pip install --upgrade pip            # venv only, as contracts.yml does -> pip 26.2.1
.venv\Scripts\python.exe -m pip install -r analytics/requirements.txt       # full analytics lock
.venv\Scripts\python.exe -m pip install -r contracts/requirements.txt       # jsonschema 4.26.0 -> 4.23.0
.venv\Scripts\python.exe -m pip install -r sim/haptic/requirements.txt      # referencing 0.37.0 -> 0.35.1, pytest 9.1.1 -> 8.3.1
.venv\Scripts\python.exe -m pip install -r sim/live/requirements.txt        # aiohttp 3.10.5 (+deps), pytest-asyncio 0.24.0
.venv\Scripts\python.exe -m pip install -e "analytics[dev]"                 # analytics/README.md Setup -> opus-analytics 0.1.0 (editable)
.venv\Scripts\python.exe -m pip install -e sim                              # analytics/README.md Setup -> synthetic-patients 0.1.0 (editable)
.venv\Scripts\python.exe -m pip check                                       # "No broken requirements found."
```
- `tools/demo` has no requirements file; it needs aiohttp, numpy, jsonschema and opus_analytics, all provided by the lines above. Pillow is NOT installed (only the optional `PilCanvas` backend in live_plot.py imports it; no test needed it).
- **Pin conflict (real output):** `pip install --dry-run -r analytics/... -r contracts/... -r sim/haptic/... -r sim/live/...` ->
  `ERROR: Cannot install jsonschema==4.23.0 and jsonschema==4.26.0 ... ResolutionImpossible`. Same story for referencing (0.35.1 vs 0.37.0) and pytest (8.3.1 vs 9.1.1).
  pytest-asyncio 0.24.0 declares `pytest<9,>=8.2` (checked via importlib.metadata), so it cannot sit next to analytics' pytest 9.1.1 at all.
  Resolution used: install sequentially, so the contracts/sim pins win for those three packages. Consequence: the analytics suite ran on pytest 8.3.1 / jsonschema 4.23.0 / referencing 0.35.1 instead of its own freeze (9.1.1 / 4.26.0 / 0.37.0). Result was the expected 83 passed.
- Final venv (`pip freeze`, 32 entries): aiohappyeyeballs 2.7.1, aiohttp 3.10.5, aiosignal 1.4.0, attrs 26.1.0, colorama 0.4.6, frozenlist 1.8.0, hypothesis 6.168.0, idna 3.20, iniconfig 2.3.0, jsonschema 4.23.0, jsonschema-specifications 2025.9.1, multidict 6.9.1, numpy 2.5.3, opus_analytics 0.1.0 (-e analytics), packaging 26.3, pandas 3.0.5, pluggy 1.6.0, propcache 0.5.4, Pygments 2.21.0, pytest 8.3.1, pytest-asyncio 0.24.0, python-dateutil 2.9.0.post0, referencing 0.35.1, rpds-py 2026.6.3, scipy 1.18.1, six 1.17.0, sortedcontainers 2.4.0, synthetic_patients 0.1.0 (-e sim), typing_extensions 4.16.0, tzdata 2026.4, yarl 1.25.1.

## Results: suite | expected | got
Canonical commands came from contracts.yml (CI) and each folder's README. Interpreter = `.venv\Scripts\python.exe`.

| Suite (cwd) | Expected | Got (first run, before any edit) | Got (after the 3-file fix) | Status |
|---|---|---|---|---|
| `contracts\validate.py` (cwd contracts, as CI) | "All validations passed" | `[PASS] All validations passed`, 105 `[PASS]` / 0 `[FAIL]`, exit 0; CI "required fixture files" check replicated: 0 missing | unchanged | PASS |
| analytics `-m pytest -q` (cwd analytics) | 83 passed | 83 passed in 323.31s (0:05:23) | not affected | PASS |
| sim/haptic `-m pytest -q` (cwd sim\haptic) | 26 passed | 26 passed in 3.45s | not affected | PASS |
| sim/sleeve `-m pytest ../sleeve -q` (cwd sim\haptic) | 38 passed | 38 passed in 9.04s | not affected | PASS |
| sim/live `-m pytest -q` (cwd sim\live) | 45 passed | 45 passed in 57.03s (26 protocol + 16 phantom_replay + 3 fake_headset_generate) | not affected | PASS |
| sim/live `-m pytest e2e.py -v` (extra: not collected by default) | n/a | 2 failed, 1 passed in 13.72s | 3 passed in 17.09s | PASS after fix |
| tools/demo/tests `-m pytest tools/demo/tests -q -rs` (cwd repo root) | 114 passed | 6 failed, 100 passed, 8 skipped in 66.92s | **106 passed, 8 skipped in 113.87s** (106+8 = 114 collected) | PASS, 8 ENV skips |
| L3 smoke `run_pipeline.py --game phantom_hand --sim --no-unity` | 7/7 | not run before the fix (it would have hit the same missing analytics python) | **7/7 checks passed -> G2 L3 GREEN**, exit 0, 119 s | PASS |
| L3 `... --faults` (extra) | n/a | n/a | fault A 6/6 + 5/5, fault B 7/7 + 3/3, fault C 7/7 + 4/4, "PHANTOM HAND L3 GREEN", exit 0, 273 s | PASS |
| default Orchard `run_pipeline.py` (extra; exercises the run_pipeline.py edit) | n/a | n/a | 26/26 checks, `PIPELINE OK`, exit 0, 43.8 s | PASS |

Final summary lines, verbatim: `83 passed in 323.31s (0:05:23)` | `26 passed in 3.45s` | `38 passed in 9.04s` | `45 passed in 57.03s` | `3 passed in 17.09s` | `106 passed, 8 skipped in 113.87s (0:01:53)`.

L3 smoke result table (verbatim from the run; hub 127.0.0.1:54335, twin A 39790 / discovery 39791 / B 39792 / control 39793):
```
PASS  Phases in order, both conditions                100 %                           15 phases, 2 conditions (async/sync), 100 %
PASS  Stroke cues acked by the twin                   >= 98 %                         100.0 % (16/16 delivered per the session events; 16 executed / 0 rejected in the twin log)
PASS  SYNC visual-to-send timing error                mean <= 20 ms, p95 <= 40 ms     mean 2.0, p95 2.0 ms (n=6; recomputed from stamps mean 2.3 ms)
PASS  ASYNC delay                                     500-700 ms on >= 95 % of strokes  100 % of 4 strokes in range (min 562, max 563 ms); sync delays max 38 ms
PASS  Live status RTT to hub                          p50 < 250 ms; invalid 0         RTT p50 12.3 ms, p95 24.8 ms (n=74); 0 invalid messages; 148 statuses with game_state
PASS  Session valid and uploaded to the hub           yes                             validate.py --session exit 0; 20/20 files uploaded 200/201; 20/20 on the hub's disk; hub got 73/73 trial_events exactly once
PASS  Analytics writes embodiment with quality flags  yes                             embodiment sync+async+sync_minus_async; flags: missing 4, ok 28
7/7 checks passed -> G2 L3 GREEN
```
`--no-unity` caveat printed by the tool itself: the SYNC-timing and ASYNC-delay rows check the table logic on the fixture's recorded stroke stamps; acks, RTT, statuses, sensor files, uploads and analytics are live.

## Failures and classification
1. **PORTABILITY, fixed.** `tools/demo/tests/test_fixture_freshness.py::test_stored_metrics_equal_current_analytics[ph_l3_main]`, `[ph_l3_fault_node_a_off]`, `[ph_l3_fault_node_b_absent]`, `[ph_l3_fault_hub_absent]`, `::test_staleness_guard_detects_a_stale_metrics_file`, and `tools/demo/tests/test_phantom_pipeline.py::test_a_truncated_run_goes_through_the_whole_harness_and_is_not_green`.
   Decisive line: `FileNotFoundError: [WinError 2] The system cannot find the file specified` raised by `subprocess.run([str(PP.ANALYTICS_PY), ...])` (`test_fixture_freshness.py:31 _analyse`; `phantom_pipeline.py:213 run_validate`, called from `one_run` at :354). Line numbers are as of the failing run, i.e. before the 2-line edit.
2. **PORTABILITY, fixed.** `sim/live/e2e.py::test_e2e_basic` and `::test_e2e_with_drop` (`test_e2e_discovery` passed).
   Decisive line: `[FAIL] analytics venv python found at H:\Chenta\phantomhand\analytics\.venv\Scripts\python.exe (exists=False)` -> `AssertionError: 1 check(s) failed` (e2e.py:386 / :392).
   Root cause of 1 and 2: three modules hardcode `ANALYTICS_PY = analytics/.venv/Scripts/python.exe`, the per-folder-venv layout of the old PC. This machine uses one repo-root venv, so that file does not exist.
3. **ENV (data not in the clone), not fixed.** 8 SKIPPED in `tools/demo/tests/test_run_pipeline.py` (lines 50, 62, 67, 83, 93, 103, 113, 130 = every test using the `session` fixture). Reason printed: `recorded session not present: H:\Chenta\phantomhand\app\.hub_data\c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7`. `git ls-files app/.hub_data` lists 29 files and none is under that id, and `git log --all -- app/.hub_data/c93ae4fa-...` is empty, so the recording was never committed. Fixing it would mean touching app/ or the test, both out of scope.
   Supplementary evidence (scratch wrapper outside the repo, REAL_SESSION monkeypatched to `contracts/fixtures/sessions/healthy`): all 10 tests of that file pass (`10 passed in 2.20s`). Not counted in the table; it only shows the logic is fine and the skips are data-only.
No REAL DEFECT (logic) found. No Windows encoding problem surfaced (stdout was a pipe, i.e. cp1252, in every run).

## Files changed (6 added lines, 3 files; nothing else edited)
```
sim/live/e2e.py          @@ -43,0 +44,2 @@
+if not ANALYTICS_PY.exists():  # single-venv layout (repo-root .venv): the running interpreter has opus_analytics too
+    ANALYTICS_PY = Path(sys.executable)
tools/demo/phantom_pipeline.py   @@ -55,0 +56,2 @@   (same two lines)
tools/demo/run_pipeline.py       @@ -81,0 +82,2 @@   (same two lines)
```
Each goes directly under the existing `ANALYTICS_PY = ... ".venv" / "Scripts" / "python.exe"` line, so the old value is still used whenever it exists (old PC behaviour unchanged) and the interpreter running the harness is the fallback. `Path` and `sys` were already imported in all three. I chose the interpreter fallback rather than a new env var because the harness is always started with the interpreter that has every package installed.
Verified by re-runs: e2e.py 3 passed; tools/demo/tests 106 passed / 8 skipped; phantom smoke 7/7; `--faults` all green; default pipeline 26/26 (this one is the only run that exercises the run_pipeline.py copy).

Gitignored artifacts created by the runs: `.venv/`, `analytics/.hypothesis/`, `analytics/.pytest_cache/`, `analytics/opus_analytics.egg-info/`, `sim/.pytest_cache/`, `sim/synthetic_patients.egg-info/`, `__pycache__/`, repo-root `.pytest_cache/`. Pipeline outputs (twin log, hub sessions, l3 reports) went to a temp scratch dir via `--out`, not into the repo.

## Port and process hygiene
- Pipeline ports: twin from `--port-offset` (default 31000): Node A 39790, discovery 39791, Node B 39792, control 39793; fake hub on an OS-assigned ephemeral TCP port (54335 in the smoke run). The strings 8787/8788/8797 appear 0 times in the pipeline output. No flag was needed to avoid 8787; the defaults already keep clear of it. Unit tests use their own offsets (sleeve 10000+/11000+, live phantom_replay 12100+, tools/demo phantom_pipeline 14000+) or ephemeral ports.
- After all runs (Get-NetTCPConnection / Get-NetUDPEndpoint): 8790, 8791, 8792, 8793, 8797, 8788 and 39790-39793 are free; the only listener on the watched ports is 127.0.0.1:8787 owned by PID 14036 (unchanged). No python process referencing the repo is left running.

## Found but not exercised, not changed (hardcoded paths)
- `tools/demo/prove_live_to_phone.py:46` `ADB_DEFAULT = r"C:\Users\GARV BANSAL\AppData\Local\Android\sdk\platform-tools\adb.exe"` (previous user's home; phone-only tool). Candidate fix on approval: `shutil.which("adb") or <old value>`.
- `tools/demo/phantom_pipeline.py:56` `DART = Path("C:/flutter/bin/dart.bat")` (line 132 already falls back to `dart` on PATH; only used with `--hub flutter`) and `:57` `UNITY_EXE` (only used without `--no-unity`).
- `tools/demo/start_pc_demo.ps1:59` `$DartExe = 'C:\flutter\bin\dart.bat'`.
- Docs/docstrings only (left alone by rule): `tools/demo/START_DEMO.md`, the `run_pipeline.py` usage docstring and several test docstrings still show `C:\Users\GARV BANSAL\...\OPUS`, `sim\live\.venv` and `analytics\.venv` examples; the FATAL hint texts in run_pipeline.py still say "sim/live venv".

## Open issues
1. requirements files cannot all be satisfied in one env (jsonschema / referencing / pytest, see Install). Needs a decision: a root-level requirements file, or relaxing one side's pins.
2. The recording `app/.hub_data/c93ae4fa-3a21-4c7b-9bb6-cbe77c98e0b7` is missing, so 8 tests skip on any fresh clone. Needs a decision: commit it, or point the fixture at an in-repo session.
3. Every pytest run prints a pytest-asyncio `PytestDeprecationWarning` (`asyncio_default_fixture_loop_scope` unset). Harmless; it now also appears for the analytics suite because pytest-asyncio is installed in the shared venv.
4. `git status` at the end also shows changes that are not mine: `M game/ProjectSettings/ProjectSettings.asset` and `?? tools/unity_mcp.py` (both already present when I started), plus `M logs/sessions/screens/unity/**.png` (10 files), `?? .claude/worktrees/`, `?? logs/sessions/2026-10-08-PH-O-RESUME-run1.md`, `?? logs/sessions/unity_logs/run12_full_pipeline/...` that appeared during the session. I did not touch game/, app/ or those paths.
5. Not run (needs Flutter): app/ tests and the hub on 8797. Not run (Unity editor open): the PlayMode `PH_FullRun` path (`run_pipeline.py` without `--no-unity`).

## Next step
Opus: review the 6-line diff and decide on commit (agents do not commit); decide open issues 1 and 2; optionally approve the `adb`/`dart` PATH-lookup fixes. Any agent can now run the commands below without further setup.

## Commands for the next agent (all verified this session)
Activation: PowerShell `& H:\Chenta\phantomhand\.venv\Scripts\Activate.ps1` (worked; machine policy is Bypass, so prefer the explicit interpreter path if a policy blocks it); Git Bash `source /h/Chenta/phantomhand/.venv/Scripts/activate` (worked). Or skip activation and call `H:\Chenta\phantomhand\.venv\Scripts\python.exe` directly (P below).
```
cd H:\Chenta\phantomhand\contracts  ; P validate.py                          # All validations passed
cd H:\Chenta\phantomhand\analytics  ; P -m pytest -q                         # 83 passed, ~5.5 min
cd H:\Chenta\phantomhand\sim\haptic ; P -m pytest -q                         # 26 passed
cd H:\Chenta\phantomhand\sim\haptic ; P -m pytest ../sleeve -q               # 38 passed
cd H:\Chenta\phantomhand\sim\live   ; P -m pytest -q                         # 45 passed
cd H:\Chenta\phantomhand\sim\live   ; P -m pytest e2e.py -v                  # 3 passed (not collected by default)
cd H:\Chenta\phantomhand            ; P -m pytest tools/demo/tests -q -rs    # 106 passed, 8 skipped (missing recording)
cd H:\Chenta\phantomhand            ; P tools/demo/run_pipeline.py --game phantom_hand --sim --no-unity --out <dir>            # 7/7, ~2 min
cd H:\Chenta\phantomhand            ; P tools/demo/run_pipeline.py --game phantom_hand --sim --no-unity --faults --out <dir>   # ~4.5 min
cd H:\Chenta\phantomhand            ; P tools/demo/run_pipeline.py                                                              # Orchard, 26/26, ~45 s
```
Run suites one at a time (they bind sockets); the pipeline binds only offset/ephemeral ports, never 8787.
