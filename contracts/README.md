# Chetna Contracts (v0.2)

JSON Schema definitions and validation for Chetna game manifests, events, sessions, and programs.

## Schemas

- **game-manifest.schema.json**: Declares game metadata, input types, parameter schemas, events, and metrics.
- **event.schema.json**: Trial events (session_start, trial_end, grasp, etc.) with timestamps and outcomes.
- **kinematics-chunk.schema.json**: Columnar pose frames (~5s per chunk) with positions, rotations, and confidence.
- **session-envelope.schema.json**: Session metadata, calibration, device info, and block configurations.
- **program.schema.json**: Clinician prescriptions with ordered game blocks and scheduling.
- **sensor-file.schema.json** (v0.2): `sens_###.json`, columnar EMG envelope + IMU samples from the wearable nodes, listed in the session envelope (`sensor_chunks`) next to the kinematics chunks.
- **metrics.schema.json**: analytics output; v0.2 adds the optional `embodiment` object (Phantom Hand).
- **haptic-message.schema.json** / **haptic-device-command.schema.json**: haptic protocol v1.2 (two message families; stroke cue). See `HAPTIC_PROTOCOL.md`.
- **live-message.schema.json**: live protocol; v0.2 adds operator commands and `game_state` / `trace`. See `LIVE_PROTOCOL.md`.

**Version v0.2 (2026-10-07):** backward compatible. `session-envelope.contracts_version` accepts `"0.1"` and `"0.2"`; every v0.1 fixture and session still validates. New event types, commands and schemas are additive. Schema `$id`s moved to `contracts/v0.2/`.

## Fixtures

Test data for contract validation:

- `fixtures/valid/` — fixtures that must pass schema validation
- `fixtures/invalid/` — fixtures that must fail schema validation
- `fixtures/*.manifest.json` — game manifests for testing (orchard_reach, phantom_hand); `validate.py` checks each against the game-manifest schema
- `fixtures/sessions/*` — whole session directories (`phantom_hand_min` covers every Phantom Hand event, a kin chunk, a sens chunk and metrics with `embodiment`)
- fixture prefixes for `valid/` and `invalid/`: game-manifest, event, kinematics-chunk, session-envelope, program, metrics, live-message, haptic-message, haptic-device-command, sensor-file

## Validation

### Setup

```bash
# Create a virtual environment (optional but recommended)
python -m venv .venv
source .venv/bin/activate  # On Windows: .venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt
```

### Run all fixture validations

```bash
python validate.py
```

Expected output:
```
✓ valid/game-manifest.1.json
✓ valid/game-manifest.2.json
✓ invalid/game-manifest.1.json: correctly rejected
✗ invalid/game-manifest.2.json: should have failed validation but passed
...
✗ Some validations failed
```

Exit code: 0 if all tests pass, 1 if any test fails.

### Validate a session directory

```bash
python validate.py --session /path/to/session/dir
```

Validates:
- `session.json` against session-envelope schema
- `events.ndjson` (line by line) against event schema
- `kin_*.json` files against kinematics-chunk schema
- `sens_*.json` files against sensor-file schema (and that every array matches its `t_ms` length)
- `metrics.json` against the metrics schema

Example:
```bash
python validate.py --session ~/sessions/session_2026_09_14_001
```

## Adding fixtures

1. **Valid fixtures**: Place `.json` files in `valid/` following the naming pattern `<schema>.<n>.json`.
   They must pass schema validation.

2. **Invalid fixtures**: Place `.json` files in `invalid/` following the same naming pattern.
   They must fail schema validation (an error in validation is the expected result).

3. **Manifests**: Place game manifest `.manifest.json` files in `fixtures/`.
   These are used by programs to validate parameter blocks.

## Integration

The GitHub Actions workflow `.github/workflows/contracts.yml` runs validation on:
- Every push to `main`
- Every pull request

Validation must pass before merging.

## Contract change requests

If a schema needs modification, file an issue describing:
- Which schema and what needs to change
- Why (e.g., "missing field for feature X", "constraint is too strict")
- Impact on existing data

Schema changes are reviewed by the Chetna team before merge.
