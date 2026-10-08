"""Mirror a headset's log and session files to this PC over Wi-Fi, and its adb logcat while it is on USB.

    .venv\\Scripts\\python.exe tools\\demo\\quest_collect.py [out_dir] [--quest IP[:PORT]] [--hub IP[:PORT]] [--seconds N] [--once] [--no-adb] [--quiet]

The Quest app (game/Assets/Shell/Runtime/DeviceDiagnostics.cs) writes every Unity log line to a file and serves that file and
its session folders, read-only, on TCP 8796; every 2 s it says where it is with a UDP datagram to port 8796. This tool finds
the headset (that datagram, --quest, the Wi-Fi address adb reports, or a scan of this PC's /24 networks), then once a second
copies what is new into <out_dir>/<device>/ (logs/*.log, opus_sessions/<id>/*) and prints the log as it grows. With a headset
on USB and debugging allowed it also saves the whole `adb logcat` to <out_dir>/adb/logcat_<serial>.txt and, at the end, pulls
the app's data folder a second time over the cable. The Unity editor in play mode on this PC answers like a headset too
(device editor-...); it is left alone unless --editor is given. It only ever sends HTTP GETs to a headset and nothing to any
other host.
Default out_dir: sim/out/quest_logs/<time>/. Stop with Ctrl+C: one last pass, then a summary (warnings, errors, sessions).
"""
import argparse
import http.client
import json
import os
import socket
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

import tool_paths

PORT = 8796
REPO = Path(__file__).resolve().parents[2]
APPEND_ONLY = (".log", ".ndjson")          # these only grow: fetch from the size we have


def http_get(addr, target, timeout=5.0):
    """One GET; (status, body). Raises OSError / http.client.HTTPException when the headset does not answer in full."""
    c = http.client.HTTPConnection(addr[0], addr[1], timeout=timeout)
    try:
        c.request("GET", target)
        r = c.getresponse()
        return r.status, r.read()
    finally:
        c.close()


def safe_path(dev_dir: Path, rel: str):
    """Where a listed file lands, or None: the path comes from the network and must stay inside the device's folder."""
    if not rel or rel.startswith(("/", "\\")) or ":" in rel or "\0" in rel:
        return None
    base = dev_dir.resolve()
    p = (base / rel).resolve()
    return p if base in p.parents else None


class Device:
    def __init__(self, addr):
        self.addr, self.id, self.info = addr, None, {}
        self.since = 0                      # ask only for files changed after this time of the headset's clock
        self.seen = {}                      # path -> (size, mtime_ms) of what was mirrored
        self.tail = b""                     # the unfinished last line of the log, kept until its end arrives
        self.files = self.bytes = 0
        self.first = self.last = None       # first and last contact, PC time
        self.offset_ms = None               # headset clock minus PC clock
        self.lost = False


def mirror_once(dev: Device, out_dir: Path, echo=None, accept=None) -> bool:
    """One pass over one headset: the index, then every listed file that is new or grew. False when it did not answer, or when
    `accept(device id)` says no (nothing is fetched then). `echo(text)` gets each new complete line of the running log."""
    try:
        status, body = http_get(dev.addr, "/?since=%d" % dev.since if dev.since else "/")
        if status != 200:
            return False
        index = json.loads(body.decode("utf-8", "replace"))
    except (OSError, http.client.HTTPException, ValueError):
        return False
    if not index.get("opus_diag"):
        return False
    now = time.time()
    dev.id = str(index.get("device") or "unknown").replace("/", "_").replace("\\", "_").replace("..", "_")
    if accept and not accept(dev.id):
        return False
    dev.info = {k: index.get(k) for k in ("device", "boot", "app", "log")}
    dev.first = dev.first or now
    dev.last = now
    dev.offset_ms = int(index.get("now_ms", 0) - now * 1000)
    dev_dir = out_dir / dev.id
    all_ok = True
    for f in index.get("files") or []:
        rel, size, mtime = str(f.get("path", "")), int(f.get("size", 0)), int(f.get("mtime_ms", 0))
        local = safe_path(dev_dir, rel)
        if local is None:
            print("  refused a listed path outside the device folder: %r" % rel[:120], flush=True)
            continue
        have = local.stat().st_size if local.exists() else 0
        grows = rel.endswith(APPEND_ONLY)
        if grows and size == have and local.exists():
            continue
        if not grows and dev.seen.get(rel) == (size, mtime) and local.exists():
            continue
        offset = have if grows and size > have else 0
        try:
            status, data = http_get(dev.addr, "/f/" + rel.replace(" ", "%20") + ("?offset=%d" % offset if offset else ""), timeout=20.0)
        except (OSError, http.client.HTTPException):
            all_ok = False
            continue
        if status != 200:                   # 503: the game is rewriting it; 404: gone again. Next pass.
            all_ok = all_ok and status == 404
            continue
        local.parent.mkdir(parents=True, exist_ok=True)
        if offset:
            with open(local, "ab") as fh:
                fh.write(data)
        else:
            part = local.with_name(local.name + ".part")
            part.write_bytes(data)
            os.replace(part, local)
        dev.seen[rel] = (size, mtime)
        dev.files += 0 if offset else 1
        dev.bytes += len(data)
        if echo and rel == index.get("log"):
            if not offset:
                dev.tail = b""
            lines = (dev.tail + data).split(b"\n")
            dev.tail = lines.pop()
            for line in lines:
                echo(line.decode("utf-8", "replace"))
    if all_ok:                              # after a failed file keep the old time, so the next index lists it again
        dev.since = max(0, int(index.get("now_ms", 0)) - 3000)
    return True


def write_state(dev: Device, out_dir: Path):
    iso = lambda t: datetime.fromtimestamp(t, timezone.utc).isoformat(timespec="seconds") if t else None
    d = dict(dev.info, address="%s:%d" % dev.addr, first_seen=iso(dev.first), last_seen=iso(dev.last),
             clock_offset_ms=dev.offset_ms, files=dev.files, bytes=dev.bytes)
    (out_dir / dev.id).mkdir(parents=True, exist_ok=True)
    (out_dir / dev.id / "_collector.json").write_text(json.dumps(d, indent=1), encoding="utf-8")


def summary(dev_dir: Path) -> str:
    """Warnings, errors and exceptions per log (a record starts with `HH:MM:SS.mmm L `), and the sessions with their file counts."""
    out = []
    for log in sorted((dev_dir / "logs").glob("*.log")) if (dev_dir / "logs").exists() else []:
        n = {"W": 0, "E": 0, "X": 0}
        lines = 0
        for line in log.read_text(encoding="utf-8", errors="replace").splitlines():
            lines += 1
            if len(line) > 14 and line[12] == " " and line[14] == " " and line[13] in n and line[2] == ":":
                n[line[13]] += 1
        out.append("  %s: %d lines, %d warnings, %d errors, %d exceptions" % (log.name, lines, n["W"], n["E"], n["X"]))
    sessions = dev_dir / "opus_sessions"
    for s in sorted(sessions.iterdir()) if sessions.exists() else []:
        if s.is_dir():
            out.append("  session %s: %d files" % (s.name, sum(1 for f in s.iterdir() if f.is_file())))
    return "\n".join(out)


# ---- finding headsets ---------------------------------------------------------------------------------------------------

def local_ipv4s():
    ips = set()
    try:
        ips.update(socket.gethostbyname_ex(socket.gethostname())[2])
    except OSError:
        pass
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.connect(("192.0.2.1", 9))     # no packet is sent: this only picks the route
            ips.add(s.getsockname()[0])
    except OSError:
        pass
    return sorted(i for i in ips if not i.startswith(("127.", "169.254.")))


def scan(found: list):
    """Every address of this PC's /24 networks with TCP 8796 open goes to `found` (the mirror pass tells whether it is a headset)."""
    def probe(ip):
        try:
            with socket.create_connection((ip, PORT), timeout=0.4):
                found.append((ip, PORT))
        except OSError:
            pass
    threads = []
    for mine in local_ipv4s():
        net = mine.rsplit(".", 1)[0]
        for i in range(1, 255):
            t = threading.Thread(target=probe, args=("%s.%d" % (net, i),), daemon=True)
            t.start()
            threads.append(t)
    for t in threads:
        t.join(1.0)


def hello_socket():
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind(("", PORT))
        s.setblocking(False)
        return s
    except OSError as e:
        print("cannot listen for the headset's datagram on UDP %d (%s): --quest, adb or the scan still find it" % (PORT, e), flush=True)
        return None


def hellos(sock):
    """Addresses of the headsets whose datagram arrived since the last call."""
    out = []
    while sock:
        try:
            data, sender = sock.recvfrom(2048)
        except (BlockingIOError, OSError):
            break
        try:
            j = json.loads(data.decode("utf-8", "replace"))
            if j.get("opus_diag"):
                out.append((sender[0], int(j.get("port") or PORT)))     # the sender's address, not the one it claims
        except ValueError:
            pass
    return out


# ---- adb ------------------------------------------------------------------------------------------------------------------

def adb(exe, *args, timeout=15):
    try:
        return subprocess.run([exe, *args], capture_output=True, text=True, errors="replace", timeout=timeout).stdout
    except (OSError, subprocess.SubprocessError):
        return ""


def adb_headsets(exe):
    """Serials of the attached, authorised devices made by Oculus / Meta."""
    out = []
    for line in adb(exe, "devices").splitlines()[1:]:
        parts = line.split()
        if len(parts) == 2 and parts[1] == "device" and adb(exe, "-s", parts[0], "shell", "getprop", "ro.product.manufacturer").strip().lower() in ("oculus", "meta"):
            out.append(parts[0])
    return out


def adb_attach(exe, serial, out_dir: Path, package: str):
    """Starts the logcat capture of one headset and notes what it is; the process."""
    d = out_dir / "adb"
    d.mkdir(parents=True, exist_ok=True)
    wlan = adb(exe, "-s", serial, "shell", "ip", "-4", "addr", "show", "wlan0")
    pkg = [l.strip() for l in adb(exe, "-s", serial, "shell", "dumpsys", "package", package).splitlines()
           if any(k in l for k in ("versionName", "firstInstallTime", "lastUpdateTime"))]
    (d / ("device_%s.txt" % serial)).write_text(
        "model: %s\nbuild: %s\npackage %s: %s\n%s\n" % (
            adb(exe, "-s", serial, "shell", "getprop", "ro.product.model").strip(),
            adb(exe, "-s", serial, "shell", "getprop", "ro.build.fingerprint").strip(),
            package, "; ".join(pkg) or "not installed", wlan), encoding="utf-8")
    log = open(d / ("logcat_%s.txt" % serial), "ab")
    proc = subprocess.Popen([exe, "-s", serial, "logcat", "-v", "threadtime"], stdout=log, stderr=subprocess.STDOUT)
    print("adb: %s logcat -> %s" % (serial, log.name), flush=True)
    return proc


def adb_wifi_address(exe, serial):
    """The headset's own Wi-Fi address (it changes when the headset is moved to another network), or None."""
    wlan = adb(exe, "-s", serial, "shell", "ip", "-4", "addr", "show", "wlan0")
    return next((w.split()[1].split("/")[0] for w in wlan.splitlines() if w.strip().startswith("inet ")), None)


# ---- the operator app's side ----------------------------------------------------------------------------------------------

def watch_hub(hub: str, out_dir: Path, stop: threading.Event):
    """What the operator app's hub says about the headset, once a second (its two read-only routes, LIVE_PROTOCOL "Viewer"):
    a line in hub_view.log whenever the number of connected headsets, the run state, the phase or a node's link changes."""
    host, _, port = hub.partition(":")
    addr, last, path = (host, int(port or 8787)), None, out_dir / "hub_view.log"
    while not stop.is_set():
        view = {"headsets": None, "state": None, "phase": None, "sleeve": None, "sensor": None, "events": None}
        try:
            status, body = http_get(addr, "/opus/v1/health", timeout=2.0)
            view["headsets"] = json.loads(body.decode("utf-8", "replace")).get("connected_headsets") if status == 200 else "HTTP %d" % status
            status, body = http_get(addr, "/opus/v1/live/last_status", timeout=2.0)
            j = json.loads(body.decode("utf-8", "replace")) if status == 200 else {}
            st = j.get("status") or {}
            gs = st.get("game_state") or {}
            nodes = gs.get("nodes") or {}
            view.update(state=st.get("state"), phase=gs.get("phase"), sleeve=(nodes.get("haptic") or {}).get("connected"),
                        sensor=(nodes.get("bio") or {}).get("connected"), events=len(j.get("events") or []))
        except (OSError, http.client.HTTPException, ValueError):
            view["headsets"] = "no answer"
        key = {k: v for k, v in view.items() if k != "events"}
        if key != last:
            last = key
            line = "%s hub %s: headsets %s, state %s, phase %s, sleeve %s, sensor %s, events kept %s" % (
                time.strftime("%H:%M:%S"), hub, view["headsets"], view["state"], view["phase"], view["sleeve"], view["sensor"], view["events"])
            with open(path, "a", encoding="utf-8") as fh:
                print(line, file=fh)
            print(line, flush=True)
        stop.wait(1.0)


# ---- main -----------------------------------------------------------------------------------------------------------------

def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("out_dir", nargs="?", default=str(REPO / "sim" / "out" / "quest_logs" / time.strftime("%Y%m%d_%H%M%S")))
    ap.add_argument("--quest", action="append", default=[], help="IP or IP:PORT of a headset (repeatable)")
    ap.add_argument("--seconds", type=float, default=0, help="stop after this long (default: until Ctrl+C)")
    ap.add_argument("--once", action="store_true", help="look for headsets for up to 8 s, one pass each, exit")
    ap.add_argument("--no-adb", action="store_true")
    ap.add_argument("--quiet", action="store_true", help="do not print the log lines (they are in the mirrored file)")
    ap.add_argument("--editor", action="store_true", help="also mirror a Unity editor in play mode (its data folder holds every session it ever recorded)")
    ap.add_argument("--hub", help="IP[:PORT] of the operator app's hub: also record what it says about the headset (hub_view.log)")
    ap.add_argument("--package", default="com.DefaultCompany.OPUS")
    a = ap.parse_args(argv)
    out_dir = Path(a.out_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    print("mirroring into %s" % out_dir, flush=True)

    named = set()                           # addresses that are asked again and again: --quest, and what adb reports
    for q in a.quest:
        host, _, port = q.partition(":")
        named.add((host, int(port or PORT)))
    by_cable = {}                           # serial -> the Wi-Fi address adb last reported
    candidates = set()
    devices = {}                            # device id -> Device
    sock = hello_socket()
    stop = threading.Event()
    if a.hub:
        threading.Thread(target=watch_hub, args=(a.hub, out_dir, stop), daemon=True).start()
    exe = None if a.no_adb else str(tool_paths.find_adb())
    logcats = {}                            # serial -> process
    t0 = time.time()
    next_scan, next_adb, next_status = t0 + 6, t0, t0 + 10
    scanned = []
    try:
        while True:
            now = time.time()
            candidates.update(hellos(sock))
            candidates.update(scanned)
            scanned.clear()
            if exe and now >= next_adb:
                next_adb = now + 5
                for serial in adb_headsets(exe):
                    if serial not in logcats or logcats[serial].poll() is not None:
                        logcats[serial] = adb_attach(exe, serial, out_dir, a.package)
                    ip = adb_wifi_address(exe, serial)
                    if ip != by_cable.get(serial):
                        by_cable[serial] = ip
                        print("adb: %s is %s" % (serial, "on Wi-Fi at %s" % ip if ip else "not on Wi-Fi"), flush=True)
            candidates.update(named)
            candidates.update((ip, PORT) for ip in by_cable.values() if ip)
            if not devices and now >= next_scan:
                next_scan = now + 15
                threading.Thread(target=scan, args=(scanned,), daemon=True).start()
            known = {d.addr for d in devices.values()}
            for addr in sorted(candidates - known):
                probe = Device(addr)
                if mirror_once(probe, out_dir, None if a.quiet else lambda s, p=probe: print("[%s] %s" % (p.id, s), flush=True),
                               accept=lambda device: a.editor or not device.startswith("editor-")):
                    old = devices.get(probe.id)
                    if old is not None and now - (old.last or 0) < 5:
                        continue            # the same headset under a second address (a hint that was wrong): keep the one that answers
                    devices[probe.id] = probe
                    print("headset %s at %s:%d (app %s, clock %+d ms against this PC), log %s" % (
                        probe.id, addr[0], addr[1], probe.info.get("app"), probe.offset_ms, probe.info.get("log")), flush=True)
            candidates.clear()              # what did not answer is asked again only when it is named, announced or scanned again
            for dev in list(devices.values()):
                if dev.addr in known:
                    ok = mirror_once(dev, out_dir, None if a.quiet else lambda s, d=dev: print("[%s] %s" % (d.id, s), flush=True))
                    if ok == dev.lost:
                        dev.lost = not ok
                        print("headset %s: %s" % (dev.id, "contact again" if ok else "lost contact (taken off, asleep, or off the Wi-Fi)"), flush=True)
            if now >= next_status:
                next_status = now + 10
                for dev in devices.values():
                    write_state(dev, out_dir)
                    if a.quiet:
                        print("%s  %s at %s: %d files, %d bytes, last contact %.0f s ago" % (
                            time.strftime("%H:%M:%S"), dev.id, dev.addr[0], dev.files, dev.bytes, now - (dev.last or now)), flush=True)
                if not devices:
                    print("%s  no headset found yet (is the app running, and on this Wi-Fi? --quest IP names it)" % time.strftime("%H:%M:%S"), flush=True)
            if a.once and (devices or now - t0 > 8):
                break
            if a.seconds and now - t0 >= a.seconds:
                break
            time.sleep(1.0)
    except KeyboardInterrupt:
        pass
    stop.set()
    for dev in devices.values():
        mirror_once(dev, out_dir)
        write_state(dev, out_dir)
    for serial, proc in logcats.items():
        proc.terminate()
        pulled = out_dir / "adb" / ("files_%s" % serial)
        r = adb(exe, "-s", serial, "pull", "/sdcard/Android/data/%s/files" % a.package, str(pulled), timeout=120)
        print("adb: second copy over the cable -> %s (%s)" % (pulled, (r.strip().splitlines() or ["nothing pulled"])[-1][:120]), flush=True)
    for dev in devices.values():
        print("\n%s (%s), %d files, %d bytes:\n%s" % (dev.id, dev.addr[0], dev.files, dev.bytes, summary(out_dir / dev.id)), flush=True)
    if not devices:
        print("no headset was mirrored")
    return 0 if devices else 1


if __name__ == "__main__":
    sys.exit(main())
