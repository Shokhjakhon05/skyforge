using System;
using Skyforge;
using Skyforge.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sniper
{
    /// <summary>
    /// What the shooter sees over the world: the black of the scope and its cross, the round count,
    /// the buttons under the right thumb, names over the people in the glass, and the word when a
    /// round lands.
    /// </summary>
    public sealed class SniperHud
    {
        public event Action FirePressed, ReloadPressed, ZoomInPressed, ZoomOutPressed, BreathPressed, PausePressed;

        readonly Canvas _canvas, _scopeCanvas;
        readonly GameObject _scope;
        readonly Text _objective, _clock, _ammo, _zoom, _data, _message, _sub, _breathLabel;
        readonly RectTransform _breathFill, _healthFill;
        readonly Text _hint;
        readonly GameObject _healthBox;
        readonly Image _hurt;
        readonly Text[] _tags = new Text[10];
        float _messageAge = 99f, _hurtShown;

        Text _fire;

        /// <summary>The buttons are made once; when the language changes their words are written again.</summary>
        public void Relabel()
        {
            if (_fire != null) _fire.text = Loc.T("sn.fire");
            if (_breathLabel != null) _breathLabel.text = Loc.T("sn.breath");
        }

        public SniperHud(Transform parent)
        {
            var middle = new Vector2(0.5f, 0.5f);
            _scopeCanvas = UiKit.CreateCanvas(parent, "Hud", 4);
            UnityEngine.Object.Destroy(_scopeCanvas.GetComponent<GraphicRaycaster>());
            RectTransform scope = UiKit.Rect("Scope", _scopeCanvas.transform);
            UiKit.Stretch(scope);
            _scope = scope.gameObject;
            Image mask = Still(UiKit.Box(scope, "Mask", Color.black, Sprite.Create(ScopeTexture(), new Rect(0, 0, 256, 256), middle)));
            UiKit.Place(mask.rectTransform, middle, Vector2.zero, new Vector2(1082f, 1082f));
            foreach (float side in new[] { -1f, 1f })
            {
                Image bar = Still(UiKit.Box(scope, "Side", Color.black));
                bar.rectTransform.anchorMin = new Vector2(side < 0f ? 0f : 0.5f, 0f);
                bar.rectTransform.anchorMax = new Vector2(side < 0f ? 0.5f : 1f, 1f);
                bar.rectTransform.offsetMin = new Vector2(side < 0f ? 0f : 540f, 0f);
                bar.rectTransform.offsetMax = new Vector2(side < 0f ? -540f : 0f, 0f);
            }
            var ink = new Color(0f, 0f, 0f, 0.92f);
            Line(scope, new Vector2(0f, 0f), new Vector2(1000f, 2.2f), ink);
            Line(scope, new Vector2(0f, 0f), new Vector2(2.2f, 1000f), ink);
            Line(scope, new Vector2(-330f, 0f), new Vector2(340f, 7f), ink);
            Line(scope, new Vector2(330f, 0f), new Vector2(340f, 7f), ink);
            Line(scope, new Vector2(0f, -330f), new Vector2(7f, 340f), ink);
            // Hold-over marks down the lower post, a mil apart.
            for (int i = 1; i <= 5; i++) Line(scope, new Vector2(0f, -i * 26f), new Vector2(i % 2 == 0 ? 26f : 14f, 2.2f), ink);
            for (int i = 1; i <= 4; i++)
            {
                Line(scope, new Vector2(i * 30f, 0f), new Vector2(2.2f, 12f), ink);
                Line(scope, new Vector2(-i * 30f, 0f), new Vector2(2.2f, 12f), ink);
            }
            _data = UiKit.Label(scope, 24, TextAnchor.UpperLeft);
            _data.color = new Color(0.78f, 1f, 0.8f, 0.9f);
            UiKit.Place(_data.rectTransform, middle, new Vector2(170f, -150f), new Vector2(300f, 110f));
            _data.rectTransform.pivot = new Vector2(0f, 1f);
            _scope.SetActive(false);

            _canvas = UiKit.CreateCanvas(parent, "Hud", 5);
            RectTransform safe = UiKit.SafeArea(_canvas.transform);
            _hurt = Still(UiKit.Box(_canvas.transform, "Hurt", new Color(0.8f, 0.04f, 0.02f, 0f), UiKit.Gradient));
            UiKit.Stretch(_hurt.rectTransform);

            _objective = UiKit.Label(safe, 34, TextAnchor.UpperCenter);
            UiKit.Place(_objective.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1200f, 44f));
            _objective.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            _clock = UiKit.Label(safe, 26, TextAnchor.UpperCenter);
            _clock.color = new Color(1f, 1f, 1f, 0.75f);
            UiKit.Place(_clock.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(600f, 34f));

            _message = UiKit.Label(safe, 64, TextAnchor.MiddleCenter);
            _message.color = UiKit.Accent;
            UiKit.Place(_message.rectTransform, middle, new Vector2(0f, 250f), new Vector2(1600f, 80f));
            _message.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            _sub = UiKit.Label(safe, 30, TextAnchor.MiddleCenter);
            UiKit.Place(_sub.rectTransform, middle, new Vector2(0f, 196f), new Vector2(1600f, 40f));
            _sub.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);

            _hint = UiKit.Label(safe, 36, TextAnchor.MiddleCenter);
            _hint.color = UiKit.Cyan;
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1300f, 50f));
            _hint.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            Text fire = UiKit.Button(safe, "Fire", UiKit.Accent, 46, () => FirePressed?.Invoke());
            _fire = fire;
            fire.text = Loc.T("sn.fire");
            fire.color = new Color(0.1f, 0.05f, 0f);
            UiKit.Place((RectTransform)fire.transform.parent, new Vector2(1f, 0f), new Vector2(-70f, 150f), new Vector2(300f, 190f));
            _ammo = UiKit.Button(safe, "Reload", UiKit.SolidPanelColor, 34, () => ReloadPressed?.Invoke());
            UiKit.Place((RectTransform)_ammo.transform.parent, new Vector2(1f, 0f), new Vector2(-70f, 360f), new Vector2(300f, 84f));
            Text plus = UiKit.Button(safe, "ZoomIn", UiKit.SolidPanelColor, 48, () => ZoomInPressed?.Invoke());
            plus.text = "+";
            UiKit.Place((RectTransform)plus.transform.parent, new Vector2(1f, 0.5f), new Vector2(-70f, 150f), new Vector2(120f, 110f));
            Text minus = UiKit.Button(safe, "ZoomOut", UiKit.SolidPanelColor, 48, () => ZoomOutPressed?.Invoke());
            minus.text = "–";
            UiKit.Place((RectTransform)minus.transform.parent, new Vector2(1f, 0.5f), new Vector2(-70f, 20f), new Vector2(120f, 110f));
            _zoom = UiKit.Label(safe, 28, TextAnchor.MiddleCenter);
            _zoom.color = UiKit.Cyan;
            UiKit.Place(_zoom.rectTransform, new Vector2(1f, 0.5f), new Vector2(-70f, 232f), new Vector2(120f, 40f));

            _breathLabel = UiKit.Button(safe, "Breath", UiKit.SolidPanelColor, 30, () => BreathPressed?.Invoke());
            _breathLabel.text = Loc.T("sn.breath");
            var breath = (RectTransform)_breathLabel.transform.parent;
            UiKit.Place(breath, new Vector2(0f, 0f), new Vector2(70f, 150f), new Vector2(300f, 120f));
            Image breathBack = Still(UiKit.Box(breath, "Back", new Color(0f, 0f, 0f, 0.5f)));
            UiKit.Place(breathBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(260f, 10f));
            Image breathFill = Still(UiKit.Box(breathBack.transform, "Fill", UiKit.Cyan));
            _breathFill = breathFill.rectTransform;
            _breathFill.anchorMin = Vector2.zero;
            _breathFill.anchorMax = Vector2.one;
            _breathFill.offsetMin = _breathFill.offsetMax = Vector2.zero;

            Text pause = UiKit.Button(safe, "Pause", UiKit.SolidPanelColor, 30, () => PausePressed?.Invoke());
            pause.text = "II";
            UiKit.Place((RectTransform)pause.transform.parent, new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(96f, 76f));

            Image healthBack = Still(UiKit.Box(safe, "Health", new Color(0f, 0f, 0f, 0.5f)));
            UiKit.Place(healthBack.rectTransform, new Vector2(0f, 1f), new Vector2(160f, -52f), new Vector2(320f, 16f));
            _healthBox = healthBack.gameObject;
            Image healthFill = Still(UiKit.Box(healthBack.transform, "Fill", UiKit.Bad));
            _healthFill = healthFill.rectTransform;
            _healthFill.anchorMin = Vector2.zero;
            _healthFill.anchorMax = Vector2.one;
            _healthFill.offsetMin = new Vector2(2f, 2f);
            _healthFill.offsetMax = new Vector2(-2f, -2f);
            _healthBox.SetActive(false);

            for (int i = 0; i < _tags.Length; i++)
            {
                Text tag = UiKit.Label(_canvas.transform, 24, TextAnchor.LowerCenter);
                tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = Vector2.zero;
                tag.rectTransform.pivot = new Vector2(0.5f, 0f);
                tag.rectTransform.sizeDelta = new Vector2(500f, 60f);
                tag.supportRichText = true;
                tag.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                tag.gameObject.SetActive(false);
                _tags[i] = tag;
            }
        }

        static Image Still(Image image)
        {
            image.raycastTarget = false;
            return image;
        }

        static void Line(Transform parent, Vector2 at, Vector2 size, Color color)
        {
            Image line = Still(UiKit.Box(parent, "Line", color));
            UiKit.Place(line.rectTransform, new Vector2(0.5f, 0.5f), at, size);
        }

        static Texture2D ScopeTexture()
        {
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                float d = new Vector2(x - 127.5f, y - 127.5f).magnitude / 127.5f;
                // Black outside the glass, and a soft shadow just inside its rim.
                float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 0.94f, d));
                float shade = Mathf.SmoothStep(0f, 0.55f, Mathf.InverseLerp(0.6f, 0.93f, d));
                pixels[y * 256 + x] = new Color(1f, 1f, 1f, Mathf.Max(rim, shade * 0.75f));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        public bool Visible
        {
            get => _canvas.gameObject.activeSelf;
            set
            {
                if (_canvas.gameObject.activeSelf != value) _canvas.gameObject.SetActive(value);
                if (_scopeCanvas.gameObject.activeSelf != value) _scopeCanvas.gameObject.SetActive(value);
            }
        }

        public float ScaleFactor => _canvas.scaleFactor;

        public void SetButtons(bool shown)
        {
            // While a round is followed there is nothing to press.
            if (_canvas.enabled != shown) _canvas.enabled = shown;
        }

        public void SetScope(bool on)
        {
            if (_scope.activeSelf != on) _scope.SetActive(on);
        }

        public void SetObjective(string text) { if (_objective.text != text) _objective.text = text; }
        public void SetClock(string text) { if (_clock.text != text) _clock.text = text; }
        public void SetAmmo(string text) { if (_ammo.text != text) _ammo.text = text; }
        public void SetZoom(string text) { if (_zoom.text != text) _zoom.text = text; }
        public void SetHint(string text) { if (_hint.text != text) _hint.text = text; }
        public void SetData(string text) { if (_data.text != text) _data.text = text; }

        public void SetBreath(float share, bool held)
        {
            _breathFill.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            _breathLabel.color = held ? UiKit.Cyan : Color.white;
        }

        public void SetHealth(float share, bool shown)
        {
            if (_healthBox.activeSelf != shown) _healthBox.SetActive(shown);
            if (shown) _healthFill.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
        }

        public void Message(string text, string sub, Color color)
        {
            _message.text = text;
            _message.color = color;
            _sub.text = sub ?? "";
            _messageAge = 0f;
        }

        public void Hurt() => _hurtShown = 0.5f;

        /// <summary>A name over a man in the glass. Slots not set this frame are hidden by <see cref="EndTags"/>.</summary>
        public void Tag(int slot, Vector3 screen, string text)
        {
            if (slot >= _tags.Length) return;
            Text tag = _tags[slot];
            if (!tag.gameObject.activeSelf) tag.gameObject.SetActive(true);
            tag.rectTransform.anchoredPosition = new Vector2(screen.x, screen.y) / _canvas.scaleFactor;
            if (tag.text != text) tag.text = text;
        }

        public void EndTags(int used)
        {
            for (int i = used; i < _tags.Length; i++)
                if (_tags[i].gameObject.activeSelf) _tags[i].gameObject.SetActive(false);
        }

        public void Update(float deltaTime)
        {
            if (_messageAge < 2.2f)
            {
                _messageAge += deltaTime;
                float alpha = _messageAge < 1.5f ? 1f : Mathf.Clamp01(1f - (_messageAge - 1.5f) / 0.7f);
                float scale = _messageAge < 0.14f ? Mathf.Lerp(1.8f, 1f, _messageAge / 0.14f) : 1f;
                _message.rectTransform.localScale = new Vector3(scale, scale, 1f);
                Color c = _message.color;
                c.a = alpha;
                _message.color = c;
                Color s = _sub.color;
                s.a = alpha;
                _sub.color = s;
                if (_messageAge >= 2.2f) _message.text = _sub.text = "";
            }
            _hurtShown = Mathf.Max(0f, _hurtShown - deltaTime * 0.9f);
            Color h = _hurt.color;
            if (Mathf.Abs(h.a - _hurtShown) > 0.004f)
            {
                h.a = _hurtShown;
                _hurt.color = h;
            }
        }
    }
}
