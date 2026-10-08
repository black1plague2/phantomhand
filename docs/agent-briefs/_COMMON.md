# Rules for every agent (prepend to every brief)

1. Read `CONTEXT.md`, `docs/ARCHITECTURE.md`, and your brief. Stay inside your track folder, except for the log files named below.
2. `contracts/schemas/*` are **read-only** for you. If a contract seems wrong, stop and write the proposal in your session log under "Contract change request". Opus decides.
3. Reference projects (`VR_games/balloon`, `VR_games/anubhav`, GitHub `Nainikap/aastheen`) are read-only inspiration. Never copy their per-game hardcoding patterns (see `docs/AUDIT.md`).
4. Unity: only the agent assigned the Unity track may run the `unity` CLI or MCP Unity tools.
5. Test everything in simulation or with mocks. Paste real command output as evidence. Never claim a pass you didn't see.
6. Before you stop, always write `logs/sessions/<date>-<track>-<task>.md`: what you did, files touched, test evidence (real output), issues, proposed CHANGELOG lines, next step.
   **Do not run `git commit`/`git tag` and do not edit `logs/CHANGELOG.md`, `CONTEXT.md`, or `releases/VERSIONS.md`.** Agents run in parallel on one repo; the orchestrator (Opus) reviews, commits, tags, and updates those files.
7. **Checkpoints (usage limits can kill you mid-task):** after every milestone, append `## CHECKPOINT n: <what is done, verified how, what's next>` to your session log immediately, before starting the next milestone. A replacement agent must be able to resume from your log alone.
8. **Manual gaps:** anything that needs the human (installs, hardware, logins, 3D assets, GUI-only clicks you can't automate) goes in `docs/MANUAL_TODO.md` under your track, with exact steps. Then skip it and continue; don't stop the run.
9. Never put secrets in files. Never publish or push without the orchestrator's instruction.
