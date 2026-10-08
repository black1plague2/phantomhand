# Manual TODO (things only the human can do)

> **Swept by Opus 2026-09-19.** Three entries were resolved but still marked open (run9 hand-mesh
> verification, the GUI-editor block, and `fake_hub.py`'s missing `pong`) and are now ticked. The genuinely
> blocking human items are: the **Electronics team's §4 answers**, the **Gradle loopback APK failure**, and
> the **Visual Studio C++ workload** for a Windows build. Everything else below is informational or
> agent-actionable.

Agents append here instead of blocking. Format: `- [ ] (track) what: exact steps · why it's needed · what's blocked until it's done`

## User / environment
- [x] (U, 2026-09-15 run4) **Unity Editor PID 31560 was hung, not just idle** (resolved by Opus: PID 31560 gone,
  new editor PID 11320 opening `OPUS/game`). `Editor.log` stopped growing at
  ~00:57 IST mid domain-reload (`Begin MonoManager ReloadAssembly` / `[HttpMcpServer] Stopped`, no matching
  `Domain Reload Profiling` line after it), but `Get-Process -Id 31560` shows ~21.9 hours of accumulated CPU time
  against ~7.6 hours of wall-clock runtime (i.e. it is actively spinning across multiple threads, not blocked/idle)
  and `Responding: True`. The Meta AI Agent Bridge (port 48736) is not listening, and `unity status` /
  `unity pipeline list` see no reachable instances either (the Pipeline package still isn't installed in this
  project, confirmed again this run). Sandbox policy blocked this agent from `taskkill`-ing the stuck process
  (auto-mode classifier: "Interfere With Workloads"), so it could not self-recover. **Action needed:** force-quit
  `Unity.exe` (Task Manager → End Task, or `taskkill /PID 31560 /F` in your own terminal) and reopen the project
  (`OPUS/game`) via Unity Hub or `unity open "OPUS/game"`. Once reopened and the Meta AI Agent Bridge auto-starts
  (or you click "Start Server" under the Meta bridge settings if it doesn't), the next Unity-track agent can resume
  R2 (Building Blocks rig), R5 (visual audit), R6 (live link + latency), and R7 (Android build) — none of these can
  proceed without a live bridge/editor. R1 and R3 file-level prep continued without the editor this run (see
  `logs/sessions/2026-09-15-U-unity-run4.md`).
- [x] (ops) Revoke the gh CLI OAuth token that was exposed in the `anubhav` remote (done by user; replaced by a new login)
- [x] (ops) (resolved 2026-09-15 by user; push succeeded 764960a..6fde4cb) **GitHub push blocked (403):** the new `gh` login is a fine-grained PAT without access to private repo `black1plague2/opus-rehab` (the API returns 404). Fix either way: (a) GitHub → Settings → Developer settings → Fine-grained tokens → edit the token → Repository access: add `opus-rehab` with **Contents: Read and write**; or (b) in your own terminal run `gh auth logout`, then `gh auth login` → GitHub.com → HTTPS → **Login with a web browser**. Then `gh auth setup-git` and `git -C "C:/Users/GARV BANSAL/Documents/VR_games/OPUS" push --follow-tags`. Local commits are safe meanwhile.
- [x] (U) Install the standalone Meta XR Simulator (user reports installed, 2026-09-14)

## Track A (app) — added 2026-09-19
- [ ] (A, human) **Build the APK in your own terminal.** `cd app && flutter build apk --debug`. It cannot be
  built from an agent sandbox: Gradle fails in `sun.nio.ch.PipeImpl` while forking its daemon. Confirmed this
  is NOT a Java or loopback problem — `java.nio.channels.Pipe.open()` succeeds on both installed JDKs, and the
  hub/live/haptic simulators use 127.0.0.1 constantly. Once it builds: `adb install build/app/outputs/flutter-apk/app-debug.apk`.
- [ ] (A, human) **Phone not visible to adb.** After a clean `adb kill-server && adb start-server`,
  `adb devices` lists nothing. On the phone: Settings → About phone → tap Build number 7×; Developer options →
  USB debugging ON; notification shade → USB mode → File transfer (MTP); accept "Allow USB debugging?".
- [ ] (A) **Rebuild + republish the web bundle.** `releases/app/0.5.1/web` was built 2026-09-18 22:32, but
  `ff5dd83` landed 23:46, so the published app still shows the inverted trunk-lean label, the empty SPARC/RT
  recovery charts and the coarse reach-area axis. Do not demo 0.5.1.
- [ ] (A) **Speed-profile tooltip prints a raw double** (`1.2472826592065636`, no rounding, no unit) instead of
  the formatted `1.25 m/s` every other readout uses. Fix in the chart tooltip builder via the shared helpers in
  `app/lib/shared/metrics/metric_format.dart`; `docs/APP_DESIGN.md`'s copy rules require the formatted form.

## Track U (Unity)
- [ ] (N, 2026-09-19 run3) **Re-run the full-pipeline session recording to confirm the `endpoint_error_cm`
  chest-relative fix against real data.** `game/Assets/Games/OrchardReach/Runtime/OrchardReachModule.cs`
  (`TargetToTrialTarget`/`SubtractChest`) now subtracts the chest reference from the target position before
  writing the `target_shown` event, fixing the 79-133 cm frame-mismatch bug from
  `logs/sessions/2026-09-19-FULL-PIPELINE-RUN.md`. But every session currently under `app/.hub_data/`
  predates that fix (file mtime 00:52 IST vs. the newest session directory's 00:45 IST) — re-running
  `analytics/opus_analytics` on the newest of them (`fdf86238-1616-4eb1-ad3c-04b1301e5c06`) still reproduces
  ~79-133 cm. **Action needed:** re-run the Unity `FullPipelineIntegrationTests` PlayMode test (with the same
  Flutter hub + `fake_haptic` peers as before) to produce one fresh session, then re-run
  `./analytics/.venv/Scripts/python.exe -m opus_analytics <new session dir>` and confirm `endpoint_error_cm`
  drops to a few cm. Full analysis in `analytics/VALIDATION.md` section 10. Track N cannot do this itself
  (no Unity access).
- [ ] (N, 2026-09-19 run3) **`app/assets/fixtures/sessions/longitudinal__*` (Track A's bundled demo fixtures)
  were generated before this run's `patient_ref` fix** (`sim/synthetic_patients/longitudinal.py` /
  `session.py` previously gave every session in a longitudinal series a different `patient_ref` —
  `synthetic-longitudinal-9000`, `-9100`, `-9200`, ... instead of one shared id; now fixed, see
  `logs/sessions/2026-09-19-N-analytics-run3.md`). `contracts/fixtures/sessions/longitudinal/` (this track's
  own copy) has been regenerated and now shares one `patient_ref` (`synthetic-longitudinal-001`) across all
  7 weeks. `app/assets/fixtures/sessions/longitudinal__week_*` is a **separate, app-owned copy** (flattened
  filenames, plus `traces.json` this track doesn't produce) that Track N did not touch or regenerate — it is
  out of this track's scope (`app/` is off-limits). If the app's patient-history grouping was relying on that
  stale copy specifically, Track A should regenerate it from the fixed generator (`python -m
  synthetic_patients fixtures --out contracts/fixtures/sessions`, or whatever produced the `app/assets` copy
  originally) so the app and analytics fixtures actually agree.
- [x] (U, 2026-09-17 run9 — **RESOLVED 2026-09-19, Opus**: the batch run happened; the ghost hand is visible and correct in `logs/sessions/screens/unity/run11/during_trunk_lean.png`, EditMode 107/107 and PlayMode 6/6. The wrist-`rot` limitation in point (1) below still stands and is a fixture-generator issue, not a Unity one.)
  **Hand mesh swapped from a bare sphere to `ghost_hand_static`, but the batch run to actually compile/test/
  screenshot it never happened this session.** `Assets/Shell/Editor/OrchardSceneDressingTool.cs` now instantiates
  `Assets/Art/Prefabs/ghost_hand_static/ghost_hand_static.prefab` (`DemoHandProxy_R_GhostHand`) and
  `OrchardReachSceneController.DriveGhostHandVisual()` poses it at the wrist each frame. Two things could NOT be
  done and need a human/next-run follow-up: (1) **"oriented by the recorded wrist rotation" is factually
  impossible with this project's data** — verified by reading `contracts/fixtures/sessions/healthy/kin_000.json`
  directly: `r_wrist`'s `rot` field is absent (null) for every frame, so `SyntheticHandDriver.TryGetJointPose`
  always returns `rotQuatXyzw = null` for it. The code instead derives a heading from wrist->index-tip direction
  — if real recorded wrist orientation is ever wanted, the fixture GENERATOR (whatever produced
  `contracts/fixtures/sessions/healthy/kin_*.json`) needs to start emitting `rot` for the wrist joint; this is not
  something the Unity side can fix. (2) The result has never been screenshotted/visually confirmed — the GUI
  editor being open this session blocked the batch run that would produce `run9_playmode/*.png`. Old bare-sphere
  screenshots remain at `logs/sessions/screens/unity/run8_playmode/*.png` for reference only; they do NOT reflect
  this change.
- [x] (U, 2026-09-17 run9 — **RESOLVED**: no GUI editor has been open since; every run since run10 has been genuine batch mode, confirmed by an absent `game/Temp/UnityLockfile`.) **User had the GUI Unity Editor open (PID 33516, "game - OrchardReach - Android -
  Unity 6.4") — Track U cannot run its usual batch-mode workflow while it's open**, per this track's own
  documented root-cause rule (a GUI editor locks the project; batch `-executeMethod`/test runs need the GUI
  closed). Please close that Unity Editor window before the next Track U run so it can compile/test/screenshot
  this run's code changes (see `logs/sessions/2026-09-17-U-unity-run8.md` for exactly what's pending
  verification). Two other things checked and found NOT usable as an alternative this session, for the record:
  (1) the generic Unity-MCP-skill toolset (`unity-mcp-cli`) reports this project has **no MCP plugin package
  installed** (`Packages/manifest.json` has no `unity-mcp`-style entry), so it cannot reach this project's Editor
  even with the GUI open; (2) the Meta AI Agent Bridge (port 48736) IS listening with the GUI editor open, but
  authenticating to it needs a registry-stored access token that this agent's own sandbox permission classifier
  blocks reading (flagged "Credential Exploration") — this is a hard safety stop, not a missing tool, so don't
  ask a future agent to try to route around it; if this bridge path is wanted, a human needs to relay the token
  or open a different, permitted channel.
- [ ] (U, 2026-09-17 run8) **No grass texture asset anywhere in the project.** `OrchardSceneDressingTool.PlaceGround`
  uses a flat, saturated-green `Assets/Art/Materials/OrchardGround.mat` (URP/Lit, no texture) — checked both
  `Assets/Art/` and `Assets/MetaAssets/` for any grass/ground texture and found none. This satisfies the ">=40x40m
  ground" requirement numerically (`SeatedLayoutTests.Ground_IsAtLeast40x40_AndCenteredUnderPlayer`, 45x45m) but
  not a literal "grass material" ask. Needs a new grass texture asset (tileable, ASTC-compressible) before this
  can be a real textured material rather than a flat color.
- [ ] (U, 2026-09-17 run8) **`CenterEyeAnchor`'s (and `TrackingSpace`'s) local rotation resets to identity on
  scene reopen**, even in batch/no-headset Edit mode with no Play mode involved — confirmed by 3 separate failed
  attempts this run (direct `CenterEyeAnchor.localRotation` set: saved correctly to the `.unity` file, per a grep
  of the saved scene, but read back as identity by a *separate* batch process reopening it; a wrapper-GameObject
  reparenting attempt: also reset, AND broke `RigValidatorTests.Rig_HasExactlyOneEnabledCamera` — 2 cameras
  enabled instead of 1; pitching `TrackingSpace`'s own local rotation directly, no reparenting: camera count
  stayed correct but the rotation still reset). Full narrative in
  `logs/sessions/2026-09-17-U-unity-run7.md` checkpoint 8, bug #1. Worked around by pitching the RIG ROOT
  ("[BuildingBlock] Camera Rig") instead, which is NOT one of the anchors this reset logic touches — but that is a
  documented compromise (a real headset's live head tracking updates `CenterEyeAnchor`, not the rig root, so this
  static pitch would compound with a real patient's own head tilt on real hardware rather than being superseded
  by it). **What's needed:** either find and neutralize whatever resets these anchors in batch/Edit mode (likely
  `OVRCameraRig.UpdateAnchors()` or similar, running via `[ExecuteInEditMode]`/`[ExecuteAlways]`), or — better —
  implement R4's calibration step to set the seated gaze pose from a real per-session calibration action instead
  of any scene-baked default at all.
- [ ] (U, 2026-09-17 run6) **R7 Android build fails at the Gradle step: `java.io.IOException: Unable to
  establish loopback connection`.** Full evidence in `logs/sessions/unity_logs/run6_androidbuild2.log` and
  `logs/sessions/2026-09-17-U-unity-run6.md` checkpoint 6. IL2CPP itself succeeds (all 2182 C++ units compile,
  `libil2cpp.so` builds and strips fine, ~13 min); Gradle's daemon then fails to start because it cannot open a
  **loopback TCP socket** to talk to its own parent process — this is host-machine networking/security
  configuration, not a Unity or project problem. **Try, in order:** (1) check Windows Defender Firewall (or any
  third-party AV/EDR) for a rule blocking Java/`gradle`/`java.exe` from listening on `127.0.0.1` — add an allow
  rule for `C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\java.exe`
  (or wherever the project's configured JDK lives) for loopback/localhost traffic; (2) check `C:\Windows\System32\drivers\etc\hosts`
  for anything mapping `localhost`/`127.0.0.1` unusually; (3) try disabling the Gradle Daemon entirely as a
  workaround (Preferences → External Tools → Android → or add `org.gradle.daemon=false` to a
  `gradle.properties` the build uses) — slower per-build but avoids the loopback requirement; (4) if a VPN or
  corporate proxy is active, try building with it disabled once to isolate the cause. Once resolved, re-run
  `-executeMethod Opus.Shell.Editor.OpusBuildScript.BuildAndroidApk -quit` (batch mode, editor closed, per this
  track's standing rule) — no code changes are needed on the Unity side.

- [ ] (U, 2026-09-17 run5) **Meta AI Agent Bridge (port 48736) hung again mid domain-reload**, same failure class as
  run4's PID 31560 incident (see the entry above). Sequence: bridge was healthy (compiled clean, ran
  `IReflectionService`/`CompilationTools`/`BuildingBlocksTools` calls fine for ~20 min), then after adding a new
  `Shell.Editor` asmdef (referencing `Oculus.Interaction`/`Oculus.Interaction.OVR`) and calling
  `AssetDatabase.Refresh`, the editor entered a domain reload that never completed. `tasklist` shows PID 28100's
  window title stuck at `Reloading Domain (busy for 03:43)...` and climbing (confirmed not idle/frozen: CPU time
  kept increasing), but `Editor.log` (`C:\Users\GARV BANSAL\AppData\Local\Unity\Editor\Editor.log`) shows it is
  stuck in a **tight infinite retry loop** repeating exactly these two lines thousands of times (line count still
  climbing when checked, e.g. 5194 -> 5196 within ~2s):
  ```
  IPCStream (hubIPCService): IPC stream failed to write (Timed out)
  IPCStream (hubIPCService): Warning: attempting to connect to IPC stream with a partially written outgoing message; this may result in undefined behaviour
  ```
  preceded by `[HttpMcpServer] ... OnBeforeAssemblyReload` (`Library/PackageCache/com.meta.xr.sdk.core@.../Editor/
  MCPBridge/HttpMcpServer.cs`), i.e. the bridge's own HTTP server never came back up after tearing itself down for
  the reload — it's retrying an IPC write forever instead. `netstat` confirms port 48736 has no `LISTENING` entry
  (only stale `TIME_WAIT`/`SYN_SENT`). This is a bug in the Meta AI Agent Bridge package itself
  (`com.meta.xr.sdk.core`'s `HttpMcpServer`/`hubIPCService`), not something this session's code changes can fix.
  **Action needed:** same as run4 — force-quit `Unity.exe` (Task Manager → End Task on the one whose window title
  was "game - OrchardReach..." before it hung, or `taskkill /PID <pid> /F`) and reopen `OPUS/game`. This agent's
  sandbox blocks `taskkill` on a running process (same "Interfere With Workloads" classifier as run4), so it
  cannot self-recover and is continuing file-level work (writing/reviewing code, updating docs) while waiting to
  see if the bridge comes back on its own; if you're at the machine before it recovers, restarting now saves time.
  Once it's back: re-run `Tools/OPUS/Dress Orchard Scene` (`Assets/Shell/Editor/OrchardSceneDressingTool.cs`,
  method `DressOrchardScene()` — already written, not yet executed) via
  `IReflectionService.InvokeStaticMethodFromJson`, verify the scene/prefabs on disk, then continue to R5 (Play-mode
  screenshots) and R6 (live link). See `logs/sessions/2026-09-17-U-unity-run5.md` checkpoint 1 for what's already
  written and waiting to run.
- [ ] (U) Meta Project Setup Tool "Required" fix `fb94093919708b05a2082822adc7abd5` ("Manual selection of Graphic
  API, favoring Direct3D11") needs `UPSTTools.FixTask` which the tool itself warns **restarts the Unity Editor**.
  Given this run already survived one 13+ minute domain-reload hang and one force-restart (see session log
  checkpoints 1d/1e), I deliberately did not trigger another editor restart unsupervised. Apply it yourself (Meta
  \> Tools > Project Setup Tool > Rendering group > fix it) when you're at the machine and can watch the restart,
  or ask the next Unity-track agent to do it as its very first action (least state to lose).
- [ ] (U) Recommended UPST fix "Meta XR Operator requires OpenXR Plugin 1.17.0+" (installed: 1.16.1) is
  auto-fixable via Package Manager but package-version changes are exactly what triggered the earlier long domain
  reload; deferred for the same reason as above. Low urgency (Recommended, not Required).
- [ ] (U) Recommended "Newest Meta XR Simulator not installed" — the user already has the **standalone** Meta XR
  Simulator app installed (per the entry above, 2026-09-14); this UPST check is about the deprecated in-project
  simulator package that UNITY_PRACTICES.md explicitly says NOT to add. Leave as-is; note this if it keeps
  reappearing so nobody "fixes" it into a regression.
- [ ] (U) Recommended "Complete a Data Use Checkup" / "Set up application ID and package name" (both manual,
  Platform SDK-related) — not applicable yet (the project isn't using Meta Platform SDK APIs); revisit only if/when
  that changes.
- [x] (U) `contracts/validate.py --session <dir>` run against the U5 replay-built session directory (deterministic
  path `%TEMP%/opus_u5_sessions_root/11111111-1111-4111-8111-111111111111`, no need to scrape console logs) —
  `[PASS] All validations passed` (see session log `2026-09-14-U-unity-run2.md`, checkpoint 3).
- [ ] (U) M2 (Building Blocks rig: Camera Rig → Hand Tracking → Interaction Rig → Grab on fruit, + a
  rig-validator EditMode test), M4/M5 (Shell + Orchard Reach scenes, materials, screenshots), the rest of M6
  (shell wiring for the now-written `LiveClient`, Android manifest permissions/network-security-config, and the
  `sim/live/fake_hub.py` integration test + RTT/latency measurement), and M7 (Android build) are all **not
  started/incomplete** — see `logs/sessions/2026-09-14-U-unity-run2.md`'s "Final status by milestone" table for
  the full breakdown. This run spent most of its time on M1 (a genuine asmdef misconfiguration causing 512
  compile errors) and recovering from two editor-stability incidents (a 13+ min domain-reload hang, one forced
  restart); M3 (all EditMode tests, 92/92) is fully done with real evidence.

## Track A (Flutter)
- [ ] (A, **do when back at the laptop**, 2026-09-15, still incomplete as of run4 -- `flutter doctor -v`
  still reports "The current Visual Studio installation is incomplete", checked this run; `flutter
  build windows` was skipped per Opus's instruction rather than re-attempted against a known-incomplete
  toolchain) **Approve the Visual Studio install.** Opus launched the VS 2022 installer to add "Desktop development with C++" (user-approved), but it needs a UAC click and the user is remote. A **UAC prompt may still be waiting on screen**: click **Yes** and it installs unattended (passive, several GB). If the prompt is gone, run this in an elevated PowerShell:
  `& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe" modify --installPath "C:\Program Files\Microsoft Visual Studio\2022\Community" --add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended --passive --norestart`
  Then `flutter doctor -v` should show Visual Studio ✓, and `flutter build windows` can run. Only the Windows desktop build (the hub on Windows) is blocked; web + tests + hub CLI work without it.
- [x] (A) JDK for Android builds: set `flutter config --jdk-dir "C:\Program Files\Android\Android Studio\jbr"` (OpenJDK 21.0.10) on 2026-09-15 to avoid the JDK 22 AF_UNIX loopback failure. APK retry assigned to Flutter run4 -- **retried, root cause is NOT the JDK version** (see the 2026-09-15 run4 entry below): the JDK swap alone does not fix it.
- [ ] (A, **2026-09-15 run4, conclusive root cause -- this is not fixable from inside this agent's sandbox**) Retried `flutter build apk --debug` with JDK 21 (`JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"`, confirmed via `java -version`) after `cd android && ./gradlew --stop`: **identical failure**, same stack trace as run3's JDK 22 diagnosis (`java.io.IOException: Unable to establish loopback connection` -> `WEPollSelectorImpl` -> `UnixDomainSockets.connect` -> `Invalid argument: connect`). This run went one level deeper than "try a different JDK": also ran `cd android && ./gradlew assembleDebug --no-daemon --stacktrace` (fails identically inside `SingleUseDaemonClient`, i.e. not daemon-specific either), and then wrote and ran a **minimal 4-line Java program** with no Gradle/Flutter involved at all:
  ```java
  import java.nio.channels.Selector;
  public class SelTest { public static void main(String[] a) throws Exception {
    Selector s = Selector.open(); System.out.println("ok: " + s); } }
  ```
  This **also fails**, with the exact same `Unable to establish loopback connection` / `UnixDomainSockets.connect` stack trace, on plain `java.nio.channels.Selector.open()` -- proving this has nothing to do with Gradle, the Gradle daemon, or the JDK version (reproduced on both JDK 22.0.1 and JDK 21.0.10). **Any Java NIO `Selector` (used internally by countless JVM libraries, including Gradle's socket-based build daemon protocol) cannot be created in this execution environment at all**, because the JDK's internal wakeup-pipe implementation on Windows routes through an AF_UNIX domain socket `connect()` that the OS/sandbox refuses. This is consistent with a coding-agent sandbox's network/IPC policy blocking AF_UNIX sockets specifically (this whole run's shell is itself sandboxed) rather than a real machine-level AV/EDR/firewall issue -- **the fix is to run `flutter build apk --debug` (or the `SelTest.java` repro above, to confirm first) from the user's own unsandboxed terminal**, not from inside a coding-agent session. If it still fails there, then it's a genuine machine-level block and the AV/EDR/Windows Defender ASR investigation in the entry below applies for real.
- [ ] (A) `flutter doctor -v` on this machine reports two environment issues that block M5 builds:
  1. Android SDK lives at `C:\Users\GARV BANSAL\AppData\Local\Android\sdk` — the space in `GARV BANSAL`
     breaks NDK tooling. Fix: move the SDK to a space-free path (e.g. `C:\Android\sdk`) and update
     `local.properties` / `ANDROID_SDK_ROOT`, or install a second SDK copy there and point
     `flutter config --android-sdk` at it.
  2. Visual Studio Community 2026 (18.6.0) install is reported incomplete by `flutter doctor`. Fix: open
     Visual Studio Installer → Modify → ensure "Desktop development with C++" workload is fully installed.
     **Confirmed at M5**: `flutter build windows --release` fails with `Unable to find suitable Visual
     Studio toolchain. Please run 'flutter doctor' for more details.` -- no `.exe` produced, `app/build/
     windows/` doesn't exist. Needed for `flutter build windows` (CMake/MSBuild toolchain).
- [ ] (A) M5: `flutter build apk --debug` **fails** with a different, unexpected error (not the SDK-path
  issue predicted above): `java.io.IOException: Unable to establish loopback connection` from Gradle's
  `assembleDebug` task, after ~260 s. This is a known class of Gradle-daemon issue, typically caused by
  something on the machine blocking a local TCP loopback connection Gradle's daemon needs to itself
  (antivirus/firewall software intercepting 127.0.0.1, a VPN client capturing loopback traffic, or a
  Java/JDK version mismatch with the Gradle version used by Flutter's Android embedding). Full error is in
  `logs/sessions/2026-09-14-A-flutter-run2.md` checkpoint 4. Fix: try `flutter build apk --debug
  --verbose` for more detail, check for security software intercepting loopback (temporarily disable and
  retry), or try `cd android && ./gradlew assembleDebug --stacktrace` directly to see the full Gradle
  stack trace. Not attempted further here since it needs either elevated/security-software changes or
  investigation on the actual machine.
- [ ] (A) **2026-09-15 (run3) update, root cause narrowed further:** ran `cd android && ./gradlew
  assembleDebug --stacktrace` directly (per the suggestion above). Real stack trace: `java.net.
  SocketException: Invalid argument: connect` inside `sun.nio.ch.UnixDomainSockets.connect` inside
  `PipeImpl$Initializer$LoopbackConnector`, i.e. the JDK's own internal loopback `Pipe` (used by
  Gradle's daemon-connection `Selector`) is trying to connect via an **AF_UNIX domain socket**, not
  TCP, and that connect is failing. Reproduced identically with `GRADLE_OPTS=-Djava.nio.channels.
  spi.SelectorProvider=sun.nio.ch.WindowsSelectorProvider` (forcing the legacy selector) -- so it isn't
  the newer WEPoll-based selector specifically; both Windows `Selector` implementations in this JDK
  route their internal loopback `Pipe` through `UnixDomainSockets`, and that fails here every time
  (`java -version`: `22.0.1+8-16`). This smells like either (a) AF_UNIX sockets being blocked for this
  process specifically (security software/sandbox policy), or (b) a JDK 22 Windows regression in
  `sun.nio.ch.PipeImpl` unrelated to this project's code. Next things to try on the real machine (not
  attempted here -- needs elevated/security-software changes or a JDK swap): (1) install a JDK 21 LTS
  and point `JAVA_HOME`/`org.gradle.java.home` at it to sidestep whatever changed in 22's `PipeImpl`;
  (2) temporarily disable AV/EDR/VPN software and retry; (3) check Windows Firewall/Defender "Attack
  surface reduction" rules for anything blocking local Unix Domain Sockets (`%TEMP%\...sock` files) --
  Gradle 8.x's daemon uses these on Windows since ~2023, and this is a known class of failure when such
  sockets are blocked.
- [ ] (A) Windows Firewall: if M3's hub (`HttpServer` on 0.0.0.0:8787, UDP beacon on 8788) can't be
  reached from a second device (e.g. a real or simulated headset) on the same network, run (elevated,
  PowerShell) — exact commands, do not run yourself:
  ```
  New-NetFirewallRule -DisplayName "OPUS Hub TCP 8787" -Direction Inbound -Protocol TCP -LocalPort 8787 -Action Allow
  New-NetFirewallRule -DisplayName "OPUS Hub UDP 8788" -Direction Inbound -Protocol UDP -LocalPort 8788 -Action Allow
  ```
- [x] (A) mDNS evaluated for `lib/core/hub/udp_beacon.dart` (brief M3: "evaluate nsd/bonsoir, justify the
  choice"): neither `nsd` nor `bonsoir` ships a Windows backend (both wrap Android NSD / Apple Bonjour
  only), so per the brief's own fallback clause this hub is **UDP-beacon-only, no mDNS**. If a future agent
  wants real mDNS on Android specifically (hub running on an Android tablet, not Windows), `nsd` would be
  the one to add conditionally for that platform — not attempted here since the beacon already covers it
  and adding a platform-conditional dependency untested on real Android hardware seemed riskier than useful.
- [ ] (A) `parseIpconfigBroadcasts()` in `lib/core/hub/udp_beacon.dart` computes each Windows network
  interface's real subnet broadcast address from `ipconfig /all` output — verified by manual review of the
  regex against typical `ipconfig` output, but **not exercised against this machine's real multi-adapter
  output** (the manual hub<->fake_headset test in the session log ran with `--no-beacon`, loopback only).
  On Android there is no zero-dependency way to read a netmask from Dart, so only the global 255.255.255.255
  broadcast fires there — a real Android headset relying on subnet-broadcast discovery (not just global
  broadcast, which most home/office routers do pass) may not discover the hub. Verify on a real Windows
  machine with `ipconfig /all` piped to the hub's log, and decide whether Android needs a platform channel
  or `network_info_plus`-style plugin for its netmask if subnet-only discovery turns out to be needed.
- [x] (A) M3/M4 hub core is real and tested: `dart run tool/hub_cli.dart --auto-drive` (same hub code the
  Flutter app will run) was driven end-to-end against `sim/live/fake_headset.py --no-scenario` — this
  satisfies Track S's own MANUAL_TODO item above ("Once the Flutter hub is running for real on port 8787,
  run fake_headset against it") for the CLI form of the hub; a full Flutter Windows-build UI test with a
  real Devices screen is still outstanding since no UI/providers wrap the hub yet (see session log
  checkpoint 3, "NOT done yet").

- [x] (U, 2026-09-17 run6, RESOLVED run8) **Demo-mode fruit never actually reaches the basket — needs a design decision, not a
  quick fix.** RESOLVED in run8 via option (b) below: `OrchardReachSceneController.Update()` now glides the held
  fruit (`Vector3.Lerp`) from its grasp point to just above the basket over ~0.9s once `Grasped`/`Holding` begins,
  instead of following the raw fixture path. Confirmed: `04_placed_in_basket.png` now exists
  (`logs/sessions/screens/unity/run8_playmode/04_placed_in_basket.png`), `successCount=1` in the PlayMode audit
  (`run8_playmode2_results.xml`), the real `"placed"` `TrialEvent` fires. See
  `logs/sessions/2026-09-17-U-unity-run7.md` checkpoint 8 for full detail. Original write-up kept below for
  context. Found during R5's dynamic PlayMode audit: over 30s / 4 completed trials, `TrialState` reached
  `TargetShown`, `MovementOnset`, and `Grasped` every time (screenshots captured:
  `logs/sessions/screens/unity/run6_playmode/01_target_shown.png`, `02_mid_reach.png`, `03_grasp.png`), but the
  "placed" event (`TrialOutcome.Success`) never fired once (`successCount` stayed 0 across all 4 trials — the
  earlier apparent "placed" in a first draft of the test was a mislabeled `Timeout`, since `TrialsCompleted` also
  increments on timeout/miss, not just success; the test now hooks the real `"placed"` event type instead). Root
  cause (two independent gaps compounding):
  1. `OrchardReachSceneController.Awake()` calibrates via `chestReference.position` = the rig's `CenterEyeAnchor`,
     which sits at its edited default local position (effectively world origin) in batch/no-headset Play mode —
     there's no real OpenXR runtime driving it, so it never moves to a realistic ~1.6m eye height. Target
     positions are sampled as `chestReference + reach*(direction)` (see `TargetPlacement.Place`), so with a
     near-origin chest they land near the ground close to the camera — visibly confirmed in the screenshots (the
     apple sits near y≈0, nowhere near the table at y=0.6).
  2. Separately and more fundamentally: while `Grasped`/`Holding`, the controller moves the fruit to
     `demoHandProxyR.position`, which is the **raw** `SyntheticHandDriver` fixture joint position
     (`contracts/fixtures/sessions/healthy/kin_*.json`) — i.e. that recording's own native coordinate frame from
     whatever session/calibration produced it, with **no transform back into this scene's chest/basket frame at
     all**. Even with (1) fixed, the held fruit's trajectory is decoupled from the dressed scene's basket
     position, so the demo can show a *plausible* reach/grasp motion but there is no principled way for it to
     path through the basket without an explicit remap.
  **What's needed:** a real design decision on how demo/no-headset audits should visualize "carry to basket" —
  options include (a) transforming fixture joint positions relative to the fixture's own recorded chest reference
  (stored in that session's `session.json`/`truth.json`) and re-projecting into this scene's calibration frame so
  relative motion is preserved but the endpoint lines up with the dressed basket, or (b) for demo/audit purposes
  only, blending the driven position toward the basket target once `Holding` begins (a "scripted" pickup-carry
  that isn't meant to be kinematically faithful, just visually complete for stakeholders). This wasn't attempted
  this run — it's a product/visual-fidelity decision, not just a bug fix. Until decided, the `04_placed_in_basket`
  screenshot in R5's PlayMode audit will keep coming up empty (test logs a warning, does not fail).

## Track S (sim/live tools)
- [x] (S, 2026-09-17 — **RESOLVED**: `fake_hub.py` now handles `ping` and replies `pong` echoing `echo_ts_ms` (see `_handle_ping`), and tracks RTT; `sim/live` pytest 26 passed on 2026-09-19.) **Was: `sim/live/fake_hub.py` never replies `pong` to the headset's `ping`,
  so RTT/latency can never be measured against it.** Found while wiring `LiveClient` (headset side) against
  `fake_hub.py --scenario basic` for U's R6 live-link PlayMode integration test
  (`Assets/Shell/Tests/PlayMode/LiveLinkIntegrationTests.cs`): the full scenario (hello → assign_program → start →
  pause → resume → stop → file uploads → hub-stored session passing `contracts/validate.py`) completed correctly
  end to end (0 invalid messages logged by the hub), but `LiveClient.LastRttMs` never updated across the whole
  ~13s run because `fake_hub.py`'s `handle_message()` switch only handles `hello`/`status`/`trial_event`/`ack` and
  falls through to `logger.warning(f"[MSG] Unhandled message type: {msg_type}")` for anything else, including
  `ping` — there is no `pong` reply path at all, even though `contracts/LIVE_PROTOCOL.md` §"Reliability rules"
  requires it ("Heartbeat: `ping` every 1s from both sides; `pong` echoes `echo_ts_ms`. RTT is measured
  continuously"). **Fix:** add a `ping` case to `handle_message` in `sim/live/fake_hub.py` that replies with a
  `pong` message echoing `payload.echo_ts_ms` (mirroring what `LiveClient.HandleIncoming`'s own `"ping"` case
  already does on the headset side, in `Packages/com.opus.sdk/Runtime/Transport/LiveClient.cs`). Not fixed by
  this Unity-track run since `sim/live/` is Track S's file, not Track U's, per `_COMMON.md` rule 1. Until fixed,
  R6's latency p50/p95 requirement cannot be measured against this hub (see
  `logs/sessions/2026-09-17-U-unity-run6.md` checkpoint 5 for the full test evidence).

- [ ] (S) Verify real cross-machine UDP beacon discovery: run `python -m fake_hub --port 8787 --beacon --scenario basic`
  on one machine and `python -m fake_headset --session contracts/fixtures/sessions/healthy --host auto` on a second
  machine on the same LAN/Wi-Fi. This session only proved discovery works on loopback (same machine, same process);
  a real Windows Defender Firewall "Public network" profile may block inbound UDP broadcast between two separate
  machines. If it fails, either add a firewall rule allowing UDP 8788 inbound (see the Track A firewall entry above
  for the exact command shape), or fall back to the manual `--host <ip>` / QR-code pairing path from LIVE_PROTOCOL.md.
- [ ] (S) Once the Flutter hub (Track A) is running for real on port 8787, run
  `sim/live/.venv/Scripts/python.exe -m fake_headset --session contracts/fixtures/sessions/healthy --host <flutter-hub-ip> --port 8787 --no-scenario --speed 1`
  and confirm it pairs, waits for a real `start` command, replays, and uploads files the Flutter hub actually stores.
  This session only validated `--no-scenario` against a hand-driven stand-in for a real hub (see
  logs/sessions/2026-09-14-S-live-tools-run2.md, checkpoint 4), not the real Flutter app.

## Electronics team (haptic sleeve) � answers needed
- [ ] (haptics) Send docs/ELECTRONICS_HANDOFF.md to the Electronics team and get �4 answered: motor 0/1 body location, minimum gap + max on-time, UDP 8790/8791 confirmation (or BLE UUIDs), battery_pct meaning, thermal/duty limits. **UDP first** is required for the prototype: Unity on Quest has no built-in BLE central API, so BLE needs an extra Android plugin we can't add before the demo.

## Track A (Flutter) -- run 7, 2026-09-18 (haptics UI)
- [ ] (A/Opus, run7) The `hapticsEnabled`/`hapticMaxIntensity` program params were added only to
  `app/assets/fixtures/manifests/orchard_reach.manifest.json` (this brief's scope was `app/` only).
  Opus should sync the same two `paramSchema` properties (see that file's `Feedback` group) into the
  canonical `contracts/fixtures/orchard_reach.manifest.json` and
  `game/Assets/Games/OrchardReach/manifest.json` so Track U's game and the contracts fixtures agree
  with the app's dynamic form.
- [ ] (A, run7, pre-existing, reproduced 3x this run, not caused by run6 or run7) `flutter test
  test/reach_trace/reach_trace_golden_test.dart` (`ReachTraceGlyph` at 48/160/280dp, light+dark) flakes
  with small (<1%) pixel diffs on roughly 2 of every 3 runs, a different subset of the 6 each time; once
  `test/hub/hub_connection_test.dart`'s ack-retry-timer test also flaked in the same session. This file
  has no `@Tags(['golden'])` (unlike `test/goldens/screen_goldens_test.dart`), so `flutter test
  --exclude-tags golden` does not skip it -- worth either tagging it consistently or root-causing the
  actual pixel nondeterminism (candidate causes not yet investigated: font hinting/subpixel rendering
  differing by process/test-order state, or a shared golden-comparison threshold that's too tight for
  this machine's rendering).
- [ ] (A, run7) No golden/widget-test screenshot exists for `DevicesScreen`'s new "Haptic sleeve" card
  or the live monitor's cue indicator actually firing (the `connection != null` path) -- both need a
  real `HubConnection` with a live `status`/`trialEventStream`, which
  `test/goldens/screen_goldens_test.dart`'s harness doesn't create (no screen there has a real hub
  connection, even pre-existing ones). Covered instead by `test/metrics/haptic_status_test.dart`'s unit
  tests of the parsing/copy logic these widgets render. A future run could add a small widget test
  that constructs a fake `HubConnection`/`LiveSocket` directly (the pattern
  `test/hub/fake_live_socket.dart` already uses) and pumps `DevicesScreen`/`LiveMonitorScreen` against
  it to get a real screenshot of the connected-sleeve and cue-firing states.
- [ ] (A, run7) `sim/haptic/fake_haptic.py` (`contracts/HAPTIC_PROTOCOL.md`'s "Testing without
  hardware" section) does not exist yet -- Track S's file per that doc, not built this run (Track A
  scope was `app/` only). Once it exists, the Devices screen's `_HapticSleeveTile` and the live
  monitor's cue indicator should be re-verified against its real UDP status/cue traffic instead of
  only the hand-written `healthy__events.ndjson` fixture and unit tests this run used.


## Track A (Flutter) -- run 8, 2026-09-18 (patient profile rejection fix)
- [ ] (A/Opus, run8) `contracts/fixtures/sessions/longitudinal/week_0{1..6}/session_0/session.json`
  each have a different, incrementing `patient_ref` (`synthetic-longitudinal-9100` .. `-9600`) instead
  of the single patient id (`synthetic-longitudinal-9000`, "Priya Nair") that week 0's session and
  `MockOutcomesRepository`'s seeded Fugl-Meyer entries use -- a real generator bug, not an app/-only
  issue (confirmed identical values in the canonical `contracts/fixtures/...` copies). Effect: any
  code calling `listSessionsForPatient('synthetic-longitudinal-9000')` only ever gets 1 of the 7 weeks
  back, so the one fixture built for demonstrating a real multi-session longitudinal trend couldn't
  actually do so. Fixed only the `app/`-local copies this run (`app/assets/fixtures/sessions/
  longitudinal__week_0{1..6}__session_0__session.json`, scope = `app/`); Opus should fix the canonical
  `contracts/` fixtures and whatever generates/syncs the app/-local copies so it doesn't reintroduce
  the bug.
- [ ] (A, run8) `test/goldens/screen_goldens_test.dart` and `test/reach_trace/reach_trace_golden_test.dart`
  both had header comments since run5/6/7 claiming they were tagged `@Tags(['golden'])` for
  `--exclude-tags golden` to skip -- neither file actually had the annotation anywhere. Fixed this run
  (real tags + `app/dart_test.yaml` declaring the tag), but worth a sweep for other doc/session-log
  claims in this repo about tag-based test filtering that might have the same doc-vs-code drift.
- [ ] (A, run8) `patient_profile`'s "Sessions" reach-trace glyph strip and "Outcome measures" rows are
  not visually covered by a golden **for the `synthetic-longitudinal-9000` patient now used by that
  golden** -- the real multi-point charts (fixed this run) push those two sections below the fold at
  every captured viewport size (phone/tablet/desktop, 1.0x/2.0x). They do render (confirmed via
  intermediate screenshots with the previous, shorter-content patient, and via unit-level reasoning
  about the same provider/widget code), just not pixel-verified for this specific patient/viewport
  combination. A future run could scroll the `ListView` before an additional golden capture, or add a
  plain (non-golden) widget test that scrolls to the bottom and asserts on `ReachTraceGlyph`/outcome
  text presence.
- [ ] (A, run8, informational) `test/hub/hub_connection_test.dart`'s ack-retry-timer test flaked once
  more this run (1 of 6 executions across 3 combined `test/reach_trace/ test/hub/` runs) -- the same
  pre-existing flake `docs/MANUAL_TODO.md`'s run7 entry already tracks, reproduced again as supporting
  evidence that it's unrelated to this run's reach-trace changes (0 reach-trace failures across 18
  total executions this run), not a new issue to chase separately.

## Track S (demo pipeline tooling) -- run 1, 2026-09-19
- [ ] (S/human, tonight before the demo) **Actually run `tools/demo/start_pc_demo.ps1` once at the
  machine and confirm three real windows come up cleanly.** It was never executed this run on
  purpose: it binds 8787/8788 (and, with `-WithHaptics`, 8790/8791), which this run's own brief
  forbade while Opus's live Unity session is using those same ports. Both `.ps1` files were checked
  to parse as valid PowerShell (`[scriptblock]::Create` on each, no execution) and
  `tools/demo/open_firewall.ps1` (which only prints, never binds anything) was run for real and
  produces the expected `New-NetFirewallRule` block. Before the real demo: run
  `tools\demo\start_pc_demo.cmd` (or `-AutoDrive`/`-WithHaptics` as needed), confirm the hub window
  logs `HubServer listening on 0.0.0.0:8787`, then start a headset (Unity PlayMode
  `FullPipelineIntegrationTests`, a real Quest, or `sim/live/fake_headset.py --no-scenario` as
  fallback) and confirm the watcher window prints a plain-English summary once the session ends.
  Full detail: `logs/sessions/2026-09-19-S-pipeline-run1.md` checkpoint 2.
- [ ] (S, run1) `tools/demo/watch_and_analyse.py`'s default watch dir is `app/.hub_data` -- if the
  real demo's hub is started with a different `--data-dir`, pass the matching `--dir` to the
  watcher (`start_pc_demo.ps1` does not currently expose a `--data-dir` passthrough for the hub,
  since the runbook's own examples never use one; add one if the demo ends up needing it).

## Electronics team — answers owed (updated 2026-09-19, see `docs/ELECTRONICS_HANDOFF.md` §11)

Restructured handoff now separates settled / open / blocked. Four answers are owed by the Electronics team,
two of which are live contradictions found by comparing their draft against the firmware in this repo:

1. **Is motor 1 (forearm) actually fitted?** Their build pipeline marks it done; `firmware/opus_sleeve/opus_sleeve.ino`
   v0.3.0 says `MOTOR_COUNT 1` and `ROUTE_MOTOR1_TO_MOTOR0 1`, i.e. every forearm cue is currently felt on the
   upper arm. If it is fitted, they must ship the firmware update (`MOTOR_COUNT 2`, `ROUTE_MOTOR1_TO_MOTOR0 0`).
   Until then, do not claim two-zone haptics in the demo.
2. **`pattern` enum.** Their draft lists `pulse | continuous | ramp`, which drops `buzz` (the success cue's
   waveform, implemented in their own firmware and in both schemas) and adds `continuous` (which their firmware
   maps to `pulse` but which fails `contracts/validate.py`). Confirm `buzz` stays; decide whether `continuous`
   is dropped from the spec or proposed to Opus as a schema addition.
3. **Motor 2 / motor 3 physical placement** — undecided, and blocking the 4-motor scale-up.
4. **Bench-measured** minimum inter-command gap, max continuous on-time, and a real duty-cycle/thermal limit
   (the firmware's 100 ms / 400 ms / 50 % are assumptions, not measurements).

### Opus decision recorded, no action needed from the team
The proposed `motor: 0-3` schema extension is **approved in direction, deferred in implementation** until after
the prototype demo. Firmware may implement 0-3 defensively so bench work continues; software will not send
`motor` > 1 and the schema stays `enum: [0,1]` until Opus lands the change set as one commit.

## ⚠️ Unity MCP: run17 (2026-09-19, ~2 hours after the 15:30 restart) — client reconnect needed, NOT the orphan-port issue below

**This is a different failure from the 15:30 entry directly below, which is now RESOLVED** (confirmed this run):
```
Get-Process Unity                                  -> PID 40924 "game - OrchardReach - Android - Unity 6.4 ..."
                                                       StartTime 17:11:49 (the restart happened), Responding=True
Get-NetTCPConnection -State Listen -LocalPort 48735,48736
                                                    -> both ports, OwningProcess 40924 -- the LIVE editor, no orphan
```
So the editor correctly rebound the bridge ports after Opus's restart — no dead PID is holding them this time.
**But `ToolSearch("select:mcp__meta-xr-unity-runtime__...")` still returns "no matching deferred tools found /
server failed to connect (CONNECT_TIMEOUT after 30000ms)"** on every retry across roughly 10 minutes of this
run (~15 retries, spaced with real file-level work in between, per this run's own brief). This is the same
failure class run16 hit under its CHECKPOINT 2: the editor and its HTTP bridge server are alive and correctly
bound, but **this agent session's own MCP client connection never (re-)establishes itself** — that is a
client-side reconnect only the user can trigger.

**Action needed:** in Claude Code, run `/mcp` and reconnect `meta-xr-unity-runtime`. No editor restart, no
taskkill, no port-clearing should be needed this time — just the client reconnect. Once reconnected, the next
step (already prepared, not yet run) is `CompilationTools(GetCompilationStatus)` to confirm a clean compile of
this run's `OpusHud.cs`/`OpusSessionRunner.cs` edits (see `logs/sessions/2026-09-19-U-unity-run17.md`), then
`TestRunnerTools(RunAll, EditMode)` for the full suite.

## ⚠️ Unity MCP bridge will not start — orphaned sockets on 48735/48736 (2026-09-19 15:30) — RESOLVED, kept for history

**Diagnosis (Opus):** ports 48735 and 48736 are held by **LISTEN sockets owned by dead PID 19948**, the previous
Unity editor. `Get-NetTCPConnection -LocalPort 48735,48736 -State Listen` resolves the owning process to nothing.
The current editor (PID 31732, `game - OrchardReach - Android - Unity 6.4`) is alive and responding but cannot
bind the bridge ports while those orphans hold them. Reconnecting from `/mcp` cannot fix this — nothing is
listening for the client to reach.

**Fix, in order. Run these in an elevated terminal (cmd.exe or PowerShell):**

1. See who currently owns the ports:
```
powershell -Command "Get-NetTCPConnection -LocalPort 48735,48736 -State Listen | ForEach-Object { $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue; [PSCustomObject]@{Port=$_.LocalPort; PID=$_.OwningProcess; Name=$(if($p){$p.ProcessName}else{'<DEAD>'}) } }"
```

2. The orphan handle is most likely inherited by the Claude CLI MCP helper (`unity.exe mcp`, a windowless
   `unity` process — PID 28768 at the time of writing). It is restartable and safe to end. **Do not confuse it
   with the editor** — the editor is the `Unity` process that has a window title.
```
taskkill /PID 28768 /F
```

3. Re-check the ports are free (the command in step 1 should return nothing).

4. **Close and reopen the Unity editor** so it rebinds the bridge ports on startup.

5. In Claude Code, run `/mcp` and reconnect `meta-xr-unity-runtime`.

6. Confirm it works — a healthy bridge answers:
```
CompilationTools(method: "GetCompilationStatus")  ->  {"success":true,"status":"clean","errorCount":0}
```

If the ports are still held after step 3, a reboot clears orphaned sockets unconditionally.

## Phantom Hand — human steps (new machine, 2026-10-08)

- [x] (human, phone) **Tap "Allow USB debugging" on the phone.** Done 2026-10-08: phone 164cd676 is authorised.
- [ ] (human, network) **Windows Firewall for the Unity editor.** The Wi-Fi network is Public and Unity.exe has an inbound **BLOCK rule on Public networks** ("Unity 6000.4.6f1 Editor"), so node/hub UDP beacons on 8791, 8788 and the nodes' replies cannot reach the editor. In an **elevated** PowerShell: `tools\demo\open_firewall.ps1` (report only, safe) → `tools\demo\open_firewall.ps1 -RemoveUnityBlock -Apply -WhatIf` (rehearsal) → the same without `-WhatIf`. On the PC that runs the twin or the nodes' side: `-Apply -Twin`. Agents never run it with a change switch. Source: manager, 2026-10-08.
- [ ] (human, wearer + electronics, 20 min) **Bench-tune the stroke on the real sleeve.** Default is now a slow brush (12 cm/s: `motor_soa_ms` 833, spec D16) with one tap as the brush passes each motor. Run the protocol in `docs/agent-briefs/ph/research/R2-two-motor-stroke.md` section E (E0–E6) and send back its one hand-back table: per-motor intensity floor and trim, pulse length, `tactile_lead_ms`, whether the fast "flick" (`motor_soa_ms` 100–130) reads as one moving touch, wearer remarks. If two taps read as two pokes, say so: the next step is a shorter visible brush path. Source: research R2, 2026-10-08.
- [ ] (electronics team, answers) **Four firmware questions (R2 FW0):** (a) is the 100 ms cue gap start-to-start or end-to-start; (b) does a cue to a still-running motor retrigger or return `accepted:false`; (c) what do `pulse` and `buzz` do in v0.5.0; (d) coin-motor model and its start voltage. Also: does Node A/B telemetry go to the sender's source port or to 8790 (the twin assumes the source port). Source: research R2 + sim run, 2026-10-08.
- [ ] (human, 1 h, tonight) **Record a golden run.** Once one full run works on a teammate (with consent): record 60 s of the headset cast + the live trace + a camera on the arm, label it "recorded at <time>". It is the last rung of the fallback ladder (research R4, C1): played when the network or the sleeve is down. Never present it as live.
- [ ] (human, 30 min, optional) **Classic rubber-hand kit for the queue.** A stuffed glove, a card divider and two brushes with a 5-line instruction card: waiting visitors try the 1998 version while one person is in the rig (research R4, row 7).
- [ ] (human, team decision) **G3 pilot wording.** PRD §12 asks "SYNC > ASYNC for drift and flinch in ≥ 4 of 5". Research R4 shows the drift part would fail about half the time even if the effect is real; proposed: "rating higher in SYNC in ≥ 4 of 5 and a flinch visible on the trace". Note added in `04-E2E.md`; the threshold itself is unchanged until you decide.
- [ ] (human, venue) **Network at the venue by 08:30.** Dedicated router or the laptop hotspot on a fixed 2.4 GHz channel (ESP32 is 2.4 GHz only), node IPs noted, firewall step done on every PC, one cue test from the venue network.
- [ ] (optional, human) **Windows Developer Mode for Flutter desktop builds.** `flutter pub get` / `flutter build windows` need symlinks; the agents used a build-tree junction workaround that breaks whenever the plugin list changes. Settings → System → For developers → Developer Mode ON removes the problem. Not needed for the phone APK. Source: app setup run, 2026-10-08.
- [ ] (optional, human) **Add H:\flutter\bin to PATH.** Flutter is being installed at `H:\flutter` (not `C:\flutter`) with your approval. Once installed, add `H:\flutter\bin` to your system PATH so commands like `dart` and `flutter` are available in the terminal. Source: manager, 2026-10-08.
- [ ] (optional, human, Unity) **"Activate XR Operator" in the Unity AI Tools window.** Only needed for XR Operator runtime tests on a headset. Source: manager, 2026-10-08.
- [x] (human) **Rigged hand credit line.** Done 2026-10-08: the user sent the Sketchfab page; "Rigged hand" by Elena FF, CC BY-SA 4.0, recorded in `CREDITS.md` (README links to it).
- [ ] (human, pitch) **Show the hand's credit.** CC BY-SA 4.0 requires credit wherever the model is shown publicly: put the one-line credit from `CREDITS.md` on a slide or a card on the demo table. Modified versions of the model stay under the same licence.
- [ ] (human, before a public release build) **Licence terms of the Meta asset-library models** in `game/Assets/MetaAssets/` (table, stone, brush, sleeve, glove): check Meta's terms for redistribution in a public repo and add them to `CREDITS.md`.
- [ ] (human, owner of the team phone) **Decide whether to uninstall the old operator app on the team phone.** The repo's debug APK is rejected as an update of the old `com.opus.opus_app` (it carries another PC's debug key, `INSTALL_FAILED_UPDATE_INCOMPATIBLE`), so a side-by-side build `com.opus.opus_app.pc` is installed beside it. Uninstalling the old app wipes its data; the other fix is to reuse the old PC's debug key. Until one of them is done, a build made from the repo reaches the phone only as a side-by-side copy. The `.pc` build predates the card rework in `aec2aed`. Details: `logs/sessions/2026-10-08-PH-A-SETUP-run1.md` (checkpoint 5). Source: PH-A-SETUP-run1.
- [ ] (owner, one line) **`CLAUDE.md` still says Flutter is at `C:\flutter\bin`** (line 40, the Flutter section). On the current dev PC it is `H:\flutter\bin`, not on PATH, called by full path. `docs/agent-briefs/ph/02-RULES.md` already says so (a667def). Source: `docs/PH_ON_DEVICE_RUNBOOK.md` 2.4.
- [ ] (electronics team, answers) **One supply per board, and where the sensor data goes.** Confirm in writing: (1) each board has its own supply and their grounds are not joined (so no shared bank for the two nodes); (2) a node sends its sensor data to the last sender only, back on UDP 8790, so the Quest must be the last to send and no laptop tool may talk to the nodes during a session. Their handoff §A says both and the contracts now assume it (`contracts/HAPTIC_PROTOCOL.md` v1.3, `03-SPEC.md` D3 and the power line). Source: `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` §A, §B rows 4 and 13.
- [ ] (human, Unity, optional) **Import the four Unity Asset Store packs named in `CREDITS.md`, only if they are wanted.** Pack Gesta Furniture #1, Stones, Dark Wave Paint Table 01, Mobile Books: in Unity's Package Manager (My Assets) download and import each. Keep them out of git: `CREDITS.md` says to use a git-ignored folder on the build PC, because the Asset Store licence lets the team ship a package in a built game but not publish its files. None is imported yet: still true at 14:45 on 8 Oct (they are not downloaded on this PC). After importing them, tell the session so they can be wired in and git-ignored. Source: `CREDITS.md`; wave 6 notes in `logs/sessions/2026-10-08-PH-O-RESUME-run1.md`.
- [ ] (human, team) **Confirm that the seven `PH_*.glb` props are the team's own models.** `game/Assets/Art/PhantomHand/Models/`: `PH_Brush`, `PH_PendantLamp`, `PH_Plant`, `PH_Window`, `PH_SingingBowl`, `PH_TeaCup`, `PH_FramedPicture`. The files carry no author or licence; their generator tag is `THREE.GLTFExporter r184` and their materials use the `PH_` prefix, so `CREDITS.md` takes them to be the team's own procedural models. Confirm before a public release build; if any is a download, add its credit to `CREDITS.md`. They are wired into the scene since ce05e51 (8 Oct, 14:35). Source: `CREDITS.md`, commits 9a011a3 and ce05e51.

## Phantom Hand — firmware

### Node A firmware v0.5.0 is UNCOMPILED — compile it before flashing (F1, 2026-10-07)
No arduino-cli with the esp32 core exists on the authoring machine, so `firmware/opus_sleeve/opus_sleeve.ino` was only statically reviewed.
- [ ] (human/F) Install arduino-cli (or use Arduino IDE 2), then:
  `arduino-cli core update-index` · `arduino-cli core install esp32:esp32` ·
  `arduino-cli lib install "ArduinoJson" "Adafruit MPU6050" "Adafruit Unified Sensor" "Adafruit SSD1306" "Adafruit GFX Library"` ·
  `arduino-cli compile --fqbn esp32:esp32:esp32 firmware/opus_sleeve`
  Need ArduinoJson major version 7 (check `arduino-cli lib list`). Expect 0 warnings in opus_sleeve.ino; paste the output into the F1 log
  `logs/sessions/2026-10-07-PH-F-F1-run1.md` and report any error to Opus.
- [ ] (human) Bench with Serial 115200: `scan` shows 0x3C and 0x68; `selftest`; `soa 100`; confirm the OLED shows IDLE/LINK lines.

## Phantom Hand — human steps

Source of each item: `docs/PH_STATUS.md` (written 2026-10-08) and the CONTEXT.md Phantom Hand box.

- [ ] (human) **Reconnect the Unity MCP after any Unity editor restart.** In Claude Code run `/mcp`, reconnect
  `meta-xr-unity-runtime`, then run `CompilationTools(method: "GetCompilationStatus")`. A healthy bridge answers
  `{"success":true,"status":"clean","errorCount":0}`. Source: U2 and U3 logs (`logs/sessions/2026-10-07-PH-U-U2-run1.md`,
  `logs/sessions/2026-10-08-PH-U-U3-run1.md`: MCP ECONNREFUSED during domain reload).
- [ ] (human) **Hindi review of the new strings.** A native speaker reads (a) the Hindi `x-ui.title` values on the 27
  Phantom Hand parameters in `game/Assets/Games/PhantomHand/manifest.json` and its two byte-identical copies
  (`app/assets/fixtures/manifests/phantom_hand.manifest.json`, `contracts/fixtures/phantom_hand.manifest.json`), and
  (b) the Phantom Hand strings in `app/lib/l10n/app_hi.arb`, and (c) the Hindi strings in `app/lib/features/sessions/embodiment_report.dart` (the newest: "Not asked in this round", 2629398). Report wrong words to Opus; the builder changes them.
  Source: `logs/sessions/2026-10-08-PH-A-A1c-run1.md` (titles machine-written); `logs/sessions/2026-10-08-PH-A-A1-run2.md`
  (Hindi strings machine-quality).
- [ ] (human, electronics team) **Send your files and fill the measurement table.** Send the firmware files you
  actually flash for Node A and Node B, and fill the table in `docs/PH_ELECTRONICS_INTERFACE.md` §5 (`motor_soa_ms`,
  motor spin-up ms, `tactile_lead_ms`, ack p95 < 100 ms, EMG artefact with motors on, 30-minute power-bank result,
  Node A and Node B IP addresses from the serial console). Firmware is owned by the electronics team (user decision
  2026-10-07); the repo `firmware/` code is a reference only. Source: `docs/PH_ELECTRONICS_INTERFACE.md` §5, §7, §8;
  `logs/sessions/2026-10-07-PH-F-F1-run1.md`.
- [ ] (human) **The hand model has no rig, so fingers cannot pose.** `game/Assets/MetaAssets/Prefabs/324213/` (hand,
  no rig). This is fine for the MVP but blocks addition A5 (hand-closing). If A5 is wanted, source a **rigged** hand
  model (skeleton with finger bones), drop it in the same folder layout, and tell Opus. Source: manager brief
  2026-10-08; `logs/sessions/2026-10-08-PH-U-U3-run1.md` (procedural hand; A5 via `arm.Curl`).
  **Update 2026-10-08:** a rigged skin hand is now in the repo (`game/Assets/Art/PhantomHand/Models/RiggedHand/handRig_02.fbx`,
  right hand `hand.R`, 68 bones; commit `5c0b12d`). It is the hand the arm wiring will switch to. Its licence is still open (next item).
- [x] (human, 2026-10-08) **Restart this Claude Code session to reconnect the Unity MCP.** CLOSED (manager fix is the user-scope registration via `python tools/unity_mcp.py register`; session restart no longer needed). Source: manager report 2026-10-08; `logs/sessions/2026-10-08-PH-O-RESUME-run1.md` "Unity MCP root cause"; `docs/PH_STATUS.md` Next 1.
- [ ] (human) **Check the rigged hand's licence and source, then send Opus the credit line.** Files:
  `game/Assets/Art/PhantomHand/Models/RiggedHand/` (`handRig_02.fbx`, `hand_Co/No/Ro/Sp` textures). Licence and source are
  unknown. Check the download page or the author's terms, write the credit line (or "none required") and send it to Opus.
  The FBX also carries a camera and a light: drop them on import. Blocks: shipping the rigged hand and the credit text.
  Source: manager report 2026-10-08; `logs/sessions/2026-10-08-PH-U-MODELS-run1.md` (model licence not recorded there).
- [ ] (human, electronics team) **Forward the 5 requests in `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` §B to the electronics team.**
  (1) Confirm `sensor_chunk.timestamp_ms` is the device time of the first value in `emg_envelope`.
  (2) Confirm Node A ignores unknown extra fields (`v`, `id`, `ts_ms` on `stop`; `text` next to `mode` on `display`).
  (3) Say whether the nodes send any `status` message or `emg_burst`; if yes, paste one example of each.
  (4) Optional: stream to up to 3 recent senders (03-SPEC D3). Not required; the laptop plot reads through the hub.
  (5) After the Wi-Fi test: run `python tools/demo/node_probe.py <ip> 8790` against each node, send the output, and fill the §5
  table of `docs/PH_ELECTRONICS_INTERFACE.md`. Blocks: the software side of the §B "who changes" items that wait on them.
  Source: `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` §B.
- [ ] (human, electronics team) **Wi-Fi credentials and the network test on their side.** Their own table (handoff §A) says
  Wi-Fi/UDP between the nodes and another device is **not tested yet**, the Wi-Fi credentials are **placeholders** in both
  sketches, and "everything powered from the power bank together" is not tested. Credentials are typed by a human at flash
  time, never put in a file or a log (02-RULES §1.5). Blocks: any end-to-end Quest-to-node run.
  Source: `docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md` §A.
- [ ] (human) **Delete `rigged-hand (1).zip` from the repo root, but only after confirming the copy.** Confirm that
  `game/Assets/Art/PhantomHand/Models/RiggedHand/` holds `handRig_02.fbx` and the four `hand_*` textures (open the folder in
  Unity or Explorer), then delete `rigged-hand (1).zip` by hand. Source: manager report 2026-10-08 (the zip is in the repo root).
- [ ] (owner of this PC, decision) **This PC throttles its CPU to about a third of its speed while the Unity editor is in play mode.**
  Dell Precision 5560 (i9-11950H, RTX A2000), on AC, "High power plan", maximum processor state 100 %. Windows' counter
  `% Processor Performance`, sampled once a second during a full run, showed about 35 % of nominal speed for long stretches: in the
  measured run 150 of 191 s were below 50 % speed, and 73 % of those seconds had a frame over 50 ms. Throttled runs of `PH_FullRun`
  delivered 68-91 % of the stroke cues; a run without the throttle delivered 98-100 % (112/114, 114/114). Why the CPU throttles (heat or
  a power limit) is not determined. Decide about cooling or the Dell thermal mode. No setting was changed. Until then a throttled
  editor run ends Inconclusive, by design. Source: commit 3fefd61; wave 6 notes in `logs/sessions/2026-10-08-PH-O-RESUME-run1.md`.
- [ ] (human with a Quest 3) **Install the development APK on a Quest and run it.** Two APKs exist in
  `releases/game/0.1.0` (both git-ignored, package `com.DefaultCompany.OPUS`). The release APK `chetna-phantom-hand.apk`
  (85 951 560 bytes, 82.0 MB) was built on 8 Oct, 14:45 to 15:04, from commit 2629398, so it has the room props. It was checked by
  content only. It is the APK to install for the demo. The development APK `chetna-phantom-hand-dev.apk` (131 430 360 bytes, 125.3 MB)
  was rebuilt 15:09 to 15:22. It was not checked by content. While it was building, a second session saved uncommitted edits to
  `PhMaterials.cs`, `VirtualArmRig.cs` and `ThreatDrop.cs` (15:11:05), so it may or may not contain them. It is not known to match a
  commit and is to be rebuilt once those edits are committed. The first development APK (13:43, 95.7 MB, old room) was set aside as
  `chetna-phantom-hand-dev.apk.prev`. No APK was installed, and nothing has run on a headset (no Quest is attached to this
  PC). Follow `docs/PH_ON_DEVICE_RUNBOOK.md` 4.7 to 4.10: (1) connect the Quest to this PC by USB and accept the prompt inside the headset
  until `adb devices` lists it as `device` (the repo has no Quest developer-mode steps); (2) `adb install -r` the APK; (3) start the hub
  (runbook 4.2) and the nodes or the twin, then start the app; (4) if it finds neither hub nor nodes, write `phantom_endpoints.json`
  (runbook 4.10); (5) send back what happened: what the headset showed, and the `adb logcat -d -s Unity` lines that contain "PhantomHand".
  Do not share a development APK: it carries the editor bridge's address and token. Whether the release APK carries them was not
  checked. Source: `docs/PH_ON_DEVICE_RUNBOOK.md` 4.7; `docs/PH_STATUS.md`
  APK row.
