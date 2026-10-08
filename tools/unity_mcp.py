"""Minimal MCP (streamable HTTP) client for the Meta XR Unity agent bridge (the OPEN editor).

Usage:
  python tools/unity_mcp.py list                      # tool names + one-line descriptions
  python tools/unity_mcp.py schema <tool>             # full input schema of one tool
  python tools/unity_mcp.py call <tool> '<json args>' # call a tool, print the result text
  python tools/unity_mcp.py call <tool> @args.json    # same, args read from a file
  python tools/unity_mcp.py test EditMode             # run all EditMode tests, print a summary
  python tools/unity_mcp.py test PlayMode "PhantomHand|PH_" [--assembly X] [--timeout 1200] [--out f.json]
                                                      # PlayMode ALWAYS with this filter: never play Orchard Reach
  python tools/unity_mcp.py recompile                 # after editing C#: refresh + compile + print errors
  python tools/unity_mcp.py register                  # register the bridge with Claude Code (user scope)

The bearer token is read from game/Assets/Resources/DevAgentSettings.asset (written by the
editor, local only) and is never printed.

Why `test` and `call` "pump": the bridge's TestRunnerTools (and a few other tools) hand their work
to the main thread with EditorApplication.delayCall, which does NOT fire while the editor window
is unfocused, so a run never starts and the call hangs. The reflection tool goes through the
SynchronizationContext instead, which does run unfocused, so we use it to flush the pending
delay calls (EditorApplication.Internal_CallDelayFunctions) while we wait.
"""
import json
import os
import pathlib
import re
import sys
import threading
import time
import urllib.error
import urllib.request

URL = os.environ.get("UNITY_MCP_URL", "http://127.0.0.1:48736/mcpbridge/")
REPO = pathlib.Path(__file__).resolve().parents[1]
ASSET = REPO / "game" / "Assets" / "Resources" / "DevAgentSettings.asset"
PUMP_ARGS = {"method": "InvokeStaticMethodFromJson", "typeName": "UnityEditor.EditorApplication",
             "methodName": "Internal_CallDelayFunctions", "arguments": "[]"}


class McpError(Exception):
    pass


def token() -> str:
    m = re.search(r"^\s*accessToken:\s*(\S+)\s*$", ASSET.read_text(encoding="utf-8"), re.M)
    if not m:
        raise McpError("no accessToken in DevAgentSettings.asset (open Meta XR AI tools in the editor)")
    return m.group(1)


class Client:
    def __init__(self, timeout: float = 600.0):
        self.timeout = timeout
        self.session = None
        self.tok = token()
        self.next_id = 0

    def _post(self, payload: dict):
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
            "Authorization": "Bearer " + self.tok,
        }
        if self.session:
            headers["Mcp-Session-Id"] = self.session
        req = urllib.request.Request(URL, data=json.dumps(payload).encode("utf-8"), headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as r:
                sid = r.headers.get("Mcp-Session-Id")
                if sid:
                    self.session = sid
                ctype = r.headers.get("Content-Type", "")
                body = r.read().decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            raise McpError(f"HTTP {e.code}: {e.read().decode('utf-8', 'replace')[:2000]}")
        except OSError as e:  # refused / reset / timed out: editor closed or mid domain reload
            raise McpError(f"bridge unreachable at {URL}: {e}")
        if "id" not in payload:
            return None
        if "text/event-stream" in ctype:
            for line in body.splitlines():
                if line.startswith("data:"):
                    msg = json.loads(line[5:].strip())
                    if msg.get("id") == payload["id"]:
                        return msg
            raise McpError("no matching SSE message: " + body[:2000])
        return json.loads(body) if body.strip() else None

    def rpc(self, method: str, params: dict | None = None):
        self.next_id += 1
        msg = self._post({"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params or {}})
        if msg is None:
            raise McpError(f"{method}: empty response")
        if "error" in msg:
            raise McpError(f"{method}: {json.dumps(msg['error'])[:4000]}")
        return msg["result"]

    def start(self):
        info = self.rpc("initialize", {
            "protocolVersion": "2025-03-26",
            "capabilities": {},
            "clientInfo": {"name": "ph-unity-mcp", "version": "0.2"},
        })
        self._post({"jsonrpc": "2.0", "method": "notifications/initialized"})
        return info

    def tool(self, name: str, args: dict) -> str:
        """Call a tool, return its text (non-text parts are summarised)."""
        res = self.rpc("tools/call", {"name": name, "arguments": args})
        parts = [p["text"] if p.get("type") == "text" else f"[{p.get('type')} content, {len(json.dumps(p))} bytes]"
                 for p in res.get("content", [])]
        text = "\n".join(parts)
        if res.get("isError"):
            raise McpError(text or "tool error")
        return text


def connect(timeout: float = 600.0) -> Client:
    c = Client(timeout)
    c.start()
    return c


def pumped(name: str, args: dict, timeout: float = 600.0) -> str:
    """Run one tool call on a worker thread while flushing the editor's delay calls (see module doc)."""
    box = {}

    def work():
        try:
            box["text"] = connect(timeout).tool(name, args)
        except BaseException as e:  # noqa: BLE001 - reported to the caller below
            box["err"] = e

    t = threading.Thread(target=work, daemon=True)
    t.start()
    pump, deadline = None, time.time() + timeout
    while t.is_alive() and time.time() < deadline:
        t.join(0.5)
        if not t.is_alive():
            break
        try:
            pump = pump or connect(30)
            pump.tool("IReflectionService", PUMP_ARGS)
        except McpError:
            pump = None  # domain reload in progress: reconnect on the next round
    if t.is_alive():
        raise McpError(f"{name}: no answer in {timeout:.0f} s")
    if "err" in box:
        raise box["err"] if isinstance(box["err"], McpError) else McpError(repr(box["err"]))
    return box["text"]


def _value(text: str):
    d = json.loads(text)
    return d.get("return value", d) if isinstance(d, dict) else d


def run_tests(platform: str, name_filter: str | None, assembly: str | None, timeout: float, out: pathlib.Path) -> int:
    args = {"method": "RunFiltered", "testPlatform": platform, "testFilter": name_filter or ".*"}
    if assembly:
        args["assemblyFilter"] = assembly
    started = _value(pumped("TestRunnerTools", args, 300))
    if not isinstance(started, dict) or started.get("error") or not started.get("runId"):
        print("could not start:", json.dumps(started)[:600])
        return 2
    run_id, queued = started["runId"], started.get("testsQueued")
    print(f"started {platform} run {run_id}: {queued} tests queued", flush=True)

    c, res, deadline, last = None, None, time.time() + timeout, None
    while time.time() < deadline:
        try:  # PlayMode reloads the domain: the bridge drops for a few seconds, so reconnect and go on
            c = c or connect(30)
            c.tool("IReflectionService", PUMP_ARGS)
            r = _value(c.tool("TestRunnerTools", {"method": "GetResults"}))
        except McpError:
            c = None
            time.sleep(1.0)
            continue
        key = (r.get("passed"), r.get("failed"), r.get("skipped"))
        if key != last:
            last = key
        if r.get("runId") == run_id and not r.get("isRunning"):
            res = r
            break
        time.sleep(0.5)
    if res is None:
        print(f"TIMEOUT after {timeout:.0f} s; last counts (passed, failed, skipped) = {last}")
        return 3

    uniq = {}
    for t in res.get("results", []):  # the bridge records every result twice: keep one per test
        uniq.setdefault(t["fullName"], t)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"runId": run_id, "platform": platform, "results": list(uniq.values())}, indent=1),
                   encoding="utf-8")
    state = lambda t: (t.get("resultState") or "").lower()  # noqa: E731
    failed = [t for t in uniq.values() if state(t).startswith(("fail", "error"))]
    passed = sum(1 for t in uniq.values() if state(t).startswith("pass"))
    skipped = len(uniq) - passed - len(failed)
    print(f"DONE {platform} passed={passed} failed={len(failed)} skipped={skipped} total={len(uniq)} "
          f"queued={queued} duration={res.get('duration', 0):.1f}s results={out}")
    for t in failed:
        msg = " | ".join((t.get("message") or "").split("\n"))[:400]
        frames = [ln.strip() for ln in (t.get("stackTrace") or "").splitlines() if "Assets" in ln or "Packages" in ln]
        print(f"FAIL {t['fullName']}\n     {msg}\n     {frames[0][:220] if frames else ''}")
    return 1 if failed or len(uniq) == 0 else 0


def recompile(timeout: float = 600.0) -> int:
    """ForceRecompile, wait, print the compile errors. Exit 1 on errors, 3 on timeout.

    The wait is SHORT status polls on fresh connections. Never hold one request (WaitForCompilation) open
    across the domain reload: the old domain's listener then still owns ports 48735/48736 when the new
    domain binds, the bridge does not come back, and only focusing the editor window recovers it
    (2026-10-08). "Done" = 5 clean polls in a row; an unreachable bridge or "compiling" resets the count,
    so a run where nothing changed on disk ends after about 5 s.
    """
    try:
        pumped("CompilationTools", {"method": "ForceRecompile"}, 60)
    except McpError:
        pass  # the reload can cut this very call
    deadline, clean, status = time.time() + timeout, 0, None
    while clean < 5:
        if time.time() > deadline:
            print(f"TIMEOUT after {timeout:.0f} s; last status = {status}")
            return 3
        time.sleep(1.0)
        try:
            c = connect(10)
            c.tool("IReflectionService", PUMP_ARGS)
            status = _value(c.tool("CompilationTools", {"method": "GetCompilationStatus"}))
        except McpError:
            clean = 0  # domain reload in progress
            continue
        clean = clean + 1 if status.get("status") != "compiling" else 0
    print("status:", json.dumps(status))
    if status.get("errorCount"):
        print(connect(30).tool("CompilationTools", {"method": "GetCompilationErrors"})[:6000])
        return 1
    return 0


def register():
    """Register the bridge with Claude Code at USER scope.

    The editor's own Register button writes a local-scope entry under the git root
    (H:/.../phantomhand) but then looks for it under the Unity project dir (.../game), so it
    reports "Not Registered" and sessions opened in any other folder never get the tools.
    Its check falls back to the user-scope list, so user scope fixes both. Re-run after
    "Regenerate" in the editor's AI Tools settings (the token changes).
    """
    import shutil
    import subprocess
    exe = shutil.which("claude")
    if not exe:
        raise McpError("claude CLI not found on PATH")
    name = "meta-xr-unity-runtime"
    subprocess.run([exe, "mcp", "remove", name, "-s", "user"], capture_output=True)
    r = subprocess.run([exe, "mcp", "add", "-s", "user", "--transport", "http", name, URL,
                        "--header", "Authorization:Bearer " + token()], capture_output=True, text=True)
    print((r.stdout + r.stderr).replace(token(), "<redacted>").strip())
    return r.returncode


def main(argv) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2
    cmd = argv[1]
    if cmd == "register":
        return register()
    if cmd == "recompile":
        return recompile()
    if cmd == "test":
        opts = {"--assembly": None, "--timeout": "1200", "--out": None}
        rest = argv[2:]
        for k in list(opts):
            if k in rest:
                i = rest.index(k)
                opts[k] = rest[i + 1]
                del rest[i:i + 2]
        platform = rest[0] if rest else "EditMode"
        if platform == "PlayMode" and (len(rest) < 2 or rest[1] in ("", ".*")):
            print('refused: PlayMode needs a name filter, e.g. "PhantomHand|PH_" '
                  "(an unfiltered run plays Orchard Reach, which is out of scope)")
            return 2
        out = pathlib.Path(opts["--out"]) if opts["--out"] else REPO / "game" / "Logs" / f"ph_tests_{platform}.json"
        return run_tests(platform, rest[1] if len(rest) > 1 else None, opts["--assembly"], float(opts["--timeout"]), out)
    if cmd == "call":
        raw = argv[3] if len(argv) > 3 else "{}"
        if raw.startswith("@"):
            raw = pathlib.Path(raw[1:]).read_text(encoding="utf-8")
        print(pumped(argv[2], json.loads(raw)))
        return 0
    c = connect()
    if cmd == "info":
        print(json.dumps(c.rpc("initialize", {"protocolVersion": "2025-03-26", "capabilities": {},
                                              "clientInfo": {"name": "ph-unity-mcp", "version": "0.2"}}), indent=1)[:6000])
    elif cmd == "list":
        for t in c.rpc("tools/list")["tools"]:
            desc = (t.get("description") or "").strip().splitlines()
            print(f"{t['name']}: {desc[0][:150] if desc else ''}")
    elif cmd == "schema":
        for t in c.rpc("tools/list")["tools"]:
            if t["name"] == argv[2]:
                print(json.dumps(t, indent=1))
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    try:
        sys.exit(main(sys.argv))
    except McpError as e:
        sys.exit(f"unity_mcp: {e}")
