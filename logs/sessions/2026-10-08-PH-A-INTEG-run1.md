# PH A INTEG run1 - merge app track 1 (live card) + track 2 (witness mirror, embodiment report) into the main tree

Date: 2026-10-08. Main tree: `H:\Chenta\phantomhand` (branch `main`, HEAD a667defa). Agent: Sonnet 5.5 (integrator, no subagents, no commit).
Inputs: CARD worktree `.claude\worktrees\agent-aef41b62c9af0001f` (log `2026-10-08-PH-A-CARD-run1.md`), MIRROR worktree `.claude\worktrees\agent-ac81c9c63580c6ea6` (log `2026-10-08-PH-A-MIRROR-run1.md`, section "Integration lines owed by Opus").
Both worktrees branch from ab533e22; `git diff --stat ab533e22 HEAD -- app` is empty, so `app/` in main equals their base.

## Plan (step -> verify)
Constraint: correctness of what the audience screen and the report show, and not undoing track 1 (its files and the hooks overlap in 4 files). Cost is secondary.
Failure modes: (1) patch does not apply because of CRLF (autocrlf=true) -> `git apply --check` first; (2) a hook silently does nothing -> one test per hook, then a mutation check (remove the hook, the test must fail); (3) the manifest fixture change (motor_soa_ms default 833 ...) breaks form/builder goldens -> regenerate only the files that fail, look at them; (4) port 8797 busy -> report, do not kill; (5) the mirror header sits under the audience screen's exit button -> measure it in a test, not by eye.
Ledger: no delegation. Every step is a few tool calls on files already in context; nothing touches money/compliance.
1 baseline -> 2 track 1 patch -> 3 track 2 copy -> 4 logs -> 5 four hooks + mock -> 6 four tests -> 7 analyze -> 8 full test -> 9 goldens -> 10 final analyze + test.

## Step 1 - baseline (real output)
`git status --short -- app` in main: ` M app/assets/fixtures/manifests/phantom_hand.manifest.json` and nothing else. As expected.
sha256 of that manifest before any of my work: `788a9ff8b590ebe893b024d0bc3ab5609636ca913157a095aa91419b9935dc1d` (must be unchanged at the end).

## Step 2 - track 1 patch (real output)
In the CARD worktree: `git add -N -- app` (scoped to app/, not `.`: same diff, fewer side effects on the worktree index), `git diff --binary -- app > card.patch` (2.4 MB, 44 files: 40 modified + 4 new). Scratch: `scratchpad/integ/card.patch`.
Main: `git apply --check` exit 0, `git apply` exit 0.
Check: sorted path lists of `git status --short -uall -- app`: worktree 44 paths, main 44 paths (manifest excluded), identical; `git hash-object` equal for all 44 (so CRLF/LF normalised content is identical). Manifest sha256 unchanged after apply.

## Step 3 - track 2 copy (real output)
29 untracked files under app/ in the MIRROR worktree = 4 lib + 7 test .dart + 18 PNG (the brief's "30" counts the MIRROR log, which is step 4). None existed in main. Copied with `cp -p`, sha256 equal for all 29. Main now has 33 untracked paths under app/ (4 track 1 + 29 track 2).

## Step 4 - logs (real output)
`2026-10-08-PH-A-CARD-run1.md` and `2026-10-08-PH-A-MIRROR-run1.md` copied to `logs/sessions/`, sha256 equal to the originals.

## Step 5 - the four hooks + mock (files and lines after the edits)
Read the merged code first; track 1's changes are intact (all hooks are additive; `git diff` of each file still contains track 1's hunks).
- (a) `app/lib/data/models/phantom_live.dart`: import l.3; `PhantomLiveSnapshot` ctor arg l.263 + field l.283 (`witness`, per snapshot like `markers`); `PhantomLiveModel.witness` l.432; reset `witness = null;` l.450 inside the `wasRunOrOver && waitingAgain` block; keep `if (s.witness != null) witness = s.witness;` l.453 (after the reset, so a summary arriving in the same snapshot survives).
- (b) `app/lib/data/repositories/hub/hub_phantom_live_repository.dart`: import l.6; `_onEvent` l.68-72 parses `PhantomWitness.tryParseEvent(payload)` first and emits it; `_emit(... witness)` l.77/86. A summary does not move `receivedAt` (events never do).
- (c) `app/lib/features/live/phantom_live_screen.dart`: import l.9; `PhantomLiveView.build` l.322-327, directly before `if (observer) {`.
- (d) `app/lib/features/sessions/session_report_screen.dart` (NOT in either builder's set): imports l.1-2, 8, 12; branch l.126-134 after the `metrics == null` block; `_embodimentProvider` l.419 (raw `metrics.json`: hub session dir, else a user-opened session folder, else null; `hubCapable` guard; `on Exception` -> null).
- mock: `app/lib/data/repositories/mock/mock_phantom_live_repository.dart`: const `mockPhantomWitness` l.16 (fixture numbers + flinch latency/strength, no q5 because the demo run has no agency phase), attached to every snapshot of the witness phase l.231 (same const object, so the mirror's closing fade is not restarted by each 200 ms tick).
Decisions:
1. Hook (c) is wrapped in `Padding(top: 48)`. Measured with a throwaway probe test (deleted): at 1280x800 the audience screen's exit button (1228..1276 x 4..52) overlapped the mirror's "Preliminary" chip (1072.5..1234.3 x 21.1..65.2) by 6 px; on a phone it only escaped because the chip wrapped to line 2. After the band: chip 1046.6..1198.6 x 67.8..109.3, no overlap at 1280x800, 1280x800 + agency row, 390x844, 1024x768 (probe PNGs looked at). Cost: mirror scale 0.96 -> about 0.90 (0.87 -> about 0.82 with the agency row). Same 48 dp the phone layout of the traces already uses.
2. Hook (d) also looks in the user-opened session folders (`openedSessionDirectoriesProvider`), not only `sessionDirFor`, like the existing `metrics == null` branch four lines above it does. Otherwise a PH session opened with "Open a session folder..." (how the fixture is viewed) would never get its report.
3. First analyze after the hooks was 240 infos: 6 `prefer_int_literals` in my mock const (2.0 -> 2 ...) and 1 `avoid_catches_without_on_clauses` (`catch (_)` -> `on Exception catch (_)`); fixed, back to 233.
4. Known, not fixed: the report branch uses `.value` of an async provider, so for a PH session the old "Trials 0 / Success 0 %" layout can show for 1-3 frames before the report replaces it. A spinner while loading would add a frame to every Orchard report test; not worth it.

## Step 6 - tests (`app/test/phantom/witness_embodiment_hooks_test.dart`, 6 tests)
(a) model keeps/clears the summary [plain `test`, it is a pure model]; (b) real `HubConnection` over `FakeLiveSocket` fed the real `witness_summary` line of `contracts/fixtures/sessions/phantom_hand_min/events.ndjson` (read via Track 2's `fixtureWitnessEvent()`, read-only) [plain `test`]; (c) audience view shows the mirror only in the witness phase, numbers from the event, exit button clear of the header; (c2) operator card never shows it; (d) `SessionReportScreen` for the fixture folder opened through the real `openedSessionDirectoriesProvider` shows `EmbodimentReport` with the R3 verdict "(5.5 vs 2.5)", not the trial layout; (e) the demo run (`MockPhantomLiveRepository`, 10 x phase_next) reaches the witness phase and the mirror shows numbers and no "No data", and the closing fade is complete after 4 s.
Run: `flutter test --no-pub test/phantom/witness_embodiment_hooks_test.dart` -> `00:09 +6: All tests passed!`.
Mutation check (one single-line change at a time, bytes restored and sha256 verified after each; script `scratchpad/integ/mutate.ps1`):
- M1 remove `if (s.witness != null) witness = s.witness;` -> FAILED (a), (c), (e)
- M2 remove `witness = null;` -> FAILED (a)
- M3 skip the witness branch in the hub `_onEvent` -> FAILED (b)
- M4 mirror condition never true -> FAILED (c), (e)
- M5 remove the 48 dp band -> FAILED (c) (geometry)
- M6 skip the embodiment branch -> FAILED (d)
- M7 mock emits no witness -> FAILED (e)
All seven restored-ok.

## Step 7 - analyze (real output)
`flutter analyze --no-pub` -> `233 issues found. (ran in 4.7s)`: 0 error, 0 warning, 233 info (baseline 236, track 1 ended at 233). Nothing in the hook files, in track 2's new files or in my test.

## Step 8 - full test (real output)
Port 8797 checked free before the run (`Get-NetTCPConnection -LocalPort 8797` empty, no flutter/dart process running).
`flutter test --no-pub --concurrency=2 --reporter expanded` -> `00:50 +455: All tests passed!` (exit 0, 55 s wall). 455 = 303 baseline + 49 track 1 + 97 track 2 + 6 mine. 0 failures, 0 skips.
Note: the first `flutter analyze` and `flutter test` ran Flutter's implicit `pub get` ("Resolving dependencies... Got dependencies!", resolution only: `pubspec.lock` and `.dart_tool/package_config.json` unchanged, no `+ package` lines). All later runs used `--no-pub`. `git status` shows no stray generated file.

## Step 9 - goldens: nothing failed, nothing regenerated
The manifest fixture change broke no golden. Why (checked, not assumed): the only test that reads `phantom_hand.manifest.json` is `test/phantom/phantom_manifest_form_test.dart` (a widget test with `find.*` asserts, no golden; passes); none of the 117 golden PNGs (72 screens, 39 phantom, 6 reach trace) renders the Phantom Hand manifest (`program_builder` goldens use patient `synthetic-healthy-42`). So no `--update-goldens` was run and no PNG was regenerated. Track 1's 21 changed PNGs and track 2's 18 new PNGs were compared by the full run (no golden flag) and all matched in the merged tree.
Extra visual check of the integration: rendered the real audience screen in the witness phase (4 sizes) and the real `SessionReportScreen` of the PH fixture (phone, wide, Hindi) to PNGs with throwaway probes (deleted), looked at them: no overflow, no clipping.

## Step 10 - final check, run last (real output, nothing edited after it except this log)
Port 8797 free before the run.
- `flutter analyze --no-pub` -> `233 issues found. (ran in 2.9s)` (0 error, 0 warning, 233 info; exit 1 only because infos are fatal by default)
- `flutter test --no-pub --concurrency=2 --reporter expanded` -> `00:58 +455: All tests passed!` (exit 0, 63 s wall, 0 failure markers)
- manifest fixture sha256 unchanged (`788a9ff8...935dc1d`); `git status` for `pubspec.yaml`, `pubspec.lock` and the platform folders is empty; no probe/temp file left under `app/`.
- `git status --short -uall -- app`: 76 entries = track 1's 44 + the manifest + track 2's 29 + `lib/features/sessions/session_report_screen.dart` (hook d) + `test/phantom/witness_embodiment_hooks_test.dart`.
Files changed that are in neither builder's set: `app/lib/features/sessions/session_report_screen.dart`, `app/test/phantom/witness_embodiment_hooks_test.dart`, this log. (The manifest was the pre-existing change, untouched.)
Side effect outside the main tree: `git add -N -- app` in the CARD worktree (intent-to-add on its 4 new files; its content is unchanged). No git commit/tag/push/stash/checkout/reset anywhere.

## Cross-check done by reading (not by running): Unity emitter vs app parser
`game/Assets/Games/PhantomHand/Runtime/WitnessSummary.cs` (someone else's uncommitted work) writes `drift_change_cm`, `flinch_latency_ms`, `flinch_strength`, `flinch_emg_peak_x`, `flinch_wrist_peak_mps`, `ownership`, `control`, `witness_q4` (and `q4` for the screen row), `agency_q5` (only when answered), `condition_order`, `sync_minus_async`, `closing_en/hi`, and shifts the on-screen -3..+3 by +4 onto the wire scale 1..7 (`Likert7`). Same names and scale as `phantom_witness.dart` (`phantomAnswerMid = 4`). Not proven with a real Quest event.

## NOT VERIFIED / OPEN
1. Nothing ran on a phone or a Quest: the audience mirror and the report were only rendered in the Flutter test harness; the real `witness_summary` from a Quest build is unseen (only the contract fixture and the Unity source were read).
2. Hindi: all new Hindi (mirror closing lines, report verdicts, reasons) is machine quality (see the MIRROR log); the test font has no Devanagari, so glyph rendering is unchecked.
3. A card (re)opened after the witness event shows "No data" in the mirror: the repository replays only `lastStatus`, not events, and the model lives in the screen State (leaving the Monitor tab during the witness phase and coming back loses it). Same as the threat/burst markers today. Not changed.
4. Report: 1-3 frames of the old trial layout before the embodiment report replaces it (see decision 4).
5. Fixture inconsistency (track 2 found it; contracts/ is not mine): `contracts/fixtures/sessions/phantom_hand_min/metrics.json` says drift 2.4 / 0.4, its own `events.ndjson` witness_summary says 2.0 / 0.0, so the report and the mirror disagree for the same fixture session (seen in my report render). `contracts/fixtures/valid/event.11.json` still has the old -2.4 / -0.4.
6. The 6 new tests pin the hooks; the demo sample numbers (`mockPhantomWitness`) are scripted demo values, shown on the screen that says "Demo, no headset".

## Decisions to confirm
1. 48 dp top band under the audience mirror (exit-button clearance; costs about 6 % of the mirror's scale). Alternative: add an `endInset` parameter to `PhantomWitnessMirror` so only the header moves.
2. Hook (d) also finds `metrics.json` in a user-opened session folder, not only in the hub's session dir.
3. Tests (a) and (b) are plain `test()` (model and repository, no widget involved); (c), (d) are widget tests. Two extra tests beyond the four asked: (c2) the operator card never shows the mirror, (e) the demo run reaches the witness phase.
4. The mock emits its sample on every snapshot of the witness phase (one shared const object) instead of exactly one snapshot: stateless, and the model keeps it, so the screen sees the same as one event.
