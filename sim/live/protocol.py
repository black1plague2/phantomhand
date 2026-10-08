"""OPUS Live Protocol v1: message builder, validator, outbox, ack tracking."""

import json
import uuid
import time
import asyncio
from pathlib import Path
from typing import Any, Dict, Optional, List, Tuple
from dataclasses import dataclass, field, asdict
from datetime import datetime, timedelta

import jsonschema
from jsonschema import Draft202012Validator, ValidationError
import referencing
import referencing.jsonschema


class ProtocolError(Exception):
    """Protocol-level error."""
    pass


class SchemaRegistry:
    """Load OPUS schemas into a referencing.Registry with $id and filename resolution."""

    def __init__(self, schemas_dir: Path):
        """
        Initialize registry from schemas_dir.
        Each schema is registered under both its $id and filename (without .json).
        """
        self.schemas_dir = Path(schemas_dir)
        self._registry = None
        self._load_schemas()

    def _load_schemas(self) -> None:
        """Load all schemas and build referencing.Registry."""
        resources = {}

        # Load all .json schemas from schemas_dir
        for schema_file in sorted(self.schemas_dir.glob("*.json")):
            schema_name = schema_file.stem  # e.g., "live-message"
            with open(schema_file, 'r') as f:
                schema = json.load(f)

            # Register under filename: "live-message" → URI form
            filename_uri = f"file:///{schema_name}.schema.json"
            resource = referencing.Resource.from_contents(schema)
            resources[filename_uri] = resource

            # Also register under $id if present
            if "$id" in schema:
                schema_id = schema["$id"]
                resources[schema_id] = resource

        self._registry = referencing.Registry().with_resources(resources.items())

    def get_registry(self) -> referencing.Registry:
        """Return the fully-populated registry."""
        return self._registry


class MessageBuilder:
    """Build OPUS Live Protocol messages with uuid, seq, ts_ms, from."""

    def __init__(self, sender: str, seq_start: int = 0):
        """
        Initialize builder.

        Args:
            sender: "headset" or "hub"
            seq_start: starting sequence number (default 0)
        """
        if sender not in ("headset", "hub"):
            raise ValueError(f"Invalid sender: {sender}")
        self.sender = sender
        self.seq = seq_start

    def build(
        self,
        msg_type: str,
        payload: Optional[Dict[str, Any]] = None,
        session_id: Optional[str] = None,
        requires_ack: bool = False,
    ) -> Dict[str, Any]:
        """
        Build a message.

        Args:
            msg_type: message type (hello, status, trial_event, etc.)
            payload: message payload (required for most types)
            session_id: session ID (optional, per spec)
            requires_ack: whether this message requires acknowledgment

        Returns:
            Complete message dict
        """
        msg = {
            "v": 1,
            "type": msg_type,
            "id": str(uuid.uuid4()),
            "seq": self.seq,
            "ts_ms": time.time() * 1000,
            "from": self.sender,
        }

        if session_id is not None:
            msg["session_id"] = session_id

        if payload is not None:
            msg["payload"] = payload

        if requires_ack:
            msg["requires_ack"] = True

        self.seq += 1
        return msg

    def hello(
        self,
        device_id: str,
        role: str,
        versions: Dict[str, Any],
        games: Optional[List[Dict[str, Any]]] = None,
        resume_from_seq: Optional[int] = None,
        active_session_id: Optional[str] = None,
        model: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Build a hello message."""
        payload = {
            "device_id": device_id,
            "role": role,
            "versions": versions,
        }
        if games is not None:
            payload["games"] = games
        if resume_from_seq is not None:
            payload["resume_from_seq"] = resume_from_seq
        if active_session_id is not None:
            payload["active_session_id"] = active_session_id
        if model is not None:
            payload["model"] = model

        return self.build("hello", payload=payload)

    def hello_ack(
        self,
        ack_id: str,
        pair_token: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Build a hello_ack message."""
        payload = {
            "ack_id": ack_id,
            "ok": True,
        }
        if pair_token is not None:
            payload["pair_token"] = pair_token

        return self.build("hello_ack", payload=payload)

    def status(
        self,
        state: str,
        game_id: Optional[str] = None,
        block: Optional[int] = None,
        trial: Optional[int] = None,
        trials_total: Optional[int] = None,
        fps: float = 90.0,
        tracking_rate_hz: float = 72.0,
        hands: Optional[Dict[str, str]] = None,
        battery_pct: Optional[float] = None,
        elapsed_s: float = 0.0,
        session_id: Optional[str] = None,
        patient_ref: Optional[str] = None,
        trials_completed: Optional[int] = None,
        real_hands: Optional[bool] = None,
        haptic: Optional[Dict[str, Any]] = None,
        stage: Optional[str] = None,
        rule: Optional[str] = None,
        good_dose: Optional[bool] = None,
    ) -> Dict[str, Any]:
        """Build a status message.

        `session_id` (top-level, kept in payload too for convenience), `patient_ref`,
        `trials_completed`, `real_hands`, `haptic`, `stage`, `rule` and `good_dose` are not
        named in live-message.schema.json's `status` sub-schema, but that sub-schema has no
        `additionalProperties: false`, so extra fields validate fine. They mirror what the
        real Unity runner sends (CONTEXT.md / contracts/LIVE_PROTOCOL.md) so fake_headset.py's
        mock status is realistic for app-side testing, not merely schema-valid.
        """
        payload = {
            "state": state,
            "fps": fps,
            "tracking_rate_hz": tracking_rate_hz,
            "elapsed_s": elapsed_s,
        }
        if game_id is not None:
            payload["game_id"] = game_id
        if block is not None:
            payload["block"] = block
        if trial is not None:
            payload["trial"] = trial
        if trials_total is not None:
            payload["trials_total"] = trials_total
        if hands is not None:
            payload["hands"] = hands
        if battery_pct is not None:
            payload["battery_pct"] = battery_pct
        if session_id is not None:
            payload["session_id"] = session_id
        if patient_ref is not None:
            payload["patient_ref"] = patient_ref
        if trials_completed is not None:
            payload["trials_completed"] = trials_completed
        if real_hands is not None:
            payload["real_hands"] = real_hands
        if haptic is not None:
            payload["haptic"] = haptic
        if stage is not None:
            payload["stage"] = stage
        if rule is not None:
            payload["rule"] = rule
        if good_dose is not None:
            payload["good_dose"] = good_dose

        return self.build("status", payload=payload)

    def trial_event(self, event: Dict[str, Any]) -> Dict[str, Any]:
        """Build a trial_event message."""
        return self.build("trial_event", payload=event)

    def command(
        self,
        command: str,
        params: Optional[Dict[str, Any]] = None,
        text: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Build a command message (requires_ack=True)."""
        payload = {"command": command}
        if params is not None:
            payload["params"] = params
        if text is not None:
            payload["text"] = text

        return self.build("command", payload=payload, requires_ack=True)

    def ack(self, ack_id: str, ok: bool, error: Optional[str] = None) -> Dict[str, Any]:
        """Build an ack message."""
        payload = {
            "ack_id": ack_id,
            "ok": ok,
            "recv_ts_ms": time.time() * 1000,
        }
        if error is not None:
            payload["error"] = error

        return self.build("ack", payload=payload)

    def ping(self, echo_ts_ms: float) -> Dict[str, Any]:
        """Build a ping message."""
        return self.build("ping", payload={"echo_ts_ms": echo_ts_ms})

    def pong(self, echo_ts_ms: float) -> Dict[str, Any]:
        """Build a pong message."""
        return self.build("pong", payload={"echo_ts_ms": echo_ts_ms})

    def metrics_tick(self, window_trials: int, metrics: Dict[str, Any]) -> Dict[str, Any]:
        """Build a metrics_tick message: on-device approximations, not the authoritative
        analytics (contracts/LIVE_PROTOCOL.md: 'metrics_tick per trial'; live-message.schema.json's
        metrics_tick payload requires window_trials + metrics). `metrics` is metric id ->
        {value, unit, quality}."""
        return self.build("metrics_tick", payload={"window_trials": window_trials, "metrics": metrics})


class MessageValidator:
    """Validate OPUS Live Protocol messages against the live-message schema."""

    def __init__(self, schemas_dir: Path):
        """Initialize validator with schema registry."""
        self.registry = SchemaRegistry(schemas_dir)
        self._schema = None
        self._validator = None
        self._load_schema()

    def _load_schema(self) -> None:
        """Load the live-message schema."""
        schema_file = self.registry.schemas_dir / "live-message.schema.json"
        with open(schema_file, 'r') as f:
            self._schema = json.load(f)

        # Create validator with the registry
        self._validator = Draft202012Validator(
            self._schema,
            registry=self.registry.get_registry(),
        )

    def validate(self, message: Dict[str, Any]) -> Tuple[bool, Optional[str]]:
        """
        Validate a message.

        Returns:
            (is_valid, error_message)
        """
        try:
            self._validator.validate(message)
            return True, None
        except ValidationError as e:
            return False, str(e)


@dataclass
class OutboxEntry:
    """An entry in the outbox waiting for acknowledgment."""

    message_id: str
    message: Dict[str, Any]
    sent_ts: float
    timeout_sec: float
    acked: bool = False
    ack_ts: Optional[float] = None


class Outbox:
    """
    Outbox for unacked requires_ack messages and trial_events.

    Rules:
    - Messages with requires_ack=True are kept until ack arrives (within timeout_sec)
    - Trial events (status, trial_event, metrics_tick) are kept for resume-from-seq
    - De-duplication by message id
    - On resume: resend any message with seq > resume_from_seq that hasn't been acked
    """

    def __init__(self, timeout_sec: float = 5.0):
        """
        Initialize outbox.

        Args:
            timeout_sec: time to wait for ack before timing out
        """
        self.timeout_sec = timeout_sec
        self.entries: Dict[str, OutboxEntry] = {}  # message_id -> OutboxEntry
        self._lock = asyncio.Lock()

    async def add(self, message: Dict[str, Any]) -> None:
        """Add a message to the outbox."""
        msg_id = message.get("id")
        if not msg_id:
            raise ProtocolError("Message missing 'id'")

        async with self._lock:
            if msg_id not in self.entries:
                self.entries[msg_id] = OutboxEntry(
                    message_id=msg_id,
                    message=message,
                    sent_ts=time.time(),
                    timeout_sec=self.timeout_sec,
                )

    async def ack(self, ack_id: str) -> bool:
        """
        Mark a message as acked.

        Returns:
            True if the message was in the outbox
        """
        async with self._lock:
            if ack_id in self.entries:
                self.entries[ack_id].acked = True
                self.entries[ack_id].ack_ts = time.time()
                return True
            return False

    async def get_unacked(self, after_seq: Optional[int] = None) -> List[Dict[str, Any]]:
        """
        Get unacked messages (for resume or timeout check).

        Args:
            after_seq: only include messages with seq > after_seq (for resume)

        Returns:
            List of message dicts
        """
        async with self._lock:
            result = []
            for entry in self.entries.values():
                if entry.acked:
                    continue

                # If after_seq is set, filter by seq
                if after_seq is not None:
                    msg_seq = entry.message.get("seq", -1)
                    if msg_seq <= after_seq:
                        continue

                result.append(entry.message)

            return sorted(result, key=lambda m: m.get("seq", 0))

    async def check_timeouts(self) -> List[str]:
        """
        Check for timed-out messages.

        Returns:
            List of timed-out message ids
        """
        async with self._lock:
            timed_out = []
            now = time.time()
            for msg_id, entry in self.entries.items():
                if not entry.acked:
                    age_sec = now - entry.sent_ts
                    if age_sec > entry.timeout_sec:
                        timed_out.append(msg_id)
            return timed_out

    async def cleanup_acked(self) -> None:
        """Remove acked entries from the outbox (optional: to save memory)."""
        async with self._lock:
            self.entries = {
                msg_id: entry
                for msg_id, entry in self.entries.items()
                if not entry.acked
            }

    async def clear(self) -> None:
        """Clear the entire outbox."""
        async with self._lock:
            self.entries.clear()

    async def len(self) -> int:
        """Get the number of entries in the outbox."""
        async with self._lock:
            return len(self.entries)
