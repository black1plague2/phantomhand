"""tools/demo/l3_checks.py -- the G2 / L3 table of docs/agent-briefs/ph/04-E2E.md as pure check functions.

Every function takes recorded data (events from the hub-stored session, the twin's JSON-lines log, the hub's
recording, the analytics metrics.json) and returns `Row`s. No sockets, no sleeps, no clocks: that is what lets
tests/test_l3_checks.py feed it recorded fixtures AND deliberately corrupted ones (a check that cannot fail is
not a check; 02-RULES section 2).

Meaning, not shape: each row is about what happened (order of phases, cues actually acknowledged by the node,
the timing the strokes really had), and where analytics computes the same number independently
(stroke_timing_err_ms, async_delay_ms, cue_delivery_rate) the two are cross-checked.

Thresholds (04-E2E.md, G2 L3 table):
    phases in order, both conditions       100 %
    stroke cues acked by the twin          >= 98 %
    SYNC timing_err_ms                     mean <= 20, p95 <= 40
    ASYNC delay                            500-700 ms on >= 95 % of strokes (felt delay = send - pass + lead,
                                           window widened by the SYNC row's 40 ms send allowance)
    live status RTT p50                   < 250 ms; invalid messages 0
    session valid + uploaded               yes
    analytics writes embodiment + flags    yes
    faults                                 completes; "Sleeve offline" shown; flags set; upload retried
"""
from __future__ import annotations

import json
import math
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple

# ------------------------------------------------------------------ thresholds (04-E2E.md)
ACK_RATE_MIN = 0.98
SYNC_ERR_MEAN_MAX_MS = 20.0
SYNC_ERR_P95_MAX_MS = 40.0
ASYNC_DELAY_RANGE_MS = (500.0, 700.0)
ASYNC_IN_RANGE_MIN = 0.95
RTT_P50_MAX_MS = 250.0
MIN_CUES = 10                 # a "100 % acked" run of 0 cues must fail
MIN_STROKES_PER_CONDITION = 2

CORE_RANK = {"induction": 2, "agency": 3, "threat": 4, "probe_post": 5, "questionnaire": 6}
FINALE_RANK = {"dissolve": 10, "reveal": 11, "witness": 12, "done": 13}
BLOCK_REQUIRED = ("induction", "probe_post", "questionnaire")      # threat is required unless threat_enabled=false


@dataclass
class Row:
    check: str
    threshold: str
    observed: str
    ok: bool
    detail: str = ""
    gating: bool = True          # False: reported as WARN, does not decide GREEN (a finding about another track)

    def as_dict(self) -> Dict[str, Any]:
        return {"check": self.check, "threshold": self.threshold, "observed": self.observed,
                "ok": self.ok, "detail": self.detail, "gating": self.gating}

    @property
    def passed(self) -> bool:
        return self.ok or not self.gating


# ------------------------------------------------------------------ readers
def read_events(session_dir: Path) -> List[Dict[str, Any]]:
    p = Path(session_dir) / "events.ndjson"
    if not p.exists():
        return []
    return [json.loads(l) for l in p.read_text(encoding="utf-8").splitlines() if l.strip()]


def read_jsonl(path: Optional[Path]) -> List[Dict[str, Any]]:
    if not path or not Path(path).exists():
        return []
    out = []
    for l in Path(path).read_text(encoding="utf-8").splitlines():
        l = l.strip()
        if l:
            try:
                out.append(json.loads(l))
            except ValueError:
                pass
    return out


def session_params(session_dir: Path) -> Dict[str, Any]:
    try:
        d = json.loads((Path(session_dir) / "session.json").read_text(encoding="utf-8"))
        return (d.get("blocks") or [{}])[0].get("params") or {}
    except (OSError, ValueError, IndexError):
        return {}


def percentile(values: Sequence[float], q: float) -> Optional[float]:
    """Linear-interpolated percentile (same convention as numpy.percentile). None for empty input."""
    v = sorted(values)
    if not v:
        return None
    k = (len(v) - 1) * q / 100.0
    lo, hi = int(math.floor(k)), int(math.ceil(k))
    return v[lo] + (v[hi] - v[lo]) * (k - lo)


def trial_conditions(events: List[Dict[str, Any]]) -> Dict[Any, str]:
    """trial index -> 'sync'|'async' from the phase_start events that carry a condition (03-SPEC: trial = condition index)."""
    m: Dict[Any, str] = {}
    for e in events:
        if e.get("type") == "phase_start":
            d = e.get("data") or {}
            if d.get("condition") and e.get("trial") is not None:
                m[e["trial"]] = d["condition"]
    return m


def strokes_by_condition(events: List[Dict[str, Any]]) -> Dict[str, List[Dict[str, Any]]]:
    cond = trial_conditions(events)
    out: Dict[str, List[Dict[str, Any]]] = {"sync": [], "async": []}
    for e in events:
        if e.get("type") == "stroke":
            c = cond.get(e.get("trial"))
            if c in out:
                out[c].append(e.get("data") or {})
    return out


# ------------------------------------------------------------------ 1. phases in order
def phase_sequence(events: List[Dict[str, Any]]) -> List[Tuple[str, Optional[str]]]:
    return [((e.get("data") or {}).get("phase"), (e.get("data") or {}).get("condition"))
            for e in events if e.get("type") == "phase_start"]


def check_phases(events: List[Dict[str, Any]], threat_enabled: bool = True) -> Row:
    """calibrate, then per condition [probe_pre] induction < [agency] < threat < probe_post < questionnaire, the two
    conditions being sync and async (either order), then [dissolve] [reveal] witness and a clean end.

    What the game really records (PhaseStateMachine, PhantomHandModule; first checked against a Unity recording on
    8 Oct): a probe_pre before EVERY condition, and no `done` phase_start: the run ends with block_end
    {aborted: false} and session_end {end_reason: completed}. A recording with one shared probe_pre and a `done`
    phase_start (the fake headset's fixture) is the other accepted shape."""
    seq = phase_sequence(events)
    problems: List[str] = []
    names = [p for p, _ in seq]
    if names[:2] != ["calibrate", "probe_pre"]:
        problems.append(f"run must open calibrate,probe_pre (got {names[:2]})")
    i = 1
    blocks: List[List[Tuple[str, Optional[str]]]] = []
    while i < len(seq) and seq[i][0] not in FINALE_RANK:
        k = i + 1 if seq[i][0] == "probe_pre" else i
        if k >= len(seq) or seq[k][0] != "induction":
            problems.append(f"block must start with [probe_pre] induction (got {seq[i][0]} at #{i})")
            i += 1
            continue
        j = k + 1
        while j < len(seq) and seq[j][0] in CORE_RANK and seq[j][0] != "induction":
            j += 1
        blocks.append(seq[i:j])
        i = j
    if len(blocks) != 2:
        problems.append(f"expected 2 condition blocks, found {len(blocks)}")
    conds = []
    required = list(BLOCK_REQUIRED) + (["threat"] if threat_enabled else [])
    for b in blocks:
        ranks = [CORE_RANK[p] for p, _ in b if p != "probe_pre"]
        if ranks != sorted(set(ranks)):
            problems.append(f"phases out of order inside a block: {[p for p, _ in b]}")
        have = {p for p, _ in b}
        for r in required:
            if r not in have:
                problems.append(f"block {[c for _, c in b][-1:]} lacks {r}")
        cs = {c for p, c in b if not (p == "probe_pre" and c is None)}     # a shared probe_pre carries no condition
        if len(cs) != 1 or None in cs:
            problems.append(f"block mixes/lacks a condition: {cs}")
        conds.extend(cs)
    if sorted(c for c in conds if c) != ["async", "sync"]:
        problems.append(f"conditions must be one sync + one async (got {conds})")
    tail = [p for p, _ in seq[i:]]
    franks = [FINALE_RANK.get(p, -1) for p in tail]
    clean_end = "done" in tail or any(
        (e.get("type") == "block_end" and (e.get("data") or {}).get("aborted") is False) or
        (e.get("type") == "session_end" and (e.get("data") or {}).get("end_reason") == "completed") for e in events)
    if [p for p in tail if p != "done"][-1:] != ["witness"] or franks != sorted(franks) or -1 in franks or not clean_end:
        problems.append(f"finale must be [dissolve] [reveal] witness, then done or a block_end/session_end that says "
                        f"completed (got {tail}, clean end: {clean_end})")
    ok = not problems
    obs = f"{len(seq)} phases, 2 conditions ({'/'.join(c for c in conds if c)}), 100 %" if ok else \
        f"{len(seq)} phases; " + "; ".join(problems)
    return Row("Phases in order, both conditions", "100 %", obs, ok)


# ------------------------------------------------------------------ 2. cues acked
def check_acks(events: List[Dict[str, Any]], twin_log: List[Dict[str, Any]], twin_expected: bool = True) -> Row:
    cues = [e for e in events if e.get("type") == "haptic_cue" and (e.get("data") or {}).get("cue") == "stroke"]
    n = len(cues)
    delivered = sum(1 for e in cues if (e.get("data") or {}).get("delivered") is True)
    twin_exec = sum(1 for r in twin_log if r.get("ev") == "stroke" and r.get("status") == "executed")
    twin_rej = sum(1 for r in twin_log if r.get("ev") == "stroke" and r.get("status") == "rejected")
    rate_ev = delivered / n if n else 0.0
    parts = [f"{delivered}/{n} delivered per the session events"]
    ok = n >= MIN_CUES and rate_ev >= ACK_RATE_MIN
    rate = rate_ev
    if twin_expected:
        # the twin's own log is the independent witness: it must have executed what the headset says it delivered
        rate_twin = twin_exec / n if n else 0.0
        parts.append(f"{twin_exec} executed / {twin_rej} rejected in the twin log")
        ok = ok and rate_twin >= ACK_RATE_MIN and twin_exec >= delivered
        rate = min(rate_ev, rate_twin)
    if n < MIN_CUES:
        parts.append(f"only {n} stroke cues (need >= {MIN_CUES})")
    return Row("Stroke cues acked by the twin", ">= 98 %", f"{rate * 100:.1f} % ({'; '.join(parts)})", ok)


# ------------------------------------------------------------------ 3. SYNC timing
def check_sync_timing(events: List[Dict[str, Any]], tactile_lead_ms: float = 40.0) -> Row:
    strokes = strokes_by_condition(events)["sync"]
    errs = [float(s["timing_err_ms"]) for s in strokes if s.get("timing_err_ms") is not None]
    if len(errs) < MIN_STROKES_PER_CONDITION:
        return Row("SYNC visual-to-send timing error", "mean <= 20 ms, p95 <= 40 ms",
                   f"only {len(errs)} sync strokes carry timing_err_ms", False)
    mean, p95 = sum(errs) / len(errs), percentile(errs, 95)
    # independent recompute from the raw stamps: |cue_send - (brush_pass - lead)| (mean of A and B)
    recomputed: List[float] = []
    for s in strokes:
        if s.get("swapped"):
            continue
        try:
            ea = abs(s["cue_a_send_ms"] - (s["brush_pass_a_ms"] - tactile_lead_ms))
            eb = abs(s["cue_b_send_ms"] - (s["brush_pass_b_ms"] - tactile_lead_ms))
        except (KeyError, TypeError):
            continue
        recomputed.append((ea + eb) / 2.0)
    consistent = True
    note = ""
    if recomputed:
        rmean = sum(recomputed) / len(recomputed)
        consistent = abs(rmean - mean) <= 5.0
        note = f"; recomputed from stamps mean {rmean:.1f} ms"
        if not consistent:
            note += " DISAGREES with the reported timing_err_ms"
    ok = mean <= SYNC_ERR_MEAN_MAX_MS and p95 <= SYNC_ERR_P95_MAX_MS and consistent
    return Row("SYNC visual-to-send timing error", "mean <= 20 ms, p95 <= 40 ms",
               f"mean {mean:.1f}, p95 {p95:.1f} ms (n={len(errs)}{note})", ok)


# ------------------------------------------------------------------ 4. ASYNC delay
def check_async_delay(events: List[Dict[str, Any]], tactile_lead_ms: float = 40.0) -> Row:
    """The delay is the one the skin gets: send - brush_pass + tactile_lead_ms (every cue, delayed ones too, leaves
    the headset tactile_lead_ms early so the motor is up to speed on time; the SYNC row uses the same convention).
    A measured delay also carries the send error the SYNC row allows (p95 <= 40 ms), so the 500-700 ms window is
    widened by that much at both ends: without it a delay drawn near 700 ms and sent one frame late would fail a
    scheduler that is working as specified."""
    strokes = strokes_by_condition(events)["async"]
    lo, hi = ASYNC_DELAY_RANGE_MS[0] - SYNC_ERR_P95_MAX_MS, ASYNC_DELAY_RANGE_MS[1] + SYNC_ERR_P95_MAX_MS
    delays: List[float] = []
    good = 0
    for s in strokes:
        ds = []
        for c, b in (("cue_a_send_ms", "brush_pass_a_ms"), ("cue_b_send_ms", "brush_pass_b_ms")):
            if s.get(c) is not None and s.get(b) is not None:
                ds.append(float(s[c]) - float(s[b]) + tactile_lead_ms)
        if not ds:
            continue
        delays.extend(ds)
        good += all(lo <= d <= hi for d in ds)
    n = sum(1 for s in strokes if s.get("cue_a_send_ms") is not None or s.get("cue_b_send_ms") is not None)
    if n < MIN_STROKES_PER_CONDITION:
        return Row("ASYNC delay", "500-700 ms on >= 95 % of strokes", f"only {n} async strokes with send stamps", False)
    frac = good / n
    sync_d = [float(s["cue_a_send_ms"]) - float(s["brush_pass_a_ms"]) + tactile_lead_ms
              for s in strokes_by_condition(events)["sync"]
              if s.get("cue_a_send_ms") is not None and s.get("brush_pass_a_ms") is not None]
    # the sync strokes must NOT look delayed, otherwise the conditions were not actually different
    sync_ok = (not sync_d) or max(abs(d) for d in sync_d) < ASYNC_DELAY_RANGE_MS[0]
    obs = (f"{frac * 100:.0f} % of {n} strokes in range (felt delay = send - pass + lead {tactile_lead_ms:.0f}: "
           f"min {min(delays):.0f}, max {max(delays):.0f} ms; +/-{SYNC_ERR_P95_MAX_MS:.0f} ms send tolerance)")
    if sync_d:
        obs += f"; sync delays max {max(abs(d) for d in sync_d):.0f} ms"
    return Row("ASYNC delay", "500-700 ms on >= 95 % of strokes", obs, frac >= ASYNC_IN_RANGE_MIN and sync_ok)


# ------------------------------------------------------------------ 5. live status
def check_live(rtt_ms: Sequence[float], invalid: Optional[int], status_seen: int, invalid_msgs: Iterable[str] = ()) -> Row:
    p50 = percentile(rtt_ms, 50)
    p95 = percentile(rtt_ms, 95)
    parts = []
    ok = True
    if p50 is None:
        parts.append("no RTT samples")
        ok = False
    else:
        parts.append(f"RTT p50 {p50:.1f} ms, p95 {p95:.1f} ms (n={len(rtt_ms)})")
        ok = ok and p50 < RTT_P50_MAX_MS
    if invalid is None:
        parts.append("invalid messages n/a (external hub)")
    else:
        parts.append(f"{invalid} invalid messages" + (f" {list(invalid_msgs)[:2]}" if invalid else ""))
        ok = ok and invalid == 0
    parts.append(f"{status_seen} statuses with game_state")
    ok = ok and status_seen > 0
    return Row("Live status RTT to hub", "p50 < 250 ms; invalid 0", "; ".join(parts), ok)


# ------------------------------------------------------------------ 6. session valid + uploaded
def check_session(validate_rc: Optional[int], uploads: Dict[str, bool], expected: Sequence[str],
                  landed: Optional[Sequence[str]], events_received: Optional[Tuple[int, int]] = None) -> Row:
    missing = [f for f in expected if not uploads.get(f)]
    parts = [f"validate.py --session exit {validate_rc}", f"{len(expected) - len(missing)}/{len(expected)} files uploaded 200/201"]
    ok = validate_rc == 0 and not missing
    if missing:
        parts.append(f"not uploaded: {missing}")
    if landed is not None:
        gone = [f for f in expected if f not in landed]
        parts.append(f"{len(expected) - len(gone)}/{len(expected)} on the hub's disk")
        ok = ok and not gone
    if events_received is not None:
        parts.append(f"hub got {events_received[0]}/{events_received[1]} trial_events exactly once")
        ok = ok and events_received[0] == events_received[1]
    return Row("Session valid and uploaded to the hub", "yes", "; ".join(parts), ok)


# ------------------------------------------------------------------ 7. analytics embodiment
def _metric_flags(block: Dict[str, Any]) -> List[Tuple[str, str]]:
    out = []
    for k, v in block.items():
        if isinstance(v, dict) and "quality" in v:
            out.append((k, v["quality"]))
        elif isinstance(v, dict):   # stroke_timing_err_ms {mean,p95}
            for kk, vv in v.items():
                if isinstance(vv, dict) and "quality" in vv:
                    out.append((f"{k}.{kk}", vv["quality"]))
    return out


def check_embodiment(metrics: Optional[Dict[str, Any]], events: List[Dict[str, Any]], analytics_rc: Optional[int]) -> Row:
    if analytics_rc != 0 or not metrics:
        return Row("Analytics writes embodiment with quality flags", "yes", f"analytics exit {analytics_rc}, metrics.json "
                   f"{'present' if metrics else 'missing'}", False)
    emb = metrics.get("embodiment")
    if not isinstance(emb, dict):
        return Row("Analytics writes embodiment with quality flags", "yes", "metrics.json has no `embodiment`", False)
    problems = []
    quality_counts: Dict[str, int] = {}
    for cond in ("sync", "async"):
        blk = emb.get(cond)
        if not isinstance(blk, dict):
            problems.append(f"no {cond} block")
            continue
        flags = _metric_flags(blk)
        if not flags:
            problems.append(f"{cond} has no quality-flagged metrics")
        for _, q in flags:
            quality_counts[q] = quality_counts.get(q, 0) + 1
        if any(q not in ("ok", "degraded", "missing") for _, q in flags):
            problems.append(f"{cond}: unknown quality flag")
        for need in ("drift_change_cm", "ownership", "stroke_timing_err_ms", "cue_delivery_rate"):
            if need not in blk:
                problems.append(f"{cond} lacks {need}")
    if "sync_minus_async" not in emb:
        problems.append("no sync_minus_async")
    # independent cross-check: analytics' own stroke timing/delay numbers vs ours from the raw events
    ours_sync = [s["timing_err_ms"] for s in strokes_by_condition(events)["sync"] if s.get("timing_err_ms") is not None]
    try:
        theirs = emb["sync"]["stroke_timing_err_ms"]["mean"]["value"]
    except (KeyError, TypeError):
        theirs = None
    if ours_sync and theirs is not None and abs(theirs - sum(ours_sync) / len(ours_sync)) > 1.0:
        problems.append(f"analytics sync timing mean {theirs} != events mean {sum(ours_sync) / len(ours_sync):.2f}")
    qs = ", ".join(f"{k} {v}" for k, v in sorted(quality_counts.items()))
    return Row("Analytics writes embodiment with quality flags", "yes",
               ("embodiment sync+async+sync_minus_async; flags: " + qs) if not problems else "; ".join(problems),
               not problems)


# ------------------------------------------------------------------ faults
def _statuses(status_records: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    return [s for s in status_records if isinstance(s.get("game_state"), dict)]


def check_fault_node_a_off(events: List[Dict[str, Any]], status_records: List[Dict[str, Any]],
                           metrics: Optional[Dict[str, Any]], run_complete: bool) -> List[Row]:
    cues = [(e.get("data") or {}).get("delivered") for e in events if e.get("type") == "haptic_cue"]
    cut = cues.index(False) if False in cues else None
    clean_cut = cut is not None and cut > 0 and all(c is True for c in cues[:cut]) and all(c is False for c in cues[cut:])
    sts = _statuses(status_records)
    off_seen = sum(1 for s in sts if s["game_state"]["nodes"]["haptic"].get("connected") is False)
    on_seen = sum(1 for s in sts if s["game_state"]["nodes"]["haptic"].get("connected") is True)
    flagged: List[str] = []
    cue_flag = None
    try:
        for cond in ("sync", "async"):
            blk = metrics["embodiment"][cond]
            for k in ("flinch_imu_peak", "flinch_imu_latency_ms", "cue_delivery_rate"):
                m = blk.get(k) or {}
                if m.get("quality") not in (None, "ok"):
                    flagged.append(f"{cond}.{k}={m.get('quality')}({','.join(m.get('quality_reasons') or [])})")
            cdr = blk.get("cue_delivery_rate") or {}
            if cdr.get("value") is not None and cdr["value"] < ACK_RATE_MIN:
                cue_flag = (cond, cdr)
    except (KeyError, TypeError):
        pass
    rows = [
        Row("Fault A: Node A off at 50 %: run completes", "yes", f"run complete={run_complete}", run_complete),
        Row("Fault A: cues delivered until the cut, undelivered (delivered=false) after it", "clean cut",
            f"{cut if cut is not None else len(cues)} delivered then {len(cues) - (cut or 0) if cut is not None else 0} undelivered "
            f"(of {len(cues)})", clean_cut),
        Row("Fault A: \"Sleeve offline\" shown (status haptic.connected=false)", "seen after being true",
            f"{on_seen} statuses connected, {off_seen} disconnected", on_seen > 0 and off_seen > 0),
        Row("Fault A: flags set in analytics (an affected metric not quality ok)", ">= 1 flagged",
            "; ".join(flagged) or "none flagged", bool(flagged)),
    ]
    # gating: below the ack threshold analytics must say so (quality != ok); a missing value is a failure too
    cond, cdr = cue_flag if cue_flag is not None else ("sync", (metrics or {}).get("embodiment", {}).get("sync", {}).get("cue_delivery_rate") or {})
    val = cdr.get("value")
    flag_ok = val is not None and (val >= ACK_RATE_MIN or cdr.get("quality") != "ok")
    rows.append(Row("Fault A: cue_delivery_rate < 0.98 carries a quality flag", "quality != ok",
                    f"{cond}: value {val} but quality {cdr.get('quality')}"
                    + (f" ({','.join(cdr.get('quality_reasons') or [])})" if cdr.get("quality_reasons") else ""), flag_ok))
    return rows


def check_fault_node_b_absent(events: List[Dict[str, Any]], status_records: List[Dict[str, Any]],
                              metrics: Optional[Dict[str, Any]], run_complete: bool, session_dir: Path) -> List[Row]:
    sts = _statuses(status_records)
    bio_up = sum(1 for s in sts if s["game_state"]["nodes"]["bio"].get("connected") is True)
    bio_down = len(sts) - bio_up
    emg_in_sens = False
    for f in Path(session_dir).glob("sens_*.json"):
        try:
            if "emg_env" in json.loads(f.read_text(encoding="utf-8")):
                emg_in_sens = True
        except ValueError:
            pass
    bursts = sum(1 for e in events if e.get("type") == "emg_burst")
    q = None
    try:
        q = metrics["embodiment"]["sync"]["flinch_emg_peak_x"]
    except (KeyError, TypeError):
        pass
    flagged = bool(q) and q.get("quality") == "missing" and bool(q.get("quality_reasons"))
    return [
        Row("Fault B: Node B absent: run completes", "yes", f"run complete={run_complete}", run_complete),
        Row("Fault B: status shows bio.connected=false throughout, no EMG recorded", "yes",
            f"{bio_down} statuses bio down, {bio_up} up; emg in sens files={emg_in_sens}; emg_burst events={bursts}",
            bio_down > 0 and bio_up == 0 and not emg_in_sens and bursts == 0),
        Row("Fault B: flinch EMG flagged missing with a reason in analytics", "missing + reason",
            json.dumps({"quality": (q or {}).get("quality"), "reasons": (q or {}).get("quality_reasons")}), flagged),
    ]


def check_fault_hub_absent(events: List[Dict[str, Any]], headset_report: Dict[str, Any], outage_s: float,
                           events_received: Tuple[int, int], run_complete: bool, uploads_ok: bool) -> List[Row]:
    attempts = headset_report.get("upload_attempts", {})
    retried = {k: v for k, v in attempts.items() if v > 1}
    reconnects = headset_report.get("reconnects", 0)
    return [
        Row(f"Fault C: hub absent {outage_s:.0f} s: run completes", "yes", f"run complete={run_complete}", run_complete),
        Row("Fault C: websocket re-established after the outage", ">= 1 reconnect", f"{reconnects} reconnect(s)", reconnects >= 1),
        Row("Fault C: upload retried until it landed", "retried and all 200/201",
            f"retried: {retried or 'none'}; all uploaded={uploads_ok}", bool(retried) and uploads_ok),
        Row("Fault C: every trial_event arrived exactly once after resume", "all",
            f"{events_received[0]}/{events_received[1]}", events_received[0] == events_received[1] > 0),
    ]


# ------------------------------------------------------------------ the table
def core_rows(*, events: List[Dict[str, Any]], twin_log: List[Dict[str, Any]], params: Dict[str, Any],
              rtt_ms: Sequence[float], invalid: Optional[int], invalid_msgs: Iterable[str], status_seen: int,
              validate_rc: Optional[int], uploads: Dict[str, bool], expected_files: Sequence[str],
              landed: Optional[Sequence[str]], events_received: Optional[Tuple[int, int]],
              metrics: Optional[Dict[str, Any]], analytics_rc: Optional[int], twin_expected: bool = True) -> List[Row]:
    return [
        check_phases(events, threat_enabled=bool(params.get("threat_enabled", True))),
        check_acks(events, twin_log, twin_expected=twin_expected),
        check_sync_timing(events, float(params.get("tactile_lead_ms", 40))),
        check_async_delay(events, float(params.get("tactile_lead_ms", 40))),
        check_live(rtt_ms, invalid, status_seen, invalid_msgs),
        check_session(validate_rc, uploads, expected_files, landed, events_received),
        check_embodiment(metrics, events, analytics_rc),
    ]


def render_text(rows: List[Row], title: str = "G2 / L3 TABLE") -> str:
    w = max(len(r.check) for r in rows) if rows else 10
    lines = [f"{title}", "-" * 100, f"{'':6}{'Check':<{w}}  {'Threshold':<30}  Observed", "-" * 100]
    for r in rows:
        lines.append(f"{('PASS' if r.ok else 'FAIL' if r.gating else 'WARN'):<6}{r.check:<{w}}  {r.threshold:<30}  {r.observed}")
    lines.append("-" * 100)
    warn = sum(1 for r in rows if not r.ok and not r.gating)
    lines.append(f"{sum(r.passed for r in rows)}/{len(rows)} checks passed"
                 f"{f' ({warn} non-gating WARN)' if warn else ''} -> {'G2 L3 GREEN' if all(r.passed for r in rows) else 'NOT GREEN'}")
    return "\n".join(lines)


def render_markdown(rows: List[Row], title: str = "G2 / L3 table") -> str:
    out = [f"### {title}", "", "| Result | Check | Threshold | Observed |", "|---|---|---|---|"]
    for r in rows:
        out.append(f"| {'PASS' if r.ok else 'FAIL' if r.gating else 'WARN'} | {r.check} | {r.threshold} | {r.observed.replace('|', '/')} |")
    out.append("")
    out.append(f"{sum(r.passed for r in rows)}/{len(rows)} checks passed.")
    return "\n".join(out)
