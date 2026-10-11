using System.Collections.Generic;
using UnityEngine;

namespace Sniper
{
    /// <summary>Recorded sounds by name: the rifle, the bolt, a round landing, the noise of the place.</summary>
    public sealed class SniperAudio
    {
        readonly AudioSource[] _voices = new AudioSource[8];
        readonly AudioSource _ambience;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly List<KeyValuePair<float, string>> _later = new List<KeyValuePair<float, string>>();
        readonly List<float> _laterVolume = new List<float>();
        int _next;
        string _ambient;

        public SniperAudio(Transform parent)
        {
            var root = new GameObject("Audio");
            root.transform.SetParent(parent, false);
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = root.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
                _voices[i].spatialBlend = 0f;
            }
            _ambience = root.AddComponent<AudioSource>();
            _ambience.playOnAwake = false;
            _ambience.loop = true;
            _ambience.spatialBlend = 0f;
        }

        AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out AudioClip clip)) return clip;
            clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null) Debug.LogWarning("[Sniper] Missing sound " + name);
            _clips[name] = clip;
            return clip;
        }

        public void Play(string name, float volume = 1f, float pitch = 1f)
        {
            AudioClip clip = Clip(name);
            if (clip == null) return;
            AudioSource voice = _voices[_next];
            _next = (_next + 1) % _voices.Length;
            voice.pitch = pitch;
            voice.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>A sound that belongs a moment after what caused it: the bolt after the shot, the body after the hit.</summary>
        public void PlayLater(string name, float seconds, float volume = 1f)
        {
            _later.Add(new KeyValuePair<float, string>(seconds, name));
            _laterVolume.Add(volume);
        }

        public void Ambience(string name, float volume)
        {
            if (name == _ambient)
            {
                _ambience.volume = volume;
                return;
            }
            _ambient = name;
            AudioClip clip = name != null ? Clip(name) : null;
            _ambience.Stop();
            _ambience.clip = clip;
            _ambience.volume = volume;
            if (clip != null) _ambience.Play();
        }

        public void Update(float realSeconds)
        {
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                float left = _later[i].Key - realSeconds;
                if (left > 0f)
                {
                    _later[i] = new KeyValuePair<float, string>(left, _later[i].Value);
                    continue;
                }
                Play(_later[i].Value, _laterVolume[i]);
                _later.RemoveAt(i);
                _laterVolume.RemoveAt(i);
            }
        }
    }
}
