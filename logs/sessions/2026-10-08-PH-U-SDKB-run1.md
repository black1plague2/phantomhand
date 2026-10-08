# PH U SDKB run 1: SDK change set for the real electronics firmware + three live-link defects (ph-builder)

Goal: seven SDK items so `com.opus.sdk` talks to the electronics team's real ESP32 firmware (acks, keepalive, display `mode`, node matching by kind, LiveClient `hubPort`) and survives three live-link defects (WSAECONNRESET, StrokeDriver after pause+resume), each with EditMode tests.

Scope and state: isolated git worktree `H:\Chenta\phantomhand\.claude\worktrees\agent-aa3b63e4f8e12b618`, branch `worktree-agent-aa3b63e4f8e12b618`. Unity was NOT launched, no Unity MCP, main checkout untouched, nothing committed/tagged/pushed/stashed. Result is an uncommitted working-tree diff (12 modified files, no new .cs file, so no .meta to generate). `DevAgentSettings.asset` was never opened. No secrets.

**Honest status: every test below was run only in a standalone .NET harness with a stubbed UnityEngine (243/243 green). None has run in the Unity editor. The authoritative run is the later editor run.** Compile correctness was additionally checked against Unity's real managed engine DLLs (0 errors, 0 warnings).

## Plan (written before coding)
Constraint: the editor is busy elsewhere and cannot be used, so every claim has to come from a standalone harness plus a compile-only pass against Unity's real DLLs.
Failure modes: (a) a test that compiles in the harness but not in Unity -> second compile against the real UnityEngine modules + Unity's own NUnit, C# 9, both net10.0 and netstandard2.1 reference sets; (b) a test that passes vacuously -> every new test was also run against the ORIGINAL production code (red check) and two mutations; (c) a safety limit loosened -> no limit constant touched (see Safety).
Ledger: everything built directly (small, tightly coupled edits); no subagents.

## Findings worth knowing first
1. **Item 4 needed no production change.** `DiscoveryFilter.Haptic` already matched by `device_kind` only; both `SLEEVE_001` and `CHETNA_HAPTIC_001` latched on the original code. The 5 new node-matching tests pass on the old code too; they pin the behaviour (and fail if someone adds an id check).
2. **Item 1 was wrong in a way the brief did not say.** `status:"accepted"` was already a success. The real firmware's boolean `accepted:true` only "worked" by accident (no `ok`, no `status` -> counted as a plain receipt). **`accepted:false` was ALSO reported as delivered** (a firmware rejection for the 100 ms gap, the 10 s duty budget, a bad motor would have counted as delivered and inflated `cue_delivery_rate`). Also a non-boolean `ok`/non-string `status` threw inside the receive thread (which ends the loop). Fixed.
3. **WSAECONNRESET premise verified on this machine** (Windows 11 build 26300, .NET 10.0.12), one-off probe:
```
without SIO_UDP_CONNRESET: ioctl=not called -> SocketException ErrorCode=10054 SocketErrorCode=ConnectionReset
with    SIO_UDP_CONNRESET: ioctl=ok -> SocketException ErrorCode=10060 SocketErrorCode=TimedOut
```
   and the original `UdpHapticTransport` receive loop really dies on it (red test below).

## Per item (files + methods, before/after, tests)
Test classes: `Opus.Sdk.Tests` in `game/Packages/com.opus.sdk/Tests/Editor/` (asmdef `Opus.Sdk.Tests.Editor`), and `Opus.Games.PhantomHand.Tests` in `game/Assets/Games/PhantomHand/Tests/Editor/` (asmdef `PhantomHand.Tests.Editor`, item 7 only).

| # | Item | Files / methods | Before -> after | Tests added |
|---|---|---|---|---|
| 1 | acks | `HapticClient.cs`: `HandleMessage` (ack case), new private `AckSucceeded` | success = `ok` (v1) else boolean `accepted` (real fw) else `status` accepted/executed; status text read type-safely. `accepted:false`/`status:rejected`/`error`/unknown -> not delivered, reason = `error_code` ?? status ?? "rejected". Ack with none of the three = plain receipt (delivered), as before | HapticStrokeTests: `Ack_RealFirmwareShape_AcceptedTrue_IsDeliveredWithLatency`, `Ack_EveryDialect_DeliveredAndReason` (13 shapes), `Ack_V1Dialect_AckIdAndOk_StillWorks`, `Ack_WithOddlyTypedFields_NeverThrows_AndFallsBackToTheReceiptRule`, `Ack_ForACueNobodySent_IsIgnored` |
| 2 | keepalive | `HapticClient.cs`: `PumpKeepalive`, new `NodeLost`, `_lastRxMs` (set in `HandleMessage`); `SleeveSensorClient.cs`: `Pump`, new `NodeLost` | next to ping+subscribe each second, `{"type":"keepalive"}` (exact bare string). Enqueue only (never blocks; the transport writes on its own thread). Gated by: client started, device known, node not lost (heard from at least once, then silent > 3000 ms = `SleeveSensorClient.SilenceTimeoutMs`). A node never heard from still gets it; ping+subscribe go on while lost so the node re-attaches. Stops with `StopKeepalive()` / `Stop()` | HapticStrokeTests: `Keepalive_AlsoSendsTheBareKeepaliveDatagramOncePerSecond`, `Keepalive_StopsForALostNode_AndResumesWhenItSpeaksAgain`, `Keepalive_NotSentWhileNoDeviceIsKnown_AndStartsWhenOneAppears`, `Keepalive_PumpOnlyEnqueues_SoARealTransportNeverBlocksTheGameThread`; SleeveSensorClientTests: `Keepalive_AlsoSendsTheBareKeepaliveDatagramOncePerSecond_StopsWithStop`, `Keepalive_StopsWhenTheNodeIsLost_AndResumesWhenItSpeaksAgain`, `Keepalive_NotSentWhileNoDeviceIsKnown` |
| 3 | display | `HapticClient.cs`: `SendDisplay` | `{"type":"display","text":T}` -> `{"type":"display","text":T,"mode":T}`, same sanitized line (printable ASCII, 12 chars, 2/s unchanged) | HapticStrokeTests: `Display_CarriesTheSameLineInTextAndMode` |
| 4 | node match | none (already by kind) | unchanged | DiscoveryTests: `Parse_RealFirmwareBeacons_AreMatchedByKind`, `Filter_TheKindDecides_TheIdNever`, `Parse_LegacyHello_WithoutAKind_StaysHaptic_WhateverItsId`, `Transport_DefaultFilter_LatchesNodeA_UnderTheContractIdAndTheRealId`, `Hub_RealTwoNodeSession_EachTransportLatchesItsKind_InEitherArrivalOrder`; SleeveSensorClientTests: `RealFirmware_NodeA_AccelOnlySensorData_UnderItsOwnId_IsAccepted`, `RealFirmware_NodeB_FourValueChunks_AreSpacedBySampleRate`; SensorRecorderTests: `Imu_FromTheRealNodeA_IsRecordedUnderItsOwnId` (verbatim datagrams from the handoff) |
| 5 | hubPort | `LiveClient.cs`: ctor `hubPort = HubPort` (last optional arg, existing named/positional callers unchanged), `_hubPort`, `ActiveHubPort`, static `BuildWsUri`, `BuildUploadUrl`; used in `ConnectAndRunAsync` (ws) and `StartUploadCoroutineless` (HTTP PUT) | const 8787 everywhere -> instance port, default 8787, out of range (<1, >65535) falls back to 8787. The const `HubPort` stays (callers use it). Discovery beacon port 8788 unchanged | LiveMessageTests.cs, new class `LiveClientHubPortTests`: `DefaultHubPort_Is8787`, `ExplicitHubPort_IsKept_AndOutOfRangeFallsBackToTheDefault`, `Urls_CarryThePort`, `CustomHubPort_ReachesTheSocket` (real TcpListener on an ephemeral port: request line `GET /opus/v1/live`, Host header carries the port) |
| 6 | 10054 | `UdpHapticTransport.cs`: `Start` (`UdpConnReset.Disable`), `ReceiveLoopAsync` (`catch (SocketException e) when (IsConnReset) { continue; }` before the generic `break`), new public static `UdpConnReset` (`IsConnReset`, `Disable`, consts); `DiscoveryHub.cs`: `ListenLoopAsync` same two lines | loop ended on any SocketException -> 10054 swallowed and receiving continues; `Disable` = `Socket.IOControl(-1744830452, {0,0,0,0})` on Windows only, never throws, returns false elsewhere; every other SocketException still ends the loop. `LiveClient`'s one-shot hub-beacon receive is not a loop and its socket never sends, so Windows cannot raise 10054 there: left alone | DiscoveryTests.cs, new class `UdpConnResetTests`: `IsConnReset_IsTrueFor10054Only`, `Disable_NeverThrows_IsWindowsOnly_AndWhereItWorksTheOsStopsReportingTheReset`, `Transport_KeepsReceiving_AfterSendsToAClosedPort` (real loopback sockets) |
| 7 | resume | **`game/Assets/Games/PhantomHand/Runtime/Presentation/StrokeDriver.cs` (not in the SDK)**: `Begin`/`End` (subscribe/unsubscribe module events), `OnCue` (flags cues cancelled by `HapticClient.Stop`), new `OnModuleEvent`, `Reschedule`, `Requeue`, `Submit` (marks refused), `OnPass` made public | pause -> shell calls `HapticClient.Stop` (cancels every queued cue) and nothing queued them again, so the touch ended with the pause. Now on the module's `resume` event every cancelled cue whose send time is still ahead is scheduled again at its ORIGINAL plan time with the same tactile lead (the brush follows the same absolute plan and the clock never stops, so SYNC stays on the visual pass, ASYNC keeps its delay). Cues whose moment passed during the pause are never sent late. Cues that were not cancelled are not queued twice. A stroke whose event the paused module refused gets another try once its re-queued cues resolve (ASYNC delays of ~600 ms make a straddling stroke likely). Ack-thread safety: `_cues` guarded by a lock | PhantomHand `StrokeSchedulerTests.cs`, new class `StrokeDriverTests`: `PauseThenResume_SchedulesTheRemainingCuesAgain_AtTheirOriginalPlanTimes`, `ResumeWithoutAStop_QueuesNothingTwice`, `TwoPauses_EveryCueOutsideThePauseWindowsIsSentExactlyOnce_AndNoneInsideThem`, `AsyncStrokeCaughtByAShortPause_StillGetsItsStrokeEvent_OnceItsCuesGoOut`, `Begin_Twice_StillQueuesEachCueOnlyOnceOnResume`, `AfterTheInductionEnded_AResumeSchedulesNothing`; SDK side (contract the driver relies on): HapticStrokeTests `Stroke_ScheduledAgainAfterStop_IsSentAtTheOriginalPlayAt` |

Why `OnPass` is public: EditMode has no `BrushRig` (a MonoBehaviour); the test's "brush" reports each pass at its plan time through `StrokeDriver.OnPass`. `BrushRig.OnPass += OnPass` still compiles (checked against the real BrushRig).

Safety (02-RULES section 4): untouched `StrokeIntensityCap` 150, 200 ms pulse, 250 ms/motor and 4/s stroke gates, 50 ms late drop, 2 s software watchdog, `HapticCueMapper`. Re-queued strokes go through the same `PumpStrokes` gates. A keepalive never refreshes the cue watchdog (`_lastAnySendMs`). No PII in any new message. No blocking socket call on the main thread (the new test code blocks only on the test thread).

## Commands and real output
Environment: `.NET SDK 10.0.401`, runtime 10.0.12, win-x64. NuGet is unreachable here (`curl https://api.nuget.org/v3/index.json` -> `http=000`, no `~/.nuget/packages`), so **`dotnet test` could not be used** (needs Microsoft.NET.Test.Sdk + NUnit3TestAdapter from NuGet). Substitute: a ~100-line reflection runner (`Program.cs`) over Unity's own `nunit.framework.dll` (`game/Library/PackageCache/com.unity.ext.nunit@d8c07649098d/net40/unity-custom`, read only). All harness projects live outside the repo in the session scratchpad (`...\scratchpad\harness*`), LangVersion 9.0:
- `UnityStub` = assembly `UnityEngine` with only `Debug`, `PlayerPrefs`, `Application.dataPath`, `UnityWebRequest` family (the real DLLs cannot execute outside the engine);
- `Opus.Sdk.Runtime` = the real `Runtime/**/*.cs`; `PhantomHand.Runtime` = real `Runtime/*.cs`; `PhantomHand.Presentation` = real `StrokeDriver.cs` + a 12-line stub `BrushRig`;
- `Harness.Tests` = the real SDK `Tests/Editor/*.cs` + PhantomHand `PhaseAndParamsTests/ModuleTests/StrokeSchedulerTests/ProbeAndAnalyzerTests` (the other PhantomHand editor tests need UnityEngine.UI / GameObjects).

```
dotnet build -c Debug -v q        -> Build succeeded, 0 Warning(s), 0 Error(s)
dotnet bin\Debug\net10.0\Harness.Tests.dll [-v] [filter]
```
1. Baseline, ORIGINAL production code, before any edit: `DONE passed=208 failed=0 skipped=0 total=208` (SDK editor tests 123 + PhantomHand pure-logic tests 85).
2. FINAL code: `DONE passed=243 failed=0 skipped=0 total=243` (208 + 35 new: 29 SDK + 6 driver). Six consecutive runs, all green: 12.4 s, 10.2, 6.5, 9.4, 12.7, 12.6 (no flakiness seen). Slowest new test 0.7 s (the 700 ms receive timeout in the `Disable` test).
3. **Red check** - the same 35 new tests against the ORIGINAL files from git HEAD (compile-only shims added for the new API surface, old behaviour kept): `DONE passed=227 failed=16 skipped=0 total=243`. Failed on old code:
```
StrokeDriverTests.PauseThenResume_...  / TwoPauses_... / AsyncStrokeCaughtByAShortPause_... / Begin_Twice_...
HapticStrokeTests.Ack_EveryDialect_DeliveredAndReason (",\"accepted\":false" counted as delivered)
HapticStrokeTests.Ack_WithOddlyTypedFields_NeverThrows_... (old code throws)
HapticStrokeTests.Keepalive_AlsoSends... / Keepalive_StopsForALostNode... / Keepalive_NotSentWhileNoDeviceIsKnown...
HapticStrokeTests.Display_CarriesTheSameLineInTextAndMode (no `mode`)
LiveClientHubPortTests.ExplicitHubPort_IsKept... / CustomHubPort_ReachesTheSocket (client never connected to the custom port)
SleeveSensorClientTests.Keepalive_AlsoSends... / Keepalive_StopsWhenTheNodeIsLost...
UdpConnResetTests.Transport_KeepsReceiving_AfterSendsToAClosedPort (5166 ms, "the receive loop must survive WSAECONNRESET")
UdpConnResetTests.Disable_... (fails only because the shim's Disable returns false; not meaningful)
```
   The other 19 new tests pass on the old code, as expected (they pin existing behaviour or guard against over-doing): the 5 node-matching tests and the 3 real-firmware-shape tests (`RealFirmware_NodeA/NodeB...`, `Imu_FromTheRealNodeA...`); `Ack_RealFirmwareShape_AcceptedTrue...`, `Ack_V1Dialect...`, `Ack_ForACueNobodySent...`; `Keepalive_PumpOnlyEnqueues...`, `SleeveSensorClientTests.Keepalive_NotSentWhileNoDeviceIsKnown`; `Stroke_ScheduledAgainAfterStop...`; `ResumeWithoutAStop_QueuesNothingTwice`, `AfterTheInductionEnded_AResumeSchedulesNothing`; `IsConnReset_...`; `DefaultHubPort_Is8787`, `Urls_CarryThePort`.
4. **Mutations** on a copy of the final code: ioctl switched off (`Disable` returns false) AND the driver's refused-stroke retry removed -> `DONE passed=241 failed=2` (`AsyncStrokeCaughtByAShortPause...` fails = the retry is load-bearing; `Disable_...` fails = expected). `Transport_KeepsReceiving_...` still passes, i.e. the exception handler alone also survives 10054 when the ioctl is unavailable.
5. **Compile against Unity's real managed DLLs** (compile only, never executed): SDK runtime, SDK editor tests, PhantomHand.Runtime (root + Scene + Ui), the real `Presentation/*.cs` (real `BrushRig`, `StrokeDriver`, `ArmThreatPresenter`), PhantomHand editor tests; references = `Editor/Data/Managed/UnityEngine/UnityEngine.*Module.dll` + Unity's `nunit.framework.dll` + the package Newtonsoft; both net10.0 and netstandard2.1 reference sets:
```
harness_real\RealPhantomTests  -> 0 Error(s) | 0 Warning(s) | Build succeeded.
harness_real\RealSdkTests      -> 0 Error(s) | 0 Warning(s) | Build succeeded.
harness_ns21\RealPhantomTests  -> 0 Error(s) | 0 Warning(s) | Build succeeded.
harness_ns21\RealSdkTests      -> 0 Error(s) | 0 Warning(s) | Build succeeded.
```
6. `git status --short`: 12 `M` files (listed below) + this log; no untracked source file.

Changed files: `game/Packages/com.opus.sdk/Runtime/Transport/{HapticClient,SleeveSensorClient,UdpHapticTransport,DiscoveryHub,LiveClient}.cs`; `game/Packages/com.opus.sdk/Tests/Editor/{HapticStrokeTests,SleeveSensorClientTests,DiscoveryTests,LiveMessageTests,SensorRecorderTests}.cs`; `game/Assets/Games/PhantomHand/Runtime/Presentation/StrokeDriver.cs`; `game/Assets/Games/PhantomHand/Tests/Editor/StrokeSchedulerTests.cs`. Line endings stay CRLF (verified, no mixed endings, no BOM added).

**Not proven by anything above:** behaviour on Unity's Mono/IL2CPP (`Socket.IOControl` with the vendor ioctl, `ClientWebSocket`, `UdpClient.ReceiveAsync` fault shape), the Unity compiler itself (Roslyn version), PlayMode (`PH_FullRun`, the Orchard totals), the real BrushRig animation feeding the driver, and the real firmware over Wi-Fi.

## CONTRACT REQUESTS (Opus owns contracts/**; the SDK already works before and after each change)
C1. `contracts/schemas/haptic-message.schema.json` `$defs.deviceAck`: the real firmware ack has no `status`, so it fails the schema today. Change `required` to `["type","cue_id","timestamp_ms"]`, add `"anyOf":[{"required":["status"]},{"required":["accepted"]}]`, add properties `"accepted":{"type":"boolean"}` and `"device_id":{"type":"string"}`. Valid fixtures to add:
```json
{"type":"ack","device_id":"CHETNA_HAPTIC_001","cue_id":"stroke_17","accepted":true,"timestamp_ms":123500}
{"type":"ack","device_id":"CHETNA_HAPTIC_001","cue_id":"stroke_18","accepted":false,"timestamp_ms":123600}
```
   and HAPTIC_PROTOCOL.md ack row: "`accepted` (bool) is the third dialect; true = executed".
C2. `keepalive` is in no schema family. Add `{ "$ref": "#/$defs/keepalive" }` to `$defs.wire.anyOf` and `"keepalive":{"description":"Game -> Node A or B, once per second per client; feeds the 2 s watchdog and registers the sender for telemetry.","type":"object","required":["type"],"properties":{"type":{"const":"keepalive"}}}`. Fixture: `{"type":"keepalive"}`. HAPTIC_PROTOCOL.md v1.2 table: new row `keepalive | game -> node A/B | bare {"type":"keepalive"} each second per client, never to a node that went silent`.
C3. `display`: the firmware reads `mode`, the fake reads `text`; the SDK sends both. Schema `$defs.display`: `"required":["type"]`, `"anyOf":[{"required":["text"]},{"required":["mode"]}]`, add `mode` with the same `maxLength 12` + printable-ASCII pattern. Fixture: `{"type":"display","text":"SYNC","mode":"SYNC"}`; also the firmware's own `{"type":"display","mode":"SYNC"}`.
C4. Docs only: 03-SPEC D7 / HAPTIC_PROTOCOL v1.2 "Device ids": Node A id is not a contract (real hardware `CHETNA_HAPTIC_001`, fixtures `SLEEVE_001`); consumers match `device_kind`. The SDK does.

## CROSS-TRACK REQUESTS (files I may not touch)
X1. `sim/sleeve/twin.py`: a `{"type":"keepalive"}` datagram falls into the final `else` of `NodeA.on_message` (~line 360) and `NodeB.on_message` (~line 543) and bumps `stats["rx_invalid"]` + logs `unknown_type` once or twice per second. Add before the `else`: `elif typ == "keepalive": pass  # on_datagram already refreshed last_rx; no ack, not invalid`. `sim/haptic/fake_haptic.py` likewise if it counts unknown types. Add a `test_twin.py` case that a keepalive leaves `rx_invalid` at 0.
X2. `sim/sleeve/twin.py` `handle_display` (~line 365): `text = msg.get("text", msg.get("mode"))` so the real firmware's `mode`-only form also drives the fake.
X3. Twin/fake default Node A id -> `CHETNA_HAPTIC_001` (handoff row 1); keep `SLEEVE_001` selectable. No SDK change needed.
X4. Shell wiring for item 5 (the SDK now accepts the port; nothing passes it yet): `game/Assets/Shell/Runtime/OpusSessionRunner.cs` line ~183 `new LiveClient(..., manualHost: host, hubPort: port)` with `port` from a new `int HubPortHint` on `ISessionHost` (PhantomHandSceneController returns `_ep.HubPort`, 0 = default 8787; Orchard returns 0); the localhost probe at line ~159 should use the same port; delete the warning at `PhantomHandSceneController.cs` lines 136-137; `BootstrapLoader.cs:53` likewise; `PhantomHandFullRunTests` `HubPortRequired` assertion can then be relaxed.
X5. Shell, headset removed mid-induction: `PhantomHandSceneController.OnApplicationPause(true)` -> `StopSleeveSafely` -> `HapticClient.Stop` cancels the queued strokes WITHOUT a module pause, so no `resume` event follows and the driver (rightly, it cannot tell this from `abort_phase`) does not re-schedule. If strokes should survive a doff/don, route `OnApplicationPause` through `PauseSession`/`ResumeSession`.
X6. A PlayMode twin of the driver tests with the real `BrushRig` (`PhantomHandPresentationTests.Fx`: pause at 10 s of an induction, resume after 3 s, assert the cues after resume and that stroke events keep coming). Not written (EditMode only, per the brief).

## Open issues / decisions I made that you may want to flip
1. **Node A receives two keepalives per second in the Phantom Hand composition**: `PhantomHandSceneController` runs `HapticClient.StartKeepalive` AND a `SleeveSensorClient` over the same Node A transport (they already double ping+subscribe the same way). I put the keepalive in both so each client is self-sufficient; harmless to a 2 s watchdog. For exactly 1/s per node, drop the datagram from one of the two (one line each: `PumpKeepalive` in `HapticClient.cs`, `Pump` in `SleeveSensorClient.cs`). Node B gets 1/s.
2. "Stop when the node is lost" is implemented as: heard at least once, then silent more than 3 s, keepalive skipped (ping+subscribe continue), resumes on the next datagram. A node never heard from still gets keepalives, because the handoff says a node only streams to whoever last sent it a packet. If you want "no keepalive until first contact", gate on `Connected`; I judged that a deadlock risk.
3. `stop` message: `HapticClient.SendStop` still sends `{"v":1,"type":"stop","id":...,"ts_ms":...,"zone":"all"}`. The handoff's open request 10 (does the firmware ignore the extra fields?) is unanswered; the real firmware documents the bare `{"type":"stop"}`. This is a safety message, so I did not change it blind. If the firmware is strict, add a bare-stop datagram.
4. `HapticClient.DeviceId` is only set from `status` messages; the real firmware may send none (its acks carry `device_id`). It only feeds the Orchard live-status field; not changed.
5. Node IP change after a power cycle: the transport latches the first beacon forever (existing behaviour), so a node that returns on a new DHCP address is never re-found. Joint-test item 6 ("rediscovered within 2 s") needs re-discovery; not in these seven items.
6. Pre-existing and left alone: an exception thrown by an `OnMessage` handler on the receive thread ends that loop (only socket errors were in scope). LiveClient's hub beacon (port 8788) advertises the hub's port but the SDK ignores it; `hubPort` is explicit only.
7. Electronics-team requests from the handoff section B (timestamp of the first chunk sample, extra-field tolerance, status/emg_burst examples, subscribers, node_probe output) are theirs to answer; the SDK is tolerant of the answers either way (it already treats `timestamp_ms` as the first sample and ignores unknown fields).

## Firmware dialect points I am unsure about (for the electronics team)
- Does the real firmware feed its watchdog from `keepalive` only, or also from `ping`/`subscribe`? The SDK sends all three.
- `display.mode`: arbitrary text or a fixed list? The SDK only sends `SYNC`, `ASYNC`, `IDLE` (and whatever a caller passes, 12 printable ASCII chars).
- Empty display line: the contract fake shows IDLE for empty `text`; for an empty `mode` the SDK sends `""` as is.
- Acks: the SDK now needs nothing but `cue_id` + (`accepted` | `status` | `ok`).

## Next step
1. Opus/test agent: in the editor run EditMode for assemblies `Opus.Sdk.Tests.Editor` (expect 123 existing + 29 new = 152) and `PhantomHand.Tests.Editor` (+6 in `StrokeDriverTests`); check the totals do not drop (Orchard >= 107 EditMode / >= 6 PlayMode, app, analytics unaffected by this diff). Then `PH_FullRun`.
2. Apply C1-C4 and X1-X5 (small), then re-run the twin tests and the L3 fault runs (node A off mid-run exercises keepalive loss + WSAECONNRESET + pause/resume).
3. Wi-Fi bring-up with the real nodes: `python tools/demo/node_probe.py <ip> 8790`, and watch that Node A receives about 2 keepalives/s (decision 1).
