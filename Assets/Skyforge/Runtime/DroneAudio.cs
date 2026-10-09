using UnityEngine;

namespace Skyforge
{
    /// <summary>Motor hum and wind, synthesised at start so the game ships without audio files.</summary>
    public sealed class DroneAudio
    {
        const int SampleRate = 44100;
        readonly AudioSource _motor, _wind;

        public DroneAudio(Transform parent)
        {
            _motor = CreateSource(parent, "MotorAudio", MotorClip());
            _wind = CreateSource(parent, "WindAudio", WindClip());
            _motor.volume = 0f;
            _wind.volume = 0f;
        }

        static AudioSource CreateSource(Transform parent, string name, AudioClip clip)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var source = obj.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.Play();
            return source;
        }

        /// <param name="load">0 idle .. 1 full effort.</param>
        /// <param name="speedShare">0 hovering .. 1 top speed.</param>
        public void Update(float load, float speedShare, bool running, float deltaTime)
        {
            float ease = 1f - Mathf.Exp(-8f * deltaTime);
            _motor.pitch = Mathf.Lerp(_motor.pitch, 0.75f + load * 0.75f, ease);
            _motor.volume = Mathf.Lerp(_motor.volume, running ? 0.20f + load * 0.16f : 0f, ease);
            _wind.volume = Mathf.Lerp(_wind.volume, running ? speedShare * speedShare * 0.45f : 0f, ease);
            _wind.pitch = 0.8f + speedShare * 0.5f;
        }

        /// <summary>Four slightly detuned rotors. Whole-number frequencies make the one-second loop seamless.</summary>
        static AudioClip MotorClip()
        {
            int[] frequencies = { 148, 151, 155, 160 };
            var samples = new float[SampleRate];
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)SampleRate;
                float value = 0f;
                foreach (int frequency in frequencies)
                {
                    for (int harmonic = 1; harmonic <= 6; harmonic++)
                        value += Mathf.Sin(2f * Mathf.PI * frequency * harmonic * time) / (harmonic * 1.3f);
                }
                samples[i] = value * 0.09f;
            }
            return CreateClip("Motor", samples);
        }

        /// <summary>Low-passed noise; the ends are cross-faded so the loop does not click.</summary>
        static AudioClip WindClip()
        {
            int length = SampleRate * 2;
            int fade = SampleRate / 4;
            var noise = new float[length + fade];
            var random = new System.Random(5);
            float low = 0f, lower = 0f;
            for (int i = 0; i < noise.Length; i++)
            {
                float white = (float)random.NextDouble() * 2f - 1f;
                low += (white - low) * 0.06f;
                lower += (low - lower) * 0.02f;
                noise[i] = (low * 0.6f + lower * 1.6f) * 1.8f;
            }

            var samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float value = noise[i];
                if (i < fade)
                {
                    float blend = i / (float)fade;
                    value = value * blend + noise[length + i] * (1f - blend);
                }
                samples[i] = Mathf.Clamp(value, -1f, 1f);
            }
            return CreateClip("Wind", samples);
        }

        static AudioClip CreateClip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
