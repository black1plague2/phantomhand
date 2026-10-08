// Minimal Arduino-ESP32 stubs, ONLY so `g++ -fsyntax-only` can check bio_node.ino's own code on a host that has
// no esp32 core. This is NOT a compile of the real firmware (types/signatures here are written from memory of
// core 3.x); the authoritative check is arduino-cli compile (see firmware/README.md).
#pragma once
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <string>
#define IRAM_ATTR
#define ADC_11db 3
#define WL_CONNECTED 3
#define WIFI_STA 1
#define configMAX_PRIORITIES 25
#define pdTRUE 1
#define pdFALSE 0
#define portMAX_DELAY 0xFFFFFFFFu
typedef int BaseType_t;
typedef void* TaskHandle_t;
typedef struct hw_timer_s hw_timer_t;
struct IPAddress {
  uint32_t v = 0;
  IPAddress() {}
  IPAddress(uint32_t x) : v(x) {}
  IPAddress(int a, int b, int c, int d) : v((uint32_t)a | (uint32_t)b << 8 | (uint32_t)c << 16 | (uint32_t)d << 24) {}
  operator uint32_t() const { return v; }
  uint8_t& operator[](int i) { return ((uint8_t*)&v)[i]; }
  struct S { std::string s; const char* c_str() const { return s.c_str(); } };
  S toString() const { return S{"0.0.0.0"}; }
};
struct HWCDC {
  void begin(int) {}
  void println() {}
  void println(const char*) {}
  void println(unsigned short) {}
  void print(const char*) {}
  int printf(const char*, ...) __attribute__((format(printf, 2, 3))) { return 0; }
  int available() { return 0; }
  int read() { return 0; }
  int availableForWrite() { return 100; }
};
extern HWCDC Serial;
struct WiFiClass {
  void mode(int) {}
  void setSleep(bool) {}
  void begin(const char*, const char*) {}
  void disconnect() {}
  int status() { return WL_CONNECTED; }
  IPAddress localIP() { return IPAddress(); }
  struct S { std::string s; const char* c_str() const { return s.c_str(); } };
  S macAddress() { return S{"00"}; }
};
extern WiFiClass WiFi;
struct WiFiUDP {
  void begin(int) {}
  int parsePacket() { return 0; }
  int read(char*, int) { return 0; }
  int read(uint8_t*, int) { return 0; }
  IPAddress remoteIP() { return IPAddress(); }
  uint16_t remotePort() { return 0; }
  void beginPacket(IPAddress, uint16_t) {}
  void write(const uint8_t*, size_t) {}
  void endPacket() {}
};
struct EspClass { uint32_t getFreeHeap() { return 0; } };
extern EspClass ESP;
uint32_t millis();
void delay(uint32_t);
int analogRead(int);
void analogReadResolution(int);
void analogSetPinAttenuation(int, int);
uint32_t ulTaskNotifyTake(int, uint32_t);
void vTaskNotifyGiveFromISR(TaskHandle_t, BaseType_t*);
void portYIELD_FROM_ISR(BaseType_t);
int xTaskCreatePinnedToCore(void (*)(void*), const char*, int, void*, int, TaskHandle_t*, int);
hw_timer_t* timerBegin(uint32_t);
void timerAttachInterrupt(hw_timer_t*, void (*)());
void timerAlarm(hw_timer_t*, uint64_t, bool, uint64_t);
