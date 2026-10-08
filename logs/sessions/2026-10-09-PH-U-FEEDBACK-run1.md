# PH-U-FEEDBACK run 1 (2026-10-09, 00:15 to 01:30 IST, orchestrating session on the build PC): the owner's five points after the first runs in the headset

The owner, after wearing the headset for the runs of PH-U-DIAG-run1, asked for five things: (1) both hands possible, one at a
time, and for now the LEFT; (2) the camera at eye level by default; (3) the questions answerable by hand ("when the question is
asked I am unable to interact with it"); (4) the EMG graph readable ("the range 0-3000 barely shows a spike"); (5) the
artificial hand copying the real hand's movements. This log says what was built for each, how it was checked, and what only a
headset can still show. Tags: **[V]** verified here with the command named, **[N]** not run on the device named.

## 0. State at 01:10

| # | Asked | Built | Checked | Open |
|---|---|---|---|---|
| 1 | left hand, one at a time | `stimulated_side` left or right, default left; the layout mirrors | EditMode, and a whole scripted run on the left arm [V] | on the headset [N] |
| 2 | camera at eye level | the rig is moved so the head is at the scene's eye point; the calibration takes the arm at any table height and moves the room to it | EditMode, four tests [V] | on the headset [N] |
| 3 | answer the questions by hand | a tracked fingertip presses the buttons (touch, or 0.6 s over one); a dot shows the fingertip | EditMode, five tests [V] | on the headset [N] |
| 4 | EMG graph readable | the axis follows the signal; a rest line; "x resting" for the newest peak | Flutter 526/526, a picture looked at [V] | on the phone [N] |
| 5 | hand copies the real hand | 15 finger joints and the wrist from 21 tracked joints | EditMode 74 tests, two sheets of pictures looked at [V] | with a real hand [N] |

Commits: 8184943 (interface), f30d4ff (left arm, fingertip press), c5307bc (seat, calibration, joints), 7b67165 (EMG plot),
and the commit of the hand pose after it. EditMode 855/855; PlayMode `"PhantomHand|PH_"` 30/30 at c5307bc (589 s), run again
after the hand pose: section 6.

## 1. The left arm
The game was written for a right arm: the real arm rests at +x, the virtual one 15 cm toward the middle, the left index points
in the probes. Now `stimulated_side` (manifest, three identical copies, default `left`) names the arm; `PhantomHandParams.Arm`
and `.Pointer` are the two hands; `PhArm` gives the joint names and mirrors an x. A left arm is the right-arm layout mirrored:
- `PhantomAnchors.LayOutFor` mirrors the arm-rest outline, the virtual arm's anchor and the HUD in x (a rotation, so the HUD's
  text still reads), once, when a presenter is bound;
- `VirtualArmRig.LeftArm` builds the arm mirrored in its own x and puts it to the RIGHT of the real wrist. The hand model's
  bones are bound in the unmirrored pose and the mirror sits above them, so every local rotation that closes the right hand
  closes the left one too;
- the drift's sign follows the arm (`DriftProbe.TowardVirtualSignX`); positive is still "toward the virtual hand";
- the scripted hands and the scripted participant of the editor runs answer to the other side's joint names.
The operator's app shows "Which arm" with left and right (its form is built from the manifest). The Ready card and the health
line of the device log name the arm. [V: `LeftArmTests` (5), the scripted-hands test, and PlayMode
`PH_Standalone_NoHubNoNodes` on the default: `session start: ... arm=left`, calibration `ok` at wrist x -0.18, 14 phases, valid]

## 2. Eye level, and why no calibration had ever completed
The scene is laid out around one eye point (0, 1.18, 0.02). The headset reports the head relative to its own origin, floor
level; the system's recentre corrects position and heading but never height. The owner recentred ten times in two minutes on
the first night. `PhSeat.Align` moves the camera rig so the head is at the eye point facing the table: 0.75 s after the head is
tracked, when the headset's origin jumps (more than 25 cm or 25 degrees in one frame: the system's recentre), when the headset
is put on again, and on the operator's "recenter"; only while no run depends on where the room stands.

That alone would have made the calibration impossible for most people: the wrist had to be within 3 cm of a point at the
VIRTUAL table's height, and an eye-level room puts the virtual table wherever the wearer's build puts it. In the three runs of
the first night the calibration never confirmed (the operator skipped it each time). Three changes:
- the outline counts on the table plane (4 cm), with 18 cm of tolerance in height (`CalibrationTracker`, a fourth argument);
- while calibrating, the virtual arm lies exactly where the real one is (no offset), at table height, turning with the forearm:
  the wearer sees the arm they are placing. The 15 cm offset comes with the induction, after the dark probe;
- when the arm has been held for 2 s the camera rig glides up or down during the 0.7 s of "done", so that the resting wrist is
  at table height. The room comes to the arm; the virtual arm then lies on the table where the real one feels one.
[V: `SeatAndCalibrationHeightTests`: a head 40 cm aside, 1.62 m up and turned 40 degrees lands on the eye point with its tilt
kept; a left arm resting 7 cm low calibrates, the rig rises 7 cm, the recorded wrist is at table height]

## 3. The questions
The panels were wired for the Interaction SDK's poke (`PhUiKit.AttachPoke`), but the scene contains no poke interactor (0
references in `PhantomHand.unity`) and every hand visual is switched off, so on the headset nothing could press a button and the
wearer saw no hand to press with. `PhFingerTouch` needs only the fingertip position the hand source already gives: a button is
pressed when an index fingertip (either hand) reaches its surface after 120 ms over it, or stays over it within 4.5 cm for
0.6 s; one press per visit; a hand sliding along the row presses nothing. The hovered button grows while the finger dwells and
the probes' dot rides the fingertip. [V: `FingerTouchTests` (5) on a real uGUI panel at the scene's pose]

## 4. The EMG graph (phone app)
A Sonnet builder in a worktree; merged as 7b67165. The vertical range is the 20 s window's minimum and maximum plus 10 % on each
side, at least 60 counts wide, inside 0 to 4095; it widens at once and narrows slowly; a dashed line marks the resting level
(the median) and the caption says how high the newest peak is over it. With made-up samples at the levels measured on a person
(rest 230, a +90 and a +260 contraction) the axis reads 170 to 520 and the second contraction is a spike to nine tenths of the
plot; on the old axis both were bumps a few pixels high. [V: Flutter 526/526; `phantom_card_realrun_phone_dark_1.0x.png` looked at]

## 5. The hand
Two parts. The headset side (`MetaHandSource.TryGetSkeleton`) gives 21 joint positions per hand. From them
`PhHandPoseSolver` (a Sonnet builder, worktree, compiled against the editor's DLLs) measures the bend of each of the 15 finger
joints in that finger's own bending plane and the direction the hand points and faces; `PhRiggedHand.SetFlexion` turns each bone
by the measured bend minus the bend the model has at rest; `VirtualArmRig.SetHandOrientation` turns the hand at the wrist (at
most 75 degrees). The presenter applies both while the arm follows the real one (the calibration and the reveal), smoothed over
50 ms. For the induction and the stone the hand is flat and open again; the agency phase keeps its muscle-driven closing.
[V: EditMode 74 tests; `sim/out/quest_diag/hand_poses_right.png` and `hand_poses_left.png` (flat, fist, point, half, wrist up)
looked at: the fingers close toward the palm, the index points, the thumb is on the correct side, the left hand is a mirror]
To check first on the headset: a flat hand, palm down, gives a flat virtual hand (if the fingers bend backwards, the tracked
positions are mirrored against what the solver expects); and the thumb, whose three bones were matched to the tracked thumb's
joints by their order, not by measurement.

## 6. Builds (all local, none installed: at 01:10 the headset was asleep and adb listed it as `unauthorized`)

| File | Built | Bytes | sha256 |
|---|---|---|---|
| `releases/game/0.1.0/chetna-phantom-hand.apk` | 01:05 to 01:10, `result=Succeeded` | 91 864 870 | `05e2d67b8336f9482ead795e96f83128c6233e1e90addb39d760ebb886e0e1ff` |
| `releases/app/1.0.0/chetna-operator-app-pc.apk` | 01:09 | 65 706 104 | `c49d07716f7fff1b7c89b3350251591f02da1ce99e4e82a8b64e12a0380c02fc` |
| `releases/app/1.0.0/chetna-operator-app.apk` | 01:09 | 65 706 100 | `6c0cb478eb71bb769b4e8dc357218780bd877780db6d80202b197d87fd54762d` |

The game APK: the bridge token 0 hits in 875 entries, this PC's address 0 hits; `PhFingerTouch`, `PhHandPoseSolver`, `PhSeat`,
`stimulated_side` and the Ready card's "Sleeve arm" are in it. It also holds everything of PH-U-DIAG-run1 that the headset's
23:12 build lacks (the run pauses when the headset comes off, the link is rebuilt on waking, uploads retry). The phone APKs hold
the EMG plot, the hub's two fixes and the manifest with "Which arm: left / right". [V the files; N on the devices]

## 7. What to look at first on the headset, in this order
1. The room at start: the table in front at a natural height without any recentre. Then long-press the Meta button: the view
   must come back to the same seat.
2. The Ready card: "Sleeve arm: left".
3. Calibration: the LEFT forearm in the outline (it is on the left now); the virtual arm lies where the real arm is and turns
   with it; the fingers copy the real ones (a flat hand must look flat); after 2 s "done", and the room may glide a few
   centimetres up or down.
4. The dark probe: point with the RIGHT index finger.
5. The induction: the virtual arm now lies to the right of the real one, flat and still; strokes on the sleeve.
6. The questions: touch a number with a fingertip, or hold the fingertip over it for half a second; a dot shows the fingertip.
7. On the phone: the muscle trace with its own axis, a dashed rest line, "x resting".

## 8. Not done
- Nothing of this has run on a headset or a phone yet.
- A left-arm run recorded as left: `session.json` carries `stimulated_side` in its block's parameters; the analytics and the
  report do not use the side.
- The Hindi strings were not touched; the Ready card and the hints of this night are English only.
- The hand's wrist turns, the forearm does not: a hand rolled over (palm up) is limited to 75 degrees and twists at the wrist seam.
- The poke wiring of the panels (`AttachPoke`) is still there and still unused.
