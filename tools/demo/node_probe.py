#!/usr/bin/env python3
# Usage (repo root): python tools/demo/node_probe.py <node-ip> <port>   e.g. 127.0.0.1 18790 (twin) or 192.168.x.y 8790 (real Node A/B)
# Send one stroke + subscribe to a node, print 1.5 s of replies, validate each against the contracts.
import json, socket, sys, time
sys.path.insert(0, "contracts")
from validate import load_schema, build_schema_registry, validate_against_schema
from pathlib import Path
host, port = sys.argv[1], int(sys.argv[2])
reg = build_schema_registry(Path("contracts/schemas")); schema = load_schema("haptic-message")
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.settimeout(0.2)
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
