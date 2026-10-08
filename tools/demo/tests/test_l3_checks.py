"""Tests for tools/demo/l3_checks.py -- the G2 / L3 table.

The fixtures under tests/fixtures/ph_l3_main are RECORDED from a real `run_pipeline.py --game phantom_hand --sim
--no-unity` run (events/session/metrics as stored by the hub, the twin's stroke log lines). The healthy run
must pass every row; then every row is attacked with a corruption of exactly the thing it measures and must FAIL.
A check that cannot fail is not a check (02-RULES section 2).

    analytics/.venv/Scripts/python.exe -m pytest tools/demo/tests/test_l3_checks.py -q
"""
from __future__ import annotations

import copy
import json
from pathlib import Path
from typing import Any, Dict, List

import pytest

import l3_checks as L3

FIX = Path(__file__).resolve().parent / "fixtures" / "ph_l3_main"
RTT_OK = [3.1, 4.9, 9.1]          # the recorded run: p50 4.9 ms, p95 9.1 ms


@pytest.fixture
def events() -> List[Dict[str, Any]]:
    return L3.read_events(FIX)


@pytest.fixture
def twin() -> List[Dict[str, Any]]:
    return L3.read_jsonl(FIX / "twin_strokes.jsonl")


@pytest.fixture
def metrics() -> Dict[str, Any]:
    return json.loads((FIX / "metrics.json").read_text(encoding="utf-8"))


def rows(events, twin, metrics, **over):
    kw = dict(events=events, twin_log=twin, params=L3.session_params(FIX), rtt_ms=RTT_OK, invalid=0, invalid_msgs=[],
              status_seen=147, validate_rc=0, uploads={"session.json": True, "events.ndjson": True},
              expected_files=["session.json", "events.ndjson"], landed=["session.json", "events.ndjson"],
              events_received=(73, 73), metrics=metrics, analytics_rc=0)
    kw.update(over)
    return {r.check: r for r in L3.core_rows(**kw)}


def only_failing(rs: Dict[str, L3.Row]) -> List[str]:
    return [k for k, r in rs.items() if not r.ok]


# ------------------------------------------------------------------ the healthy recording
def test_recorded_healthy_run_passes_every_row(events, twin, metrics):
    rs = rows(events, twin, metrics)
    assert len(rs) == 7
    assert only_failing(rs) == [], {k: r.observed for k, r in rs.items() if not r.ok}


def test_fixture_is_the_real_shape(events, twin):
    assert sum(1 for e in events if e["type"] == "haptic_cue") == 16
    assert len(twin) == 16 and all(r["status"] == "executed" for r in twin)
    assert L3.trial_conditions(events) == {0: "async", 1: "sync"}


def test_percentile_matches_numpy_convention():
    assert L3.percentile([1, 2, 3, 4], 50) == 2.5
    assert L3.percentile([10], 95) == 10
    assert L3.percentile([], 50) is None
    assert L3.percentile(list(range(101)), 95) == 95


# ------------------------------------------------------------------ 1. phases
def phases(events):
    return L3.check_phases(events)


def test_phases_wrong_order_inside_a_block_fails(events):
    ev = copy.deepcopy(events)
    idx = [i for i, e in enumerate(ev) if e["type"] == "phase_start" and e["data"]["phase"] in ("threat", "probe_post")]
    ev[idx[0]], ev[idx[1]] = ev[idx[1]], ev[idx[0]]
    assert not phases(ev).ok


def test_phases_missing_questionnaire_in_one_block_fails(events):
    ev = [e for e in events if not (e["type"] == "phase_start" and e["data"]["phase"] == "questionnaire" and e["trial"] == 1)]
    r = phases(ev)
    assert not r.ok and "questionnaire" in r.observed


def test_phases_one_condition_only_fails(events):
    ev = [e for e in events if e.get("trial") != 1 or e["type"] != "phase_start"]
    assert not phases(ev).ok


def test_phases_two_syncs_fail(events):
    ev = copy.deepcopy(events)
    for e in ev:
        if e["type"] == "phase_start" and e["data"]["condition"] == "async":
            e["data"]["condition"] = "sync"
    assert not phases(ev).ok


def test_phases_missing_witness_or_done_fails(events):
    assert not phases([e for e in events if not (e["type"] == "phase_start" and e["data"]["phase"] == "witness")]).ok
    assert not phases([e for e in events if not (e["type"] == "phase_start" and e["data"]["phase"] == "done")]).ok


def test_phases_missing_calibration_fails(events):
    assert not phases([e for e in events if not (e["type"] == "phase_start" and e["data"]["phase"] == "calibrate")]).ok


def test_phases_empty_run_fails():
    assert not L3.check_phases([]).ok


def test_phases_threat_disabled_run_is_allowed_when_declared(events):
    ev = [e for e in events if not (e["type"] == "phase_start" and e["data"]["phase"] == "threat")]
    assert not L3.check_phases(ev, threat_enabled=True).ok
    assert L3.check_phases(ev, threat_enabled=False).ok


# ------------------------------------------------------------------ 2. acks
def test_acks_three_undelivered_fails(events, twin):
    ev = copy.deepcopy(events)
    n = 0
    for e in ev:
        if e["type"] == "haptic_cue" and n < 3:
            e["data"]["delivered"] = False
            n += 1
    assert not L3.check_acks(ev, twin).ok


def test_acks_twin_log_is_an_independent_witness(events, twin):
    """The session says 16 delivered but the twin only executed 10: the headset's claim must not be believed."""
    r = L3.check_acks(events, twin[:10])
    assert not r.ok and "10 executed" in r.observed


def test_acks_rejected_by_the_node_is_not_executed(events, twin):
    bad = copy.deepcopy(twin)
    for rec in bad[:4]:
        rec["status"] = "rejected"
    assert not L3.check_acks(events, bad).ok


def test_acks_zero_cues_cannot_pass_vacuously(twin):
    assert not L3.check_acks([], twin).ok
    assert not L3.check_acks([], [], twin_expected=False).ok


def test_acks_hardware_mode_without_a_twin_uses_events_only(events):
    assert L3.check_acks(events, [], twin_expected=False).ok


def test_acks_one_in_sixteen_lost_is_below_98(events, twin):
    ev = copy.deepcopy(events)
    next(e for e in ev if e["type"] == "haptic_cue")["data"]["delivered"] = False
    r = L3.check_acks(ev, twin)
    assert not r.ok          # 15/16 = 93.8 %


# ------------------------------------------------------------------ 3. SYNC timing
def sync_strokes(ev):
    cond = L3.trial_conditions(ev)
    return [e for e in ev if e["type"] == "stroke" and cond.get(e["trial"]) == "sync"]


def test_sync_timing_mean_over_20_fails(events):
    ev = copy.deepcopy(events)
    for e in sync_strokes(ev):
        e["data"]["timing_err_ms"] = 25
        e["data"]["cue_a_send_ms"] = e["data"]["brush_pass_a_ms"] - 40 + 25     # keep the stamps consistent
        e["data"]["cue_b_send_ms"] = e["data"]["brush_pass_b_ms"] - 40 + 25
    assert not L3.check_sync_timing(ev).ok


def test_sync_timing_one_outlier_breaks_p95_not_mean(events):
    ev = copy.deepcopy(events)
    s = sync_strokes(ev)
    s[0]["data"]["timing_err_ms"] = 90
    s[0]["data"]["cue_a_send_ms"] = s[0]["data"]["brush_pass_a_ms"] - 40 + 90
    s[0]["data"]["cue_b_send_ms"] = s[0]["data"]["brush_pass_b_ms"] - 40 + 90
    r = L3.check_sync_timing(ev)
    assert not r.ok and "p95" in r.observed


def test_sync_timing_reported_value_that_the_stamps_contradict_fails(events):
    ev = copy.deepcopy(events)
    for e in sync_strokes(ev):
        e["data"]["cue_a_send_ms"] += 60               # the cue was really sent 60 ms late; timing_err_ms still says 2
        e["data"]["cue_b_send_ms"] += 60
    r = L3.check_sync_timing(ev)
    assert not r.ok and "DISAGREES" in r.observed


def test_sync_timing_without_sync_strokes_fails(events):
    cond = L3.trial_conditions(events)
    ev = [e for e in events if not (e["type"] == "stroke" and cond.get(e["trial"]) == "sync")]
    assert not L3.check_sync_timing(ev).ok


def test_sync_timing_wrong_lead_param_is_noticed(events):
    """tactile_lead_ms 40 is baked into the stamps; claiming lead 0 must make the recompute disagree by ~40 ms."""
    assert not L3.check_sync_timing(events, tactile_lead_ms=0.0).ok


# ------------------------------------------------------------------ 4. ASYNC delay
def async_strokes(ev):
    cond = L3.trial_conditions(ev)
    return [e for e in ev if e["type"] == "stroke" and cond.get(e["trial"]) == "async"]


def test_async_delay_too_short_fails(events):
    ev = copy.deepcopy(events)
    for e in async_strokes(ev):
        e["data"]["cue_a_send_ms"] = e["data"]["brush_pass_a_ms"] + 300
        e["data"]["cue_b_send_ms"] = e["data"]["brush_pass_b_ms"] + 300
    assert not L3.check_async_delay(ev).ok


def test_async_delay_too_long_fails(events):
    ev = copy.deepcopy(events)
    for e in async_strokes(ev):
        e["data"]["cue_a_send_ms"] = e["data"]["brush_pass_a_ms"] + 900
    assert not L3.check_async_delay(ev).ok


def test_async_delay_one_of_four_out_of_range_is_75_percent_and_fails(events):
    ev = copy.deepcopy(events)
    a = async_strokes(ev)[0]["data"]
    a["cue_a_send_ms"] = a["brush_pass_a_ms"] + 200
    r = L3.check_async_delay(ev)
    assert not r.ok and "75 %" in r.observed


def test_async_delay_when_sync_strokes_look_delayed_too_fails(events):
    """If SYNC strokes are also 600 ms late the two conditions are not different; the row must catch it."""
    ev = copy.deepcopy(events)
    for e in sync_strokes(ev):
        e["data"]["cue_a_send_ms"] = e["data"]["brush_pass_a_ms"] + 600
    assert not L3.check_async_delay(ev).ok


def test_async_delay_without_async_strokes_fails(events):
    cond = L3.trial_conditions(events)
    ev = [e for e in events if not (e["type"] == "stroke" and cond.get(e["trial"]) == "async")]
    assert not L3.check_async_delay(ev).ok


# ------------------------------------------------------------------ 5. live
def test_live_slow_rtt_fails():
    assert not L3.check_live([300.0, 310.0, 400.0], 0, 10).ok


def test_live_invalid_message_fails():
    assert not L3.check_live(RTT_OK, 1, 10, ["status: boom"]).ok


def test_live_no_rtt_or_no_game_state_fails():
    assert not L3.check_live([], 0, 10).ok
    assert not L3.check_live(RTT_OK, 0, 0).ok


def test_live_external_hub_has_no_invalid_count_but_still_needs_rtt():
    r = L3.check_live(RTT_OK, None, 10)
    assert r.ok and "n/a" in r.observed


# ------------------------------------------------------------------ 6. session
def test_session_validate_failure_fails():
    assert not L3.check_session(1, {"a": True}, ["a"], ["a"], (1, 1)).ok


def test_session_missing_upload_fails():
    assert not L3.check_session(0, {"a": True, "b": False}, ["a", "b"], ["a", "b"], (1, 1)).ok


def test_session_file_not_on_hub_disk_fails():
    assert not L3.check_session(0, {"a": True, "b": True}, ["a", "b"], ["a"], (1, 1)).ok


def test_session_lost_trial_event_fails():
    assert not L3.check_session(0, {"a": True}, ["a"], ["a"], (72, 73)).ok


def test_session_external_hub_cannot_see_disk_or_event_count():
    assert L3.check_session(0, {"a": True}, ["a"], None, None).ok


# ------------------------------------------------------------------ 7. embodiment
def test_embodiment_missing_block_fails(events, metrics):
    m = copy.deepcopy(metrics)
    del m["embodiment"]["sync"]
    assert not L3.check_embodiment(m, events, 0).ok


def test_embodiment_absent_fails(events, metrics):
    m = copy.deepcopy(metrics)
    del m["embodiment"]
    assert not L3.check_embodiment(m, events, 0).ok
    assert not L3.check_embodiment(None, events, 0).ok
    assert not L3.check_embodiment(metrics, events, 1).ok


def test_embodiment_without_quality_flags_fails(events, metrics):
    m = copy.deepcopy(metrics)
    for k, v in m["embodiment"]["sync"].items():
        if isinstance(v, dict) and "quality" in v:
            del v["quality"]
        elif isinstance(v, dict):
            for vv in v.values():
                vv.pop("quality", None)
    assert not L3.check_embodiment(m, events, 0).ok


def test_embodiment_unknown_flag_fails(events, metrics):
    m = copy.deepcopy(metrics)
    m["embodiment"]["async"]["ownership"]["quality"] = "great"
    assert not L3.check_embodiment(m, events, 0).ok


def test_embodiment_disagreeing_with_the_events_fails(events, metrics):
    m = copy.deepcopy(metrics)
    m["embodiment"]["sync"]["stroke_timing_err_ms"]["mean"]["value"] = 17.0
    assert not L3.check_embodiment(m, events, 0).ok


# ------------------------------------------------------------------ faults
STS_UP = {"game_state": {"nodes": {"haptic": {"connected": True}, "bio": {"connected": True, "emg_level": 0.1}}}}
STS_A_DOWN = {"game_state": {"nodes": {"haptic": {"connected": False}, "bio": {"connected": True, "emg_level": 0.1}}}}
STS_B_DOWN = {"game_state": {"nodes": {"haptic": {"connected": True}, "bio": {"connected": False, "emg_level": None}}}}


def cut_events(events, keep_delivered=8):
    ev = copy.deepcopy(events)
    n = 0
    for e in ev:
        if e["type"] == "haptic_cue":
            n += 1
            if n > keep_delivered:
                e["data"]["delivered"] = False
                e["data"]["ack_latency_ms"] = None
    return ev


def test_fault_a_clean_cut_with_offline_status_and_flag_passes(events, metrics):
    m = copy.deepcopy(metrics)
    m["embodiment"]["sync"]["flinch_imu_peak"].update(quality="missing", quality_reasons=["no_samples_in_window"])
    rs = L3.check_fault_node_a_off(cut_events(events), [STS_UP, STS_UP, STS_A_DOWN], m, True)
    assert all(r.passed for r in rs), [(r.check, r.observed) for r in rs if not r.passed]


def test_fault_a_every_row_can_fail(events, metrics):
    m = copy.deepcopy(metrics)
    m["embodiment"]["sync"]["flinch_imu_peak"].update(quality="missing", quality_reasons=["x"])
    good = cut_events(events)
    assert not L3.check_fault_node_a_off(good, [STS_UP, STS_A_DOWN], m, False)[0].ok               # run did not complete
    assert not L3.check_fault_node_a_off(events, [STS_UP, STS_A_DOWN], m, True)[1].ok               # nothing was cut
    noisy = copy.deepcopy(good)
    next(e for e in noisy if e["type"] == "haptic_cue")["data"]["delivered"] = False                 # lost at the very start
    assert not L3.check_fault_node_a_off(noisy, [STS_UP, STS_A_DOWN], m, True)[1].ok
    assert not L3.check_fault_node_a_off(good, [STS_UP, STS_UP], m, True)[2].ok                      # never shown offline
    assert not L3.check_fault_node_a_off(good, [STS_A_DOWN, STS_A_DOWN], m, True)[2].ok              # never was online
    assert not L3.check_fault_node_a_off(good, [STS_UP, STS_A_DOWN], metrics, True)[3].ok            # nothing flagged


def test_fault_a_unflagged_low_cue_delivery_fails_the_gating_row(events, metrics):
    m = copy.deepcopy(metrics)
    m["embodiment"]["sync"]["cue_delivery_rate"]["value"] = 0.375
    m["embodiment"]["sync"]["flinch_imu_peak"].update(quality="missing", quality_reasons=["x"])
    rs = L3.check_fault_node_a_off(cut_events(events), [STS_UP, STS_A_DOWN], m, True)
    finding = rs[-1]
    assert not finding.ok and finding.gating and not finding.passed
    assert "FAIL" in L3.render_text(rs) and "NOT GREEN" in L3.render_text(rs)


def test_fault_b_absent_passes_only_when_b_is_really_gone(events, metrics, tmp_path):
    ev = [e for e in events if e["type"] != "emg_burst"]
    m = copy.deepcopy(metrics)
    m["embodiment"]["sync"]["flinch_emg_peak_x"].update(value=None, quality="missing", quality_reasons=["node_absent_bio"])
    (tmp_path / "sens_000.json").write_text(json.dumps({"imu": {}}), encoding="utf-8")
    ok = L3.check_fault_node_b_absent(ev, [STS_B_DOWN, STS_B_DOWN], m, True, tmp_path)
    assert all(r.passed for r in ok)
    # B reported up in one status -> not absent
    assert not L3.check_fault_node_b_absent(ev, [STS_B_DOWN, STS_UP], m, True, tmp_path)[1].ok
    # EMG sneaks into a sensor file / emg_burst events remain -> not absent
    (tmp_path / "sens_001.json").write_text(json.dumps({"emg_env": {}}), encoding="utf-8")
    assert not L3.check_fault_node_b_absent(ev, [STS_B_DOWN], m, True, tmp_path)[1].ok
    assert not L3.check_fault_node_b_absent(events, [STS_B_DOWN], m, True, tmp_path)[1].ok
    # analytics not flagging the missing EMG -> fails
    assert not L3.check_fault_node_b_absent(ev, [STS_B_DOWN], metrics, True, tmp_path)[2].ok


def test_fault_c_needs_a_retry_and_a_reconnect_and_every_event(events):
    rpt = {"upload_attempts": {"session.json": 3, "events.ndjson": 1}, "reconnects": 1}
    assert all(r.passed for r in L3.check_fault_hub_absent(events, rpt, 30, (73, 73), True, True))
    no_retry = {"upload_attempts": {"session.json": 1}, "reconnects": 1}
    assert not L3.check_fault_hub_absent(events, no_retry, 30, (73, 73), True, True)[2].ok
    no_reconnect = {"upload_attempts": {"session.json": 3}, "reconnects": 0}
    assert not L3.check_fault_hub_absent(events, no_reconnect, 30, (73, 73), True, True)[1].ok
    assert not L3.check_fault_hub_absent(events, rpt, 30, (70, 73), True, True)[3].ok
    assert not L3.check_fault_hub_absent(events, rpt, 30, (73, 73), True, False)[2].ok


# ------------------------------------------------------------------ rendering
def test_render_text_and_markdown_mark_failures_and_count(events, twin, metrics):
    rs = list(rows(events, twin, metrics, invalid=2).values())
    txt, md = L3.render_text(rs), L3.render_markdown(rs)
    assert "FAIL" in txt and "NOT GREEN" in txt and "6/7" in txt
    assert "| FAIL |" in md and md.count("| PASS |") == 6
    good = L3.render_text(list(rows(events, twin, metrics).values()))
    assert "7/7" in good and "G2 L3 GREEN" in good


# ------------------------------------------------------------------ the three fault runs, RECORDED
FIXROOT = Path(__file__).resolve().parent / "fixtures"


def fault_fixture(name: str):
    d = FIXROOT / f"ph_l3_{name}"
    return (d, L3.read_events(d), json.loads((d / "status_records.json").read_text(encoding="utf-8")),
            json.loads((d / "metrics.json").read_text(encoding="utf-8")),
            json.loads((d / "headset_report.json").read_text(encoding="utf-8")))


FLAG_ROW = "Fault A: cue_delivery_rate < 0.98 carries a quality flag"


def test_recorded_fault_a_run_passes_and_carries_the_analytics_finding():
    _, ev, sts, m, _ = fault_fixture("fault_node_a_off")
    rs = L3.check_fault_node_a_off(ev, sts, m, True)
    assert all(r.passed for r in rs)
    flag_row = next(r for r in rs if r.check == FLAG_ROW)
    assert flag_row.gating and flag_row.ok and "degraded" in flag_row.observed
    # a corrupted metrics.json (analytics forgot to flag the undelivered cues) must FAIL the now-gating row
    bad = copy.deepcopy(m)
    cdr = bad["embodiment"]["sync"]["cue_delivery_rate"]
    cdr["quality"] = "ok"
    cdr["quality_reasons"] = []
    rs_bad = L3.check_fault_node_a_off(ev, sts, bad, True)
    bad_row = next(r for r in rs_bad if r.check == FLAG_ROW)
    assert bad_row.gating and not bad_row.ok and not bad_row.passed
    # the same recording with the cut removed (cues all delivered) must fail the cut row
    ev2 = copy.deepcopy(ev)
    for e in ev2:
        if e["type"] == "haptic_cue":
            e["data"]["delivered"] = True
    assert not L3.check_fault_node_a_off(ev2, sts, m, True)[1].ok
    # and with every status saying the sleeve is fine, "Sleeve offline" was never shown
    up = copy.deepcopy(sts)
    for s in up:
        s["game_state"]["nodes"]["haptic"]["connected"] = True
    assert not L3.check_fault_node_a_off(ev, up, m, True)[2].ok


def test_recorded_fault_b_run_passes_and_fails_when_b_comes_back():
    d, ev, sts, m, _ = fault_fixture("fault_node_b_absent")
    assert all(r.passed for r in L3.check_fault_node_b_absent(ev, sts, m, True, d))
    up = copy.deepcopy(sts)
    up[5]["game_state"]["nodes"]["bio"].update(connected=True, emg_level=0.2)
    assert not L3.check_fault_node_b_absent(ev, up, m, True, d)[1].ok


def test_recorded_fault_c_run_passes_and_fails_without_the_retry():
    _, ev, _, _, rpt = fault_fixture("fault_hub_absent")
    assert rpt["reconnects"] >= 1 and max(rpt["upload_attempts"].values()) > 1
    assert all(r.passed for r in L3.check_fault_hub_absent(ev, rpt, 30, (73, 73), True, True))
    rpt2 = copy.deepcopy(rpt)
    rpt2["upload_attempts"] = {k: 1 for k in rpt2["upload_attempts"]}
    assert not L3.check_fault_hub_absent(ev, rpt2, 30, (73, 73), True, True)[2].ok
