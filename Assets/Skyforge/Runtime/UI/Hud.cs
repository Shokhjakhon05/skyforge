using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>Flight HUD built from code: telemetry, buttons, stick visuals and the target marker.</summary>
    public sealed class Hud
    {
        public event Action CameraPressed, ModePressed, SticksPressed, ResetPressed;

        static readonly Color PanelColor = new Color(0.05f, 0.08f, 0.12f, 0.55f);
        static readonly Color Accent = new Color(1f, 0.62f, 0.10f, 1f);

        readonly Canvas _canvas;
        readonly Font _font;
        readonly Text _speed, _altitude, _course, _best, _message, _hint, _markerText;
        readonly Text _cameraLabel, _modeLabel;
        readonly RectTransform _marker;
        readonly StickVisual _left, _right;
        int _shownSpeed = -1, _shownAltitude = -1, _shownDistance = -1;

        sealed class StickVisual
        {
            public RectTransform Base, Knob;
            public Image BaseImage, KnobImage;
            public Text Label;
        }

        public Hud(Transform parent)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasObject = new GameObject("Hud");
            canvasObject.transform.SetParent(parent, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var events = new GameObject("EventSystem");
            events.transform.SetParent(parent, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();

            // Sticks and the marker are placed in raw screen coordinates, under everything else.
            var screen = CreateRect("Screen", canvasObject.transform);
            Stretch(screen);
            Sprite disc = CreateDiscSprite(false), ring = CreateDiscSprite(true);
            _left = CreateStick(screen, disc, ring);
            _right = CreateStick(screen, disc, ring);

            _marker = CreateRect("Marker", screen);
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            _marker.sizeDelta = new Vector2(34f, 34f);
            var markerImage = CreateRect("Diamond", _marker);
            Stretch(markerImage);
            markerImage.localRotation = Quaternion.Euler(0f, 0f, 45f);
            markerImage.gameObject.AddComponent<Image>().color = Accent;
            _markerText = CreateText(_marker, 30, TextAnchor.UpperCenter);
            Place(_markerText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -14f), new Vector2(240f, 40f));

            // Everything else stays inside the safe area, clear of the camera cut-out.
            var safe = CreateRect("Safe", canvasObject.transform);
            Rect area = Screen.safeArea;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;

            _speed = CreateText(safe, 64, TextAnchor.UpperLeft);
            Place(_speed.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -28f), new Vector2(420f, 76f));
            _altitude = CreateText(safe, 40, TextAnchor.UpperLeft);
            Place(_altitude.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -104f), new Vector2(420f, 50f));

            _course = CreateText(safe, 40, TextAnchor.UpperCenter);
            Place(_course.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(700f, 50f));
            _best = CreateText(safe, 30, TextAnchor.UpperCenter);
            _best.color = new Color(1f, 1f, 1f, 0.75f);
            Place(_best.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(700f, 40f));

            _message = CreateText(safe, 84, TextAnchor.MiddleCenter);
            _message.color = Accent;
            Place(_message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(1500f, 110f));
            _hint = CreateText(safe, 40, TextAnchor.MiddleCenter);
            Place(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(1500f, 56f));

            _cameraLabel = CreateButton(safe, 0, () => CameraPressed?.Invoke());
            _modeLabel = CreateButton(safe, 1, () => ModePressed?.Invoke());
            CreateButton(safe, 2, () => SticksPressed?.Invoke()).text = Loc.T("hud.sticks");
            CreateButton(safe, 3, () => ResetPressed?.Invoke()).text = Loc.T("hud.reset");
        }

        public void SetTelemetry(float speedKmh, float altitude)
        {
            int speed = Mathf.RoundToInt(speedKmh), alt = Mathf.RoundToInt(altitude);
            if (speed != _shownSpeed) _speed.text = Loc.T("hud.speed", _shownSpeed = speed);
            if (alt != _shownAltitude) _altitude.text = Loc.T("hud.altitude", _shownAltitude = alt);
        }

        public void SetCourse(string text) => _course.text = text;

        public void SetBest(string text) => _best.text = text;
        public void SetMessage(string text) => _message.text = text;
        public void SetHint(string text) => _hint.text = text;
        public void SetCameraLabel(string text) => _cameraLabel.text = text;
        public void SetModeLabel(string text) => _modeLabel.text = text;

        /// <summary>Shows where each stick is, or where it will appear when nothing touches the screen.</summary>
        public void UpdateSticks(TouchSticks sticks, string leftLabel, string rightLabel)
        {
            float radius = sticks.RadiusPixels;
            var restLeft = new Vector2(Screen.width * 0.16f, Screen.height * 0.30f);
            var restRight = new Vector2(Screen.width * 0.84f, Screen.height * 0.30f);
            UpdateStick(_left, sticks.Left, restLeft, radius, leftLabel);
            UpdateStick(_right, sticks.Right, restRight, radius, rightLabel);
        }

        void UpdateStick(StickVisual visual, TouchSticks.Stick stick, Vector2 rest, float radius, string label)
        {
            float scale = _canvas.scaleFactor;
            Vector2 origin = stick.Active ? stick.Origin : rest;
            Vector2 knob = stick.Active ? stick.Knob : rest;
            visual.Base.anchoredPosition = origin / scale;
            visual.Base.sizeDelta = Vector2.one * (radius * 2f / scale);
            visual.Knob.anchoredPosition = knob / scale;
            visual.Knob.sizeDelta = Vector2.one * (radius * 0.8f / scale);
            visual.BaseImage.color = new Color(1f, 1f, 1f, stick.Active ? 0.50f : 0.22f);
            visual.KnobImage.color = new Color(1f, 1f, 1f, stick.Active ? 0.60f : 0.25f);
            if (visual.Label.text != label) visual.Label.text = label;
            visual.Label.enabled = !stick.Active;
        }

        /// <summary>Points at a world position; sticks to the screen edge when the target is out of view.</summary>
        public void UpdateMarker(Camera camera, Vector3 world, bool visible)
        {
            _marker.gameObject.SetActive(visible);
            if (!visible) return;

            Vector3 point = camera.WorldToScreenPoint(world);
            float margin = Screen.height * 0.07f;
            if (point.z < 0f)
            {
                // Behind the camera: mirror it and pin it to the bottom so it reads as "turn around".
                point.x = Screen.width - point.x;
                point.y = margin;
            }
            point.x = Mathf.Clamp(point.x, margin, Screen.width - margin);
            point.y = Mathf.Clamp(point.y, margin, Screen.height * 0.72f);
            _marker.anchoredPosition = new Vector2(point.x, point.y) / _canvas.scaleFactor;

            int distance = Mathf.RoundToInt(Vector3.Distance(camera.transform.position, world) / 5f) * 5;
            if (distance != _shownDistance) _markerText.text = Loc.T("hud.distance", _shownDistance = distance);
        }

        StickVisual CreateStick(RectTransform parent, Sprite disc, Sprite ring)
        {
            var visual = new StickVisual();
            visual.Base = CreateRect("StickBase", parent);
            visual.Base.anchorMin = visual.Base.anchorMax = Vector2.zero;
            visual.BaseImage = visual.Base.gameObject.AddComponent<Image>();
            visual.BaseImage.sprite = ring;
            visual.BaseImage.raycastTarget = false;

            visual.Knob = CreateRect("StickKnob", parent);
            visual.Knob.anchorMin = visual.Knob.anchorMax = Vector2.zero;
            visual.KnobImage = visual.Knob.gameObject.AddComponent<Image>();
            visual.KnobImage.sprite = disc;
            visual.KnobImage.raycastTarget = false;

            visual.Label = CreateText(visual.Base, 28, TextAnchor.MiddleCenter);
            visual.Label.color = new Color(1f, 1f, 1f, 0.7f);
            Place(visual.Label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -30f), new Vector2(620f, 40f));
            return visual;
        }

        /// <summary>Buttons sit in a row along the top right, in the band the sticks ignore.</summary>
        Text CreateButton(RectTransform parent, int index, Action onClick)
        {
            const float width = 300f, height = 84f, gap = 14f;
            var rect = CreateRect("Button" + index, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            // Two columns of two, so the row does not reach the middle of the screen.
            int column = index % 2, row = index / 2;
            rect.anchoredPosition = new Vector2(-36f - column * (width + gap), -26f - row * (height + gap));
            rect.sizeDelta = new Vector2(width, height);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = PanelColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            Text label = CreateText(rect, 30, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return label;
        }

        Text CreateText(RectTransform parent, int size, TextAnchor anchor)
        {
            var rect = CreateRect("Text", parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return (RectTransform)obj.transform;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>Anchors and pivots a rect at one point of its parent.</summary>
        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static Sprite CreateDiscSprite(bool hollow)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = hollow ? "Ring" : "Disc" };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f, outer = size * 0.5f - 1f, inner = outer - 5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                float alpha = Mathf.Clamp01(outer - distance);
                if (hollow) alpha *= Mathf.Lerp(0.18f, 1f, Mathf.Clamp01(distance - inner));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
