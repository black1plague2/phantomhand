# Docs prompts (ph-scribe, Haiku)

Every prompt starts with: "Read docs/agent-briefs/ph/02-RULES.md §1 and §3 (Docs). You change format, never
facts. Every number you write is copied from a log, with that log's path. Disagreeing logs → list both +
FLAG FOR OPUS." Owns logs/**, docs/MANUAL_TODO.md, docs/PH_*.md, CONTEXT.md sections named by Opus.

---

## H1 — After every run (builder or E2E)

```text
Input: the run log path.
1) Make the log follow this order without changing content: Goal · Plan · Files changed · Commands and output ·
   Acceptance table (pass/fail per item) · Open issues · Requests (CONTRACT/SPEC/CROSS-TRACK) · Next step ·
   Review decision (left for Opus if absent). Fix only headings, ordering and broken code fences.
2) Append one line to logs/CHANGELOG.md: `<date> PH <track> <prompt> run<N>: <one-line result> (<log path>)`.
3) Copy every human-only step from the log into docs/MANUAL_TODO.md under "PH" with the exact steps and the
   source log; don't duplicate items already there (match by text).
4) Update the row for this prompt in docs/PH_STATUS.md: Last log, and Status = "awaiting review" (only Opus
   sets PASS/REDO).
Report what you changed in 5 lines.
```

## H2 — Gate summary (G1, G2, G3)

```text
Read all PH logs since the last gate. Write docs/PH_<gate>_SUMMARY.md: what merged, test counts per suite (with
log paths), open defects, MANUAL_TODO items pending for humans, risks raised in logs. No opinions, no new numbers.
```

## H3 — CONTEXT.md START HERE refresh (only when Opus asks)

```text
Opus gives you the facts list. Rewrite only the "★ START HERE" box's PhantomHand rows/paragraphs, keeping the
existing structure, UTF-8 symbols and every Orchard line untouched. Each new line ends with its evidence
(log path or commit). Show Opus the diff before saving.
```

## H4 — Human runbooks

```text
From U6's log, firmware/README.md, 05-ELECTRONICS-TEAM.md, PRD_v2.md §11 and the E2E L4/L5 logs, write docs/PH_ON_DEVICE_RUNBOOK.md:
numbered steps a teammate can follow alone: charge the bank (nodes detached) → nodes on the bank → check OLED/Serial →
sleeve on (A 5 cm, B 15 cm from the wrist crease) → electrodes (bold: Node B on the power bank only, never a laptop
or charger) → app pairing → APK launch → calibrate →
demo_mode run → reset for next person → troubleshooting table (symptom, cause, fix) built only from issues seen
in logs. Plus docs/PH_DEMO_CHECKLIST.md (one page: night before / 1 h before / per participant / after).
```

## H5 — Demo fact sheet for the pitch

```text
Write docs/PH_FACTS.md: every number we may say on stage (latencies, test counts, sync−async effects from L4/L5,
battery life, frame rate), each with its log path and date. Mark any number from simulation as "(simulated)".
Nothing without a source.
```
