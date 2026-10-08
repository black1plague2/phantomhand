"""Turn true joint trajectories into recorded (noisy, dropout-afflicted) 5s chunks."""
from __future__ import annotations

import numpy as np

from .session import JOINTS, SAMPLE_RATE_HZ, FRAMES_PER_CHUNK, DT_S


def apply_rate_hold(pos: np.ndarray, orig_rate: float, target_rate: float | None) -> np.ndarray:
    """Zero-order-hold degrade: emulate a device that only produces a genuinely new pose every
    round(orig_rate/target_rate) frames of the orig_rate grid (frame count / t_ms unchanged, so
    trial segmentation and event timing are unaffected -- only the *density* of new information
    drops, which is what should make derivative-based metrics like SPARC/LDLJ unreliable).
    No-op when target_rate is None or >= orig_rate.
    """
    if not target_rate or target_rate >= orig_rate:
        return pos
    stride = max(1, int(round(orig_rate / target_rate)))
    if stride <= 1:
        return pos
    out = pos.copy()
    n = out.shape[0]
    for i in range(n):
        if i % stride != 0:
            out[i] = out[i - (i % stride)]
    return out


def apply_recording_noise(joints_true: dict[str, np.ndarray], profile: dict,
                           rng: np.random.Generator, orig_rate: float = SAMPLE_RATE_HZ,
                           rot_true: dict[str, np.ndarray] | None = None) -> tuple[dict, list[dict]]:
    """Returns (recorded_joints {joint: {pos, conf, [rot]}}, dropout_spans for truth logging).

    `rot_true` (wrist/palm orientation, see orientation.py) passes through unmodified -- it is
    demo-visual metadata, not fed to any metric, so it does not get jitter/dropout treatment.
    """
    n = next(iter(joints_true.values())).shape[0]
    jitter_std = profile.get("jitter_std_m", 0.0)
    dropout_rate = profile.get("dropout_rate", 0.0)
    target_rate = profile.get("tracking_rate_hz", orig_rate)

    recorded = {}
    dropout_spans = []
    n_chunks = int(np.ceil(n / FRAMES_PER_CHUNK))

    for joint in JOINTS:
        pos = joints_true[joint].copy()
        pos = apply_rate_hold(pos, orig_rate, target_rate)
        if jitter_std > 0:
            pos = pos + rng.normal(0.0, jitter_std, size=pos.shape)
        conf = np.ones(n, dtype=float)
        recorded[joint] = {"pos": pos, "conf": conf}
        if rot_true is not None and joint in rot_true:
            recorded[joint]["rot"] = rot_true[joint].copy()

    # dropout: independently, per chunk, per active fingertip/wrist joint, a short span
    dropout_joints = [j for j in JOINTS if "index_tip" in j or "wrist" in j]
    for c in range(n_chunks):
        start = c * FRAMES_PER_CHUNK
        end = min(n, start + FRAMES_PER_CHUNK)
        for joint in dropout_joints:
            if rng.random() < dropout_rate:
                span_len = int(rng.integers(int(0.15 * SAMPLE_RATE_HZ), int(0.8 * SAMPLE_RATE_HZ) + 1))
                span_len = min(span_len, end - start)
                if span_len <= 0:
                    continue
                span_start = start + int(rng.integers(0, max(1, end - start - span_len + 1)))
                span_end = span_start + span_len
                recorded[joint]["conf"][span_start:span_end] = 0.0
                # freeze position at last good sample (typical tracker-loss behaviour)
                freeze_val = recorded[joint]["pos"][max(span_start - 1, 0)]
                recorded[joint]["pos"][span_start:span_end] = freeze_val
                dropout_spans.append({"joint": joint, "start_frame": int(span_start), "end_frame": int(span_end)})

    return recorded, dropout_spans


def build_chunks(session_id: str, recorded: dict, profile_name: str, seed: int,
                  trial_frame_ranges: list[dict], rate_hz: float = SAMPLE_RATE_HZ) -> list[dict]:
    """Split recorded joints into kin_###.json chunk dicts (schema-valid).

    `rate_hz` is the *declared* device tracking rate (profile's `tracking_rate_hz`), which is
    what opus_analytics' quality gating reads -- it may be lower than the internal generation
    grid (SAMPLE_RATE_HZ); frame count / t_ms spacing stay on the internal grid regardless (see
    `apply_rate_hold`), matching how a real chunked recorder would still timestamp on a fixed
    capture clock even when the underlying sensor updates less often.
    """
    n = next(iter(recorded.values()))["pos"].shape[0]
    n_chunks = int(np.ceil(n / FRAMES_PER_CHUNK))
    chunks = []
    for c in range(n_chunks):
        start = c * FRAMES_PER_CHUNK
        end = min(n, start + FRAMES_PER_CHUNK)
        t_ms = (np.arange(start, end) * DT_S * 1000.0).tolist()
        frames = {}
        for joint in JOINTS:
            frames[joint] = {
                "pos": recorded[joint]["pos"][start:end].round(6).tolist(),
                "conf": recorded[joint]["conf"][start:end].round(3).tolist(),
            }
            if "rot" in recorded[joint]:
                frames[joint]["rot"] = recorded[joint]["rot"][start:end].round(6).tolist()
        trials_in_chunk = [t["index"] for t in trial_frame_ranges
                            if t["start"] < end and t["end"] > start]
        chunk = {
            "session_id": session_id,
            "seq": c,
            "rate_hz": rate_hz,
            "source": "synthetic",
            "joints": JOINTS,
            "t_ms": [round(x, 3) for x in t_ms],
            "frames": frames,
            "ground_truth": {
                "profile": profile_name,
                "seed": seed,
                "note": "full per-trial ground truth is in truth.json at the session root",
                "trials_in_chunk": trials_in_chunk,
            },
        }
        chunks.append(chunk)
    return chunks
