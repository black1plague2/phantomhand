"""CLI: python -m synthetic_patients <command> ...

Commands:
  session --profile healthy --seed 1 --out sim/out/healthy_001 [--trials 20]
  all-profiles --out sim/out [--trials 20]     generate one session per profile
  longitudinal --out sim/out/longitudinal
  fixtures --out contracts/fixtures/sessions   small per-profile sessions + longitudinal summary for the repo
"""
from __future__ import annotations

import argparse
from pathlib import Path

from .generate import write_session
from .longitudinal import write_longitudinal_series
from .profiles import PROFILES


def main(argv=None):
    parser = argparse.ArgumentParser(prog="synthetic_patients")
    sub = parser.add_subparsers(dest="command", required=True)

    p_session = sub.add_parser("session")
    p_session.add_argument("--profile", choices=list(PROFILES.keys()), required=True)
    p_session.add_argument("--seed", type=int, default=1)
    p_session.add_argument("--out", type=Path, required=True)
    p_session.add_argument("--trials", type=int, default=20)

    p_all = sub.add_parser("all-profiles")
    p_all.add_argument("--out", type=Path, default=Path("sim/out"))
    p_all.add_argument("--trials", type=int, default=20)
    p_all.add_argument("--seed", type=int, default=1)

    p_long = sub.add_parser("longitudinal")
    p_long.add_argument("--out", type=Path, default=Path("sim/out/longitudinal"))
    p_long.add_argument("--sessions-per-week", type=int, default=2)

    p_fix = sub.add_parser("fixtures")
    p_fix.add_argument("--out", type=Path, default=Path("contracts/fixtures/sessions"))
    p_fix.add_argument("--trials", type=int, default=6)

    args = parser.parse_args(argv)

    if args.command == "session":
        truth = write_session(args.out, args.profile, args.seed, params={"trialCount": args.trials})
        print(f"wrote session to {args.out} ({len(truth['trials'])} trials)")
    elif args.command == "all-profiles":
        for name in PROFILES:
            out = args.out / name
            write_session(out, name, args.seed, params={"trialCount": args.trials})
            print(f"wrote {out}")
    elif args.command == "longitudinal":
        result = write_longitudinal_series(args.out, sessions_per_week=args.sessions_per_week)
        print(f"wrote longitudinal series to {args.out} ({len(result['weeks'])} weeks)")
    elif args.command == "fixtures":
        for name in PROFILES:
            out = args.out / name
            write_session(out, name, seed=42, params={"trialCount": args.trials})
            print(f"wrote fixture {out}")
        write_longitudinal_series(args.out / "longitudinal", sessions_per_week=1, trials_per_session=args.trials)
        print(f"wrote fixture {args.out / 'longitudinal'}")


if __name__ == "__main__":
    main()
