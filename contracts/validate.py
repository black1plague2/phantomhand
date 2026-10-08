#!/usr/bin/env python3
"""
Validate OPUS contract fixtures and session directories.

Validates:
- All fixtures in contracts/fixtures/{valid,invalid}/
- Manifest paramSchemas against program blocks
- Session directories with --session flag
"""

import json
import sys
import argparse
from pathlib import Path
from typing import Any, Dict, Tuple, List

import jsonschema
from jsonschema import Draft202012Validator, ValidationError, FormatChecker
import referencing
import referencing.jsonschema


def load_json(path: Path) -> Any:
    """Load and parse a JSON file."""
    with open(path, 'r', encoding='utf-8') as f:
        return json.load(f)


def load_schema(schema_name: str) -> Dict:
    """Load a schema from contracts/schemas/"""
    schema_path = Path(__file__).parent / "schemas" / f"{schema_name}.schema.json"
    return load_json(schema_path)


def load_manifest(manifest_name: str) -> Dict:
    """Load a manifest from contracts/fixtures/"""
    manifest_path = Path(__file__).parent / "fixtures" / f"{manifest_name}.manifest.json"
    return load_json(manifest_path)


def build_schema_registry(schemas_dir: Path) -> referencing.Registry:
    """Build a referencing.Registry from all schemas in schemas_dir."""
    resources = {}

    for schema_file in sorted(schemas_dir.glob("*.json")):
        schema_name = schema_file.stem
        with open(schema_file, 'r', encoding='utf-8') as f:
            schema = json.load(f)

        # Register under filename
        filename_uri = f"file:///{schema_name}.schema.json"
        resource = referencing.Resource.from_contents(schema)
        resources[filename_uri] = resource

        # Also register under $id if present
        if "$id" in schema:
            schema_id = schema["$id"]
            resources[schema_id] = resource

    return referencing.Registry().with_resources(resources.items())


def validate_against_schema(data: Any, schema: Dict, path: str, registry: referencing.Registry = None) -> Tuple[bool, str]:
    """
    Validate data against a JSON schema.
    Returns (is_valid, message).
    """
    try:
        format_checker = FormatChecker()
        if registry:
            validator = Draft202012Validator(schema, format_checker=format_checker, registry=registry)
        else:
            validator = Draft202012Validator(schema, format_checker=format_checker)
        validator.validate(data)
        return True, f"[PASS] {path}"
    except ValidationError as e:
        return False, f"[FAIL] {path}: {e.message}"


def validate_fixtures() -> Tuple[int, List[str]]:
    """
    Validate all fixtures in contracts/fixtures/{valid,invalid}/.

    - valid/*.json files must validate against their schema
    - invalid/*.json files must NOT validate (validation should fail)

    Returns (exit_code, messages).
    """
    messages = []
    fixtures_dir = Path(__file__).parent / "fixtures"
    schemas_dir = Path(__file__).parent / "schemas"

    valid_dir = fixtures_dir / "valid"
    invalid_dir = fixtures_dir / "invalid"

    # Map fixture prefixes to schema names
    schema_map = {
        "game-manifest": "game-manifest",
        "event": "event",
        "kinematics-chunk": "kinematics-chunk",
        "session-envelope": "session-envelope",
        "program": "program",
        "metrics": "metrics",
        "live-message": "live-message",
        "haptic-device-command": "haptic-device-command",
        "haptic-message": "haptic-message",
        "sensor-file": "sensor-file",
    }

    exit_code = 0

    # Build registry for schemas with $refs
    registry = build_schema_registry(schemas_dir)

    # Validate valid fixtures
    if valid_dir.exists():
        for fixture_file in sorted(valid_dir.glob("*.json")):
            fixture_name = fixture_file.stem
            # Extract schema name from fixture prefix
            schema_name = None
            for prefix, schema in schema_map.items():
                if fixture_name.startswith(prefix):
                    schema_name = schema
                    break

            if not schema_name:
                messages.append(f"[SKIP] {fixture_file.name}: unknown fixture type")
                continue

            schema = load_schema(schema_name)
            data = load_json(fixture_file)

            is_valid, msg = validate_against_schema(data, schema, f"valid/{fixture_file.name}", registry)
            messages.append(msg)

            if not is_valid:
                exit_code = 1

    # Every fixtures/*.manifest.json must be a valid game manifest
    manifest_schema = load_schema("game-manifest")
    for manifest_file in sorted(fixtures_dir.glob("*.manifest.json")):
        is_valid, msg = validate_against_schema(load_json(manifest_file), manifest_schema, f"{manifest_file.name}", registry)
        messages.append(msg)
        if not is_valid:
            exit_code = 1

    # Validate invalid fixtures (must fail)
    if invalid_dir.exists():
        for fixture_file in sorted(invalid_dir.glob("*.json")):
            fixture_name = fixture_file.stem
            # Extract schema name from fixture prefix
            schema_name = None
            for prefix, schema in schema_map.items():
                if fixture_name.startswith(prefix):
                    schema_name = schema
                    break

            if not schema_name:
                messages.append(f"[SKIP] {fixture_file.name}: unknown fixture type")
                continue

            schema = load_schema(schema_name)
            data = load_json(fixture_file)

            is_valid, msg = validate_against_schema(data, schema, f"invalid/{fixture_file.name}", registry)

            # For invalid fixtures, we expect validation to FAIL
            if is_valid:
                messages.append(f"[FAIL] invalid/{fixture_file.name}: should have failed validation but passed")
                exit_code = 1
            else:
                messages.append(f"[PASS] invalid/{fixture_file.name}: correctly rejected")

    return exit_code, messages


def validate_program_params(program_data: Dict, manifest_map: Dict[str, Dict]) -> Tuple[bool, str]:
    """
    Validate program.blocks[].params against the game's manifest paramSchema.

    Returns (is_valid, message).
    """
    try:
        for i, block in enumerate(program_data.get("blocks", [])):
            game_id = block.get("game_id")
            params = block.get("params", {})

            if game_id not in manifest_map:
                return False, f"Block {i}: game_id '{game_id}' not found in manifests"

            manifest = manifest_map[game_id]
            param_schema = manifest.get("paramSchema")

            if not param_schema:
                return False, f"Block {i}: game '{game_id}' has no paramSchema"

            # Validate params against paramSchema
            format_checker = FormatChecker()
            validator = Draft202012Validator(param_schema, format_checker=format_checker)
            validator.validate(params)

        return True, "[PASS] Program params valid"
    except ValidationError as e:
        return False, f"[FAIL] Program params: {e.message}"


def validate_session_directory(session_dir: Path) -> Tuple[int, List[str]]:
    """
    Validate a session directory.

    Expects:
    - session.json (validates against session-envelope schema)
    - events.ndjson (each line validates against event schema)
    - kin_*.json files (each validates against kinematics-chunk schema)
    - metrics.json (if present, validates against metrics schema)

    Returns (exit_code, messages).
    """
    messages = []
    exit_code = 0

    if not session_dir.is_dir():
        return 1, [f"[FAIL] {session_dir} is not a directory"]

    # Load schemas
    schemas_dir = Path(__file__).parent / "schemas"
    registry = build_schema_registry(schemas_dir)

    session_schema = load_schema("session-envelope")
    event_schema = load_schema("event")
    kin_schema = load_schema("kinematics-chunk")
    metrics_schema = load_schema("metrics")
    sens_schema = load_schema("sensor-file")

    # Validate session.json
    session_file = session_dir / "session.json"
    if session_file.exists():
        session_data = load_json(session_file)
        is_valid, msg = validate_against_schema(
            session_data, session_schema, f"{session_dir.name}/session.json", registry
        )
        messages.append(msg)
        if not is_valid:
            exit_code = 1
    else:
        messages.append(f"[WARN] {session_dir.name}/session.json not found")

    # Validate events.ndjson
    events_file = session_dir / "events.ndjson"
    if events_file.exists():
        with open(events_file, 'r', encoding='utf-8') as f:
            line_num = 0
            for line in f:
                line_num += 1
                line = line.strip()
                if not line:
                    continue

                try:
                    event_data = json.loads(line)
                    try:
                        format_checker = FormatChecker()
                        validator = Draft202012Validator(event_schema, format_checker=format_checker)
                        validator.validate(event_data)
                    except ValidationError as e:
                        messages.append(f"[FAIL] {session_dir.name}/events.ndjson:{line_num}: {e.message}")
                        exit_code = 1
                except json.JSONDecodeError as e:
                    messages.append(f"[FAIL] {session_dir.name}/events.ndjson:{line_num}: JSON parse error: {e}")
                    exit_code = 1

        if line_num > 0:
            messages.append(f"[PASS] {session_dir.name}/events.ndjson: {line_num} events validated")
    else:
        messages.append(f"[WARN] {session_dir.name}/events.ndjson not found")

    # Validate kin_*.json files
    kin_files = sorted(session_dir.glob("kin_*.json"))
    for kin_file in kin_files:
        kin_data = load_json(kin_file)
        is_valid, msg = validate_against_schema(
            kin_data, kin_schema, f"{session_dir.name}/{kin_file.name}", registry
        )
        messages.append(msg)
        if not is_valid:
            exit_code = 1

    if not kin_files:
        messages.append(f"[WARN] {session_dir.name}: no kin_*.json files found")

    # Validate sens_*.json files (v0.2, optional)
    for sens_file in sorted(session_dir.glob("sens_*.json")):
        sens_data = load_json(sens_file)
        is_valid, msg = validate_against_schema(
            sens_data, sens_schema, f"{session_dir.name}/{sens_file.name}", registry
        )
        messages.append(msg)
        if not is_valid:
            exit_code = 1
            continue
        # columnar streams: every array must be as long as its t_ms
        for stream, keys in (("emg_env", ["value", "motor_excl"]), ("imu", ["ax", "ay", "az", "gx", "gy", "gz"])):
            block = sens_data.get(stream)
            if not block:
                continue
            n = len(block["t_ms"])
            for k in keys:
                if k in block and len(block[k]) != n:
                    messages.append(f"[FAIL] {session_dir.name}/{sens_file.name}: {stream}.{k} has {len(block[k])} entries, t_ms has {n}")
                    exit_code = 1

    # Validate metrics.json if present
    metrics_file = session_dir / "metrics.json"
    if metrics_file.exists():
        metrics_data = load_json(metrics_file)
        is_valid, msg = validate_against_schema(
            metrics_data, metrics_schema, f"{session_dir.name}/metrics.json", registry
        )
        messages.append(msg)
        if not is_valid:
            exit_code = 1
    else:
        messages.append(f"[WARN] {session_dir.name}/metrics.json not found")

    return exit_code, messages


def main():
    parser = argparse.ArgumentParser(
        description="Validate OPUS contracts fixtures and session directories"
    )
    parser.add_argument(
        "--session",
        type=Path,
        help="Validate a session directory instead of all fixtures"
    )

    args = parser.parse_args()

    all_messages = []
    exit_code = 0

    if args.session:
        # Validate a single session directory
        session_exit_code, session_messages = validate_session_directory(args.session)
        all_messages.extend(session_messages)
        exit_code = session_exit_code
    else:
        # Validate all fixtures
        fixture_exit_code, fixture_messages = validate_fixtures()
        all_messages.extend(fixture_messages)
        exit_code = fixture_exit_code

    # Print results (with UTF-8 encoding for Windows compatibility)
    import io
    if sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

    for msg in all_messages:
        print(msg)

    if exit_code == 0:
        print("\n[PASS] All validations passed")
    else:
        print("\n[FAIL] Some validations failed")

    sys.exit(exit_code)


if __name__ == "__main__":
    main()
