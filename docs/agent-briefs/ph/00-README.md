# Phantom Hand pack — start here (v2, Makeathon 7–9 Oct 2026)

Everything needed to build Chetna's Theme 5 entry per **PRD v2** (`PRD_v2.md`, in this folder): Unity game,
Flutter app, analytics, both sleeve firmwares, the electronics build, and the end-to-end proof.

Hardware (PRD v2): Meta Quest 3 · 2 × ESP-WROOM-32 (Node A haptic + IMU + OLED, Node B EMG) · one URBN
20,000 mAh power bank · BioAmp EXG Pill · 2 ERM motors.

Repo: work in the LOCAL copy `Documents/VR_games/OPUS` (ahead of GitHub; has firmware/ and tools/demo/).
O0 pushes it before parallel work starts.

## Precedence

`PRD_v2.md` > `03-SPEC.md` §0 decisions > rest of `03-SPEC.md` > `CLAUDE.md` > other docs. CLAUDE.md's hard
rules (honesty, no commits by agents, contracts via Opus, batch-mode Unity) are never overridden.

## Files

| File | For | What |
|---|---|---|
| `PRD_v2.md` | everyone | the product requirements (team's document) |
| `01-ORCHESTRATION.md` | Opus + humans | Opus manages, Sonnet builds, Haiku docs/logs; branches, gates, the Unity lock |
| `02-RULES.md` | every agent | hard rules, per-track rules, safety, testing honesty |
| `03-SPEC.md` | every agent | how the PRD is built: decisions D1–D8, protocol, phases, params, events, metrics |
| `04-E2E.md` | Opus, ph-e2e | test levels, gates and KPIs mapped to the 3-day plan |
| `05-ELECTRONICS-TEAM.md` | electronics humans | WROOM + power bank build guide, bench checks, sign-off |
| `06-TEAM-PLAYBOOK.md` | Garv, Vaibhav, Bhavya | hour-by-hour plan, roles, demo runbook, pitch |
| `agents/*.md` | Claude Code | subagents (model per role), installed by O0 into `.claude/agents/` |
| `prompts/OPUS.md` | Opus | O0 setup · O1 contracts · O2 gate · O3 checkpoint · O4 sign-off |
| `prompts/UNITY.md` | Sonnet | U1–U6 MVP, U7–U8 stretch |
| `prompts/FIRMWARE.md` | Sonnet | F1 Node A v0.5.0 · F2 Node B v0.5.0 |
| `prompts/SIM.md` | Sonnet | S1 twin-lite (MVP) · S2 E2E harness + live plot (MVP) · S3 full twin (stretch) |
| `prompts/APP.md` | Sonnet | A1 live card + traces (MVP) · A2 report (stretch) |
| `prompts/ANALYTICS.md` | Sonnet | N1 embodiment metrics (MVP) · N2 validation (stretch) |
| `prompts/DOCS.md` | Haiku | H1 per run · H2 summary · H3 CONTEXT · H4 runbook · H5 facts |

## Run order (critical path is Unity; everything else feeds it)

```
Oct 7 evening (now → night)
  Opus  : O0 setup → O1 contracts v0.2
  Humans: PRD §14 bench list (flash, I2C scan, motors, BioAmp, SOA tuning, tracking with sleeve)
  Sonnet: F1 Node A · F2 Node B · S1 twin-lite · U1 SDK          (parallel; U1 holds the Unity lock)
  GATE G1: both nodes discovered over Wi-Fi; EMG trace usable; U1 green
Oct 8
  morning  : U2 logic+scene · N1 metrics · A1 live card · S2 harness + live plot
  afternoon: U3 arm+brush+threat · U4 probe/questions/witness UI
  evening  : U5 composition + live link → E2E L3 (sim) → E2E L4 (real nodes) → tune tactile_lead_ms, motor_soa_ms
  GATE G2: one full 4-minute run recorded end to end (PRD §14)
Oct 9
  morning  : U6 APK + demo mode → E2E L5 on Quest → 5-teammate pilot → tune offset, stroke rate
  midday   : H4 runbook, H5 facts, pitch rehearsal; stretch only if G3 already met
  GATE G3: demo runs twice in a row without a reset → O4 sign-off → return parts (CARF)
Haiku H1 after EVERY run.
```

Stretch (only after G2, never on the critical path): U7 agency phase, U8 synthetic participant, S3 full twin
+ Wokwi, A2 Embodiment report, N2 validation, counterbalanced order UI, left-arm support.

## How to start

At the repo root: `claude --model opus`, then give it prompt O0 from `prompts/OPUS.md`. Opus launches workers
with the subagents ("Use the ph-builder agent to run U1 from docs/agent-briefs/ph/prompts/UNITY.md"), or
teammates run a worker on their own machine: `claude --model sonnet`, paste the prompt, push the branch.
After every run: "Use the ph-scribe agent to run H1 on <log path>".
