"""Wrist/palm `rot` (requested by Opus/Unity for the demo hand mesh): every recorded frame must
carry a unit-norm xyzw quaternion, deterministically reproducible from the same seed, and must
validate against kinematics-chunk.schema.json (which allows an optional `rot` per joint)."""
from __future__ import annotations

import numpy as np
import pytest

from synthetic_patients.generate import write_session
from synthetic_patients.orientation import home_quat, quat_normalize, synth_wrist_quats
from synthetic_patients.profiles import PROFILES
from opus_analytics.io_session import load_session

ROT_JOINTS = ["l_wrist", "r_wrist", "l_palm", "r_palm"]


@pytest.mark.parametrize("profile_name", list(PROFILES.keys()))
def test_every_wrist_and_palm_frame_has_unit_quaternion(tmp_path, profile_name):
    out_dir = tmp_path / profile_name
    write_session(out_dir, profile_name, seed=17, params={"trialCount": 6})

    for kin_file in sorted(out_dir.glob("kin_*.json")):
        import json
        chunk = json.load(open(kin_file, encoding="utf-8"))
        for joint in ROT_JOINTS:
            frame = chunk["frames"][joint]
            assert "rot" in frame, f"{joint} missing rot in {kin_file.name}"
            rot = np.asarray(frame["rot"])
            assert rot.shape[1] == 4
            norms = np.linalg.norm(rot, axis=1)
            assert np.allclose(norms, 1.0, atol=1e-3), f"{joint} has non-unit quaternion(s) in {kin_file.name}"
        # head and fingertips are position-only, unchanged by this feature
        assert "rot" not in chunk["frames"]["head"]


def test_wrist_rot_is_schema_valid_via_load_session(tmp_path):
    out_dir = tmp_path / "moderate"
    write_session(out_dir, "moderate", seed=21, params={"trialCount": 5})
    data = load_session(out_dir, validate=True)
    assert data.validation_errors["kin_chunks"] == []


def test_deterministic_by_seed(tmp_path):
    a = tmp_path / "a"
    b = tmp_path / "b"
    write_session(a, "mild", seed=99, params={"trialCount": 4})
    write_session(b, "mild", seed=99, params={"trialCount": 4})
    import json
    ca = json.load(open(a / "kin_000.json", encoding="utf-8"))
    cb = json.load(open(b / "kin_000.json", encoding="utf-8"))
    assert ca["frames"]["r_wrist"]["rot"] == cb["frames"]["r_wrist"]["rot"]


def test_home_quat_is_unit_norm():
    q = home_quat()
    assert np.linalg.norm(q) == pytest.approx(1.0, abs=1e-9)


def test_synth_wrist_quats_stationary_pose_is_constant_and_unit_norm():
    """Zero velocity + constant aperture + no tremor -> every frame is the same unit quaternion
    (direction defaults to home orientation; a constant aperture is a degenerate 0/0 case for
    the pronation-twist normalization, but the result must still be one fixed, valid quat)."""
    pos = np.tile(np.array([0.1, 0.2, 0.3]), (10, 1))
    aperture = np.full(10, 0.08)
    quats = synth_wrist_quats(pos, aperture, trial_index=0, tremor_hz_range=(0.0, 0.0),
                               tremor_amp_m=0.0, dt=1 / 72.0)
    norms = np.linalg.norm(quats, axis=1)
    assert np.allclose(norms, 1.0, atol=1e-9)
    for q in quats[1:]:
        assert np.allclose(q, quats[0], atol=1e-9)
