# Analytics prompts (ph-builder, Sonnet)

Every prompt starts with: "Read CLAUDE.md, docs/agent-briefs/ph/PRD_v2.md (FR-AN-01, §12), 01–04,
analytics/README.md, analytics/VALIDATION.md, analytics/opus_analytics/**." Owns analytics/**.

---

## N1 — Embodiment metrics (FR-AN-01, MVP)  · Oct 8 · branch ph/n1-embodiment · timebox 3 h

```text
1) io_session: read sens_###.json (EMG envelope + IMU, contracts/schemas/sensor-file.schema.json) and the new
   events; orchard sessions read exactly as before.
2) opus_analytics/metrics/embodiment.py, per condition (03-SPEC §8):
   - drift_change_cm = post.drift_cm − pre.drift_cm (confirmed probes only; else quality missing).
   - ownership = mean(q1,q2); control = q3.
   - flinch recomputed from raw streams (don't trust on-device numbers): baseline 2 s before impact_ms; EMG
     envelope peak ÷ baseline RMS and latency (first sample > baseline + 3 SD for ≥ 30 ms); IMU |a| − baseline mean
     → peak + latency; stimulated wrist speed from kinematics (existing filtering) → peak + latency (> 0.15 m/s).
   - EMG gating: exclude samples within [cue send, send + 200 + 50] ms of every delivered haptic_cue; count
     emg_windows_excluded; > 50 % of the response window excluded → degraded.
   - stroke_timing_err_ms {mean, p95} (SYNC), async delay distribution, cue_delivery_rate.
   - sync_minus_async for drift, ownership, flinch size; quality per value with reasons (rate_hz < 45, sensor gaps
     > 200 ms, node absent, probe unconfirmed).
3) `python -m opus_analytics <session_dir>` writes metrics.json with `embodiment`, valid against
   contracts/schemas/metrics.schema.json; a `--summary` flag prints the witness numbers in one table (used on stage
   if the app report isn't ready).
Tests: hand-made signals with known latencies (±5 ms) and peaks; gating cases; missing-node cases; the contracts
fixture phantom_hand_min end to end; existing tests green (≥ O0 floor). Paste pytest output.
```

---

## N2 — Validation vs synthetic truth (STRETCH, needs S3) · branch ph/n2-validation

```text
analytics/scripts/validate_embodiment.py: 20 seeds × both orders × (all nodes | no bio | no haptic) from S3's
generator; error < 10 % on drift/ownership/latency for complete sessions; correct quality flags for degraded ones;
contrast signs right in ≥ 95 % of seeds. Results to analytics/validation/embodiment/<date>/; add an Embodiment
section to analytics/VALIDATION.md.
```
