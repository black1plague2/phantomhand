"""tools/demo/phantom_pipeline.py -- `run_pipeline.py --game phantom_hand` (G2 / L3, 04-E2E.md).

    --sim [--no-unity]   hub + sleeve twin (--kind both) on offset ports + (Unity PlayMode PH_FullRun | the
                         fake headset replaying the phantom_hand_min fixture through the live protocol and
                         driving the twin with real stroke datagrams). Readiness waits only: the twin prints
                         {"event":"ready"}, discovery announcements are awaited, node streams are awaited.
                         Afterwards: validate.py --session, `python -m opus_analytics --summary`, then the L3
                         table (tools/demo/l3_checks.py) printed and written as JSON + markdown.
    --faults             the three fault runs of the table: Node A off at 50 %, Node B absent, hub absent 30 s.
    --hardware           same checks against real nodes (discovery on the real ports, no twin). Add --spinup to
                         measure motor start delay (IMU spike after a single pulse, median of 10).
    --lan                the electronics are on another PC (or may be): the twin binds 0.0.0.0 (its control port stays on
                         127.0.0.1), the Unity PlayMode test gets no OPUS_PH_NODE_* overrides (real discovery), and the exact
                         command that runs the twin on a second PC is printed. Default behaviour is unchanged without it.
    --dialect team       the twin speaks the electronics team's own firmware dialect (contracts HAPTIC_PROTOCOL v1.3).

Run with the sim/live venv (aiohttp + numpy):
    sim\\live\\.venv\\Scripts\\python.exe tools\\demo\\run_pipeline.py --game phantom_hand --sim --no-unity

PORTS: everything is derived from --port-offset (default 31000: Node A 31000+8790, discovery +8791, Node B +8792,
twin control +8793) and the hub takes an ephemeral port. 8787/8788/8790/8791 are never bound in --sim.
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import socket
import statistics
import subprocess
import sys
import tempfile
import threading
import time
from datetime import date
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[2]
for _p in (REPO_ROOT / "sim" / "live", REPO_ROOT / "sim", REPO_ROOT, Path(__file__).resolve().parent):
    if str(_p) not in sys.path:
        sys.path.insert(0, str(_p))

import l3_checks as L3  # noqa: E402
from tool_paths import find_dart, find_unity  # noqa: E402

try:
    from fake_hub import FakeHub  # noqa: E402
    from phantom_replay import (DEFAULT_FIXTURE, NodeLink, PhantomHeadset, discover_nodes,  # noqa: E402
                                prepare_session_copy)
    SIM_IMPORT_ERROR: Optional[str] = None
except ImportError as _exc:      # pragma: no cover - wrong venv
    FakeHub = PhantomHeadset = NodeLink = discover_nodes = prepare_session_copy = None  # type: ignore
    DEFAULT_FIXTURE = REPO_ROOT / "contracts" / "fixtures" / "sessions" / "phantom_hand_min"
    SIM_IMPORT_ERROR = f"{type(_exc).__name__}: {_exc}"

VALIDATE_PY = REPO_ROOT / "contracts" / "validate.py"
ANALYTICS_DIR = REPO_ROOT / "analytics"
ANALYTICS_PY = ANALYTICS_DIR / ".venv" / "Scripts" / "python.exe"
if not ANALYTICS_PY.exists():  # single-venv layout (repo-root .venv): the running interpreter has opus_analytics too
    ANALYTICS_PY = Path(sys.executable)
DART = find_dart()          # OPUS_DART, FLUTTER_ROOT, PATH, H:\flutter\bin, then the old C:\flutter (tool_paths.py)
UNITY_EXE = find_unity()    # OPUS_UNITY_EXE, the Hub editor game/ProjectSettings pins (not PATH), then the old path
UNITY_LOCK = REPO_ROOT / "game" / ".ph_unity.lock"
DEFAULT_PORT_OFFSET = 31000


def banner(title: str) -> None:
    print(f"\n{'=' * 100}\n== {title}\n{'=' * 100}", flush=True)


def free_tcp_port() -> int:
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


# ============================================================================ the hub
if FakeHub is not None:
    class RecordingHub(FakeHub):
        """fake_hub that also remembers every status payload (game_state + trace), pings the headset once a second
        so the hub measures RTT itself, and serves GET /opus/v1/live/last_status for tools/demo/live_plot.py --hub."""

        def __init__(self, *a: Any, **kw: Any) -> None:
            super().__init__(*a, **kw)
            self.status_records: List[Dict[str, Any]] = []
            self.recent_events: List[Dict[str, Any]] = []
            self.ping_tasks: List[asyncio.Task] = []
            self.app.router.add_get("/opus/v1/live/last_status", self._last_status)

        async def _last_status(self, request: Any) -> Any:
            from aiohttp import web
            return web.json_response({"status": self.status_records[-1] if self.status_records else None,
                                      "events": self.recent_events[-60:]})

        async def handle_message(self, data: str, ws: Any) -> None:
            await super().handle_message(data, ws)
            try:
                m = json.loads(data)
            except ValueError:
                return
            if m.get("type") == "status":
                p = dict(m.get("payload") or {})
                p["_recv_ms"] = time.time() * 1000.0
                self.status_records.append(p)
            elif m.get("type") == "trial_event":
                self.recent_events.append(m.get("payload") or {})

        async def handle_hello(self, msg: Dict[str, Any], ws: Any) -> None:
            await super().handle_hello(msg, ws)
            sid = msg.get("session_id") or "session-000"
            self.ping_tasks.append(asyncio.create_task(self.send_ping_loop(ws, sid)))

        async def stop(self) -> None:
            for t in self.ping_tasks:
                t.cancel()
            ws = self.scenario_headset_ws
            if ws is not None and not ws.closed:
                await ws.close()          # otherwise aiohttp waits its shutdown_timeout for the open socket
            await super().stop()


async def start_hub(kind: str, out_dir: Path, port: Optional[int] = None, reuse_from: Optional[Any] = None) -> Tuple[Any, str, int]:
    """Returns (hub handle, host, port). kind: fake | flutter. Readiness = the hub answers /health (fake) or prints
    its 'listening' line (flutter)."""
    port = port or free_tcp_port()
    if kind == "fake":
        hub = RecordingHub(port=port, beacon=False, scenario=None, beacon_port=free_tcp_port(), out_dir=out_dir)
        if reuse_from is not None:                         # restart after a simulated outage keeps the books
            hub.state = reuse_from.state
            hub.status_records = reuse_from.status_records
            hub.recent_events = reuse_from.recent_events
        await hub.start()
        return hub, "127.0.0.1", port
    if kind == "flutter":
        exe = str(DART) if DART.exists() else "dart"
        proc = subprocess.Popen([exe, "run", "tool/hub_cli.dart", "--port", str(port), "--no-beacon",
                                 "--data-dir", str(out_dir)], cwd=str(REPO_ROOT / "app"), stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, text=True)
        ready = threading.Event()

        def pump() -> None:
            for line in proc.stdout:           # type: ignore[union-attr]
                if "listening" in line:
                    ready.set()

        threading.Thread(target=pump, daemon=True).start()
        if not await asyncio.get_running_loop().run_in_executor(None, ready.wait, 180.0):
            proc.kill()
            raise RuntimeError("flutter hub_cli did not report 'listening' in 180 s")
        return proc, "127.0.0.1", port
    raise ValueError(kind)


async def stop_hub(hub: Any) -> None:
    if hasattr(hub, "terminate"):                    # `dart.bat` leaves dart/dartvm children: kill the whole tree
        if os.name == "nt":
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(hub.pid)], capture_output=True)
        else:
            hub.terminate()
    else:
        await hub.stop()


# ============================================================================ the twin
def twin_command(kind: str, offset: int, seed: int, log_path: Path, host: Optional[str] = None,
                 dialect: Optional[str] = None) -> List[str]:
    """argv of sim/sleeve/twin.py as the harness starts it. The defaults (loopback, reference dialect) add no flag."""
    cmd = [sys.executable, "-m", "sim.sleeve.twin", "--kind", kind, "--port-offset", str(offset), "--seed", str(seed),
           "--log", str(log_path), "--no-stdin"]
    if host:
        cmd += ["--host", host]
    if dialect and dialect != "reference":
        cmd += ["--dialect", dialect]
    return cmd


def twin_host(args: argparse.Namespace) -> Optional[str]:
    """--lan binds the twin to every interface; otherwise None = the twin's own default (127.0.0.1)."""
    return "0.0.0.0" if getattr(args, "lan", False) else None


def unity_env(hub_host: str, hub_port: int, ports: Dict[str, int], lan: bool = False) -> Dict[str, str]:
    """The OPUS_PH_* endpoints handed to the PlayMode test. With `lan` (or real nodes) there are NO node overrides: the
    game finds Node A / Node B by their discovery beacons, whichever PC they run on."""
    env = {"OPUS_PH_HUB": f"{hub_host}:{hub_port}"}
    if not lan:
        env["OPUS_PH_NODE_A"] = f"127.0.0.1:{ports['haptic']}"
        env["OPUS_PH_NODE_B"] = f"127.0.0.1:{ports['bio']}"
    env["OPUS_PH_DISCOVERY_PORT"] = str(ports["discovery"])
    return env


def second_pc_command(args: argparse.Namespace) -> str:
    """The command line that runs the twin on another PC (real ports, so a harness or the game finds it by discovery)."""
    cmd = ["python", r"sim\sleeve\twin.py", "--kind", "both", "--host", "0.0.0.0", "--port-offset", "0",
           "--seed", str(getattr(args, "seed", 1))]
    if getattr(args, "dialect", "reference") != "reference":
        cmd += ["--dialect", args.dialect]
    return " ".join(cmd)


def lan_banner(args: argparse.Namespace) -> str:
    """What --lan prints once: where the twin runs and how to run it on a second PC instead."""
    dialect = getattr(args, "dialect", "reference")
    if getattr(args, "hardware", False):
        where = f"no twin is started here (--hardware): the nodes are found by discovery on UDP {args.discovery_port}"
    else:
        where = (f"the twin runs on THIS PC bound to 0.0.0.0 (control port stays on 127.0.0.1) and announces itself on every "
                 f"local network, ports {8790 + args.port_offset}/{8791 + args.port_offset}/{8792 + args.port_offset}")
    return "\n".join([
        "LAN MODE: " + where,
        f"  dialect: {dialect}",
        r"  To run the electronics on a SECOND PC instead (Python only, stdlib: copy sim\sleeve\twin.py or clone the repo):",
        "      " + second_pc_command(args),
        "  then, on THIS PC, aim the harness at the real ports (no twin is started here):",
        r"      python tools\demo\run_pipeline.py --game phantom_hand --hardware --discovery-port 8791 --lan"
        + (f" --dialect {dialect}" if dialect != "reference" else ""),
        "  Windows Firewall: second PC UDP 8790 + 8792 in; this PC UDP 8791 in for python.exe / Unity.exe, hub TCP 8787 for a Quest.",
        r"  tools\demo\open_firewall.ps1 reports the current rules (-Twin / -AllowUnity fix them, as Administrator).",
        "  Control commands (flinch, off-a, loss ...) work only on the twin's own PC: type them into its console (stdin).",
        "  Unity: with no OPUS_PH_NODE_* the PlayMode test must accept real discovery (CROSS-TRACK REQUEST, see the run log).",
    ])


class TwinProc:
    """sim/sleeve/twin.py as a subprocess. Ready when it prints {"event":"ready",...} (no sleeping)."""

    def __init__(self, kind: str, offset: int, seed: int, log_path: Path, host: Optional[str] = None,
                 dialect: Optional[str] = None):
        self.kind, self.offset, self.log_path = kind, offset, log_path
        self.proc = subprocess.Popen(
            twin_command(kind, offset, seed, log_path, host, dialect), cwd=str(REPO_ROOT), stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, text=True)
        self.ready: Dict[str, Any] = {}
        self._evt = threading.Event()
        self.lines: List[str] = []
        threading.Thread(target=self._pump, daemon=True).start()

    def _pump(self) -> None:
        for line in self.proc.stdout:          # type: ignore[union-attr]
            self.lines.append(line.strip())
            try:
                ev = json.loads(line)
            except ValueError:
                continue
            if ev.get("event") == "ready":
                self.ready = ev
                self._evt.set()

    async def wait_ready(self, timeout: float = 15.0) -> Dict[str, Any]:
        ok = await asyncio.get_running_loop().run_in_executor(None, self._evt.wait, timeout)
        if not ok:
            raise RuntimeError("twin did not print its ready line")
        return self.ready

    @property
    def ports(self) -> Dict[str, int]:
        return self.ready["ports"]

    async def stop(self) -> None:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.sendto(b"quit", ("127.0.0.1", self.ports["control"]))
            s.close()
        except (OSError, KeyError):
            pass
        loop = asyncio.get_running_loop()
        try:
            await loop.run_in_executor(None, lambda: self.proc.wait(timeout=5))
        except subprocess.TimeoutExpired:
            self.proc.kill()


# ============================================================================ subprocess helpers
def run_validate(session_dir: Path) -> Tuple[int, str]:
    proc = subprocess.run([str(ANALYTICS_PY), str(VALIDATE_PY), "--session", str(session_dir)],
                          capture_output=True, text=True)
    out = (proc.stdout + proc.stderr).strip()
    return proc.returncode, out


def run_analytics(session_dir: Path) -> Tuple[int, str, Optional[Dict[str, Any]]]:
    proc = subprocess.run([str(ANALYTICS_PY), "-m", "opus_analytics", str(session_dir), "--summary"],
                          capture_output=True, text=True, cwd=str(ANALYTICS_DIR))
    mp = session_dir / "metrics.json"
    metrics = json.loads(mp.read_text(encoding="utf-8")) if mp.exists() else None
    return proc.returncode, (proc.stdout + proc.stderr).strip(), metrics


# ============================================================================ Unity path (wired, NOT run by the L3 smoke)
async def run_unity_playmode(hub_host: str, hub_port: int, ports: Dict[str, int], out_dir: Path,
                             timeout_s: float = 900.0, lan: bool = False) -> int:
    """Batch PlayMode PH_FullRun (UNITY.md U5/U6). Takes game/.ph_unity.lock (01-ORCHESTRATION section 5), runs
    Unity with the live endpoints in environment variables, releases the lock. NOTE: the env var names are this
    harness's proposal (CROSS-TRACK REQUEST to the Unity track); the test must read them to point at the hub/twin."""
    if UNITY_LOCK.exists() and time.time() - UNITY_LOCK.stat().st_mtime < 30 * 60:
        print(f"  Unity lock {UNITY_LOCK} is younger than 30 min: another Unity agent is running. Not starting.")
        return 3
    UNITY_LOCK.write_text(f"S2 run_pipeline {time.strftime('%Y-%m-%dT%H:%M:%S')}\n", encoding="utf-8")
    try:
        xml = out_dir / "ph_fullrun_playmode.xml"
        env = dict(os.environ, **unity_env(hub_host, hub_port, ports, lan))
        cmd = [str(UNITY_EXE), "-batchmode", "-projectPath", str(REPO_ROOT / "game"), "-runTests",
               "-testPlatform", "PlayMode", "-testFilter", "PH_FullRun", "-testResults", str(xml),
               "-logFile", str(out_dir / "unity_playmode.log")]
        proc = await asyncio.create_subprocess_exec(*cmd, env=env)
        try:
            return await asyncio.wait_for(proc.wait(), timeout_s)
        except asyncio.TimeoutError:
            proc.kill()
            return 124
    finally:
        UNITY_LOCK.unlink(missing_ok=True)


# ============================================================================ the run (sim)
async def one_run(args: argparse.Namespace, *, name: str, node_b: bool = True, off_a_at: Optional[float] = None,
                  hub_outage: Optional[Tuple[float, float]] = None) -> Dict[str, Any]:
    """One full L3 run. Returns everything the table needs. hub_outage = (fraction, seconds)."""
    out = Path(args.out) / name
    out.mkdir(parents=True, exist_ok=True)
    hub_out = out / "hub_sessions"
    t0 = time.time()
    result: Dict[str, Any] = {"name": name}

    banner(f"[{name}] twin + hub")
    twin: Optional[TwinProc] = None
    hub: Any = None
    ext_hub = bool(args.hub) and args.hub not in ("fake", "flutter")
    twin_log = out / "twin.jsonl"
    twin_log.unlink(missing_ok=True)
    try:
        if not args.hardware:
            twin = TwinProc("both" if node_b else "haptic", args.port_offset, args.seed, twin_log,
                            host=twin_host(args), dialect=getattr(args, "dialect", None))
            ready = await twin.wait_ready()
            print(f"  twin ready: {json.dumps(ready['ports'])} (kinds {ready['kinds']})")
        if ext_hub:
            hub_host, _, hp = args.hub.partition(":")
            hub_port = int(hp or 8787)
        else:
            hub, hub_host, hub_port = await start_hub(args.hub or "fake", hub_out, port=args.hub_port or None)
        print(f"  hub {'external' if ext_hub else (args.hub or 'fake')} at {hub_host}:{hub_port}")

        # ---- nodes: discovery is the readiness signal
        banner(f"[{name}] discovery")
        disc_port = args.discovery_port if args.hardware else twin.ports["discovery"]
        wanted = ("haptic", "bio") if node_b else ("haptic",)
        found = await discover_nodes(disc_port, wanted, timeout=8.0)
        print(f"  discovered: {found}")
        result["discovered"] = {k: list(v) for k, v in found.items()}
        if "haptic" not in found:
            raise RuntimeError("Node A never announced itself on the discovery port")

        # ---- unity path
        if not args.no_unity:
            banner(f"[{name}] Unity batch PlayMode PH_FullRun (wired, not run by S2)")
            rc = await run_unity_playmode(hub_host, hub_port, twin.ports if twin else {"discovery": args.discovery_port},
                                          out, lan=getattr(args, "lan", False) or twin is None)
            result["unity_rc"] = rc
            sid_dirs = sorted(p for p in hub_out.glob("*") if p.is_dir()) if hub_out.exists() else []
            session_dir = sid_dirs[-1] if sid_dirs else None
            headset = None
        else:
            banner(f"[{name}] fake_headset --game phantom_hand (fixture replay)")
            work = out / "headset_session"
            sdir, sid = prepare_session_copy(Path(args.fixture), work, drop_bio=("bio" not in found))
            control = ("127.0.0.1", twin.ports["control"]) if twin else None
            headset = PhantomHeadset(sdir, node_a=found.get("haptic"), node_b=found.get("bio"), control=control,
                                     host=hub_host, port=hub_port, speed=args.ph_speed, patient_ref="ph-demo-001",
                                     compress_gap_ms=args.compress_gap_ms, off_a_at=off_a_at)
            headset.state.session_id = sid
            await headset.start_nodes()
            streams = await headset.wait_node_streams(timeout=8.0)
            print(f"  node streams flowing: {streams}")
            if hub_outage:
                frac, secs = hub_outage

                async def outage() -> None:
                    nonlocal hub
                    print(f"  >>> hub goes away for {secs:.0f} s (fraction {frac:.2f} of the events sent)")
                    old = hub
                    await old.stop()

                    async def comeback() -> None:
                        nonlocal hub
                        await asyncio.sleep(secs)           # the outage itself, not a readiness wait
                        hub, _, _ = await start_hub("fake", hub_out, port=hub_port, reuse_from=old)
                        result["hub_back_at"] = time.time()
                        print("  >>> hub is back")
                    result["comeback"] = asyncio.create_task(comeback())
                    result["hub_gone_at"] = time.time()
                headset.progress_hooks.append((frac, outage))
                headset.progress_hooks.sort(key=lambda h: h[0])
            await headset.connect_hub(hub_host, hub_port)
            ping = asyncio.create_task(headset.measure_latency())
            t_rep = time.monotonic()
            await headset.replay_session()
            result["replay_s"] = time.monotonic() - t_rep
            ping.cancel()
            if "comeback" in result:
                await result.pop("comeback")
            await headset.disconnect()
            result["headset_report"] = headset.report
            session_dir = (hub_out / sid) if (hub_out / sid).exists() else None
            result["session_id"] = sid
        result["session_dir"] = str(session_dir) if session_dir else None

        # ---- verdict inputs
        banner(f"[{name}] contracts + analytics")
        events: List[Dict[str, Any]] = []
        metrics = None
        vrc = arc = None
        vout = aout = ""
        landed = None
        if session_dir:
            vrc, vout = run_validate(session_dir)
            print("  " + "\n  ".join(vout.splitlines()[-4:]))
            arc, aout, metrics = run_analytics(session_dir)
            events = L3.read_events(session_dir)
            landed = sorted(p.name for p in session_dir.glob("*"))
            print(aout)
        result.update(validate_rc=vrc, analytics_rc=arc, analytics_out=aout, events=len(events))
        if twin:
            await twin.stop()
            twin = None
        tlog = L3.read_jsonl(twin_log)
        rpt = result.get("headset_report", {})
        hub_state = getattr(hub, "state", None)
        rtt = list(hub_state.rtt_samples) if hub_state and hub_state.rtt_samples else (
            list(headset.state.rtt_samples) if headset else [])
        status_records = list(getattr(hub, "status_records", []))
        sts_with_gs = [s for s in status_records if isinstance(s.get("game_state"), dict)]
        (out / "status_records.json").write_text(json.dumps(
            [{"game_state": s["game_state"], "_recv_ms": s.get("_recv_ms")} for s in sts_with_gs]), encoding="utf-8")
        (out / "headset_report.json").write_text(json.dumps(
            {k: v for k, v in rpt.items() if k != "cue_rtt_ms"}, indent=1), encoding="utf-8")
        uploads = {k: v for k, v in (rpt.get("upload_ok") or {}).items()}
        expected = list(uploads) if uploads else []
        params = L3.session_params(session_dir) if session_dir else {}
        rec_events = None
        if hub_state is not None and headset is not None:
            rec_events = (len(hub_state.received_trial_events), len(headset.events_out))
        rows = L3.core_rows(
            events=events, twin_log=tlog, params=params, rtt_ms=rtt,
            invalid=(hub_state.invalid_count if hub_state else None),
            invalid_msgs=(hub_state.invalid_messages if hub_state else []),
            status_seen=len(sts_with_gs) if hasattr(hub, 'status_records') else int(rpt.get('statuses_sent', 0)),
            validate_rc=vrc, uploads=uploads, expected_files=expected, landed=landed, events_received=rec_events,
            metrics=metrics, analytics_rc=arc, twin_expected=not args.hardware)
        extra: List[L3.Row] = []
        complete = bool(uploads) and all(uploads.values()) and bool(events) and events[-1].get("type") == "session_end"
        if name == "fault_node_a_off":
            extra = L3.check_fault_node_a_off(events, status_records, metrics, complete)
        elif name == "fault_node_b_absent":
            extra = L3.check_fault_node_b_absent(events, status_records, metrics, complete, session_dir)
        elif name == "fault_hub_absent":
            extra = L3.check_fault_hub_absent(events, rpt, hub_outage[1] if hub_outage else 0.0,
                                              rec_events or (0, 1), complete, bool(uploads) and all(uploads.values()))
        if name == "fault_node_a_off":      # the cut is the point of this run: its ack row is judged in the fault rows
            rows = [r for r in rows if r.check != "Stroke cues acked by the twin"]
        result["rows"] = rows
        result["fault_rows"] = extra
        result["wall_s"] = time.time() - t0
        return result
    finally:
        if twin:
            await twin.stop()
        if hub is not None:
            try:
                await stop_hub(hub)
            except Exception:
                pass


def write_reports(out: Path, runs: List[Dict[str, Any]]) -> None:
    j = [{"name": r["name"], "session_dir": r.get("session_dir"), "wall_s": r.get("wall_s"),
          "rows": [x.as_dict() for x in r.get("rows", [])], "fault_rows": [x.as_dict() for x in r.get("fault_rows", [])],
          "headset_report": {k: v for k, v in (r.get("headset_report") or {}).items() if k != "cue_rtt_ms"}} for r in runs]
    (out / "l3_report.json").write_text(json.dumps(j, indent=2), encoding="utf-8")
    md = []
    for r in runs:
        md.append(L3.render_markdown(r.get("rows", []), f"G2 / L3 table - {r['name']}"))
        if r.get("fault_rows"):
            md.append(L3.render_markdown(r["fault_rows"], f"Fault checks - {r['name']}"))
    (out / "l3_report.md").write_text("\n\n".join(md) + "\n", encoding="utf-8")


async def run_spinup(args: argparse.Namespace) -> int:
    """--spinup: median of 10 single pulses on Node A (real node via discovery / --a-ip, or the twin with --sim)."""
    twin: Optional[TwinProc] = None
    try:
        if args.sim and not args.hardware:
            twin = TwinProc("haptic", args.port_offset, args.seed, Path(tempfile.mkdtemp(prefix="ph_spinup_")) / "twin.jsonl",
                            host=twin_host(args), dialect=getattr(args, "dialect", None))
            await twin.wait_ready()
            disc = twin.ports["discovery"]
        else:
            disc = args.discovery_port
        if args.a_ip:
            host, _, p = args.a_ip.partition(":")
            addr = (host, int(p or 8790))
        else:
            found = await discover_nodes(disc, ("haptic",), timeout=8.0)
            if "haptic" not in found:
                print("Node A not discovered")
                return 2
            addr = found["haptic"]
        print(f"measuring motor start delay on Node A at {addr[0]}:{addr[1]} (10 single pulses)")
        res = await measure_spinup(addr, 10)
        for t in res["trials"]:
            print(f"  pulse {t['trial']}: acked={t['acked']} ack_rtt={t['ack_rtt_ms'] and round(t['ack_rtt_ms'], 1)} ms "
                  f"IMU spike at +{t['spike_ms'] and round(t['spike_ms'], 1)} ms -> start delay "
                  f"{t['start_delay_ms'] and round(t['start_delay_ms'], 1)} ms")
        print(f"MEDIAN motor start delay: {res['median_start_delay_ms']} ms  ({res['detected']}/{res['n']} detected)")
        if args.out:
            Path(args.out).mkdir(parents=True, exist_ok=True)
            (Path(args.out) / "spinup.json").write_text(json.dumps(res, indent=2), encoding="utf-8")
        return 0 if res["detected"] >= 8 else 1
    finally:
        if twin:
            await twin.stop()


async def run_phantom(args: argparse.Namespace) -> int:
    if args.spinup:
        return await run_spinup(args)
    args.out = args.out or str(Path(tempfile.mkdtemp(prefix=f"ph_l3_{date.today().isoformat()}_")))
    Path(args.out).mkdir(parents=True, exist_ok=True)
    print(f"output dir: {args.out}")
    if getattr(args, "lan", False):
        print(lan_banner(args), flush=True)
    runs: List[Dict[str, Any]] = []
    plan: List[Tuple[str, Dict[str, Any]]] = [("main", {})]
    if args.faults:
        plan = [("fault_node_a_off", {"off_a_at": 0.5}),
                ("fault_node_b_absent", {"node_b": False}),
                ("fault_hub_absent", {"hub_outage": (0.9, args.hub_outage_s)})]
        if args.with_main:
            plan.insert(0, ("main", {}))
    for name, kw in plan:
        try:
            runs.append(await one_run(args, name=name, **kw))
        except Exception as e:      # a harness failure must be loud and must not hide the other runs
            import traceback
            traceback.print_exc()
            runs.append({"name": name, "rows": [L3.Row("Harness", "runs", f"{type(e).__name__}: {e}", False)]})
    ok = True
    for r in runs:
        banner(f"RESULT {r['name']}  (wall {r.get('wall_s', 0):.0f} s, session {r.get('session_dir')})")
        print(L3.render_text(r.get("rows", [])))
        if args.no_unity and not args.hardware:
            print("NOTE (--no-unity): the stroke stamps (brush_pass / cue_send / timing_err_ms) are the fixture's, so the\n"
                  "      SYNC-timing and ASYNC-delay rows check the table logic on recorded stamps; acks, RTT, statuses,\n"
                  "      sensor files, uploads and analytics are live. The Unity PlayMode path (no --no-unity) measures\n"
                  "      real stroke timing.")
        if r.get("fault_rows"):
            print()
            print(L3.render_text(r["fault_rows"], "FAULT CHECKS"))
        ok = ok and all(x.passed for x in r.get("rows", [])) and all(x.passed for x in r.get("fault_rows", []))
        hr = r.get("headset_report")
        if hr:
            print("\n  headset report: " + json.dumps({k: v for k, v in hr.items()
                                                       if k not in ("cue_rtt_ms", "upload_ok", "cue_rejected", "oled")}))
    write_reports(Path(args.out), runs)
    print(f"\nreports: {Path(args.out) / 'l3_report.json'}  {Path(args.out) / 'l3_report.md'}")
    print(f"\nPHANTOM HAND L3 {'GREEN' if ok else 'NOT GREEN'}")
    return 0 if ok else 1


# ============================================================================ --spinup (hardware)
def spike_delay_ms(samples: List[Tuple[float, float]], t_send: float, base_mean: float, base_sd: float,
                   k: float = 6.0, floor: float = 0.25) -> Optional[float]:
    """First sample after `t_send` (monotonic s) whose |accel| deviates from the baseline by > max(k*sd, floor);
    returns ms from the send to that sample's arrival, or None."""
    thr = max(k * base_sd, floor)
    for t, mag in samples:
        if t >= t_send and abs(mag - base_mean) > thr:
            return (t - t_send) * 1000.0
    return None


async def measure_spinup(addr: Tuple[str, int], n: int = 10) -> Dict[str, Any]:
    """Motor start delay on Node A: a single stroke pulse, detect the vibration in the IMU, report the median.
    start_delay = spike arrival - send - ack_rtt/2 (our estimate of the command's one-way trip). Resolution is the
    IMU's 10 ms sample period. Readiness/settling are awaited on incoming samples, never sleeps."""
    q: "asyncio.Queue[Tuple[float, float]]" = asyncio.Queue()
    from phantom_replay import accel_magnitude

    def on_msg(_n: str, msg: Dict[str, Any]) -> None:
        if msg.get("type") == "sensor_data":
            m = accel_magnitude(msg)
            if m is not None:
                q.put_nowait((time.monotonic(), m))

    link = NodeLink("haptic", addr, on_msg)
    await link.open()
    link.send({"type": "subscribe"})
    results: List[Dict[str, Any]] = []
    try:
        await asyncio.wait_for(link.first_data.wait(), 5.0)
        for i in range(n):
            link.send({"type": "subscribe"})
            base: List[float] = []
            while len(base) < 50:                       # 0.5 s of quiet IMU at 100 Hz = baseline
                base.append((await asyncio.wait_for(q.get(), 2.0))[1])
            mean = statistics.fmean(base)
            sd = statistics.pstdev(base)
            while not q.empty():
                q.get_nowait()
            t_send = time.monotonic()
            ack, rtt = await link.request({"cue_id": f"spin-{i}", "motor": 0, "intensity": 150, "duration_ms": 200,
                                           "pattern": "pulse", "cue": "stroke"}, f"spin-{i}", timeout=1.0)
            seen: List[Tuple[float, float]] = []
            for _ in range(60):                         # watch 0.6 s of samples after the send
                seen.append(await asyncio.wait_for(q.get(), 2.0))
            d = spike_delay_ms(seen, t_send, mean, sd)
            results.append({"trial": i, "acked": bool(ack), "ack_rtt_ms": rtt,
                            "spike_ms": d, "start_delay_ms": (d - (rtt or 0) / 2.0) if d is not None else None})
            for _ in range(80):                         # let the duty window / gap recover: 0.8 s of samples
                await asyncio.wait_for(q.get(), 2.0)
    finally:
        link.close()
    vals = [r["start_delay_ms"] for r in results if r["start_delay_ms"] is not None]
    return {"n": n, "detected": len(vals), "median_start_delay_ms": statistics.median(vals) if vals else None,
            "trials": results}
