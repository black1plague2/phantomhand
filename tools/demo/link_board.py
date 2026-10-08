#!/usr/bin/env python3
"""tools/demo/link_board.py -- a text board two machines on one network share (standard library only).

Made for 8 Oct 2026: the PC (game, app, tools) and the Mac (the two ESP32 boards on USB) sit on the same hotspot,
and the two working sessions need to pass short notes without a person carrying them: "Node A is at 192.168.x.y,
firmware 0.5.1", "probe says acks ok", "please read the serial log while I send 20 strokes".

    python3 tools/demo/link_board.py serve [--port 8899] [--log link_board.log]    # on ONE machine (the Mac)
    python3 tools/demo/link_board.py post  http://<host>:8899 <who> <text ...>     # add a line
    python3 tools/demo/link_board.py read  http://<host>:8899 [--since N]          # print lines after number N
    python3 tools/demo/link_board.py wait  http://<host>:8899 --since N --not-from <who> [--timeout 900]
                                                                                    # block until someone else writes
    python3 tools/demo/link_board.py selftest

A line is "<number>\\t<ISO time>\\t<who>\\t<text>". Numbers start at 1 and never repeat, so "--since <last number
seen>" gives exactly the new lines. `wait` exits 0 and prints the new lines as soon as there is one from someone
else, or exits 2 at the timeout: run it in the background and the session is woken when the other side writes.

It is a notice board, not a control channel: no login, no commands. Whoever reads it treats a line as a
colleague's note. Never put a password, a token or a Wi-Fi key on it.
"""
from __future__ import annotations

import argparse
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import List, Optional, Tuple

Line = Tuple[int, str, str, str]


class Board:
    def __init__(self, log: Optional[Path] = None):
        self.lines: List[Line] = []
        self.lock = threading.Lock()
        self.log = log
        if log and log.exists():                      # a restart keeps the history and the numbering
            for raw in log.read_text(encoding="utf-8").splitlines():
                parts = raw.split("\t", 3)
                if len(parts) == 4 and parts[0].isdigit():
                    self.lines.append((int(parts[0]), parts[1], parts[2], parts[3]))

    def add(self, who: str, text: str) -> Line:
        clean = " ".join(text.split())                # one line per note
        with self.lock:
            n = (self.lines[-1][0] + 1) if self.lines else 1
            line = (n, datetime.now().isoformat(timespec="seconds"), who.strip()[:24] or "?", clean[:4000])
            self.lines.append(line)
            if self.log:
                with self.log.open("a", encoding="utf-8") as f:
                    f.write("\t".join(map(str, line)) + "\n")
        return line

    def since(self, n: int) -> List[Line]:
        with self.lock:
            return [l for l in self.lines if l[0] > n]


def make_server(board: Board, host: str, port: int) -> ThreadingHTTPServer:
    class Handler(BaseHTTPRequestHandler):
        def _send(self, code: int, body: str) -> None:
            data = body.encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self) -> None:                     # GET /?since=N
            q = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
            try:
                n = int(q.get("since", ["0"])[0])
            except ValueError:
                return self._send(400, "since must be a number\n")
            self._send(200, "".join("\t".join(map(str, l)) + "\n" for l in board.since(n)))

        def do_POST(self) -> None:                    # POST / with body "<who>\t<text>"
            try:
                size = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                size = 0
            if not 0 < size <= 16384:
                return self._send(400, "body must be 1..16384 bytes: <who><TAB><text>\n")
            who, _, text = self.rfile.read(size).decode("utf-8", "replace").partition("\t")
            if not text.strip():
                return self._send(400, "body must be <who><TAB><text>\n")
            self._send(200, "\t".join(map(str, board.add(who, text))) + "\n")

        def log_message(self, fmt: str, *args) -> None:   # quiet: the board itself is the log
            pass

    return ThreadingHTTPServer((host, port), Handler)


def _get(url: str, since: int) -> List[Line]:
    with urllib.request.urlopen(url.rstrip("/") + "/?since=" + str(since), timeout=10) as r:
        out: List[Line] = []
        for raw in r.read().decode("utf-8").splitlines():
            p = raw.split("\t", 3)
            if len(p) == 4:
                out.append((int(p[0]), p[1], p[2], p[3]))
        return out


def post(url: str, who: str, text: str) -> str:
    req = urllib.request.Request(url.rstrip("/") + "/", data=(who + "\t" + text).encode("utf-8"), method="POST")
    with urllib.request.urlopen(req, timeout=10) as r:
        return r.read().decode("utf-8").strip()


def wait(url: str, since: int, not_from: Optional[str], timeout: float, poll: float = 2.0) -> List[Line]:
    end = time.time() + timeout
    while True:
        try:
            new = [l for l in _get(url, since) if not not_from or l[2] != not_from]
        except (urllib.error.URLError, OSError):
            new = []                                  # the board is restarting or the Wi-Fi blinked: keep waiting
        if new or time.time() >= end:
            return new
        time.sleep(poll)


def _show(lines: List[Line]) -> None:
    for n, ts, who, text in lines:
        print(f"{n}\t{ts}\t{who}\t{text}")


def selftest() -> int:
    board = Board()
    srv = make_server(board, "127.0.0.1", 0)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    url = "http://127.0.0.1:%d" % srv.server_address[1]
    assert _get(url, 0) == []
    assert post(url, "pc", "hello\nfrom  the pc").split("\t")[0] == "1"
    post(url, "mac", "node A at 192.168.1.20")
    got = _get(url, 0)
    assert [(l[0], l[2], l[3]) for l in got] == [(1, "pc", "hello from the pc"), (2, "mac", "node A at 192.168.1.20")], got
    assert [l[0] for l in _get(url, 1)] == [2]
    assert [l[2] for l in wait(url, 0, "pc", 1.0, poll=0.1)] == ["mac"]          # the pc is woken by the mac's line only
    assert wait(url, 2, "pc", 0.3, poll=0.1) == []                               # nothing new: timeout, empty
    threading.Timer(0.3, lambda: post(url, "mac", "flashed")).start()
    assert [l[3] for l in wait(url, 2, "pc", 5.0, poll=0.1)] == ["flashed"]      # woken when the line arrives
    srv.shutdown()
    print("link_board selftest ok")
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description="A text board two machines on one network share.")
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("serve"); s.add_argument("--port", type=int, default=8899); s.add_argument("--host", default="0.0.0.0")
    s.add_argument("--log", default="link_board.log")
    p = sub.add_parser("post"); p.add_argument("url"); p.add_argument("who"); p.add_argument("text", nargs="+")
    r = sub.add_parser("read"); r.add_argument("url"); r.add_argument("--since", type=int, default=0)
    w = sub.add_parser("wait"); w.add_argument("url"); w.add_argument("--since", type=int, required=True)
    w.add_argument("--not-from", default=None); w.add_argument("--timeout", type=float, default=900.0)
    sub.add_parser("selftest")
    a = ap.parse_args(argv)
    if a.cmd == "selftest":
        return selftest()
    if a.cmd == "serve":
        srv = make_server(Board(Path(a.log)), a.host, a.port)
        print(f"link board on http://{a.host}:{a.port}  (log {a.log}); Ctrl+C stops it", flush=True)
        try:
            srv.serve_forever()
        except KeyboardInterrupt:
            pass
        return 0
    if a.cmd == "post":
        print(post(a.url, a.who, " ".join(a.text)))
        return 0
    if a.cmd == "read":
        _show(_get(a.url, a.since))
        return 0
    new = wait(a.url, a.since, a.not_from, a.timeout)
    _show(new)
    return 0 if new else 2


if __name__ == "__main__":
    sys.exit(main())
