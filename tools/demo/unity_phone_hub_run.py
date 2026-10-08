"""A full session of the GAME (Unity, the OPEN editor) with the operator's PHONE as the hub.

    .venv\\Scripts\\python.exe tools\\demo\\unity_phone_hub_run.py <phone-ip>:8787 <out_dir> [adb-serial] [twin]

`unity_fault_run.py hardware` uses a recording hub on the PC. Here the hub is the operator app on the phone (signed in,
Monitor tab open: its hub then answers GET /opus/v1/health). The game is pointed at it and at the two boards: the REAL ones,
found by their beacons, or with `twin` the simulated pair on this PC (no motors; a muscle burst is asked of it every 7 s of
the long phases, so the phone's trace has something to show). While PH_FullRun plays, the phone's screen is saved every
20 s (adb screencap, only while the operator app has the screen; the app is brought back when something else takes it) and
the hub's /opus/v1/live/last_status is polled, so the run leaves pictures of the live card and of the report as the
operator saw them. Afterwards the local session is validated and analysed. Without `twin` it buzzes the motors (about 114
strokes): announce it first. The run leaves one more session in the phone's list, from a device named editor-....
The hub must be on port 8787 (LiveClient's hub port). Needs the editor for about 5 minutes: take game/.ph_unity.lock.
"""
import asyncio
import json
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import unity_fault_run as U          # editor_env(), SESSIONS, REPO, and through it phantom_pipeline as U.P

ADB = Path.home() / "AppData" / "Local" / "Android" / "Sdk" / "platform-tools" / "adb.exe"


def get(url: str, timeout: float = 3.0):
    try:
        with urllib.request.urlopen(url, timeout=timeout) as r:
            return json.loads(r.read().decode("utf-8", "replace"))
    except Exception as e:  # noqa: BLE001 - a hub that does not answer is a result, not a crash
        return {"error": repr(e)[:120]}


APP = "com.opus.opus_app"            # also the side-by-side build, com.opus.opus_app.pc


def screencap(serial: str, path: Path) -> str:
    """Saves the phone's screen, but only the operator app's: whatever else is in front is the owner's business. When
    something else has the screen the operator app is asked back to the front and no picture is taken this time."""
    if not ADB.exists():
        return "no adb"
    adb = [str(ADB)] + (["-s", serial] if serial else [])
    try:
        focus = subprocess.run(adb + ["shell", "dumpsys window | grep mCurrentFocus"], capture_output=True, timeout=10).stdout.decode(errors="replace")
        if APP not in focus:
            pkgs = subprocess.run(adb + ["shell", "pm list packages -e " + APP], capture_output=True, timeout=10).stdout.decode(errors="replace").split()
            for pkg in sorted((x.partition(":")[2] for x in pkgs), reverse=True)[:1]:
                subprocess.run(adb + ["shell", "monkey -p %s -c android.intent.category.LAUNCHER 1" % pkg], capture_output=True, timeout=10)
            return "not in front: " + focus.strip()[-60:]
        data = subprocess.run(adb + ["exec-out", "screencap", "-p"], capture_output=True, timeout=20).stdout
        if data[:4] == b"\x89PNG":
            path.write_bytes(data)
        return "ok"
    except Exception as e:  # noqa: BLE001
        return repr(e)[:80]


async def main(hub: str, out: Path, serial: str, twin_mode: bool = False) -> int:
    out.mkdir(parents=True, exist_ok=True)
    host, _, port = hub.partition(":")
    if port != "8787":
        print("the hub must be on port 8787 (LiveClient.HubPort)")
        return 2
    base = f"http://{host}:{port}"
    health = get(base + "/opus/v1/health")
    print("hub health before:", json.dumps(health), flush=True)
    if health.get("status") != "ok":
        print("the phone's hub does not answer: sign in on the phone and open the Monitor tab")
        return 2
    twin, ctl_addr, ctl, off_screen = None, None, socket.socket(socket.AF_INET, socket.SOCK_DGRAM), []
    if twin_mode:
        (out / "twin.jsonl").unlink(missing_ok=True)
        twin = U.P.TwinProc("both", U.OFFSET, 5, out / "twin.jsonl")
        found = (await twin.wait_ready())["ports"]
        env = U.P.unity_env(host, int(port), found)
        ctl_addr = ("127.0.0.1", found["control"])
    else:
        found = await U.P.discover_nodes(8791, ("haptic", "bio"), timeout=8.0)
        if "haptic" not in found or "bio" not in found:
            print("real nodes not found on UDP 8791 within 8 s:", found)
            return 2
        env = {"OPUS_PH_HUB": hub, "OPUS_PH_NODE_A": "%s:%d" % tuple(found["haptic"]),
               "OPUS_PH_NODE_B": "%s:%d" % tuple(found["bio"]), "OPUS_PH_DISCOVERY_PORT": "8791"}
    print("twin ports" if twin_mode else "real nodes", json.dumps(found), flush=True)
    before = {p.name for p in U.SESSIONS.glob("*")} if U.SESSIONS.exists() else set()
    test, green, polls = None, False, []
    try:
        for k, v in env.items():
            print("  set in the editor:", k, "=", v, "->", U.editor_env("SetEnvironmentVariable", {"variable": k, "value": v}), flush=True)
        t0 = time.time()
        test = await asyncio.create_subprocess_exec(
            sys.executable, "tools/unity_mcp.py", "test", "PlayMode", "PH_FullRun", "--timeout", "900", "--out", str(out / "playmode.json"),
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT, cwd=str(U.REPO))
        reader = asyncio.create_task(test.stdout.read())
        next_shot, last_phase, next_burst = 15.0, None, 0.0
        while test.returncode is None:
            await asyncio.sleep(1.0)
            now = time.time() - t0
            if now > 1000:
                test.kill()
                break
            st = await asyncio.to_thread(get, base + "/opus/v1/live/last_status", 2.0)
            gs = (st.get("status") or st).get("game_state") if isinstance(st, dict) else None
            phase = (gs or {}).get("phase")
            hl = await asyncio.to_thread(get, base + "/opus/v1/health", 2.0)
            polls.append({"t": round(now, 1), "phase": phase, "condition": (gs or {}).get("condition"),
                          "connected_headsets": hl.get("connected_headsets"), "events": len(st.get("events") or []) if isinstance(st, dict) else None})
            if ctl_addr and phase in ("induction", "witness", "agency") and now >= next_burst:
                next_burst = now + 7.0
                ctl.sendto(b"flinch", ctl_addr)
            shot = None
            if phase != last_phase:
                last_phase = phase
                print("  %6.1f s  phone hub sees phase %s (%s), headsets connected %s" % (now, phase, (gs or {}).get("condition"), hl.get("connected_headsets")), flush=True)
                shot = await asyncio.to_thread(screencap, serial, out / ("phone_%03ds_%s.png" % (int(now), phase or "none")))
            if now >= next_shot:
                next_shot += 20.0
                shot = await asyncio.to_thread(screencap, serial, out / ("phone_%03ds.png" % int(now)))
            if shot and shot != "ok":
                off_screen.append(round(now, 1))
                print("  %6.1f s  no picture: %s" % (now, shot), flush=True)
        text = (await reader).decode(errors="replace")
        (out / "unity_test_output.txt").write_text(text, encoding="utf-8")
        print("\n--- test output (tail)\n" + "\n".join(text.strip().splitlines()[-8:]), flush=True)
        await asyncio.sleep(8.0)          # the uploads finish after the test's last assert
        await asyncio.to_thread(screencap, serial, out / "phone_after.png")
        new = sorted((p for p in U.SESSIONS.glob("*") if p.is_dir() and p.name not in before), key=lambda p: p.stat().st_mtime)
        local = new[-1] if new else None
        print("local session:", local, flush=True)
        vrc = arc = None
        if local:
            vrc, _ = U.P.run_validate(local)
            arc, aout, _ = U.P.run_analytics(local)
            print(aout)
        seen = [p for p in polls if p["connected_headsets"]]
        phases = []
        for p in polls:
            if p["phase"] and (not phases or phases[-1] != p["phase"]):
                phases.append(p["phase"])
        print("phone hub: polls with a headset connected %d of %d; phases it reported: %s" % (len(seen), len(polls), phases))
        unanswered = [p["t"] for p in polls if p["connected_headsets"] is None]
        print("phone hub: polls it did not answer %d (at %s); the operator app was off the screen at %s" % (len(unanswered), unanswered[:12], off_screen))
        print("local session: validate exit %s, analytics exit %s" % (vrc, arc))
        green = "passed=1" in text and bool(seen) and vrc == 0
        (out / "result.json").write_text(json.dumps({"hub": hub, "nodes": found, "local": str(local), "validate_rc": vrc, "analytics_rc": arc,
                                                     "polls": polls, "phases_seen_by_hub": phases, "hub_unanswered_at": unanswered,
                                                     "app_off_screen_at": off_screen, "green": green}, indent=1), encoding="utf-8")
    finally:
        if twin is not None:
            await twin.stop()
        for k in env:
            print("  removed from the editor:", k, "->", U.editor_env("SetEnvironmentVariable", {"variable": k, "value": ""}), flush=True)
        if test is not None and test.returncode is None:
            test.kill()
    return 0 if green else 1


if __name__ == "__main__":
    argv = [a for a in sys.argv[1:] if a != "twin"]
    if len(argv) not in (2, 3):
        sys.exit(__doc__)
    sys.exit(asyncio.run(main(argv[0], Path(argv[1]).resolve(), argv[2] if len(argv) == 3 else "", "twin" in sys.argv[1:])))
