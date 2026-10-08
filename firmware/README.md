# Firmware

## Node A — haptic sleeve (`firmware/opus_sleeve/`, v0.5.0)

ESP-WROOM-32, 2 coin motors (A = GPIO25 near the wrist, B = GPIO26, 10 cm toward the elbow), MPU6050 (0x68) and
SSD1306 OLED (0x3C) on I2C SDA 21 / SCL 22. Device id `SLEEVE_001`. Wiring: `docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md` §3.
**Status: written but UNCOMPILED and untested on hardware** (no arduino-cli on the authoring machine, see
`logs/sessions/2026-10-07-PH-F-F1-run1.md`). Compile it before flashing.

### Flash (human, bank cable unplugged)
1. Arduino IDE 2: Boards Manager, install "esp32 by Espressif" 3.x; board "ESP32 Dev Module".
2. Library Manager: ArduinoJson (v7), Adafruit MPU6050, Adafruit Unified Sensor, Adafruit SSD1306, Adafruit GFX Library.
3. Open `firmware/opus_sleeve/opus_sleeve.ino`. Type the hotspot name and password into `WIFI_SSID` / `WIFI_PASS`
   at the top (2.4 GHz only). **Do not commit that edit; undo it after uploading.**
4. Flash with the power-bank cable unplugged (USB from the laptop only while flashing). No upload? Hold BOOT while
   the IDE says "Connecting...", or use the CH9102 module.
5. Serial Monitor 115200: boot prints IP, MAC, `[IMU] ... found`, `[OLED] ... found at 0x3C`. Unplug USB, run from the bank.
CLI equivalent: `arduino-cli compile --fqbn esp32:esp32:esp32 firmware/opus_sleeve` (needs the esp32 core and the five libraries).

### Behaviour in one table
| Item | Value |
|---|---|
| Intensity cap | 150/255 (3 V motors on 5 V). Pulses 50-400 ms, 100 ms gap per motor, 50 % duty per 10 s per motor |
| Watchdog | no command or keepalive (ping, subscribe, display, ...) for 2000 ms while a motor is on: all outputs 0 |
| Subscribers | up to 3, expire after 5 s. Send `{"type":"subscribe"}` every <= 2 s (any valid command or ping also refreshes). sensor_data (100 Hz) and status (1 Hz) go to all; acks only to the sender |
| OLED | line 1 id + fw, line 2 IP or `NO WIFI`, line 3 text from `{"type":"display","text":"SYNC"}` (<= 12 chars, default `IDLE`), line 4 `LINK n` or `NO LINK` |
| Discovery | 1 Hz broadcast on UDP 8791: PRD 9.1 form plus the legacy `opus_haptic` hello. Commands on UDP 8790 |
| Messages | `cue:"stroke"` is accepted, `play_at_ms` is ignored, unknown types are ignored and counted (`unknown_msgs` in status) |

### Bench commands (Serial 115200, line ending "Newline" or "Both")
- `selftest`: pulses A then B, 3 times, intensity 150, 200 ms; prints whether the IMU and OLED were found. Also runs
  if you press BOOT during the 2 s window right after reset ("[BOOT] press BOOT ..."). Do not hold BOOT through the reset itself.
- `soa <ms>` (60..300): 10 strokes, one per second, A then B after `<ms>`. Try `soa 60`, `100`, `150`, `200`, `300`; the
  wearer picks the value where A to B feels like ONE stroke toward the elbow. That number is `motor_soa_ms` in the manifest.
- `scan`: I2C scan. Expect `0x3C` and `0x68`.
- `help`.

### Check it from the laptop
Send `{"type":"subscribe"}` to the node's UDP 8790 from a script and watch `sensor_data` arrive at ~100 Hz.

