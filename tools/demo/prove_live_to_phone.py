#!/usr/bin/env python
"""tools/demo/prove_live_to_phone.py -- S run5, task 2.

Proves (or reports honestly why it cannot yet prove) that the REAL game reaches the REAL phone
app, closing the gap CONTEXT.md's RUN 18 box calls out as "the single most important open item":
> The two legs have never been joined... The test that settles it must assert the phone received
> a session whose device.model is NOT synthetic.

Four checks, run in order, each best-effort (a failed early check does not crash the later ones --
this tool is a diagnostic, not a hard gate):

  (a) `adb forward --list` includes `tcp:8787` (the USB port-forward from the PC's hub to the
      phone -- per the task brief, 8787 is "forwarded by adb to the phone's hub", i.e. the PHONE
      is the thing listening on the far end of that forward).
  (b) connect to 127.0.0.1:8787 over TCP and confirm the phone's hub answers (a live probe, not
      just trusting the forward list -- the forward can exist while the phone-side listener is
      down).
  (c) list the phone's received sessions (`adb -s <serial> shell run-as com.opus.opus_app ls
      files/sessions`), read each session.json, and report device.model/trials/file count per
      session -- flagging `synthetic` (fake_headset.py / simulation) vs a real game
      (`quest-link-editor` or any other non-synthetic model).
  (d) optional `--pull <session>`: adb pull that one session to the PC and run
      `contracts/validate.py --session`, `python -m opus_analytics`, and
      `tools/demo/check_session.py` against the local copy.

Never touches the phone's app data destructively (read-only `ls`/`cat` via run-as, and a plain
`adb pull`, which does not delete the source). Run with the analytics venv's interpreter (needs
nothing exotic, but keeps every other tool in this directory using the same interpreter):

    analytics/.venv/Scripts/python.exe tools/demo/prove_live_to_phone.py
    analytics/.venv/Scripts/python.exe tools/demo/prove_live_to_phone.py --pull session-000
"""
from __future__ import annotations

import argparse
import json
import socket
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any, Dict, List, Optional

from tool_paths import find_adb

REPO_ROOT = Path(__file__).resolve().parents[2]

# OPUS_ADB, ANDROID_HOME, PATH, this PC's Android SDK / Unity-bundled adb, and only then the old PC's path (tool_paths.py)
ADB_DEFAULT = str(find_adb())
PACKAGE = "com.opus.opus_app"
HUB_FORWARD_PORT = 8787


def _run_adb(adb: str, args: List[str], serial: Optional[str] = None, timeout: float = 10.0) -> subprocess.CompletedProcess:
    cmd = [adb]
    if serial:
        cmd += ["-s", serial]
    cmd += args
    return subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)


# ---------------------------------------------------------------------------
# (a) adb forward --list has tcp:8787
# ---------------------------------------------------------------------------

def check_forward(adb: str) -> Dict[str, Any]:
    result: Dict[str, Any] = {"check": "adb forward --list", "ok": False, "detail": ""}
    try:
        proc = _run_adb(adb, ["forward", "--list"])
    except (FileNotFoundError, subprocess.TimeoutExpired) as e:
        result["detail"] = f"could not run adb: {e}"
        return result
    if proc.returncode != 0:
        result["detail"] = f"adb forward --list exited {proc.returncode}: {proc.stderr.strip()}"
        return result
    lines = [l for l in proc.stdout.splitlines() if l.strip()]
    hit = [l for l in lines if f"tcp:{HUB_FORWARD_PORT}" in l]
    result["raw"] = lines
    if hit:
        result["ok"] = True
        result["detail"] = f"found: {hit[0]}"
    else:
        result["detail"] = (
            f"no tcp:{HUB_FORWARD_PORT} forward registered "
            f"(run: adb forward tcp:{HUB_FORWARD_PORT} tcp:{HUB_FORWARD_PORT} on the PC, once, "
            "per docs/MANUAL_TODO.md-style human step, if this is meant to be live right now)"
        )
    return result


# ---------------------------------------------------------------------------
# (b) TCP-connect to 127.0.0.1:8787 and see if anything answers
# ---------------------------------------------------------------------------

def check_hub_reachable(port: int = HUB_FORWARD_PORT, timeout: float = 2.0) -> Dict[str, Any]:
    result: Dict[str, Any] = {"check": f"connect 127.0.0.1:{port}", "ok": False, "detail": ""}
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=timeout):
            result["ok"] = True
            result["detail"] = "TCP connect succeeded (something is listening -- the phone's hub, via the forward)"
    except (ConnectionRefusedError, OSError, socket.timeout) as e:
        result["detail"] = f"connect failed: {e} (phone hub may be off, or the forward may not be set up)"
    return result


# ---------------------------------------------------------------------------
# (c) list + summarize the phone's received sessions
# ---------------------------------------------------------------------------

def list_phone_sessions(adb: str, serial: str) -> Dict[str, Any]:
    result: Dict[str, Any] = {"check": "phone sessions (run-as ls)", "ok": False, "sessions": [], "detail": ""}
    try:
        proc = _run_adb(adb, ["shell", "run-as", PACKAGE, "ls", "files/sessions"], serial=serial)
    except (FileNotFoundError, subprocess.TimeoutExpired) as e:
        result["detail"] = f"could not run adb: {e}"
        return result
    if proc.returncode != 0:
        result["detail"] = f"run-as ls exited {proc.returncode}: {proc.stderr.strip() or proc.stdout.strip()}"
        return result
    names = [l.strip() for l in proc.stdout.splitlines() if l.strip()]
    if not names:
        result["ok"] = True  # not an error -- just nothing recorded yet
        result["detail"] = "no sessions on the phone"
        return result

    sessions = []
    for name in names:
        info: Dict[str, Any] = {"name": name}
        cat = _run_adb(adb, ["shell", "run-as", PACKAGE, "cat", f"files/sessions/{name}/session.json"], serial=serial)
        ls = _run_adb(adb, ["shell", "run-as", PACKAGE, "ls", f"files/sessions/{name}"], serial=serial)
        file_count = len([l for l in ls.stdout.splitlines() if l.strip()]) if ls.returncode == 0 else None
        info["file_count"] = file_count
        if cat.returncode == 0 and cat.stdout.strip():
            try:
                sj = json.loads(cat.stdout)
                model = sj.get("device", {}).get("model")
                info["device_model"] = model
                info["mode"] = sj.get("mode")
                info["trials"] = sj.get("blocks", [{}])[0].get("params", {}).get("n_trials") if sj.get("blocks") else None
                info["chunks"] = sj.get("chunks")
                info["is_synthetic"] = (model == "synthetic")
            except json.JSONDecodeError as e:
                info["parse_error"] = str(e)
        else:
            info["read_error"] = cat.stderr.strip() or cat.stdout.strip()
        sessions.append(info)

    result["ok"] = True
    result["sessions"] = sessions
    real_hits = [s for s in sessions if s.get("device_model") and not s.get("is_synthetic")]
    if real_hits:
        result["detail"] = (
            f"{len(sessions)} session(s) on phone; REAL (non-synthetic) device.model found: "
            f"{[s['device_model'] for s in real_hits]} -- the two legs (real game -> phone) ARE joined"
        )
        result["real_game_reached_phone"] = True
    else:
        result["detail"] = (
            f"{len(sessions)} session(s) on phone; all are synthetic/simulation "
            f"(device.model={[s.get('device_model') for s in sessions]}) -- "
            "the real-game -> phone leg is still NOT proven, per CONTEXT.md's RUN 18 finding"
        )
        result["real_game_reached_phone"] = False
    return result


# ---------------------------------------------------------------------------
# (d) optional --pull: copy one session to the PC and run the full local check chain
# ---------------------------------------------------------------------------

def pull_and_check(adb: str, serial: str, session_name: str) -> Dict[str, Any]:
    result: Dict[str, Any] = {"check": f"--pull {session_name}", "ok": False, "detail": "", "steps": []}
    dest_root = Path(tempfile.mkdtemp(prefix="opus_pulled_session_"))
    dest = dest_root / session_name
    remote = f"/data/data/{PACKAGE}/files/sessions/{session_name}"

    # `adb pull` on an app-private path needs run-as-copied staging on most stock Android builds
    # (adb itself is not root); the reliable path is: run-as tar the dir to a world-readable temp
    # location, pull that, then untar locally would need tar on PC too -- simpler and sufficient
    # for a demo tool: copy file-by-file via `run-as cat` (already proven to work above) since
    # session dirs are small (a handful of json files).
    ls = _run_adb(adb, ["shell", "run-as", PACKAGE, "ls", f"files/sessions/{session_name}"], serial=serial)
    if ls.returncode != 0:
        result["detail"] = f"could not list {session_name} on phone: {ls.stderr.strip()}"
        return result
    names = [l.strip() for l in ls.stdout.splitlines() if l.strip()]
    dest.mkdir(parents=True, exist_ok=True)
    copied = []
    for fname in names:
        cat = _run_adb(adb, ["shell", "run-as", PACKAGE, "cat", f"files/sessions/{session_name}/{fname}"],
                        serial=serial, timeout=30.0)
        if cat.returncode != 0:
            result["steps"].append(f"FAILED to read {fname}: {cat.stderr.strip()}")
            continue
        (dest / fname).write_text(cat.stdout, encoding="utf-8")
        copied.append(fname)
    result["steps"].append(f"copied {len(copied)}/{len(names)} files to {dest}")
    if not copied:
        result["detail"] = "nothing copied; skipping local checks"
        return result

    def run_py(args: List[str]) -> str:
        proc = subprocess.run([sys.executable] + args, capture_output=True, text=True, cwd=str(REPO_ROOT))
        out = (proc.stdout + proc.stderr).strip()
        return f"[exit {proc.returncode}]\n{out}"

    result["steps"].append("--- contracts/validate.py --session ---")
    result["steps"].append(run_py(["contracts/validate.py", "--session", str(dest)]))

    result["steps"].append("--- python -m opus_analytics ---")
    result["steps"].append(run_py(["-m", "opus_analytics", str(dest)]))

    result["steps"].append("--- tools/demo/check_session.py ---")
    result["steps"].append(run_py(["tools/demo/check_session.py", str(dest)]))

    result["ok"] = True
    result["detail"] = f"pulled to {dest} and ran validate/analytics/check_session (see steps)"
    result["local_path"] = str(dest)
    return result


# ---------------------------------------------------------------------------

def main() -> int:
    parser = argparse.ArgumentParser(description="Prove (or honestly report on) the real game -> phone app path")
    parser.add_argument("--adb", type=str, default=ADB_DEFAULT, help="Path to adb.exe")
    parser.add_argument("--serial", type=str, default=None, help="adb device serial (auto-detected if omitted and exactly one device is attached)")
    parser.add_argument("--pull", type=str, default=None, metavar="SESSION_NAME",
                         help="Also pull this session (by directory name, e.g. session-000) to the PC and run validate/analytics/check_session against it")
    args = parser.parse_args()

    adb = args.adb
    serial = args.serial

    print("=== PROVE LIVE TO PHONE ===")
    print(f"adb: {adb}")

    # Resolve serial if not given.
    if serial is None:
        try:
            devs = _run_adb(adb, ["devices"])
            lines = [l for l in devs.stdout.splitlines()[1:] if l.strip() and "\tdevice" in l]
            if len(lines) == 1:
                serial = lines[0].split("\t")[0]
                print(f"Auto-detected serial: {serial}")
            elif len(lines) == 0:
                print("No adb device attached (adb devices listed none) -- checks (c)/(d) will be skipped, (a)/(b) still run.")
            else:
                print(f"Multiple devices attached ({[l.split(chr(9))[0] for l in lines]}); pass --serial. Checks (c)/(d) will be skipped.")
        except (FileNotFoundError, subprocess.TimeoutExpired) as e:
            print(f"adb devices failed: {e} -- checks (c)/(d) will be skipped, (a)/(b) still run.")

    results = []

    print("\n(a) adb forward --list")
    r = check_forward(adb)
    results.append(r)
    print(f"  {'OK' if r['ok'] else 'NOT FOUND'}: {r['detail']}")

    print("\n(b) TCP connect 127.0.0.1:8787 (phone hub)")
    r = check_hub_reachable()
    results.append(r)
    print(f"  {'OK' if r['ok'] else 'UNREACHABLE'}: {r['detail']}")

    if serial:
        print(f"\n(c) phone sessions (serial={serial})")
        r = list_phone_sessions(adb, serial)
        results.append(r)
        print(f"  {'OK' if r['ok'] else 'FAILED'}: {r['detail']}")
        for s in r.get("sessions", []):
            tag = "SYNTHETIC" if s.get("is_synthetic") else ("REAL" if s.get("device_model") else "?")
            print(f"    - {s['name']}: device.model={s.get('device_model')} [{tag}] mode={s.get('mode')} "
                  f"trials={s.get('trials')} chunks={s.get('chunks')} files={s.get('file_count')}")

        if args.pull:
            print(f"\n(d) --pull {args.pull}")
            r = pull_and_check(adb, serial, args.pull)
            results.append(r)
            print(f"  {'OK' if r['ok'] else 'FAILED'}: {r['detail']}")
            for step in r.get("steps", []):
                print(f"  {step}")
    else:
        print("\n(c)/(d) skipped: no single adb device resolved (see above)")

    print("\n=== SUMMARY ===")
    for r in results:
        print(f"  [{'OK' if r['ok'] else 'FAIL/SKIP'}] {r['check']}: {r['detail']}")

    joined = next((r for r in results if "real_game_reached_phone" in r), None)
    if joined is not None:
        print(f"\nreal-game -> phone leg joined: {joined['real_game_reached_phone']}")

    return 0  # diagnostic tool: never a hard failure exit code, the printed report is the point


if __name__ == "__main__":
    raise SystemExit(main())
