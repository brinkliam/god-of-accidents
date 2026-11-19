using System;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>Tiny offline synthesiser. Every sound in the game is generated here at startup - wholly original audio.</summary>
    public static class Synth
    {
        public const int Rate = 22050;

        public static AudioClip Clip(string name, float[] data, bool loop = false)
        {
            if (loop) Crossfade(data, Rate / 20);
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static void Crossfade(float[] d, int n)
        {
            n = Mathf.Min(n, d.Length / 4);
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                d[i] = d[i] * u + d[d.Length - n + i] * (1 - u);
            }
            Array.Resize(ref d, d.Length);
        }

        public static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        static void Normalize(float[] d, float peak = 0.9f)
        {
            float m = 0.0001f;
            foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        sealed class Rng { uint s; public Rng(uint seed) { s = seed * 2654435761u + 1; } public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xffffff) / (float)0x800000 - 1f; } }

        /// <summary>One-pole low-pass filter in place.</summary>
        static void LowPass(float[] d, float cutoff)
        {
            float rc = 1f / (2 * Mathf.PI * cutoff), dt = 1f / Rate, a = dt / (rc + dt), y = 0;
            for (int i = 0; i < d.Length; i++) { y += a * (d[i] - y); d[i] = y; }
        }

        static void HighPass(float[] d, float cutoff)
        {
            float rc = 1f / (2 * Mathf.PI * cutoff), dt = 1f / Rate, a = rc / (rc + dt), y = 0, px = 0;
            for (int i = 0; i < d.Length; i++) { float x = d[i]; y = a * (y + x - px); px = x; d[i] = y; }
        }

        public static float[] Noise(float seconds, uint seed)
        {
            var d = Buffer(seconds); var r = new Rng(seed);
            for (int i = 0; i < d.Length; i++) d[i] = r.Next();
            return d;
        }

        // ------------------------------------------------------------------ ambience

        public static AudioClip Wind()
        {
            var d = Noise(8, 1); LowPass(d, 380); LowPass(d, 600);
            for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; d[i] *= 0.55f + 0.45f * Mathf.Sin(t * 0.8f) * Mathf.Sin(t * 0.33f + 1); }
            Normalize(d, 0.7f); return Clip("wind", d, true);
        }

        public static AudioClip River()
        {
            var d = Noise(6, 2); HighPass(d, 300); LowPass(d, 2400);
            var r = new Rng(9);
            for (int k = 0; k < 160; k++)
            {
                int at = (int)((r.Next() * 0.5f + 0.5f) * (d.Length - 2000)); float f = 600 + (r.Next() * 0.5f + 0.5f) * 1400;
                for (int i = 0; i < 1200; i++) d[at + i] += Mathf.Sin(i * f * (1 + i / 3000f) * 2 * Mathf.PI / Rate) * Mathf.Exp(-i / 250f) * 0.25f;
            }
            Normalize(d, 0.6f); return Clip("river", d, true);
        }

        public static AudioClip Rain()
        {
            var d = Noise(5, 3); HighPass(d, 900);
            var r = new Rng(4);
            for (int k = 0; k < 900; k++)
            {
                int at = (int)((r.Next() * 0.5f + 0.5f) * (d.Length - 400));
                for (int i = 0; i < 300; i++) d[at + i] += r.Next() * Mathf.Exp(-i / 40f) * 0.8f;
            }
            LowPass(d, 6000);
            Normalize(d, 0.6f); return Clip("rain", d, true);
        }

        public static AudioClip Fire()
        {
            var d = Noise(5, 5); LowPass(d, 250);
            for (int i = 0; i < d.Length; i++) d[i] *= 0.6f;
            var r = new Rng(6);
            for (int k = 0; k < 520; k++)
            {
                int at = (int)((r.Next() * 0.5f + 0.5f) * (d.Length - 600)); float amp = 0.3f + (r.Next() * 0.5f + 0.5f);
                for (int i = 0; i < 500; i++) d[at + i] += r.Next() * Mathf.Exp(-i / 35f) * amp;
            }
            Normalize(d, 0.75f); return Clip("fire", d, true);
        }

        public static AudioClip Crickets()
        {
            var d = Buffer(4);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float chirp = Mathf.Sin(t * 2 * Mathf.PI * 4600) * Mathf.Max(0, Mathf.Sin(t * 2 * Mathf.PI * 30)) * (Mathf.Sin(t * 2 * Mathf.PI * 0.9f) > 0.2f ? 1 : 0);
                float chirp2 = Mathf.Sin(t * 2 * Mathf.PI * 5100) * Mathf.Max(0, Mathf.Sin(t * 2 * Mathf.PI * 26 + 1)) * (Mathf.Sin(t * 2 * Mathf.PI * 0.7f + 2) > 0.4f ? 1 : 0);
                d[i] = chirp * 0.5f + chirp2 * 0.35f;
            }
            Normalize(d, 0.35f); return Clip("crickets", d, true);
        }

        public static AudioClip Bird(int variant)
        {
            var d = Buffer(0.9f + variant * 0.1f);
            var r = new Rng((uint)(variant + 20));
            int notes = 3 + variant % 3;
            float pos = 0.02f;
            for (int n = 0; n < notes; n++)
            {
                float f0 = 2600 + (r.Next() * 0.5f + 0.5f) * 1800, f1 = f0 + r.Next() * 1200, len = 0.06f + (r.Next() * 0.5f + 0.5f) * 0.1f;
                int s0 = (int)(pos * Rate), sl = (int)(len * Rate);
                float ph = 0;
                for (int i = 0; i < sl && s0 + i < d.Length; i++)
                {
                    float u = i / (float)sl;
                    float f = Mathf.Lerp(f0, f1, u) + Mathf.Sin(u * 40) * 120;
                    ph += f * 2 * Mathf.PI / Rate;
                    d[s0 + i] += Mathf.Sin(ph) * Mathf.Sin(u * Mathf.PI);
                }
                pos += len + 0.03f + (r.Next() * 0.5f + 0.5f) * 0.06f;
            }
            Normalize(d, 0.5f); return Clip("bird" + variant, d);
        }

        // ------------------------------------------------------------------ effects

        static float[] Tone(float seconds, Func<float, float> f, Func<float, float> env, float harm2 = 0, float harm3 = 0)
        {
            var d = Buffer(seconds); float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += f(t) * 2 * Mathf.PI / Rate;
                d[i] = (Mathf.Sin(ph) + Mathf.Sin(ph * 2) * harm2 + Mathf.Sin(ph * 3) * harm3) * env(t);
            }
            return d;
        }

        public static AudioClip Click() { var d = Tone(0.06f, t => 1250, t => Mathf.Exp(-t * 90), 0.2f); Normalize(d, 0.4f); return Clip("click", d); }
        public static AudioClip Hover() { var d = Tone(0.05f, t => 1700, t => Mathf.Exp(-t * 120) * 0.5f); Normalize(d, 0.12f); return Clip("hover", d); }
        public static AudioClip Select()
        {
            var a = Tone(0.25f, t => t < 0.08f ? 880 : 1320, t => Mathf.Exp(-t * 14), 0.3f);
            Normalize(a, 0.4f); return Clip("select", a);
        }
        public static AudioClip Error() { var d = Tone(0.22f, t => 180, t => Mathf.Exp(-t * 14), 0.6f, 0.3f); Normalize(d, 0.35f); return Clip("error", d); }

        public static AudioClip Arpeggio(string name, float[] freqs, float noteLen, float decay, float peak = 0.55f)
        {
            var d = Buffer(freqs.Length * noteLen + decay * 1.2f);
            for (int n = 0; n < freqs.Length; n++)
            {
                int s0 = (int)(n * noteLen * Rate);
                float f = freqs[n];
                for (int i = 0; s0 + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t / decay) * Mathf.Min(1, t * 200);
                    d[s0 + i] += (Mathf.Sin(t * f * 2 * Mathf.PI) + 0.35f * Mathf.Sin(t * f * 4 * Mathf.PI) + 0.12f * Mathf.Sin(t * f * 6 * Mathf.PI)) * env;
                }
            }
            Normalize(d, peak); return Clip(name, d);
        }

        public static AudioClip Bell(string name, float f0, float seconds)
        {
            float[] ratios = { 0.5f, 1f, 1.19f, 1.56f, 2f, 2.51f, 3.01f };
            float[] amps = { 0.4f, 1f, 0.5f, 0.35f, 0.3f, 0.15f, 0.1f };
            var d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate; float s = 0;
                for (int k = 0; k < ratios.Length; k++) s += Mathf.Sin(t * f0 * ratios[k] * 2 * Mathf.PI) * amps[k] * Mathf.Exp(-t * (0.8f + k * 0.6f));
                d[i] = s * Mathf.Min(1, t * 400);
            }
            Normalize(d, 0.6f); return Clip(name, d);
        }

        public static AudioClip Tok()
        {
            var n = Noise(0.18f, 11); LowPass(n, 1800);
            var t = Tone(0.18f, x => 260 - x * 400, x => Mathf.Exp(-x * 40));
            for (int i = 0; i < n.Length; i++) n[i] = n[i] * Mathf.Exp(-i / 300f) * 0.8f + t[i];
            Normalize(n, 0.6f); return Clip("tok", n);
        }

        public static AudioClip Whoosh()
        {
            var d = Noise(1.4f, 12);
            float y = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float u = i / (float)d.Length;
                float cutoff = 200 + Mathf.Sin(u * Mathf.PI) * 2200;
                float rc = 1f / (2 * Mathf.PI * cutoff), a = (1f / Rate) / (rc + 1f / Rate);
                y += a * (d[i] - y);
                d[i] = y * Mathf.Sin(u * Mathf.PI);
            }
            Normalize(d, 0.6f); return Clip("whoosh", d);
        }

        public static AudioClip Sparkle()
        {
            var r = new Rng(13);
            var d = Buffer(1.6f);
            for (int k = 0; k < 22; k++)
            {
                float f = 1800 + (r.Next() * 0.5f + 0.5f) * 3200;
                int s0 = (int)((k / 22f) * 0.8f * Rate);
                for (int i = 0; s0 + i < d.Length && i < Rate; i++)
                {
                    float t = i / (float)Rate;
                    d[s0 + i] += Mathf.Sin(t * f * 2 * Mathf.PI) * Mathf.Exp(-t * 7) * 0.4f;
                }
            }
            Normalize(d, 0.45f); return Clip("sparkle", d);
        }

        public static AudioClip Caw()
        {
            var d = Buffer(0.9f); float ph = 0, mph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float seg = (t % 0.42f) / 0.3f;
                float env = seg < 1 ? Mathf.Sin(seg * Mathf.PI) : 0;
                float f = 620 - seg * 160;
                mph += f * 1.5f * 2 * Mathf.PI / Rate;
                ph += (f + Mathf.Sin(mph) * f * 1.8f) * 2 * Mathf.PI / Rate;
                d[i] = Mathf.Sin(ph) * env;
            }
            LowPass(d, 2600);
            Normalize(d, 0.45f); return Clip("caw", d);
        }

        public static AudioClip Thunder()
        {
            var d = Noise(4.5f, 14); LowPass(d, 140); LowPass(d, 180);
            var r = new Rng(15);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] *= Mathf.Exp(-t * 0.9f) * (1 + 0.6f * Mathf.Sin(t * 7 + r.Next() * 0.1f)) * Mathf.Min(1, t * 30);
            }
            Normalize(d, 0.9f); return Clip("thunder", d);
        }

        public static AudioClip Crash()
        {
            var d = Noise(1.2f, 16); LowPass(d, 900);
            for (int i = 0; i < d.Length; i++) d[i] *= Mathf.Exp(-i / (float)Rate * 4.5f);
            Normalize(d, 0.7f); return Clip("crash", d);
        }

        // ------------------------------------------------------------------ music

        static readonly float[] DMajorPent = { 293.66f, 329.63f, 369.99f, 440f, 493.88f, 587.33f, 659.25f, 739.99f, 880f };
        static readonly float[] DMinorPent = { 293.66f, 349.23f, 392f, 440f, 523.25f, 587.33f, 698.46f, 783.99f };

        /// <summary>Generative ambient loop: soft pad chords plus a plucked, wandering pentatonic melody.</summary>
        public static AudioClip Music(string name, int mood, uint seed, float seconds = 32f)
        {
            var d = Buffer(seconds);
            var r = new Rng(seed);
            var scale = mood == 1 || mood == 2 ? DMinorPent : DMajorPent;
            float[][] chords = mood switch
            {
                2 => new[] { new[] { 146.83f, 174.61f, 220f }, new[] { 116.54f, 146.83f, 174.61f }, new[] { 130.81f, 164.81f, 196f }, new[] { 110f, 138.59f, 164.81f } },
                1 => new[] { new[] { 146.83f, 220f, 261.63f }, new[] { 116.54f, 174.61f, 233.08f }, new[] { 130.81f, 196f, 261.63f }, new[] { 98f, 146.83f, 196f } },
                3 => new[] { new[] { 146.83f, 185f, 220f, 277.18f }, new[] { 196f, 246.94f, 293.66f }, new[] { 164.81f, 207.65f, 246.94f }, new[] { 220f, 277.18f, 329.63f } },
                _ => new[] { new[] { 146.83f, 185f, 220f }, new[] { 196f, 246.94f, 293.66f }, new[] { 123.47f, 146.83f, 185f }, new[] { 164.81f, 220f, 277.18f } },
            };
            float chordLen = seconds / chords.Length;
            for (int c = 0; c < chords.Length; c++)
            {
                int s0 = (int)(c * chordLen * Rate), sl = (int)(chordLen * Rate);
                foreach (var f in chords[c])
                    for (int i = 0; i < sl + Rate && s0 + i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float env = Mathf.Min(1, t / 1.6f) * (i < sl ? 1 : Mathf.Max(0, 1 - (i - sl) / (float)Rate));
                        float vib = 1 + Mathf.Sin(t * 4.5f) * 0.002f;
                        d[s0 + i] += (Mathf.Sin(t * f * vib * 2 * Mathf.PI) * 0.6f + Mathf.Sin(t * f * 2.003f * 2 * Mathf.PI) * 0.18f) * env * 0.16f;
                    }
            }
            // melody
            float noteLen = mood == 1 ? 0.75f : mood == 2 ? 0.375f : 0.5f;
            int idx = 3;
            for (float t0 = 0.5f; t0 < seconds - 1; t0 += noteLen * (r.Next() > 0.4f ? 1 : 2))
            {
                if (r.Next() < (mood == 1 ? 0.1f : -0.3f)) continue;
                idx = Mathf.Clamp(idx + (int)Mathf.Round(r.Next() * 2.2f), 0, scale.Length - 1);
                float f = scale[idx] * (mood == 2 ? 0.5f : 1);
                int s0 = (int)(t0 * Rate);
                for (int i = 0; i < Rate * 1.5f && s0 + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t * (mood == 2 ? 5 : 2.6f)) * Mathf.Min(1, t * 300);
                    d[s0 + i] += (Mathf.Sin(t * f * 2 * Mathf.PI) + 0.25f * Mathf.Sin(t * f * 4 * Mathf.PI) + 0.08f * Mathf.Sin(t * f * 6 * Mathf.PI)) * env * 0.22f;
                }
            }
            if (mood == 2)
                for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; d[i] += Mathf.Sin(t * 73.4f * 2 * Mathf.PI) * Mathf.Max(0, Mathf.Sin(t * Mathf.PI * 2 * 1.5f)) * 0.12f; }
            // gentle echo
            int delay = (int)(0.37f * Rate);
            for (int i = delay; i < d.Length; i++) d[i] += d[i - delay] * 0.28f;
            Normalize(d, 0.55f);
            return Clip(name, d, true);
        }
    }
}
