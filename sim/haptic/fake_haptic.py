"""S: fake_haptic.py - software fake for the OPUS haptic sleeve (contracts/HAPTIC_PROTOCOL.md).

UDP JSON sleeve simulator. Software only: no firmware, no BLE, no hardware.
Validates every inbound datagram against contracts/schemas/haptic-message.schema.json,
acks cues, answers pings, broadcasts `hello` for discovery, and sends `status` at 1 Hz
(with a slowly-draining battery) so Unity/Flutter can develop and test against it with
no physical sleeve present.
"""

import sys
import json
import time
import uuid
import socket
import asyncio
import logging
import argparse
from pathlib import Path
from dataclasses import dataclass, field
from typing import Optional, Dict, Any, Tuple, List

import jsonschema
from jsonschema import Draft202012Validator, ValidationError

logger = logging.getLogger(__name__)

SCHEMA_PATH = Path(__file__).resolve().parents[2] / "contracts" / "schemas" / "haptic-message.schema.json"
DEVICE_SCHEMA_PATH = Path(__file__).resolve().parents[2] / "contracts" / "schemas" / "haptic-device-command.schema.json"


class MessageValidator:
    """Validate a JSON object against a single self-contained schema file.

    Unlike sim/live/protocol.py's MessageValidator, these schemas have no
    cross-file $ref, so a bare Draft202012Validator (no referencing.Registry)
    is enough. Used for both haptic-message.schema.json (the v1 envelope: cue/
    stop/config/ping/status/ack/imu) and, as of v1.1, haptic-device-command.schema.json
    (the Electronics team's wire format: motor/intensity/duration_ms/pattern).
    """

    def __init__(self, schema_path: Path = SCHEMA_PATH):
        self.schema_path = Path(schema_path)
        with open(self.schema_path, "r") as f:
            self._schema = json.load(f)
        self._validator = Draft202012Validator(self._schema)

    def validate(self, message: Dict[str, Any]) -> Tuple[bool, Optional[str]]:
        """Return (is_valid, error_message)."""
        try:
            self._validator.validate(message)
            return True, None
        except ValidationError as e:
            return False, str(e)


def now_ms() -> float:
    return time.time() * 1000


def new_id() -> str:
    return str(uuid.uuid4())


# ---- firmware v0.4.0 parity constants (firmware/opus_sleeve/opus_sleeve.ino) ----
# These mirror the #define values in the .ino exactly, so the emulator rejects/clamps the
# same way the real board does. Keep in sync if the firmware's numbers change.
MOTOR_CHANNELS = 4            # addressable channels; matches schema enum [0,1,2,3]
MIN_MOTOR_INTENSITY = 0
MAX_MOTOR_INTENSITY = 230     # ~90% duty: coin motors run hot/loud at 100% on 5V (ino MAX_INTENSITY)
MIN_DURATION_MS = 50
MAX_DURATION_MS = 400
MIN_CUE_GAP_MS = 100          # per motor, start-to-start (ino MIN_CUE_GAP_MS)
MOTOR_LOCATIONS = ["upper_arm", "forearm", "tbd_2", "tbd_3"]
FIRMWARE_VERSION = "0.4.0"
PROTOCOL_VERSION = "1.0"


@dataclass
class SleeveConfig:
    device_id: str = "sleeve-sim-01"
    device_name: str = "OPUS Haptic Sleeve (sim)"
    fw: str = "sim-0.4.0"
    port: int = 8790
    discovery_port: int = 8791
    imu: bool = False
    imu_rate_hz: float = 50.0
    drop_after: Optional[float] = None
    battery_start: float = 100.0
    json_log: Optional[Path] = None
    # v0.4.0 parity: how many of the 4 addressable channels are physically fitted, and
    # whether a cue addressed to an unfitted channel falls back to motor 0 (ino ROUTE_MOTORn_TO_MOTOR0)
    # or is rejected outright with MOTOR_UNAVAILABLE. Real board default today: 1 fitted, routed.
    motor_count: int = 1
    route_unfitted_to_0: bool = True


@dataclass
class SleeveState:
    """Mutable runtime state of the fake sleeve."""

    battery_pct: float
    motors_ok: bool = True
    imu_ok: bool = True
    last_cue_id: Optional[str] = None
    last_cue_ts: Optional[float] = None
    enabled: bool = True
    max_intensity: float = 0.8
    min_gap_ms: int = 800
    peer_addr: Optional[Tuple[str, int]] = None
    invalid_count: int = 0
    cue_count: int = 0
    ping_count: int = 0
    ack_count: int = 0
    dropped: bool = False
    start_ts: float = field(default_factory=time.monotonic)
    imu_seq: int = 0
    # per-motor cue-gap clock (monotonic ms), keyed by channel index 0..MOTOR_CHANNELS-1
    motor_last_start_ms: Dict[int, float] = field(default_factory=dict)
    motor_active: Dict[int, bool] = field(default_factory=dict)


class FakeHapticSleeve:
    """Owns the UDP transport, discovery beacon, status/imu loops, and message handling."""

    def __init__(self, cfg: SleeveConfig):
        self.cfg = cfg
        self.validator = MessageValidator(SCHEMA_PATH)
        self.device_validator = MessageValidator(DEVICE_SCHEMA_PATH)
        self.state = SleeveState(battery_pct=cfg.battery_start)
        self.transport: Optional[asyncio.DatagramTransport] = None
        self._json_log_fp = None
        if cfg.json_log:
            cfg.json_log.parent.mkdir(parents=True, exist_ok=True)
            self._json_log_fp = open(cfg.json_log, "a", encoding="utf-8")
        self._tasks: List[asyncio.Task] = []

    # ---- lifecycle -----------------------------------------------------

    async def start(self) -> None:
        loop = asyncio.get_event_loop()
        transport, _protocol = await loop.create_datagram_endpoint(
            lambda: _SleeveDatagramProtocol(self),
            local_addr=("0.0.0.0", self.cfg.port),
        )
        self.transport = transport
        logger.info(f"[HAPTIC] Listening on UDP 0.0.0.0:{self.cfg.port}")

        self._tasks.append(asyncio.create_task(self._hello_loop()))
        self._tasks.append(asyncio.create_task(self._status_loop()))
        if self.cfg.imu:
            self._tasks.append(asyncio.create_task(self._imu_loop()))
            # §7 sensor_data rides the same --imu opt-in as the legacy `imu` message
            # (both need imu_ok/peer_addr; no separate flag to avoid doubling default traffic).
            self._tasks.append(asyncio.create_task(self._sensor_loop()))

    async def stop(self) -> None:
        for t in self._tasks:
            t.cancel()
        for t in self._tasks:
            try:
                await t
            except (asyncio.CancelledError, Exception):
                pass
        if self.transport:
            self.transport.close()
        if self._json_log_fp:
            self._json_log_fp.close()

    def _is_dropped(self) -> bool:
        """True once --drop-after seconds have elapsed: the sleeve stops responding
        (no acks, no status, no hello beacons) to simulate a disconnect."""
        if self.cfg.drop_after is None:
            return False
        elapsed = time.monotonic() - self.state.start_ts
        if elapsed >= self.cfg.drop_after and not self.state.dropped:
            self.state.dropped = True
            logger.warning(f"[HAPTIC] --drop-after {self.cfg.drop_after}s elapsed: going silent")
        return self.state.dropped

    # ---- outbound helpers ------------------------------------------------

    def _send(self, msg: Dict[str, Any], addr: Tuple[str, int]) -> None:
        is_valid, error = self.validator.validate(msg)
        if not is_valid:
            logger.error(f"[HAPTIC TX] Refusing to send invalid message: {error}")
            return
        if self.transport is None:
            return
        try:
            self.transport.sendto(json.dumps(msg).encode("utf-8"), addr)
        except OSError as e:
            logger.debug(f"[HAPTIC TX] send failed: {e}")

    def _base(self, msg_type: str) -> Dict[str, Any]:
        return {"v": 1, "type": msg_type, "id": new_id(), "ts_ms": now_ms()}

    def send_ack(self, ack_id: str, addr: Tuple[str, int], ok: bool = True, error: Optional[str] = None,
                 status: Optional[str] = None, motor: Optional[int] = None,
                 error_code: Optional[str] = None) -> None:
        """v0.4.0 dual dialect (opus_sleeve.ino sendAck): the reply carries BOTH
        cue_id + status (the Electronics contract's fields) AND ack_id + ok (the
        game/HapticClient fields) on the same message, so either dialect's reader
        finds what it expects. `status` is one of accepted|executed|rejected|error;
        defaults to "executed"/"rejected" from `ok` when the caller omits it."""
        if status is None:
            status = "executed" if ok else "rejected"
        msg = self._base("ack")
        msg["ack_id"] = ack_id
        msg["cue_id"] = ack_id
        msg["status"] = status
        msg["ok"] = ok
        if motor is not None:
            msg["motor"] = motor
        if error is not None:
            msg["error"] = error
        if error_code is not None:
            msg["error_code"] = error_code
            msg["error_message"] = error or ""
        self._send(msg, addr)
        self.state.ack_count += 1

    def send_status(self, addr: Tuple[str, int]) -> None:
        """v0.4.0 dual dialect (opus_sleeve.ino sendStatus): nested motor_0..motor_3
        blocks (the Electronics contract's §4 shape) PLUS the flat motors_ok/imu_ok/fw
        fields the game already reads. motor_count/motor_channels distinguish fitted
        (self.cfg.motor_count) from addressable (MOTOR_CHANNELS=4)."""
        msg = self._base("status")
        msg["device_id"] = self.cfg.device_id
        msg["battery_pct"] = round(self.state.battery_pct, 2)
        msg["connected"] = True
        for i in range(MOTOR_CHANNELS):
            msg[f"motor_{i}"] = {
                "available": self._fitted(i),
                "location": MOTOR_LOCATIONS[i],
                "temperature_c": None,
                "duty_pct": 0,
                "active": bool(self.state.motor_active.get(i, False)) if self._fitted(i) else False,
            }
        msg["motor_count"] = self.cfg.motor_count
        msg["motor_channels"] = MOTOR_CHANNELS
        msg["imu_available"] = self.state.imu_ok
        # flat/game-compat fields (unchanged from earlier versions of this simulator)
        msg["motors_ok"] = self.state.motors_ok
        msg["imu_ok"] = self.state.imu_ok
        msg["fw"] = self.cfg.fw
        msg["last_cue_id"] = self.state.last_cue_id
        self._send(msg, addr)

    def _fitted(self, motor: int) -> bool:
        return 0 <= motor < self.cfg.motor_count

    def _route_motor(self, requested: int) -> Optional[int]:
        """Mirror opus_sleeve.ino's routeMotor(): a fitted channel runs as itself;
        an unfitted one in range falls back to motor 0 when cfg.route_unfitted_to_0
        is set (ROUTE_MOTORn_TO_MOTOR0 in the firmware), else it is rejected
        (returns None -> MOTOR_UNAVAILABLE). Motor 0 itself unfitted means nothing
        is fitted -- always rejected, never routed to itself."""
        if self._fitted(requested):
            return requested
        if requested == 0:
            return None
        return 0 if self.cfg.route_unfitted_to_0 else None

    def send_imu(self, addr: Tuple[str, int]) -> None:
        import math

        t = time.monotonic()
        msg = self._base("imu")
        msg["seq"] = self.state.imu_seq
        # Fake but plausible values: near-rest accel (~1g on Z) with small jitter.
        msg["ax"] = round(0.02 * math.sin(t * 3.1), 4)
        msg["ay"] = round(0.02 * math.cos(t * 2.7), 4)
        msg["az"] = round(1.0 + 0.01 * math.sin(t * 5.0), 4)
        msg["gx"] = round(0.5 * math.sin(t * 1.3), 4)
        msg["gy"] = round(0.5 * math.cos(t * 1.1), 4)
        msg["gz"] = round(0.3 * math.sin(t * 0.9), 4)
        self._send(msg, addr)
        self.state.imu_seq += 1

    def send_sensor_data(self, addr: Tuple[str, int]) -> None:
        """§7 sensor_data (opus_sleeve.ino sendSensors): RAW-unit IMU readings nested
        under a `sensors` dict, distinct from the legacy `imu` v1-envelope message
        above (send_imu) which the app/Unity side already reads. Not in
        haptic-message.schema.json's type enum, so -- like discovery -- this bypasses
        the envelope validator, matching how the real firmware's extra message types
        are not validated against the software's own schema (the schema describes what
        WE send, not everything the sleeve emits)."""
        import math

        t = time.monotonic()

        def put(name: str, value: float, unit: str) -> Dict[str, Any]:
            return {"value": round(value, 4), "unit": unit, "status": "ok"}

        payload = {
            "type": "sensor_data",
            "device_id": self.cfg.device_id,
            "timestamp_ms": now_ms(),
            "sensors": {
                "imu_accel_x": put("imu_accel_x", 0.2 * math.sin(t * 3.1), "m/s2"),
                "imu_accel_y": put("imu_accel_y", 0.2 * math.cos(t * 2.7), "m/s2"),
                "imu_accel_z": put("imu_accel_z", 9.81 + 0.1 * math.sin(t * 5.0), "m/s2"),
                "imu_gyro_x": put("imu_gyro_x", 0.05 * math.sin(t * 1.3), "rad/s"),
                "imu_gyro_y": put("imu_gyro_y", 0.05 * math.cos(t * 1.1), "rad/s"),
                "imu_gyro_z": put("imu_gyro_z", 0.03 * math.sin(t * 0.9), "rad/s"),
                "imu_temperature": put("imu_temperature", 30.0 + 0.5 * math.sin(t * 0.2), "degC"),
            },
        }
        data = json.dumps(payload).encode("utf-8")
        if self.transport is not None:
            try:
                self.transport.sendto(data, addr)
            except OSError as e:
                logger.debug(f"[HAPTIC TX] sensor_data send failed: {e}")

    def broadcast_hello(self) -> None:
        """Broadcast the discovery beacon on the discovery port. Not part of
        haptic-message.schema.json (no v1 envelope) per HAPTIC_PROTOCOL.md -- like the
        real firmware's sendDiscovery(), this is ONE datagram carrying both dialects:
        the Electronics contract's §5 `device_discovery` fields (device_id, device_name,
        firmware_version, protocol_version, ip, command_port, status, motor_count,
        motor_channels) AND the game/HapticClient's hello fields (opus_haptic, port)."""
        payload = {
            "type": "device_discovery",
            "device_id": self.cfg.device_id,
            "device_name": self.cfg.device_name,
            "firmware_version": self.cfg.fw,
            "protocol_version": PROTOCOL_VERSION,
            "ip": "127.0.0.1",
            "command_port": self.cfg.port,
            "status": "available",
            "motor_count": self.cfg.motor_count,
            "motor_channels": MOTOR_CHANNELS,
            "timestamp_ms": now_ms(),
            # game/HapticClient compat fields (unchanged since the pre-v0.4.0 simulator)
            "opus_haptic": 1,
            "port": self.cfg.port,
            "fw": self.cfg.fw,
        }
        data = json.dumps(payload).encode("utf-8")
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        except OSError:
            pass
        for target in ("255.255.255.255", "127.255.255.255"):
            try:
                sock.sendto(data, (target, self.cfg.discovery_port))
            except OSError as e:
                logger.debug(f"[HAPTIC] hello broadcast to {target} failed: {e}")
        # Loopback directly too: some Windows setups filter broadcast (see
        # sim/live/fake_hub.py's identical workaround / docs/MANUAL_TODO.md).
        try:
            sock.sendto(data, ("127.0.0.1", self.cfg.discovery_port))
        except OSError as e:
            logger.debug(f"[HAPTIC] hello loopback failed: {e}")
        sock.close()

    # ---- background loops -----------------------------------------------

    async def _hello_loop(self) -> None:
        try:
            while True:
                if not self._is_dropped():
                    self.broadcast_hello()
                await asyncio.sleep(1.0)
        except asyncio.CancelledError:
            pass

    async def _status_loop(self) -> None:
        try:
            while True:
                await asyncio.sleep(1.0)
                # Slow drain: ~0.05%/s, floored at 0.
                self.state.battery_pct = max(0.0, self.state.battery_pct - 0.05)
                if not self._is_dropped() and self.state.peer_addr:
                    self.send_status(self.state.peer_addr)
        except asyncio.CancelledError:
            pass

    async def _imu_loop(self) -> None:
        interval = 1.0 / max(1.0, min(self.cfg.imu_rate_hz, 50.0))
        try:
            while True:
                await asyncio.sleep(interval)
                if not self._is_dropped() and self.state.peer_addr:
                    self.send_imu(self.state.peer_addr)
        except asyncio.CancelledError:
            pass

    async def _sensor_loop(self) -> None:
        """§7 sensor_data at ino's SENSOR_PERIOD_MS=50 (20 Hz), gated on imu_ok like the
        real firmware's `if (havePeer && imuOk && ...)`."""
        try:
            while True:
                await asyncio.sleep(0.05)
                if not self._is_dropped() and self.state.peer_addr and self.state.imu_ok:
                    self.send_sensor_data(self.state.peer_addr)
        except asyncio.CancelledError:
            pass

    # ---- inbound handling -------------------------------------------------

    def handle_datagram(self, data: bytes, addr: Tuple[str, int]) -> None:
        self.state.peer_addr = addr

        try:
            msg = json.loads(data.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError) as e:
            self.state.invalid_count += 1
            logger.error(f"[HAPTIC RX] Failed to parse JSON from {addr}: {e}")
            return

        # v1.1: the primary wire format is the Electronics team's device-level
        # command (motor/intensity/duration_ms/pattern). Our richer semantic `cue`
        # envelope (v/type/id/ts_ms/...) is kept as an internal/back-compat format.
        #
        # S run4 fix: the device command reaches the sleeve in TWO shapes, and this
        # simulator only accepted one of them. The real firmware
        # (firmware/opus_sleeve/opus_sleeve.ino, dispatch line:
        #   if (strcmp(type,"haptic")==0 || msg["type"].isNull()) handleHaptic(...)
        # ) accepts the device command both bare (no "type") and wrapped in the
        # Electronics team's {"type":"haptic",...} envelope -- its own §2 header
        # documents the envelope form as THE command format. tools/demo/sleeve_test.py
        # sends the envelope form, so every device command it sent was rejected here
        # as schema-invalid while the identical command worked on real hardware.
        # The simulator is a stand-in for the firmware, so it must accept everything
        # the firmware accepts. (contracts/HAPTIC_PROTOCOL.md v1.1 shows only the bare
        # form and is silent on the envelope -- flagged to Opus as a wording gap; no
        # schema change is needed, "type" is simply stripped before validation.)
        if isinstance(msg, dict) and (
            "type" not in msg or msg.get("type") == "haptic"
        ):
            self._handle_device_command(
                {k: v for k, v in msg.items() if k != "type"}, addr
            )
            return

        is_valid, error = self.validator.validate(msg)
        if not is_valid:
            self.state.invalid_count += 1
            logger.error(f"[HAPTIC RX] Validation failed from {addr}: {error}")
            return

        if self._is_dropped():
            # Still counted above (a dropped sleeve is still receiving, just not
            # replying), but no further action / no replies.
            return

        msg_type = msg.get("type")
        if msg_type == "cue":
            self._handle_cue(msg, addr)
        elif msg_type == "ping":
            self._handle_ping(msg, addr)
        elif msg_type == "stop":
            self._handle_stop(msg, addr)
        elif msg_type == "config":
            self._handle_config(msg, addr)
        else:
            logger.debug(f"[HAPTIC RX] Unhandled type={msg_type} from {addr}")

    def _handle_cue(self, msg: Dict[str, Any], addr: Tuple[str, int]) -> None:
        self.state.cue_count += 1
        now = time.monotonic()
        gap_ms = None if self.state.last_cue_ts is None else (now - self.state.last_cue_ts) * 1000.0
        self.state.last_cue_ts = now
        self.state.last_cue_id = msg.get("id")

        cue = msg.get("cue")
        intensity = msg.get("intensity")
        pattern = msg.get("pattern")
        duration_ms = msg.get("duration_ms")

        gap_str = "n/a" if gap_ms is None else f"{gap_ms:.1f}ms"
        logger.info(
            f"[CUE] cue={cue} intensity={intensity} pattern={pattern} "
            f"duration_ms={duration_ms} zone={msg.get('zone', 'upper_arm')} gap_since_prev={gap_str}"
        )

        if self._json_log_fp:
            record = {
                "kind": "cue",
                "recv_ts_ms": now_ms(),
                "id": msg.get("id"),
                "cue": cue,
                "intensity": intensity,
                "pattern": pattern,
                "duration_ms": duration_ms,
                "zone": msg.get("zone", "upper_arm"),
                "repeat": msg.get("repeat", 1),
                "gap_ms": msg.get("gap_ms", 0),
                "gap_since_prev_ms": gap_ms,
                "min_gap_ms": self.state.min_gap_ms,
            }
            self._json_log_fp.write(json.dumps(record) + "\n")
            self._json_log_fp.flush()

        self.send_ack(ack_id=msg.get("id"), addr=addr, ok=True)

    def _handle_device_command(self, msg: Dict[str, Any], addr: Tuple[str, int]) -> None:
        """v1.1/v0.4.0: handle the Electronics team's device-level wire format
        ({"motor","intensity","duration_ms","pattern","cue_id"?,"cue"?}). This is the
        primary format sent by `HapticClient`; the semantic `cue` envelope above is
        kept for internal/back-compat use.

        Mirrors opus_sleeve.ino's handleHaptic() safety order exactly:
          1. schema/range check (INVALID_MOTOR is already covered by the device
             schema's `motor` enum [0,1,2,3] -- anything else is schema-invalid, not
             a "rejected" ack, same as the firmware's `requested < 0 || >= MOTOR_CHANNELS`
             short-circuit before routing is even attempted)
          2. route to a fitted channel or reject MOTOR_UNAVAILABLE
          3. per-motor MIN_CUE_GAP_MS or reject CUE_GAP
          4. clamp (not reject) intensity/duration into firmware range
          5. accept: ack "executed", echo cue_id, report the channel that actually ran
        """
        is_valid, error = self.device_validator.validate(msg)
        if not is_valid:
            self.state.invalid_count += 1
            logger.error(f"[HAPTIC RX] Device-command validation failed from {addr}: {error}")
            return

        if self._is_dropped():
            return

        cue_id = msg.get("cue_id")
        ack_id = cue_id if cue_id else new_id()

        requested = msg.get("motor")
        intensity_255 = msg.get("intensity")
        duration_ms = msg.get("duration_ms")
        pattern = msg.get("pattern")
        cue_label = msg.get("cue")

        # Step 2: route to a fitted channel, or MOTOR_UNAVAILABLE (ino's routeMotor()).
        motor = self._route_motor(requested)
        if motor is None:
            self.send_ack(ack_id=ack_id, addr=addr, ok=False, status="rejected",
                          error_code="MOTOR_UNAVAILABLE",
                          error="requested motor channel is not fitted")
            logger.info(f"[CUE][device] rejected MOTOR_UNAVAILABLE requested={requested} cue_id={cue_id}")
            return

        # Step 3: per-motor cue-gap clock (ino: `now - m.lastStartMs < MIN_CUE_GAP_MS`).
        now_monotonic_ms = time.monotonic() * 1000.0
        last_start = self.state.motor_last_start_ms.get(motor)
        if last_start is not None and (now_monotonic_ms - last_start) < MIN_CUE_GAP_MS:
            self.send_ack(ack_id=ack_id, addr=addr, ok=False, status="rejected", motor=motor,
                          error_code="CUE_GAP", error="commands closer than MIN_CUE_GAP_MS")
            logger.info(
                f"[CUE][device] rejected CUE_GAP motor={motor} "
                f"gap_ms={now_monotonic_ms - last_start:.1f} (< {MIN_CUE_GAP_MS}) cue_id={cue_id}"
            )
            return

        self.state.cue_count += 1
        now = time.monotonic()
        gap_ms = None if self.state.last_cue_ts is None else (now - self.state.last_cue_ts) * 1000.0
        self.state.last_cue_ts = now
        self.state.last_cue_id = ack_id
        self.state.motor_last_start_ms[motor] = now_monotonic_ms
        self.state.motor_active[motor] = True

        # Step 4: clamp (not reject) out-of-firmware-range intensity/duration.
        intensity_clamped = max(MIN_MOTOR_INTENSITY, min(MAX_MOTOR_INTENSITY, int(intensity_255)))
        duration_clamped = max(MIN_DURATION_MS, min(MAX_DURATION_MS, int(duration_ms)))

        gap_str = "n/a" if gap_ms is None else f"{gap_ms:.1f}ms"
        logger.info(
            f"[CUE][device] cue={cue_label} motor={motor}"
            f"{' (requested ' + str(requested) + ', routed)' if motor != requested else ''} "
            f"intensity_255={intensity_clamped} pattern={pattern} duration_ms={duration_clamped} "
            f"cue_id={cue_id} gap_since_prev={gap_str}"
        )

        if self._json_log_fp:
            record = {
                "kind": "device",
                "recv_ts_ms": now_ms(),
                "cue_id": cue_id,
                "cue": cue_label,
                "motor": motor,
                "requested_motor": requested,
                "intensity_255": intensity_clamped,
                "pattern": pattern,
                "duration_ms": duration_clamped,
                "gap_since_prev_ms": gap_ms,
            }
            self._json_log_fp.write(json.dumps(record) + "\n")
            self._json_log_fp.flush()

        # v1.1: ack echoes cue_id (when the caller supplied one) so the app
        # can measure latency, per contracts/HAPTIC_PROTOCOL.md's v1.1 section.
        self.send_ack(ack_id=ack_id, addr=addr, ok=True, status="executed", motor=motor)

    def _handle_ping(self, msg: Dict[str, Any], addr: Tuple[str, int]) -> None:
        self.state.ping_count += 1
        logger.info(f"[PING] from {addr} id={msg.get('id')}")
        # The v1 schema has no dedicated pong type; "answers ping" per the
        # contract is done via an ack of the ping's own id (ack schema only
        # requires ack_id + ok, same as for a cue).
        self.send_ack(ack_id=msg.get("id"), addr=addr, ok=True)

    def _handle_stop(self, msg: Dict[str, Any], addr: Tuple[str, int]) -> None:
        logger.info(f"[STOP] zone={msg.get('zone', 'all')} from {addr}")
        # Fire-and-forget per the contract: no reply.

    def _handle_config(self, msg: Dict[str, Any], addr: Tuple[str, int]) -> None:
        self.state.enabled = msg.get("enabled", self.state.enabled)
        self.state.max_intensity = msg.get("max_intensity", self.state.max_intensity)
        self.state.min_gap_ms = msg.get("min_gap_ms", self.state.min_gap_ms)
        logger.info(
            f"[CONFIG] enabled={self.state.enabled} max_intensity={self.state.max_intensity} "
            f"min_gap_ms={self.state.min_gap_ms}"
        )
        # Fire-and-forget per the contract: no reply.

    def print_report(self) -> None:
        print("\n=== FAKE HAPTIC SLEEVE REPORT ===")
        print(f"Device: {self.cfg.device_id} fw={self.cfg.fw}")
        print(f"Cues received: {self.state.cue_count}  Pings: {self.state.ping_count}  Acks sent: {self.state.ack_count}")
        print(f"Invalid datagrams: {self.state.invalid_count}")
        print(f"Battery: {self.state.battery_pct:.2f}%")
        print(f"Dropped (disconnect simulated): {self.state.dropped}")


class _SleeveDatagramProtocol(asyncio.DatagramProtocol):
    def __init__(self, sleeve: FakeHapticSleeve):
        self.sleeve = sleeve

    def datagram_received(self, data: bytes, addr: Tuple[str, int]) -> None:
        self.sleeve.handle_datagram(data, addr)

    def error_received(self, exc: Exception) -> None:
        logger.error(f"[HAPTIC] Transport error: {exc}")


# ---- selftest: scripted sender showing an end-to-end cue -> ack round trip ----

async def run_selftest(cfg: SleeveConfig) -> None:
    """Start the sleeve, send it a device command (v1.1 primary wire format)
    and a legacy semantic `cue` envelope from a throwaway UDP client socket,
    wait for each `ack`, and print the measured send->ack latency."""
    sleeve = FakeHapticSleeve(cfg)
    await sleeve.start()
    try:
        await asyncio.sleep(0.2)  # let the listener settle

        loop = asyncio.get_event_loop()
        client_transport, client_proto = await loop.create_datagram_endpoint(
            lambda: _SelftestClientProtocol(),
            remote_addr=("127.0.0.1", cfg.port),
        )

        print("\n=== SELFTEST ===")

        # 1) v1.1 primary format: device-level command, ack echoes cue_id.
        cue_id = new_id()
        device_msg = {
            "motor": 0,
            "intensity": 153,  # round(255 * 0.6)
            "duration_ms": 300,
            "pattern": "pulse",
            "cue_id": cue_id,
            "cue": "trunk_lean",
        }
        sent_at = time.perf_counter()
        client_transport.sendto(json.dumps(device_msg).encode("utf-8"))
        try:
            ack = await asyncio.wait_for(client_proto.wait_for_ack(cue_id), timeout=2.0)
            latency_ms = (time.perf_counter() - sent_at) * 1000.0
            print(f"[device] Sent cue_id={cue_id} -> ack ok={ack.get('ok')} ack_id={ack.get('ack_id')}")
            print(f"[device] Measured send->ack latency: {latency_ms:.3f} ms")
        except asyncio.TimeoutError:
            print("[device] SELFTEST FAILED: no ack received within 2s")

        # 2) legacy/internal semantic cue envelope, still accepted for back-compat.
        legacy_id = new_id()
        cue_msg = {
            "v": 1,
            "type": "cue",
            "id": legacy_id,
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
        client_transport.sendto(json.dumps(cue_msg).encode("utf-8"))
        try:
            ack = await asyncio.wait_for(client_proto.wait_for_ack(legacy_id), timeout=2.0)
            latency_ms = (time.perf_counter() - sent_at) * 1000.0
            print(f"[legacy] Sent cue id={legacy_id} -> ack ok={ack.get('ok')} ack_id={ack.get('ack_id')}")
            print(f"[legacy] Measured send->ack latency: {latency_ms:.3f} ms")
        except asyncio.TimeoutError:
            print("[legacy] SELFTEST FAILED: no ack received within 2s")

        client_transport.close()
    finally:
        await sleeve.stop()
        sleeve.print_report()


class _SelftestClientProtocol(asyncio.DatagramProtocol):
    def __init__(self):
        self._waiters: Dict[str, asyncio.Future] = {}
        self._received: List[Dict[str, Any]] = []

    def datagram_received(self, data: bytes, addr: Tuple[str, int]) -> None:
        try:
            msg = json.loads(data.decode("utf-8"))
        except json.JSONDecodeError:
            return
        self._received.append(msg)
        if msg.get("type") == "ack":
            ack_id = msg.get("ack_id")
            fut = self._waiters.get(ack_id)
            if fut and not fut.done():
                fut.set_result(msg)

    async def wait_for_ack(self, ack_id: str) -> Dict[str, Any]:
        loop = asyncio.get_event_loop()
        fut: asyncio.Future = loop.create_future()
        self._waiters[ack_id] = fut
        return await fut


async def run_server(cfg: SleeveConfig) -> None:
    sleeve = FakeHapticSleeve(cfg)
    await sleeve.start()
    try:
        await asyncio.Event().wait()  # run until Ctrl+C
    except asyncio.CancelledError:
        pass
    finally:
        await sleeve.stop()
        sleeve.print_report()


def main() -> None:
    parser = argparse.ArgumentParser(description="OPUS fake haptic sleeve (software only)")
    parser.add_argument("--port", type=int, default=8790, help="UDP port to listen on (default 8790)")
    parser.add_argument("--discovery-port", type=int, default=8791, help="UDP port for hello broadcast (default 8791)")
    parser.add_argument("--imu", action="store_true", help="Emit fake imu messages")
    parser.add_argument("--imu-rate", type=float, default=50.0, help="imu rate in Hz, capped at 50 (default 50)")
    parser.add_argument("--drop-after", type=float, default=None, help="Seconds after which the sleeve stops responding (simulate disconnect)")
    parser.add_argument("--battery-start", type=float, default=100.0, help="Starting battery_pct (default 100)")
    parser.add_argument("--json-log", type=str, default=None, help="Path to write one JSON line per received cue")
    parser.add_argument("--device-id", type=str, default="sleeve-sim-01", help="device_id reported in status/hello")
    parser.add_argument("--motor-count", type=int, default=1, choices=[0, 1, 2, 3, 4],
                         help="Fitted channels, 0-4 (default 1, matching the real board's MOTOR_COUNT today)")
    parser.add_argument("--no-route-unfitted", dest="route_unfitted", action="store_false", default=True,
                         help="Reject cues to an unfitted channel with MOTOR_UNAVAILABLE instead of "
                              "falling back to motor 0 (default: fall back, matching ROUTE_MOTORn_TO_MOTOR0=1)")
    parser.add_argument("--selftest", action="store_true", help="Run a built-in cue->ack round trip and exit")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="[%(asctime)s] %(levelname)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    cfg = SleeveConfig(
        device_id=args.device_id,
        port=args.port,
        discovery_port=args.discovery_port,
        imu=args.imu,
        imu_rate_hz=args.imu_rate,
        drop_after=args.drop_after,
        battery_start=args.battery_start,
        json_log=Path(args.json_log) if args.json_log else None,
        motor_count=args.motor_count,
        route_unfitted_to_0=args.route_unfitted,
    )

    try:
        if args.selftest:
            asyncio.run(run_selftest(cfg))
        else:
            asyncio.run(run_server(cfg))
    except KeyboardInterrupt:
        logger.info("[HAPTIC] Shutting down")


if __name__ == "__main__":
    main()
