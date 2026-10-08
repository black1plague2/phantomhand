using System;
using System.Collections.Generic;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>Procedurally generated sound effects (no audio assets needed): brush swish, stone creak, thud. Cached per kind.</summary>
    public static class ProceduralSfx
    {
        public const int Rate = 24000;
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public static AudioClip Swish() { return Get("swish", 0.28f, (t, d, rng) => (float)(Noise(rng) * Math.Sin(Math.PI * t / d) * 0.5)); }

        /// <summary>Short soft confirmation tick (calibration held, probe recorded). U4.</summary>
        public static AudioClip Tick()
        {
            return Get("tick", 0.14f, (t, d, rng) => (float)(Math.Sin(2 * Math.PI * 880 * t) * Math.Exp(-t * 28) * 0.5));
        }

        public static AudioClip Thud()
        {
            return Get("thud", 0.45f, (t, d, rng) =>
            {
                float env = Mathf.Exp(-t * 9f);
                return (float)((Math.Sin(2 * Math.PI * 62 * t) * 0.8 + Noise(rng) * 0.25 * Math.Exp(-t * 30)) * env);
            });
        }

        public static AudioClip Creak()
        {
            return Get("creak", 0.6f, (t, d, rng) =>
            {
                double f = 140 + 90 * Math.Sin(2 * Math.PI * 7 * t) + 60 * t;
                double env = Math.Sin(Math.PI * t / d);
                double saw = ((t * f) % 1.0) * 2 - 1;
                return (float)(saw * 0.18 * env * (0.7 + 0.3 * Noise(rng)));
            });
        }

        public static void Play(AudioSource src, AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (src == null || clip == null) return;
            src.pitch = pitch;
            src.PlayOneShot(clip, volume);
        }

        private static double Noise(System.Random r) { return r.NextDouble() * 2 - 1; }

        private static AudioClip Get(string key, float seconds, Func<float, float, System.Random, float> gen)
        {
            if (Cache.TryGetValue(key, out var c) && c != null) return c;
            int n = (int)(Rate * seconds);
            var data = new float[n];
            var rng = new System.Random(key.GetHashCode() & 0x7fffffff);
            double lp = 0;
            for (int i = 0; i < n; i++)
            {
                float v = gen(i / (float)Rate, seconds, rng);
                lp += (v - lp) * (key == "swish" ? 0.35 : 0.8);   // soften
                data[i] = (float)lp;
            }
            c = AudioClip.Create("ph_" + key, n, 1, Rate, false);
            c.SetData(data, 0);
            Cache[key] = c;
            return c;
        }
    }
}
