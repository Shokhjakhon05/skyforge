using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Skyforge
{
    /// <summary>
    /// A character brought in with its skeleton and its animation clips. Plays one clip at a time,
    /// by name, without an animator controller: the clip is driven by hand through a playable graph
    /// (or through the legacy Animation component, if that is how the clips were imported).
    /// </summary>
    public sealed class Actor : MonoBehaviour
    {
        public AnimationClip[] Clips;
        /// <summary>Height of the model as imported, in its own units, so it can be scaled to a man.</summary>
        public float Height = 1.8f;
        /// <summary>Bones of a motion-captured skeleton, where the model has one: the game hangs a rifle on the hands.</summary>
        public Transform Hips, RightHand, LeftHand, HeadBone;
        /// <summary>The clips were recorded on one body; a taller or shorter one carries its hips higher or lower by this much.</summary>
        public float HipScale = 1f;

        PlayableGraph _graph;
        AnimationClipPlayable _playable;
        AnimationClip _clip;
        Animation _legacy;
        float _time;
        bool _loop;
        public float Speed = 1f;

        public string Current => _clip != null ? _clip.name : null;
        public bool Finished => _clip != null && !_loop && _time >= _clip.length;

        void Awake()
        {
            // Nothing plays until the game says what: the importer's player would start the first clip on its own.
            Animation player = GetComponentInChildren<Animation>();
            if (player != null) player.playAutomatically = false;
        }

        public AnimationClip Find(string name)
        {
            if (Clips == null) return null;
            foreach (AnimationClip clip in Clips)
                if (clip != null && (clip.name == name || clip.name.EndsWith("|" + name))) return clip;
            return null;
        }

        public bool Play(string name, bool loop = true, float from = 0f)
        {
            AnimationClip clip = Find(name);
            if (clip == null) return false;
            _clip = clip;
            _loop = loop;
            _time = from;
            if (clip.legacy)
            {
                if (_legacy == null) _legacy = GetComponentInChildren<Animation>() ?? gameObject.AddComponent<Animation>();
                _legacy.playAutomatically = false;
                if (_legacy.GetClip(clip.name) == null) _legacy.AddClip(clip, clip.name);
                _legacy[clip.name].wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
                _legacy[clip.name].time = from;
                // One movement runs into the next rather than snapping to it.
                if (_legacy.isPlaying && Hips != null) _legacy.CrossFade(clip.name, 0.22f);
                else _legacy.Play(clip.name);
                return true;
            }
            Animator animator = GetComponent<Animator>() ?? gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            if (_graph.IsValid()) _graph.Destroy();
            _graph = PlayableGraph.Create("Actor");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _playable = AnimationClipPlayable.Create(_graph, clip);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Out", animator);
            output.SetSourcePlayable(_playable);
            Apply();
            return true;
        }

        void Apply()
        {
            if (!_graph.IsValid() || _clip == null) return;
            float t = _loop ? Mathf.Repeat(_time, Mathf.Max(0.01f, _clip.length)) : Mathf.Min(_time, _clip.length);
            _playable.SetTime(t);
            _graph.Evaluate(0f);
        }

        void Update()
        {
            if (_clip == null) return;
            _time += Time.deltaTime * Speed;
            if (_clip.legacy)
            {
                if (_legacy != null && _legacy[_clip.name] != null) _legacy[_clip.name].speed = Speed;
                return;
            }
            Apply();
        }

        void LateUpdate()
        {
            if (Hips == null || _legacy == null || !_legacy.isPlaying || Mathf.Approximately(HipScale, 1f)) return;
            Hips.localPosition *= HipScale;
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
