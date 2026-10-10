using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// The score: a quiet bed under the menus and a harder track under a fight. Two sources, so
    /// one track can fade out while the next fades in. The files are streamed from Resources/Music.
    /// </summary>
    public sealed class Music : MonoBehaviour
    {
        const float FadeSeconds = 1.6f;
        const int MenuTracks = 2, BattleTracks = 4;

        AudioSource _playing, _leaving;
        string _current;
        float _level = 0.42f;

        /// <summary>0 silences the score without touching the other sounds.</summary>
        public float Level
        {
            get => _level;
            set => _level = Mathf.Clamp01(value);
        }

        public static Music Create(Transform parent)
        {
            var obj = new GameObject("Music");
            obj.transform.SetParent(parent, false);
            var music = obj.AddComponent<Music>();
            music._playing = music.Source();
            music._leaving = music.Source();
            return music;
        }

        AudioSource Source()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            source.priority = 0;
            return source;
        }

        /// <summary>The bed under the menus; which one depends on the region, so theatres sound different.</summary>
        public void Menu(int variety) => Play("menu_" + (Mathf.Abs(variety) % MenuTracks + 1), 0.8f);

        /// <summary>A track for a fight, picked by the mission so the same operation always has the same one.</summary>
        public void Battle(int variety) => Play("battle_" + (Mathf.Abs(variety) % BattleTracks + 1), 1f);

        /// <summary>Wind and motors only.</summary>
        public void Silence() => Play(null, 0f);

        float _gain = 1f;

        void Play(string track, float gain)
        {
            if (track == _current) return;
            _current = track;
            (_playing, _leaving) = (_leaving, _playing);
            _gain = gain;
            if (track == null)
            {
                _playing.clip = null;
                return;
            }
            var clip = Resources.Load<AudioClip>("Music/" + track);
            if (clip == null)
            {
                Debug.LogWarning("[Skyforge] Missing music " + track);
                return;
            }
            _playing.clip = clip;
            _playing.volume = 0f;
            _playing.Play();
        }

        void Update()
        {
            float step = Time.unscaledDeltaTime / FadeSeconds;
            if (_playing.clip != null)
                _playing.volume = Mathf.MoveTowards(_playing.volume, _level * _gain, step);
            if (_leaving.isPlaying)
            {
                _leaving.volume = Mathf.MoveTowards(_leaving.volume, 0f, step);
                if (_leaving.volume <= 0.001f) _leaving.Stop();
            }
        }
    }
}
