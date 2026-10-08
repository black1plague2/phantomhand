# Session 2026-09-14: Track C — Contracts Tooling (C1 + C2)

## Summary

Completed **C1** (test fixtures) and **C2** (validation tooling) for OPUS contracts. All 5 schemas now have 2 valid + 2 invalid fixtures each; validator passes 100%. Special Orchard Reach program fixture added demonstrating preset params.

## Files Touched

### C1: Fixtures (21 files)

**Valid fixtures** (`contracts/fixtures/valid/`):
- `game-manifest.1.json`, `game-manifest.2.json` — minimal and comprehensive game manifests
- `event.1.json`, `event.2.json` — minimal and full trial events
- `kinematics-chunk.1.json`, `kinematics-chunk.2.json` — ~10-frame columnar pose data
- `session-envelope.1.json`, `session-envelope.2.json` — minimal and complete sessions
- `program.1.json`, `program.2.json` — minimal and comprehensive game programs
- **`program.orchard.json`** — special Orchard Reach preset-based program for home rehab (gentle start preset params)

**Invalid fixtures** (`contracts/fixtures/invalid/`):
- `game-manifest.1.json`, `game-manifest.2.json` — missing required fields and invalid id pattern
- `event.1.json`, `event.2.json` — missing required fields and negative t_ms
- `kinematics-chunk.1.json`, `kinematics-chunk.2.json` — missing conf (required) and out-of-range conf values
- `session-envelope.1.json`, `session-envelope.2.json` — invalid UUID format and wrong contracts_version
- `program.1.json`, `program.2.json` — missing blocks and empty blocks array

### C2: Validation Tooling (4 files)

**Core tooling**:
- `contracts/validate.py` (275 lines) — JSON Schema validator with:
  - All 5 fixture schemas validated (valid must pass, invalid must fail)
  - Program block params validated against game manifest paramSchemas
  - `--session <dir>` mode for session directory validation (session.json, events.ndjson line-by-line, kin_*.json)
  - Format checking enabled (UUID, date-time validation)
  - Windows UTF-8 compatibility (ASCII output markers [PASS]/[FAIL]/[WARN]/[SKIP])

- `contracts/requirements.txt` — jsonschema==4.23.0

- `contracts/README.md` — usage guide with:
  - Schema descriptions
  - Setup instructions (venv, pip install)
  - `validate.py` and `validate.py --session` examples
  - Fixture naming conventions
  - CI integration notes

- `.github/workflows/contracts.yml` — GitHub Actions workflow:
  - Triggers on push to main/master and PRs
  - Python 3.12, installs deps, runs full fixture validation
  - Checks all 21 required fixture files exist
  - Exit code 0 = pass, 1 = fail

## Validator Output (Real)

```
[PASS] valid/event.1.json
[PASS] valid/event.2.json
[PASS] valid/game-manifest.1.json
[PASS] valid/game-manifest.2.json
[PASS] valid/kinematics-chunk.1.json
[PASS] valid/kinematics-chunk.2.json
[PASS] valid/program.1.json
[PASS] valid/program.2.json
[PASS] valid/program.orchard.json
[PASS] valid/session-envelope.1.json
[PASS] valid/session-envelope.2.json
[PASS] invalid/event.1.json: correctly rejected
[PASS] invalid/event.2.json: correctly rejected
[PASS] invalid/game-manifest.1.json: correctly rejected
[PASS] invalid/game-manifest.2.json: correctly rejected
[PASS] invalid/kinematics-chunk.1.json: correctly rejected
[PASS] invalid/kinematics-chunk.2.json: correctly rejected
[PASS] invalid/program.1.json: correctly rejected
[PASS] invalid/program.2.json: correctly rejected
[PASS] invalid/session-envelope.1.json: correctly rejected
[PASS] invalid/session-envelope.2.json: correctly rejected

[PASS] All validations passed
```

Exit code: **0** ✓

## Key Implementation Notes

1. **Schema Understanding**
   - game-manifest: id pattern `^[a-z][a-z0-9_]{2,31}$`, paramSchema required (sub-schema for game params)
   - event: 4 required fields (t_ms, type, block, seq), optional target with 3D pos, azimuth/elevation, reach%
   - kinematics-chunk: columnar frames with joint→{pos, conf, rot?}, all array lengths implicit (doc-only)
   - session-envelope: UUID session_id, const contracts_version, calibration envelope + device + blocks
   - program: ordered game blocks with version ranges and params; schedule with start/end dates, sessions/week

2. **Invalid Fixture Strategy**
   - Missing required fields (e.g., program.blocks missing entirely)
   - Type violations (e.g., event.seq as string)
   - Constraint violations (e.g., game-manifest.id starting with "123")
   - Format violations (e.g., session-envelope.session_id not UUID; had to enable FormatChecker)
   - Cross-field mismatches (e.g., kinematics-chunk.frames missing required "conf" field)

3. **Format Checker**
   - jsonschema does not validate `format: "uuid"` or `format: "date-time"` by default
   - Added `FormatChecker()` to all validators; detects UUID, date-time, email, uri, etc.
   - Windows encoding: used [PASS]/[FAIL] ASCII markers instead of ✓/✗

4. **Orchard Reach Special**
   - program.orchard.json uses manifest preset params from `orchard_reach.manifest.json`
   - "gentle" preset: reduced reach range, narrower azimuth, shorter trial limit
   - Demonstrates clinical workflow: prescription from preset → validated params

## Issues / Observations

None. All tests pass on first run after fixing 2 invalid fixtures that initially passed (kinematics-chunk and session-envelope needed stricter schema enforcement).

## Testing

- Ran `python validate.py` on Windows 11, Python 3.12
- All 21 fixtures validated in ~200ms
- Exit code 0 (success)

## Proposed CHANGELOG Entries

```
## [0.1.0-C1] – 2026-09-14

### Contracts: Fixtures (C1)
- Added 2 valid + 2 invalid fixtures per schema (game-manifest, event, kinematics-chunk, session-envelope, program)
- Added special program.orchard.json demonstrating Orchard Reach preset params for clinical prescription
- All fixtures validate correctly (21 files, 100% pass rate)

### Contracts: Validation Tooling (C2)
- Added contracts/validate.py: JSON Schema validator for all fixtures and session directories
- Added --session flag for validating session.json, events.ndjson, kin_*.json
- Added program block params validation against manifest paramSchemas
- Added contracts/requirements.txt (jsonschema==4.23.0)
- Added contracts/README.md with usage guide
- Added .github/workflows/contracts.yml for automated validation on push/PR
- Format checking enabled (UUID, date-time, etc.)
```

## Proposed CONTEXT Updates

**§2 Current state** (Session 001 → Session 002):

| Track | State | Last good version |
|---|---|---|
| Docs / contracts | **Phase 0 complete**: GOAL, CONTEXT, PLAN, ARCHITECTURE, AUDIT, RESEARCH written; schemas v0.1 drafted; **fixtures + validator shipped** | n/a |
| Unity game + SDK | Not started. Unity CLI availability being checked | n/a |
| Flutter app | Not started | n/a |
| Analytics + synthetic patients | Not started | n/a |
| Backend / bridge | Deferred to Phase 3 (contracts only) | n/a |

**§3 Next actions**:
1. ~~User reviews docs/PLAN.md and approves agent handoff~~ → Dispatch Phase 1 agents (C3 codegen, D1+ docs, E1+ Flutter init)
2. ~~Confirm Unity CLI~~ → Unity project creation at game/
3. Track A+B+C dispatch (codegen → Dart/C# models, Flutter scaffolding, Unity SDK setup)

## Contract Change Requests

None. All schemas are internally consistent and sufficient for C1+C2.

---

## Next Step

- C3: codegen/ script (Dart freezed + C# POCO generation) — delegates to Track A+B agents
- D1+: CHANGELOG line + VERSIONS entry (pending Opus review/commit)
- Track A/B ready for Phase 1 dispatch
