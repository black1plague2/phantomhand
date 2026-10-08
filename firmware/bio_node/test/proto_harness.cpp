// Host test of the REAL bio_proto.h. Self-checks (subscriber table, classify, JSON scan, ChunkQuality) abort with
// a message on failure; then prints one sample of every outgoing message as "NAME<TAB>json" for the Python test
// to parse with json.loads and compare to PRD 9.1 / 9.3.
// Build: g++ -O2 -std=c++17 -Wall -Wextra proto_harness.cpp -o proto_harness
#include <cstdio>
#include <cstdlib>
#include "../bio_proto.h"

#define CHECK(c) do { if (!(c)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #c); exit(1); } } while (0)

int main() {
  // ---- subscribers (03-SPEC D3: max 3, subscribe adds/refreshes, ping refreshes, 5 s expiry)
  SubscriberTable t;
  CHECK(t.count(0) == 0);
  CHECK(t.subscribe(1, 100, 1000));
  CHECK(t.subscribe(2, 100, 1000));
  CHECK(t.subscribe(3, 100, 1000));
  CHECK(!t.subscribe(4, 100, 1500));            // 4th live peer refused, nobody evicted
  CHECK(t.count(1500) == 3);
  CHECK(t.subscribe(1, 100, 3000));             // refresh (not a new entry)
  CHECK(t.count(3000) == 3);
  CHECK(t.refresh(2, 100, 5500));               // ping refreshes an existing peer ...
  CHECK(!t.refresh(9, 100, 5500));              // ... but never adds one
  CHECK(t.count(6000) == 3);                    // peer 3 is exactly 5000 ms old: still live (expiry is > 5000)
  CHECK(t.count(6001) == 2);                    // peer 3 expired; 1 (age 3001) and 2 (age 501) live
  CHECK(t.subscribe(4, 100, 6001));             // the freed slot is reusable
  CHECK(t.count(8001) == 2);                    // peer 1 (last 3000) expired
  CHECK(t.count(10501) == 1);                   // peer 2 (last 5500) expired; peer 4 (6001) live
  CHECK(t.count(11002) == 0);
  // millis() wrap-around
  SubscriberTable w;
  CHECK(w.subscribe(7, 1, 0xFFFFFF00u));
  CHECK(w.count(0x00000100u) == 1);             // 512 ms later across the wrap
  CHECK(w.count(0x00001500u) == 0);

  // ---- incoming JSON
  char out[48];
  CHECK(classify("{\"type\":\"subscribe\"}") == MSG_SUBSCRIBE);
  CHECK(classify("{ \"type\" : \"ping\", \"id\":\"x\"}") == MSG_PING);
  CHECK(classify("{\"type\":\"haptic\",\"motor\":0,\"intensity\":150}") == MSG_MOTOR_CMD);
  CHECK(classify("{\"cue_id\":\"stroke_017\",\"motor\":0,\"intensity\":150,\"duration_ms\":200,\"pattern\":\"pulse\",\"cue\":\"stroke\"}") == MSG_MOTOR_CMD);
  CHECK(classify("{\"v\":1,\"type\":\"cue\",\"cue\":\"success\"}") == MSG_MOTOR_CMD);
  CHECK(classify("{\"type\":\"stop\"}") == MSG_MOTOR_CMD);
  CHECK(classify("{\"type\":\"display\",\"text\":\"SYNC\"}") == MSG_UNKNOWN);
  CHECK(classify("{\"note\":\"type subscribe\"}") == MSG_UNKNOWN);
  CHECK(classify("garbage") == MSG_UNKNOWN);
  CHECK(classify("") == MSG_UNKNOWN);
  CHECK(jsonGetString("{\"cue_id\":\"stroke_017\",\"motor\":0}", "cue_id", out, sizeof(out)) && !strcmp(out, "stroke_017"));
  CHECK(!jsonGetString("{\"cue_id\":5}", "cue_id", out, sizeof(out)));
  CHECK(jsonGetString("{\"cue_id\":\"a\\\"b\"}", "cue_id", out, sizeof(out)) && !strcmp(out, "a_b"));   // quote sanitised: the id is re-embedded in our JSON
  char tiny[4];
  CHECK(jsonGetString("{\"cue_id\":\"abcdefgh\"}", "cue_id", tiny, sizeof(tiny)) && !strcmp(tiny, "abc"));   // truncated, terminated

  // ---- chunk quality: ok / saturated / flat
  ChunkQuality q;
  for (int c = 0; c < 12; c++) {
    for (int i = 0; i < 10; i++) q.add(1900 + c, 2100 - c, 0);
    CHECK(!strcmp(q.finishChunk(), "ok"));
  }
  for (int i = 0; i < 10; i++) q.add(2000, 4095, i < 3 ? 1 : 0);
  CHECK(!strcmp(q.finishChunk(), "saturated"));
  ChunkQuality f;
  const char* s = "";
  for (int c = 0; c < 10; c++) { for (int i = 0; i < 10; i++) f.add(2047, 2048, 0); s = f.finishChunk(); }
  CHECK(!strcmp(s, "flat"));                    // 1 s with a 1-count swing
  ChunkQuality g;                               // a single 1-count-wide chunk is not "flat" before 1 s of history
  for (int i = 0; i < 10; i++) g.add(2047, 2048, 0);
  CHECK(!strcmp(g.finishChunk(), "ok"));

  // ---- outgoing messages (printed for the Python test)
  char b[480];
  float env[10] = {412.5f, 415.1f, 418.94f, 421.3f, 0, 1, 2, 3, 4, 5};
  size_t n = fmtChunk(b, sizeof(b), "CHETNA_BIO_001", 143100, env, 10, "ok");   CHECK(n > 0); printf("chunk\t%s\n", b);
  n = fmtBurst(b, sizeof(b), "CHETNA_BIO_001", 143550, 68.243f, 1.0811f);         CHECK(n > 0); printf("burst\t%s\n", b);
  n = fmtDiscovery(b, sizeof(b), "CHETNA_BIO_001", "0.5.0", 8790, 142050);        CHECK(n > 0); printf("discovery\t%s\n", b);
  n = fmtStatus(b, sizeof(b), "CHETNA_BIO_001", "0.5.0", 5000, 2, 7, 1000, "ok", 3, 1,
                "SAFETY: power bank only. Never connect to a laptop or charger while electrodes are on a person.");
  CHECK(n > 0); printf("status_first\t%s\n", b);
  n = fmtStatus(b, sizeof(b), "CHETNA_BIO_001", "0.5.0", 6000, 2, 7, 1000, "flat", 3, 1, nullptr);   CHECK(n > 0); printf("status\t%s\n", b);
  n = fmtAck(b, sizeof(b), "CHETNA_BIO_001", "stroke_017", "rejected", false, "NOT_A_HAPTIC_NODE", "no motors", 9000);
  CHECK(n > 0); printf("ack_reject\t%s\n", b);
  n = fmtAck(b, sizeof(b), "CHETNA_BIO_001", "p1", "accepted", true, nullptr, nullptr, 9100);   CHECK(n > 0); printf("ack_ok\t%s\n", b);
  char small[40];
  CHECK(fmtChunk(small, sizeof(small), "CHETNA_BIO_001", 1, env, 10, "ok") == 0);   // too small -> 0, never a partial message
  return 0;
}
