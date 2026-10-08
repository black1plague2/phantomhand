# Opus prompts (manager session: `claude --model opus` at the repo root)

Every prompt assumes you already read CLAUDE.md, docs/agent-briefs/ph/PRD_v2.md and 00–04.

---

## O0 — Setup and baseline  · Oct 7, first · timebox 45 min

```text
You are the manager (Opus) for the Phantom Hand build (PRD v2, Vedanta Makeathon 7–9 Oct). Read CLAUDE.md,
CONTEXT.md (START HERE), GOAL.md, docs/agent-briefs/ph/PRD_v2.md, 00-README.md, 01-ORCHESTRATION.md,
02-RULES.md, 03-SPEC.md, 04-E2E.md.
1) Repo: git status; git log origin/main..HEAD. If clean and only ahead, push main. Untracked
   game/Assets/_Recovery/** → don't delete; add a MANUAL_TODO item for a human to inspect it.
2) Install subagents: copy docs/agent-briefs/ph/agents/*.md to .claude/agents/; confirm they load.
3) Add `game/.ph_unity.lock` to .gitignore.
4) Baseline (real output → logs/sessions/<date>-PH-O0-run1.md): python contracts/validate.py; analytics pytest;
   pytest sim/haptic sim/live tools/demo/tests; flutter analyze + flutter test (C:\flutter\bin); Unity EditMode +
   PlayMode batch (take the Unity lock). Counts = regression floor. If Unity is slow, start it first and do the rest
   while it runs.
5) CLAUDE.md, under "Binding docs", add one line: "Phantom Hand (PRD v2): docs/agent-briefs/ph/ — PRD, rules,
   spec, E2E, prompts; binding for PH work." Nothing else in CLAUDE.md changes.
6) docs/PH_STATUS.md: table of every prompt (O1, U1–U8, F1–F2, S1–S3, A1–A2, N1–N2, H1–H5) with MVP/stretch,
   day, owner model, branch, status, last log. Keep it current after every gate.
7) Commit "ph(o0): PRD v2 pack installed, baseline recorded", push. Tell the humans the baseline and start O1.
   In parallel with O1 you may already launch F1, F2 and S1 (they don't depend on contract files beyond the PRD).
```

---

## O1 — Contracts v0.2  · Oct 7 · timebox 1.5 h

```text
Implement 03-SPEC.md §0, §4, §6, §7, §8 as contract changes on ph/o1-contracts:
1) haptic-device-command.schema.json: cue enum + "stroke"; optional play_at_ms (int); intensity still 0–255
   (firmware caps 150; document it).
2) haptic-message.schema.json: discovery PRD §9.1 form (device_kind haptic|bio, firmware_version, command_port,
   status, motor_count, timestamp_ms) alongside the legacy hello; sensor_data v1 (Node A); sensor_chunk (Node B,
   PRD §9.3); emg_burst; display; subscribe; status gains device_kind, subscribers, battery_pct nullable.
3) event.schema.json: phase_start, calibration, stroke, drift_probe, threat_impact, threat_response,
   questionnaire_item, emg_burst, witness_summary; haptic_cue.data.cue may be "stroke".
4) New sensor-file.schema.json for sens_###.json (session_id, chunk_seq, t0_ms, source; emg_env[t_ms,value,
   device_id], imu[t_ms,ax,ay,az,gx,gy,gz,device_id]); list it in session-envelope next to kinematics chunks.
5) live-message.schema.json + LIVE_PROTOCOL.md: commands phase_next, set_condition_order, abort_phase,
   next_person; status.game_state and status.trace (03-SPEC §4).
6) metrics.schema.json: optional `embodiment` (03-SPEC §8), quality on every value.
7) game/Assets/Games/PhantomHand/manifest.json (03-SPEC §6, x-ui hints, EN+HI display names; agency_enabled
   default false; stimulated_side fixed right) + the copy at app/assets/fixtures/manifests/phantom_hand.manifest.json.
8) Fixtures contracts/fixtures/sessions/phantom_hand_min/ covering every new event, one kin chunk, one sens chunk,
   metrics.json with embodiment.
9) HAPTIC_PROTOCOL.md → v1.2 (stroke mapping 200 ms pulse, intensity round(150 × max), lead/delay semantics, D1
   brush/SOA rule, subscribe, display, bio messages, keepalive). ELECTRONICS_HANDOFF.md: point to
   docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md as current. Contracts version v0.2.
Verify with real output: validate.py; --session on phantom_hand_min and every existing fixture; analytics, sim and
app test suites ≥ floor. Log, commit "ph(o1): contracts v0.2", merge, push, update PH_STATUS. Launch U1 (Unity
lock), and A1/N1/S2 as soon as their day starts.
```

---

## O2 — Review gate (after every run; fast-gate rules in 01-ORCHESTRATION.md §8)

```text
Review <log> on <branch> with 01-ORCHESTRATION.md §6. Read the log, git diff main...<branch>, re-run the cheapest
decisive test. Check names, numbers, events, params, ports and safety limits against PRD_v2.md, 03-SPEC.md and
02-RULES.md §4. Decide PASS / REDO (numbered fixes) / SPLIT / CUT (stretch, out of time). Append the decision.
PASS → commit "ph(<track>): <prompt> <summary>", merge, push, rebase open branches, PH_STATUS, ask ph-scribe for H1.
```

---

## O3 — Gate checkpoint (G1 Oct 7 night · G2 Oct 8 night · G3 Oct 9)

```text
For gate <G>: pull main; run the regression floor; assign ph-e2e the level(s) 04-E2E.md ties to this gate; collect
the electronics sign-off rows due by now. If any MVP check fails: list it, assign REDOs, and decide the fallback
from PRD §13 (e.g. drop EMG and keep IMU + hand-tracking flinch; laptop plot instead of the Flutter card; Quest Link
instead of APK). Write the decision and the fallback in logs/sessions/<date>-PH-<G>.md, ask ph-scribe for H2 and H3,
and tell the humans in 5 lines what is green, what is red, and what each person does next.
```

---

## O4 — Final sign-off  · Oct 9 after G3

```text
Confirm with evidence links: G3 table all PASS; PRD §12 KPIs; electronics sign-off sheet complete; regression floor
met; CONTEXT.md START HERE current (H3); PH_STATUS final; APK in releases/game/<ver>/ with its human run logged; H5
fact sheet only contains logged numbers. Write logs/sessions/<date>-PH-O4-signoff.md, commit, tag `ph-v1.0`, push.
Remind the team to return issued parts per the CARF.
```
