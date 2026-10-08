// =====================================================================================================
// OPUS Haptic Sleeve firmware v0.5.0 — "Node A" of the Phantom Hand build — ESP-WROOM-32 (Arduino-ESP32 core 3.x)
//
// v0.5.0 (2026-10-07, Phantom Hand; extends v0.4.0 in place — every v0.4.0 safety behaviour is kept):
//   1. Two fitted motors: MOTOR_COUNT 2 (GPIO25 = A near the wrist, GPIO26 = B 10 cm toward the elbow).
//      MOTOR_CHANNELS stays 4 for addressing; cues for channels 2/3 still route to motor 0.
//      MAX_INTENSITY 150 (3 V coin motors on the 5 V rail, PRD v2 §8.2). The power-on buzz now obeys it too.
//   2. Watchdog (PRD FR-FW-04): no command OR keepalive (ping / subscribe / display / stop / config /
//      status_request) for WATCHDOG_MS = 2000 ms while any motor is active -> every output to 0. The per-motor
//      400 ms ceiling is unchanged. Unparseable and unknown messages do NOT feed the watchdog.
//   3. Subscribers (03-SPEC D3): up to 3 {ip, port, lastSeenMs}, expiry 5 s. {"type":"subscribe"} adds or
//      refreshes (and, when the table is full, evicts the least recently seen entry); any other valid message
//      also refreshes its sender (adds it if there is room). sensor_data / status go to every live subscriber;
//      acks go only to the sender of the command. Status reports `subscribers`.
//   4. MPU6050 sampled and sent at 100 Hz (SENSOR_PERIOD_MS 10): sensor_data v1 form, one sample per packet,
//      accel xyz + gyro xyz, NO temperature field, timestamp_ms per packet. DLPF 44 Hz (was 21 Hz: too slow for
//      100 Hz sampling).
//   5. SSD1306 OLED 128x64 at 0x3C on the shared I2C bus (21/22): line 1 "<device id> fw<version>", line 2 IP
//      or "NO WIFI", line 3 big text from the last {"type":"display","text"} (<= 12 chars, default "IDLE";
//      10 chars or fewer are drawn at text size 2, 11-12 chars at size 1 because size 2 is 12 px/char on a
//      128 px panel), line 4 "LINK n" (live subscribers) or "NO LINK". Refreshed at most 5 Hz, only when the
//      content changed, and not while a motor is on (a full-frame I2C flush takes ~25 ms). OLED absent -> the
//      firmware runs without it and says so at boot.
//   6. Discovery 1 Hz in the PRD §9.1 form (device_kind "haptic", motor_count 2, ...) AND the legacy
//      {"opus_haptic":1,"port":...} hello in the same packet. battery_pct stays null in status.
//   7. Bench tools (Serial 115200): `selftest` (A then B, 3x, intensity 150, 200 ms; prints IMU/OLED found),
//      `soa <ms>` (10 strokes, A then B after <ms>, one per second, to choose motor_soa_ms), `scan` (I2C scan),
//      `help`. selftest also runs when BOOT is pressed during the 2 s window right after reset (do not hold
//      BOOT through the reset itself: that enters the ROM downloader). All three are non-blocking.
//   8. Accepts cue:"stroke" (just a label) and ignores play_at_ms (the game times its sends). Unknown message
//      types are ignored and counted (`unknown_msgs` in status), never treated as a motor command.
//
// v0.4.0 (2026-09-19): FOUR independent motor channels (schema `motor` enum widened to 0-3 by user
//   decision on 2026-09-19; see contracts/schemas/haptic-device-command.schema.json and
//   docs/ELECTRONICS_HANDOFF.md §9). The single hardcoded motor-0 player of v0.3.0 is replaced by an array
//   of per-channel state: every channel has its own intensity, pattern, start/duration, cue-gap clock,
//   duty-cycle window and watchdog. Channel 0 is NOT special-cased anywhere except as the fallback target
//   for cues addressed to a channel that is not physically fitted (see MOTOR_COUNT below).
//
// Implements "OPUS Haptic Sleeve — Electronics Data & Communication Contract":
//   §1  UDP, JSON/UTF-8. Commands on 8790, discovery broadcast on 8791.
//   §2  Command  {"type":"haptic","cue_id","motor","intensity","duration_ms","pattern"}
//   §3  ACK      {"type":"ack","cue_id","status":"accepted|executed|rejected|error","timestamp_ms",...}
//   §4  Status   {"type":"status","device_id","battery_pct","connected","motor_0".."motor_3","timestamp_ms"}
//   §5  Discovery{"type":"device_discovery","device_id","device_name","firmware_version","protocol_version","ip",
//                 "command_port":8790,"status":"available"}
//   §7  Sensors  {"type":"sensor_data","device_id","timestamp_ms","sensors":{name:{value,unit,status}}} RAW units
//   §8  Every message carries timestamp_ms (device monotonic clock, millis()).
//   §9  Firmware enforces its own limits (clamp intensity/duration, min cue gap, duty cycle, watchdog).
//
// TWO DIALECTS, ON PURPOSE (docs/ELECTRONICS_HANDOFF.md §3.1 — do not "clean this up"):
//   The Unity HapticClient sends the command WITHOUT "type":"haptic" and reads "ack_id"/"ok",
//   "opus_haptic":1/"port", "motors_ok"/"imu_ok"; the Electronics contract uses "cue_id"/"status",
//   "command_port" and nested "motor_N". This firmware emits BOTH and accepts BOTH command shapes, so the
//   same binary works with the game, the Flutter app, the simulator and the demo tools. Removing either
//   dialect breaks a shipped component.
//
// HARDWARE (Phantom Hand build, docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md §3) — one identical driver per channel:
//   GPIO -> 1.5 kOhm -> 2N2222A base; motor between the 5 V rail and the collector; 1N4007 across the motor
//   (cathode to 5 V); 100 uF across 5 V/GND local to each motor/transistor pair. LEDC PWM 2 kHz, 8-bit;
//   Arduino-ESP32 core 3.x allocates one LEDC channel per pin passed to ledcAttach().
//     motor 0 = A, near the wrist (upper_arm in the 4-channel schema): GPIO25  (fitted)
//     motor 1 = B, 10 cm toward the elbow (forearm)                  : GPIO26  (fitted)
//     motor 2 = TBD : GPIO27 (not fitted)      motor 3 = TBD : GPIO14 (not fitted)
//   None of these collide with I2C (21/22) or the boot-strapping pins.
//   I2C (SDA 21, SCL 22, 3V3, 400 kHz): MPU6050 at 0x68 and SSD1306 OLED at 0x3C. Both optional.
//   Power: power bank over micro-USB (no battery sense wired -> battery_pct null). Never power the board from
//   the bank and laptop USB at the same time.
//
// LIBRARIES: ArduinoJson v7 (Benoit Blanchon), Adafruit MPU6050, Adafruit Unified Sensor, Adafruit SSD1306,
//            Adafruit GFX Library.
// BOARD:     Tools > Board > esp32 > "ESP32 Dev Module" (esp32 by Espressif, version 3.x).
// NETWORK:   ESP32 is 2.4 GHz only. It must be on the SAME Wi-Fi/hotspot as the PC (Quest Link) or the Quest.
// FLASHING:  firmware/README.md (Node A section) and docs/agent-briefs/ph/05-ELECTRONICS-TEAM.md §6.
// =====================================================================================================

#include <WiFi.h>
#include <WiFiUdp.h>
#include <ArduinoJson.h>
#include <Wire.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

// ---------------------------------- EDIT THESE ----------------------------------
// Type your network name and password here in the Arduino IDE, on your own machine, immediately before
// uploading. Do NOT commit real credentials to the repository. Undo the edit after flashing.
const char* WIFI_SSID = "YOUR_HOTSPOT_NAME";
const char* WIFI_PASS = "YOUR_HOTSPOT_PASSWORD";
// --------------------------------------------------------------------------------

// ============ §6 hardware values (the software team reads these back from Serial at boot) ============
#define DEVICE_ID            "SLEEVE_001"
#define DEVICE_NAME          "OPUS Haptic Sleeve"
#define DEVICE_KIND          "haptic"
#define FIRMWARE_VERSION     "0.5.0"
#define PROTOCOL_VERSION     "1.0"
#define COMMAND_PORT         8790
#define DISCOVERY_PORT       8791

// ---------------------------------------------------------------------------------------------------
// MOTOR_COUNT = how many channels are PHYSICALLY FITTED (driver soldered AND motor attached), counting
// from channel 0 upward. Addressing is always 0-3 (MOTOR_CHANNELS); fitting is 1..4.
//   2 = A (GPIO25) + B (GPIO26)   <- Phantom Hand
// Cues addressed to a channel >= MOTOR_COUNT are NOT hard-rejected: they fall back to motor 0 (see the
// ROUTE_MOTORn_TO_MOTOR0 switches).
// ---------------------------------------------------------------------------------------------------
#define MOTOR_COUNT          2          // fitted channels: A and B

#define MOTOR_CHANNELS       4          // addressable channels; matches schema enum [0,1,2,3]
#define MOTOR_0_LOCATION     "upper_arm"   // motor A (wrist side); name kept for the 4-channel schema
#define MOTOR_1_LOCATION     "forearm"     // motor B (elbow side)
#define MOTOR_2_LOCATION     "tbd_2"
#define MOTOR_3_LOCATION     "tbd_3"
#define MIN_INTENSITY        0
#define MAX_INTENSITY        150        // 3 V motors on the 5 V rail: ~59 % duty keeps the average near 3 V
#define MIN_DURATION_MS      50
#define MAX_DURATION_MS      400
#define MIN_CUE_GAP_MS       100        // per motor, start-to-start
#define MAX_CONTINUOUS_ON_MS 400        // == MAX_DURATION_MS; no command can hold a motor on longer
#define DUTY_WINDOW_MS       10000      // duty cycle measured over a rolling 10 s window, per motor
#define DUTY_CYCLE_LIMIT_PCT 50
#define WATCHDOG_MS          2000       // no command/keepalive for 2 s while vibrating -> every output to 0
#define TEMPERATURE_LIMIT_C  70.0f      // ESP32 die temperature (no motor thermistor fitted)
#define STATUS_PERIOD_MS     1000
#define SENSOR_PERIOD_MS     10         // 100 Hz sensor_data, one sample per packet
#define DISCOVERY_PERIOD_MS  1000
#define ENFORCE_CHIP_TEMPERATURE 0      // ESP32 die sensor is uncalibrated; reported in status, not enforced
// While a channel is not fitted, play its cues on motor 0 (1) or reject them with MOTOR_UNAVAILABLE (0).
#define ROUTE_MOTOR1_TO_MOTOR0 1
#define ROUTE_MOTOR2_TO_MOTOR0 1
#define ROUTE_MOTOR3_TO_MOTOR0 1
// Subscribers (03-SPEC D3)
#define MAX_SUBSCRIBERS      3
#define SUBSCRIBER_EXPIRY_MS 5000
// OLED
#define OLED_ADDR            0x3C
#define OLED_WIDTH           128
#define OLED_HEIGHT          64
#define OLED_PERIOD_MS       200        // <= 5 Hz
#define OLED_DEFER_MAX_MS    1000       // never defer a changed frame longer than this because motors are on
#define DISPLAY_TEXT_MAX     12
// Bench tools
#define BENCH_INTENSITY      150        // == MAX_INTENSITY
#define BENCH_DURATION_MS    200
#define BOOT_SELFTEST_WINDOW_MS 2000
#define BOOT_BUTTON_PIN      0          // read-only, after reset, as an input with pull-up; never driven
#define SOA_MIN_MS           60
#define SOA_MAX_MS           300
#define SOA_STROKES          10
// ======================================================================================================

// GPIO per channel. Index = motor number.
const int MOTOR_PINS[MOTOR_CHANNELS] = { 25, 26, 27, 14 };
const char* MOTOR_LOCATIONS[MOTOR_CHANNELS] = {
  MOTOR_0_LOCATION, MOTOR_1_LOCATION, MOTOR_2_LOCATION, MOTOR_3_LOCATION
};
const int PWM_FREQ = 2000;
const int PWM_RESOLUTION = 8;

WiFiUDP udp;
Adafruit_MPU6050 mpu;
// Pass clkAfter = 400 kHz so a display flush does not drop the shared bus (and the MPU6050) back to 100 kHz.
Adafruit_SSD1306 oled(OLED_WIDTH, OLED_HEIGHT, &Wire, -1, 400000UL, 400000UL);
bool imuOk = false;
bool oledOk = false;

// ------------------------------------ subscribers (03-SPEC D3) ------------------------------------
struct Subscriber {
  IPAddress ip;
  uint16_t port = 0;
  uint32_t lastSeenMs = 0;
  bool used = false;
};
Subscriber subs[MAX_SUBSCRIBERS];

// Context of the datagram being handled: acks go only to this sender.
IPAddress rxIp;
uint16_t rxPort = 0;
bool rxValid = false;

uint32_t lastDiscoveryMs = 0, lastStatusMs = 0, lastSensorMs = 0;
uint32_t lastRxMs = 0;                                   // last command/keepalive (feeds the watchdog)
uint32_t idCounter = 0;
uint32_t unknownMsgs = 0, badJsonMsgs = 0, subRejected = 0;
String lastCueId = "";
char displayText[DISPLAY_TEXT_MAX + 1] = "IDLE";

inline bool subLive(const Subscriber& s, uint32_t now) { return s.used && (now - s.lastSeenMs) <= SUBSCRIBER_EXPIRY_MS; }

int liveSubscribers(uint32_t now) {
  int n = 0;
  for (int i = 0; i < MAX_SUBSCRIBERS; i++) if (subLive(subs[i], now)) n++;
  return n;
}

// Add or refresh a sender. `explicitSubscribe` (a real {"type":"subscribe"}) may evict the least recently
// seen entry when the table is full, so a laptop plot can always join; implicit refreshes never evict.
void touchSubscriber(const IPAddress& ip, uint16_t port, uint32_t now, bool explicitSubscribe) {
  int freeIdx = -1, oldestIdx = -1;
  for (int i = 0; i < MAX_SUBSCRIBERS; i++) {
    if (subs[i].used && subs[i].ip == ip && subs[i].port == port) { subs[i].lastSeenMs = now; return; }
    if (freeIdx < 0 && !subLive(subs[i], now)) freeIdx = i;
    if (oldestIdx < 0 || (now - subs[i].lastSeenMs) > (now - subs[oldestIdx].lastSeenMs)) oldestIdx = i;
  }
  int slot = freeIdx;
  if (slot < 0 && explicitSubscribe) slot = oldestIdx;
  if (slot < 0) { subRejected++; return; }
  subs[slot].ip = ip;
  subs[slot].port = port;
  subs[slot].lastSeenMs = now;
  subs[slot].used = true;
  Serial.printf("[NET] subscriber %s:%u (live: %d)\n", ip.toString().c_str(), port, liveSubscribers(now));
}

// ------------------------- per-channel motor state (non-blocking, one entry per motor) -------------------------
struct Motor {
  bool active = false;
  String pattern;
  uint8_t intensity = 0;
  uint32_t startMs = 0, durationMs = 0;
  uint32_t lastStartMs = 0;                              // cue-gap clock, per channel
  // duty-cycle accounting: ON milliseconds inside this channel's rolling window
  uint32_t windowStartMs = 0, onMsInWindow = 0, lastTickMs = 0;
};
Motor motors[MOTOR_CHANNELS];

inline bool fitted(int i) { return i >= 0 && i < MOTOR_COUNT; }

inline void pwm(int i, uint8_t duty) { if (fitted(i)) ledcWrite(MOTOR_PINS[i], duty); }

void motorStop(int i) { if (i < 0 || i >= MOTOR_CHANNELS) return; motors[i].active = false; pwm(i, 0); }

void motorStopAll() { for (int i = 0; i < MOTOR_CHANNELS; i++) motorStop(i); }

void accountDuty(int i, uint32_t now) {
  Motor& m = motors[i];
  if (now - m.windowStartMs >= DUTY_WINDOW_MS) { m.windowStartMs = now; m.onMsInWindow = 0; }
  if (m.active) m.onMsInWindow += now - m.lastTickMs;
  m.lastTickMs = now;
}

uint32_t dutyPct(int i) {
  Motor& m = motors[i];
  uint32_t span = max<uint32_t>(1, millis() - m.windowStartMs);
  return (100UL * m.onMsInWindow) / max<uint32_t>(span, 1000);  // treat <1 s windows as 1 s
}

// pulse / continuous: steady; buzz: 25 ms on/off; ramp: 0 -> intensity; double_tap: two halves with a gap.
// buzz is the `success` reward waveform and must not be removed (handoff §3.2).
void updateMotor(int i) {
  uint32_t now = millis();
  accountDuty(i, now);
  Motor& m = motors[i];
  if (!m.active) return;
  uint32_t t = now - m.startMs;
  // MAX_CONTINUOUS_ON_MS is a hard ceiling independent of the (already clamped) requested duration.
  if (t >= m.durationMs || t >= MAX_CONTINUOUS_ON_MS) { motorStop(i); return; }
  uint8_t d = m.intensity;
  if (m.pattern == "buzz") d = ((t / 25) % 2 == 0) ? m.intensity : 0;
  else if (m.pattern == "ramp") d = (uint8_t)((uint32_t)m.intensity * t / m.durationMs);
  else if (m.pattern == "double_tap") d = (t < m.durationMs * 2 / 5 || t > m.durationMs * 3 / 5) ? m.intensity : 0;
  pwm(i, d);
}

void updateMotors() { for (int i = 0; i < MOTOR_COUNT; i++) updateMotor(i); }

bool anyMotorActive() {
  for (int i = 0; i < MOTOR_COUNT; i++) if (motors[i].active) return true;
  return false;
}

// Shared by network cues and the bench tools. Caller has already validated and clamped.
void startMotor(int motor, uint8_t intensity, uint32_t duration, const char* pattern, uint32_t now) {
  Motor& m = motors[motor];
  m.active = true;
  m.pattern = pattern;
  m.intensity = intensity;
  m.startMs = now;
  m.durationMs = duration;
  m.lastStartMs = now;
  m.lastTickMs = now;
  pwm(motor, m.intensity);                              // vibration starts NOW
}

// ------------------------- bench scheduler (selftest / soa): non-blocking, serial-driven -------------------------
#define SCHED_MAX 24
struct SchedEvent { uint32_t atMs; int8_t motor; };   // motor -1 = free slot
SchedEvent sched[SCHED_MAX];
int schedPending = 0;

void schedClear() { for (int i = 0; i < SCHED_MAX; i++) sched[i].motor = -1; schedPending = 0; }

bool schedAdd(uint32_t atMs, int motor) {
  for (int i = 0; i < SCHED_MAX; i++) {
    if (sched[i].motor < 0) { sched[i].atMs = atMs; sched[i].motor = (int8_t)motor; schedPending++; return true; }
  }
  return false;
}

void runSched() {
  if (schedPending == 0) return;
  uint32_t now = millis();
  for (int i = 0; i < SCHED_MAX; i++) {
    if (sched[i].motor < 0 || (int32_t)(now - sched[i].atMs) < 0) continue;
    int motor = sched[i].motor;
    sched[i].motor = -1;
    schedPending--;
    lastRxMs = now;                                      // bench activity counts as a keepalive
    if (fitted(motor)) startMotor(motor, BENCH_INTENSITY, BENCH_DURATION_MS, "pulse", now);
    if (schedPending == 0) Serial.println("[BENCH] done");
  }
}

void startSelftest() {
  motorStopAll();
  schedClear();
  uint32_t t0 = millis() + 100;
  for (int k = 0; k < 3; k++) {                          // A, then B 300 ms later, repeated every 600 ms
    schedAdd(t0 + k * 600UL, 0);
    schedAdd(t0 + k * 600UL + 300UL, 1);
  }
  Serial.printf("[SELFTEST] A then B x3 at intensity %d for %d ms | IMU %s | OLED %s\n", BENCH_INTENSITY,
                BENCH_DURATION_MS, imuOk ? "found (0x68)" : "NOT found", oledOk ? "found (0x3C)" : "NOT found");
}

void startSoa(int soaMs) {
  soaMs = constrain(soaMs, SOA_MIN_MS, SOA_MAX_MS);
  motorStopAll();
  schedClear();
  uint32_t t0 = millis() + 200;
  for (int k = 0; k < SOA_STROKES; k++) {
    schedAdd(t0 + k * 1000UL, 0);
    schedAdd(t0 + k * 1000UL + soaMs, 1);
  }
  Serial.printf("[SOA] %d strokes, A then B after %d ms, one per second. Stroke feels like ONE movement toward the elbow? "
                "write the number down (motor_soa_ms).\n", SOA_STROKES, soaMs);
}

void i2cScan() {
  Serial.println("[SCAN] I2C (SDA 21, SCL 22):");
  int found = 0;
  for (uint8_t addr = 1; addr < 127; addr++) {
    Wire.beginTransmission(addr);
    if (Wire.endTransmission() == 0) {
      const char* what = addr == 0x68 ? "MPU6050" : (addr == 0x69 ? "MPU6050 (AD0 high)" : (addr == 0x3C ? "SSD1306 OLED" : ""));
      Serial.printf("  0x%02X %s\n", addr, what);
      found++;
    }
  }
  Serial.printf("[SCAN] %d device(s). Expect 0x3C and 0x68.\n", found);
}

// ---------------------------------- Serial bench commands ----------------------------------
char serBuf[40];
size_t serLen = 0;

void handleSerialLine(const char* line) {
  while (*line == ' ') line++;
  if (strcmp(line, "selftest") == 0) { startSelftest(); return; }
  if (strncmp(line, "soa", 3) == 0 && (line[3] == 0 || line[3] == ' ')) {
    int v = atoi(line + 3);
    if (v <= 0) { Serial.printf("[SOA] usage: soa <ms>  (%d..%d)\n", SOA_MIN_MS, SOA_MAX_MS); return; }
    startSoa(v);
    return;
  }
  if (strcmp(line, "scan") == 0) { i2cScan(); return; }
  if (strcmp(line, "help") == 0 || line[0] == 0) {
    Serial.println("commands: selftest | soa <ms> | scan | help");
    return;
  }
  Serial.printf("[SERIAL] unknown command '%s' (try help)\n", line);
}

void pollSerial() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    if (c == '\n' || c == '\r') {
      if (serLen > 0) { serBuf[serLen] = 0; handleSerialLine(serBuf); serLen = 0; }
    } else if (serLen < sizeof(serBuf) - 1) {
      serBuf[serLen++] = c;
    }
  }
}

// ---------------------------------- JSON out ----------------------------------
String newId() { return String(DEVICE_ID) + "-" + String(millis()) + "-" + String(idCounter++); }

static char txBuf[1400];  // status is ~800 bytes at 4 channels; 512 truncated it into invalid JSON.

// Serialises into txBuf; returns the length, or 0 if it did not fit (nothing is sent then).
size_t encodeDoc(JsonDocument& doc) {
  size_t n = serializeJson(doc, txBuf, sizeof(txBuf));
  if (n >= sizeof(txBuf) - 1) { Serial.println("[TX] message too large, dropped"); return 0; }
  return n;
}

void sendRaw(const IPAddress& ip, uint16_t port, size_t n) {
  udp.beginPacket(ip, port);
  udp.write((const uint8_t*)txBuf, n);
  udp.endPacket();
}

void sendTo(const IPAddress& ip, uint16_t port, JsonDocument& doc) {
  size_t n = encodeDoc(doc);
  if (n) sendRaw(ip, port, n);
}

// Every live subscriber (status, sensor_data).
void sendSubscribers(JsonDocument& doc) {
  uint32_t now = millis();
  if (liveSubscribers(now) == 0) return;
  size_t n = encodeDoc(doc);
  if (!n) return;
  for (int i = 0; i < MAX_SUBSCRIBERS; i++) if (subLive(subs[i], now)) sendRaw(subs[i].ip, subs[i].port, n);
}

// Only the sender of the datagram being handled (acks).
void sendSender(JsonDocument& doc) { if (rxValid) sendTo(rxIp, rxPort, doc); }

// Compatibility fields for the current OPUS game (v1 envelope): v, id, ts_ms.
void addCompat(JsonDocument& d) { d["v"] = 1; d["id"] = newId(); d["ts_ms"] = (double)millis(); }

// §3 ACK. status: accepted | executed | rejected | error. Dual dialect: cue_id + ack_id, status + ok.
void sendAck(const String& cueId, const char* status, uint32_t rxMs, uint32_t startMs,
             const char* errCode = nullptr, const char* errMsg = nullptr, int motor = -1) {
  JsonDocument d;
  d["type"] = "ack";
  d["cue_id"] = cueId;
  d["status"] = status;
  d["timestamp_ms"] = millis();
  d["received_ms"] = rxMs;                            // §8: device receives
  if (startMs) d["vibration_start_ms"] = startMs;     // §8: vibration starts
  if (motor >= 0) d["motor"] = motor;                 // which channel actually ran (after any fallback)
  if (errCode) { d["error_code"] = errCode; d["error_message"] = errMsg ? errMsg : ""; }
  // game compatibility
  addCompat(d);
  d["ack_id"] = cueId;
  d["ok"] = (strcmp(status, "accepted") == 0 || strcmp(status, "executed") == 0);
  sendSender(d);
}

// §4 status — nested motor_0..motor_3 (contract dialect) AND flat motors_ok/imu_ok/fw (game dialect).
void sendStatus() {
  JsonDocument d;
  d["type"] = "status";
  d["device_id"] = DEVICE_ID;
  d["device_kind"] = DEVICE_KIND;
  d["battery_pct"] = nullptr;                         // power bank: no battery sense fitted
  d["connected"] = true;
  char key[8];
  for (int i = 0; i < MOTOR_CHANNELS; i++) {
    snprintf(key, sizeof(key), "motor_%d", i);
    JsonObject o = d[key].to<JsonObject>();
    o["available"] = fitted(i);
    o["location"] = MOTOR_LOCATIONS[i];
    o["temperature_c"] = nullptr;                     // no motor thermistor fitted
    o["duty_pct"] = fitted(i) ? dutyPct(i) : 0;
    o["active"] = fitted(i) ? motors[i].active : false;
  }
  d["motor_count"] = MOTOR_COUNT;                     // fitted channels
  d["motor_channels"] = MOTOR_CHANNELS;               // addressable channels (schema enum width)
  d["chip_temperature_c"] = temperatureRead();
  d["imu_available"] = imuOk;
  d["oled_available"] = oledOk;
  d["subscribers"] = liveSubscribers(millis());
  d["unknown_msgs"] = unknownMsgs;                    // ignored message types (never treated as commands)
  d["last_cue_id"] = lastCueId.length() ? lastCueId.c_str() : nullptr;
  d["timestamp_ms"] = millis();
  // game compatibility
  addCompat(d);
  d["motors_ok"] = true;
  d["imu_ok"] = imuOk;
  d["fw"] = FIRMWARE_VERSION;
  sendSubscribers(d);
}

// §5 / PRD §9.1 discovery (broadcast; the same packet also carries the game's legacy hello fields)
void sendDiscovery() {
  JsonDocument d;
  d["type"] = "device_discovery";
  d["device_id"] = DEVICE_ID;
  d["device_name"] = DEVICE_NAME;
  d["device_kind"] = DEVICE_KIND;
  d["firmware_version"] = FIRMWARE_VERSION;
  d["protocol_version"] = PROTOCOL_VERSION;
  d["ip"] = WiFi.localIP().toString();
  d["command_port"] = COMMAND_PORT;
  d["status"] = "available";
  d["motor_count"] = MOTOR_COUNT;
  d["motor_channels"] = MOTOR_CHANNELS;
  d["timestamp_ms"] = millis();
  d["opus_haptic"] = 1;                               // legacy hello (Unity HapticClient)
  d["port"] = COMMAND_PORT;
  size_t n = encodeDoc(d);
  if (!n) return;
  IPAddress sub = WiFi.localIP(); sub[3] = 255;       // subnet broadcast: many APs drop 255.255.255.255
  sendRaw(IPAddress(255, 255, 255, 255), DISCOVERY_PORT, n);
  sendRaw(sub, DISCOVERY_PORT, n);
}

// §7 sensor_data — RAW units as delivered by the driver (m/s2, rad/s). No normalisation. One sample per packet.
// No temperature field: keeps the 100 Hz packets small.
void sendSensors() {
  if (liveSubscribers(millis()) == 0) return;          // nobody listening: skip the I2C read and the JSON
  sensors_event_t a, g, t;
  mpu.getEvent(&a, &g, &t);
  JsonDocument d;
  d["type"] = "sensor_data";
  d["device_id"] = DEVICE_ID;
  d["timestamp_ms"] = millis();
  JsonObject s = d["sensors"].to<JsonObject>();
  auto put = [&](const char* name, float v, const char* unit) {
    JsonObject o = s[name].to<JsonObject>(); o["value"] = v; o["unit"] = unit; o["status"] = "ok";
  };
  put("imu_accel_x", a.acceleration.x, "m/s2");
  put("imu_accel_y", a.acceleration.y, "m/s2");
  put("imu_accel_z", a.acceleration.z, "m/s2");
  put("imu_gyro_x", g.gyro.x, "rad/s");
  put("imu_gyro_y", g.gyro.y, "rad/s");
  put("imu_gyro_z", g.gyro.z, "rad/s");
  sendSubscribers(d);
}

// ---------------------------------- OLED ----------------------------------
// Never blocks the motor loop for long: at most one ~25 ms flush per changed frame, <= 5 Hz, and a changed
// frame waits (up to OLED_DEFER_MAX_MS) while a motor is on.
String oledLastKey;
uint32_t oledLastCheckMs = 0, oledLastFlushMs = 0;

void oledService() {
  if (!oledOk) return;
  uint32_t now = millis();
  if (now - oledLastCheckMs < OLED_PERIOD_MS) return;
  oledLastCheckMs = now;

  char l1[28], l4[16];
  snprintf(l1, sizeof(l1), "%s fw%s", DEVICE_ID, FIRMWARE_VERSION);
  String l2 = (WiFi.status() == WL_CONNECTED) ? WiFi.localIP().toString() : String("NO WIFI");
  int links = liveSubscribers(now);
  if (links > 0) snprintf(l4, sizeof(l4), "LINK %d", links); else snprintf(l4, sizeof(l4), "NO LINK");

  String key = String(l1) + "|" + l2 + "|" + displayText + "|" + l4;
  if (key == oledLastKey) return;
  if (anyMotorActive() && now - oledLastFlushMs < OLED_DEFER_MAX_MS) return;   // try again next tick
  oledLastKey = key;
  oledLastFlushMs = now;

  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 0);  oled.print(l1);
  oled.setCursor(0, 12); oled.print(l2);
  bool big = strlen(displayText) <= 10;                // size 2 = 12 px per char -> 10 chars fit in 128 px
  oled.setTextSize(big ? 2 : 1);
  oled.setCursor(0, big ? 28 : 32); oled.print(displayText);
  oled.setTextSize(1);
  oled.setCursor(0, 54); oled.print(l4);
  oled.display();
}

// Things that must keep running even while connectWifi() waits.
void serviceLocal() {
  pollSerial();
  runSched();
  updateMotors();
  oledService();
}

// ---------------------------------- §2 command handling + §9 safety ----------------------------------
// Returns the channel a cue addressed to `motor` should actually run on, or -1 if it must be rejected.
int routeMotor(int motor) {
  if (fitted(motor)) return motor;
  switch (motor) {
    case 1: return ROUTE_MOTOR1_TO_MOTOR0 ? 0 : -1;
    case 2: return ROUTE_MOTOR2_TO_MOTOR0 ? 0 : -1;
    case 3: return ROUTE_MOTOR3_TO_MOTOR0 ? 0 : -1;
    default: return -1;                                // motor 0 unfitted means nothing is fitted
  }
}

void handleHaptic(JsonDocument& msg, uint32_t rxMs) {
  String cueId = msg["cue_id"] | "";
  if (cueId.length() == 0) cueId = newId();
  if (msg["motor"].isNull() || msg["intensity"].isNull() || msg["duration_ms"].isNull()) {
    sendAck(cueId, "rejected", rxMs, 0, "MISSING_FIELD", "motor, intensity and duration_ms are required");
    return;
  }
  int requested = msg["motor"];
  long intensity = msg["intensity"];
  long duration = msg["duration_ms"];
  String pattern = msg["pattern"] | "pulse";
  if (pattern == "continuous") pattern = "pulse";      // same waveform for one bounded burst
  // msg["cue"] (e.g. "stroke") is only a label for the log; msg["play_at_ms"] is deliberately ignored:
  // the game times its own sends, the firmware plays on arrival.

  // Schema enum is [0,1,2,3]: anything outside that range is INVALID_MOTOR.
  if (requested < 0 || requested >= MOTOR_CHANNELS) {
    sendAck(cueId, "rejected", rxMs, 0, "INVALID_MOTOR", "motor must be 0-3");
    return;
  }
  // Inside the range but not fitted: fall back to motor 0 so a partially built sleeve still demos.
  int motor = routeMotor(requested);
  if (motor < 0) {
    sendAck(cueId, "rejected", rxMs, 0, "MOTOR_UNAVAILABLE", "requested motor channel is not fitted");
    return;
  }
  if (pattern != "pulse" && pattern != "buzz" && pattern != "ramp" && pattern != "double_tap") {
    sendAck(cueId, "rejected", rxMs, 0, "INVALID_PATTERN", "pattern must be pulse|continuous|buzz|ramp|double_tap");
    return;
  }
  Motor& m = motors[motor];
  uint32_t now = millis();
  if (m.lastStartMs && now - m.lastStartMs < MIN_CUE_GAP_MS) {
    sendAck(cueId, "rejected", rxMs, 0, "CUE_GAP", "commands closer than MIN_CUE_GAP_MS", motor);
    return;
  }
  if (dutyPct(motor) >= DUTY_CYCLE_LIMIT_PCT) {
    sendAck(cueId, "rejected", rxMs, 0, "DUTY_CYCLE_LIMIT", "motor duty cycle limit reached, cooling down", motor);
    return;
  }
#if ENFORCE_CHIP_TEMPERATURE
  // Off by default: the ESP32's internal sensor is uncalibrated (on the team's board it read >= 70 C at idle and
  // refused every cue). Enable only once a real motor thermistor replaces it.
  if (temperatureRead() >= TEMPERATURE_LIMIT_C) {
    sendAck(cueId, "rejected", rxMs, 0, "TEMPERATURE_LIMIT", "device temperature limit reached", motor);
    return;
  }
#endif
  // Clamp (not reject) out-of-range intensity/duration, per §9 "reject/clamp".
  intensity = constrain(intensity, MIN_INTENSITY, MAX_INTENSITY);
  duration = constrain(duration, MIN_DURATION_MS, MAX_DURATION_MS);
  if (duration > MAX_CONTINUOUS_ON_MS) duration = MAX_CONTINUOUS_ON_MS;

  startMotor(motor, (uint8_t)intensity, (uint32_t)duration, pattern.c_str(), now);
  lastCueId = cueId;

  Serial.printf("[HAPTIC] cue=%s motor=%d%s intensity=%ld duration=%ldms pattern=%s (%s)\n",
                cueId.c_str(), motor, (motor == requested ? "" : " (routed from unfitted channel)"),
                intensity, duration, pattern.c_str(), (const char*)(msg["cue"] | ""));
  sendAck(cueId, "executed", rxMs, now, nullptr, nullptr, motor);
}

// {"type":"display","text":"SYNC"}: printable ASCII only, truncated to DISPLAY_TEXT_MAX; empty -> "IDLE".
void handleDisplay(JsonDocument& msg) {
  const char* txt = msg["text"] | "";
  size_t n = 0;
  for (size_t i = 0; txt[i] != 0 && n < DISPLAY_TEXT_MAX; i++) {
    unsigned char c = (unsigned char)txt[i];
    if (c >= 0x20 && c <= 0x7E) displayText[n++] = (char)c;
  }
  displayText[n] = 0;
  if (n == 0) strcpy(displayText, "IDLE");
}

void handleMessage(const char* buf, uint32_t rxMs) {
  JsonDocument msg;
  DeserializationError err = deserializeJson(msg, buf);
  if (err) {
    badJsonMsgs++;
    Serial.printf("[RX] bad JSON (%s): %s\n", err.c_str(), buf);
    sendAck("", "error", rxMs, 0, "BAD_JSON", err.c_str());
    return;
  }
  const char* type = msg["type"] | "";
  bool noType = msg["type"].isNull();
  bool isHaptic = noType || strcmp(type, "haptic") == 0;
  bool known = isHaptic || strcmp(type, "stop") == 0 || strcmp(type, "ping") == 0 ||
               strcmp(type, "status_request") == 0 || strcmp(type, "config") == 0 || strcmp(type, "cue") == 0 ||
               strcmp(type, "subscribe") == 0 || strcmp(type, "display") == 0;
  if (!known) {                                          // ignored and counted; not a keepalive, not a subscriber
    unknownMsgs++;
    if (unknownMsgs <= 5 || unknownMsgs % 100 == 0) Serial.printf("[RX] unknown type '%s' ignored (count %lu)\n", type, (unsigned long)unknownMsgs);
    return;
  }
  lastRxMs = rxMs;                                       // command or keepalive: feeds the 2 s watchdog
  touchSubscriber(rxIp, rxPort, rxMs, strcmp(type, "subscribe") == 0);

  if (isHaptic) { handleHaptic(msg, rxMs); return; }     // contract / current game
  if (strcmp(type, "subscribe") == 0) return;            // registration is the whole job
  if (strcmp(type, "display") == 0) { handleDisplay(msg); return; }
  if (strcmp(type, "stop") == 0) {                       // optional "motor" field stops one channel only
    if (msg["motor"].isNull()) { motorStopAll(); Serial.println("[STOP] all"); }
    else { int i = msg["motor"]; motorStop(i); Serial.printf("[STOP] motor %d\n", i); }
    return;
  }
  if (strcmp(type, "ping") == 0) {
    String pid = msg["id"].isNull() ? String((const char*)(msg["cue_id"] | "")) : String((const char*)(msg["id"] | ""));
    sendAck(pid, "accepted", rxMs, 0);
    return;
  }
  if (strcmp(type, "status_request") == 0) { sendStatus(); return; }
  if (strcmp(type, "config") == 0) {                     // game on/off switch
    if (!(msg["enabled"] | true)) motorStopAll();
    return;
  }
  if (strcmp(type, "cue") == 0) {                        // game's semantic envelope -> device command
    JsonDocument cmd;
    cmd["cue_id"] = (const char*)(msg["id"] | "");
    cmd["motor"] = msg["motor"] | 0;                     // honour an explicit channel if the game sends one
    cmd["intensity"] = (int)(255 * (float)(msg["intensity"] | 0.5f));
    cmd["duration_ms"] = msg["duration_ms"] | 200;
    cmd["pattern"] = msg["pattern"] | "pulse";
    handleHaptic(cmd, rxMs);
    return;
  }
}

void pollUdp() {
  for (int guard = 0; guard < 4; guard++) {              // drain a few datagrams per pass, never starve the motors
    int size = udp.parsePacket();
    if (size <= 0) return;
    uint32_t rxMs = millis();
    char buf[600];
    int n = udp.read(buf, sizeof(buf) - 1);
    if (n <= 0) return;
    buf[n] = 0;
    rxIp = udp.remoteIP();
    rxPort = udp.remotePort();
    rxValid = true;
    handleMessage(buf, rxMs);
    rxValid = false;
  }
}

// ---------------------------------- Wi-Fi ----------------------------------
void connectWifi() {
  WiFi.mode(WIFI_STA);
  WiFi.setSleep(false);                                 // modem sleep adds 100+ ms to incoming UDP
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  Serial.printf("[NET] connecting to '%s'", WIFI_SSID);
  uint32_t t0 = millis(), lastDot = millis();
  while (WiFi.status() != WL_CONNECTED) {
    serviceLocal();                                     // serial bench tools and the OLED ("NO WIFI") keep working
    delay(1);
    uint32_t now = millis();
    if (now - lastDot >= 300) { lastDot = now; Serial.print("."); }
    if (now - t0 > 20000) {
      Serial.println("\n[NET] not connected yet — is it 2.4 GHz and the password right? retrying");
      WiFi.disconnect(); WiFi.begin(WIFI_SSID, WIFI_PASS); t0 = now;
    }
  }
  Serial.printf("\n[NET] connected. IP %s  MAC %s\n", WiFi.localIP().toString().c_str(), WiFi.macAddress().c_str());
}

void printHandoff() {
  Serial.println("\n===== §6/§10 HANDOFF VALUES =====");
  Serial.printf("DEVICE_ID=%s\nDEVICE_KIND=%s\nDEVICE_IP=%s\nMAC_ADDRESS=%s\nFIRMWARE_VERSION=%s\n", DEVICE_ID, DEVICE_KIND,
                WiFi.localIP().toString().c_str(), WiFi.macAddress().c_str(), FIRMWARE_VERSION);
  Serial.printf("COMMAND_PORT=%d\nDISCOVERY_PORT=%d\nMOTOR_CHANNELS=%d (addressable, schema 0-3)\nMOTOR_COUNT=%d (fitted)\n",
                COMMAND_PORT, DISCOVERY_PORT, MOTOR_CHANNELS, MOTOR_COUNT);
  for (int i = 0; i < MOTOR_CHANNELS; i++)
    Serial.printf("MOTOR_%d: gpio=%d location=%s %s\n", i, MOTOR_PINS[i], MOTOR_LOCATIONS[i],
                  fitted(i) ? "FITTED" : "not fitted (cues fall back to motor 0)");
  Serial.printf("MIN_INTENSITY=%d\nMAX_INTENSITY=%d\nMIN_DURATION_MS=%d\nMAX_DURATION_MS=%d\nMIN_CUE_GAP_MS=%d (per motor)\n",
                MIN_INTENSITY, MAX_INTENSITY, MIN_DURATION_MS, MAX_DURATION_MS, MIN_CUE_GAP_MS);
  Serial.printf("MAX_CONTINUOUS_ON_MS=%d\nDUTY_CYCLE_LIMIT=%d%% over %d ms (per motor)\nWATCHDOG_MS=%d\nBATTERY=not sensed (power bank)\nTEMPERATURE_LIMIT_C=%.0f (chip, %s; reads %.1f now)\n",
                MAX_CONTINUOUS_ON_MS, DUTY_CYCLE_LIMIT_PCT, DUTY_WINDOW_MS, WATCHDOG_MS, TEMPERATURE_LIMIT_C,
                ENFORCE_CHIP_TEMPERATURE ? "enforced" : "reported only", temperatureRead());
  Serial.printf("IMU=%s (MPU6050, accel m/s2 +-8g, gyro rad/s +-500dps, 44 Hz DLPF, sent at %d Hz)\n",
                imuOk ? "present" : "absent", 1000 / SENSOR_PERIOD_MS);
  Serial.printf("OLED=%s (SSD1306 0x3C)\nSUBSCRIBERS=max %d, expiry %d ms\n", oledOk ? "present" : "absent",
                MAX_SUBSCRIBERS, SUBSCRIBER_EXPIRY_MS);
  Serial.println("ACK_SUPPORTED=true\nSTATUS_SUPPORTED=true\nDISCOVERY_SUPPORTED=true");
  Serial.println("SERIAL COMMANDS: selftest | soa <ms> | scan | help");
  Serial.println("=================================\n");
}

// ---------------------------------- setup / loop ----------------------------------
void setup() {
  Serial.begin(115200);
  delay(300);
  Serial.printf("\nOPUS sleeve fw %s (%s, Phantom Hand Node A) — %d of %d channels fitted\n",
                FIRMWARE_VERSION, DEVICE_ID, MOTOR_COUNT, MOTOR_CHANNELS);
  schedClear();

  // One LEDC channel per fitted motor pin (core 3.x allocates it inside ledcAttach).
  uint32_t nowMs = millis();
  for (int i = 0; i < MOTOR_COUNT; i++) {
    ledcAttach(MOTOR_PINS[i], PWM_FREQ, PWM_RESOLUTION);
    pwm(i, 0);
    motors[i].windowStartMs = motors[i].lastTickMs = nowMs;
  }
  // Power-on self-test: one short buzz per fitted channel, in order (at the intensity cap), so you can hear which is which.
  for (int i = 0; i < MOTOR_COUNT; i++) { pwm(i, MAX_INTENSITY); delay(150); pwm(i, 0); delay(150); }

  Wire.begin(21, 22);
  Wire.setClock(400000);
  imuOk = mpu.begin();
  if (imuOk) {
    mpu.setAccelerometerRange(MPU6050_RANGE_8_G);
    mpu.setGyroRange(MPU6050_RANGE_500_DEG);
    mpu.setFilterBandwidth(MPU6050_BAND_44_HZ);
  }
  Serial.printf("[IMU] MPU6050 %s\n", imuOk ? "found" : "NOT found (VCC->3.3V, GND, SCL->22, SDA->21)");

  oledOk = oled.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR);
  Serial.printf("[OLED] SSD1306 %s\n", oledOk ? "found at 0x3C" : "NOT found at 0x3C — running without a display");
  if (oledOk) { oled.clearDisplay(); oled.display(); }

  // BOOT-button self-test window (BOOT is GPIO0; read only, after reset; never hold it through reset).
  pinMode(BOOT_BUTTON_PIN, INPUT_PULLUP);
  Serial.printf("[BOOT] press BOOT within %d ms for the bench self-test (or type 'selftest')\n", BOOT_SELFTEST_WINDOW_MS);
  bool selftestRequested = false;
  uint32_t w0 = millis();
  while (millis() - w0 < BOOT_SELFTEST_WINDOW_MS) {
    if (digitalRead(BOOT_BUTTON_PIN) == LOW) { selftestRequested = true; break; }
    delay(10);
  }
  if (selftestRequested) startSelftest();                // runs from the scheduler, even while Wi-Fi is still connecting

  connectWifi();
  udp.begin(COMMAND_PORT);
  Serial.printf("[NET] listening UDP %d, discovery broadcast -> %d every %d ms\n", COMMAND_PORT, DISCOVERY_PORT, DISCOVERY_PERIOD_MS);
  printHandoff();
  nowMs = millis();
  for (int i = 0; i < MOTOR_CHANNELS; i++) { motors[i].windowStartMs = nowMs; motors[i].lastTickMs = nowMs; }
  lastSensorMs = nowMs;
}

void loop() {
  if (WiFi.status() != WL_CONNECTED) { motorStopAll(); connectWifi(); }

  pollUdp();
  serviceLocal();

  uint32_t now = millis();
  // FR-FW-04: nothing (command or keepalive) for 2 s while any motor is active -> every output to 0.
  if (anyMotorActive() && now - lastRxMs > WATCHDOG_MS) { motorStopAll(); Serial.println("[SAFETY] watchdog stop"); }
  if (now - lastDiscoveryMs >= DISCOVERY_PERIOD_MS) { lastDiscoveryMs = now; sendDiscovery(); }
  if (now - lastStatusMs >= STATUS_PERIOD_MS) { lastStatusMs = now; sendStatus(); }
  if (imuOk && now - lastSensorMs >= SENSOR_PERIOD_MS) {
    lastSensorMs += SENSOR_PERIOD_MS;                    // fixed step: no drift
    if (now - lastSensorMs >= 3UL * SENSOR_PERIOD_MS) lastSensorMs = now;   // fell far behind: resync
    sendSensors();
  }
}
