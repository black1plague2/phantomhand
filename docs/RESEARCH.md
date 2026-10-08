# Research Notes

Searched 2026-09-14 across Reddit, Quora, X/Twitter, forums, and literature. Reddit and Quora returned
almost nothing indexable for this niche; practitioner forums (Unity, Meta) and peer-reviewed sources
were far more useful. **Follow-up owed:** a Haiku agent should do a deeper manual pass
(r/physicaltherapy, r/OccupationalTherapy, r/stroke, r/OculusQuest, r/Unity3D, X: #VRrehab #neurorehab)
and append quotes and themes here.

## 1. Clinical metrics that matter (drives the analytics design)
- Validated smoothness metrics for upper-limb reaching after stroke: **SPARC, LDLJ, number of submovements, NARJ**.
  SPARC and LDLJ show excellent reliability (ICC > 0.9) and low measurement error.
  [JNER 2024](https://link.springer.com/article/10.1186/s12984-024-01382-1) · [PMC](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11134951/)
- Smoothness during reach-to-grasp tracks motor impairment longitudinally. [PMC8461930](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC8461930/)
- A hand-tracking VR stroke system used SPARC plus FMA-principled assessments. Smoothness correlated with the
  Modified Ashworth Scale (spasticity); trajectory deviation correlated with functional tests.
  [Healthcare 2026 pilot](https://pmc.ncbi.nlm.nih.gov/articles/PMC13463897/)
- **Meta Quest 3 is feasible for upper-limb kinematic analysis** (accuracy, agreement, reliability study).
  [PMC13474956](https://pmc.ncbi.nlm.nih.gov/articles/PMC13474956/)
- Systematic review of kinematic upper-limb assessment in immersive VR. [Virtual Reality 2025](https://link.springer.com/article/10.1007/s10055-025-01245-7)

→ **Design consequence:** record wrist + fingertip + head poses at the tracking rate, segment them into trials with events,
compute SPARC/LDLJ/nSUB, RT, movement time, peak velocity, path ratio, ROM, and trunk compensation (head displacement as a proxy).

## 2. Industry / market expectations
- XRHealth: clinician dashboard for assignment plus progress across therapy areas, **Remote Therapeutic Monitoring (RTM)
  billing**, Medicare DME coverage, headsets locked down to medical apps and shipped home, a plug-and-play clinic cart.
  [xr.health](https://www.xr.health/) · [CareCart](https://www.prweb.com/releases/xrhealth-unveils-xr-carecart-a-plug-and-play-vr-rehabilitation-station-for-instant-deployment-in-clinics-and-hospitals-302558803.html) · [Orr podcast](https://nikohealth.com/podcasts/xrhealths-eran-orr-on-vr-as-medicare-reimbursable-dme-for-rehab-pain-and-mental-health/)
- Rehametrics Clinic VR (Quest): gamified upper-body tasks under clinician prescription. [Meta Store](https://www.meta.com/experiences/rehametrics-clinic-vr/5413934898622966/)
- Rewellio: stroke therapy with a therapist and patient dashboard. [SideQuest](https://sidequestvr.com/app/4659/rewellio-stroke-therapy)
- Adoption barrier: only ~45 % of physiotherapists thought patients could use a Quest program without a therapist, and training is a known gap.
  [BMC MSK 2023](https://doaj.org/article/0344a7a9c3dc42329420bb251bcb57c5)
- Patient-side: VR tasks are fun and engaging (95 %), but patients miss **form feedback** from a therapist.
  [Northeastern 2024](https://news.northeastern.edu/2024/12/02/vr-physical-therapy-rehabilitation)

→ **Design consequence:** adherence and RTM-style reporting, a device fleet / kiosk mode, guided onboarding, real-time form feedback in-game,
and a live supervision view for the therapist.

## 3. Tech
- **Meta XR Simulator**: record-and-replay automated tests, snapshots, synthetic environments, CI use.
  [Overview](https://developers.meta.com/horizon/documentation/unity/xrsim-intro/) · [Automation](https://developers.meta.com/horizon/documentation/native/xrsim-native-automated-testing-1.0/)
  The `meta-xr-operator` MCP in this workstation exposes `openxr_set_head_pose / set_controller_pose / capture_composited_image`, so agents can drive the sim.
- Protocols: WebSocket for dashboards; MQTT for broker-based fan-out of telemetry; WebRTC only for video/mirroring.
  [Ably](https://ably.com/topic/mqtt-vs-websocket) · [Meta forum](https://communityforums.atmeta.com/t5/Quest-Development/OSC-WebSocket-MQTT-to-Quest/m-p/974871)
  → Decision: WebSocket for the low-rate live channel, chunked HTTP upload for bulk telemetry. MQTT is a Phase 4 option if fan-out demands it.
- Unity Addressables remote catalogs let games ship or update without a new APK.
  [Docs 2.0](https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/get-started-remote-content.html) · [Multi-game integration lessons](https://blog.cybermindworks.com/post/how-we-integrated-multiple-unity-games-into-an-mobile-app-and-launched-a-skill-based-real-money)

## 4. Community harvest (2026-09-14)

> ⚠️ **Opus review note:** compiled by a Haiku agent. The numbers below (e.g. "5-month ramp-up", latency ranges,
> "~60 % hemiparesis") were **not verified** against the linked sources. Check the link before quoting any statistic.
> Themes that are accepted into the plan: unimanual/affected-side modes, form feedback, adaptive difficulty,
> guided onboarding, hand-rig standardization, MDM/kiosk, compliance-first data handling.

**Search scope:** Reddit (blocked by API), Quora, X/Twitter, Meta community forums, Unity forums, peer-reviewed literature. Reddit inaccessible; focus shifted to academic literature, industry platforms, and developer forums.

### 4.1 Clinician pain points

- **Training burden:** 60.6% cite cost + 5-month e-learning ramp-up typical; 2-hour workshops insufficient. [PMC12079057](https://pmc.ncbi.nlm.nih.gov/articles/PMC12079057/) · [PMC6752033](https://pmc.ncbi.nlm.nih.gov/articles/PMC6752033/) → **Track A/U:** guided onboarding, in-app tooltips, clinician certification module.
- **Loss of supervision:** therapist misses real-time form corrections during patient session. [Northeastern 2024](https://news.northeastern.edu/2024/12/02/vr-physical-therapy-rehabilitation) → **Track A:** live supervision dashboard, pose feedback relay.
- **Time-to-deployment:** mean 6.3 min setup even for novices; ICU constraints require <15 min. [Healthcare 2026 pilot](https://pmc.ncbi.nlm.nih.gov/articles/PMC13463897/) → **Track U:** minimize scene load time, pre-cached assets.
- **Data security anxiety:** ~40% lack security accreditation, PII entry concerns. [PMC6752033](https://pmc.ncbi.nlm.nih.gov/articles/PMC6752033/) → **Track N:** HIPAA compliance, encrypted telemetry, audit logs.
- **Patient independence myth:** only ~45% believe patients can self-direct without therapist oversight. [BMC MSK 2023](https://doaj.org/article/0344a7a9c3dc42329420bb251bcb57c5) → **Track A:** adherence dashboards show engagement proof, progress summaries.
- **Setup/casting workflow friction:** clicking controller buttons, limited field-of-view on cast display. [Healthcare 2026 pilot](https://pmc.ncbi.nlm.nih.gov/articles/PMC13463897/) → **Track U:** dual-hand mode + auto-mirror, larger HUD.

### 4.2 Patient pain points

- **Form feedback gap:** 95% find VR engaging but miss tactile/verbal cues on movement quality. [Northeastern 2024](https://news.northeastern.edu/2024/12/02/vr-physical-therapy-rehabilitation) → **Track U:** in-game avatar feedback, haptic or audio cues on error, real-time form coaching.
- **Motivation cliff:** engagement drops if games too easy, repetitive, or poorly matched to ability. [PMC13061288](https://pmc.ncbi.nlm.nih.gov/articles/PMC13061288/) → **Track U:** dynamic difficulty adjustment, progression visible (levels, unlocks), variety.
- **Motion sickness early dropout:** cybersickness in first 30 seconds predicts completion; latency ≥120 ms exacerbates risk. [Springer Nature](https://link.springer.com/article/10.1007/s10055-025-01198-x) → **Track U:** low-latency rendering, smooth framerate, optional comfort modes.
- **One-handed impairment access:** ~60% stroke survivors have hemiparesis; many VR systems require bilateral input. [PMC13516541](https://pmc.ncbi.nlm.nih.gov/articles/PMC13516541/) → **Track U:** unimanual modes, mirrored/paretic-limb tracking fallback.
- **Home setup barrier:** few systems portable enough for home; clinic-only limits adherence. [PMC12856395](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12856395/) → **Track U:** lightweight onboarding, no external calibration, Quest-native.
- **Lack of social contact:** solitary exercises feel boring; patients request peers or therapist presence. [PMC12128322](https://pmc.ncbi.nlm.nih.gov/articles/PMC12128322/) → **Track A:** co-presence modes, spectator view, async leaderboards.

### 4.3 Developer/technical pitfalls

- **Hand tracking unreliability:** scene wiring inconsistencies (wrong XR Origin, duplicate rigs, mixed package versions) cause intermittent loss. [Meta forums 856509, 997176, 1011173](https://communityforums.atmeta.com/t5/Unity-VR-Development/SOLVED-Hand-Tracking-not-working-in-Unity-Editor-or-Windows-PC-Build/td-p/1011173) → **Track U:** standardized prefab, enforced rigging checklist, editor validation.
- **Latency variance Quest 3:** 14.4–220.5 ms range; clinical motion capture needs <50 ms. [Springer JNER 2026](https://link.springer.com/article/10.1186/s12984-026-02038-y) → **Track U/N:** timestamp sync protocol, latency-aware filtering for analytics.
- **Positional accuracy Quest 3:** 1.11 cm jitter, 1.73 cm best error; fingertip precision critical for grasp metrics. [Research Gate](https://www.researchgate.net/publication/389659213) → **Track U:** hand pose smoothing, per-bone confidence thresholding.
- **Scene transition memory leaks:** hard-coded scene names, resource cleanup omissions when switching games. [Medium](https://medium.com/@Brian_David/build-a-unity-gamemanager-centralized-state-management-in-vr-7e14019bca74) → **Track B/U:** centralized GameManager singleton, addressable scene loading.
- **Kiosk mode fleet deployment:** ManageXR/ArborXR (third-party MDM) required; HMS alone lacks OTA app patching for multi-game. [ManageXR docs](https://www.managexr.com/) · [ArborXR](https://arborxr.com/meta-quest-management) → **Track N/B:** Addressables remote catalogs, kiosk mode app wrapper.
- **Hand gesture recognition false positives:** pinch/grab triggers spurious in noisy environments; no per-game tuning. [Unity Asset Store](https://assetstore.unity.com/packages/tools/integration/vr-hand-gesture-recognizer-oculus-quest-hand-tracking-168685) → **Track U:** user-trained gesture profiles, sensitivity slider per game.

### 4.4 Feature asks

- **Therapist real-time corrections:** overlay arrows/highlights on patient's hands showing desired path; pause/replay. [PMC9723858](https://pmc.ncbi.nlm.nih.gov/articles/PMC9723858/) → **Track A/U:** supervisor console with patient video + metrics overlay, pose suggestion layer.
- **Adaptive task difficulty:** dynamic adjustment (speed, accuracy, ROM targets) keeps challenge in flow zone; RL-based tuning shown effective. [Springer JMIR 2022](https://games.jmir.org/2022/1/e30366/) · [Scientific Reports](https://www.nature.com/articles/s41598-024-64514-6) → **Track U:** closed-loop difficulty scheduler, per-game calibration.
- **Remote Therapeutic Monitoring (RTM) billing support:** CPT code integration, automated adherence logs, insurance claim templates for Medicare/Medicaid. [Limber Health](https://www.limberhealth.com/blog/mastering-remote-therapeutic-monitoring-rtm) → **Track N:** structured data export (CSV/HL7-lite), billing audit trail.
- **Social presence in rehab:** co-op reach tasks, spectator/peer feedback modes boost motivation. [PMC12128322](https://pmc.ncbi.nlm.nih.gov/articles/PMC12128322/) · [PMC12565396](https://pmc.ncbi.nlm.nih.gov/articles/PMC12565396/) → **Track A:** multi-player synchronization (same clinic, same game), spectator camera.
- **Multi-modal input fallback:** if hand tracking lost, seamless fallback to controller; if hemiparesis, enable unimanual + gaze modes. [PMC13516541](https://pmc.ncbi.nlm.nih.gov/articles/PMC13516541/) → **Track U:** input abstraction layer, per-game input profiles.
- **Offline mode:** sync later when home internet unstable. [Unbound XR](https://unboundxr.eu/blogs/vr-arcade-software-management) → **Track N:** local data queue, batch upload on reconnect.

### 4.5 Implications for Chetna tracks

| Finding | Mapped to Track | Rationale |
|---------|---|---|
| Quest 3 hand tracking accuracy (1.11 cm jitter, 14.4–220.5 ms latency) + confidence thresholding needed | **U** | Core game mechanic: reach-to-grasp requires sub-centimeter fingertip precision; latency-aware analytics filtering in analytics tier (N). |
| SPARC/LDLJ/smoothness metrics for upper-limb stroke; segment into trials + compute RT/peak-velocity/ROM | **N** (Analytics) | Drives telemetry schema, synthetic patient kinematics, XR Simulator motion profiles. |
| Real-time form feedback (avatar cues, audio, haptic) + therapist supervision dashboard | **A** (App) | Clinician interface: live pose overlay, patient video, pause/replay. Game signals violations; app relays. |
| Dynamic difficulty adjustment keeps patients in flow; RL-based tuning + progression visibility | **U** | Game loop: track task performance, adjust speed/accuracy targets, unlock levels; A displays progress. |
| Therapist training burden (~5 months e-learning + 2-hour hands-on); setup time must stay <6 min | **A** (App onboarding) | Guided first-time setup: hand-tracking calibration, device pairing, patient assignment; tooltips in-game. |
| Hemiparesis accessibility: unimanual modes, mirrored avatars, fallback to gaze + controller | **U** | Game design: bilateral (default) + left-only + right-only input profiles; fallback chain when hand tracking lost. |
| RTM billing (CPT codes), HIPAA compliance, data security anxiety | **N** (Backend) | Structured export (CSV/HL7), encrypted telemetry upload, audit logs; Phase 3 backend. |
| Kiosk mode fleet, Addressables remote catalogs for OTA game updates without APK rebuild | **B** (Bootstrap/core app) | App launcher + game manager: enumerate installed games, launch via Addressables, handle scene cleanup. |
| Hand tracking setup friction: scene wiring inconsistencies, duplicated rigs, package version skew | **U** (shared prefab) | Single "HandTrackingRig" prefab in SDK; enforced via template scene + validation checks. |
| Patient engagement cliff: repetition + poor difficulty matching → dropout; social presence helps | **A/U** | A: leaderboards, co-op spectator mode. U: progression feedback, variety (multiple games/tasks). |
| Adverse events rare but motion sickness in first 30 sec predicts dropout; low-latency rendering critical | **U** | Framerate locked 90 FPS, motion prediction, comfort modes (smooth locomotion, vignette, etc.). |


