"""S2: fake_hub.py - Hub-side simulator with UDP beacon, WebSocket, and HTTP file upload."""

import sys
import json
import asyncio
import argparse
import socket
import logging
import hashlib
from pathlib import Path
from datetime import datetime
from typing import Optional, Dict, Set, Any, List
from collections import defaultdict

import aiohttp
from aiohttp import web

sys.path.insert(0, str(Path(__file__).parent))
from protocol import MessageBuilder, MessageValidator, Outbox


logger = logging.getLogger(__name__)


def get_local_broadcast_addrs() -> List[str]:
    """Best-effort discovery of this host's subnet broadcast addresses (for UDP beacon)."""
    addrs = set()
    try:
        hostname = socket.gethostname()
        for info in socket.getaddrinfo(hostname, None, socket.AF_INET):
            ip = info[4][0]
            if ip.startswith("127."):
                continue
            parts = ip.split(".")
            if len(parts) == 4:
                addrs.add(".".join(parts[:3]) + ".255")
    except Exception as e:
        logger.debug(f"Could not enumerate local addresses: {e}")
    return sorted(addrs)


class HubState:
    """Manage hub-side session state."""

    def __init__(self):
        self.device_id = "hub-000"
        self.sessions: Dict[str, Dict[str, Any]] = {}  # session_id -> session data
        self.connections: Dict[str, web.WebSocketResponse] = {}  # session_id -> ws
        self.outboxes: Dict[str, Outbox] = {}  # session_id -> outbox
        self.message_counts: Dict[str, int] = defaultdict(int)  # type -> count
        self.sent_ts: Dict[str, float] = {}  # msg_id -> sent timestamp
        self.recv_ts: Dict[str, float] = {}  # msg_id -> recv timestamp
        self.invalid_count: int = 0
        self.invalid_messages: List[str] = []  # error strings, for reporting
        # Every trial_event received, keyed by message id -> payload. A dict
        # naturally de-dups by id, which is required for exactly-once counting
        # when the headset resends its outbox after a reconnect.
        self.received_trial_events: Dict[str, Dict[str, Any]] = {}
        self.trial_event_arrival_order: List[str] = []  # msg ids, first-seen order
        # command msg_id -> sent_ts_ms, popped once ack'd
        self.pending_command_acks: Dict[str, float] = {}
        self.command_ack_latencies_ms: List[float] = []
        self.command_ack_over_1s: List[str] = []  # msg ids that missed the 1s SLA
        # ping RTT tracking: echo_ts_ms -> sent_ts, used to measure roundtrip from pong
        self.pending_pings: Dict[float, float] = {}  # echo_ts_ms -> sent_ts_ms
        self.rtt_samples: List[float] = []  # RTT measurements in ms


class UDPBeacon:
    """Send UDP beacon (broadcast + subnet broadcast) advertising the hub."""

    def __init__(self, host_port: int = 8787, beacon_port: int = 8788, hub_id: str = "hub-000"):
        self.host_port = host_port
        self.beacon_port = beacon_port
        self.running = False
        self.beacon_data = {
            "opus_hub": 1,
            "hub_id": hub_id,
            "port": host_port,
            "name": "OPUS Hub (Simulated)",
        }

    async def start(self):
        """Start sending beacons every 1 second."""
        self.running = True
        loop = asyncio.get_event_loop()

        targets = ["255.255.255.255", "127.255.255.255"] + get_local_broadcast_addrs()

        def send_beacon():
            beacon_json = json.dumps(self.beacon_data)
            beacon_bytes = beacon_json.encode("utf-8")

            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            try:
                sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
            except OSError:
                pass

            for target in targets:
                try:
                    sock.sendto(beacon_bytes, (target, self.beacon_port))
                except Exception as e:
                    logger.debug(f"Beacon send to {target} failed: {e}")

            # Also send directly to loopback so same-machine discovery (used by
            # the e2e harness) works even when broadcast is filtered by the OS
            # firewall (observed on some Windows setups; see docs/MANUAL_TODO.md).
            try:
                sock.sendto(beacon_bytes, ("127.0.0.1", self.beacon_port))
            except Exception as e:
                logger.debug(f"Beacon send to loopback failed: {e}")

            sock.close()

        while self.running:
            await loop.run_in_executor(None, send_beacon)
            await asyncio.sleep(1.0)

    def stop(self):
        """Stop sending beacons."""
        self.running = False


class FakeHub:
    """OPUS Hub simulator."""

    def __init__(
        self,
        port: int = 8787,
        beacon: bool = True,
        scenario: Optional[str] = None,
        beacon_port: int = 8788,
        contracts_dir: Optional[Path] = None,
        out_dir: Optional[Path] = None,
        scenario_timing: Optional[Dict[str, float]] = None,
    ):
        self.port = port
        self.beacon_port = beacon_port
        self.enable_beacon = beacon
        self.scenario = scenario
        # Defaults tuned for a fast (speed>=10) replay of the ~13s sample
        # fixture. IMPORTANT: these are wall-clock seconds unrelated to the
        # session's own (possibly sped-up) clock. If the scripted "stop" fires
        # before the headset's replay -- driven by session-clock time / speed
        # -- actually finishes, the session gets clipped early. Callers running
        # a slower replay (e.g. speed=1) must pass a scenario_timing sized to
        # the session length, or the "basic" scenario will send `stop` too soon.
        self.scenario_timing = {
            "assign_wait": 1.0,
            "pre_pause": 5.0,
            "pause_hold": 2.0,
            "post_resume": 5.0,
        }
        if scenario_timing:
            self.scenario_timing.update(scenario_timing)
        self.state = HubState()
        self.contracts_dir = contracts_dir or (Path(__file__).resolve().parents[2] / "contracts")
        self.out_dir = out_dir or (Path(__file__).resolve().parents[1] / "out" / "hub_sessions")
        self.validator = MessageValidator(self.contracts_dir / "schemas")
        self.builder = MessageBuilder("hub")
        self.beacon: Optional[UDPBeacon] = None
        self.app = web.Application()
        self.setup_routes()

        # Scenario state
        self.scenario_headset_ws: Optional[web.WebSocketResponse] = None
        self.scenario_program_assigned = False
        self.scenario_session_started = False
        self.scenario_paused = False
        self.scenario_start_time: Optional[float] = None
        self.scenario_task: Optional[asyncio.Task] = None

    def setup_routes(self):
        """Setup HTTP routes."""
        self.app.router.add_get("/opus/v1/live", self.websocket_handler)
        self.app.router.add_put("/opus/v1/sessions/{session_id}/files/{filename}", self.file_upload_handler)
        self.app.router.add_get("/opus/v1/health", self.health_handler)

    async def websocket_handler(self, request: web.Request) -> web.WebSocketResponse:
        """Handle WebSocket connection."""
        ws = web.WebSocketResponse()
        await ws.prepare(request)

        logger.info(f"[WS] Client connected from {request.remote}")
        self.scenario_headset_ws = ws

        try:
            async for msg in ws:
                if msg.type == aiohttp.WSMsgType.TEXT:
                    await self.handle_message(msg.data, ws)
                elif msg.type == aiohttp.WSMsgType.ERROR:
                    logger.error(f"[WS] Error: {ws.exception()}")
                    break
                elif msg.type == aiohttp.WSMsgType.CLOSED:
                    break

        finally:
            logger.info("[WS] Client disconnected")
            self.scenario_headset_ws = None

        return ws

    async def handle_message(self, data: str, ws: web.WebSocketResponse):
        """Handle incoming message. Every inbound message is schema-validated;
        invalid ones are counted (never silently ignored) so the e2e harness
        can assert the count is zero."""
        try:
            msg = json.loads(data)
        except json.JSONDecodeError as e:
            logger.error(f"[MSG] Failed to parse JSON: {e}")
            self.state.invalid_count += 1
            self.state.invalid_messages.append(f"json_decode_error: {e}")
            return

        is_valid, error = self.validator.validate(msg)
        if not is_valid:
            logger.error(f"[MSG] Validation failed: {error}")
            self.state.invalid_count += 1
            self.state.invalid_messages.append(f"{msg.get('type', '?')}: {error}")
            return

        msg_type = msg.get("type")
        msg_id = msg.get("id")
        self.state.message_counts[msg_type] += 1
        recv_ts = datetime.now().timestamp() * 1000
        self.state.recv_ts[msg_id] = recv_ts

        logger.info(f"[MSG RX] type={msg_type} id={msg_id} seq={msg.get('seq')}")

        if msg_type == "hello":
            await self.handle_hello(msg, ws)
        elif msg_type == "status":
            pass
        elif msg_type == "trial_event":
            if msg_id not in self.state.received_trial_events:
                self.state.trial_event_arrival_order.append(msg_id)
            self.state.received_trial_events[msg_id] = msg.get("payload", {})
        elif msg_type == "ack":
            payload = msg.get("payload", {})
            ack_id = payload.get("ack_id")
            if ack_id in self.state.pending_command_acks:
                sent_ts = self.state.pending_command_acks.pop(ack_id)
                latency_ms = recv_ts - sent_ts
                self.state.command_ack_latencies_ms.append(latency_ms)
                if latency_ms > 1000.0:
                    self.state.command_ack_over_1s.append(ack_id)
                logger.info(f"[ACK] {ack_id} latency={latency_ms:.1f}ms")
        elif msg_type == "ping":
            await self.handle_ping(msg, ws)
        elif msg_type == "pong":
            payload = msg.get("payload", {})
            echo_ts_ms = payload.get("echo_ts_ms")
            if echo_ts_ms in self.state.pending_pings:
                sent_ts = self.state.pending_pings.pop(echo_ts_ms)
                rtt_ms = recv_ts - sent_ts
                self.state.rtt_samples.append(rtt_ms)
                logger.info(f"[PONG] echo_ts_ms={echo_ts_ms} rtt={rtt_ms:.1f}ms")
        else:
            logger.warning(f"[MSG] Unhandled message type: {msg_type}")

    async def handle_hello(self, msg: Dict[str, Any], ws: web.WebSocketResponse):
        """Handle hello message from headset."""
        payload = msg.get("payload", {})
        device_id = payload.get("device_id")
        resume_from_seq = payload.get("resume_from_seq")

        logger.info(f"[HELLO] device={device_id} resume_from_seq={resume_from_seq}")

        session_id = msg.get("session_id") or "session-000"
        msg_id = msg.get("id")

        ack_msg = self.builder.hello_ack(ack_id=msg_id, pair_token="token-123")
        ack_msg["session_id"] = session_id
        await self.send_message(ack_msg, ws)

        self.state.connections[session_id] = ws
        if session_id not in self.state.outboxes:
            self.state.outboxes[session_id] = Outbox()

        if self.scenario == "basic" and not self.scenario_program_assigned:
            self.scenario_program_assigned = True
            self.scenario_task = asyncio.create_task(self.run_scenario_basic(ws, session_id))

    async def handle_ping(self, msg: Dict[str, Any], ws: web.WebSocketResponse):
        """Handle ping message from headset, reply with pong echoing echo_ts_ms."""
        payload = msg.get("payload", {})
        echo_ts_ms = payload.get("echo_ts_ms")
        session_id = msg.get("session_id") or "session-000"

        pong_msg = self.builder.pong(echo_ts_ms)
        pong_msg["session_id"] = session_id
        await self.send_message(pong_msg, ws)
        logger.info(f"[PING] Received ping, sent pong with echo_ts_ms={echo_ts_ms}")

    async def send_ping_loop(self, ws: web.WebSocketResponse, session_id: str):
        """Send ping messages every 1 second."""
        try:
            while True:
                await asyncio.sleep(1.0)
                echo_ts_ms = datetime.now().timestamp() * 1000
                ping_msg = self.builder.ping(echo_ts_ms)
                ping_msg["session_id"] = session_id
                sent_ts = datetime.now().timestamp() * 1000
                self.state.pending_pings[echo_ts_ms] = sent_ts
                await self.send_message(ping_msg, ws)
                logger.info(f"[PING] Sent ping with echo_ts_ms={echo_ts_ms}")
        except asyncio.CancelledError:
            pass

    async def run_scenario_basic(self, ws: web.WebSocketResponse, session_id: str):
        """Run the basic scenario: assign_program -> start -> pause after 5s -> resume -> stop."""
        logger.info("[SCENARIO] Starting basic scenario")

        program_file = self.contracts_dir / "fixtures" / "valid" / "program.orchard.json"
        with open(program_file, "r") as f:
            program = json.load(f)

        # Start ping loop
        ping_task = asyncio.create_task(self.send_ping_loop(ws, session_id))

        try:
            assign_msg = self.builder.build(
                "assign_program",
                payload={"program": program, "patient_ref": "pat-001", "manifests": []},
                session_id=session_id,
                requires_ack=True,
            )
            await self.send_message(assign_msg, ws, track_ack=True)
            logger.info("[SCENARIO] Program assigned, waiting for ack")

            t = self.scenario_timing
            await asyncio.sleep(t["assign_wait"])

            start_msg = self.builder.command("start")
            start_msg["session_id"] = session_id
            await self.send_message(start_msg, ws, track_ack=True)
            self.scenario_session_started = True
            logger.info("[SCENARIO] Start command sent")

            await asyncio.sleep(t["pre_pause"])
            pause_msg = self.builder.command("pause")
            pause_msg["session_id"] = session_id
            await self.send_message(pause_msg, ws, track_ack=True)
            self.scenario_paused = True
            logger.info(f"[SCENARIO] Pause command sent after {t['pre_pause']}s")

            await asyncio.sleep(t["pause_hold"])
            resume_msg = self.builder.command("resume")
            resume_msg["session_id"] = session_id
            await self.send_message(resume_msg, ws, track_ack=True)
            logger.info("[SCENARIO] Resume command sent")

            await asyncio.sleep(t["post_resume"])
            stop_msg = self.builder.command("stop")
            stop_msg["session_id"] = session_id
            await self.send_message(stop_msg, ws, track_ack=True)
            logger.info("[SCENARIO] Stop command sent")
        finally:
            ping_task.cancel()
            try:
                await ping_task
            except asyncio.CancelledError:
                pass

    async def send_message(self, msg: Dict[str, Any], ws: web.WebSocketResponse, track_ack: bool = False):
        """Send a message over WebSocket."""
        msg_type = msg.get("type")
        msg_id = msg.get("id")

        is_valid, error = self.validator.validate(msg)
        if not is_valid:
            logger.error(f"[MSG TX] Validation failed: {error}")
            return

        self.state.message_counts[msg_type] += 1
        sent_ts = datetime.now().timestamp() * 1000
        self.state.sent_ts[msg_id] = sent_ts
        if track_ack and msg.get("requires_ack"):
            self.state.pending_command_acks[msg_id] = sent_ts

        logger.info(f"[MSG TX] type={msg_type} id={msg_id} seq={msg.get('seq')}")

        try:
            await ws.send_str(json.dumps(msg))
        except Exception as e:
            logger.error(f"[MSG TX] Failed to send: {e}")

    async def file_upload_handler(self, request: web.Request) -> web.Response:
        """Handle HTTP PUT file upload. Idempotent by (session_id, name) + sha256."""
        session_id = request.match_info.get("session_id", "unknown")
        filename = request.match_info.get("filename", "unknown")

        try:
            data = await request.read()
        except Exception as e:
            logger.error(f"[FILE] Read error: {e}")
            return web.Response(status=400, text="Read error")

        sha256_hash = hashlib.sha256(data).hexdigest()

        out_dir = self.out_dir / session_id
        out_dir.mkdir(parents=True, exist_ok=True)

        file_path = out_dir / filename
        if file_path.exists():
            with open(file_path, "rb") as f:
                existing_sha256 = hashlib.sha256(f.read()).hexdigest()

            if existing_sha256 == sha256_hash:
                logger.info(f"[FILE] {filename} already exists (sha256 match) -> 200")
                return web.Response(status=200, text="OK")
            else:
                logger.warning(f"[FILE] {filename} sha256 mismatch -> 409")
                return web.Response(status=409, text="SHA256 mismatch")

        try:
            with open(file_path, "wb") as f:
                f.write(data)
            logger.info(f"[FILE] {filename} uploaded ({len(data)} bytes) -> 201")
            return web.Response(status=201, text="Created")
        except Exception as e:
            logger.error(f"[FILE] Write error: {e}")
            return web.Response(status=500, text="Write error")

    async def health_handler(self, request: web.Request) -> web.Response:
        """Handle GET /opus/v1/health."""
        return web.json_response({"status": "healthy"})

    async def start(self):
        """Start the hub."""
        if self.enable_beacon:
            self.beacon = UDPBeacon(self.port, self.beacon_port)
            asyncio.create_task(self.beacon.start())
            logger.info(f"[HUB] UDP beacon started on port {self.beacon_port}")

        runner = web.AppRunner(self.app)
        await runner.setup()
        site = web.TCPSite(runner, "0.0.0.0", self.port)
        await site.start()
        self._runner = runner

        logger.info(f"[HUB] Server started on http://0.0.0.0:{self.port}")
        logger.info(f"[HUB] WebSocket: ws://localhost:{self.port}/opus/v1/live")
        logger.info(f"[HUB] HTTP PUT: http://localhost:{self.port}/opus/v1/sessions/<session_id>/files/<filename>")
        return runner

    async def stop(self):
        if self.beacon:
            self.beacon.stop()
        if self.scenario_task and not self.scenario_task.done():
            self.scenario_task.cancel()
            try:
                await self.scenario_task
            except asyncio.CancelledError:
                pass
        if getattr(self, "_runner", None):
            await self._runner.cleanup()

    def print_report(self):
        """Print message counts and statistics."""
        print("\n=== HUB REPORT ===")
        print(f"Message counts: {dict(self.state.message_counts)}")
        print(f"Invalid messages: {self.state.invalid_count}")
        print(f"Unique trial_events received: {len(self.state.received_trial_events)}")

        if self.state.command_ack_latencies_ms:
            lat = sorted(self.state.command_ack_latencies_ms)
            p50 = lat[len(lat) // 2]
            p95 = lat[int(len(lat) * 0.95) if len(lat) > 1 else 0]
            print(
                f"Command->Ack latency (ms): p50={p50:.1f}, p95={p95:.1f}, "
                f"max={max(lat):.1f}, over_1s={len(self.state.command_ack_over_1s)}"
            )

        if self.state.rtt_samples:
            rtt = sorted(self.state.rtt_samples)
            p50 = rtt[len(rtt) // 2]
            p95 = rtt[int(len(rtt) * 0.95) if len(rtt) > 1 else 0]
            print(
                f"Ping RTT (ms): p50={p50:.1f}, p95={p95:.1f}, "
                f"max={max(rtt):.1f}, count={len(rtt)}"
            )


async def main():
    parser = argparse.ArgumentParser(description="OPUS Hub Simulator")
    parser.add_argument("--port", type=int, default=8787, help="WebSocket/HTTP port")
    parser.add_argument("--beacon-port", type=int, default=8788, help="UDP beacon port")
    parser.add_argument("--beacon", action="store_true", help="Enable UDP beacon")
    parser.add_argument("--scenario", choices=["basic"], help="Run a scripted scenario")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="[%(asctime)s] %(levelname)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    hub = FakeHub(port=args.port, beacon=args.beacon, scenario=args.scenario, beacon_port=args.beacon_port)

    try:
        await hub.start()
        await asyncio.sleep(3600)
    except KeyboardInterrupt:
        logger.info("[HUB] Shutting down")
    finally:
        await hub.stop()
        hub.print_report()


if __name__ == "__main__":
    asyncio.run(main())
