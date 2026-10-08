"""EMG on a person, with the motors as the cue (wearing session, 8 Oct 2026).

One socket on local UDP 8790 (the boards ack to the source port and stream to port 8790 of the sender), 1 Hz keepalives to both.
Timeline: REST seconds of rest; buzz 1 (motor 0) = "squeeze"; SQ seconds later buzz 2 (motor 1) = "relax"; AFTER seconds later
buzz 3 (motor 0) with the arm at rest (motor noise on the EMG); TAIL seconds more. Three 200 ms pulses in all.
usage: python tools/demo/hw_emg_check.py <out.json>   (the wearer squeezes on buzz 1 and relaxes on buzz 2)
"""
import json
import math
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
REST, SQ, AFTER, TAIL = 15.0, 3.5, 6.0, 4.0
pulses = [("c1", 0, REST), ("c2", 1, REST + SQ), ("c3", 0, REST + SQ + AFTER)]
END = REST + SQ + AFTER + TAIL

s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
s.bind(("", 8790))
s.setblocking(False)
emg, imu, acks, sent = [], [], {}, {}
t0 = time.perf_counter()
next_keep, todo = 0.0, list(pulses)
while True:
    t = time.perf_counter() - t0
    if t >= END:
        break
    if t >= next_keep:
        next_keep += 1.0
        for n in (A, B):
            s.sendto(b'{"type":"keepalive"}', n)
    if todo and t >= todo[0][2]:
        cid, motor, _ = todo.pop(0)
        s.sendto(json.dumps({"motor": motor, "intensity": 150, "duration_ms": 200, "pattern": "pulse", "cue": "stroke",
                             "cue_id": cid}).encode(), A)
        sent[cid] = time.perf_counter() - t0
    try:
        data, src = s.recvfrom(4096)
    except BlockingIOError:
        time.sleep(0.0005)
        continue
    t = time.perf_counter() - t0
    try:
        m = json.loads(data)
    except ValueError:
        continue
    typ = m.get("type")
    if typ == "sensor_chunk" and src[0] == B[0]:
        v = m.get("emg_envelope") or []
        emg += [(t - (len(v) - 1 - i) * 0.010, float(x)) for i, x in enumerate(v)]
    elif typ == "sensor_data" and src[0] == A[0]:
        sd = m.get("sensors") or {}
        try:
            imu.append((t, math.sqrt(sum(float(sd["imu_accel_" + k]["value"]) ** 2 for k in "xyz"))))
        except (KeyError, TypeError, ValueError):
            pass
    elif typ == "ack":
        acks[m.get("cue_id")] = (t, m.get("accepted"))
s.sendto(b'{"type":"stop"}', A)


def win(series, a, b):
    return [v for (t, v) in series if a <= t < b]


def ms(x):
    return (round(st.mean(x), 1), round(st.pstdev(x), 1), round(max(x), 1), len(x)) if x else None


t1, t2, t3 = sent["c1"], sent["c2"], sent["c3"]
rest = win(emg, 3.0, t1 - 0.2)
squeeze = win(emg, t1 + 0.7, t2)                    # 0.7 s to react to the buzz
after = win(emg, t2 + 1.5, t3 - 0.2)
buzz3 = win(emg, t3, t3 + 0.45)
rest_mean = st.mean(rest) if rest else float("nan")
rest_sd = st.pstdev(rest) if rest else float("nan")
out = {
    "emg_samples": len(emg), "emg_rate_hz": round(len(emg) / END, 1), "imu_samples": len(imu), "imu_rate_hz": round(len(imu) / END, 1),
    "rest (mean, sd, max, n)": ms(rest), "squeeze (mean, sd, max, n)": ms(squeeze), "after_relax (mean, sd, max, n)": ms(after),
    "squeeze_mean_over_rest_mean": round(st.mean(squeeze) / rest_mean, 2) if squeeze and rest_mean else None,
    "squeeze_peak_over_rest_mean": round(max(squeeze) / rest_mean, 2) if squeeze and rest_mean else None,
    "buzz3_at_rest (mean, sd, max, n)": ms(buzz3),
    "buzz3_mean_shift_in_rest_sd": round((st.mean(buzz3) - rest_mean) / rest_sd, 2) if buzz3 and rest_sd else None,
    "buzz3_peak_over_rest_mean": round(max(buzz3) / rest_mean, 2) if buzz3 and rest_mean else None,
    "acks": {c: {"accepted": acks[c][1], "round_trip_ms": round((acks[c][0] - sent[c]) * 1000, 1)} if c in acks else None for c in sent},
}
jolts = {}
for cid, motor, _ in pulses:
    ts = sent[cid]
    base = win(imu, ts - 1.0, ts)
    if len(base) < 20:
        jolts[cid] = None
        continue
    bm, bs = st.mean(base), st.pstdev(base)
    thr = max(6 * bs, 0.3)
    first = next((t for (t, v) in imu if t > ts and abs(v - bm) > thr), None)
    peak = max((abs(v - bm) for (t, v) in imu if ts <= t < ts + 0.5), default=0.0)
    jolts[cid] = {"motor": motor, "imu_rest_sd": round(bs, 3), "first_jolt_after_send_ms": round((first - ts) * 1000, 1) if first and first - ts < 0.5 else None,
                  "peak_jolt_m_s2": round(peak, 2)}
out["imu_jolt_per_buzz"] = jolts
json.dump({"summary": out, "sent": sent, "emg": emg, "imu": imu}, open(sys.argv[1], "w"))
for k, v in out.items():
    print(f"{k}: {v}")
