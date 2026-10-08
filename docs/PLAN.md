# Chetna Plan & Agent Handoff

## 1. Model policy (user rule, 2026-09-14)
| Model | Allowed to | Not allowed to |
|---|---|---|
| **Opus** | Ideate, plan, write contracts/briefs, assign, review diffs, accept or reject versions, update CONTEXT | Implementation/execution work |
| **Sonnet** | Implementation that needs judgment: Unity SDK/game C#, Flutter features, analytics algorithms, sim harness, backend | Changing contracts or decisions without an Opus review |
| **Haiku** | Mechanical/bounded work: fixtures, boilerplate, docs, logs, changelog, lint fixes, test data, research harvesting, schema → model codegen runs | Architecture or algorithm work |

**Unity constraint:** one editor instance means only **one** agent drives the Unity CLI/editor at a time.
Other agents may write C# files but must not compile, import or run tests concurrently. Unity tracks are serialized in §3.

## 2. Phases
| Phase | Scope | Exit criteria |
|---|---|---|
| 0 | Docs, contracts v0.1, repo, tooling check | Schemas + fixtures validate in CI; user approves plan |
| 1 | Standalone: Unity SDK + Orchard Reach · Flutter app on mocks · analytics + synthetic patients | Each track tagged `v0.1.0` with test evidence |
| 2 | Simulation: XR Simulator automated runs, synthetic trajectories replayed into Unity, analytics on recorded sessions | Recorded sim session → analytics → app report renders from real files |
| 3 | Bridge: FastAPI + Postgres + storage + worker; LAN relay in app; transports in SDK | End-to-end sim session with a network-loss test (G3) and latency test (G2) |
| 4 | Hardening: security, load test (G7), a11y (G6), real-Quest pilot | v1.0 |

## 3. Task board
Status: ☐ todo · ◐ in progress · ☑ done (tested) · ✖ blocked

> **Status refreshed by Opus 2026-09-19.** The board below was never updated after Phase 0 and showed every
> task as "todo" well into Phase 2, which is badly misleading. Marks now reflect the tree. The authoritative
> current state is still the ★ START HERE box in `CONTEXT.md`; this board is the coarse phase view.

### Track P — Phase 0 (Opus)
- ☑ P1 GOAL, CONTEXT, AUDIT, RESEARCH, ARCHITECTURE, PLAN
- ☑ P2 Contracts v0.1 (now 10 schemas incl. live-message, haptic-message, haptic-device-command) schemas (drafted in `contracts/schemas`); Opus review
- ☑ P3 `git init`, `.gitignore`, create private repo `black1plague2/opus-rehab`, first push *(needs user OK for the GitHub publish)*
- ☑ P4 Verify Unity CLI + Meta XR Simulator + Flutter toolchain

### Track C — Contracts tooling · **Haiku**
- ☑ C1 Example fixtures for every schema (`contracts/fixtures/`), including 3 synthetic sessions
- ☑ C2 `contracts/validate.py` (jsonschema) + GitHub Action
- ☐ C3 Codegen scripts (still not built; models are hand-written on both sides): JSON Schema → Dart (freezed) and C# model stubs

### Track U — Unity SDK + game · **Sonnet** (serialized; sole Unity CLI user)
- ☑ U1 Create Unity 6000.4.6f1 project `game/` (URP, OpenXR, Meta XR Core + XR Hands, Newtonsoft, Addressables, Test Framework, XR Simulator); AAR patcher copied from reference
- ☑ U2 `com.opus.sdk` embedded package: IGameModule, GameManifest, ParamBinder, TrialRecorder, KinematicsRecorder, Outbox, MockTransport, EditMode tests
- ◐ U3 Shell scene (Orchard scene + seated rig + lean vignette done; pairing/calibration/results flow NOT built): mock pairing → program runner → calibration → game → results
- ☑ U4 Orchard Reach game + manifest + PlayMode tests (EditMode 107/107, PlayMode 6/6) + manifest + PlayMode tests
- ☑ U5 Synthetic hand-driver: replay `sim/` trajectories into XR Hands in Editor/Simulator (test without a headset)
- ☐ U6 XR Simulator smoke test; no tags cut yet; tag `sdk-v0.1.0`, `game-orchard-v0.1.0`

### Track A — Flutter app · **Sonnet** (UI polish sub-tasks · Haiku)
- ☑ A1 Scaffold `app/`: Flutter stable, Riverpod, go_router, freezed, Drift, intl (EN/HI), Material 3 theme, CI analyze/test
- ☑ A2 Mock data layer reading `contracts/fixtures` + synthetic sessions (repository interfaces so the real API drops in later)
- ☑ A3 Auth/roles, patients, patient profile, patients, patient profile
- ☑ A4 Program builder with **dynamic forms from manifest param schema**
- ☑ A5 Session report + longitudinal progress + longitudinal progress (charts, MDC bands, quality flags)
- ☑ A6 Live monitor (on a REAL hub stream, not a mock) + outcome-measure entry on a mock stream; outcome-measure entry (FMA-UE, ARAT, BBT)
- ◐ A7 a11y pass, goldens (157 tests incl. 38 goldens); web build only, no tag, golden tests, strings extraction; tag `app-v0.1.0` (web + Android builds)

### Track N — Analytics + synthetic patients · **Sonnet** (tests/fixtures · Haiku)
- ☑ N1 `sim/synthetic_patients`: minimum-jerk reach generator with impairment knobs → kinematics chunks + events matching the contracts
- ☑ N2 `analytics/opus_analytics`: trial segmentation, RT/MT/peak speed, SPARC, LDLJ, nSUB, path ratio, trunk compensation, neglect index, fatigue slope, quality flags
- ☑ N3 Validation report: metric error vs ground truth (G4) → `analytics/VALIDATION.md`
- ☑ N4 pytest fixtures + CLI; tagged `analytics-v0.2.0`, CLI `opus-analyze <session_dir>` producing `metrics.json`; tag `analytics-v0.1.0`

### Track B — Bridge (Phase 3, later) · **Sonnet**
> Not started, by design — but note the Flutter app now hosts its own LAN hub (WS + HTTP + UDP beacon), so
> part of what B3 describes already exists on the app side.
- ☐ B1 FastAPI (async, SQLAlchemy 2 + Alembic), OpenAPI generated from contracts
- ☐ B2 Chunk upload, live WS hub (Redis pub/sub), worker queue
- ☐ B3 SDK LanTransport/CloudTransport; app LAN relay with mDNS
- ☐ B4 E2E sim tests: G2 latency, G3 network loss, G7 load

### Track D — Docs & logs · **Haiku** (continuous)
- ◐ D1 After every merged task: CHANGELOG line, VERSIONS entry if tagged, CONTEXT §2 refresh draft (Opus approves)
- ☑ D2 Deeper Reddit/Quora/X harvest → RESEARCH.md §4
- ☐ D3 `docs/STATUS.html` (never built) visual progress page for the user

### Track S — simulation tooling (added after this board was written) · **Sonnet/Haiku**
- ☑ S1 `sim/live`: fake hub + fake headset, ping-pong RTT, drop/resume, UDP discovery (26 tests)
- ☑ S2 `sim/haptic`: sleeve simulator on UDP 8790/8791 with acks + safety-cap checker (14 tests)

### Haptics (GOAL G9–G12, added 2026-09-18)
- ☑ `contracts/HAPTIC_PROTOCOL.md` v1.1 + device-command schema + `docs/ELECTRONICS_HANDOFF.md`
- ☑ Unity `HapticClient` + UDP transport, cue mapping, safety gating; cues in `events.ndjson`
- ☑ Flutter: sleeve device tile, live cue indicator, session-report cue analysis
- ☐ Real ESP32 hardware (Electronics team; the simulator stands in until their firmware answers on 8790)

## 4. Parallelism map
```
Now ─┬─ P2..P4 (Opus) ──────────────┐
     ├─ C1..C3 (Haiku) ─────────────┤ contracts frozen at v0.1
     │                              ▼
     ├─ U1→U2→U3→U4→U5→U6 (Sonnet, Unity serialized)
     ├─ A1→A2→(A3,A4,A5,A6 parallel)→A7 (Sonnet + Haiku)
     └─ N1→N2→N3→N4 (Sonnet + Haiku)
               N1 output feeds A2 mocks and U5 sim driver
Phase 2 joins U + N; Phase 3 builds B on top.
```

## 5. Definition of done (every task)
1. Code + tests in the track folder; tests pass locally (evidence pasted into the session log)
2. Opus review of the diff
3. Commit with a conventional message; tag if it is a version milestone
4. `logs/CHANGELOG.md` + `CONTEXT.md` §2/§3 updated; `releases/VERSIONS.md` if tagged
