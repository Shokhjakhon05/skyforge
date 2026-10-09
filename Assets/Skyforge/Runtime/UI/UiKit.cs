using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>Building blocks shared by every screen. All UI is created from code.</summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(0.05f, 0.08f, 0.12f, 0.55f);
        public static readonly Color SolidPanelColor = new Color(0.04f, 0.06f, 0.09f, 0.88f);
        public static readonly Color Accent = new Color(1f, 0.62f, 0.10f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.85f, 0.45f, 1f);
        public static readonly Color Bad = new Color(0.95f, 0.30f, 0.25f, 1f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.22f);
        public const string AccentHex = "#FF9E1A";

        static Font _font;
        static Sprite _disc, _ring, _star;

        public static Font Font =>
            _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Sprite Disc => _disc != null ? _disc : _disc = CreateDisc(false);
        public static Sprite Ring => _ring != null ? _ring : _ring = CreateDisc(true);
        public static Sprite Star => _star != null ? _star : _star = CreateStar();

        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var canvas = obj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = obj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Height-matched: a canvas is always 1080 units tall, and wider on wider phones.
            scaler.matchWidthOrHeight = 1f;
            obj.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem");
                events.transform.SetParent(parent, false);
                events.AddComponent<EventSystem>();
                events.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        /// <summary>A full-size child limited to the part of the screen free of cut-outs.</summary>
        public static RectTransform SafeArea(Transform canvas)
        {
            RectTransform safe = Rect("Safe", canvas);
            UnityEngine.Rect area = Screen.safeArea;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            return safe;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return (RectTransform)obj.transform;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>Anchors and pivots a rect at one point of its parent.</summary>
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static Text Label(Transform parent, int size, TextAnchor anchor, bool wrap = false)
        {
            RectTransform rect = Rect("Text", parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        public static Image Box(Transform parent, string name, Color color, Sprite sprite = null)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Returns the button's label; position the button through the label's parent.</summary>
        public static Text Button(Transform parent, string name, Color color, int fontSize, Action onClick)
        {
            Image image = Box(parent, name, color);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            Text label = Label(image.transform, fontSize, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return label;
        }

        static Sprite CreateDisc(bool hollow)
        {
            const int size = 128;
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
            return ToSprite(hollow ? "Ring" : "Disc", size, pixels);
        }

        static Sprite CreateStar()
        {
            const int size = 128, samples = 3;
            var corners = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float radius = (i % 2 == 0 ? 0.48f : 0.20f) * size;
                corners[i] = new Vector2(size * 0.5f + Mathf.Cos(angle) * radius, size * 0.47f + Mathf.Sin(angle) * radius);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // A few samples per pixel smooth the edges.
                int hits = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                    if (InsidePolygon(corners, x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples)) hits++;
                pixels[y * size + x] = new Color(1f, 1f, 1f, hits / (float)(samples * samples));
            }
            return ToSprite("Star", size, pixels);
        }

        static bool InsidePolygon(Vector2[] corners, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
            {
                Vector2 a = corners[i], b = corners[j];
                if (a.y > y != b.y > y && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static Sprite ToSprite(string name, int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
