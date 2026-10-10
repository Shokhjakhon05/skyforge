using UnityEngine;

namespace Skyforge
{
    public enum Sound
    {
        Click,
        Scan,
        Success,
        Crash,
        Warning,
        Rocket,
        Explosion,
        Gun,
        Release,
        Lock,
        Tap,
        Open,
        Radio,
    }

    /// <summary>Short interface and event sounds, synthesised at start like the motor hum.</summary>
    public sealed class Sfx
    {
        const int SampleRate = 44100;
        readonly AudioSource _source;
        readonly AudioClip[] _clips;

        public Sfx(Transform parent)
        {
            var obj = new GameObject("Sfx");
            obj.transform.SetParent(parent, false);
            _source = obj.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;

            _clips = new[]
            {
                Tones("Click", 0.035f, 0.42f, 1250f, 1880f),
                Tones("Scan", 0.12f, 0.5f, 880f, 1320f),
                Tones("Success", 0.14f, 0.55f, 660f, 880f, 1100f, 1320f),
                Crash(),
                Tones("Warning", 0.10f, 0.45f, 520f, 0f, 520f),
                Rocket(),
                Explosion(),
                Gun(),
                Tones("Release", 0.07f, 0.5f, 300f, 210f),
                Tones("Lock", 0.05f, 0.4f, 1760f, 0f, 1760f),
                Tones("Tap", 0.022f, 0.30f, 2400f, 1900f),
                Sweep("Open", 0.20f, 0.22f, 520f, 1500f),
                Radio(),
            };
        }

        public void Play(Sound sound, float volume = 1f)
        {
            if (volume > 0.02f) _source.PlayOneShot(_clips[(int)sound], Mathf.Clamp01(volume));
        }

        /// <summary>A rocket leaving the rail: a hiss that falls away.</summary>
        static AudioClip Rocket()
        {
            var samples = new float[SampleRate * 6 / 10];
            var random = new System.Random(3);
            float band = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float position = i / (float)samples.Length;
                float white = (float)random.NextDouble() * 2f - 1f;
                // The filter closes as the rocket goes: bright at the start, a rumble at the end.
                band += (white - band) * Mathf.Lerp(0.55f, 0.06f, position);
                float envelope = Mathf.Min(1f, position * 40f) * (1f - position);
                samples[i] = Mathf.Clamp(band * 1.6f * envelope, -1f, 1f) * 0.75f;
            }
            return Create("Rocket", samples);
        }

        /// <summary>A crack, then a long low roll.</summary>
        static AudioClip Explosion()
        {
            var samples = new float[SampleRate * 16 / 10];
            var random = new System.Random(17);
            float low = 0f, lower = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)SampleRate;
                float position = i / (float)samples.Length;
                float white = (float)random.NextDouble() * 2f - 1f;
                low += (white - low) * 0.18f;
                lower += (low - lower) * 0.05f;
                float crack = white * Mathf.Exp(-time * 38f);
                float thump = Mathf.Sin(2f * Mathf.PI * (62f - 34f * position) * time) * Mathf.Exp(-time * 3.2f);
                float roll = (low * 0.9f + lower * 2.6f) * Mathf.Exp(-time * 2.4f);
                samples[i] = Mathf.Clamp(crack * 0.7f + thump * 0.9f + roll * 1.5f, -1f, 1f) * 0.9f;
            }
            return Create("Explosion", samples);
        }

        /// <summary>Four rounds from a heavy gun.</summary>
        static AudioClip Gun()
        {
            var samples = new float[SampleRate * 34 / 100];
            var random = new System.Random(29);
            float low = 0f;
            int spacing = SampleRate * 8 / 100;
            for (int i = 0; i < samples.Length; i++)
            {
                float sinceShot = (i % spacing) / (float)SampleRate;
                float white = (float)random.NextDouble() * 2f - 1f;
                low += (white - low) * 0.35f;
                float envelope = Mathf.Exp(-sinceShot * 55f);
                float thump = Mathf.Sin(2f * Mathf.PI * 140f * sinceShot);
                samples[i] = Mathf.Clamp((low * 1.2f + thump * 0.6f) * envelope, -1f, 1f) * 0.7f;
            }
            return Create("Gun", samples);
        }

        /// <summary>Notes played one after another; a frequency of zero is a rest.</summary>
        static AudioClip Tones(string name, float noteSeconds, float volume, params float[] frequencies)
        {
            int noteLength = Mathf.RoundToInt(noteSeconds * SampleRate);
            var samples = new float[noteLength * frequencies.Length];
            for (int note = 0; note < frequencies.Length; note++)
            {
                if (frequencies[note] <= 0f) continue;
                for (int i = 0; i < noteLength; i++)
                {
                    float time = i / (float)SampleRate;
                    float position = i / (float)noteLength;
                    // Quick attack, smooth release: no clicks at the note edges.
                    float envelope = Mathf.Min(1f, position * 20f) * (1f - position) * (1f - position);
                    float wave = Mathf.Sin(2f * Mathf.PI * frequencies[note] * time) +
                                 0.3f * Mathf.Sin(4f * Mathf.PI * frequencies[note] * time);
                    samples[note * noteLength + i] = wave * envelope * volume * 0.7f;
                }
            }
            return Create(name, samples);
        }

        /// <summary>A tone that glides from one pitch to another: a panel sliding open.</summary>
        static AudioClip Sweep(string name, float seconds, float volume, float from, float to)
        {
            var samples = new float[Mathf.RoundToInt(seconds * SampleRate)];
            float phase = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float position = i / (float)samples.Length;
                phase += 2f * Mathf.PI * Mathf.Lerp(from, to, position * position) / SampleRate;
                float envelope = Mathf.Min(1f, position * 12f) * (1f - position) * (1f - position);
                samples[i] = (Mathf.Sin(phase) + 0.25f * Mathf.Sin(phase * 2f)) * envelope * volume;
            }
            return Create(name, samples);
        }

        /// <summary>A radio keying up: a click and a breath of static.</summary>
        static AudioClip Radio()
        {
            var samples = new float[SampleRate * 22 / 100];
            var random = new System.Random(41);
            float band = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float position = i / (float)samples.Length;
                float white = (float)random.NextDouble() * 2f - 1f;
                band += (white - band) * 0.5f;
                float hiss = (white - band) * 0.35f * (position < 0.85f ? 1f : (1f - position) / 0.15f);
                float click = position < 0.04f ? Mathf.Sin(position * 900f) * (1f - position / 0.04f) : 0f;
                samples[i] = (hiss + click * 0.8f) * 0.5f;
            }
            return Create("Radio", samples);
        }

        /// <summary>A burst of noise over a falling low thump.</summary>
        static AudioClip Crash()
        {
            var samples = new float[SampleRate * 7 / 10];
            var random = new System.Random(11);
            float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)SampleRate;
                float position = i / (float)samples.Length;
                float white = (float)random.NextDouble() * 2f - 1f;
                low += (white - low) * 0.25f;
                float thump = Mathf.Sin(2f * Mathf.PI * (90f - 50f * position) * time);
                float envelope = (1f - position) * (1f - position);
                samples[i] = Mathf.Clamp((low * 1.1f + thump * 0.7f) * envelope, -1f, 1f) * 0.8f;
            }
            return Create("Crash", samples);
        }

        static AudioClip Create(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
