# R2 - Two coin ERMs 10 cm apart: rendering one brush stroke (Phantom Hand research note, 2026-10-08)
Tags: [EVIDENCE] stated in a source I opened (S#, section G); [INFERENCE] my reasoning from evidence; [GUESS] unverified, with what would verify it. L# = repo files (G). Only this file was created. Questions 1-5 of the brief: Q1 A3-A4 + P2; Q2 B4; Q3 B5 + FW1-FW2; Q4 A1-A2 + C; Q5 section F.

## A. Executive summary and verdict
1. VERDICT: change D1. Choose the visible brush speed first (start 12 cm/s) and fire each pulse when the brush reaches its motor; `motor_soa_ms` stops driving the brush and survives only as the gap of an optional "flick" mode. [INFERENCE from S11-S16]
2. D1 lets skin physics set the picture: 10 cm / 100 ms = 1 m/s, 6-33x faster than the 3-18 cm/s of rubber-hand work [EVIDENCE S12] (S15's 1-2 cm strokes lasting 0.6-0.7 s imply ~2-3 cm/s [INFERENCE]); the only speed comparison I found saw the sync-vs-async ownership contrast at 3 cm/s but not at 18 cm/s (n=49) [EVIDENCE S12].
3. The pair itself (200 ms pulses, 100 ms onset gap) is a good apparent-motion setting: DC-motor tactors on the arm gave "apparent movement" in ~80 % of trials at 200/100 ms (n=5, tactors ~140 mm apart across the elbow, Fig. 7) [EVIDENCE S2]; fitted SOA-vs-duration lines give 111 ms (S1, extrapolated beyond 160 ms) and 135 ms (S4) at 200 ms [EVIDENCE].
4. Its limits: at 100 ms a 10 cm gap is felt as only ~3 cm (tau effect, forearm tau ~0.1 s) [INFERENCE S5]; direction was still identified >90 % at 100 mm spacing (voice-coil-type tactors, S3) but a second-hand report calls apparent motion faint at 80-100 mm (S10) [EVIDENCE]; ERM lag + rise (40 + 87 ms, 10 mm coin) eats about two thirds of a 200 ms pulse [EVIDENCE S6].
5. Two actuators cannot give a slow continuous stroke: continuity needs spacing <= speed x max SOA (12 cm/s x ~0.2 s = 2.4 cm) [INFERENCE S1,S2,S4]; a forearm stroking study (indentation/skin-slip, not vibration) needed >= 4 contact points [EVIDENCE S11]. At a believable speed the felt result is two taps and vision must supply continuity; vibration standing in for a brushed hand has produced ownership [EVIDENCE S15, S16].
6. Funnelling / moving phantom: not achievable on this device. A static phantom needs only two simultaneous cues, but ERM start voltage leaves 1.4-7 cm of reachable phantom travel (unit-dependent), and chaining pulses is blocked by the software gate, ERM dynamics and an unclear gap rule [INFERENCE, B4].
7. Do now, no firmware change: P1 pass-timed taps (200 ms, intensity 150/150, lead 60 ms). Keep ASYNC at 600 +-100 ms (illusion absent at 600 ms in S13; +-600 ms significantly below synchrony in S14) [EVIDENCE].
8. Firmware asks: clarify gap / running-motor / buzz semantics (FW0); optional device-side `delay_ms` (helps flick mode only, FW1); optional 30 ms boost (FW2). Do not ask for ramps or shorter gaps: ERMs cannot render them [INFERENCE].
9. Biggest open risk: do two taps read as "brush touch"? [GUESS; to be tested in bench step E6 and the first Quest pilot]. Fallbacks in section F: shorter visible contact path, flick mode, third motor, voice-coil actuators.
10. Evidence is thin for ERM + forearm + visible brush (studies n=5-50, none combine them): every number below is a bench start value, not a result.

## B. Rendering algorithms (ranked)
Notation: xA=5, xB=15 cm from the wrist crease, L=25 cm forearm, v brush speed cm/s, d pulse ms, I[m] intensity <=150, lead = tactile_lead_ms.
### P1 - pass-timed taps (brush first). Works within today's limits. RECOMMENDED
```
for each stroke k, brush touches the wrist at T_k:                       # wire format unchanged
  passA = T_k + 1000*xA/v ; passB = T_k + 1000*xB/v                      # what BrushRig measures; felt gap = 1000*(xB-xA)/v
  for (m, pass) in ((0,passA), (1,passB)):
     due     = pass + (ASYNC ? async_delay_ms +-100 : 0)                 # 50 % of ASYNC strokes swap the A/B slots (as now)
     send_at = due - d/2 - lead                                          # pulse centred on the pass [GUESS, E6]
     UDP {"cue_id":..,"motor":m,"intensity":I[m],"duration_ms":d,"pattern":"pulse"} at send_at
  T_{k+1} = max(T_k + 1000/stroke_rate_hz +-jitter, T_k + 1000*L/v + 300)   # brush must finish and return
```
- Start: v=12 -> passA 417 ms, passB 1250 ms, felt gap 833 ms, stroke 2.08 s, ~0.42 strokes/s (~38 per 90 s); d=200, I=150/150, lead=60 [INFERENCE].
- Why: vibration that starts with the brush and lasts about as long as its contact produced ownership in two vibrotactile studies (strokes ~1 Hz; ERM disc on during contact) [EVIDENCE S15, S16]; short taps also work [EVIDENCE S14]; ownership tolerates +-300 ms mistiming in either direction [EVIDENCE S13, S14], so +-50 ms of lead error is harmless [INFERENCE].
- Felt A-B distance is near true at gaps >= 0.3 s (tau: 8.2 cm at 0.3 s, 9.7 cm at 0.83 s), so sight and touch agree in space [INFERENCE S5].
- Software implied (their call; I changed nothing): new `brush_speed_cm_s` replaces the speed derived from `motor_soa_ms` (StrokeScheduler.SpeedCmPerMs, BrushRig.SetGeometry); cue = pass - d/2; `HapticCueMapper.StrokeDurationMs` (const 200) becomes a param; manifest `stroke_rate_hz` min 0.5 is unreachable at 12 cm/s [EVIDENCE L1, L5, L7].
### P2 - flick (apparent motion). Works within today's limits. Optional mode, only if bench step E3 passes
```
d = 250 ; SOA = 130                      # 0.38*d+59 = 154 (S4) ; 0.32*d+47 = 127 (S1) ; DC tactor best 100-145 at d=200 (S2)
send A at tA - lead ; send B at tA + SOA - lead ; both d ms, I=150/150     # A and B overlap d-SOA = 120 ms (different motors: allowed)
brush speed to show = (xB-xA)/SOA = 77 cm/s ; stroke period >= 1000 ms
```
- Valid window: continuous motion lies between a lower SOA (stimuli merge) and an upper SOA (discrete); at d=160 about 50-160 ms, widening with d, narrowing as frequency rises 150->270 Hz (read off S1 Fig. 4); S17 agrees on both trends [EVIDENCE]. Optimum 92-99 ms at d=100 and 205-216 ms at d=400 (voice coils, hands 31 cm apart) [EVIDENCE S4]; DC-motor tactors give no apparent motion below d=200 ms [EVIDENCE S2].
- Wearers matching a tactile apparent-motion to a moving virtual ball chose tactile durations ~1/4 of the visual ones (visual 90-570 ms): the felt flick is not scaled to the picture [EVIDENCE S4].
- Direction was identified >95 % only with >= ~400 ms between the end of one sequence and the start of the next; shorter gaps bounce back and forth (two tactors 100 mm apart, upper arm) [EVIDENCE S3]. P2 at 1 Hz leaves ~620 ms [INFERENCE].
- Hairy forearm: all upper-limb sites detect vibration at 2-6 % of a tactor's maximum, dorsal slightly worse than ventral (2.9 vs 2.3 %) [EVIDENCE S9]; two-point threshold ~3-4 cm [GUESS: verify in Weinstein 1968 or Mancini 2014 Figs 2-4], so 10 cm is two clearly separate places [INFERENCE].
- Weak point: Wi-Fi jitter (tens of ms, L3) is as large as the SOA window; P1 does not care [INFERENCE].
### FW. Needs a firmware change (ask the electronics team; none is needed for P1)
- FW0 clarify (no code): (a) is the 100 ms gap start-to-start (reference sketch opus_sleeve.ino:120 and the twin say so) or end-to-start; (b) does a cue to a still-running motor retrigger (twin: yes) or return accepted:false (their handoff text); (c) what `pulse` and `buzz` do in v0.5.0 (reference: buzz = 25 ms on/off); (d) coin-motor model and measured start voltage [EVIDENCE L3, L4, L8].
- FW1 (small): optional `"delay_ms"` (int 0-1000, default 0) on a cue: the node holds it and starts the pulse that long after arrival; gap, duty and watchdog checks run at start time; ack `accepted` is sent on arrival and adds `start_dev_ms`. Unity then sends A (delay 0) and B (delay SOA) back to back, so the A-B gap loses Wi-Fi jitter. Only P2 benefits [INFERENCE].
- FW2 (optional, safety owner decides, default off): for the first 30 ms of a pulse drive min(commanded x 1.25, 180/255) = 3.5 V at most, inside the 3.6 V maximum operating voltage of the reference coin motor [S6]; PMD's overdrive example cut time-to-steady-state from ~180 to ~130 ms on a 5 mm motor [S7]; TI also uses overdrive then a rated-voltage hold [S8]. Sustained cap stays 150 [INFERENCE].
- Do NOT request: ramp/crossfade, shorter gaps or pulses, more subscribers; they do not fix this problem (B4). If continuity matters more than the schedule: voice-coil or LRA actuators with a haptic driver and >= 4 contacts [EVIDENCE S11; INFERENCE].
### B4. Why funnelling, phantom glide and pulse chaining are out (question 2)
- Funnelling needs both actuators on together; a phantom's place and strength follow A1=sqrt(1-b)Av, A2=sqrt(b)Av (energy model; validated for intensity with 11 listeners, location only in a 3-person pilot; Seo-Choi found log fits intensity and linear fits location, so no model gets both) [EVIDENCE S1]. Tactile Brush then chained phantoms + apparent motion into one felt stroke using voice coils 63 mm apart and precise amplitude control [EVIDENCE S1].
- Static phantom today = two simultaneous cues with unequal intensity [INFERENCE]. Reach: with floor F and cap 150, r=F/150, b_min = r^2/(1+r^2): F=130 -> b 0.43-0.57 (1.4 cm of 10); F=80 -> 0.22-0.78 (5.6 cm); F=61 -> 0.14-0.86 (7.2 cm). F is unit-specific (reference motor start voltage 1.2 V typical, 2.3 V max -> 61-117 of 255 at 5 V) [INFERENCE from S6; measure in E1].
- Weighted motors also differ in frequency (ERM amplitude and frequency move together with voltage) while the model assumes equal frequency [EVIDENCE S1, S2].
- Chaining 50 ms pulses of different intensity: >= 150 ms per motor under an end-to-start gap; the software gate (>=250 ms per motor, <=4 sends/s total, L7) allows at most 2 steps per motor per second; a 50 ms pulse ends before the 87 ms rise and the 115 ms stop time smears it into the next, so intensity steps are unreadable [EVIDENCE S6; INFERENCE]; quick A/B alternation gives back-and-forth, not a glide [EVIDENCE S3].
- Saltation (earlier note L6) needs taps under ~50 ms; an ERM with 40 ms lag cannot render them [INFERENCE S6].
### B5. ERM facts that set the numbers (question 3)
- Timing: 10 mm coin at 3 V: lag 40, rise 87, stop 115 ms (100 g test load) [EVIDENCE S6]; DC-motor tactor at 2.5 V: >50 ms to start, >100 ms to full, ~60 ms to stop [EVIDENCE S2]. At lower voltage expect slower and weaker [GUESS: compare 150 vs 130 in E5].
- Perceived onset ~ link + lag + part of rise = 60-110 ms after the command [INFERENCE]; the headset's own display delay (~30-50 ms, [GUESS: not measured]) offsets part of it, hence lead 60.
- Overdrive shortens onset; reverse-drive braking needs a differential driver [EVIDENCE S8]; our single low-side 2N2222 cannot brake and the cap forbids overdrive [INFERENCE L4].
- Strength ~ rotation speed: frequency and amplitude are not independent [EVIDENCE S2, S8]; 150/255 of 5 V ~ 2.9 V mean, near the 3 V rating; PWM 2 kHz >> 150-200 Hz vibration [INFERENCE].
- Lowest intensity: the skin is not the limit (2-6 % of a tactor's maximum, S9); the motor start voltage is (1.2 V typical, 2.3 V max, S6) -> floor 61-117 of 255, so amplitude coding has at most ~2:1 range [INFERENCE]. Unit spread: speed +-25 %, amplitude 0.75-1.3 G [EVIDENCE S6].
- Minimum useful pulse: >=100 ms for a felt tap, >=200 ms for apparent motion [EVIDENCE S2]; the firmware's 50 ms minimum is below anything an ERM renders usefully [INFERENCE].

## C. Parameter table (P1 / P2 start values; ranges to sweep on the bench)
| Parameter | Start | Range | Basis |
|---|---|---|---|
| brush speed (new `brush_speed_cm_s`) | P1 12 cm/s | 6-20 | humans stroke a forearm ~13.5 cm/s, pleasant-touch band 1-10 [EVIDENCE S11]; contrast lost at 18, kept at 3 cm/s [EVIDENCE S12]; start value [INFERENCE] |
| `motor_soa_ms` | P1 derived 833 | 500-1700 | = 1000*(xB-xA)/v, no longer an input [INFERENCE] |
| `motor_soa_ms` (flick) | P2 130 | 90-200 | S1, S2, S4 lines above [EVIDENCE]; at 10 cm spacing [GUESS: E3] |
| `duration_ms` | P1 200 / P2 250 | 150-300 / 200-400 | tap >= 100 ms, apparent motion >= 200 ms [EVIDENCE S2]; lag 40 + rise 87 [EVIDENCE S6]; match brush contact time [INFERENCE S15, S16] |
| `intensity` A / B | 150 / 150 | B 130-150 trim; floor per unit | unit spread [EVIDENCE S6, reference motor only]; difference limen ~20 % [EVIDENCE S9], so equalise within 20 % [INFERENCE] |
| `tactile_lead_ms` | 60 | 0-120 (manifest max 150) | ERM start >50 ms, full >100 ms [EVIDENCE S2]; link a few-tens ms with spikes [EVIDENCE L3]; display delay works against it [INFERENCE]; set in E5, confirm in Quest sweep |
| `stroke_rate_hz` | P1 0.45 (effective 0.42) / P2 1.0 | 0.3-0.5 / 0.8-1.2 | P1 = 1/(L/v + 0.3 s) [INFERENCE]; manifest min 0.5 must drop to 0.3; classic strokes 0.5-1 Hz [EVIDENCE S13] |
| pulse anchor | centre | start / centre | [GUESS: E6] |
| `async_delay_ms` | 600 +-100 (unchanged) | 500-700 | [EVIDENCE S13, S14] |

## D. Duty and gap check (firmware: pulse 50-400, gap >=100 per motor, <=50 % duty per 10 s, watchdog 2 s; software: >=250 ms per motor, <=4 sends/s; all arithmetic [INFERENCE])
- P1 default: stroke 2.08 s; today's scheduler spaces strokes by max(nominal +-150 ms, end + 50 ms) -> period >= 2.13 s (>= 2.38 s with P1's +300 ms). Worst 10 s window: ceil(10/2.13) = 5 pulses x 200 ms = 1.0 s = 10 % (limit 50 %); d=300 -> 15 %. Same-motor start gap >= 2.13 s, end-to-start >= 1.93 s (limits 250 / 100 ms). A and B 833 ms apart -> <=2 sends in any 1 s (limit 4).
- P1 ASYNC: both slots shift by 600 +-100 ms, so A-B spacing stays 633-1033 ms; pulses per motor unchanged -> same 10 %; the A/B swap keeps >= 250 ms per motor.
- P1 watchdog: pulses ~2.1 s apart, so the 1 Hz `keepalive` must run (it does, L2 v1.3) [EVIDENCE L2].
- P2 default: d=250, nominal 1000 ms, jitter 150 -> min interval 850 ms -> worst 12 pulses per 10 s x 250 = 3.0 s = 30 %. Same-motor end-to-start >= 600 ms, start-to-start >= 850 ms; 2 sends per stroke -> <=2.4/s at 1.2 Hz (limit 4).
- P2 limits: 1.5 Hz with d=250 -> min interval 517 ms -> 20 pulses = 5.0 s = 50 % -> FORBIDDEN (the twin rejects at >=50 %); 1.2 Hz -> 15 pulses = 3.75 s = 37.5 % ok. d=400 at 1 Hz -> 12 x 400 = 4.8 s = 48 %, too tight: cap d=300 at 1 Hz, or d=400 at <=0.8 Hz (10 x 400 = 40 %).
- A and B overlap in P2; the gap rule is per motor, so allowed [EVIDENCE L1, L3]; confirm no supply sag with both on (E5). In every run log `accepted:false` acks: any is a rule hit.

## E. Bench-tuning protocol (20 min, 1-2 wearers, laptop tool sends single cues; a script with sleep() may stand in for a pair mode)
Setup: sleeve on the dorsal forearm, A 5 cm and B 15 cm from the wrist crease, marked with tape; wearer eyes closed except E6; random order from a printed list; one CSV: step, trial, motor, intensity, duration_ms, soa_ms, lead_ms, answer, ack accepted, notes; photograph the sheet. Design is [INFERENCE]; thresholds in the last column are [GUESS] until measured.
| Step | Min | Send | Ask the wearer | Record / decide |
|---|---|---|---|---|
| E0 | 1 | one 200 ms cue per motor at 150 | "felt?" | acks accepted:true; tape marks right |
| E1 | 3 | each motor: I in {150,130,115,100,80,60} x2 + 4 blanks, 200 ms | "felt / not", strength 1-5; then A-vs-B at 150, 6 trials: "which stronger?" | lowest I that starts 10/10 and is felt = floor per motor; lower the stronger motor to equalise |
| E2 | 3 | each motor: d in {100,150,200,300,400} at 150 | "one clear tap?" 1-5; "how long?" | smallest d rated >=4 on both = P1 `duration_ms` |
| E3 | 5 | P2 pairs: d {200,300} x SOA {60,100,130,160,200,300}, 2 repeats | "one moving touch / two touches / one blob"; direction; distance travelled (point on the other forearm) | SOA with >=80 % "moving" and right direction; if none, P2 is dead |
| E4 | 3 | P1 pairs d=200, SOA {500,833,1000,1500} x2 | "one brush passing wrist-side to elbow-side?" 1-5; felt distance | pick the cadence; try B-before-A once (the ASYNC swap) |
| E5 | 3 | 20 single cues per motor with the `chetna_udp_tool.py` live watch of the Node A IMU (L3; or `run_pipeline.py --spinup`, L4); A+B together x5 | none | median/p95 send-to-vibration onset (100 Hz IMU aliases 150-200 Hz vibration, variance should still rise [GUESS]); ack round trip; supply sag; lead start = median onset - 40 ms (40 = assumed display delay [GUESS]) |
| E6 | 2 | brush emulation: soft brush hovering 1-2 cm above the sleeve, ticks at passA/passB for the examiner; P1 at 12 cm/s; lead {0,60,120} x3, random; once with the brush shifted 2 cm | watching the arm: "does the touch seem to come from the brush?" 1-5; "early/late?" | best lead; centre vs start anchor (lead +100); mean < 3 -> go to the ladder in section F |
Hand-back (one table): floors and trims A/B; d; best P2 SOA or "dead"; P1 cadence; lead; onset median/p95; accepted:false count; motor model; wearer remarks. 1-2 wearers is screening, not validation [INFERENCE].

## F. Risks and fallbacks
- Two taps may read as "two pokes" [GUESS]. Ladder: (1) shorten the visible contact path to xA-3 .. xB+3 cm (16 cm: 1.33 s per stroke, ~0.6 Hz, less touch-without-sensation); (2) raise v toward 20 cm/s if it feels sluggish (S12 warns the contrast may shrink); (3) P2 flick; (4) a third motor at 10 cm (spare coin motor, L4; needs a driver channel and firmware motor 2); (5) voice-coil/LRA actuators [INFERENCE].
- Apparent motion may be faint at 10 cm [EVIDENCE S10, second-hand; S3 is more positive]: E3 decides; P2 may be dropped.
- Unit spread and drift: per-motor floors and trims; re-check when the sleeve warms or the bank sags; the team's actual coin motor is unknown [GUESS: ask FW0d].
- Wi-Fi spikes: P1 tolerates them (+-300 ms window, S13, S14); P2 does not (use FW1 or P1) [INFERENCE].
- Fewer strokes: ~38 per 90 s at 12 cm/s vs 90 at 1 Hz; published vibrotactile illusions used 45 strokes in 45 s (S15) and 30 stimulations in ~100 s (S16) [EVIDENCE].
- Spatial tolerance of brush-vs-motor position (~2 cm) is probably fine [GUESS: E6 shift test]; no spatial-tolerance source was opened; rubber-hand ownership fading beyond ~30 cm hand separation is a search snippet only [GUESS]; our offset is 15 cm.
- Question 5: S15 two vibrators (165 Hz) on the hidden fingertips, brush 1 Hz, 1-2 cm in 0.6-0.7 s, vibration for the same time, 45 s, n=20: questionnaire sync > async, drift difference 0.9 cm (p=0.07). S16 one ERM disc (11,000 rpm) on a finger, on during brush contact, async +500 ms, n=27: drift 2.0 +-2.8 cm vs 3.0 +-3.4 brush-sync and 1.2 +-3.2 async [EVIDENCE]. None used the arm, two motors as a stroke, or apparent motion; my search for that combination found nothing (not exhaustive) [INFERENCE].
- Process: D1 and the manifest ranges live in 03-SPEC and HAPTIC_PROTOCOL v1.2; amending them is Opus's call [EVIDENCE L1, L2].

## G. Sources (opened = read in full or the relevant part; S5, S7-S10, S12-S17 were read through the fetch tool's summary, the rest as raw text or figures: re-check numbers before quoting outside)
- S1 Israr, Poupyrev, Tactile Brush, CHI 2011, doi 10.1145/1978942.1979235: https://la.disneyresearch.com/wp-content/uploads/Tactile-Brush-Drawing-on-Skin-with-a-Tactile-Grid-Display-Paper.pdf
- S2 Niwa et al., Vibrotactile apparent movement by DC motors and voice-coil tactors, ICAT 2004: https://icat.vrsj.org/papers/2004/S2-1.pdf
- S3 Niwa, Lindeman, Itoh, Kishino, apparent-motion parameters on the upper arm, Haptics Symp. 2009: https://web.cs.wpi.edu/~gogo/papers/Niwa_HapticSymp_2009.pdf
- S4 Pittera, Obrist, Israr, Hand-to-Hand, ICMI 2017: https://la.disneyresearch.com/wp-content/uploads/Hand-to-Hand-An-Intermanual-Illusion-of-Movement-Paper.pdf
- S5 Goldreich, Tong 2013, Front. Psychol. 4:221, doi 10.3389/fpsyg.2013.00221: https://pmc.ncbi.nlm.nih.gov/articles/PMC3650428
- S6 Precision Microdrives 310-103 datasheet (10 mm coin, R002-V011): https://precisionmicrodrives.com/cdn/catalog_product/89437681-576e-46e1-97cf-5ae185adeb1c/310-103-datasheet.pdf
- S7 PMD, new testing for precision haptic ERMs (5 mm motor): https://www.precisionmicrodrives.com/new-testing-for-the-precision-haptic-erms
- S8 TI SLOA207A, ERM overdrive and braking (qualitative only): https://www.ti.com/document-viewer/lit/html/SLOA207A/GUID-0CD8BBF7-307B-4E83-B637-E12C8A9A7A1F
- S9 Upper-limb vibrotactile thresholds, Front. Neurosci. 2022: https://www.frontiersin.org/journals/neuroscience/articles/10.3389/fnins.2022.958415/full
- S10 Wearable phantom-sensation sleeve, Front. ICT 2019 (cites Cha 2008 and Cholewiak, Collins 2003): https://www.frontiersin.org/journals/ict/articles/10.3389/fict.2019.00019/full
- S11 Nunez et al., SHIFTS forearm stroking, 2020: https://arxiv.org/pdf/2003.00954
- S12 Crucianelli et al. 2013, Front. Psychol. 4:703: https://pmc.ncbi.nlm.nih.gov/articles/PMC3792699/
- S13 Shimada, Fukuda, Hiraki 2009, PLoS ONE 4:e6185: https://pmc.ncbi.nlm.nih.gov/articles/PMC2702687/
- S14 Bekrater-Bodmann et al. 2014, PLoS ONE 9:e87013: https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0087013
- S15 D'Alonzo, Cipriani 2012, PLoS ONE 7:e50756: https://pmc.ncbi.nlm.nih.gov/articles/PMC3511354/
- S16 Svensson et al. 2023, RHI with different stimulation modalities, Front. Neurosci.: https://pmc.ncbi.nlm.nih.gov/articles/PMC10536259/
- S17 Wake, Wake, ASA 1996 abstract (optimal SOA grows with duration, shrinks with frequency): https://auditory.org/asamtgs/asa96haw/4pBVa/4pBVa5.html
- L1 docs/agent-briefs/ph/03-SPEC.md; L2 contracts/HAPTIC_PROTOCOL.md; L3 docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md; L4 docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md; L5 game/Assets/Games/PhantomHand/Runtime/StrokeScheduler.cs (+ Presentation/BrushRig.cs); L6 docs/agent-briefs/ph/research-phantom-hand-additions.md; L7 game/Packages/com.opus.sdk/Runtime/Transport/HapticClient.cs + HapticCueMapper.cs; L8 firmware/opus_sleeve/opus_sleeve.ino and sim/sleeve/twin.py (reference code, uncompiled).
- Cited only second-hand, not opened: Sherrick and Rogers 1966, Kirman 1974, von Bekesy 1957, Alles 1970, Seo and Choi, Cha 2008, Cholewiak and Collins 2003 (all via S1, S2, S10). Seen only as search snippets: Pavani, Spence, Driver 2000 (doi 10.1111/1467-9280.00270); Padilla et al. 2010 (doi 10.1007/978-3-642-14075-4_28).
