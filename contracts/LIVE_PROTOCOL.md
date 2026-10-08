# Chetna Live Protocol v1 (headset ↔ hub), contracts v0.2

Schema: `schemas/live-message.schema.json`. Opus-owned contract; agents implement it, they don't change it.

## Roles and topology
- **Hub** = the Flutter app, running as a **server** on the Windows desktop build (clinic PC or tablet with dart:io) and Android. The web build can't host, so it is a viewer only (Phase 3 cloud relay).
- **Headset** = Quest/Unity app (or Unity Editor Play Mode, or `sim/fake_headset.py`) as a **client**.
- Phase 3: a cloud relay speaks the same protocol, so home mode needs no protocol change.

## Discovery and pairing (no hardcoded IPs, ever)
1. Hub advertises mDNS/DNS-SD service `_opus-hub._tcp` (TXT: `v=1`, `hub_id`, `name`, `ws=/opus/v1/live`, `http=/opus/v1`).
2. Hub also sends a UDP beacon every 1 s to broadcast **and** subnet-broadcast on port **8788**: `{"opus_hub":1,"hub_id":…,"port":8787,"name":…}`. This is the fallback where mDNS is blocked (the old pipeline's lesson).
3. Manual fallback: the hub shows a **QR / 6-char pairing code** plus IP:port; the headset has a manual entry field.
4. Headset connects to `ws://<host>:8787/opus/v1/live`. The first message must be `hello`; the hub replies `hello_ack`. Pairing token: the hub issues `payload.pair_token` in hello_ack on first pairing, and the headset stores it and sends it in later hellos. (v1 LAN trust model; TLS + real auth in Phase 3.)

## Channels
| Channel | Transport | Rate | Content |
|---|---|---|---|
| Live | WebSocket JSON text frames | status ≤ 5 Hz, trial_event as it happens, metrics_tick per trial | small messages only (< 4 KB) |
| Control | same WebSocket, `requires_ack: true` | on demand | assign_program, command. The receiver must `ack` within 1 s |
| Bulk | HTTP `PUT /opus/v1/sessions/{session_id}/files/{name}` on the hub (port 8787) | after each 5 s chunk (live mode) or at session end | session.json, events.ndjson, kin_###.json, metrics.json. Idempotent by (session_id, name) + sha256; 201 new / 200 identical / 409 mismatch |
| Viewer (v0.2) | HTTP `GET /opus/v1/live/last_status` on the hub | polled by viewers (e.g. `tools/demo/live_plot.py --hub`) | 200 JSON `{"status": <latest headset status payload incl. game_state, trace, plus `_recv_ms` hub receive epoch ms> or null, "events": [<last ≤ 60 trial_event payloads, oldest first>]}`. Read-only, unauthenticated (LAN trust model, like /health); carries patient_ref, never PII. Not cleared on disconnect |

Never send kinematics frames over the live WebSocket.

## v0.2 additions — Phantom Hand (contracts v0.2, backward compatible)
**Operator commands** (`command` message, `requires_ack: true`, ack within 1 s). Added to the existing `start | pause | resume | stop | recenter | skip_block | adjust_params | show_message`:
| command | params | Effect |
|---|---|---|
| `phase_next` | none | Skip to the next phase of the run (for example end an induction early). Ignored when no run is active |
| `set_condition_order` | `{"condition_order":"sync_first"\|"async_first"}` (required) | Sets the order for the NEXT run; rejected (`ack.ok=false`) once a run has started. Same value domain as the manifest param |
| `abort_phase` | none | Abort the current phase (stop motors, hide stimuli) and go to the next safe phase; the event log records the abort |
| `next_person` | none | End the current run if needed, reset the scene and counters, open a new session for the next participant (the demo's "twice in a row without a reset") |

All four are idempotent where it makes sense and never block the headset's loop.

**`status.payload.game_state`** (optional, sent while a game that supports it runs, at the normal status rate ≤ 5 Hz):
```json
{"phase":"induction","condition":"sync","remaining_s":41.5,
 "nodes":{"haptic":{"connected":true},"bio":{"connected":true,"emg_level":0.07}}}
```
`phase` is the phase name used in `phase_start` events; `condition` is `sync`, `async` or `null`; `remaining_s` may be `null`; `emg_level` is the normalised EMG (0 = rest, 1 = MVC) or `null`. All four keys are required when `game_state` is present.

**`status.payload.trace`** (optional, 03-SPEC D4): a down-sampled live trace the headset forwards for the app's plots.
```json
{"emg_env":[412.5,415.1,418.9],"accel_mag":[9.8,9.79,9.81],"t0_ms":96400,"fs_hz":20}
```
`fs_hz` is fixed at 20; each array holds the newest samples since the previous status (at most 100 per array, and keep the whole message under 4 KB); `t0_ms` is the session time of the first sample. The app concatenates successive traces. EMG is in raw ADC units, `accel_mag` in m/s2.

**`file_available`** names may now also be `sens_###.json` (sensor files, see `schemas/sensor-file.schema.json`), uploaded over the same bulk `PUT` route as `kin_###.json`. The `trial_event` payload is the event schema, so the new Phantom Hand event types flow through unchanged.

## Reliability rules
- Heartbeat: `ping` every 1 s from both sides; `pong` echoes `echo_ts_ms`. **RTT is measured continuously** and shown in the app. After 3 missed pongs → disconnected.
- Reconnect: exponential backoff 0.5 s → 8 s max, rediscovering if the host changed.
- Resume: each side keeps an **outbox of unacked requires_ack messages and all trial_events since the last `hello_ack`**. On reconnect, `hello.resume_from_seq` tells the peer what arrived; the peer replays anything after it. Receivers de-dup by `id`.
  - **Known deviation of the operator app's hub (2026-10-08, deliberate until after the event):** its `hello_ack.resume_from_seq` repeats the number the headset sent (the last HUB seq the headset saw) instead of the last HEADSET seq the hub has, and it does not remember message ids across a reconnect (`hub_server.dart`, `hub_connection.dart`). So after a reconnect the headset replays the session's trial events and the app sees them again. The app's consumers are idempotent, and the Phantom Hand live card **depends** on that replay: it starts a fresh model for the new connection and gets its witness numbers and markers back from the replayed events. Fix the hub only together with a card state that survives a reconnect; tests in `app/test` pin today's behaviour. `sim/live/fake_hub.py` sends no `resume_from_seq` in `hello_ack` and de-dups by id.
- The session keeps running on the headset during a disconnect (local-first). Files upload when the connection returns.
- Command safety: `stop`/`pause` are idempotent. `adjust_params` is validated against the game manifest on the headset and rejected with `ack.ok=false` + error if invalid.

## Latency targets (goal G2)
- LAN: hub→headset command applied < 250 ms p95; headset→hub trial_event visible in UI < 250 ms p95.
- Measure in the E2E harness from `ts_ms` + `ack.recv_ts_ms`, and via RTT. Report p50/p95/max.

## Typical flow
```
headset hello → hub hello_ack
hub assign_program(requires_ack) → headset ack
headset status(paired → calibrating → ready)
hub command start(requires_ack) → ack → session_started
loop: status(running) · trial_event… · metrics_tick · file_available + HTTP PUT kin_###.json
hub command pause/resume/adjust_params/stop → ack
session_ended → HTTP PUT session.json, events.ndjson → hub stores in its sessions dir → app renders the report
```
Analytics (Python) runs on hub-stored sessions via `python -m opus_analytics <dir>` (the hub can trigger it on desktop). metrics.json then appears in the report.
