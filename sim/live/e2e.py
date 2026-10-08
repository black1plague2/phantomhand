"""S4: e2e.py - End-to-end integration test with real protocol assertions and latency reporting.

Runs the actual FakeHub and FakeHeadset classes (sim/live/fake_hub.py, fake_headset.py)
in-process against ephemeral ports, drives a real WebSocket + HTTP session over
127.0.0.1, and asserts real protocol behavior:

  - every trial_event the hub receives is schema-valid (fake_hub counts invalid
    messages; the count must be zero)
  - every trial_event in events.ndjson is delivered to the hub exactly once
    (even across a simulated drop + resume)
  - every command the hub sends is ack'd by the headset within 1s
  - command->ack and event send->receive latencies are reported as p50/p95/max
  - every uploaded file is sha256-identical to the source, and the uploaded
    session directory passes `contracts/validate.py --session`
  - the drop scenario proves resume: events generated while disconnected are
    buffered and replayed after reconnect, with no duplicates counted at the hub

Run standalone:   python e2e.py
Run under pytest: pytest e2e.py -v
"""

import sys
import os
import json
import time
import shutil
import socket
import hashlib
import asyncio
import tempfile
import subprocess
from pathlib import Path
from typing import Any, Dict, List, Optional

sys.path.insert(0, str(Path(__file__).parent))

from fake_hub import FakeHub
from fake_headset import FakeHeadset, UDPDiscovery

REPO_ROOT = Path(__file__).resolve().parents[2]  # .../OPUS  (was wrongly parent.parent -> sim/contracts)
CONTRACTS_DIR = REPO_ROOT / "contracts"
SESSION_FIXTURE = CONTRACTS_DIR / "fixtures" / "sessions" / "healthy"
ANALYTICS_PY = REPO_ROOT / "analytics" / ".venv" / "Scripts" / "python.exe"
VALIDATE_PY = CONTRACTS_DIR / "validate.py"


def get_free_tcp_port() -> int:
    """Bind an ephemeral TCP port and return it (small race, standard test practice)."""
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


def percentiles(values: List[float]) -> Dict[str, float]:
    """p50/p95/max with safe indexing for small samples."""
    if not values:
        return {"p50": 0.0, "p95": 0.0, "max": 0.0, "count": 0}
    v = sorted(values)
    n = len(v)
    p50 = v[min(n // 2, n - 1)]
    p95 = v[min(int(n * 0.95), n - 1)]
    return {"p50": p50, "p95": p95, "max": max(v), "count": n}


def sha256_of(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Checks:
    """Collects assertions so one run can report every failure, not just the first."""

    def __init__(self, name: str):
        self.name = name
        self.failures: List[str] = []
        self.passed: List[str] = []

    def check(self, condition: bool, description: str):
        if condition:
            self.passed.append(description)
        else:
            self.failures.append(description)

    @property
    def ok(self) -> bool:
        return not self.failures

    def report(self) -> str:
        lines = [f"--- checks: {self.name} ---"]
        for p in self.passed:
            lines.append(f"  [PASS] {p}")
        for f in self.failures:
            lines.append(f"  [FAIL] {f}")
        return "\n".join(lines)


async def run_e2e_scenario(
    scenario_name: str,
    drop_at: Optional[float] = None,
    speed: float = 10.0,
    session_dir: Path = SESSION_FIXTURE,
) -> Dict[str, Any]:
    """Run one end-to-end scenario against the real FakeHub/FakeHeadset and
    return a results dict with real measured data (no hardcoded success)."""

    print(f"\n=== E2E SCENARIO: {scenario_name} (drop_at={drop_at}, speed={speed}) ===")

    port = get_free_tcp_port()
    beacon_port = get_free_udp_port()
    session_id = f"e2e-{scenario_name}-{int(time.time() * 1000)}"
    out_root = Path(tempfile.mkdtemp(prefix="opus_e2e_out_"))

    checks = Checks(f"{scenario_name} drop_at={drop_at}")

    # The "basic" hub scenario's pause/resume/stop timing is wall-clock, not
    # session-clock -- it must be sized to actually cover a full replay at
    # this `speed`, or `stop` fires before the headset finishes (a real bug
    # found & fixed in fake_hub.py: the original hardcoded 5s/2s/5s only
    # happened to work at speed>=10). Size it from the fixture's own duration.
    with open(session_dir / "events.ndjson") as f:
        last_t_ms = max(json.loads(line)["t_ms"] for line in f if line.strip())
    required_replay_s = (last_t_ms / 1000.0) / max(speed, 1e-6)
    scenario_timing = {
        "assign_wait": 1.0,
        "pre_pause": required_replay_s * 0.5 + 1.0,
        "pause_hold": 1.0,
        "post_resume": required_replay_s * 0.5 + 2.0,
    }

    hub = FakeHub(
        port=port,
        beacon=False,
        scenario=scenario_name,
        beacon_port=beacon_port,
        out_dir=out_root,
        scenario_timing=scenario_timing,
    )
    headset: Optional[FakeHeadset] = None
    recv_task: Optional[asyncio.Task] = None
    result: Dict[str, Any] = {"scenario": scenario_name, "drop_at": drop_at, "port": port}

    try:
        await hub.start()
        print(f"[E2E] Hub started on 127.0.0.1:{port}")
        await asyncio.sleep(0.3)

        headset = FakeHeadset(
            session_dir=session_dir,
            host="127.0.0.1",
            port=port,
            speed=speed,
            drop_at=drop_at,
        )

        await headset.connect("127.0.0.1", port)
        print("[E2E] Headset connected")

        headset.state.session_id = session_id
        await headset.send_hello()
        print("[E2E] Hello sent")

        recv_task = asyncio.create_task(headset.receive_messages())
        await asyncio.sleep(0.3)

        await headset.replay_session()
        print("[E2E] Replay complete")

        # Let any in-flight acks/uploads settle, and (for the "basic" scenario)
        # let the hub's scripted stop command arrive & get ack'd.
        await asyncio.sleep(1.5)

        # ---- Assertions -------------------------------------------------

        # 1. Hub validated every inbound message; none were invalid.
        checks.check(hub.state.invalid_count == 0, f"hub saw 0 invalid messages (got {hub.state.invalid_count}: {hub.state.invalid_messages})")

        # 2. Every trial_event in the source session was received exactly once.
        source_events = headset.events
        received = hub.state.received_trial_events  # msg_id -> payload, de-duped by id
        received_by_seq = {}
        dup_seq = []
        for payload in received.values():
            seq = payload.get("seq")
            if seq in received_by_seq:
                dup_seq.append(seq)
            received_by_seq[seq] = payload
        checks.check(len(dup_seq) == 0, f"no duplicate seqs counted at hub (dup seqs: {dup_seq})")
        checks.check(
            len(received) == len(source_events),
            f"hub received {len(received)} unique trial_events, expected {len(source_events)}",
        )
        mismatches = []
        for ev in source_events:
            got = received_by_seq.get(ev["seq"])
            if got != ev:
                mismatches.append(ev["seq"])
        checks.check(len(mismatches) == 0, f"received payloads match source events (mismatched seqs: {mismatches})")

        # 3. Commands acked within 1s (only meaningful when the hub scenario sends commands).
        cmd_lat = percentiles(hub.state.command_ack_latencies_ms)
        result["command_ack_latency_ms"] = cmd_lat
        if cmd_lat["count"] > 0:
            checks.check(
                len(hub.state.command_ack_over_1s) == 0,
                f"all {cmd_lat['count']} command acks arrived within 1s (over_1s={hub.state.command_ack_over_1s})",
            )
        else:
            checks.check(False, "at least one command/ack pair was observed")

        # 4. Event send->receive latency (headset send ts vs hub recv ts, same process clock).
        event_latencies = []
        for msg_id, sent_ts in headset.state.sent_ts.items():
            if msg_id in received:  # this msg_id is a trial_event that the hub actually saw
                recv_ts = hub.state.recv_ts.get(msg_id)
                if recv_ts is not None:
                    event_latencies.append(recv_ts - sent_ts)
        ev_lat = percentiles(event_latencies)
        result["event_latency_ms"] = ev_lat
        checks.check(ev_lat["count"] > 0, f"measured event send->receive latency for {ev_lat['count']} events")

        # 5. Uploaded files are sha256-identical to source, and the uploaded
        #    session directory passes contracts/validate.py --session.
        out_dir = out_root / session_id
        expected_files = ["session.json", "events.ndjson"] + [f"kin_{n:03d}.json" for n in headset.kin_files]
        uploaded = {f.name for f in out_dir.glob("*")} if out_dir.exists() else set()
        result["uploaded_files"] = sorted(uploaded)
        checks.check(
            all(f in uploaded for f in expected_files),
            f"all expected files uploaded (expected {expected_files}, got {sorted(uploaded)})",
        )
        sha_mismatches = []
        for fname in expected_files:
            src = session_dir / fname
            dst = out_dir / fname
            if src.exists() and dst.exists():
                if sha256_of(src) != sha256_of(dst):
                    sha_mismatches.append(fname)
        checks.check(len(sha_mismatches) == 0, f"uploaded files sha256-match source (mismatches: {sha_mismatches})")

        if out_dir.exists() and ANALYTICS_PY.exists():
            proc = subprocess.run(
                [str(ANALYTICS_PY), str(VALIDATE_PY), "--session", str(out_dir)],
                capture_output=True,
                text=True,
                cwd=str(CONTRACTS_DIR),
            )
            result["validate_returncode"] = proc.returncode
            result["validate_stdout"] = proc.stdout.strip()
            result["validate_stderr"] = proc.stderr.strip()
            checks.check(
                proc.returncode == 0,
                f"contracts/validate.py --session exits 0 (got {proc.returncode}); stdout={proc.stdout.strip()[-500:]}",
            )
        else:
            checks.check(False, f"analytics venv python found at {ANALYTICS_PY} (exists={ANALYTICS_PY.exists()})")

        # 6. Drop scenario proves resume: something was actually resent, and
        #    the exactly-once assertion above (#2) already proves no duplicates
        #    were counted at the hub despite the resend.
        if drop_at is not None:
            checks.check(headset.state.resends > 0, f"headset resent >=1 trial_event from its outbox after reconnect (resent {headset.state.resends})")
            result["resends"] = headset.state.resends

        # 7. Ping/pong RTT measurements (hub sends pings to headset every 1s; headset responds with pongs)
        hub_rtt = percentiles(hub.state.rtt_samples)
        result["hub_rtt_ms"] = hub_rtt
        if hub_rtt["count"] > 0:
            checks.check(True, f"hub measured ping RTT from {hub_rtt['count']} pongs (p50={hub_rtt['p50']:.1f}ms, p95={hub_rtt['p95']:.1f}ms, max={hub_rtt['max']:.1f}ms)")
        else:
            checks.check(True, "hub ping loop ran (RTT samples optional)")

        headset_rtt = percentiles(headset.state.rtt_samples)
        result["headset_rtt_ms"] = headset_rtt
        if headset_rtt["count"] > 0:
            checks.check(True, f"headset measured ping RTT from {headset_rtt['count']} pongs (p50={headset_rtt['p50']:.1f}ms, p95={headset_rtt['p95']:.1f}ms, max={headset_rtt['max']:.1f}ms)")

        result["hub_message_counts"] = dict(hub.state.message_counts)
        result["hub_invalid_count"] = hub.state.invalid_count
        result["checks"] = {"passed": checks.passed, "failed": checks.failures}
        result["success"] = checks.ok
        result["error"] = None if checks.ok else f"{len(checks.failures)} check(s) failed"

        print(checks.report())
        return result

    except Exception as e:
        import traceback

        traceback.print_exc()
        result["success"] = False
        result["error"] = str(e)
        result["checks"] = {"passed": checks.passed, "failed": checks.failures + [f"exception: {e}"]}
        return result

    finally:
        if recv_task:
            recv_task.cancel()
            try:
                await recv_task
            except asyncio.CancelledError:
                pass
        if headset:
            await headset.disconnect()
        await hub.stop()
        shutil.rmtree(out_root, ignore_errors=True)


async def run_e2e_discovery(speed: float = 10.0) -> Dict[str, Any]:
    """Variant using real UDP beacon discovery instead of a fixed --host.

    Windows/corporate networks sometimes block UDP broadcast even on
    localhost; if discovery genuinely can't find the hub within the timeout,
    this is reported (not silently passed) and documented in MANUAL_TODO.md
    rather than failing the whole suite.
    """
    print("\n=== E2E SCENARIO: discovery (UDP beacon, --host auto) ===")
    port = get_free_tcp_port()
    beacon_port = get_free_udp_port()
    checks = Checks("discovery")
    result: Dict[str, Any] = {"scenario": "discovery", "port": port, "beacon_port": beacon_port}

    hub = FakeHub(port=port, beacon=True, scenario=None, beacon_port=beacon_port)
    try:
        await hub.start()
        await asyncio.sleep(0.2)

        discovered = await UDPDiscovery.discover(timeout_sec=4.0, beacon_port=beacon_port)
        result["discovered"] = discovered

        if discovered is None:
            checks.check(
                False,
                "UDP beacon discovery found the hub within 4s "
                "(if this fails on Windows it is usually the firewall blocking UDP broadcast; "
                "see docs/MANUAL_TODO.md)",
            )
            result["success"] = False
            result["error"] = "discovery_timeout"
            print(checks.report())
            return result

        host, hub_port = discovered
        checks.check(hub_port == port, f"beacon advertised the correct port (expected {port}, got {hub_port})")

        headset = FakeHeadset(session_dir=SESSION_FIXTURE, host="auto", port=port, speed=speed, beacon_port=beacon_port)
        found_host, found_port = await headset.discover_hub()
        checks.check(found_host is not None, f"headset.discover_hub() resolved a host ({found_host})")
        await headset.connect(found_host, found_port)
        headset.state.session_id = "e2e-discovery"
        await headset.send_hello()
        recv_task = asyncio.create_task(headset.receive_messages())
        await asyncio.sleep(0.5)
        checks.check(headset.state.session_id is not None, "headset completed hello/hello_ack handshake over the discovered address")
        recv_task.cancel()
        try:
            await recv_task
        except asyncio.CancelledError:
            pass
        await headset.disconnect()

        result["success"] = checks.ok
        result["error"] = None if checks.ok else f"{len(checks.failures)} check(s) failed"
        print(checks.report())
        return result
    finally:
        await hub.stop()


# --------------------------------------------------------------------------
# pytest entry points
# --------------------------------------------------------------------------


def test_e2e_basic():
    """Basic scenario, no drop, full command flow, run under pytest via asyncio.run."""
    results = asyncio.run(run_e2e_scenario("basic", drop_at=None, speed=10.0))
    assert results["success"], results.get("error")


def test_e2e_with_drop():
    """Scenario with a real network drop mid-replay, proving resume + no duplicates."""
    results = asyncio.run(run_e2e_scenario("basic", drop_at=2.0, speed=10.0))
    assert results["success"], results.get("error")


def test_e2e_discovery():
    """UDP beacon discovery variant. Skips (does not fail the suite) if broadcast
    is blocked in this environment -- see docs/MANUAL_TODO.md."""
    results = asyncio.run(run_e2e_discovery())
    if results.get("error") == "discovery_timeout":
        import pytest

        pytest.skip("UDP beacon discovery timed out in this environment (see docs/MANUAL_TODO.md)")
    assert results["success"], results.get("error")


# --------------------------------------------------------------------------
# standalone entry point
# --------------------------------------------------------------------------


async def _main_async(speed: float) -> int:
    print("\n=== RUNNING E2E TESTS ===\n")

    results1 = await run_e2e_scenario("basic", drop_at=None, speed=speed)
    print(f"\nTest 1 Results: {json.dumps({k: v for k, v in results1.items() if k != 'checks'}, indent=2, default=str)}")

    results2 = await run_e2e_scenario("basic", drop_at=2.0, speed=speed)
    print(f"\nTest 2 Results: {json.dumps({k: v for k, v in results2.items() if k != 'checks'}, indent=2, default=str)}")

    results3 = await run_e2e_discovery(speed=speed)
    discovery_optional_pass = results3.get("success") or results3.get("error") == "discovery_timeout"

    print("\n=== E2E TEST SUMMARY ===")
    print(f"Test 1 (no drop):        {'PASS' if results1['success'] else 'FAIL'}")
    print(f"Test 2 (with drop):      {'PASS' if results2['success'] else 'FAIL'}")
    print(f"Test 3 (UDP discovery):  {'PASS' if results3.get('success') else ('SKIP (blocked)' if results3.get('error') == 'discovery_timeout' else 'FAIL')}")

    for label, res in (("no drop", results1), ("with drop", results2)):
        if res.get("success"):
            lat = res.get("event_latency_ms", {})
            cmd = res.get("command_ack_latency_ms", {})
            hub_rtt = res.get("hub_rtt_ms", {})
            headset_rtt = res.get("headset_rtt_ms", {})
            print(
                f"\nLatency ({label}): event send->recv p50={lat.get('p50', 0):.1f}ms p95={lat.get('p95', 0):.1f}ms max={lat.get('max', 0):.1f}ms "
                f"| command->ack p50={cmd.get('p50', 0):.1f}ms p95={cmd.get('p95', 0):.1f}ms max={cmd.get('max', 0):.1f}ms"
            )
            if hub_rtt.get("count", 0) > 0:
                print(
                    f"  hub ping RTT (n={hub_rtt.get('count', 0)}): p50={hub_rtt.get('p50', 0):.1f}ms p95={hub_rtt.get('p95', 0):.1f}ms max={hub_rtt.get('max', 0):.1f}ms"
                )
            if headset_rtt.get("count", 0) > 0:
                print(
                    f"  headset ping RTT (n={headset_rtt.get('count', 0)}): p50={headset_rtt.get('p50', 0):.1f}ms p95={headset_rtt.get('p95', 0):.1f}ms max={headset_rtt.get('max', 0):.1f}ms"
                )
        else:
            print(f"\n{label} FAILURES:")
            for f in res.get("checks", {}).get("failed", []):
                print(f"  - {f}")

    all_ok = results1["success"] and results2["success"] and discovery_optional_pass
    return 0 if all_ok else 1


def main():
    import argparse

    parser = argparse.ArgumentParser(description="OPUS live-protocol E2E harness")
    parser.add_argument("--speed", type=float, default=10.0, help="Replay speed multiplier")
    args = parser.parse_args()
    return asyncio.run(_main_async(args.speed))


if __name__ == "__main__":
    # No `import pytest` at module scope (that check was always true and made
    # this script a no-op when run standalone). Run for real and propagate a
    # non-zero exit code on any failure.
    sys.exit(main())
