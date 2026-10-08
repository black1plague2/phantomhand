"""Unit tests for tools/demo/sleeve_test.py against a LOCAL fake sleeve only -- never the real
ESP32 IP (per the S run2 task brief). The fake here mirrors firmware/opus_sleeve/opus_sleeve.ino's
exact `handleMessage` dispatch (type "haptic"/"cue"/"ping"/"stop"/"status_request", ack fields
"ack_id"/"status"/"ok"), which is a different wire shape than sim/haptic/fake_haptic.py's
dispatcher (that one treats a device command as the *absence* of a "type" key, matching the
Electronics team's original pre-firmware spec -- see CHECKPOINT notes in the session log: the
real firmware adds "type":"haptic" to the device command, so fake_haptic.py's `_handle_device_command`
branch is not reachable by a client that sends what the real firmware expects. Flagged, not fixed
here -- out of this task's remit to touch fake_haptic.py's dispatch without checking with N/U/A
tracks that depend on its current behavior).

Run: analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_sleeve_test.py -v
"""
import json
import socket
import sys
import threading
import time
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import sleeve_test as st


class LocalFakeSleeve(threading.Thread):
    """Minimal stand-in for the real ESP32 firmware's UDP handler, run in a background thread
    on an OS-assigned port. Not sim/haptic/fake_haptic.py on purpose (see module docstring).

    S run5: added a per-motor MIN_CUE_GAP_MS=100 clock, matching
    firmware/opus_sleeve/opus_sleeve.ino's handleHaptic() (`now - m.lastStartMs < MIN_CUE_GAP_MS`
    -> reject "CUE_GAP"), so sleeve_test.py's new deliberate too-fast-pair case has something real
    -- not just the live emulator -- to exercise in an offline unit test."""

    MIN_CUE_GAP_MS = 100

    def __init__(self, reject_pattern: str = None):
        super().__init__(daemon=True)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self.sock.settimeout(0.2)
        self.port = self.sock.getsockname()[1]
        self._stop = False
        self.reject_pattern = reject_pattern
        self.received = []
        self._motor_last_start = {}

    def run(self):
        while not self._stop:
            try:
                data, addr = self.sock.recvfrom(4096)
            except socket.timeout:
                continue
            except OSError:
                break
            try:
                msg = json.loads(data.decode("utf-8"))
            except json.JSONDecodeError:
                continue
            self.received.append(msg)
            mtype = msg.get("type")
            if mtype == "haptic":
                cue_id = msg.get("cue_id", "")
                motor = msg.get("motor", 0)
                now = time.monotonic() * 1000.0
                last = self._motor_last_start.get(motor)
                if last is not None and (now - last) < self.MIN_CUE_GAP_MS:
                    self._send_ack(addr, cue_id, "rejected", ok=False, error="CUE_GAP", motor=motor)
                    continue
                if self.reject_pattern and msg.get("pattern") == self.reject_pattern:
                    self._send_ack(addr, cue_id, "rejected", ok=False, error="INVALID_PATTERN", motor=motor)
                else:
                    self._motor_last_start[motor] = now
                    self._send_ack(addr, cue_id, "executed", ok=True, motor=motor)
                # Interleave a status datagram right after, like the real firmware's periodic
                # sends can race with an ack -- the client must skip over it.
                self._send_status(addr)
            elif mtype == "cue":
                self._send_ack(addr, msg.get("id", ""), "executed", ok=True)
            elif mtype == "ping":
                self._send_ack(addr, msg.get("id", ""), "accepted", ok=True)

    def _send_ack(self, addr, cue_id, status, ok, error=None, motor=None):
        reply = {"type": "ack", "cue_id": cue_id, "ack_id": cue_id, "status": status, "ok": ok}
        if error:
            reply["error"] = error
            reply["error_code"] = error
        if motor is not None:
            reply["motor"] = motor
        self.sock.sendto(json.dumps(reply).encode("utf-8"), addr)

    def _send_status(self, addr):
        reply = {"type": "status", "device_id": "fake-sleeve", "battery_pct": 91, "connected": True}
        self.sock.sendto(json.dumps(reply).encode("utf-8"), addr)

    def stop(self):
        self._stop = True
        self.sock.close()


@pytest.fixture
def fake_sleeve():
    sleeve = LocalFakeSleeve()
    sleeve.start()
    time.sleep(0.05)
    yield sleeve
    sleeve.stop()


def test_device_pulse_gets_executed_ack(fake_sleeve):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        msg = st._device_command(0, 180, 300, "pulse")
        result = st.send_and_wait_ack(sock, ("127.0.0.1", fake_sleeve.port), msg, timeout_s=1.0)
    finally:
        sock.close()
    assert result.ack is not None
    assert result.ok
    assert result.latency_ms is not None and result.latency_ms >= 0


def test_status_and_sensor_data_are_skipped_not_mistaken_for_ack(fake_sleeve):
    """The fake sends a status datagram right after every ack (see LocalFakeSleeve.run) --
    confirms send_and_wait_ack does not stop on that status and still returns the real ack."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        msg = st._device_command(0, 150, 250, "ramp")
        result = st.send_and_wait_ack(sock, ("127.0.0.1", fake_sleeve.port), msg, timeout_s=1.0)
    finally:
        sock.close()
    assert result.ack is not None
    assert result.ack.get("type") == "ack"


def test_game_format_cue_gets_ack(fake_sleeve):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        msg = st._game_format_cue(0.6, 200, "buzz")
        result = st.send_and_wait_ack(sock, ("127.0.0.1", fake_sleeve.port), msg, timeout_s=1.0)
    finally:
        sock.close()
    assert result.ok


def test_rejected_pattern_reports_not_ok():
    sleeve = LocalFakeSleeve(reject_pattern="buzz")
    sleeve.start()
    time.sleep(0.05)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        msg = st._device_command(0, 200, 200, "buzz")
        result = st.send_and_wait_ack(sock, ("127.0.0.1", sleeve.port), msg, timeout_s=1.0)
    finally:
        sock.close()
        sleeve.stop()
    assert result.ack is not None
    assert result.ok is False
    assert result.ack.get("status") == "rejected"


def test_no_sleeve_times_out_cleanly():
    """No fake running on this port: send_and_wait_ack must return ack=None, not raise."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    unused_port = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    unused_port.bind(("127.0.0.1", 0))
    port = unused_port.getsockname()[1]
    unused_port.close()
    try:
        msg = st._device_command(0, 180, 300, "pulse")
        result = st.send_and_wait_ack(sock, ("127.0.0.1", port), msg, timeout_s=0.3)
    finally:
        sock.close()
    assert result.ack is None
    assert result.latency_ms is None


def test_run_suite_end_to_end(fake_sleeve):
    """S run5: run_suite now sends the core 4 + a 4-motor sweep + a deliberate too-fast
    pair = 10 commands total, spaced by gap_ms except the deliberate pair. Use a small
    gap_ms so the test stays fast; LocalFakeSleeve's MIN_CUE_GAP_MS=100 still gates the
    pass/fail judgment correctly as long as gap_ms > 100."""
    results = st.run_suite("127.0.0.1", fake_sleeve.port, timeout_s=1.0, gap_ms=120.0)
    assert len(results) == 4 + 4 + 2  # core smoke + motor sweep (0-3) + fast-pair (1st + 2nd)
    assert all(r.passed for r in results), [
        (r.label, r.ack) for r in results if not r.passed
    ]
    exit_code = st.print_report(results)
    assert exit_code == 0


def test_deliberate_fast_pair_is_flagged_pass_when_second_is_rejected(fake_sleeve):
    """The last result is the fast-pair's second command: it must come back rejected
    CUE_GAP against LocalFakeSleeve's gap clock, and that rejection must count as PASS
    (not FAIL) because the safety limit firing correctly IS the expected behavior."""
    results = st.run_suite("127.0.0.1", fake_sleeve.port, timeout_s=1.0, gap_ms=120.0)
    fast_pair_2nd = results[-1]
    assert fast_pair_2nd.expect_reject is True
    assert fast_pair_2nd.ok is False, "the too-fast second command must be rejected, not executed"
    assert fast_pair_2nd.ack.get("error") == "CUE_GAP" or fast_pair_2nd.ack.get("error_code") == "CUE_GAP"
    assert fast_pair_2nd.passed is True, "a correctly-fired CUE_GAP rejection must be reported PASS"


def test_motor_sweep_covers_all_requested_motors():
    sleeve = LocalFakeSleeve()
    sleeve.start()
    time.sleep(0.05)
    try:
        results = st.run_suite("127.0.0.1", sleeve.port, timeout_s=1.0, gap_ms=120.0, motors=[0, 1])
    finally:
        sleeve.stop()
    sweep_labels = [r.label for r in results if r.label.startswith("sweep motor")]
    assert sweep_labels == ["sweep motor 0", "sweep motor 1"]
