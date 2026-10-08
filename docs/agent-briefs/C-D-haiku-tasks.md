# Brief C + D: Contracts tooling, docs & logs · model: Haiku

> **Status (Opus, 2026-09-19): C1 and C2 are DONE** (10 schemas, fixtures both valid and invalid,
> `contracts/validate.py` green including `--session` mode, CI workflow in place). **C3 (codegen) was never
> built** — Dart and C# models are hand-written on both sides and kept in sync by review, which is a real
> risk every time a schema changes. **D1 is partly done** (CHANGELOG is maintained; agents must not edit it
> themselves). **D2 is done.** **D3 (`docs/STATUS.html`) was never built.**

## C: Contracts tooling
- **C1** For each schema in `contracts/schemas`, write 2 valid and 2 invalid fixtures in `contracts/fixtures/{valid,invalid}/`. Add `program.orchard.json` using `orchard_reach.manifest.json` presets.
- **C2** `contracts/validate.py`: validates every fixture (valid ones must pass, invalid must fail); also validates `program.blocks[].params` against the game's manifest `paramSchema`; `--session <dir>` mode validates a session directory. Add `.github/workflows/contracts.yml`.
- **C3** `contracts/codegen/`: script generating Dart freezed models (`quicktype` or hand template) into `app/lib/data/dto/generated/` and C# POCOs into `game/Packages/com.opus.sdk/Runtime/Contracts/Generated/`. Document the command in `contracts/README.md`.

## D: Docs & logs (continuous)
- **D1** After each merged task: CHANGELOG line, VERSIONS entry if tagged, and a proposed CONTEXT §2/§3 diff in the session log for Opus to approve.
- **D2** Research harvest: r/physicaltherapy, r/OccupationalTherapy, r/stroke, r/OculusQuest, r/Unity3D, Quora "VR physiotherapy", X #VRrehab. Extract clinician/patient pain points and feature asks as short paraphrases with links (no long quotes). Append to `docs/RESEARCH.md §4`.
- **D3** `docs/STATUS.html`: a single static page that reads a small embedded JSON (tracks, tasks, versions) and shows progress for the user. Refresh it when the task board changes.
