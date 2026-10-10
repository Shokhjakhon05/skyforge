using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>Building blocks shared by every screen. All UI is created from code.</summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(0.03f, 0.07f, 0.11f, 0.58f);
        public static readonly Color SolidPanelColor = new Color(0.025f, 0.05f, 0.085f, 0.86f);
        /// <summary>Panels that sit over another screen are nearly opaque, so nothing shows through their text.</summary>
        public static readonly Color ModalColor = new Color(0.03f, 0.05f, 0.08f, 0.97f);
        public static readonly Color Accent = new Color(1f, 0.62f, 0.10f, 1f);
        /// <summary>The cold instrument colour: frames, rules, readouts.</summary>
        public static readonly Color Cyan = new Color(0.30f, 0.86f, 1f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.85f, 0.45f, 1f);
        public static readonly Color Bad = new Color(0.95f, 0.30f, 0.25f, 1f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.22f);
        public const string AccentHex = "#FF9E1A";
        public const string CyanHex = "#4DDBFF";

        /// <summary>Played when a finger lands on any button; the game plugs its sound in here.</summary>
        public static Action Tap;

        static Font _font, _light, _title;
        static Sprite _disc, _ring, _star, _chamfer, _chamferFrame, _brackets, _gradient;

        public static Font Font => _font != null ? _font : _font = LoadFont("ui");
        /// <summary>The lighter cut, for paragraphs.</summary>
        public static Font LightFont => _light != null ? _light : _light = LoadFont("ui_light");
        /// <summary>The wide display face, for titles and big numbers.</summary>
        public static Font TitleFont => _title != null ? _title : _title = LoadFont("title");

        static Font LoadFont(string name)
        {
            var font = Resources.Load<Font>("Fonts/" + name);
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>Sets a label in the lighter cut: body text rather than a heading.</summary>
        public static void Light(Text text)
        {
            text.font = LightFont;
            text.fontStyle = FontStyle.Normal;
        }

        public static Sprite Disc => _disc != null ? _disc : _disc = CreateDisc(false);
        public static Sprite Ring => _ring != null ? _ring : _ring = CreateDisc(true);
        public static Sprite Star => _star != null ? _star : _star = CreateStar();
        /// <summary>A plate with two corners cut off, sliced so it stretches to any size.</summary>
        public static Sprite Chamfer => _chamfer != null ? _chamfer : _chamfer = CreateChamfer(false);
        public static Sprite ChamferFrame => _chamferFrame != null ? _chamferFrame : _chamferFrame = CreateChamfer(true);
        /// <summary>A hairline frame with heavy corner brackets.</summary>
        public static Sprite Brackets => _brackets != null ? _brackets : _brackets = CreateBrackets();
        /// <summary>White fading to clear from top to bottom: the sheen on a pane of glass.</summary>
        public static Sprite Gradient => _gradient != null ? _gradient : _gradient = CreateGradient();

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
            // Screens ease in; the flight HUD and the toasts are simply there.
            if (name != "Hud" && name != "Toast") obj.AddComponent<UiAppear>();

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
            text.font = size >= 50 ? TitleFont : Font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        /// <summary>Lets a label shrink its font, down to a floor, instead of running out of its box.</summary>
        public static void FitWidth(Text text, int smallest)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = smallest;
            text.resizeTextMaxSize = text.fontSize;
        }

        /// <summary>
        /// A coloured rectangle. Asked for in one of the three panel colours it becomes a pane of
        /// glass: the scene behind it blurred and darkened, a sheen, a hairline frame with corner brackets.
        /// </summary>
        public static Image Box(Transform parent, string name, Color color, Sprite sprite = null)
        {
            Image image = Plain(parent, name, color, sprite);
            if (sprite == null && (color == PanelColor || color == SolidPanelColor || color == ModalColor))
                Glaze(image, color);
            return image;
        }

        static Image Plain(Transform parent, string name, Color color, Sprite sprite)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        static void Glaze(Image panel, Color color)
        {
            // The blurred scene goes underneath, on a sibling drawn first, so the panel's own
            // colour tints it; a panel that hides everything behind it needs none.
            if (color.a < 0.95f)
            {
                RectTransform back = Rect("Glass", panel.transform);
                Stretch(back);
                var raw = back.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                raw.color = new Color(0.62f, 0.70f, 0.80f, 1f);
                back.gameObject.AddComponent<GlassPanel>();
                // Its own colour now lies over the blur instead of over the live scene.
                Image tint = Plain(panel.transform, "Tint", color, null);
                Stretch(tint.rectTransform);
                panel.color = Color.clear;
            }

            Image sheen = Plain(panel.transform, "Sheen", new Color(0.55f, 0.85f, 1f, 0.07f), Gradient);
            Stretch(sheen.rectTransform);
            Image frame = Plain(panel.transform, "Frame", new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f), Brackets);
            frame.type = Image.Type.Sliced;
            Stretch(frame.rectTransform);
        }

        /// <summary>A thin rule in the instrument colour, for separating the parts of a panel.</summary>
        public static Image Rule(Transform parent, Vector2 anchor, Vector2 position, float width, float alpha = 0.45f)
        {
            Image rule = Plain(parent, "Rule", new Color(Cyan.r, Cyan.g, Cyan.b, alpha), null);
            Place(rule.rectTransform, anchor, position, new Vector2(width, 2f));
            return rule;
        }

        /// <summary>A small filled tag with text, the kind that labels a threat level or a class.</summary>
        public static Text Tag(Transform parent, Color color, int fontSize)
        {
            Image plate = Plain(parent, "Tag", color, Chamfer);
            plate.type = Image.Type.Sliced;
            Text label = Label(plate.transform, fontSize, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return label;
        }

        /// <summary>Returns the button's label; position the button through the label's parent.</summary>
        public static Text Button(Transform parent, string name, Color color, int fontSize, Action onClick)
        {
            Image image = Plain(parent, name, color, Chamfer);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            image.gameObject.AddComponent<ButtonFx>();

            // A lit edge: warm on the buttons that act, cold on the rest.
            bool warm = color.r > 0.8f && color.g > 0.4f && color.b < 0.4f;
            Image frame = Plain(image.transform, "Frame",
                warm ? new Color(1f, 0.88f, 0.55f, 0.9f) : new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f), ChamferFrame);
            frame.type = Image.Type.Sliced;
            Stretch(frame.rectTransform);

            Text label = Label(image.transform, fontSize, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return label;
        }

        const int PlateSize = 64, PlateCut = 14, PlateBorder = 22;

        /// <summary>The cut-corner plate, or only its outline.</summary>
        static Sprite CreateChamfer(bool outline)
        {
            var pixels = new Color32[PlateSize * PlateSize];
            for (int y = 0; y < PlateSize; y++)
            for (int x = 0; x < PlateSize; x++)
            {
                // Distance inside the shape: the four straight edges and the two cut corners
                // (top left and bottom right; y runs upwards).
                float edge = Mathf.Min(Mathf.Min(x + 0.5f, PlateSize - x - 0.5f), Mathf.Min(y + 0.5f, PlateSize - y - 0.5f));
                float topLeft = ((x + 0.5f) + (PlateSize - y - 0.5f) - PlateCut) * 0.7071f;
                float bottomRight = ((PlateSize - x - 0.5f) + (y + 0.5f) - PlateCut) * 0.7071f;
                float inside = Mathf.Min(edge, Mathf.Min(topLeft, bottomRight));
                float alpha = Mathf.Clamp01(inside + 0.5f);
                if (outline) alpha *= Mathf.Clamp01(2.6f - inside);
                pixels[y * PlateSize + x] = new Color(1f, 1f, 1f, alpha);
            }
            return ToSprite(outline ? "ChamferFrame" : "Chamfer", PlateSize, pixels, PlateBorder);
        }

        /// <summary>A one-pixel frame whose corners are drawn heavier and longer.</summary>
        static Sprite CreateBrackets()
        {
            const int arm = 18;
            var pixels = new Color32[PlateSize * PlateSize];
            for (int y = 0; y < PlateSize; y++)
            for (int x = 0; x < PlateSize; x++)
            {
                int dx = Mathf.Min(x, PlateSize - 1 - x), dy = Mathf.Min(y, PlateSize - 1 - y);
                float alpha = 0f;
                if (dx == 0 || dy == 0) alpha = 0.32f;
                if ((dx < 3 && dy < arm) || (dy < 3 && dx < arm)) alpha = 1f;
                pixels[y * PlateSize + x] = new Color(1f, 1f, 1f, alpha);
            }
            return ToSprite("Brackets", PlateSize, pixels, PlateBorder);
        }

        static Sprite CreateGradient()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float t = y / (float)(size - 1);
                float alpha = t * t;
                for (int x = 0; x < size; x++) pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            return ToSprite("Gradient", size, pixels);
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

        static Sprite ToSprite(string name, int size, Color32[] pixels, int border = 0)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}
