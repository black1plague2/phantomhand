# 2026-09-19 — Opus: real ESP32 sleeve UDP + buzz verification

Run on the user's request ("the first udp code is working? the buzzing part?") immediately after all four
track agents were stopped for manual pipeline testing. Real hardware, not the simulator.

## Reachability (changed since the 04:30 note in CONTEXT.md)
CONTEXT.md records that the PC could NOT reach the sleeve because they were on different subnets. That is no
longer true — the sleeve answers from the PC now, over a slower route:

```
ping 10.179.145.125  ->  Packets: Sent = 2, Received = 2, Lost = 0 (0% loss)
                         Minimum = 110ms, Maximum = 302ms, Average = 206ms
```

## First run — tools/demo/sleeve_test.py (2/4)
```
Command                      Result     Latency (ms)   Detail
device pulse (motor 0)       executed   47.0
device buzz (motor 0)        rejected   94.0           CUE_GAP
device ramp (motor 0)        executed   62.0
game-format cue (success)    rejected   47.0           CUE_GAP
2/4 commands accepted/executed
```

**This is a tool bug, not a firmware or protocol failure.** `sleeve_test.py` sends its four commands
back-to-back, faster than the firmware's `MIN_CUE_GAP_MS = 100` (per motor, start-to-start,
`opus_sleeve.ino:58`), so every second command is correctly rejected with `CUE_GAP`. The firmware is
enforcing the contract; the test tool is violating it. Note this also means the "4/4 cues executed" recorded
in CONTEXT.md was timing-dependent and could flip either way — it passed on luck.

## Second run — isolated, ≥ 1 s apart (3/3)
```
buzz (isolated)              executed       49 ms
pulse (1s later)             executed      172 ms
buzz on motor 1              executed       80 ms
```

## Verdict
- UDP command path to the real ESP32: **working**.
- `buzz` pattern: **working** (implemented at `opus_sleeve.ino:121`, 25 ms on/off).
- `pulse`, `ramp`: **working**.
- `motor: 1` is accepted and executed, but physically it is **motor 0 doing the vibrating** —
  `MOTOR_COUNT 1` and `ROUTE_MOTOR1_TO_MOTOR0 1` mean the forearm channel is not fitted and its cues are
  replayed on the upper-arm motor. See `docs/ELECTRONICS_HANDOFF.md` §3.3: the Electronics team's draft
  claims motor 1 is built, the firmware says it is not. Unresolved.
- Ack RTT 49–172 ms, higher than the 25–91 ms previously recorded, because of the slower route (see ping).
  G9 (< 100 ms trigger -> datagram SENT) is a send-side measure and is unaffected by this ack RTT.

## Follow-up owed (not done — the project is stopped for manual testing)
1. `tools/demo/sleeve_test.py` must space its commands >= `MIN_CUE_GAP_MS` (100 ms), or explicitly assert
   `CUE_GAP` as the expected result for the too-fast case. Right now it reports a green protocol as a
   2/4 failure, which is misleading.
2. Re-check whether this gap violation is the same root cause as the `sleeve_test.py` <-> `fake_haptic.py`
   dispatch mismatch that S run3 flagged and S run4 was mid-investigation on when stopped.
