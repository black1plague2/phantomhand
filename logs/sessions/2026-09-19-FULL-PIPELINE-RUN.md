# Full pipeline run — 2026-09-19 (Opus)

The first time every component of OPUS ran **as one system**, and the first time a session recorded by the
Unity game has ever reached the analytics engine.

Before this run each leg was proven alone: Unity against a Python fake hub, the Flutter hub against a Python
fake headset, the sleeve simulator against Unity. Nothing had ever been joined, and running them together
turned up four real defects that no single-leg test could have caught.

## What ran, simultaneously

| Process | What it is | Port |
|---|---|---|
| `dart run tool/hub_cli.dart --port 8787 --auto-drive` | the real Flutter clinician hub | 8787 |
| Unity 6000.4.6f1 batch PlayMode, `FullPipelineIntegrationTests` | the headset session | — |
| `python -m haptic.fake_haptic` | the sleeve simulator | 8790 / 8791 |

The Unity test deliberately **starts no peers of its own** — unlike the existing single-leg tests, it attaches
to whatever is already running, and skips with a clear message if nothing is. That is the whole point: it
cannot pass by talking to a stub it spawned itself.

## Result: PASS

```
Unity PlayMode  FullPipeline_AppHub_Headset_Sleeve_ProducesAnalysableSession   Passed (13.6 s)
session 933e98f3-7754-4e47-b6ff-389f0e9e3694
  trialsCompleted=6  trialEvents=34  formWarnings=2  hapticCues=4  kinChunks=2
  uploaded 4 files to the hub (session.json, events.ndjson, kin_000.json, kin_001.json)
  live-link RTT to the app hub: p50 165.1 ms, p95 167.8 ms   (G2 target < 250 ms on LAN)
  contracts/validate.py --session   exit 0
```

The hub's stored copy (`app/.hub_data/933e98f3-.../`) is byte-identical and independently passes
`contracts/validate.py --session`. **G3's local-first path is therefore demonstrated end to end**: the headset
recorded, uploaded, and the clinician side holds a valid session.

## Analytics over the session the game actually produced

`python -m opus_analytics app/.hub_data/933e98f3-7754-4e47-b6ff-389f0e9e3694`

### Session
| Metric | Value | Quality |
|---|---|---|
| rate_hz | 72.0 Hz | ok |
| success_rate | 1.0 | ok |
| trunk_lean_cm | 0.35 cm | ok |
| tracking_loss_pct | 40.0 % | ok |
| neglect_index | — | invalid |
| fatigue_slope | — | invalid |
| reach_envelope_area_m2 | — | invalid |

### Per trial
| trial | RT ms | MT ms | peak m/s | SPARC | LDLJ | endpoint cm | trunk cm | loss % |
|---|---|---|---|---|---|---|---|---|
| 0 | — | — | — | — | — | — | — | — |
| 1 | 3.81 | 0.05 | — | — | — | — | — | 100 |
| 2 | 0.31 | 0.03 | — | — | — | — | — | 100 |
| 3 | 0.75 | 1137.55 | 1.35 | −1.50 | −7.06 | 79.15 | 0.88 | 0 |
| 4 | 0.87 | 1012.25 | 2.25 | −1.54 | −7.37 | 132.94 | 0.08 | 0 |
| 5 | 0.76 | 929.83 | 1.33 | −1.53 | −6.95 | 90.59 | 0.09 | 0 |
| 6 | — | — | — | — | — | — | — | — |

Trials 3–5 are real, plausible movement metrics computed from kinematics this game recorded. Trials 1–2 fall
inside the demo's **deliberate** tracking-loss window, and analytics correctly refuses to compute rather than
inventing numbers — the quality gating works, which is itself worth noting.

## Four defects this run exposed

1. **The game never recorded kinematics at all.** `KinematicsRecorder` existed in the SDK and nothing ever
   constructed it, so every session the game had ever produced was events-only and *could not be analysed*.
   GOAL.md's north star is "the headset records raw kinematics + trial events, not just scores" — the pipeline
   was broken at exactly that step and no test noticed, because no test had ever asked analytics for an answer.
   Now wired into `OrchardReachSceneController`, sampling at 72 Hz off the same `IHandSource` the game uses.

2. **`trial_end` was never emitted on the successful path.** The timeout and dropped-grip paths both emitted
   it; a successful basket placement — the *normal* path — emitted only `placed` and finished the trial
   internally. Analytics segments a trial as `target_shown -> trial_end`, so every successful trial was left
   unclosed and **every metric came back null**. Every file still passed schema validation, which is exactly
   why this survived so long: the contracts check shape, not meaning.

3. **`contact` was never emitted.** Analytics uses it as the end of the reach window and gates movement time,
   peak speed, SPARC, LDLJ, submovements, path ratio, endpoint error and trunk lean on it
   (`quality.flag_for_window(..., events_complete)` -> `required_event_missing`). Now emitted at the moment
   `IsGraspOverTarget` first becomes true, which is precisely "the hand reached the target".

4. **Two clocks in one session.** A recorder built with its own `new SessionClock()` put trial events on a
   different origin from the kinematics, so no sample fell inside any trial window. The controller now exposes
   its `Clock` and the rule is documented on it: one session, one timeline.

Also fixed: `python -m opus_analytics` crashed with `KeyError: 'value'` when printing its summary — it
iterated `result["session"]` (which is `{"metrics": {...}}`) instead of the metric map, and assumed every
metric has a value when `quality: invalid` legitimately carries `None`.

## Known-wrong and still open

- **`endpoint_error_cm` of 79–133 cm is not a physical reach.** The recorded joint positions are chest-relative
  (the fixture's frame) while the trial target is in world coordinates, so the error is measuring a frame
  mismatch, not the patient. Everything downstream of it is suspect. **Do not put this metric in front of a
  clinician until the frames are reconciled.** This is the single most important follow-up.
- `neglect_index`, `fatigue_slope` and `reach_envelope_area_m2` stay invalid — they need more completed trials
  with clean tracking than this 9-second demo produces.
- Reaction times under 1 ms are an artifact of the demo driver, which satisfies the grasp condition almost
  immediately; they are not a measurement of anything. A realistic demo reach profile is needed before RT from
  this path means anything.
- `logs/sessions/screens/unity/run12/02_grasp.png` captured a frame with neither hand nor apple visible — the
  shot fires on the first frame the state is entered, before the visual is positioned. The mid-reach and placed
  shots are good. `01_mid_reach.png` also shows a stretched dark-red sliver between the trees (a degenerate
  apple/LOD artifact) that should be investigated.
- The `hubSaidStart=False` in later runs is benign — the hub had already driven its scripted scenario for an
  earlier connection — but the demo would read better if the hub re-armed per headset.

## Regression check after these changes

`trial_end` and `contact` are changes to shipped game logic, so both suites were re-run in full:

```
EditMode  107/107 passed
PlayMode    (see run12_playmode_results.xml)
```
