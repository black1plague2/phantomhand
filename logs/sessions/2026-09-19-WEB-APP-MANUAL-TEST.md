# Manual test of the Flutter app in a browser — 2026-09-19 (Opus)

Driven for real in a browser (clicked through, screenshots read back), not asserted from tests.
Served the existing release bundle: `releases/app/0.5.1/web` on `127.0.0.1:8899`.

Android emulator was not an option: creating an AVD would not help, because running the app on Android needs
the same Gradle build that fails on this machine (see below). The web build is the working simulator path.

## What works

| Screen | Result |
|---|---|
| Sign-in | Role picker (Administrator / Clinician / Therapist / Nurse / Patient), clean and legible |
| Patients | 7 synthetic patients with real clinical descriptions and affected-side chips |
| Patient overview | Recovery section, honest "Synthetic estimate … not a validated clinical MDC" caveat |
| Sessions | 7 weekly sessions, each with its reach-trace glyph; glyph strip across the top |
| Session report | Per-trial table: reaction time, movement time, peak speed, SPARC; timeouts shown as `-`, not as zeros |
| Speed profile | Bell-shaped velocity curve with hover readout |
| Devices | Degrades correctly on web: "The hub is not available on web. Run OPUS on Windows or Android to pair a headset." |
| Navigation | Rail + tabs behave at 1280×800 and reflow to a bottom bar at phone width |

## The release bundle is stale — an artifact problem, not a regression

`releases/app/0.5.1/web/main.dart.js` was built **2026-09-18 22:32**. The fixes in `ff5dd83` landed
**2026-09-18 23:46**, 74 minutes later. So the published bundle still shows all three bugs that commit fixed,
and they were plainly visible in the browser:

1. "Smoothness (SPARC)" and "Reaction time" both read **"No sessions recorded yet."** for a patient with 7
   sessions.
2. The Reach-area y-axis repeats **0.1 / 0.1 / 0.1 / 0.0** (insufficient precision).
3. **"Trunk lean — Worse beyond normal variation"** while the plotted line visibly *falls* from 1.2. This is
   the inverted clinical direction, and it is the most damaging of the three: it tells a clinician a patient
   is deteriorating when they are improving.

All three are fixed in source and covered by `app/test/metrics/metric_format_test.dart`. **Action: rebuild and
republish the web bundle**; do not demo from `0.5.1`.

## New bug found in this session

- **Unformatted number in the speed-profile tooltip.** It renders the raw double
  `1.2472826592065636` (wrapped across two lines as "1.247282659206 5636"), with no rounding and no unit.
  Everything else in the app formats to 2 significant figures with a unit (`1.35 m/s`), and
  `docs/APP_DESIGN.md`'s copy rules require it. Fix in the chart's tooltip builder to use the shared
  `metricValueText` / `metricDisplayText` helpers in `app/lib/shared/metrics/metric_format.dart`.

## Android / phone: still blocked, with the root cause now isolated

`flutter build apk --debug` fails in ~3 s with `java.io.IOException: Unable to establish loopback connection`.
Stack trace shows it failing inside `sun.nio.ch.PipeImpl$Initializer` while Gradle forks its **daemon**.

Diagnosis done this session:
- Loopback itself is fine on this machine — the Flutter hub, the Unity live link and the haptic simulator all
  talk over 127.0.0.1 continuously.
- Java is fine too: a small program calling `java.nio.channels.Pipe.open()` **succeeds on both installed
  JDKs** (Android Studio JBR and JDK 22).
- Adding `-Djava.net.preferIPv4Stack=true -Djava.rmi.server.hostname=127.0.0.1` to `org.gradle.jvmargs` did
  not help (reverted).

So it is specifically the Gradle **daemon fork** that cannot be established from inside the agent sandbox,
which matches the conclusion already recorded in `docs/MANUAL_TODO.md`. **The human must run the build in
their own terminal**:

```
cd "C:\Users\GARV BANSAL\Documents\VR_games\OPUS\app"
flutter build apk --debug
```

The phone was also not reachable: `adb devices` listed none after a clean `adb kill-server` /
`adb start-server`. Needs Developer options → USB debugging on, USB mode set to File transfer (MTP), and the
"Allow USB debugging?" prompt accepted.
