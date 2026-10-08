# Orchestration — who does what

One manager, many workers, one scribe. Humans own hardware, headsets, final commits' approval and the demo.

## 1. Model roles

| Role | Model | Subagent | Does | Never does |
|---|---|---|---|---|
| **Manager / architect / reviewer** | Opus | main session + `ph-reviewer` | Plans the day against PRD v2, writes and owns `contracts/`, assigns prompts, reviews every diff against PRD + spec + rules, runs gates (O2, O3), merges branches, commits, pushes, updates the START HERE box decisions, decides when a prompt is redone | Write large feature code itself (only small fixes during review, ≤ 30 lines) |
| **Implementer** | Sonnet | `ph-builder` | Runs one prompt (U/A/N/S/F) end to end: explore → plan → code → test → paste real output → checkpoint log | Edit `contracts/schemas/*`, commit, push, touch files outside its ownership, run Unity in parallel with another Unity agent |
| **E2E runner** | Sonnet | `ph-e2e` | Runs the L2–L5 test levels in `04-E2E.md`, collects numbers and screenshots, files defects as a list with repro | Fix code (it reports; Opus assigns the fix to a builder) |
| **Scribe** | Haiku | `ph-scribe` | After every run: tidies the run log, appends `logs/CHANGELOG.md`, updates `docs/MANUAL_TODO.md`, refreshes CONTEXT.md sections it is told to, writes runbooks and the demo fact sheet from verified logs | Invent results, change code, change numbers, edit contracts, decide anything |

Why this split: Opus judgment is spent where mistakes are expensive (contracts, reviews, merges). Sonnet does
the volume of code. Haiku keeps the paper trail cheap, so logs are never skipped.

## 2. The loop for every prompt

```
Opus: assign prompt X to a builder, on branch ph/<track>-<X>, with its file-ownership list
  └─ builder (Sonnet): run X → CHECKPOINT log logs/sessions/<date>-PH-<track>-<X>-run<N>.md
       └─ scribe (Haiku): H1 on that log (format, CHANGELOG line, MANUAL_TODO items)
            └─ Opus: O2 review gate → PASS (merge) | REDO (same builder, list of exact fixes) | SPLIT (new prompt)
```

A builder's turn ends only when: tests it was asked to run are green with real pasted output, OR a blocker
is written in the log with the exact error and what was tried. "Should work" is never a finish.

## 3. Branches and commits

- One branch per prompt: `ph/u1-sdk`, `ph/a1-app-live`, `ph/f2-node-a` … from `main` at the time of assignment.
- Builders and scribes never commit. They leave a clean working tree diff and a log. Opus reviews the diff,
  commits with a message `ph(<track>): <prompt> <summary>` + the attribution lines from the session, merges to `main`, pushes.
- Contracts change only on `ph/o1-contracts` (or later `ph/o1b-…`) by Opus; any builder needing a contract
  change writes a "CONTRACT REQUEST" section in its log and stops that part of the work.
- After a merge, every open branch rebases on `main` before its next run.

## 4. File ownership (prevents parallel collisions)

| Track | Owns (may write) | Reads only |
|---|---|---|
| O contracts | `contracts/**`, `game/Assets/Games/PhantomHand/manifest.json`, `app/assets/fixtures/manifests/phantom_hand.manifest.json` | all |
| U Unity | `game/Packages/com.opus.sdk/**` (U1 only), `game/Assets/Games/PhantomHand/**` (except manifest), `game/Assets/Shell/**`, `game/Assets/Scenes/PhantomHand.unity`, `game/Assets/Art/PhantomHand/**`, `game/ProjectSettings/EditorBuildSettings.asset` | contracts |
| A App | `app/lib/**`, `app/test/**`, `app/assets/**` (not the manifest fixture) , `app/l10n*` | contracts |
| N Analytics | `analytics/**` | contracts, sim outputs |
| S Sim | `sim/**`, `tools/demo/**` (incl. `live_plot.py`) | contracts |
| F Firmware | `firmware/**` | contracts |
| H Docs | `logs/**`, `docs/MANUAL_TODO.md`, `docs/PH_*.md`, CONTEXT.md sections named by Opus | all |

Shared files nobody edits without Opus: `CLAUDE.md`, `GOAL.md`, `contracts/schemas/*`, `.github/workflows/*`.

## 5. Parallel rules

- **Unity lock.** Only one Unity batch process may run at a time (one project). Before any Unity batch run an
  agent creates `game/.ph_unity.lock` containing its prompt id and start time, and deletes it after the run
  (also on failure). If the lock exists and is < 30 min old, wait in the foreground and re-check every 60 s;
  if older, ask Opus. Two Unity builders may WRITE code in parallel only when their files don't overlap (e.g.
  U2 logic while U1's tests run), but compile/test runs are serialized by the lock. One agent per scene file.
- Max 3 builders in parallel; their ownership rows must not overlap.
- Network ports per agent to avoid clashes: Unity live tests 8787 (reserved), app hub tests 8797, sim twin
  ports 8790/8791 only in the agent that owns the E2E run; other agents use `--port-offset` flags
  (S1 adds them) e.g. 18790/18791.
- No background waits: a stopped agent is not woken. Long jobs run in the foreground with a timeout.

## 6. Review gate O2 (Opus) — checklist applied to every run

1. Log exists, has real command output for every claim, and a "next step".
2. Diff touches only owned files; no secrets; UTF-8 docs intact.
3. Spec conformance: names, numbers, events, params match `03-SPEC.md` exactly.
4. Tests: new tests exist for new behaviour; all suites the prompt lists pass; OrchardReach and
   existing app/analytics suites still pass (no regressions).
5. Safety rules from `02-RULES.md` §4 honoured (haptic limits, EMG isolation, watchdogs).
6. Contracts: `python contracts/validate.py` green on `main` after merge.
7. Decision: PASS / REDO (exact fix list) / SPLIT. Record it at the bottom of the run log.

## 7. Escalation

- Builder stuck > 2 attempts on the same error → stop, log it, Opus decides (redo with hints, different
  approach, or MANUAL_TODO for a human).
- Anything needing a headset, the physical sleeve, a phone, or the Gradle loopback (R7) → MANUAL_TODO with
  exact steps; the run continues with what it can do.
- Spec ambiguity → builder writes "SPEC QUESTION" in its log and picks the most conservative option; Opus
  answers in `03-SPEC.md` (versioned, dated line at the top).

## 8. Makeathon pace (7–9 Oct)

- **MVP first, always.** A stretch prompt starts only after gate G2 (one full run recorded end to end) and only
  if no MVP item is red. Opus may cut any stretch item without asking.
- **Fast gate:** for small runs (< 300 changed lines, tests green) Opus may do O2 in one pass without
  ph-reviewer. Anything touching contracts, safety limits, the session runner or firmware motor code always
  gets the full checklist.
- **Timebox:** every builder prompt has a timebox (in its header). At the timebox the builder checkpoints
  whatever state it has; Opus decides to continue, cut scope, or switch to the fallback.
- **Freeze:** at Oct 9 12:00 only bug fixes for the demo path merge. Every merge after freeze is followed by
  one full demo_mode run on the Quest.

## 9. Definition of done (whole project)

PRD §12 KPIs met with evidence: full run twice in a row without a reset (L5); both nodes discovered and
streaming wirelessly; SYNC touch-to-sight < 100 ms; sync > async for drift and flinch; EMG flinch clearly above
baseline on the live trace; 5-teammate pilot logged. Electronics sign-off sheet (05 §9) complete,
CONTEXT.md START HERE current, O4 signed, parts returned per CARF.
