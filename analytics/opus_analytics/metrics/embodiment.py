"""Phantom Hand embodiment metrics (FR-AN-01, 03-SPEC section 8 and 12).

compute_embodiment(events, data) -> the `embodiment` object of metrics.json:
    {"condition_order": [...], "sync": {id: metric}, "async": {id: metric}, "sync_minus_async": {id: metric}}

Conventions
- condition = trial index of the Phantom Hand event stream (events carry `trial`; condition name sync|async comes
  from the phase_start events). All event fields live in `data`. Positions are metres, drift_cm is + toward the
  virtual hand. Time is session ms (events, kin chunks and sens chunks share the clock).
- Flinch metrics are recomputed from the raw streams; the on-device `threat_response` is never used.
  Baseline = the 2 s before `threat_impact.impact_ms`; response window = 1.5 s after it.
- EMG (and IMU, which sits on the same sleeve as the motors) samples inside [cue send, send + 200 + 50] ms of every
  delivered haptic_cue (and every self_touch cue) are excluded; samples flagged `motor_excl` in the file are
  excluded too. Excluded samples never contribute to baseline, peak or latency and never join a latency run.
- Every value: {value, unit, method_version, quality ok|degraded|invalid|missing, quality_reasons?, n?}.
  `missing` means value null (sensor absent, window fully excluded, probe unconfirmed, ...).
- Touch delivery (N1b). Per condition, delivery = delivered haptic_cue / scheduled haptic_cue.
  cue_delivery_rate: missing [no_cues, node_absent_haptic] when no cue was scheduled; degraded
  [cues_undelivered] when delivery < CUE_DELIVERY_MIN (0.9). stroke_timing_err_ms (mean and p95) is degraded
  [cues_undelivered] under the same rule and [few_delivered_strokes_<n>_lt_<MIN>] when fewer than
  MIN_DELIVERED_CUES (6) cues were delivered (cues exist). When delivery < TOUCH_INCOMPLETE_MAX (0.5) the touch
  condition is compromised: every non-missing induction-dependent value of that condition (drift, ownership,
  control, flinch_*) is degraded with reason `touch_incomplete` (the sync_minus_async contrast inherits it as
  `sync:`/`async:` reasons). Thresholds are documented in analytics/VALIDATION.md section 13.
"""
from __future__ import annotations

import numpy as np

from ..kinematics import derive_kinematics, slice_window

METHOD_VERSION = "embodiment_v0.1.0"

BASELINE_MS = 2000.0
RESPONSE_MS = 1500.0
CUE_EXCL_MS = 200.0 + 50.0           # pulse + 50 ms (FR-AN-01)
LATENCY_MIN_DUR_MS = 30.0            # signal must stay above threshold at least this long
SD_K = 3.0
GAP_MS = 200.0                       # sensor gap that degrades a window
RATE_HZ_MIN = 45.0
WRIST_THRESH_MPS = 0.15
EXCL_FRAC_DEGRADED = 0.5
CUE_DELIVERY_MIN = 0.9               # below this: cue_delivery_rate / stroke timing degraded (cues_undelivered)
TOUCH_INCOMPLETE_MAX = 0.5           # below this: induction-dependent values degraded (touch_incomplete)
MIN_DELIVERED_CUES = 6               # fewer delivered cues -> stroke timing rests on too few delivered strokes
MIN_BASELINE_SAMPLES = 10
IMU_SD_FLOOR = 0.05                  # m/s2, avoids a zero-SD threshold on a noiseless baseline

CONTRAST_IDS = [
    "drift_change_cm", "ownership", "control",
    "flinch_emg_peak_x", "flinch_emg_latency_ms",
    "flinch_imu_peak", "flinch_imu_latency_ms",
    "flinch_wrist_peak_mps", "flinch_wrist_latency_ms",
    "witness_q4",
]


# ---------------------------------------------------------------- value helpers
def _val(value, unit, quality="ok", reasons=None, n=None) -> dict:
    out = {"value": None if value is None else float(value), "unit": unit,
           "method_version": METHOD_VERSION, "quality": quality}
    if reasons:
        out["quality_reasons"] = list(reasons)
    if n is not None:
        out["n"] = int(n)
    return out


def _missing(unit, *reasons) -> dict:
    return _val(None, unit, "missing", list(reasons))


_RANK = {"ok": 0, "degraded": 1, "invalid": 2, "missing": 3}


def _worse(a: str, b: str) -> str:
    return a if _RANK[a] >= _RANK[b] else b


def has_phantom_hand_events(events: list[dict]) -> bool:
    return any(e.get("type") in ("phase_start", "threat_impact", "drift_probe") for e in events)


# ---------------------------------------------------------------- signal helpers
def cue_windows(events: list[dict]) -> list[tuple[float, float]]:
    """Exclusion windows [send, send + 250] ms for every delivered haptic_cue and every self_touch cue."""
    wins = []
    for e in events:
        d = e.get("data") or {}
        if e.get("type") == "haptic_cue" and d.get("delivered"):
            wins.append((float(e["t_ms"]), float(e["t_ms"]) + CUE_EXCL_MS))
        elif e.get("type") == "self_touch" and d.get("cue_send_ms") is not None:
            wins.append((float(d["cue_send_ms"]), float(d["cue_send_ms"]) + CUE_EXCL_MS))
    return sorted(wins)


def excluded_mask(t: np.ndarray, wins: list[tuple[float, float]], file_flags=None) -> np.ndarray:
    mask = np.zeros(len(t), dtype=bool)
    for a, b in wins:
        mask |= (t >= a) & (t <= b)
    if file_flags is not None and len(file_flags) == len(t):
        mask |= np.asarray(file_flags, dtype=bool)
    return mask


def first_sustained_crossing(t, above, valid, min_dur_ms=LATENCY_MIN_DUR_MS, interp=None):
    """Time of the first sample of a run of consecutive valid&above samples lasting >= min_dur_ms.
    interp: optional (signal, threshold) -> refine the onset by linear interpolation of the threshold crossing
    between the previous valid sample and the first one above. Returns time or None."""
    ok = above & valid
    n = len(t)
    i = 0
    while i < n:
        if not ok[i]:
            i += 1
            continue
        j = i
        while j + 1 < n and ok[j + 1]:
            j += 1
        if t[j] - t[i] >= min_dur_ms - 1e-9:
            t0 = float(t[i])
            if interp is not None and i > 0 and valid[i - 1]:
                sig, thr = interp
                lo, hi = sig[i - 1], sig[i]
                if hi > lo and lo <= thr:
                    t0 = float(t[i - 1] + (thr - lo) / (hi - lo) * (t[i] - t[i - 1]))
            return t0
        i = j + 1
    return None


def _gap_ms(t_in: np.ndarray, lo: float, hi: float) -> float:
    edges = np.concatenate([[lo], t_in, [hi]])
    return float(np.max(np.diff(edges))) if len(edges) > 1 else hi - lo


def _window_quality(t_all, valid_all, lo, hi, rate_hz, reasons: list[str], resp_lo=None) -> str:
    """Reasons + quality for a response window: rate, sensor gaps, exclusion fraction."""
    q = "ok"
    if rate_hz is not None and rate_hz < RATE_HZ_MIN:
        reasons.append(f"rate_hz_{rate_hz:.1f}_below_{RATE_HZ_MIN:.0f}")
        q = "degraded"
    sel = (t_all >= lo) & (t_all <= hi)
    gap = _gap_ms(t_all[sel], lo, hi)
    if gap > GAP_MS:
        reasons.append(f"sensor_gap_{gap:.0f}ms_gt_{GAP_MS:.0f}")
        q = "degraded"
    rsel = (t_all >= (hi - RESPONSE_MS if resp_lo is None else resp_lo)) & (t_all <= hi)
    if rsel.any():
        frac = float(np.mean(~valid_all[rsel]))
        if frac > EXCL_FRAC_DEGRADED:
            reasons.append(f"motor_exclusion_{frac:.2f}_of_response_window_gt_0.5")
            q = "degraded"
    return q


# ---------------------------------------------------------------- flinch from raw streams
def _stream_flinch(t, sig, valid, rate_hz, impact, floor_sd, unit_peak, unit_name, absdev):
    """Shared baseline/peak/latency logic. sig is the scalar stream; absdev=True takes |sig - baseline mean|.
    Returns (peak_metric_value_dict_fields..., latency) via a dict."""
    b_lo, b_hi = impact - BASELINE_MS, impact
    r_lo, r_hi = impact, impact + RESPONSE_MS
    bsel = (t >= b_lo) & (t < b_hi) & valid
    rsel = (t >= r_lo) & (t <= r_hi)
    res = {"ok": False}
    if not (t >= b_lo).any() or not rsel.any():
        res["why"] = "no_samples_in_window"
        return res
    if bsel.sum() < MIN_BASELINE_SAMPLES:
        res["why"] = "baseline_unavailable_or_excluded"
        return res
    if not (rsel & valid).any():
        res["why"] = "response_window_fully_excluded"
        return res
    base = sig[bsel]
    mean = float(np.mean(base))
    sd = max(float(np.std(base)), floor_sd(mean))
    rms = float(np.sqrt(np.mean(base ** 2)))
    resp = np.where(rsel, sig, np.nan)
    dev = np.abs(sig - mean) if absdev else sig
    thr = (SD_K * sd) if absdev else (mean + SD_K * sd)
    peak_src = np.where(rsel & valid, dev, -np.inf)
    peak = float(np.max(peak_src))
    seg = np.where(rsel, 1, 0).astype(bool)
    idx = np.where(seg)[0]
    lat_t = first_sustained_crossing(t[idx], dev[idx] > thr, valid[idx], interp=(dev[idx], thr))
    reasons: list[str] = []
    q = _window_quality(t, valid, b_lo, r_hi, rate_hz, reasons)
    res.update(ok=True, peak=peak, mean=mean, sd=sd, rms=rms, latency=None if lat_t is None else lat_t - impact,
               quality=q, reasons=reasons, n=int(bsel.sum()), thr=thr)
    return res


def _flinch_emg(emg, wins, impact):
    out = {}
    if emg is None:
        for k, u in (("flinch_emg_peak_x", "x_baseline_rms"), ("flinch_emg_latency_ms", "ms")):
            out[k] = _missing(u, "node_absent_bio")
        return out
    t, sig = emg["t_ms"], emg["value"]
    valid = ~excluded_mask(t, wins, emg.get("motor_excl"))
    r = _stream_flinch(t, sig, valid, emg.get("rate_hz"), impact,
                       floor_sd=lambda m: max(0.01 * abs(m), 0.5), unit_peak="x", unit_name="emg", absdev=False)
    if not r["ok"]:
        out["flinch_emg_peak_x"] = _missing("x_baseline_rms", r["why"])
        out["flinch_emg_latency_ms"] = _missing("ms", r["why"])
        return out
    reasons = list(r["reasons"])
    if r["rms"] <= 0:
        out["flinch_emg_peak_x"] = _missing("x_baseline_rms", "baseline_rms_zero")
    else:
        out["flinch_emg_peak_x"] = _val(r["peak"] / r["rms"], "x_baseline_rms", r["quality"], reasons, r["n"])
    if r["latency"] is None:
        out["flinch_emg_latency_ms"] = _missing("ms", "no_flinch_detected")
    else:
        out["flinch_emg_latency_ms"] = _val(r["latency"], "ms", r["quality"], reasons, r["n"])
    return out


def _flinch_imu(imu, wins, impact):
    out = {}
    if imu is None:
        out["flinch_imu_peak"] = _missing("m/s2", "node_absent_haptic")
        out["flinch_imu_latency_ms"] = _missing("ms", "node_absent_haptic")
        return out
    t = imu["t_ms"]
    mag = np.sqrt(imu["ax"] ** 2 + imu["ay"] ** 2 + imu["az"] ** 2)
    valid = ~excluded_mask(t, wins)
    r = _stream_flinch(t, mag, valid, imu.get("rate_hz"), impact,
                       floor_sd=lambda m: IMU_SD_FLOOR, unit_peak="m/s2", unit_name="imu", absdev=True)
    if not r["ok"]:
        out["flinch_imu_peak"] = _missing("m/s2", r["why"])
        out["flinch_imu_latency_ms"] = _missing("ms", r["why"])
        return out
    out["flinch_imu_peak"] = _val(r["peak"], "m/s2", r["quality"], r["reasons"], r["n"])
    if r["latency"] is None:
        out["flinch_imu_latency_ms"] = _missing("ms", "no_flinch_detected")
    else:
        out["flinch_imu_latency_ms"] = _val(r["latency"], "ms", r["quality"], r["reasons"], r["n"])
    return out


def _flinch_wrist(joints, rate_hz, impact, joint="r_wrist"):
    out = {}
    j = joints.get(joint) if joints else None
    if j is None:
        out["flinch_wrist_peak_mps"] = _missing("m/s", f"no_{joint}_track")
        out["flinch_wrist_latency_ms"] = _missing("ms", f"no_{joint}_track")
        return out
    lo, hi = impact - 1000.0, impact + RESPONSE_MS
    w = slice_window(j, lo, hi)
    t = w["t_ms"]
    if len(t) < 10 or t[0] > impact or t[-1] < impact + 0.5 * RESPONSE_MS:
        out["flinch_wrist_peak_mps"] = _missing("m/s", "no_samples_in_window")
        out["flinch_wrist_latency_ms"] = _missing("ms", "no_samples_in_window")
        return out
    conf = w["conf"]
    kin = derive_kinematics(w["pos"], rate_hz)
    speed = kin["speed"]
    reasons: list[str] = []
    q = "ok"
    if rate_hz < RATE_HZ_MIN:
        reasons.append(f"rate_hz_{rate_hz:.1f}_below_{RATE_HZ_MIN:.0f}")
        q = "degraded"
    loss = float(np.mean(conf <= 0.0))
    if loss > 0.05:
        reasons.append(f"tracking_loss_{loss:.2f}_gt_0.05")
        q = "degraded"
    gap = _gap_ms(t, lo, hi)
    if gap > GAP_MS:
        reasons.append(f"sensor_gap_{gap:.0f}ms_gt_{GAP_MS:.0f}")
        q = "degraded"
    after = t >= impact
    out["flinch_wrist_peak_mps"] = _val(float(np.max(speed[after])), "m/s", q, reasons, int(after.sum()))
    idx = np.where(after)[0]
    ta, sa = t[idx], speed[idx]
    if sa[0] >= WRIST_THRESH_MPS:
        out["flinch_wrist_latency_ms"] = _missing("ms", "already_moving_at_impact")
        return out
    t0 = first_sustained_crossing(ta, sa > WRIST_THRESH_MPS, np.ones(len(ta), bool), min_dur_ms=0.0,
                                  interp=(sa, WRIST_THRESH_MPS))
    if t0 is None:
        out["flinch_wrist_latency_ms"] = _missing("ms", "no_withdrawal_detected")
    else:
        out["flinch_wrist_latency_ms"] = _val(t0 - impact, "ms", q, reasons, int(after.sum()))
    return out


# ---------------------------------------------------------------- per-condition
def _conditions(events):
    """[(trial_index, name)] in order of first appearance."""
    seen: dict[int, str] = {}
    for e in sorted(events, key=lambda e: (e["t_ms"], e.get("seq", 0))):
        if e.get("type") == "phase_start" and e.get("trial") is not None:
            c = (e.get("data") or {}).get("condition")
            if c in ("sync", "async") and e["trial"] not in seen:
                seen[e["trial"]] = c
    return list(seen.items())


def _drift(events, trial, t_cond_start):
    probes = [e for e in events if e.get("type") == "drift_probe"]
    post = [p for p in probes if p["data"].get("when") == "post" and p.get("trial") == trial]
    pre = [p for p in probes if p["data"].get("when") == "pre" and p.get("trial") == trial]
    shared = False
    if not pre:
        earlier = [p for p in probes if p["data"].get("when") == "pre" and p["t_ms"] < t_cond_start]
        if earlier:
            pre = [max(earlier, key=lambda p: p["t_ms"])]
            shared = True
    if not post:
        return _missing("cm", "post_probe_absent")
    if not pre:
        return _missing("cm", "pre_probe_absent")
    a, b = pre[-1]["data"], post[-1]["data"]
    if not (a.get("confirmed") and b.get("confirmed")):
        return _missing("cm", "probe_unconfirmed")
    reasons = ["shared_pre_probe"] if shared else []
    return _val(b["drift_cm"] - a["drift_cm"], "cm", "ok", reasons, 2)


def _questionnaire(events, trial):
    items: dict[str, float] = {}
    for e in events:
        if e.get("type") == "questionnaire_item" and e.get("trial") == trial:
            items[e["data"]["item"]] = float(e["data"]["value"])
    out = {}
    own = [items[k] for k in ("q1", "q2") if k in items]
    if len(own) == 2:
        out["ownership"] = _val(np.mean(own), "likert_1_7", "ok", None, 2)
    elif len(own) == 1:
        out["ownership"] = _val(own[0], "likert_1_7", "degraded", ["only_one_of_q1_q2"], 1)
    else:
        out["ownership"] = _missing("likert_1_7", "questionnaire_absent")
    out["control"] = _val(items["q3"], "likert_1_7", "ok", None, 1) if "q3" in items else \
        _missing("likert_1_7", "q3_absent")
    out["witness_q4"] = _val(items["q4"], "likert_1_7", "ok", None, 1) if "q4" in items else \
        _missing("likert_1_7", "q4_absent")
    return out


def _stroke_metrics(events, trial, haptic_expected=True):
    strokes = [e["data"] for e in events if e.get("type") == "stroke" and e.get("trial") == trial]
    cues = [e["data"] for e in events if e.get("type") == "haptic_cue" and e.get("trial") == trial]
    out = {}
    errs = np.array([abs(float(s["timing_err_ms"])) for s in strokes if s.get("timing_err_ms") is not None])
    if len(errs):
        out["stroke_timing_err_ms"] = {"mean": _val(errs.mean(), "ms", "ok", None, len(errs)),
                                       "p95": _val(np.percentile(errs, 95), "ms", "ok", None, len(errs))}
    else:
        m = _missing("ms", "no_strokes")
        out["stroke_timing_err_ms"] = {"mean": m, "p95": dict(m)}
    delays = np.array([float(s["cue_a_send_ms"]) - float(s["brush_pass_a_ms"]) for s in strokes
                       if s.get("cue_a_send_ms") is not None and s.get("brush_pass_a_ms") is not None])
    if len(delays):
        out["async_delay_ms"] = {"mean": _val(delays.mean(), "ms", "ok", None, len(delays)),
                                 "p95": _val(np.percentile(delays, 95), "ms", "ok", None, len(delays))}
    else:
        m = _missing("ms", "no_strokes")
        out["async_delay_ms"] = {"mean": m, "p95": dict(m)}
    if cues:
        n_del = sum(1 for c in cues if c.get("delivered"))
        rate = n_del / len(cues)
        low = rate < CUE_DELIVERY_MIN
        out["cue_delivery_rate"] = _val(rate, "unitless", "degraded" if low else "ok",
                                        ["cues_undelivered"] if low else None, len(cues))
        for m in out["stroke_timing_err_ms"].values():
            if low:
                _degrade(m, "cues_undelivered")
            if n_del < MIN_DELIVERED_CUES:
                _degrade(m, f"few_delivered_strokes_{n_del}_lt_{MIN_DELIVERED_CUES}")
    else:
        out["cue_delivery_rate"] = _missing("unitless", "no_cues", "node_absent_haptic")
    return out


TOUCH_DEPENDENT = ("drift_change_cm", "ownership", "control", "flinch_emg_peak_x", "flinch_emg_latency_ms",
                   "flinch_imu_peak", "flinch_imu_latency_ms", "flinch_wrist_peak_mps", "flinch_wrist_latency_ms")


def _delivery(events, trial):
    cues = [e["data"] for e in events if e.get("type") == "haptic_cue" and e.get("trial") == trial]
    n = len(cues)
    d = sum(1 for c in cues if c.get("delivered"))
    return n, d


def _degrade(v, *reasons):
    if v is None or v.get("quality") in ("missing", "invalid"):
        return
    v["quality"] = _worse(v["quality"], "degraded")
    rs = v.setdefault("quality_reasons", [])
    for r in reasons:
        if r not in rs:
            rs.append(r)


def condition_metrics(events, data, trial, wins):
    t_start = min((e["t_ms"] for e in events if e.get("trial") == trial and e.get("type") == "phase_start"),
                  default=0.0)
    m: dict = {}
    m["drift_change_cm"] = _drift(events, trial, t_start)
    m.update(_questionnaire(events, trial))

    impacts = [e for e in events if e.get("type") == "threat_impact" and e.get("trial") == trial]
    if not impacts or not impacts[0]["data"].get("ok", True):
        why = "threat_impact_absent" if not impacts else "threat_impact_not_ok"
        for k, u in (("flinch_emg_peak_x", "x_baseline_rms"), ("flinch_emg_latency_ms", "ms"),
                     ("flinch_imu_peak", "m/s2"), ("flinch_imu_latency_ms", "ms"),
                     ("flinch_wrist_peak_mps", "m/s"), ("flinch_wrist_latency_ms", "ms")):
            m[k] = _missing(u, why)
        m["emg_windows_excluded"] = _missing("count", why)
    else:
        impact = float(impacts[0]["data"]["impact_ms"])
        m.update(_flinch_emg(data.emg, wins, impact))
        m.update(_flinch_imu(data.imu, wins, impact))
        m.update(_flinch_wrist(data.joints, data.rate_hz, impact))
        lo, hi = impact - BASELINE_MS, impact + RESPONSE_MS
        n_excl = sum(1 for a, b in wins if b >= lo and a <= hi)
        if data.emg is None:
            m["emg_windows_excluded"] = _missing("count", "node_absent_bio")
        else:
            m["emg_windows_excluded"] = _val(n_excl, "count", "ok", None, len(wins))
    m.update(_stroke_metrics(events, trial))
    n_cues, n_del = _delivery(events, trial)
    if n_cues and n_del / n_cues < TOUCH_INCOMPLETE_MAX:
        for k in TOUCH_DEPENDENT:
            _degrade(m.get(k), "touch_incomplete")
    return m


def _contrast(sync: dict, asy: dict) -> dict:
    out = {}
    for k in CONTRAST_IDS:
        a, b = sync.get(k), asy.get(k)
        unit = (a or b or {}).get("unit", "unitless")
        if a is None or b is None or a["value"] is None or b["value"] is None:
            reasons = []
            for name, v in (("sync", a), ("async", b)):
                if v is None or v["value"] is None:
                    reasons.append(f"{name}_value_missing")
            out[k] = _missing(unit, *reasons)
            continue
        q = _worse(a["quality"], b["quality"])
        reasons = [f"sync:{r}" for r in a.get("quality_reasons", [])] + \
                  [f"async:{r}" for r in b.get("quality_reasons", [])]
        out[k] = _val(a["value"] - b["value"], unit, q, reasons)
    return out


def compute_embodiment(events: list[dict], data) -> dict:
    conds = _conditions(events)
    wins = cue_windows(events)
    emb: dict = {"condition_order": [name for _, name in conds]}
    for trial, name in conds:
        emb[name] = condition_metrics(events, data, trial, wins)
    if "sync" in emb and "async" in emb:
        emb["sync_minus_async"] = _contrast(emb["sync"], emb["async"])
    return emb


# ---------------------------------------------------------------- witness table
SUMMARY_ROWS = [
    ("drift_change_cm", "Drift toward virtual hand (cm)"),
    ("ownership", "Ownership (1-7)"),
    ("control", "Control (1-7)"),
    ("flinch_emg_peak_x", "EMG flinch peak (x baseline)"),
    ("flinch_emg_latency_ms", "EMG flinch latency (ms)"),
    ("flinch_imu_peak", "IMU jolt (m/s2)"),
    ("flinch_wrist_peak_mps", "Wrist withdrawal peak (m/s)"),
    ("flinch_wrist_latency_ms", "Wrist latency (ms)"),
    ("witness_q4", "q4 awareness (pointer only)"),
]


def _fmt(v):
    if v is None:
        return "-"
    if v["value"] is None:
        return "n/a"
    s = f"{v['value']:.2f}"
    return s if v["quality"] == "ok" else s + "*"


def witness_table(emb: dict) -> str:
    """Plain-text sync / async / difference table; '*' = degraded, 'n/a' = missing."""
    lines = [f"{'metric':<34}{'SYNC':>10}{'ASYNC':>10}{'SYNC-ASYNC':>12}"]
    lines.append("-" * len(lines[0]))
    s, a, d = emb.get("sync", {}), emb.get("async", {}), emb.get("sync_minus_async", {})
    for key, label in SUMMARY_ROWS:
        lines.append(f"{label:<34}{_fmt(s.get(key)):>10}{_fmt(a.get(key)):>10}{_fmt(d.get(key)):>12}")
    for name in ("sync", "async"):
        sm = emb.get(name, {}).get("stroke_timing_err_ms")
        if sm:
            lines.append(f"{'Stroke timing err ' + name + ' (ms)':<34}{'mean ' + _fmt(sm['mean']):>14}"
                         f"{'p95 ' + _fmt(sm['p95']):>12}")
    lines.append(f"condition order: {', '.join(emb.get('condition_order', [])) or '-'}   "
                 f"(* = degraded, n/a = missing; see metrics.json for reasons)")
    return "\n".join(lines)
