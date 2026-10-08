#!/usr/bin/env python
"""tools/demo/sleeve_test.py -- S run5, task 1.

Sends spaced-out pulse/buzz/ramp device-format haptic commands, a game-format "cue" command, a
4-motor sweep, and one DELIBERATE too-fast pair to a real (or fake) OPUS haptic sleeve on UDP
`--port` (default 8790, per contracts/HAPTIC_PROTOCOL.md and firmware/opus_sleeve/opus_sleeve.ino),
and prints each command's ack status + round-trip latency in a PASS/FAIL table. Any interleaved
`status`/`sensor_data`/`device_discovery` datagrams the sleeve sends on its own schedule (status
~1 Hz, sensor_data ~20 Hz per the firmware's SENSOR_PERIOD_MS) are read and ignored -- this tool
only reports on the `ack` that answers each command it sent.

S run3/the OPUS sleeve-check log found that the PREVIOUS version of this tool fired its 4 commands
back-to-back with no spacing, which is faster than the firmware's (and now the emulator's, see
sim/haptic/fake_haptic.py) MIN_CUE_GAP_MS=100 PER MOTOR -- every second command was correctly
rejected CUE_GAP by a firmware behaving safely, and the tool reported that as a false "2/4 failed".
Fixed here by (a) spacing every command >= `--gap-ms` (default 150, comfortably above the 100 ms
floor) apart, and (b) adding a command pair that DELIBERATELY violates the gap and treating a
CUE_GAP rejection on that pair as a PASS (the safety limit is supposed to fire), not a failure.

Message formats (read directly from firmware/opus_sleeve/opus_sleeve.ino, not just the protocol
doc, since the real firmware is the ground truth this tool talks to):
  device command : {"type": "haptic", "cue_id": <uuid>, "motor": 0|1|2|3, "intensity": 0-230,
                     "duration_ms": 50-400, "pattern": "pulse"|"buzz"|"ramp"|"double_tap"}
  game-format cue: {"type": "cue", "id": <uuid>, "intensity": 0.0-1.0, "duration_ms": ..,
                     "pattern": "pulse"|"buzz"|"ramp"|"double_tap"}
  ack (reply)    : {"type": "ack", "cue_id"/"ack_id": <uuid>, "status": "accepted|executed|
                     rejected|error", "ok": true|false, "error_code": <str, optional>}

Run (do NOT run this against the real sleeve IP in automated tests -- see
tools/demo/tests/test_sleeve_test.py for the local-fake-backed unit tests):
    analytics/.venv/Scripts/python.exe tools/demo/sleeve_test.py --ip 192.168.1.42
    analytics/.venv/Scripts/python.exe tools/demo/sleeve_test.py --ip 127.0.0.1 --port 8792  # fake
"""
from __future__ import annotations

import argparse
import json
import socket
import time
import uuid
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional


@dataclass
class CommandResult:
    label: str
    sent: Dict[str, Any]
    ack: Optional[Dict[str, Any]]
    latency_ms: Optional[float]
    # True for the deliberate-violation command: passing means the sleeve REJECTED it with
    # CUE_GAP (the safety limit firing correctly), not that it executed.
    expect_reject: bool = False
    expected_error_code: str = "CUE_GAP"

    @property
    def ok(self) -> bool:
        """Whether the sleeve accepted/executed the command (ignores `expect_reject`)."""
        if self.ack is None:
            return False
        status = self.ack.get("status")
        if status is not None:
            return status in ("accepted", "executed")
        return bool(self.ack.get("ok"))

    @property
    def passed(self) -> bool:
        """The PASS/FAIL judgment this tool actually reports: for a normal command, pass means
        accepted/executed. For the deliberate too-fast pair's second command, pass means the
        sleeve correctly rejected it (ideally with error_code == CUE_GAP; a bare fake with no gap
        concept at all that still executes it is reported as a FAIL here, since it means gap
        enforcement was not exercised -- see --against-plain-fake note in main() docs)."""
        if not self.expect_reject:
            return self.ok
        if self.ack is None:
            return False
        if self.ok:
            return False  # it was supposed to be rejected and wasn't
        error_code = self.ack.get("error_code") or self.ack.get("error")
        return error_code == self.expected_error_code

    @property
    def detail(self) -> str:
        if self.ack is None:
            return "timed out waiting for ack"
        error = self.ack.get("error_code") or self.ack.get("error") or ""
        status = self.ack.get("status", "ok" if self.ack.get("ok") else "rejected")
        if self.expect_reject:
            return f"{status} {error} (expected: rejected/{self.expected_error_code})".strip()
        return f"{status} {error}".strip()


def _device_command(motor: int, intensity: int, duration_ms: int, pattern: str,
                     cue_id: Optional[str] = None) -> Dict[str, Any]:
    return {
        "type": "haptic",
        "cue_id": cue_id or str(uuid.uuid4()),
        "motor": motor,
        "intensity": intensity,
        "duration_ms": duration_ms,
        "pattern": pattern,
    }


def _game_format_cue(intensity: float, duration_ms: int, pattern: str) -> Dict[str, Any]:
    return {
        "v": 1,
        "type": "cue",
        "id": str(uuid.uuid4()),
        "ts_ms": time.time() * 1000,
        "cue": "success",
        "intensity": intensity,
        "duration_ms": duration_ms,
        "pattern": pattern,
        "zone": "upper_arm",
    }


def send_and_wait_ack(sock: socket.socket, addr, msg: Dict[str, Any], timeout_s: float = 1.5,
                       max_ignored: int = 25) -> CommandResult:
    """Send one command, then read datagrams until the matching ack arrives (or timeout),
    skipping any status/sensor_data/device_discovery/hello the sleeve interleaves on its own
    schedule."""
    cmd_id = msg.get("cue_id") or msg.get("id")
    payload = json.dumps(msg).encode("utf-8")
    t_sent = time.monotonic()
    sock.sendto(payload, addr)

    sock.settimeout(timeout_s)
    ignored = 0
    while True:
        try:
            data, _ = sock.recvfrom(4096)
        except socket.timeout:
            return CommandResult(label="", sent=msg, ack=None, latency_ms=None)
        except ConnectionResetError:
            # Windows surfaces a prior ICMP port-unreachable (e.g. nothing listening on the
            # target port) as WinError 10054 on the *next* recv, not as a clean timeout. To a
            # caller this means the same thing a timeout does: no sleeve answered.
            return CommandResult(label="", sent=msg, ack=None, latency_ms=None)
        try:
            reply = json.loads(data.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            continue
        rtype = reply.get("type")
        if rtype in ("status", "sensor_data", "hello", "device_discovery", "imu"):
            ignored += 1
            if ignored > max_ignored:
                return CommandResult(label="", sent=msg, ack=None, latency_ms=None)
            continue
        if rtype == "ack":
            ref = reply.get("ack_id") or reply.get("cue_id")
            # Some fakes/firmware echo the id in different fields across versions; accept the
            # ack if it references our id, or (single-in-flight tool, so safe) if it doesn't
            # carry a reference at all.
            if ref is None or ref == cmd_id:
                latency_ms = (time.monotonic() - t_sent) * 1000.0
                return CommandResult(label="", sent=msg, ack=reply, latency_ms=latency_ms)
            continue
        # Any other message type: ignore and keep waiting (bounded by `timeout_s` overall via
        # the socket timeout on the next recv call raising if nothing more arrives).
        ignored += 1
        if ignored > max_ignored:
            return CommandResult(label="", sent=msg, ack=None, latency_ms=None)


def parse_motors(spec: str) -> List[int]:
    return [int(x.strip()) for x in spec.split(",") if x.strip() != ""]


def run_suite(ip: str, port: int, timeout_s: float = 1.5, gap_ms: float = 150.0,
              motors: Optional[List[int]] = None) -> List[CommandResult]:
    """Run the full smoke suite: 3 device-format commands + 1 game-format cue on motor 0
    (spaced >= gap_ms apart), a sweep of `motors` (default 0-3, one pulse each, also spaced),
    and one deliberate too-fast pair on motor 0 that must be rejected CUE_GAP."""
    if motors is None:
        motors = [0, 1, 2, 3]
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    addr = (ip, port)
    results: List[CommandResult] = []
    gap_s = gap_ms / 1000.0
    try:
        first = True

        def send(label: str, msg: Dict[str, Any], expect_reject: bool = False) -> CommandResult:
            nonlocal first
            if not first:
                time.sleep(gap_s)
            first = False
            r = send_and_wait_ack(sock, addr, msg, timeout_s=timeout_s)
            r.label = label
            r.expect_reject = expect_reject
            results.append(r)
            return r

        # --- core smoke: 3 device-format patterns + 1 game-format cue, all on motor 0 ---
        send("device pulse (motor 0)", _device_command(0, 180, 300, "pulse"))
        send("device buzz (motor 0)", _device_command(0, 200, 200, "buzz"))
        send("device ramp (motor 0)", _device_command(0, 150, 250, "ramp"))
        send("game-format cue (success)", _game_format_cue(0.6, 200, "buzz"))

        # --- motor sweep: one pulse per requested channel, still spaced by gap_ms ---
        for motor in motors:
            send(f"sweep motor {motor}", _device_command(motor, 160, 200, "pulse"))

        # --- deliberate too-fast pair on motor 0: NO gap before the second send, on purpose ---
        first_of_pair = send("fast-pair 1st (motor 0)", _device_command(0, 160, 200, "pulse"))
        cmd_id_2 = str(uuid.uuid4())
        msg2 = _device_command(0, 160, 200, "pulse", cue_id=cmd_id_2)
        # Bypass the `send()` helper's gap sleep entirely -- this pair is the one deliberate
        # exception to "every command spaced >= gap_ms".
        r2 = send_and_wait_ack(sock, addr, msg2, timeout_s=timeout_s)
        r2.label = "fast-pair 2nd (motor 0, NO gap)"
        r2.expect_reject = True
        results.append(r2)
        first = False
    finally:
        sock.close()
    return results


def print_report(results: List[CommandResult]) -> int:
    print(f"\n{'Command':<32} {'PASS/FAIL':<10} {'Ack status':<12} {'Latency (ms)':<14} Detail")
    print("-" * 100)
    n_pass = 0
    for r in results:
        verdict = "PASS" if r.passed else "FAIL"
        n_pass += 1 if r.passed else 0
        status = "-" if r.ack is None else r.ack.get("status", "ok" if r.ack.get("ok") else "rejected")
        lat = f"{r.latency_ms:.1f}" if r.latency_ms is not None else "-"
        print(f"{r.label:<32} {verdict:<10} {status:<12} {lat:<14} {r.detail}")
    print("-" * 100)
    print(f"{n_pass}/{len(results)} PASS")
    return 0 if n_pass == len(results) else 1


def main() -> int:
    parser = argparse.ArgumentParser(description="OPUS haptic sleeve smoke test")
    parser.add_argument("--ip", required=True, help="Sleeve IP address")
    parser.add_argument("--port", type=int, default=8790, help="Sleeve UDP command port (default 8790)")
    parser.add_argument("--timeout", type=float, default=1.5, help="Per-command ack timeout, seconds")
    parser.add_argument("--gap-ms", type=float, default=150.0,
                         help="Spacing between commands, ms (default 150, above the firmware's "
                              "MIN_CUE_GAP_MS=100 floor -- do not set this below 100 or every "
                              "other command will be legitimately CUE_GAP-rejected)")
    parser.add_argument("--motors", type=str, default="0,1,2,3",
                         help="Comma-separated motor channels to sweep (default 0,1,2,3)")
    args = parser.parse_args()

    results = run_suite(args.ip, args.port, timeout_s=args.timeout, gap_ms=args.gap_ms,
                         motors=parse_motors(args.motors))
    return print_report(results)


if __name__ == "__main__":
    raise SystemExit(main())
