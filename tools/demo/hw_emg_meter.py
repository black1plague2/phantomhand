"""Tactile EMG meter: the sleeve buzzes whenever the EMG envelope rises well above its resting level.
For finding the electrode spot without a screen. usage: python tools/demo/hw_emg_meter.py <seconds> <out.json>"""
import json
import socket
import statistics as st
import sys
import time

import os
# node addresses: PH_NODE_A / PH_NODE_B ("ip" or "ip:port"), default = the boards as they were on the hotspot on 8 Oct 2026
def _node(env, default):
    h, _, p = os.environ.get(env, default).partition(":")
    return (h, int(p or 8790))
A, B = _node("PH_NODE_A", "192.168.242.254"), _node("PH_NODE_B", "192.168.242.244")
DUR = float(sys.argv[1])
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
s.bind(("", 8790))
s.setblocking(False)
emg, means, buzzes = [], [], []          # (t, value); (t, 0.25 s mean, baseline); buzz times
t0 = time.perf_counter()
next_keep = next_eval = 0.0
last_buzz, n = -9.0, 0
while True:
    t = time.perf_counter() - t0
    if t >= DUR:
        break
    if t >= next_keep:
        next_keep += 1.0
        for node in (A, B):
            s.sendto(b'{"type":"keepalive"}', node)
    if t >= next_eval:
        next_eval += 0.1
        recent = [v for (x, v) in emg[-40:] if x >= t - 0.25]
        if len(recent) >= 10:
            m = st.mean(recent)
            hist = sorted(v for (x, v, _) in means if x >= t - 8.0)
            base = hist[len(hist) // 5] if len(hist) >= 20 else None      # resting level: the low fifth of the last 8 s
            means.append((t, m, base))
            if base is not None and t > 6.0 and m > max(2.0 * base, base + 60.0) and t - last_buzz > 0.7 and n < 45:
                n += 1
                last_buzz = t
                buzzes.append((round(t, 2), round(m / base, 2)))
                s.sendto(json.dumps({"motor": 0, "intensity": 120, "duration_ms": 100, "pattern": "pulse", "cue": "stroke",
                                     "cue_id": "fb%d" % n}).encode(), A)
    try:
        data, src = s.recvfrom(4096)
    except BlockingIOError:
        time.sleep(0.0005)
        continue
    if src[0] != B[0]:
        continue
    try:
        msg = json.loads(data)
    except ValueError:
        continue
    if msg.get("type") == "sensor_chunk":
        v = msg.get("emg_envelope") or []
        t = time.perf_counter() - t0
        emg += [(t - (len(v) - 1 - i) * 0.010, float(x)) for i, x in enumerate(v)]
s.sendto(b'{"type":"stop"}', A)
json.dump({"buzzes": buzzes, "means": means}, open(sys.argv[2], "w"))
print("buzzes:", len(buzzes), buzzes[:45])
print("t(s)  low  mean  high (0.25 s means of the envelope, 5 s rows)  best ratio to the resting level")
t = 0.0
while t < DUR:
    row = [(m, b) for (x, m, b) in means if t <= x < t + 5.0]
    if row:
        ms = [m for m, _ in row]
        ratios = [m / b for m, b in row if b]
        print(f"{t:4.0f}  {min(ms):5.0f} {st.mean(ms):5.0f} {max(ms):6.0f}   {max(ratios):.2f}" if ratios else f"{t:4.0f}  {min(ms):5.0f} {st.mean(ms):5.0f} {max(ms):6.0f}")
    t += 5.0
