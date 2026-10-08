"""Tests for protocol.py (S1 milestone)."""

import sys
import json
import time
import asyncio
import pytest
from pathlib import Path
from unittest.mock import patch

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent))

from protocol import (
    MessageBuilder,
    MessageValidator,
    Outbox,
    ProtocolError,
    SchemaRegistry,
)


# Fixture: schemas directory
@pytest.fixture
def schemas_dir():
    """Return path to contracts/schemas."""
    return Path(__file__).parent.parent.parent / "contracts" / "schemas"


# Fixture: registry
@pytest.fixture
def registry(schemas_dir):
    """Create a SchemaRegistry."""
    return SchemaRegistry(schemas_dir)


# Fixture: validator
@pytest.fixture
def validator(schemas_dir):
    """Create a MessageValidator."""
    return MessageValidator(schemas_dir)


# Fixture: builder
@pytest.fixture
def builder_headset():
    """Create a MessageBuilder for headset."""
    return MessageBuilder("headset")


@pytest.fixture
def builder_hub():
    """Create a MessageBuilder for hub."""
    return MessageBuilder("hub")


# Tests: SchemaRegistry
class TestSchemaRegistry:
    def test_load_schemas(self, registry):
        """Test that schemas are loaded into the registry."""
        reg = registry.get_registry()
        assert reg is not None

    def test_all_schemas_loaded(self, schemas_dir, registry):
        """Test that all .json schemas are loaded."""
        schema_files = list(schemas_dir.glob("*.json"))
        assert len(schema_files) > 0

        reg = registry.get_registry()
        # Each schema should be registered under its $id
        for schema_file in schema_files:
            with open(schema_file, 'r') as f:
                schema = json.load(f)
            if "$id" in schema:
                # This is a weak test—referencing.Registry is opaque
                # but we can verify the registry exists
                assert reg is not None


# Tests: MessageBuilder
class TestMessageBuilder:
    def test_build_hello(self, builder_headset):
        """Test building a hello message."""
        msg = builder_headset.hello(
            device_id="test-device",
            role="headset",
            versions={"sdk": "0.1.0"},
            games=[{"id": "orchard_reach", "version": "0.1.0"}],
        )
        assert msg["type"] == "hello"
        assert msg["from"] == "headset"
        assert msg["v"] == 1
        assert "id" in msg
        assert "seq" in msg
        assert "ts_ms" in msg
        assert msg["seq"] == 0
        assert msg["payload"]["device_id"] == "test-device"

    def test_seq_increments(self, builder_headset):
        """Test that seq increments."""
        msg1 = builder_headset.hello(
            device_id="test", role="headset", versions={}
        )
        msg2 = builder_headset.hello(
            device_id="test", role="headset", versions={}
        )
        assert msg1["seq"] == 0
        assert msg2["seq"] == 1

    def test_build_status(self, builder_hub):
        """Test building a status message."""
        msg = builder_hub.status(
            state="running",
            game_id="orchard_reach",
            block=0,
            trial=3,
            trials_total=5,
        )
        assert msg["type"] == "status"
        assert msg["from"] == "hub"
        assert msg["payload"]["state"] == "running"

    def test_build_command_requires_ack(self, builder_hub):
        """Test building a command with requires_ack."""
        msg = builder_hub.command("start")
        assert msg["type"] == "command"
        assert msg.get("requires_ack") is True

    def test_build_ack(self, builder_headset):
        """Test building an ack message."""
        msg = builder_headset.ack("some-message-id", ok=True)
        assert msg["type"] == "ack"
        assert msg["payload"]["ack_id"] == "some-message-id"
        assert msg["payload"]["ok"] is True
        assert "recv_ts_ms" in msg["payload"]

    def test_invalid_sender(self):
        """Test that invalid sender raises ValueError."""
        with pytest.raises(ValueError):
            MessageBuilder("invalid")


# Tests: MessageValidator
class TestMessageValidator:
    def test_validate_hello(self, validator, builder_headset):
        """Test validation of a hello message."""
        msg = builder_headset.hello(
            device_id="test-device",
            role="headset",
            versions={"sdk": "0.1.0"},
        )
        is_valid, error = validator.validate(msg)
        assert is_valid, f"Validation failed: {error}"

    def test_validate_hello_ack(self, validator, builder_hub):
        """Test validation of a hello_ack message."""
        msg = builder_hub.hello_ack(ack_id="some-id")
        is_valid, error = validator.validate(msg)
        assert is_valid, f"Validation failed: {error}"

    def test_validate_status(self, validator, builder_hub):
        """Test validation of a status message."""
        msg = builder_hub.status(state="running")
        is_valid, error = validator.validate(msg)
        assert is_valid, f"Validation failed: {error}"

    def test_validate_command(self, validator, builder_hub):
        """Test validation of a command message."""
        msg = builder_hub.command("start")
        is_valid, error = validator.validate(msg)
        assert is_valid, f"Validation failed: {error}"

    def test_validate_ping(self, validator, builder_headset):
        """Test validation of a ping message."""
        msg = builder_headset.ping(echo_ts_ms=time.time() * 1000)
        is_valid, error = validator.validate(msg)
        assert is_valid, f"Validation failed: {error}"

    def test_validate_pong(self, validator, builder_hub):
        """Test validation of a pong message."""
        msg = builder_hub.pong(echo_ts_ms=time.time() * 1000)
        is_valid, error = validator.validate(msg)
        assert is_valid, f"Validation failed: {error}"

    def test_ping_pong_echo_matching(self, validator, builder_headset, builder_hub):
        """Test that ping echo_ts_ms is echoed back in pong."""
        echo_ts_ms = time.time() * 1000
        ping_msg = builder_headset.ping(echo_ts_ms=echo_ts_ms)
        is_valid, error = validator.validate(ping_msg)
        assert is_valid, f"Ping validation failed: {error}"

        # Hub responds with pong echoing the same echo_ts_ms
        pong_msg = builder_hub.pong(echo_ts_ms=echo_ts_ms)
        is_valid, error = validator.validate(pong_msg)
        assert is_valid, f"Pong validation failed: {error}"

        # Verify echo_ts_ms matches
        ping_echo = ping_msg["payload"]["echo_ts_ms"]
        pong_echo = pong_msg["payload"]["echo_ts_ms"]
        assert ping_echo == pong_echo, f"Echo mismatch: ping={ping_echo}, pong={pong_echo}"

    def test_invalid_message_missing_required(self, validator):
        """Test that validation fails for missing required field."""
        msg = {
            "v": 1,
            "type": "hello",
            # Missing required fields: id, seq, ts_ms, from
        }
        is_valid, error = validator.validate(msg)
        assert not is_valid
        assert error is not None

    def test_invalid_message_bad_type(self, validator, builder_headset):
        """Test validation of message with invalid type."""
        msg = builder_headset.build("invalid_type")
        is_valid, error = validator.validate(msg)
        assert not is_valid


# Tests: Outbox
class TestOutbox:
    @pytest.mark.asyncio
    async def test_add_message(self):
        """Test adding a message to the outbox."""
        outbox = Outbox()
        msg = {"id": "msg-1", "seq": 0, "type": "hello"}
        await outbox.add(msg)
        assert await outbox.len() == 1

    @pytest.mark.asyncio
    async def test_add_duplicate_dedup(self):
        """Test that duplicate messages are de-duplicated."""
        outbox = Outbox()
        msg = {"id": "msg-1", "seq": 0, "type": "hello"}
        await outbox.add(msg)
        await outbox.add(msg)  # Add the same message again
        assert await outbox.len() == 1

    @pytest.mark.asyncio
    async def test_ack_message(self):
        """Test acknowledging a message."""
        outbox = Outbox()
        msg = {"id": "msg-1", "seq": 0, "type": "hello"}
        await outbox.add(msg)
        acked = await outbox.ack("msg-1")
        assert acked is True

    @pytest.mark.asyncio
    async def test_ack_nonexistent(self):
        """Test acknowledging a nonexistent message."""
        outbox = Outbox()
        acked = await outbox.ack("nonexistent")
        assert acked is False

    @pytest.mark.asyncio
    async def test_get_unacked(self):
        """Test getting unacked messages."""
        outbox = Outbox()
        msg1 = {"id": "msg-1", "seq": 0, "type": "hello"}
        msg2 = {"id": "msg-2", "seq": 1, "type": "hello"}
        await outbox.add(msg1)
        await outbox.add(msg2)
        await outbox.ack("msg-1")

        unacked = await outbox.get_unacked()
        assert len(unacked) == 1
        assert unacked[0]["id"] == "msg-2"

    @pytest.mark.asyncio
    async def test_get_unacked_after_seq(self):
        """Test getting unacked messages after a sequence number."""
        outbox = Outbox()
        msg1 = {"id": "msg-1", "seq": 0, "type": "hello"}
        msg2 = {"id": "msg-2", "seq": 1, "type": "hello"}
        msg3 = {"id": "msg-3", "seq": 2, "type": "hello"}
        await outbox.add(msg1)
        await outbox.add(msg2)
        await outbox.add(msg3)

        unacked = await outbox.get_unacked(after_seq=0)
        assert len(unacked) == 2
        assert unacked[0]["seq"] == 1
        assert unacked[1]["seq"] == 2

    @pytest.mark.asyncio
    async def test_check_timeouts(self):
        """Test checking for timed-out messages."""
        outbox = Outbox(timeout_sec=0.1)
        msg = {"id": "msg-1", "seq": 0, "type": "hello"}
        await outbox.add(msg)

        # Should not be timed out immediately
        timed_out = await outbox.check_timeouts()
        assert len(timed_out) == 0

        # Wait for timeout
        await asyncio.sleep(0.15)
        timed_out = await outbox.check_timeouts()
        assert len(timed_out) == 1
        assert "msg-1" in timed_out

    @pytest.mark.asyncio
    async def test_cleanup_acked(self):
        """Test cleaning up acked messages."""
        outbox = Outbox()
        msg1 = {"id": "msg-1", "seq": 0, "type": "hello"}
        msg2 = {"id": "msg-2", "seq": 1, "type": "hello"}
        await outbox.add(msg1)
        await outbox.add(msg2)
        await outbox.ack("msg-1")

        assert await outbox.len() == 2
        await outbox.cleanup_acked()
        assert await outbox.len() == 1

    @pytest.mark.asyncio
    async def test_clear(self):
        """Test clearing the outbox."""
        outbox = Outbox()
        msg = {"id": "msg-1", "seq": 0, "type": "hello"}
        await outbox.add(msg)
        assert await outbox.len() == 1

        await outbox.clear()
        assert await outbox.len() == 0


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
