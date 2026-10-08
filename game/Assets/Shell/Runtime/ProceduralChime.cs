using UnityEngine;

namespace Opus.Shell
{
    /// <summary>Run15 (task 3): a pleasant success chime built entirely in code -- no audio asset to import/
    /// license, and its pitch can rise smoothly with the patient's current success streak (a small, calm
    /// reward escalation) without needing N pre-recorded clips. A short two-partial tone (fundamental + a
    /// quiet fifth above it) with a fast attack / exponential decay envelope, which reads as a soft
    /// bell/marimba-like "ding" rather than a harsh beep -- deliberately simple DSP so it stays cheap to
    /// generate every trial in VR.</summary>
    public static class ProceduralChime
    {
        private const int SampleRate = 44100;
        private const float DurationSec = 0.35f;

        /// <summary>Builds (and does not cache -- each call gets its own short clip, cheap at 44.1kHz*0.35s
        /// mono) a chime clip whose base pitch rises with <paramref name="streak"/>, capped so a long streak
        /// doesn't end up shrieking. Semitone step chosen so 8 in a row is a full octave up, a musically
        /// sensible "climbing" feel.</summary>
        public static AudioClip Build(int streak, bool bonus = false)
        {
            int cappedStreak = Mathf.Clamp(streak, 0, 8);
            float semitoneStep = 1.5f; // 8 streak * 1.5 semitones = 12 semitones = one octave
            float baseFreq = 523.25f;  // C5 -- calm, mid-register, not shrill
            float freq = baseFreq * Mathf.Pow(2f, (cappedStreak * semitoneStep) / 12f);
            // Variable-ratio bonus success gets a brighter, slightly longer chime (a second, higher partial
            // louder than usual) so it's felt as "extra good" without being alarming.
            float fifthGain = bonus ? 0.55f : 0.30f;
            float duration = bonus ? DurationSec * 1.4f : DurationSec;

            int sampleCount = Mathf.CeilToInt(SampleRate * duration);
            var clip = AudioClip.Create($"opus_chime_{cappedStreak}_{bonus}", sampleCount, 1, SampleRate, false);
            var data = new float[sampleCount];
            float fifth = freq * 1.5f; // perfect fifth above the fundamental
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                // Fast attack (~5ms), exponential decay -- a struck-bell envelope, not a sustained tone.
                float attack = Mathf.Clamp01(t / 0.005f);
                float decay = Mathf.Exp(-t * 6f);
                float env = attack * decay;
                float sample = Mathf.Sin(2f * Mathf.PI * freq * t) + fifthGain * Mathf.Sin(2f * Mathf.PI * fifth * t);
                data[i] = sample * env * 0.5f; // 0.5 headroom so the two partials never clip
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Convenience: build and immediately play one-shot on <paramref name="source"/>. The scene
        /// controller calls this from the "placed" trial event, passing its own running streak counter and
        /// whether this particular success drew the variable-ratio bonus (see OrchardReachModule's `data.bonus`
        /// on that event).</summary>
        public static void PlaySuccess(AudioSource source, int streak, bool bonus = false)
        {
            if (source == null) return;
            source.PlayOneShot(Build(streak, bonus), bonus ? 0.9f : 0.7f);
        }
    }
}
