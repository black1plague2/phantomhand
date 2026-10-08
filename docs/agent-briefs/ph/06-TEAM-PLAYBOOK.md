# Team playbook — Kela (Vaibhav, Bhavya, Garv) · Vedanta Makeathon 7–9 Oct 2026

Vaibhav is team leader and representative (PRD). Roles below are a suggestion; keep one named owner per row.

## 1. Roles

| Person | Owns | Agent prompts they watch/approve | Human-only duties |
|---|---|---|---|
| Vaibhav (lead) | Electronics, firmware, team decisions, organiser contact | F1, F2 | Wiring, flashing, bench checks, sign-off sheet, SOA/tactile tuning with the wearer, electrode consent and organiser approval, parts return (CARF) |
| Garv | Unity + integration + the Opus manager session | O0–O4, U1–U8, E2E levels | Quest 3 (Link + APK), tracking check with the sleeve, demo build, tech part of the pitch |
| Bhavya | App, analytics, live plot, demo operator | A1–A2, N1–N2, S1–S3, H-docs | Hindi strings check, operator on the app/laptop plot during runs, pilot scheduling, story part of the pitch |

## 2. Clock

**Oct 7 evening → night**
- Garv: O0 → O1 in the Opus session; launch F1, F2, S1 right after O0, U1 after O1.
- Vaibhav: PRD §14 bench list with the current v0.4.0 firmware on Node A (it already works on the WROOM):
  flash, I2C scan, motor drive per channel (1.5 kΩ, parallel pair if weak), power bank both boards; when F2 lands,
  flash Node B and do the BioAmp checks (bank only, laptop disconnected from Node B). When F1 lands, flash it and
  run the `soa` test to pick motor_soa_ms.
- Bhavya: find 2 micro-USB cables (one data), confirm the bank has 2 USB-A ports, organiser rules for electrodes,
  prepare the consent line; review S1 when it lands.
- **G1 before sleep:** both nodes discovered over Wi-Fi, EMG trace usable, U1 green. Post the 5-line O3 summary in the team chat.

**Oct 8**
- 09:00 stand-up (10 min) from docs/PH_STATUS.md + MANUAL_TODO.
- Morning: U2 (logic + scene), N1, A1, S2 in parallel. Vaibhav: Node A/B on the sleeve, placement, cable routing,
  hand-tracking check with Garv.
- Afternoon: U3 (arm, brush, threat) → U4 (UI). Bhavya: live plot against the real nodes.
- Evening: U5 (composition + live) → L3 in sim → L4 on real nodes → tune tactile_lead_ms (wearer picks most
  simultaneous of 0/20/40/60/80 ms) → one full 4-minute run recorded.
- **G2 before sleep:** one full run end to end. If red, O3 picks the fallback (PRD §13).

**Oct 9**
- Morning: U6 (APK, demo mode) → L5 on the Quest → 5-teammate pilot (offset and stroke rate tuning).
- 12:00 freeze: only demo-path fixes; each merge followed by one demo_mode run.
- Afternoon: H4 runbook, H5 facts, two dry runs of pitch + demo, G3, O4.
- After the event: return all issued parts (CARF).

## 3. Running agents (rules in 01/02)

- One Opus session (Garv's laptop) is the manager; it is the only one that commits.
- Builders run either inside it (ph-builder subagent) or on teammates' laptops (`claude --model sonnet`, paste the
  prompt, push the branch). Only ONE Unity batch run at a time anywhere (Unity lock).
- After every run: ph-scribe H1, then Opus O2. Don't push to main by hand.

## 4. Rules we hold ourselves to

- "Works" only with the log line that proves it.
- Never commit Wi-Fi passwords or recovery scenes.
- Node B with electrodes on a person: power bank only, never a laptop or charger, bank not charging. No exceptions.
- Unity editor closed while a Unity agent runs.
- Stretch work only after G2.

## 5. Demo runbook (short; full one is docs/PH_ON_DEVICE_RUNBOOK.md)

**Night before:** charge Quest, laptop, phone, power bank (nodes detached); APK installed; one demo_mode run;
≥ 20 spare electrodes, alcohol wipes, tape measure, spare motor, spare resistors, CH9102, both cables.

**1 h before:** hotspot up (2.4 GHz); nodes on the bank → OLED shows IP; app/laptop plot sees both nodes; one
self-run; observer screen (Flutter card or `live_plot.py --fullscreen`) facing the audience.

**Per visitor (~4 min):**
1. Consent (Bhavya): "This vibrates gently and reads your muscle activity with stickers. You can stop any time.
   OK?" No pacemakers; no electrodes on broken skin.
2. Sleeve on: Motor A 5 cm from the wrist crease, Motor B 10 cm further; electrodes on; Node B on the bank only.
3. Headset on, calibrate, demo_mode run; operator watches the live card.
4. Witness screen; Garv says the Theme 5 link while the audience sees the traces and cards.
5. Next person: reset gesture or the app button; wipe the headset and sleeve; fresh electrodes.

**If something fails (PRD §13):** Node A off → run continues visual-only, call it the no-touch control; Node B off →
flinch from IMU + hand tracking; hub off → the game records locally, uploads later, use the laptop plot; tracking
poor → light, arm in view, recalibrate; APK broken → Quest Link from the laptop.

## 6. Pitch in three lines (numbers only from docs/PH_FACTS.md)

1. A hand that is not yours became "yours" in under two minutes, because sight and touch arrived together.
2. Delay the touch by 600 ms and it stops being yours; we measured both states on you just now.
3. What timing can build and dissolve is an appearance of self; the one who watched it happen did not change. The
   same measurement engine is Chetna's rehab product.
