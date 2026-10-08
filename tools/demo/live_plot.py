#!/usr/bin/env python3
"""tools/demo/live_plot.py -- laptop live plot for Phantom Hand (PRD FR-AP-01 fallback, 03-SPEC D3/D4).

Subscribes to Node A (haptic + IMU) and Node B (bio / EMG) directly over UDP (the firmware keeps up to 3
subscribers, so this never steals the stream from the Quest) and plots the last 10 s of

    * EMG envelope (Node B `sensor_chunk`, 100 Hz) with the median baseline, `emg_burst` marked
    * |accel| (Node A `sensor_data`, 100 Hz), `threat_impact` marked

plus SYNC / ASYNC, the phase and the node status taken from the game's live status when `--hub` is given.

    python tools/demo/live_plot.py                                   # discover both nodes on :8791 (real nodes)
    python tools/demo/live_plot.py --a-ip 192.168.1.40 --b-ip 192.168.1.41   # both use port 8790 on their own IPs
    python tools/demo/live_plot.py --a-ip 127.0.0.1:39790 --b-ip 127.0.0.1:39792 --discovery-port 0   # the twin
    python tools/demo/live_plot.py --hub 192.168.1.10:8787 --fullscreen      # audience screen (Esc leaves)
    python tools/demo/live_plot.py --save-png out.png --duration 10          # headless: collect 10 s, write a PNG

Backends (this machine: no matplotlib, no pyqtgraph -- not installable per the run rules): a tkinter window
(stdlib) for the live view; for --save-png Pillow if importable, else a built-in pure-python rasteriser
(no text labels). matplotlib / pyqtgraph are NOT wired: they could not be tested here.

SYNC/ASYNC from the hub: `--hub` polls `GET /opus/v1/live/last_status` -> {"status":{...,"game_state":{...}},
"events":[...]} (contracts/LIVE_PROTOCOL.md), served by the Flutter hub (since 2026-10-08) and by
tools/demo/phantom_pipeline.py's RecordingHub. Against an older hub without that route the banner says so.
"""
from __future__ import annotations

import argparse
import json
import math
import socket
import struct
import sys
import threading
import time
import urllib.error
import urllib.request
import zlib
from collections import deque
from typing import Any, Deque, Dict, List, Optional, Tuple

WINDOW_S = 10.0
STALE_S = 1.5
EMG_BASELINE_HINT = 420.0

# palette (dark, audience-friendly)
BG = (16, 20, 28)
PANEL = (24, 30, 42)
GRID = (52, 62, 80)
TEXT = (226, 232, 240)
DIM = (140, 152, 172)
EMG_C = (64, 196, 255)
ACC_C = (120, 224, 143)
BURST_C = (255, 176, 46)
IMPACT_C = (255, 82, 82)
SYNC_C = (46, 204, 113)
ASYNC_C = (255, 140, 0)
NA_C = (120, 128, 140)


# =============================================================================== data
class LiveData:
    """Thread-safe ring buffers + markers + status. Times are monotonic seconds."""

    def __init__(self, window_s: float = WINDOW_S):
        self.window_s = window_s
        self.lock = threading.Lock()
        self.cond = threading.Condition(self.lock)    # lets tests/tools wait for data instead of sleeping
        self.emg: Deque[Tuple[float, float]] = deque(maxlen=int(window_s * 100 * 3))
        self.acc: Deque[Tuple[float, float]] = deque(maxlen=int(window_s * 100 * 3))
        self.bursts: Deque[Tuple[float, float]] = deque(maxlen=64)       # (t, peak)
        self.impacts: Deque[float] = deque(maxlen=64)
        self.last_rx: Dict[str, float] = {}
        self.counts: Dict[str, int] = {"sensor_chunk": 0, "sensor_data": 0, "emg_burst": 0, "status_a": 0, "status_b": 0}
        self.phase: Optional[str] = None
        self.condition: Optional[str] = None
        self.hub_state: str = "no --hub given"          # shown when the condition is unknown
        self.game_nodes: Dict[str, Any] = {}
        self._last_seq = -1

    def ingest_node(self, node: str, msg: Dict[str, Any], t: float) -> None:
        typ = msg.get("type")
        with self.lock:
            self.last_rx[node] = t
            if typ == "sensor_chunk":
                vals = [float(v) for v in (msg.get("emg_envelope") or []) if isinstance(v, (int, float))]
                rate = float(msg.get("sample_rate_hz") or 100.0)
                for i, v in enumerate(vals):                       # the chunk arrives right after its last sample
                    self.emg.append((t - (len(vals) - 1 - i) / rate, v))
                self.counts["sensor_chunk"] += 1
            elif typ == "sensor_data":
                s = msg.get("sensors") or {}
                try:
                    x, y, z = (float(s[f"imu_accel_{a}"]["value"]) for a in "xyz")
                    self.acc.append((t, math.sqrt(x * x + y * y + z * z)))
                    self.counts["sensor_data"] += 1
                except (KeyError, TypeError, ValueError):
                    pass
            elif typ == "emg_burst":
                self.bursts.append((t, float(msg.get("peak") or 0.0)))
                self.counts["emg_burst"] += 1
            elif typ == "status":
                self.counts["status_a" if node == "A" else "status_b"] += 1
            self.cond.notify_all()

    def ingest_hub(self, doc: Dict[str, Any], t: float) -> None:
        """doc = {"status": {...payload...}|None, "events": [event payloads]}"""
        with self.lock:
            st = (doc or {}).get("status") or {}
            gs = st.get("game_state")
            if isinstance(gs, dict):
                self.phase, self.condition = gs.get("phase"), gs.get("condition")
                self.game_nodes = gs.get("nodes") or {}
                self.hub_state = "ok"
            else:
                self.hub_state = "hub reachable, no game_state yet"
            evs = (doc or {}).get("events") or []
            new = [e for e in evs if isinstance(e.get("seq"), int) and e["seq"] > self._last_seq]
            if new:
                self._last_seq = max(e["seq"] for e in new)
            for e in new:
                if e.get("type") == "threat_impact":
                    self.impacts.append(t)
            self.cond.notify_all()

    def wait_for(self, pred: Any, timeout: float) -> bool:
        """Block until pred(self) is true (checked on every ingest) or timeout; True if it became true."""
        with self.cond:
            return self.cond.wait_for(lambda: pred(self), timeout)

    def hub_error(self, why: str) -> None:
        with self.lock:
            self.hub_state = why

    def node_ok(self, node: str, now: float) -> bool:
        with self.lock:
            return node in self.last_rx and now - self.last_rx[node] < STALE_S

    def snapshot(self, now: float) -> Dict[str, Any]:
        lo = now - self.window_s
        with self.lock:
            return {
                "emg": [(t - now, v) for t, v in self.emg if t >= lo],
                "acc": [(t - now, v) for t, v in self.acc if t >= lo],
                "bursts": [(t - now, p) for t, p in self.bursts if t >= lo],
                "impacts": [t - now for t in self.impacts if t >= lo],
                "phase": self.phase, "condition": self.condition, "hub_state": self.hub_state,
                "a_ok": "A" in self.last_rx and now - self.last_rx["A"] < STALE_S,
                "b_ok": "B" in self.last_rx and now - self.last_rx["B"] < STALE_S,
                "counts": dict(self.counts), "window_s": self.window_s,
            }


# =============================================================================== network
def parse_hostport(s: Optional[str], default_port: int = 8790) -> Optional[Tuple[str, int]]:
    if not s:
        return None
    host, _, port = s.partition(":")
    return host, int(port or default_port)


class Feeds:
    """Background threads: one UDP subscriber per node, optional discovery, optional hub poller."""

    def __init__(self, data: LiveData, a: Optional[Tuple[str, int]], b: Optional[Tuple[str, int]],
                 discovery_port: int, hub: Optional[str]):
        self.data = data
        self.addr: Dict[str, Optional[Tuple[str, int]]] = {"A": a, "B": b}
        self.discovery_port = discovery_port
        self.hub = hub
        self.stop_evt = threading.Event()
        self.threads: List[threading.Thread] = []
        self.discovered: Dict[str, Tuple[str, int]] = {}
        self.socks: List[socket.socket] = []

    def start(self) -> None:
        if self.discovery_port and (self.addr["A"] is None or self.addr["B"] is None):
            self._spawn(self._discovery_loop)
        for node in ("A", "B"):
            self._spawn(self._node_loop, node)
        if self.hub:
            self._spawn(self._hub_loop)

    def _spawn(self, fn: Any, *args: Any) -> None:
        t = threading.Thread(target=fn, args=args, daemon=True)
        t.start()
        self.threads.append(t)

    def stop(self) -> None:
        self.stop_evt.set()
        for s in self.socks:
            try:
                s.close()
            except OSError:
                pass

    def _discovery_loop(self) -> None:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            s.bind(("0.0.0.0", self.discovery_port))
        except OSError as e:
            print(f"live_plot: cannot bind discovery port {self.discovery_port}: {e}", file=sys.stderr)
            return
        s.settimeout(0.2)
        self.socks.append(s)
        while not self.stop_evt.is_set():
            try:
                raw, addr = s.recvfrom(4096)
                msg = json.loads(raw.decode("utf-8"))
            except (socket.timeout, ValueError, UnicodeDecodeError, ConnectionResetError):
                continue
            except OSError:
                return
            if msg.get("type") == "device_discovery" and isinstance(msg.get("command_port"), int):
                node = {"haptic": "A", "bio": "B"}.get(msg.get("device_kind"))
                if node and self.addr[node] is None:
                    ip = msg.get("ip") if isinstance(msg.get("ip"), str) and msg.get("ip") else addr[0]
                    self.discovered[node] = (ip, msg["command_port"])
                    self.addr[node] = self.discovered[node]

    def _node_loop(self, node: str) -> None:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.bind(("0.0.0.0", 0))
        s.settimeout(0.05)
        self.socks.append(s)
        next_sub = 0.0
        while not self.stop_evt.is_set():
            now = time.monotonic()
            addr = self.addr[node]
            if addr is not None and now >= next_sub:
                try:
                    s.sendto(b'{"type":"subscribe"}', addr)      # every <= 2 s keeps the subscription (D3)
                except OSError:
                    pass
                next_sub = now + 1.0
            try:
                raw, _ = s.recvfrom(4096)
            except socket.timeout:
                continue
            except ConnectionResetError:        # Windows: ICMP port-unreachable from a node that is not up YET
                continue
            except OSError:
                return
            try:
                msg = json.loads(raw.decode("utf-8"))
            except (ValueError, UnicodeDecodeError):
                continue
            if isinstance(msg, dict):
                self.data.ingest_node(node, msg, time.monotonic())

    def _hub_loop(self) -> None:
        host, _, port = self.hub.partition(":")                  # type: ignore[union-attr]
        url = f"http://{host}:{int(port or 8787)}/opus/v1/live/last_status"
        while not self.stop_evt.is_set():
            try:
                with urllib.request.urlopen(url, timeout=1.0) as r:
                    self.data.ingest_hub(json.loads(r.read().decode("utf-8")), time.monotonic())
            except urllib.error.HTTPError as e:
                self.data.hub_error("hub has no /opus/v1/live/last_status (HTTP %d)" % e.code)
            except (OSError, ValueError) as e:
                self.data.hub_error(f"hub unreachable ({type(e).__name__})")
            self.stop_evt.wait(0.4)


# =============================================================================== drawing (shared by every backend)
def nice_range(vals: List[float], pad: float = 0.1, min_span: float = 1.0) -> Tuple[float, float]:
    if not vals:
        return 0.0, 1.0
    lo, hi = min(vals), max(vals)
    span = max(hi - lo, min_span)
    mid = (hi + lo) / 2.0
    return mid - span * (0.5 + pad), mid + span * (0.5 + pad)


def draw_scene(c: Any, snap: Dict[str, Any], w: int, h: int, big: bool = False) -> None:
    """Draw one frame on a canvas with rect/line/polyline/text (see the three canvas classes below)."""
    k = 1.6 if big else 1.0
    c.rect(0, 0, w, h, BG)
    head_h = int(70 * k)
    # ---- header: title, SYNC/ASYNC badge, phase, node chips
    c.text(16, 12, "Chetna Phantom Hand - live", TEXT, int(20 * k))
    cond = snap["condition"]
    badge, col = ("SYNC", SYNC_C) if cond == "sync" else ("ASYNC", ASYNC_C) if cond == "async" else ("--", NA_C)
    bw = int(150 * k)
    c.rect(w - bw - 16, 8, w - 16, head_h - 8, col)
    c.text(w - bw - 16 + int(14 * k), int(14 * k), badge, (10, 14, 20), int(30 * k))
    sub = f"phase: {snap['phase'] or 'n/a'}" if cond or snap["phase"] else f"condition: n/a ({snap['hub_state']})"
    c.text(16, int(40 * k), sub, DIM, int(14 * k))
    for i, (name, ok) in enumerate((("Node A  haptic+IMU", snap["a_ok"]), ("Node B  EMG", snap["b_ok"]))):
        x = int(w * 0.40) + i * int(230 * k)
        c.rect(x, int(40 * k), x + int(14 * k), int(40 * k) + int(14 * k), SYNC_C if ok else IMPACT_C)
        c.text(x + int(22 * k), int(38 * k), f"{name} {'OK' if ok else 'OFFLINE'}", TEXT if ok else IMPACT_C, int(14 * k))

    # ---- two stacked plots
    margin_l, margin_r = int(70 * k), 16
    gap = int(24 * k)
    ph = (h - head_h - gap * 3) // 2
    plots = [("EMG envelope (raw ADC)", snap["emg"], EMG_C, "emg"), ("|accel| (m/s2)", snap["acc"], ACC_C, "acc")]
    for i, (title, series, color, kind) in enumerate(plots):
        y0 = head_h + gap + i * (ph + gap)
        y1 = y0 + ph
        x0, x1 = margin_l, w - margin_r
        c.rect(x0, y0, x1, y1, PANEL)
        vals = [v for _, v in series]
        if kind == "emg":
            lo, hi = nice_range(vals + [EMG_BASELINE_HINT], pad=0.12, min_span=60.0)
        else:
            lo, hi = nice_range(vals + [9.81], pad=0.12, min_span=0.6)
        ws = snap["window_s"]

        def px(t: float) -> float:
            return x0 + (t + ws) / ws * (x1 - x0)

        def py(v: float) -> float:
            return y1 - (v - lo) / (hi - lo) * (y1 - y0)

        for s in range(0, int(ws) + 1, 2):                       # vertical grid every 2 s
            gx = px(-s)
            c.line(gx, y0, gx, y1, GRID, 1)
            c.text(gx - 8, y1 + 2, f"-{s}s" if s else "now", DIM, int(11 * k))
        for j in range(5):                                       # 5 horizontal grid lines + labels
            gv = lo + (hi - lo) * j / 4.0
            c.line(x0, py(gv), x1, py(gv), GRID, 1)
            c.text(6, py(gv) - 6, f"{gv:.0f}" if kind == "emg" else f"{gv:.1f}", DIM, int(11 * k))
        if kind == "emg" and vals:                               # median baseline
            base = sorted(vals)[len(vals) // 2]
            c.line(x0, py(base), x1, py(base), DIM, 1)
            c.text(x0 + 6, py(base) - 14, f"baseline {base:.0f}", DIM, int(11 * k))
        if series:
            c.polyline([(px(t), py(v)) for t, v in series], color, 2 if not big else 3)
        else:
            c.text(x0 + 12, y0 + ph // 2 - 8, "no data", NA_C, int(16 * k))
        c.text(x0 + 8, y0 + 4, title, TEXT, int(13 * k))
        marks = snap["bursts"] if kind == "emg" else snap["impacts"]
        for m in marks:
            t = m[0] if isinstance(m, tuple) else m
            c.line(px(t), y0, px(t), y1, BURST_C if kind == "emg" else IMPACT_C, 2)
            c.text(px(t) + 4, y0 + 4 + int(16 * k), "emg_burst" if kind == "emg" else "threat_impact",
                   BURST_C if kind == "emg" else IMPACT_C, int(11 * k))
        if kind == "emg":                                        # impacts also shown on the EMG plot (the flinch story)
            for t in snap["impacts"]:
                c.line(px(t), y0, px(t), y1, IMPACT_C, 2)


# =============================================================================== canvases
class RasterCanvas:
    """Pure-python RGB raster -> PNG. No text (no font available without Pillow); `text` is a no-op."""
    name = "raster"

    def __init__(self, w: int, h: int):
        self.w, self.h = w, h
        self.buf = bytearray(BG * (w * h))

    def rect(self, x0: float, y0: float, x1: float, y1: float, col: Tuple[int, int, int]) -> None:
        xa, xb = max(0, int(min(x0, x1))), min(self.w, int(max(x0, x1)))
        ya, yb = max(0, int(min(y0, y1))), min(self.h, int(max(y0, y1)))
        if xb <= xa:
            return
        row = bytes(col) * (xb - xa)
        for y in range(ya, yb):
            o = (y * self.w + xa) * 3
            self.buf[o:o + len(row)] = row

    def _px(self, x: int, y: int, col: Tuple[int, int, int]) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            o = (y * self.w + x) * 3
            self.buf[o:o + 3] = bytes(col)

    def line(self, x0: float, y0: float, x1: float, y1: float, col: Tuple[int, int, int], width: int = 1) -> None:
        x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
        dx, dy = abs(x1 - x0), -abs(y1 - y0)
        sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
        err = dx + dy
        r = width // 2
        while True:
            for ox in range(-r, r + (width % 2)):
                for oy in range(-r, r + (width % 2)):
                    self._px(x0 + ox, y0 + oy, col)
            if x0 == x1 and y0 == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def polyline(self, pts: List[Tuple[float, float]], col: Tuple[int, int, int], width: int = 2) -> None:
        for (xa, ya), (xb, yb) in zip(pts, pts[1:]):
            self.line(xa, ya, xb, yb, col, width)

    def text(self, x: float, y: float, s: str, col: Tuple[int, int, int], size: int = 12) -> None:
        pass

    def png_bytes(self) -> bytes:
        raw = b"".join(b"\x00" + bytes(self.buf[y * self.w * 3:(y + 1) * self.w * 3]) for y in range(self.h))

        def chunk(tag: bytes, data: bytes) -> bytes:
            return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

        return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


class PilCanvas:
    name = "pil"

    def __init__(self, w: int, h: int):
        from PIL import Image, ImageDraw, ImageFont
        self.w, self.h = w, h
        self.img = Image.new("RGB", (w, h), BG)
        self.d = ImageDraw.Draw(self.img)
        self._ImageFont = ImageFont
        self._fonts: Dict[int, Any] = {}

    def _font(self, size: int) -> Any:
        if size not in self._fonts:
            try:
                self._fonts[size] = self._ImageFont.load_default(size=size)
            except TypeError:                                    # old Pillow: fixed-size bitmap font
                self._fonts[size] = self._ImageFont.load_default()
        return self._fonts[size]

    def rect(self, x0: float, y0: float, x1: float, y1: float, col: Tuple[int, int, int]) -> None:
        self.d.rectangle([x0, y0, x1, y1], fill=col)

    def line(self, x0: float, y0: float, x1: float, y1: float, col: Tuple[int, int, int], width: int = 1) -> None:
        self.d.line([(x0, y0), (x1, y1)], fill=col, width=width)

    def polyline(self, pts: List[Tuple[float, float]], col: Tuple[int, int, int], width: int = 2) -> None:
        if len(pts) > 1:
            self.d.line(pts, fill=col, width=width, joint="curve")

    def text(self, x: float, y: float, s: str, col: Tuple[int, int, int], size: int = 12) -> None:
        self.d.text((x, y), s, fill=col, font=self._font(size))

    def png_bytes(self) -> bytes:
        import io
        b = io.BytesIO()
        self.img.save(b, "PNG")
        return b.getvalue()


def render_png(snap: Dict[str, Any], path: str, w: int = 1280, h: int = 720, prefer_pil: bool = True) -> str:
    """Write one frame as a PNG. Returns the backend used ('pil' or 'raster')."""
    canvas: Any
    try:
        if not prefer_pil:
            raise ImportError
        canvas = PilCanvas(w, h)
    except ImportError:
        canvas = RasterCanvas(w, h)
    draw_scene(canvas, snap, w, h, big=w >= 1800)
    with open(path, "wb") as f:
        f.write(canvas.png_bytes())
    return canvas.name


class TkCanvas:
    name = "tk"

    def __init__(self, cv: Any):
        self.cv = cv

    @staticmethod
    def _hex(c: Tuple[int, int, int]) -> str:
        return "#%02x%02x%02x" % c

    def rect(self, x0: float, y0: float, x1: float, y1: float, col: Tuple[int, int, int]) -> None:
        self.cv.create_rectangle(x0, y0, x1, y1, fill=self._hex(col), outline="")

    def line(self, x0: float, y0: float, x1: float, y1: float, col: Tuple[int, int, int], width: int = 1) -> None:
        self.cv.create_line(x0, y0, x1, y1, fill=self._hex(col), width=width)

    def polyline(self, pts: List[Tuple[float, float]], col: Tuple[int, int, int], width: int = 2) -> None:
        if len(pts) > 1:
            self.cv.create_line(*[v for p in pts for v in p], fill=self._hex(col), width=width)

    def text(self, x: float, y: float, s: str, col: Tuple[int, int, int], size: int = 12) -> None:
        self.cv.create_text(x, y, text=s, fill=self._hex(col), anchor="nw", font=("Segoe UI", -size))


def run_tk(data: LiveData, fullscreen: bool, duration: Optional[float]) -> int:
    import tkinter as tk
    root = tk.Tk()
    root.title("Chetna Phantom Hand - live")
    root.configure(bg="#10141c")
    if fullscreen:
        root.attributes("-fullscreen", True)
        root.bind("<Escape>", lambda _e: root.destroy())
    else:
        root.geometry("1280x720")
    cv = tk.Canvas(root, bg="#10141c", highlightthickness=0)
    cv.pack(fill="both", expand=True)
    t_end = None if duration is None else time.monotonic() + duration

    def frame() -> None:
        if t_end is not None and time.monotonic() > t_end:
            root.destroy()
            return
        cv.delete("all")
        w, h = max(cv.winfo_width(), 400), max(cv.winfo_height(), 300)
        draw_scene(TkCanvas(cv), data.snapshot(time.monotonic()), w, h, big=fullscreen or w >= 1800)
        root.after(50, frame)

    root.after(50, frame)
    root.mainloop()
    return 0


# =============================================================================== CLI
def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Phantom Hand laptop live plot (EMG envelope + |accel|, SYNC/ASYNC).")
    p.add_argument("--a-ip", help="Node A ip[:port] (default port 8790); omit to use discovery")
    p.add_argument("--b-ip", help="Node B ip[:port] (default port 8790); omit to use discovery")
    p.add_argument("--discovery-port", type=int, default=8791, help="UDP discovery port (0 = off)")
    p.add_argument("--hub", help="hub ip:port, polled for SYNC/ASYNC + phase (GET /opus/v1/live/last_status)")
    p.add_argument("--window", type=float, default=WINDOW_S, help="seconds shown")
    p.add_argument("--fullscreen", action="store_true", help="audience mode (Esc leaves)")
    p.add_argument("--save-png", help="headless: collect for --duration then write this PNG and exit")
    p.add_argument("--duration", type=float, default=None, help="seconds to run (required with --save-png; default 10)")
    p.add_argument("--snapshot-every", type=float, default=0.0, help="with --save-png: also write a frame every N s (name_NN.png)")
    p.add_argument("--size", default="1280x720", help="PNG size WxH")
    p.add_argument("--no-pil", action="store_true", help="force the pure-python PNG rasteriser")
    return p


def main(argv: Optional[List[str]] = None) -> int:
    a = build_parser().parse_args(argv)
    data = LiveData(a.window)
    feeds = Feeds(data, parse_hostport(a.a_ip), parse_hostport(a.b_ip), a.discovery_port, a.hub)
    feeds.start()
    try:
        if a.save_png:
            dur = a.duration if a.duration is not None else 10.0
            w, h = (int(x) for x in a.size.lower().split("x"))
            stop = threading.Event()
            written: List[str] = []
            if a.snapshot_every:
                # a frame every N seconds (png name gets _NN): pick the one you want from a live run
                n = 0
                t_end = time.monotonic() + dur
                while time.monotonic() < t_end:
                    stop.wait(min(a.snapshot_every, max(0.0, t_end - time.monotonic())))
                    n += 1
                    path = a.save_png.replace(".png", f"_{n:02d}.png")
                    render_png(data.snapshot(time.monotonic()), path, w, h, prefer_pil=not a.no_pil)
                    written.append(path)
            else:
                stop.wait(dur)                 # collection window: data arrives on the feed threads
            snap = data.snapshot(time.monotonic())
            backend = render_png(snap, a.save_png, w, h, prefer_pil=not a.no_pil)
            c = snap["counts"]
            print(f"live_plot: wrote {a.save_png} ({w}x{h}, backend {backend}){' + ' + str(len(written)) + ' snapshots' if written else ''}; "
                  f"Node A {'OK' if snap['a_ok'] else 'OFFLINE'}, "
                  f"Node B {'OK' if snap['b_ok'] else 'OFFLINE'}; {len(snap['emg'])} EMG / {len(snap['acc'])} accel samples in "
                  f"the window; {len(snap['bursts'])} emg_burst, {len(snap['impacts'])} threat_impact marks; "
                  f"condition={snap['condition']}; nodes found by discovery: {feeds.discovered or 'none'}; counts {c}")
            return 0 if (snap["a_ok"] or snap["b_ok"]) else 1
        return run_tk(data, a.fullscreen, a.duration)
    finally:
        feeds.stop()


if __name__ == "__main__":
    sys.exit(main())
