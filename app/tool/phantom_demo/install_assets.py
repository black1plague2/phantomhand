"""Copies the recorded Phantom Hand session into the app's demo asset folder.

usage: install_assets.py <session_copy_dir> <trace.json> <app/assets/demo/phantom_hand>

- events.ndjson and metrics.json are copied byte for byte.
- session.json is copied byte for byte except `patient_ref`, which becomes the
  demo patient's id (the recording used the editor's default patient).
- the compact live trace is copied as live_trace.json.
"""
import hashlib
import os
import shutil
import sys

OLD = b'"patient_ref": "synthetic-longitudinal-9000"'
NEW = b'"patient_ref": "demo-phantom-hand"'


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()[:16]


def main():
    src, trace, dst = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(dst, exist_ok=True)
    for name in ("events.ndjson", "metrics.json"):
        shutil.copyfile(os.path.join(src, name), os.path.join(dst, name))
    with open(os.path.join(src, "session.json"), "rb") as f:
        raw = f.read()
    print("session.json: BOM %s, CRLF %s, %d bytes" % (raw.startswith(b"\xef\xbb\xbf"), b"\r\n" in raw, len(raw)))
    assert raw.count(OLD) == 1, "patient_ref not found exactly once"
    with open(os.path.join(dst, "session.json"), "wb") as f:
        f.write(raw.replace(OLD, NEW))
    shutil.copyfile(trace, os.path.join(dst, "live_trace.json"))
    total = 0
    for name in sorted(os.listdir(dst)):
        p = os.path.join(dst, name)
        total += os.path.getsize(p)
        same = ""
        s = os.path.join(src, name)
        if os.path.exists(s):
            same = "identical to the recording" if sha(s) == sha(p) else "DIFFERS from the recording"
        print("%-16s %7d bytes  sha256 %s  %s" % (name, os.path.getsize(p), sha(p), same))
    print("total %d bytes (%.1f KB)" % (total, total / 1024))


main()
