using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>
    /// A screen that eases in each time it is shown: it fades up and settles from slightly small.
    /// Runs on unscaled time, so it also plays while the game is paused.
    /// </summary>
    public sealed class UiAppear : MonoBehaviour
    {
        const float Seconds = 0.24f;
        CanvasGroup _group;
        Transform _content;
        float _time;

        void OnEnable()
        {
            if (_group == null) _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _time = 0f;
            Apply();
        }

        void Update()
        {
            if (_time >= Seconds) return;
            _time += Time.unscaledDeltaTime;
            Apply();
        }

        void Apply()
        {
            // The first child is the safe-area rect every screen builds itself in.
            if (_content == null && transform.childCount > 0) _content = transform.GetChild(0);
            float t = Mathf.Clamp01(_time / Seconds);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            _group.alpha = eased;
            if (_content != null)
            {
                float scale = Mathf.Lerp(0.955f, 1f, eased);
                _content.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }

    /// <summary>A button that gives under the finger and springs back, with a tick of sound.</summary>
    public sealed class ButtonFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        float _scale = 1f, _target = 1f;
        bool _bornSettled;

        void OnEnable()
        {
            _scale = _target = 1f;
            transform.localScale = Vector3.one;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            var button = GetComponent<Button>();
            if (button != null && !button.interactable) return;
            _target = 0.93f;
            UiKit.Tap?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _target = 1f;
        }

        void Update()
        {
            if (Mathf.Abs(_scale - _target) < 0.001f)
            {
                if (!_bornSettled) return;
                _bornSettled = false;
                _scale = _target;
                transform.localScale = new Vector3(_scale, _scale, 1f);
                return;
            }
            _bornSettled = true;
            _scale = Mathf.Lerp(_scale, _target, 1f - Mathf.Exp(-28f * Time.unscaledDeltaTime));
            transform.localScale = new Vector3(_scale, _scale, 1f);
        }
    }

    /// <summary>
    /// The blurred picture of the scene that glass panels show. A second camera follows the main
    /// one into a small render texture, which is shrunk once more; stretched back over the screen
    /// it is as soft as a blur and costs no shader. It runs only while a panel is on screen.
    /// </summary>
    public sealed class GlassBackdrop : MonoBehaviour
    {
        static GlassBackdrop _instance;
        static int _users;
        /// <summary>The cheapest graphics tier turns the effect off; panels are then plainly tinted.</summary>
        public static bool Allowed = true;

        Camera _source, _camera;
        RenderTexture _wide, _soft;

        public static Texture Picture => _instance != null && _instance._camera.enabled ? _instance._soft : null;

        public static void Attach(Camera main)
        {
            if (_instance != null) return;
            var obj = new GameObject("GlassCamera");
            obj.transform.SetParent(main.transform.parent, false);
            _instance = obj.AddComponent<GlassBackdrop>();
            _instance._source = main;
            _instance._camera = obj.AddComponent<Camera>();
            _instance._wide = new RenderTexture(256, 144, 16) { name = "GlassWide", filterMode = FilterMode.Bilinear };
            _instance._soft = new RenderTexture(64, 36, 0) { name = "GlassSoft", filterMode = FilterMode.Bilinear };
            _instance._camera.targetTexture = _instance._wide;
            _instance._camera.depth = main.depth - 10f;
            _instance._camera.enabled = false;
        }

        public static void Use(bool on)
        {
            _users += on ? 1 : -1;
            if (_users < 0) _users = 0;
        }

        void LateUpdate()
        {
            bool on = Allowed && _users > 0 && _source != null && _source.isActiveAndEnabled;
            if (_camera.enabled != on) _camera.enabled = on;
            if (!on) return;
            _camera.transform.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
            _camera.fieldOfView = _source.fieldOfView;
            _camera.nearClipPlane = _source.nearClipPlane;
            _camera.farClipPlane = _source.farClipPlane;
            _camera.clearFlags = _source.clearFlags;
            _camera.backgroundColor = _source.backgroundColor;
            _camera.cullingMask = _source.cullingMask;
            Graphics.Blit(_wide, _soft);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_wide != null) _wide.Release();
            if (_soft != null) _soft.Release();
        }
    }

    /// <summary>Shows the part of the blurred scene that lies behind this panel.</summary>
    public sealed class GlassPanel : MonoBehaviour
    {
        static readonly Vector3[] Corners = new Vector3[4];
        RawImage _image;
        RectTransform _rect;

        void Awake()
        {
            _image = GetComponent<RawImage>();
            _rect = (RectTransform)transform;
        }

        void OnEnable() => GlassBackdrop.Use(true);

        void OnDisable() => GlassBackdrop.Use(false);

        void LateUpdate()
        {
            Texture picture = GlassBackdrop.Picture;
            if (picture == null)
            {
                if (_image.enabled) _image.enabled = false;
                return;
            }
            if (!_image.enabled) _image.enabled = true;
            _image.texture = picture;
            // On an overlay canvas the world corners are screen pixels.
            _rect.GetWorldCorners(Corners);
            float width = Mathf.Max(1f, Screen.width), height = Mathf.Max(1f, Screen.height);
            _image.uvRect = new Rect(Corners[0].x / width, Corners[0].y / height,
                (Corners[2].x - Corners[0].x) / width, (Corners[2].y - Corners[0].y) / height);
        }
    }

    /// <summary>Text that arrives a few characters at a time, as if typed over a teleprinter.</summary>
    public sealed class Typewriter : MonoBehaviour
    {
        const float CharactersPerSecond = 110f;
        Text _text;
        string _full = "";
        float _shown;

        public void Type(string text)
        {
            if (_text == null) _text = GetComponent<Text>();
            _full = text ?? "";
            _shown = 0f;
            _text.text = "";
        }

        /// <summary>Shows the whole text at once.</summary>
        public void Finish(string text)
        {
            if (_text == null) _text = GetComponent<Text>();
            // Mid-typing, a refresh of the screen must not jump to the end.
            if (_full == text && _shown < _full.Length) return;
            _full = text ?? "";
            _shown = _full.Length;
            _text.text = _full;
        }

        void Update()
        {
            if (_text == null || _shown >= _full.Length) return;
            int before = (int)_shown;
            _shown = Mathf.Min(_full.Length, _shown + CharactersPerSecond * Time.unscaledDeltaTime);
            int now = (int)_shown;
            if (now == before) return;
            _text.text = now >= _full.Length ? _full : _full.Substring(0, now) + "_";
        }
    }

    /// <summary>A band of light that climbs the tactical map again and again.</summary>
    public sealed class MapSweep : MonoBehaviour
    {
        public float Height = 380f;
        RectTransform _rect;

        void Update()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            float t = Mathf.Repeat(Time.unscaledTime * 0.22f, 1f);
            _rect.anchoredPosition = new Vector2(0f, t * (Height + 70f));
        }
    }
}
