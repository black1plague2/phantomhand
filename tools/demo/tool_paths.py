"""Where this PC keeps the tools the demo harness runs: adb, dart (Flutter) and the Unity editor.

For each tool, in this order: an environment variable, then PATH, then the known install places of THIS PC, and last
the path the repo used to hardcode (so the PC it was written on behaves as before). Nothing here requires the tool to
exist: Flutter may still be installing, so a missing tool resolves to its expected place and the caller decides what a
missing file means.

    adb    OPUS_ADB (full path), ANDROID_HOME / ANDROID_SDK_ROOT (<root>\\platform-tools\\adb.exe), PATH, the Android SDK
           under %LOCALAPPDATA%, the adb bundled with any Unity Hub editor
    dart   OPUS_DART (full path), FLUTTER_ROOT (<root>\\bin\\dart.bat), PATH, H:\\flutter\\bin
    Unity  OPUS_UNITY_EXE (full path), the Unity Hub editor of the version game/ProjectSettings pins (no PATH lookup: a
           `Unity.EXE` on PATH can be the Unity CLI, not the editor)
"""
from __future__ import annotations

import glob
import os
import shutil
from pathlib import Path
from typing import Callable, List, Mapping, Optional, Sequence, Tuple

REPO_ROOT = Path(__file__).resolve().parents[2]

# the values the repo hardcoded before (another user's PC); still the last resort
ADB_OLD = r"C:\Users\GARV BANSAL\AppData\Local\Android\sdk\platform-tools\adb.exe"
DART_OLD = "C:/flutter/bin/dart.bat"
UNITY_OLD = r"C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe"


def resolve(env_paths: Sequence[str], env_roots: Sequence[Tuple[str, str]], names: Sequence[str],
            known: Sequence[str], old: str, *, env: Optional[Mapping[str, str]] = None,
            which: Callable[[str], Optional[str]] = shutil.which,
            exists: Callable[[str], bool] = os.path.exists) -> Path:
    """First hit of: a variable in `env_paths` (taken as given, even if the file is missing: an explicit choice),
    `<root>\\<rel>` for each (variable, rel) in `env_roots`, `names` on PATH, each of `known`; else `old`."""
    env = os.environ if env is None else env
    for var in env_paths:
        v = (env.get(var) or "").strip().strip('"')
        if v:
            return Path(v)
    for var, rel in env_roots:
        root = (env.get(var) or "").strip().strip('"')
        if root and exists(os.path.join(root, rel)):
            return Path(root) / rel
    for name in names:
        found = which(name)
        if found:
            return Path(found)
    for k in known:
        if exists(k):
            return Path(k)
    return Path(old)


def find_adb(env: Optional[Mapping[str, str]] = None, glob_fn: Callable[[str], List[str]] = glob.glob, **kw) -> Path:
    e = os.environ if env is None else env
    known = []
    if e.get("LOCALAPPDATA"):
        known.append(os.path.join(e["LOCALAPPDATA"], "Android", "Sdk", "platform-tools", "adb.exe"))
    pf = e.get("ProgramFiles", r"C:\Program Files")
    known += sorted(glob_fn(os.path.join(pf, "Unity", "Hub", "Editor", "*", "Editor", "Data", "PlaybackEngines",
                                         "AndroidPlayer", "SDK", "platform-tools", "adb.exe")), reverse=True)
    return resolve(["OPUS_ADB"], [("ANDROID_HOME", r"platform-tools\adb.exe"),
                                  ("ANDROID_SDK_ROOT", r"platform-tools\adb.exe")], ["adb"], known, ADB_OLD, env=e, **kw)


def find_dart(env: Optional[Mapping[str, str]] = None, **kw) -> Path:
    return resolve(["OPUS_DART"], [("FLUTTER_ROOT", r"bin\dart.bat")], ["dart"], [r"H:\flutter\bin\dart.bat"],
                   DART_OLD, env=env, **kw)


def unity_version(project_dir: Path) -> Optional[str]:
    """m_EditorVersion of a Unity project (game/ProjectSettings/ProjectVersion.txt), None if unreadable."""
    try:
        for line in (project_dir / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8").splitlines():
            if line.startswith("m_EditorVersion:"):
                return line.split(":", 1)[1].strip() or None
    except OSError:
        pass
    return None


def find_unity(project_dir: Optional[Path] = None, env: Optional[Mapping[str, str]] = None, **kw) -> Path:
    """Only the editor version the project pins is considered (another version would offer to upgrade the project).
    PATH is deliberately NOT searched: on this PC `Unity.EXE` on PATH is the new Unity CLI ("unity" 1.0.0.0), not the editor."""
    e = os.environ if env is None else env
    ver = unity_version(project_dir or REPO_ROOT / "game")
    pf = e.get("ProgramFiles", r"C:\Program Files")
    known = [os.path.join(pf, "Unity", "Hub", "Editor", ver, "Editor", "Unity.exe")] if ver else []
    return resolve(["OPUS_UNITY_EXE"], [], [], known, UNITY_OLD, env=e, **kw)
