# Contributing to Chetna

Thanks for your interest in contributing. This is a contracts-first project: the JSON
Schemas under `contracts/schemas/` are the only coupling between the Unity game, the
Flutter app, and the Python analytics — so most changes should stay clear of them.

## Ground rules

- **Read first**: `CONTEXT.md` is the living state + handoff doc; `GOAL.md` lists the
  success criteria (G1–G12). Check both before changing anything.
- **Contracts are frozen**: files under `contracts/schemas/` change only via the project
  lead. Keep `contracts/validate.py` green at all times.
- **No functional changes without verification**: paste the real command output for any
  test/build claim. Never assume a test or build passed.
- **Docs are UTF-8** (they use ★, —, ≤). Prefer the Edit tool or Python with
  `encoding='utf-8'` over shell redirection when editing them.
- **Secrets never go into files, logs, or commits.**

## Where changes usually land

| Area | Folder |
|---|---|
| Unity (Quest) game | `game/` |
| Flutter clinician app | `app/` |
| Python analytics | `analytics/` |
| Shared contracts | `contracts/` (lead-only) |
| Documentation | `docs/`, `logs/` |

## Submitting

1. Fork the repo and create a topic branch (`docs/...`, `feat/...`, `fix/...`).
2. Keep the change minimal and verifiable; documentation-only PRs are welcome.
3. Open a PR against `black1plague2/chetna` `main` with a short description of what was
   changed and how it was verified.
