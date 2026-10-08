"""Top-level orchestration: load a session, segment trials, compute all metrics."""
from __future__ import annotations

import datetime as _dt
import json
from pathlib import Path

from . import __version__ as PKG_VERSION
from .io_session import load_session
from .metrics.embodiment import compute_embodiment, has_phantom_hand_events
from .metrics.session_metrics import compute_session_metrics
from .metrics.trial_metrics import compute_trial_metrics
from .segmentation import segment_trials


def analyze_session(session_dir: str | Path, validate: bool = True) -> dict:
    session_dir = Path(session_dir)
    data = load_session(session_dir, validate=validate)
    phantom = has_phantom_hand_events(data.events)
    # Phantom Hand sessions have no reach trials: `trial` there is a condition index.
    trials = [] if phantom else segment_trials(data.events)

    trial_metrics_by_index = {}
    for trial in trials:
        trial_metrics_by_index[trial.index] = compute_trial_metrics(trial, data.joints, data.rate_hz)

    session_metrics = {} if phantom else compute_session_metrics(trials, trial_metrics_by_index, fs=data.rate_hz)

    # Build trials list with block, trial number, and optional t_start_ms/t_end_ms
    trials_list = []
    for trial_num, t in enumerate(trials):
        trial_obj = {
            "block": t.block,
            "trial": trial_num,
            "hand": t.hand,
            "outcome": t.outcome,
            "target": t.target,
            "metrics": trial_metrics_by_index[t.index],
        }
        # Add timing info if available
        if t.t_trial_start is not None:
            trial_obj["t_start_ms"] = t.t_trial_start
        if t.t_trial_end is not None:
            trial_obj["t_end_ms"] = t.t_trial_end
        trials_list.append(trial_obj)

    result = {
        "session_id": data.session_id,
        "computed_at": _dt.datetime.now(_dt.timezone.utc).isoformat(),
        "analytics_version": PKG_VERSION,
        "sample_rate_hz": data.rate_hz,
        "validation": {k: v for k, v in data.validation_errors.items()} if validate else None,
        "trials": trials_list,
        "session": {"metrics": session_metrics},
    }
    if phantom:
        result["embodiment"] = compute_embodiment(data.events, data)
    return result


def write_metrics(session_dir: str | Path, result: dict | None = None, validate: bool = True) -> Path:
    session_dir = Path(session_dir)
    if result is None:
        result = analyze_session(session_dir, validate=validate)
    out_path = session_dir / "metrics.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)
    return out_path
