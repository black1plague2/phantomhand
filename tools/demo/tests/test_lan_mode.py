"""--lan / --dialect plumbing of run_pipeline.py -> phantom_pipeline.py. Pure builders and argument parsing only:
nothing is started, no port is bound, Unity is never launched.

    sim/live/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_lan_mode.py -q
"""
from __future__ import annotations

import argparse
import asyncio
import sys
from pathlib import Path

import pytest

from conftest import REPO_ROOT

sys.path.insert(0, str(REPO_ROOT))
import phantom_pipeline as PP  # noqa: E402
import run_pipeline as RP  # noqa: E402

PORTS = {"haptic": 39790, "discovery": 39791, "bio": 39792, "control": 39793}
OLD_TWIN_ARGV = [sys.executable, "-m", "sim.sleeve.twin", "--kind", "both", "--port-offset", "31000", "--seed", "1",
                 "--log", "twin.jsonl", "--no-stdin"]


def parse(*argv):
    return RP.build_parser().parse_args(["--game", "phantom_hand", "--sim", "--no-unity", *argv])


def test_flags_default_off_and_parse():
    a = parse()
    assert a.lan is False and a.dialect == "reference"
    b = parse("--lan", "--dialect", "team")
    assert b.lan is True and b.dialect == "team"
    with pytest.raises(SystemExit):
        parse("--dialect", "bogus")
    assert RP.build_parser().parse_args([]).lan is False            # the OrchardReach default is untouched


def test_default_twin_command_is_the_old_one_flag_for_flag():
    a = parse()
    assert PP.twin_command("both", 31000, 1, Path("twin.jsonl")) == OLD_TWIN_ARGV
    assert PP.twin_command("both", 31000, 1, Path("twin.jsonl"), PP.twin_host(a), a.dialect) == OLD_TWIN_ARGV


def test_lan_and_dialect_add_exactly_their_flags():
    a = parse("--lan", "--dialect", "team")
    cmd = PP.twin_command("both", 31000, 1, Path("twin.jsonl"), PP.twin_host(a), a.dialect)
    assert cmd == OLD_TWIN_ARGV + ["--host", "0.0.0.0", "--dialect", "team"]
    only_lan = parse("--lan")
    assert PP.twin_command("both", 31000, 1, Path("twin.jsonl"), PP.twin_host(only_lan), only_lan.dialect) \
        == OLD_TWIN_ARGV + ["--host", "0.0.0.0"]


def test_hand_built_namespaces_without_the_new_flags_still_work():
    # test_phantom_pipeline builds argparse.Namespace by hand: the harness must read the new flags with getattr
    bare = argparse.Namespace(port_offset=14000, seed=2, discovery_port=8791, hardware=False)
    assert PP.twin_host(bare) is None
    assert PP.second_pc_command(bare).endswith("--seed 2")
    assert "LAN MODE" in PP.lan_banner(bare)


def test_unity_env_has_node_overrides_by_default_and_none_in_lan_mode():
    d = PP.unity_env("127.0.0.1", 5555, PORTS)
    assert d == {"OPUS_PH_HUB": "127.0.0.1:5555", "OPUS_PH_NODE_A": "127.0.0.1:39790",
                 "OPUS_PH_NODE_B": "127.0.0.1:39792", "OPUS_PH_DISCOVERY_PORT": "39791"}
    lan = PP.unity_env("127.0.0.1", 5555, PORTS, lan=True)
    assert lan == {"OPUS_PH_HUB": "127.0.0.1:5555", "OPUS_PH_DISCOVERY_PORT": "39791"}
    assert not [k for k in lan if k.startswith("OPUS_PH_NODE")]                    # real discovery
    assert PP.unity_env("10.0.0.2", 8787, {"discovery": 8791}, lan=True)["OPUS_PH_DISCOVERY_PORT"] == "8791"   # remote nodes


def test_second_pc_command_is_the_exact_line_for_this_run():
    assert PP.second_pc_command(parse("--lan")) == \
        "python sim\\sleeve\\twin.py --kind both --host 0.0.0.0 --port-offset 0 --seed 42"
    assert PP.second_pc_command(parse("--lan", "--seed", "7", "--dialect", "team")) == \
        "python sim\\sleeve\\twin.py --kind both --host 0.0.0.0 --port-offset 0 --seed 7 --dialect team"


def test_lan_banner_names_both_machines_and_the_matching_harness_command():
    sim = PP.lan_banner(parse("--lan", "--dialect", "team"))
    assert "THIS PC bound to 0.0.0.0" in sim and "39790/39791/39792" in sim
    assert PP.second_pc_command(parse("--lan", "--dialect", "team")) in sim
    assert "--hardware --discovery-port 8791 --lan --dialect team" in sim          # what to run here against the remote twin
    assert "UDP 8790 + 8792" in sim and "open_firewall.ps1" in sim and "127.0.0.1" in sim
    hw = PP.lan_banner(RP.build_parser().parse_args(["--game", "phantom_hand", "--hardware", "--lan"]))
    assert "no twin is started here" in hw and "UDP 8791" in hw


def test_run_phantom_prints_the_banner_only_with_lan(monkeypatch, capsys, tmp_path):
    async def fake_one_run(args, *, name, **kw):
        return {"name": name, "rows": [PP.L3.Row("x", "t", "o", True)]}

    monkeypatch.setattr(PP, "one_run", fake_one_run)
    for lan in (False, True):
        a = parse("--out", str(tmp_path / f"out_{lan}"), *(["--lan"] if lan else []))
        assert asyncio.run(PP.run_phantom(a)) == 0
        assert ("LAN MODE" in capsys.readouterr().out) is lan
