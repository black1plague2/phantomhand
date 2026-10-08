"""Phantom Hand embodiment metrics (N1): hand-made signals with known peaks/latencies, gating, missing nodes,
and the contracts fixture phantom_hand_min end to end."""
import json
import shutil
from pathlib import Path

import numpy as np
import pytest
from jsonschema import Draft202012Validator, FormatChecker

from opus_analytics.analyze import analyze_session, write_metrics
from opus_analytics.io_session import SessionData, load_session
from opus_analytics.metrics import embodiment as emb
from opus_analytics.__main__ import main as cli_main
from opus_analytics.schemas import find_contracts_dir

FIXTURE = find_contracts_dir() / "fixtures" / "sessions" / "phantom_hand_min"
IMPACT = 10_000.0


def ev(t, typ, trial=None, **data):
    return {"t_ms": t, "seq": 0, "block": 0, "trial": trial, "type": typ, "data": data}


def cond_events(trial, name, impact=IMPACT, extra=()):
    return [
        ev(impact - 5000, "phase_start", trial, phase="induction", condition=name),
        ev(impact, "threat_impact", trial, impact_ms=impact, impact_pos=[0, 0, 0], ok=True),
        *extra,
    ]


def emg_stream(onset_ms=None, rise_per_ms=10.0, top=1000.0, base=100.0, t_end=14000.0, artifacts=(), seed=1):
    t = np.arange(0.0, t_end, 10.0)
    rng = np.random.default_rng(seed)
    v = base + rng.normal(0, 0.5, len(t))
    if onset_ms is not None:
        v = np.where(t >= onset_ms, np.minimum(top, base + (t - onset_ms) * rise_per_ms), v)
    for a, b, h in artifacts:
        v = np.where((t >= a) & (t <= b), h, v)
    return {"device_id": "CHETNA_BIO_001", "rate_hz": 100.0, "t_ms": t, "value": v, "motor_excl": None}


def imu_stream(onset_ms=None, bump=3.0, t_end=14000.0, seed=2):
    t = np.arange(0.0, t_end, 10.0)
    rng = np.random.default_rng(seed)
    a = 9.81 + rng.normal(0, 0.01, len(t))
    if onset_ms is not None:
        a = np.where((t >= onset_ms) & (t < onset_ms + 200), a + bump * np.minimum(1.0, (t - onset_ms) / 40.0), a)
    z = np.zeros(len(t))
    return {"device_id": "SLEEVE_001", "rate_hz": 100.0, "t_ms": t, "ax": z.copy(), "ay": z.copy(), "az": a,
            "gx": z.copy(), "gy": z.copy(), "gz": z.copy()}


def wrist_joint(onset_ms=None, rate=72.0, v_peak=0.5, dur_ms=400.0, t_end=14000.0):
    dt = 1000.0 / rate
    t = np.arange(0.0, t_end, dt)
    x = np.zeros(len(t))
    if onset_ms is not None:
        s = np.clip((t - onset_ms) / dur_ms, 0, 1)
        # minimum-jerk displacement, peak speed 1.875*D/T -> choose D for v_peak
        D = v_peak * (dur_ms / 1000.0) / 1.875
        x = D * (10 * s ** 3 - 15 * s ** 4 + 6 * s ** 5)
    pos = np.stack([x, np.full(len(t), 1.0), np.zeros(len(t))], axis=1)
    return {"t_ms": t, "pos": pos, "conf": np.ones(len(t))}


def session(events, emg=None, imu=None, joints=None, rate=72.0):
    return SessionData(session_dir=Path("."), envelope={"session_id": "x"}, events=events,
                       joints=joints or {}, rate_hz=rate, emg=emg, imu=imu)


def run(events, **kw):
    return emb.compute_embodiment(events, session(events, **kw))


# ------------------------------------------------------------------ EMG
def test_emg_latency_and_peak_known():
    out = run(cond_events(0, "sync"), emg=emg_stream(onset_ms=IMPACT + 120))["sync"]
    lat, peak = out["flinch_emg_latency_ms"], out["flinch_emg_peak_x"]
    assert lat["quality"] == "ok" and abs(lat["value"] - 120.0) <= 5.0
    assert abs(peak["value"] - 10.0) / 10.0 < 0.10  # 1000 / baseline RMS 100


def test_emg_no_flinch_gives_missing_latency_but_peak():
    out = run(cond_events(0, "async"), emg=emg_stream(onset_ms=None))["async"]
    assert out["flinch_emg_latency_ms"]["quality"] == "missing"
    assert "no_flinch_detected" in out["flinch_emg_latency_ms"]["quality_reasons"]
    assert out["flinch_emg_peak_x"]["value"] == pytest.approx(1.0, abs=0.1)


def test_short_blip_under_30ms_is_not_a_flinch():
    s = emg_stream(onset_ms=None)
    t = s["t_ms"]
    s["value"] = np.where((t >= IMPACT + 100) & (t <= IMPACT + 120), 900.0, s["value"])
    out = run(cond_events(0, "sync"), emg=s)["sync"]
    assert out["flinch_emg_latency_ms"]["quality"] == "missing"


def test_motor_artifact_is_gated_and_counted():
    cue = ev(IMPACT + 50, "haptic_cue", 0, cue="stroke", motor=0, delivered=True, ack_latency_ms=10)
    s = emg_stream(onset_ms=IMPACT + 400, artifacts=[(IMPACT + 50, IMPACT + 300, 5000.0)])
    out = run(cond_events(0, "sync", extra=[cue]), emg=s)["sync"]
    assert abs(out["flinch_emg_latency_ms"]["value"] - 400.0) <= 5.0   # artefact ignored
    assert abs(out["flinch_emg_peak_x"]["value"] - 10.0) / 10.0 < 0.10
    assert out["emg_windows_excluded"]["value"] == 1


def test_undelivered_cue_does_not_gate():
    cue = ev(IMPACT + 50, "haptic_cue", 0, cue="stroke", motor=0, delivered=False, ack_latency_ms=None)
    s = emg_stream(onset_ms=None, artifacts=[(IMPACT + 50, IMPACT + 300, 5000.0)])
    out = run(cond_events(0, "sync", extra=[cue]), emg=s)["sync"]
    assert 0 <= out["flinch_emg_latency_ms"]["value"] <= 60.0   # the (step) artefact counts: not gated
    assert out["emg_windows_excluded"]["value"] == 0


def test_baseline_cue_excluded_from_baseline_rms():
    cue = ev(IMPACT - 1000, "haptic_cue", 0, cue="stroke", motor=0, delivered=True, ack_latency_ms=10)
    s = emg_stream(onset_ms=IMPACT + 200, artifacts=[(IMPACT - 1000, IMPACT - 750, 4000.0)])
    out = run(cond_events(0, "sync", extra=[cue]), emg=s)["sync"]
    assert abs(out["flinch_emg_peak_x"]["value"] - 10.0) / 10.0 < 0.10


def test_over_half_response_window_excluded_is_degraded():
    cues = [ev(IMPACT + 100 + 250 * i, "haptic_cue", 0, cue="stroke", motor=0, delivered=True, ack_latency_ms=10)
            for i in range(5)]  # excludes IMPACT+100 .. IMPACT+1350 = 83 % of the window
    out = run(cond_events(0, "sync", extra=cues), emg=emg_stream(onset_ms=IMPACT + 40))["sync"]
    assert out["flinch_emg_peak_x"]["quality"] == "degraded"
    assert any("motor_exclusion" in r for r in out["flinch_emg_peak_x"]["quality_reasons"])


def test_fully_excluded_response_is_missing():
    cues = [ev(IMPACT + 250 * i - 100, "haptic_cue", 0, cue="stroke", motor=0, delivered=True) for i in range(8)]
    out = run(cond_events(0, "sync", extra=cues), emg=emg_stream(onset_ms=IMPACT + 40))["sync"]
    assert out["flinch_emg_peak_x"]["quality"] == "missing"
    assert out["flinch_emg_peak_x"]["quality_reasons"] == ["response_window_fully_excluded"]


def test_low_rate_and_sensor_gap_degrade():
    s = emg_stream(onset_ms=IMPACT + 120)
    s["rate_hz"] = 30.0
    keep = ~((s["t_ms"] > IMPACT + 600) & (s["t_ms"] < IMPACT + 900))     # 300 ms hole
    for k in ("t_ms", "value"):
        s[k] = s[k][keep]
    out = run(cond_events(0, "sync"), emg=s)["sync"]["flinch_emg_peak_x"]
    assert out["quality"] == "degraded"
    assert any(r.startswith("rate_hz_30") for r in out["quality_reasons"])
    assert any(r.startswith("sensor_gap_") for r in out["quality_reasons"])


def test_file_motor_excl_flags_are_honoured():
    s = emg_stream(onset_ms=IMPACT + 400, artifacts=[(IMPACT + 50, IMPACT + 300, 5000.0)])
    s["motor_excl"] = (s["t_ms"] >= IMPACT + 50) & (s["t_ms"] <= IMPACT + 300)
    out = run(cond_events(0, "sync"), emg=s)["sync"]
    assert abs(out["flinch_emg_latency_ms"]["value"] - 400.0) <= 5.0


# ------------------------------------------------------------------ IMU
def test_imu_peak_and_latency_known():
    out = run(cond_events(0, "sync"), imu=imu_stream(onset_ms=IMPACT + 80, bump=3.0))["sync"]
    assert abs(out["flinch_imu_peak"]["value"] - 3.0) / 3.0 < 0.10
    assert abs(out["flinch_imu_latency_ms"]["value"] - 82.0) <= 5.0   # 40 ms ramp crosses 0.15 m/s2 ~2 ms in


# ------------------------------------------------------------------ wrist
def test_wrist_peak_and_latency_known():
    j = wrist_joint(onset_ms=IMPACT + 150, v_peak=0.5, dur_ms=400)
    out = run(cond_events(0, "sync"), joints={"r_wrist": j})["sync"]
    peak, lat = out["flinch_wrist_peak_mps"], out["flinch_wrist_latency_ms"]
    assert abs(peak["value"] - 0.5) / 0.5 < 0.10
    # truth: first time the unfiltered minimum-jerk speed passes 0.15 m/s
    tt = np.arange(0, 400, 0.1)
    s = tt / 400.0
    v = 0.5 * (30 * s ** 2 - 60 * s ** 3 + 30 * s ** 4) / 1.875
    truth = 150.0 + tt[np.argmax(v > 0.15)]
    assert abs(lat["value"] - truth) <= 15.0, (lat, truth)   # 6 Hz zero-phase filter + 72 Hz sampling


def test_wrist_no_withdrawal():
    out = run(cond_events(0, "async"), joints={"r_wrist": wrist_joint(onset_ms=None)})["async"]
    assert out["flinch_wrist_latency_ms"]["quality"] == "missing"
    assert out["flinch_wrist_latency_ms"]["quality_reasons"] == ["no_withdrawal_detected"]
    assert out["flinch_wrist_peak_mps"]["value"] < 0.05


# ------------------------------------------------------------------ missing nodes
def test_missing_nodes():
    out = run(cond_events(0, "sync"))["sync"]
    for k, why in (("flinch_emg_peak_x", "node_absent_bio"), ("flinch_emg_latency_ms", "node_absent_bio"),
                   ("flinch_imu_peak", "node_absent_haptic"), ("flinch_wrist_peak_mps", "no_r_wrist_track"),
                   ("cue_delivery_rate", "node_absent_haptic"), ("emg_windows_excluded", "node_absent_bio")):
        assert out[k]["quality"] == "missing" and out[k]["value"] is None and why in out[k]["quality_reasons"], k


def test_threat_absent_or_not_ok():
    events = [ev(0, "phase_start", 0, phase="induction", condition="sync")]
    assert run(events)["sync"]["flinch_emg_peak_x"]["quality_reasons"] == ["threat_impact_absent"]
    events.append(ev(5, "threat_impact", 0, impact_ms=100, impact_pos=[0, 0, 0], ok=False))
    assert run(events)["sync"]["flinch_emg_peak_x"]["quality_reasons"] == ["threat_impact_not_ok"]


# ------------------------------------------------------------------ probes, questionnaire, strokes
def probe(t, when, drift, trial=None, confirmed=True):
    return ev(t, "drift_probe", trial, when=when, perceived_x=0.0, actual_x=0.0, drift_cm=drift, confirmed=confirmed)


def test_drift_change_and_unconfirmed():
    events = cond_events(0, "sync") + [probe(100, "pre", 0.5), probe(9000, "post", 2.5, trial=0)]
    d = run(events)["sync"]["drift_change_cm"]
    assert d["value"] == pytest.approx(2.0) and d["quality"] == "ok" and d["quality_reasons"] == ["shared_pre_probe"]
    events = cond_events(0, "sync") + [probe(100, "pre", 0.5), probe(9000, "post", 2.5, trial=0, confirmed=False)]
    d = run(events)["sync"]["drift_change_cm"]
    assert d["quality"] == "missing" and d["quality_reasons"] == ["probe_unconfirmed"]
    d = run(cond_events(0, "sync"))["sync"]["drift_change_cm"]
    assert d["quality"] == "missing"


def test_questionnaire_ownership_control_q4():
    q = [ev(9500 + i, "questionnaire_item", 0, item=k, value=v) for i, (k, v) in
         enumerate([("q1", 6), ("q2", 5), ("q3", 3), ("q4", 6)])]
    out = run(cond_events(0, "sync", extra=q))["sync"]
    assert out["ownership"]["value"] == 5.5 and out["control"]["value"] == 3 and out["witness_q4"]["value"] == 6
    q = [ev(9500, "questionnaire_item", 0, item="q1", value=4)]
    own = run(cond_events(0, "sync", extra=q))["sync"]["ownership"]
    assert own["quality"] == "degraded" and own["value"] == 4


def test_demo_mode_single_ownership_item_is_the_planned_measure():
    """03-SPEC D18: demo_mode asks q1 alone after each condition, so one item is not a data loss there."""
    events = cond_events(0, "sync", extra=[ev(9500, "questionnaire_item", 0, item="q1", value=6)])

    def own(params, game_id="phantom_hand", item="q1"):
        evs = [dict(e, data=dict(e["data"], item=item)) if e["type"] == "questionnaire_item" else e for e in events]
        data = session(evs)
        data.envelope["blocks"] = [{"game_id": game_id, "params": params}]
        return emb.compute_embodiment(evs, data)["sync"]["ownership"]

    o = own({"demo_mode": True})
    assert (o["value"], o["quality"], o["quality_reasons"], o["n"]) == (6, "ok", ["single_item_demo_mode"], 1)
    assert own({"demo_mode": False})["quality"] == "degraded"          # the full form asked q2 and it is absent
    assert own({"demo_mode": True}, game_id="another_game")["quality"] == "degraded"
    assert own({"demo_mode": True}, item="q2")["quality"] == "degraded"  # the short form asks q1, not q2


def test_demo_mode_says_not_asked_for_the_questions_the_short_form_leaves_out():
    """D18: q3 is never asked and q4 only after the last condition. The phone shows the reason, so it must not read as 'not answered'."""
    events = cond_events(0, "async", impact=10_000.0, extra=[ev(9500, "questionnaire_item", 0, item="q1", value=3)]) + \
        cond_events(1, "sync", impact=30_000.0, extra=[ev(29500, "questionnaire_item", 1, item="q1", value=6)])

    def reasons(params):
        data = session(events)
        data.envelope["blocks"] = [{"game_id": "phantom_hand", "params": params}]
        e = emb.compute_embodiment(events, data)
        return {c: (e[c]["control"]["quality_reasons"], e[c]["witness_q4"]["quality_reasons"]) for c in ("async", "sync")}

    demo = reasons({"demo_mode": True})
    assert demo["async"] == (["q3_not_asked"], ["q4_not_asked"])      # first condition: neither is asked
    assert demo["sync"] == (["q3_not_asked"], ["q4_absent"])          # last condition: q4 was due and is missing
    full = reasons({"demo_mode": False})
    assert full["async"] == full["sync"] == (["q3_absent"], ["q4_absent"])


def test_stroke_timing_and_delivery():
    strokes = [ev(1000 + 1000 * i, "stroke", 0, index=i, brush_pass_a_ms=1000 + 1000 * i,
                  brush_pass_b_ms=1100 + 1000 * i, cue_a_send_ms=962 + 1000 * i, cue_b_send_ms=1062 + 1000 * i,
                  swapped=False, timing_err_ms=e) for i, e in enumerate([2, 4, 6, 8, 10])]
    cues = [ev(1000 * i, "haptic_cue", 0, cue="stroke", motor=0, delivered=(i != 3)) for i in range(4)]
    out = run(cond_events(0, "sync", extra=strokes + cues))["sync"]
    assert out["stroke_timing_err_ms"]["mean"]["value"] == pytest.approx(6.0)
    assert out["stroke_timing_err_ms"]["p95"]["value"] == pytest.approx(9.6)
    assert out["cue_delivery_rate"]["value"] == pytest.approx(0.75)
    assert out["async_delay_ms"]["mean"]["value"] == pytest.approx(-38.0)


def test_contrast_and_condition_order():
    def cond(trial, name, drift_post, own):
        off = 20000 * trial
        e = cond_events(trial, name, impact=IMPACT + off)
        e += [probe(100 + off, "pre", 0.0, trial=trial), probe(9000 + off, "post", drift_post, trial=trial)]
        e += [ev(9500 + off, "questionnaire_item", trial, item="q1", value=own),
              ev(9501 + off, "questionnaire_item", trial, item="q2", value=own)]
        return e
    events = cond(0, "async", 0.5, 2) + cond(1, "sync", 2.5, 6)
    t = np.arange(0.0, 40000.0, 10.0)
    v = np.full(len(t), 100.0)
    v = np.where((t >= IMPACT + 100) & (t < IMPACT + 800), 300.0, v)
    v = np.where((t >= IMPACT + 20000 + 100) & (t < IMPACT + 20000 + 800), 900.0, v)
    emg = {"device_id": "b", "rate_hz": 100.0, "motor_excl": None, "t_ms": t, "value": v}
    res = run(events, emg=emg)
    assert res["condition_order"] == ["async", "sync"]
    c = res["sync_minus_async"]
    assert c["drift_change_cm"]["value"] == pytest.approx(2.0)
    assert c["ownership"]["value"] == pytest.approx(4.0)
    assert c["flinch_emg_peak_x"]["value"] == pytest.approx(6.0, rel=0.05)
    assert c["flinch_wrist_peak_mps"]["quality"] == "missing"   # no kinematics -> contrast missing


def test_single_condition_has_no_contrast():
    assert "sync_minus_async" not in run(cond_events(0, "sync"))


def test_witness_table_text():
    table = emb.witness_table(run(cond_events(0, "sync"), emg=emg_stream(onset_ms=IMPACT + 120)))
    assert "EMG flinch peak" in table and "SYNC" in table


# ------------------------------------------------------------------ fixture end to end
@pytest.fixture()
def fixture_copy(tmp_path):
    dst = tmp_path / "phantom_hand_min"
    shutil.copytree(FIXTURE, dst)
    return dst


def test_fixture_loads_sensor_streams(fixture_copy):
    data = load_session(fixture_copy, validate=True)
    assert data.is_valid, data.validation_errors
    assert data.emg is not None and len(data.emg["t_ms"]) == 20 and data.emg["rate_hz"] == 100
    assert data.imu is not None and len(data.imu["az"]) == 20


def test_fixture_end_to_end_metrics_valid(fixture_copy):
    write_metrics(fixture_copy)
    m = json.loads((fixture_copy / "metrics.json").read_text(encoding="utf-8"))
    assert m["trials"] == [] and m["session"] == {"metrics": {}}
    e = m["embodiment"]
    assert e["condition_order"] == ["async", "sync"]
    assert e["sync"]["drift_change_cm"]["value"] == pytest.approx(2.0)      # post 2.4 - pre 0.4
    assert e["async"]["drift_change_cm"]["value"] == pytest.approx(0.0)
    assert e["sync"]["ownership"]["value"] == 5.5 and e["async"]["ownership"]["value"] == 2.5
    assert e["sync"]["cue_delivery_rate"]["value"] == 1.0
    assert e["sync_minus_async"]["ownership"]["value"] == pytest.approx(3.0)
    # the fixture's sensor files stop at 190 ms: flinch must be missing, not copied from the on-device numbers
    assert e["sync"]["flinch_emg_peak_x"]["quality"] == "missing"
    schema = json.loads((find_contracts_dir() / "schemas" / "metrics.schema.json").read_text(encoding="utf-8"))
    errs = list(Draft202012Validator(schema, format_checker=FormatChecker()).iter_errors(m))
    assert errs == []


def test_cli_summary_prints_table_and_writes(fixture_copy, capsys):
    cli_main([str(fixture_copy), "--summary"])
    out = capsys.readouterr().out
    assert "SYNC-ASYNC" in out and "Ownership" in out
    assert (fixture_copy / "metrics.json").exists()


def test_flinch_from_raw_in_written_sensor_files(tmp_path):
    """Full file round trip: write sens_000.json with a real flinch, run the pipeline, get the known numbers."""
    d = tmp_path / "s"
    shutil.copytree(FIXTURE, d)
    t = np.arange(0.0, 80000.0, 10.0)
    v = np.full(len(t), 100.0)
    for imp, top in ((34000.0, 300.0), (68000.0, 900.0)):
        v = np.where((t >= imp + 100) & (t < imp + 900), top, v)
    sid = json.loads((d / "session.json").read_text(encoding="utf-8"))["session_id"]
    sens = {"session_id": sid, "chunk_seq": 0, "t0_ms": 0, "source": "synthetic",
            "emg_env": {"device_id": "CHETNA_BIO_001", "rate_hz": 100, "unit": "raw_adc",
                        "t_ms": t.tolist(), "value": v.tolist()}}
    (d / "sens_000.json").write_text(json.dumps(sens), encoding="utf-8")
    res = analyze_session(d)["embodiment"]
    assert res["sync"]["flinch_emg_peak_x"]["value"] == pytest.approx(9.0, rel=0.1)
    assert res["async"]["flinch_emg_peak_x"]["value"] == pytest.approx(3.0, rel=0.1)
    assert abs(res["sync"]["flinch_emg_latency_ms"]["value"] - 100.0) <= 10.0
    assert res["sync_minus_async"]["flinch_emg_peak_x"]["value"] > 0


# ------------------------------------------------------------------ N1b: delivery quality + touch_incomplete
def _cues(n, delivered_n, base=1000):
    return [ev(base + 100 * i, "haptic_cue", 0, cue="stroke", motor=0, delivered=(i < delivered_n)) for i in range(n)]


def _strokes(n):
    return [ev(1000 + 100 * i, "stroke", 0, index=i, brush_pass_a_ms=1000 + 100 * i, brush_pass_b_ms=1050 + 100 * i,
               cue_a_send_ms=1010 + 100 * i, cue_b_send_ms=1060 + 100 * i, swapped=False, timing_err_ms=3)
            for i in range(n)]


def _probes():
    return [ev(100, "drift_probe", 0, when="pre", drift_cm=0.0, confirmed=True),
            ev(9000, "drift_probe", 0, when="post", drift_cm=2.0, confirmed=True),
            ev(9500, "questionnaire_item", 0, item="q1", value=5), ev(9501, "questionnaire_item", 0, item="q2", value=5),
            ev(9502, "questionnaire_item", 0, item="q3", value=4)]


def test_full_delivery_is_ok():
    out = run(cond_events(0, "sync", extra=_cues(20, 20) + _strokes(10) + _probes()))["sync"]
    assert out["cue_delivery_rate"]["quality"] == "ok" and out["stroke_timing_err_ms"]["mean"]["quality"] == "ok"
    assert out["drift_change_cm"]["quality"] == "ok" and out["ownership"]["quality"] == "ok"


def test_partial_delivery_degrades_rate_and_timing_but_not_drift():
    out = run(cond_events(0, "sync", extra=_cues(20, 14) + _strokes(10) + _probes()))["sync"]   # 0.70
    assert out["cue_delivery_rate"]["quality"] == "degraded"
    assert "cues_undelivered" in out["cue_delivery_rate"]["quality_reasons"]
    assert out["stroke_timing_err_ms"]["mean"]["quality"] == "degraded"
    assert out["drift_change_cm"]["quality"] == "ok"        # >= 50 % delivered: touch condition not compromised


def test_boundary_090_is_ok():
    out = run(cond_events(0, "sync", extra=_cues(20, 18) + _strokes(10)))["sync"]
    assert out["cue_delivery_rate"]["value"] == pytest.approx(0.9) and out["cue_delivery_rate"]["quality"] == "ok"


def test_touch_incomplete_propagates():
    out = run(cond_events(0, "sync", extra=_cues(16, 6) + _strokes(10) + _probes()))["sync"]   # 0.375
    for k in ("drift_change_cm", "ownership", "control"):
        assert out[k]["quality"] == "degraded" and "touch_incomplete" in out[k]["quality_reasons"], k
        assert out[k]["value"] is not None
    assert "touch_incomplete" not in out["witness_q4"].get("quality_reasons", [])


def test_touch_incomplete_inherited_by_contrast():
    ev_s = cond_events(0, "sync", extra=_cues(16, 6) + _probes())
    asy = [e for e in cond_events(1, "async", impact=30000.0)]
    pr = [ev(21000, "drift_probe", 1, when="pre", drift_cm=0.0, confirmed=True),
          ev(29000, "drift_probe", 1, when="post", drift_cm=0.5, confirmed=True)]
    out = run(ev_s + asy + pr)["sync_minus_async"]["drift_change_cm"]
    assert out["quality"] == "degraded" and "sync:touch_incomplete" in out["quality_reasons"]


def test_few_delivered_strokes():
    out = run(cond_events(0, "sync", extra=_cues(4, 4) + _strokes(2)))["sync"]
    assert out["cue_delivery_rate"]["quality"] == "ok"
    m = out["stroke_timing_err_ms"]["mean"]
    assert m["quality"] == "degraded" and any(r.startswith("few_delivered_strokes") for r in m["quality_reasons"])


def test_no_cues_is_missing_no_cues():
    out = run(cond_events(0, "sync", extra=_strokes(5)))["sync"]
    d = out["cue_delivery_rate"]
    assert d["quality"] == "missing" and d["value"] is None and "no_cues" in d["quality_reasons"]
