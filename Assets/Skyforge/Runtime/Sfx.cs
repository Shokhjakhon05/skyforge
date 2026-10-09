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
                Tones("Click", 0.05f, 0.5f, 1400f),
                Tones("Scan", 0.12f, 0.5f, 880f, 1320f),
                Tones("Success", 0.14f, 0.55f, 660f, 880f, 1100f, 1320f),
                Crash(),
                Tones("Warning", 0.10f, 0.45f, 520f, 0f, 520f),
            };
        }

        public void Play(Sound sound) => _source.PlayOneShot(_clips[(int)sound]);

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
