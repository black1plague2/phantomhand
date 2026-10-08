"""Locate and load the read-only JSON Schemas under contracts/schemas/, and validate against them.

contracts/schemas is never edited by this package (see docs/agent-briefs/_COMMON.md rule 2).
"""
from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

import jsonschema


def find_contracts_dir(start: Path | None = None) -> Path:
    """Walk upward from `start` (default: this file) looking for a `contracts/schemas` dir."""
    here = Path(start or __file__).resolve()
    for parent in [here] + list(here.parents):
        candidate = parent / "contracts" / "schemas"
        if candidate.is_dir():
            return parent / "contracts"
    raise FileNotFoundError("Could not locate contracts/schemas/ above " + str(here))


@lru_cache(maxsize=None)
def _schema(name: str) -> dict:
    contracts = find_contracts_dir()
    with open(contracts / "schemas" / name, "r", encoding="utf-8") as f:
        return json.load(f)


def session_envelope_schema() -> dict:
    return _schema("session-envelope.schema.json")


def event_schema() -> dict:
    return _schema("event.schema.json")


def kinematics_chunk_schema() -> dict:
    return _schema("kinematics-chunk.schema.json")


def sensor_file_schema() -> dict:
    return _schema("sensor-file.schema.json")


def game_manifest_schema() -> dict:
    return _schema("game-manifest.schema.json")


def validate_instance(instance: dict, schema: dict) -> list[str]:
    """Returns a list of human-readable validation error strings (empty = valid)."""
    validator_cls = jsonschema.validators.validator_for(schema)
    validator_cls.check_schema(schema)
    validator = validator_cls(schema)
    errors = sorted(validator.iter_errors(instance), key=lambda e: list(e.path))
    return [f"{'.'.join(str(p) for p in e.path) or '<root>'}: {e.message}" for e in errors]
