"""S3: fake_headset.py - Headset-side simulator with session replay, file upload, and network resilience."""

import sys
import json
import asyncio
import argparse
import socket
import logging
import time
import hashlib
import aiohttp
from pathlib import Path
from typing import Optional, Dict, Any, List, Tuple
from urllib.parse import urljoin

sys.path.insert(0, str(Path(__file__).parent))
from protocol import MessageBuilder, MessageValidator, Outbox

# sim/synthetic_patients is an editable-installed package (analytics/.venv), but fake_headset.py
# runs under sim/live/.venv -- add the source dir directly so `--generate` works without needing
# the analytics venv. See sim/synthetic_patients/generate.py: write_session() is the exact
# function analytics' own fixtures are built from, so a generated session is contract-valid by
# construction (same code path check_session.py / contracts/validate.py already trust).
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

logger = logging.getLogger(__name__)

# Stages this mock headset can script. "reach" uses synthetic_patients' orchard-reach profile
# as-is. "sorting" reuses the same reach kinematics (moving an object to a target) but relabels
# the target as one of several containers and injects a wrong_target outcome on some trials --
# a light-weight stand-in for a real sorting minigame, not a new physics model. Documented scope
# limit: this does NOT change sim/synthetic_patients (out of this task's remit; it's a shared
# generator other tracks rely on) -- the sorting flavor is applied only to the copy fake_headset
# streams live, after write_session() has already produced a contract-valid session on disk.
SORTING_CONTAINERS = ["container_left", "container_center", "container_right"]


def _apply_sorting_flavor(events: List[Dict[str, Any]], rng) -> List[Dict[str, Any]]:
    """Relabel target_shown/trial_end events for a sorting-stage mock (containers + wrong_target).

    Mutates and returns the same list. Every trial that already ends in "success" has a 1-in-4
    chance of being rewritten as a "wrong_target" outcome instead (placed in a container event,
    but the WRONG container per the sorting rule) -- this is what
    contracts/LIVE_PROTOCOL.md/event.schema.json's trial_end.outcome enum calls "wrong_target".
    """
    container_for_trial: Dict[int, str] = {}
    for ev in events:
        trial = ev.get("trial")
        if trial is None:
            continue
        if ev.get("type") == "target_shown":
            container = SORTING_CONTAINERS[rng.integers(0, len(SORTING_CONTAINERS))]
            container_for_trial[trial] = container
            ev["container"] = container
            ev["containers"] = SORTING_CONTAINERS
        elif ev.get("type") == "trial_end" and ev.get("outcome") == "success":
            if rng.random() < 0.25:
                ev["outcome"] = "wrong_target"
                ev["placed_container"] = SORTING_CONTAINERS[
                    (SORTING_CONTAINERS.index(container_for_trial.get(trial, SORTING_CONTAINERS[0])) + 1)
                    % len(SORTING_CONTAINERS)
                ]
            else:
                ev["placed_container"] = container_for_trial.get(trial)
    return events


def generate_scripted_session(out_dir: Path, stage: str = "reach", seed: int = 42,
                               n_trials: int = 6, patient_ref: str = "mock-patient-001") -> Path:
    """Build a fresh, contract-valid session directory for `--generate` (no pre-recorded
    fixture needed). Returns out_dir. Used by fake_headset.py's `--generate` mode and by
    tools/demo tests."""
    from synthetic_patients.generate import write_session
    import numpy as np

    import uuid
    out_dir = Path(out_dir)
    # session.json's schema requires session_id to be a uuid (contracts/schemas/session.schema.json)
    # -- deterministic per (stage, seed) so repeated --generate runs with the same seed are
    # reproducible, but still a valid uuid4-shaped string.
    session_id = str(uuid.uuid5(uuid.NAMESPACE_DNS, f"opus-mock-{stage}-{seed}"))
    params = {"n_trials": n_trials}  # NOTE: sim/synthetic_patients.session.generate_session does
    # not currently read n_trials (out of this task's scope: it's a shared generator other
    # tracks depend on) -- it always produces its profile's fixed trial count. --trials is
    # accepted for forward-compatibility and documented as a no-op today; see CHECKPOINT log.
    write_session(
        out_dir, profile_name="moderate", seed=seed, params=params,
        session_id=session_id, patient_ref=patient_ref,
    )
    if stage == "sorting":
        events_path = out_dir / "events.ndjson"
        events = [json.loads(line) for line in events_path.read_text(encoding="utf-8").splitlines() if line.strip()]
        rng = np.random.default_rng(seed + 7)
        events = _apply_sorting_flavor(events, rng)
        with open(events_path, "w", encoding="utf-8") as f:
            for ev in events:
                f.write(json.dumps(ev) + "\n")
    return out_dir


class HeadsetState:
    """Manage headset-side session state."""

    def __init__(self):
        self.device_id = "headset-000"
        self.role = "headset"
        self.session_id: Optional[str] = None
        self.last_acked_seq: int = -1
        self.sent_ts: Dict[str, float] = {}  # msg_id -> sent timestamp (headset clock)
        self.recv_ts: Dict[str, float] = {}  # msg_id -> recv timestamp (headset clock)
        self.rtt_samples: List[float] = []
        self.resends: int = 0  # count of trial_events re-sent from the outbox after reconnect
        self.pending_pings: Dict[float, float] = {}  # echo_ts_ms -> sent_ts_ms for RTT tracking


class UDPDiscovery:
    """Discover hub via UDP beacon."""

    @staticmethod
    async def discover(timeout_sec: float = 2.0, beacon_port: int = 8788) -> Optional[Tuple[str, int]]:
        """
        Discover hub via UDP beacon.

        Returns:
            (hub_host, hub_port) or None if not found

        Note: the actual socket wait is a blocking call, run in a thread via
        run_in_executor. A plain `async def` that calls socket.recvfrom()
        directly blocks the whole event loop for up to timeout_sec, which is
        harmless with a separate hub process but silently starves any other
        asyncio task sharing the loop -- e.g. the hub's own UDP beacon task
        when hub and headset run in the same process, as the e2e harness does.
        """
        logger.info(f"[DISCOVERY] Listening for UDP beacon on port {beacon_port} for {timeout_sec}s")

        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, UDPDiscovery._blocking_discover, timeout_sec, beacon_port)

    @staticmethod
    def _blocking_discover(timeout_sec: float, beacon_port: int) -> Optional[Tuple[str, int]]:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            sock.bind(("0.0.0.0", beacon_port))
        except OSError as e:
            logger.warning(f"[DISCOVERY] Could not bind port {beacon_port}: {e}")
            return None
        sock.settimeout(timeout_sec)

        try:
            while True:
                try:
                    data, addr = sock.recvfrom(512)
                    beacon = json.loads(data.decode("utf-8"))
                    if beacon.get("opus_hub") == 1:
                        hub_port = int(beacon.get("port", 8787))
                        logger.info(f"[DISCOVERY] Found hub at {addr[0]}:{hub_port}: {beacon}")
                        return addr[0], hub_port
                except socket.timeout:
                    break
                except json.JSONDecodeError:
                    continue
        finally:
            sock.close()

        logger.warning("[DISCOVERY] No hub found")
        return None


class FakeHeadset:
    """OPUS Headset simulator."""

    def __init__(
        self,
        session_dir: Path,
        host: str = "auto",
        port: int = 8787,
        speed: float = 1.0,
        drop_at: Optional[float] = None,
        drop_duration: float = 3.0,
        no_scenario: bool = False,
        beacon_port: int = 8788,
        stage: str = "reach",
        sorting_rule: str = "color",
        patient_ref: Optional[str] = None,
    ):
        self.session_dir = Path(session_dir)
        self.host = host
        self.port = port
        self.speed = speed  # Replay speed multiplier
        self.drop_at = drop_at  # Seconds (session-clock) to trigger a network drop
        self.drop_duration = drop_duration
        self.no_scenario = no_scenario
        self.beacon_port = beacon_port
        self.stage = stage
        self.sorting_rule = sorting_rule
        self.trials_total: Optional[int] = None  # set below once session_data is loaded
        self.state = HeadsetState()
        self.validator = MessageValidator(
            Path(__file__).resolve().parents[2] / "contracts" / "schemas"
        )
        self.builder = MessageBuilder("headset")
        self.ws: Optional[aiohttp.ClientWebSocketResponse] = None
        self.http_session: Optional[aiohttp.ClientSession] = None
        self.hub_url: Optional[str] = None
        self.resolved_host: Optional[str] = None
        self.resolved_port: int = port

        # Load session data
        self.session_data = self._load_session()
        self.events = self._load_events()
        self.kin_files = self._load_kin_files()
        self.trials_total = self.session_data.get("trials_total") or self.session_data.get(
            "params", {}
        ).get("n_trials")
        self.patient_ref = patient_ref or self.session_data.get("patient_ref") or "mock-patient-001"

        # Replay state
        self.paused = False
        self.stopped = False
        self.connected = False
        self.replay_start_time: Optional[float] = None
        self.paused_at: Optional[float] = None
        self.total_paused_ms: float = 0.0
        self.drop_triggered = False
        self.start_event = asyncio.Event()  # set when a "start" command is received

        # Outbox of trial_events sent since the last hello_ack. Per LIVE_PROTOCOL.md
        # ("each side keeps an outbox of ... all trial_events since the last
        # hello_ack"), everything in here gets resent verbatim (same message id)
        # after a reconnect so the hub can de-dup and achieve exactly-once delivery.
        self.trial_outbox = Outbox()

        # Per-trial on-device metrics approximation, sent as a metrics_tick right after each
        # trial_end -- LIVE_PROTOCOL.md: "status <=5Hz, trial_event as it happens, metrics_tick
        # per trial". This is deliberately a coarse, on-device-style estimate computed straight
        # from this trial's own event timestamps (reaction/movement time), NOT the authoritative
        # analytics -- that always comes from opus_analytics on the hub-stored session afterwards.
        # Reset at each trial_start so a miss/timeout trial with missing events just sends fewer
        # (or zero) metrics rather than stale numbers from a previous trial.
        self.metrics_state: Dict[str, float] = {}

        # Persists across trials (unlike metrics_state, which resets every trial_start) --
        # feeds the enriched status message's trial/trials_completed/haptic/good_dose fields.
        self.live_status: Dict[str, Any] = {
            "current_trial": None,
            "trials_completed": 0,
            "last_cue_id": None,
            "good_dose": True,
        }

    def _note_event_for_metrics(self, event: Dict[str, Any]) -> None:
        etype = event.get("type")
        trial = event.get("trial")
        if trial is not None:
            self.live_status["current_trial"] = trial
        if etype == "trial_start":
            self.metrics_state = {}
        elif etype == "target_shown":
            self.metrics_state["t_target_shown"] = event.get("t_ms", 0.0)
        elif etype == "movement_onset":
            self.metrics_state["t_movement_onset"] = event.get("t_ms", 0.0)
        elif etype == "contact":
            self.metrics_state["t_contact"] = event.get("t_ms", 0.0)
        elif etype == "haptic_cue":
            self.live_status["last_cue_id"] = event.get("cue_id")
        elif etype == "trial_end":
            self.live_status["trials_completed"] = (self.live_status.get("trials_completed") or 0) + 1
            outcome = event.get("outcome")
            if outcome in ("timeout", "dropped", "wrong_target"):
                self.live_status["good_dose"] = False
            elif outcome == "success":
                self.live_status["good_dose"] = True

    async def maybe_send_metrics_tick(self) -> None:
        """Called after a trial_end event: build a metrics_tick from whatever timestamps this
        trial actually produced (a miss/timeout trial with no movement_onset/contact simply
        yields fewer -- possibly zero -- metrics, which is sent as an empty tick rather than
        invented values)."""
        ts = self.metrics_state
        metrics: Dict[str, Any] = {}
        if "t_target_shown" in ts and "t_movement_onset" in ts:
            metrics["reaction_time_ms"] = {
                "value": ts["t_movement_onset"] - ts["t_target_shown"],
                "unit": "ms",
                "quality": "ok",
            }
        if "t_movement_onset" in ts and "t_contact" in ts:
            metrics["movement_time_ms"] = {
                "value": ts["t_contact"] - ts["t_movement_onset"],
                "unit": "ms",
                "quality": "ok",
            }
        msg = self.builder.metrics_tick(window_trials=1, metrics=metrics)
        msg["session_id"] = self.state.session_id
        await self.send_message(msg)

    def _load_session(self) -> Dict[str, Any]:
        session_file = self.session_dir / "session.json"
        # Keep the exact source bytes for upload (see replay_session): re-
        # serializing via json.dumps() would not be byte-identical to the file
        # on disk (float formatting, key order, whitespace), which breaks the
        # hub's sha256 idempotency check even though the *content* matches.
        self._session_raw_bytes = session_file.read_bytes()
        with open(session_file, "r") as f:
            return json.load(f)

    def _load_events(self) -> List[Dict[str, Any]]:
        events_file = self.session_dir / "events.ndjson"
        self._events_raw_bytes = events_file.read_bytes()
        events = []
        with open(events_file, "r") as f:
            for line in f:
                line = line.strip()
                if line:
                    events.append(json.loads(line))
        return events

    def _load_kin_files(self) -> Dict[int, Path]:
        kin_files = {}
        for kin_file in sorted(self.session_dir.glob("kin_*.json")):
            chunk_num = int(kin_file.stem.split("_")[1])
            kin_files[chunk_num] = kin_file
        return kin_files

    def _elapsed_ms(self) -> float:
        """Session-clock elapsed ms: wall-clock since replay start, sped up by
        `speed`, minus any time spent paused (the replay clock must not advance
        while paused)."""
        if self.replay_start_time is None:
            return 0.0
        now = time.time()
        paused_ms = self.total_paused_ms
        if self.paused and self.paused_at is not None:
            paused_ms += (now - self.paused_at) * 1000.0
        raw_ms = (now - self.replay_start_time) * 1000.0 - paused_ms
        return max(0.0, raw_ms) * self.speed

    async def discover_hub(self) -> Tuple[str, int]:
        """Discover hub address/port."""
        if self.host != "auto":
            return self.host, self.port

        discovered = await UDPDiscovery.discover(timeout_sec=2.0, beacon_port=self.beacon_port)
        if discovered:
            return discovered

        logger.warning("[DISCOVERY] Falling back to localhost")
        return "127.0.0.1", self.port

    async def connect(self, host: str, port: Optional[int] = None):
        """Connect to hub via WebSocket."""
        port = port if port is not None else self.port
        self.resolved_host = host
        self.resolved_port = port
        self.hub_url = f"http://{host}:{port}"
        ws_url = f"ws://{host}:{port}/opus/v1/live"

        logger.info(f"[WS] Connecting to {ws_url}")

        self.http_session = aiohttp.ClientSession()

        try:
            self.ws = await self.http_session.ws_connect(ws_url)
            self.connected = True
            logger.info("[WS] Connected")
        except Exception as e:
            logger.error(f"[WS] Connection failed: {e}")
            await self.http_session.close()
            self.http_session = None
            raise

    async def disconnect(self):
        """Disconnect from hub."""
        self.connected = False
        if self.ws:
            await self.ws.close()
            logger.info("[WS] Disconnected")
        if self.http_session:
            await self.http_session.close()

    async def send_hello(self, resume_from_seq: Optional[int] = None):
        """Send hello message."""
        hello_msg = self.builder.hello(
            device_id=self.state.device_id,
            role=self.state.role,
            versions={"sdk": "0.0.0-sim"},
            games=[{"id": "orchard_reach", "version": "0.1.0"}],
            resume_from_seq=resume_from_seq,
        )
        hello_msg["session_id"] = self.state.session_id
        await self.send_message(hello_msg, is_trial_event=False, force=True)

    async def send_message(self, msg: Dict[str, Any], is_trial_event: bool = False, force: bool = False) -> bool:
        """Send a message over WebSocket.

        Returns True if the message was actually written to the socket. When
        disconnected (a simulated drop), trial_events are kept in the outbox
        (added by the caller before this returns) and simply not written; the
        session keeps running locally as LIVE_PROTOCOL.md's "local-first" rule
        requires.
        """
        is_valid, error = self.validator.validate(msg)
        if not is_valid:
            logger.error(f"[MSG TX] Validation failed: {error}")
            return False

        msg_type = msg.get("type")
        msg_id = msg.get("id")

        if not self.connected or not self.ws:
            if not force:
                logger.debug(f"[MSG TX] Dropped (disconnected): type={msg_type} id={msg_id}")
                return False

        self.state.sent_ts[msg_id] = time.time() * 1000
        logger.info(f"[MSG TX] type={msg_type} id={msg_id} seq={msg.get('seq')}")

        try:
            await self.ws.send_str(json.dumps(msg))
            return True
        except Exception as e:
            logger.error(f"[MSG TX] Send failed: {e}")
            self.connected = False
            return False

    async def send_trial_event(self, event: Dict[str, Any]):
        """Build, outbox, and (if connected) send a trial_event."""
        msg = self.builder.trial_event(event)
        msg["session_id"] = self.state.session_id
        await self.trial_outbox.add(msg)
        await self.send_message(msg, is_trial_event=True)

    async def flush_trial_outbox(self):
        """Resend every trial_event in the outbox (same message ids) so the
        hub can de-dup and still achieve exactly-once delivery."""
        pending = await self.trial_outbox.get_unacked()
        logger.info(f"[OUTBOX] Resending {len(pending)} trial_event(s) after reconnect")
        for msg in pending:
            ok = await self.send_message(msg, is_trial_event=True, force=True)
            if ok:
                self.state.resends += 1

    async def receive_messages(self):
        """Receive and handle messages from hub."""
        if not self.ws:
            logger.warning("[MSG RX] No WebSocket connection")
            return

        try:
            async for msg_obj in self.ws:
                if msg_obj.type == aiohttp.WSMsgType.TEXT:
                    try:
                        msg = json.loads(msg_obj.data)
                        msg_id = msg.get("id")
                        msg_type = msg.get("type")

                        if msg_id:
                            self.state.recv_ts[msg_id] = time.time() * 1000

                        logger.info(f"[MSG RX] type={msg_type} id={msg_id}")

                        await self.handle_message(msg)
                    except json.JSONDecodeError as e:
                        logger.error(f"[MSG RX] JSON parse error: {e}")
                elif msg_obj.type == aiohttp.WSMsgType.ERROR:
                    logger.error(f"[MSG RX] Error: {msg_obj.data}")
                    break
                elif msg_obj.type == aiohttp.WSMsgType.CLOSED:
                    break
        except asyncio.CancelledError:
            pass

    async def handle_message(self, msg: Dict[str, Any]):
        """Handle incoming message."""
        msg_type = msg.get("type")
        payload = msg.get("payload", {})

        if msg_type == "hello_ack":
            logger.info("[PROTOCOL] Pairing acknowledged")
            self.state.session_id = msg.get("session_id", "session-000")

        elif msg_type == "command":
            command = payload.get("command")
            logger.info(f"[COMMAND] Received: {command}")

            if command == "start":
                self.paused = False
                self.stopped = False
                if self.replay_start_time is None:
                    self.replay_start_time = time.time()
                self.start_event.set()
                logger.info("[REPLAY] Starting")

            elif command == "pause":
                if not self.paused:
                    self.paused = True
                    self.paused_at = time.time()
                logger.info("[REPLAY] Paused (clock frozen)")

            elif command == "resume":
                if self.paused:
                    if self.paused_at is not None:
                        self.total_paused_ms += (time.time() - self.paused_at) * 1000.0
                    self.paused_at = None
                    self.paused = False
                logger.info("[REPLAY] Resumed")

            elif command == "stop":
                self.stopped = True
                logger.info("[REPLAY] Stopped")

            ack_msg = self.builder.ack(msg.get("id"), ok=True)
            ack_msg["session_id"] = self.state.session_id
            await self.send_message(ack_msg, force=True)

        elif msg_type == "assign_program":
            ack_msg = self.builder.ack(msg.get("id"), ok=True)
            ack_msg["session_id"] = self.state.session_id
            await self.send_message(ack_msg, force=True)

        elif msg_type == "ping":
            echo_ts_ms = payload.get("echo_ts_ms")
            pong_msg = self.builder.pong(echo_ts_ms)
            pong_msg["session_id"] = self.state.session_id
            await self.send_message(pong_msg, force=True)

        elif msg_type == "pong":
            echo_ts_ms = payload.get("echo_ts_ms")
            if echo_ts_ms in self.state.pending_pings:
                sent_ts = self.state.pending_pings.pop(echo_ts_ms)
                recv_ts_ms = time.time() * 1000
                rtt_ms = recv_ts_ms - sent_ts
                self.state.rtt_samples.append(rtt_ms)
                logger.info(f"[PONG] Received pong with echo_ts_ms={echo_ts_ms}, rtt={rtt_ms:.1f}ms")

    async def upload_file(self, filename: str, data: bytes) -> bool:
        """Upload a file via HTTP PUT."""
        if not self.hub_url or not self.http_session:
            logger.warning(f"[FILE] No HTTP session to upload {filename}")
            return False

        url = urljoin(
            self.hub_url,
            f"/opus/v1/sessions/{self.state.session_id}/files/{filename}",
        )

        try:
            async with self.http_session.put(url, data=data) as resp:
                status = resp.status
                logger.info(f"[FILE] {filename} uploaded: {status}")
                return status in (200, 201)
        except Exception as e:
            logger.error(f"[FILE] Upload failed: {e}")
            return False

    async def maybe_trigger_drop(self):
        """If drop_at is configured and we've reached that point on the session
        clock, actually close the WebSocket for drop_duration seconds, then
        reconnect: new hello with resume_from_seq, wait for hello_ack, then
        flush the trial_event outbox."""
        if self.drop_at is None or self.drop_triggered:
            return
        if self._elapsed_ms() < self.drop_at * 1000:
            return

        self.drop_triggered = True
        logger.info(f"[DROP] Simulating network drop at t={self._elapsed_ms():.0f}ms for {self.drop_duration}s")
        self.connected = False
        if self.ws:
            await self.ws.close()
        if self.http_session:
            await self.http_session.close()
            self.http_session = None
        self.ws = None

        await asyncio.sleep(self.drop_duration)
        await self.reconnect()

    async def reconnect(self):
        """Reconnect to the hub (for network drop simulation)."""
        logger.info("[RECONNECT] Attempting to reconnect")
        try:
            await self.connect(self.resolved_host or self.host, self.resolved_port)
            resume_from_seq = self.state.last_acked_seq
            await self.send_hello(resume_from_seq=resume_from_seq)
            logger.info(f"[RECONNECT] Reconnected, sent hello (resume_from_seq={resume_from_seq})")

            asyncio.create_task(self.receive_messages())
            # Give the hub a moment to hello_ack before flushing the outbox.
            await asyncio.sleep(0.3)
            await self.flush_trial_outbox()
        except Exception as e:
            logger.error(f"[RECONNECT] Failed: {e}")

    async def replay_session(self):
        """Replay the session from events and files."""
        logger.info("[REPLAY] Starting session replay")
        # The hub's hello_ack carries no session id, so without a 'start' command state.session_id stayed
        # None and every status/trial_event went out with session_id null. The app's Monitor keys the live
        # view on that id (hub_connection.dart activeSessionId), so a --generate replay never appeared live
        # on the phone even though the hub was receiving it (found on-device 2026-09-19). Use the replayed
        # session's own id, exactly as the real Unity runner does.
        if not self.state.session_id:
            import uuid as _uuid
            try:
                self.state.session_id = self._load_session().get("session_id") or str(_uuid.uuid4())
            except Exception:
                self.state.session_id = str(_uuid.uuid4())

        if self.no_scenario:
            logger.info("[REPLAY] --no-scenario: waiting for a real 'start' command from the hub")
            try:
                await asyncio.wait_for(self.start_event.wait(), timeout=30.0)
            except asyncio.TimeoutError:
                logger.error("[REPLAY] Timed out waiting for 'start' command from hub")
                raise RuntimeError("no 'start' command received from hub within 30s")
        else:
            if self.replay_start_time is None:
                self.replay_start_time = time.time()

        status_task = asyncio.create_task(self.send_status_loop())

        try:
            event_idx = 0
            while event_idx < len(self.events) and not self.stopped:
                event = self.events[event_idx]
                t_ms = event.get("t_ms", 0)

                await self.maybe_trigger_drop()

                while self.paused and not self.stopped:
                    await asyncio.sleep(0.05)
                if self.stopped:
                    break

                target_elapsed = t_ms
                while self._elapsed_ms() < target_elapsed and not self.stopped and not self.paused:
                    remaining_ms = (target_elapsed - self._elapsed_ms()) / max(self.speed, 1e-6)
                    await asyncio.sleep(min(0.05, max(0.0, remaining_ms / 1000.0)))
                    if self.paused:
                        break

                if self.stopped:
                    break

                await self.send_trial_event(event)
                self._note_event_for_metrics(event)
                if event.get("type") == "trial_end":
                    await self.maybe_send_metrics_tick()
                event_idx += 1

            logger.info(f"[REPLAY] Events completed ({event_idx}/{len(self.events)} sent)")

            await self.upload_file("session.json", self._session_raw_bytes)
            await self.upload_file("events.ndjson", self._events_raw_bytes)

            for chunk_num, kin_file in self.kin_files.items():
                with open(kin_file, "rb") as f:
                    kin_data = f.read()
                filename = f"kin_{chunk_num:03d}.json"
                await self.upload_file(filename, kin_data)

            logger.info("[REPLAY] Session replay complete")

        finally:
            status_task.cancel()
            try:
                await status_task
            except asyncio.CancelledError:
                pass

    async def send_status_loop(self):
        """Send status messages at 5 Hz.

        Fields mirror what the real Unity runner sends (CONTEXT.md): session_id, patient_ref,
        trial, trials_completed, trials_total, fps, tracking_rate_hz, hands, real_hands (always
        false -- this is a mock), haptic, stage, rule, good_dose.
        """
        try:
            while not self.stopped:
                elapsed_ms = self._elapsed_ms()
                state = "paused" if self.paused else "running"
                trial = self.live_status.get("current_trial")

                status_msg = self.builder.status(
                    state=state,
                    game_id="orchard_reach" if self.stage != "sorting" else "orchard_sorting",
                    elapsed_s=elapsed_ms / 1000.0,
                    trial=int(trial) if trial is not None else None,
                    trials_completed=int(self.live_status.get("trials_completed", 0)),
                    trials_total=self.trials_total,
                    hands={"left": "high", "right": "high"},
                    real_hands=False,
                    haptic={
                        "connected": True,
                        "last_cue_id": self.live_status.get("last_cue_id"),
                        "battery_pct": 87,
                    },
                    stage=self.stage,
                    rule=self.sorting_rule if self.stage == "sorting" else None,
                    good_dose=self.live_status.get("good_dose", True),
                    patient_ref=self.patient_ref,
                )
                status_msg["session_id"] = self.state.session_id

                try:
                    await self.send_message(status_msg)
                except Exception as e:
                    logger.warning(f"[STATUS] Send failed: {e}")

                await asyncio.sleep(0.2)  # 5 Hz
        except asyncio.CancelledError:
            pass

    async def measure_latency(self):
        """Measure ping RTT by sending pings and waiting for pongs."""
        try:
            while not self.stopped:
                echo_ts_ms = time.time() * 1000
                sent_ts_ms = time.time() * 1000
                self.state.pending_pings[echo_ts_ms] = sent_ts_ms
                ping_msg = self.builder.ping(echo_ts_ms)
                ping_msg["session_id"] = self.state.session_id
                await self.send_message(ping_msg)
                logger.info(f"[PING] Sent ping with echo_ts_ms={echo_ts_ms}")
                await asyncio.sleep(1.0)
        except asyncio.CancelledError:
            pass

    def summary(self) -> Dict[str, Any]:
        """Compute a JSON-serializable exit summary (also used by print_report)."""
        latencies = []
        for msg_id in self.state.sent_ts:
            if msg_id in self.state.recv_ts:
                latencies.append(self.state.recv_ts[msg_id] - self.state.sent_ts[msg_id])

        out = {
            "device_id": self.state.device_id,
            "session_id": self.state.session_id,
            "events_total": len(self.events),
            "trial_event_resends": self.state.resends,
            "stopped": self.stopped,
        }
        if latencies:
            latencies.sort()
            out["latency_ms"] = {
                "p50": latencies[len(latencies) // 2],
                "p95": latencies[int(len(latencies) * 0.95) if len(latencies) > 1 else 0],
                "max": max(latencies),
                "count": len(latencies),
            }
        if self.state.rtt_samples:
            rtt = sorted(self.state.rtt_samples)
            out["rtt_ms"] = {
                "p50": rtt[len(rtt) // 2],
                "p95": rtt[int(len(rtt) * 0.95) if len(rtt) > 1 else 0],
                "max": max(rtt),
            }
        return out

    def print_report(self):
        """Print a clean, human-readable exit summary."""
        s = self.summary()
        print("\n=== HEADSET REPORT ===")
        print(f"Device: {s['device_id']}  Session: {s['session_id']}")
        print(f"Events replayed: {s['events_total']}  Resent after reconnect: {s['trial_event_resends']}")
        print(f"Stopped cleanly: {s['stopped']}")
        if "latency_ms" in s:
            lat = s["latency_ms"]
            print(f"Round-trip latency (ms): p50={lat['p50']:.1f}, p95={lat['p95']:.1f}, max={lat['max']:.1f} (n={lat['count']})")
        if "rtt_ms" in s:
            rtt = s["rtt_ms"]
            print(f"Ping RTT (ms): p50={rtt['p50']:.1f}, p95={rtt['p95']:.1f}, max={rtt['max']:.1f}")


async def main():
    parser = argparse.ArgumentParser(description="OPUS Headset Simulator (mock headset)")
    parser.add_argument("--session", type=Path, help="Session directory to replay (skip --generate)")
    parser.add_argument(
        "--generate", action="store_true",
        help="Generate a fresh scripted session (via sim/synthetic_patients) instead of "
        "requiring a pre-recorded --session directory. This is the mode for testing the "
        "Flutter app: a realistic mock headset with no fixture files needed.",
    )
    parser.add_argument("--stage", choices=["reach", "sorting"], default="reach",
                         help="Scripted stage to play when --generate is used.")
    parser.add_argument("--trials", type=int, default=6, help="Trial count for --generate.")
    parser.add_argument("--seed", type=int, default=42, help="RNG seed for --generate.")
    parser.add_argument("--patient-ref", default="mock-patient-001", help="patient_ref for --generate.")
    parser.add_argument("--host", default="auto", help="Hub host (auto = UDP discovery, or an IP to skip it)")
    parser.add_argument("--port", type=int, default=8787, help="Hub WebSocket/HTTP port")
    parser.add_argument("--beacon-port", type=int, default=8788, help="UDP beacon port for --host auto")
    parser.add_argument("--speed", type=float, default=1.0, help="Replay speed multiplier")
    parser.add_argument("--drop-at", type=float, help="Simulate network drop after N session-seconds")
    parser.add_argument(
        "--no-scenario",
        action="store_true",
        help="Do not assume a scripted hub scenario: wait for a real 'start' command "
        "before replaying (use this against the real Flutter hub).",
    )
    parser.add_argument("--game", choices=["orchard_reach", "phantom_hand"], default="orchard_reach",
                        help="phantom_hand: replay a Phantom Hand session (default fixture phantom_hand_min) "
                        "through the live protocol while driving the sleeve twin / real nodes "
                        "(sim/live/phantom_replay.py).")
    parser.add_argument("--node-a", help="phantom_hand: Node A (haptic) ip:port; omit to use --discovery-port")
    parser.add_argument("--node-b", help="phantom_hand: Node B (bio) ip:port; omit to use --discovery-port")
    parser.add_argument("--discovery-port", type=int, default=0,
                        help="phantom_hand: node discovery UDP port (8791 + twin offset); 0 = no discovery")
    parser.add_argument("--telemetry-port", type=int, default=0,
                        help="phantom_hand: UDP port on this PC that takes the nodes' sensor stream, the way the real boards "
                        "send it (to the IP of the last sender on the fixed port 8790, not to the port a command came from); "
                        "0 = the stream comes back on the socket the commands leave from")
    parser.add_argument("--control", help="phantom_hand: twin control ip:port (flinch at threat_impact)")
    parser.add_argument("--compress-gap-ms", type=float, default=2500.0,
                        help="phantom_hand: cap idle gaps between events (0 = real time)")
    parser.add_argument("--off-a-at", type=float, help="phantom_hand: power Node A off at this fraction (0-1) of the run")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="[%(asctime)s] %(levelname)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    if args.game == "phantom_hand":
        from phantom_replay import run_phantom_cli
        sys.exit(await run_phantom_cli(args))

    if args.generate:
        import tempfile
        gen_dir = Path(tempfile.mkdtemp(prefix="opus_mock_headset_"))
        generate_scripted_session(
            gen_dir, stage=args.stage, seed=args.seed, n_trials=args.trials,
            patient_ref=args.patient_ref,
        )
        session_dir = gen_dir
        logger.info(f"[GENERATE] Scripted '{args.stage}' session written to {gen_dir}")
    elif args.session is not None:
        session_dir = args.session
    else:
        parser.error("one of --session or --generate is required")

    headset = FakeHeadset(
        session_dir=session_dir,
        host=args.host,
        port=args.port,
        speed=args.speed,
        drop_at=args.drop_at,
        no_scenario=args.no_scenario,
        beacon_port=args.beacon_port,
        stage=args.stage,
        patient_ref=args.patient_ref,
    )

    exit_code = 0
    try:
        host, port = await headset.discover_hub()
        await headset.connect(host, port)

        headset.state.session_id = "session-test"
        await headset.send_hello()

        recv_task = asyncio.create_task(headset.receive_messages())
        ping_task = asyncio.create_task(headset.measure_latency())

        await asyncio.sleep(0.5)

        await headset.replay_session()

        await asyncio.sleep(1.0)
        recv_task.cancel()
        ping_task.cancel()

    except Exception as e:
        logger.error(f"Error: {e}")
        exit_code = 1
    finally:
        await headset.disconnect()
        headset.print_report()

    sys.exit(exit_code)


if __name__ == "__main__":
    asyncio.run(main())
