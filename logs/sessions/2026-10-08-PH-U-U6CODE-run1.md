# PH U6 CODE run 1: Quest APK build method, demo mode, performance + two Quest-only networking gaps (ph-builder, worktree, NO Unity)

Worktree `H:\Chenta\phantomhand\.claude\worktrees\agent-aef59972804fa8e91`, branch `worktree-agent-aef59972804fa8e91`, base `018682b`. Uncommitted diff only (no commit/tag/push/stash).
Constraints from the coordinator: Unity is NOT launched, no Unity MCP, main checkout untouched, file-ownership list obeyed. Written 2026-10-08, resumed 10:08 IST after a PC restart that cut the first attempt (nothing from attempt 1 was on disk).

## Status board (updated at every checkpoint)
| # | Deliverable | State |
|---|---|---|
| D1 | `OpusBuildScript.BuildAndroidApkPhantomHand` (+ pure helpers + tests) | CODE DONE + compiled against real UnityEditor DLLs (3 profiles) + 28 pure tests pass in the harness (checkpoint 3); the real Unity build = MANUAL (never run) |
| D2 | Demo mode + performance per U6 | DONE for what is code (checkpoint 4): audit + 6 new tests pass in the harness and caught 2 mutants; reset timing, draw calls, SetPass, FFR decision = MANUAL |
| D3 | Runtime endpoint override file `phantom_endpoints.json` (+ tests) | CODE DONE + compiled + 41 pure tests pass in the harness (checkpoint 1); 2 engine tests written, not run |
| D4 | Android multicast lock helper (+ tests) | CODE DONE + compiled both branches + 9 pure tests pass in the harness (checkpoint 2); on-device behaviour = MANUAL |
| V  | Standalone compile / run of the pure logic with `dotnet` | DONE: compiled in 3 profiles against the real Unity DLLs; 84 new pure test cases pass in a reflection runner; Unity itself never launched, so NO Unity test totals exist |

## Plan (step -> verify)
1. D3 `PhantomEndpoints.cs`: pure `TryParseFile(text)` + a third `Resolve(env, settings, readFile, warn)` overload; the 2-arg `Resolve` (used by the controller and BootstrapLoader) delegates to it with the on-device file reader, so NO caller edit is needed. Precedence env > file > asset > discovery. -> verify: EditMode tests (written) + standalone harness run of the pure tests.
2. D4 `AndroidMulticastLock.Acquire()` + a pure holder class (idempotent, never throws, warns once, releases on `Application.quitting`). -> verify: compile with and without `UNITY_ANDROID`; holder tests.
3. D1 build method + `PhantomBuildPlan` (pure) + tests in a new nested test asmdef (the existing `Shell.Tests.Editor` cannot see `Shell.Editor`). -> verify: compile against the real UnityEditor DLLs; pure tests.
4. D2 audit of what U2/U5 already did, new EditMode test file for the demo-run timeline and "twice in a row" at module level. -> verify: harness run.
5. Standalone harness in the scratchpad (not tracked): compile everything against `C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Data\Managed`, run the pure NUnit tests with a tiny reflection runner (no `dotnet test`: no NuGet cache here).
6. Update this log after EVERY deliverable; final report <= 40 lines.

Constraint: a compile error in any file I add breaks the Unity driver's editor session after merge -> every file is compiled standalone before it is called done. Failure modes: Unity API signature wrong (caught by the real-DLL compile), test that needs the native engine (kept out of the pure set and listed as "not run"), coordinator's Bootstrap scene present or absent (both handled in the scene resolver). Ledger: everything built directly (no subagents; small, tightly coupled, nothing to delegate).

## Facts verified this session (read-only, from the worktree)
- **Demo mode already exists** (U2/U5): `PhantomHandParams.cs:44-47` (induction 45 s), `PhaseStateMachine.cs:125-126` (one questionnaire, at the end), `PhantomHandModule.cs:325` (single answer set describes the last condition only), tests `PhaseAndParamsTests.DemoMode_ForcesInduction45`, `DemoMode_OneQuestionnaire_AtTheEnd`, `PhantomHandShellTests.ScriptedParticipantTests.AutoParticipant_PlaysDemoRun_ThroughAllPhasesToDone`, PlayMode `PH_FullRun_DemoMode_AgainstFakeHubAndTwin` (written, needs venvs + port 8787; never green on this machine yet).
- **Reset between people already exists** (U5): both-hands pinch 1.5 s in `Finished`/`WaitingToStart` (`OpusSessionRunner.cs:244`, `bothHandsPinchHoldSec` :55) -> `StartSession` -> `NeedsFreshScene` -> `SwitchToNextPerson` (:297-306, :407-415) -> `PhantomHandSceneController.PrepareNextSession` (:495-513, scene reload, auto-start at Calibrate once the head has tracked 2.5 s, :120-123); operator `next_person` command `OpusSessionRunner.cs:610`; app side Next person button exists (`app/test/phantom/phantom_live_screen_test.dart`). Never timed on a device.
- 72 Hz: `RigRuntimeSettings.cs:16,53-65` sets `OVRManager.display.displayFrequency` by reflection, scene value `PhantomHand.unity:6596 displayFrequencyHz: 72`, asserted by `PhantomHandRigValidatorTest.RigRuntimeSettings_At72Hz`.
- Shadows: the single key light has `m_Shadows.m_Type: 0` in the committed `PhantomHand.unity` (asserted by `Lighting_WarmKey_Ambient_LightFog`), every presenter renderer has shadows off, the stone uses a `BlobShadow` quad (`ThreatDrop.cs:88`). URP asset `Assets/Settings/OpusQuestURP.asset`: MSAA 4, HDR off, SRP batcher on, but `m_MainLightShadowsSupported: 1` (only strips variants if turned off: human/Opus decision, not touched).
- ProjectSettings already: Vulkan only for Android (`m_APIs: 15000000`, `m_Automatic: 0`), scripting backend Android IL2CPP (`scriptingBackend: Android: 1`), `AndroidTargetArchitectures: 2` (ARM64), min SDK 32 / target 34, `bundleVersion: 1.0`, `applicationIdentifier: {}` (so the Android id is the Unity default `com.DefaultCompany.OPUS` (inference, company DefaultCompany / product OPUS), the build prints the real one), texture compression unset (`m_BuildTargetDefaultTextureCompressionFormat: []`).
- FFR is set nowhere (no `oveated` hit in scenes/scripts; `MetaXRFoveationFeature Android` has `m_enabled: 1` in `Assets/XR/Settings/OpenXR Package Settings.asset:512-514`, so the feature is available). 02-RULES says FFR high -> reported, not changed (see open issues).
- `OVRProjectConfig` `targetDeviceTypes: [2,3,4,5]` = Quest2, QuestPro, Quest3, Quest3S (enum in `OVRProjectConfig.cs:50-58`), and `Assets/Plugins/Android/AndroidManifest.xml` lists `quest|quest2|questpro|quest3|quest3s`; 02-RULES section 3 says Quest 3 only. Reported, not changed (not my files).
- The existing `BuildAndroidApk` resolves `Application.dataPath + "../releases/game/0.2.0/..."` = `game/releases/game/0.2.0/` (there is a leftover `game/releases/game/0.2.0/OPUS_BurstDebugInformation_DoNotShip`), NOT the repo-root `releases/` that `releases/VERSIONS.md` and U6 name. The new method writes to the repo-root `releases/game/<ver>/`.
- `BootstrapLoader.SceneFor` maps `orchard_reach -> OrchardReach`; an APK without the Orchard scene cannot open it (see INTEGRATION LINES).
- `Shell.Tests.Editor.asmdef` references Shell.Runtime/PhantomHand.Runtime/Opus.Sdk.Runtime + TestRunner but NOT Shell.Editor -> build-plan tests need a new nested test asmdef.

## Design decisions (so far)
- Endpoint file read ONLY on a real Android player (`Application.platform == RuntimePlatform.Android`), so editor EditMode/PlayMode tests stay hermetic; a missing file is the normal case and logs at info level once; unreadable/malformed logs ONE warning per process; never an exception.
- File keys (case-insensitive): hubHost, hubPort, nodeAHost, nodeAPort, nodeBHost, nodeBPort, discoveryPort. Empty/null value = key absent (falls through, like the env layer). A bad value in a known key rejects the whole file (warning). `host:port` inside a host key is accepted (same parser as the settings asset); an explicit port key wins.
- Build method: scenes = Bootstrap (only if the file exists) + PhantomHand; PhantomHand missing = exception; Orchard is never added (brief says "never Orchard"; U6 text says "OrchardReach as fallback": brief followed, flagged).

## CHECKPOINT 1 - D3 endpoint override file (done)
Files: `game/Assets/Shell/Runtime/PhantomEndpoints.cs` (edited, additive: nothing existing was removed or reordered) and NEW `game/Assets/Shell/Tests/Editor/PhantomEndpointsFileTests.cs`.
Behaviour:
- Precedence per field: env > `phantom_endpoints.json` > settings asset > discovery. The existing 2-arg `Resolve(env, settings)` (called by `PhantomHandSceneController.Awake` and `BootstrapLoader.Start`) now delegates to a new 4-arg `Resolve(env, settings, readEndpointsFile, warn)`, so NO caller edit is needed.
- The 2-arg path reads the file ONLY when `Application.platform == RuntimePlatform.Android` (a real player; the editor reports WindowsEditor), from `Application.persistentDataPath/phantom_endpoints.json`; the existing EditMode/PlayMode tests therefore stay hermetic. Absent file: one info log with the exact path; unreadable / over 64 KB / malformed: ONE warning per process (static flag), file ignored, never an exception.
- Pure `PhantomEndpoints.TryParseFile(text, out FileValues, out problem)`: keys hubHost, hubPort, nodeAHost, nodeAPort, nodeBHost, nodeBPort, discoveryPort; case-insensitive; empty/null value = not set; `host:port` inside a host key accepted, an explicit port key wins; ports as JSON integers 1-65535 or numeric strings; a UTF-8 BOM is stripped; unknown keys ignored; a wrong value in a known key rejects the whole file (all or nothing, the reason names the key). `hubPort` lands in `PhantomEndpoints.HubPort` (the controller already warns when it is not 8787; with the SDK `hubPort` patch that warning can go, see INTEGRATION LINES).
- New public surface: `PhantomEndpoints.FileName`, `MaxFileBytes`, `FileStatus`, `FileValues`, `TryParseFile`, the 4-arg `Resolve`.
Operator command (package id is printed by the APK build; Unity default for this project is `com.DefaultCompany.OPUS`, inference):
`adb push phantom_endpoints.json /sdcard/Android/data/<package>/files/phantom_endpoints.json` then restart the app. If the `files` dir does not exist yet (the app never ran): `adb shell mkdir -p /sdcard/Android/data/<package>/files`. Check: `adb logcat -d -s Unity | findstr PhantomHand` shows `phantom_endpoints.json applied ...` or the one warning.
Tests (43 cases in 19 methods = the 41 passed + 2 engine-only skipped in the runner output below): parser (all keys, partial, 11 not-an-object texts, 12 wrong-value cases, BOM/case/unknown keys, empty/null, numeric strings, host:port), Resolve precedence (file over defaults, env over file field by field, no file = no warning, malformed = exactly one warning, reader throws = one warning, hubPort carried), 2 engine tests (`[Category("UnityEngine")]`: file between asset and env with a real ScriptableObject; the 2-arg overload reads no file outside Android).
Real output (reflection runner against Unity's own `nunit.framework.dll`, Newtonsoft from the Unity package cache, `UnityEngine.CoreModule.dll` from 6000.4.6f1, C# 9, net10.0):
```
dotnet build  ->  Build succeeded. 0 Warning(s) 0 Error(s)
RESULT passed=41 failed=0 skipped(engine)=2
```
One test assumption was wrong on the first run and was removed, not the code: Json.NET accepts a trailing comma in an object (`{"hubHost":"1.2.3.4",}`), which is harmless, so it is not in the "must reject" list.

## CHECKPOINT 2 - D4 Android multicast lock (done)
Files (both NEW): `game/Assets/Shell/Runtime/AndroidMulticastLock.cs`, `game/Assets/Shell/Tests/Editor/AndroidMulticastLockTests.cs`.
Behaviour:
- `Opus.Shell.AndroidMulticastLock.Acquire()` (static, returns true when the lock is held after the call, never throws). On a real Android player (`#if UNITY_ANDROID && !UNITY_EDITOR`) it does `UnityPlayer.currentActivity.getApplicationContext().getSystemService("wifi").createMulticastLock("phantomhand")`, `setReferenceCounted(false)`, `acquire()`, then checks `isHeld()`; the Java object is kept in a static (Android's `MulticastLock.finalize()` would release a collected lock). Everywhere else (editor, Windows, tests) it is a silent `return false`. Same `currentActivity` pattern as Meta's own `OVRManager.cs:1669-1670` in this project.
- Release: `Application.quitting` (hooked on the first successful acquire). If the process is killed instead, Android frees the lock with the process.
- The rules live in the platform-independent `MulticastLockHolder` (idempotent, one platform call, a failure is remembered so there is ONE warning and no retry storm, release is safe any time, a throwing platform or logger cannot escape).
- NOT called from any existing file (the controller and `BootstrapLoader` are not mine): see INTEGRATION LINES.
- Needs the manifest permission `CHANGE_WIFI_MULTICAST_STATE`, already in `Assets/Plugins/Android/AndroidManifest.xml:26`.
Verification: compiled twice against the real `UnityEngine.CoreModule.dll` / `UnityEngine.AndroidJNIModule.dll`, once without and once with `UNITY_ANDROID` (the on-device branch): both `0 Warning(s) 0 Error(s)`. The harness ran 9 holder/facade tests:
```
RESULT passed=50 failed=0 skipped(engine)=2     (41 endpoint-file tests + 9 multicast tests; 2 engine-only tests skipped by category)
```
Not proven: that the lock really keeps 8791/8788 beacons arriving on a Quest (needs the headset; MANUAL).

## CHECKPOINT 3 - D1 Phantom Hand APK build method (done, never run in Unity)
Files: `game/Assets/Shell/Editor/OpusBuildScript.cs` (edited, additive: the old `BuildAndroidApk` is untouched), NEW `game/Assets/Shell/Editor/PhantomBuildPlan.cs` (pure helpers, no UnityEditor use), NEW `game/Assets/Shell/Tests/Editor/Build/Shell.Tests.Build.Editor.asmdef` + `PhantomBuildPlanTests.cs` (a nested test assembly: the existing `Shell.Tests.Editor` cannot see `Shell.Editor` and I may not edit its asmdef; Unity writes the .meta files).
**Entry points** (all static, parameterless, so `-executeMethod` and `MethodInfo.Invoke(null, null)` both work; no overloads with those names, so `GetMethod(name)` is unambiguous):
- `Opus.Shell.Editor.OpusBuildScript.BuildAndroidApkPhantomHand` - menu `Tools/OPUS/Build Phantom Hand APK`; release unless `-phDevelopment` is on the command line or env `PH_BUILD_DEVELOPMENT` is 1/true/yes/on.
- `Opus.Shell.Editor.OpusBuildScript.BuildAndroidApkPhantomHandDevelopment` - menu `Tools/OPUS/Build Phantom Hand APK (development)`; always a development build (`BuildOptions.Development`).
- `OpusBuildScript.BuildPhantomHandApk(bool development)` returns the path (the shared core).
Batch: `Unity.exe -batchmode -projectPath <repo>\game -executeMethod Opus.Shell.Editor.OpusBuildScript.BuildAndroidApkPhantomHand [-phDevelopment] -logFile <log> -quit` (editor closed, or use the menu in the open editor). Not run by me.
**Behaviour**
- Scenes: `Assets/Scenes/Bootstrap.unity` only if the file exists (warning when absent: the APK then opens PhantomHand directly, which works without Bootstrap), then `Assets/Scenes/PhantomHand.unity`; PhantomHand missing = exception, no fallback; Orchard is never added (the old method's OrchardReach fallback is not carried over). `EditorBuildSettings` is not touched (explicit scene list).
- Output: REPO-ROOT `releases/game/<version>/chetna-phantom-hand.apk` (development: `chetna-phantom-hand-dev.apk`), `<version>` = `version` in `Assets/Games/PhantomHand/manifest.json` (now `0.1.0`), else `PlayerSettings.bundleVersion`, sanitised for a folder name. (`releases/*/` is gitignored at the repo root.)
- Settings it guarantees, each read first and written ONLY when different (so a normal run leaves ProjectSettings untouched), each logged `ok (...)` or `was X, setting Y`: active build target Android, scripting backend IL2CPP, CPU architectures ARM64, Gradle, texture compression ASTC (`EditorUserBuildSettings.androidBuildSubtarget`), App Bundle off, export-as-Gradle-project off, graphics API Vulkan only. From ProjectSettings today (read, YAML): Vulkan only, IL2CPP and ARM64 are already set, so the build should only log `ok`; ASTC is a per-user Library setting, unset in ProjectSettings.
- Not set by the build (and why): 72 Hz = `RigRuntimeSettings` at run time (scene value 72, existing test); MSAA 4x / HDR off = URP asset `OpusQuestURP` (already 4 / off); Quest-3-only = OVRProjectConfig (see open issues, not mine); FFR (see open issues).
- Pre-build check (added after the first draft): `PhantomHand.unity` must carry a `PhantomHandSceneController` (script GUID looked up with `AssetDatabase.AssetPathToGUID`, then searched in the scene YAML), else `BuildFailedException` BEFORE the long build. Reason, verified this session: the committed scene has 0 references to the controller's GUID `e1f5cf6a...` (worklist A1/B2), so an APK built from it would start and do nothing. It will therefore refuse until `PhantomHandSceneBuilder` adds the controller and the scene is rebuilt.
- Fails loudly: a previous APK at the target path is first moved to `<name>.apk.prev` (a failed build, e.g. the known R7 Gradle error, must not leave an old APK that looks new); `BuildResult != Succeeded` deletes any partial file and throws `BuildFailedException` whose text has result, error/warning counts, time, mode, output path and the first 15 error messages of the report; a "successful" build whose file is not a plausible APK (missing, < 5 MB, not a zip, no `AndroidManifest.xml`, no `lib/arm64-v8a/libil2cpp.so`) is renamed `<name>.apk.rejected` and throws. In batch mode an exception means exit code 1.
- On success it prints: output path, size in bytes and MB, the package id (`PlayerSettings.GetApplicationIdentifier`), the `adb install -r` command and the exact `adb push phantom_endpoints.json /sdcard/Android/data/<package>/files/phantom_endpoints.json` command.
**Tests** (28 cases in 20 methods, namespace `Opus.Shell.Tests.Build`): Bootstrap present / absent / Orchard never / PhantomHand missing throws; scene-controller GUID scan; dev switch (env on x5, off x5, command-line flag, null-safe); release vs `-dev` names; version from manifest / fallback / `0.0.0` / cannot escape `releases/`; the adb command names the same file as `PhantomEndpoints.FileName`; summary text and error cap; APK check on synthetic zips (missing, too small, not a zip, no manifest, Mono or wrong arch, good).
**Real output**
```
check_editor (net10.0, C# 9, real UnityEngine.CoreModule + UnityEditor.CoreModule + UnityEditor.BuildProfileModule):  Build succeeded. 0 Warning(s) 0 Error(s)
csc 5.9.0 against Unity's NetStandard\ref\2.1.0\netstandard.dll, -langversion:9.0, 5 Shell sources:   exit ok, netstd_check.dll emitted (only CS2023 noconfig + CS1701 Newtonsoft identity notices)
csc 5.9.0 against MonoBleedingEdge\lib\mono\4.7.1-api (+ Facades), same sources:                     no diagnostics, netfx_check.dll emitted
RESULT passed=77 failed=0 skipped(engine)=2     (41 endpoint-file + 9 multicast + 27 build-plan cases; the 2 skipped need the native engine)
after adding the scene-controller pre-build check: all three compiles above repeated clean (check_editor, netstandard 2.1, .NET Framework 4.7.1) and
RESULT passed=78 failed=0 skipped(engine)=2     (28 build-plan cases)
```
The APIs the compile proved to exist in 6000.4.6f1: `EditorUserBuildSettings.androidBuildSubtarget` / `MobileTextureSubtarget.ASTC` / `buildAppBundle` / `exportAsGoogleAndroidProject`, `PlayerSettings.GetApplicationIdentifier(NamedBuildTarget)`, `Get/SetGraphicsAPIs`, `Get/SetUseDefaultGraphicsAPIs`, `BuildFailedException(string)`, `BuildSummary.totalTime`.
Not proven (needs Unity): that the whole build produces an APK, how long it takes, that Gradle works on this machine (R7), the menu entries, `SwitchActiveBuildTarget` behaviour in an interactive editor.

## CHECKPOINT 4 - D2 demo mode + performance (done as far as it is code)
**What was ALREADY there (U2/U5), so nothing was duplicated and no existing file was edited:**
- demo_mode = induction 45 s: `PhantomHandParams.cs:44-47`; one questionnaire, at the end: `PhaseStateMachine.cs:125-126`, `PhantomHandModule.cs:325`; tests `PhaseAndParamsTests.DemoMode_ForcesInduction45` / `DemoMode_OneQuestionnaire_AtTheEnd`, `PhantomHandShellTests.ScriptedParticipantTests.AutoParticipant_PlaysDemoRun_ThroughAllPhasesToDone`, PlayMode `PH_FullRun_DemoMode_AgainstFakeHubAndTwin` (never green here yet: venvs + port 8787).
- One-gesture reset: both-hands pinch held 1.5 s once the run is `Finished` (`OpusSessionRunner.cs:244`, `bothHandsPinchHoldSec` :55) -> `StartSession` -> `NeedsFreshScene` -> `SwitchToNextPerson` (:297-306, :407-415) -> `PhantomHandSceneController.PrepareNextSession` (:495-513: sleeve stop + "IDLE", scene reload, the new scene auto-starts at Calibrate once the head tracked 2.5 s, :120-123). Operator "Next person": `next_person` (`OpusSessionRunner.cs:610`, same path, also mid-run) and the app button (`app/test/phantom/phantom_live_screen_test.dart`). Not timed on a device.
- 72 Hz `RigRuntimeSettings.cs:16,53-65` + scene value 72 + test `RigRuntimeSettings_At72Hz`. Only the stone's blob shadow: key light `m_Shadows.m_Type: 0` in `PhantomHand.unity` (test `Lighting_WarmKey_Ambient_LightFog`), presenter renderers shadows off, `BlobShadow` quad `ThreatDrop.cs:88`. MSAA 4x, HDR off, SRP batcher on: `OpusQuestURP.asset`. Per-texture Android ASTC 6x6 in `PhantomModelImporter.SetAstc` (:310-322).
**New (NEW file only):** `game/Assets/Shell/Tests/Editor/PhantomHandDemoModeTests.cs`, 6 tests: timer phases add up to 130 s (demo) vs 220 s (full); a scripted demo run lasts 130-165 s; a full run 215-250 s and demo is under 70 % of it; the order is calibrate, 2 x (probe, induction, threat, probe), ONE questionnaire, witness, done, with ownership only on the last condition; two runs from fresh modules repeat each other to the millisecond and share no result object (what the next-person reload builds); demo_mode changes the timeline only, not stroke rate, jitter, motor SOA, async delay, tactile lead, haptic intensity cap or condition order.
**Measured (scripted participant, manual clock, harness probe, same loop as the existing test):**
```
demo_mode:  Calibrate 2.22 | ProbePre 1.52 | Induction 45.00 | Threat 5.00 | ProbePost 1.52 | ProbePre 1.52 | Induction 45.00 | Threat 5.00 | ProbePost 1.52 | Questionnaire 2.12 | Witness 30.00   TOTAL 140.42 s
default:    Calibrate 2.22 | ... Induction 90.00 ... Questionnaire 2.12 (x2) ... Witness 30.00                                                                                              TOTAL 232.52 s
```
So "~2.5 min" holds as 130 s of timers + about 10 s scripted (a human takes longer on calibrate, probes and questions; 2.5 to 3 min is an inference, to be timed on the headset).
**Run in the harness:** `PhantomHandDemoModeTests` 6/6 pass. Mutation check (patched COPIES in the scratchpad, repo untouched): induction 45 -> 60 is caught by 4 tests (TimerPhases, ScriptedDemoRun, Demo_Shortens, ScriptedFullRun), "questionnaire after every condition" by the phase-order test; with both mutants 5 of 6 fail and only the run-to-run repeat test passes, as designed.
**Baseline of the existing Shell tests in the same harness** (`PhantomHandShellTests`, 25 tests): 22 pass; 3 fail: `PhantomEndpointsTests.Resolve_EnvironmentWinsOverSettings_AndFlagsEnvironment` (needs `ScriptableObject.CreateInstance`) and `Resolve_NullSettingsAndEnv_MeansDiscovery` (the 2-arg `Resolve` now reads `Application.platform`) die with `SecurityException: ECall methods must be packaged into a system module` = the harness has no native engine; in Unity the editor reports WindowsEditor so the file layer is skipped and both behave as before (reasoned, NOT run); `PhantomLiveStatusTests.Trace_20HzBins_AreAveragedAndAligned` fails with expected 422.5 / actual 420.0, the SAME numbers as defect G6 of the Unity baseline in `PH-O-WORKLIST-run1.md` (pre-existing, not mine; it also shows the harness reproduces Unity's result for pure logic).
**Performance bullets of U6 item 2:**
| Bullet | State |
|---|---|
| only the stone's blob shadow | DONE before this run (see above); optional hardening, not done: turn `m_MainLightShadowsSupported` off in the URP asset to strip shadow variants (asset not mine) |
| ASTC | DONE in the build method (`androidBuildSubtarget = ASTC`, logged) + per-texture ASTC 6x6 in the model importer |
| draw calls/eye < 100 (target < 60), SetPass < 40 | MANUAL: needs a profile. Use the development APK (`...PhantomHandDevelopment`), Unity Profiler (Rendering) over USB or the Frame Debugger in Play mode (the mono editor camera is one eye's worth of draws), and OVR Metrics Tool for fps/stale frames |
| log numbers; fix the top three costs | MANUAL (nothing to fix blind) |
| 72 Hz held, frame drops < 1 % (G3) | MANUAL (OVR Metrics Tool) |

## U6 acceptance table (docs/agent-briefs/ph/prompts/UNITY.md, section U6)
Legend: DONE = code exists and is verified as far as it can be without Unity; PRE = already built in U2/U5, only verified / tested here; MANUAL = needs the headset or a human; NOT DONE = not possible under this brief.
| # | U6 bullet | Status | Evidence |
|---|---|---|---|
| 1a | demo_mode: induction 45 s | PRE + new test | `PhantomHandParams.cs:44-47`; `PhantomHandDemoModeTests.TimerPhases_DemoIs130Seconds_FullRunIs220`, `Demo_ShortensTheTimeline_...` |
| 1b | one questionnaire at the end | PRE + new test | `PhaseStateMachine.cs:125-126`; `Demo_AsksOneQuestionnaire_AtTheEnd_ForTheLastConditionOnly` |
| 1c | ~2.5 min run | DONE for the scripted run (140.4 s = 130 s of timers + about 10 s); human-paced length MANUAL | `ScriptedDemoRun_TakesAboutTwoAndAHalfMinutes` (window 130-165 s), full run 232.5 s |
| 1d | one-gesture reset to Calibrate < 10 s without app restart | PRE (pinch 1.5 s when Finished -> reload -> auto-start at Calibrate); the < 10 s is MANUAL | `OpusSessionRunner.cs:244,297-306,407-415`, `PhantomHandSceneController.cs:120-123,495-513`; `TwoRunsInARow_FromFreshModules_...` covers the logic of the second run only |
| 1e | operator "Next person" does the same | PRE; end to end MANUAL (needs hub + headset) | `OpusSessionRunner.cs:610`, app button + tests under `app/test/phantom/` |
| 2a | only the stone's blob shadow | PRE, verified by reading | key light shadows None, `ThreatDrop.cs:88`, test `Lighting_WarmKey_Ambient_LightFog` |
| 2b | ASTC | DONE | `androidBuildSubtarget = ASTC` in the build + `PhantomModelImporter.SetAstc` |
| 2c | draw calls/eye < 100 (target < 60), SetPass < 40 | MANUAL | needs a profile on the headset (dev APK) or the Frame Debugger |
| 2d | log numbers; fix the top three costs | MANUAL | nothing to fix blind |
| 3a | `OpusBuildScript.BuildAndroidApkPhantomHand`, Bootstrap + PhantomHand | DONE (code), never run in Unity | `OpusBuildScript.cs`, `PhantomBuildPlan.cs`; **deviation: OrchardReach is NOT added** (the brief says "never Orchard", U6 text says "OrchardReach as fallback"); adding it later = one entry in `PhantomBuildPlan.ResolveScenes` |
| 3b | IL2CPP, ARM64, Vulkan | DONE: enforced and logged by the build (already set in ProjectSettings, so it should only log `ok`) | `ApplyQuestBuildSettings` |
| 3c | 72 Hz | PRE (run-time, `RigRuntimeSettings`) - not a build setting | `RigRuntimeSettings.cs:53-65` |
| 3d | output `releases/game/<ver>/chetna-phantom-hand.apk` | DONE (repo-root `releases/`, `<ver>` = manifest version 0.1.0; `-dev` suffix for the development build) | `PhantomBuildPlan.RelativeOutputPath` |
| 3e | R7 Gradle loopback: log to MANUAL_TODO, tell Opus | MANUAL: no Unity run here; `docs/MANUAL_TODO.md` is not my file; the build throws with the report's first errors and keeps no stale APK | `docs/MANUAL_TODO.md:120-134` |
| 4 | list every human step for H4 | MANUAL (docs agent); the steps this work adds are listed below | |
| - | "Final: all EditMode + PlayMode (Orchard + PhantomHand + SDK); paste totals" | NOT DONE (Unity may not be launched) | the harness results below are NOT Unity totals |
| + | Quest-only gap 1: runtime endpoint override | DONE (code + 41 pure tests) | checkpoint 1 |
| + | Quest-only gap 2: Wi-Fi multicast lock | DONE (code + 9 pure tests), NOT wired, effect on a Quest MANUAL | checkpoint 2 + integration line 1 |

## INTEGRATION LINES FOR OPUS
(files I may not edit; exact lines + anchors)
1. **Wire the multicast lock (needed for D4 to do anything).** `game/Assets/Shell/Runtime/PhantomHandSceneController.cs`, method `Awake()`: insert as its own line immediately BEFORE the line `_clock = new SessionClock();` (it follows `_useDemo = !real;`, before `_transportA = new UdpHapticTransport(...)`), same namespace so no `using`:
   ```csharp
   AndroidMulticastLock.Acquire();   // Quest: hold the Wi-Fi multicast lock before the UDP sockets are bound; silent no-op in the editor
   ```
2. **Same line, earlier (recommended).** `game/Assets/Shell/Runtime/BootstrapLoader.cs`, `Start()`: first statement, before `if (settings == null) settings = Resources.Load<PhantomHandSettings>(...)`. The Bootstrap scene's `LiveClient` listens for the hub beacon on 8788 during the 3 s program wait, before the game scene exists. `Acquire()` is idempotent, so lines 1 and 2 together are fine.
3. **Hub port, after the SDK patch (`LiveClient(..., int hubPort = 8787)` as the last constructor argument) is merged.** What this run guarantees: `PhantomEndpoints.Resolve(...).HubPort` carries the file's `hubPort` (0 = not given). To use it: (a) `PhantomHandSceneController.Awake`: delete the block that starts `if (_ep.HubPort != 0 && _ep.HubPort != PhantomEndpoints.DefaultHubPort)` / `requested but LiveClient always uses`; (b) pass `_ep.HubPort > 0 ? _ep.HubPort : PhantomEndpoints.DefaultHubPort` as the new last argument in `BootstrapLoader.Start` (`new LiveClient(_deviceId, ..., host)`), and through a new `ISessionHost.HubPortHint` in `OpusSessionRunner.StartClient` (the SDK agent's change set already plans `HubPortHint`).
4. **Orchard-less APK safety (recommended).** `game/Assets/Shell/Runtime/BootstrapLoader.cs`, replace the body of `SceneFor` so a scene that is not in the build is "unknown":
   ```csharp
   public static string SceneFor(string gameId)
   {
       string name;
       switch (gameId) { case "orchard_reach": name = "OrchardReach"; break; case "phantom_hand": name = "PhantomHand"; break; default: return null; }
       return Application.CanStreamedLevelBeLoaded(name) ? name : null;   // an APK built without that scene must not try to open it
   }
   ```
   Why: this APK has no Orchard scene. An `assign_program` naming `orchard_reach` (the app fixtures still carry Orchard programs) would today be acked ok, the live client disposed, and `SceneManager.LoadScene("OrchardReach")` would fail: the headset stays on the black Bootstrap scene with no hub link. With the change the command is acked `ok=false "no known game in the program"` by the existing branch. `SceneFor` is used only inside `BootstrapLoader` (grep: no test calls it).
5. **Prerequisite, not a line:** `PhantomHandSceneBuilder.BuildScene` must add the controller (U5 hook `var controller = root.AddComponent<Opus.Shell.PhantomHandSceneController>();`, worklist B2) and the scene must be rebuilt, otherwise the new build method refuses (see D1, pre-build check).
6. **Optional, not applied (decision after seeing the ruler on the headset): foveated rendering "High" (02-RULES section 3).** It is set nowhere in code, scenes or settings (grep); `MetaXRFoveationFeature Android` is enabled in the OpenXR settings (`m_enabled: 1`). A reflection call in the style of `RigRuntimeSettings.SetDisplayFrequency`, e.g. in `RigRuntimeSettings.Start()` after `SetDisplayFrequency();` (property confirmed in `OVRManager.cs:1570`; this snippet is not compiled and not run):
   ```csharp
   var prop = ovr?.GetProperty("foveatedRenderingLevel", BindingFlags.Public | BindingFlags.Static);   // ovr = the OVRManager type found as in SetDisplayFrequency
   if (prop != null && prop.CanWrite) prop.SetValue(null, Enum.Parse(prop.PropertyType, "High"));
   ```
   Not applied because FFR blurs the periphery and the 1 m ruler with cm ticks reaches it.

## Human steps and toggles this work adds (feed for H4; all MANUAL)
1. Before any APK: the scene builder wiring (line 5 above), `Tools/OPUS/Build PhantomHand Scene`, `Tools/OPUS/Build Bootstrap Scene` (the driver's tree already has Bootstrap; the build also works without it, with a warning).
2. Build in a normal user session (R7: Gradle "Unable to establish loopback connection" is a host problem, `docs/MANUAL_TODO.md:120-134`): menu `Tools/OPUS/Build Phantom Hand APK (development)` first (profiling, Meta XR Operator needs a development build), `Tools/OPUS/Build Phantom Hand APK` for the demo, or batch `Unity.exe -batchmode -projectPath <repo>\game -executeMethod Opus.Shell.Editor.OpusBuildScript.BuildAndroidApkPhantomHand [-phDevelopment] -logFile <log> -quit` with the editor closed. IL2CPP alone took about 13 min in R7.
3. Settings the build cannot or should not change (check once): Player Settings > Identification > package name (default `com.DefaultCompany.OPUS`, set a real id before sharing); XR Plug-in Management > Android has the OpenXR loader (verified in `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`, init on start); OVRProjectConfig target devices and the manifest `supportedDevices` (see open issue 7); the debug keystore is used (fine for sideloading).
4. Install: `adb install -r releases\game\0.1.0\chetna-phantom-hand.apk` (the build prints the exact path). Check the first-launch permission prompts on the headset.
5. Set the addresses without a rebuild: write `phantom_endpoints.json` (keys above), `adb push phantom_endpoints.json /sdcard/Android/data/<package>/files/phantom_endpoints.json` (create the folder with `adb shell mkdir -p /sdcard/Android/data/<package>/files` if the app never ran), restart the app, then `adb logcat -d -s Unity | findstr PhantomHand` must show `phantom_endpoints.json applied ...`; if the file is absent the log prints the exact path the app looked at.
6. Demo run: assign the Demo preset program (manifest preset `demo`: `demo_mode: true`) from the app BEFORE Start, otherwise the headset runs the full 3 min 53 s program (open issue 4). Reset between people: both-hands pinch 1.5 s when the Witness screen is over, or "Next person" in the app; time it (< 10 s) with a stopwatch.
7. Measure: OVR Metrics Tool during a demo run (fps, stale frames; G3 asks frame drops < 1 %), Unity Profiler on the dev APK for draw calls per eye and SetPass.

## Commands + real output
Harness (outside the repo, scratchpad `...\scratchpad\u6code\`): `check_runtime`, `check_runtime_android`, `check_editor`, `run_tests`, `run_demo`, `run_demo_mut`; compiler `dotnet` SDK 10.0.401 (Roslyn 5.9.0), `LangVersion 9.0`; references: Unity 6000.4.6f1 `Editor\Data\Managed\UnityEngine\*.dll`, Newtonsoft.Json and `nunit.framework.dll` (Unity's `net40\unity-custom`) from `H:\Chenta\phantomhand\game\Library\PackageCache` (read-only). `dotnet test` was not possible (no NuGet cache), so a 60-line reflection runner (`Runner.cs`) executes the `[Test]` / `[TestCase]` methods.
```
check_runtime         (PhantomEndpoints, PhantomHandSettings, AndroidMulticastLock; editor branch)   Build succeeded. 0 Warning(s) 0 Error(s)
check_runtime_android (same + UNITY_ANDROID, AndroidJNIModule; on-device branch)                       Build succeeded. 0 Warning(s) 0 Error(s)
check_editor          (OpusBuildScript, PhantomBuildPlan + the Shell files they use; UnityEditor.CoreModule, BuildProfileModule)   Build succeeded. 0 Warning(s) 0 Error(s)
csc @netstd.rsp       (Unity NetStandard\ref\2.1.0\netstandard.dll, the 5 files)                       netstd_check.dll emitted; only CS2023 (noconfig in rsp) and CS1701 (Newtonsoft identity) notices
csc @netfx.rsp        (Unity MonoBleedingEdge\lib\mono\4.7.1-api + Facades)                            netfx_check.dll emitted; no diagnostics
run_tests (endpoint-file 41+2 engine, multicast 9, build-plan 28)                                      RESULT passed=78 failed=0 skipped(engine)=2
run_demo  (SDK + PhantomHand runtime + pure Shell from source; the 25 existing PhantomHandShellTests + all new Shell tests except build-plan)
   RUNNER_SKIP="Resolve_EnvironmentWinsOverSettings_AndFlagsEnvironment;Resolve_NullSettingsAndEnv_MeansDiscovery;ProbeDemoTimeline"
   FAIL PhantomLiveStatusTests.Trace_20HzBins_AreAveragedAndAligned: Expected 422.5d +/- 0.1, But was 420.0d     (pre-existing, worklist G6)
   RESULT passed=78 failed=1 skipped(engine)=5
run_demo_mut (2 mutants, patched copies in the scratchpad)                                              RESULT passed=1 failed=5 skipped(engine)=0   (mutants caught)
probe (throw-away, harness only)  demo_mode TOTAL 140.42 s done=True ; default TOTAL 232.52 s done=True
```
New passing test cases: 41 (endpoint file) + 9 (multicast) + 28 (build plan) + 6 (demo mode) = 84; plus 2 engine-only cases written, not run. These are NOT Unity test-runner totals.
`git status` (uncommitted): `M game/Assets/Shell/Editor/OpusBuildScript.cs` (+ additive, 0 lines removed), `M game/Assets/Shell/Runtime/PhantomEndpoints.cs` (+178, 1 doc line replaced); new: `Shell/Editor/PhantomBuildPlan.cs`, `Shell/Runtime/AndroidMulticastLock.cs`, `Shell/Tests/Editor/{PhantomEndpointsFileTests,AndroidMulticastLockTests,PhantomHandDemoModeTests}.cs`, `Shell/Tests/Editor/Build/{Shell.Tests.Build.Editor.asmdef,PhantomBuildPlanTests.cs}`, this log. No .meta files written, nothing outside the allowed list touched, no `DevAgentSettings.asset` opened.

## Open issues
1. **U6 text vs brief on Orchard.** U6 says "Bootstrap + PhantomHand + OrchardReach as fallback"; the brief says "never Orchard" (and Opus is removing Orchard, worklist section E). Followed the brief. Integration line 4 keeps the Orchard-less APK from stranding the headset.
2. **The new build refuses to run on today's committed `PhantomHand.unity`** (no controller, see D1). Intended; the driver's scene work must land first.
3. **Unity never compiled or ran any of this.** What was proven: compilation against the real Unity DLLs in three profiles, and the pure logic in a reflection runner. NOT proven: any `[MenuItem]`, `BuildPipeline.BuildPlayer`, `SwitchActiveBuildTarget` in an interactive editor (re-import, minutes), Gradle on this machine, the nested test assembly's asmdef resolving in Unity, the 2 engine-category tests, the 2 existing `PhantomEndpointsTests` that call `ScriptableObject.CreateInstance` / the 2-arg `Resolve` (reasoned to be unchanged in the editor because `Application.platform` is not Android there).
4. **demo_mode needs an assigned program.** `OpusSessionRunner._programParams` starts empty (`:83`) and only `assign_program` fills it (`ApplyProgram`, `:624-637`; re-sent by every next-person reload, `:425`). A headset that never receives a program (no hub, or Start pressed without assigning) runs the full program. There is no settings-asset default for demo_mode (`PhantomHandSettings` has no such field; adding one means editing the settings class and the controller, not mine).
5. **Hub port is carried but not used until the SDK patch is integrated** (integration line 3). Until then the controller logs its existing warning when the file asks for a port other than 8787.
6. **Android path of the endpoints file is inferred.** `Application.persistentDataPath` on a Quest is expected to be `/storage/emulated/0/Android/data/<package>/files` (= `/sdcard/Android/data/<package>/files` over adb) and the package id `com.DefaultCompany.OPUS` (no `applicationIdentifier` is set; the build prints the real one). The app logs the exact path it looked at when the file is absent, so a wrong guess shows up in logcat at once. `adb push` into `Android/data/<package>` on Horizon OS is expected to work but was not tried.
7. **Device targeting disagrees with 02-RULES section 3 ("Quest 3, keep 3S/2 off").** `Assets/Oculus/OculusProjectConfig.asset` `targetDeviceTypes` = Quest2, QuestPro, Quest3, Quest3S (enum `OVRProjectConfig.cs:50-58`) and `Assets/Plugins/Android/AndroidManifest.xml:16` lists `quest|quest2|questpro|quest3|quest3s`. Not changed (not my files); Opus decision.
8. **FFR "High" is set nowhere** (integration line 6, not applied on purpose).
9. **URP asset still has `m_MainLightShadowsSupported: 1`** (no light casts shadows, so only shader variants are affected). Not touched.
10. **The old `BuildAndroidApk` writes to `game/releases/game/0.2.0/`** (`Application.dataPath + "../releases"` is `game/releases`), not the repo-root `releases/` named by `releases/VERSIONS.md`; a leftover `game/releases/game/0.2.0/OPUS_BurstDebugInformation_DoNotShip/tempburstlibs/arm64-v8a/lib_burst_generated.txt` is tracked in git. The new method writes to the repo-root `releases/`. Left alone.
11. Files the build can leave next to the APK: `*.apk.prev` (the previous APK, moved aside before building) and `*.apk.rejected` (a build that "succeeded" but failed the file check). Both are under the gitignored `releases/*/`.
12. `PhantomHandSceneController` still has the 2.5 s head-tracking gate before the auto-start after a reload (`:120-123`), so "reset < 10 s" is about 1-3 s reload + 2.5 s + calibrate start: plausible, unmeasured.

## Next step
Opus: review and merge this diff (uncommitted in the worktree). Then the Unity driver, with the lock: apply integration lines 1, 2 and 4, let the scene builder add the controller and rebuild the scene, run EditMode (expect 84 new passing cases plus the 2 engine ones to be checked) and PH_FullRun, then `BuildAndroidApkPhantomHandDevelopment` and the human steps above. Whoever merges the SDK `hubPort` patch applies integration line 3.
