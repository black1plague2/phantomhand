# Phantom Hand — handoff for the next Claude session

This repo (`black1plague2/phantomhand`) is the **Phantom Hand** build (Chetna PRD v2, Team Kela, Vedanta
Makeathon 7–9 Oct 2026), split out of `black1plague2/chetna` on 2026-10-08 by user decision:
**chetna = Orchard Reach only; phantomhand = Phantom Hand.** History starts fresh here; the full history
(every run, every review) is in chetna up to commit `5c0b12d`.

## Read in this order
1. `CLAUDE.md` (hard rules; the Unity section's "user override" line is binding).
2. `CONTEXT.md` — the "★★★★★ PHANTOM HAND" box at the top is the current state.
3. `docs/PH_STATUS.md` — every prompt, status, commit, log, test counts, the Next list.
4. `docs/agent-briefs/ph/` — PRD_v2.md (product truth, incl. §5.1 additions), 01-ORCHESTRATION.md (Opus manages,
   Sonnet builds, Haiku writes docs; ≤ 3 builders; one Unity driver), 02-RULES.md, 03-SPEC.md (dated decision lines
   v1 → v3.1 at the top, D1–D10, §12 additions), 04-E2E.md (gates G1–G3, L3/L4 tables), prompts/*.md.
5. `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` — the electronics team's real firmware dialect + the consistency table.
6. Newest logs: `logs/sessions/2026-10-07-PH-*` and `2026-10-08-PH-*` (each ends with an "O2 review" decision).

## How the user wants it run
- Delegate to parallel builder subagents (`.claude/agents/ph-*.md`), disjoint file ownership, real test output in
  every log, Opus reviews and commits. The user asked for as much parallelism as possible.
- **Unity: the OPEN editor through the Unity MCP (`meta-xr-unity-runtime`), never batch mode, one Unity driver.**
  After any editor restart, the Claude session must reconnect the MCP (`/mcp` → Reconnect, or restart the session);
  adding the server again doesn't help. Without the MCP, builders can still write code and check compiles in
  `%LOCALAPPDATA%\Unity\Editor\Editor.log` (`error CS`).
- Electronics/firmware belong to a separate team. We own Unity, the app, contracts, simulator, docs.
- The Android phone runs the Flutter app as the **hub** (`adb -s 164cd676 forward tcp:8787 tcp:8787`); restore the
  forward after any test that removes it.
- Ask the user (Rudra) about real design doubts; pick sensible defaults for the rest.

## State at the split (2026-10-08 ~08:00 IST)
Verified (tests run): contracts v0.2; SDK stroke/discovery/sensor client; Phantom Hand logic + `PhantomHand.unity`
scene; virtual arm, brush, stone (EditMode 290/290, PlayMode 20/20 at U3); Flutter live operator card on the phone
(303 tests); embodiment analytics (83 tests); sleeve twin + E2E harness (sim L3 7/7, faults pass, 114 tests).
**Provisional (compiles, never run — the MCP was down):** U4 in-headset UI, U5 composition/live link/bootstrap,
3D-model wiring. Their logs list the exact builder hook lines still to wire.

## Do next, in order

> This list is the state at the split (8 Oct, 08:00). Items 3 to 5 have been done since (rigged hand and props in the game,
> SDK changes for the real firmware, the L3 table green in simulation, Quest APKs built but never run on a headset).
> The current state and the current next steps are in `docs/PH_STATUS.md`; what only a human can do is in `docs/MANUAL_TODO.md`.
1. **Reconnect the Unity MCP**, then one Unity driver runs ALL EditMode + PlayMode tests (PhantomHand + Shell)
   and fixes failures; capture U4 screenshots (`CaptureU4Shots`).
2. **Remove Orchard Reach from this repo** (user: "except Orchard Reach"). It is still here only because the shared
   shell compiles against it. Delete `game/Assets/Games/OrchardReach/**`, `game/Assets/Scenes/OrchardReach.unity`,
   `game/Assets/Shell/Runtime/{OrchardReachSceneController,BasketTrigger,FruitMarker,TrunkLeanVignette?}.cs`,
   `game/Assets/Shell/Editor/{OrchardSceneDressingTool,LodSliverDiagnosticTool}.cs`, Orchard-only Shell tests
   (RigValidator/SeatedLayout/FullPipeline/Haptic/Lean/Sliver), the Orchard manifests under `contracts/fixtures`
   and `app/assets/fixtures/manifests/` (keep the app's dynamic form generic), and the Orchard branches in
   `OpusSessionRunner` / `ISessionHost` / `BootstrapLoader` / `OpusBuildScript` / `OpusHud`. Keep the SDK, the shell
   pieces Phantom Hand uses, the app, analytics (Orchard metrics code may stay as the generic kinematics engine).
   Verify with a clean compile + all remaining tests, then commit "remove Orchard Reach".
3. Wire the builder hooks (UI installer, PhantomHandSceneController, model importer + table, bootstrap scene) and
   switch the arm to the **rigged skin hand** `game/Assets/Art/PhantomHand/Models/RiggedHand/handRig_02.fbx`
   (`hand.R`, 68 bones; drop its camera + light). The gloves (MetaAssets 324213, 1571125) are fallbacks only.
   Licence of the rigged hand: unknown — ask the user before any public demo build.
4. SDK changes for the real firmware (`PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` §B): accept `accepted` acks, send
   `{"type":"keepalive"}`, display `mode` + `text`, find Node A by `device_kind` (id `CHETNA_HAPTIC_001`); plus U5's
   open issues: LiveClient hub port, UDP receive loop must survive WSAECONNRESET 10054, re-schedule strokes on resume.
5. L3 with Unity (`tools/demo/run_pipeline.py --game phantom_hand --sim` without `--no-unity`) → gate G2 → L4 on
   real nodes once the electronics team has Wi-Fi working → APK (U6) → demo mode → additions A1–A3 (PRD §5.1).

## Security
The Unity MCP bridge token was blanked in `game/Assets/Resources/DevAgentSettings.asset` for this public repo.
Never commit tokens, Wi-Fi passwords or `~/.claude.json` contents. This repo is **public**: check the licences of the
Meta asset-library models and the rigged hand before keeping them here.
