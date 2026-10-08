# Phantom Hand: judge sheet (Theme 5)

Chetna · Team Kela · Vedanta Makeathon 7–9 Oct 2026 · working subtitle: **Manufacturing the sense of "me"**

Source: the Theme 5 audit of 8 Oct 2026 (applied per 03-SPEC D11–D15), PRD v2 §3, research note
`docs/agent-briefs/ph/research-phantom-hand-additions.md`. Build status is NOT kept here: see `docs/PH_STATUS.md`.
Numbers quoted to a judge come from that visitor's own run or from `docs/PH_FACTS.md`; design values below are
settings, not results.

## 1. The one sentence

We do not simulate consciousness. We build, measure and take apart the feeling of "this is me", and the visitor
watches it happen to them.

## 2. What we claim, and what we never claim

| Say | Never say |
|---|---|
| We created an **artificial sense of self**: "this hand is mine" (ownership) and "I moved it" (agency). | "We created consciousness." |
| That feeling is **conditional**: it is built from timing and it falls apart when the timing breaks. | "We proved consciousness is beyond the body." |
| The visitor was aware of every state. That is our **pointer** to the Tattva. | "The witness did not change" as a result. |
| q4 is a pointer, not proof. | "q4 measures consciousness", "awareness stayed constant". |
| One run on one person is indicative. We report "X of N" whatever it is. | Any percentage or p-value from one person; "works on everyone". |
| A measurement and demonstration rig, built on Chetna's rehab engine. | "Treats / relieves" anything; "clinically validated"; "medical device". |

Engineering one-liner (safe with any judge): *Phantom Hand is a bench prototype that reproduces a published
body-ownership illusion on a Quest 3 and a vibrating, muscle-sensing sleeve, and records how your own body
responds when touch is in time with sight and when it is not.*

**self vs Self.** Small-s *self* is the constructed "mine" and "I did it": that is what the machine changes.
Capital-S *Self* is what the Tattva points to: we do not build it and we do not measure it. The demo makes the
difference between the two something a person can notice.

## 3. Opening (30 seconds)

> Theme 5 asks us to show an artificial sense of Self in body and mind. So we built an experiment instead of a
> talk. In the next four minutes you will feel that a virtual hand is your own. You will see it, feel it, react
> when something hits it, and then we will take away the signals that made it yours. We are not claiming we made
> consciousness. We are showing how easily body and mind build a "me", and that you stay aware while it comes
> and goes.

Do not open with Vedanta vocabulary. Open with: "I am going to make a hand that is not yours feel like yours."

## 4. The run as an argument: SEE → FEEL → ACT → BREAK → DISSOLVE → NOTICE

Order in the build is **delayed first, then in step** (03-SPEC D9): the ordinary, unconvinced state comes first,
then the self is built, then it is deliberately removed. `condition_order = sync_first` gives the other order.

| Beat | Phase in the build | What the visitor lives | What it shows | What we record |
|---|---|---|---|---|
| SEE | Calibrate, baseline probe | A ruler in a dark room: "where is your right hand?" | The starting point | pre drift |
| BREAK | Induction, delayed 600 ms | Brush seen, touch late | Same hand, same touch, wrong timing: not "mine" | drift, flinch, q1–q3 |
| FEEL | Induction, in step (< 100 ms target) | Brush seen and felt together, 15 cm from the real arm | "This is my body" can be **constructed** | drift toward the virtual hand, q1–q2 |
| ACT (opt-in) | Agency | A squeeze closes the virtual hand; then it closes by itself | "I am the doer" can be constructed, and separated from ownership | closes driven / by itself, q5 |
| (react) | Threat | A stone falls on the virtual hand | The body defends what it took as itself | EMG burst, IMU jolt, wrist speed |
| DISSOLVE | Dissolve | The arm fades; the brush and the touch go on in empty space | The felt body outlives the seen body | strokes delivered, `dissolve_start` |
| | Reveal | The hand slides back onto the real one, 15 cm away | "Me" was never where it felt | `passthrough_on` |
| NOTICE | Witness screen | Their own numbers: Body / Mind / The one who noticed | Everything on the screen changed; they noticed each change | `witness_summary`, q4 (pointer) |

Agency is opt-in (`agency_enabled`, `autonomous_close_enabled`): switch it on when Node B streams cleanly; with no
EMG it falls back to the real hand's flexion from hand tracking.

Closing copy in the headset (EN, Hindi alongside):
*The body changed. The touch changed. The feeling of "mine" changed. You noticed every change.*
*Tattva 5: consciousness is beyond the body and mind.*

## 5. Three channels, not one question

1. **Body reaction:** flinch after the stone: EMG, IMU, wrist speed (implicit). This is the spectacle: the
   audience sees it on the trace. It is a proxy with thin published support, so do not rest the claim on it.
2. **Report:** the rating "it felt like my hand", asked after both conditions (q3 is the control item). This is
   the best-supported measure: lead the claim with it.
3. **Felt position:** pointing drift toward the virtual hand (implicit). Show it, never promise it: on one
   person it is small, about the size of the tracking error, and it can come out either way (research R4, A.4).

The result is the **direction** in step vs delayed, on that visitor, not a particular number. "No clear
difference this time" is a normal outcome: say it plainly and show the published pattern instead of arguing
with the screen.

## 6. Questions judges ask

**Why is this Theme 5?** The theme does not ask us to build consciousness; it asks for an artificial sense of Self
in body and mind. Synchronised sight and touch build "this is my body". The squeeze that closes the hand builds
"I am doing this". Break the timing and both fade; remove the virtual body and the touch is still felt. The
constructed self is separated from the awareness that notices it. That is our reading of the Tattva.

**Isn't this just VR?** VR is the instrument, not the idea. We do not stop at the illusion: we measure how it is
built, break it with a 600 ms delay, add agency, and end by removing the virtual body. (The theme guide is said to
list a phantom hand in VR among its example routes: check the PDF before quoting that.)

**How do you know the person felt ownership?** Three channels (section 5), compared in step vs delayed on the
same person. We lead with the two that do not depend on what the person says.

**Isn't the questionnaire just suggestion?** Partly, and the literature says so. That is why q3 is a control
item, why drift and flinch come first, and why q4 is labelled a pointer.

**What if this person shows no difference?** Then that is what we show. People differ a lot in this illusion,
which is why we record instead of assume. One run is one data point.

**Does the order matter?** Possibly. The judged run is delayed-first on purpose and has one stone per condition,
so the second stone is the expected one. In the pilot we alternate the order and report X of N.

**Why 600 ms?** In step we target under 100 ms between seeing and feeling. 600 ms is far outside the window in
which the brain binds sight and touch into one event, so it is a clear break, not a subtle one.

**What exactly is artificial?** Not the consciousness. The ownership and the agency: we manufacture the sensory
evidence that normally tells the brain "this is my body" and "I did that".

**What does q4 prove?** Nothing. It asks the visitor to check whether the one who noticed the changes seemed the
same throughout. It is an invitation to look, in the visitor's own experience.

**The deepest version.** The virtual hand is an object of experience. Timing makes the visitor identify with it
as "my body"; the squeeze adds "I am the agent". Both can be changed and removed, so body and mind show up as
changing contents of experience, not as a fixed definition of the Self. The hardware does not prove Vedanta; it
makes the distinction in Tattva 5 something you can observe in yourself.

## 7. What the judge should walk away with

"They made me experience how the sense of 'I' is built around a body and a mind, and then they took it apart
while I watched."

## 8. Deliberately left out of the judge run

- Breathing arm (A4) and voice-over lines: nice, but they read as effects, and they are not built.
- Agency (A5) is opt-in. Two reviews disagree on it: the Theme 5 audit calls it central to "mind", the demo
  review (R4) says cut it for the judged three minutes. Switch it on only after it has worked on two teammates.
- If the headset path is down: the tier ladder and the no-headset sleeve station are in
  `docs/PH_ON_DEVICE_RUNBOOK.md`. The tier is announced, never hidden.
- A third block (in step → delayed → in step): the run must stay near four minutes and the build, analytics and
  witness screen compare exactly two conditions.
- Engineering detail (two nodes, EMG isolation, watchdog, stroke limits): one sentence if asked; it is in
  `docs/PH_ELECTRONICS_INTERFACE.md`.
