"""Plot what the game received in one run: EMG envelope, arm movement, stroke cues. usage: python tools/demo/hw_plot_run.py traces.json out.png "title"   (needs matplotlib; traces.json = {emg:[[t_ms,v]], imu:[[t_ms,|a|]], cues:[[t_ms,delivered,motor]], impacts:[t_ms], phases:[[t_ms,phase,condition]]})"""
import json
import statistics as st
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

T = json.load(open(sys.argv[1]))
emg, imu, cues, imp, ph = T["emg"], T["imu"], T["cues"], T["impacts"], T["phases"]


def binned(series, w=100.0):
    out, cur, acc = [], None, []
    for t, v in series:
        b = int(t // w)
        if b != cur and acc:
            out.append((cur * w / 1000.0, sum(acc) / len(acc)))
            acc = []
        cur = b
        acc.append(v)
    return out


e = binned(emg)
g = st.median(v for _, v in imu)
m = binned([(t, abs(v - g)) for t, v in imu])
fig, ax = plt.subplots(3, 1, figsize=(12, 7.2), sharex=True, gridspec_kw={"height_ratios": [3, 3, 1.3]})
ax[0].plot([t for t, _ in e], [v for _, v in e], color="#0f7b6c", lw=1.0)
ax[0].set_ylabel("EMG envelope (raw ADC)\nNode B, pads on forearm")
ax[0].set_title(sys.argv[3], fontsize=11, loc="left")
ax[1].plot([t for t, _ in m], [v for _, v in m], color="#1f5fbf", lw=1.0)
ax[1].set_ylabel("Arm movement |a| - g (m/s2)\nNode A IMU")
ok = [t / 1000 for t, d, mo in cues if d]
bad = [t / 1000 for t, d, mo in cues if not d]
ax[2].vlines([t / 1000 for t, d, mo in cues if d and mo == 0], 0.55, 1.0, color="#d85a30", lw=1.2)
ax[2].vlines([t / 1000 for t, d, mo in cues if d and mo == 1], 0.0, 0.45, color="#b0417a", lw=1.2)
ax[2].scatter(bad, [0.22] * len(bad), marker="x", color="black", zorder=5, s=60)
ax[2].set_yticks([0.22, 0.78])
ax[2].set_yticklabels(["motor B", "motor A"])
ax[2].set_ylim(-0.1, 1.1)
ax[2].set_xlabel(f"session time (s)   |   strokes acked {len(ok)}/{len(cues)}; x = no ack ({len(bad)})   |   "
                 f"EMG {len(emg)} samples, IMU {len(imu)} samples, both about 100 Hz")
for a in ax[:2]:
    for t in imp:
        a.axvline(t / 1000, color="#444", ls="--", lw=1)
for a in ax:
    for t, p, c in ph:
        if p == "induction":
            a.axvspan(t / 1000, t / 1000 + 60, color="#f2c94c" if c == "async" else "#6fcf97", alpha=0.13, lw=0)
    a.grid(alpha=0.25)
    a.spines[["top", "right"]].set_visible(False)
top = ax[0].get_ylim()[1]
for t, p, c in ph:
    if p == "induction":
        ax[0].text(t / 1000 + 1, top * 0.93, f"{c.upper()} strokes", fontsize=9)
for t in imp:
    ax[0].text(t / 1000 + 0.6, top * 0.80, "stone\nimpact", fontsize=8)
fig.tight_layout()
fig.savefig(sys.argv[2], dpi=110)
print("saved", sys.argv[2])
