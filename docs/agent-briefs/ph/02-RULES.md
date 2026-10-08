# Rules — every agent reads this before working

These add to `CLAUDE.md` (which still applies in full). If they conflict, CLAUDE.md wins and the agent writes
a "RULE CONFLICT" note in its log.

## 1. Global

1. **Explore → Plan → Code → Verify.** Read the files you will change before changing them. Write a short plan
   in the log before coding.
2. **Real output only.** Every claim ("tests pass", "builds", "validates", "latency 12 ms") has the real
   command and its real output pasted in the log. Never claim an APK or hardware works unless a human ran it.
3. **Stay in your lane.** Write only files your track owns (`01-ORCHESTRATION.md` §4). Need something else?
   Write a CONTRACT REQUEST / CROSS-TRACK REQUEST in the log.
4. **No commits, tags or pushes** (Opus only). No force-anything. No deleting other people's files.
5. **No secrets** in files, logs, firmware or screenshots (Wi-Fi passwords are typed by a human at flash time).
6. **UTF-8.** Docs use ★ — ≤ µ; never rewrite them with PowerShell Get-Content/Set-Content without
   `-Encoding UTF8`. Prefer the Edit tool or Python with `encoding='utf-8'`.
7. **Never end your turn while work remains**, and never wait in the background. Long commands run in the
   foreground with a timeout.
8. **Checkpoint before stopping**: `logs/sessions/<YYYY-MM-DD>-PH-<track>-<prompt>-run<N>.md` with: goal,
   plan, files changed, commands + real output, results vs acceptance, open issues, CONTRACT/SPEC requests,
   next step. A replacement agent must be able to resume from that log alone.
9. **Spec is law.** Names, numbers, event types, ports and params come from `03-SPEC.md`. Don't invent new
   ones; ask.
10. **Determinism.** Anything random takes a seed (default from session id). Tests use fixed seeds.

## 2. Testing honesty

- A test that waits on a network peer waits for readiness explicitly (discovery latched, socket bound), never
  on a fixed sleep. (Run 11 lesson: a green run was luck.)
- Never judge an event by sampling state one frame later (the basket "near-miss" lesson).
- Contracts check shape, not meaning: every pipeline test also asserts meaning (events in order, metrics
  non-null, values in plausible ranges). This is how the five silent defects of 19 Sep were found.
- No skipped or commented-out tests to get green. A flaky test is a bug; log it with its failure rate.
- Regression rule: OrchardReach (≥ 107 EditMode, ≥ 6 PlayMode), app (≥ 157), analytics (≥ 49), sim
  suites must not drop in count or go red.

## 3. Per-track rules

**Unity (U)**
- Unity 6000.4.6f1, URP only. Batch mode only, GUI editor closed, Meta AI Agent Bridge disabled
  (EditorPref `Meta.XR.SDK.AI Agent Bridge.Enabled` = 0), Unity Hub running. One Unity agent at a time.
- Command: `"C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe" -batchmode -projectPath "<repo>\game" -executeMethod <Class.Method> -logFile <path> -quit`;
  tests: `-runTests -testPlatform EditMode|PlayMode -testResults <xml>` (no `-quit`). Check exit code and
  grep the log for `error CS` after every run.
- Take the Unity lock (`game/.ph_unity.lock`, 01-ORCHESTRATION.md §5) before every batch run; release it after.
- Target device is Quest 3 (PRD); OVRProjectConfig must list Quest 3 and keep Quest 3S/2 off unless Opus says so.
- Scenes and assets are created by editor scripts (reproducible), not by hand.
- Physics bodies move by forces / `Rigidbody.MovePosition`, never `Transform.position`. Never
  `OVRInput.Get()` for grabs. IL2CPP + ARM64 + Vulkan, 72 Hz, MSAA 4×, HDR off, FFR high,
  < 100 draw calls per eye (target < 60 here). Run the Meta Project Setup Tool after any Meta package change.
- Hand data: raw ISDK `IHand` (before HandFilter) via `MetaHandSource`; HT frequency LOW, FMM off;
  record on `CurrentDataVersion` change; measured rate goes into `session.json`.
- Game logic classes have no MonoBehaviour/OVR dependency (testable in EditMode), like OrchardReach.
- Never block the main thread on network I/O; all transports are fire-and-forget.

**App (A)**
- Flutter at `H:\flutter\bin` on the current dev PC (old PC: `C:\flutter\bin`); not on PATH, call it by full path. GoRouter inside a Riverpod provider; each feature exports its RouteBase list.
- Zero game-specific widgets for parameters (dynamic form from manifest `paramSchema`). Game-specific
  *results* views are allowed only inside the session report's metric sections, keyed by metric names.
- Goldens at 390×844 / 1280×800 / 1600×1000, light/dark, text scale 1.0/2.0; EN + HI strings for every
  new label; WCAG 2.2 AA contrast.
- Hub tests on port 8797 (8787 is reserved for Unity). `flutter analyze` 0 errors / 0 warnings.

**Analytics (N)**
- Always propagate `rate_hz` and `quality`; every metric carries a quality flag. Target < 5 % error vs
  synthetic ground truth for kinematic metrics, < 10 % for embodiment metrics (drift, flinch latency).
- EMG windows overlapping a motor-on interval (from `haptic_cue` events + 50 ms) are excluded and counted.

**Sim (S)**
- Speaks the exact wire formats in `contracts/`. Supports `--port-offset` so agents can run in parallel.
- Every fake has fault injection: drop, delay, jitter, malformed packet, node power-off mid-run.

**Firmware (F)**
- ESP-WROOM-32 dev boards, Arduino-ESP32 core 3.x, board "ESP32 Dev Module". ArduinoJson v7, Adafruit MPU6050,
  Adafruit Unified Sensor, Adafruit SSD1306 + GFX. Pins only as in `03-SPEC.md` §3 / PRD §8.1; never GPIO 0, 2,
  12, 15 (boot straps) or 6–11 (flash) for anything new.
- Node A extends v0.4.0 in place (no rewrite): its safety code is proven. Keep BOTH message dialects (see the
  v0.4.0 header). Unknown message types are ignored with a counter, never treated as a motor command.
- Non-blocking loop only (no `delay()` in the main loop beyond 1 ms); sampling uses hardware timers.
- `arduino-cli compile --fqbn esp32:esp32:esp32` must pass with 0 warnings in our files before any human flashes.
- No Wi-Fi credentials in files: placeholders only, typed by a human before flashing, edit undone after.

**Docs (H)**
- Haiku only rewrites format, never facts. Every number it writes is copied from a log with the log's path.
  If two logs disagree, it lists both and flags Opus.

## 4. Safety (non-negotiable, every track)

1. **EMG isolation (PRD §11):** Node B runs from the power bank only. It never touches a laptop, charger or
   mains-powered anything while electrodes are on a person, and the bank is never charging while a node is
   attached. Flash with the bank cable unplugged and electrodes off. Firmware prints the warning at boot; the
   runbook and the app repeat it.
2. **Haptic limits** (enforced in BOTH software and firmware): pulse 50–400 ms (stroke 200 ms); firmware
   100 ms minimum gap per motor; software ≥ 250 ms between stroke sends per motor and ≤ 4 stroke sends/s;
   intensity ≤ 150/255 (3 V motors on a 5 V rail) and ≤ 150 × `haptic_max_intensity`; duty ≤ 50 % per 10 s per
   motor; watchdog: all motors 0 after 2000 ms without a command or keepalive.
3. **Power (PRD §8.4):** one board per bank port; never feed a board from the bank and laptop USB at once;
   never short or reverse a supply lead; bank stays on the table, never on the forearm; 100 µF on each 5 V rail.
4. **Participants:** consent line read before every run; stop on any discomfort; no participants with
   pacemakers or skin conditions under electrodes; disposable electrodes only; threat is a harmless stone
   (no blood, no blades).
5. **Privacy:** no names in sessions; `patient_ref` only; no PII ever reaches the sleeve.
