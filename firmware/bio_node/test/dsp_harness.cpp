// Host harness: runs the REAL firmware DSP (../bio_dsp.h) over samples read from a text file
// (one ADC count per line, 1 kHz). Build: g++ -O2 -std=c++17 dsp_harness.cpp -o dsp_harness
//   dsp_harness bp  <file>   -> one band-passed sample per line (every input sample)
//   dsp_harness env <file>   -> "E <t_ms> <env> <rms>" per 100 Hz sample and "B <onset_ms> <peak> <baseline_rms>" per burst
#include <cstdio>
#include <cstring>
#include "../bio_dsp.h"

int main(int argc, char** argv) {
  if (argc < 3) { fprintf(stderr, "usage: dsp_harness bp|env file\n"); return 2; }
  FILE* f = fopen(argv[2], "r");
  if (!f) { perror("open"); return 2; }
  bool bpMode = strcmp(argv[1], "bp") == 0;
  BioDsp dsp; BurstDetector det; BurstEvent ev;
  double v; uint32_t t = 0;
  while (fscanf(f, "%lf", &v) == 1) {
    float env, rms;
    bool tick = dsp.push((float)v, env, rms);
    if (bpMode) { printf("%.6f\n", dsp.lastBp); }
    else if (tick) {
      printf("E %u %.6f %.6f\n", t, env, rms);
      if (det.push(env, rms, t, ev)) printf("B %u %.6f %.6f\n", ev.onsetMs, ev.peak, ev.baselineRms);
    }
    t++;
  }
  fclose(f);
  return 0;
}
