#!/usr/bin/env python3
# Usage (repo root): python tools/demo/node_probe.py <node-ip> <port>   e.g. 127.0.0.1 18790 (twin) or 192.168.x.y 8790 (real Node A/B)
# Send one stroke + subscribe to a node, print 1.5 s of replies, validate each against the contracts.
# Sends from local UDP 8790 when it is free (else an ephemeral port, with a note): the real boards ack to the source port but
# send sensor_data / sensor_chunk to the sender's IP on the fixed port 8790, so one socket then gets both.
import json, socket, sys, time
sys.path.insert(0, "contracts")
from validate import load_schema, build_schema_registry, validate_against_schema
from live_plot import open_node_socket
from pathlib import Path
host, port = sys.argv[1], int(sys.argv[2])
reg = build_schema_registry(Path("contracts/schemas")); schema = load_schema("haptic-message")
s = open_node_socket(); s.settimeout(0.2)
s.sendto(b'{"type":"subscribe"}', (host, port))
s.sendto(b'{"motor":0,"intensity":150,"duration_ms":200,"pattern":"pulse","cue":"stroke","cue_id":"stroke_001"}', (host, port))
end, kinds = time.time() + 1.5, {}
while time.time() < end:
    try: data, _ = s.recvfrom(4096)
    except socket.timeout: continue
    msg = json.loads(data); t = msg.get("type", "?")
    ok, why = validate_against_schema(msg, schema, t, reg)
    kinds[t] = kinds.get(t, 0) + 1
    if kinds[t] == 1 or not ok: print(why if not ok else f"[PASS] {t}: {json.dumps(msg)[:160]}")
print("received:", kinds)
