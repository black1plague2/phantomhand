"""Write a full session directory to disk: session.json, events.ndjson, kin_###.json, truth.json."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np

from .chunking import apply_recording_noise, build_chunks
from .profiles import PROFILES
from .session import generate_session


def _json_default(o):
    if isinstance(o, np.ndarray):
        return o.tolist()
    if isinstance(o, (np.floating,)):
        return float(o)
    if isinstance(o, (np.integer,)):
        return int(o)
    raise TypeError(f"not JSON serializable: {type(o)}")


def write_session(out_dir: Path, profile_name: str, seed: int, params: dict | None = None,
                   profile_override: dict | None = None, session_id: str | None = None,
                   started_at: str = "2026-01-01T09:00:00Z",
                   patient_ref: str | None = None) -> dict:
    """Generate one full session and write it to out_dir. Returns the truth dict."""
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    profile = dict(profile_override or PROFILES[profile_name])
    envelope, events, joints_true, truth, trial_ranges, rot_true = generate_session(
        profile_name=profile_name, profile=profile, seed=seed, params=params,
        session_id=session_id, started_at=started_at, patient_ref=patient_ref,
    )

    rng = np.random.default_rng(seed + 1_000_003)
    recorded, dropout_spans = apply_recording_noise(joints_true, profile, rng, rot_true=rot_true)
    tracking_rate_hz = profile.get("tracking_rate_hz", envelope["device"]["tracking_rate_hz"])
    chunks = build_chunks(envelope["session_id"], recorded, profile_name, seed, trial_ranges,
                           rate_hz=tracking_rate_hz)
    truth["dropout_spans"] = dropout_spans
    truth["session_id"] = envelope["session_id"]

    with open(out_dir / "session.json", "w", encoding="utf-8") as f:
        json.dump(envelope, f, indent=2, default=_json_default)

    with open(out_dir / "events.ndjson", "w", encoding="utf-8") as f:
        for ev in events:
            f.write(json.dumps(ev, default=_json_default) + "\n")

    for chunk in chunks:
        seq = chunk["seq"]
        with open(out_dir / f"kin_{seq:03d}.json", "w", encoding="utf-8") as f:
            json.dump(chunk, f, default=_json_default)

    with open(out_dir / "truth.json", "w", encoding="utf-8") as f:
        json.dump(truth, f, indent=2, default=_json_default)

    return truth
