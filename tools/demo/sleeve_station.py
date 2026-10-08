#!/usr/bin/env python3
"""tools/demo/sleeve_station.py -- "brush your own arm": the sleeve demo with no headset (R4 table B row 3, tier T3).

A visitor wears the two-motor sleeve (Node A: motor A 5 cm from the wrist, motor B 10 cm further up the forearm) and strokes the
forearm drawn on this screen with the mouse. The pointer passing band A taps motor A once; passing band B taps motor B once. Space
adds a 600 ms delay, so the visitor can feel touch that lands with what they see against touch that lands late. That timing
contrast is all this station shows.

    python tools/demo/sleeve_station.py                                   # find Node A on the LAN (its UDP 8791 beacon)
    python tools/demo/sleeve_station.py --a-ip 192.168.1.40               # manual, host[:port] (default port 8790)
    python tools/demo/sleeve_station.py --a-ip 192.168.1.40 --fullscreen  # audience screen
    python tools/demo/sleeve_station.py --a-ip 127.0.0.1:24000 --script   # no window: replay strokes, one JSON line, exit 0 = ok
    # rehearsal without the sleeve: python -m sim.sleeve.twin --kind haptic --port-offset 15210 --no-stdin [--dialect team]
    #   -> Node A on 127.0.0.1:24000, beacon on 24001 (try discovery with --discovery-port 24001)

Keys: Space = delay on/off, Esc = quit. No --dialect switch: the wire is the same for both firmware dialects (bare device command,
bare {"type":"keepalive"} at 1 Hz, bare {"type":"stop"}; acks read with the precedence ok > accepted > status).

Safety. Limits inside StrokeEngine, out of reach of the UI: >= 250 ms between taps to one motor, <= 4 taps/s in total, intensity
<= 150 of 255, 200 ms pulses, <= 50 % on-time per motor in any 10 s; a tap over a limit is dropped and counted, never queued.
Nothing is ever sent to Node B. `stop` goes out on exit, window close and any exception. No ack for 2 s: the window shows
"Sleeve offline" and taps are held (one trial tap per 2 s, since only a tap can draw an ack).

WARNING: the electronics team's firmware streams telemetry only to the LAST sender. While the Quest is in a session, running this
station (or live_plot.py, which subscribes to the nodes directly) takes the sensor stream away from the headset. Use it headset-off.
"""
from __future__ import annotations

import argparse
import json
import socket
import sys
import threading
import time
import traceback
from dataclasses import dataclass
from typing import Any, Dict, Iterator, List, Optional, Tuple

from live_plot import Feeds, LiveData, parse_hostport      # reused: host[:port] parsing and the 8791 beacon listener

ARM_CM = 25.0                           # forearm drawn on screen, wrist = 0 cm
BANDS_CM = (5.0, 15.0)                  # motor A, motor B (R2 section B: 5 and 15 cm from the wrist crease)
BAND_IN_CM, BAND_OUT_CM = 0.75, 1.25    # a band is 1.5 cm wide; once fired it re-arms when the brush is 1.25 cm from its centre
DELAY_MS = 600.0
PULSE_MS, INTENSITY_CAP = 200, 150      # one tap = a 200 ms pulse; wire intensity never above 150 of 255
GAP_MS = 250.0                          # software limits (02-RULES 4.2): same-motor gap, total rate, duty
RATE_MAX, RATE_WINDOW_MS = 4, 1000.0
DUTY_MAX_MS, DUTY_WINDOW_MS = 5000.0, 10000.0
OFFLINE_MS = 2000.0                     # no ack for this long = "Sleeve offline"
DISCOVER_S = 12.0

BG, ARM, BAND, HOT, GREY, INK = "#10141c", "#c9a27e", "#3a8fd9", "#ffd23f", "#9aa7bd", "#0b0f14"
GREEN, ORANGE, RED = "#2ecc71", "#ffa726", "#e53935"


def now_ms() -> float:
    return time.perf_counter() * 1000.0          # not time.monotonic(): 15.6 ms steps on Windows before Python 3.13


# =============================================================================== stroke logic (pure)
@dataclass
class Cue:
    motor: int
    due_ms: float                   # when the brush passed the band (+ DELAY_MS in the delayed condition)
    t_ms: float                     # when the limiter let it out, i.e. when it is sent
    intensity: int = INTENSITY_CAP
    duration_ms: int = PULSE_MS

    def wire(self, cue_id: str) -> Dict[str, Any]:
        """The device command both firmware dialects take (contracts v1.3): no `type`, no optional extras."""
        return {"cue_id": cue_id, "motor": self.motor, "intensity": self.intensity,
                "duration_ms": self.duration_ms, "pattern": "pulse"}


class StrokeEngine:
    """Pointer position along the arm (cm) + time (ms) in, tap commands out. No tkinter, no sockets.

    A band fires when the brush path touches its 1.5 cm zone, in either direction, and then stays quiet until the brush has moved
    1.25 cm from its centre, so hovering or jitter cannot re-fire it. `move(None, t)` = brush lifted (forgets the path, re-arms).
    The delayed condition holds every tap for DELAY_MS. The limits are checked when a tap would be sent and cannot be switched
    off; a tap that would break one is dropped and counted in `dropped`, never queued."""

    def __init__(self, delayed: bool = False, intensity: int = INTENSITY_CAP, bands_cm: Tuple[float, ...] = BANDS_CM):
        self.delayed, self.bands = delayed, bands_cm
        self.intensity = max(0, min(INTENSITY_CAP, int(intensity)))
        self.prev: Optional[float] = None                         # last on-arm position; None = brush lifted
        self.armed = [True] * len(bands_cm)
        self.pending: List[Tuple[float, int]] = []                # (due_ms, motor), waiting for their time
        self.starts: List[List[float]] = [[] for _ in bands_cm]   # per motor: recent send times
        self.dropped = {"gap": 0, "rate": 0, "duty": 0}

    def set_delayed(self, on: bool) -> None:
        self.delayed = on
        self.pending.clear()            # a tap scheduled under the other condition must not land in this one

    def move(self, pos_cm: Optional[float], t_ms: float) -> List[Cue]:
        if pos_cm is None:
            self.prev, self.armed = None, [True] * len(self.bands)
            return self.poll(t_ms)
        p0 = pos_cm if self.prev is None else self.prev
        lo, hi = min(p0, pos_cm), max(p0, pos_cm)
        for m in sorted(range(len(self.bands)), key=lambda i: abs(self.bands[i] - p0)):     # nearest band first
            c = self.bands[m]
            if self.armed[m] and lo <= c + BAND_IN_CM and hi >= c - BAND_IN_CM:
                self.armed[m] = False
                self.pending.append((t_ms + (DELAY_MS if self.delayed else 0.0), m))
            if abs(pos_cm - c) > BAND_OUT_CM:
                self.armed[m] = True
        self.prev = pos_cm
        return self.poll(t_ms)

    def poll(self, t_ms: float) -> List[Cue]:
        """Release the taps that are due (limits checked now, at send time). Call at least every ~10 ms."""
        due = sorted((p for p in self.pending if p[0] <= t_ms), key=lambda p: p[0])      # stable: same instant = path order
        self.pending = [p for p in self.pending if p[0] > t_ms]
        out: List[Cue] = []
        for due_ms, m in due:
            why = self._refuse(m, t_ms)
            if why:
                self.dropped[why] += 1
                continue
            self.starts[m].append(t_ms)
            out.append(Cue(m, due_ms, t_ms, self.intensity))
        return out

    def _refuse(self, m: int, t: float) -> Optional[str]:
        for s in self.starts:
            s[:] = [x for x in s if t - x < DUTY_WINDOW_MS + PULSE_MS]            # older pulses are out of every window
        if self.starts[m] and t - self.starts[m][-1] < GAP_MS:
            return "gap"
        if sum(1 for s in self.starts for x in s if t - x <= RATE_WINDOW_MS) >= RATE_MAX:     # a closed 1 s window holds <= 4
            return "rate"
        # on-time of this motor in [t - 10 s, t + 200 ms], the tap about to start included, must stay <= 50 % of 10 s
        used = sum(max(0.0, PULSE_MS - max(0.0, t - DUTY_WINDOW_MS - x)) for x in self.starts[m])
        return "duty" if used + PULSE_MS > DUTY_MAX_MS else None


class Link:
    """Ack watchdog (pure). Offline = a tap has waited OFFLINE_MS for ANY ack. Acks only ever answer taps, so while offline one
    trial tap per OFFLINE_MS is let through (a stroke of the visitor's); without it the station could never see the sleeve return."""

    def __init__(self) -> None:
        self.waiting: Optional[float] = None        # send time of the first tap sent since the last ack
        self.trial = float("-inf")

    def offline(self, now: float) -> bool:
        return self.waiting is not None and now - self.waiting >= OFFLINE_MS

    def allow(self, now: float) -> bool:
        if not self.offline(now):
            return True
        if now - self.trial >= OFFLINE_MS:
            self.trial = now
            return True
        return False

    def sent(self, now: float) -> None:
        if self.waiting is None:
            self.waiting = now

    def acked(self) -> None:
        self.waiting = None


def ack_ok(ack: Dict[str, Any]) -> bool:
    """Did the node play the tap? `ok`, then `accepted`, then `status`; none of them = a plain receipt (as HapticClient does)."""
    for key in ("ok", "accepted"):
        if isinstance(ack.get(key), bool):
            return ack[key]
    return ack.get("status") in (None, "accepted", "executed")


# =============================================================================== I/O
class Sleeve:
    """UDP to Node A and nobody else: bare cue, bare keepalive, bare stop. Acks come back on the same socket; whatever else the
    node streams there (the team firmware sends its sensors to the last sender) is read and thrown away."""

    def __init__(self, addr: Tuple[str, int], sock: Any = None):
        self.addr = addr
        self.sock = sock
        if sock is None:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            self.sock.bind(("0.0.0.0", 0))
            self.sock.setblocking(False)

    def send(self, msg: Dict[str, Any]) -> bool:
        try:
            self.sock.sendto(json.dumps(msg, separators=(",", ":")).encode("utf-8"), self.addr)
            return True
        except OSError:
            return False

    def keepalive(self) -> None:
        self.send({"type": "keepalive"})

    def stop(self) -> None:
        for _ in range(2):                          # one datagram can be lost; the node's own 2 s watchdog is the backstop
            self.send({"type": "stop"})

    def close(self) -> None:
        self.sock.close()

    def recv_acks(self) -> List[Tuple[str, bool, float]]:
        """(cue_id, played, arrival_ms) for each ack waiting on the socket; never blocks."""
        out: List[Tuple[str, bool, float]] = []
        for _ in range(500):
            try:
                data, _ = self.sock.recvfrom(4096)
            except (BlockingIOError, socket.timeout):
                break
            except ConnectionResetError:            # Windows: an earlier datagram met a closed port; nothing to read
                continue
            except OSError:
                break
            t = now_ms()
            if b'"ack"' not in data:                # sensor_data and the like: not worth parsing
                continue
            try:
                msg = json.loads(data.decode("utf-8"))
            except ValueError:
                continue
            if isinstance(msg, dict) and msg.get("type") == "ack":
                out.append((str(msg.get("ack_id") or msg.get("cue_id") or ""), ack_ok(msg), t))
        return out


class Station:
    """Everything between the stroke engine and the sleeve: the offline gate, UDP, acks, the 1 Hz keepalive and the counters."""

    def __init__(self, sleeve: Any, engine: Optional[StrokeEngine] = None):
        self.sleeve, self.engine, self.link = sleeve, engine or StrokeEngine(), Link()
        self.sent = self.acked = self.refused = self.held = 0
        self.latency_ms: List[float] = []
        self.pending: Dict[str, float] = {}         # cue_id -> send time, until its ack arrives
        self.flash: Dict[int, float] = {}           # motor -> last send time (the screen lights the band)
        self.last_ack_ms: Optional[float] = None
        self._next_ka, self._n, self._closed = 0.0, 0, False

    @property
    def dropped(self) -> int:
        return sum(self.engine.dropped.values())

    def pointer(self, pos_cm: Optional[float], now: float) -> None:
        self._send(self.engine.move(pos_cm, now))

    def toggle_delay(self) -> None:
        self.engine.set_delayed(not self.engine.delayed)

    def tick(self, now: float) -> None:
        self._send(self.engine.poll(now))
        if now >= self._next_ka:
            self._next_ka = now + 1000.0
            self.sleeve.keepalive()
        for cue_id, played, t in self.sleeve.recv_acks():
            self.link.acked()
            self.last_ack_ms = t
            t0 = self.pending.pop(cue_id, None)
            if t0 is None:                          # not one of ours: proof of life, nothing to count
                continue
            if played:
                self.acked += 1
                self.latency_ms.append(t - t0)
            else:
                self.refused += 1

    def _send(self, cues: List[Cue]) -> None:
        for c in cues:
            if not self.link.allow(c.t_ms):
                self.held += 1
                continue
            self._n += 1
            cue_id = f"st_{self._n:04d}"
            ok = self.sleeve.send(c.wire(cue_id))
            self.link.sent(c.t_ms)
            if ok:
                self.sent += 1
                self.pending[cue_id] = c.t_ms
                self.flash[c.motor] = c.t_ms
                while len(self.pending) > 64:       # taps whose ack never came
                    self.pending.pop(next(iter(self.pending)))

    def summary(self) -> Dict[str, Any]:
        lat = self.latency_ms
        return {"cues_sent": self.sent, "acks_accepted": self.acked, "acks_refused": self.refused,
                "acks_missing": self.sent - self.acked - self.refused, "dropped_by_limiter": self.dropped,
                "held_offline": self.held, "mean_ack_latency_ms": round(sum(lat) / len(lat), 1) if lat else None}

    def close(self) -> None:
        if not self._closed:
            self._closed = True
            self.sleeve.stop()
            self.sleeve.close()


# =============================================================================== headless script
def script_samples(strokes: int = 6, sync: int = 3, speed_cm_s: float = 12.0, hz: float = 100.0
                   ) -> Iterator[Tuple[float, Optional[float], bool]]:
    """(t_ms, position_cm or None = brush lifted, delayed?) for slow strokes wrist to elbow, 0.6 s apart: first `sync` in sync."""
    n, t = int(ARM_CM / speed_cm_s * hz), 0.0
    for i in range(strokes):
        for k in range(n + 1):
            yield t + k * 1000.0 / hz, speed_cm_s * k / hz, i >= sync
        t += (n + 1) * 1000.0 / hz
        yield t, None, i >= sync
        t += 600.0


def run_script(st: Station, strokes: int = 6, sync: int = 3) -> Dict[str, Any]:
    """Replay the built-in strokes in real time; the summary plus "pass" (>= 95 % of the taps sent were acked as played)."""
    t0 = now_ms()
    for t_ms, pos, delayed in script_samples(strokes, sync):
        while now_ms() - t0 < t_ms:
            st.tick(now_ms())
            time.sleep(0.001)
        if delayed != st.engine.delayed:
            st.toggle_delay()
        st.pointer(pos, now_ms())
    end = now_ms() + DELAY_MS + 1500.0              # late taps and their acks
    while now_ms() < end and (st.engine.pending or st.pending):
        st.tick(now_ms())
        time.sleep(0.001)
    s = st.summary()
    s["pass"] = s["cues_sent"] > 0 and s["acks_accepted"] >= 0.95 * s["cues_sent"]
    return s


# =============================================================================== window
class StationApp:
    """The tkinter window: forearm, two bands, brush dot, banner, counters. All the logic is in Station / StrokeEngine."""

    def __init__(self, station: Station, fullscreen: bool = False):
        import tkinter as tk
        self.st, self.closed, self.failed, self.mouse, self.next_draw = station, False, False, None, 0.0
        self.root = tk.Tk()
        self.root.title("Chetna sleeve station - touch and sight timing")
        self.root.configure(bg=BG)
        if fullscreen:
            self.root.attributes("-fullscreen", True)
        else:
            self.root.geometry("1280x720")
        self.cv = tk.Canvas(self.root, bg=BG, highlightthickness=0)
        self.cv.pack(fill="both", expand=True)
        self.cv.bind("<Motion>", self.on_motion)
        self.cv.bind("<Leave>", lambda e: self.on_motion(None))
        self.root.bind("<space>", lambda e: self.st.toggle_delay())
        self.root.bind("<Escape>", lambda e: self.quit())
        self.root.protocol("WM_DELETE_WINDOW", self.quit)
        self.root.report_callback_exception = self.on_error
        self.root.focus_force()
        self.root.after(5, self.tick)

    def geom(self) -> Tuple[float, float, float, float, float]:
        w, h = max(self.cv.winfo_width(), 400), max(self.cv.winfo_height(), 300)
        return w, h, 0.07 * w, 0.93 * w, 0.54 * h        # canvas size, arm left/right edge, arm centre line

    def on_motion(self, e: Any) -> None:
        pos = None
        if e is not None:
            w, h, x0, x1, yc = self.geom()
            cm = (e.x - x0) / (x1 - x0) * ARM_CM
            pos = cm if 0.0 <= cm <= ARM_CM and abs(e.y - yc) <= (0.09 + 0.04 * cm / ARM_CM) * h else None   # inside the drawn arm
        self.mouse = None if e is None else (e.x, e.y)
        self.st.pointer(pos, now_ms())

    def tick(self) -> None:
        if self.closed:
            return
        now = now_ms()
        self.st.tick(now)
        if now >= self.next_draw:
            self.next_draw = now + 33.0
            self.draw(now)
        self.root.after(5, self.tick)

    def text(self, x: float, y: float, s: str, size: float, fill: str, anchor: str = "center") -> None:
        self.cv.create_text(x, y, text=s, fill=fill, anchor=anchor, font=("Segoe UI", -max(8, int(size)), "bold"))

    def draw(self, now: float) -> None:
        st, cv = self.st, self.cv
        w, h, x0, x1, yc = self.geom()
        cv.delete("all")
        delayed = st.engine.delayed
        cv.create_rectangle(0, 0, w, 0.19 * h, fill=ORANGE if delayed else GREEN, outline="")
        self.text(w / 2, 0.095 * h, f"DELAYED {DELAY_MS:.0f} ms" if delayed else "IN SYNC", 0.12 * h, INK)
        if st.link.offline(now):
            cv.create_rectangle(0, 0.19 * h, w, 0.29 * h, fill=RED, outline="")
            self.text(w / 2, 0.24 * h, "Sleeve offline - no ack for 2 s, taps held", 0.06 * h, "#ffffff")
        else:
            ago = "waiting for the first tap" if st.last_ack_ms is None else f"last ack {(now - st.last_ack_ms) / 1000:.1f} s ago"
            self.text(w / 2, 0.24 * h, f"Sleeve: {ago}", 0.04 * h, GREY)
        cv.create_polygon(x0, yc - 0.09 * h, x1, yc - 0.13 * h, x1, yc + 0.13 * h, x0, yc + 0.09 * h, fill=ARM, outline="")
        half = BAND_IN_CM / ARM_CM * (x1 - x0)
        for m, cm in enumerate(BANDS_CM):
            bx, hot = x0 + cm / ARM_CM * (x1 - x0), now - st.flash.get(m, -1e9) < 250.0     # lit while the tap goes out
            cv.create_rectangle(bx - half, yc - 0.15 * h, bx + half, yc + 0.15 * h, fill=HOT if hot else BAND, outline="")
            self.text(bx, yc - 0.2 * h, "AB"[m], 0.07 * h, "#ffffff")
            self.text(bx, yc + 0.2 * h, f"motor {'AB'[m]}, {cm:.0f} cm", 0.035 * h, GREY)
        self.text(x0, yc + 0.27 * h, "wrist", 0.035 * h, GREY, "w")
        self.text(x1, yc + 0.27 * h, "elbow", 0.035 * h, GREY, "e")
        if self.mouse:
            r, down = 0.03 * h, st.engine.prev is not None
            cv.create_oval(self.mouse[0] - r, self.mouse[1] - r, self.mouse[0] + r, self.mouse[1] + r,
                           fill="#ffffff" if down else "", outline="#ffffff", width=3)
        parts = [f"taps sent {st.sent}", f"acked {st.acked}", f"dropped by limiter {st.dropped}"]
        if st.refused:
            parts.append(f"refused by sleeve {st.refused}")
        if st.held:
            parts.append(f"held (offline) {st.held}")
        self.text(w / 2, 0.83 * h, "    |    ".join(parts), 0.05 * h, "#ffffff")
        self.text(w / 2, 0.93 * h, "Stroke the arm slowly with the mouse, wrist to elbow.     Space: delay on/off     Esc: quit",
                  0.035 * h, GREY)

    def quit(self) -> None:
        if not self.closed:
            self.closed = True
            self.st.close()
            self.root.destroy()

    def on_error(self, *exc: Any) -> None:
        traceback.print_exception(*exc)
        self.failed = True
        self.quit()                                   # the safe state: motors stopped, window gone

    def run(self) -> None:
        try:
            self.root.mainloop()
        finally:
            self.st.close()


# =============================================================================== CLI
def discover(port: int, wait_s: float = DISCOVER_S) -> Optional[Tuple[str, int]]:
    """Node A's address from its 1 Hz beacon, with live_plot's own listener (it only listens; nothing is sent)."""
    feeds = Feeds(LiveData(), None, None, port, None)
    threading.Thread(target=feeds._discovery_loop, daemon=True).start()
    end = time.monotonic() + wait_s
    while feeds.addr["A"] is None and time.monotonic() < end:
        time.sleep(0.05)
    feeds.stop()
    return feeds.addr["A"]


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description="Chetna sleeve station: stroke an on-screen forearm, the sleeve taps (touch-sight timing)")
    ap.add_argument("--a-ip", help="Node A ip[:port] (default port 8790); omit to discover it")
    ap.add_argument("--discovery-port", type=int, default=8791, help="UDP port Node A announces itself on (default 8791)")
    ap.add_argument("--fullscreen", action="store_true", help="audience mode (Esc leaves)")
    ap.add_argument("--script", action="store_true",
                    help="no window: replay built-in strokes, print one JSON line, exit 0 if >= 95 %% of the taps were acked as played")
    a = ap.parse_args(argv)
    print("sleeve_station: note - the team firmware streams sensors to the LAST sender, so with the Quest in a session this takes "
          "the stream away from the headset; run it headset-off", file=sys.stderr)
    addr = parse_hostport(a.a_ip)
    if addr is None:
        print(f"sleeve_station: listening for Node A on UDP {a.discovery_port} (up to {DISCOVER_S:.0f} s) ...", file=sys.stderr)
        addr = discover(a.discovery_port)
        if addr is None:
            print("sleeve_station: Node A not found; pass --a-ip host[:port]", file=sys.stderr)
            return 2
        print(f"sleeve_station: Node A at {addr[0]}:{addr[1]}", file=sys.stderr)
    station = Station(Sleeve(addr))
    try:
        if a.script:
            s = run_script(station)
            print(json.dumps(s))
            return 0 if s["pass"] else 1
        app = StationApp(station, a.fullscreen)
        app.run()
        return 1 if app.failed else 0
    finally:
        station.close()


if __name__ == "__main__":
    sys.exit(main())
