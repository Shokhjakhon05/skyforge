using System;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>Flight HUD: telemetry, gauges, the current goal, buttons, stick visuals and the target marker.</summary>
    public sealed class Hud
    {
        public event Action CameraPressed, ModePressed, SticksPressed, MenuPressed;

        readonly Canvas _canvas;
        readonly Text _speed, _altitude, _headline, _subline, _message, _hint, _markerText;
        readonly Text _cameraLabel, _modeLabel;
        readonly RectTransform _marker;
        readonly StickVisual _left, _right;
        readonly Gauge _battery, _signal, _hull;
        readonly GameObject _gauges;
        int _shownSpeed = -1, _shownAltitude = -1, _shownDistance = -1;

        sealed class StickVisual
        {
            public RectTransform Base, Knob;
            public Image BaseImage, KnobImage;
            public Text Label;
        }

        sealed class Gauge
        {
            public Text Label;
            public RectTransform Fill;
            public Image FillImage;
            public string Name;
            int _shownPercent = -1;
            bool _shownLow;

            /// <summary>A low gauge turns red and gains an exclamation mark, so it does not rely on colour.</summary>
            public void Set(float value, bool low)
            {
                int percent = Mathf.CeilToInt(Mathf.Clamp01(value) * 100f);
                if (percent == _shownPercent && low == _shownLow) return;
                _shownPercent = percent;
                _shownLow = low;
                Fill.anchorMax = new Vector2(percent / 100f, 1f);
                FillImage.color = low ? UiKit.Bad : Color.white;
                Label.text = Name + " " + percent + "%" + (low ? " !" : "");
                Label.color = low ? UiKit.Bad : Color.white;
            }
        }

        public Hud(Transform parent)
        {
            _canvas = UiKit.CreateCanvas(parent, "Hud", 0);

            // Sticks and the marker are placed in raw screen coordinates, under everything else.
            RectTransform screen = UiKit.Rect("Screen", _canvas.transform);
            UiKit.Stretch(screen);
            _left = CreateStick(screen);
            _right = CreateStick(screen);

            _marker = UiKit.Rect("Marker", screen);
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            _marker.sizeDelta = new Vector2(34f, 34f);
            Image diamond = UiKit.Box(_marker, "Diamond", UiKit.Accent);
            UiKit.Stretch(diamond.rectTransform);
            diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _markerText = UiKit.Label(_marker, 30, TextAnchor.UpperCenter);
            UiKit.Place(_markerText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -14f), new Vector2(240f, 40f));

            // Everything else stays inside the safe area, clear of the camera cut-out.
            RectTransform safe = UiKit.SafeArea(_canvas.transform);
            var topLeft = new Vector2(0f, 1f);
            var topCenter = new Vector2(0.5f, 1f);
            var middle = new Vector2(0.5f, 0.5f);

            _speed = UiKit.Label(safe, 64, TextAnchor.UpperLeft);
            UiKit.Place(_speed.rectTransform, topLeft, new Vector2(40f, -28f), new Vector2(420f, 76f));
            _altitude = UiKit.Label(safe, 40, TextAnchor.UpperLeft);
            UiKit.Place(_altitude.rectTransform, topLeft, new Vector2(40f, -104f), new Vector2(420f, 50f));

            RectTransform gauges = UiKit.Rect("Gauges", safe);
            UiKit.Place(gauges, topLeft, new Vector2(40f, -168f), new Vector2(330f, 150f));
            _gauges = gauges.gameObject;
            _battery = CreateGauge(gauges, 0, Loc.T("hud.battery"));
            _signal = CreateGauge(gauges, 1, Loc.T("hud.signal"));
            _hull = CreateGauge(gauges, 2, Loc.T("hud.hull"));

            _headline = UiKit.Label(safe, 40, TextAnchor.UpperCenter);
            UiKit.Place(_headline.rectTransform, topCenter, new Vector2(0f, -28f), new Vector2(900f, 50f));
            _subline = UiKit.Label(safe, 30, TextAnchor.UpperCenter);
            _subline.color = new Color(1f, 1f, 1f, 0.75f);
            UiKit.Place(_subline.rectTransform, topCenter, new Vector2(0f, -78f), new Vector2(900f, 40f));

            _message = UiKit.Label(safe, 84, TextAnchor.MiddleCenter);
            _message.color = UiKit.Accent;
            UiKit.Place(_message.rectTransform, middle, new Vector2(0f, 250f), new Vector2(1500f, 110f));
            _hint = UiKit.Label(safe, 40, TextAnchor.MiddleCenter);
            UiKit.Place(_hint.rectTransform, middle, new Vector2(0f, 170f), new Vector2(1500f, 56f));

            _cameraLabel = CreateButton(safe, 0, () => CameraPressed?.Invoke());
            _modeLabel = CreateButton(safe, 1, () => ModePressed?.Invoke());
            CreateButton(safe, 2, () => SticksPressed?.Invoke()).text = Loc.T("hud.sticks");
            CreateButton(safe, 3, () => MenuPressed?.Invoke()).text = Loc.T("hud.menu");
        }

        public bool Visible
        {
            set => _canvas.gameObject.SetActive(value);
        }

        public bool GaugesVisible
        {
            set => _gauges.SetActive(value);
        }

        public void SetTelemetry(float speedKmh, float altitude)
        {
            int speed = Mathf.RoundToInt(speedKmh), alt = Mathf.RoundToInt(altitude);
            if (speed != _shownSpeed) _speed.text = Loc.T("hud.speed", _shownSpeed = speed);
            if (alt != _shownAltitude) _altitude.text = Loc.T("hud.altitude", _shownAltitude = alt);
        }

        public void SetGauges(float battery, bool batteryLow, float signal, bool signalLow, float hull)
        {
            _battery.Set(battery, batteryLow);
            _signal.Set(signal, signalLow);
            _hull.Set(hull, hull < 0.4f);
        }

        public void SetHeadline(string text) => _headline.text = text;
        public void SetSubline(string text) => _subline.text = text;
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
            if (_marker.gameObject.activeSelf != visible) _marker.gameObject.SetActive(visible);
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

        static StickVisual CreateStick(RectTransform parent)
        {
            var visual = new StickVisual();
            visual.BaseImage = UiKit.Box(parent, "StickBase", Color.white, UiKit.Ring);
            visual.Base = visual.BaseImage.rectTransform;
            visual.Base.anchorMin = visual.Base.anchorMax = Vector2.zero;

            visual.KnobImage = UiKit.Box(parent, "StickKnob", Color.white, UiKit.Disc);
            visual.Knob = visual.KnobImage.rectTransform;
            visual.Knob.anchorMin = visual.Knob.anchorMax = Vector2.zero;

            visual.Label = UiKit.Label(visual.Base, 28, TextAnchor.MiddleCenter);
            visual.Label.color = new Color(1f, 1f, 1f, 0.7f);
            UiKit.Place(visual.Label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -30f), new Vector2(620f, 40f));
            return visual;
        }

        static Gauge CreateGauge(RectTransform parent, int row, string name)
        {
            var gauge = new Gauge { Name = name };
            float y = -row * 48f;
            gauge.Label = UiKit.Label(parent, 26, TextAnchor.UpperLeft);
            UiKit.Place(gauge.Label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(330f, 30f));

            Image back = UiKit.Box(parent, "Bar", UiKit.Faint);
            UiKit.Place(back.rectTransform, new Vector2(0f, 1f), new Vector2(0f, y - 32f), new Vector2(260f, 8f));
            gauge.FillImage = UiKit.Box(back.transform, "Fill", Color.white);
            gauge.Fill = gauge.FillImage.rectTransform;
            UiKit.Stretch(gauge.Fill);
            return gauge;
        }

        /// <summary>Buttons sit in two columns at the top right, in the band the sticks ignore.</summary>
        static Text CreateButton(RectTransform parent, int index, Action onClick)
        {
            const float width = 300f, height = 84f, gap = 14f;
            Text label = UiKit.Button(parent, "Button" + index, UiKit.PanelColor, 30, onClick);
            var rect = (RectTransform)label.transform.parent;
            int column = index % 2, row = index / 2;
            UiKit.Place(rect, new Vector2(1f, 1f),
                new Vector2(-36f - column * (width + gap), -26f - row * (height + gap)), new Vector2(width, height));
            return label;
        }
    }
}
