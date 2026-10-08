"""Every profile's generated session must validate against contracts/schemas/*."""
import json
import pytest
import jsonschema
from jsonschema import Draft202012Validator, FormatChecker

from synthetic_patients.generate import write_session
from synthetic_patients.profiles import PROFILES
from opus_analytics.io_session import load_session
from opus_analytics.analyze import analyze_session
from opus_analytics.schemas import find_contracts_dir


@pytest.mark.parametrize("profile_name", list(PROFILES.keys()))
def test_generated_session_is_schema_valid(tmp_path, profile_name):
    out_dir = tmp_path / profile_name
    write_session(out_dir, profile_name, seed=123, params={"trialCount": 6})

    data = load_session(out_dir, validate=True)

    for fname, errors in data.validation_errors.items():
        assert errors == [], f"{fname} failed schema validation for profile {profile_name}: {errors}"
    assert data.is_valid


def test_longitudinal_week_session_is_schema_valid(tmp_path):
    from synthetic_patients.profiles import interpolated_profile
    profile = interpolated_profile(3)
    out_dir = tmp_path / "week_03"
    write_session(out_dir, "longitudinal", seed=555, params={"trialCount": 6}, profile_override=profile)
    data = load_session(out_dir, validate=True)
    assert data.is_valid


@pytest.mark.parametrize("profile_name", list(PROFILES.keys()))
def test_metrics_json_conforms_to_schema(tmp_path, profile_name):
    """Generated metrics.json must validate against metrics.schema.json."""
    out_dir = tmp_path / profile_name
    write_session(out_dir, profile_name, seed=123, params={"trialCount": 6})

    # Generate metrics
    metrics = analyze_session(out_dir, validate=True)

    # Load and validate against schema
    contracts_dir = find_contracts_dir()
    schema_path = contracts_dir / "schemas" / "metrics.schema.json"
    with open(schema_path, "r", encoding="utf-8") as f:
        schema = json.load(f)

    format_checker = FormatChecker()
    validator = Draft202012Validator(schema, format_checker=format_checker)
    errors = list(validator.iter_errors(metrics))

    assert errors == [], f"metrics.json for {profile_name} failed schema validation:\n" + \
                        "\n".join(f"  {e.message} at {'.'.join(str(p) for p in e.path)}" for e in errors)
