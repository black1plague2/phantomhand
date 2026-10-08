# Chetna — Goal

## North star
A **clinical-grade VR neurorehabilitation platform** where:
1. A clinician prescribes a program (one or more games, each with its own parameters) from a clean app.
2. A patient plays on a Meta Quest, in the clinic or at home, with hand tracking.
3. The headset records **raw kinematics + trial events**, not just scores.
4. An analytics engine turns those into **validated movement-quality biomarkers** (smoothness, ROM, compensation, reaction/movement time, spatial neglect, fatigue), tracked over time.
5. **Adding a new game is a plug-in operation**: a scene + a manifest. No edits to the app, the backend, or the core SDK.

This is a clean rebuild. The old Aastheen/Verse pipeline is a **reference only**, not a base.

## Measurable success criteria (v1.0)
| # | Criterion | Target |
|---|---|---|
| G1 | New game added end-to-end | ≤ 1 day, 0 lines changed outside the game folder |
| G2 | Clinician sees live session status | < 250 ms on LAN, < 1 s over cloud |
| G3 | Session data survives network loss | 100 % (local-first outbox, verified by a sim test) |
| G4 | Analytics validity | SPARC/LDLJ/RT/ROM error < 5 % against synthetic ground truth |
| G5 | Tracking-quality awareness | Every metric carries a data-quality flag (tracking loss %, confidence) |
| G6 | App accessibility | WCAG 2.2 AA contrast, 200 % text scale, EN + HI |
| G7 | Scale | Backend handles 1 000 concurrent sessions in a load test |
| G8 | Everything tested in simulation | Meta XR Simulator + synthetic-patient generator; no headset needed until the pilot |

## Execution order (user-approved 2026-09-14)
1. **Phase 0**: contracts, docs, workspace (Opus)
2. **Phase 1, in parallel**: standalone Unity game + SDK · standalone Flutter app on mocks · analytics lib + synthetic patients
3. **Phase 2**: simulation test harness, versioned builds
4. **Phase 3**: bridge (backend + transports) built on the Phase 1 components
5. **Phase 4**: hardening, pilot on real Quest

## Haptics addendum (2026-09-18)
Optional vibrotactile form feedback is in scope as **software only**; the wearable is built by a separate Electronics team against contracts/HAPTIC_PROTOCOL.md.
Added success criteria:
| # | Criterion | Target |
|---|---|---|
| G9 | A cue reaches the sleeve fast enough to feel immediate | < 100 ms from trigger to datagram sent (measured against the software fake) |
| G10 | The game is never harmed by the sleeve | No blocking writes; a session with no sleeve, or one that disconnects mid-session, behaves identically apart from missing cues |
| G11 | Cues are auditable | Every cue is in events.ndjson as haptic_cue (with delivered true/false) and surfaced in the clinician's session report |
| G12 | Safety | Intensity ceiling per program, min gap between cues, watchdog stop, no PII in any haptic message |
