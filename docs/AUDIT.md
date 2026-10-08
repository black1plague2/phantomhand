# Audit of the Reference Pipeline (Aastheen V1 → Verse V2)

Audited 2026-09-14 by Opus. Sources: `VR_games/balloon` (branch `pose-baseline`, HEAD `e04d11f`),
`VR_games/anubhav`, GitHub `Nainikap/aastheen@main` (pushed 2026-08-23), plus earlier session notes.

## 1. How the old pipeline works
```
Flutter app ──REST──► FastAPI (Render / VPS / Neon, changed 3×) ──► Postgres
   │ creates prescription → 5-digit code
Quest (Bootstrap scene) ── POST /prescriptions/verify {code} ──► session JWT + prescription
   │ loads game scene by game_id (balloon | garden | pose | paintwall | trophy | multi)
   └── end of session: POST /sessions/{id}/results {game_metrics, emg_samples, imu_samples}
V1 legacy (still in repo): Flutter WS server :8765 + UDP discovery :8766 ↔ Quest WSClient
```

## 2. Findings, by the user's four known pain points

### A. Scalability / adding games
| # | Finding | Evidence | Impact |
|---|---|---|---|
| A1 | Every game is hardcoded in **4 places**: Unity DataModels, VerseClient parse branch, Flutter settings form, backend | `VerseClient.cs` has `if gameId=="garden" … else if "paintwall"` with a two-pass `JsonUtility` re-parse per game; `PrescriptionPublic` has one `[NonSerialized]` field per game | New game = cross-team change across 3 repos |
| A2 | `JsonUtility` can't handle polymorphic or dictionary params | the two-pass parse workaround | Forces per-game C# types in core code |
| A3 | One Unity project holds all games; each `GameManager` is a monolith (23 KB) | `Assets/Scripts/*` flat plus game subfolders | Merge conflicts, APK bloat, one bad game breaks the build |
| A4 | Prescription = **one game** and becomes `completed` after a single session | `sessions.py`: `prescription.status = "completed"` | No real programs (3×/week for 6 weeks) |
| A5 | `Base.metadata.create_all` plus an ad-hoc `migrate.py`, no Alembic | `main.py` | Schema drift across Render/VPS/Neon (DB was reset once) |
| A6 | Infra hopped Render → VPS → Render+Neon; URL baked into APK and PlayerPrefs | `VerseClient.BaseUrl` comments | Every infra change needs a rebuild |
| A7 | Game registry hardcoded in Flutter | `games_page.dart`, `admin/games_catalog.dart` | App release needed per game |

### B. Latency
| # | Finding | Impact |
|---|---|---|
| B1 | Free-tier Render cold start; `req.timeout = 40` s in VerseClient | Patient waits up to 40 s at the code screen |
| B2 | No live channel in V2: clinician sees nothing until the session ends | Can't supervise, pause or adjust |
| B3 | V1 LAN path needed a Windows hotspot, a firewall rule, and custom UDP broadcast; phone hotspots fail (AP isolation) | Fragile clinic setup |
| B4 | All sensor data in one final JSON POST | Large payload on weak Wi-Fi; one failure loses the whole session |

### C. Neuroscientific analysis
| # | Finding | Impact |
|---|---|---|
| C1 | Only **game-level aggregates** are stored (score, accuracy, avg RT, streak, spatial L/C/R) | Game performance ≠ motor recovery; no clinical validity |
| C2 | No raw hand/head trajectories are recorded | SPARC, jerk, ROM, path efficiency and compensation can't be computed |
| C3 | EMG/IMU as JSON arrays in a Postgres row (`emg_samples JSON`) | Doesn't scale; no time alignment with game events |
| C4 | No trial/event model (target onset, movement onset, grasp, release) | RT and movement time are approximate |
| C5 | No data-quality info (hand-tracking loss, confidence) | Metrics silently wrong when tracking drops |
| C6 | No baseline/calibration (arm length, comfortable workspace) | Metrics not normalized per patient |
| C7 | CBS/MPT scores are manually typed sliders, not linked to standard outcome measures (FMA-UE, ARAT, MAS, Box & Block) | No longitudinal clinical story |

### D. Flutter app
| # | Finding |
|---|---|
| D1 | No state-management architecture; services are singletons called from pages |
| D2 | Dead V1 code still shipped: `balloon_session.dart`, `game_session.dart`, `websocket.dart`, `camera`, `image`, `image_picker` deps |
| D3 | Hardcoded/fake dashboard data (`d_home.dart`); a `.dart` file sitting in `.github/workflows/` |
| D4 | No offline support, no typed API client, no tests (only the default `widget_test.dart`) |
| D5 | `fl_chart ^0.68` (old); no report export, no accessibility or i18n work |
| D6 | Per-game settings forms hand-written |
| D7 | Weak role model (doctor/staff/admin) and no patient-facing experience |

### E. Security / ops (extra findings)
| # | Finding | Severity |
|---|---|---|
| E1 | **GitHub OAuth token embedded in `VR_games/anubhav` git remote URL** | HIGH: revoke now |
| E2 | Unity MCP bearer tokens stored in plaintext notes and `.mcp.json` | Medium |
| E3 | 5-digit numeric code, SHA-256 hashed with no salt → 100 k keyspace, brute-forceable offline | Medium (a rate limiter exists) |
| E4 | CORS hardcoded to the VPS Tailscale URL | Low |
| E5 | APKs, `args*.json` and screenshots committed or left in the Unity repo root | Low (hygiene) |
| E6 | No audit log or consent records for health data (DPDP Act 2023 / HIPAA expectations) | Medium for any real deployment |

## 3. What to keep (worth carrying forward as ideas)
- Prescription code → headset auth UX (patients can't type emails in VR). Keep it, but harden: 6+ chars, per-device pairing, expiry.
- Clinical game modes from Balloon: sequence, reaction, USN (neglect), adaptive difficulty.
- Ghost-hand guide + inactivity guide.
- Clinic mode (LAN) vs home mode (cloud) split, done with mDNS and a local-first outbox instead of custom UDP.
- The AAR namespace patcher for Meta XR SDK builds.
