"""Where does a real node send what? One command socket on an ephemeral port, one listener on UDP 8790.
usage: python tools/demo/hw_wire_check.py <node-ip> [--pulse]     (--pulse: one 150 ms pulse on motor 0, Node A only)
Prints, per local socket, how many datagrams of each type arrived in 3 s and from which source port."""
import json
import socket
import sys
import time

ip = sys.argv[1]
cmd = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
cmd.bind(("", 0))
tel = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
tel.bind(("", 8790))
for s in (cmd, tel):
    s.setblocking(False)
print("command socket on local port", cmd.getsockname()[1], "| listener on 8790")
seen = {"cmd": {}, "8790": {}}
first = {}
t0 = time.time()
next_keep = 0.0
pulsed = False
while time.time() - t0 < 3.0:
    now = time.time() - t0
    if now >= next_keep:
        cmd.sendto(b'{"type":"keepalive"}', (ip, 8790))
        next_keep += 1.0
    if "--pulse" in sys.argv and not pulsed and now > 1.0:
        pulsed = True
        cmd.sendto(json.dumps({"motor": 0, "intensity": 120, "duration_ms": 150, "pattern": "pulse", "cue": "stroke",
                               "cue_id": "wire_check_1"}).encode(), (ip, 8790))
    for name, s in (("cmd", cmd), ("8790", tel)):
        try:
            data, src = s.recvfrom(4096)
        except BlockingIOError:
            continue
        if src[0] != ip:
            continue
        try:
            msg = json.loads(data)
        except ValueError:
            msg = {"type": "?"}
        key = (msg.get("type", "?"), src[1])
        seen[name][key] = seen[name].get(key, 0) + 1
        first.setdefault((name, msg.get("type", "?")), json.dumps(msg)[:230])
    time.sleep(0.001)
for name in seen:
    print(name, {f"{t} from port {p}": n for (t, p), n in seen[name].items()} or "nothing")
for (name, typ), text in first.items():
    print(f"first {typ} on {name}: {text}")
