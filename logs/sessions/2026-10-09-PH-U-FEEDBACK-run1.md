# PH-U-FEEDBACK run 1 (2026-10-09, 00:15 to 02:20 IST, orchestrating session on the build PC): the owner's five points after the first runs in the headset

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

**Update, 02:20 (sections 9 to 13).** Point 4 has now been seen on the phone with data arriving live from a run (simulated
muscle data). The phone's hub no longer stops when another app takes the screen (the cause of the lost headset every ten
minutes was found on the phone and worked around in the app). A whole run of the game in the editor passed with the phone
as its hub. The virtual hand now also turns palm up. Points 1, 2, 3 and 5 are still unseen on a headset: the Quest lay
asleep, listed `unauthorized` by adb, all night; the build that carries them (section 12) is waiting for it.

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
   with it; the fingers copy the real ones (a flat hand must look flat; turned palm up, the virtual hand turns too);
   after 2 s "done", and the room may glide a few centimetres up or down.
4. The dark probe: point with the RIGHT index finger.
5. The induction: the virtual arm now lies to the right of the real one, flat and still; strokes on the sleeve.
6. The questions: touch a number with a fingertip, or hold the fingertip over it for half a second; a dot shows the fingertip.
7. On the phone: the muscle trace with its own axis, a dashed rest line, "x resting".

## 8. Not done
- Nothing of this has run on a headset yet. (The phone: sections 9 and 10.)
- A left-arm run recorded as left: `session.json` carries `stimulated_side` in its block's parameters; the analytics and the
  report do not use the side.
- The Hindi strings were not touched; the Ready card and the hints of this night are English only.
- The hand's wrist turns, the forearm does not. Until c6ab2da a hand rolled over (palm up) stopped at 75 degrees; since then
  it turns all the way, at the wrist seam (section 11).
- The poke wiring of the panels (`AttachPoke`) is still there and still unused.

## 9. After 01:30: why the phone lost the headset every ten minutes, and what the app does about it
**Found on the phone.** Its own logs (`adb logcat -b events` and `-b system`) hold, every ten minutes from 23:23:06 to
01:33:07 (14 entries), then seen live at 01:43:07, 01:53:07 and 02:03:06:
`START u0 {act=android.intent.action.MAIN cat=[android.intent.category.LAUNCHER] flg=0x10200000 cmp=<another app>} ... from uid 0`.
A root job on the phone opens another app (the owner knows which) every ten minutes. Whatever had the screen goes to the
background.

**What that did to the hub.** Measured on the app build of 01:09 (Monitor open, the hub answering, then Home at 01:46:05):
the hub answered at 0 s, no longer at 8 s, and not once in the 95 s watched; Android logged `am_freeze` for the app. A
headset sees that as an operator app that is gone until somebody opens the app again. PH-U-DIAG-run1 (section 6) put the
evening's lost hub down to a dozing phone. Whether that loss was a doze or this job cannot be told any more (the phone's
log buffer starts at 23:23); from 23:23 on the phone was awake each time and the app was merely not in front. [V for the
measurement; the link to the earlier loss is an inference]

**What the app does now (commit 1671759).** While the hub runs, a foreground service (`HubKeepAliveService.kt`, type
connectedDevice) keeps the process in the foreground class and holds a partial wake lock (12 h ceiling), so the hub goes
on answering behind another app and with the screen off. `HubController` starts and stops it over one method channel.
Its notice, "Chetna: the hub is running", is only shown when notifications are allowed for the app (not asked for); the
service runs either way. Flutter 526/526.

**Measured after** (installed 01:48:04): `dumpsys activity services` shows `isForeground=true types=0x00000010`, the wake
lock `opus:hub` is held. Home at 01:48:32: every poll answered for the 67 s watched (process state 4, no freeze). Then one
poll every 2 s from 01:50:02 to 02:05:01: 366 of 366 answered, with that other app on the screen from 01:53:07 to
01:57:37 and again from 02:03:06 (`sim/out/quest_diag/hub_keepalive_poll.txt`). [V]

**Not touched:** the root job. It is the owner's, and the operator app no longer needs it gone. After each install the app
starts at "Sign in": Clinician, then the Monitor tab, starts the hub.

## 10. One whole run of the game with the phone as its hub (01:57 to 02:02)
`tools/demo/unity_phone_hub_run.py 192.168.242.162:8787 sim/out/phone_hub_run1 164cd676 twin` plays `PH_FullRun` in the
open editor (the game as it runs on the headset, the scripted participant with its fingertip answers and whole hand)
against the operator app on the phone; with `twin` the two boards are the simulated pair on the PC, so no motor ran.
- The test passed (1/1, 288.7 s); the session it wrote validates (`contracts/validate.py` exit 0), analytics exit 0. [V]
- The phone's hub answered 264 of 264 polls, had the headset connected in every poll from 73.5 s to 280.9 s, and reported
  the 14 phases in order. The run began with the other app on the phone's screen: the hub took the game's connection
  from the background. [V]
- The live card on the phone (pictures in `sim/out/phone_hub_run1/`, git-ignored): "Brush and touch", ASYNC, the time
  left, "Sleeve: Connected", "Muscle sensor: Connected", the link at 3 to 6 ms. Below it the muscle trace: its axis ran
  from 190 to 2760 for the simulated bursts, "6.0x resting" beside the value, a marker at each burst. [V: `phone_095s.png`,
  `phone_scrolled_1.png`, looked at] That is point 4 on the real phone with data arriving live. The muscle data was
  simulated; a person's signal has not been on this plot yet.
- It left one more session in the phone's list: `39a744a7-...`, from a device named `editor-...`. It is simulated.
- The tool saves a picture only while the operator app has the screen, and asks the app back to the front otherwise.

## 11. The hand turns palm up (commit c6ab2da)
Until 01:50 the hand followed the real hand's orientation up to 75 degrees in any direction, so a hand turned palm up
stopped a quarter of the way: the first thing a person tries. `VirtualArmRig.SetHandOrientation` now splits the turn in
two: the part about the forearm's own axis (palm up, thumb up) is followed all the way round, the bend away from that axis
keeps the 75 degree limit (a bad frame still cannot fold the hand back over the arm). The forearm itself does not turn;
the seam lies under the sleeve's cuff. [V: `HandMimicPictureTests`, both arms: palm up, thumb up, palm up and lifted;
`sim/out/quest_diag/hand_poses_left.png` and `_right.png`, now seven poses, looked at: the palm shows, the thumb changes
sides as it should]

A cross-check that was missing: the editor's scripted whole hand is drawn from anatomy (the thumb on the body's side, the
fingers closing toward the table), the solver was written from the tracked hand's side. `ScriptedHands_WholeHand_
ReadsAsFlatPalmDown_ThenAsAFist` feeds one to the other on either arm: back of the hand up, every finger joint 0 +/- 1.5
degrees when flat, 80 / 95 / 60 +/- 6 as a fist. A sign the two did not share would have bent the fingers backwards on the
headset too. EditMode 857/857. [V]

## 12. Builds at 02:10 (they replace the table of section 6; none of the game builds has been installed on the Quest)

| File | Built | Bytes | sha256 | Where it is |
|---|---|---|---|---|
| `releases/game/0.1.0/chetna-phantom-hand.apk` | 02:03 to 02:07 from c6ab2da, `result=Succeeded` | 91 873 506 | `a0f59ad079c71fe8e8c9563f99316cd2e9c66408ae2675e1e6ff92062c223a1d` | on this PC only |
| `releases/app/1.0.0/chetna-operator-app-pc.apk` | 01:44 from the tree of 1671759 | 65 706 328 | `36c41e87e187d9e83c9cb112ba117641fbc2b9879021e7c62eab12343eece34a` | on the team phone since 01:48:04 |
| `releases/app/1.0.0/chetna-operator-app.apk` | 02:05 from 1671759 | 65 706 316 | `3dd0004ab10e16e98bc7d880fa4d51b916c16aefff000dd7ce04d1f6223689b6` | on this PC only |

The game APK: the bridge token 0 hits in 875 entries, this PC's address 0 hits; `PhFingerTouch`, `PhHandPoseSolver`, `PhSeat`,
`stimulated_side`, "Sleeve arm" and `TryGetSkeleton` are in it. The file of 01:34 (`a5ff2b58...7664`, the same without the
palm-up turn) is kept beside it as `.apk.prev`. The phone APKs of 01:09 are in `releases/app/1.0.0/old/`.
The Quest still runs the build of 23:12 on 8 Oct, which has none of the five points. With the new phone app that old build
still starts a run (an unknown `stimulated_side` is a warning, not a refusal), on the right arm.

## 13. What is left, and who can do it
1. **Install the game build.** Only with the headset awake: adb lists it as `unauthorized` while it sleeps. Put it on with
   the cable in, accept the debugging prompt if one shows. A watcher on this PC is waiting for that moment
   (`adb get-state`), the orchestrating session then installs and starts the build.
   By hand: `adb -s 2G97C5ZH4T02Q7 install -r releases\game\0.1.0\chetna-phantom-hand.apk`.
2. **Look at points 1, 2, 3 and 5 in the headset**, in the order of section 7. Added to step 3 of that list: turn the hand
   palm up; the virtual hand must turn with it.
3. **A person's muscle signal on the phone's trace.** The plot was seen with simulated bursts only; the sensor on a person
   gave 1.39 times rest for its one event on 8 Oct (PH-U-DIAG-run1), which is a matter of the electrodes, not of the plot.
4. Unchanged from section 8: Hindi for the night's new strings, the side in analytics and the report, the unused poke wiring.

## 14. 02:45 to 03:15: the first runs on the headset with these builds, what they showed, and build 9
The headset was put on at 02:44:49 (adb then listed it as authorised; a watcher installed build 8 at 02:45:28). The owner
started a run at 02:50:32 by pinching both hands: session `c08c7a38-...`, `arm=left`, 14 phases, finished "completed" at
02:54:41, uploaded to the phone (102 files). Mirror: `sim/out/quest_logs/run2/quest-f43ab9d2/` (git-ignored).
The operator shortened both inductions and the dissolve with "next phase".

**What worked on the headset** [V: the session's events, its log, `contracts/validate.py` exit 0, analytics exit 0]
- Point 1: `session start: ... arm=left`; the outline is on the left once the run starts (picture `quest_b8_run_2.png`).
- Point 2: `seated: the head is at the scene's eye point` 0.8 s after the app came up and again right before the run; nobody
  recentred by hand.
- Point 3: all 8 questionnaire items were answered by fingertip (4, 5, 6, 5 and 6, 5, 7, 6); no operator command stands
  between them. The dark probe confirmed four times with the right index finger.
- The muscle sensor on a person: flinch 7.6 and 7.7 times rest, 108 and 80 ms after the stone; IMU jolt 6.0 and 4.6 m/s2.
  (PH-U-DIAG-run1 had 1.39 times rest for its one event.)
- The sleeve: 85 of 85 sent strokes acked, median 12.9 ms, 95 % under 43 ms; 77 more were cancelled by "next phase".
- The link to the phone: connected once at the start, no drop in the 4 minutes, with the phone's root job firing at
  02:53:07 in the middle of it. 72 frames per second throughout.

**What did not** [V: the same files]
- The calibration timed out after 60 s (`"calibration": {"ok": false}`). The recorded left wrist was never inside the
  4 cm: for 50 of the 60 s it rested about 23 cm from the outline's wrist point (11 cm toward the middle, 18 cm further
  away, 7 cm above the virtual table). The same had happened in all three runs of the first night; height was only part
  of it. An arm rests where the real table and the sleeve's cable let it.
- The instruction read "Rest your right forearm inside the outline", "I can't see your right hand", and the probe told the
  wearer to point with the left index: three texts that knew only the right arm.
- Before Start the table showed the scene as saved: a right arm beside its outline on the right, under the card that says
  "Sleeve arm: left" (picture `quest_build8_1.png`).
- With the arm that far forward the hand lay behind the instruction panel, so the fingers copying the real ones could not
  be seen during the calibration.

**Build 9 (commit aa8501a; built 03:03 to 03:09, `result=Succeeded`, 91 877 702 bytes, sha256
`694aedd89ec0a1c3f0d1e4f8b17a0f89575b67aa73d5609bf73479e4bae8fb86`, token 0 hits; installed and started 03:10:47)**
- The arm may rest anywhere within 35 cm of the outline's wrist point and 25 cm above or below the table. What counts is
  that the wrist stays within 3 cm for 2 s, after 2 s in which nothing counts (the hands that pinched to start are still
  in the air). `CalibrationTracker` has the stillness rule as a fifth argument; without it the old rule holds.
- The room then glides to the arm in all three directions (1 s), so the wrist ends on the outline's wrist point and the
  table, the panels and the ruler are where they were laid out to be; the hand is then in front of the instruction panel.
- `calib_title`, `calib_lost` and `probe_title` have a left-arm twin in English and Hindi (whole sentences, because the
  Hindi word for a side changes with its noun). The Hindi was not read by a native speaker.
- Until the first run is bound the scene shows no arm and no outline, and is laid out for the arm the card names.
- Replayed on the recorded wrist path of that run, the new rule confirms 8.6 s into the phase, with the wrist 13 cm above
  the table: the arm was held still there for 2 s. A wrist held still in the air calibrates in the air; the rule cannot
  tell resting from hovering. [V: scratch replay of the kin files; N on the headset]
- Tests: EditMode calibration, seat and panel tests 41/41 (two new: the tracker's stillness rule with that person's
  numbers; the presenter with an arm 21 cm beside the outline, the rig moves by (-0.11, -0.07, -0.18) and the recorded
  wrist is the outline's wrist point). The whole EditMode suite: 859 of 860; the one failure is
  `RingBuffers_AreSafeUnderConcurrentWritersAndReaders`, a 10 s deadline of the SDK's test missed on a busy PC (it failed
  2 of 3 times alone during the collector's run and passed the third; the code under it did not change). PlayMode Ready-card
  test passed with the new checks. The long PlayMode runs were started after the build: see the next lines of this log.

Seen in passing, not changed: when the headset is put on, the seat is taken 0.75 s later, wherever the head is at that
moment (at 03:07:53 it was 27 cm lower than before); four more "seated" lines followed within 55 ms as the tracking
settled. The calibration now corrects the seat from the arm, and the seat is taken again right before a run starts.
