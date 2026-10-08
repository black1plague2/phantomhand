# Session 2026-09-14: Analytics Output Schema Conformance

## Task
Make `analytics/opus_analytics` output `metrics.json` conform to `contracts/schemas/metrics.schema.json` through mechanical shape changes only, without modifying metric computation logic.

## Changes Made

### 1. Core Analytics Changes (`analyze.py`)
- **Field Renames:**
  - `generated_at` → `computed_at` (ISO date-time format preserved)
  - `opus_analytics_version` → `analytics_version`
- **Trial Structure:**
  - Added `block` field (extracted from trial.block via segmentation)
  - Added `trial` field (trial number within session, indexed from 0)
  - Added `t_start_ms` and `t_end_ms` fields (from trial.t_trial_start/t_trial_end)
  - Kept existing fields: `hand`, `outcome`, `target`, `metrics`
- **Session Structure:**
  - Wrapped session metrics in `{"metrics": {...}}` structure
  - Preserved extra top-level keys: `sample_rate_hz`, `validation`

**File:** `/analytics/opus_analytics/analyze.py`

### 2. Unit String Normalization
Updated all metric definitions to use `"unitless"` instead of `""` for dimensionless values:

**trial_metrics.py:**
- `reaction_time_ms`, `movement_time_ms`, `peak_speed_mps`, `time_to_peak_speed_pct`: unitless early returns
- `sparc`, `ldlj`: unitless
- `path_length_ratio`: unitless

**session_metrics.py:**
- `success_rate`: unitless
- `neglect_index`: unitless

**Files Modified:** 
- `/analytics/opus_analytics/metrics/trial_metrics.py`
- `/analytics/opus_analytics/metrics/session_metrics.py`

### 3. Documentation Updates
Updated metrics.json shape documentation in README to reflect new format with example showing:
- `computed_at` and `analytics_version` fields
- `block`, `trial`, `t_start_ms`, `t_end_ms` in trials
- `session.metrics` wrapping
- Use of `unitless` unit string

**File:** `/analytics/README.md`

### 4. Test Coverage
Added pytest test to validate generated metrics.json against the schema:
- `test_metrics_json_conforms_to_schema` parametrized over all profiles
- Uses `Draft202012Validator` from jsonschema library
- Validates structure matches `metrics.schema.json` exactly

**File:** `/analytics/tests/test_schema_validity.py`

### 5. Test Fixture Updates
- Updated `test_quality_dropout.py` to access session metrics via new nested path: `result["session"]["metrics"]["tracking_loss_pct"]["value"]` (was flat before)

**File:** `/analytics/tests/test_quality_dropout.py`

### 6. Fixture Regeneration
Regenerated all `metrics.json` files in `contracts/fixtures/sessions/` using CLI:
```
.venv\Scripts\python.exe -m opus_analytics <session_dir> --quiet
```

Regenerated for all profile sessions:
- `healthy`, `mild`, `moderate`, `severe`, `left_neglect`, `noisy_tracking`
- `longitudinal/week_00-06/session_0` (7 weeks)

All fixtures now conform to the new schema with:
- Proper field names and structure
- `unitless` units for dimensionless metrics
- Block and trial numbering
- Timing information (t_start_ms, t_end_ms)

### 7. Contract Validation Extension
Extended `contracts/validate.py` to:
- Load and validate `metrics.schema.json`
- Add `--session <dir>` support for validating metrics.json in session directories
- Add metrics fixtures (valid/invalid) to schema_map for fixture validation
- Check metrics.json presence and validity when validating sessions

**File:** `/contracts/validate.py`

### 8. Test Fixtures
Created fixture files for schema validation:

**Valid fixture:** `/contracts/fixtures/valid/metrics.1.json`
- Minimal but complete valid metrics.json
- All required fields present
- Proper nesting and units

**Invalid fixture:** `/contracts/fixtures/invalid/metrics.1.json`
- Missing `analytics_version` (required)
- Missing `session.metrics` key (required structure)
- Only `session.by_side` present (insufficient)
- Correctly rejected by schema validation

## Test Results

### 1. Analytics Tests (28 tests)
```
28 passed in 25.78s
```
All existing tests plus new metrics schema validation tests pass.

### 2. Fixture Validation (24 fixtures)
```
[PASS] All validations passed
```
All valid fixtures pass, all invalid fixtures correctly rejected, including new metrics fixtures.

### 3. Session Directory Validation (13 sessions)
```
healthy: [PASS] All validations passed
left_neglect: [PASS] All validations passed
mild: [PASS] All validations passed
moderate: [PASS] All validations passed
severe: [PASS] All validations passed
noisy_tracking: [PASS] All validations passed
session_0 (week_00): [PASS] All validations passed
session_0 (week_01): [PASS] All validations passed
session_0 (week_02): [PASS] All validations passed
session_0 (week_03): [PASS] All validations passed
session_0 (week_04): [PASS] All validations passed
session_0 (week_05): [PASS] All validations passed
session_0 (week_06): [PASS] All validations passed
```

All fixture session directories validate successfully with regenerated metrics.json.

## Verification Output

### 1. Analytics pytest
```bash
$ cd analytics; .\.venv\Scripts\python.exe -m pytest -q
............................
28 passed in 25.78s
```

### 2. Contracts Fixture Validation
```bash
$ cd contracts; ..\analytics\.venv\Scripts\python.exe validate.py
[PASS] valid/event.1.json
[PASS] valid/event.2.json
[PASS] valid/game-manifest.1.json
[PASS] valid/game-manifest.2.json
[PASS] valid/kinematics-chunk.1.json
[PASS] valid/kinematics-chunk.2.json
[PASS] valid/metrics.1.json
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
[PASS] invalid/metrics.1.json: correctly rejected
[PASS] invalid/program.1.json: correctly rejected
[PASS] invalid/program.2.json: correctly rejected
[PASS] invalid/session-envelope.1.json: correctly rejected
[PASS] invalid/session-envelope.2.json: correctly rejected

[PASS] All validations passed
```

### 3. Session Directory Validation Loop
All 13 fixture session directories (6 profiles + 7 longitudinal weeks) pass with `validate.py --session <dir>`.

## Files Changed

### Modified
1. `/analytics/opus_analytics/analyze.py` - Schema-conformant output generation
2. `/analytics/opus_analytics/metrics/trial_metrics.py` - Unit string normalization
3. `/analytics/opus_analytics/metrics/session_metrics.py` - Unit string normalization
4. `/analytics/README.md` - Documentation update
5. `/analytics/tests/test_schema_validity.py` - Added metrics validation test
6. `/analytics/tests/test_quality_dropout.py` - Updated nested path access
7. `/contracts/validate.py` - Extended for metrics.json validation

### Created
1. `/contracts/fixtures/valid/metrics.1.json` - Valid test fixture
2. `/contracts/fixtures/invalid/metrics.1.json` - Invalid test fixture

### Regenerated (all now conformant)
- 13 metrics.json files in `/contracts/fixtures/sessions/` directories
  - 6 profile sessions (healthy, mild, moderate, severe, left_neglect, noisy_tracking)
  - 7 longitudinal week sessions (week_00-06)

## Notes

- **Metric Computation:** No changes to any metric algorithms or calculations; purely output shape changes
- **Backward Compatibility:** metrics.json output format changed; existing parsers must be updated
- **Schema Validation:** All output now validates against `contracts/schemas/metrics.schema.json` using Draft202012Validator
- **No sim/ Changes:** Verified no files in sim/ directory read metrics.json, so no updates needed there

## Proposed CHANGELOG Entry

```
### Changed
- Analytics output metrics.json now conforms to metrics.schema.json:
  - Renamed `generated_at` → `computed_at`, `opus_analytics_version` → `analytics_version`
  - Added `block`, `trial`, `t_start_ms`, `t_end_ms` to trial objects
  - Wrapped session metrics in `{"metrics": {...}}` structure  
  - Changed empty unit strings to `"unitless"` for dimensionless values
- All fixture metrics.json files regenerated with new schema-conformant format
- Added pytest validation for metrics.json schema conformance
- Extended contracts/validate.py to validate metrics.json in session directories
```

## Next Steps

1. Opus reviews and commits these changes
2. Any parsers of metrics.json in downstream systems should be updated for new schema
3. Consider backward compatibility migration strategy if needed
