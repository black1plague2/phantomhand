#!/usr/bin/env python3
"""tools/demo/run_pipeline.py -- ONE COMMAND that proves game -> UDP/WS -> hub -> phone.

This is the repeatable version of the manual multi-terminal demo ritual described in
`tools/demo/START_DEMO.md` and `docs/TESTING_RUNBOOK.md` section 6. It:

  1. generates (or replays) a realistic session,
  2. starts the haptic sleeve simulator (`sim/haptic/fake_haptic.py`) on an ephemeral UDP port,
  3. starts a local hub (`sim/live/fake_hub.py`) on ephemeral ports  -- OR targets a real hub
     (the Flutter hub on a phone) with `--hub <ip:port>`,
  4. drives `sim/live/fake_headset.py` as the headset: `status` ~2 Hz, every `trial_event`,
     a `metrics_tick` per trial, then HTTP-PUTs session.json/events.ndjson/kin_*.json,
  5. fires a real `success` haptic cue at each completed trial and measures
     trigger -> datagram arrival at the sleeve (G9 target < 100 ms),
  6. measures the live-link leg (G2 target: status < 250 ms on LAN),
  7. asserts MEANING, not just shape (see CHECKS below),
  8. runs `contracts/validate.py --session` and then `opus_analytics`, and prints the biomarkers.

Exit code is 0 only when every assertion passed -- usable in CI.

PORT DISCIPLINE: nothing here binds 8787/8788/8790/8791/8797 unless you explicitly ask for it.
The hub, its beacon and the sleeve all take OS-assigned ephemeral ports by default.

CHECKS (the ones that are about meaning, not schema shape -- the full-pipeline run of
2026-09-19 found five defects that all produced schema-valid files):
  - every trial_event the headset sent arrived at the hub, exactly once (local hub only)
  - every trial has BOTH trial_start and trial_end  (defect #2: trial_end never emitted)
  - every successful trial has a `contact` event    (defect #3: contact never emitted)
  - kinematics exist and every trial window overlaps the recorded kinematics range
                                                     (defects #1 and #4: no kin / two clocks)
  - status messages actually arrived at >= 1.0 Hz over the replay
  - one metrics_tick per trial_end
  - every uploaded file came back 200/201
  - analytics produced at least one non-null movement_time_ms, and every non-null biomarker is
    physiologically plausible                       (defect #5: endpoint error read 79-133 cm)
  - rate_hz is present and >= 45 (else SPARC/LDLJ must be flagged `degraded`)

USAGE (Windows cmd.exe):
    cd /d "C:\\Users\\GARV BANSAL\\Documents\\VR_games\\OPUS"

    rem local hub, everything simulated on this PC (the CI / smoke-test mode)
    sim\\live\\.venv\\Scripts\\python.exe tools\\demo\\run_pipeline.py

    rem aim it at the phone's Flutter hub
    sim\\live\\.venv\\Scripts\\python.exe tools\\demo\\run_pipeline.py --hub 172.17.221.245:8787
"""
from __future__ import annotations

import argparse
import asyncio
import json
import socket
import statistics
import subprocess
import sys
import tempfile
import time
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "sim" / "live"))
sys.path.insert(0, str(REPO_ROOT / "sim"))

# The live/haptic simulators need aiohttp + numpy, which live in sim\live\.venv.
# The session/metrics assertions below are pure functions and are unit-tested from
# analytics\.venv (no aiohttp), so the import is soft and only main() insists on it.
try:
    from fake_hub import FakeHub  # noqa: E402
    from fake_headset import FakeHeadset, generate_scripted_session  # noqa: E402
    from haptic.fake_haptic import FakeHapticSleeve, SleeveConfig  # noqa: E402
    SIM_IMPORT_ERROR: Optional[str] = None
except ImportError as _exc:  # pragma: no cover - exercised by the wrong-venv path
    FakeHub = FakeHeadset = FakeHapticSleeve = SleeveConfig = None  # type: ignore[assignment]
    generate_scripted_session = None  # type: ignore[assignment]
    SIM_IMPORT_ERROR = f"{type(_exc).__name__}: {_exc}"

VALIDATE_PY = REPO_ROOT / "contracts" / "validate.py"
ANALYTICS_DIR = REPO_ROOT / "analytics"
ANALYTICS_PY = ANALYTICS_DIR / ".venv" / "Scripts" / "python.exe"
if not ANALYTICS_PY.exists():  # single-venv layout (repo-root .venv): the running interpreter has opus_analytics too
    ANALYTICS_PY = Path(sys.executable)

# Goal thresholds (GOAL.md G2 / G9).
G2_STATUS_LATENCY_MS = 250.0
G9_HAPTIC_LATENCY_MS = 100.0

# Physiological plausibility windows for the "meaning, not shape" assertions.
PLAUSIBLE = {
    "reaction_time_ms": (80.0, 3000.0),
    "movement_time_ms": (200.0, 8000.0),
    "peak_speed_mps": (0.05, 6.0),
    "endpoint_error_cm": (0.0, 40.0),
    "sparc": (-12.0, 0.0),
    "ldlj": (-20.0, 0.0),
}


# --------------------------------------------------------------------------- utilities
def get_free_tcp_port() -> int:
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def get_free_udp_port() -> int:
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("0.0.0.0", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def pct(values: List[float]) -> Dict[str, float]:
    if not values:
        return {"p50": 0.0, "p95": 0.0, "max": 0.0, "count": 0}
    v = sorted(values)
    n = len(v)
    return {
        "p50": v[min(n // 2, n - 1)],
        "p95": v[min(int(n * 0.95), n - 1)],
        "max": v[-1],
        "count": n,
    }


class Checks:
    """Collect every assertion so one run reports all failures, not just the first."""

    def __init__(self) -> None:
        self.rows: List[Tuple[bool, str, str]] = []  # (ok, leg, description)

    def check(self, ok: bool, leg: str, description: str) -> bool:
        self.rows.append((bool(ok), leg, description))
        print(f"  [{'PASS' if ok else 'FAIL'}] ({leg}) {description}")
        return bool(ok)

    @property
    def failures(self) -> List[Tuple[bool, str, str]]:
        return [r for r in self.rows if not r[0]]

    @property
    def ok(self) -> bool:
        return not self.failures


def banner(title: str) -> None:
    print(f"\n{'=' * 78}\n== {title}\n{'=' * 78}")


# --------------------------------------------------------------------------- haptic leg
class _CueClientProtocol(asyncio.DatagramProtocol):
    """asyncio datagram endpoint for the cue driver.

    NOTE (real bug, fixed here): the first version of this used a raw non-blocking
    socket with `asyncio.wait_for(loop.sock_recv(...), 0.5)` in a loop. On Windows'
    ProactorEventLoop every cancelled `sock_recv` leaks its overlapped buffer, and
    the harness ballooned to ~4.6 GB RSS and hung. Use a real endpoint.
    """

    def __init__(self, driver: "CueDriver"):
        self.driver = driver

    def datagram_received(self, data: bytes, addr: Tuple[str, int]) -> None:
        t_now = time.monotonic() * 1000.0
        try:
            reply = json.loads(data.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            return
        if reply.get("type") != "ack":
            return
        payload = reply.get("payload") or {}
        ref = reply.get("ack_id") or reply.get("cue_id") or payload.get("ack_id")
        if ref in self.driver.sent:
            self.driver.ack_ms.append(t_now - self.driver.sent[ref])

    def error_received(self, exc: Exception) -> None:
        # Windows surfaces ICMP port-unreachable here; a missing sleeve is not fatal.
        pass


class CueDriver:
    """Fires a real contract cue at the sleeve every time a trial completes, and
    measures trigger -> datagram-arrival (G9). Uses one monotonic clock for both
    ends, so the number is a true one-way latency (no clock-skew guessing)."""

    def __init__(self, sleeve_host: str, sleeve_port: int, sleeve: Optional[FakeHapticSleeve]):
        self.addr = (sleeve_host, sleeve_port)
        self.sleeve = sleeve
        self.transport: Optional[asyncio.DatagramTransport] = None
        self.sent: Dict[str, float] = {}       # cue id -> t_trigger (monotonic ms)
        self.arrival_ms: List[float] = []      # trigger -> sleeve datagram_received
        self.ack_ms: List[float] = []          # trigger -> ack back at the harness
        self.cues_sent = 0

    async def start(self) -> None:
        loop = asyncio.get_event_loop()
        self.transport, _ = await loop.create_datagram_endpoint(
            lambda: _CueClientProtocol(self), local_addr=("0.0.0.0", 0)
        )

    def _cue(self, cue: str, intensity: float, duration_ms: int, pattern: str, zone: str) -> Dict[str, Any]:
        return {
            "v": 1,
            "type": "cue",
            "id": str(uuid.uuid4()),
            "ts_ms": time.time() * 1000.0,
            "cue": cue,
            "intensity": intensity,
            "duration_ms": duration_ms,
            "pattern": pattern,
            "zone": zone,
        }

    def fire(self, cue: str = "success", intensity: float = 0.6,
             duration_ms: int = 200, pattern: str = "buzz", zone: str = "upper_arm") -> str:
        msg = self._cue(cue, intensity, duration_ms, pattern, zone)
        t_trigger = time.monotonic() * 1000.0
        self.sent[msg["id"]] = t_trigger
        if self.transport is not None:
            self.transport.sendto(json.dumps(msg).encode("utf-8"), self.addr)
            self.cues_sent += 1
        return msg["id"]

    def stop(self) -> None:
        if self.transport is not None:
            self.transport.close()
            self.transport = None


def instrument_sleeve(sleeve: FakeHapticSleeve, driver: CueDriver) -> None:
    """Wrap the sleeve's datagram handler so we timestamp arrival on the SAME
    monotonic clock the cue trigger used. This is the G9 measurement."""
    original = sleeve.handle_datagram

    def wrapped(data: bytes, addr: Tuple[str, int]) -> None:
        t_arrive = time.monotonic() * 1000.0
        try:
            msg = json.loads(data.decode("utf-8"))
            cue_id = msg.get("id") or msg.get("cue_id")
            if cue_id in driver.sent:
                driver.arrival_ms.append(t_arrive - driver.sent[cue_id])
        except (json.JSONDecodeError, UnicodeDecodeError, AttributeError):
            pass
        original(data, addr)

    sleeve.handle_datagram = wrapped  # type: ignore[method-assign]


# --------------------------------------------------------------------------- session checks
def load_events(session_dir: Path) -> List[Dict[str, Any]]:
    path = session_dir / "events.ndjson"
    if not path.exists():
        return []
    return [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]


def kin_time_range(session_dir: Path) -> Optional[Tuple[float, float]]:
    """Span of the recorded kinematics clock. A kin chunk is
    {session_id, seq, rate_hz, source, joints[], t_ms[], frames{joint:{pos:[[x,y,z]..]}}}
    -- `t_ms` is a top-level parallel array, NOT a field on per-sample objects."""
    lo, hi = None, None
    for chunk in sorted(session_dir.glob("kin_*.json")):
        try:
            data = json.loads(chunk.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        ts = [t for t in (data.get("t_ms") or []) if isinstance(t, (int, float))]
        if not ts:
            continue
        lo = min(ts) if lo is None else min(lo, min(ts))
        hi = max(ts) if hi is None else max(hi, max(ts))
    return None if lo is None else (lo, hi)


def session_rate_hz(session_dir: Path) -> Optional[float]:
    """The recorded tracking rate. Canonical home is session.json
    `device.tracking_rate_hz`; each kin chunk also carries its own `rate_hz`."""
    try:
        env = json.loads((session_dir / "session.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        env = {}
    rate = (env.get("device") or {}).get("tracking_rate_hz") or env.get("rate_hz")
    if rate is not None:
        return float(rate)
    for chunk in sorted(session_dir.glob("kin_*.json")):
        try:
            data = json.loads(chunk.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if data.get("rate_hz") is not None:
            return float(data["rate_hz"])
    return None


def check_session_meaning(session_dir: Path, checks: Checks) -> None:
    """The assertions that catch the class of bug the 2026-09-19 pipeline run found:
    files that are schema-valid but clinically meaningless."""
    events = load_events(session_dir)
    checks.check(len(events) > 0, "session", f"events.ndjson is non-empty ({len(events)} events)")

    by_trial: Dict[Any, List[Dict[str, Any]]] = {}
    for ev in events:
        by_trial.setdefault(ev.get("trial"), []).append(ev)
    by_trial.pop(None, None)

    unclosed = [t for t, evs in by_trial.items()
                if not any(e["type"] == "trial_start" for e in evs)
                or not any(e["type"] == "trial_end" for e in evs)]
    checks.check(not unclosed, "session",
                 f"every one of {len(by_trial)} trial(s) has trial_start AND trial_end "
                 f"(unclosed: {sorted(unclosed)})")

    successes = [t for t, evs in by_trial.items()
                 if any(e["type"] == "trial_end" and (e.get("outcome") == "success") for e in evs)]
    no_contact = [t for t in successes
                  if not any(e["type"] == "contact" for e in by_trial[t])]
    checks.check(not no_contact, "session",
                 f"every one of {len(successes)} successful trial(s) has a contact event "
                 f"(missing: {sorted(no_contact)})")

    rng = kin_time_range(session_dir)
    checks.check(rng is not None, "session",
                 f"kinematics recorded ({len(list(session_dir.glob('kin_*.json')))} chunk(s), "
                 f"range={None if rng is None else f'{rng[0]:.0f}..{rng[1]:.0f} ms'})")
    if rng is not None:
        lo, hi = rng
        off_clock = []
        for t, evs in by_trial.items():
            ts = [e.get("t_ms") for e in evs if e.get("t_ms") is not None]
            if ts and (max(ts) < lo or min(ts) > hi):
                off_clock.append(t)
        checks.check(not off_clock, "session",
                     f"every trial window overlaps the kinematics clock (off-clock trials: {sorted(off_clock)})")

    rate = session_rate_hz(session_dir)
    checks.check(rate is not None, "session",
                 f"session carries a tracking rate_hz (got {rate})")
    if rate is not None:
        checks.check(rate >= 45.0, "session",
                     f"rate_hz {rate:.1f} >= 45 (below this SPARC/LDLJ must be flagged degraded)")


def run_validate(session_dir: Path, checks: Checks) -> None:
    proc = subprocess.run(
        [str(ANALYTICS_PY), str(VALIDATE_PY), "--session", str(session_dir)],
        capture_output=True, text=True,
    )
    out = (proc.stdout + proc.stderr).strip()
    print("  " + "\n  ".join(out.splitlines()[-6:]))
    checks.check(proc.returncode == 0, "contracts",
                 f"contracts/validate.py --session exits 0 (got {proc.returncode})")


def run_analytics_and_report(session_dir: Path, checks: Checks) -> None:
    proc = subprocess.run(
        [str(ANALYTICS_PY), "-m", "opus_analytics", str(session_dir), "--quiet"],
        capture_output=True, text=True, cwd=str(ANALYTICS_DIR),
    )
    if proc.returncode != 0:
        print((proc.stdout + proc.stderr)[-2000:])
    checks.check(proc.returncode == 0, "analytics", f"opus_analytics exits 0 (got {proc.returncode})")

    metrics_path = session_dir / "metrics.json"
    if not checks.check(metrics_path.exists(), "analytics", "metrics.json written"):
        return

    data = json.loads(metrics_path.read_text(encoding="utf-8"))
    trials = data.get("trials", [])

    print("\n  --- BIOMARKERS (per trial) " + "-" * 45)
    header = f"  {'trial':>5} {'outcome':<12} {'RT ms':>8} {'MT ms':>8} {'peak m/s':>9} {'SPARC':>7} {'LDLJ':>8} {'err cm':>7}"
    print(header)
    collected: Dict[str, List[float]] = {k: [] for k in PLAUSIBLE}
    implausible: List[str] = []
    for t in trials:
        m = t.get("metrics", {})

        def g(mid: str) -> str:
            mv = m.get(mid, {})
            v = mv.get("value")
            if v is None:
                return "-"
            if mv.get("quality") in ("ok", "degraded"):
                collected[mid].append(v)
                lo, hi = PLAUSIBLE[mid]
                if not (lo <= v <= hi):
                    implausible.append(f"trial {t.get('trial')} {mid}={v:.2f} outside [{lo}, {hi}]")
            suffix = "~" if mv.get("quality") == "degraded" else ""
            return f"{v:.2f}{suffix}"

        print(f"  {str(t.get('trial')):>5} {str(t.get('outcome')):<12} "
              f"{g('reaction_time_ms'):>8} {g('movement_time_ms'):>8} {g('peak_speed_mps'):>9} "
              f"{g('sparc'):>7} {g('ldlj'):>8} {g('endpoint_error_cm'):>7}")
    print("  " + "-" * 72 + "   (~ = degraded quality)")

    for mid, vals in collected.items():
        if vals:
            print(f"  {mid:<20} n={len(vals):<3} mean={statistics.mean(vals):8.2f}  "
                  f"min={min(vals):8.2f}  max={max(vals):8.2f}")

    checks.check(len(collected["movement_time_ms"]) > 0, "analytics",
                 "analytics produced at least one non-null movement_time_ms "
                 "(a session with every metric null is the 2026-09-19 defect #2 signature)")
    checks.check(not implausible, "analytics",
                 f"every non-null biomarker is physiologically plausible ({'; '.join(implausible) or 'all in range'})")


# --------------------------------------------------------------------------- the run
async def run(args: argparse.Namespace) -> int:
    checks = Checks()
    t0 = time.time()

    # ---- session source ---------------------------------------------------
    banner("STEP 1/6  session source")
    if args.session:
        session_dir = Path(args.session)
        print(f"  replaying recorded session: {session_dir}")
    else:
        session_dir = Path(tempfile.mkdtemp(prefix="opus_pipeline_")) / "session"
        generate_scripted_session(session_dir, stage=args.stage, seed=args.seed,
                                  n_trials=args.trials, patient_ref=args.patient_ref)
        print(f"  generated {args.stage} session (seed={args.seed}) at {session_dir}")
    checks.check(session_dir.exists(), "session", f"session source exists ({session_dir})")

    # ---- sleeve -----------------------------------------------------------
    banner("STEP 2/6  haptic sleeve")
    sleeve: Optional[FakeHapticSleeve] = None
    driver: Optional[CueDriver] = None
    if args.no_sleeve:
        print("  --no-sleeve: skipping the haptic leg")
    elif args.sleeve_host:
        host, _, p = args.sleeve_host.partition(":")
        sleeve_port = int(p or 8790)
        print(f"  targeting an EXTERNAL sleeve at {host}:{sleeve_port} (no simulator started)")
        driver = CueDriver(host, sleeve_port, None)
        await driver.start()
    else:
        sleeve_port = args.sleeve_port or get_free_udp_port()
        disc_port = get_free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=sleeve_port, discovery_port=disc_port))
        await sleeve.start()
        print(f"  fake_haptic sleeve listening on UDP 127.0.0.1:{sleeve_port} (discovery {disc_port})")
        driver = CueDriver("127.0.0.1", sleeve_port, sleeve)
        instrument_sleeve(sleeve, driver)
        await driver.start()

    # ---- hub --------------------------------------------------------------
    banner("STEP 3/6  hub")
    hub: Optional[FakeHub] = None
    hub_out: Optional[Path] = None
    if args.hub:
        host, _, p = args.hub.partition(":")
        hub_host, hub_port = host, int(p or 8787)
        print(f"  targeting an EXTERNAL hub at {hub_host}:{hub_port}")
        print("  (the hub stores the session on ITS OWN disk -- this run validates the source")
        print("   session the headset uploaded, and asserts every PUT returned 200/201.")
        print("   To see the arriving events, watch the app's Live Monitor screen.)")
        # Preflight: both the Flutter hub (app/lib/core/hub/hub_server.dart) and
        # fake_hub expose GET /opus/v1/health. Failing here gives a human a far
        # better message than a WebSocket stack trace 10 seconds later.
        import aiohttp
        health_url = f"http://{hub_host}:{hub_port}/opus/v1/health"
        t_health = time.monotonic() * 1000.0
        health_ok, health_body = False, "no response"
        try:
            timeout = aiohttp.ClientTimeout(total=5.0)
            async with aiohttp.ClientSession(timeout=timeout) as s:
                async with s.get(health_url) as resp:
                    health_body = (await resp.text())[:120]
                    health_ok = resp.status == 200
        except Exception as e:  # noqa: BLE001 - any transport error is the same story
            health_body = f"{type(e).__name__}: {e}"
        health_ms = time.monotonic() * 1000.0 - t_health
        checks.check(health_ok, "hub",
                     f"GET {health_url} -> 200 in {health_ms:.0f} ms ({health_body}). "
                     f"If this fails: wrong IP, hub not started in the app, or a firewall.")
        if not health_ok:
            print("\n  ABORTING: the hub is not reachable. Nothing else can pass.")
            print("  Check: (1) the app's Devices screen shows the hub RUNNING and its IP,")
            print("         (2) the phone and this PC are on the same subnet,")
            print("         (3) tools\\demo\\open_firewall.ps1 was applied on this PC.")
            if driver:
                driver.stop()
            if sleeve:
                await sleeve.stop()
            return 1
    else:
        hub_port = get_free_tcp_port()
        beacon_port = get_free_udp_port()
        hub_out = Path(tempfile.mkdtemp(prefix="opus_hub_out_"))
        hub = FakeHub(port=hub_port, beacon=False, scenario=None,
                      beacon_port=beacon_port, out_dir=hub_out)
        await hub.start()
        hub_host = "127.0.0.1"
        print(f"  local fake_hub on 127.0.0.1:{hub_port} (ephemeral; beacon off), out={hub_out}")
        await asyncio.sleep(0.3)

    # ---- headset ----------------------------------------------------------
    banner("STEP 4/6  headset -> hub (live protocol)")
    session_id = f"pipeline-{int(time.time() * 1000)}"
    headset = FakeHeadset(session_dir=session_dir, host=hub_host, port=hub_port,
                          speed=args.speed, stage=args.stage, patient_ref=args.patient_ref,
                          no_scenario=args.wait_for_start)
    recv_task: Optional[asyncio.Task] = None
    ping_task: Optional[asyncio.Task] = None
    cue_task: Optional[asyncio.Task] = None
    upload_results: Dict[str, bool] = {}

    try:
        t_connect = time.monotonic() * 1000.0
        await headset.connect(hub_host, hub_port)
        headset.state.session_id = session_id
        await headset.send_hello()
        recv_task = asyncio.create_task(headset.receive_messages())
        # hello -> hello_ack is a real network round trip and works against ANY hub.
        for _ in range(100):
            if headset.state.recv_ts:
                break
            await asyncio.sleep(0.05)
        handshake_ms = time.monotonic() * 1000.0 - t_connect
        checks.check(bool(headset.state.recv_ts), "live",
                     f"hub answered hello with hello_ack ({handshake_ms:.0f} ms round trip)")

        ping_task = asyncio.create_task(headset.measure_latency())

        # Fire a haptic cue every time a trial completes -- the real trigger point.
        async def cue_on_trial_complete() -> None:
            last = 0
            while not headset.stopped:
                done = int(headset.live_status.get("trials_completed", 0))
                if driver is not None and done > last:
                    for _ in range(done - last):
                        cue_id = driver.fire()
                        headset.live_status["last_cue_id"] = cue_id
                    last = done
                await asyncio.sleep(0.02)

        cue_task = asyncio.create_task(cue_on_trial_complete())

        # Track upload results honestly (the only visibility we get against a remote hub).
        original_upload = headset.upload_file

        async def tracked_upload(filename: str, data: bytes) -> bool:
            ok = await original_upload(filename, data)
            upload_results[filename] = ok
            return ok

        headset.upload_file = tracked_upload  # type: ignore[method-assign]

        t_replay = time.monotonic()
        await headset.replay_session()
        replay_s = time.monotonic() - t_replay
        await asyncio.sleep(1.0)  # let in-flight acks/cues land
    finally:
        for task in (cue_task, ping_task, recv_task):
            if task:
                task.cancel()
                try:
                    await task
                except (asyncio.CancelledError, Exception):
                    pass

    # ---- assertions: transport -------------------------------------------
    banner("STEP 5/6  assertions + measured latency")
    print(f"  replay wall time: {replay_s:.1f}s at speed x{args.speed}")

    status_lat: List[float] = []
    if hub is not None:
        counts = dict(hub.state.message_counts)
        print(f"  hub message counts: {counts}")
        checks.check(hub.state.invalid_count == 0, "live",
                     f"hub saw 0 schema-invalid messages (got {hub.state.invalid_count}: "
                     f"{hub.state.invalid_messages[:3]})")

        sent_trial_events = {mid for mid, _ in headset.state.sent_ts.items()}
        received = hub.state.received_trial_events
        checks.check(len(received) == len(headset.events), "live",
                     f"hub received every trial_event exactly once "
                     f"({len(received)} received / {len(headset.events)} in the session)")

        n_status = counts.get("status", 0)
        rate = n_status / max(replay_s, 1e-6)
        checks.check(rate >= 1.0, "live",
                     f"status arrived at {rate:.2f} Hz over the replay ({n_status} messages; "
                     f"the runner targets 2 Hz, protocol cap 5 Hz)")

        n_ticks = counts.get("metrics_tick", 0)
        n_trial_end = sum(1 for e in headset.events if e.get("type") == "trial_end")
        checks.check(n_ticks >= max(1, n_trial_end - 1), "live",
                     f"metrics_tick per completed trial ({n_ticks} ticks / {n_trial_end} trial_end events)")

        # True one-way latency: both timestamps come from this process's wall clock.
        for mid, sent in headset.state.sent_ts.items():
            r = hub.state.recv_ts.get(mid)
            if r is not None:
                status_lat.append(r - sent)
        p = pct(status_lat)
        print(f"  LEG game->hub (one-way, in-process): p50={p['p50']:.1f} ms  "
              f"p95={p['p95']:.1f} ms  max={p['max']:.1f} ms  n={p['count']}")
        checks.check(p["count"] > 0 and p["p50"] < G2_STATUS_LATENCY_MS, "live",
                     f"G2: live message p50 {p['p50']:.1f} ms < {G2_STATUS_LATENCY_MS:.0f} ms")
        _ = sent_trial_events

    rtt = pct(headset.state.rtt_samples)
    if rtt["count"] > 0:
        print(f"  LEG headset<->hub ping RTT: p50={rtt['p50']:.1f} ms  p95={rtt['p95']:.1f} ms  "
              f"max={rtt['max']:.1f} ms  n={rtt['count']}")
        checks.check(rtt["p50"] / 2.0 < G2_STATUS_LATENCY_MS, "live",
                     f"G2: one-way (RTT/2) {rtt['p50'] / 2.0:.1f} ms < {G2_STATUS_LATENCY_MS:.0f} ms")
    elif hub is None:
        # Remote hub with no pong support: fall back to the handshake round trip.
        checks.check(handshake_ms / 2.0 < G2_STATUS_LATENCY_MS, "live",
                     f"G2 (fallback, hello/hello_ack RTT/2) {handshake_ms / 2.0:.1f} ms "
                     f"< {G2_STATUS_LATENCY_MS:.0f} ms -- this hub sent no pongs")

    expected_files = ["session.json", "events.ndjson"] + \
                     [f"kin_{n:03d}.json" for n in sorted(headset.kin_files)]
    missing = [f for f in expected_files if not upload_results.get(f)]
    checks.check(not missing, "upload",
                 f"all {len(expected_files)} session file(s) uploaded with 200/201 (failed: {missing})")

    if hub is not None and hub_out is not None:
        landed = sorted(p.name for p in (hub_out / session_id).glob("*")) \
            if (hub_out / session_id).exists() else []
        checks.check(all(f in landed for f in expected_files), "upload",
                     f"hub wrote the files to disk ({landed})")

    # ---- haptic leg -------------------------------------------------------
    if driver is not None:
        arr = pct(driver.arrival_ms)
        ack = pct(driver.ack_ms)
        print(f"  LEG cue trigger->sleeve datagram: p50={arr['p50']:.2f} ms  "
              f"max={arr['max']:.2f} ms  n={arr['count']}")
        print(f"  LEG cue trigger->ack back:        p50={ack['p50']:.2f} ms  "
              f"max={ack['max']:.2f} ms  n={ack['count']}")
        checks.check(driver.cues_sent > 0, "haptic",
                     f"fired a haptic cue at each completed trial ({driver.cues_sent} cues)")
        if sleeve is not None:
            checks.check(sleeve.state.cue_count == driver.cues_sent, "haptic",
                         f"sleeve received every cue ({sleeve.state.cue_count}/{driver.cues_sent})")
            checks.check(sleeve.state.invalid_count == 0, "haptic",
                         f"sleeve rejected 0 cues as invalid (got {sleeve.state.invalid_count})")
            checks.check(arr["count"] > 0 and arr["max"] < G9_HAPTIC_LATENCY_MS, "haptic",
                         f"G9: cue trigger -> datagram max {arr['max']:.2f} ms < {G9_HAPTIC_LATENCY_MS:.0f} ms")
        else:
            checks.check(ack["count"] > 0, "haptic",
                         f"external sleeve acked {ack['count']}/{driver.cues_sent} cues")

    # ---- contracts + analytics -------------------------------------------
    banner("STEP 6/6  contracts + analytics")
    validate_dir = (hub_out / session_id) if (hub_out and (hub_out / session_id).exists()) else session_dir
    print(f"  validating: {validate_dir}")
    check_session_meaning(validate_dir, checks)
    run_validate(validate_dir, checks)
    run_analytics_and_report(validate_dir, checks)

    # ---- teardown ---------------------------------------------------------
    try:
        await headset.disconnect()
    except Exception:
        pass
    if driver:
        driver.stop()
    if sleeve:
        await sleeve.stop()
    if hub:
        await hub.stop()

    # ---- verdict ----------------------------------------------------------
    banner("VERDICT")
    n_pass = len(checks.rows) - len(checks.failures)
    print(f"  {n_pass}/{len(checks.rows)} checks passed in {time.time() - t0:.1f}s")
    for _, leg, desc in checks.failures:
        print(f"  FAILED ({leg}) {desc}")
    print(f"\n  PIPELINE {'OK' if checks.ok else 'FAILED'}")

    if args.json_report:
        Path(args.json_report).write_text(json.dumps({
            "ok": checks.ok,
            "checks": [{"ok": o, "leg": l, "desc": d} for o, l, d in checks.rows],
            "status_latency_ms": pct(status_lat),
            "ping_rtt_ms": rtt,
            "haptic_arrival_ms": pct(driver.arrival_ms) if driver else None,
            "haptic_ack_ms": pct(driver.ack_ms) if driver else None,
            "session_dir": str(validate_dir),
        }, indent=2), encoding="utf-8")
        print(f"  json report: {args.json_report}")

    return 0 if checks.ok else 1


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="One-command OPUS pipeline proof: game -> UDP/WS -> hub -> phone.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument("--hub", help="Target an EXISTING hub as ip:port (e.g. the phone at "
                                 "172.17.221.245:8787). Omit to start a local fake hub on an "
                                 "ephemeral port.")
    p.add_argument("--sleeve-host", help="Target an EXISTING sleeve as ip[:port] (e.g. the real "
                                         "ESP32). Omit to start sim/haptic/fake_haptic.py locally.")
    p.add_argument("--sleeve-port", type=int, default=None,
                   help="Explicit UDP port for the LOCAL sleeve simulator (default: ephemeral).")
    p.add_argument("--no-sleeve", action="store_true", help="Skip the haptic leg entirely.")
    p.add_argument("--session", help="Replay this recorded session directory instead of generating one.")
    p.add_argument("--stage", choices=["reach", "sorting"], default="reach")
    p.add_argument("--trials", type=int, default=6)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--patient-ref", default="demo-patient-001")
    p.add_argument("--speed", type=float, default=4.0,
                   help="Replay speed multiplier. Use 1.0 for a real-time demo on the phone.")
    p.add_argument("--wait-for-start", action="store_true",
                   help="Wait for a real 'start' command from the hub before replaying "
                        "(use when a human drives the session from the app).")
    p.add_argument("--json-report", help="Write a machine-readable report to this path.")
    # ---- Phantom Hand (G2 / L3, docs/agent-briefs/ph/04-E2E.md) -> tools/demo/phantom_pipeline.py
    g = p.add_argument_group("Phantom Hand (--game phantom_hand)")
    g.add_argument("--game", choices=["phantom_hand"], default=None,
                   help="phantom_hand: the L3 pipeline (hub + sleeve twin + game + analytics) and its table. "
                        "--hub then also accepts 'fake' (default) or 'flutter' (dart run app/tool/hub_cli.dart).")
    g.add_argument("--sim", action="store_true", help="twin (--kind both) + hub on offset ports")
    g.add_argument("--no-unity", action="store_true",
                   help="replay the phantom_hand_min fixture with sim/live/fake_headset (phantom_replay.py) instead "
                        "of the Unity batch PlayMode PH_FullRun test")
    g.add_argument("--faults", action="store_true", help="the three fault runs: Node A off at 50 %%, Node B absent, hub absent")
    g.add_argument("--hardware", action="store_true", help="real nodes (discovery on the real ports), no twin")
    g.add_argument("--spinup", action="store_true", help="measure motor start delay on Node A (median of 10 pulses)")
    g.add_argument("--port-offset", type=int, default=31000, help="twin ports = 8790/8791/8792/8793 + N")
    g.add_argument("--discovery-port", type=int, default=8791, help="--hardware / --spinup discovery port")
    g.add_argument("--a-ip", help="--spinup: Node A ip[:port] instead of discovery")
    g.add_argument("--hub-port", type=int, default=0, help="--sim fake hub port (default: ephemeral)")
    g.add_argument("--out", help="output dir for twin log, hub sessions, l3_report.json/.md (default: a temp dir)")
    g.add_argument("--fixture", default=str(REPO_ROOT / "contracts" / "fixtures" / "sessions" / "phantom_hand_min"))
    g.add_argument("--ph-speed", type=float, default=1.0, help="replay speed for phantom_hand (1.0 = real time)")
    g.add_argument("--compress-gap-ms", type=float, default=2500.0, help="cap idle gaps between events (0 = real time)")
    g.add_argument("--hub-outage-s", type=float, default=30.0, help="--faults: how long the hub stays away")
    g.add_argument("--with-main", action="store_true", help="--faults: also run the unfaulted main run first")
    return p


def main() -> int:
    args = build_parser().parse_args()
    if args.game == "phantom_hand":
        import phantom_pipeline
        if phantom_pipeline.SIM_IMPORT_ERROR is not None:
            print(f"FATAL: {phantom_pipeline.SIM_IMPORT_ERROR}\n"
                  f"       Use the sim/live venv (aiohttp): sim/live/.venv/Scripts/python.exe "
                  f"tools/demo/run_pipeline.py --game phantom_hand --sim", file=sys.stderr)
            return 2
        if not (args.sim or args.hardware or args.spinup):
            print("--game phantom_hand needs --sim, --hardware or --spinup", file=sys.stderr)
            return 2
        try:
            return asyncio.run(phantom_pipeline.run_phantom(args))
        except KeyboardInterrupt:
            return 130
    if SIM_IMPORT_ERROR is not None:
        print(f"FATAL: the simulators could not be imported ({SIM_IMPORT_ERROR}).\n"
              f"       Run this harness with the sim/live venv, which has aiohttp + numpy:\n"
              f'       sim\\live\\.venv\\Scripts\\python.exe tools\\demo\\run_pipeline.py',
              file=sys.stderr)
        return 2
    if not ANALYTICS_PY.exists():
        print(f"FATAL: analytics venv python not found at {ANALYTICS_PY}", file=sys.stderr)
        return 2
    try:
        return asyncio.run(run(args))
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
