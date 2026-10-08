"""A faulted run of the GAME itself (Unity, the OPEN editor) against the sleeve twin and a recording hub.

    .venv\\Scripts\\python.exe tools\\demo\\unity_fault_run.py node_a_off|node_b_absent|hub_absent|none|hardware <out_dir>

`hardware` starts no twin: the two REAL nodes are found by their discovery beacons (UDP 8791) and the game is pointed at
them. No fault and no simulated flinch; the rows that need the twin's own log are left out. It buzzes the motors: nobody
wears the sleeve unless that is the point of the run, and no other tool may talk to the nodes meanwhile (their firmware
streams to the last sender only).

The L3 fault rows of `run_pipeline.py --faults` are played by the fake headset. This plays them with the game: the
PlayMode test PH_FullRun runs in its "external" mode against a hub and a twin this script owns, and the script injects
the fault by phase, read from the live status:

    node_a_off      `off-a` on the twin's control port 27 s into the SYNC induction (about half way through the run)
    node_b_absent   the twin starts without Node B (the test needs OPUS_PH_FAULT=node_b_absent to accept that)
    hub_absent      the hub is stopped for 30 s from 20 s into the witness phase, so the session ends while it is away

How the open editor is reached: OPUS_PH_HUB / _NODE_A / _NODE_B / _DISCOVERY_PORT / _FAULT are set inside the editor
PROCESS through the bridge (System.Environment.SetEnvironmentVariable) and removed again at the end, whatever happens.
Take game/.ph_unity.lock first: this needs the editor for about 5 minutes and port 8787 (LiveClient's hub port).
No sleeps wait for readiness; the 30 s is the outage itself. Run it on a quiet PC: a slow host drops cues as "late".
"""
import asyncio
import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

# the repository root, wherever this file sits (tools/demo/ in the repo, or the git-ignored sim/out/ copy)
REPO = next(p for p in Path(__file__).resolve().parents if (p / "tools" / "demo" / "phantom_pipeline.py").exists())
os.chdir(REPO)
sys.path.insert(0, str(REPO / "tools" / "demo"))
import phantom_pipeline as P  # noqa: E402
import l3_checks as L3  # noqa: E402

OFFSET, HUB_PORT, OUTAGE_S = 33000, 8787, 30.0   # 33000: clear of the tests (12100) and of run_pipeline.py (31000)
SESSIONS = Path(os.environ.get("USERPROFILE", "")) / "AppData" / "LocalLow" / "DefaultCompany" / "OPUS" / "opus_sessions"
NOT_LIVE = ("session_start", "session_end")      # these travel as session_started / session_ended, not as trial_events


def editor_env(method: str, args: dict) -> str:
    """A static System.Environment call inside the open editor, through the bridge."""
    req = {"method": "InvokeStaticMethodFromJson", "typeName": "System.Environment", "methodName": method,
           "arguments": json.dumps(args)}
    r = subprocess.run([sys.executable, "tools/unity_mcp.py", "call", "IReflectionService", json.dumps(req)],
                       capture_output=True, text=True, timeout=120, cwd=str(REPO))
    return (r.stdout + r.stderr).strip().replace("\n", " ")[:160]


async def main(fault: str, out: Path) -> int:
    out.mkdir(parents=True, exist_ok=True)
    hub_out, twin_log = out / "hub_sessions", out / "twin.jsonl"
    twin_log.unlink(missing_ok=True)
    before = {p.name for p in SESSIONS.glob("*")} if SESSIONS.exists() else set()
    hellos = []
    plain_hello = P.RecordingHub.handle_hello

    async def counting_hello(self, msg, ws):
        hellos.append(time.time())
        await plain_hello(self, msg, ws)
    P.RecordingHub.handle_hello = counting_hello

    marks, env, twin, hub, comeback, test, green = {}, {}, None, None, None, None, False
    hardware, found = fault == "hardware", {}
    try:
        if hardware:
            found = await P.discover_nodes(8791, ("haptic", "bio"), timeout=8.0)
            if "haptic" not in found or "bio" not in found:
                print("real nodes not found on UDP 8791 within 8 s:", found, flush=True)
                return 2
            ports = {"discovery": 8791}
        else:
            twin = P.TwinProc("haptic" if fault == "node_b_absent" else "both", OFFSET, 5, twin_log)
            ports = (await twin.wait_ready())["ports"]
        hub, _, _ = await P.start_hub("fake", hub_out, port=HUB_PORT)
        if hardware:    # the game listens for their sensor stream on UDP 8790 by default (no OPUS_PH_TELEMETRY_PORT)
            env = {"OPUS_PH_HUB": "127.0.0.1:%d" % HUB_PORT, "OPUS_PH_NODE_A": "%s:%d" % tuple(found["haptic"]),
                   "OPUS_PH_NODE_B": "%s:%d" % tuple(found["bio"]), "OPUS_PH_DISCOVERY_PORT": "8791"}
        else:
            env = P.unity_env("127.0.0.1", HUB_PORT, ports)
        if fault == "node_b_absent":
            del env["OPUS_PH_NODE_B"]
        if fault not in ("none", "hardware"):
            env["OPUS_PH_FAULT"] = fault
        print("real nodes" if hardware else "twin ports", json.dumps(found if hardware else ports), "| hub 127.0.0.1:%d" % HUB_PORT, flush=True)
        for k, v in env.items():
            print("  set in the editor:", k, "=", v, "->", editor_env("SetEnvironmentVariable", {"variable": k, "value": v}), flush=True)

        t0 = time.time()
        test = await asyncio.create_subprocess_exec(
            sys.executable, "tools/unity_mcp.py", "test", "PlayMode", "PH_FullRun", "--timeout", "900", "--out", str(out / "playmode.json"),
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT, cwd=str(REPO))
        reader = asyncio.create_task(test.stdout.read())
        ctl = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        ctl_addr = None if hardware else ("127.0.0.1", ports["control"])
        seen, impacts, sync_at, witness_at, phases = 0, set(), None, None, []
        while test.returncode is None:
            await asyncio.sleep(0.05)
            now = time.time()
            if now - t0 > 1000:
                test.kill()
                marks["killed"] = round(now - t0, 1)
                break
            ev = hub.recent_events
            while seen < len(ev):                       # the test sends no flinch in external mode: do it here, once per impact
                p = ev[seen]                            # (a reconnect replays old events: the seq tells them apart)
                if ctl_addr and isinstance(p, dict) and p.get("type") == "threat_impact" and p.get("seq") not in impacts:
                    impacts.add(p.get("seq"))
                    ctl.sendto(b"flinch", ctl_addr)
                seen += 1
            gs = (hub.status_records[-1] if hub.status_records else {}).get("game_state") or {}
            ph = gs.get("phase")
            if ph and (not phases or phases[-1][0] != ph):
                phases.append((ph, round(now - t0, 1)))
                print("  %6.1f s  phase %s (%s)" % (now - t0, ph, gs.get("condition")), flush=True)
            if fault == "node_a_off" and "off_a" not in marks and ph == "induction" and gs.get("condition") == "sync":
                sync_at = sync_at or now
                if now - sync_at >= 27.0:
                    ctl.sendto(b"off-a", ctl_addr)
                    marks["off_a"] = round(now - t0, 1)
                    print("  >>> %6.1f s  Node A switched off" % (now - t0), flush=True)
            if fault == "hub_absent" and "hub_gone" not in marks and ph == "witness":
                witness_at = witness_at or now
                if now - witness_at >= 20.0:
                    old = hub
                    marks["hub_gone"] = round(now - t0, 1)
                    print("  >>> %6.1f s  hub goes away for %.0f s" % (now - t0, OUTAGE_S), flush=True)
                    await old.stop()

                    async def back() -> None:
                        nonlocal hub
                        await asyncio.sleep(OUTAGE_S)           # the outage itself, not a readiness wait
                        hub, _, _ = await P.start_hub("fake", hub_out, port=HUB_PORT, reuse_from=old)
                        marks["hub_back"] = round(time.time() - t0, 1)
                        marks["hub_back_wall"] = time.time()
                        print("  >>> %6.1f s  hub is back" % (time.time() - t0), flush=True)
                    comeback = asyncio.create_task(back())
        text = (await reader).decode(errors="replace")
        if comeback:
            await comeback
        (out / "unity_test_output.txt").write_text(text, encoding="utf-8")
        print("\n--- test output (tail)\n" + "\n".join(text.strip().splitlines()[-12:]), flush=True)

        # ---- verdict inputs: the hub's copy of the session when it is whole, else the local one
        new = sorted((p for p in SESSIONS.glob("*") if p.is_dir() and p.name not in before), key=lambda p: p.stat().st_mtime)
        local = new[-1] if new else None
        hub_dirs = sorted((p for p in hub_out.glob("*") if p.is_dir()), key=lambda p: p.stat().st_mtime) if hub_out.exists() else []
        copy = hub_dirs[-1] if hub_dirs else None
        uploads = {f.name: bool(copy and (copy / f.name).exists()) for f in sorted(local.glob("*"))
                   if not f.name.startswith(".") and f.name != "metrics.json"} if local else {}
        target = copy if copy and (copy / "events.ndjson").exists() else local
        print("\nlocal session:", local, "\nhub copy:     ", copy, flush=True)
        vrc = arc = metrics = None
        events = []
        if target:
            vrc, _ = P.run_validate(target)
            arc, aout, metrics = P.run_analytics(target)
            events = L3.read_events(target)
            print(aout)
        if twin:
            await twin.stop()
        twin = None
        hs = hub.state
        status_records = list(hub.status_records)
        sts = [s for s in status_records if isinstance(s.get("game_state"), dict)]
        (out / "status_records.json").write_text(json.dumps(
            [{"game_state": s["game_state"], "_recv_ms": s.get("_recv_ms")} for s in sts]), encoding="utf-8")
        live = [e for e in events if e.get("type") not in NOT_LIVE]
        got = {p.get("seq") for p in hs.received_trial_events.values() if isinstance(p, dict)}
        lost = [(e.get("type"), e.get("seq")) for e in live if e.get("seq") not in got]
        rows = L3.core_rows(
            events=events, twin_log=[] if hardware else L3.read_jsonl(twin_log), params=L3.session_params(target) if target else {},
            rtt_ms=list(hs.rtt_samples or []), invalid=hs.invalid_count, invalid_msgs=hs.invalid_messages, status_seen=len(sts),
            validate_rc=vrc, uploads=uploads, expected_files=list(uploads),
            landed=sorted(p.name for p in target.glob("*")) if target else None,
            events_received=(len(live) - len(lost), len(live)), metrics=metrics, analytics_rc=arc, twin_expected=not hardware)
        complete = bool(uploads) and all(uploads.values()) and bool(events) and events[-1].get("type") == "session_end"
        cues = [(e.get("data") or {}) for e in events if e.get("type") == "haptic_cue"]
        extra = []
        if fault == "node_a_off":
            rows = [r for r in rows if r.check != "Stroke cues acked by the twin"]   # the cut is the point: judged below
            extra = L3.check_fault_node_a_off(events, status_records, metrics, complete)
            # the same cut with the host's own "late" drops set aside (a slow PC drops cues before the cut too)
            cut = next((i for i, c in enumerate(cues) if not c.get("delivered") and c.get("reason") != "late"), None)
            ok = cut is not None and cut > 0 and not any(c.get("delivered") for c in cues[cut:])
            extra.append(L3.Row("Fault A: nothing delivered after the cut (late drops set aside)", "0 delivered after it",
                                "cut at cue %s of %d; delivered after it: %d" % (cut, len(cues), sum(1 for c in cues[cut or 0:] if c.get("delivered")) if cut is not None else -1), ok))
        elif fault == "node_b_absent":
            extra = L3.check_fault_node_b_absent(events, status_records, metrics, complete, target)
        elif fault == "hub_absent":
            back_at = marks.get("hub_back_wall", 9e18)
            late_files = [p.name for p in (copy.glob("*") if copy else []) if p.name != "metrics.json" and p.stat().st_mtime >= back_at]
            extra = [
                L3.Row("Fault C: hub absent %.0f s: run completes" % OUTAGE_S, "yes", "run complete=%s" % complete, complete),
                L3.Row("Fault C: websocket re-established after the outage", "a hello after the comeback",
                       "%d hello(s); after the comeback: %d" % (len(hellos), sum(1 for h in hellos if h >= back_at)), any(h >= back_at for h in hellos)),
                L3.Row("Fault C: the session's files landed after the hub came back", "all, written after the comeback",
                       "%d of %d files on the hub; %d written after the comeback" % (sum(uploads.values()), len(uploads), len(late_files)),
                       bool(uploads) and all(uploads.values()) and len(late_files) > 0),
                L3.Row("Fault C: every trial_event arrived exactly once after resume", "all",
                       "%d/%d; missing: %s" % (len(live) - len(lost), len(live), lost or "none"), bool(live) and not lost),
            ]
        print(L3.render_text(rows, "UNITY RUN, fault=%s: core rows" % fault))
        if extra:
            print(L3.render_text(extra, "UNITY RUN, fault=%s: fault rows" % fault))
        tally = {}
        for c in cues:
            key = "delivered" if c.get("delivered") else "undelivered:" + str(c.get("reason"))
            tally[key] = tally.get(key, 0) + 1
        print("\ncues:", json.dumps(tally), "| flinch commands sent:", len(impacts), "| hellos:", len(hellos),
              "| trial_events at the hub:", len(hs.received_trial_events), "of", len(live), "| missing:", lost or "none")
        print("marks (s after the test was started):", json.dumps({k: v for k, v in marks.items() if not k.endswith("_wall")}),
              "| phases:", phases)
        green = all(r.ok for r in rows + extra if r.gating)
        (out / "result.json").write_text(json.dumps({
            "fault": fault, "green": green, "marks": marks, "phases": phases, "cues": tally, "flinches": len(impacts),
            "hellos": len(hellos), "trial_events_lost": lost, "complete": complete, "local": str(local), "copy": str(copy),
            "rows": [[r.check, r.threshold, r.observed, r.ok] for r in rows + extra]}, indent=1), encoding="utf-8")
    finally:
        for k in env:
            print("  removed from the editor:", k, "->", editor_env("SetEnvironmentVariable", {"variable": k, "value": ""}), flush=True)
        print("  OPUS_PH_HUB in the editor now:", editor_env("GetEnvironmentVariable", {"variable": "OPUS_PH_HUB"}), flush=True)
        if test is not None and test.returncode is None:
            test.kill()
        if comeback and not comeback.done():
            comeback.cancel()
        if twin:
            await twin.stop()
        if hub is not None:
            try:
                await P.stop_hub(hub)
            except Exception:  # noqa: BLE001 - a hub that is already down is fine here
                pass
    return 0 if green else 1


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] not in ("node_a_off", "node_b_absent", "hub_absent", "none", "hardware"):
        sys.exit(__doc__)
    sys.exit(asyncio.run(main(sys.argv[1], Path(sys.argv[2]).resolve())))
