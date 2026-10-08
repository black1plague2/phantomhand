"""tools/demo/quest_collect.py against a fake headset on 127.0.0.1 (the routes of DeviceDiagnostics.cs): what is copied, from
where, and that nothing lands outside the device's folder.

    .venv/Scripts/python.exe -m pytest tools/demo/tests/test_quest_collect.py -q
"""
from __future__ import annotations

import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

import pytest

import quest_collect as QC

LOG = "logs/20261008T170102Z_ab12cd34.log"


class FakeHeadset:
    """Serves a folder the way the app does; `extra` adds index entries, `busy` paths answer 503, `asked` records the requests."""

    def __init__(self, root: Path):
        self.root, self.extra, self.busy, self.asked = root, [], set(), []
        fake = self

        class H(BaseHTTPRequestHandler):
            def log_message(self, *a):  # quiet
                pass

            def do_GET(self):
                u = urlparse(self.path)
                q = parse_qs(u.query)
                fake.asked.append(self.path)
                if u.path == "/":
                    since = int(q.get("since", ["0"])[0])
                    files = [{"path": p.relative_to(root).as_posix(), "size": p.stat().st_size, "mtime_ms": int(p.stat().st_mtime * 1000)}
                             for p in sorted(root.rglob("*")) if p.is_file()]
                    files = [f for f in files if f["mtime_ms"] > since] + fake.extra
                    body = json.dumps({"opus_diag": 1, "device": "quest-test0001", "boot": "ab12cd34", "app": "0.1.0",
                                       "now_ms": int(time.time() * 1000), "log": LOG, "files": files}).encode()
                    return self.reply(200, body)
                rel = unquote(u.path[3:]) if u.path.startswith("/f/") else None
                if rel in fake.busy:
                    return self.reply(503, b"busy")
                p = root / rel if rel else None
                if p is None or not p.is_file():
                    return self.reply(404, b"no")
                self.reply(200, p.read_bytes()[int(q.get("offset", ["0"])[0]):])

            def reply(self, status, body):
                self.send_response(status)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), H)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.addr = ("127.0.0.1", self.server.server_address[1])


@pytest.fixture
def headset(tmp_path):
    root = tmp_path / "headset"
    (root / "logs").mkdir(parents=True)
    (root / "opus_sessions" / "s1").mkdir(parents=True)
    (root / LOG).write_bytes(b"# header\n17:01:02.123 I first line\n17:01:02.500 W a warning\n")
    (root / "opus_sessions" / "s1" / "events.ndjson").write_bytes(b'{"n":1}\n')
    (root / "opus_sessions" / "s1" / "session.json").write_bytes(b'{"ended_at":null}')
    h = FakeHeadset(root)
    yield h
    h.server.shutdown()


def same(headset, out, rel):
    return (out / "quest-test0001" / rel).read_bytes() == (headset.root / rel).read_bytes()


def test_first_pass_copies_everything_and_prints_the_log(headset, tmp_path):
    out, dev, printed = tmp_path / "out", QC.Device(headset.addr), []
    assert QC.mirror_once(dev, out, printed.append)
    assert dev.id == "quest-test0001" and dev.files == 3
    for rel in (LOG, "opus_sessions/s1/events.ndjson", "opus_sessions/s1/session.json"):
        assert same(headset, out, rel), rel
    assert printed == ["# header", "17:01:02.123 I first line", "17:01:02.500 W a warning"]
    assert abs(dev.offset_ms) < 5000 and dev.since > 0


def test_a_grown_log_is_fetched_from_the_size_we_have(headset, tmp_path):
    out, dev, printed = tmp_path / "out", QC.Device(headset.addr), []
    QC.mirror_once(dev, out, printed.append)
    size = (headset.root / LOG).stat().st_size
    with open(headset.root / LOG, "ab") as f:
        f.write(b"17:01:03.000 E an error\n\tits stack\n17:01:04.000 I half a li")
    headset.asked.clear(); printed.clear()
    assert QC.mirror_once(dev, out, printed.append)
    assert "/f/%s?offset=%d" % (LOG, size) in headset.asked
    assert same(headset, out, LOG)
    assert printed == ["17:01:03.000 E an error", "\tits stack"]          # the unfinished line waits for its end
    with open(headset.root / LOG, "ab") as f:
        f.write(b"ne\n")
    printed.clear()
    QC.mirror_once(dev, out, printed.append)
    assert printed == ["17:01:04.000 I half a line"] and same(headset, out, LOG)


def test_a_rewritten_file_of_the_same_size_is_replaced(headset, tmp_path):
    out, dev = tmp_path / "out", QC.Device(headset.addr)
    QC.mirror_once(dev, out)
    p = headset.root / "opus_sessions" / "s1" / "session.json"
    p.write_bytes(b'{"ended_at":"now"}'[:len(p.read_bytes())].ljust(len(p.read_bytes()), b" "))
    os.utime(p, (time.time() + 30, time.time() + 30))
    dev.since = 0
    QC.mirror_once(dev, out)
    assert same(headset, out, "opus_sessions/s1/session.json")


def test_a_busy_file_is_not_lost(headset, tmp_path):
    out, dev = tmp_path / "out", QC.Device(headset.addr)
    headset.busy.add("opus_sessions/s1/session.json")
    assert QC.mirror_once(dev, out)
    assert dev.since == 0, "a failed file must keep the next index complete"
    assert not (out / "quest-test0001" / "opus_sessions" / "s1" / "session.json").exists()
    headset.busy.clear()
    QC.mirror_once(dev, out)
    assert same(headset, out, "opus_sessions/s1/session.json") and dev.since > 0


@pytest.mark.parametrize("bad", ["../evil.txt", "logs/../../evil.txt", "/abs/evil.txt", "C:\\evil.txt", "\\\\host\\share\\evil.txt"])
def test_a_listed_path_outside_the_device_folder_is_refused(headset, tmp_path, bad):
    out, dev = tmp_path / "out", QC.Device(headset.addr)
    headset.extra.append({"path": bad, "size": 4, "mtime_ms": int(time.time() * 1000)})
    assert QC.mirror_once(dev, out)
    assert not any("evil" in p.name for p in tmp_path.rglob("*")), "nothing may be written for " + bad
    assert not any("evil" in a for a in headset.asked), "and it is not even asked for"


def test_no_headset_is_false_not_a_crash(tmp_path):
    assert QC.mirror_once(QC.Device(("127.0.0.1", 9)), tmp_path) is False


def test_summary_counts_warnings_errors_and_sessions(headset, tmp_path):
    out, dev = tmp_path / "out", QC.Device(headset.addr)
    with open(headset.root / LOG, "ab") as f:
        f.write(b"17:01:03.000 E an error\n\t17:01:03.000 W not a record, a stack line\n17:01:05.000 X boom\n")
    QC.mirror_once(dev, out)
    text = QC.summary(out / dev.id)
    assert "6 lines, 1 warnings, 1 errors, 1 exceptions" in text
    assert "session s1: 2 files" in text
