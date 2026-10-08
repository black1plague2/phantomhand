# 2026-09-19 — Track E (Electronics / firmware) run 1

Brief: extend the sleeve firmware from 1 hardcoded motor to 4 independent channels against the newly
approved `motor: 0-3` schema, keep every safety limit and both wire dialects, resolve the motor-count
contradiction on paper, write human flashing instructions, and update the handoff doc.

Ownership for this run: `firmware/**`, `docs/ELECTRONICS_HANDOFF.md`, `docs/MANUAL_TODO.md` (append only),
this log. Not touched: `app/`, `game/`, `sim/`, `analytics/`, `tools/`, `contracts/`.

---

## CHECKPOINT 1 — explore (done)

Read, in order: `CLAUDE.md`, `docs/ELECTRONICS_HANDOFF.md` (full), `contracts/HAPTIC_PROTOCOL.md`,
`contracts/schemas/haptic-device-command.schema.json`, `firmware/opus_sleeve/opus_sleeve.ino` (v0.3.0),
`logs/sessions/2026-09-19-OPUS-sleeve-check.md`.

Confirmed from the files themselves:

- Schema is already extended — `"motor": {"type":"integer","enum":[0,1,2,3]}` with the note
  "Extended from [0,1] on 2026-09-19 by user decision." So the firmware is now the lagging side.
- Firmware v0.3.0 has `MOTOR_COUNT 1`, `ROUTE_MOTOR1_TO_MOTOR0 1`, a single `Motor m0` global, a single
  `MOTOR0_PIN = 25`, and rejects anything that is not motor 0/1 with `INVALID_MOTOR`.
- `status` emits `motor_0` + `motor_1` only.
- Dual dialect confirmed in code: ack carries `cue_id`+`ack_id` and `status`+`ok` (`sendAck`), status carries
  nested `motor_N` plus flat `motors_ok`/`imu_ok`/`fw` (`sendStatus`), commands accepted with or without
  `{"type":"haptic"}` (`handleMessage`, `msg["type"].isNull()` branch).
- `buzz` is implemented at line 121 (25 ms on/off). It stays.

### Toolchain reality check (done before writing any code)

```
$ which arduino-cli
which: no arduino-cli in (...)        # not on PATH
$ arduino-cli version
bash: arduino-cli: command not found

$ ls "/c/Users/GARV BANSAL/AppData/Local/Arduino15/packages"
arduino/
builtin/                               # <- NO esp32/ package: the ESP32 core is not installed

$ ls "/c/Users/GARV BANSAL/AppData/Local/Arduino15/libraries"
Arduino_BuiltIn/ Ethernet/ Firmata/ Keyboard/ LiquidCrystal/ Mouse/ SD/ Servo/ Stepper/ TFT/
                                       # <- NO ArduinoJson, NO Adafruit MPU6050

$ ls -la "/c/Users/GARV BANSAL/AppData/Local/Programs/Arduino IDE"
total 8
drwxr-xr-x ... ./
drwxr-xr-x ... ../                     # <- empty: Arduino IDE app dir is a leftover, not an install
$ ls "/c/Program Files/Arduino IDE"
ls: cannot access '/c/Program Files/Arduino IDE': No such file or directory
```

**Conclusion, stated plainly: this machine cannot compile the sketch.** No `arduino-cli`, no ESP32 board
package, no ArduinoJson, no Adafruit MPU6050, no working Arduino IDE install. Whoever flashed v0.3.0 today
did it from another machine or from an install that has since been removed. Therefore **nothing in this run
claims the firmware compiles on the ESP32 toolchain**, and nothing claims it runs on hardware — the board at
10.179.145.125 is still on v0.3.0. Compilation and flashing are human steps; see `docs/MANUAL_TODO.md`.
