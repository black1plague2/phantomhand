# Electronics team — build guide (PRD v2: 2 × ESP-WROOM-32 + power bank)

Two boards on one power bank, talking to the Quest over Wi-Fi. The software side is tested against a
simulator of your boards first, so your hardware drops in when it's switched on. Firmware: Node A =
`firmware/opus_sleeve/` upgraded to v0.5.0 (F1), Node B = `firmware/bio_node/` v0.5.0 (F2). Agents write the
code; you flash and test.

## 1. The two nodes

| | Node A — haptic | Node B — bio |
|---|---|---|
| Board | ESP-WROOM-32 dev board #1 (micro-USB) | ESP-WROOM-32 dev board #2 (micro-USB) |
| Does | 2 ERM motors, MPU6050, OLED (SYNC/ASYNC + link) | BioAmp EXG Pill → muscle envelope (EMG) |
| Worn | Motors + IMU on the right forearm; board on the table or elbow strap | Electrodes on the forearm; board on the table |
| Power | Bank USB-A port 1 → micro-USB | Bank USB-A port 2 → micro-USB |
| Id | SLEEVE_001 | CHETNA_BIO_001 |
| Firmware | 0.5.0 | 0.5.0 |

## 2. Parts (PRD §7)

| Part | Use |
|---|---|
| 2 × ESP-WROOM-32 | Node A, Node B |
| MPU6050, 0.96" SSD1306 OLED | Node A, I2C |
| BioAmp EXG Pill + cable + gel electrodes | Node B |
| 2 × ERM 3 V coin motors (+1 spare) | Motor A, Motor B |
| 2 × 2N2222A, 2 × 1N4007, 2 × 1.5 kΩ (+3 spare for parallel pairs) | motor drivers |
| 4 × 100 µF | 2 motor channels + 2 board 5 V rails |
| 2 breadboards, jumpers | one per node |
| URBN 20,000 mAh bank (2 USB-A), 2 × USB-A→micro-USB (one with data) | power, flashing |
| Grove EMG (backup), CH9102 USB-TTL (flash fallback) | return once not needed |

## 3. Node A wiring

| Signal | Pin |
|---|---|
| Motor A drive | GPIO25 → 1.5 kΩ → 2N2222A base |
| Motor B drive | GPIO26 → 1.5 kΩ → 2N2222A base |
| I2C SDA (MPU6050 0x68 + OLED 0x3C) | GPIO21 |
| I2C SCL | GPIO22 |
| MPU6050 / OLED VCC | 3V3 |

Per motor channel:
```
GPIO25 (or 26) ──1.5 kΩ──► 2N2222A base
emitter ──► GND
collector ──► motor (−)
motor (+) ──► board 5 V (VIN) rail
1N4007 across the motor: stripe (cathode) on motor (+), other end on the collector
100 µF across 5 V / GND next to the transistor (+ to 5 V)
```
Plus one 100 µF across the board's 5 V and GND near the USB input (stops Wi-Fi brownouts).
Motor weak or transistor warm → second 1.5 kΩ in parallel on that base (~750 Ω).
Firmware caps PWM at 150/255 (3 V motors on 5 V) — don't raise it.
Never use GPIO 0, 2, 12, 15 or 6–11. Common ground everywhere.

**Placement:** back (dorsal) of the right forearm. Motor A **5 cm** from the wrist crease, Motor B **10 cm
further toward the elbow** (15 cm from the crease). Flat, facing the skin, held by sleeve fabric or tape on the
sleeve (not glued to skin). MPU6050 flat between them. Wrist and hand completely clear. Wires run toward the
elbow; boards and the bank stay on the table.

## 4. Node B wiring

| BioAmp EXG Pill | ESP-WROOM-32 |
|---|---|
| VCC | 3V3 (so OUT can't exceed the ADC range) |
| GND | GND |
| OUT | GPIO34 (ADC1, input-only) |

Electrodes: clean skin with an alcohol wipe. IN+ and IN− ~3 cm apart over the forearm flexor belly (inner
forearm, the muscle that bulges when you squeeze, about a third of the way from elbow to wrist). REF on the bony
point of the elbow. Route the electrode cable away from motor wires. Grove EMG is the swap-in if the BioAmp is
too noisy.

## 5. SAFETY — read before Node B touches anyone

- **Node B runs from the power bank only. Never connect it to a laptop, charger or anything plugged into mains
  while electrodes are on a person. Never charge the bank while a node is attached.**
- Flash with the bank cable unplugged, and flash Node B only with electrodes off.
- Never feed a board from the bank and laptop USB at the same time. Never short or reverse supply leads.
- Motors only through the transistor driver, never straight from a GPIO.
- Disposable electrodes, no broken skin, no pacemakers/implants; stop on any discomfort.

## 6. Bring-up tonight (PRD §14, Oct 7) — tick each in §9

1. **Flash both boards** (Arduino IDE 2, Boards Manager "esp32 by Espressif" 3.x, board "ESP32 Dev Module";
   libraries ArduinoJson 7, Adafruit MPU6050, Adafruit Unified Sensor, Adafruit SSD1306, Adafruit GFX). Type
   the hotspot name/password at the top of the .ino, upload, then undo that edit. Bank unplugged while flashing.
   No upload? Hold BOOT while it says "Connecting…", or use the CH9102 module.
2. **Wi-Fi:** Serial (115200) prints the IP; OLED shows id + IP (Node A).
3. **I2C scan:** boot printout shows IMU found (0x68) and OLED found (0x3C).
4. **Motors:** self-test (hold BOOT at reset) pulses A then B 3×. Both clearly felt? If weak → parallel resistor.
5. **Inter-motor delay (`motor_soa_ms`):** with the self-test's SOA mode (Serial command `soa 60`, `soa 100`,
   `soa 150`, `soa 200`, `soa 300`), the wearer picks the delay where A→B feels like ONE stroke moving toward the
   elbow. Write it down; it goes into the manifest.
6. **Spin-up:** with the software team, `python tools/demo/run_pipeline.py --game phantom_hand --hardware
   --spinup` reports the motor start delay (expect 20–40 ms).
7. **BioAmp at 3.3 V (electrodes on YOU, Node B on the bank, laptop NOT connected):** `python tools/demo/live_plot.py`
   on the laptop (it receives over Wi-Fi); squeeze → envelope ≥ 3× baseline. Then repeat with Node A pulsing next
   to it; describe any spikes.
8. **Power bank auto-off:** both boards running 30 min from the bank; it must not switch off.
9. **Hand tracking with the sleeve on:** in the headset, hands stay tracked (software team checks).

**G1 gate:** both nodes discovered over Wi-Fi, EMG trace usable.

## 7. Oct 8 — integrate

Flash firmware 0.5.0 (F1/F2 builds) → joint test (§8) → full run → tune `tactile_lead_ms` with the software team
(20 SYNC strokes at 0/20/40/60/80 ms; wearer picks "most simultaneous").

## 8. Joint test with the software team (~30 min)

1. App/laptop sees SLEEVE_001 (haptic) and CHETNA_BIO_001 (bio).
2. Phase change shows SYNC/ASYNC on the OLED.
3. 20 SYNC strokes feel like A then B, one stroke toward the elbow, in time with the brush.
4. Harness: send→ack p95 < 100 ms.
5. Threat: EMG + IMU spikes visible live.
6. Unplug Node A mid-run → game shows "Sleeve offline" and continues; plug back → rediscovered.
7. Laptop live plot and the Quest receive sensor data at the same time (subscriber list working).

## 9. Sign-off sheet (photo it into logs/sessions/<date>-PH-HW-signoff.md)

| Check | Node A | Node B | By / time |
|---|---|---|---|
| Flashed v0.5.0, Serial OK | | | |
| Wi-Fi + discovered wirelessly | | | |
| IMU 0x68 + OLED 0x3C found | | n/a | |
| Both motors felt; parallel resistor needed? | | n/a | |
| motor_soa_ms chosen | | n/a | |
| Spin-up (ms) | | n/a | |
| EMG squeeze ≥ 3× baseline | n/a | | |
| EMG with motors running (describe) | | | |
| Bank stays on 30 min | | | |
| Placement A 5 cm / B 15 cm from crease | | n/a | |
| Joint test 1–7 passed | | | |
| Safety briefing (§5) done | | | |

## 10. Tell the software team

Chosen `motor_soa_ms`, measured spin-up, whether a parallel resistor was needed, BioAmp OK at 3.3 V or swapped to
Grove, EMG noise with motors, node IPs (for manual host if the hotspot blocks broadcast), photos of wiring and the
sleeve on an arm, and any change from this guide. All issued parts go back on 9 Oct per the CARF.
