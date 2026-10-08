# Chetna Improvement Brief (user-supplied 2026-09-17; binding) + Opus reconciliation

The user's brief is below, after the status map. Where it conflicts with an older brief, **this file wins**.
Clinical figures in §1 are the user's summary of the literature; agents must cite primary sources before putting any number into user-facing copy.

## Opus status map (2026-09-17 22:10 IST)
| Brief item | Status now | Owner |
|---|---|---|
| §5.1 fake_hub.py replies `pong` (echo_ts_ms) | ☐ not done → **Track S agent (Haiku)** | S |
| §5.2 Flutter hub providers + Devices + Live monitor (B3) | ☑ done in A run3–run4 (verified: hub↔fake headset 46/46; live monitor on real stream; devices screen) | A |
| §5.3 ReachTraceGlyph + OpusTokens + goldens (B2/B4) | ◐ tokens, font, glyph, 38 goldens done; patient overview recovery line + glyph strip, chart labels, contrast test → **A run6 running** | A |
| §5.4 Unity demo hand mesh, carry-to-basket, R5 re-audit | ◐ carry-to-basket glide done (run7); hand mesh + eye-camera captures → **U run8 running** | U |
| §5.5 VALIDATION.md tables + SPARC quality gating by rate_hz | ◐ VALIDATION.md exists (analytics-v0.1.0); rate_hz < 45 gating, tracking-loss threshold, per-trial trunk lean + neglect with MDC → **N run2 (Sonnet)** | N |
| §5.6 validate.py green + Dart/C# codegen per schema change | ◐ validator green; codegen (C3) not built → queued after N/S | C |
| §1 unimanual / side profiles | ◐ manifest has `side`; Unity enforcement + tests → U | U |
| §1 real-time form feedback (lean vignette + audio/haptic) | ☐ → U (after run8) | U |
| §1 dose ≥ 30 min + adherence in the Flutter progress view | ☐ → program schema already has duration; add dose tracking in U + adherence in A | U, A |
| §2 bridge disabled, batch mode, no GUI while agents write | ⚠️ **conflict:** the user reopened the GUI editor with the bridge on (21:55). Resolution: follow this brief. The user closes the editor → U agent returns to batch mode. Until then the U agent does file-level work only | U + user |
| §2 "stop and wait for human confirmation after reload-triggering changes" | Applies **only when a GUI editor is used**; batch runs self-verify (exit code + log) | U |
| §2 never claim a successful APK until the human runs it outside the sandbox | ☑ adopted | U, A |
| §3 GoRouter inside a Riverpod provider, refreshListenable, one redirect fn, feature RouteBase lists | ☐ verify/refactor → A (after run6 items) | A |
| §4 CLAUDE.md ≤ 200 lines, feature-specific agents with Input/Output/Constraints/Stop-when | ☑ `OPUS/CLAUDE.md` created; agent prompts follow that format from now on | Opus |

---

(The user's brief follows verbatim.)

## Context (read first)
- Project: contracts-first VR upper-limb neurorehab (Quest pure-VR, hand tracking primary).
- Tracks: A (Flutter clinician app), U (Unity SDK + Orchard Reach), N (analytics + synthetic patients), C/D (contracts/docs).
- Binding docs: APP_DESIGN.md, UNITY_PRACTICES.md, ARCHITECTURE.md, LIVE_PROTOCOL.md, MODEL_REQUESTS.md.
- Pain points already logged in MANUAL_TODO.md (Unity editor hangs from Meta AI Agent Bridge, Gradle AF_UNIX loopback, Windows VS C++ incomplete, demo hand mesh missing, no grass texture, CenterEyeAnchor rotation reset, fake_hub.py missing pong, etc.).

## 1. Clinical & scientific hardening (highest leverage)
Evidence (2025-2026 RCTs/meta-analyses + AHA/ASA 2026 Stroke Rehab Guideline):
- Immersive VR + sufficient dose (>600 min total, ≥30 min/session, 4–5 days/week, ≥3–4 weeks) gives moderate effect on FMA-UE (SMD ~0.58).
- Task-specific, graded practice + real-time form feedback is gold standard; patients report missing therapist cues.
- Quest 2/3 hand tracking: ~1.1 cm fingertip positional error, ~45 ms delay, velocity overestimated ~1.3–1.5×, acceleration noisier. Reliable enough for position/peak-velocity/ROM after filtering; SPARC/LDLJ need rate_hz ≥45 and quality flags.
- Vagus-nerve stimulation paired with rehab doubles gains in chronic stroke; vibrotactile feedback helps when combined with active training.
- Unimanual / affected-side modes mandatory (~60 % hemiparesis).

**Instructions:**
- In `analytics/opus_analytics` and `contracts/schemas/metrics.schema.json`:
  - Always record and propagate `rate_hz` and `quality` (ok/degraded/invalid).
  - Flag SPARC/LDLJ as degraded when rate_hz < 45 or tracking loss > X % (document threshold from VALIDATION.md).
  - Add per-trial trunk-lean (head displacement) and workspace asymmetry (neglect index) with MDC bands.
- In Unity Orchard Reach / Shell:
  - Enforce unimanual + left-only / right-only profiles from program params (already in manifest).
  - Add real-time form feedback (trunk-lean vignette already planned; add audio/haptic on excessive lean or low confidence).
  - Guarantee ≥30 min effective reach practice per prescribed session; surface dose adherence in Flutter progress view.
- Synthetic patients (N1): expand impairment knobs to include fatigue drift, tracking dropouts, and side-asymmetry that match the clinical literature above. Keep ground_truth for <5 % metric error goal.
- Outcome measures: keep FMA-UE, ARAT, Box & Block, MAS entry forms; overlay them on VR metrics with the same MDC language used for SPARC.

## 2. Unity / Meta XR / hand-tracking reliability
From UNITY_PRACTICES.md + Quest validation papers + repeated editor hangs:
- Keep Meta AI Agent Bridge **disabled** (EditorPref). Run Unity only in batch mode with Hub open; never leave a GUI editor open while agents write files.
- Hand data must come from the **raw** IHand (before any HandFilter). Record only on CurrentDataVersion change. Map joints exactly as documented.
- Frequency: default LOW + FMM off; expose OPUS_HT_FAST_MOTION define for A/B. Measure actual rate at runtime and write to session.json.
- Demo / no-headset path:
  - Replace DemoHandProxy_R sphere with ghost_hand_static (or ISDK translucent hand) oriented by recorded wrist rotation.
  - Fix carry-to-basket: either remap fixture kinematics into scene calibration frame or (demo-only) lerp held fruit to basket once Grasped.
- R5 visual audit: re-shoot after every material/scale change; assert apple body ≈ 7 cm, table height seated, no pink materials, <100 draw calls.
- Android build: document the exact Gradle loopback failure; agent must never claim a successful APK until the human runs it outside the sandbox.

**Claude Code rule:** After any .cs or scene change that can trigger domain reload, write a CHECKPOINT, stop, and wait for human confirmation that the editor is healthy before the next tool call.

## 3. Flutter app (Track A) polish & architecture
From APP_DESIGN.md + current Flutter Riverpod + go_router best practices:
- Router: always create GoRouter inside a Riverpod provider; use refreshListenable on auth/role state; keep all redirects in one top-level function (or per-route guards). Never rebuild the whole router on every state change.
- Feature folders: auth, patients, programs, sessions, progress, live, outcomes, devices, settings — each exporting its own RouteBase list; shell composes them.
- Dynamic form engine (A4): already the differentiator. Keep zero game-specific widgets. Add live workspace preview that updates from paramSchema ranges.
- Design system: finish OpusTokens (light/dark), bundle Atkinson Hyperlegible Next (tabular figures), ReachTraceGlyph from real kin chunks.
- Live monitor: bind to real hub stream (B3); draw straight home→target stroke live, replace with real path when kin chunk arrives.
- a11y (A7): semantics, 200 % text scale, contrast unit test, golden tests at 390×844 / 1280×800 / 1600×1000 light/dark × 1.0/2.0.
- Builds: web release first; APK only after human confirms loopback works outside sandbox; Windows only after VS C++ workload is complete.

## 4. Multi-agent / Claude Code workflow (apply immediately)
From Anthropic Claude Code docs + 2026 multi-agent patterns:
- Keep CLAUDE.md / AGENTS.md under ~200 lines; put track-specific rules in sub-folders.
- Sub-agents: feature-specific, not generic “backend” or “qa”. Give each a clear Input / Output / Constraints / Stop-when.
- Parallel only when no shared files and no shared Unity editor.
- Always: Explore → Plan (plan mode) → Code → Verify (run the exact test/command and paste real output).
- Checkpoint after every milestone into `logs/sessions/<date>-<track>-runN.md`. A replacement agent must resume from the log alone.
- Never claim “tests pass” or “build succeeded” without the real command output in the log.
- Manual gaps go into MANUAL_TODO.md with exact steps; never block the run.

## 5. Immediate next actions (ordered)
1. Fix `sim/live/fake_hub.py` to reply `pong` (echo echo_ts_ms) so RTT can be measured.
2. Wire Flutter hub providers + Devices + Live monitor (B3).
3. Finish ReachTraceGlyph + OpusTokens + golden tests (B2/B4).
4. Unity: swap demo hand mesh, fix carry-to-basket decision, re-run R5 visual audit with real screenshots.
5. Analytics: publish VALIDATION.md error tables; enforce quality flags on SPARC when rate_hz low.
6. Contracts: keep validate.py + fixtures green; codegen Dart/C# on every schema change.

## Done criteria for any improvement PR
- Real test / analyze / build output pasted.
- Session log updated with CHECKPOINT.
- No secrets, no git commit/tag by the agent.
- MANUAL_TODO updated for any human-only step.
