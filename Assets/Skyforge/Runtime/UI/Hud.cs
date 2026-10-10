using System;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>Flight HUD: telemetry, gauges, the current goal, buttons, stick visuals and the target marker.</summary>
    public sealed class Hud
    {
        public event Action CameraPressed, ModePressed, SticksPressed, MenuPressed, ZoomPressed, ActionPressed, BombPressed;

        /// <summary>How many targets can be marked on screen at once.</summary>
        const int TargetMarks = 10;

        readonly Canvas _canvas;
        readonly Text _speed, _altitude, _headline, _subline, _message, _hint, _markerText, _wind, _fps;
        readonly Text _cameraLabel, _modeLabel, _zoomLabel, _actionLabel;
        readonly GameObject _action;
        readonly Image _markerImage;
        readonly RectTransform _marker;
        readonly StickVisual _left, _right;
        readonly Gauge _battery, _signal, _hull, _detection;
        readonly GameObject _gauges;
        int _shownSpeed = -1, _shownAltitude = -1, _shownDistance = -1;

        // Combat: the second weapon button, a mark on every target, the lock frame and the hit flash.
        readonly Text _secondaryLabel, _lockText;
        readonly GameObject _secondary;
        readonly Image _actionImage, _lockImage, _damage;
        readonly RectTransform _lock;
        readonly RectTransform[] _marks = new RectTransform[TargetMarks];
        readonly Image[] _markImages = new Image[TargetMarks];
        int _shownLock = -1, _shownLockHealth = -1;
        float _damageShown;

        // The scope: everything around the drone as dots, nose up.
        const int ScopeDots = 14;
        const float ScopeRange = 380f, ScopeRadius = 96f;
        readonly GameObject _scope;
        readonly RectTransform[] _dots = new RectTransform[ScopeDots];
        readonly Image[] _dotImages = new Image[ScopeDots];
        readonly RectTransform _homeDot;

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
            public GameObject Bar;
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

            // A wash of red over the whole picture while rounds are hitting the drone.
            _damage = UiKit.Box(screen, "Damage", new Color(0.9f, 0.08f, 0.05f, 0f));
            UiKit.Stretch(_damage.rectTransform);

            for (int i = 0; i < TargetMarks; i++)
            {
                _marks[i] = UiKit.Rect("TargetMark", screen);
                _marks[i].anchorMin = _marks[i].anchorMax = Vector2.zero;
                _marks[i].sizeDelta = new Vector2(20f, 20f);
                _marks[i].localRotation = Quaternion.Euler(0f, 0f, 45f);
                _markImages[i] = UiKit.Box(_marks[i], "Diamond", UiKit.Bad);
                UiKit.Stretch(_markImages[i].rectTransform);
                _marks[i].gameObject.SetActive(false);
            }

            _lock = UiKit.Rect("Lock", screen);
            _lock.anchorMin = _lock.anchorMax = Vector2.zero;
            _lock.sizeDelta = new Vector2(120f, 120f);
            _lockImage = UiKit.Box(_lock, "Frame", UiKit.Good, UiKit.Ring);
            UiKit.Stretch(_lockImage.rectTransform);
            _lockText = UiKit.Label(_lock, 28, TextAnchor.UpperCenter);
            _lockText.color = UiKit.Good;
            UiKit.Place(_lockText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -10f), new Vector2(520f, 36f));
            _lock.gameObject.SetActive(false);

            _marker = UiKit.Rect("Marker", screen);
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            _marker.sizeDelta = new Vector2(34f, 34f);
            _markerImage = UiKit.Box(_marker, "Diamond", UiKit.Accent);
            UiKit.Stretch(_markerImage.rectTransform);
            _markerImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
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
            UiKit.Place(gauges, topLeft, new Vector2(40f, -168f), new Vector2(330f, 198f));
            _gauges = gauges.gameObject;
            _battery = CreateGauge(gauges, 0, Loc.T("hud.battery"));
            _signal = CreateGauge(gauges, 1, Loc.T("hud.signal"));
            _hull = CreateGauge(gauges, 2, Loc.T("hud.hull"));
            _detection = CreateGauge(gauges, 3, Loc.T("hud.detection"));

            _wind = UiKit.Label(safe, 26, TextAnchor.UpperLeft);
            UiKit.Place(_wind.rectTransform, topLeft, new Vector2(40f, -368f), new Vector2(420f, 34f));
            _fps = UiKit.Label(safe, 24, TextAnchor.LowerLeft);
            _fps.color = new Color(1f, 1f, 1f, 0.7f);
            UiKit.Place(_fps.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 16f), new Vector2(300f, 30f));

            _headline = UiKit.Label(safe, 40, TextAnchor.UpperCenter);
            // The space between the gauges and the buttons is narrow on a 16:9 screen; long objectives shrink to fit.
            UiKit.Place(_headline.rectTransform, topCenter, new Vector2(0f, -24f), new Vector2(760f, 88f));
            UiKit.FitWidth(_headline, 26);
            _subline = UiKit.Label(safe, 30, TextAnchor.UpperCenter);
            _subline.color = new Color(1f, 1f, 1f, 0.75f);
            UiKit.Place(_subline.rectTransform, topCenter, new Vector2(0f, -114f), new Vector2(900f, 40f));

            _message = UiKit.Label(safe, 84, TextAnchor.MiddleCenter);
            _message.color = UiKit.Accent;
            UiKit.Place(_message.rectTransform, middle, new Vector2(0f, 250f), new Vector2(1500f, 110f));
            _hint = UiKit.Label(safe, 40, TextAnchor.MiddleCenter);
            UiKit.Place(_hint.rectTransform, middle, new Vector2(0f, 170f), new Vector2(1500f, 56f));

            _cameraLabel = CreateButton(safe, 0, () => CameraPressed?.Invoke());
            _modeLabel = CreateButton(safe, 1, () => ModePressed?.Invoke());
            CreateButton(safe, 2, () => SticksPressed?.Invoke()).text = Loc.T("hud.sticks");
            CreateButton(safe, 3, () => MenuPressed?.Invoke()).text = Loc.T("hud.menu");
            _zoomLabel = CreateButton(safe, 4, () => ZoomPressed?.Invoke());

            // The scope sits at the bottom, between the thumbs.
            Image scope = UiKit.Box(safe, "Scope", new Color(0.03f, 0.05f, 0.07f, 0.45f), UiKit.Disc);
            UiKit.Place(scope.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(ScopeRadius * 2f + 16f, ScopeRadius * 2f + 16f));
            _scope = scope.gameObject;
            Image rim = UiKit.Box(scope.transform, "Rim", new Color(1f, 1f, 1f, 0.45f), UiKit.Ring);
            UiKit.Stretch(rim.rectTransform);
            Image self = UiKit.Box(scope.transform, "Self", Color.white);
            UiKit.Place(self.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 14f));
            self.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Image home = UiKit.Box(scope.transform, "Home", Color.white, UiKit.Ring);
            _homeDot = home.rectTransform;
            UiKit.Place(_homeDot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            _homeDot.pivot = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < ScopeDots; i++)
            {
                _dotImages[i] = UiKit.Box(scope.transform, "Dot", UiKit.Bad, UiKit.Disc);
                _dots[i] = _dotImages[i].rectTransform;
                UiKit.Place(_dots[i], new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13f, 13f));
                _dots[i].pivot = new Vector2(0.5f, 0.5f);
                _dots[i].gameObject.SetActive(false);
            }
            _scope.SetActive(false);

            // The action button sits above the right thumb; it only exists while there is something to do.
            _actionLabel = UiKit.Button(safe, "Action", UiKit.Accent, 44, () => ActionPressed?.Invoke());
            _actionLabel.color = new Color(0.08f, 0.06f, 0.02f);
            var actionRect = (RectTransform)_actionLabel.transform.parent;
            UiKit.Place(actionRect, new Vector2(1f, 0.5f), new Vector2(-36f, 70f), new Vector2(300f, 140f));
            _action = actionRect.gameObject;
            _actionImage = actionRect.GetComponent<Image>();
            UiKit.FitWidth(_actionLabel, 26);
            _action.SetActive(false);

            // Bombs go beside it, a little lower, where the same thumb finds them.
            _secondaryLabel = UiKit.Button(safe, "Secondary", UiKit.Bad, 38, () => BombPressed?.Invoke());
            UiKit.FitWidth(_secondaryLabel, 24);
            var secondaryRect = (RectTransform)_secondaryLabel.transform.parent;
            UiKit.Place(secondaryRect, new Vector2(1f, 0.5f), new Vector2(-350f, 70f), new Vector2(260f, 140f));
            _secondary = secondaryRect.gameObject;
            _secondary.SetActive(false);
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

        public void SetGauges(float battery, bool batteryLow, float signal, bool signalLow, float hull,
            float detection, bool seen)
        {
            _battery.Set(battery, batteryLow);
            _signal.Set(signal, signalLow);
            _hull.Set(hull, hull < 0.4f);
            _detection.Set(detection, seen);
        }

        /// <summary>The fourth gauge is only shown in missions where something can see the drone.</summary>
        public bool DetectionVisible
        {
            set
            {
                _detection.Label.gameObject.SetActive(value);
                _detection.Bar.SetActive(value);
            }
        }

        /// <summary>Null or empty hides the action button.</summary>
        public void SetAction(string label)
        {
            bool show = !string.IsNullOrEmpty(label);
            if (_action.activeSelf != show) _action.SetActive(show);
            if (show && _actionLabel.text != label) _actionLabel.text = label;
        }

        /// <summary>Null or empty hides the second weapon button.</summary>
        public void SetSecondary(string label)
        {
            bool show = !string.IsNullOrEmpty(label);
            if (_secondary.activeSelf != show) _secondary.SetActive(show);
            if (show && _secondaryLabel.text != label) _secondaryLabel.text = label;
        }

        /// <summary>The action button is bright when pressing it would do something, dull when it would not.</summary>
        public void SetActionReady(bool ready)
        {
            Color wanted = ready ? UiKit.Accent : new Color(0.45f, 0.42f, 0.36f, 0.85f);
            if (_actionImage.color != wanted) _actionImage.color = wanted;
        }

        /// <summary>
        /// Marks what is left to destroy and frames the target the next rocket would go for.
        /// </summary>
        /// <param name="objective">The Destroy objective in progress, or null to hide everything.</param>
        public void UpdateTargets(Camera camera, Skyforge.Core.MissionRun run, Skyforge.Core.MissionDefinition mission,
            Skyforge.Core.ObjectiveDefinition objective)
        {
            int used = 0;
            float scale = _canvas.scaleFactor;
            if (run != null)
            {
                for (int i = 0; i < run.TargetCount && used < TargetMarks; i++)
                {
                    Skyforge.Core.TargetState target = run.Target(i);
                    Skyforge.Core.TargetDefinition definition = mission.Targets[i];
                    if (!target.Alive || i == run.LockedTarget) continue;
                    // Red for what must die, pale for what merely shoots, green for what is ours.
                    bool wanted = objective != null && objective.TargetIndices.Contains(i);
                    if (!wanted && !definition.Friendly && !definition.Kind.Armed) continue;
                    Vector3 point = camera.WorldToScreenPoint(target.Position + Vector3.up * (definition.Kind.Height + 2.5f));
                    if (point.z <= 0f || point.x < 0f || point.x > Screen.width || point.y < 0f || point.y > Screen.height) continue;

                    RectTransform mark = _marks[used];
                    if (!mark.gameObject.activeSelf) mark.gameObject.SetActive(true);
                    mark.anchoredPosition = new Vector2(point.x, point.y) / scale;
                    Color color = definition.Friendly ? UiKit.Good : wanted ? UiKit.Bad : new Color(1f, 0.75f, 0.7f, 0.7f);
                    if (_markImages[used].color != color) _markImages[used].color = color;
                    used++;
                }
            }
            for (int i = used; i < TargetMarks; i++)
                if (_marks[i].gameObject.activeSelf) _marks[i].gameObject.SetActive(false);

            int locked = run != null ? run.LockedTarget : -1;
            bool show = locked >= 0;
            if (show)
            {
                Skyforge.Core.TargetState target = run.Target(locked);
                Vector3 point = camera.WorldToScreenPoint(target.Position + Vector3.up * 1.5f);
                show = point.z > 0f;
                if (show)
                {
                    _lock.anchoredPosition = new Vector2(point.x, point.y) / scale;
                    Skyforge.Core.TargetKind kind = mission.Targets[locked].Kind;
                    int health = Mathf.CeilToInt(target.Health / kind.Health * 100f);
                    if (locked != _shownLock || health != _shownLockHealth)
                    {
                        _shownLock = locked;
                        _shownLockHealth = health;
                        _lockText.text = Loc.T(kind.NameKey) + "  " + health + "%";
                    }
                }
            }
            if (_lock.gameObject.activeSelf != show) _lock.gameObject.SetActive(show);
        }

        /// <summary>
        /// The scope: targets as dots around the drone, the top of the circle being where the nose
        /// points. Anything farther than its range sits on the rim. Hidden when there is no fight.
        /// </summary>
        public void UpdateScope(Skyforge.Core.MissionRun run, Skyforge.Core.MissionDefinition mission,
            Skyforge.Core.ObjectiveDefinition objective, Vector3 drone, float yawDeg)
        {
            bool show = run != null && run.IsCombat;
            if (_scope.activeSelf != show) _scope.SetActive(show);
            if (!show) return;

            float yaw = yawDeg * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            Vector2 OnScope(Vector3 world)
            {
                float dx = world.x - drone.x, dz = world.z - drone.z;
                // Into the drone's frame: x to its right, y ahead of it.
                var local = new Vector2(dx * cos - dz * sin, dx * sin + dz * cos) * (ScopeRadius / ScopeRange);
                return local.magnitude > ScopeRadius ? local.normalized * ScopeRadius : local;
            }

            _homeDot.anchoredPosition = OnScope(mission.BasePosition);
            int used = 0;
            for (int i = 0; i < run.TargetCount && used < ScopeDots; i++)
            {
                Skyforge.Core.TargetState target = run.Target(i);
                if (!target.Alive) continue;
                Skyforge.Core.TargetDefinition definition = mission.Targets[i];
                bool wanted = objective != null && objective.TargetIndices.Contains(i);
                RectTransform dot = _dots[used];
                if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
                dot.anchoredPosition = OnScope(target.Position);
                Color color = definition.Friendly ? UiKit.Good
                    : i == run.LockedTarget ? Color.white
                    : wanted ? UiKit.Bad
                    : new Color(1f, 0.72f, 0.3f);
                if (_dotImages[used].color != color) _dotImages[used].color = color;
                used++;
            }
            for (int i = used; i < ScopeDots; i++)
                if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);
        }

        /// <param name="underFire">Rounds are hitting the drone right now.</param>
        public void SetDamage(bool underFire, float deltaTime)
        {
            float wanted = underFire ? 0.07f + 0.05f * Mathf.Sin(Time.unscaledTime * 18f) : 0f;
            _damageShown = Mathf.MoveTowards(_damageShown, wanted, deltaTime * 0.9f);
            Color color = _damage.color;
            if (Mathf.Abs(color.a - _damageShown) < 0.004f) return;
            color.a = _damageShown;
            _damage.color = color;
        }

        public void SetZoomLabel(string text) => _zoomLabel.text = text;

        public void SetWind(string text) => _wind.text = text;
        public void SetFps(string text) => _fps.text = text;
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
        /// <param name="framed">The target is in the camera's frame: the marker turns green.</param>
        public void UpdateMarker(Camera camera, Vector3 world, bool visible, bool framed = false)
        {
            if (_marker.gameObject.activeSelf != visible) _marker.gameObject.SetActive(visible);
            if (!visible) return;
            _markerImage.color = framed ? UiKit.Good : UiKit.Accent;

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
            gauge.Bar = back.gameObject;
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
            UiKit.FitWidth(label, 20);
            var rect = (RectTransform)label.transform.parent;
            int column = index % 2, row = index / 2;
            UiKit.Place(rect, new Vector2(1f, 1f),
                new Vector2(-36f - column * (width + gap), -26f - row * (height + gap)), new Vector2(width, height));
            return label;
        }
    }
}
