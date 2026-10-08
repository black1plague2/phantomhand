# Chetna: agent rules (keep < 200 lines)

Contracts-first VR upper-limb neurorehab: Quest pure VR (hand tracking), Flutter clinician app, Python analytics.
Product name: **Chetna** (चेतना); OPUS is the internal codename, kept in code identifiers.
Product name: **Chetna** (चेतना); OPUS is the internal codename, kept in code identifiers.

## Start / stop
1. Read `CONTEXT.md` (state + resume point) → `docs/IMPROVEMENT_BRIEF.md` (binding) → your track brief in `docs/agent-briefs/`.
2. Before stopping: CHECKPOINT in `logs/sessions/<date>-<track>-runN.md`. A replacement agent must be able to resume from that log alone.

## New session / new account: read these, in this order
1. `CONTEXT.md` — the "★ START HERE" box (current state, next steps, rules); history below it
2. `GOAL.md` — success criteria G1–G12
3. `docs/IMPROVEMENT_BRIEF.md` — user's binding brief + status map
4. `docs/MANUAL_TODO.md` — what only the human can do
5. Latest session logs: `logs/sessions/2026-09-18-*` (newest run per track = resume point); `logs/CHANGELOG.md` for the full timeline
6. Per task: `contracts/*.md` (LIVE_PROTOCOL, HAPTIC_PROTOCOL), `docs/agent-briefs/U-next-run.md` (Unity), `docs/APP_DESIGN.md` (app), `analytics/VALIDATION.md`, `docs/ELECTRONICS_HANDOFF.md` (sleeve)

## Binding docs
`docs/IMPROVEMENT_BRIEF.md` > `docs/APP_DESIGN.md` · `docs/UNITY_PRACTICES.md` · `docs/ARCHITECTURE.md` · `contracts/LIVE_PROTOCOL.md` · `docs/MODEL_REQUESTS.md`
Phantom Hand (PRD v2): docs/agent-briefs/ph/ — PRD, rules, spec, E2E, prompts; binding for PH work.

## Hard rules
- Explore → Plan → Code → Verify. Paste the **real** command output. Never claim tests pass, a build succeeded, or an APK works without it (APKs: only once the human has run them outside the sandbox).
- Agents don't `git commit`/`tag`. Opus reviews, commits and pushes.
- `contracts/schemas/*` change only via Opus. Keep `contracts/validate.py` green.
- Human-only steps → `docs/MANUAL_TODO.md` with exact steps; never block the run.
- No secrets in files or logs.
- Docs are UTF-8 (they use ★, —, ≤). Never rewrite them with Windows PowerShell `Get-Content`/`Set-Content` without `-Encoding UTF8`; prefer the Edit tool or Python with `encoding='utf-8'`. (This corrupted CONTEXT.md once; repaired in 6f1cc42.)
- Never end your turn while work remains; no background waits (a stopped agent isn't woken).
- Parallel agents only with no shared files and no shared Unity editor.

## Unity (Track U)
- Meta AI Agent Bridge **disabled**; Unity Hub running; **batch mode only** with the GUI editor closed (see the root-cause section of `docs/agent-briefs/U-next-run.md`).
- **User override (2026-09-19, reaffirmed 2026-10-07):** drive the OPEN editor through the Unity MCP (`meta-xr-unity-runtime`), one Unity driver at a time, instead of batch mode. After an editor restart the session's MCP client must be reconnected (`/mcp`).
- If a GUI editor is in use: after any reload-triggering change, CHECKPOINT and wait for human confirmation that the editor is healthy.
- Raw ISDK `IHand` (before HandFilter), record on `CurrentDataVersion` change; HT frequency LOW, FMM off (`OPUS_HT_FAST_MOTION` for A/B); measured rate → `session.json`.

## Flutter (Track A)
- Flutter at `C:\flutter\bin`. GoRouter lives inside a Riverpod provider (refreshListenable on auth/role; one redirect function). Each feature exports its RouteBase list.
- Zero game-specific widgets (dynamic form from manifest `paramSchema`). Goldens at 390×844 / 1280×800 / 1600×1000, light/dark, 1.0/2.0.
- Hub tests on port 8797 (8787 is reserved for Unity live tests).

## Analytics (Track N)
- Always propagate `rate_hz` + `quality`; SPARC/LDLJ `degraded` when `rate_hz < 45` or tracking loss exceeds the documented threshold. Target < 5 % error vs synthetic ground truth.
