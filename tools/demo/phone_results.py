"""Gives the operator's phone the results of each finished run.

    .venv\\Scripts\\python.exe tools\\demo\\phone_results.py <mirror_dir> <phone-ip>:8787

The phone's session report reads `metrics.json` from the session's folder. The headset does not make that file and the
phone cannot (the analysis is Python), so on the phone a real run's results never appeared. This watches the folder that
`quest_collect.py` mirrors from the headset (`<mirror_dir>/<device>/opus_sessions/<id>/`), runs the analysis on every
session that has ended (its `session.json` is there and no new file has arrived for 2 s), and sends the `metrics.json`
to the phone's hub (`PUT /opus/v1/sessions/<id>/files/metrics.json`: 201 new, 200 already there). One line per session.
Needs `analytics/.venv`. Ctrl+C ends it.
"""
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PY = REPO / "analytics" / ".venv" / "Scripts" / "python.exe"


def push(hub: str, sid: str, data: bytes) -> int:
    req = urllib.request.Request(f"http://{hub}/opus/v1/sessions/{sid}/files/metrics.json", data=data, method="PUT")
    try:
        with urllib.request.urlopen(req, timeout=5) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code
    except OSError:
        return 0


def main(mirror: Path, hub: str) -> None:
    seen = {}       # session dir -> (file count, since when)
    done = set()
    print(f"watching {mirror} for finished runs; results go to {hub}", flush=True)
    while True:
        for d in mirror.glob("*/opus_sessions/*"):
            if d in done or not (d / "session.json").exists():
                continue
            n = sum(1 for _ in d.iterdir())
            if seen.get(d, (None, 0))[0] != n:
                seen[d] = (n, time.time())
                continue
            if time.time() - seen[d][1] < 2.0:
                continue
            m = d / "metrics.json"
            if not m.exists():
                subprocess.run([str(PY), "-m", "opus_analytics", str(d)], cwd=str(REPO / "analytics"), capture_output=True, timeout=120)
            if not m.exists():
                print(f"{time.strftime('%H:%M:%S')} {d.name}: the analysis wrote no metrics.json ({n} files)", flush=True)
                done.add(d)
                continue
            code = push(hub, d.name, m.read_bytes())
            print(f"{time.strftime('%H:%M:%S')} {d.name}: metrics.json to the phone, HTTP {code}", flush=True)
            if code in (200, 201, 409):
                done.add(d)     # 409: the phone already has another one; 0: the hub did not answer, try again
        time.sleep(1.0)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    try:
        main(Path(sys.argv[1]).resolve(), sys.argv[2])
    except KeyboardInterrupt:
        pass
