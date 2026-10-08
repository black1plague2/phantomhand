"""Host tests for the Node B DSP. They compile and run the REAL firmware header (bio_dsp.h) with g++, and
cross-check the band-pass design against scipy using the coefficients parsed out of filter_coeffs.h.

Run: python -m pytest firmware/bio_node/test -q
"""
import re
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
import pytest
from scipy.signal import sosfreqz

HERE = Path(__file__).resolve().parent
NODE = HERE.parent
sys.path.insert(0, str(NODE))
import filter_design  # noqa: E402

FS = 1000


@pytest.fixture(scope="session")
def harness(tmp_path_factory):
    gxx = shutil.which("g++")
    assert gxx, "g++ is required for the host DSP test (MinGW g++ on Windows)"
    exe = tmp_path_factory.mktemp("bio") / ("dsp_harness.exe" if sys.platform == "win32" else "dsp_harness")
    r = subprocess.run([gxx, "-O2", "-std=c++17", "-Wall", "-Wextra", str(HERE / "dsp_harness.cpp"), "-o", str(exe)],
                       capture_output=True, text=True)
    assert r.returncode == 0, r.stderr
    assert r.stderr == "", "warnings in firmware DSP: " + r.stderr
    return exe


def run(harness, mode, samples, tmp_path):
    f = tmp_path / "in.txt"
    f.write_text("\n".join(f"{x:.4f}" for x in samples), encoding="utf-8")
    out = subprocess.run([str(harness), mode, str(f)], capture_output=True, text=True, check=True).stdout
    return out.strip().splitlines()


def parse_sos(name):
    text = (NODE / "filter_coeffs.h").read_text(encoding="utf-8")
    body = re.search(name + r"\[[^\]]*\]\[5\] = \{(.*?)\};", text, re.S).group(1)
    rows = re.findall(r"\{([^}]*)\}", body)
    sos = []
    for r in rows:
        b0, b1, b2, a1, a2 = [float(x.rstrip("f")) for x in r.split(",")]
        sos.append([b0, b1, b2, 1.0, a1, a2])
    return np.array(sos)


def test_header_is_current():
    assert subprocess.run([sys.executable, str(NODE / "filter_design.py"), "--check"]).returncode == 0, \
        "filter_coeffs.h is stale: run python firmware/bio_node/filter_design.py"


def test_bandpass_gain_design():
    bp = parse_sos("BIO_BP_SOS")
    f = np.array([20, 75, 100, 150, 300])
    _, h = sosfreqz(bp, worN=f, fs=FS)
    g = np.abs(h)
    print("design gain dB:", dict(zip(f.tolist(), (20 * np.log10(g)).round(2).tolist())))
    assert abs(20 * np.log10(g[1])) < 3.5 and abs(20 * np.log10(g[3])) < 3.5   # edges ~ -3 dB
    assert g[2] > 0.9                                                         # mid-band ~ 1
    assert g[0] < 0.1                                                         # 20 Hz  < -20 dB
    assert g[4] < 0.2                                                         # 300 Hz < -14 dB


@pytest.mark.parametrize("f,lo,hi", [(20, 0, 0.1), (75, 0.6, 0.85), (100, 0.9, 1.05), (150, 0.6, 0.85), (300, 0, 0.2)])
def test_bandpass_gain_firmware(harness, tmp_path, f, lo, hi):
    n = 3000
    t = np.arange(n) / FS
    x = 2048 + 500 * np.sin(2 * np.pi * f * t)
    y = np.array([float(v) for v in run(harness, "bp", x, tmp_path)])
    amp = (y[1500:].max() - y[1500:].min()) / 2
    gain = amp / 500
    print(f"firmware gain at {f} Hz: {gain:.3f} ({20*np.log10(gain):.1f} dB)")
    assert lo <= gain <= hi


def burst_signal(rng, n_s=6, burst=None, noise=3.0, seed_dc=2048):
    """ADC counts: DC + broadband noise, optional (start_s, dur_s, amp) EMG-like burst (band-limited noise)."""
    n = int(n_s * FS)
    x = seed_dc + rng.normal(0, noise, n)
    if burst:
        s, d, amp = burst
        i0, i1 = int(s * FS), int((s + d) * FS)
        b = rng.normal(0, 1, i1 - i0)
        from scipy.signal import butter, sosfilt
        b = sosfilt(butter(2, [74.5, 149.5], "bandpass", fs=FS, output="sos"), b)
        b = b / b.std() * amp
        x[i0:i1] += b
    return x


def test_envelope_step_90pct_under_40ms(harness, tmp_path):
    # A 110 Hz in-band burst switching on at t = 2.000 s. Measured from onset to 90 % of the settled envelope.
    n = 4000
    t = np.arange(n) / FS
    x = 2048 + np.where(t >= 2.0, 400 * np.sin(2 * np.pi * 110 * t), 0.0)
    lines = run(harness, "env", x, tmp_path)
    E = np.array([[float(v) for v in l.split()[1:]] for l in lines if l[0] == "E"])
    ts, env = E[:, 0], E[:, 1]
    final = env[(ts > 3200) & (ts < 3900)].mean()
    after = (ts >= 2000)
    t90 = ts[after][np.argmax(env[after] >= 0.9 * final)] - 2000
    print(f"settled envelope {final:.1f}; 90% reached {t90:.0f} ms after onset")
    assert t90 < 40
    assert 0.5 * 400 * 2 / np.pi < final < 400 * 2 / np.pi * 1.1  # ~ mean(|A sin|) = 2A/pi after BP gain ~1


def test_burst_fires_on_flinch(harness, tmp_path):
    rng = np.random.default_rng(7)
    x = burst_signal(rng, burst=(4.0, 0.25, 60.0))
    lines = run(harness, "env", x, tmp_path)
    bursts = [l.split() for l in lines if l[0] == "B"]
    print("bursts:", bursts)
    assert len(bursts) == 1
    onset, peak, base = float(bursts[0][1]), float(bursts[0][2]), float(bursts[0][3])
    assert 4000 <= onset <= 4100          # detected within ~100 ms of true onset (filter + 30 ms rule)
    assert peak > 5 * base
    assert base > 0


@pytest.mark.parametrize("seed", [1, 2, 3, 4, 5])
def test_burst_silent_on_noise(harness, tmp_path, seed):
    rng = np.random.default_rng(seed)
    x = burst_signal(rng, n_s=60)                                  # 60 s of noise, no flinch
    lines = run(harness, "env", x, tmp_path)
    assert [l for l in lines if l[0] == "B"] == []


def test_burst_silent_on_slow_drift_and_dc_step(harness, tmp_path):
    rng = np.random.default_rng(11)
    x = burst_signal(rng, n_s=30)
    x += 80 * np.sin(2 * np.pi * 0.5 * np.arange(len(x)) / FS)     # 0.5 Hz baseline wander (out of band)
    x[15000:] += 40                                               # 40-count DC step (~13 sigma of the noise); a 120-count step fires a burst (known limit, see log)
    lines = run(harness, "env", x, tmp_path)
    b = [l for l in lines if l[0] == "B"]
    assert len(b) == 0, b


def test_two_flinches_refractory_and_second_detected(harness, tmp_path):
    rng = np.random.default_rng(3)
    x = burst_signal(rng, n_s=10, burst=(4.0, 0.2, 60.0))
    x2 = burst_signal(rng, n_s=10, burst=(7.0, 0.2, 60.0))
    x[6500:] = x2[6500:]
    lines = run(harness, "env", x, tmp_path)
    bursts = [l.split() for l in lines if l[0] == "B"]
    assert len(bursts) == 2, bursts
    assert abs(float(bursts[1][1]) - 7000) < 150


# ----------------------------------------------------------------------------------------------------------
# Protocol / subscriber / syntax checks (bio_proto.h, bio_node.ino)
# ----------------------------------------------------------------------------------------------------------
import json  # noqa: E402


@pytest.fixture(scope="session")
def proto_lines(tmp_path_factory):
    gxx = shutil.which("g++")
    assert gxx
    exe = tmp_path_factory.mktemp("proto") / ("proto_harness.exe" if sys.platform == "win32" else "proto_harness")
    r = subprocess.run([gxx, "-O2", "-std=c++17", "-Wall", "-Wextra", str(HERE / "proto_harness.cpp"), "-o", str(exe)],
                       capture_output=True, text=True)
    assert r.returncode == 0 and r.stderr == "", r.stderr
    run_ = subprocess.run([str(exe)], capture_output=True, text=True)
    assert run_.returncode == 0, "C++ self-checks failed: " + run_.stderr
    return {l.split("\t", 1)[0]: json.loads(l.split("\t", 1)[1]) for l in run_.stdout.strip().splitlines()}


def test_chunk_matches_prd_9_3(proto_lines):
    c = proto_lines["chunk"]
    assert c["type"] == "sensor_chunk" and c["device_id"] == "CHETNA_BIO_001" and c["device_kind"] == "bio"
    assert c["sample_rate_hz"] == 100 and c["unit"] == "raw_adc" and c["status"] in ("ok", "saturated", "flat")
    assert isinstance(c["timestamp_ms"], int) and len(c["emg_envelope"]) == 10


def test_discovery_matches_prd_9_1(proto_lines):
    d = proto_lines["discovery"]
    assert d == {"type": "device_discovery", "device_id": "CHETNA_BIO_001", "device_kind": "bio",
                 "firmware_version": "0.5.0", "command_port": 8790, "status": "available", "motor_count": 0,
                 "timestamp_ms": 142050}


def test_burst_status_ack_shapes(proto_lines):
    b = proto_lines["burst"]
    assert set(b) == {"type", "device_id", "timestamp_ms", "peak", "baseline_rms"} and b["type"] == "emg_burst"
    s = proto_lines["status_first"]
    assert s["battery_pct"] is None and s["subscribers"] == 2 and s["samples_dropped"] == 7 and "safety_notice" in s
    assert s["safety_notice"].startswith("SAFETY: power bank only")
    assert "safety_notice" not in proto_lines["status"] and proto_lines["status"]["signal"] == "flat"
    a = proto_lines["ack_reject"]
    assert a["status"] == "rejected" and a["error_code"] == "NOT_A_HAPTIC_NODE" and a["ok"] is False
    assert a["cue_id"] == a["ack_id"] == "stroke_017"
    assert proto_lines["ack_ok"]["ok"] is True


def test_ino_has_no_output_pins_and_safety_banner():
    src = (NODE / "bio_node.ino").read_text(encoding="utf-8")
    code = "\n".join(l.split("//")[0] for l in src.splitlines())
    for forbidden in ("pinMode", "digitalWrite", "ledcAttach", "ledcWrite", "analogWrite", "dacWrite"):
        assert forbidden not in code, f"Node B must not drive any output: found {forbidden}"
    assert "SAFETY: power bank only. Never connect to a laptop or charger while electrodes are on a person." in src
    assert "WIFI_SSID = \"YOUR_HOTSPOT_NAME\"" in src and "WIFI_PASS = \"YOUR_HOTSPOT_PASSWORD\"" in src


def test_ino_syntax_with_host_stubs():
    """-fsyntax-only on bio_node.ino with hand-written Arduino stubs. NOT a substitute for arduino-cli compile."""
    gxx = shutil.which("g++")
    r = subprocess.run([gxx, "-std=gnu++17", "-fsyntax-only", "-Wall", "-Wextra", f"-I{HERE / 'stubs'}", f"-I{NODE}",
                        "-x", "c++", str(HERE / "ino_syntax_check.cpp")], capture_output=True, text=True)
    assert r.returncode == 0 and r.stderr == "", r.stderr
