#!/usr/bin/env python3
"""tools/demo/watch_and_analyse.py

Watches a hub sessions directory (default `app/.hub_data`) and, when a session directory looks
finished and not yet analysed, runs `contracts/validate.py --session <dir>` and then
`python -m opus_analytics <dir>`, printing a short plain-English summary (trials, success, mean
RT/MT/peak speed/SPARC, quality flags).

A session is "finished and not yet analysed" when:
  - session.json exists and its `ended_at` field is non-null,
  - events.ndjson exists,
  - at least one kin_*.json chunk exists,
  - metrics.json does not exist yet.

Run with the analytics venv's interpreter (it has jsonschema/referencing/numpy, everything both
contracts/validate.py and opus_analytics need):

    analytics/.venv/Scripts/python.exe tools/demo/watch_and_analyse.py [--dir PATH] [--interval SECONDS] [--once]

Polls every 2 seconds by default. Idempotent: a session directory is only ever processed once per
run (tracked in-memory), and is skipped up front if it already has a metrics.json (e.g. from a
previous run of this watcher, or from `opus_analytics` having already been run by hand).

Windows-safe: every path is a pathlib.Path and every subprocess call passes an argument list (never
a shell string), so the space in `...\\GARV BANSAL\\...` is never at risk of being split on.
"""
from __future__ import annotations

import argparse
import json
import statistics
import subprocess
import sys
import time
from pathlib import Path
from typing import Iterable

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_WATCH_DIR = REPO_ROOT / "app" / ".hub_data"
VALIDATE_PY = REPO_ROOT / "contracts" / "validate.py"
ANALYTICS_DIR = REPO_ROOT / "analytics"


def is_session_ready(session_dir: Path) -> bool:
    """True if `session_dir` has ended, has events + at least one kin chunk, and has not been
    analysed yet (no metrics.json). Never raises: a session directory being written to mid-poll
    (partial/corrupt session.json) is treated as "not ready yet", not an error."""
    session_json = session_dir / "session.json"
    events_ndjson = session_dir / "events.ndjson"
    metrics_json = session_dir / "metrics.json"

    if metrics_json.exists():
        return False
    if not session_json.exists() or not events_ndjson.exists():
        return False
    if not any(session_dir.glob("kin_*.json")):
        return False

    try:
        envelope = json.loads(session_json.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False

    return envelope.get("ended_at") is not None


def run_validate(session_dir: Path) -> tuple[bool, str]:
    proc = subprocess.run(
        [sys.executable, str(VALIDATE_PY), "--session", str(session_dir)],
        capture_output=True,
        text=True,
    )
    return proc.returncode == 0, (proc.stdout + proc.stderr)


def run_analytics(session_dir: Path) -> tuple[bool, str]:
    proc = subprocess.run(
        [sys.executable, "-m", "opus_analytics", str(session_dir), "--quiet"],
        capture_output=True,
        text=True,
        cwd=str(ANALYTICS_DIR),
    )
    return proc.returncode == 0, (proc.stdout + proc.stderr)


def _mean(values: list[float], digits: int) -> str:
    return f"{statistics.mean(values):.{digits}f}" if values else "-"


def _collect_metric(trials: list[dict], metric_id: str) -> list[float]:
    values = []
    for t in trials:
        mv = t.get("metrics", {}).get(metric_id, {})
        if mv.get("quality") in ("ok", "degraded") and mv.get("value") is not None:
            values.append(mv["value"])
    return values


def summarize(session_dir: Path) -> str:
    """Read metrics.json (written by run_analytics) and render a short plain-English report."""
    metrics_path = session_dir / "metrics.json"
    data = json.loads(metrics_path.read_text(encoding="utf-8"))
    trials = data.get("trials", [])
    n_trials = len(trials)
    n_success = sum(1 for t in trials if t.get("outcome") == "success")

    rt = _collect_metric(trials, "reaction_time_ms")
    mt = _collect_metric(trials, "movement_time_ms")
    peak = _collect_metric(trials, "peak_speed_mps")
    sparc = _collect_metric(trials, "sparc")

    quality_counts = {"ok": 0, "degraded": 0, "invalid": 0}
    for t in trials:
        for mv in t.get("metrics", {}).values():
            q = mv.get("quality")
            if q in quality_counts:
                quality_counts[q] += 1

    lines = [
        f"  session {data.get('session_id', session_dir.name)}: {n_trials} trials, {n_success} succeeded",
        f"  mean reaction time {_mean(rt, 1)} ms, mean movement time {_mean(mt, 1)} ms",
        f"  mean peak speed {_mean(peak, 2)} m/s, mean SPARC {_mean(sparc, 2)}",
        f"  metric quality across all trials: ok={quality_counts['ok']} degraded={quality_counts['degraded']}"
        f" invalid={quality_counts['invalid']}",
    ]
    return "\n".join(lines)


def process_session(session_dir: Path) -> None:
    print(f"[watch] {session_dir.name}: session ended, validating + analysing...")

    ok, out = run_validate(session_dir)
    for line in out.strip().splitlines():
        print(f"  {line}")
    if not ok:
        print(f"[watch] {session_dir.name}: contracts/validate.py FAILED -- not running analytics.")
        return

    ok, out = run_analytics(session_dir)
    if not ok:
        print(f"[watch] {session_dir.name}: opus_analytics FAILED:")
        for line in out.strip().splitlines():
            print(f"  {line}")
        return

    print(f"[watch] {session_dir.name}: analysed.")
    print(summarize(session_dir))


def watch_once(watch_dir: Path, processed: set[str]) -> None:
    if not watch_dir.exists():
        return
    for session_dir in sorted(p for p in watch_dir.iterdir() if p.is_dir()):
        if session_dir.name in processed:
            continue
        if (session_dir / "metrics.json").exists():
            # Already analysed (by us in an earlier run of this process, or by hand) -- don't
            # reprocess, but remember it so we stop looking at it every poll.
            processed.add(session_dir.name)
            continue
        if is_session_ready(session_dir):
            process_session(session_dir)
            processed.add(session_dir.name)


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dir", type=Path, default=DEFAULT_WATCH_DIR, help="hub sessions directory to watch")
    parser.add_argument("--interval", type=float, default=2.0, help="poll interval in seconds (default 2)")
    parser.add_argument("--once", action="store_true", help="poll a single time and exit (used by tests)")
    args = parser.parse_args(list(argv) if argv is not None else None)

    watch_dir: Path = args.dir
    print(f"[watch] watching {watch_dir} every {args.interval}s (ctrl-C to stop)")

    processed: set[str] = set()

    if args.once:
        watch_once(watch_dir, processed)
        return 0

    try:
        while True:
            watch_once(watch_dir, processed)
            time.sleep(args.interval)
    except KeyboardInterrupt:
        print("\n[watch] stopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
