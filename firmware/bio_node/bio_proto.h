// Node B wire protocol helpers, pure C++ (no Arduino headers) so the exact same code runs in the host test
// (firmware/bio_node/test/proto_test.cpp). Message shapes: docs/agent-briefs/ph/PRD_v2.md section 9 and
// 03-SPEC.md section 4. No ArduinoJson: every outgoing message is one snprintf, incoming messages are only
// scanned for "type", "cue_id"/"id" and a few keys, so the sketch needs no extra libraries.
#pragma once
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define BIO_MAX_SUBS         3        // 03-SPEC D3: max 3 subscribers
#define BIO_SUB_EXPIRY_MS    5000     // expiry 5 s without a subscribe / ping
#define BIO_CHUNK_N          10       // envelope values per sensor_chunk (100 ms at 100 Hz)
#define BIO_SAT_HI           4090     // 12-bit ADC counts treated as clipped
#define BIO_SAT_LO           5
#define BIO_SAT_MIN_COUNT    2        // clipped raw samples in a 100 ms chunk -> "saturated"
#define BIO_FLAT_PTP         3        // raw peak-to-peak below this over 1 s -> "flat" (electrode/BioAmp unplugged)

// ------------------------------------------------ subscribers ------------------------------------------------
struct Subscriber { uint32_t ip = 0; uint16_t port = 0; uint32_t lastSeenMs = 0; bool used = false; };

struct SubscriberTable {
  Subscriber s[BIO_MAX_SUBS];

  static bool expired(const Subscriber& e, uint32_t now) { return (uint32_t)(now - e.lastSeenMs) > BIO_SUB_EXPIRY_MS; }

  void prune(uint32_t now) { for (auto& e : s) if (e.used && expired(e, now)) e.used = false; }

  int find(uint32_t ip, uint16_t port) const {
    for (int i = 0; i < BIO_MAX_SUBS; i++) if (s[i].used && s[i].ip == ip && s[i].port == port) return i;
    return -1;
  }

  // `subscribe`: add or refresh. Returns false only when the table is full of live entries (the newcomer is
  // ignored; a live subscriber is never evicted).
  bool subscribe(uint32_t ip, uint16_t port, uint32_t now) {
    prune(now);
    int i = find(ip, port);
    if (i < 0) for (int k = 0; k < BIO_MAX_SUBS; k++) if (!s[k].used) { i = k; break; }
    if (i < 0) return false;
    s[i].ip = ip; s[i].port = port; s[i].lastSeenMs = now; s[i].used = true;
    return true;
  }

  // ping / any other command: refresh the sender only if it is already subscribed.
  bool refresh(uint32_t ip, uint16_t port, uint32_t now) {
    prune(now);
    int i = find(ip, port);
    if (i < 0) return false;
    s[i].lastSeenMs = now;
    return true;
  }

  int count(uint32_t now) { prune(now); int n = 0; for (auto& e : s) if (e.used) n++; return n; }
};

// ------------------------------------------------ chunk status ------------------------------------------------
// Fed with the per-100 Hz-sample min/max/clip count the sampler collects; one status per 100 ms chunk.
struct ChunkQuality {
  uint16_t chunkMin[10]; uint16_t chunkMax[10]; int hist = 0, histHead = 0;
  uint16_t curMin = 4095, curMax = 0; int curSat = 0;

  void add(uint16_t rawMin, uint16_t rawMax, uint8_t satCount) {
    if (rawMin < curMin) curMin = rawMin;
    if (rawMax > curMax) curMax = rawMax;
    curSat += satCount;
  }

  // Call once per finished chunk. Returns "saturated", "flat" or "ok".
  const char* finishChunk() {
    chunkMin[histHead] = curMin; chunkMax[histHead] = curMax;
    histHead = (histHead + 1) % 10; if (hist < 10) hist++;
    bool sat = curSat >= BIO_SAT_MIN_COUNT;
    curMin = 4095; curMax = 0; curSat = 0;
    if (sat) return "saturated";
    if (hist >= 10) {
      uint16_t lo = 4095, hi = 0;
      for (int i = 0; i < 10; i++) { if (chunkMin[i] < lo) lo = chunkMin[i]; if (chunkMax[i] > hi) hi = chunkMax[i]; }
      if ((int)hi - (int)lo < BIO_FLAT_PTP) return "flat";
    }
    return "ok";
  }
};

// ------------------------------------------------ incoming JSON ------------------------------------------------
// Minimal scanners for flat, well-formed command objects. Not a general JSON parser.
inline const char* jsonFindKey(const char* j, const char* key) {
  char pat[40];
  snprintf(pat, sizeof(pat), "\"%s\"", key);
  const char* p = j;
  while ((p = strstr(p, pat)) != nullptr) {
    const char* q = p + strlen(pat);
    while (*q == ' ' || *q == '\t') q++;
    if (*q == ':') { q++; while (*q == ' ' || *q == '\t') q++; return q; }
    p += strlen(pat);                       // the text was a string value, not a key
  }
  return nullptr;
}
inline bool jsonHasKey(const char* j, const char* key) { return jsonFindKey(j, key) != nullptr; }

inline bool jsonGetString(const char* j, const char* key, char* out, size_t cap) {
  const char* v = jsonFindKey(j, key);
  if (!v || *v != '"' || cap == 0) return false;
  v++;
  size_t n = 0;
  while (*v && *v != '"' && n + 1 < cap) {
    if (*v == '\\' && v[1]) v++;            // an escaped character is taken literally (sanitised below)
    char c = *v++;
    out[n++] = ((unsigned char)c < 0x20 || c == '"' || c == '\\') ? '_' : c;   // output is re-embedded in JSON: no quotes/controls
  }
  out[n] = 0;
  return true;
}

enum MsgKind { MSG_UNKNOWN = 0, MSG_SUBSCRIBE, MSG_PING, MSG_MOTOR_CMD };

// Anything that looks like a motor/haptic command is rejected with NOT_A_HAPTIC_NODE; Node B has no output pins.
inline MsgKind classify(const char* j) {
  char type[24] = "";
  jsonGetString(j, "type", type, sizeof(type));
  if (!strcmp(type, "subscribe")) return MSG_SUBSCRIBE;
  if (!strcmp(type, "ping") || !strcmp(type, "keepalive")) return MSG_PING;
  if (!strcmp(type, "haptic") || !strcmp(type, "cue") || !strcmp(type, "stop") || !strcmp(type, "config"))
    return MSG_MOTOR_CMD;
  if (!type[0] && (jsonHasKey(j, "motor") || jsonHasKey(j, "intensity"))) return MSG_MOTOR_CMD;   // Unity dialect: no "type"
  return MSG_UNKNOWN;
}

// ------------------------------------------------ outgoing JSON ------------------------------------------------
// All return the length written, or 0 if the buffer was too small (caller drops the message).
inline size_t fin(int n, size_t cap) { return (n > 0 && (size_t)n < cap) ? (size_t)n : 0; }

inline size_t fmtChunk(char* b, size_t cap, const char* id, uint32_t tsMs, const float* env, int n, const char* status) {
  int w = snprintf(b, cap, "{\"type\":\"sensor_chunk\",\"device_id\":\"%s\",\"device_kind\":\"bio\",\"timestamp_ms\":%lu,"
                   "\"sample_rate_hz\":100,\"emg_envelope\":[", id, (unsigned long)tsMs);
  if (w < 0 || (size_t)w >= cap) return 0;
  for (int i = 0; i < n; i++) {
    int k = snprintf(b + w, cap - w, i ? ",%.1f" : "%.1f", env[i]);
    if (k < 0 || (size_t)(w + k) >= cap) return 0;
    w += k;
  }
  int k = snprintf(b + w, cap - w, "],\"unit\":\"raw_adc\",\"status\":\"%s\"}", status);
  return (k < 0) ? 0 : fin(w + k, cap);
}

inline size_t fmtBurst(char* b, size_t cap, const char* id, uint32_t tsMs, float peak, float baselineRms) {
  return fin(snprintf(b, cap, "{\"type\":\"emg_burst\",\"device_id\":\"%s\",\"timestamp_ms\":%lu,\"peak\":%.1f,\"baseline_rms\":%.2f}",
                      id, (unsigned long)tsMs, peak, baselineRms), cap);
}

inline size_t fmtDiscovery(char* b, size_t cap, const char* id, const char* fw, int cmdPort, uint32_t tsMs) {
  return fin(snprintf(b, cap, "{\"type\":\"device_discovery\",\"device_id\":\"%s\",\"device_kind\":\"bio\",\"firmware_version\":\"%s\","
                      "\"command_port\":%d,\"status\":\"available\",\"motor_count\":0,\"timestamp_ms\":%lu}",
                      id, fw, cmdPort, (unsigned long)tsMs), cap);
}

// safety = nullptr -> field omitted (only the first status carries the notice).
inline size_t fmtStatus(char* b, size_t cap, const char* id, const char* fw, uint32_t tsMs, int subscribers,
                        uint32_t dropped, int adcRateHz, const char* signal, uint32_t unknownMsgs, uint32_t bursts,
                        const char* safety) {
  int w = snprintf(b, cap, "{\"type\":\"status\",\"device_id\":\"%s\",\"device_kind\":\"bio\",\"firmware_version\":\"%s\","
                   "\"battery_pct\":null,\"connected\":true,\"subscribers\":%d,\"samples_dropped\":%lu,\"adc_rate_hz\":%d,"
                   "\"signal\":\"%s\",\"unknown_msgs\":%lu,\"bursts\":%lu,\"timestamp_ms\":%lu",
                   id, fw, subscribers, (unsigned long)dropped, adcRateHz, signal, (unsigned long)unknownMsgs,
                   (unsigned long)bursts, (unsigned long)tsMs);
  if (w < 0 || (size_t)w >= cap) return 0;
  int k = safety ? snprintf(b + w, cap - w, ",\"safety_notice\":\"%s\"}", safety) : snprintf(b + w, cap - w, "}");
  return (k < 0) ? 0 : fin(w + k, cap);
}

// Ack in both dialects (cue_id/status and ack_id/ok), like the sleeve. cueId is sanitised by jsonGetString.
inline size_t fmtAck(char* b, size_t cap, const char* id, const char* cueId, const char* status, bool ok,
                     const char* errCode, const char* errMsg, uint32_t tsMs) {
  int w = snprintf(b, cap, "{\"type\":\"ack\",\"cue_id\":\"%s\",\"status\":\"%s\",\"timestamp_ms\":%lu,\"received_ms\":%lu",
                   cueId, status, (unsigned long)tsMs, (unsigned long)tsMs);
  if (w < 0 || (size_t)w >= cap) return 0;
  int k = 0;
  if (errCode) {
    k = snprintf(b + w, cap - w, ",\"error_code\":\"%s\",\"error_message\":\"%s\"", errCode, errMsg ? errMsg : "");
    if (k < 0 || (size_t)(w + k) >= cap) return 0;
    w += k;
  }
  k = snprintf(b + w, cap - w, ",\"v\":1,\"ts_ms\":%lu,\"device_id\":\"%s\",\"ack_id\":\"%s\",\"ok\":%s}",
               (unsigned long)tsMs, id, cueId, ok ? "true" : "false");
  return (k < 0) ? 0 : fin(w + k, cap);
}
