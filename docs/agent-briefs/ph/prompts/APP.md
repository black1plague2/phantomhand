# App prompts (ph-builder, Sonnet) — Flutter operator/clinician app

Every prompt starts with: "Read CLAUDE.md, docs/agent-briefs/ph/PRD_v2.md (§4, §10.3), 01–03, docs/APP_DESIGN.md,
app/README.md, contracts/LIVE_PROTOCOL.md (v0.2). Flutter is at C:\flutter\bin. Hub tests use port 8797."
Owns app/** except app/assets/fixtures/manifests/phantom_hand.manifest.json (Opus).

---

## A1 — Live operator card with EMG + accel traces (FR-AP-01, MVP)  · Oct 8 · branch ph/a1-live · timebox 3 h

```text
1) Program builder: phantom_hand appears from its manifest only (dynamic form, x-ui groups, EN/HI). If anything
   game-specific is needed, STOP and write a CROSS-TRACK REQUEST (rule: zero game widgets for params).
2) Hub: accept status.game_state and status.trace (03-SPEC §4, D4); accept uploads of sens_###.json; keep all
   existing behaviour.
3) Live monitor, when the session's game is phantom_hand: a generic game-state card — phase, condition chip
   (SYNC/ASYNC, large, readable from 2 m), time left, node chips (haptic, bio: connected/offline, EMG level), and
   two live traces from `trace`: EMG envelope and |accel|, last 10 s, with markers for threat_impact and emg_burst
   events. Observer mode toggle: full-screen card for a projector/laptop facing the audience.
4) Operator buttons: Start, Next phase (phase_next), Abort phase (abort_phase), Pause, Resume, End, Next person,
   and Condition order (sync_first/async_first; disabled after the first induction). Show sent/acked/failed per
   command.
5) Mock live repository: a scripted phantom_hand session (phases, game_state, synthetic traces) so the UI runs
   with no hub.
Tests: game_state + trace parsing; command enabling rules; trace buffer windowing; hub test storing a phantom_hand
session with sens chunks (port 8797); goldens for the card (390×844, 1280×800, 1600×1000; light/dark; text scale
1.0/2.0). flutter analyze 0/0; flutter test green (≥ O0 floor). Paste output.
```

---

## A2 — Embodiment report + witness mirror (FR-AP-02, STRETCH after G2) · branch ph/a2-report

```text
1) Parse metrics.json `embodiment` (03-SPEC §8, quality flags) and phantom_hand events.
2) Report section "Embodiment" (only when present): drift pre/post per condition (dot + arrow, cm, + toward the
   virtual hand); ownership q1/q2 bars per condition, q3 greyed; flinch traces around impact (EMG env, |accel|,
   wrist speed; SYNC vs ASYNC overlaid; latency markers); stroke timing histogram and cue delivery rate; quality
   chips; sync − async contrast row. Orchard reports unchanged.
3) Witness mirror: when witness_summary arrives, the same two cards as the headset, projector-friendly.
4) Devices screen: haptic and bio nodes as separate rows; bio row shows the power-bank-only safety notice.
5) Rebuild the web bundle to releases/app/<next>/ (CONTEXT says 0.5.1 must not be demoed); log the command + output.
Tests: model tests on contracts/fixtures/sessions/phantom_hand_min; goldens; hidden-when-absent test.
```
