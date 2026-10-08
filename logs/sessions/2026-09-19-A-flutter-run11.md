# Track A — Flutter app, run 11 (2026-09-19)

Resume point for the next A agent if this run is interrupted. Previous: `2026-09-19-A-flutter-run10.md`.
New brief for this run (from the user/session prompt, not previously in CONTEXT.md): rebuild the app UI to
match the new Stitch design set (`docs/design/stitch/stitch_opus_neurorehabilitation_workspace/`) across all
screens and wire every screen to real hub data, in 3 iterations (solo -> UDP/WS mock headset -> real Unity).

## CHECKPOINT 0 — read-in + baseline verification (this run only got this far)

Read: CLAUDE.md, CONTEXT.md ★ START HERE, docs/APP_DESIGN.md, contracts/LIVE_PROTOCOL.md, the 4 Stitch
screen briefs' `screen.png` (looked at `live_session_monitor/screen.png` directly with the Read tool) and
`opus_vr_neurorehabilitation/DESIGN.md` (the design-system doc — dark slate-indigo canvas `#161E2F`/`#0b1324`
family, soft peach coral `#FFA586` primary, crimson `#B51A2B` for alerts/stop, Inter type, 8/16/24px radii).
Also `docs/design/stitch/.../kinetic_clinical_vr/DESIGN.md` exists as a second, apparently older/alternate
design-system doc in the same folder — not yet reconciled with `opus_vr_neurorehabilitation/DESIGN.md`; the
next agent should check whether the 4 screen HTMLs (`code.html`) were built against one or the other before
picking a token source.

Confirmed device state: `adb devices` — `b83663ef device` (CPH2381), connected and authorized.
Flutter: 3.47.4 stable / Dart 3.13.3, matches CLAUDE.md.

### Baseline `flutter analyze` (real output, this run)
```
123 issues found (ran in 101.7s)
```
All 123 are `info`-level lints (implementation_imports/depend_on_referenced_packages noise from riverpod's
internal export paths, cascade/dynamic-call/deprecated-onReorder style lints, pubspec sort order) — **grepped
explicitly for `error -` in the output: 0 matches.** So the app currently compiles clean; run10's "0 err /
0 warn" claim for *errors* holds, but run10 said "0 warnings" and this run counted 123 info lints existing
already (not new — nothing was edited this run). Flagging the discrepancy rather than re-asserting run10's
"0 warnings" without having actually diffed it against a clean baseline count.

One thing worth a second look that turned out to be a false alarm, logged so nobody re-investigates it: in
`app/lib/core/theme/opus_tokens.dart` line 19, `OpusTokens`'s const constructor is declared as
`const new({...})`. This looks like a typo for the class name but is valid modern Dart — `ClassName.new` is
the explicit name for the unnamed constructor, and a bare `new(...)` inside the class body IS that same
declaration. `flutter analyze` has 0 errors and doesn't flag it. Confirmed real, not a corruption.

### Baseline `flutter test` — completed after this checkpoint was first drafted; real output below

The backgrounded run finished with **2 FAILURES**, not run10's claimed "157 passed, 0 failed" — the suite has
grown to 158 cases since run10 and two of them are broken:

```
158 total, +156 -2 => 2 failing, 156 passing

FAIL 1: test/hub/directory_session_repository_test.dart
  "SessionReportScreen renders a real hub-received session opened via DirectorySessionsRepository"
  TimeoutException after 0:10:00.000000: Test timed out after 10 minutes.
  (stack: dart:isolate _RawReceivePort._handleMessage)

FAIL 2: test/hub/hub_connection_test.dart
  "HubConnection protocol engine (in-process fake socket, no real I/O) requires_ack message is retried
   after ackTimeout if unacked"
```

Both failures are hang/timeout-shaped (one explicitly timed out at 10 minutes; the other is in the same
family — an ack-retry timer test — and finished only because the whole run was capped). This looks like a
new regression or flake introduced after run10 (run10's own count was 157, all passing, on a clean tree) —
**not something this run caused** (no files in `app/` were edited this run before or during the test). This
is exactly the kind of "green run can be luck / a hung timer-dependent test" issue CONTEXT.md's rules section
warns about for Unity, and the same risk applies here: both failures depend on timers/async completion
(`ackTimeout` retry, a fake-socket round trip), which is fragile under real wall-clock waits.

**The next agent must NOT start the Stitch rebuild until this is triaged**: rerun each failing test in
isolation (`flutter test test/hub/hub_connection_test.dart -n "requires_ack message is retried"` and the
`directory_session_repository_test.dart` one), see if they fail again or were a one-off flake, and if they
fail consistently, treat it as a real regression to fix (or file it explicitly as a known-broken baseline in
this log before touching anything else) rather than silently carrying it forward.

### Existing app screens inventory (`app/lib/features/`)
auth (login), devices, live (live monitor), outcomes, patients (list + profile), programs (builder), progress,
sessions (report), settings — i.e. today's nav does not yet match the Stitch bottom-nav set
(Patients / Monitor / Programs / Reports) 1:1; `progress`, `outcomes`, `settings`, `devices` are separate
routes today with no obvious Stitch screen counterpart in the 4 delivered screens (`live_session_monitor`,
`patient_overview`, `program_builder`, `session_report`). The next agent needs to decide (or ask Opus) whether
those extra screens fold into the 4-tab design or stay as secondary routes reached from within a tab.

## What this run did NOT get to (honest gap — do not mark iteration 1 done)

This is a multi-screen visual rebuild + full real-data wiring task that this run only scoped, it did not
implement. Nothing in `app/` was edited this run (`git status` on `app/` is unchanged from run10's end state
aside from the untracked `.hub_data/` scratch dirs already present before this run started). Iteration 1
("SOLO: analyze 0/0, tests green with updated goldens, build APK, walk every screen on device") has NOT been
executed. In particular, none of the following happened yet:
- No screen was rebuilt against the Stitch `code.html`/`screen.png` pairs.
- No golden was regenerated.
- No APK was built or installed this run (`b83663ef` was confirmed reachable but nothing was pushed to it).
- Live monitor / patient overview / program builder / session report real-data wiring was not audited against
  the new brief's exact field list (trial X of N, RT/peak speed/smoothness/trunk lean cards, haptic status,
  latency, per-trial table, speed profile, haptic cues) — only visually spot-checked one screenshot
  (`live_session_monitor/screen.png`) against the DESIGN.md token doc.

## Suggested next steps for whoever resumes run 11 (or opens run 12)

1. Finish the backgrounded `flutter test` run, paste the real pass/fail count, and decide the golden-update
   strategy before any screen rebuild (since goldens will change on every visual edit — better to do the
   whole visual pass, then regenerate all goldens once).
2. Read all 4 `code.html` files (not just the pngs) — they're the more precise spec (exact spacing/colors/
   copy) than the screenshots.
3. Reconcile `kinetic_clinical_vr/DESIGN.md` vs `opus_vr_neurorehabilitation/DESIGN.md` — figure out which
   one the 4 screen HTMLs actually use (diff a hex value from a screen's inline styles against both docs).
4. Decide the OpusTokens mapping: today's `OpusTokens` (mist/paper/ink/slate/rule/lake/ochre/leaf/alert) is a
   *semantic* palette with strict role rules (see the doc comment in `opus_tokens.dart`); the Stitch doc's
   palette is a literal Material-style token set. Map Stitch's literal colors onto OPUS's semantic roles
   (e.g. Stitch primary `#FFA586` -> which existing role?) rather than replacing the semantic system, since
   the existing side-color rule (`lake`=left, `ochre`=right, never reassigned) is load-bearing elsewhere.
5. Only then start the per-screen rebuild, screen by screen, verifying against a live device screenshot after
   each one (not just golden tests) — per the brief's explicit "test ON THE DEVICE" instruction.
6. Iteration 2 (mock headset via `sim/live/fake_headset.py`) and iteration 3 (real Unity) come after iteration
   1 is actually done and checkpointed.

Nothing here should be read as iteration 1 being complete — it is not. This checkpoint exists so a fresh
agent does not repeat the same read-in and baseline check from scratch.
