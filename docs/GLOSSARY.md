# Glossary

Short reference for terms used across the docs, contracts, and code.

| Term | Meaning |
|---|---|
| **Chetna** | The product name (चेतना — Sanskrit/Hindi for consciousness/awareness). Contracts-first VR upper-limb neurorehabilitation platform. |
| **OPUS** | Internal codename from the rebuild; survives in code identifiers (`com.opus.sdk`, `opus_analytics`, `opus_app`, `OpusTokens`, `OPUS_HT_FAST_MOTION`, mDNS `_opus-hub._tcp`, URL prefix `/opus/v1`). Not used in new prose. |
| **Opus** (agent) | The planning/review agent role in `docs/PLAN.md` and session logs — a role name, not the product. |
| **ISDK** | Meta Interaction SDK — the Unity package whose `IHand` interface is the raw source of hand data (read *before* any HandFilter). |
| **HT frequency** | Hand-tracking frequency setting on `OVRProjectConfig`. Kept at **Low (Default)**; see `docs/UNITY_PRACTICES.md`. |
| **FMM** | Fast Motion Mode — what the "High" HT frequency label actually is. Raises jitter; bad for slow, precise rehab reaches. Kept off (`OPUS_HT_FAST_MOTION` exists only for A/B tests). |
| **SPARC** | Spectral arc length — a movement-smoothness biomarker computed by `opus_analytics`. More negative = less smooth. |
| **LDLJ** | Log dimensionless jerk — a second smoothness biomarker, cross-checked against SPARC. |
| **rate_hz** | The measured (not assumed) tracking rate recorded into `session.json` and propagated with every metric. |
| **degraded** | Quality flag on a metric: set when `rate_hz < 45` or tracking loss exceeds the documented threshold (see `analytics/VALIDATION.md`). |
| **Contracts** | The JSON Schemas under `contracts/schemas/` — the only coupling between game, app, and analytics. Change only via the project lead. |
| **Quest** | Meta Quest headset running the pure-VR game with hand tracking (no controllers). |
