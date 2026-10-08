"""Load a session directory (session.json, events.ndjson, kin_###.json) into structured data,
validating each file against contracts/schemas/*.
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

from . import schemas


@dataclass
class SessionData:
    session_dir: Path
    envelope: dict
    events: list[dict]
    joints: dict  # joint name -> {"t_ms": np.ndarray, "pos": (N,3) array, "conf": (N,) array}
    rate_hz: float
    validation_errors: dict = field(default_factory=dict)  # file -> [errors]
    # v0.2 Phantom Hand sensor streams from sens_###.json (None when the session has none):
    #   emg: {"device_id","rate_hz","t_ms","value","motor_excl"(bool array or None)}
    #   imu: {"device_id","rate_hz","t_ms","ax","ay","az","gx","gy","gz"}
    emg: dict | None = None
    imu: dict | None = None

    @property
    def session_id(self) -> str:
        return self.envelope["session_id"]

    @property
    def is_valid(self) -> bool:
        return all(len(v) == 0 for v in self.validation_errors.values())


def _sorted_chunk_files(session_dir: Path) -> list[Path]:
    files = sorted(session_dir.glob("kin_*.json"), key=lambda p: p.name)
    return files


def load_session(session_dir: str | Path, validate: bool = True) -> SessionData:
    session_dir = Path(session_dir)
    validation_errors: dict[str, list[str]] = {}

    with open(session_dir / "session.json", "r", encoding="utf-8") as f:
        envelope = json.load(f)
    if validate:
        validation_errors["session.json"] = schemas.validate_instance(envelope, schemas.session_envelope_schema())

    events: list[dict] = []
    event_errors: list[str] = []
    events_path = session_dir / "events.ndjson"
    if events_path.exists():
        with open(events_path, "r", encoding="utf-8") as f:
            for i, line in enumerate(f):
                line = line.strip()
                if not line:
                    continue
                ev = json.loads(line)
                events.append(ev)
                if validate:
                    errs = schemas.validate_instance(ev, schemas.event_schema())
                    event_errors.extend(f"line {i}: {e}" for e in errs)
    if validate:
        validation_errors["events.ndjson"] = event_errors

    chunk_files = _sorted_chunk_files(session_dir)
    chunk_errors: list[str] = []
    per_chunk = []
    for cf in chunk_files:
        with open(cf, "r", encoding="utf-8") as f:
            chunk = json.load(f)
        if validate:
            errs = schemas.validate_instance(chunk, schemas.kinematics_chunk_schema())
            chunk_errors.extend(f"{cf.name}: {e}" for e in errs)
        per_chunk.append(chunk)
    if validate:
        validation_errors["kin_chunks"] = chunk_errors

    rate_hz = per_chunk[0]["rate_hz"] if per_chunk else float(envelope.get("device", {}).get("tracking_rate_hz", 72.0))
    all_joints = sorted({j for c in per_chunk for j in c.get("joints", [])})

    joints: dict[str, dict] = {}
    for joint in all_joints:
        t_parts, pos_parts, conf_parts = [], [], []
        for c in per_chunk:
            if joint not in c["frames"]:
                continue
            t_parts.append(np.asarray(c["t_ms"], dtype=float))
            pos_parts.append(np.asarray(c["frames"][joint]["pos"], dtype=float))
            conf_parts.append(np.asarray(c["frames"][joint]["conf"], dtype=float))
        if not t_parts:
            continue
        joints[joint] = {
            "t_ms": np.concatenate(t_parts),
            "pos": np.concatenate(pos_parts, axis=0),
            "conf": np.concatenate(conf_parts),
        }

    emg, imu = _load_sensor_files(session_dir, validate, validation_errors)

    return SessionData(
        session_dir=session_dir, envelope=envelope, events=events,
        joints=joints, rate_hz=float(rate_hz), validation_errors=validation_errors,
        emg=emg, imu=imu,
    )


def _stream_rate(t: np.ndarray, declared) -> float | None:
    if declared:
        return float(declared)
    if len(t) > 2:
        dt = float(np.median(np.diff(t)))
        if dt > 0:
            return 1000.0 / dt
    return None


def _load_sensor_files(session_dir: Path, validate: bool, validation_errors: dict):
    """Read sens_###.json (EMG envelope + IMU) into one time-sorted array set per stream."""
    files = sorted(session_dir.glob("sens_*.json"), key=lambda p: p.name)
    if not files:
        return None, None
    errors: list[str] = []
    emg_parts: list[dict] = []
    imu_parts: list[dict] = []
    for sf in files:
        with open(sf, "r", encoding="utf-8") as f:
            chunk = json.load(f)
        if validate:
            errors.extend(f"{sf.name}: {e}" for e in schemas.validate_instance(chunk, schemas.sensor_file_schema()))
        if "emg_env" in chunk:
            emg_parts.append(chunk["emg_env"])
        if "imu" in chunk:
            imu_parts.append(chunk["imu"])
    if validate:
        validation_errors["sens_chunks"] = errors

    def _merge(parts, keys):
        if not parts:
            return None
        out = {"device_id": parts[0].get("device_id"), "rate_hz": None}
        t = np.concatenate([np.asarray(p["t_ms"], dtype=float) for p in parts])
        order = np.argsort(t, kind="stable")
        out["t_ms"] = t[order]
        for k in keys:
            out[k] = np.concatenate([np.asarray(p[k], dtype=float) for p in parts])[order]
        out["rate_hz"] = _stream_rate(out["t_ms"], parts[0].get("rate_hz"))
        return out

    emg = _merge(emg_parts, ["value"])
    if emg is not None:
        if all("motor_excl" in p for p in emg_parts):
            t = np.concatenate([np.asarray(p["t_ms"], dtype=float) for p in emg_parts])
            order = np.argsort(t, kind="stable")
            emg["motor_excl"] = np.concatenate([np.asarray(p["motor_excl"], dtype=bool) for p in emg_parts])[order]
        else:
            emg["motor_excl"] = None
    imu = _merge(imu_parts, ["ax", "ay", "az", "gx", "gy", "gz"])
    return emg, imu


def load_truth(session_dir: str | Path) -> dict | None:
    path = Path(session_dir) / "truth.json"
    if not path.exists():
        return None
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)
