"""CLI: python -m opus_analytics <session_dir> [--no-validate]"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .analyze import analyze_session, write_metrics


def main(argv=None):
    parser = argparse.ArgumentParser(prog="opus_analytics")
    parser.add_argument("session_dir", type=Path, help="path to a session directory (session.json, events.ndjson, kin_*.json)")
    parser.add_argument("--no-validate", action="store_true", help="skip contracts/schemas validation")
    parser.add_argument("--quiet", action="store_true", help="don't print the metrics summary")
    parser.add_argument("--summary", action="store_true",
                        help="Phantom Hand: print the witness table (sync / async / difference)")
    args = parser.parse_args(argv)

    result = analyze_session(args.session_dir, validate=not args.no_validate)

    if result.get("validation"):
        n_errors = sum(len(v) for v in result["validation"].values())
        if n_errors:
            print(f"WARNING: {n_errors} schema validation error(s) in {args.session_dir}:", file=sys.stderr)
            for fname, errs in result["validation"].items():
                for e in errs[:10]:
                    print(f"  {fname}: {e}", file=sys.stderr)

    out_path = write_metrics(args.session_dir, result=result)

    if args.summary:
        from .metrics.embodiment import witness_table
        if "embodiment" in result:
            print(witness_table(result["embodiment"]))
        else:
            print("no Phantom Hand embodiment data in this session", file=sys.stderr)
        return

    if not args.quiet:
        print(f"wrote {out_path}")
        print(f"{len(result['trials'])} trials, session metrics:")
        # result["session"] is {"metrics": {...}}, not the metric map itself -- iterating it directly
        # crashed with KeyError: 'value' on the first (and only) key. Print the metrics, and tolerate a
        # metric with no value (quality "invalid" legitimately carries value=None).
        for k, v in result["session"].get("metrics", {}).items():
            value = v.get("value")
            print(f"  {k}: {'-' if value is None else value} {v.get('unit', '')} [{v.get('quality', '?')}]")


if __name__ == "__main__":
    main()
