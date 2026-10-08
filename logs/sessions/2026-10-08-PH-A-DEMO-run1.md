# PH-A-DEMO run 1 — "Run Phantom Hand demo" in the operator app (8 Oct 2026)

Track A (Flutter, `app/`). Builder: the app demo agent. No commit, no tag, no hub, no Unity, no phone, no adb.

## 1. What was asked

One demo patient with one complete Phantom Hand session and its report, and ONE button that plays the whole
Phantom Hand demo inside the app with no headset, sleeve or hub: live card through every phase, then the audience
results mirror, then that patient's session report. Both in mock mode and in hub mode with nothing connected.
Data = the real run recorded today against the sleeve simulator (scripted participant = SIMULATED data, labelled so).

## 2. What was built (all verified: see section 5)

Button, where it is, what one tap does:
- On the **login screen** ("Sign in", the first screen of a cold start) above the role buttons: a light button
  "Run Phantom Hand demo" (Hindi twin "फैंटम हैंड डेमो चलाएँ", by the app language) with a one-line hint.
  Tapping it signs in as the mock Clinician and opens the demo. It is also the first thing on the profile of
  **"Demo participant (simulated)"** (first row of the patient list). It is NOT on the patient list screen: that
  would have changed 12 goldens (decision of the main session).
- One tap, nothing else to do. Step by step (73 s + 12 s):
  1. Live card, the existing `PhantomLiveView` (Controls section hidden), badge "Demo, simulated data" in the app
     bar, one plain-words caption per phase and an always-visible **Skip / Stop demo** bar. The 14 phases of the
     recording in its order: calibrate, probe_pre, induction, threat, probe_post, questionnaire (delayed = ASYNC
     first), probe_pre, induction, threat, probe_post, questionnaire (SYNC), dissolve, reveal, witness. SYNC/ASYNC
     chip, Sleeve and Muscle sensor chips, the two traces with the flinch after each stone, "Stone lands" and "Muscle
     burst" markers.
  2. After the witness phase: the audience results mirror (`PhantomWitnessMirror`) fed with the recording's own
     `witness_summary` (wire 1..7, shown -3..+3), 12 s, closing lines fade in.
  3. The demo participant's session report opens in place of the demo (Back returns to where the demo started),
     with the embodiment report from the recording's `metrics.json` and a plain "Simulated run" label.
- Skip: live -> mirror -> report. Stop demo: back to where it started (patient list when the demo was the first
  screen). Running it twice in a row works (each visit builds a new engine; nothing is kept).

Files added (app/):
- `lib/data/models/phantom_demo.dart` pure Dart: `PhantomRecordedTrace`, `PhantomDemoScript.tryBuild` (events.ndjson +
  live_trace.json -> the compressed timeline), ids, the per-phase seconds table (`phantomDemoSeconds`).
- `lib/data/repositories/mock/phantom_demo_repository.dart`: `PhantomDemoAssets` (reads the bundle) and
  `PhantomDemoSessionsRepository` (serves the demo session; sits in the composite repository, so it works with or
  without a hub).
- `lib/features/live/phantom_demo_screen.dart`, `phantom_demo_button.dart`.
- `lib/shared/clinical/phantom_demo_strings.dart`: EN + HI table (button, hint, badge, Skip/Stop, label, 10 captions).
- `assets/demo/phantom_hand/` (4 files, 97 271 bytes, each < 50 KB): `session.json`, `events.ndjson`, `metrics.json`,
  `live_trace.json`. Kept out of `assets/fixtures/sessions/` because `tool/sync_fixtures.dart` wipes that folder.
- `tool/phantom_demo/build_trace.py`, `install_assets.py`: how the 4 files were made (section 4).
- Tests: `test/phantom/phantom_demo_test.dart` (unit), `phantom_demo_flow_test.dart` (end to end in the real app),
  `phantom_demo_pictures_test.dart` (writes the pictures), `phantom_demo_test_support.dart`.

Files changed (app/): `lib/data/repositories/mock/mock_phantom_live_repository.dart` (engine takes an optional `demo:`
script and plays it instead of the synthetic run; without it nothing changed), `lib/features/live/phantom_live_screen.dart`
(`PhantomLiveView.showControls`, default true), `lib/features/live/live_routes.dart` (route `/demo/phantom-hand`),
`lib/features/auth/login_screen.dart` (the button; the column now scrolls), `lib/features/patients/patient_profile_screen.dart`
(the button for the demo participant; a patient whose sessions are all Phantom Hand shows no Orchard tiles/charts, and the
session row says "Simulated run" instead of "Pending"), `lib/features/sessions/session_report_screen.dart` (the bundled
demo session's embodiment, the "Simulated run" label for `mode: simulation`, no flash of the empty trial layout while
the embodiment loads), `lib/core/providers/repository_providers.dart` (the demo repository in the composite),
`assets/fixtures/patients.json` (demo participant first: id `demo-phantom-hand`, "Demo participant (simulated)", no
personal data; `age` 0 and gender "n/a" are placeholders the model requires and no screen shows), `pubspec.yaml` (asset folder).
No new packages. No generated file needed regenerating (no arb change, no new annotated provider).

## 3. Collision during the run (so the next reader is not confused)

A second run of this same task (started by a forked session) wrote into this worktree between 15:03 and 15:12: it
overwrote `phantom_demo.dart` with another design (`PhantomRunRecording`), replaced the 4 asset files at 15:04:23 and
touched the engine file (reverted by itself). The main session stopped it. This log's design is mine
(`PhantomDemoScript`); I kept the other run's assets (see section 4) and its two findings (trace = mean per 50 ms bin
like the headset; a cold start lands on /login).

## 4. Data: where every number comes from (nothing invented)

Recording: `C:\Users\DELL\AppData\LocalLow\DefaultCompany\OPUS\opus_sessions\a7c5d8c6-2e5a-40cd-a3f4-cda09ef85e92`
(session id kept; mode `simulation`; 14 phases; 114/114 cues delivered; 199 s).
- `events.ndjson`: byte for byte the recording's (sha256 compared: identical).
- `session.json`: the recording's, only `patient_ref` `synthetic-longitudinal-9000` -> `demo-phantom-hand` (checked: swap it
  back and the text equals the original), otherwise the demo session would belong to another patient.
- `metrics.json`: output of `opus_analytics` on a COPY of the recording (outside the repo):
  `H:\Chenta\phantomhand\analytics\.venv\Scripts\python.exe -m opus_analytics "<copy>" --summary`, real output:
```
metric                                  SYNC     ASYNC  SYNC-ASYNC
Drift toward virtual hand (cm)          2.99      1.00        1.99
Ownership (1-7)                         6.00      3.00        3.00
Control (1-7)                            n/a       n/a         n/a
EMG flinch peak (x baseline)            6.02      6.04       -0.02
EMG flinch latency (ms)               112.49    114.82       -2.33
IMU jolt (m/s2)                        14.29     14.09        0.20
Wrist withdrawal peak (m/s)             0.00      0.00       -0.00
Wrist latency (ms)                       n/a       n/a         n/a
q4 awareness (pointer only)             4.00       n/a         n/a
Stroke timing err sync (ms)            mean 5.64    p95 9.62
Stroke timing err async (ms)            mean n/a     p95 n/a
condition order: async, sync   (* = degraded, n/a = missing; see metrics.json for reasons)
```
  (the shipped file is the same analytics output from a second run, `computed_at` 2026-10-08T09:27:24Z; same numbers).
- `live_trace.json`: `tool/phantom_demo/build_trace.py <recording copy> <out>`: EMG envelope and |accel| from
  `sens_###.json`, 50 ms bins, **mean per bin** (what the headset sends in `status.payload.trace`, rule in
  `game/Assets/Shell/Runtime/PhantomLiveStatus.cs`), 3981 bins. Rebuilt today from the recording: sha256 identical to the
  shipped file. `install_assets.py <copy> <trace> app/assets/demo/phantom_hand` copies the four files (and swaps `patient_ref`).
  The demo's own compression keeps the largest bin of each stretch, so both flinches survive it.

Two methods, both real, shown where their source is shown (not "fixed"):
- the audience mirror shows the game's live `witness_summary`: flinch 122 ms (in sync) and 125 ms (delayed), size 6.02x / 5.97x;
- the report shows the analytics' `metrics.json`: flinch 112 ms and 115 ms, size 6.0x and 6.0x ("about the same").
Everything else ties: drift +3.0 / +1.0 cm, ownership 6 / 3 (shown +2.0 / -1.0 on the mirror), q4 = 4 asked once after the
last condition (the delayed side reads "No data" / "Question not answered", not 0).

Timing (`phantomDemoSeconds`): calibrate 4, probe 4 (x4), induction 10 (x2, recorded 60 s), threat 5 (x2, recorded speed:
5 s), questionnaire 4 (x2), dissolve 5, reveal 5, witness 5 = 73 s; the mirror stays 12 s.

## 5. Commands and real output

- `H:\flutter\bin\flutter.bat pub get` (app/): "Got dependencies!" then "Building with plugins requires symlink support.
  Please enable Developer Mode" (exit 1; Windows Developer Mode is off in this account; `flutter test` and `flutter analyze`
  still run). `windows/flutter/generated_plugins.cmake`, rewritten by that step, was restored with git.
- `flutter analyze` (app/): section 7.
- `flutter test test/phantom/phantom_demo_test.dart test/phantom/phantom_demo_flow_test.dart` while building: the unit file
  15 tests, the flow file `02:03 +6: All tests passed!` (a 7th flow test, small phone + 2.0 text, was added later and passed alone).
- Full run: section 7.

## 6. Not done / not verified

- Not run on a phone, an emulator, Windows desktop or the web build; no APK was built by me. Pictures and tests are at
  390x844 only; text scale 2.0 and 1280x800 of the new screens not looked at.
- The Hindi captions and labels are new and need a native check (the closing lines in Hindi are the recording's own).
- Not on the patient list screen (see section 2); the Monitor tab's old "Try demo" is untouched and is another demo.
- The demo participant shows in the Programs and Reports tabs too (it is a normal patient with one session).
- Hub mode was tested with a fake running hub (no headsets, empty sessions folder), not with a real hub server.
- The profile of the demo participant still offers "New program" (opens the Orchard program builder).

## 7. Results (real output, `app/`, `H:\flutter\bin\flutter.bat`)

`flutter analyze`:
```
0 errors, 0 warnings, 251 infos
251 issues found. (ran in 15.8s)
```
The 251 infos are the ones the project already had (234, mostly `unnecessary_type_name_in_constructor` and friends) plus 17
style infos in my four new test files (escaped quotes, a missing type annotation). Exit code 1 as before, because infos count.

`flutter test --concurrency=2` (full suite, goldens included):
```
01:50 +481: All tests passed!
```
481 tests (the baseline was +456). New: 15 unit tests (`phantom_demo_test.dart`), 7 end-to-end tests
(`phantom_demo_flow_test.dart`: cold start from the login button with the 14 phases in order, delayed first, SYNC/ASYNC chip,
Sleeve + Muscle sensor chips, badge and Skip/Stop in every phase, stone marker and ~6x flinch in both stone drops, 73 s;
the mirror with the witness_summary numbers 122/125 ms, +3.0/+1.0 cm, +2.0/-1.0, q4 0.0 / No data and the closing lines;
the report with the metrics.json numbers 112/115 ms, 6.0 vs 3.0, +3.0 vs +1.0 cm and the "Simulated run" label, after 12 s;
twice in a row from the profile; hub mode with a running hub and nothing connected; Skip; Stop; a 360x640 phone at 2.0 text
scale without overflow; Hindi), 1 picture test (`phantom_demo_pictures_test.dart`).
The existing goldens (patient list, profile, session report, ...) still match: nothing in them changed.

The first full run, with the default concurrency while the Unity APK build kept the CPU at 100 %, had 3 failures, all in
hub tests that race a wall clock: `hub_connection_test` "retries stop after 3 attempts" (a fixed 250 ms wait for 3 retries),
`hub_live_repository_test` "reflects real status/trial_event traffic", `hub_integration_test` "full flow". They touch nothing I
changed; run alone, two passed at once and the first passed 3 of 3 times; the second full run (`--concurrency=2`) was green.
They are flaky under load, not broken by this work.

Pictures (390x844, written by `phantom_demo_pictures_test.dart` into `logs/sessions/screens/ph/app-demo/`):
`01-button-login.png` (the button on the first screen), `01-button-profile.png`, `02-live-card-induction.png`,
`03-results-mirror-1.png`, `03-results-mirror-2.png` (scrolled to the Delayed card), `04-report-1.png`, `04-report-2.png`.

## 8. How to run it

Start the app (any mode). On the first screen ("Sign in") tap "Run Phantom Hand demo" (or open "Demo participant (simulated)"
in the patient list and tap it there). Nothing else to press; Skip and Stop demo are at the bottom. About 85 s in all.
To rebuild the bundled files from a recording: copy the recording folder, run `opus_analytics <copy> --summary`, then
`tool/phantom_demo/build_trace.py <copy> <trace.json>` and `install_assets.py <copy> <trace.json> app/assets/demo/phantom_hand`.
