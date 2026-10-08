"""Tests for fake_haptic.py and haptic_report.py.

Uses a small local-event-loop helper (asyncio.run per test) instead of
pytest-asyncio, so this track doesn't need an extra dependency beyond
jsonschema/referencing/pytest already used elsewhere in sim/.
"""

import json
import socket
import asyncio
import time
import uuid

import pytest

from fake_haptic import (
    FakeHapticSleeve,
    SleeveConfig,
    MessageValidator,
    SCHEMA_PATH,
    DEVICE_SCHEMA_PATH,
    now_ms,
    new_id,
)
from haptic_report import analyze, DEFAULT_MIN_GAP_MS


def free_udp_port() -> int:
    """Grab an OS-assigned free UDP port (best-effort; small race window like
    any 'find a free port' helper, acceptable for tests)."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


class _ClientProtocol(asyncio.DatagramProtocol):
    """Minimal UDP client used by tests to talk to the sleeve."""

    def __init__(self):
        self.received: list = []
        self._waiters: dict = {}

    def datagram_received(self, data, addr):
        try:
            msg = json.loads(data.decode("utf-8"))
        except json.JSONDecodeError:
            return
        self.received.append(msg)
        key = msg.get("type")
        fut = self._waiters.get(key)
        if fut and not fut.done():
            fut.set_result(msg)

    async def wait_for_type(self, msg_type: str, timeout: float = 2.0):
        loop = asyncio.get_event_loop()
        fut = loop.create_future()
        self._waiters[msg_type] = fut
        return await asyncio.wait_for(fut, timeout=timeout)


async def _make_client(port: int):
    loop = asyncio.get_event_loop()
    transport, proto = await loop.create_datagram_endpoint(
        lambda: _ClientProtocol(), remote_addr=("127.0.0.1", port)
    )
    return transport, proto


# ---------------------------------------------------------------------------
# Schema validation of replies
# ---------------------------------------------------------------------------

def test_ack_message_validates_against_schema():
    validator = MessageValidator(SCHEMA_PATH)
    ack = {"v": 1, "type": "ack", "id": new_id(), "ts_ms": now_ms(), "ack_id": new_id(), "ok": True}
    is_valid, error = validator.validate(ack)
    assert is_valid, error


def test_status_message_validates_against_schema():
    validator = MessageValidator(SCHEMA_PATH)
    status = {
        "v": 1,
        "type": "status",
        "id": new_id(),
        "ts_ms": now_ms(),
        "device_id": "sleeve-sim-01",
        "battery_pct": 91.2,
        "motors_ok": True,
        "imu_ok": True,
        "fw": "sim-0.1.0",
        "last_cue_id": None,
    }
    is_valid, error = validator.validate(status)
    assert is_valid, error


def test_device_command_validates_against_device_schema():
    validator = MessageValidator(DEVICE_SCHEMA_PATH)
    cmd = {"motor": 0, "intensity": 153, "duration_ms": 300, "pattern": "pulse", "cue_id": new_id()}
    is_valid, error = validator.validate(cmd)
    assert is_valid, error


def test_device_command_out_of_range_is_rejected():
    validator = MessageValidator(DEVICE_SCHEMA_PATH)
    # intensity out of 0-255, duration out of 50-400.
    cmd = {"motor": 0, "intensity": 500, "duration_ms": 20, "pattern": "pulse"}
    is_valid, error = validator.validate(cmd)
    assert not is_valid


# ---------------------------------------------------------------------------
# cue -> ack round trip (legacy envelope) and ping -> reply
# ---------------------------------------------------------------------------

def test_cue_ack_round_trip():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            cue_id = new_id()
            cue_msg = {
                "v": 1,
                "type": "cue",
                "id": cue_id,
                "ts_ms": now_ms(),
                "cue": "trunk_lean",
                "intensity": 0.6,
                "duration_ms": 300,
                "pattern": "pulse",
                "zone": "upper_arm",
                "repeat": 1,
                "gap_ms": 0,
            }
            sent_at = time.perf_counter()
            transport.sendto(json.dumps(cue_msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")
            latency_ms = (time.perf_counter() - sent_at) * 1000.0

            assert ack["ack_id"] == cue_id
            assert ack["ok"] is True
            assert latency_ms < 500  # loopback UDP should be near-instant
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_device_command_ack_echoes_cue_id():
    """v1.1: the sleeve replies to a device-level command with an ack whose
    ack_id echoes the caller-supplied cue_id."""
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            cue_id = new_id()
            device_msg = {
                "motor": 1,
                "intensity": 102,
                "duration_ms": 120,
                "pattern": "pulse",
                "cue_id": cue_id,
                "cue": "low_confidence",
            }
            transport.sendto(json.dumps(device_msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")

            assert ack["type"] == "ack"
            assert ack["ack_id"] == cue_id
            assert ack["ok"] is True
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_device_command_without_cue_id_still_acks():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            device_msg = {"motor": 0, "intensity": 200, "duration_ms": 200, "pattern": "buzz"}
            transport.sendto(json.dumps(device_msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")

            assert ack["ok"] is True
            assert isinstance(ack["ack_id"], str) and len(ack["ack_id"]) > 0
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_device_command_with_type_haptic_envelope_is_accepted():
    """S run4 regression: the Electronics team's envelope form
    {"type":"haptic", motor, intensity, duration_ms, pattern, cue_id} must be
    accepted exactly like the bare device command.

    The real firmware dispatches BOTH (opus_sleeve.ino:
    `if (strcmp(type,"haptic")==0 || msg["type"].isNull()) handleHaptic(...)`),
    and tools/demo/sleeve_test.py sends the envelope form. Before this fix the
    simulator validated the envelope against haptic-message.schema.json, which
    has no "haptic" type, so it silently rejected every command that real
    hardware executed -- a simulator that disagreed with the thing it stands in
    for. This test fails if that regresses.
    """
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            cue_id = new_id()
            envelope_msg = {
                "type": "haptic",
                "cue_id": cue_id,
                "motor": 0,
                "intensity": 153,
                "duration_ms": 200,
                "pattern": "buzz",
            }
            transport.sendto(json.dumps(envelope_msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")

            assert ack["ack_id"] == cue_id
            assert ack["ok"] is True
            assert sleeve.state.cue_count == 1
            assert sleeve.state.invalid_count == 0, "envelope form must not be counted invalid"
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_device_command_envelope_still_enforces_ranges():
    """Stripping "type" must not also strip the safety validation: an out-of-range
    intensity in the envelope form is still rejected, exactly as the bare form is."""
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, _proto = await _make_client(port)
            bad = {
                "type": "haptic",
                "cue_id": new_id(),
                "motor": 0,
                "intensity": 999,      # > 255
                "duration_ms": 200,
                "pattern": "buzz",
            }
            transport.sendto(json.dumps(bad).encode("utf-8"))
            await asyncio.sleep(0.3)

            assert sleeve.state.invalid_count == 1
            assert sleeve.state.cue_count == 0
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_ping_gets_a_reply():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            ping_id = new_id()
            ping_msg = {"v": 1, "type": "ping", "id": ping_id, "ts_ms": now_ms()}
            transport.sendto(json.dumps(ping_msg).encode("utf-8"))
            reply = await proto.wait_for_type("ack")

            assert reply["ack_id"] == ping_id
            assert reply["ok"] is True
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_invalid_datagram_is_counted_not_crashed():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            # "type":"cue" but missing required fields -> schema-invalid envelope message.
            bad_msg = {"v": 1, "type": "cue", "id": new_id(), "ts_ms": now_ms()}
            transport.sendto(json.dumps(bad_msg).encode("utf-8"))
            await asyncio.sleep(0.2)
            assert sleeve.state.invalid_count == 1
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


# ---------------------------------------------------------------------------
# Disconnect mode
# ---------------------------------------------------------------------------

def test_drop_after_stops_replies():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, drop_after=0.3))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)

            # Before the cutoff: normal ack.
            cue_id = new_id()
            cue_msg = {
                "v": 1, "type": "cue", "id": cue_id, "ts_ms": now_ms(),
                "cue": "success", "intensity": 0.6, "duration_ms": 200, "pattern": "buzz",
            }
            transport.sendto(json.dumps(cue_msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")
            assert ack["ack_id"] == cue_id

            # Wait past the drop-after cutoff.
            await asyncio.sleep(0.4)

            cue_id_2 = new_id()
            cue_msg_2 = {
                "v": 1, "type": "cue", "id": cue_id_2, "ts_ms": now_ms(),
                "cue": "success", "intensity": 0.6, "duration_ms": 200, "pattern": "buzz",
            }
            transport.sendto(json.dumps(cue_msg_2).encode("utf-8"))
            with pytest.raises(asyncio.TimeoutError):
                await proto.wait_for_type("ack", timeout=0.5)

            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


# ---------------------------------------------------------------------------
# haptic_report.py safety-cap checker
# ---------------------------------------------------------------------------

def test_report_flags_no_violations_on_clean_log():
    records = [
        {"kind": "cue", "id": "a", "cue": "trunk_lean", "intensity": 0.6, "duration_ms": 300,
         "gap_since_prev_ms": None, "min_gap_ms": 800, "recv_ts_ms": 1000.0},
        {"kind": "cue", "id": "b", "cue": "trunk_lean", "intensity": 0.7, "duration_ms": 300,
         "gap_since_prev_ms": 900.0, "min_gap_ms": 800, "recv_ts_ms": 1900.0},
    ]
    result = analyze(records)
    assert result.violations == []
    assert result.total == 2
    assert result.max_intensity_normalized == 0.7
    assert result.min_gap_ms == 900.0


def test_report_flags_synthetic_violations():
    """A synthetic log that violates every cap: intensity > 1.0, duration > 1000ms,
    gap below min_gap, and > 2 cues/s sustained."""
    records = [
        {"kind": "cue", "id": "a", "cue": "trunk_lean", "intensity": 1.5, "duration_ms": 1500,
         "gap_since_prev_ms": None, "min_gap_ms": 800, "recv_ts_ms": 1000.0},
        {"kind": "cue", "id": "b", "cue": "trunk_lean", "intensity": 0.9, "duration_ms": 300,
         "gap_since_prev_ms": 50.0, "min_gap_ms": 800, "recv_ts_ms": 1050.0},
        {"kind": "cue", "id": "c", "cue": "trunk_lean", "intensity": 0.9, "duration_ms": 300,
         "gap_since_prev_ms": 50.0, "min_gap_ms": 800, "recv_ts_ms": 1100.0},
        {"kind": "cue", "id": "d", "cue": "trunk_lean", "intensity": 0.9, "duration_ms": 300,
         "gap_since_prev_ms": 50.0, "min_gap_ms": 800, "recv_ts_ms": 1150.0},
    ]
    result = analyze(records)
    assert len(result.violations) > 0
    joined = "\n".join(result.violations)
    assert "intensity 1.5" in joined
    assert "duration_ms 1500" in joined
    assert "gap_since_prev_ms 50.0 < min_gap_ms 800" in joined
    assert "sustained rate" in joined


def test_report_flags_device_level_violations():
    """Device-format record outside the v1.1 firmware limits (intensity 0-255,
    duration_ms 50-400)."""
    records = [
        {"kind": "device", "cue_id": "x", "cue": "success", "intensity_255": 300, "duration_ms": 40,
         "gap_since_prev_ms": None, "recv_ts_ms": 1000.0},
    ]
    result = analyze(records)
    assert any("device intensity 300" in v for v in result.violations)
    assert any("device duration_ms 40" in v for v in result.violations)


def test_report_default_min_gap_used_when_missing():
    assert DEFAULT_MIN_GAP_MS == 800


# ---------------------------------------------------------------------------
# v0.4.0 firmware parity: 4-motor routing, per-motor MIN_CUE_GAP_MS, clamping,
# dual-dialect ack, nested status, device_discovery, sensor_data.
# ---------------------------------------------------------------------------

from fake_haptic import MOTOR_CHANNELS, MIN_CUE_GAP_MS, MAX_MOTOR_INTENSITY, MIN_DURATION_MS  # noqa: E402


def test_motor_0_to_3_all_accepted_when_all_fitted():
    """With motor_count=4 every channel 0-3 runs as itself (no routing)."""
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=4))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            for motor in range(4):
                cue_id = new_id()
                msg = {"motor": motor, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": cue_id}
                transport.sendto(json.dumps(msg).encode("utf-8"))
                ack = await proto.wait_for_type("ack")
                assert ack["ok"] is True, f"motor {motor} should be accepted, got {ack}"
                assert ack["motor"] == motor, "a fitted channel must run as itself, not be routed"
                assert ack["status"] == "executed"
                await asyncio.sleep(MIN_CUE_GAP_MS / 1000.0 + 0.02)  # clear the per-motor gap before the next
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_unfitted_motor_routes_to_motor0_by_default():
    """Default config: only motor 0 fitted. A cue to motor 1 must fall back to
    motor 0 and the ack must report motor=0 (routed), per ROUTE_MOTOR1_TO_MOTOR0=1."""
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=1, route_unfitted_to_0=True))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            cue_id = new_id()
            msg = {"motor": 1, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": cue_id}
            transport.sendto(json.dumps(msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")
            assert ack["ok"] is True
            assert ack["motor"] == 0, "unfitted motor 1 must fall back to motor 0"
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_unfitted_motor_rejected_when_routing_disabled():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=1, route_unfitted_to_0=False))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            cue_id = new_id()
            msg = {"motor": 2, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": cue_id}
            transport.sendto(json.dumps(msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")
            assert ack["ok"] is False
            assert ack["status"] == "rejected"
            assert ack["error_code"] == "MOTOR_UNAVAILABLE"
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_min_cue_gap_ms_rejects_too_fast_repeat_on_same_motor():
    """Two commands to the same motor faster than MIN_CUE_GAP_MS (100ms): the
    second must be rejected CUE_GAP, matching opus_sleeve.ino's per-motor clock."""
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=4))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)

            id1 = new_id()
            transport.sendto(json.dumps(
                {"motor": 0, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": id1}
            ).encode("utf-8"))
            ack1 = await proto.wait_for_type("ack")
            assert ack1["ok"] is True

            id2 = new_id()
            transport.sendto(json.dumps(
                {"motor": 0, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": id2}
            ).encode("utf-8"))
            # second ack for the SAME id2 must come back rejected -- wait_for_type keys only
            # on message "type", and only one ack is in flight, so this is unambiguous.
            ack2 = await proto.wait_for_type("ack")
            assert ack2["ok"] is False
            assert ack2["status"] == "rejected"
            assert ack2["error_code"] == "CUE_GAP"
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_min_cue_gap_ms_is_per_motor_not_global():
    """The gap clock is per-channel: a second command on a DIFFERENT motor,
    sent immediately after the first, must still be accepted."""
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=4))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)

            id1 = new_id()
            transport.sendto(json.dumps(
                {"motor": 0, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": id1}
            ).encode("utf-8"))
            ack1 = await proto.wait_for_type("ack")
            assert ack1["ok"] is True

            id2 = new_id()
            transport.sendto(json.dumps(
                {"motor": 1, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": id2}
            ).encode("utf-8"))
            ack2 = await proto.wait_for_type("ack")
            assert ack2["ok"] is True, "a different motor's gap clock must be independent"
            assert ack2["motor"] == 1
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_out_of_firmware_range_intensity_and_duration_are_clamped_not_rejected():
    """Within the device schema's wider range (intensity 0-255) but above the
    firmware's MAX_INTENSITY=230: the firmware clamps, it does not reject."""
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=1))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            cue_id = new_id()
            # intensity 255 > MAX_MOTOR_INTENSITY (230); duration 400 is already in range.
            msg = {"motor": 0, "intensity": 255, "duration_ms": 400, "pattern": "pulse", "cue_id": cue_id}
            transport.sendto(json.dumps(msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")
            assert ack["ok"] is True, "out-of-firmware-range but in-schema-range must clamp, not reject"
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_ack_dual_dialect_has_both_cue_id_and_ack_id_and_status_and_ok():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=1))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            cue_id = new_id()
            msg = {"motor": 0, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue_id": cue_id}
            transport.sendto(json.dumps(msg).encode("utf-8"))
            ack = await proto.wait_for_type("ack")
            assert ack["cue_id"] == cue_id
            assert ack["ack_id"] == cue_id
            assert ack["status"] == "executed"
            assert ack["ok"] is True
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_status_has_nested_motor_blocks_and_flat_fields():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port, motor_count=2))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            # Prime peer_addr via a ping, then ask the sleeve to emit a status now.
            ping_id = new_id()
            transport.sendto(json.dumps(
                {"v": 1, "type": "ping", "id": ping_id, "ts_ms": now_ms()}
            ).encode("utf-8"))
            await proto.wait_for_type("ack")
            sleeve.send_status(sleeve.state.peer_addr)
            status = await proto.wait_for_type("status")

            for i in range(MOTOR_CHANNELS):
                block = status[f"motor_{i}"]
                assert "available" in block and "location" in block and "active" in block
                assert block["available"] == (i < 2)
            assert status["motor_count"] == 2
            assert status["motor_channels"] == MOTOR_CHANNELS
            # flat/game-compat fields must still be present
            assert "motors_ok" in status and "imu_ok" in status and "fw" in status
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())


def test_discovery_broadcast_has_device_discovery_and_opus_haptic_fields():
    """Not sent through self._send (bypasses the v1 envelope schema, same as before) --
    captured directly off a UDP socket bound to the discovery port."""
    async def scenario():
        port = free_udp_port()
        disc_port = free_udp_port()
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind(("127.0.0.1", disc_port))
        sock.settimeout(2.0)

        sleeve = FakeHapticSleeve(SleeveConfig(port=port, discovery_port=disc_port, motor_count=3))
        await sleeve.start()
        try:
            loop = asyncio.get_event_loop()
            data, _addr = await loop.run_in_executor(None, sock.recvfrom, 4096)
            payload = json.loads(data.decode("utf-8"))
            assert payload["type"] == "device_discovery"
            assert payload["opus_haptic"] == 1
            assert payload["port"] == port
            assert payload["command_port"] == port
            assert payload["motor_count"] == 3
            assert payload["motor_channels"] == MOTOR_CHANNELS
            assert payload["protocol_version"]
            assert payload["status"] == "available"
        finally:
            sock.close()
            await sleeve.stop()

    asyncio.run(scenario())


def test_sensor_data_message_shape():
    async def scenario():
        port = free_udp_port()
        sleeve = FakeHapticSleeve(SleeveConfig(port=port))
        await sleeve.start()
        try:
            transport, proto = await _make_client(port)
            ping_id = new_id()
            transport.sendto(json.dumps(
                {"v": 1, "type": "ping", "id": ping_id, "ts_ms": now_ms()}
            ).encode("utf-8"))
            await proto.wait_for_type("ack")
            sleeve.send_sensor_data(sleeve.state.peer_addr)
            sensor_msg = await proto.wait_for_type("sensor_data")
            assert sensor_msg["type"] == "sensor_data"
            assert sensor_msg["device_id"] == sleeve.cfg.device_id
            for name in ("imu_accel_x", "imu_accel_y", "imu_accel_z", "imu_gyro_x", "imu_gyro_y", "imu_gyro_z"):
                assert name in sensor_msg["sensors"]
                assert "value" in sensor_msg["sensors"][name] and "unit" in sensor_msg["sensors"][name]
            transport.close()
        finally:
            await sleeve.stop()

    asyncio.run(scenario())
