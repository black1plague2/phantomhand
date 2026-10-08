"""Builds the compact live-card trace of a recorded Phantom Hand session.

Reads sens_###.json (EMG envelope at 100 Hz, raw ADC; IMU ax/ay/az at 100 Hz) and
bins both the way the headset does for `status.payload.trace`
(game/Assets/Shell/Runtime/PhantomLiveStatus.cs, TraceDownsampler): 50 ms bins,
the mean of the samples in a bin, a bin with no sample repeats the last value
(leading gaps use the first value), |accel| = sqrt(ax^2 + ay^2 + az^2).

usage: build_trace.py <session_dir> <out.json> [--report]
"""
import glob
import json
import math
import os
import sys

BIN_MS = 50.0


def load(session_dir):
    emg, acc = [], []
    for path in sorted(glob.glob(os.path.join(session_dir, "sens_*.json"))):
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        e = d.get("emg_env") or {}
        for t, v in zip(e.get("t_ms", []), e.get("value", [])):
            if v is not None and math.isfinite(v):
                emg.append((t, v))
        i = d.get("imu") or {}
        for t, ax, ay, az in zip(i.get("t_ms", []), i.get("ax", []), i.get("ay", []), i.get("az", [])):
            m = math.sqrt(ax * ax + ay * ay + az * az)
            if math.isfinite(m):
                acc.append((t, m))
    return emg, acc


def bin_mean(samples, bins):
    s = [0.0] * bins
    n = [0] * bins
    for t, v in samples:
        i = int(math.floor(t / BIN_MS))
        if 0 <= i < bins:
            s[i] += v
            n[i] += 1
    first = next((i for i in range(bins) if n[i] > 0), None)
    if first is None:
        return [], 0
    out = []
    carry = s[first] / n[first]
    empty = 0
    for i in range(bins):
        if n[i] > 0:
            carry = s[i] / n[i]
        else:
            empty += 1
        out.append(carry)
    return out, empty


def main():
    session_dir, out_path = sys.argv[1], sys.argv[2]
    events = [json.loads(l) for l in open(os.path.join(session_dir, "events.ndjson"), encoding="utf-8") if l.strip()]
    end_ms = max(e["t_ms"] for e in events)
    bins = int(math.ceil(end_ms / BIN_MS))
    emg, acc = load(session_dir)
    emg_b, emg_empty = bin_mean(emg, bins)
    acc_b, acc_empty = bin_mean(acc, bins)
    session = json.load(open(os.path.join(session_dir, "session.json"), encoding="utf-8"))
    doc = {
        "session_id": session["session_id"],
        "source": "sens_###.json of the session, binned like status.payload.trace (50 ms bins, mean per bin)",
        "simulated": True,
        "t0_ms": 0,
        "fs_hz": 20,
        "emg_unit": "raw_adc",
        "accel_unit": "m/s2",
        "emg_env": [round(v, 1) for v in emg_b],
        "accel_mag": [round(v, 2) for v in acc_b],
    }
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, separators=(",", ":"))
        f.write("\n")
    print("samples: emg %d, imu %d; bins %d (%.1f s); empty bins emg %d, accel %d" % (len(emg), len(acc), bins, bins * BIN_MS / 1000, emg_empty, acc_empty))
    print("out: %s, %d bytes" % (out_path, os.path.getsize(out_path)))
    if "--report" in sys.argv:
        srt = sorted(doc["emg_env"])
        print("emg median %.1f, min %.1f, max %.1f" % (srt[len(srt) // 2], srt[0], srt[-1]))
        srt = sorted(doc["accel_mag"])
        print("accel median %.2f, min %.2f, max %.2f" % (srt[len(srt) // 2], srt[0], srt[-1]))
        for e in events:
            if e["type"] in ("threat_impact", "emg_burst"):
                i = int(e["t_ms"] // BIN_MS)
                lo, hi = max(0, i - 4), min(bins, i + 30)
                print(e["type"], "t_ms", e["t_ms"], "bin", i)
                print("  emg  ", doc["emg_env"][lo:hi])
                print("  accel", doc["accel_mag"][lo:hi])
        # raw peaks for comparison
        for name, series in (("emg raw", emg), ("accel raw", acc)):
            t, v = max(series, key=lambda p: p[1])
            print("%s max %.2f at %.1f ms" % (name, v, t))
        # peaks per phase
        starts = [(e["t_ms"], e["data"]["phase"], e["data"].get("condition")) for e in events if e["type"] == "phase_start"]
        starts.append((end_ms, "end", None))
        for (t0, ph, c), (t1, _, _) in zip(starts, starts[1:]):
            a, b = int(t0 // BIN_MS), int(t1 // BIN_MS)
            seg_e, seg_a = doc["emg_env"][a:b], doc["accel_mag"][a:b]
            print("%-14s %-6s %8.1f..%8.1f s=%5.1f emg max %7.1f mean %6.1f | accel max %5.2f min %5.2f" % (
                ph, c, t0, t1, (t1 - t0) / 1000, max(seg_e), sum(seg_e) / len(seg_e), max(seg_a), min(seg_a)))


main()
