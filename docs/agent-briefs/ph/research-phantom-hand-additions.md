# Phantom Hand: what to add (research note, 7 Oct 2026)

**Input:** `Chetna_PhantomHand_PRD_v2.md`, `UNITY_1.md`, `03-SPEC_1.md`. **Constraint:** about 2 days, no hardware beyond the PRD BOM (Quest 3, Node A with 2 ERM + MPU6050 + OLED, Node B with BioAmp EMG, power bank, Flutter hub, Python analytics).
**Bottom line:** the current run is a clean rubber-hand replication with a threat measure: one illusion, built once, broken once. To make it read as an *artificial Self in body and mind* rather than a lab demo, the cheapest gains are:
(a) a **narrative spine** that peels identification layer by layer (body, breath, mind, witness), using the five koshas and neti-neti;
(b) a **finale that dissolves the body on purpose** (the hand turns invisible while touch continues, then passthrough reveals the real arm), so the end is an experience, not only a scoreboard;
(c) **one "mind" beat**: the participant becomes the agent (self-touch, or EMG intent). The best version also takes agency away while ownership stays.

All of this is software on hardware the team already holds. Add nothing until the MVP gate ("one full run recorded end to end") is green. The ranking below assumes the team has roughly **8–12 spare hours** on 8–9 Oct.

---

## 1. Ranked shortlist (best value per hour first)

### #1 Self-inquiry spine plus a "witness" item (neti-neti voice-over, kosha staging, and a q4 that is meant not to move)
- **What:** short EN/HI voice lines (Hindi optional) at each phase start, framed as layers: *Annamaya* (body: "this arm is felt as you"), *Pranamaya* (breath), *Manomaya/Vijnanamaya* (mind and agency: "you are the one moving it... or are you?"), then *neti, neti* as each layer is dissolved. Add one **witness item, q4**, asked after each condition: "The awareness that noticed these sensations was the same as before" (7-point). The witness screen then shows every body/mind number moving between SYNC and ASYNC, and **one line that stays flat: awareness**. The closing line now points at data on the screen, not only at a slogan.
- **Why it helps Theme 5:** it turns the PRD's argument ("what can be built and dissolved is appearance; the witness did not change") into the *structure* of the run. Drg-Drsya-Viveka is literally a staged seer/seen exercise: form is seen by the eye, the eye by the mind, the mind by the witness, which is never itself an object ([Drg Drsya Viveka summary](https://www.yesvedanta.com/drg-drsya-viveka/lecture-19/); [Sakshi](https://en.wikipedia.org/wiki/Sakshi_(witness))). The five sheaths of Taittiriya Upanishad 2.1–5 give a ready-made, judge-recognisable ordering from gross to fine ([Kosha](https://en.wikipedia.org/wiki/Kosha)). Optional: end in silence after "Om" (Mandukya: A-U-M = waking/dream/sleep, the silence = turiya, the witness) ([Turiya](https://en.wikipedia.org/wiki/Turiya)).
- **Evidence and honesty note:** questionnaire RHI reports are partly driven by expectation and suggestion ([Lush et al. 2020, Nat Commun](https://doaj.org/article/0d4fd24ccccb4322979ecd0db0fd566f)). Present q4 as a *phenomenological pointer*, not proof. Lead the science with the implicit measures (drift, EMG/IMU flinch) and keep the q3 control item, which is what science-minded judges will look for.
- **Build cost:** **S, about 2–3 h.** Audio clips on `phase_start` (an AudioSource in the existing Voice mixer group), one more questionnaire item (the item list already exists in U4), one more witness row, plus string-table entries.
- **Demo risk:** very low. The only risk is cringe: keep each line under 8 words, calm voice, no music bed during probes.
- **New hardware:** none.

### #2 "Neti" finale: invisible hand, then passthrough reveal of the real arm
- **What:** after the SYNC threat, the virtual arm **fades to nothing over 3 s while the brush and motors keep stroking the empty space** for about 15 s ("the touch is still here; where is the hand?"). Optional: a second, smaller stone drops onto the empty space. Then **passthrough fades in** and the participant sees their real arm resting 15 cm from where they felt it. Voice: "Not this." Then the witness question and screen.
- **Why it helps:** it is the most memorable possible ending, and it says the thesis without words: the felt body survives removal of the visible body, and the felt location was wrong. Guterstam and Ehrsson showed that people embody *empty space* stroked in sync with their hidden hand: 234 participants, threat-evoked sweat responses to a knife aimed at the empty space, and pointing drift toward it ([KI press release](https://news.ki.se/scientists-create-phantom-sensations-in-non-amputees); [National Geographic summary](https://www.nationalgeographic.com/science/article/the-invisible-hand-illusion)). The same lab extended this to a whole invisible body ([KI, 2015](https://news.ki.se/scientists-create-the-sensation-of-invisibility)). VR can also make touch felt where nothing touches ([Phantom touch, Sci Rep 2023 / RUB](https://news.rub.de/english/press-releases/2023-11-14-neuroscience-when-we-feel-things-are-not-there)).
- **Build cost:** **S, about 2–4 h.** Alpha fade on the arm material (URP transparent variant); brush and `StrokeScheduler` already run without the arm; a new `Dissolve` phase in `PhaseStateMachine`. Passthrough: add `OVRPassthroughLayer` (underlay) plus a skybox/table alpha fade. **Note:** U2's scene builder and rig validator say "no passthrough", so the validator test needs one line changed. This uses the ordinary passthrough layer, *not* the Passthrough Camera API, so no new permission is needed.
- **Demo risk:** low to medium. Passthrough on a lit demo table looks great. Check that turning it on does not change hand-tracking frequency or the 72 Hz rig. Fallback: skip passthrough and render a ghost outline of the tracked real hand instead.
- **New hardware:** none.

### #3 "You are the brush": active self-touch phase (visuo-tactile becomes self-generated touch)
- **What:** for the last 15–20 s of SYNC induction the brush retires and a glowing dot appears on the participant's **left index fingertip** (already tracked for the drift probe). They stroke the virtual right arm themselves. When the fingertip crosses the motor-A band, then the motor-B band, Node A fires immediately (async: +600 ms). The participant feels their own touch arrive on an arm 15 cm away.
- **Why it helps:** it adds the *mind* (agency) to the *body* (ownership) beat, it is interactive, and judges love doing something. It is the VR form of Ehrsson's somatic RHI: moving one's own finger to touch a fake hand while the real hand is touched in sync produced the illusion of touching one's own hand in about 10 s, with premotor/cerebellar activity tracking its strength ([Ehrsson et al. 2005, J Neurosci](https://pmc.ncbi.nlm.nih.gov/articles/PMC1395356)). Visuomotor synchrony alone can induce ownership of a virtual arm, with a 3.5 cm drift difference sync vs async ([Sanchez-Vives et al. 2010, PLoS ONE](https://doaj.org/article/843caa6308d8480980b8a38cefaf5359)).
- **Build cost:** **M, about 3–4 h.** Trigger colliders at `WorldPos(motor_a_from_wrist_cm)` and `WorldPos(+motor_spacing_cm)` (the U3 helper exists). On enter, call `HapticClient` for an immediate stroke (no `play_at_ms`). Respect the 250 ms/motor gate (rubbing back and forth must not spam). Log a `self_touch` event. The measured link latency (3–72 ms) is fine.
- **Demo risk:** medium-low. Hand tracking of the left index near the table is good; the occluded-hands case is not involved because the right hand is still. Risk: a participant pokes too fast. Show a "slow stroke, wrist to elbow" ghost animation first.
- **New hardware:** none.

### #4 Pranamaya layer: a virtual arm that breathes with you (breath-sync glow, with a self-replay control)
- **What:** a soft inner glow and a 2–3 % swell on the virtual arm follow the participant's **breath**. In SYNC the glow is live; in ASYNC it is the participant's *own breath from 20 s earlier*, a matched control in which only the timing is wrong. Breath source, in order of preference: (1) the **Quest 3 microphone**: ask for slow audible exhales through the mouth, or a low hum, then take the RMS envelope and low-pass it at 1 Hz. A hum also gives an "Om" closing tie-in, as in SoundSelf, the voice-driven meditation VR ([Venice Biennale 2020 entry](https://www.labiennale.org/en/cinema/2020/venice-vr-expanded/sound-self-technodelic)). (2) **Headset head-pose micro-motion**: HMD gyro signals carry breathing ([HMD/IMU respiration studies](https://openaccess.cms-conferences.org/publications/book/978-1-958651-05-6/article/978-1-958651-05-6_0)), but they are weak when seated; treat this as a bonus only. (3) Moving the MPU6050 to the sternum on longer I2C leads works ([chest/abdomen accelerometer respiration](https://hal.archives-ouvertes.fr/hal-03501205)), but it **costs the IMU flinch metric**, so do not do it.
- **Why it helps:** it is the *interoceptive* self, the bridge from body to life-force. Breathing synchronised with a virtual body changes bodily self-consciousness ([Adler, Herbelin & Blanke 2014](https://infoscience.epfl.ch/entities/publication/820c57e6-6ce3-4ddd-9cc2-793d38648c15)). In the "embreathment" illusion, breath was almost as important as visual appearance for ownership, and more important than any other cue for agency ([Monti et al. 2020, J Neurophysiol](https://research.uniroma1.it/node/41924)). It is also the honest substitute for the famous heartbeat-flash illusion (see the heartbeat note below).
- **Build cost:** **M, about 4–5 h.** Unity `Microphone` on Android needs the RECORD_AUDIO permission. Envelope and ring buffer for the 20 s replay. Shader `_Glow` property. Log `breath_env` at 20 Hz in the live trace so the dashboard shows it next to EMG.
- **Demo risk:** medium. Hall noise can swamp quiet exhales: gate on a calibrated noise floor and prefer the hum. If the signal is bad, the glow simply follows a slow 0.2 Hz "guide breath" and the participant breathes with it (paced breathing), which still reads well but is no longer a sync manipulation. Label it as such.
- **New hardware:** none.

### #5 Mind layer: the hand that obeys intent, then the hand that moves by itself (agency vs ownership)
- **What:** an upgrade of the PRD's stretch agency phase. (a) 15 s: the participant *intends* to make a fist but keeps the real hand still (an isometric squeeze), and the normalised EMG closes the virtual hand. On the dashboard the virtual hand visibly closes **before** any movement on the IMU/hand-tracking trace, because EMG leads motion by tens of ms ([EMG precedes movement onset, ~50 ms](https://arxiv.org/html/2603.05418)). (b) The final 5 s: the virtual hand **closes on its own** while EMG is flat. Ask q5: "I caused that movement."
- **Why it helps:** it splits the Self into two machine-made parts. Ownership ("mine") survives passive or foreign movement; agency ("I did it") does not ([Kalckert & Ehrsson 2012, double dissociation](https://pmc.ncbi.nlm.nih.gov/articles/PMC3303087)). For Theme 5 this is the "mind" half: even the doer-sense (*kartritva*) is a computed match of intention and feedback, so it is appearance, and the witness watched both. Intentional binding is the classic implicit agency measure ([Haggard et al. 2002 overview](https://www.hedtags.org/hed-task/tasks/hedtsk_intentional_binding.html)), but it is too slow to run in a demo; mention it only.
- **Build cost:** **M–L, about 5–7 h**, *only if Node B and the FR-VR-07 rest/MVC calibration already work*. Hand pose blend driven by `EmgLevel01` with hysteresis; a scripted autonomous close; one more questionnaire item.
- **Demo risk:** medium-high. EMG false triggers from motor noise (PRD risk row) and from judges who tense up. Keep it as stretch. Fallback: drive (a) with the participant's *real* finger flexion from hand tracking (the visuomotor version), which keeps the agency/autonomy contrast without EMG.
- **New hardware:** none.

### #6 Stretch the self: the very long arm
- **What:** after SYNC ownership peaks, the virtual forearm slowly lengthens to 2–3× over 20 s. The brush path and motor-band positions scale with it, so touch still lands. The stone then falls on a hand far out on the table.
- **Why it helps:** it is a strong spectator moment on the dashboard mirror, and it shows the body schema is a *model*, editable at will. People accepted ownership of a virtual arm up to 3× real length, and less at 4× ([Kilteni et al. 2012, PLoS ONE](https://pmc.ncbi.nlm.nih.gov/articles/PMC3400672)). Clinically relevant: distorted limb representation in CRPS and stroke is a therapy target ([VR body-representation review](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2020.01962/pdf)).
- **Build cost:** **S–M, about 2–3 h.** The forearm is already a procedural tapered mesh with `forearm_length_cm`. Animate it and recompute brush speed and `WorldPos`.
- **Demo risk:** low to medium. Competes with #2 for the "wow" slot and time. Pick one of #2 and #6 for the main run; #2 carries the Theme better.
- **New hardware:** none.

### #7 (craft, cheap) The witness screen as a live "self-model" meter, plus the right kind of threat
- **What:** (a) a single **Ownership Index** bar on the dashboard and OLED (for example z-scored drift + flinch + q1), rising during SYNC and falling in ASYNC or Dissolve, labelled "self-model: assembling / dissolving". Spectators see the Self being built in real time. (b) Keep the threat an **impact** (falling stone), not a knife or blade. Affective responses to *impact* depend on ownership (sync > async), while responses to *threat* (a knife) appear even without ownership ([Ma & Hommel 2013, Front Psychol](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2013.00604/full)). Score the flinch window from impact, not from the 0.6 s telegraph, to protect the sync−async contrast.
- **Build cost:** **S, about 2 h** (analytics already computes the parts; this adds one composite plus a Flutter bar).
- **Demo risk:** low. Do not call the bar "consciousness".
- **New hardware:** none.

---

## 2. Suggested revised run (demo mode, about 4:40, under 5:00)

Key changes: **ASYNC first, then SYNC** (the "ordinary, unconvinced" state, then the Self is built, then deliberately dissolved). `condition_order: async_first` already exists. Keep counterbalancing for the pilot data and say so. Breath glow runs *during* induction, so it costs no extra time.

| # | Kosha / beat | Time | What happens |
|---|---|---|---|
| 0 | Calibrate | 20 s | Forearm outline. Voice: "Rest. Watch." Mic noise floor sampled |
| 1 | Baseline probe | 15 s | Dark room, ruler, left-index point |
| 2 | *Annamaya* (unconvinced) ASYNC | 40 s | Brush + motors 600 ms late, breath glow = self-replay from 20 s earlier. OLED "ASYNC" |
| 3 | Threat, probe, q1+q4 | 5 + 15 + 15 s | Stone, drift probe, two items |
| 4 | *Annamaya + Pranamaya* SYNC | 35 s | Brush + motors in step, arm breathes with the participant (#4). OLED "SYNC". Ownership Index climbs (#7) |
| 5 | *Manomaya* (you are the brush) | 15 s | Left finger strokes the arm; motors follow (#3) |
| 6 | *Vijnanamaya* (doer), optional | 20 s | EMG intent closes the hand, then it closes by itself (#5). Skip if EMG is not solid |
| 7 | Threat, probe, q1+q4 | 5 + 15 + 15 s | Same as row 3 |
| 8 | *Neti, neti* dissolve | 15 s | Arm fades; strokes continue on empty air (#2). "The touch is here. Where is the hand?" |
| 9 | Reveal | 10 s | Passthrough fades in: the real arm, 15 cm away. "Not this." |
| 10 | *Witness / Anandamaya* | 30 s | Witness screen: body numbers moved, the awareness line (q4) did not. Closing line, then silence (optional hum/Om) |

Total ≈ 280 s with row 6, ≈ 260 s without. If the slot is tight, drop row 6 first, then the q4 items in row 3 (ask q4 once, at the end).

Effort budget if the team can spend ~10 h: #1 (3 h) + #2 (3 h) + #3 (3.5 h) = 9.5 h. Add #4 if time remains. Treat #5 as the PRD's existing stretch item.

---

## 3. Considered and rejected

- **Heartbeat-synced pulsing hand (cardio-visual RHI):** the strongest interoceptive result in the literature ([Suzuki et al. 2013](https://www.sussex.ac.uk/broadcast/read/20658); [Aspell et al. 2013](https://infoscience.epfl.ch/record/190308?ln=en)), but there is no heart sensor in the BOM and the Quest has no PPG. The BioAmp EXG Pill *can* record ECG ([datasheet](https://robu.in/wp-content/uploads/2024/04/BioAmp-EXG-Pill-Data-Sheet.pdf)), but ECG needs electrodes across the heart axis (for example both wrists, Lead I), while the single channel is committed to right-forearm EMG, and gel electrodes cannot be moved mid-run. A Lead-I placement with ECG and EMG split by frequency band is possible in principle, but the left arm moves for drift probes (motion artefact), the EMG flinch would mix both arms, and the Pill would need re-jumpering. Verdict: **not in 2 days.** At most a 30-min bench curiosity *after* the MVP. Never play a generic heartbeat and present it as theirs. Breath (#4) is the honest interoceptive substitute.
- **Full-body / out-of-body illusion (Lenggenhager/Ehrsson 2007):** needs a third-person view of one's own body and touch on the back or chest ([Lenggenhager et al. 2007](https://infoscience.epfl.ch/record/154869); [Ehrsson 2007](https://www.neuro.ki.se/ehrsson/pdfs/Ehrsson-Science-2007-with-SOM.pdf)). The motors are on the forearm and there is no body avatar pipeline: L effort. Cite it in the pitch as lineage only.
- **Body swap / Machine to Be Another:** needs a second camera-headset and a performer ([BeAnotherLab](https://beanotherlab.org); [Petkova & Ehrsson 2008](https://sciencedaily.com/releases/2008/12/081202115148.htm)). New hardware; reject.
- **Enfacement / mirror face:** no face tracking or mirror pipeline in the stack, and it is off-topic for upper-limb rehab.
- **Third arm (two right hands at once):** cheap to render ([Guterstam et al. 2011](https://www.plosone.org/article/info:doi/10.1371/journal.pone.0017208)), but showing the real-position hand spoils the drift probe and muddies the "which one is me" story that #2 tells better. Keep it as a backup if passthrough fails.
- **Group ego-dissolution (Isness-D):** multi-user networked VR ([Glowacki et al. 2022](https://citius.gal/en/research/publications/group-vr-experiences-can-produce-ego-attenuation-and-connectedness-comparable-to-psychedelics/)). Needs several headsets; borrow only its visual language (the glowing body in #4).
- **Libet clock / intentional binding task:** valid agency measure, but each estimate needs dozens of trials. Too slow for a 5-min run.
- **"Predict their answer before they give it" AI:** demand characteristics make q1 noisy ([Lush 2020](https://doaj.org/article/0d4fd24ccccb4322979ecd0db0fd566f)); a wrong live guess in front of judges undermines the demo. #7 shows the same idea without the gamble.
- **Cutaneous-rabbit (saltation) patterns between the two motors:** a nice craft upgrade, since touches feel as if they hop between and even beyond the actuators ([Miyazaki et al. 2010, J Neurosci](https://en.wikipedia.org/wiki/Cutaneous_rabbit_illusion)). But it needs inter-pulse gaps of about 40–80 ms, which **conflicts with firmware safety limit FR-FW-03 (100 ms minimum gap, 50 ms minimum pulse)**. Do not loosen safety limits at a hackathon.
- **Skin-temperature disownership drop (Moseley 2008):** compelling ([PNAS summary](https://www.sciencedaily.com/releases/2008/08/080830192456.htm)), but there is no temperature sensor in the BOM. The MPU6050 die temperature is not skin temperature.
- **Scarier threats (knife, fire):** responses to threat appear even without ownership, so they would shrink the sync−async contrast ([Ma & Hommel 2013](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2013.00604/full)). They are also an ethics and optics risk with judges.

---

## 4. Rehab tie-in (keep the clinical story)

- Mirror therapy came from phantom-limb pain (Ramachandran). A Cochrane review of 62 studies (n = 1982) found improved motor function and ADL after stroke, with pain reduction mainly in CRPS ([Cochrane CD008449](https://cochranelibrary.com/cdsr/doi/10.1002/14651858.CD008449); [overview](https://en.wikipedia.org/wiki/Mirror_therapy)).
- People with stroke report *stronger* ownership and agency in the RHI, with blunted GSR, temperature and EMG responses in the paretic hand. This is a measurable biomarker of body-schema plasticity ([Llorens group, UPV](https://riunet.upv.es/handle/10251/148003)). Phantom Hand's drift plus EMG/IMU flinch is a portable version of that panel.
- VR embodiment is used to re-own limbs in CRPS and phantom-limb pain and to correct distorted limb representation after stroke ([Frontiers review 2020](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2020.01962/pdf); [CRPS VR](https://hci.uni-wuerzburg.de/topics/20260713-crps-vr-therapy/)).
- Pitch line: *"The same knobs that dissolve a Self in the demo are the knobs a therapist turns to give a stroke patient their hand back."* #3 (self-touch) and #5 (EMG intent shown on a hand that cannot yet move) are the most direct rehab extensions: intent made visible before motion returns.

---

## 5. Sources

- Ehrsson lab, invisible hand: https://news.ki.se/scientists-create-phantom-sensations-in-non-amputees · https://www.nationalgeographic.com/science/article/the-invisible-hand-illusion · invisible body: https://news.ki.se/scientists-create-the-sensation-of-invisibility
- Phantom touch in VR (Pilacinski et al. 2023): https://news.rub.de/english/press-releases/2023-11-14-neuroscience-when-we-feel-things-are-not-there
- Somatic/self-touch RHI (Ehrsson et al. 2005): https://pmc.ncbi.nlm.nih.gov/articles/PMC1395356
- Visuomotor virtual hand illusion (Sanchez-Vives et al. 2010): https://doaj.org/article/843caa6308d8480980b8a38cefaf5359
- Very long arm (Kilteni et al. 2012): https://pmc.ncbi.nlm.nih.gov/articles/PMC3400672
- Third arm (Guterstam et al. 2011): https://www.plosone.org/article/info:doi/10.1371/journal.pone.0017208
- Ownership vs agency (Kalckert & Ehrsson 2012): https://pmc.ncbi.nlm.nih.gov/articles/PMC3303087
- Intentional binding (Haggard 2002), task overview: https://www.hedtags.org/hed-task/tasks/hedtsk_intentional_binding.html
- EMG precedes movement onset: https://arxiv.org/html/2603.05418
- Cardio-visual hand (Suzuki et al. 2013): https://www.sussex.ac.uk/broadcast/read/20658 · cardio-visual full body (Aspell et al. 2013): https://infoscience.epfl.ch/record/190308?ln=en
- Visuo-respiratory (Adler, Herbelin & Blanke 2014): https://infoscience.epfl.ch/entities/publication/820c57e6-6ce3-4ddd-9cc2-793d38648c15 · embreathment (Monti et al. 2020): https://research.uniroma1.it/node/41924
- Full-body illusion (Lenggenhager et al. 2007): https://infoscience.epfl.ch/record/154869 · OBE (Ehrsson 2007): https://www.neuro.ki.se/ehrsson/pdfs/Ehrsson-Science-2007-with-SOM.pdf · Blanke & Metzinger 2009: https://infoscience.epfl.ch/items/32d4c4e1-04df-41c1-95ab-b698cf7b73d4
- Body swap (Petkova & Ehrsson 2008): https://sciencedaily.com/releases/2008/12/081202115148.htm · Machine to Be Another: https://beanotherlab.org · https://www.planetdigital.uzh.ch/en/project/the-machine-to-be-another-body-swap
- Isness-D (Glowacki et al. 2022): https://citius.gal/en/research/publications/group-vr-experiences-can-produce-ego-attenuation-and-connectedness-comparable-to-psychedelics/
- SoundSelf: https://www.labiennale.org/en/cinema/2020/venice-vr-expanded/sound-self-technodelic · https://vrscout.com/news/psychaedlic-vr-sound-self-now-available
- Impact vs threat (Ma & Hommel 2013): https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2013.00604/full
- Demand characteristics (Lush et al. 2020): https://doaj.org/article/0d4fd24ccccb4322979ecd0db0fd566f
- Disownership temperature (Moseley et al. 2008): https://www.sciencedaily.com/releases/2008/08/080830192456.htm
- Cutaneous rabbit: https://en.wikipedia.org/wiki/Cutaneous_rabbit_illusion
- HMD/IMU respiration: https://openaccess.cms-conferences.org/publications/book/978-1-958651-05-6/article/978-1-958651-05-6_0 · accelerometer respiration: https://hal.archives-ouvertes.fr/hal-03501205
- BioAmp EXG Pill: https://robu.in/wp-content/uploads/2024/04/BioAmp-EXG-Pill-Data-Sheet.pdf · https://www.tindie.com/products/upsidedownlabs/bioamp-exg-pill-sensor-for-ecg-emg-eog-or-eeg/
- Quest passthrough / camera API: https://developers.meta.com/horizon/documentation/unity/unity-sample-camera-viewer
- Vedanta: Kosha https://en.wikipedia.org/wiki/Kosha · Sakshi https://en.wikipedia.org/wiki/Sakshi_(witness) · Drg Drsya Viveka https://www.yesvedanta.com/drg-drsya-viveka/lecture-19/ · Turiya https://en.wikipedia.org/wiki/Turiya
- Rehab: Cochrane mirror therapy https://cochranelibrary.com/cdsr/doi/10.1002/14651858.CD008449 · stroke RHI (UPV) https://riunet.upv.es/handle/10251/148003 · VR body representation review https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2020.01962/pdf · CRPS VR https://hci.uni-wuerzburg.de/topics/20260713-crps-vr-therapy/
