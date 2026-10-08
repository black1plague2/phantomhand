"""tools/demo/tool_paths.py: where adb, dart and Unity are found. Environment, PATH and the file system are injected,
so these tests do not depend on what is installed on the PC that runs them.

    sim/live/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_tool_paths.py -q
"""
from __future__ import annotations

import os
from pathlib import Path

import tool_paths as TP

J = os.path.join


def kw(existing=(), on_path=None):
    have = set(existing)
    return {"exists": lambda p: p in have, "which": lambda n: (on_path or {}).get(n)}


def test_adb_env_var_wins_even_when_the_file_is_missing():
    got = TP.find_adb(env={"OPUS_ADB": '"D:\\x\\adb.exe"', "LOCALAPPDATA": "C:\\L"}, glob_fn=lambda p: [],
                      **kw(existing=[J("C:\\L", "Android", "Sdk", "platform-tools", "adb.exe")], on_path={"adb": "C:\\p\\adb.EXE"}))
    assert got == Path("D:\\x\\adb.exe")                                   # an explicit choice is never second-guessed


def test_adb_order_sdk_root_then_path_then_local_sdk_then_unity_then_old_pc():
    sdk = J("D:\\sdk", "platform-tools\\adb.exe")
    local = J("C:\\Users\\me\\AppData\\Local", "Android", "Sdk", "platform-tools", "adb.exe")
    unity_a = "C:\\PF\\Unity\\Hub\\Editor\\6000.3.1f1\\Editor\\Data\\PlaybackEngines\\AndroidPlayer\\SDK\\platform-tools\\adb.exe"
    unity_b = unity_a.replace("6000.3.1f1", "6000.4.6f1")
    env = {"ANDROID_HOME": "D:\\sdk", "LOCALAPPDATA": "C:\\Users\\me\\AppData\\Local", "ProgramFiles": "C:\\PF"}
    g = lambda pat: [unity_a, unity_b]                                      # noqa: E731
    everything = [sdk, local, unity_a, unity_b]
    assert TP.find_adb(env=env, glob_fn=g, **kw(everything, {"adb": "C:\\p\\adb.exe"})) == Path(sdk)
    assert TP.find_adb(env={**env, "ANDROID_HOME": ""}, glob_fn=g, **kw(everything, {"adb": "C:\\p\\adb.exe"})) == Path("C:\\p\\adb.exe")
    assert TP.find_adb(env=env, glob_fn=g, **kw([local, unity_a, unity_b])) == Path(local)          # ANDROID_HOME has no adb: skipped
    assert TP.find_adb(env={**env, "LOCALAPPDATA": ""}, glob_fn=g, **kw([unity_a, unity_b])) == Path(unity_b)   # newest bundled one
    assert TP.find_adb(env={}, glob_fn=lambda p: [], **kw()) == Path(TP.ADB_OLD)                    # nothing found: the old value


def test_dart_order_env_flutter_root_path_known_place_then_old_pc():
    root_dart = J("D:\\fl", "bin\\dart.bat")
    assert TP.find_dart(env={"OPUS_DART": "E:\\d.bat"}, **kw([root_dart])) == Path("E:\\d.bat")
    assert TP.find_dart(env={"FLUTTER_ROOT": "D:\\fl"}, **kw([root_dart, "H:\\flutter\\bin\\dart.bat"],
                                                             {"dart": "C:\\p\\dart.exe"})) == Path(root_dart)
    assert TP.find_dart(env={"FLUTTER_ROOT": "D:\\fl"}, **kw([], {"dart": "C:\\p\\dart.exe"})) == Path("C:\\p\\dart.exe")
    assert TP.find_dart(env={}, **kw(["H:\\flutter\\bin\\dart.bat"])) == Path("H:\\flutter\\bin\\dart.bat")


def test_dart_never_requires_flutter_to_exist():
    got = TP.find_dart(env={}, **kw())                                      # not installed yet: no exception, the old value
    assert got == Path("C:/flutter/bin/dart.bat") and isinstance(got, Path)


def test_unity_version_is_read_from_the_project(tmp_path):
    (tmp_path / "ProjectSettings").mkdir()
    (tmp_path / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 6000.4.6f1\nm_EditorVersionWithRevision: 6000.4.6f1 (0b051c2e5d54)\n", encoding="utf-8")
    assert TP.unity_version(tmp_path) == "6000.4.6f1"
    assert TP.unity_version(tmp_path / "missing") is None


def test_unity_only_the_editor_version_the_project_pins(tmp_path):
    (tmp_path / "ProjectSettings").mkdir()
    (tmp_path / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.4.6f1\n", encoding="utf-8")
    pinned = J("C:\\PF", "Unity", "Hub", "Editor", "6000.4.6f1", "Editor", "Unity.exe")
    other = pinned.replace("6000.4.6f1", "6000.5.0f1")
    env = {"ProgramFiles": "C:\\PF"}
    assert TP.find_unity(tmp_path, env=env, **kw([pinned, other])) == Path(pinned)
    assert TP.find_unity(tmp_path, env=env, **kw([other])) == Path(TP.UNITY_OLD)     # another version would offer to upgrade the project
    assert TP.find_unity(tmp_path, env={**env, "OPUS_UNITY_EXE": "E:\\U\\Unity.exe"}, **kw([pinned])) == Path("E:\\U\\Unity.exe")
    assert TP.find_unity(tmp_path / "nope", env=env, **kw([pinned])) == Path(TP.UNITY_OLD)   # unreadable version: old value
    # PATH is not searched: a Unity.EXE there can be the Unity CLI (it is, on the PC this was written on), not the editor
    assert TP.find_unity(tmp_path, env=env, **kw([pinned], {"Unity": "C:\\u\\bin\\Unity.EXE"})) == Path(pinned)
    assert TP.find_unity(tmp_path, env=env, **kw([], {"Unity": "C:\\u\\bin\\Unity.EXE"})) == Path(TP.UNITY_OLD)


def test_the_harness_constants_come_from_the_resolver():
    import phantom_pipeline as PP
    import prove_live_to_phone as PL
    assert isinstance(PP.DART, Path) and isinstance(PP.UNITY_EXE, Path)
    assert PL.ADB_DEFAULT == str(TP.find_adb()) and isinstance(PL.ADB_DEFAULT, str)
